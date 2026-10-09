namespace HD2RuntimeGUI.Core.Metadata;

// Value types this build authors (an editor, a saved change and a Lua form), per catalog. Runtime may publish other writable types;
// they stay visible with Runtime's baseline but read-only, and the capability audit reports each one it finds.
public static class AuthoredTypes
{
    public const string NotAuthoredReason = "Published by this Runtime SDK but not authored by this ModBuilder build yet.";
    // 1.4.0 (Runtime 0.28.0) weapon composition: rate-of-fire slots, weapon-function bindings, the programmable-ammunition projectile,
    // armory presentation labels and typed status references.
    public static readonly IReadOnlySet<string> PlayerWeapon = new HashSet<string>(StringComparer.Ordinal)
    {
        "number", "integer", "boolean", "enum", "projectile_reference", "explosion_reference", WeaponCapability.FireModeSet,
        WeaponCapability.FireRateSet, WeaponCapability.WeaponFunction, WeaponCapability.FunctionProjectileReference,
        WeaponCapability.TraitSet, WeaponCapability.ArmorPenetrationLabel, WeaponCapability.StatusReference,
    };
    public static readonly IReadOnlySet<string> Support = new HashSet<string>(StringComparer.Ordinal)
    {
        "scalar/number", "scalar/integer", "scalar/boolean", "scalar/" + WeaponCapability.FireModeSet,
        "scalar/" + WeaponCapability.FireRateSet, "scalar/" + WeaponCapability.WeaponFunction, "reference/" + WeaponCapability.FunctionProjectileReference,
        "scalar/" + WeaponCapability.TraitSet, "scalar/" + WeaponCapability.ArmorPenetrationLabel, "reference/" + WeaponCapability.StatusReference,
        // 1.7.0 (Runtime 0.30.0): the explosion a charge weapon's overcharge failure spawns (charge.overcharge_explosion).
        "reference/" + Metadata.SupportField.OverchargeExplosionReference,
    };
    // Value types first authored by 1.4.0: a project that saves one needs project format 11.
    public static readonly IReadOnlySet<string> Composition = new HashSet<string>(StringComparer.Ordinal)
    {
        WeaponCapability.FireRateSet, WeaponCapability.WeaponFunction, WeaponCapability.FunctionProjectileReference,
        WeaponCapability.TraitSet, WeaponCapability.ArmorPenetrationLabel, WeaponCapability.StatusReference,
    };
    // Backpack-linked entities (Guard Dog drones, the SH-51 energy shield: hd2.backpack(name):drone()), authored since 1.4.0.
    public static readonly bool LinkedBackpackEntities = true;
    // Mounted-weapon value types, and whether weapons carried by a backpack drone (hd2.backpack(name):drone():weapon()) are authored.
    // 1.4.0 adds typed status references (damage.status_<k>_type) and the Guard Dog drone weapons. A projectile reference (a host swap)
    // is not in this set, so it stays read-only here.
    public static readonly IReadOnlySet<string> MountedWeapon = new HashSet<string>(StringComparer.Ordinal) { "number", "integer", WeaponCapability.StatusReference };
    public static readonly bool CarriedWeapons = true;
    // 0.30.0 projectile donors (attack outputs of kind projectile_donor: Eagle payloads, orbital shells, sentry rounds, weapons' second
    // projectiles), offered in every projectile selector since 1.7.0 (docs/runtime030-integration-plan.md, Phase 7). None is live-tested:
    // every write carries allow_unverified_reference and allow_unverified_effect, shown as warnings.
    public static readonly bool ProjectileDonors = true;
    // 0.30.0 turret motion on vehicle mounts (turret.* with a published range and ordering), authored since 1.7.0 with its limit pairs kept
    // ordered inline (EntityOrder; docs/runtime030-integration-plan.md, Phase 5.2).
    public static readonly bool MountedTurretMotion = true;
    // 0.30.0 vehicle motion tuning (steering response, Exosuit rotation: Vehicle.TuningFieldInstanceKeys), authored since 1.7.0 (Phase 5.1).
    public static readonly bool VehicleTuning = true;
    // 0.30.0 support attack roles (the PLAS-45 Epoch's full-charge shot and its explosion, the Epoch's and the RS-422 Railgun's overcharge
    // explosions), authored since 1.7.0: two of them share one damage row, which the field editor resolves inline (ChargeFields.SameBytes).
    public static readonly IReadOnlySet<string> SupportRoles030 = new HashSet<string>(StringComparer.Ordinal) { "full_charge", "full_charge_impact", "overcharge_explosion" };
    public static readonly bool SupportChargeRoles = true;
    // 0.30.2 status slots on player-weapon explosions (explosion.<role>.<phase>.damage.status_<k>_type and _strength; Runtime's validator
    // floor is 0.30.2), authored since 1.7.1 on the explosion rows of Player Weapons (the direct-hit status editor; every write carries the
    // fields' published allow_unverified_effect, and allow_shared on the shared rows).
    public static readonly bool PlayerExplosionStatus = true;
    private static readonly System.Text.RegularExpressions.Regex PlayerExplosionStatusField =
        new(@"\Aexplosion\.[^.]+\.(impact|expiry)\.damage\.status_\d+_(type|strength)\z", System.Text.RegularExpressions.RegexOptions.CultureInvariant);
    public static bool IsPlayerExplosionStatus(string semanticFieldId) => PlayerExplosionStatusField.IsMatch(semanticFieldId);
    // 0.30.2 extended statuses (StatusEffectCatalog attachTier enemy_slot and other_system, 23 of them): every write needs
    // allow_unverified_effect on every row, live-proven rows included. Offered by every status slot since 1.7.1, grouped by tier; the write
    // carries the opt-in when the chosen value is extended (CompositionOptIns.Extended) and the export requires 0.30.2 (RuntimeRequirement).
    public static readonly bool ExtendedStatuses = true;
    public static bool Status(StatusEffect s) => ExtendedStatuses || !s.Extended;
    // 0.30.2 sentry and emplacement projectile hosts (VehicleWeaponCapabilities stratagemHosts; hd2.stratagem(name):attack('primary')
    // :projectile_source()): authored since 1.7.1 in the sentry or emplacement editor (the A/MG-43 and A/G-16 stay read-only with Runtime's reason).
    public static readonly bool StratagemProjectileHosts = true;
    public static bool Weapon(WeaponCapability f) => PlayerWeapon.Contains(f.Type) && (PlayerExplosionStatus || !IsPlayerExplosionStatus(f.SemanticFieldId));
    public static bool VehicleWeapon(VehicleWeaponFieldInstance f, bool carried) => MountedWeapon.Contains(f.Type) && (CarriedWeapons || !carried);
    public static bool SupportField(SupportField f) => Support.Contains(f.Value.Kind + "/" + f.Value.Type)
        && (SupportChargeRoles || f.Target.AttackRole is not { } role || !SupportRoles030.Contains(role));
    // Stratagem value types. 1.7.0 authors 0.30.0's call-in code (calldown_code) and presentation (stratagem_presentation; a project that saves
    // either needs project format 14).
    public static readonly IReadOnlySet<string> Stratagem = new HashSet<string>(StringComparer.Ordinal)
    { "number", "integer", "boolean", StratagemUses.Type, Metadata.StratagemField.CalldownCode, Metadata.StratagemField.Presentation };
    public static bool StratagemField(StratagemField f) => Stratagem.Contains(f.Type);
}
