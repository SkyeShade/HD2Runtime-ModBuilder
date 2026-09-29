using System.Globalization;
using System.Text.Json;
using HD2RuntimeGUI.Core.Metadata;
using HD2RuntimeGUI.Core.Models;

namespace HD2RuntimeGUI.Core.Generation;

public static class StratagemScalar
{
    public static JsonElement Normalize(StratagemField f, JsonElement value)
    {
        if (f.Type == "boolean" && value.ValueKind is JsonValueKind.True or JsonValueKind.False) return value.Clone();
        if (f.Type == StratagemUses.Type) return StratagemUses.Normalize(f, value);
        if (value.ValueKind == JsonValueKind.Number)
        {
            if (f.Type == "integer" && value.TryGetDecimal(out var n) && n == decimal.Truncate(n) && n >= int.MinValue && n <= uint.MaxValue) return JsonSerializer.SerializeToElement(n);
            if (f.Type == "number" && value.TryGetDouble(out var x) && float.IsFinite((float)x)) return JsonSerializer.SerializeToElement((float)x);
        }
        throw new InvalidDataException("Enter a complete, finite value of the published scalar type.");
    }
    public static bool Equal(StratagemField f, JsonElement a, JsonElement b) => JsonElement.DeepEquals(Normalize(f, a), Normalize(f, b));
    // Lua literal. Mission uses are Runtime's 'unlimited' token or an integer.
    public static string Text(StratagemField f, JsonElement v) => v.ValueKind == JsonValueKind.Null ? "Unknown" : f.Type switch
    {
        "number" => Normalize(f, v).GetSingle().ToString("R", CultureInfo.InvariantCulture),
        StratagemUses.Type => StratagemUses.Lua(Normalize(f, v)),
        _ => Normalize(f, v).GetRawText(),
    };
    // What a person reads ("Unlimited" rather than the Lua token).
    public static string Display(StratagemField f, JsonElement v) => f.Type == StratagemUses.Type && v.ValueKind != JsonValueKind.Null ? StratagemUses.Text(Normalize(f, v)) : Text(f, v);
}
public interface IStratagemChangeService
{
    StratagemChange Create(SdkMetadata sdk, string instance, string value);
    void Validate(ModProject p, SdkMetadata sdk, StratagemChange change);
}
public sealed class StratagemChangeService : IStratagemChangeService
{
    public static StratagemCatalog Catalog(SdkMetadata sdk) => sdk.Stratagems ?? throw new InvalidDataException("Explicitly rebind to SDK 0.21 or newer for stratagem authoring.");
    public static string TargetKind(StratagemField f) => f.Target.Path switch
    {
        "deployed_entity" or "damage_zone" => "deployed_entity",
        _ when StratagemCatalog.EntityComponents.Contains(f.Target.Path) => "deployed_entity",
        "weapon" => "mounted_weapon",
        "attack" when f.Target.Weapon != null => "mounted_weapon",
        _ => "stratagem",
    };
    public StratagemChange Create(SdkMetadata sdk, string instance, string value)
    {
        var f = Catalog(sdk).Field(instance); Writable(f);
        try
        {
            using var doc = JsonDocument.Parse(value);
            var desired = StratagemScalar.Normalize(f, doc.RootElement);
            if (f.Type == StratagemUses.Type) StratagemUses.CheckTransition(f, f.CurrentDefault, desired);
            return new() { InstanceKey = instance, TargetKind = TargetKind(f), Stratagem = f.Target.Stratagem, Path = f.Target.Path,
                Entity = f.Target.Entity, Weapon = f.Target.Weapon, Zone = f.Target.Zone, Attack = f.Target.Attack,
                SemanticFieldId = f.SemanticFieldId, FieldType = f.Type, ExpectedValue = f.CurrentDefault.Clone(), DesiredValue = desired,
                BaselineSdkVersion = sdk.Version, CapabilityEvidence = Evidence(f) };
        }
        catch (JsonException e) { throw new InvalidDataException("Enter a complete scalar value.", e); }
    }
    private static void Writable(StratagemField f) { if (!f.Editable) throw new InvalidDataException(f.Reason ?? "Read-only stratagem field."); }
    public static StratagemField Resolve(StratagemCatalog catalog, StratagemChange c) => catalog.Resolve(c)
        ?? throw new InvalidDataException("Stratagem capability is missing. Review or reset this modification.");
    public void Validate(ModProject p, SdkMetadata sdk, StratagemChange c)
    {
        var f = Resolve(Catalog(sdk), c); Writable(f);
        if (c.InstanceKey != f.InstanceKey || c.TargetKind != TargetKind(f) || c.Stratagem != f.Target.Stratagem || c.Path != f.Target.Path
            || c.Entity != f.Target.Entity || c.Weapon != f.Target.Weapon || c.Zone != f.Target.Zone || c.Attack != f.Target.Attack
            || c.SemanticFieldId != f.SemanticFieldId || c.FieldType != f.Type || c.CapabilityEvidence != Evidence(f))
            throw new InvalidDataException("Stratagem capability or ownership changed. Review and accept the current capability, or reset the change.");
        if (!StratagemScalar.Equal(f, c.ExpectedValue, f.CurrentDefault)) throw new InvalidDataException("Stratagem baseline changed. Review the saved and current values before accepting the new baseline.");
        _ = StratagemScalar.Normalize(f, c.DesiredValue);
        if (f.Type == StratagemUses.Type) StratagemUses.CheckTransition(f, c.ExpectedValue, c.DesiredValue);
        // allow_shared / allow_unverified_effect are implicit: shown as warnings and always emitted where Runtime requires them.
    }
    // 0.26.0: Runtime requires allow_unverified_effect for this change (mission uses except gameplay-proven targets).
    public static bool EffectRequired(StratagemField f, JsonElement desired) => f.Type == StratagemUses.Type ? StratagemUses.EffectRequired(f, desired) : f.Acknowledgement == "allow_unverified_effect";
    public static string EffectEvidence(StratagemField f) => SupportChangeService.Hash(JsonSerializer.Serialize(new { f.InstanceKey, f.Acknowledgement, f.AcknowledgementReason }));
    public static string Evidence(StratagemField f) => SupportChangeService.Hash(JsonSerializer.Serialize(new { f.Target, f.Type, f.Editable, f.ApiFieldConstant,
        f.BackingObjectId, f.OperationGroup, f.PlanGroup, f.PlanPhase, f.DependsOn, Approval = ApprovalEvidence(f) }));
    // Schema 1 publishes the reviewed scope on the backing identity itself. Schema 2 publishes an exact shared-scope key;
    // one acknowledgement covers exactly that key and its reviewed consumer list.
    public static string ApprovalEvidence(StratagemField f) => f.SharedScopeKey == null
        ? SupportChangeService.Hash(JsonSerializer.Serialize(new { f.BackingObjectId, f.Shared, f.AllowSharedRequired, f.ReviewedScopeComplete, f.DynamicConsumersPossible, Consumers = SortedConsumers(f) }))
        : SupportChangeService.Hash(JsonSerializer.Serialize(new { f.SharedScopeKey, f.Shared, f.AllowSharedRequired, f.ReviewedScopeComplete, f.DynamicConsumersPossible, Consumers = SortedConsumers(f) }));
    // Scope content without any opaque identity, used only to decide whether a rebind changed the reviewed scope.
    public static string ScopeContent(StratagemField f) => SupportChangeService.Hash(JsonSerializer.Serialize(new { f.BackingObjectKind, f.Shared, f.AllowSharedRequired,
        f.ReviewedScopeComplete, f.DynamicConsumersPossible, Consumers = SortedConsumers(f) }));
    // Capability content without opaque backing/operation/plan identities.
    public static string SemanticEvidence(StratagemField f) => SupportChangeService.Hash(JsonSerializer.Serialize(new { f.Target, f.Type, f.Editable, f.ApiFieldConstant,
        f.BackingObjectKind, f.Requires, f.PlanPhase, f.DependsOn, Scope = ScopeContent(f) }));
    private static IEnumerable<StratagemConsumer> SortedConsumers(StratagemField f) => f.SharedConsumers.OrderBy(c => c.Stratagem, StringComparer.Ordinal).ThenBy(c => c.Path, StringComparer.Ordinal);
    public static bool Approved(ModProject p, StratagemField f) => p.StratagemApprovals.GetValueOrDefault(f.ScopeKey) == ApprovalEvidence(f);
    // Coalesce only the SDK's explicitly shared Eagle rearm handles, never independent branch instances.
    public static bool SameEdit(StratagemField a, StratagemField b) => a.InstanceKey == b.InstanceKey ||
        a.Target.Path == "eagle_rearm" && b.Target.Path == "eagle_rearm" && a.BackingObjectId == b.BackingObjectId && a.ApiFieldConstant == b.ApiFieldConstant;
    public static StratagemChange? Saved(ModProject? p, StratagemCatalog c, StratagemField f) => p?.StratagemChanges.SingleOrDefault(x =>
        c.Resolve(x) is { } other && SameEdit(f, other));
    public static bool NoOp(SdkMetadata sdk, StratagemChange c)
    {
        var f = sdk.Stratagems?.Resolve(c);
        return f != null && f.InstanceKey == c.InstanceKey && f.Type == c.FieldType && f.CurrentDefault.ValueKind != JsonValueKind.Null
            && StratagemScalar.Equal(f, c.ExpectedValue, f.CurrentDefault) && StratagemScalar.Equal(f, c.DesiredValue, f.CurrentDefault);
    }
    // Explicit rebind: move saved intent to the canonical instance with the same semantic target. Evidence is refreshed only when
    // the previously valid capability is semantically unchanged (target, type, API field, backing kind, sequencing and reviewed scope);
    // otherwise the old evidence is kept so the change requires review. Baselines and desired values are never altered.
    public static int Rebind(ModProject p, StratagemCatalog? previous, StratagemCatalog next)
    {
        var moved = 0; var approvals = new Dictionary<string, string>(StringComparer.Ordinal);
        p.StratagemChanges = p.StratagemChanges.Select(c =>
        {
            if (next.Resolve(c) is not { } f || f.InstanceKey == c.InstanceKey && c.CapabilityEvidence == Evidence(f)) return c;
            var old = previous?.Find(c.InstanceKey);
            var unchanged = old != null && c.CapabilityEvidence == Evidence(old) && SemanticEvidence(old) == SemanticEvidence(f) && old.Type == f.Type
                && JsonElement.DeepEquals(old.CurrentDefault, f.CurrentDefault);
            if (unchanged && old!.AllowSharedRequired && Approved(p, old) && ScopeContent(old) == ScopeContent(f))
                approvals[f.ScopeKey] = ApprovalEvidence(f);
            moved++;
            return c with { InstanceKey = f.InstanceKey, TargetKind = TargetKind(f), Entity = f.Target.Entity, Weapon = f.Target.Weapon, Zone = f.Target.Zone,
                CapabilityEvidence = unchanged ? Evidence(f) : c.CapabilityEvidence };
        }).ToList();
        foreach (var (key, value) in approvals) p.StratagemApprovals[key] = value;
        // Acknowledgements for scopes absent from the new catalog can never authorize a write; drop them.
        var scopes = next.FieldInstances.Select(f => f.ScopeKey).ToHashSet(StringComparer.Ordinal);
        foreach (var key in p.StratagemApprovals.Keys.Where(k => !scopes.Contains(k)).ToArray()) p.StratagemApprovals.Remove(key);
        return moved;
    }
}
