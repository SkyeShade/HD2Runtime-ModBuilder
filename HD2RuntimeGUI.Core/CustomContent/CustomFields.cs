using System.Globalization;
using HD2RuntimeGUI.Core.Models;

namespace HD2RuntimeGUI.Core.CustomContent;

public enum CustomFieldKind { Number, Integer, Choice, Toggle, Payload, Round, Mode, Text }
public enum CustomFieldStatus { Supported, Refused, Unavailable }

/// <summary>What the target Runtime says about one field for one custom stratagem: whether it can be written (and why not), its unit, range,
/// default and the choices a dropdown offers. Produced by the target adapter from Runtime's schema.</summary>
public sealed record CustomFieldInfo(string Path, CustomFieldStatus Status, string? Reason = null, string? Unit = null, double? Min = null,
    double? Max = null, bool ExclusiveMin = false, string? Default = null, bool Required = false, string? Doc = null,
    IReadOnlyList<string>? Choices = null)
{
    public bool Supported => Status == CustomFieldStatus.Supported;
    public static CustomFieldInfo Unknown(string path) => new(path, CustomFieldStatus.Unavailable, Localization.CoreText.Get("CustomContent.Field.NotDescribed"));
}

/// <summary>One field of a delivery family as ModBuilder edits it. Path is the field's place in the Runtime's project form (payload.gun.rpm,
/// payload.items[].modify.rpm): the target describes it from the schema by that path, and the Runtime validator's problems name it. Label and
/// Help are resource keys; Get and Set read and write the project model. Refused-only fields have no storage: they exist so the editor can say
/// why they are absent.</summary>
public sealed record CustomFieldDef(string Path, string Label, CustomFieldKind Kind, string Group,
    Func<CustomStratagem, object?>? Get = null, Action<CustomStratagem, object?>? Set = null, string? Help = null, string? Refusal = null)
{
    public bool RefusedOnly => Get == null;
}

/// <summary>ModBuilder's fields per delivery family (the editor is generated from these and the target's CustomFieldInfo). Lists (pod items),
/// presentation and models have their own editors.</summary>
public static class CustomFields
{
    public const string GroupDonor = "donor", GroupWeapon = "weapon", GroupTiming = "timing", GroupSpread = "spread", GroupPayload = "payload",
        GroupFlight = "flight", GroupOrbit = "orbit", GroupGun = "gun", GroupRefused = "refused";

    private static SupportDelivery Support(CustomStratagem s) => s.Delivery.Support ??= new();
    private static SentryDelivery Sentry(CustomStratagem s) => s.Delivery.Sentry ??= new();
    private static EagleDelivery Eagle(CustomStratagem s) => s.Delivery.Eagle ??= new();
    private static OrbitalDelivery Orbital(CustomStratagem s) => s.Delivery.Orbital ??= new();
    private static PelicanDelivery Pelican(CustomStratagem s) => s.Delivery.Pelican ??= new();
    private static SiloDelivery Silo(CustomStratagem s) => s.Delivery.Silo ??= new();
    private static ExpendableDelivery Expendable(CustomStratagem s) => s.Delivery.Expendable ??= new();
    private static VariantDelivery Variant(CustomStratagem s) => s.Delivery.Variant ??= new();
    private static void Gun(CustomStratagem s, Action<PelicanGun> edit)
    {
        var gun = Pelican(s).Gun ?? new PelicanGun();
        edit(gun);
        Pelican(s).Gun = gun.IsEmpty ? null : gun;
    }
    private static void Orbit(CustomStratagem s, Action<PelicanOrbit> edit)
    {
        var orbit = Pelican(s).Orbit ?? new PelicanOrbit();
        edit(orbit);
        Pelican(s).Orbit = orbit.IsEmpty ? null : orbit;
    }
    private static void Approach(CustomStratagem s, Action<PelicanApproach> edit)
    {
        var approach = Pelican(s).Approach ?? new PelicanApproach();
        edit(approach);
        Pelican(s).Approach = approach.IsEmpty ? null : approach;
    }
    private static void Weapon(CustomStratagem s, Func<CustomStratagem, WeaponOverrides?> get, Action<CustomStratagem, WeaponOverrides?> set, Action<WeaponOverrides> edit)
    {
        var weapon = get(s) ?? new WeaponOverrides();
        edit(weapon);
        set(s, weapon.IsEmpty ? null : weapon);
    }
    public static double? D(object? v) => v switch { null => null, double d => d, int i => i, string t when t.Length == 0 => null, _ => Convert.ToDouble(v, CultureInfo.InvariantCulture) };
    public static int? I(object? v) => v switch { null => null, int i => i, double d => (int)d, string t when t.Length == 0 => null, _ => Convert.ToInt32(v, CultureInfo.InvariantCulture) };
    public static string? S(object? v) => v is string s && s.Length > 0 ? s : null;
    private static bool? True(object? v) => v is true ? true : null;

