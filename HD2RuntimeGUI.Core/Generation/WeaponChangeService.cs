using System.Text.Json;
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
        var capability = Catalog(sdk).Field(weapon, field);
        JsonElement parsed;
        try { using var document = JsonDocument.Parse(value); parsed = document.RootElement.Clone(); }
        catch (JsonException e) { throw new InvalidDataException("Enter a valid field value.", e); }
        var c = new WeaponChange { Weapon = weapon, SemanticFieldId = field, ExpectedValue = capability.CurrentDefault.Clone(), DesiredValue = parsed,
            FieldType = capability.Type, SharedAcknowledged = sharedAcknowledged && capability.AffectsMultipleWeapons,
            AcknowledgedWriteScope = sharedAcknowledged ? capability.WriteScope : null, AcknowledgedConsumerCount = sharedAcknowledged ? capability.Backing?.ConsumerCount : null,
            AcknowledgedAffectedWeapons = sharedAcknowledged ? capability.SharedWithWeapons.Order(StringComparer.Ordinal).ToList() : [], BaselineSdkVersion = sdk.Version };
        // Pending acknowledgement is allowed in saved projects, but validation blocks the build.
        ValidateValue(capability, parsed);
        if (!capability.Editable || Catalog(sdk).Weapon(weapon).OrdinaryWritesBlocked) throw new InvalidDataException(capability.Reason ?? "Ordinary authoring is unavailable for this weapon.");
        return c;
    }
    public void Validate(SdkMetadata sdk, WeaponChange c)
    {
        var weapon = Catalog(sdk).Weapon(c.Weapon); var f = Catalog(sdk).Field(c.Weapon, c.SemanticFieldId);
        if (weapon.OrdinaryWritesBlocked) throw new InvalidDataException(weapon.BlockReason ?? "Ambiguous weapon identity; ordinary writes are blocked.");
        if (!f.Editable || f.DerivedReadOnly || f.Backing == null) throw new InvalidDataException(f.Reason ?? "This field is read-only or unavailable.");
        if (c.FieldType != f.Type) throw new InvalidDataException("The capability type changed; review this modification.");
        ValidateValue(f, c.ExpectedValue); ValidateValue(f, c.DesiredValue);
        if (f.Format(c.ExpectedValue) != f.Format(f.CurrentDefault)) throw new InvalidDataException($"SDK baseline changed: saved {f.Format(c.ExpectedValue)}, current {f.Format(f.CurrentDefault)}. Review and explicitly accept the new baseline.");
        if (f.AffectsMultipleWeapons && (!c.SharedAcknowledged || c.AcknowledgedWriteScope != f.WriteScope || c.AcknowledgedConsumerCount != f.Backing.ConsumerCount || !c.AcknowledgedAffectedWeapons.Order(StringComparer.Ordinal).SequenceEqual(f.SharedWithWeapons.Order(StringComparer.Ordinal)))) throw new InvalidDataException("Shared setting requires acknowledgement of the current affected weapons and write scope.");
    }
    public IReadOnlyList<WeaponChangeIssue> Review(SdkMetadata sdk, IEnumerable<WeaponChange> changes)
    {
        var issues = new List<WeaponChangeIssue>();
        foreach (var c in changes.Where(c => c.Enabled))
            try { Validate(sdk, c); } catch (InvalidDataException e) { issues.Add(new(c.Id, e.Message)); }
        return issues;
    }
    public static PlayerWeaponCatalog Catalog(SdkMetadata sdk) => sdk.PlayerWeapons ?? throw new InvalidDataException("Select SDK 0.13.0 or newer for player-weapon authoring.");
    public static void ValidateValue(WeaponCapability f, JsonElement value)
    {
        if (f.Type == "boolean")
        { if (value.ValueKind is not (JsonValueKind.True or JsonValueKind.False)) throw new InvalidDataException("Expected a boolean."); return; }
        if (f.Type == "enum")
        { if (f.EnumValues == null || !f.EnumValues.Values.Any(v => JsonElement.DeepEquals(v, value))) throw new InvalidDataException("Choose a proven enum member."); return; }
        if (value.ValueKind != JsonValueKind.Number || !value.TryGetDouble(out var number) || !double.IsFinite(number) || (f.Type == "integer" && Math.Truncate(number) != number)) throw new InvalidDataException("Enter a finite value of the field's type.");
        // Representable storage bounds are not invented gameplay bounds.
        if ((f.Backing?.Storage == "f32" && !float.IsFinite((float)number)) || (f.Backing?.Storage == "u32" && (number < 0 || number > uint.MaxValue)) || (f.Backing?.Storage == "i32" && (number < int.MinValue || number > int.MaxValue)) || (f.Backing?.Storage == "u8" && (number < 0 || number > byte.MaxValue)) || (f.Min is double min && number < min) || (f.Max is double max && number > max)) throw new InvalidDataException("Value is outside the SDK's supported representation or bounds.");
    }
}
