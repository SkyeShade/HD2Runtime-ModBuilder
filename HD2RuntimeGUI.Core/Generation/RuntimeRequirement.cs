using HD2RuntimeGUI.Core.Metadata;
using HD2RuntimeGUI.Core.Models;

namespace HD2RuntimeGUI.Core.Generation;

/// <summary>
/// The minimum HD2Runtime an export declares (hd2runtime.json requires.hd2runtime.min_version, the README and the wrapper's check): the bound
/// SDK's version, raised by the custom content target's (CustomContentBuild.RequiredRuntime) and by every field or support attack role the
/// project writes that only a newer Runtime has. The floors mirror HD2Runtime 0.30.2's scripts/validate_examples.py
/// (UNRELEASED_FIELDS, KIND_FIELD_FLOORS, SUPPORT_ROLES_UNRELEASED for 0.30.0; NEXT_RELEASE, WEAPON_ROOTS_0_30_2 and the player-weapon
/// explosion status pattern for 0.30.2), which refuses an example that writes them with a lower min_version, and 0.30.3's custom stratagem
/// uses range (an explicit 0 or -1, unlimited, which 0.30.2 refuses).
/// Older floors (0.24.0 boosters, 0.28.0 fields, ...) never raise anything: a project writes only what its bound SDK publishes.
/// </summary>
public static class RuntimeRequirement
{
    // The Runtime an export that writes a 0.30 field needs: the released 0.30.0, whose validator names it (a project still bound to the
    // 0.30.0-dev test SDK declares 0.30.0 too when it writes one).
    public const string Release030 = "0.30.0";
    // The first SDK that carries the 0.30 behaviour: the 0.30.0-dev test builds (a prerelease sorts below its release, so ">= First030" holds
    // for both). SDK checks use it, never the export floors.
    public const string First030 = "0.30.0-dev";
    // The release that added a feature of 0.30.2 (0.30.1 added none): writes on the seven weapons it resolved to their proven roots, status
    // slots on player-weapon explosions, the sentry / emplacement projectile hosts, and the two sound names its catalogue added (custom
    // stratagems: the Runtime 0.30.0 does not know them). What 0.30.0 released keeps Release030.
    public const string Release0302 = "0.30.2";
    public static readonly IReadOnlySet<string> WeaponRoots0302 = new HashSet<string>(StringComparer.Ordinal)
    { "GP-31 Grenade Pistol", "P-72 Crisper", "LAS-5 Scythe", "LAS-7 Dagger", "CQC-42 Machete", "CQC-73 Entrenchment Tool", "SMG-37 Defender" };
    public static readonly IReadOnlySet<string> Sounds0302 = new HashSet<string>(StringComparer.Ordinal) { "seaf/3", "other/wep_grenadier_rifle/1" };
    // The release that added a feature of 0.30.3: a custom stratagem's uses may be 0 or -1, both unlimited (0.30.2 refuses uses below 1).
    // ModBuilder writes no uses for unlimited (every 0.30 Runtime reads that as unlimited) and keeps an explicit 0 or -1 only from an imported
    // Runtime project, so it round-trips; such a value raises the export to 0.30.3.
    public const string Release0303 = "0.30.3";
    /// <summary>Whether a custom stratagem's uses is an explicit unlimited (0 or -1, HD2Runtime 0.30.3) rather than none.</summary>
    public static bool ExplicitUnlimitedUses(int? uses) => uses is 0 or -1;
    // The 23 statuses 0.30.2 made attachable (schemas/status_attachment_policy.json: tiers enemy_slot and other_system). Runtime 0.30.0 accepts
    // none of them in a status slot, so a write of one needs 0.30.2 (the validator has no rule of its own; Runtime refuses them at load).
    public static readonly IReadOnlySet<string> ExtendedStatuses0302 = new HashSet<string>(StringComparer.Ordinal)
    {
        "confusion", "lava", "acid_splash", "choked", "stun_massive", "inverted_aim_assist", "tremor", "tornado_stun",
        "acid_stream", "thermite", "cyborg_fire", "burning_light", "radiation_light", "radiation_heavy", "electric", "bleed", "poison", "gloom",
        "slowed", "rooted", "blind", "deaf", "stun_illuminate",
    };
    private static bool ExtendedStatus(string fieldType, System.Text.Json.JsonElement value) => fieldType == WeaponCapability.StatusReference
        && value.ValueKind == System.Text.Json.JsonValueKind.String && ExtendedStatuses0302.Contains(value.GetString()!);
    // UNRELEASED_FIELDS, normalised as the validator normalises them (projectile/damage/explosion role and phase qualifiers removed).
    public static readonly IReadOnlySet<string> Fields030 = new HashSet<string>(StringComparer.Ordinal)
    {
        "stratagem.calldown_code", "stratagem.presentation.name", "stratagem.presentation.name_cased", "stratagem.presentation.description",
        "stratagem.presentation.icon", "gore.whole_body_gib_damage",
        "charge.speed_multiplier_min", "charge.speed_multiplier_overcharge", "charge.damage_multiplier_min", "charge.damage_multiplier_overcharge",
        "charge.penetration_multiplier_min", "charge.penetration_multiplier_overcharge", "charge.arc_distance_multiplier_min",
        "charge.arc_distance_multiplier_overcharge", "charge.auto_fire_at_full", "charge.explode_at_overcharge", "charge.overcharge_explosion",
        "charge.overcharge_limit_seconds", "charge.burst_shots", "charge.burst_interval_seconds",
        "jump.launch_duration", "jump.launch_forward_ratio", "jump.sustain_thrust", "jump.sustain_duration", "jump.sustain_forward_ratio",
        "jump.sustain_start_delay", "jump.sustain_start_speed", "jump.sustain_cutoff_speed", "jump.air_control_acceleration", "jump.air_control_max_speed",
        "jump.takeoff_forward_speed", "jump.takeoff_speed", "jump.takeoff_speed_alternate_stance",
        "hover.max_horizontal_speed", "hover.max_vertical_speed", "hover.vertical_acceleration_low_speed", "hover.vertical_acceleration_high_speed",
        "hover.vertical_speed_range_end", "hover.fuel_rate_low_speed", "hover.fuel_rate_high_speed",
        "rotation.turn_speed", "rotation.acceleration", "rotation.deceleration", "vehicle.steering_response_speed",
        "turret.pitch_yaw_coupling", "targeting.side_range", "targeting.rear_range",
        "orbital.salvos", "orbital.shells_per_salvo", "orbital.shell_interval", "orbital.shell_interval_random", "orbital.salvo_interval",
        "orbital.salvo_interval_random", "orbital.scatter", "orbital.salvo_scatter", "stratagem.call_in_time",
        "eagle.airstrike_pattern", "eagle.drop_interval", "eagle.fire_duration", "eagle.attack_sweep_length", "eagle.target_radius", "eagle.attack_angle",
        "attachment.modifier.sway", "attachment.modifier.recoil_horizontal", "attachment.modifier.recoil_vertical", "attachment.modifier.climb_horizontal",
        "attachment.modifier.climb_vertical", "attachment.modifier.spread_horizontal", "attachment.modifier.spread_vertical",
    };
    // KIND_FIELD_FLOORS: ids that existed for other resources and are new on this one (turret motion on vehicle mounts, the sentry weapon
    // handling fields on a stratagem's deployed entity).
    public static readonly IReadOnlyDictionary<(string Kind, string Field), string> KindFieldFloors = new Dictionary<(string, string), string>
    {
        [("stratagem", "stratagem.max_uses")] = "0.26.0",
        [("vehicle_weapon", "turret.yaw_speed")] = Release030, [("vehicle_weapon", "turret.pitch_speed")] = Release030,
        [("vehicle_weapon", "turret.pitch_min")] = Release030, [("vehicle_weapon", "turret.pitch_max")] = Release030,
        [("vehicle_weapon", "turret.yaw_min")] = Release030, [("vehicle_weapon", "turret.yaw_max")] = Release030,
        [("stratagem", "weapon.horizontal_spread")] = Release030, [("stratagem", "weapon.vertical_spread")] = Release030,
        [("stratagem", "weapon.recoil_drift_horizontal")] = Release030, [("stratagem", "weapon.recoil_drift_vertical")] = Release030,
        [("stratagem", "weapon.recoil_climb_horizontal")] = Release030, [("stratagem", "weapon.recoil_climb_vertical")] = Release030,
        [("stratagem", "windup.wind_up_seconds")] = Release030, [("stratagem", "windup.wind_down_seconds")] = Release030,
        [("stratagem", "beam.fire_rate")] = Release030,
    };
    // SUPPORT_ROLES_UNRELEASED: the PLAS-45 Epoch's full-charge shot and its explosion, the Epoch's and the RS-422 Railgun's overcharge explosions.
    public static readonly IReadOnlySet<string> SupportRoles030 = AuthoredTypes.SupportRoles030;