    /// <summary>The weapon fields of a delivered weapon's own instance: a support weapon from its pod, a pod item, an expendable clone or a sentry's
    /// gun. prefix is the weapon table's project path (payload.items[].modify, payload.modify, payload.weapon).</summary>
    public static IEnumerable<CustomFieldDef> WeaponFields(string prefix, Func<CustomStratagem, WeaponOverrides?> get, Action<CustomStratagem, WeaponOverrides?> set,
        bool projectile = true, bool impact = true, bool sound = false)
    {
        if (projectile)
            yield return new(prefix + ".projectile", "CustomStratagem.Field.Projectile", CustomFieldKind.Round, GroupWeapon,
                s => get(s)?.Projectile, (s, v) => Weapon(s, get, set, w => w.Projectile = v as ContentRef), "CustomStratagem.Field.Projectile.Help");
        yield return new(prefix + ".rpm", "CustomStratagem.Field.FireRate", CustomFieldKind.Number, GroupWeapon,
            s => get(s)?.FireRate, (s, v) => Weapon(s, get, set, w => w.FireRate = D(v)));
        yield return new(prefix + ".spread", "CustomStratagem.Field.Spread", CustomFieldKind.Number, GroupWeapon,
            s => get(s)?.Spread, (s, v) => Weapon(s, get, set, w => w.Spread = D(v)));
        yield return new(prefix + ".ammo", "CustomStratagem.Field.Magazine", CustomFieldKind.Integer, GroupWeapon,
            s => get(s)?.Magazine, (s, v) => Weapon(s, get, set, w => w.Magazine = I(v)));
        yield return new(prefix + ".recoil", "CustomStratagem.Field.ZeroRecoil", CustomFieldKind.Toggle, GroupWeapon,
            s => get(s)?.Recoil == "zero", (s, v) => Weapon(s, get, set, w => w.Recoil = v is true ? "zero" : null));
        if (sound)
            yield return new(prefix + ".sound", "CustomStratagem.Field.Sound", CustomFieldKind.Choice, GroupWeapon,
                s => get(s)?.Sound, (s, v) => Weapon(s, get, set, w => w.Sound = S(v)), "CustomStratagem.Field.Sound.Help");
        if (impact)
        {
            yield return new(prefix + ".impact_explosion", "CustomStratagem.Field.ImpactPayload", CustomFieldKind.Payload, GroupPayload,
                s => get(s)?.ImpactPayload, (s, v) => Weapon(s, get, set, w => w.ImpactPayload = v as ContentRef), "CustomStratagem.Field.ImpactPayload.Help");
            yield return new(prefix + ".rounds", "CustomStratagem.Field.ConvertedRounds", CustomFieldKind.Integer, GroupPayload,
                s => get(s)?.ConvertedRounds, (s, v) => Weapon(s, get, set, w => w.ConvertedRounds = I(v)));
        }
    }

