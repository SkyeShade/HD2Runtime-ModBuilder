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
    };
    // Value types first authored by 1.4.0: a project that saves one needs project format 11.
    public static readonly IReadOnlySet<string> Composition = new HashSet<string>(StringComparer.Ordinal)
    {
        WeaponCapability.FireRateSet, WeaponCapability.WeaponFunction, WeaponCapability.FunctionProjectileReference,
        WeaponCapability.TraitSet, WeaponCapability.ArmorPenetrationLabel, WeaponCapability.StatusReference,
    };
    // Backpack-linked entities (Guard Dog drones, the SH-51 energy shield: hd2.backpack(name):drone()).
    public const bool LinkedBackpackEntities = false;
    // Mounted-weapon value types, and whether weapons carried by a backpack drone (hd2.backpack(name):drone():weapon()) are authored.
    public static readonly IReadOnlySet<string> MountedWeapon = new HashSet<string>(StringComparer.Ordinal) { "number", "integer" };
    public const bool CarriedWeapons = false;
    public static bool Weapon(WeaponCapability f) => PlayerWeapon.Contains(f.Type);
    public static bool VehicleWeapon(VehicleWeaponFieldInstance f, bool carried) => MountedWeapon.Contains(f.Type) && (CarriedWeapons || !carried);
    public static bool SupportField(SupportField f) => Support.Contains(f.Value.Kind + "/" + f.Value.Type);
}
