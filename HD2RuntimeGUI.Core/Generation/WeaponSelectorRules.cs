using System.Text.Json;
using HD2RuntimeGUI.Core.Localization;
using HD2RuntimeGUI.Core.Metadata;
using HD2RuntimeGUI.Core.Models;

namespace HD2RuntimeGUI.Core.Generation;

// Runtime's rules for one weapon's 1.4.0 composition edits, checked before saving (the editors resolve them where the edit happens) and
// again before building:
// - the weapon_selector group (fire_rate.modes, weapon_function.left/right, function_ammo.projectile) is one transaction, and Runtime's
//   SELECTOR_REQUIRED pairing holds: rates beyond the default need a bound rate-of-fire selector, a function projectile needs a bound
//   ProgrammableAmmo selector, and binding either needs what it selects in the same transaction (docs/fire-rate-modes.md, weapon-feeds.md);
// - fields over the same native bytes are never combined: fire_rate.modes with weapon.fire_rate (the Y slot), presentation.traits with
//   presentation.armor_penetration (the same five trait slots).
public static class WeaponSelectorRules
{
    public static readonly string[] SelectorFields = [FireRateModes.Field, WeaponFunctions.Field("left"), WeaponFunctions.Field("right"), FunctionProjectile.Field];
    public static bool IsSelectorField(string semanticFieldId) => SelectorFields.Contains(semanticFieldId);

    // The pairing problem of one weapon's desired selector state (rates / projectile: the saved desired values, null when not edited;
    // bindings: the edited inputs and the function each binds), or null.
    public static string? Problem(string weapon, IReadOnlyList<float>? rates, bool ratesBound, string? projectile, bool projectileBound, IReadOnlyDictionary<string, string> bindings)
    {
        var bound = bindings.Where(b => b.Value != WeaponFunctions.None).ToArray();
        if (bound.GroupBy(b => b.Value).Any(g => g.Count() > 1)) return CoreText.Format("Messages.Build.Selector.DuplicateBinding", weapon);
        var filled = rates?.Count(r => r != 0) ?? 0;
        var rateBinding = bound.Any(b => b.Value == WeaponFunctions.RateOfFire);
        var ammoBinding = bound.Any(b => b.Value == WeaponFunctions.ProgrammableAmmo);
        if (filled > 1 && !ratesBound && !rateBinding) return CoreText.Format("Messages.Build.Selector.RatesNeedBinding", weapon);
        if (rateBinding && filled < 2) return CoreText.Format("Messages.Build.Selector.BindingNeedsRates", weapon);
        if (projectile != null && FunctionProjectile.IsOutput(projectile) && !projectileBound && !ammoBinding) return CoreText.Format("Messages.Build.Selector.ProjectileNeedsBinding", weapon);
        if (ammoBinding && (projectile == null || projectile == FunctionProjectile.None)) return CoreText.Format("Messages.Build.Selector.BindingNeedsProjectile", weapon);
        return null;
    }
    private static IReadOnlyList<float>? Rates(JsonElement? value)
    { try { return value is { } v ? FireRateModes.Rates(v) : null; } catch (InvalidDataException) { return null; } }
    private static string? Token(JsonElement? value)
    { try { return value is { } v ? FunctionProjectile.Token(v) : null; } catch (InvalidDataException) { return null; } }
    private static string? Name(JsonElement value) => value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    // Every issue of the saved player-weapon edits, keyed by the change that shows it.
    public static IReadOnlyList<(Guid Id, string Message)> PlayerIssues(SdkMetadata sdk, IReadOnlyList<WeaponChange> changes)
    {
        var result = new List<(Guid, string)>();
        if (sdk.PlayerWeapons is not { } catalog) return result;
        foreach (var weapon in changes.Where(c => c.Enabled).GroupBy(c => c.Weapon))
        {
            var w = catalog.Find(weapon.Key); if (w == null) continue;
            WeaponChange? Edit(string id) => weapon.FirstOrDefault(c => (catalog.FindCanonicalField(c.Weapon, c.SemanticFieldId)?.SemanticFieldId ?? c.SemanticFieldId) == id);
            WeaponCapability? Field(string id) => w.Fields.FirstOrDefault(f => f.SemanticFieldId == id);
            var rates = Edit(FireRateModes.Field); var ammo = Edit(FunctionProjectile.Field);
            var bindings = WeaponFunctions.Inputs.Select(i => (Input: i, Change: Edit(WeaponFunctions.Field(i)))).Where(x => x.Change != null && Name(x.Change.DesiredValue) != null)
                .ToDictionary(x => x.Input, x => Name(x.Change!.DesiredValue)!);
            var selector = SelectorFields.Select(Edit).Where(c => c != null).Select(c => c!).ToArray();
            if (selector.Length > 0)
            {
                if (Problem(weapon.Key, Rates(rates?.DesiredValue), Field(FireRateModes.Field)?.SelectorBound == true, Token(ammo?.DesiredValue),
                    Field(FunctionProjectile.Field)?.SelectorBound == true, bindings) is { } problem) result.Add((selector[0].Id, problem));
                if (selector.Select(c => c.EnsureEnabled).Distinct().Count() > 1) result.Add((selector[0].Id, CoreText.Format("Messages.Build.Selector.MixedPersistence", weapon.Key)));
            }
            if (rates != null && Edit(FireRateModes.LegacyField) is { } legacy) result.Add((legacy.Id, CoreText.Format("Messages.Build.FireRate.LegacyConflict", weapon.Key)));
            if (Edit(TraitSets.Field) is { } traits && Edit(TraitSets.PenetrationField) != null) result.Add((traits.Id, CoreText.Format("Messages.Build.Traits.Conflict", weapon.Key)));
        }
        return result;
    }

