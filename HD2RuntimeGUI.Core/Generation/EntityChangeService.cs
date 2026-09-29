using System.Globalization;
using System.Text.Json;
using HD2RuntimeGUI.Core.Metadata;
using HD2RuntimeGUI.Core.Models;

namespace HD2RuntimeGUI.Core.Generation;

public static class EntityScalar
{
    public static JsonElement Normalize(EntityField f, JsonElement value)
    {
        if (f.IsChoice)
        {
            if (value.ValueKind == JsonValueKind.String && (value.GetString() == f.CurrentDefault.GetString() || f.AllowedValues!.Contains(value.GetString())))
                return value.Clone();
            throw new InvalidDataException(f.IsPickup ? "Choose a reviewed pickup from Runtime's catalog, or Empty." : "Choose one of the published compatible mounted weapons.");
        }
        if (value.ValueKind == JsonValueKind.Number)
        {
            if (f.Type == "integer" && value.TryGetDecimal(out var n) && n == decimal.Truncate(n) && n >= int.MinValue && n <= uint.MaxValue) return JsonSerializer.SerializeToElement(n);
            if (f.Type == "number" && value.TryGetDouble(out var x) && float.IsFinite((float)x)) return JsonSerializer.SerializeToElement((float)x);
        }
        throw new InvalidDataException("Enter a complete, finite value of the published scalar type.");
    }
    // The published safe range (0.25.0+ boosters) is checked for desired values; baselines always lie inside it.
    public static void CheckRange(EntityField f, JsonElement value)
    {
        var normalized = Normalize(f, value);
        if (f.EffectiveRange is not { } r || f.IsChoice) return;
        var v = normalized.GetDouble();
        // Number fields are float32: compare in float so a value exactly at a published bound is accepted.
        var (min, max) = f.Type == "number" ? ((double)(float)r.Min, (double)(float)r.Max) : (r.Min, r.Max);
        if (v < min || v > max) throw new InvalidDataException($"{f.DisplayName} must be between {r.Min.ToString(CultureInfo.InvariantCulture)} and {r.Max.ToString(CultureInfo.InvariantCulture)}{(string.IsNullOrWhiteSpace(r.Reason) ? "" : ": " + r.Reason)}");
    }
    public static bool Equal(EntityField f, JsonElement a, JsonElement b) => JsonElement.DeepEquals(Normalize(f, a), Normalize(f, b));
    public static string Text(EntityField f, JsonElement v) => f.IsChoice ? Normalize(f, v).GetString()!
        : f.Type == "number" ? Normalize(f, v).GetSingle().ToString("R", CultureInfo.InvariantCulture) : Normalize(f, v).GetRawText();
    // Pickups are written as hd2.pickup(<semantic ID>) or 'empty', never as raw identifiers.
    public static string Lua(EntityField f, JsonElement v) => f.IsPickup
        ? Text(f, v) == Metadata.PodPayloadReader.Empty ? "'" + Metadata.PodPayloadReader.Empty + "'" : "hd2.pickup(" + LuaGenerator.Quote(Text(f, v)) + ")"
        : f.IsReference ? LuaGenerator.Quote(Text(f, v)) : Text(f, v);
    // Whether Runtime's published opt-in applies to this value. A pod slot needs allow_unverified_reference only for a value other
    // than its vanilla occupant; every other published acknowledgement applies to any change of the field.
    public static bool AcknowledgementRequired(EntityField f, JsonElement desired) =>
        f.Acknowledgement != null && (!f.IsPickup || Normalize(f, desired).GetString() != f.CurrentDefault.GetString());
}
public interface IEntityChangeService
{
    EntityChange Create(SdkMetadata sdk, string instance, string value);
    void Validate(ModProject p, SdkMetadata sdk, EntityChange change);
}
public sealed class EntityChangeService : IEntityChangeService
{
    public static EntityAuthoring Catalog(SdkMetadata sdk) => sdk.Entities ?? throw new InvalidDataException("Explicitly rebind to SDK 0.23.0 or newer for vehicle and backpack authoring.");
    public static EntityField Resolve(EntityAuthoring catalog, EntityChange c) => catalog.Field(c.InstanceKey)
        ?? catalog.AllFields.SingleOrDefault(f => f.Target.Resource == c.Resource && f.Target.Entity == c.Entity && f.Target.Path == c.Path
            && f.Target.Zone == c.Zone && f.Target.Mount == c.Mount && f.Target.Attack == c.Attack && f.Target.Slot == c.Slot && f.SemanticFieldId == c.SemanticFieldId)
        ?? throw new InvalidDataException("Vehicle/backpack capability is missing. Review or reset this modification.");
    public EntityChange Create(SdkMetadata sdk, string instance, string value)
    {
        var f = Catalog(sdk).Field(instance) ?? throw new InvalidDataException("Vehicle/backpack capability is missing.");
        if (!f.Editable) throw new InvalidDataException(f.Reason ?? "Read-only field.");
        JsonElement desired;
        try
        {
            // Mount replacements are chosen by published semantic ID; scalars are parsed as JSON numbers.
            desired = EntityScalar.Normalize(f, f.IsChoice ? JsonSerializer.SerializeToElement(value) : JsonDocument.Parse(value).RootElement);
            EntityScalar.CheckRange(f, desired);
        }
        catch (JsonException e) { throw new InvalidDataException("Enter a complete scalar value.", e); }
        return new() { Resource = f.Target.Resource, Entity = f.Target.Entity, Path = f.Target.Path, Zone = f.Target.Zone, Mount = f.Target.Mount, Attack = f.Target.Attack, Slot = f.Target.Slot,
            InstanceKey = f.InstanceKey, SemanticFieldId = f.SemanticFieldId, FieldType = f.Type, ExpectedValue = f.CurrentDefault.Clone(),
            DesiredValue = desired, BaselineSdkVersion = sdk.Version, CapabilityEvidence = Evidence(f) };
    }
    public void Validate(ModProject p, SdkMetadata sdk, EntityChange c)
    {
        var f = Resolve(Catalog(sdk), c);
        if (!f.Editable) throw new InvalidDataException(f.Reason ?? "Read-only field.");
        if (c.InstanceKey != f.InstanceKey || c.Resource != f.Target.Resource || c.Entity != f.Target.Entity || c.Path != f.Target.Path || c.Zone != f.Target.Zone
            || c.Mount != f.Target.Mount || c.Attack != f.Target.Attack || c.Slot != f.Target.Slot || c.SemanticFieldId != f.SemanticFieldId || c.FieldType != f.Type || c.CapabilityEvidence != Evidence(f))
            throw new InvalidDataException("Vehicle/backpack capability, evidence or ownership changed. Review and accept the current capability, or reset the change.");
        if (!EntityScalar.Equal(f, c.ExpectedValue, f.CurrentDefault)) throw new InvalidDataException("Vehicle/backpack baseline changed. Review before accepting the new baseline.");
        EntityScalar.CheckRange(f, c.DesiredValue);
        // Runtime opt-ins (allow_shared, allow_unverified_effect, allow_unverified_reference) are implicit: the UI warns and the
        // generated Lua always carries the flags Runtime requires, so a build is never blocked on an acknowledgement.
    }
    public static string Evidence(EntityField f) => SupportChangeService.Hash(JsonSerializer.Serialize(new { f.Target, f.Type, f.Editable, f.ApiFieldConstant,
        f.BackingObjectId, f.OperationGroup, f.PlanGroup, f.SharedScopeKey, f.Shared, Tier = f.Evidence.Tier, f.AllowedValues, f.Acknowledgement, f.ResidencyWarning }));
    // Acknowledgement of a Runtime-required unverified opt-in. A mount reference acknowledgement covers one exact replacement;
    // an unverified-effect acknowledgement covers the field regardless of value. Both bind the published warning and evidence tier.
    public static string ReferenceEvidence(EntityField f, JsonElement desired) => SupportChangeService.Hash(JsonSerializer.Serialize(new { f.InstanceKey,
        Desired = !f.IsChoice ? null : desired.ValueKind == JsonValueKind.String ? desired.GetString() : desired.GetRawText(), f.Acknowledgement, f.ResidencyWarning, Tier = f.Evidence.Tier }));
    public static string ApprovalEvidence(EntityField f) => SupportChangeService.Hash(JsonSerializer.Serialize(new { f.SharedScopeKey, f.Shared, f.AllowSharedRequired,
        f.ReviewedScopeComplete, f.DynamicConsumersPossible, Consumers = f.SharedConsumers.Select(c => c.Vehicle ?? c.Backpack).Order(StringComparer.Ordinal) }));
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
        foreach (var same in effective.GroupBy(r => (r.Field.BackingObjectId, r.Field.ApiFieldConstant)))
            if (same.Select(r => r.Field.Target).Distinct().Count() > 1)
                throw new InvalidDataException($"{same.First().Field.DisplayName} is one shared value reached through {string.Join(" and ", same.Select(r => r.Field.Target.Entity).Distinct())}. Edit it through one of them only.");
        foreach (var plan in effective.GroupBy(r => r.Field.PlanGroup).OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            var entries = plan.ToArray();
            if (entries.Select(r => r.Change.EnsureEnabled).Distinct().Count() != 1) throw new InvalidDataException("Edits to one vehicle or backpack require the same persistence setting.");
            var groups = entries.GroupBy(r => r.Field.OperationGroup).OrderBy(g => g.Key, StringComparer.Ordinal).ToArray();
            if (sdk.Plans == null || groups.Length > sdk.Plans.Limits.Operations || entries.Length > sdk.Plans.Limits.PhysicalChangesPerPhase)
                throw new InvalidDataException("Vehicle/backpack edit exceeds the published plan limits.");
            var operations = new List<string>();
            foreach (var group in groups)
            {
                var rows = group.OrderBy(r => r.Field.InstanceKey, StringComparer.Ordinal).ToArray(); var f = rows[0].Field;
                if (rows.Select(r => r.Field.Target).Distinct().Count() != 1 || rows.Select(r => r.Field.BackingObjectId).Distinct().Count() != 1 || rows.Length > 32)
                    throw new InvalidDataException("Unsupported vehicle/backpack operation contract.");
                var body = "{\n    id=" + LuaGenerator.Quote("entity-" + SupportChangeService.Hash(project.ResourceId + "\n" + group.Key)[..24]) + ",\n    target=" + Target(f.Target, AmmoFeed(catalog, rows.Select(r => r.Field))) + ",\n";
                if (f.AllowSharedRequired) body += "    allow_shared=true,\n";
                if (rows.Any(r => r.Field.Acknowledgement == "allow_unverified_reference" && EntityScalar.AcknowledgementRequired(r.Field, r.Change.DesiredValue))) body += "    allow_unverified_reference=true,\n";
                if (rows.Any(r => r.Field.Acknowledgement == "allow_unverified_effect")) body += "    allow_unverified_effect=true,\n";
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
    public static string Target(EntityTarget t, string? fedWeapon = null) => t.Path switch
    {
        "backpack" when fedWeapon != null => "hd2.support_weapon(" + LuaGenerator.Quote(fedWeapon) + "):backpack()",
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
        _ => throw new InvalidDataException("Unsupported vehicle/backpack target."),
    };
    // weapon(label) is the mount's weapon; projectile() is attack('primary') and explosion() is attack('impact') in Runtime.
    private static string VehicleWeaponTarget(EntityTarget t)
    {
        var (vehicle, mount) = VehicleWeaponReader.Split(t.Weapon!);
        var weapon = "hd2.vehicle(" + LuaGenerator.Quote(vehicle) + "):weapon(" + LuaGenerator.Quote(mount) + ")";
        return (t.Path, t.Attack) switch
        {
            ("weapon", null) => weapon,
            ("projectile_reference", "primary") => weapon + ":projectile()",
            ("explosion", "impact") => weapon + ":explosion()",
            ("projectile_reference" or "explosion" or "attack", { } role) => weapon + ":attack(" + LuaGenerator.Quote(role) + ")",
            _ => throw new InvalidDataException("Unsupported vehicle weapon target."),
        };
    }
}
