using HD2RuntimeGUI.Core.Metadata;
using Xunit;

namespace HD2RuntimeGUI.Tests;

// The unreleased HD2Runtime development SDK (fixture 834716f) also changes the catalogs of other domains. ModBuilder binds it without
// inventing anything: new additive metadata is read, new targets that map onto published accessors are authored, and value types this
// build has no editor for (typed status references) stay visible but read-only with a reason.
public sealed class DevelopmentSdkTests
{
    [Fact] public async Task Status_references_are_shown_read_only_in_every_weapon_domain()
    {
        using var e = new TestEnvironment(); var (_, sdk) = await EnemyAuthoringTests.Fresh(e);
        var player = sdk.PlayerWeapons!.Weapons.SelectMany(w => w.Fields).Where(f => f.Type == WeaponCapability.StatusReference).ToArray();
        Assert.NotEmpty(player); Assert.All(player, f => { Assert.False(f.Editable); Assert.False(string.IsNullOrWhiteSpace(f.Reason)); Assert.NotEmpty(f.AllowedReferences!); });
        Assert.Contains(player, f => f.Reason == WeaponCapability.StatusReferenceReason); // published writable, kept read-only by this build
        var support = sdk.SupportAuthoring!.FieldInstances.Where(f => f.IsStatusReference).ToArray();
        Assert.NotEmpty(support); Assert.All(support, f => Assert.False(f.Writable));
        var vehicle = sdk.Entities!.VehicleWeapons!.FieldInstances.Where(f => f.IsStatusReference || f.ApiFieldConstant.Length == 0).ToArray();
        Assert.NotEmpty(vehicle); Assert.All(vehicle, f => Assert.False(f.Editable));
        // The other new player-weapon fields are published as ordinary writable fields.
        Assert.Contains(sdk.PlayerWeapons.Weapons.SelectMany(w => w.Fields), f => f.SemanticFieldId == "projectile.lifetime" && f.Editable);
    }

    [Fact] public async Task A_projectile_selector_is_writable_only_where_the_shot_fires_it_directly()
    {
        using var e = new TestEnvironment(); var (_, sdk) = await EnemyAuthoringTests.Fresh(e);
        // The AR-23 Liberator fires the projectile its default ammunition patches in: its base member is dormant, so the selector is read-only.
        var liberator = sdk.Composition!.Attack("AR-23 Liberator", "primary");
        Assert.False(liberator.WritableReferenceSwap); Assert.Contains("DORMANT_PROJECTILE_REFERENCE", liberator.Reason);
        Assert.Equal(sdk.PlayerWeapons!.Summary.Composition!.Projectile.ActiveSourceWritableTargetAttacks,
            sdk.Composition.Projectiles.Weapons.Sum(w => w.Attacks.Count(a => a.WritableReferenceSwap)));
    }

    [Fact] public async Task Sentry_turret_targeting_and_minefield_targets_use_the_published_accessors()
    {
        using var e = new TestEnvironment(); var (w, sdk) = await EnemyAuthoringTests.Fresh(e);
        var fields = sdk.Stratagems!.FieldInstances;
        await w.SetStratagemAsync(fields.Single(f => f.Target.Stratagem == "A/MG-43 Machine Gun Sentry" && f.SemanticFieldId == "turret.yaw_speed").InstanceKey, "120");
        await w.SetStratagemAsync(fields.Single(f => f.Target.Stratagem == "A/MG-43 Machine Gun Sentry" && f.SemanticFieldId == "targeting.range").InstanceKey, "90");
        await w.SetStratagemAsync(fields.Single(f => f.Target.Stratagem == "MD-6 Anti-Personnel Minefield" && f.SemanticFieldId == "minefield.salvos").InstanceKey, "4");
        await w.SetStratagemAsync(fields.Single(f => f.Target.Stratagem == "MD-6 Anti-Personnel Minefield" && f.Target.Attack == "mine" && f.SemanticFieldId == "explosion.inner_radius").InstanceKey, "2");
        Assert.Null(w.BuildError); var lua = w.LuaPreview;
        Assert.Contains("hd2.stratagem('A/MG-43 Machine Gun Sentry'):deployed_entity():turret()", lua);
        Assert.Contains("hd2.stratagem('A/MG-43 Machine Gun Sentry'):deployed_entity():targeting()", lua);
        Assert.Contains("hd2.stratagem('MD-6 Anti-Personnel Minefield'):deployed_entity():minefield()", lua);
        Assert.Contains("hd2.stratagem('MD-6 Anti-Personnel Minefield'):mine()", lua);
        await w.OpenAsync(w.Project!.Id); Assert.Null(w.BuildError);
    }
}
