using HD2RuntimeGUI.Core.Localization;

namespace HD2RuntimeGUI.Core.Metadata;

/// <summary>One published projectile source (a player, support or mounted attack) with what ModBuilder can do with it: the resolved host when
/// Runtime can write the projectile it fires, otherwise Runtime's reason. Own is the row the attack fires today (its baseline); Carrier is the
/// backpack whose Guard Dog drone carries a mounted weapon (hd2.backpack(name):drone():weapon()).</summary>
public sealed record ProjectileHostEntry(AttackProjectileSource Source, AttackOutputHost? Host, string Reason, AttackOutput? Own, string? Carrier)
{
    public bool Writable => Host != null;
}

// 0.28.0 unified projectile hosts (attack-outputs.md: support hosts, mounted hosts, one donor pool). A support or mounted weapon is a host by the
// same rule as a player weapon: Runtime's active-source table says the attack's own ProjectileWeapon +0 is what it fires (ACTIVE_DIRECT,
// directly writable), and the weapon's catalog publishes the matching attack.<role>.projectile field. Its opt-ins come from that field
// (allow_unverified_effect with its reason; allow_shared for one weapon entity in several mounts), and so do the exact live-proven donors.
public static class ProjectileHosts
{
    public static readonly string[] Kinds = [AttackProjectileSource.PlayerKind, AttackProjectileSource.SupportKind, AttackProjectileSource.VehicleKind];

    public static AttackOutputHost? Host(SdkMetadata sdk, string kind, string weapon, string role, out string reason)
    {
        reason = CoreText.Get("Messages.Output.NoSources");
        if (sdk.AttackOutputs is not { } catalog) return null;
        if (kind == AttackProjectileSource.PlayerKind)
            return sdk.PlayerWeapons is { } weapons ? catalog.Host(weapons, weapon, role, out reason) : null;
        var source = catalog.Source(kind, weapon, role);
        reason = source?.Reason ?? CoreText.Get("Messages.Output.NoActiveSource");
        if (source is not { DirectWritable: true, Status: AttackProjectileSource.ActiveDirect }) return null;
        if (kind == AttackProjectileSource.SupportKind)
        {
            var identity = sdk.SupportAuthoring?.Weapons.FirstOrDefault(w => w.Name == weapon);
            if (identity == null || !SupportAuthoringWeapon.Resolved(identity.IdentityStatus))
            { reason = CoreText.Format("Messages.Output.AmbiguousRoot", weapon, identity?.IdentityStatus ?? "UNKNOWN"); return null; }
            var field = SupportField(sdk, weapon, role);
            if (field?.ProjectileReference is not { } r) { reason = CoreText.Format("Messages.Output.NoHostField", weapon); return null; }
            string[] acks = [.. new[] { field.Operation.AllowSharedRequired ? "allow_shared" : null, field.Operation.Acknowledgement }.OfType<string>()];
            return new(weapon, role, AttackOutputHost.Component, r.CompatibilityClass, acks, null, catalog.HostModel.ComponentHosts.Contains(weapon),
                kind, field.LiveEvidence is { Status: "live_proven", Values: { } values } ? values : null, field.Operation.AcknowledgementReason, source.SharedEntity);
        }
        if (kind == AttackProjectileSource.VehicleKind)
        {
            var field = VehicleField(sdk, weapon, role);
            if (field?.ProjectileReference is not { } r) { reason = CoreText.Format("Messages.Output.NoHostField", weapon); return null; }
            string[] acks = [.. new[] { field.AllowSharedRequired ? "allow_shared" : null, field.Acknowledgement }.OfType<string>()];
            return new(weapon, role, AttackOutputHost.Component, r.CompatibilityClass, acks, null, catalog.HostModel.ComponentHosts.Contains(weapon),
                kind, field.LiveProvenValues, null, source.SharedEntity);
        }
        return null;
    }
    // The support weapon's own attack projectile field (attack.projectile on hd2.support_weapon(name):attack(role)).
    public static SupportField? SupportField(SdkMetadata sdk, string weapon, string role) =>
        sdk.SupportAuthoring?.FieldInstances.FirstOrDefault(f => f.IsProjectileReference && f.SupportWeapon == weapon && f.Target.AttackRole == role);
    // The mount's attack projectile field (attack.<role>.projectile on hd2.vehicle(v):weapon(mount):attack(role)).
    public static VehicleWeaponFieldInstance? VehicleField(SdkMetadata sdk, string weapon, string role) =>
        sdk.Entities?.VehicleWeapons?.Published.Values.FirstOrDefault(f => f.Type == "projectile_reference" && f.Weapon == weapon && f.Target.Attack == role);
    // The backpack whose drone carries this mounted weapon (Guard Dog), or null for a vehicle mount.
    public static string? Carrier(SdkMetadata sdk, string weapon)
    {
        if (!weapon.Contains(" / ", StringComparison.Ordinal)) return null;
        var (vehicle, _) = VehicleWeaponReader.Split(weapon);
        return sdk.Entities?.VehicleWeapons?.Find(vehicle)?.Carrier is { Kind: VehicleWeaponCarrier.BackpackDrone } c ? c.Backpack : null;
    }
    // Every projectile source Runtime publishes, player first, then support and mounted weapons (read-only ones keep their reason).
    public static IReadOnlyList<ProjectileHostEntry> All(SdkMetadata sdk)
    {
        if (sdk.AttackOutputs is not { } catalog) return [];
        return catalog.ProjectileSources.OrderBy(s => Array.IndexOf(Kinds, s.HostKind)).ThenBy(s => s.Weapon, StringComparer.OrdinalIgnoreCase).ThenBy(s => s.Attack, StringComparer.Ordinal)
            .Select(s => { var host = Host(sdk, s.HostKind, s.Weapon, s.Attack, out var reason); return new ProjectileHostEntry(s, host, host == null ? reason : s.Reason, Own(catalog, s), Carrier(sdk, s.Weapon)); })
            .ToArray();
    }
    public static ProjectileHostEntry? Entry(SdkMetadata sdk, string kind, string weapon, string role) =>
        sdk.AttackOutputs?.Source(kind, weapon, role) is { } s ? new(s, Host(sdk, kind, weapon, role, out var reason), reason, Own(sdk.AttackOutputs, s), Carrier(sdk, weapon)) : null;
    // The published sources of one support weapon, one vehicle or one drone carrier (every mount "<owner> / <mount>").
    public static IReadOnlyList<AttackProjectileSource> SourcesOf(SdkMetadata sdk, string kind, string owner) => sdk.AttackOutputs?.ProjectileSources
        .Where(s => s.HostKind == kind && (kind == AttackProjectileSource.VehicleKind ? s.Weapon.StartsWith(owner + " / ", StringComparison.Ordinal) : s.Weapon == owner)).ToArray() ?? [];
    // The row a host's own attack fires: a player attack's output (a component host fires its own row; an ammunition host its ammunition row,
    // which the catalog publishes as the weapon's output), a support or mounted weapon's output.
    private static AttackOutput? Own(AttackOutputCatalog catalog, AttackProjectileSource s) => catalog.Outputs.FirstOrDefault(o => o.Owner.Name == s.Weapon && o.Family == "projectile");
}
