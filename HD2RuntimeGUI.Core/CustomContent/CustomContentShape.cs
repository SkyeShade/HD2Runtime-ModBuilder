using System.Text.RegularExpressions;
using HD2RuntimeGUI.Core.Models;

namespace HD2RuntimeGUI.Core.CustomContent;

/// <summary>The structural rules of a saved custom content section (ProjectIdentity.Validate): ids are valid and unique, every list and
/// text is bounded, references are typed, numbers are finite, images are base64 PNG data of bounded size. What a Runtime would think of the
/// values (ranges, donors, families) is not structure: it is reported where the object is edited and refuses the build, so a project being
/// edited always saves and reopens.</summary>
public static class CustomContentShape
{
    public const int MaxObjects = 64, MaxPngBytes = 300 * 1024, MaxLua = 100_000;
    private static readonly Regex Output = new(@"\Aoutput/v1/projectile/[a-z0-9-]{1,128}\z", RegexOptions.CultureInvariant);
    private static readonly Regex Role = new(@"\A[a-z][a-z_0-9]{0,63}\z", RegexOptions.CultureInvariant);
    private static readonly Regex Hex = new(@"\A[a-f0-9]{64}\z", RegexOptions.CultureInvariant);
    private static readonly Regex Colour = new(@"\A#[0-9A-Fa-f]{6}\z", RegexOptions.CultureInvariant);
    private static bool Text(string? s, int max, bool allowNull = false) => s == null ? allowNull : s.Length <= max && !s.Contains('\0');
    private static bool Names(List<string>? list, int count, int max) => list != null && list.Count <= count && list.All(s => s != null && s.Length > 0 && s.Length <= max && !s.Any(char.IsControl));
    private static bool Number(double? v) => v == null || double.IsFinite(v.Value);
    private static bool Ref(ContentRef? r) => r == null || r.Kind is ContentRef.CustomProjectile or ContentRef.VanillaRound or ContentRef.VanillaExplosion && Text(r.Id, 256) && r.Id.Length > 0
        && (r.Kind != ContentRef.CustomProjectile || CustomIds.Valid(r.Id));
    private static bool Weapon(WeaponOverrides? w) => w == null || Ref(w.Projectile) && Ref(w.ImpactPayload) && Number(w.FireRate) && Number(w.Spread) && Text(w.Recoil, 32, true)
        && Text(w.Sound, 256, true) && (w.Projectile == null || w.Projectile.Kind != ContentRef.VanillaExplosion) && (w.ImpactPayload == null || w.ImpactPayload.Kind == ContentRef.VanillaExplosion);
    private static bool Pod(List<PodItem>? items, bool allowNull) => items == null ? allowNull
        : items.Count <= 16 && items.All(i => i != null && Text(i.Item, 256) && Weapon(i.Weapon));
    private static bool Presentation(DeliveredPresentation? p) => p == null
        || p.NameMode is DeliveredPresentation.Stratagem or DeliveredPresentation.Donor or DeliveredPresentation.Custom && p.IconMode is DeliveredPresentation.Stratagem or DeliveredPresentation.Donor or DeliveredPresentation.Custom
        && Text(p.Name, 256, true) && (p.Icon == null || CustomIds.ValidImage(p.Icon));
    private static bool Pelican(PelicanDelivery? p) => p == null || Number(p.Hover)
        && (p.Orbit == null || Number(p.Orbit.Radius) && Number(p.Orbit.Altitude) && Number(p.Orbit.Duration) && Number(p.Orbit.Period) && Number(p.Orbit.Entry))
        && (p.Approach == null || Number(p.Approach.Distance) && Number(p.Approach.Height))
        && (p.Gun == null || Number(p.Gun.RateMultiplier) && Number(p.Gun.Rpm) && Number(p.Gun.Spread) && Number(p.Gun.AimHeight) && Text(p.Gun.Round, 64, true)
            && Text(p.Gun.BehaveAs, 64, true) && Text(p.Gun.Casing, 64, true) && Text(p.Gun.Sound, 256, true) && Text(p.Gun.ImpactExplosion, 256, true));
    private static bool Lua(CustomCallbacks? c) => c == null || new[] { c.OnCalled, c.OnBeaconCreated, c.OnBeaconLanded, c.OnActivate, c.OnDelivered, c.Setup, c.ExtraFields }.All(b => Text(b, MaxLua, true));

