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
            : catalog.Weapons.Keys.Where(w => sdk.SupportLinks?.ForWeapon(w) == null && !IsStandalone(sdk, w)).Order(StringComparer.Ordinal).ToArray();
    // 0.25.0+: equipment Runtime proves has no call-in at all (no_call_in, e.g. world pickups). Listed once as standalone equipment,
    // never as unresolved and never as a stratagem.
    public static IReadOnlyList<string> Standalone(SdkMetadata? sdk) =>
        sdk?.Advanced?.Support is not { } catalog || sdk.SupportLinks == null ? [] : catalog.Weapons.Keys.Where(w => IsStandalone(sdk, w)).Order(StringComparer.Ordinal).ToArray();
    public static bool IsStandalone(SdkMetadata? sdk, string weapon) => sdk?.SupportLinks != null
        && sdk.SupportAuthoring?.Weapons.SingleOrDefault(x => x.Name == weapon)?.LinkedStratagem?.IsNoCallIn == true;
    public static bool Linked(SdkMetadata? sdk, string weapon) => sdk?.SupportLinks?.ForWeapon(weapon) != null;
}

// One logical shared object a weapon's saved edits write. Runtime's acknowledgement stays per change (allow_shared on each
// request); the GUI presents and sets it once per shared object.
public sealed record SharedAckGroup(string Key, string Title, IReadOnlyList<string> AffectedWeapons, IReadOnlyList<string> FieldIds,
    IReadOnlyList<string> FieldNames, bool Acknowledged);
public static class WeaponAcknowledgements
{
    public static string ScopeKey(Metadata.WeaponCapability f) => string.Join("|", f.WriteScope, f.Backing?.Kind, f.Backing?.Settings ?? f.Backing?.Component,
        f.Backing?.RecordIndex, f.Backing?.Row, string.Join(",", f.SharedWithWeapons.Order(StringComparer.Ordinal)));
    public static string Title(Metadata.WeaponCapability f) => f.Domain switch
    {
        "projectile" => "Shared projectile definition", "damage" => "Shared damage definition", "explosion" => "Shared explosion definition",
        _ => "Shared " + (f.Backing?.Settings ?? f.Backing?.Component ?? "setting").Replace("ComponentData", "").Replace("Settings", " settings"),
    };
    // Saved (non-conflicting) shared edits of one weapon, grouped by the shared object they write.
    public static IReadOnlyList<SharedAckGroup> Groups(IEnumerable<Generation.WeaponChangeGroup> saved, string weapon) =>
        saved.Where(g => g.Weapon == weapon && g.Conflict == null && g.Field is { AffectsMultipleWeapons: true })
            .GroupBy(g => ScopeKey(g.Field!))
            .Select(s => new SharedAckGroup(s.Key, Title(s.First().Field!), s.First().Field!.SharedWithWeapons.Append(weapon).Distinct().Order(StringComparer.Ordinal).ToArray(),
                s.Select(g => g.FieldId).ToArray(), s.Select(g => g.Field!.DisplayName).ToArray(),
                s.All(g => Generation.WeaponChangeService.SharedAcknowledgementCurrent(g.Field!, g.Representative))))
            .OrderBy(g => g.Title, StringComparer.Ordinal).ToArray();
    // A new edit inherits the acknowledgement of its shared object when every saved edit of that object is acknowledged.
    public static bool Inherited(IEnumerable<Generation.WeaponChangeGroup> saved, string weapon, Metadata.WeaponCapability field) =>
        field.AffectsMultipleWeapons && Groups(saved, weapon).FirstOrDefault(g => g.Key == ScopeKey(field)) is { Acknowledged: true };
}

// Compact authoring summary of one weapon: what is modified and what still needs the user's acknowledgement.
public sealed record WeaponAuthoringSummary(int ModifiedFields, int SharedChanges, int AcknowledgementsRequired);
