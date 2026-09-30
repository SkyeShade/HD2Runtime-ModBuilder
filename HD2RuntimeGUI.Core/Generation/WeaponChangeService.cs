using System.Text.Json;
using HD2RuntimeGUI.Core.Localization;
using HD2RuntimeGUI.Core.Metadata;
using HD2RuntimeGUI.Core.Models;

namespace HD2RuntimeGUI.Core.Generation;

public interface IWeaponChangeService
{
    WeaponChange Create(SdkMetadata sdk, string weapon, string field, string value, bool sharedAcknowledged);
    void Validate(SdkMetadata sdk, WeaponChange change);
    IReadOnlyList<WeaponChangeIssue> Review(SdkMetadata sdk, IEnumerable<WeaponChange> changes);
}
public sealed record WeaponChangeIssue(Guid ChangeId, string Message);
public sealed class WeaponChangeService : IWeaponChangeService
{
    public WeaponChange Create(SdkMetadata sdk, string weapon, string field, string value, bool sharedAcknowledged)
    {
        var catalog = Catalog(sdk); var original = catalog.Field(weapon, field);
        var capability = catalog.FindCanonicalField(weapon, field)!;
        if (!original.WriteAccepted || !capability.WriteAccepted) throw new InvalidDataException(original.Reason ?? CoreText.Get("Messages.Build.Weapon.WriteNotAccepted"));
        JsonElement parsed;
        try { using var document = JsonDocument.Parse(value); parsed = document.RootElement.Clone(); }
        catch (JsonException e) { throw new InvalidDataException(CoreText.Get("Messages.Build.Field.InvalidValue"), e); }
        parsed = WeaponScalar.Normalize(capability, parsed);
        CheckComposition(sdk, weapon, capability, parsed);
        var c = new WeaponChange { Weapon = weapon, Subweapon = catalog.FindSubweapon(weapon)?.Kind, SemanticFieldId = capability.SemanticFieldId, ExpectedValue = capability.CurrentDefault.Clone(), DesiredValue = parsed,
            FieldType = capability.Type, SharedAcknowledged = sharedAcknowledged && capability.AffectsMultipleWeapons,
            AcknowledgedWriteScope = sharedAcknowledged ? capability.WriteScope : null, AcknowledgedConsumerCount = sharedAcknowledged ? capability.Backing?.ConsumerCount : null,
            AcknowledgedAffectedWeapons = sharedAcknowledged ? capability.SharedWithWeapons.Order(StringComparer.Ordinal).ToList() : [], BaselineSdkVersion = sdk.Version };
        // Pending acknowledgement is allowed in saved projects, but validation blocks the build.
        ValidateValue(capability, parsed);
        if (!capability.Editable || Catalog(sdk).Weapon(weapon).OrdinaryWritesBlocked) throw new InvalidDataException(capability.Reason ?? CoreText.Get("Messages.Build.Weapon.AuthoringUnavailable"));
        return c;
    }
    public void Validate(SdkMetadata sdk, WeaponChange c)
    {
        var catalog = Catalog(sdk); var weapon = catalog.Weapon(c.Weapon); var original = catalog.Field(c.Weapon, c.SemanticFieldId);
        var f = catalog.FindCanonicalField(c.Weapon, c.SemanticFieldId)!;
        if (!original.WriteAccepted || !f.WriteAccepted) throw new InvalidDataException(original.Reason ?? CoreText.Get("Messages.Build.Weapon.WriteNotAccepted"));
        if (weapon.OrdinaryWritesBlocked) throw new InvalidDataException(weapon.BlockReason ?? CoreText.Get("Messages.Build.Weapon.AmbiguousIdentity"));
        if (!f.Editable || f.DerivedReadOnly || f.Backing == null) throw new InvalidDataException(f.Reason ?? CoreText.Get("Messages.Build.Weapon.FieldUnavailable"));
        if (c.FieldType != f.Type) throw new InvalidDataException(CoreText.Get("Messages.Build.Weapon.TypeChanged"));
        // A sub-target change names its kind; a weapon change never does (the published target must still be the same kind of object).
        if (c.Subweapon != catalog.FindSubweapon(c.Weapon)?.Kind) throw new InvalidDataException(CoreText.Format("Messages.Build.Weapon.SubweaponChanged", c.Weapon));
        ValidateValue(f, c.ExpectedValue); ValidateValue(f, c.DesiredValue);
        CheckComposition(sdk, c.Weapon, f, c.DesiredValue);
        if (f.Format(c.ExpectedValue) != f.Format(f.CurrentDefault)) throw new InvalidDataException(CoreText.Format("Messages.Build.Weapon.BaselineChanged", f.Format(c.ExpectedValue), f.Format(f.CurrentDefault)));
        // allow_shared / allow_unverified_effect are implicit: shown as warnings and always emitted where Runtime requires them.
    }
    // 0.26.0: acknowledgement of Runtime's allow_unverified_effect opt-in for one weapon field, bound to its published reason.
    public static string EffectEvidence(string weapon, WeaponCapability f) => SupportChangeService.Hash(JsonSerializer.Serialize(new { weapon, f.SemanticFieldId, f.Acknowledgement, f.AcknowledgementReason }));
    // fire_mode.modes rewrites the whole native mode vector; the older default-fire-mode view covers the same bytes (Runtime rejects both in one plan).
    public static string? FireModeConflict(IEnumerable<WeaponChange> changes, string weapon) =>
        changes.Any(c => c.Enabled && c.Weapon == weapon && c.SemanticFieldId == FireModes.ModesField) && changes.Any(c => c.Enabled && c.Weapon == weapon && c.SemanticFieldId is "weapon.default_fire_mode" or "weapon.primary_fire_mode")
            ? CoreText.Format("Messages.Build.Weapon.FireModeConflict", weapon) : null;
    // A shared-setting acknowledgement covers the exact write scope, consumer count and affected weapons published today.
    public static bool SharedAcknowledgementCurrent(WeaponCapability f, WeaponChange c) => c.SharedAcknowledged && c.AcknowledgedWriteScope == f.WriteScope
        && c.AcknowledgedConsumerCount == f.Backing?.ConsumerCount && c.AcknowledgedAffectedWeapons.Order(StringComparer.Ordinal).SequenceEqual(f.SharedWithWeapons.Order(StringComparer.Ordinal));
    public IReadOnlyList<WeaponChangeIssue> Review(SdkMetadata sdk, IEnumerable<WeaponChange> changes)
    {
        var issues = new List<WeaponChangeIssue>();
        var list = changes.ToArray();
        foreach (var group in WeaponAliasResolver.Group(sdk, list).Where(g => g.Conflict != null))
            issues.Add(new(group.Representative.Id, group.Conflict!));
        foreach (var c in list.Where(c => c.Enabled))
            try { Validate(sdk, c); } catch (InvalidDataException e) { issues.Add(new(c.Id, e.Message)); }
        foreach (var weapon in list.Select(c => c.Weapon).Distinct())
            if (FireModeConflict(list, weapon) is { } conflict) issues.Add(new(list.First(c => c.Weapon == weapon && c.SemanticFieldId == FireModes.ModesField).Id, conflict));
        // 1.4.0: the rate-of-fire selector group is one Runtime transaction, and fields over the same native bytes are never combined.
        foreach (var (id, message) in WeaponSelectorRules.PlayerIssues(sdk, list)) issues.Add(new(id, message));
        return issues;
    }
    // The published values a 1.4.0 field accepts that need the SDK beyond the field itself: a function projectile's donor outputs.
    public static void CheckComposition(SdkMetadata sdk, string weapon, WeaponCapability f, JsonElement value)
    {
        if (f.Type == WeaponCapability.FunctionProjectileReference && !JsonElement.DeepEquals(value, f.CurrentDefault))
            FunctionProjectile.Check(sdk, weapon, FunctionProjectile.Token(f.CurrentDefault), FunctionProjectile.Token(value));
    }
    public static PlayerWeaponCatalog Catalog(SdkMetadata sdk) => sdk.PlayerWeapons ?? throw new InvalidDataException(CoreText.Get("Messages.Build.Weapon.SdkTooOld"));
    public static void ValidateValue(WeaponCapability f, JsonElement value)
    {
        if (f.Type is "projectile_reference" or "explosion_reference") throw new InvalidDataException(CoreText.Get("Messages.Build.Weapon.ReferenceNeedsComposition"));
        if (f.Type == WeaponCapability.FireModeSet)
        {
            if (f.AllowedModes is not { } allowed || f.MaxModes is not int maxModes) throw new InvalidDataException(f.Reason ?? CoreText.Get("Messages.Build.Weapon.FireModesReadOnly"));
            FireModes.ValidateValue(value, allowed, maxModes); return;
        }
        // 1.4.0 composition types: each validated against what the field publishes (a function projectile's donor is checked with the SDK
        // in CheckComposition; its saved expect is the published baseline object).
        switch (f.Type)
        {
            case WeaponCapability.FireRateSet: FireRateModes.Normalize(value, f.Min ?? 1, f.Max ?? 3000, f.MaxModes ?? 1); return;
            case WeaponCapability.WeaponFunction: WeaponFunctions.Normalize(value, f.AllowedNames); return;
            case WeaponCapability.FunctionProjectileReference: if (!JsonElement.DeepEquals(value, f.CurrentDefault)) FunctionProjectile.Token(value); return;
            case WeaponCapability.TraitSet: TraitSets.Normalize(value, f.MaxTraits ?? 5, f.TraitValues?.Keys.ToArray()); return;
            case WeaponCapability.ArmorPenetrationLabel:
                if (value.ValueKind != JsonValueKind.String) throw new InvalidDataException(CoreText.Get("Messages.Build.Traits.Invalid"));
                TraitSets.Label(value.GetString()!, f.AllowedNames); return;
            case WeaponCapability.StatusReference: StatusReference.Normalize(value, f.AllowedReferences ?? [], f.AllowNone == true); return;
        }
        if (f.Type == "boolean")
        { if (value.ValueKind is not (JsonValueKind.True or JsonValueKind.False)) throw new InvalidDataException(CoreText.Get("Messages.Build.Value.ExpectedBoolean")); return; }
        if (f.Type == "enum")
        { if (f.EnumValues == null || !f.EnumValues.Values.Any(v => JsonElement.DeepEquals(v, value)) || f.AllowedValues != null && (!value.TryGetInt32(out var n) || !f.AllowedValues.Contains(n))) throw new InvalidDataException(CoreText.Get("Messages.Build.Weapon.ModeNotAllowed")); return; }
        if (value.ValueKind != JsonValueKind.Number || !value.TryGetDouble(out var number) || !double.IsFinite(number) || (f.Type == "integer" && Math.Truncate(number) != number)) throw new InvalidDataException(CoreText.Get("Messages.Build.Value.FiniteOfFieldType"));
        // Representable storage bounds are not invented gameplay bounds.
        if ((f.Backing?.Storage == "f32" && !float.IsFinite((float)number)) || (f.Backing?.Storage == "u32" && (number < 0 || number > uint.MaxValue)) || (f.Backing?.Storage == "i32" && (number < int.MinValue || number > int.MaxValue)) || (f.Backing?.Storage == "u8" && (number < 0 || number > byte.MaxValue)) || (f.Min is double min && number < min) || (f.Max is double max && number > max)) throw new InvalidDataException(CoreText.Get("Messages.Build.Value.OutOfBounds"));
    }
}