    public static bool Valid(CustomContentSection c)
    {
        if (c.Version != CustomContentSection.CurrentVersion || c.Stratagems == null || c.Projectiles == null || c.Assignments == null || c.Images == null || c.Models == null
            || c.Stratagems.Count > MaxObjects || c.Projectiles.Count > MaxObjects || c.Assignments.Count > MaxObjects || c.Images.Count > MaxObjects * 2 || c.Models.Count > MaxObjects) return false;
        if (c.Stratagems.Any(s => s == null) || c.Projectiles.Any(p => p == null) || c.Assignments.Any(a => a == null) || c.Images.Any(i => i == null) || c.Models.Any(m => m == null)) return false;
        if (c.Stratagems.Select(s => s.Id).Distinct().Count() != c.Stratagems.Count || c.Projectiles.Select(p => p.Id).Distinct().Count() != c.Projectiles.Count
            || c.Images.Select(i => i.Id).Distinct().Count() != c.Images.Count || c.Assignments.Select(a => a.Id).Distinct().Count() != c.Assignments.Count
            || c.Models.Select(m => m.Id).Distinct().Count() != c.Models.Count) return false;
        foreach (var s in c.Stratagems)
        {
            var d = s.Delivery;
            if (!CustomIds.Valid(s.Id) || !Text(s.Name, 256) || !Text(s.NameCased, 256, true) || !Text(s.Description, 4000) || s.Icon != null && !CustomIds.ValidImage(s.Icon)
                || s.Code == null || s.Code.Count > 16 || s.Code.Any(x => x == null || x.Length > 16) || !Number(s.Cooldown) || !Names(s.Assets, 64, 256) || !Text(s.Notes, 4000, true)
                || s.Traits == null || s.Traits.Count > 16 || s.Traits.Any(t => t == null || t.Length > 256)
                || s.Carrier == null || !Text(s.Carrier.Group, 64, true) || !Text(s.Carrier.Beacon, 32, true) || !Names(s.Carrier.Prefer, 32, 64) || !Names(s.Carrier.Allow, 32, 64)
                || !Names(s.Carrier.Exclude, 128, 256)
                || d == null || !DeliveryFamily.All.Contains(d.Family)
                || d.Support != null && (!Text(d.Support.Item, 256) || !Weapon(d.Support.Weapon))
                || d.Pod != null && !Pod(d.Pod.Items, false)
                || d.Expendable != null && (!Text(d.Expendable.Weapon, 256) || !Weapon(d.Expendable.Modify) || !Presentation(d.Expendable.Presentation) || !Text(d.Expendable.Round, 256, true)
                    || !Text(d.Expendable.Level, 32, true) || !Pod(d.Expendable.Pod, true))
                || d.Variant != null && (!Text(d.Variant.Weapon, 256) || !Text(d.Variant.Round, 256, true) || !Presentation(d.Variant.Presentation)
                    || d.Variant.Model != null && !CustomIds.ValidImage(d.Variant.Model) || !Text(d.Variant.ModelUse, 32, true))
                || d.Sentry != null && (!Text(d.Sentry.Chassis, 256) || !Weapon(d.Sentry.Weapon))
                || d.Eagle != null && (!Text(d.Eagle.Strike, 256) || !Number(d.Eagle.RearmSeconds) || !Ref(d.Eagle.ImpactPayload))
                || d.Orbital != null && (!Text(d.Orbital.Mode, 16) || !Text(d.Orbital.Shell, 256, true) || !Text(d.Orbital.Pattern, 256, true) || !Ref(d.Orbital.ImpactPayload)
                    || !Number(d.Orbital.ShellInterval) || !Number(d.Orbital.SalvoInterval) || !Number(d.Orbital.Scatter) || !Number(d.Orbital.SalvoScatter))
                || !Pelican(d.Pelican)
                || d.Silo != null && (!Text(d.Silo.Donor, 256) || !Text(d.Silo.Blast, 256, true) || !Text(d.Silo.Fallback, 256, true))
                || d.Scripted != null && !Text(d.Scripted.VanillaPod, 256, true)
                || !Lua(s.Callbacks)) return false;
        }
        foreach (var p in c.Projectiles)
            if (!CustomIds.Valid(p.Id) || !Text(p.Name, 256) || !Text(p.Description, 4000, true) || !Text(p.Notes, 4000, true) || p.Base == null
                || p.Base.Length > 0 && !Output.IsMatch(p.Base) || new[] { p.Visual, p.Damage, p.ImpactExplosion, p.Ballistics }.Any(o => o != null && !Output.IsMatch(o))) return false;
        foreach (var a in c.Assignments)
            if (a.Id == Guid.Empty || a.HostKind is not (AttackOutputChange.PlayerHost or AttackOutputChange.SupportHost or AttackOutputChange.VehicleHost)
                || string.IsNullOrWhiteSpace(a.Weapon) || a.Weapon.Length > 256 || a.AttackRole == null || !Role.IsMatch(a.AttackRole) || a.Projectile is not { Kind: ContentRef.CustomProjectile } r || !CustomIds.Valid(r.Id)) return false;
        foreach (var i in c.Images)
            if (!CustomIds.ValidImage(i.Id) || i.Png == null || i.Png.Length > (MaxPngBytes + 2) / 3 * 4 || i.Sha256 == null || !Hex.IsMatch(i.Sha256)
                || i.Preparation is not (CustomImage.Auto or CustomImage.Raw) || !Text(i.SourceName, 256, true)) return false;
        foreach (var m in c.Models)
            if (!CustomIds.ValidImage(m.Id) || !Text(m.Base, 256) || m.All != null && !Colour.IsMatch(m.All) || m.Zones == null || m.Zones.Count > 8
                || m.Zones.Any(z => z.Key is not { Length: 1 } k || k[0] is < '0' or > '7' || z.Value == null || !Colour.IsMatch(z.Value)) || !Text(m.Description, 4000, true)) return false;
        return true;
    }
}
