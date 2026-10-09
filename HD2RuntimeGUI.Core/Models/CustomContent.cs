using System.Text.Json.Serialization;

namespace HD2RuntimeGUI.Core.Models;

// Format 14 (1.7.0): experimental custom content. The project stores what each custom object IS, in ModBuilder's own words; the
// Runtime API it is compiled into (hd2.custom_stratagem.register and the development custom projectile modules of HD2Runtime 0.30)
// is decided by a target adapter at build time (CustomContent/Runtime030Target). No Runtime field name, generated Lua, address or
// hash is ever saved here, so a later Runtime API change needs an adapter update (or a versioned migration of this section), never a
// new project. Custom objects live in their own namespaces and are never mixed with edits of existing game objects.
public sealed class CustomContentSection
{
    // The version of this section's own model (customContentVersion). CustomContentMigrations brings older sections up to date when a
    // project is opened; a newer one is refused, so an older ModBuilder never drops what it does not know.
    public const int CurrentVersion = 1;
    public int Version { get; set; } = CurrentVersion;
    public List<CustomStratagem> Stratagems { get; set; } = [];
    public List<CustomProjectile> Projectiles { get; set; } = [];
    // Existing weapons that fire a custom projectile instead of their own (a typed reference, never a vanilla id).
    public List<CustomProjectileAssignment> Assignments { get; set; } = [];
    // Source PNGs (stratagem icons, delivered weapons' prompt icons) kept in the project itself, so it reopens, duplicates and imports
    // complete. Compiled at every build.
    public List<CustomImage> Images { get; set; } = [];
    // Recoloured weapon models a weapon variant shows (derived from the user's installed game at export, never shipped as game data here).
    public List<CustomModel> Models { get; set; } = [];
    [JsonIgnore] public bool IsEmpty => Stratagems.Count == 0 && Projectiles.Count == 0 && Assignments.Count == 0;
}

/// <summary>A typed reference to something a custom object uses. The kind keeps references to the project's own custom objects apart
/// from references into the game's catalogues, for validation, packaging, migrations and dependency analysis.</summary>
public sealed record ContentRef(string Kind, string Id)
{
    // A project custom projectile, by its Builder id.
    public const string CustomProjectile = "custom_projectile";
    // The round a vanilla support weapon fires, by the weapon's catalogued name (Runtime's reviewed projectile donors).
    public const string VanillaRound = "vanilla_round";
    // A vanilla explosion donor, by its catalogued name (Runtime's reviewed explosion donors).
    public const string VanillaExplosion = "vanilla_explosion";
    public static ContentRef Custom(string id) => new(CustomProjectile, id);
    [JsonIgnore] public bool IsCustom => Kind == CustomProjectile;
}

