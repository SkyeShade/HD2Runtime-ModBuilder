namespace HD2RuntimeGUI.Core.Metadata;

// Value types this build authors (an editor, a saved change and a Lua form), per catalog. Runtime may publish other writable types;
// they stay visible with Runtime's baseline but read-only, and the capability audit reports each one it finds.
public static class AuthoredTypes
{
    public const string NotAuthoredReason = "Published by this Runtime SDK but not authored by this ModBuilder build yet.";
    public static readonly IReadOnlySet<string> PlayerWeapon = new HashSet<string>(StringComparer.Ordinal)
    {
        "number", "integer", "boolean", "enum", "projectile_reference", "explosion_reference", WeaponCapability.FireModeSet,
    };
    public static readonly IReadOnlySet<string> Support = new HashSet<string>(StringComparer.Ordinal)
    {
        "scalar/number", "scalar/integer", "scalar/boolean", "scalar/" + WeaponCapability.FireModeSet,
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
