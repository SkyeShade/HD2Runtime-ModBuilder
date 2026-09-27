using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using HD2RuntimeGUI.Core.Metadata;
using HD2RuntimeGUI.Core.Models;

namespace HD2RuntimeGUI.Core.Generation;

public static class SupportScalar
{
    public static JsonElement Normalize(SupportField f, JsonElement v)
    {
        if (f.Value.Type == "boolean" && v.ValueKind is JsonValueKind.True or JsonValueKind.False) return v.Clone();
        if (v.ValueKind != JsonValueKind.Number) throw new InvalidDataException("Enter a valid scalar value.");
        if (f.Value.Type == "integer" && v.TryGetDecimal(out var n) && n == decimal.Truncate(n) && n >= int.MinValue && n <= uint.MaxValue)
        {
            var normalized = JsonSerializer.SerializeToElement(n);
            if (JsonElement.DeepEquals(v, normalized)) return normalized;
            throw new InvalidDataException("Expected an exact integer.");
        }
        if (f.Value.Type == "number" && v.TryGetDouble(out var x) && float.IsFinite((float)x)) return JsonSerializer.SerializeToElement((float)x);
        throw new InvalidDataException("Enter a finite value of the published scalar type.");
    }
    public static bool Equal(SupportField f, JsonElement a, JsonElement b) => JsonElement.DeepEquals(Normalize(f, a), Normalize(f, b));
    public static string Text(SupportField f, JsonElement value) => f.Value.Type == "number"
        ? ((float)value.GetDouble()).ToString("R", CultureInfo.InvariantCulture) : Normalize(f, value).GetRawText();
}
public interface ISupportChangeService
{
    SupportChange Create(SdkMetadata sdk, string instance, string value);
    void Validate(ModProject project, SdkMetadata sdk, SupportChange change);
}
public sealed class SupportChangeService : ISupportChangeService
{
    public static SupportAuthoringCatalog Catalog(SdkMetadata sdk) => sdk.SupportAuthoring ?? throw new InvalidDataException("Rebind explicitly to SDK 0.20.1 or newer for support authoring.");
    public SupportChange Create(SdkMetadata sdk, string instance, string value)
    {
        var f = Catalog(sdk).Field(instance); CheckWritable(sdk, f);
        JsonElement parsed;
        try { using var d = JsonDocument.Parse(value); parsed = SupportScalar.Normalize(f, d.RootElement); }
        catch (JsonException e) { throw new InvalidDataException("Enter a complete numeric value.", e); }
        return new() { InstanceKey = instance, Weapon = f.SupportWeapon, AttackRole = f.Target.AttackRole, SemanticFieldId = f.SemanticFieldId,
            FieldType = f.Value.Type, ExpectedValue = f.Value.Baseline.Clone(), DesiredValue = parsed, BaselineSdkVersion = sdk.Version, CapabilityEvidence = Evidence(f) };
    }
    public void Validate(ModProject project, SdkMetadata sdk, SupportChange c)
    {
        var f = Catalog(sdk).Field(c.InstanceKey); CheckWritable(sdk, f);
        if (c.Weapon != f.SupportWeapon || c.AttackRole != f.Target.AttackRole || c.SemanticFieldId != f.SemanticFieldId || c.FieldType != f.Value.Type || c.CapabilityEvidence != Evidence(f))
            throw new InvalidDataException("Support capability or ownership changed. Review and accept the current capability, or reset this modification.");
        if (!SupportScalar.Equal(f, c.ExpectedValue, f.Value.Baseline)) throw new InvalidDataException($"Support SDK baseline changed: saved {SupportScalar.Text(f, c.ExpectedValue)}, current {SupportScalar.Text(f, f.Value.Baseline)}. Review and explicitly accept the new baseline.");
        _ = SupportScalar.Normalize(f, c.DesiredValue);
        if (f.SharedScope.RequiresAcknowledgement && !Approved(project, f)) throw new InvalidDataException("Acknowledge this shared object and its affected consumers before building.");
    }
    private static void CheckWritable(SdkMetadata sdk, SupportField f)
    {
        var w = Catalog(sdk).Weapons.Single(w => w.Name == f.SupportWeapon);
        if (!w.Writable || w.IdentityStatus != "UNIQUE" || !f.Writable || f.ReadOnly) throw new InvalidDataException(f.BlockedReason ?? "Duplicate or read-only support identity.");
    }
    public static string Evidence(SupportField f) => Hash(JsonSerializer.Serialize(new { f.SupportWeaponIdentity, f.Target, f.ApiFieldConstant, f.Value.Type, f.Backing, f.Operation, f.Resolution, f.SharedScope }));
    public static string ApprovalEvidence(SupportField f) => Hash(JsonSerializer.Serialize(new { f.Backing.ObjectKey, f.SharedScope }));
    public static bool Approved(ModProject p, SupportField f) => p.SupportApprovals.GetValueOrDefault(f.SharedScope.ScopeKey) == ApprovalEvidence(f);
    public static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    public static bool NoOp(SdkMetadata sdk, SupportChange c)
    {
        var f = sdk.SupportAuthoring?.FieldInstances.SingleOrDefault(f => f.InstanceKey == c.InstanceKey);
        return f != null && f.Value.Type == c.FieldType && SupportScalar.Equal(f, c.ExpectedValue, f.Value.Baseline) && SupportScalar.Equal(f, c.DesiredValue, f.Value.Baseline);
    }
}
