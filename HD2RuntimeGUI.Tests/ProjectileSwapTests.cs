using HD2RuntimeGUI.Core.Models;
using HD2RuntimeGUI.Core.Services;
using Xunit;

namespace HD2RuntimeGUI.Tests;

// Swapping an attack's projectile while it has stat edits: the edits are kept on the new projectile or discarded in the same save, and
// edits left on a projectile the attack no longer fires are resolved in place instead of only blocking the build.
public sealed class ProjectileSwapTests
{
    private const string Verdict = "P-113 Verdict", Jar = "JAR-5 Dominator", Concussive = "AR-23C Liberator Concussive", Liberator = "AR-23 Liberator", Eruptor = "R-36 Eruptor";
    private static async Task<BuilderWorkspace> Fresh(TestEnvironment e)
    {
        var sdk = await SdkFixtures.Install(e, "0.27.0"); var w = e.Workspace();
        await w.CreateAsync(new("Swaps", "Tests", "mods/tests/swaps", "0.1.0"), sdk); return w;
    }
    private static async Task VerdictEdits(BuilderWorkspace w)
    {
        await w.SetObjectScalarAsync(Verdict, "primary", "projectile", null, "projectile.velocity", "350", true);
        await w.SetObjectScalarAsync(Verdict, "primary", "projectile", null, "damage.standard_damage", "200", true);
    }

    [Fact] public async Task Keeping_values_moves_every_stat_edit_to_the_new_projectile_in_one_save()
    {
        using var e = new TestEnvironment(); var w = await Fresh(e); await VerdictEdits(w);
        Assert.Equal(2, w.ObjectEditsOnCurrentProjectile(Verdict, "primary").Count);
        var result = await w.SwapProjectileAsync(Verdict, "primary", new(Jar, "primary"), keepValues: true);
        Assert.Equal(2, result.Kept); Assert.Empty(result.NotKept);
        Assert.Null(w.BuildError); Assert.Empty(w.OrphanedObjectEdits(Verdict, "primary"));
        var edits = w.ObjectEditsOnCurrentProjectile(Verdict, "primary");
        Assert.All(edits, c => Assert.Equal(new ProjectileReference(Jar, "primary"), c.Target));
        Assert.Equal(["350", "200"], edits.OrderBy(c => c.Scalar!.SemanticFieldId == "projectile.velocity" ? 0 : 1).Select(c => c.Scalar!.DesiredValue.GetRawText()));
        // Each kept value is measured against the new projectile's own baseline.
        var velocity = edits.Single(c => c.Scalar!.SemanticFieldId == "projectile.velocity");
        Assert.Equal(w.Metadata!.PlayerWeapons!.Field(Jar, "projectile.velocity").CurrentDefault.GetRawText(), velocity.Scalar!.ExpectedValue.GetRawText());
        var lua = w.LuaPreview; Assert.Contains("value=350", lua); Assert.Contains("hd2.weapon('JAR-5 Dominator'):attack('primary'):projectile()", lua);
        // The saved project reloads in the same state.
        await w.OpenAsync(w.Project!.Id); Assert.Null(w.BuildError); Assert.Equal(2, w.ObjectEditsOnCurrentProjectile(Verdict, "primary").Count);
    }

    [Fact] public async Task Discarding_removes_the_stat_edits_with_the_swap()
    {
        using var e = new TestEnvironment(); var w = await Fresh(e); await VerdictEdits(w);
        var result = await w.SwapProjectileAsync(Verdict, "primary", new(Jar, "primary"), keepValues: false);
        Assert.Equal(0, result.Kept); Assert.Empty(w.Project!.CompositionChanges); Assert.Single(w.Project.ProjectileChanges); Assert.Null(w.BuildError);
    }

    [Fact] public async Task A_kept_value_equal_to_the_new_base_is_not_written()
    {
        using var e = new TestEnvironment(); var w = await Fresh(e);
        var jarVelocity = w.Metadata!.PlayerWeapons!.Field(Jar, "projectile.velocity").CurrentDefault.GetRawText();
        await w.SetObjectScalarAsync(Verdict, "primary", "projectile", null, "projectile.velocity", jarVelocity, true);
        var result = await w.SwapProjectileAsync(Verdict, "primary", new(Jar, "primary"), keepValues: true);
        Assert.Equal(0, result.Kept); Assert.Equal(1, result.AtBaseline); Assert.Empty(w.Project!.CompositionChanges); Assert.Null(w.BuildError);
    }

