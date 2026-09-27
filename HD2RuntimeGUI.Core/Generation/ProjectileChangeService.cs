using System.Security.Cryptography;
using System.Text;
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
    private static PlayerWeaponComposition Graph(SdkMetadata sdk) => sdk.Composition ?? throw new InvalidDataException("This pinned SDK has no projectile composition contract. Explicitly rebind to SDK 0.15 or newer.");
    private static ProjectileAttack Target(SdkMetadata sdk, string weapon, string role)
    {
        var a = Graph(sdk).Attack(weapon, role); var w = sdk.PlayerWeapons!.Weapon(weapon);
        var f = w.Fields.SingleOrDefault(f => f.Type == "projectile_reference" && f.ReferenceRole == role);
        if (w.OrdinaryWritesBlocked || !a.WritableReferenceSwap || !a.TargetOwnershipProven || a.TargetBacking?.UniqueOwner != true
            || f?.Editable != true || !f.WriteAccepted || f.AffectsMultipleWeapons || a.CompatibilityClass != "conventional_plain")
            throw new InvalidDataException(a.Reason ?? "Projectile selector is read-only, shared or ambiguous.");
        return a;
    }
    public IReadOnlyList<ProjectileReference> Sources(SdkMetadata sdk, string weapon, string role)
    {
        ProjectileAttack target;
        try { target = Target(sdk, weapon, role); } catch (InvalidDataException) { return []; }
        return Graph(sdk).Projectiles.CompatibleSources!.Where(s =>
        {
            var a = Graph(sdk).Attack(s.Weapon, s.Role);
            return a.SourceIdentityResolvable && a.ProjectileSettings != null && a.CompatibilityClass == target.CompatibilityClass
                && !sdk.PlayerWeapons!.Weapon(s.Weapon).OrdinaryWritesBlocked;
        }).Select(s => new ProjectileReference(s.Weapon, s.Role)).OrderBy(s => s.Weapon, StringComparer.Ordinal).ThenBy(s => s.AttackRole, StringComparer.Ordinal).ToArray();
    }
    public bool IsBaseline(SdkMetadata sdk, string weapon, string role, ProjectileReference replacement)
    {
        var g = Graph(sdk); var a = g.Attack(weapon, role); var b = g.Attack(replacement.Weapon, replacement.AttackRole);
        return a.ProjectileType == b.ProjectileType && a.ProjectileSettings?.SettingsType == b.ProjectileSettings?.SettingsType;
    }
    private static string Evidence(SdkMetadata sdk, ProjectileReference reference)
    {
        var a = Graph(sdk).Attack(reference.Weapon, reference.AttackRole);
        var identity = string.Join("\n", sdk.PlayerWeapons!.Weapon(reference.Weapon).Resources.Order(StringComparer.Ordinal)) + "\n" + a.ProjectileType.ToString(System.Globalization.CultureInfo.InvariantCulture) + "\n" + a.ProjectileSettings?.SettingsType + "\n" + a.CompatibilityClass;
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity))).ToLowerInvariant();
    }
    public ProjectileChange Create(SdkMetadata sdk, string weapon, string role, ProjectileReference replacement)
    {
        var a = Target(sdk, weapon, role);
        if (!Sources(sdk, weapon, role).Contains(replacement)) throw new InvalidDataException("The SDK does not approve this projectile source for this selector.");
        var expected = new ProjectileReference(weapon, role);
        return new() { Weapon = weapon, AttackRole = role, ExpectedProjectile = expected, ReplacementProjectile = replacement,
            CompatibilityClass = a.CompatibilityClass, ExpectedEvidence = Evidence(sdk, expected), ReplacementEvidence = Evidence(sdk, replacement), BaselineSdkVersion = sdk.Version };
    }
    public void Validate(SdkMetadata sdk, ProjectileChange c)
    {
        var a = Target(sdk, c.Weapon, c.AttackRole);
        if (c.SemanticFieldId != "attack.projectile" || c.ExpectedProjectile != new ProjectileReference(c.Weapon, c.AttackRole)) throw new InvalidDataException("Invalid semantic projectile selector.");
        if (!Sources(sdk, c.Weapon, c.AttackRole).Contains(c.ReplacementProjectile)) throw new InvalidDataException("Replacement projectile is no longer compatible. Reset or select a supported source.");
        if (c.CompatibilityClass != a.CompatibilityClass || c.ExpectedEvidence != Evidence(sdk, c.ExpectedProjectile) || c.ReplacementEvidence != Evidence(sdk, c.ReplacementProjectile))
            throw new InvalidDataException("Projectile baseline or compatibility evidence changed. Review and accept the current SDK baseline, or reset this replacement.");
    }
    public static IEnumerable<string> Operations(ModProject project, SdkMetadata sdk, IProjectileChangeService service)
    {
        if (project.ProjectileChanges.GroupBy(c => (c.Weapon, c.AttackRole)).Any(g => g.Count() > 1)) throw new InvalidDataException("Conflicting projectile overrides for one attack.");
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