    public static readonly IReadOnlyList<CustomFieldDef> SupportFields =
    [
        new("payload.items[].donor", "CustomStratagem.Field.SupportItem", CustomFieldKind.Choice, GroupDonor, s => s.Delivery.Support?.Item, (s, v) => Support(s).Item = S(v) ?? "",
            "CustomStratagem.Field.SupportItem.Help"),
        new("payload.items[].count", "CustomStratagem.Field.Count", CustomFieldKind.Integer, GroupDonor, s => s.Delivery.Support?.Count, (s, v) => Support(s).Count = I(v)),
        .. WeaponFields("payload.items[].modify", s => s.Delivery.Support?.Weapon, (s, w) => Support(s).Weapon = w),
        new("support.items[].modify on a backpack", "CustomStratagem.Field.BackpackChanges", CustomFieldKind.Choice, GroupRefused),
        new("two support donors in one call", "CustomStratagem.Field.SecondItem", CustomFieldKind.Choice, GroupRefused),
    ];
    public static readonly IReadOnlyList<CustomFieldDef> PodFields =
    [
        new("a pod item that is a secondary, a throwable or has no pickup zone", "CustomStratagem.Field.PodSecondary", CustomFieldKind.Choice, GroupRefused),
        new("more pod items than a carrier rack's usable slots", "CustomStratagem.Field.PodCapacity", CustomFieldKind.Choice, GroupRefused),
        new("a carrier pod with several players", "CustomStratagem.Field.PodMultiplayer", CustomFieldKind.Choice, GroupRefused),
    ];
    public static readonly IReadOnlyList<CustomFieldDef> ExpendableFields =
    [
        new("payload.weapon", "CustomStratagem.Field.CloneDonor", CustomFieldKind.Choice, GroupDonor, s => s.Delivery.Expendable?.Weapon, (s, v) => Expendable(s).Weapon = S(v) ?? "",
            "CustomStratagem.Field.CloneDonor.Help"),
        new("payload.round", "CustomStratagem.Field.CloneRound", CustomFieldKind.Choice, GroupDonor, s => s.Delivery.Expendable?.Round, (s, v) => Expendable(s).Round = S(v),
            "CustomStratagem.Field.CloneRound.Help"),
        new("payload.level", "CustomStratagem.Field.CloneLevel", CustomFieldKind.Choice, GroupDonor, s => s.Delivery.Expendable?.Level, (s, v) => Expendable(s).Level = S(v),
            "CustomStratagem.Field.CloneLevel.Help"),
        .. WeaponFields("payload.modify", s => s.Delivery.Expendable?.Modify, (s, w) => Expendable(s).Modify = w, projectile: false),
        new("payload.modify.direct_damage", "CustomStratagem.Field.DirectDamage", CustomFieldKind.Choice, GroupPayload,
            s => s.Delivery.Expendable?.Modify?.DirectDamage?.ToString(CultureInfo.InvariantCulture),
            (s, v) => Weapon(s, x => x.Delivery.Expendable?.Modify, (x, w) => Expendable(x).Modify = w,
                w => w.DirectDamage = S(v) is { } text ? double.Parse(text, CultureInfo.InvariantCulture) : null), "CustomStratagem.Field.DirectDamage.Help"),
        new("expendable.modify.projectile", "CustomStratagem.Field.Projectile", CustomFieldKind.Round, GroupRefused),
        new("an expendable clone on a weapon outside the donor's component class (e.g. the AC-8 as an EAT)", "CustomStratagem.Field.CloneOtherClass", CustomFieldKind.Choice, GroupRefused),
        new("an expendable carrier weapon a lobby member brings", "CustomStratagem.Field.CloneLobby", CustomFieldKind.Choice, GroupRefused),
    ];
    public static readonly IReadOnlyList<CustomFieldDef> VariantFields =
    [
        new("payload.weapon", "CustomStratagem.Field.VariantWeapon", CustomFieldKind.Choice, GroupDonor, s => s.Delivery.Variant?.Weapon, (s, v) => Variant(s).Weapon = S(v) ?? "",
            "CustomStratagem.Field.VariantWeapon.Help"),
        new("payload.round", "CustomStratagem.Field.VariantRound", CustomFieldKind.Choice, GroupDonor, s => s.Delivery.Variant?.Round, (s, v) => Variant(s).Round = S(v),
            "CustomStratagem.Field.VariantRound.Help"),
    ];
    public static readonly IReadOnlyList<CustomFieldDef> SentryFields =
    [
        new("payload.donor", "CustomStratagem.Field.SentryChassis", CustomFieldKind.Choice, GroupDonor, s => s.Delivery.Sentry?.Chassis, (s, v) => Sentry(s).Chassis = S(v) ?? "",
            "CustomStratagem.Field.SentryChassis.Help"),
        .. WeaponFields("payload.weapon", s => s.Delivery.Sentry?.Weapon, (s, w) => Sentry(s).Weapon = w, impact: false, sound: true),
        new("sentry.weapon.impact_explosion", "CustomStratagem.Field.ImpactPayload", CustomFieldKind.Payload, GroupRefused),
    ];
    public static readonly IReadOnlyList<CustomFieldDef> EagleFields =
    [
        new("payload.donor", "CustomStratagem.Field.EagleStrike", CustomFieldKind.Choice, GroupDonor, s => s.Delivery.Eagle?.Strike, (s, v) => Eagle(s).Strike = S(v) ?? "",
            "CustomStratagem.Field.EagleStrike.Help"),
        new("payload.uses", "CustomStratagem.Field.Uses", CustomFieldKind.Integer, GroupDonor, s => s.Delivery.Eagle?.Uses, (s, v) => Eagle(s).Uses = I(v)),
        new("payload.rearm_seconds", "CustomStratagem.Field.RearmSeconds", CustomFieldKind.Number, GroupTiming, s => s.Delivery.Eagle?.RearmSeconds, (s, v) => Eagle(s).RearmSeconds = D(v)),
        new("payload.payload.impact_explosion", "CustomStratagem.Field.ImpactPayload", CustomFieldKind.Payload, GroupPayload, s => s.Delivery.Eagle?.ImpactPayload,
            (s, v) => Eagle(s).ImpactPayload = v as ContentRef, "CustomStratagem.Field.EagleImpact.Help"),
        new("eagle.payload.projectile", "CustomStratagem.Field.EagleProjectile", CustomFieldKind.Round, GroupRefused),
        new("eagle.pattern", "CustomStratagem.Field.EaglePattern", CustomFieldKind.Choice, GroupRefused),
        new("eagle cooldown", "CustomStratagem.Field.EagleCooldown", CustomFieldKind.Number, GroupRefused),
    ];
    public static readonly IReadOnlyList<CustomFieldDef> OrbitalFields =
    [
        new("payload.native", "CustomStratagem.Field.OrbitalMode", CustomFieldKind.Mode, GroupDonor, s => s.Delivery.Orbital?.Mode ?? OrbitalDelivery.RuntimeBarrage,
            (s, v) => Orbital(s).Mode = S(v) ?? OrbitalDelivery.RuntimeBarrage, "CustomStratagem.Field.OrbitalMode.Help"),
        new("payload.shell", "CustomStratagem.Field.Shell", CustomFieldKind.Choice, GroupDonor, s => s.Delivery.Orbital?.Shell, (s, v) => Orbital(s).Shell = S(v),
            "CustomStratagem.Field.Shell.Help"),
        new("payload.pattern", "CustomStratagem.Field.Pattern", CustomFieldKind.Choice, GroupDonor, s => s.Delivery.Orbital?.Pattern, (s, v) => Orbital(s).Pattern = S(v),
            "CustomStratagem.Field.Pattern.Help"),
        new("payload.salvos", "CustomStratagem.Field.Salvos", CustomFieldKind.Integer, GroupTiming, s => s.Delivery.Orbital?.Salvos, (s, v) => Orbital(s).Salvos = I(v)),
        new("payload.shells_per_salvo", "CustomStratagem.Field.ShellsPerSalvo", CustomFieldKind.Integer, GroupTiming, s => s.Delivery.Orbital?.ShellsPerSalvo, (s, v) => Orbital(s).ShellsPerSalvo = I(v)),
        new("payload.shell_interval", "CustomStratagem.Field.ShellInterval", CustomFieldKind.Number, GroupTiming, s => s.Delivery.Orbital?.ShellInterval, (s, v) => Orbital(s).ShellInterval = D(v)),
        new("payload.salvo_interval", "CustomStratagem.Field.SalvoInterval", CustomFieldKind.Number, GroupTiming, s => s.Delivery.Orbital?.SalvoInterval, (s, v) => Orbital(s).SalvoInterval = D(v)),
        new("payload.scatter", "CustomStratagem.Field.Scatter", CustomFieldKind.Number, GroupSpread, s => s.Delivery.Orbital?.Scatter, (s, v) => Orbital(s).Scatter = D(v)),
        new("payload.salvo_scatter", "CustomStratagem.Field.SalvoScatter", CustomFieldKind.Number, GroupSpread, s => s.Delivery.Orbital?.SalvoScatter, (s, v) => Orbital(s).SalvoScatter = D(v)),
        new("payload.impact_explosion", "CustomStratagem.Field.ImpactPayload", CustomFieldKind.Payload, GroupPayload, s => s.Delivery.Orbital?.ImpactPayload,
            (s, v) => Orbital(s).ImpactPayload = v as ContentRef, "CustomStratagem.Field.OrbitalImpact.Help"),
        new("orbital.explosion_scale", "CustomStratagem.Field.ExplosionScale", CustomFieldKind.Number, GroupRefused),
    ];
    public static readonly IReadOnlyList<CustomFieldDef> PelicanFields =
    [
        new("payload.hover", "CustomStratagem.Field.Hover", CustomFieldKind.Number, GroupFlight, s => s.Delivery.Pelican?.Hover, (s, v) => Pelican(s).Hover = D(v), "CustomStratagem.Field.Hover.Help"),
        new("payload.approach.distance", "CustomStratagem.Field.ApproachDistance", CustomFieldKind.Number, GroupFlight, s => s.Delivery.Pelican?.Approach?.Distance, (s, v) => Approach(s, a => a.Distance = D(v))),
        new("payload.approach.height", "CustomStratagem.Field.ApproachHeight", CustomFieldKind.Number, GroupFlight, s => s.Delivery.Pelican?.Approach?.Height, (s, v) => Approach(s, a => a.Height = D(v))),
        new("payload.orbit.radius", "CustomStratagem.Field.OrbitRadius", CustomFieldKind.Number, GroupOrbit, s => s.Delivery.Pelican?.Orbit?.Radius, (s, v) => Orbit(s, o => o.Radius = D(v))),
        new("payload.orbit.altitude", "CustomStratagem.Field.OrbitAltitude", CustomFieldKind.Number, GroupOrbit, s => s.Delivery.Pelican?.Orbit?.Altitude, (s, v) => Orbit(s, o => o.Altitude = D(v))),
        new("payload.orbit.duration", "CustomStratagem.Field.OrbitDuration", CustomFieldKind.Number, GroupOrbit, s => s.Delivery.Pelican?.Orbit?.Duration, (s, v) => Orbit(s, o => o.Duration = D(v))),
        new("payload.orbit.period", "CustomStratagem.Field.OrbitPeriod", CustomFieldKind.Number, GroupOrbit, s => s.Delivery.Pelican?.Orbit?.Period, (s, v) => Orbit(s, o => o.Period = D(v))),
        new("payload.orbit.entry", "CustomStratagem.Field.OrbitEntry", CustomFieldKind.Number, GroupOrbit, s => s.Delivery.Pelican?.Orbit?.Entry, (s, v) => Orbit(s, o => o.Entry = D(v))),
        new("payload.gun.round", "CustomStratagem.Field.GunRound", CustomFieldKind.Choice, GroupGun, s => s.Delivery.Pelican?.Gun?.Round, (s, v) => Gun(s, g => g.Round = S(v)), "CustomStratagem.Field.GunRound.Help"),
        new("payload.gun.behave_as", "CustomStratagem.Field.GunBehaveAs", CustomFieldKind.Choice, GroupGun, s => s.Delivery.Pelican?.Gun?.BehaveAs, (s, v) => Gun(s, g => g.BehaveAs = S(v)), "CustomStratagem.Field.GunBehaveAs.Help"),
        new("payload.gun.rate_multiplier", "CustomStratagem.Field.GunRateMultiplier", CustomFieldKind.Choice, GroupGun,
            s => s.Delivery.Pelican?.Gun?.RateMultiplier?.ToString("R", CultureInfo.InvariantCulture),
            (s, v) => Gun(s, g => g.RateMultiplier = S(v) is { } t ? double.Parse(t, CultureInfo.InvariantCulture) : null)),
        new("payload.gun.rpm", "CustomStratagem.Field.FireRate", CustomFieldKind.Number, GroupGun, s => s.Delivery.Pelican?.Gun?.Rpm, (s, v) => Gun(s, g => g.Rpm = D(v))),
        new("payload.gun.casing", "CustomStratagem.Field.GunCasing", CustomFieldKind.Choice, GroupGun, s => s.Delivery.Pelican?.Gun?.Casing, (s, v) => Gun(s, g => g.Casing = S(v))),
        new("payload.gun.spread", "CustomStratagem.Field.Spread", CustomFieldKind.Number, GroupGun, s => s.Delivery.Pelican?.Gun?.Spread, (s, v) => Gun(s, g => g.Spread = D(v))),
        new("payload.gun.aim_height", "CustomStratagem.Field.GunAimHeight", CustomFieldKind.Number, GroupGun, s => s.Delivery.Pelican?.Gun?.AimHeight, (s, v) => Gun(s, g => g.AimHeight = D(v))),
        new("payload.gun.recoil", "CustomStratagem.Field.ZeroRecoil", CustomFieldKind.Toggle, GroupGun, s => s.Delivery.Pelican?.Gun?.NoRecoil == true, (s, v) => Gun(s, g => g.NoRecoil = True(v))),
        new("payload.gun.unlimited_ammo", "CustomStratagem.Field.GunUnlimitedAmmo", CustomFieldKind.Toggle, GroupGun, s => s.Delivery.Pelican?.Gun?.UnlimitedAmmo == true, (s, v) => Gun(s, g => g.UnlimitedAmmo = True(v))),
        new("payload.gun.face_target", "CustomStratagem.Field.GunFaceTarget", CustomFieldKind.Toggle, GroupGun, s => s.Delivery.Pelican?.Gun?.FaceTarget == true, (s, v) => Gun(s, g => g.FaceTarget = True(v))),
        new("payload.gun.sound", "CustomStratagem.Field.Sound", CustomFieldKind.Choice, GroupGun, s => s.Delivery.Pelican?.Gun?.Sound, (s, v) => Gun(s, g => g.Sound = S(v)), "CustomStratagem.Field.Sound.Help"),
        new("payload.gun.impact_explosion", "CustomStratagem.Field.GunImpact", CustomFieldKind.Choice, GroupGun, s => s.Delivery.Pelican?.Gun?.ImpactExplosion, (s, v) => Gun(s, g => g.ImpactExplosion = S(v)),
            "CustomStratagem.Field.GunImpact.Help"),
    ];
    public static readonly IReadOnlyList<CustomFieldDef> SiloFields =
    [
        new("payload.donor", "CustomStratagem.Field.SiloDonor", CustomFieldKind.Choice, GroupDonor, s => s.Delivery.Silo?.Donor, (s, v) => Silo(s).Donor = S(v) ?? ""),
        new("payload.blast", "CustomStratagem.Field.SiloBlast", CustomFieldKind.Choice, GroupPayload, s => s.Delivery.Silo?.Blast, (s, v) => Silo(s).Blast = S(v), "CustomStratagem.Field.SiloBlast.Help"),
        new("payload.fallback", "CustomStratagem.Field.SiloFallback", CustomFieldKind.Choice, GroupPayload, s => s.Delivery.Silo?.Fallback, (s, v) => Silo(s).Fallback = S(v), "CustomStratagem.Field.SiloFallback.Help"),
    ];

