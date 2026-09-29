using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using HD2RuntimeGUI.Core.Metadata;
using HD2RuntimeGUI.Core.Models;

namespace HD2RuntimeGUI.Core.Generation;

public interface ICompositionChangeService
{
    ProjectileReference EffectiveProjectile(ModProject project, string weapon, string role);
    IReadOnlyList<WeaponCapability> Fields(ModProject project, SdkMetadata sdk, string weapon, string role, string kind, string? phase);
    IReadOnlyList<ExplosionReference> ExplosionSources(SdkMetadata sdk);
    ExplosionReference EffectiveExplosion(ModProject project, SdkMetadata sdk, string weapon, string role, string phase);
    CompositionChange CreateScalar(ModProject project, SdkMetadata sdk, string weapon, string role, string kind, string? phase, string field, string value, bool acknowledge);
    CompositionChange CreateTerminal(ModProject project, SdkMetadata sdk, string weapon, string role, string phase, ExplosionReference desired, bool acknowledge);
    void Validate(ModProject project, SdkMetadata sdk, CompositionChange change);
    void ValidateComposition(ModProject project, SdkMetadata sdk);
    bool IsNoOp(SdkMetadata sdk, CompositionChange change);
}
public sealed class CompositionChangeService : ICompositionChangeService
{
    private readonly WeaponChangeService scalars = new();
    public ProjectileReference EffectiveProjectile(ModProject project, string weapon, string role) => project.ProjectileChanges.SingleOrDefault(c => c.Enabled && c.Weapon == weapon && c.AttackRole == role)?.ReplacementProjectile ?? new(weapon, role);
    public static bool ProjectileOwned(WeaponCapability f) => f.Domain is "projectile" or "damage" && f.Backing?.Branch != null;
    private static string Branch(string role) => role.StartsWith("feed_", StringComparison.Ordinal) ? role[5..] : role;
    public static TerminalAction Terminal(SdkMetadata sdk, ProjectileReference target, string phase) => sdk.Composition?.TerminalActions.Weapons.SingleOrDefault(w => w.Weapon == target.Weapon)?.Attacks.SingleOrDefault(a => a.Role == target.AttackRole)?.Actions.SingleOrDefault(a => a.Phase == phase) ?? throw new InvalidDataException("Terminal slot is unavailable.");
    private static ExplosionDescriptor Explosion(SdkMetadata sdk, ExplosionReference r)
    {
        if (r.IsNone) throw new InvalidDataException("None has no explosion fields.");
        var type = Terminal(sdk, r.Projectile!, r.Phase!).ReferenceType;
        return sdk.Advanced?.Explosions.Explosions.SingleOrDefault(e => e.ExplosionType == type) ?? throw new InvalidDataException("Typed explosion source is no longer available.");
    }
    public IReadOnlyList<ExplosionReference> ExplosionSources(SdkMetadata sdk) => [ExplosionReference.None, .. sdk.Advanced!.Explosions.Explosions
        .SelectMany(e => e.PlayerConsumers.Where(p => !sdk.PlayerWeapons!.Weapon(p.Weapon).OrdinaryWritesBlocked).SelectMany(p => sdk.Composition!.TerminalActions.Weapons.Single(w => w.Weapon == p.Weapon).Attacks.Where(a => a.Role == p.Role)
            .SelectMany(a => a.Actions.Where(a => a.LinkedExplosionRecord && a.ReferenceType == e.ExplosionType).Select(a => new ExplosionReference(new(p.Weapon, p.Role), a.Phase)))))
        .GroupBy(r => Explosion(sdk, r).ExplosionType).Select(g => g.OrderBy(r => r.Projectile!.Weapon, StringComparer.Ordinal).ThenBy(r => r.Phase == "impact" ? 0 : 1).First()).OrderBy(r => r.Label, StringComparer.Ordinal)];
    public static ExplosionReference BaselineExplosion(SdkMetadata sdk, ProjectileReference target, string phase) => Terminal(sdk, target, phase).ActionKind == "none" ? ExplosionReference.None : new(target, phase);
    public ExplosionReference EffectiveExplosion(ModProject project, SdkMetadata sdk, string weapon, string role, string phase) => project.CompositionChanges.SingleOrDefault(c => c.Enabled && c.Weapon == weapon && c.AttackRole == role && c.Kind == "terminal" && c.Phase == phase)?.DesiredExplosion ?? BaselineExplosion(sdk, EffectiveProjectile(project, weapon, role), phase);
    public IReadOnlyList<WeaponCapability> Fields(ModProject project, SdkMetadata sdk, string weapon, string role, string kind, string? phase)
    {
        var target = EffectiveProjectile(project, weapon, role);
        if (kind == "projectile") return sdk.PlayerWeapons!.Weapon(target.Weapon).Fields.Where(f => f.IsPreferred && ProjectileOwned(f) && f.Backing!.Branch == Branch(target.AttackRole)).ToArray();
        var explosion = EffectiveExplosion(project, sdk, weapon, role, phase!);
        if (explosion.IsNone) return [];
        var descriptor = Explosion(sdk, explosion);
        return sdk.PlayerWeapons!.Weapon(explosion.Projectile!.Weapon).Fields.Where(f => f.IsPreferred && f.Domain == "explosion" && (f.ExplosionType == descriptor.ExplosionType || f.SemanticFieldId.StartsWith("explosion." + Branch(explosion.Projectile.AttackRole) + "." + explosion.Phase + ".", StringComparison.Ordinal))).ToArray();
    }
    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    private static string ExplosionEvidence(SdkMetadata sdk, ExplosionReference r) => r.IsNone ? "none" : Hash(ProjectileChangeService.Evidence(sdk, r.Projectile!) + "\n" + JsonSerializer.Serialize(new { Explosion(sdk, r).ExplosionType, Explosion(sdk, r).DamageType }));
    // The explosion object without its source projectile's package-residency data (see ProjectileChangeService.Refresh).
    private static string ExplosionObjectEvidence(SdkMetadata sdk, ExplosionReference r) => r.IsNone ? "none" : Hash(ProjectileChangeService.ObjectEvidence(sdk, r.Projectile!) + "\n" + JsonSerializer.Serialize(new { Explosion(sdk, r).ExplosionType, Explosion(sdk, r).DamageType }));
    private static string? RefreshExplosion(string saved, SdkMetadata previous, SdkMetadata next, ExplosionReference? r)
    {
        if (r == null || r.IsNone) return null;
        try
        {
            var current = ExplosionEvidence(next, r);
            return saved != current && saved == ExplosionEvidence(previous, r) && ExplosionObjectEvidence(previous, r) == ExplosionObjectEvidence(next, r)
                && ProjectileChangeService.AssetsAutoLoaded(next, r.Projectile!) ? current : null;
        }
        catch (Exception e) when (e is InvalidDataException or InvalidOperationException or KeyNotFoundException) { return null; }
    }
    // Rebind: refresh target/reference evidence whose only difference between the SDKs is package-residency data.
    public static int Rebind(ModProject p, SdkMetadata previous, SdkMetadata next)
    {
        var refreshed = 0;
        if (previous.Composition == null || next.Composition == null) return 0;
        foreach (var c in p.CompositionChanges)
        {
            if (ProjectileChangeService.Refresh(c.TargetEvidence, previous, next, c.Target) is { } target) { c.TargetEvidence = target; refreshed++; }
            if (RefreshExplosion(c.ReferenceEvidence, previous, next, c.Kind == "terminal" ? c.ExpectedExplosion : c.ExplosionTarget) is { } reference) { c.ReferenceEvidence = reference; refreshed++; }
            if (c.Kind == "terminal" && RefreshExplosion(c.DesiredReferenceEvidence, previous, next, c.DesiredExplosion) is { } desired) { c.DesiredReferenceEvidence = desired; refreshed++; }
        }
        return refreshed;
    }
    private static string SharedEvidence(WeaponCapability f) => Hash(JsonSerializer.Serialize(new { f.WriteScope, f.SharedWithWeapons, f.SharedWithResources, f.Backing?.ConsumerCount, f.DynamicConsumersPossible }));
    public static string ApprovalScopeKey(SdkMetadata sdk, CompositionChange c) =>
        SemanticBackingObject.For(sdk, c.Scalar?.Weapon ?? c.Target.Weapon, Capability(sdk, c)) + "\n" + SharedEvidence(Capability(sdk, c));
    public static bool ApprovalCurrent(SdkMetadata sdk, CompositionChange c)
    {
        try { var f = c.Kind == "terminal" ? TerminalField(sdk, c.Target, c.Phase!) : sdk.PlayerWeapons!.Field(c.Scalar!.Weapon, c.Scalar.SemanticFieldId); return c.SharedAcknowledged && c.SharedEvidence == SharedEvidence(f); }
        catch (InvalidDataException) { return false; }
    }
    internal static WeaponCapability Capability(SdkMetadata sdk, CompositionChange c) => c.Scalar == null ? TerminalField(sdk, c.Target, c.Phase!) : sdk.PlayerWeapons!.Field(c.Scalar.Weapon, c.Scalar.SemanticFieldId);
    internal static bool SameApprovalScope(SdkMetadata sdk, CompositionChange a, CompositionChange b)
    {
        try
        {
            var af = Capability(sdk, a); var bf = Capability(sdk, b);
            return SemanticBackingObject.For(sdk, a.Scalar?.Weapon ?? a.Target.Weapon, af) == SemanticBackingObject.For(sdk, b.Scalar?.Weapon ?? b.Target.Weapon, bf)
                && SharedEvidence(af) == SharedEvidence(bf);
        }
        catch (InvalidDataException) { return false; } // Missing rebind evidence remains pending review.
    }
    public static bool HasObjectApproval(ModProject project, SdkMetadata sdk, string weapon, WeaponCapability field) => project.CompositionChanges.Any(c =>
    {
        try { var f = Capability(sdk, c); return c.Enabled && ApprovalCurrent(sdk, c) && c.TargetEvidence == ProjectileChangeService.Evidence(sdk, c.Target)
            && SemanticBackingObject.For(sdk, weapon, field) == SemanticBackingObject.For(sdk, c.Scalar?.Weapon ?? c.Target.Weapon, f) && SharedEvidence(field) == SharedEvidence(f); }
        catch (InvalidDataException) { return false; }
    });
    internal static CompositionChange WithApproval(SdkMetadata sdk, CompositionChange c, bool approved)
    {
        var copy = JsonSerializer.Deserialize<CompositionChange>(JsonSerializer.Serialize(c))!;
        var f = Capability(sdk, c); copy.SharedAcknowledged = approved; copy.SharedEvidence = approved ? SharedEvidence(f) : "";
        if (copy.Scalar is { } scalar)
        {
            scalar.SharedAcknowledged = approved; scalar.AcknowledgedWriteScope = approved ? f.WriteScope : null;
            scalar.AcknowledgedConsumerCount = approved ? f.Backing?.ConsumerCount : null;
            scalar.AcknowledgedAffectedWeapons = approved ? f.SharedWithWeapons.Order(StringComparer.Ordinal).ToList() : [];
        }
        return copy;
    }
    public static WeaponCapability TerminalField(SdkMetadata sdk, ProjectileReference target, string phase) => sdk.PlayerWeapons!.Weapon(target.Weapon).Fields.SingleOrDefault(f => f.Domain == "terminal" && f.ReferenceRole == target.AttackRole && f.ReferencePhase == phase) ?? throw new InvalidDataException("Terminal authoring capability missing.");
    private static void RequireModern(SdkMetadata sdk) { if (sdk.Advanced == null) throw new InvalidDataException("This operation requires the published 0.17 capability contracts."); }
    public CompositionChange CreateScalar(ModProject project, SdkMetadata sdk, string weapon, string role, string kind, string? phase, string field, string value, bool acknowledge)
    {
        RequireModern(sdk); var target = EffectiveProjectile(project, weapon, role);
        var f = Fields(project, sdk, weapon, role, kind, phase).SingleOrDefault(f => f.SemanticFieldId == field) ?? throw new InvalidDataException("Field does not belong to the selected composition object.");
        var explosion = kind == "explosion" ? EffectiveExplosion(project, sdk, weapon, role, phase!) : null;
        var source = explosion?.Projectile ?? target;
        var scalar = scalars.Create(sdk, source.Weapon, field, value, acknowledge);
        return new() { Weapon = weapon, AttackRole = role, Kind = kind, Phase = phase, Target = target, Scalar = scalar, ExplosionTarget = explosion,
            TargetEvidence = ProjectileChangeService.Evidence(sdk, target), ReferenceEvidence = explosion == null ? "" : ExplosionEvidence(sdk, explosion),
            BaselineSdkVersion = sdk.Version, SharedAcknowledged = acknowledge, SharedEvidence = SharedEvidence(f) };
    }
    public CompositionChange CreateTerminal(ModProject project, SdkMetadata sdk, string weapon, string role, string phase, ExplosionReference desired, bool acknowledge)
    {
        RequireModern(sdk); var target = EffectiveProjectile(project, weapon, role); var f = TerminalField(sdk, target, phase);
        if (!f.Editable || !f.WriteAccepted || !Terminal(sdk, target, phase).Writable || sdk.PlayerWeapons!.Weapon(target.Weapon).OrdinaryWritesBlocked) throw new InvalidDataException(f.Reason ?? "Terminal action is read-only.");
        if (!desired.IsNone && sdk.PlayerWeapons.Weapon(desired.Projectile!.Weapon).OrdinaryWritesBlocked) throw new InvalidDataException("Ambiguous explosion source identity.");
        if (!desired.IsNone && !ExplosionSources(sdk).Any(r => !r.IsNone && Explosion(sdk, r).ExplosionType == Explosion(sdk, desired).ExplosionType)) throw new InvalidDataException("Explosion source is not permitted.");
        var expected = BaselineExplosion(sdk, target, phase);
        return new() { Weapon = weapon, AttackRole = role, Kind = "terminal", Phase = phase, Target = target, ExpectedExplosion = expected, DesiredExplosion = desired,
            TargetEvidence = ProjectileChangeService.Evidence(sdk, target), ReferenceEvidence = ExplosionEvidence(sdk, expected), DesiredReferenceEvidence = ExplosionEvidence(sdk, desired),
            BaselineSdkVersion = sdk.Version, SharedAcknowledged = acknowledge, SharedEvidence = SharedEvidence(f) };
    }
    public bool IsNoOp(SdkMetadata sdk, CompositionChange c)
    {
        if (c.Scalar != null) return WeaponScalar.IsNoOp(sdk, c.Scalar);
        var baseline = BaselineExplosion(sdk, c.Target, c.Phase!);
        return baseline.IsNone && c.DesiredExplosion!.IsNone || !baseline.IsNone && !c.DesiredExplosion!.IsNone && Explosion(sdk, baseline).ExplosionType == Explosion(sdk, c.DesiredExplosion).ExplosionType;
    }
    public void Validate(ModProject project, SdkMetadata sdk, CompositionChange c)
    {
        RequireModern(sdk);
        if (EffectiveProjectile(project, c.Weapon, c.AttackRole) != c.Target) throw new InvalidDataException($"{c.Weapon} has edits on the {c.Target.Label}, which it no longer fires. Keep or discard them in its Projectile section.");
        if (c.TargetEvidence != ProjectileChangeService.Evidence(sdk, c.Target)) throw new InvalidDataException("Projectile object identity/residency changed. Review and reset or explicitly accept the new baseline.");
        WeaponCapability f;
        if (c.Kind == "terminal")
        {
            f = TerminalField(sdk, c.Target, c.Phase!);
            if (!f.Editable || !f.WriteAccepted || !Terminal(sdk, c.Target, c.Phase!).Writable || sdk.PlayerWeapons!.Weapon(c.Target.Weapon).OrdinaryWritesBlocked) throw new InvalidDataException("Terminal action is no longer writable.");
            if (!c.DesiredExplosion!.IsNone && sdk.PlayerWeapons.Weapon(c.DesiredExplosion.Projectile!.Weapon).OrdinaryWritesBlocked) throw new InvalidDataException("Ambiguous explosion source identity.");
            if (c.ExpectedExplosion != BaselineExplosion(sdk, c.Target, c.Phase!) || c.ReferenceEvidence != ExplosionEvidence(sdk, c.ExpectedExplosion!) || c.DesiredReferenceEvidence != ExplosionEvidence(sdk, c.DesiredExplosion!)) throw new InvalidDataException("Terminal reference baseline/source changed; review this change.");
        }
        else
        {
            f = Fields(project, sdk, c.Weapon, c.AttackRole, c.Kind, c.Phase).SingleOrDefault(f => f.SemanticFieldId == c.Scalar!.SemanticFieldId) ?? throw new InvalidDataException("Object field is no longer available.");
            var source = c.Kind == "explosion" ? EffectiveExplosion(project, sdk, c.Weapon, c.AttackRole, c.Phase!) : null;
            if (c.Scalar!.Weapon != (source?.Projectile ?? c.Target).Weapon || c.ExplosionTarget != source || source != null && c.ReferenceEvidence != ExplosionEvidence(sdk, source)) throw new InvalidDataException("Explosion/object target changed. Reset and edit the newly selected object.");
            var approved = HasObjectApproval(project, sdk, c.Scalar.Weapon, f);
            scalars.Validate(sdk, approved ? WithApproval(sdk, c, true).Scalar! : c.Scalar);
        }
        // Shared object writes are implicit (allow_shared is always emitted for them); the UI shows the affected consumers as a warning.
    }
    public void ValidateComposition(ModProject project, SdkMetadata sdk)
    {
        var swaps = project.ProjectileChanges.Where(c => c.Enabled).ToArray();
        foreach (var swap in swaps)
        {
            if (swaps.Any(s => s.Weapon == swap.ReplacementProjectile.Weapon && s.AttackRole == swap.ReplacementProjectile.AttackRole)) throw new InvalidDataException("Composition conflict: a replacement source is itself replaced by this project.");
            if (project.WeaponChanges.Any(c => c.Enabled && c.Weapon == swap.Weapon && sdk.PlayerWeapons?.FindCanonicalField(c.Weapon, c.SemanticFieldId) is { } f && ProjectileOwned(f))) throw new InvalidDataException("Composition target changed: remove the old weapon-level projectile overrides and edit the replacement object in Composition.");
        }
        foreach (var c in project.CompositionChanges.Where(c => c.Enabled))
        {
            Validate(project, sdk, c);
            if (c.Scalar != null && project.WeaponChanges.Any(w => w.Enabled && w.Weapon == c.Scalar.Weapon && sdk.PlayerWeapons!.FindCanonicalField(w.Weapon, w.SemanticFieldId)?.SemanticFieldId == c.Scalar.SemanticFieldId)) throw new InvalidDataException("An older weapon-level override also edits this object field. Remove it before using the Composition edit.");
            // Semantic handles follow live references. Do not generate a stale source chain.
            var source = c.Kind == "terminal" ? c.DesiredExplosion : c.ExplosionTarget;
            if (source?.Projectile is { } p && (swaps.Any(s => s.Weapon == p.Weapon && s.AttackRole == p.AttackRole)
                || project.CompositionChanges.Any(t => t.Enabled && t.Kind == "terminal" && t.Target == p && t.Phase == source.Phase))) throw new InvalidDataException("Composition conflict: the selected explosion source is modified by this project. Choose a stable source or separate these mods.");
        }
        if (project.CompositionChanges.GroupBy(c => (c.Weapon, c.AttackRole, c.Kind, c.Phase, c.Scalar?.SemanticFieldId)).Any(g => g.Count() > 1)) throw new InvalidDataException("Duplicate composition overrides.");
        foreach (var group in project.CompositionChanges.Where(c => c.Enabled && c.Scalar != null).GroupBy(c => {
            var f = sdk.PlayerWeapons!.Field(c.Scalar!.Weapon, c.Scalar.SemanticFieldId);
            return (f.Backing?.SettingsType, f.Backing?.Group, f.Backing?.RecordType, f.SemanticTarget);
        }))
            if (group.Select(c => sdk.PlayerWeapons!.Field(c.Scalar!.Weapon, c.Scalar.SemanticFieldId).Format(c.Scalar.DesiredValue)).Distinct().Count() > 1) throw new InvalidDataException("Conflicting changes to the same shared object field.");
        foreach (var group in project.CompositionChanges.Where(c => c.Enabled && c.Kind == "terminal").GroupBy(c => (sdk.Composition!.Attack(c.Target.Weapon, c.Target.AttackRole).ProjectileType, c.Phase)))
            if (group.Select(c => c.DesiredExplosion!.IsNone ? 0 : Explosion(sdk, c.DesiredExplosion).ExplosionType).Distinct().Count() > 1) throw new InvalidDataException("Conflicting changes to a shared terminal slot.");
    }
    public static string ProjectileLua(ProjectileReference p) => "hd2.weapon(" + LuaGenerator.Quote(p.Weapon) + "):attack(" + LuaGenerator.Quote(p.AttackRole) + "):projectile()";
    private static string TerminalLua(ProjectileReference p, string phase) => ProjectileLua(p) + ":terminal_action(" + LuaGenerator.Quote(phase) + ")";
    private static string ExplosionLua(ExplosionReference r, string terminal) => r.IsNone ? terminal + ":no_explosion()" : TerminalLua(r.Projectile!, r.Phase!) + ":explosion()";
    public static IEnumerable<string> Operations(ModProject project, SdkMetadata sdk, ICompositionChangeService service)
    {
        service.ValidateComposition(project, sdk);
        foreach (var c in project.CompositionChanges.Where(c => c.Enabled).OrderBy(c => c.Kind == "terminal" ? 0 : 1).ThenBy(c => c.Weapon, StringComparer.Ordinal).ThenBy(c => c.AttackRole).ThenBy(c => c.Phase).ThenBy(c => c.Scalar?.SemanticFieldId))
        {
            if (service.IsNoOp(sdk, c)) continue;
            var terminal = TerminalLua(c.Target, c.Phase ?? "impact");
            var f = c.Kind == "terminal" ? TerminalField(sdk, c.Target, c.Phase!) : sdk.PlayerWeapons!.Field(c.Scalar!.Weapon, c.Scalar.SemanticFieldId);
            var semantic = c.Kind == "terminal" ? "terminal.explosion" : Regex.Replace(f.SemanticFieldId, @"\.(primary|alternate|impact|expiry)(?=\.)", "");
            var parts = semantic.Split('.', 2); var key = parts[1].Replace('.', '_');
            if (sdk.Resources.Values.SelectMany(r => r.Fields.Values).Any(x => x.Domain == parts[0] && x.Name.Replace('.', '_') == key && x.Name != semantic)) key = "player_" + key;
            var target = c.Kind == "terminal" ? terminal : c.Kind == "explosion" ? ExplosionLua(c.ExplosionTarget!, terminal) : ProjectileLua(c.Target);
            var expected = c.Kind == "terminal" ? ExplosionLua(c.ExpectedExplosion!, terminal) : f.Format(c.Scalar!.ExpectedValue);
            var desired = c.Kind == "terminal" ? ExplosionLua(c.DesiredExplosion!, terminal) : f.Format(c.Scalar!.DesiredValue);
            var id = "gui-object-" + Hash(project.ResourceId + "\n" + c.Weapon + "\n" + c.AttackRole + "\n" + c.Kind + "\n" + c.Phase + "\n" + semantic)[..24];
            var body = "{\n    id=" + LuaGenerator.Quote(id) + ",\n    target=" + target + ",\n    field=hd2.fields." + parts[0] + "." + key + ",\n    expect=" + expected + ",\n    value=" + desired + ",\n" + (f.AffectsMultipleWeapons ? "    allow_shared=true,\n" : "") + "}";
            yield return c.EnsureEnabled ? "hd2.ensure({\n    patch=" + body.Replace("\n", "\n    ") + "\n})" : "hd2.patch(" + body + ")";
        }
    }
}
