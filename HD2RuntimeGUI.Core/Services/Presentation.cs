using HD2RuntimeGUI.Core.Localization;
using HD2RuntimeGUI.Core.Metadata;

namespace HD2RuntimeGUI.Core.Services;

// Readable groups for published semantic fields. Presentation only: groups never change targets, operations or guards.
public static class FieldGroups
{
    // 0.28.0 adds Functions (rate-of-fire modes, weapon-function bindings and programmable ammunition, written together) and the
    // armory Presentation labels.
    public static readonly string[] Order = ["Ammo", "Handling", "Firing", "Fire Mode", "Functions", "Projectile", "Damage", "Explosion", "Arc", "Beam", "Status", "Presentation"];
    public static string Of(string semanticFieldId, string? domain = null)
    {
        var id = semanticFieldId.ToLowerInvariant(); var head = id.Split('.')[0];
        if (id is "weapon.capacity" || head is "magazine" or "rounds" or "reload" or "attachment") return "Ammo";
        // 0.26.0: the third-person reticle is aiming presentation; fire modes are their own native model.
        if (id == "weapon.third_person_reticle") return "Handling";
        if (head == "fire_mode") return "Fire Mode";
        if (head is "fire_rate" or "weapon_function" or "function_ammo") return "Functions";
        if (head == "presentation") return "Presentation";
        // The attack's own projectile (a projectile host swap) sits with the projectile it fires.
        if (head == "attack") return "Projectile";
        if (head == "weapon" && (id.Contains("ergonomic") || id.Contains("recoil") || id.Contains("sway") || id.Contains("spread") || id.Contains("drift"))) return "Handling";
        if (head is "weapon" or "windup" or "charge" or "heat" or "heatsink") return "Firing";
        return head switch
        {
            "projectile" => "Projectile", "damage" => "Damage", "explosion" => "Explosion", "arc" => "Arc", "beam" => "Beam", "status" => "Status",
            _ => Title(domain ?? head),
        };
    }
    public static int Rank(string group) => Array.IndexOf(Order, group) is var i and >= 0 ? i : Order.Length;
    // Group names are logic keys (Order, Rank, grouping); this is their heading in the UI language. A group derived from an SDK domain
    // shows as published.
    public static string Label(string group) => group switch
    {
        "Ammo" => CoreText.Get("Presentation.Group.Ammo"), "Handling" => CoreText.Get("Presentation.Group.Handling"), "Firing" => CoreText.Get("Presentation.Group.Firing"),
        "Fire Mode" => CoreText.Get("Presentation.Group.FireMode"), "Projectile" => CoreText.Get("Presentation.Group.Projectile"), "Damage" => CoreText.Get("Presentation.Group.Damage"),
        "Explosion" => CoreText.Get("Presentation.Group.Explosion"), "Arc" => CoreText.Get("Presentation.Group.Arc"), "Beam" => CoreText.Get("Presentation.Group.Beam"),
        "Status" => CoreText.Get("Presentation.Group.Status"), "Functions" => CoreText.Get("Presentation.Group.Functions"),
        "Presentation" => CoreText.Get("Presentation.Group.Presentation"),
        _ => group,
    };
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
    // UI text in the current UI language; a settings/component name from the SDK is shown as published.
    public static string Title(Metadata.WeaponCapability f) => f.Domain switch
    {
        "projectile" => CoreText.Get("Presentation.SharedAck.Projectile"), "damage" => CoreText.Get("Presentation.SharedAck.Damage"),
        "explosion" => CoreText.Get("Presentation.SharedAck.Explosion"),
        _ => (f.Backing?.Settings ?? f.Backing?.Component) is { } name
            ? CoreText.Format("Presentation.SharedAck.Other", name.Replace("ComponentData", "").Replace("Settings", " settings"))
            : CoreText.Get("Presentation.SharedAck.Setting"),
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

// Compact authoring summary of one weapon: how many fields are modified and how many edits write shared objects.
public sealed record WeaponAuthoringSummary(int ModifiedFields, int SharedChanges);
