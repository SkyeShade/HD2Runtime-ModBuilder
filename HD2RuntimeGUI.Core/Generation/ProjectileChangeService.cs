using System.Security.Cryptography;
using System.Text;
using HD2RuntimeGUI.Core.Localization;
using HD2RuntimeGUI.Core.Metadata;
using HD2RuntimeGUI.Core.Models;

namespace HD2RuntimeGUI.Core.Generation;

public interface IProjectileChangeService
{
    IReadOnlyList<ProjectileReference> Sources(SdkMetadata sdk, string weapon, string role);
    ProjectileChange Create(SdkMetadata sdk, string weapon, string role, ProjectileReference replacement);
    void Validate(SdkMetadata sdk, ProjectileChange change);
    bool IsBaseline(SdkMetadata sdk, string weapon, string role, ProjectileReference replacement);
}
public sealed class ProjectileChangeService : IProjectileChangeService
{
    private static PlayerWeaponComposition Graph(SdkMetadata sdk) => sdk.Composition ?? throw new InvalidDataException(CoreText.Get("Messages.Build.Projectile.SdkTooOld"));
    private static ProjectileAttack Target(SdkMetadata sdk, string weapon, string role)
    {
        var a = Graph(sdk).Attack(weapon, role); var w = sdk.PlayerWeapons!.Weapon(weapon);
        var f = w.Fields.SingleOrDefault(f => f.Domain == "attack" && f.ReferenceRole == role);
        if (w.OrdinaryWritesBlocked || !a.WritableReferenceSwap || !a.TargetOwnershipProven || a.TargetBacking?.UniqueOwner != true
            || f?.Editable != true || !f.WriteAccepted || f.AffectsMultipleWeapons || !(sdk.Advanced != null ? Graph(sdk).Projectiles.GuardPolicy?.ApprovedClasses?.Contains(a.CompatibilityClass) == true : a.CompatibilityClass == "conventional_plain"))
            throw new InvalidDataException(a.Reason ?? CoreText.Get("Messages.Build.Projectile.SelectorReadOnly"));
        return a;
    }
    public IReadOnlyList<ProjectileReference> Sources(SdkMetadata sdk, string weapon, string role)
    {
        ProjectileAttack target;
        try { target = Target(sdk, weapon, role); } catch (InvalidDataException) { return []; }
        return (Graph(sdk).Projectiles.CompatibleSources ?? Graph(sdk).Projectiles.CompatibleSourcesByClass?.GetValueOrDefault(target.CompatibilityClass) ?? []).Where(s =>
        {
            var a = Graph(sdk).Attack(s.Weapon, s.Role);
            return a.SourceIdentityResolvable && a.ProjectileSettings != null && a.CompatibilityClass == target.CompatibilityClass
                && !sdk.PlayerWeapons!.Weapon(s.Weapon).OrdinaryWritesBlocked && a.Residency?.Classification != "SOURCE_WEAPON_REQUIRED";
        }).Select(s => new ProjectileReference(s.Weapon, s.Role)).OrderBy(s => s.Weapon, StringComparer.Ordinal).ThenBy(s => s.AttackRole, StringComparer.Ordinal).ToArray();
    }
    public bool IsBaseline(SdkMetadata sdk, string weapon, string role, ProjectileReference replacement)
    {
        var g = Graph(sdk); var a = g.Attack(weapon, role); var b = g.Attack(replacement.Weapon, replacement.AttackRole);
        return a.ProjectileType == b.ProjectileType && a.ProjectileSettings?.SettingsType == b.ProjectileSettings?.SettingsType;
    }
    internal static string Evidence(SdkMetadata sdk, ProjectileReference reference)
    {
        var identity = ObjectIdentity(sdk, reference);
        if (Graph(sdk).Attack(reference.Weapon, reference.AttackRole).Residency is { } residency) identity += "\n" + residency.Classification;
        return Hash(identity);
    }
    // The projectile object itself (resources, type, settings, compatibility class), without how its assets become resident.
    private static string ObjectIdentity(SdkMetadata sdk, ProjectileReference reference)
    {
        var a = Graph(sdk).Attack(reference.Weapon, reference.AttackRole);
        return string.Join("\n", sdk.PlayerWeapons!.Weapon(reference.Weapon).Resources.Order(StringComparer.Ordinal)) + "\n" + a.ProjectileType.ToString(System.Globalization.CultureInfo.InvariantCulture) + "\n" + a.ProjectileSettings?.SettingsType + "\n" + a.CompatibilityClass;
    }
    internal static string ObjectEvidence(SdkMetadata sdk, ProjectileReference reference) => Hash(ObjectIdentity(sdk, reference));
    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    // Rebind: evidence recorded against the previous SDK stays valid on the next one when the object is unchanged and the only
    // difference is that Runtime now loads its assets automatically (SDK 0.27.0 PACKAGE_AUTO_LOADED). Any other residency change,
    // for example a newly published restriction, still needs review. Returns the refreshed value, or null to leave it.
    internal static string? Refresh(string saved, SdkMetadata previous, SdkMetadata next, ProjectileReference reference)
    {
        try
        {
            var current = Evidence(next, reference);
            return saved != current && saved == Evidence(previous, reference) && ObjectEvidence(previous, reference) == ObjectEvidence(next, reference)
                && AssetsAutoLoaded(next, reference) ? current : null;
        }
        catch (Exception e) when (e is InvalidDataException or InvalidOperationException or KeyNotFoundException) { return null; }
    }
    internal static bool AssetsAutoLoaded(SdkMetadata sdk, ProjectileReference reference) => Graph(sdk).Attack(reference.Weapon, reference.AttackRole).Residency is { AssetsAutoLoaded: true };
    public static int Rebind(ModProject p, SdkMetadata previous, SdkMetadata next)
    {
        var refreshed = 0;
        if (previous.Composition == null || next.Composition == null) return 0;
        foreach (var c in p.ProjectileChanges)
        {
            if (Refresh(c.ExpectedEvidence, previous, next, c.ExpectedProjectile) is { } expected) { c.ExpectedEvidence = expected; refreshed++; }
            if (Refresh(c.ReplacementEvidence, previous, next, c.ReplacementProjectile) is { } replacement) { c.ReplacementEvidence = replacement; refreshed++; }
        }
        return refreshed;
    }
    public ProjectileChange Create(SdkMetadata sdk, string weapon, string role, ProjectileReference replacement)
    {
        var a = Target(sdk, weapon, role);
        if (!Sources(sdk, weapon, role).Contains(replacement)) throw new InvalidDataException(CoreText.Get("Messages.Build.Projectile.SourceNotApproved"));
        var expected = new ProjectileReference(weapon, role);
        return new() { Weapon = weapon, AttackRole = role, ExpectedProjectile = expected, ReplacementProjectile = replacement,
            CompatibilityClass = a.CompatibilityClass, ExpectedEvidence = Evidence(sdk, expected), ReplacementEvidence = Evidence(sdk, replacement), BaselineSdkVersion = sdk.Version };
    }
    public void Validate(SdkMetadata sdk, ProjectileChange c)
    {
        var a = Target(sdk, c.Weapon, c.AttackRole);
        if (c.SemanticFieldId != "attack.projectile" || c.ExpectedProjectile != new ProjectileReference(c.Weapon, c.AttackRole)) throw new InvalidDataException(CoreText.Get("Messages.Build.Projectile.InvalidSelector"));
        if (!Sources(sdk, c.Weapon, c.AttackRole).Contains(c.ReplacementProjectile)) throw new InvalidDataException(CoreText.Get("Messages.Build.Projectile.NoLongerCompatible"));
        if (c.CompatibilityClass != a.CompatibilityClass || c.ExpectedEvidence != Evidence(sdk, c.ExpectedProjectile) || c.ReplacementEvidence != Evidence(sdk, c.ReplacementProjectile))
            throw new InvalidDataException(CoreText.Get("Messages.Build.Projectile.EvidenceChanged"));
    }
    public static IEnumerable<string> Operations(ModProject project, SdkMetadata sdk, IProjectileChangeService service)
    {
        if (project.ProjectileChanges.GroupBy(c => (c.Weapon, c.AttackRole)).Any(g => g.Count() > 1)) throw new InvalidDataException(CoreText.Get("Messages.Build.Projectile.Conflicting"));
        foreach (var c in project.ProjectileChanges.Where(c => c.Enabled).OrderBy(c => c.Weapon, StringComparer.Ordinal).ThenBy(c => c.AttackRole, StringComparer.Ordinal))
        {
            service.Validate(sdk, c);
            if (service.IsBaseline(sdk, c.Weapon, c.AttackRole, c.ReplacementProjectile)) continue;
            string Attack(ProjectileReference r) => "hd2.weapon(" + LuaGenerator.Quote(r.Weapon) + "):attack(" + LuaGenerator.Quote(r.AttackRole) + ")";
            var id = "gui-ref-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(project.ResourceId + "\n" + c.Weapon + "\n" + c.AttackRole)))[..24].ToLowerInvariant();
            var body = "{\n    id=" + LuaGenerator.Quote(id) + ",\n    target=" + Attack(c.ExpectedProjectile)
                + ",\n    field=hd2.fields.attack.projectile,\n    expect=" + Attack(c.ExpectedProjectile) + ":projectile(),\n    value=" + Attack(c.ReplacementProjectile) + ":projectile(),\n}";
            yield return c.EnsureEnabled ? "hd2.ensure({\n    patch=" + body.Replace("\n", "\n    ") + "\n})" : "hd2.patch(" + body + ")";
        }
    }
}
