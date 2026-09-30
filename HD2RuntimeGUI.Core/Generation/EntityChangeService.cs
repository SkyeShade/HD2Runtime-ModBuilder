using System.Globalization;
using System.Text.Json;
using HD2RuntimeGUI.Core.Localization;
using HD2RuntimeGUI.Core.Metadata;
using HD2RuntimeGUI.Core.Models;

namespace HD2RuntimeGUI.Core.Generation;

public static class EntityScalar
{
    private static readonly System.Text.RegularExpressions.Regex StatusKey = new(@"\A[a-z][a-z0-9_]{0,63}\z", System.Text.RegularExpressions.RegexOptions.CultureInvariant);
    public static bool ValidStatusKey(JsonElement v) => v.ValueKind == JsonValueKind.String && StatusKey.IsMatch(v.GetString()!);
    public static JsonElement Normalize(EntityField f, JsonElement value)
    {
        if (f.IsChoice)
        {
            if (value.ValueKind == JsonValueKind.String && (value.GetString() == f.CurrentDefault.GetString() || f.AllowedValues!.Contains(value.GetString())))
                return value.Clone();
            throw new InvalidDataException(CoreText.Get(f.IsPickup ? "Messages.Build.Entity.ChoosePickup" : "Messages.Build.Entity.ChooseMount"));
        }
        // 0.28.0 status references: a status key or 'none'. Which keys a slot takes is checked against the status catalog where the SDK is
        // known (EntityChangeService.CheckStatus).
        if (f.IsStatusReference)
            return ValidStatusKey(value) ? value.Clone() : throw new InvalidDataException(CoreText.Get("Messages.Build.Status.ChooseStatus"));
        if (value.ValueKind == JsonValueKind.Number)
        {
            if (f.Type == "integer" && value.TryGetDecimal(out var n) && n == decimal.Truncate(n) && n >= int.MinValue && n <= uint.MaxValue) return JsonSerializer.SerializeToElement(n);
            if (f.Type == "number" && value.TryGetDouble(out var x) && float.IsFinite((float)x)) return JsonSerializer.SerializeToElement((float)x);
        }
        throw new InvalidDataException(CoreText.Get("Messages.Build.Value.CompleteFinitePublishedType"));
    }
    // The published safe range (0.25.0+ boosters) is checked for desired values; baselines always lie inside it.
    public static void CheckRange(EntityField f, JsonElement value)
    {
        var normalized = Normalize(f, value);
        if (f.EffectiveRange is not { } r || f.IsChoice) return;
        var v = normalized.GetDouble();
        // Number fields are float32: compare in float so a value exactly at a published bound is accepted.
        var (min, max) = f.Type == "number" ? ((double)(float)r.Min, (double)(float)r.Max) : (r.Min, r.Max);
        if (v < min || v > max) throw new InvalidDataException(string.IsNullOrWhiteSpace(r.Reason)
            ? CoreText.Format("Messages.Build.Entity.OutOfRange", f.DisplayName, r.Min.ToString(CultureInfo.InvariantCulture), r.Max.ToString(CultureInfo.InvariantCulture))
            : CoreText.Format("Messages.Build.Entity.OutOfRangeReason", f.DisplayName, r.Min.ToString(CultureInfo.InvariantCulture), r.Max.ToString(CultureInfo.InvariantCulture), r.Reason));
    }
    public static bool Equal(EntityField f, JsonElement a, JsonElement b) => JsonElement.DeepEquals(Normalize(f, a), Normalize(f, b));
    public static string Text(EntityField f, JsonElement v) => f.IsChoice || f.IsStatusReference ? Normalize(f, v).GetString()!
        : f.Type == "number" ? Normalize(f, v).GetSingle().ToString("R", CultureInfo.InvariantCulture) : Normalize(f, v).GetRawText();
    // Pickups are written as hd2.pickup(<semantic ID>) or 'empty', never as raw identifiers; a status reference as its quoted key ('fire').
    public static string Lua(EntityField f, JsonElement v) => f.IsPickup
        ? Text(f, v) == Metadata.PodPayloadReader.Empty ? "'" + Metadata.PodPayloadReader.Empty + "'" : "hd2.pickup(" + LuaGenerator.Quote(Text(f, v)) + ")"
        : f.IsStatusReference ? StatusReference.Lua(Text(f, v)) : f.IsReference ? LuaGenerator.Quote(Text(f, v)) : Text(f, v);
    // Whether Runtime's published opt-in applies to this value. A pod slot needs allow_unverified_reference only for a value other
    // than its vanilla occupant; every other published acknowledgement applies to any change of the field. A value a live test proved for
    // exactly this field (0.28.0: a pickup verified in this pod slot) needs no unverified opt-in; allow_shared is never dropped.
    public static bool AcknowledgementRequired(EntityField f, JsonElement desired) =>
        f.Acknowledgement != null && (!f.IsPickup || Normalize(f, desired).GetString() != f.CurrentDefault.GetString()) && !LiveProven(f, desired);
    public static bool LiveProven(EntityField f, JsonElement desired) => f.LiveProvenValues is { Length: > 0 } proven && (f.IsChoice || f.IsStatusReference || f.Type is "integer" or "number")
        && proven.Contains(Text(f, desired), StringComparer.Ordinal);
}
public interface IEntityChangeService
{
    EntityChange Create(SdkMetadata sdk, string instance, string value);
    void Validate(ModProject p, SdkMetadata sdk, EntityChange change);
}
public sealed class EntityChangeService : IEntityChangeService
{
    public static EntityAuthoring Catalog(SdkMetadata sdk) => sdk.Entities ?? throw new InvalidDataException(CoreText.Get("Messages.Build.Entity.SdkTooOld"));
    public static bool IsEnemy(string resource) => resource is EnemyAuthoringReader.Enemy or EnemyAuthoringReader.Structure;
    private static string Missing(string resource) => IsEnemy(resource)
        ? CoreText.Format("Messages.Build.Entity.EnemyMissing", EnemyAuthoringReader.FileName)
        : CoreText.Get("Messages.Build.Entity.Missing");
    public static EntityField Resolve(EntityAuthoring catalog, EntityChange c) => catalog.Field(c.InstanceKey)
        ?? catalog.AllFields.SingleOrDefault(f => f.Target.Resource == c.Resource && f.Target.Entity == c.Entity && f.Target.Path == c.Path
            && f.Target.Zone == c.Zone && f.Target.Mount == c.Mount && f.Target.Attack == c.Attack && f.Target.Slot == c.Slot && f.Target.Effect == c.Effect
            && f.Target.Linked == c.Linked && f.SemanticFieldId == c.SemanticFieldId)
        ?? throw new InvalidDataException(Missing(c.Resource));
    public EntityChange Create(SdkMetadata sdk, string instance, string value)
    {
        var f = Catalog(sdk).Field(instance) ?? throw new InvalidDataException(CoreText.Get("Messages.Build.Entity.MissingShort"));
        if (!f.Editable) throw new InvalidDataException(f.Reason ?? CoreText.Get("Messages.Build.Entity.ReadOnly"));
        JsonElement desired;
        try
        {
            // Mount replacements, pickups and statuses are chosen by published key; scalars are parsed as JSON numbers.
            desired = EntityScalar.Normalize(f, f.IsChoice || f.IsStatusReference ? JsonSerializer.SerializeToElement(value) : JsonDocument.Parse(value).RootElement);
            EntityScalar.CheckRange(f, desired);
            if (f.IsStatusReference) CheckStatus(sdk, f, desired);
        }
        catch (JsonException e) { throw new InvalidDataException(CoreText.Get("Messages.Build.Value.CompleteScalar"), e); }
        var enemy = IsEnemy(f.Target.Resource);
        return new() { Resource = f.Target.Resource, Entity = f.Target.Entity, Path = f.Target.Path, Zone = f.Target.Zone, Mount = f.Target.Mount, Attack = f.Target.Attack, Slot = f.Target.Slot, Effect = f.Target.Effect,
            Linked = f.Target.Linked,
            InstanceKey = f.InstanceKey, SemanticFieldId = f.SemanticFieldId, FieldType = f.Type, ExpectedValue = f.CurrentDefault.Clone(),
            DesiredValue = desired, BaselineSdkVersion = sdk.Version, CapabilityEvidence = Evidence(f),
            Group = enemy ? (f.Target.Resource == EnemyAuthoringReader.Structure ? "Structures" : "Enemies") : "Vehicles & backpacks",
            SharedConsumers = enemy && f.Shared ? f.SharedConsumers.Select(c => c.Name).ToArray() : null };
    }
    public void Validate(ModProject p, SdkMetadata sdk, EntityChange c)
    {
        var f = Resolve(Catalog(sdk), c);
        if (!f.Editable) throw new InvalidDataException(f.Reason ?? CoreText.Get("Messages.Build.Entity.ReadOnly"));
        if (c.InstanceKey != f.InstanceKey || c.Resource != f.Target.Resource || c.Entity != f.Target.Entity || c.Path != f.Target.Path || c.Zone != f.Target.Zone
            || c.Mount != f.Target.Mount || c.Attack != f.Target.Attack || c.Slot != f.Target.Slot || c.Effect != f.Target.Effect || c.Linked != f.Target.Linked
            || c.SemanticFieldId != f.SemanticFieldId || c.FieldType != f.Type || c.CapabilityEvidence != Evidence(f))
            throw new InvalidDataException(CoreText.Get(IsEnemy(c.Resource) ? "Messages.Build.Entity.EnemyCapabilityChanged" : "Messages.Build.Entity.CapabilityChanged"));
        if (!EntityScalar.Equal(f, c.ExpectedValue, f.CurrentDefault)) throw new InvalidDataException(CoreText.Get(IsEnemy(c.Resource) ? "Messages.Build.Entity.EnemyBaselineChanged" : "Messages.Build.Entity.BaselineChanged"));
        EntityScalar.CheckRange(f, c.DesiredValue);
        if (f.IsStatusReference) { CheckStatus(sdk, f, c.DesiredValue); CheckPacking(p, sdk, f); }
        // Runtime opt-ins (allow_shared, allow_unverified_effect, allow_unverified_reference) are implicit: the UI warns and the
        // generated Lua always carries the flags Runtime requires, so a build is never blocked on an acknowledgement.
    }
    // A mounted-weapon status slot (0.28.0) takes an attachable status from the SDK's status catalog, or keeps its current one; 'none'
    // clears only the last used slot.
    public static void CheckStatus(SdkMetadata sdk, EntityField f, JsonElement desired)
    {
        if (sdk.StatusEffects == null) throw new InvalidDataException(CoreText.Get("Messages.Build.Status.CatalogMissing"));
        var choices = MountedStatusSlots.For(sdk, f);
        if (desired.ValueKind == JsonValueKind.String && desired.GetString() == StatusReference.None && !choices.AllowNone)
            throw new InvalidDataException(CoreText.Format("Messages.Build.Status.ClearLastOnly", MountedStatusSlots.Slot(f) ?? 0));
        StatusReference.Normalize(desired, choices.Statuses, choices.AllowNone);
    }
    // Status slots stay packed from slot 1 (Runtime re-proves it at write time): no enabled edit may leave a status after an empty slot.
    public static void CheckPacking(ModProject p, SdkMetadata sdk, EntityField f)
    {
        var siblings = MountedStatusSlots.Siblings(Catalog(sdk), f);
        string Effective(EntityField x) => Saved(p, x) is { Enabled: true } c && c.DesiredValue.ValueKind == JsonValueKind.String ? c.DesiredValue.GetString()! : x.CurrentDefault.GetString()!;
        if (MountedStatusSlots.Gap(siblings, Effective, f) is { } gap)
            throw new InvalidDataException(CoreText.Format("Messages.Build.Status.Gap", gap.Empty, gap.Used));
    }
    public static string Evidence(EntityField f) => f.Target.Enemy != null ? EnemyEvidence(f) : SupportChangeService.Hash(JsonSerializer.Serialize(new { f.Target, f.Type, f.Editable, f.ApiFieldConstant,
        f.BackingObjectId, f.OperationGroup, f.PlanGroup, f.SharedScopeKey, f.Shared, Tier = f.Evidence.Tier, f.AllowedValues, f.Acknowledgement, f.ResidencyWarning }));
    // Enemy fields bind their semantic content only: target, type, writability, API constant, row kind, sharing (with the reviewed consumer
    // classes), opt-in and evidence tier. Runtime's opaque native row identities move between game builds without any semantic change, so they
    // are not part of it; a changed consumer set, opt-in or writability still requires review.
    private static string EnemyEvidence(EntityField f) => SupportChangeService.Hash(JsonSerializer.Serialize(new { f.Target, f.Type, f.Editable, f.ApiFieldConstant,
        f.BackingObjectKind, f.Shared, f.AllowSharedRequired, f.DynamicConsumersPossible, Consumers = f.SharedConsumers.Select(c => c.Name).Order(StringComparer.Ordinal), f.Acknowledgement, Tier = f.Evidence.Tier }));
    // Acknowledgement of a Runtime-required unverified opt-in. A mount reference acknowledgement covers one exact replacement;
    // an unverified-effect acknowledgement covers the field regardless of value. Both bind the published warning and evidence tier.
    public static string ReferenceEvidence(EntityField f, JsonElement desired) => SupportChangeService.Hash(JsonSerializer.Serialize(new { f.InstanceKey,
        Desired = !f.IsChoice ? null : desired.ValueKind == JsonValueKind.String ? desired.GetString() : desired.GetRawText(), f.Acknowledgement, f.ResidencyWarning, Tier = f.Evidence.Tier }));
    public static string ApprovalEvidence(EntityField f) => SupportChangeService.Hash(JsonSerializer.Serialize(new { f.SharedScopeKey, f.Shared, f.AllowSharedRequired,
        f.ReviewedScopeComplete, f.DynamicConsumersPossible, Consumers = f.SharedConsumers.Select(c => c.Name).Order(StringComparer.Ordinal) }));
    // Rebind: capability evidence (and the reference acknowledgement record) stays valid when the field is unchanged apart from
    // Runtime's residency prose, e.g. SDK 0.27.0 rewording pod and mount package warnings for automatic asset loading.
    public static int Rebind(ModProject p, EntityAuthoring? previous, EntityAuthoring next)
    {
        if (previous == null) return 0;
        var refreshed = 0;
        p.EntityChanges = p.EntityChanges.Select(c =>
        {
            if (next.Field(c.InstanceKey) is not { } f || previous.Field(c.InstanceKey) is not { } old || c.CapabilityEvidence == Evidence(f)
                || c.CapabilityEvidence != Evidence(old) || Evidence(old with { ResidencyWarning = null }) != Evidence(f with { ResidencyWarning = null })) return c;
            refreshed++;
            return c with { CapabilityEvidence = Evidence(f), ReferenceAcknowledgement = c.ReferenceAcknowledgement != null && c.ReferenceAcknowledgement == ReferenceEvidence(old, c.DesiredValue)
                ? ReferenceEvidence(f, c.DesiredValue) : c.ReferenceAcknowledgement };
        }).ToList();
        return refreshed;
    }
    public static bool Approved(ModProject p, EntityField f) => p.EntityApprovals.GetValueOrDefault(f.SharedScopeKey) == ApprovalEvidence(f);
    public static EntityChange? Saved(ModProject? p, EntityField f) => p?.EntityChanges.SingleOrDefault(c => c.InstanceKey == f.InstanceKey);
    public static bool NoOp(SdkMetadata sdk, EntityChange c)
    {
        var f = sdk.Entities?.Field(c.InstanceKey);
        return f != null && f.Type == c.FieldType && EntityScalar.Equal(f, c.ExpectedValue, f.CurrentDefault) && EntityScalar.Equal(f, c.DesiredValue, f.CurrentDefault);
    }
}

