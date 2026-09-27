using System.Text.Json;
using HD2RuntimeGUI.Core.Metadata;
using HD2RuntimeGUI.Core.Models;

namespace HD2RuntimeGUI.Components;

// Display comparisons use the same scalar spelling as the editor (including f32).
// This does not alter SDK metadata, saved baselines, or generation rules.
internal static class FieldPresentation
{
    public static bool Differs(WeaponCapability? field, JsonElement baseline, JsonElement current) =>
        field != null ? field.Format(baseline) != field.Format(current) : !JsonElement.DeepEquals(baseline, current);
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
        _ => field?.Format(value) ?? value.ToString()
    };
    public static string Unit(WeaponCapability? field) => field?.Unit == "rpm" ? "RPM" : field?.Unit?.Replace('_', ' ') ?? "";
    public static string? Explanation(WeaponCapability field) => field.SemanticFieldId == "weapon.suppressed" && field.Backing?.Component == "WeaponDataComponentData"
        ? "Runtime weapon-data flag; it does not necessarily indicate a visible or selectable suppressor attachment." : null;
    public static string Section(WeaponCapability f)
    {
        if (!f.Editable) return "Advanced / Read-only";
        if (f.Domain == "weapon") return f.SemanticFieldId.Contains("capacity") ? "Ammo / Feed" : f.SemanticFieldId.Contains("recoil") || f.SemanticFieldId.Contains("spread") || f.SemanticFieldId.Contains("sway") || f.SemanticFieldId.Contains("ergonomics") ? "Handling" : "Weapon";
        if (f.Domain == "damage") return f.SemanticFieldId.Contains(".ap_") ? "Penetration" : f.SemanticFieldId.Contains("status_") ? "Special Effects" : "Damage";
        return System.Globalization.CultureInfo.InvariantCulture.TextInfo.ToTitleCase(f.Domain.Replace('_', ' '));
    }
    public static int SectionOrder(string name) => name switch { "Weapon" => 0, "Handling" => 1, "Ammo / Feed" => 2, "Projectile" => 3, "Damage" => 4, "Penetration" => 5, "Special Effects" => 6, "Advanced / Read-only" => 99, _ => 7 };
}