    [Fact] public async Task Terminal_and_explosion_edits_follow_the_projectile_too()
    {
        using var e = new TestEnvironment(); var w = await Fresh(e);
        await w.SetTerminalAsync(Concussive, "primary", "impact", w.ExplosionSources.First(s => s.Projectile?.Weapon == Eruptor), true);
        await w.SetObjectScalarAsync(Concussive, "primary", "explosion", "impact", "explosion.primary.impact.inner_radius", "3", true);
        await w.SetObjectScalarAsync(Concussive, "primary", "projectile", null, "projectile.velocity", "350", true);
        var result = await w.SwapProjectileAsync(Concussive, "primary", w.ProjectileSources(Concussive, "primary").First(s => s.Weapon == Liberator), keepValues: true);
        Assert.Equal(3, result.Kept); Assert.Null(w.BuildError);
        Assert.Equal(["explosion", "projectile", "terminal"], w.ObjectEditsOnCurrentProjectile(Concussive, "primary").Select(c => c.Kind).Order(StringComparer.Ordinal));
        Assert.Contains("hd2.fields.terminal.explosion", w.LuaPreview);
    }

    [Fact] public async Task Resetting_or_swapping_without_stat_edits_needs_no_decision()
    {
        using var e = new TestEnvironment(); var w = await Fresh(e);
        Assert.Empty(w.ObjectEditsOnCurrentProjectile(Verdict, "primary"));
        var result = await w.SwapProjectileAsync(Verdict, "primary", new(Jar, "primary"), keepValues: false);
        Assert.Equal(new ProjectileReference(Jar, "primary"), w.EffectiveProjectile(Verdict, "primary")); Assert.Empty(result.NotKept);
        await w.SwapProjectileAsync(Verdict, "primary", null, keepValues: false);
        Assert.Empty(w.Project!.ProjectileChanges);
    }

    [Fact] public async Task Orphaned_edits_from_an_earlier_swap_are_found_and_resolved_in_place()
    {
        using var e = new TestEnvironment(); var w = await Fresh(e); await VerdictEdits(w);
        // An older swap path (or a project saved by an older ModBuilder) leaves the edits on the Verdict projectile.
        await w.SetProjectileAsync(Verdict, "primary", new(Jar, "primary"));
        Assert.Equal(2, w.OrphanedObjectEdits(Verdict, "primary").Count); Assert.Equal(Verdict, w.WeaponWithOrphanedObjectEdits);
        Assert.Contains("no longer fires", w.BuildError); Assert.Contains("Projectile section", w.BuildError);
        var result = await w.ResolveOrphanedObjectEditsAsync(Verdict, "primary", keepValues: true);
        Assert.Equal(2, result.Kept); Assert.Empty(w.OrphanedObjectEdits(Verdict, "primary")); Assert.Null(w.BuildError); Assert.Null(w.WeaponWithOrphanedObjectEdits);
        // Discarding is the other in-place choice.
        await w.SetProjectileAsync(Verdict, "primary", null);
        Assert.Equal(2, w.OrphanedObjectEdits(Verdict, "primary").Count);
        await w.ResolveOrphanedObjectEditsAsync(Verdict, "primary", keepValues: false);
        Assert.Empty(w.Project!.CompositionChanges); Assert.Null(w.BuildError);
    }

    [Fact] public async Task Editing_a_value_on_the_current_projectile_supersedes_an_orphaned_edit_of_it()
    {
        using var e = new TestEnvironment(); var w = await Fresh(e); await VerdictEdits(w);
        await w.SetProjectileAsync(Verdict, "primary", new(Jar, "primary"));
        await w.SetObjectScalarAsync(Verdict, "primary", "projectile", null, "projectile.velocity", "800", true);
        var edits = w.Project!.CompositionChanges.Where(c => c.Scalar?.SemanticFieldId == "projectile.velocity").ToArray();
        Assert.Single(edits); Assert.Equal(new ProjectileReference(Jar, "primary"), edits[0].Target);
        Assert.Single(w.OrphanedObjectEdits(Verdict, "primary")); // the damage edit still waits for a decision
    }

    [Fact] public async Task Swapping_back_to_the_original_projectile_revives_its_edits()
    {
        using var e = new TestEnvironment(); var w = await Fresh(e); await VerdictEdits(w);
        await w.SetProjectileAsync(Verdict, "primary", new(Jar, "primary"));
        await w.SwapProjectileAsync(Verdict, "primary", null, keepValues: false); // nothing on the JAR projectile to decide about
        Assert.Empty(w.OrphanedObjectEdits(Verdict, "primary")); Assert.Equal(2, w.ObjectEditsOnCurrentProjectile(Verdict, "primary").Count); Assert.Null(w.BuildError);
    }
}
