using System.Globalization;
using HD2RuntimeGUI.Core.Generation;
using HD2RuntimeGUI.Core.Models;
using HD2RuntimeGUI.Core.Services;
using Xunit;

namespace HD2RuntimeGUI.Tests;

public sealed class ProjectileScalarRegressionTests
{
    private const string Weapon = "P-113 Verdict", Source = "JAR-5 Dominator";
    private static async Task<BuilderWorkspace> Workspace(TestEnvironment e)
    {
        File.Delete(e.Paths.CachePath("current.json"));
        var sdk = await e.Cache.GetCurrentAsync(); var w = e.Workspace();
        await w.CreateAsync(new("Projectile scalars", "Tests", "mods/tests/projectile_scalars", "0.1.0"), sdk);
        return w;
    }

    [Theory] [InlineData(false)] [InlineData(true)]
    public async Task Selected_projectile_keeps_all_published_physics_damage_and_penetration_controls(bool replacement)
    {
        using var e = new TestEnvironment(); var w = await Workspace(e);
        if (replacement) await w.SetProjectileAsync(Weapon, "primary", new(Source, "primary"));
        var selected = replacement ? Source : Weapon;
        Assert.Equal(new ProjectileReference(selected, "primary"), w.EffectiveProjectile(Weapon, "primary"));
        var fields = w.ObjectFields(Weapon, "primary", "projectile");
        foreach (var id in new[] { "projectile.velocity", "projectile.mass", "projectile.drag", "projectile.gravity", "projectile.pellet_count",
            "damage.standard_damage", "damage.durable_damage", "damage.ap_direct", "damage.ap_slight", "damage.ap_large", "damage.ap_extreme", "damage.demolition", "damage.stagger", "damage.push_force" })
        {
            var field = Assert.Single(fields, f => f.SemanticFieldId == id);
            Assert.True(field.Editable); Assert.Same(w.Metadata!.PlayerWeapons!.Field(selected, id), field);
        }
        Assert.Empty(w.Project!.CompositionChanges);
    }

    [Theory]
    [InlineData("projectile.velocity")] [InlineData("projectile.drag")]
    [InlineData("damage.standard_damage")] [InlineData("damage.ap_direct")]
    public async Task Selected_object_edit_requires_approval_generates_after_swap_and_resets_without_noop(string id)
    {
        using var e = new TestEnvironment(); var w = await Workspace(e);
        await w.SetProjectileAsync(Weapon, "primary", new(Source, "primary"));
        var field = w.ObjectFields(Weapon, "primary", "projectile").Single(f => f.SemanticFieldId == id);
        var desired = (field.CurrentDefault.GetDouble() + 1).ToString("R", CultureInfo.InvariantCulture);
        await w.SetObjectScalarAsync(Weapon, "primary", "projectile", null, id, desired, false);
        Assert.NotNull(w.BuildError); await Assert.ThrowsAsync<InvalidDataException>(w.ExportAsync);
        await w.SetObjectScalarAsync(Weapon, "primary", "projectile", null, id, desired, true);
        Assert.Contains("Composition dependency", w.BuildError); var saved = Assert.Single(w.Project!.CompositionChanges);
        Assert.Equal(Source, saved.Scalar!.Weapon); Assert.Equal(new(Source, "primary"), saved.Target);
        Assert.True(WeaponScalar.Equal(field, field.CurrentDefault, saved.Scalar.ExpectedValue));
        await Assert.ThrowsAsync<InvalidDataException>(w.ExportAsync);
        await w.SetObjectScalarAsync(Weapon, "primary", "projectile", null, id, field.Format(field.CurrentDefault), true);
        Assert.Empty(w.Project.CompositionChanges); await w.OpenAsync(w.Project.Id);
        Assert.Empty(w.Project.CompositionChanges); Assert.Single(w.Project.ProjectileChanges);
    }
}
