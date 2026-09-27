using System.Text.Json;
using HD2RuntimeGUI.Core.Metadata;
using HD2RuntimeGUI.Core.Models;

namespace HD2RuntimeGUI.Core.Generation;

public static class WeaponScalar
{
    public static JsonElement Normalize(WeaponCapability field, JsonElement value)
    {
        WeaponChangeService.ValidateValue(field, value);
        if (field.Type == "integer")
        {
            if (!value.TryGetDecimal(out var integer) || decimal.Truncate(integer) != integer)
                throw new InvalidDataException("Expected an exact integer.");
            var normalized = JsonSerializer.SerializeToElement(decimal.Truncate(integer));
            if (!JsonElement.DeepEquals(value, normalized)) throw new InvalidDataException("Expected an exact integer.");
            return normalized;
        }
        // System.Text.Json writes Single using its round-trip representation, just
        // like the Lua generator. Keep the imported expected baseline untouched.
        return field.Type == "number" && field.Backing?.Storage == "f32"
            ? JsonSerializer.SerializeToElement((float)value.GetDouble()) : value.Clone();
    }

    public static bool Equal(WeaponCapability field, JsonElement a, JsonElement b)
    {
        if (field.Type == "boolean") return a.ValueKind is JsonValueKind.True or JsonValueKind.False && b.ValueKind == a.ValueKind;
        if (a.ValueKind != JsonValueKind.Number || b.ValueKind != JsonValueKind.Number)
            return JsonElement.DeepEquals(a, b);
        if (field.Type == "integer") return a.TryGetDecimal(out var x) && decimal.Truncate(x) == x
            && JsonElement.DeepEquals(a, JsonSerializer.SerializeToElement(x)) && JsonElement.DeepEquals(a, b);
        if (field.Type == "number" && field.Backing?.Storage == "f32")
        {
            var x = (float)a.GetDouble(); var y = (float)b.GetDouble();
            return float.IsFinite(x) && float.IsFinite(y) && x == y;
        }
        return JsonElement.DeepEquals(a, b);
    }

    public static bool IsNoOp(SdkMetadata sdk, WeaponChange change)
    {
        var field = sdk.PlayerWeapons?.FindCanonicalField(change.Weapon, change.SemanticFieldId);
        // Preserve missing or type-changed capabilities for the migration review.
        return field != null && field.Type == change.FieldType && field.CurrentDefault.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined)
            && Equal(field, field.CurrentDefault, change.DesiredValue);
    }
}