    public static IReadOnlyList<CustomFieldDef> For(string family) => family switch
    {
        DeliveryFamily.Support => SupportFields,
        DeliveryFamily.Pod => PodFields,
        DeliveryFamily.Expendable => ExpendableFields,
        DeliveryFamily.Variant => VariantFields,
        DeliveryFamily.Sentry => SentryFields,
        DeliveryFamily.Eagle => EagleFields,
        DeliveryFamily.Orbital => OrbitalFields,
        DeliveryFamily.Pelican => PelicanFields,
        DeliveryFamily.Silo => SiloFields,
        _ => [],
    };

    /// <summary>A fresh delivery of a family (only that family's settings exist), with the defaults its Runtime examples use where a field is
    /// required (a Pelican's hover, an expendable's pod).</summary>
    public static CustomDelivery NewDelivery(string family) => family switch
    {
        DeliveryFamily.Support => new() { Family = family, Support = new() },
        DeliveryFamily.Pod => new() { Family = family, Pod = new() },
        DeliveryFamily.Expendable => new() { Family = family, Expendable = new() { Pod = [new() { Item = PodItem.Clone, Count = 2 }] } },
        DeliveryFamily.Variant => new() { Family = family, Variant = new() },
        DeliveryFamily.Sentry => new() { Family = family, Sentry = new() },
        DeliveryFamily.Eagle => new() { Family = family, Eagle = new() },
        DeliveryFamily.Orbital => new() { Family = family, Orbital = new() },
        DeliveryFamily.Pelican => new() { Family = family, Pelican = new() { Hover = 60 } },
        DeliveryFamily.Silo => new() { Family = family, Silo = new() },
        _ => new() { Family = DeliveryFamily.Scripted },
    };

    /// <summary>Whether a delivery holds settings that switching family would lose.</summary>
    public static bool HasSettings(CustomDelivery d) => d.Support is { Item.Length: > 0 } || d.Sentry is { Chassis.Length: > 0 } || d.Eagle is { Strike.Length: > 0 }
        || d.Orbital is { } o && (o.Shell != null || o.Pattern != null || o.ImpactPayload != null) || d.Pod is { Items.Count: > 0 }
        || d.Expendable is { Weapon.Length: > 0 } || d.Variant is { Weapon.Length: > 0 } || d.Silo is { Donor.Length: > 0 }
        || d.Pelican is { } p && (p.Gun != null || p.Orbit != null || p.Approach != null) || d.Scripted is { VanillaPod: not null };
}
