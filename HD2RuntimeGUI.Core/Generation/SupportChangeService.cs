using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using HD2RuntimeGUI.Core.Localization;
using HD2RuntimeGUI.Core.Metadata;
using HD2RuntimeGUI.Core.Models;

namespace HD2RuntimeGUI.Core.Generation;

public static class SupportScalar
{
    public static JsonElement Normalize(SupportField f, JsonElement v)
    {
        if (f.Value.Type == "boolean" && v.ValueKind is JsonValueKind.True or JsonValueKind.False) return v.Clone();
        if (f.Value.Type == WeaponCapability.FireModeSet)
        {
            if (f.FireMode is not { AllowedModes: { } allowed, MaxModes: int max }) throw new InvalidDataException(CoreText.Get("Messages.Build.Weapon.FireModesReadOnly"));
            FireModes.ValidateValue(v, allowed, max);
            return JsonSerializer.SerializeToElement(FireModes.Modes(v));
        }
        if (v.ValueKind != JsonValueKind.Number) throw new InvalidDataException(CoreText.Get("Messages.Build.Value.ValidScalar"));
        if (f.Value.Type == "integer" && v.TryGetDecimal(out var n) && n == decimal.Truncate(n) && n >= int.MinValue && n <= uint.MaxValue)
        {
            var normalized = JsonSerializer.SerializeToElement(n);
            if (JsonElement.DeepEquals(v, normalized)) return normalized;
            throw new InvalidDataException(CoreText.Get("Messages.Build.Value.ExpectedInteger"));
        }
        if (f.Value.Type == "number" && v.TryGetDouble(out var x) && float.IsFinite((float)x)) return JsonSerializer.SerializeToElement((float)x);
        throw new InvalidDataException(CoreText.Get("Messages.Build.Value.FinitePublishedType"));
    }
    public static bool Equal(SupportField f, JsonElement a, JsonElement b) => JsonElement.DeepEquals(Normalize(f, a), Normalize(f, b));
    // Lua literal: a fire-mode set is a table of mode names ({'single','burst'}); scalars are their JSON text.
    public static string Text(SupportField f, JsonElement value) => f.Value.Type switch
    {
        "number" => ((float)value.GetDouble()).ToString("R", CultureInfo.InvariantCulture),
        WeaponCapability.FireModeSet => FireModes.Lua(value),
        _ => Normalize(f, value).GetRawText(),
    };
}
public interface ISupportChangeService
{
    SupportChange Create(SdkMetadata sdk, string instance, string value);
    void Validate(ModProject project, SdkMetadata sdk, SupportChange change);
}
public sealed class SupportChangeService : ISupportChangeService
{
    public static SupportAuthoringCatalog Catalog(SdkMetadata sdk) => sdk.SupportAuthoring ?? throw new InvalidDataException(CoreText.Get("Messages.Build.Support.SdkTooOld"));
    public SupportChange Create(SdkMetadata sdk, string instance, string value)
    {
        var f = Catalog(sdk).Field(instance); CheckWritable(sdk, f);
        JsonElement parsed;
        try { using var d = JsonDocument.Parse(value); parsed = SupportScalar.Normalize(f, d.RootElement); }
        catch (JsonException e) { throw new InvalidDataException(CoreText.Get("Messages.Build.Value.CompleteNumeric"), e); }
        return new() { InstanceKey = instance, Weapon = f.SupportWeapon, AttackRole = f.Target.AttackRole, SemanticFieldId = f.SemanticFieldId,
            FieldType = f.Value.Type, ExpectedValue = f.Value.Baseline.Clone(), DesiredValue = parsed, BaselineSdkVersion = sdk.Version, CapabilityEvidence = Evidence(f) };
    }
    public void Validate(ModProject project, SdkMetadata sdk, SupportChange c)
    {
        var f = Catalog(sdk).Field(c.InstanceKey); CheckWritable(sdk, f);
        if (c.Weapon != f.SupportWeapon || c.AttackRole != f.Target.AttackRole || c.SemanticFieldId != f.SemanticFieldId || c.FieldType != f.Value.Type || c.CapabilityEvidence != Evidence(f))
            throw new InvalidDataException(CoreText.Get("Messages.Build.Support.CapabilityChanged"));
        if (!SupportScalar.Equal(f, c.ExpectedValue, f.Value.Baseline)) throw new InvalidDataException(CoreText.Format("Messages.Build.Support.BaselineChanged", SupportScalar.Text(f, c.ExpectedValue), SupportScalar.Text(f, f.Value.Baseline)));
        _ = SupportScalar.Normalize(f, c.DesiredValue);
        // allow_shared / allow_unverified_effect are implicit: shown as warnings and always emitted where Runtime requires them.
    }
    private static void CheckWritable(SdkMetadata sdk, SupportField f)
    {
        var w = Catalog(sdk).Weapons.Single(w => w.Name == f.SupportWeapon);
        if (!w.Writable || !SupportAuthoringWeapon.Resolved(w.IdentityStatus) || !f.Writable || f.ReadOnly) throw new InvalidDataException(f.BlockedReason ?? CoreText.Get("Messages.Build.Support.ReadOnly"));
    }
    public static string Evidence(SupportField f) => Hash(JsonSerializer.Serialize(new { f.SupportWeaponIdentity, f.Target, f.ApiFieldConstant, f.Value.Type, f.Backing, f.Operation, f.Resolution, f.SharedScope }));
    // Acknowledges Runtime's allow_unverified_effect opt-in for one field, regardless of value.
    public static string EffectEvidence(SupportField f) => Hash(JsonSerializer.Serialize(new { f.InstanceKey, f.Operation.Acknowledgement, f.Operation.AcknowledgementReason }));
    public static string ApprovalEvidence(SupportField f) => Hash(JsonSerializer.Serialize(new { f.Backing.ObjectKey, f.SharedScope }));
    public static bool Approved(ModProject p, SupportField f) => p.SupportApprovals.GetValueOrDefault(f.SharedScope.ScopeKey) == ApprovalEvidence(f);
    public static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    public static bool NoOp(SdkMetadata sdk, SupportChange c)
    {
        var f = sdk.SupportAuthoring?.FieldInstances.SingleOrDefault(f => f.InstanceKey == c.InstanceKey);
        return f != null && f.Value.Type == c.FieldType && SupportScalar.Equal(f, c.ExpectedValue, f.Value.Baseline) && SupportScalar.Equal(f, c.DesiredValue, f.Value.Baseline);
    }
}
