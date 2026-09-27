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
        if (value.ValueKind == JsonValueKind.Number)
        {
            if (f.Type == "integer" && value.TryGetDecimal(out var n) && n == decimal.Truncate(n) && n >= int.MinValue && n <= uint.MaxValue) return JsonSerializer.SerializeToElement(n);
            if (f.Type == "number" && value.TryGetDouble(out var x) && float.IsFinite((float)x)) return JsonSerializer.SerializeToElement((float)x);
        }
        throw new InvalidDataException("Enter a complete, finite value of the published scalar type.");
    }
    public static bool Equal(StratagemField f, JsonElement a, JsonElement b) => JsonElement.DeepEquals(Normalize(f, a), Normalize(f, b));
    public static string Text(StratagemField f, JsonElement v) => v.ValueKind == JsonValueKind.Null ? "Unknown" : f.Type == "number"
        ? Normalize(f, v).GetSingle().ToString("R", CultureInfo.InvariantCulture) : Normalize(f, v).GetRawText();
}
public interface IStratagemChangeService
{
    StratagemChange Create(SdkMetadata sdk, string instance, string value);
    void Validate(ModProject p, SdkMetadata sdk, StratagemChange change);
}
public sealed class StratagemChangeService : IStratagemChangeService
{
    public static StratagemCatalog Catalog(SdkMetadata sdk) => sdk.Stratagems ?? throw new InvalidDataException("Explicitly rebind to SDK 0.21 or newer for stratagem authoring.");
    public StratagemChange Create(SdkMetadata sdk, string instance, string value)
    {
        var f = Catalog(sdk).Field(instance); Writable(f);
        try
        {
            using var doc = JsonDocument.Parse(value);
            return new() { InstanceKey = instance, Stratagem = f.Target.Stratagem, Path = f.Target.Path, Attack = f.Target.Attack,
                SemanticFieldId = f.SemanticFieldId, FieldType = f.Type, ExpectedValue = f.CurrentDefault.Clone(), DesiredValue = StratagemScalar.Normalize(f, doc.RootElement),
                BaselineSdkVersion = sdk.Version, CapabilityEvidence = Evidence(f) };
        }
        catch (JsonException e) { throw new InvalidDataException("Enter a complete scalar value.", e); }
    }
    private static void Writable(StratagemField f) { if (!f.Editable) throw new InvalidDataException(f.Reason ?? "Read-only stratagem field."); }
    public void Validate(ModProject p, SdkMetadata sdk, StratagemChange c)
    {
        var f = Catalog(sdk).Field(c.InstanceKey); Writable(f);
        if (c.TargetKind != "stratagem" || c.Stratagem != f.Target.Stratagem || c.Path != f.Target.Path || c.Attack != f.Target.Attack
            || c.SemanticFieldId != f.SemanticFieldId || c.FieldType != f.Type || c.CapabilityEvidence != Evidence(f))
            throw new InvalidDataException("Stratagem capability or ownership changed. Review and accept the current capability, or reset the change.");
        if (!StratagemScalar.Equal(f, c.ExpectedValue, f.CurrentDefault)) throw new InvalidDataException("Stratagem baseline changed. Review the saved and current values before accepting the new baseline.");
        _ = StratagemScalar.Normalize(f, c.DesiredValue);
        if (f.AllowSharedRequired && !Approved(p, f)) throw new InvalidDataException("Acknowledge this shared stratagem object and affected consumers before building.");
    }
    public static string Evidence(StratagemField f) => SupportChangeService.Hash(JsonSerializer.Serialize(new { f.Target, f.Type, f.Editable, f.ApiFieldConstant,
        f.BackingObjectId, f.OperationGroup, f.PlanGroup, f.PlanPhase, f.DependsOn, Approval = ApprovalEvidence(f) }));
    // v1 publishes the reviewed scope on the backing identity itself; there is no separate scope-key property.
    public static string ApprovalEvidence(StratagemField f) => SupportChangeService.Hash(JsonSerializer.Serialize(new { f.BackingObjectId, f.Shared,
        f.AllowSharedRequired, f.ReviewedScopeComplete, f.DynamicConsumersPossible, Consumers = f.SharedConsumers.OrderBy(c => c.Stratagem, StringComparer.Ordinal).ThenBy(c => c.Path, StringComparer.Ordinal) }));
    public static bool Approved(ModProject p, StratagemField f) => p.StratagemApprovals.GetValueOrDefault(f.BackingObjectId) == ApprovalEvidence(f);
    // Coalesce only the SDK's explicitly shared Eagle rearm handles, never independent branch instances.
    public static bool SameEdit(StratagemField a, StratagemField b) => a.InstanceKey == b.InstanceKey ||
        a.Target.Path == "eagle_rearm" && b.Target.Path == "eagle_rearm" && a.BackingObjectId == b.BackingObjectId && a.ApiFieldConstant == b.ApiFieldConstant;
    public static StratagemChange? Saved(ModProject? p, StratagemCatalog c, StratagemField f) => p?.StratagemChanges.SingleOrDefault(x =>
        c.FieldInstances.FirstOrDefault(y => y.InstanceKey == x.InstanceKey) is { } other && SameEdit(f, other));
    public static bool NoOp(SdkMetadata sdk, StratagemChange c)
    {
        var f = sdk.Stratagems?.FieldInstances.FirstOrDefault(f => f.InstanceKey == c.InstanceKey);
        return f != null && f.Type == c.FieldType && f.CurrentDefault.ValueKind != JsonValueKind.Null
            && StratagemScalar.Equal(f, c.ExpectedValue, f.CurrentDefault) && StratagemScalar.Equal(f, c.DesiredValue, f.CurrentDefault);
    }
}
