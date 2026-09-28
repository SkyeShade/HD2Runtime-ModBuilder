using System.Globalization;
using System.Text.Json;
using HD2RuntimeGUI.Core.Metadata;
using HD2RuntimeGUI.Core.Models;

namespace HD2RuntimeGUI.Core.Generation;

public static class EntityScalar
{
    public static JsonElement Normalize(EntityField f, JsonElement value)
    {
        if (f.IsReference)
        {
            if (value.ValueKind == JsonValueKind.String && (value.GetString() == f.CurrentDefault.GetString() || f.AllowedValues!.Contains(value.GetString())))
                return value.Clone();
            throw new InvalidDataException("Choose one of the published compatible mounted weapons.");
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
        if (f.Range is not { } r || f.IsReference) return;
        var v = normalized.GetDouble();
        // Number fields are float32: compare in float so a value exactly at a published bound is accepted.
        var (min, max) = f.Type == "number" ? ((double)(float)r.Min, (double)(float)r.Max) : (r.Min, r.Max);
        if (v < min || v > max) throw new InvalidDataException($"{f.DisplayName} must be between {r.Min.ToString(CultureInfo.InvariantCulture)} and {r.Max.ToString(CultureInfo.InvariantCulture)}{(string.IsNullOrWhiteSpace(r.Reason) ? "" : ": " + r.Reason)}");
    }
    public static bool Equal(EntityField f, JsonElement a, JsonElement b) => JsonElement.DeepEquals(Normalize(f, a), Normalize(f, b));
    public static string Text(EntityField f, JsonElement v) => f.IsReference ? Normalize(f, v).GetString()!
        : f.Type == "number" ? Normalize(f, v).GetSingle().ToString("R", CultureInfo.InvariantCulture) : Normalize(f, v).GetRawText();
    public static string Lua(EntityField f, JsonElement v) => f.IsReference ? LuaGenerator.Quote(Text(f, v)) : Text(f, v);
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
            && f.Target.Zone == c.Zone && f.Target.Mount == c.Mount && f.SemanticFieldId == c.SemanticFieldId)
        ?? throw new InvalidDataException("Vehicle/backpack capability is missing. Review or reset this modification.");
    public EntityChange Create(SdkMetadata sdk, string instance, string value)
    {
        var f = Catalog(sdk).Field(instance) ?? throw new InvalidDataException("Vehicle/backpack capability is missing.");
        if (!f.Editable) throw new InvalidDataException(f.Reason ?? "Read-only field.");
        JsonElement desired;
        try
        {
            // Mount replacements are chosen by published semantic ID; scalars are parsed as JSON numbers.
            desired = EntityScalar.Normalize(f, f.IsReference ? JsonSerializer.SerializeToElement(value) : JsonDocument.Parse(value).RootElement);
            EntityScalar.CheckRange(f, desired);
        }
        catch (JsonException e) { throw new InvalidDataException("Enter a complete scalar value.", e); }
        return new() { Resource = f.Target.Resource, Entity = f.Target.Entity, Path = f.Target.Path, Zone = f.Target.Zone, Mount = f.Target.Mount,
            InstanceKey = f.InstanceKey, SemanticFieldId = f.SemanticFieldId, FieldType = f.Type, ExpectedValue = f.CurrentDefault.Clone(),
            DesiredValue = desired, BaselineSdkVersion = sdk.Version, CapabilityEvidence = Evidence(f) };
    }
    public void Validate(ModProject p, SdkMetadata sdk, EntityChange c)
    {
        var f = Resolve(Catalog(sdk), c);
        if (!f.Editable) throw new InvalidDataException(f.Reason ?? "Read-only field.");
        if (c.InstanceKey != f.InstanceKey || c.Resource != f.Target.Resource || c.Entity != f.Target.Entity || c.Path != f.Target.Path || c.Zone != f.Target.Zone
            || c.Mount != f.Target.Mount || c.SemanticFieldId != f.SemanticFieldId || c.FieldType != f.Type || c.CapabilityEvidence != Evidence(f))
            throw new InvalidDataException("Vehicle/backpack capability, evidence or ownership changed. Review and accept the current capability, or reset the change.");
        if (!EntityScalar.Equal(f, c.ExpectedValue, f.CurrentDefault)) throw new InvalidDataException("Vehicle/backpack baseline changed. Review before accepting the new baseline.");
        EntityScalar.CheckRange(f, c.DesiredValue);
        if (f.AllowSharedRequired && !Approved(p, f)) throw new InvalidDataException("Acknowledge this shared object before building.");
        if (f.Acknowledgement != null && c.ReferenceAcknowledgement != ReferenceEvidence(f, c.DesiredValue))
            throw new InvalidDataException(f.IsReference ? "Acknowledge the unverified mount reference and package-loading risk before building."
                : f.Target.Resource == "booster" ? "Acknowledge the unverified booster effect before building." : "Acknowledge the unverified magazine attachment effect before building.");
    }
    public static string Evidence(EntityField f) => SupportChangeService.Hash(JsonSerializer.Serialize(new { f.Target, f.Type, f.Editable, f.ApiFieldConstant,
        f.BackingObjectId, f.OperationGroup, f.PlanGroup, f.SharedScopeKey, f.Shared, Tier = f.Evidence.Tier, f.AllowedValues, f.Acknowledgement, f.ResidencyWarning }));
    // Acknowledgement of a Runtime-required unverified opt-in. A mount reference acknowledgement covers one exact replacement;
    // an unverified-effect acknowledgement covers the field regardless of value. Both bind the published warning and evidence tier.
    public static string ReferenceEvidence(EntityField f, JsonElement desired) => SupportChangeService.Hash(JsonSerializer.Serialize(new { f.InstanceKey,
        Desired = !f.IsReference ? null : desired.ValueKind == JsonValueKind.String ? desired.GetString() : desired.GetRawText(), f.Acknowledgement, f.ResidencyWarning, Tier = f.Evidence.Tier }));
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
// Flags are emitted only when Runtime requires them and the user acknowledged them: allow_shared and allow_unverified_reference.
public sealed class EntityLua(IEntityChangeService service) : IEntityLua
{
    public IReadOnlyList<string> Operations(ModProject project, SdkMetadata sdk, OptionBindings? options = null)
    {
        var active = project.EntityChanges.Where(c => c.Enabled).OrderBy(c => c.InstanceKey, StringComparer.Ordinal).ToArray();
        if (active.Length == 0) return [];
        var catalog = EntityChangeService.Catalog(sdk);
        foreach (var c in active) service.Validate(project, sdk, c);
        var output = new List<string>();
        foreach (var plan in active.Where(c => !EntityChangeService.NoOp(sdk, c)).Select(c => (Change: c, Field: EntityChangeService.Resolve(catalog, c)))
            .GroupBy(r => r.Field.PlanGroup).OrderBy(g => g.Key, StringComparer.Ordinal))
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
                var body = "{\n    id=" + LuaGenerator.Quote("entity-" + SupportChangeService.Hash(project.ResourceId + "\n" + group.Key)[..24]) + ",\n    target=" + Target(f.Target) + ",\n";
                if (f.AllowSharedRequired) body += "    allow_shared=true,\n";
                if (rows.Any(r => r.Field.Acknowledgement == "allow_unverified_reference")) body += "    allow_unverified_reference=true,\n";
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
    public static string Target(EntityTarget t) => t.Path switch
    {
        "entity" => "hd2.vehicle(" + LuaGenerator.Quote(t.Vehicle!) + ")",
        "damage_zone" => "hd2.vehicle(" + LuaGenerator.Quote(t.Vehicle!) + "):damage_zone(" + LuaGenerator.Quote(t.Zone!) + ")",
        "mount" => "hd2.vehicle(" + LuaGenerator.Quote(t.Vehicle!) + "):mount(" + LuaGenerator.Quote(t.Mount!) + ")",
        "backpack" => "hd2.backpack(" + LuaGenerator.Quote(t.Backpack!) + ")",
        "magazine" when t.Resource == "weapon_attachment" => "hd2.weapon_attachment(" + LuaGenerator.Quote(t.Attachment!) + ")",
        // hd2.booster(name):deployed_entity() / status_effect() (0.24.0) and :tuning() / explosion() / status_damage() / granted_stratagem() (0.25.0).
        _ when t.Resource == "booster" && BoosterAuthoringReader.Paths.Contains(t.Path) => "hd2.booster(" + LuaGenerator.Quote(t.Booster!) + "):" + t.Path + "()",
        _ => throw new InvalidDataException("Unsupported vehicle/backpack target."),
    };
}
