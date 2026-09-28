using System.Text.Json;
using HD2RuntimeGUI.Core.Metadata;
using HD2RuntimeGUI.Core.Models;

namespace HD2RuntimeGUI.Components;

// Display comparisons use the same scalar spelling as the editor (including f32).
// This does not alter SDK metadata, saved baselines, or generation rules.
internal static class FieldPresentation
{
    public static bool Differs(WeaponCapability? field, JsonElement baseline, JsonElement current) =>
        field != null ? !HD2RuntimeGUI.Core.Generation.WeaponScalar.Equal(field, baseline, current) : !JsonElement.DeepEquals(baseline, current);
    public static bool Modified(WeaponCapability? field, WeaponChange? change) => change != null &&
        Differs(field, field?.CurrentDefault ?? change.ExpectedValue, change.DesiredValue);
    public static JsonElement? Parse(string text)
    {
        try { using var doc = JsonDocument.Parse(text); return doc.RootElement.ValueKind is JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False or JsonValueKind.String ? doc.RootElement.Clone() : null; }
        catch (JsonException) { return null; }
    }
    public static string Value(WeaponCapability? field, JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.True => "On", JsonValueKind.False => "Off",
        _ when field?.Type == "enum" && field.EnumValues?.FirstOrDefault(p => JsonElement.DeepEquals(p.Value, value)).Key is { } name => EnumLabel(name),
        _ => field?.Format(value) ?? value.ToString()
    };
    public static string EnumLabel(string name) => System.Globalization.CultureInfo.InvariantCulture.TextInfo.ToTitleCase(name.Replace('_', ' '));
    public static string Unit(WeaponCapability? field) => field?.Unit == "rpm" ? "RPM" : field?.Unit?.Replace('_', ' ') ?? "";
    public static string? Explanation(WeaponCapability field) => field.SemanticFieldId == "weapon.suppressed" && field.Backing?.Component == "WeaponDataComponentData"
        ? "Runtime weapon-data flag; it does not necessarily indicate a visible or selectable suppressor attachment." : null;
    // Authoring-first grouping: what a modder edits first comes first. Everything not writable (including derived values and
    // placeholder weapon-level magazine fields) lives in the one collapsed Advanced / Read-only section.
    public const string Advanced = "Advanced / Read-only";
    public static string Section(WeaponCapability f)
    {
        // 0.26.0: the reticle and fire modes stay in their own sections even when read-only, so their blocker is visible there.
        if (f.SemanticFieldId == HD2RuntimeGUI.Core.Metadata.FireModes.ReticleField) return "Handling";
        if (f.Domain == "fire_mode") return FireMode;
        if (!f.Editable || f.DerivedReadOnly) return Advanced;
        if (f.Domain == "heat") return "Heat";
        if (f.Domain == "heatsink") return "Heatsinks";
        if (f.SemanticFieldId == "weapon.default_fire_mode") return "Weapon";
        if (f.Domain is "magazine" or "rounds") return "Ammo / Magazine";
        if (f.Domain == "weapon") return f.SemanticFieldId.Contains("capacity") ? "Ammo / Magazine" : f.SemanticFieldId.Contains("recoil") || f.SemanticFieldId.Contains("spread") || f.SemanticFieldId.Contains("sway") || f.SemanticFieldId.Contains("ergonomics") ? "Handling" : "Weapon";
        if (f.Domain == "damage") return f.SemanticFieldId.Contains("status_") ? "Status Effects" : "Damage";
        return System.Globalization.CultureInfo.InvariantCulture.TextInfo.ToTitleCase(f.Domain.Replace('_', ' '));
    }
    public const string FireMode = "Fire Mode";
    public static int SectionOrder(string name) => name switch { "Weapon" => 0, FireMode => 1, "Ammo / Magazine" => 2, "Handling" => 3, "Heat" => 4, "Heatsinks" => 5, "Projectile" => 6, "Damage" => 7, "Status Effects" => 8, "Explosion" => 9, Advanced => 99, _ => 10 };
}