    // Every issue of the saved support-weapon edits (weapon-level fields of one support weapon), keyed by the change that shows it.
    public static IReadOnlyList<(Guid Id, string Message)> SupportIssues(SdkMetadata sdk, IReadOnlyList<SupportChange> changes)
    {
        var result = new List<(Guid, string)>();
        if (sdk.SupportAuthoring is not { } catalog) return result;
        var fields = catalog.FieldInstances.Where(f => f.Target.Path == "weapon").ToLookup(f => f.InstanceKey);
        foreach (var weapon in changes.Where(c => c.Enabled && fields.Contains(c.InstanceKey)).GroupBy(c => c.Weapon))
        {
            SupportChange? Edit(string id) => weapon.FirstOrDefault(c => c.SemanticFieldId == id);
            SupportField? Field(string id) => catalog.FieldInstances.FirstOrDefault(f => f.SupportWeapon == weapon.Key && f.Target.Path == "weapon" && f.SemanticFieldId == id);
            var rates = Edit(FireRateModes.Field); var ammo = Edit(FunctionProjectile.Field);
            var bindings = WeaponFunctions.Inputs.Select(i => (Input: i, Change: Edit(WeaponFunctions.Field(i)))).Where(x => x.Change != null && Name(x.Change.DesiredValue) != null)
                .ToDictionary(x => x.Input, x => Name(x.Change!.DesiredValue)!);
            var selector = SelectorFields.Select(Edit).Where(c => c != null).Select(c => c!).ToArray();
            if (selector.Length > 0)
            {
                if (Problem(weapon.Key, Rates(rates?.DesiredValue), Field(FireRateModes.Field)?.FireRate?.SelectorBound == true, Token(ammo?.DesiredValue),
                    Field(FunctionProjectile.Field)?.FunctionAmmo?.SelectorBound == true, bindings) is { } problem) result.Add((selector[0].Id, problem));
                if (selector.Select(c => c.EnsureEnabled).Distinct().Count() > 1) result.Add((selector[0].Id, CoreText.Format("Messages.Build.Selector.MixedPersistence", weapon.Key)));
            }
            if (rates != null && Edit(FireRateModes.LegacyField) is { } legacy) result.Add((legacy.Id, CoreText.Format("Messages.Build.FireRate.LegacyConflict", weapon.Key)));
            if (Edit(TraitSets.Field) is { } traits && Edit(TraitSets.PenetrationField) != null) result.Add((traits.Id, CoreText.Format("Messages.Build.Traits.Conflict", weapon.Key)));
        }
        return result;
    }
}