    // The validator's normalisation: projectile.primary.x, damage.impact.x, explosion.expiry.x, ... -> projectile.x, damage.x, explosion.x.
    public static string Generic(string field) => System.Text.RegularExpressions.Regex.Replace(field, @"^(projectile|damage|explosion)\.(primary|alternate|impact|expiry)\.", "$1.");

    /// <summary>Every (minimum Runtime, reason) the project's enabled edits need beyond nothing; the export takes the highest. With the bound
    /// SDK, a 0.30.0 projectile donor the project fires (an attack output swap or a programmable-ammunition projectile) raises it too: the
    /// validator's usesMoreDonors ("more projectile donors").</summary>
    public static IEnumerable<(string Floor, string Reason)> Floors(ModProject project, SdkMetadata? sdk = null)
    {
        IEnumerable<(string, string)> Field(string kind, string field)
        {
            if (Fields030.Contains(Generic(field))) yield return (Release030, kind + " " + field);
            if (KindFieldFloors.TryGetValue((kind, field), out var floor)) yield return (floor, kind + " " + field);
        }
        foreach (var c in project.StratagemChanges.Where(c => c.Enabled)) foreach (var f in Field("stratagem", c.SemanticFieldId)) yield return f;
        foreach (var c in project.SupportChanges.Where(c => c.Enabled))
        {
            foreach (var f in Field("support_weapon", c.SemanticFieldId)) yield return f;
            if (c.AttackRole is { } role && SupportRoles030.Contains(role)) yield return (Release030, "support attack role " + role);
            if (ExtendedStatus(c.FieldType, c.DesiredValue)) yield return (Release0302, "extended status " + c.DesiredValue.GetString());
        }
        foreach (var c in project.EntityChanges.Where(c => c.Enabled))
        {
            foreach (var f in Field(c.Resource, c.SemanticFieldId)) yield return f;
            if (ExtendedStatus(c.FieldType, c.DesiredValue)) yield return (Release0302, "extended status " + c.DesiredValue.GetString());
        }
        foreach (var c in project.WeaponChanges.Where(c => c.Enabled))
        {
            foreach (var f in Field("player_weapon", c.SemanticFieldId)) yield return f;
            if (WeaponRoots0302.Contains(c.Weapon)) yield return (Release0302, c.Weapon + " (blocked as a duplicate identity before 0.30.2)");
            if (AuthoredTypes.IsPlayerExplosionStatus(c.SemanticFieldId)) yield return (Release0302, "player weapon explosion status " + c.SemanticFieldId);
            if (ExtendedStatus(c.FieldType, c.DesiredValue)) yield return (Release0302, "extended status " + c.DesiredValue.GetString());
        }
        // Object scalars (a projectile's direct hit, an explosion row: Player Weapons with the composition graphs) take the status floors.
        foreach (var c in project.CompositionChanges.Where(c => c.Enabled && c.Scalar != null))
        {
            if (AuthoredTypes.IsPlayerExplosionStatus(c.Scalar!.SemanticFieldId)) yield return (Release0302, "player weapon explosion status " + c.Scalar.SemanticFieldId);
            if (ExtendedStatus(c.Scalar.FieldType, c.Scalar.DesiredValue)) yield return (Release0302, "extended status " + c.Scalar.DesiredValue.GetString());
        }
        foreach (var c in (project.AttackOutputChanges ?? []).Where(c => c.Enabled))
        {
            if (c.IsPlayer && WeaponRoots0302.Contains(c.Weapon)) yield return (Release0302, c.Weapon + " (blocked as a duplicate identity before 0.30.2)");
            if (c.Kind == AttackOutputChange.VehicleHost && sdk?.Entities?.VehicleWeapons?.StratagemHosts.ContainsKey(c.Weapon) == true)
                yield return (Release0302, "sentry / emplacement projectile host " + c.Weapon);
        }
        if (project.CustomContent is { } content)
        {
            foreach (var a in content.Assignments.Where(a => a.Enabled && a.HostKind == AttackOutputChange.PlayerHost && WeaponRoots0302.Contains(a.Weapon)))
                yield return (Release0302, a.Weapon + " (blocked as a duplicate identity before 0.30.2)");
            foreach (var sound in Sounds(System.Text.Json.JsonSerializer.SerializeToNode(content, Storage.JsonStorage.Options)).Where(Sounds0302.Contains).Distinct())
                yield return (Release0302, "custom stratagem sound " + sound);
            foreach (var s in content.Stratagems.Where(s => ExplicitUnlimitedUses(s.Uses)))
                yield return (Release0303, "custom stratagem " + s.Id + " uses " + s.Uses + " (unlimited)");
        }
        if (sdk?.AttackOutputs is not { } outputs) yield break;
        bool Donor(string? output) => output != null && outputs.Output(output)?.IsDonor == true;
        string? Token(System.Text.Json.JsonElement value) => value.ValueKind == System.Text.Json.JsonValueKind.String ? value.GetString() : null;
        foreach (var c in (project.AttackOutputChanges ?? []).Where(c => c.Enabled && Donor(c.Output))) yield return (Release030, "projectile donor " + c.Output);
        foreach (var c in project.WeaponChanges.Where(c => c.Enabled && c.SemanticFieldId == FunctionProjectile.Field && Donor(Token(c.DesiredValue))))
            yield return (Release030, "projectile donor " + Token(c.DesiredValue));
        foreach (var c in project.SupportChanges.Where(c => c.Enabled && c.SemanticFieldId == FunctionProjectile.Field && Donor(Token(c.DesiredValue))))
            yield return (Release030, "projectile donor " + Token(c.DesiredValue));
    }

    // Every string a custom content section stores under a "sound" key (a Pelican gun's, a weapon's).
    private static IEnumerable<string> Sounds(System.Text.Json.Nodes.JsonNode? node) => node switch
    {
        System.Text.Json.Nodes.JsonObject o => o.SelectMany(p => p.Key.Equals("sound", StringComparison.OrdinalIgnoreCase)
            && p.Value is System.Text.Json.Nodes.JsonValue v && v.TryGetValue<string>(out var s) ? [s] : Sounds(p.Value)),
        System.Text.Json.Nodes.JsonArray a => a.SelectMany(Sounds),
        _ => [],
    };

    /// <summary>The bound SDK's version (or the custom content target's), raised to the highest floor of what the project writes.</summary>
    public static string Minimum(ModProject project, SdkMetadata sdk)
    {
        var minimum = CustomContent.CustomContentBuild.RequiredRuntime(project, sdk);
        foreach (var (floor, _) in Floors(project, sdk))
            if (SemVersion.Parse(floor).CompareTo(SemVersion.Parse(minimum)) > 0) minimum = floor;
        return minimum;
    }
}