public interface IEntityLua { IReadOnlyList<string> Operations(ModProject project, SdkMetadata sdk, OptionBindings? options = null); }
// One request per vehicle/backpack plan group: patch for one field, transaction for one operation group, hd2.plan across groups.
// Flags are emitted exactly when Runtime requires them (allow_shared, allow_unverified_effect, allow_unverified_reference).
public sealed class EntityLua(IEntityChangeService service) : IEntityLua
{
    public IReadOnlyList<string> Operations(ModProject project, SdkMetadata sdk, OptionBindings? options = null)
    {
        var active = project.EntityChanges.Where(c => c.Enabled).OrderBy(c => c.InstanceKey, StringComparer.Ordinal).ToArray();
        if (active.Length == 0) return [];
        var catalog = EntityChangeService.Catalog(sdk);
        foreach (var c in active) service.Validate(project, sdk, c);
        var output = new List<string>();
        var effective = active.Where(c => !EntityChangeService.NoOp(sdk, c)).Select(c => (Change: c, Field: EntityChangeService.Resolve(catalog, c))).ToArray();
        // One native owner reached through several targets (a weapon in two mounts, a projectile row shared by two mounted weapons)
        // is one value: edit it through one of them only, otherwise two jobs would race on the same bytes.
        foreach (var same in effective.GroupBy(r => (r.Field.BackingObjectId, r.Field.ApiFieldConstant))
            // A Guard Dog drone weapon (0.28.0) can fire a settings row a vehicle mount also fires: the same row through both is one value too.
            .Concat(effective.Where(r => r.Field.SharedRow != null).GroupBy(r => (r.Field.SharedRow!, r.Field.ApiFieldConstant)).Where(g => g.Any(r => r.Field.Target.Linked != null))))
            if (same.Select(r => r.Field.Target).Distinct().Count() > 1)
                throw new InvalidDataException(CoreText.Format("Messages.Build.Entity.SharedValue", same.First().Field.DisplayName,
                    same.Select(r => Describe(catalog, r.Field.Target)).Distinct().Aggregate((a, b) => CoreText.Format("Messages.Build.Entity.SharedValueTargets", a, b))));
        foreach (var plan in effective.GroupBy(r => r.Field.PlanGroup).OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            var entries = plan.ToArray();
            if (entries.Select(r => r.Change.EnsureEnabled).Distinct().Count() != 1) throw new InvalidDataException(CoreText.Get("Messages.Build.Entity.MixedPersistence"));
            var groups = entries.GroupBy(r => r.Field.OperationGroup).OrderBy(g => g.Key, StringComparer.Ordinal).ToArray();
            if (sdk.Plans == null || groups.Length > sdk.Plans.Limits.Operations || entries.Length > sdk.Plans.Limits.PhysicalChangesPerPhase)
                throw new InvalidDataException(CoreText.Get("Messages.Build.Entity.PlanLimits"));
            var operations = new List<string>();
            foreach (var group in groups)
            {
                var rows = group.OrderBy(r => r.Field.InstanceKey, StringComparer.Ordinal).ToArray(); var f = rows[0].Field;
                if (rows.Select(r => r.Field.Target).Distinct().Count() != 1 || rows.Select(r => r.Field.BackingObjectId).Distinct().Count() != 1 || rows.Length > 32)
                    throw new InvalidDataException(CoreText.Get("Messages.Build.Entity.UnsupportedContract"));
                var body = "{\n    id=" + LuaGenerator.Quote("entity-" + SupportChangeService.Hash(project.ResourceId + "\n" + group.Key)[..24]) + ",\n    target=" + Target(f.Target, AmmoFeed(catalog, rows.Select(r => r.Field))) + ",\n";
                if (f.AllowSharedRequired) body += "    allow_shared=true,\n";
                if (rows.Any(r => r.Field.Acknowledgement == "allow_unverified_reference" && EntityScalar.AcknowledgementRequired(r.Field, r.Change.DesiredValue))) body += "    allow_unverified_reference=true,\n";
                if (rows.Any(r => r.Field.Acknowledgement == "allow_unverified_effect" && EntityScalar.AcknowledgementRequired(r.Field, r.Change.DesiredValue))) body += "    allow_unverified_effect=true,\n";
                if (rows.Length == 1)
                { var r = rows[0]; body += $"    field={r.Field.ApiFieldConstant},\n    expect={EntityScalar.Lua(r.Field, r.Change.ExpectedValue)},\n    value={Value(r.Field, r.Change)},\n"; }
                else body += "    changes={\n" + string.Join("\n", rows.Select(r => $"        {{field={r.Field.ApiFieldConstant},expect={EntityScalar.Lua(r.Field, r.Change.ExpectedValue)},value={Value(r.Field, r.Change)}}},")) + "\n    },\n";
                operations.Add(body + "}");
            }
            var kind = groups.Length > 1 ? "plan" : operations[0].Contains("    changes={", StringComparison.Ordinal) ? "transaction" : "patch";
            var request = kind == "plan" ? "{\n    id=" + LuaGenerator.Quote("entity-plan-" + SupportChangeService.Hash(project.ResourceId + "\n" + plan.Key)[..24])
                + ",\n    operations={\n" + string.Join(",\n", operations.Select(o => "        " + o.Replace("\n", "\n        "))) + "\n    },\n}" : operations[0];
            output.Add(OptionBindings.Wrap(options, kind, request, entries[0].Change.EnsureEnabled, entries.Select(r => (string?)ModOptionsService.EntityKey(r.Change.InstanceKey))));
        }
        return output;
        string Value(EntityField f, EntityChange c) => options?.Value(ModOptionsService.EntityKey(c.InstanceKey), EntityScalar.Lua(f, c.DesiredValue)) ?? EntityScalar.Lua(f, c.DesiredValue);
    }
    // Backpack ammunition (0.26.0) is authored through the weapon it feeds: hd2.support_weapon(name):backpack().
    public static string? AmmoFeed(EntityAuthoring catalog, IEnumerable<EntityField> fields)
    {
        var list = fields.ToArray();
        return list.All(f => f.Target.Path == "backpack" && f.UiGroup == Backpack.AmmoGroup) && catalog.Backpacks.Find(list[0].Target.Backpack!)?.Feeds is { } feeds
            ? feeds.SupportWeapon : null;
    }
    // A readable name for a target in messages: enemies by class name and zone/attack, other entities by their published name.
    public static string Describe(EntityAuthoring catalog, EntityTarget t) => t.Enemy != null ? catalog.Enemies?.Describe(t) ?? t.Enemy : t.Entity;
    public static string Target(EntityTarget t, string? fedWeapon = null) => t.Path switch
    {
        // Enemies and enemy structures first: their paths (entity, damage_zone, attack) reuse names other resources also use.
        _ when t.Enemy != null => EnemyTarget(t),
        // 0.27.0 throwables first: their target paths (entity, damage, explosion, ...) reuse names other resources also use.
        _ when t.Resource == ThrowableAuthoringReader.Resource => ThrowableTarget(t),
        "backpack" when fedWeapon != null => "hd2.support_weapon(" + LuaGenerator.Quote(fedWeapon) + "):backpack()",
        // 0.28.0: an entity the backpack deploys or projects (hd2.backpack(name):drone() / :energy_shield()), and its damage zones.
        "linked" when t.Resource == "backpack" => Linked(t),
        "damage_zone" when t.Resource == "backpack" && t.Linked != null => Linked(t) + ":damage_zone(" + LuaGenerator.Quote(t.Zone!) + ")",
        // A backpack's own damage zone (the SH-20 shield plate, 0.28.0 development SDKs), by published zone ID.
        "damage_zone" when t.Resource == "backpack" => "hd2.backpack(" + LuaGenerator.Quote(t.Backpack!) + "):damage_zone(" + LuaGenerator.Quote(t.Zone!) + ")",
        // 0.26.0 mounted vehicle weapons: hd2.vehicle(v):weapon(mount label), then its attack objects.
        _ when t.Resource == "vehicle_weapon" => VehicleWeaponTarget(t),
        // 0.26.0 drop-pod racks by published name: the rack (spawn count) or one of its slots (payload).
        "rack" when t.Resource == "pod_rack" => "hd2.pod_rack(" + LuaGenerator.Quote(t.Rack!) + ")",
        "slot" when t.Resource == "pod_rack" => "hd2.pod_rack(" + LuaGenerator.Quote(t.Rack!) + "):slot(" + t.Slot!.Value.ToString(CultureInfo.InvariantCulture) + ")",
        "entity" => "hd2.vehicle(" + LuaGenerator.Quote(t.Vehicle!) + ")",
        "damage_zone" => "hd2.vehicle(" + LuaGenerator.Quote(t.Vehicle!) + "):damage_zone(" + LuaGenerator.Quote(t.Zone!) + ")",
        "mount" => "hd2.vehicle(" + LuaGenerator.Quote(t.Vehicle!) + "):mount(" + LuaGenerator.Quote(t.Mount!) + ")",
        "backpack" => "hd2.backpack(" + LuaGenerator.Quote(t.Backpack!) + ")",
        "magazine" when t.Resource == "weapon_attachment" => "hd2.weapon_attachment(" + LuaGenerator.Quote(t.Attachment!) + ")",
        // hd2.booster(name):deployed_entity() / status_effect() (0.24.0) and :tuning() / explosion() / status_damage() / granted_stratagem() (0.25.0).
        _ when t.Resource == "booster" && BoosterAuthoringReader.Paths.Contains(t.Path) => "hd2.booster(" + LuaGenerator.Quote(t.Booster!) + "):" + t.Path + "()",
        _ => throw new InvalidDataException(CoreText.Get("Messages.Build.Entity.UnsupportedTarget")),
    };
    // hd2.enemy(class) / hd2.structure(class) by native class name (Runtime also accepts a proven wiki name; the class name does not change
    // when one is proven later), then :zone(zone_N) or :attack(slot_N...) by published ID.
    private static string EnemyTarget(EntityTarget t)
    {
        var root = (t.Resource == EnemyAuthoringReader.Structure ? "hd2.structure(" : t.Resource == EnemyAuthoringReader.Enemy ? "hd2.enemy(" : throw new InvalidDataException(CoreText.Get("Messages.Build.Entity.UnsupportedEnemyTarget")))
            + LuaGenerator.Quote(t.EnemyClass ?? throw new InvalidDataException(CoreText.Get("Messages.Build.Entity.MissingEnemyClass"))) + ")";
        return t.Path switch
        {
            EnemyAuthoringReader.EntityPath => root,
            EnemyAuthoringReader.ZonePath => root + ":zone(" + LuaGenerator.Quote(t.Zone!) + ")",
            EnemyAuthoringReader.AttackPath => root + ":attack(" + LuaGenerator.Quote(t.Attack!) + ")",
            _ => throw new InvalidDataException(CoreText.Get("Messages.Build.Entity.UnsupportedEnemyTarget")),
        };
    }
    private static string ThrowableTarget(EntityTarget t)
    {
        var chain = ThrowableAuthoringReader.Accessors.GetValueOrDefault(t.Path) ?? throw new InvalidDataException(CoreText.Get("Messages.Build.Entity.UnsupportedThrowableTarget"));
        var lua = "hd2.throwable(" + LuaGenerator.Quote(t.Throwable!) + ")";
        foreach (var accessor in chain.Where(a => a != "throwable"))
            lua += accessor == "status_effect" ? ":status_effect(" + LuaGenerator.Quote(t.Effect ?? throw new InvalidDataException(CoreText.Get("Messages.Build.Entity.MissingStatusEffect"))) + ")" : ":" + accessor + "()";
        return lua;
    }
    private static string Linked(EntityTarget t) => "hd2.backpack(" + LuaGenerator.Quote(t.Backpack ?? throw new InvalidDataException(CoreText.Get("Messages.Build.Entity.UnsupportedTarget")))
        + ")" + BackpackLinkedEntity.Accessor(t.Linked ?? throw new InvalidDataException(CoreText.Get("Messages.Build.Entity.UnsupportedTarget")));
    // weapon(label) is the mount's weapon; projectile() is attack('primary') and explosion() is attack('impact') in Runtime. A Guard Dog drone
    // weapon (0.28.0) is reached through its backpack: hd2.backpack(name):drone():weapon().
    private static string VehicleWeaponTarget(EntityTarget t)
    {
        var (vehicle, mount) = VehicleWeaponReader.Split(t.Weapon!);
        var weapon = t.Linked switch
        {
            null => "hd2.vehicle(" + LuaGenerator.Quote(vehicle) + "):weapon(" + LuaGenerator.Quote(mount) + ")",
            BackpackLinkedEntity.Drone => VehicleWeaponReader.CarrierAccessor(vehicle),
            _ => throw new InvalidDataException(CoreText.Get("Messages.Build.Entity.UnsupportedVehicleWeaponTarget")),
        };
        return (t.Path, t.Attack) switch
        {
            ("weapon", null) => weapon,
            ("projectile_reference", "primary") => weapon + ":projectile()",
            ("explosion", "impact") => weapon + ":explosion()",
            ("projectile_reference" or "explosion" or "attack", { } role) => weapon + ":attack(" + LuaGenerator.Quote(role) + ")",
            _ => throw new InvalidDataException(CoreText.Get("Messages.Build.Entity.UnsupportedVehicleWeaponTarget")),
        };
    }
}