/// <summary>A new, Runtime-owned stratagem: identity, call-in, carrier policy and one delivery family. Its Id is its identity everywhere
/// (multiplayer registries, asset names); it is generated once from the first name and never derived from the display name again.</summary>
public sealed class CustomStratagem
{
    public string Id { get; set; } = "";
    // The HUD name (Runtime shows it in capitals by convention) and the panel / loadout name (null: the HUD name).
    public string Name { get; set; } = "";
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? NameCased { get; set; }
    public string Description { get; set; } = "";
    // An image id in CustomContentSection.Images (null until an icon is imported).
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Icon { get; set; }
    // Directions: up, down, left, right.
    public List<string> Code { get; set; } = [];
    // Seconds from the call-in's arrival; null keeps the carrier's own.
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public double? Cooldown { get; set; }
    // Calls per mission, each player's own; null: unlimited. 0 or -1 (unlimited too, HD2Runtime 0.30.3) only as imported from a Runtime project,
    // kept so it round-trips (RuntimeRequirement.Release0303); ModBuilder itself writes null for unlimited.
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public int? Uses { get; set; }
    // At most this many of each player's loadout slots hold it (a further pick is refused); null: no limit.
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public int? MaxPerPlayer { get; set; }
    // The item traits the custom panel shows after the automatic CUSTOM STRATAGEM (empty: the payload family's).
    public List<string> Traits { get; set; } = [];
    public CustomCarrier Carrier { get; set; } = new();
    public CustomDelivery Delivery { get; set; } = new();
    // Extra vanilla stratagems whose packages the call needs (the delivery's own donors are added by the build).
    public List<string> Assets { get; set; } = [];
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public CustomCallbacks? Callbacks { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Notes { get; set; }
}

/// <summary>Which vanilla stratagem may carry the custom one. Group is a structural carrier pool the Runtime picks from (the usual
/// choice; the family's default when null); the beacon, preferred and allowed families and exclusions refine or replace it. A delivery
/// family adds its own rules at build time (a sentry needs a sentry carrier).</summary>
public sealed class CustomCarrier
{
    public const string Offensive = "offensive", Support = "support", Any = "any";
    public static readonly IReadOnlyList<string> Beacons = [Offensive, Support, Any];
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Group { get; set; }
    // A pod capacity to ask the group for (pod and expendable groups only); null: the pod's items.
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public int? Slots { get; set; }
    // Null: the group's beacon (a beacon is required when no group is given).
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Beacon { get; set; }
    public List<string> Prefer { get; set; } = [];
    public List<string> Allow { get; set; } = [];
    public List<string> Exclude { get; set; } = [];
}

/// <summary>The delivery families ModBuilder offers. "scripted" is the escape hatch: the carrier delivers nothing (or a vanilla support
/// pod) and the stratagem's Lua callbacks do the rest.</summary>
public static class DeliveryFamily
{
    public const string Support = "support", Pod = "pod", Expendable = "expendable", Variant = "weapon", Sentry = "sentry", Eagle = "eagle",
        Orbital = "orbital", Pelican = "pelican", Silo = "silo", Scripted = "scripted";
    public static readonly IReadOnlyList<string> All = [Orbital, Eagle, Pelican, Support, Expendable, Pod, Variant, Sentry, Silo, Scripted];
}

/// <summary>What the call delivers: exactly one family, with only that family's settings saved.</summary>
public sealed class CustomDelivery
{
    public string Family { get; set; } = DeliveryFamily.Orbital;
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public SupportDelivery? Support { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public PodDelivery? Pod { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public ExpendableDelivery? Expendable { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public VariantDelivery? Variant { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public SentryDelivery? Sentry { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public EagleDelivery? Eagle { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public OrbitalDelivery? Orbital { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public PelicanDelivery? Pelican { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public SiloDelivery? Silo { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public ScriptedDelivery? Scripted { get; set; }
}

/// <summary>A vanilla support item's own pod (a support weapon or backpack), its delivered weapon optionally reconfigured.</summary>
public sealed class SupportDelivery
{
    public string Item { get; set; } = "";
    // How many the pod brings; null: the item's own rack count (the only count Runtime accepts).
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public int? Count { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public WeaponOverrides? Weapon { get; set; }
}

/// <summary>A carrier pod packed with items of the author's choice (support weapons, backpacks, player weapons), up to the carrier rack's
/// capacity. Runtime: solo host only.</summary>
public sealed class PodDelivery
{
    public List<PodItem> Items { get; set; } = [];
}

/// <summary>One item of a pod: a catalogued pod item key (support_weapon/&lt;name&gt;, backpack/&lt;name&gt;, player_weapon/&lt;name&gt;), or
/// "clone" (an expendable pod's own cloned weapon). A weapon item can be reconfigured on its own instance.</summary>
public sealed class PodItem
{
    public const string Clone = "clone";
    public string Item { get; set; } = "";
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public int? Count { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public WeaponOverrides? Weapon { get; set; }
}

/// <summary>How a delivered weapon presents itself (its prompt, map label, weapon panel and marker icon): the custom stratagem's own name
/// and icon by default, the donor's own, or the author's.</summary>
public sealed class DeliveredPresentation
{
    public const string Stratagem = "stratagem", Donor = "donor", Custom = "custom";
    public string NameMode { get; set; } = Stratagem;
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Name { get; set; }
    public string IconMode { get; set; } = Stratagem;
    // An image id in CustomContentSection.Images (IconMode custom).
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Icon { get; set; }
    [JsonIgnore] public bool IsDefault => NameMode == Stratagem && IconMode == Stratagem;
}

/// <summary>An expendable weapon of its own: a clone of a reviewed expendable donor (EAT-17) delivered in a carrier weapon's pod, with its
/// own presentation, reconfigured weapon, another reviewed round and the pod's contents.</summary>
public sealed class ExpendableDelivery
{
    public const string LevelPresentation = "presentation", LevelModel = "model", LevelFull = "full";
    public string Weapon { get; set; } = "";
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public DeliveredPresentation? Presentation { get; set; }
    // Its weapon fields (no projectile: the clone fires its donor's round, or Round).
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public WeaponOverrides? Modify { get; set; }
    // Another support weapon's round the clone fires (a reviewed clone round), by that weapon's name.
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Round { get; set; }
    // How much of the donor the clone takes (null: full).
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Level { get; set; }
    // What the carrier weapon's pod holds (null: its vanilla rack); one entry is the clone.
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public List<PodItem>? Pod { get; set; }
}

/// <summary>A mission-scoped variant of a reviewed weapon (the M-1000 Maxigun) delivered by that weapon's own stratagem: another round of
/// its compatibility class, its own presentation and optionally a recoloured model.</summary>
public sealed class VariantDelivery
{
    public const string ModelApply = "apply", ModelCheck = "check";
    public string Weapon { get; set; } = "";
    // A catalogued attack output (output/v1/projectile/...) of the host's own compatibility class.
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Round { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public DeliveredPresentation? Presentation { get; set; }
    // A model id in CustomContentSection.Models.
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Model { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? ModelUse { get; set; }
    // An explicit allow_shared from an imported Runtime project; a shared host is always written with allow_shared = true (a warning says so).
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public bool? AllowShared { get; set; }
}

/// <summary>A vanilla sentry's own pod and chassis, with its own weapon reconfigured (never the sentry type's shared definition).</summary>
public sealed class SentryDelivery
{
    public string Chassis { get; set; } = "";
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public WeaponOverrides? Weapon { get; set; }
}

/// <summary>A vanilla Eagle's own strike from an Eagle carrier, with its own uses and an optional impact payload.</summary>
public sealed class EagleDelivery
{
    public string Strike { get; set; } = "";
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public int? Uses { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public double? RearmSeconds { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public ContentRef? ImpactPayload { get; set; }
}

/// <summary>A bombardment: either a Runtime barrage of a vanilla shell in an (adjusted) vanilla pattern, or a vanilla barrage used as
/// the game fires it with each shell's impact replaced.</summary>
public sealed class OrbitalDelivery
{
    public const string RuntimeBarrage = "runtime", NativeBarrage = "native";
    public string Mode { get; set; } = RuntimeBarrage;
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Shell { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Pattern { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public int? Salvos { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public int? ShellsPerSalvo { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public double? ShellInterval { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public double? SalvoInterval { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public double? Scatter { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public double? SalvoScatter { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public ContentRef? ImpactPayload { get; set; }
}

/// <summary>The game's own transport Pelican summoned over the beacon: how long it stays, how it circles and arrives, and its chin gun
/// (its own copies only).</summary>
public sealed class PelicanDelivery
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public double? Hover { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public PelicanOrbit? Orbit { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public PelicanApproach? Approach { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public PelicanGun? Gun { get; set; }
}
public sealed class PelicanOrbit
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public double? Radius { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public double? Altitude { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public double? Duration { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public double? Period { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public double? Entry { get; set; }
    [JsonIgnore] public bool IsEmpty => Radius == null && Altitude == null && Duration == null && Period == null && Entry == null;
}
public sealed class PelicanApproach
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public double? Distance { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public double? Height { get; set; }
    [JsonIgnore] public bool IsEmpty => Distance == null && Height == null;
}
/// <summary>The Pelican's chin gun. Null keeps its own; Runtime's rules (the Gatling AI, slow explosion donors) are checked by the target.</summary>
public sealed class PelicanGun
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Round { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? BehaveAs { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public double? RateMultiplier { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public double? Rpm { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Casing { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public double? Spread { get; set; }
    // True: no aim recoil.
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public bool? NoRecoil { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public bool? UnlimitedAmmo { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public bool? FaceTarget { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Sound { get; set; }
    // A Pelican gun explosion donor, by name.
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? ImpactExplosion { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public double? AimHeight { get; set; }
    [JsonIgnore] public bool IsEmpty => Round == null && BehaveAs == null && RateMultiplier == null && Rpm == null && Casing == null && Spread == null
        && NoRecoil == null && UnlimitedAmmo == null && FaceTarget == null && Sound == null && ImpactExplosion == null && AimHeight == null;
}

/// <summary>A vanilla missile silo's own pod whose missile requests another explosion where it detonates (and a fallback when that one's
/// packages are not resident).</summary>
public sealed class SiloDelivery
{
    public string Donor { get; set; } = "";
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Blast { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Fallback { get; set; }
}

/// <summary>The escape hatch's delivery: nothing (the callbacks deliver; null) or a vanilla support stratagem's own pod.</summary>
public sealed class ScriptedDelivery
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? VanillaPod { get; set; }
}

/// <summary>Changes to one delivered weapon's own instance (a support weapon from the pod, a pod item, an expendable clone or a sentry's gun).
/// Null keeps its own.</summary>
public sealed class WeaponOverrides
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public ContentRef? Projectile { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public double? FireRate { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public double? Spread { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public int? Magazine { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Recoil { get; set; }
    // A catalogued firing sound (sentries).
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Sound { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public ContentRef? ImpactPayload { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public int? ConvertedRounds { get; set; }
    // An expendable clone's reviewed direct-hit damage of its donor's own round (modify.direct_damage, with ImpactPayload).
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public double? DirectDamage { get; set; }
    [JsonIgnore] public bool IsEmpty => Projectile == null && FireRate == null && Spread == null && Magazine == null && Recoil == null && Sound == null
        && ImpactPayload == null && ConvertedRounds == null && DirectDamage == null;
}

/// <summary>The advanced escape hatch: Lua function bodies the call runs (each receives <c>ctx</c>), and extra registration fields. Behaviour
/// written here is not data, so other players' Runtimes cannot derive it; declarative fields are preferred whenever they can say the same.</summary>
public sealed class CustomCallbacks
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? OnCalled { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? OnBeaconCreated { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? OnBeaconLanded { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? OnActivate { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? OnDelivered { get; set; }
    // Lua code run once before the registration (local helpers and state the callbacks share), and table fields added to the registration
    // as written (for Runtime fields ModBuilder does not model yet).
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Setup { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? ExtraFields { get; set; }
    [JsonIgnore] public bool IsEmpty => new[] { OnCalled, OnBeaconCreated, OnBeaconLanded, OnActivate, OnDelivered, Setup, ExtraFields }.All(string.IsNullOrWhiteSpace);
    [JsonIgnore] public bool HasBehaviour => new[] { OnCalled, OnBeaconCreated, OnBeaconLanded, OnActivate, OnDelivered }.Any(b => !string.IsNullOrWhiteSpace(b));
}

/// <summary>A new projectile: a vanilla base projectile with whole components taken from other vanilla projectiles. Projectiles are named
/// by their catalogued output ids (output/v1/projectile/...); the custom projectile itself has its own stable Builder id.</summary>
public sealed class CustomProjectile
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Description { get; set; }
    public string Base { get; set; } = "";
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Visual { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Damage { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? ImpactExplosion { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Ballistics { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Notes { get; set; }
}

/// <summary>An existing weapon's attack that fires a custom projectile. The host kinds are AttackOutputChange's.</summary>
public sealed class CustomProjectileAssignment
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string HostKind { get; set; } = AttackOutputChange.PlayerHost;
    public string Weapon { get; set; } = "";
    public string AttackRole { get; set; } = "primary";
    public ContentRef Projectile { get; set; } = ContentRef.Custom("");
    public bool Enabled { get; set; } = true;
}

/// <summary>An editable source image (a 256 x 256 PNG) kept in the project. Preparation "auto" converts red-and-white art to the game's
/// icon masks at build (masks are used as given); "raw" uses the pixels exactly.</summary>
public sealed class CustomImage
{
    public const string Auto = "auto", Raw = "raw";
    public string Id { get; set; } = "";
    public string Png { get; set; } = "";
    public string Sha256 { get; set; } = "";
    public string Preparation { get; set; } = Auto;
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? SourceName { get; set; }
}

/// <summary>A weapon model of the mod's own: a reviewed base weapon's unit with its own colours per material zone. The build derives the
/// unit, material and colour table from the user's installed game (the base's parts must be the reviewed bytes) under the mod's names.</summary>
public sealed class CustomModel
{
    public const string DebugPalette = "debug";
    public string Id { get; set; } = "";
    public string Base { get; set; } = "";
    // True: one unmistakable colour per zone (to learn which zone is which part); otherwise All and Zones.
    public bool Debug { get; set; }
    // "#RRGGBB" for every zone without its own colour (null: the base's).
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? All { get; set; }
    // Zone ("0".."7") -> "#RRGGBB".
    public SortedDictionary<string, string> Zones { get; set; } = new(StringComparer.Ordinal);
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Description { get; set; }
}
