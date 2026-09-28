using HD2RuntimeGUI.Core.Metadata;

namespace HD2RuntimeGUI.Core.Services;

// Readable groups for published semantic fields. Presentation only: groups never change targets, operations or guards.
public static class FieldGroups
{
    public static readonly string[] Order = ["Ammo", "Handling", "Firing", "Projectile", "Damage", "Explosion", "Arc", "Beam", "Status"];
    public static string Of(string semanticFieldId, string? domain = null)
    {
        var id = semanticFieldId.ToLowerInvariant(); var head = id.Split('.')[0];
        if (id is "weapon.capacity" || head is "magazine" or "rounds" or "reload" or "attachment") return "Ammo";
        if (head == "weapon" && (id.Contains("ergonomic") || id.Contains("recoil") || id.Contains("sway") || id.Contains("spread") || id.Contains("drift"))) return "Handling";
        if (head is "weapon" or "windup" or "charge" or "heat" or "heatsink") return "Firing";
        return head switch
        {
            "projectile" => "Projectile", "damage" => "Damage", "explosion" => "Explosion", "arc" => "Arc", "beam" => "Beam", "status" => "Status",
            _ => Title(domain ?? head),
        };
    }
    public static int Rank(string group) => Array.IndexOf(Order, group) is var i and >= 0 ? i : Order.Length;
    public static IEnumerable<IGrouping<string, T>> Group<T>(IEnumerable<T> fields, Func<T, string> semanticFieldId, Func<T, string?>? domain = null)
        => fields.GroupBy(f => Of(semanticFieldId(f), domain?.Invoke(f))).OrderBy(g => Rank(g.Key)).ThenBy(g => g.Key, StringComparer.Ordinal);
    private static string Title(string s) => System.Globalization.CultureInfo.InvariantCulture.TextInfo.ToTitleCase(s.Replace('_', ' '));
}

// Support equipment is presented through its Support stratagem. Only weapons without a published structural
// call-in link (or every weapon, on SDKs that publish no linkage) are listed as unlinked equipment. Names are never used to pair them.
public static class SupportEquipment
{
    public static IReadOnlyList<string> Unlinked(SdkMetadata? sdk) =>
        sdk?.Advanced?.Support is not { } catalog ? []
            : catalog.Weapons.Keys.Where(w => sdk.SupportLinks?.ForWeapon(w) == null).Order(StringComparer.Ordinal).ToArray();
    public static bool Linked(SdkMetadata? sdk, string weapon) => sdk?.SupportLinks?.ForWeapon(weapon) != null;
}
