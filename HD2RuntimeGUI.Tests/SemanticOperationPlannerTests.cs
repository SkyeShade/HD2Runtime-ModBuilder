using HD2RuntimeGUI.Core.Generation;
using HD2RuntimeGUI.Core.Metadata;
using HD2RuntimeGUI.Core.Services;
using Xunit;

namespace HD2RuntimeGUI.Tests;

public sealed class SemanticOperationPlannerTests
{
    private const string Concussive = "AR-23C Liberator Concussive", Eruptor = "R-36 Eruptor";
    private static async Task<BuilderWorkspace> Workspace(TestEnvironment e)
    {
        File.Delete(e.Paths.CachePath("current.json")); var sdk = await e.Cache.GetCurrentAsync(); var w = e.Workspace();
        await w.CreateAsync(new("Grouped", "Tests", "mods/tests/grouped", "0.1.0"), sdk); return w;
    }
    private static IReadOnlyList<PlannedSemanticOperation> Plan(BuilderWorkspace w) => new SemanticOperationPlanner().Plan(w.Project!, w.Metadata!);
    private static async Task Damage(BuilderWorkspace w)
    {
        await w.SetObjectScalarAsync(Concussive, "primary", "projectile", null, "damage.push_force", "30", true);
        foreach (var lane in new[] { "direct", "slight", "large", "extreme" })
            await w.SetObjectScalarAsync(Concussive, "primary", "projectile", null, "damage.ap_" + lane, "3", false);
    }
    [Fact] public async Task Concussive_damage_siblings_share_one_guarded_transaction_and_object_approval()
    {
        using var e = new TestEnvironment(); var w = await Workspace(e); await Damage(w);
        var op = Assert.Single(Plan(w)); Assert.Equal(5, op.Changes.Count); Assert.True(op.AllowShared);
        Assert.Equal(new[] { "hd2.fields.damage.ap_direct", "hd2.fields.damage.ap_extreme", "hd2.fields.damage.ap_large", "hd2.fields.damage.ap_slight", "hd2.fields.damage.push_force" }, op.Changes.Select(c => c.Field));
        Assert.Equal("60", op.Changes.Last().Expected); Assert.Equal("30", op.Changes.Last().Desired);
        Assert.Contains("transaction={", w.LuaPreview); Assert.Contains("changes={", w.LuaPreview);
        Assert.DoesNotContain("patches=", w.LuaPreview); Assert.Equal(1, w.LuaPreview.Split("hd2.ensure(").Length - 1);
        Assert.All(w.Project!.CompositionChanges, c => Assert.True(c.SharedAcknowledged));
        await w.OpenAsync(w.Project.Id); Assert.Null(w.BuildError); Assert.Single(Plan(w));
    }
    [Fact] public async Task Weapon_component_and_DamageInfo_are_independent_operations()
    {
        using var e = new TestEnvironment(); var w = await Workspace(e); await Damage(w);
        await w.SetWeaponChangeAsync(Concussive, "weapon.fire_rate", "1100", false);
        var plan = Plan(w); Assert.Equal(2, plan.Count);
        var weapon = Assert.Single(plan, p => p.Changes.Any(c => c.Field == "hd2.fields.weapon.fire_rate"));
        Assert.Equal("ProjectileWeaponComponentData", weapon.Owner.Kind); Assert.Single(weapon.Changes); Assert.False(weapon.AllowShared);
        Assert.Equal(2, w.LuaPreview.Split("hd2.ensure(").Length - 1);
    }
    [Fact] public async Task Exact_Concussive_Lua_matches_reviewed_transaction_golden()
    {
        using var e = new TestEnvironment(); var w = await Workspace(e);
        w.Project!.ResourceId = "mods/skyeshade/concussivegrouped";
        w.Project.ManagerGuid = HD2RuntimeGUI.Core.Projects.ProjectIdentity.ManagerGuid(w.Project.ResourceId);
        await Damage(w); await w.SetWeaponChangeAsync(Concussive, "weapon.fire_rate", "1100", false);
        var golden = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "Golden", "ConcussiveGrouped.lua"));
        Assert.Equal(golden.Replace("\r\n", "\n"), w.LuaPreview.Replace("\r\n", "\n"));
    }
    [Fact] public async Task ProjectileSettings_siblings_group_but_DamageInfo_keeps_its_own_approval()
    {
        using var e = new TestEnvironment(); var w = await Workspace(e);
        await w.SetObjectScalarAsync(Concussive, "primary", "projectile", null, "projectile.velocity", "350", true);
        await w.SetObjectScalarAsync(Concussive, "primary", "projectile", null, "projectile.drag", "0.1", false);
        Assert.Equal(2, Assert.Single(Plan(w)).Changes.Count);
        await w.SetObjectScalarAsync(Concussive, "primary", "projectile", null, "damage.push_force", "30", false);
        Assert.NotNull(w.BuildError);
        await w.SetObjectScalarAsync(Concussive, "primary", "projectile", null, "damage.push_force", "30", true);
        Assert.Equal(2, Plan(w).Count);
    }
    [Fact] public async Task Impact_and_expiry_share_an_owner_but_released_one_phase_target_cannot_group_them()
    {
        using var e = new TestEnvironment(); var w = await Workspace(e);
        var source = w.ExplosionSources.First(s => s.Projectile?.Weapon == Eruptor);
        await w.SetTerminalAsync(Concussive, "primary", "impact", source, true); Assert.Null(w.BuildError);
        await w.SetTerminalAsync(Concussive, "primary", "expiry", source, false);
        Assert.All(w.Project!.CompositionChanges, c => Assert.True(c.SharedAcknowledged));
        Assert.Contains("one target", w.BuildError); await Assert.ThrowsAsync<InvalidDataException>(w.ExportAsync);
        await w.RemoveCompositionAsync(w.Project.CompositionChanges.First(c => c.Phase == "impact").Id);
        Assert.Null(w.BuildError); Assert.Single(Plan(w)); Assert.Contains(":no_explosion()", w.LuaPreview);
    }
    [Fact] public async Task Terminal_plus_projectile_scalar_cannot_race_the_same_settings_record()
    {
        using var e = new TestEnvironment(); var w = await Workspace(e);
        await w.SetObjectScalarAsync(Concussive, "primary", "projectile", null, "projectile.velocity", "350", true);
        await w.SetTerminalAsync(Concussive, "primary", "impact", w.ExplosionSources.First(s => s.Projectile?.Weapon == Eruptor), true);
        Assert.Contains("one target", w.BuildError);
    }
    [Fact] public async Task Explosion_radii_and_linked_damage_form_separate_grouped_objects()
    {
        using var e = new TestEnvironment(); var w = await Workspace(e);
        foreach (var (field, value) in new[] { ("outer_radius", "10"), ("inner_radius", "5"), ("damage.standard_damage", "500"), ("damage.ap_direct", "5") })
            await w.SetObjectScalarAsync(Eruptor, "primary", "explosion", "impact", "explosion.primary.impact." + field, value, false);
        var plan = Plan(w); Assert.Equal(2, plan.Count); Assert.All(plan, p => Assert.Equal(2, p.Changes.Count));
        Assert.NotEqual(plan[0].Owner, plan[1].Owner); Assert.Equal(2, w.LuaPreview.Split("transaction={").Length - 1);
    }
    [Fact] public async Task Same_component_ignores_user_groups_and_different_components_do_not_merge()
    {
        using var e = new TestEnvironment(); var w = await Workspace(e);
        await w.SetWeaponChangeAsync(Concussive, "weapon.ergonomics", "30", false);
        await w.SetWeaponChangeAsync(Concussive, "weapon.sway", "2", false);
        w.Project!.WeaponChanges[0].Group = "First"; w.Project.WeaponChanges[1].Group = "Second";
        await w.SetWeaponChangeAsync(Concussive, "weapon.fire_rate", "1100", false);
        var plan = Plan(w); Assert.Equal(2, plan.Count); Assert.Equal(2, plan.Single(p => p.Owner.Kind == "WeaponDataComponentData").Changes.Count);
    }
    [Fact] public async Task Unrelated_weapon_objects_remain_independent_and_shared_objects_do_not_duplicate_jobs()
    {
        using var e = new TestEnvironment(); var w = await Workspace(e); await Damage(w);
        await w.SetObjectScalarAsync("P-113 Verdict", "primary", "projectile", null, "damage.ap_direct", "4", true);
        Assert.Equal(2, Plan(w).Count);
        var f = w.Metadata!.PlayerWeapons!.Field(Concussive, "damage.ap_direct");
        Assert.NotEqual(SemanticBackingObject.For(w.Metadata, Concussive, f), SemanticBackingObject.For(w.Metadata, "P-113 Verdict", w.Metadata.PlayerWeapons.Field("P-113 Verdict", "damage.ap_direct")));
    }
    [Fact] public async Task Approval_revocation_and_scope_change_invalidate_the_whole_group()
    {
        using var e = new TestEnvironment(); var w = await Workspace(e); await Damage(w);
        await w.SetObjectScalarAsync(Concussive, "primary", "projectile", null, "damage.push_force", "30", false);
        Assert.NotNull(w.BuildError); Assert.All(w.Project!.CompositionChanges, c => Assert.False(c.SharedAcknowledged));
        await w.SetObjectScalarAsync(Concussive, "primary", "projectile", null, "damage.ap_direct", "3", true); Assert.Null(w.BuildError);
        var sdk = w.Metadata!; var changed = sdk with { PlayerWeapons = sdk.PlayerWeapons! with { Weapons = sdk.PlayerWeapons!.Weapons.Select(w => w.Name != Concussive ? w : w with {
            Fields = w.Fields.Select(f => f.Domain != "damage" ? f : f with { WriteScope = "shared_changed_scope" }).ToArray() }).ToArray() } };
        Assert.Throws<InvalidDataException>(() => e.Generator.Generate(w.Project, changed));
    }
    [Fact] public async Task Reset_removes_override_and_reordering_does_not_change_Lua_or_ZIP()
    {
        using var e = new TestEnvironment(); var w = await Workspace(e); await Damage(w); var lua = w.LuaPreview;
        w.Project!.CompositionChanges.Reverse(); await e.Store.SaveAsync(w.Project); await w.OpenAsync(w.Project.Id);
        Assert.Equal(lua, w.LuaPreview); await w.ExportAsync(); var bytes = await File.ReadAllBytesAsync(w.LastExport!);
        await w.ExportAsync(); Assert.Equal(bytes, await File.ReadAllBytesAsync(w.LastExport!));
        await w.SetObjectScalarAsync(Concussive, "primary", "projectile", null, "damage.push_force", "60", true);
        Assert.Equal(4, Assert.Single(Plan(w)).Changes.Count); Assert.DoesNotContain(w.Project.CompositionChanges, c => c.Scalar?.SemanticFieldId == "damage.push_force");
    }
    [Fact] public async Task Mixed_persistence_on_one_object_is_blocked_instead_of_splitting_jobs()
    {
        using var e = new TestEnvironment(); var w = await Workspace(e); await Damage(w);
        w.Project!.CompositionChanges[0].EnsureEnabled = false;
        Assert.Contains("mixed persistence", Assert.Throws<InvalidDataException>(() => Plan(w)).Message);
    }
    [Theory] [InlineData("arc")] [InlineData("beam")] [InlineData("melee")]
    public async Task Non_projectile_families_keep_their_published_damage_target(string family)
    {
        using var e = new TestEnvironment(); var w = await Workspace(e);
        var weapon = w.Metadata!.PlayerWeapons!.Weapons.First(x => x.ImplementationFamilies.Contains(family) && x.Fields.Any(f => f.Editable && f.SemanticFieldId == "damage.ap_direct"));
        var f = weapon.Fields.Single(f => f.SemanticFieldId == "damage.ap_direct");
        await w.SetWeaponChangeAsync(weapon.Name, f.SemanticFieldId, (f.CurrentDefault.GetInt32() + 1).ToString(), true);
        Assert.Null(w.BuildError); Assert.Equal("hd2.weapon(" + LuaGenerator.Quote(weapon.Name) + ")", Assert.Single(Plan(w)).Target);
    }
    [Fact] public async Task Two_consumers_of_one_DamageInfo_share_one_job()
    {
        using var e = new TestEnvironment(); var w = await Workspace(e); var sdk = w.Metadata!;
        var candidates = sdk.PlayerWeapons!.Weapons.Where(x => sdk.Composition!.Projectiles.Weapons.Single(g => g.Weapon == x.Name).Attacks.Any(a => a.Role == "primary")
            && x.Fields.Any(f => f.Editable && f.SemanticFieldId == "damage.ap_direct"));
        var pair = candidates.GroupBy(x => SemanticBackingObject.For(sdk, x.Name, sdk.PlayerWeapons.Field(x.Name, "damage.ap_direct"))).First(g => g.Count() > 1).Take(2).ToArray();
        for (var i = 0; i < pair.Length; i++)
        {
            var id = i == 0 ? "damage.ap_direct" : "damage.ap_slight"; var field = sdk.PlayerWeapons.Field(pair[i].Name, id);
            await w.SetObjectScalarAsync(pair[i].Name, "primary", "projectile", null, id, (field.CurrentDefault.GetInt32() + 1).ToString(), true);
        }
        var op = Assert.Single(Plan(w)); Assert.Equal(2, op.Changes.Count); Assert.True(op.AllowShared);
    }
}
