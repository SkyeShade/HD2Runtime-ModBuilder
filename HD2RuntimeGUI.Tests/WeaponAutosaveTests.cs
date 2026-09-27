using System.Text.Json;
using HD2RuntimeGUI.Core.Generation;
using HD2RuntimeGUI.Core.Metadata;
using HD2RuntimeGUI.Core.Services;
using HD2RuntimeGUI.Core.Storage;
using Xunit;

namespace HD2RuntimeGUI.Tests;

public sealed class WeaponAutosaveTests
{
    private const string Weapon = "AR-23C Liberator Concussive";
    private const string Rate = "weapon.fire_rate";
    private static async Task<BuilderWorkspace> Workspace(TestEnvironment env)
    {
        // Install the published offline fixture into this isolated test cache.
        await JsonStorage.WriteAtomicBytesAsync(env.Paths.SdkFile("0.13.0"), SdkCache.BundledMetadata());
        await JsonStorage.WriteAtomicBytesAsync(env.Paths.CachePath("0.13.0", PlayerWeaponCatalogReader.FileName), SdkCache.BundledCapabilities());
        var workspace = env.Workspace();
        await workspace.CreateAsync(new("Autosave", "Tests", "mods/tests/autosave", "0.1.0"), await env.Cache.GetVersionAsync("0.13.0"));
        return workspace;
    }
    [Fact] public async Task Vanilla_to_modified_creates_and_persists_override()
    {
        using var env = new TestEnvironment(); var w = await Workspace(env);
        await w.SetWeaponChangeAsync(Weapon, Rate, "1100", false);
        var change = Assert.Single((await env.Store.LoadAsync(w.Project!.Id)).WeaponChanges);
        Assert.Equal(400, change.ExpectedValue.GetDouble()); Assert.Equal(1100, change.DesiredValue.GetDouble());
        Assert.Contains("value=1100", w.LuaPreview);
    }
    [Fact] public async Task Another_modified_value_updates_identity_and_keeps_expected_baseline()
    {
        using var env = new TestEnvironment(); var w = await Workspace(env);
        await w.SetWeaponChangeAsync(Weapon, Rate, "1100", false); var id = w.Project!.WeaponChanges[0].Id;
        await w.SetWeaponChangeAsync(Weapon, Rate, "1200", false);
        var change = Assert.Single((await env.Store.LoadAsync(w.Project.Id)).WeaponChanges);
        Assert.Equal(id, change.Id); Assert.Equal(400, change.ExpectedValue.GetDouble()); Assert.Equal(1200, change.DesiredValue.GetDouble());
    }
    [Fact] public async Task Returning_to_vanilla_removes_override_and_survives_reload()
    {
        using var env = new TestEnvironment(); var w = await Workspace(env);
        await w.SetWeaponChangeAsync(Weapon, Rate, "1100", false);
        await w.SetWeaponChangeAsync(Weapon, Rate, "400.0000000", false);
        var id = w.Project!.Id; w.CloseProject(); await w.OpenAsync(id);
        Assert.Empty(w.Project!.WeaponChanges); Assert.Empty((await env.Store.LoadAsync(id)).WeaponChanges);
        Assert.DoesNotContain("hd2.ensure", w.LuaPreview);
    }
    [Theory] [InlineData("1")] [InlineData("1.0")] [InlineData("1.0000000")] [InlineData("1.00000001")]
    public async Task Float32_equivalent_spellings_do_not_create_overrides(string value)
    {
        using var env = new TestEnvironment(); var w = await Workspace(env);
        await w.SetWeaponChangeAsync("P-113 Verdict", "projectile.gravity", value, false);
        Assert.Empty((await env.Store.LoadAsync(w.Project!.Id)).WeaponChanges);
    }
    [Fact] public async Task Float32_baseline_roundtrip_and_integer_comparison_are_type_correct()
    {
        using var env = new TestEnvironment(); var w = await Workspace(env);
        await w.SetWeaponChangeAsync("P-113 Verdict", "projectile.drag", "1.2", false);
        Assert.Empty(w.Project!.WeaponChanges); // SDK baseline is 1.2000000476837158.
        var integer = w.Metadata!.PlayerWeapons!.Field("P-113 Verdict", "damage.ap_direct");
        using var spelledInteger = JsonDocument.Parse("3.000");
        Assert.True(WeaponScalar.Equal(integer, JsonSerializer.SerializeToElement(3), spelledInteger.RootElement));
        await w.SetWeaponChangeAsync("P-113 Verdict", "damage.ap_direct", "3.000", false);
        Assert.Empty(w.Project.WeaponChanges);
        await Assert.ThrowsAsync<InvalidDataException>(() => w.SetWeaponChangeAsync("P-113 Verdict", "damage.ap_direct", "3.00000000000000001", false));
        await Assert.ThrowsAsync<InvalidDataException>(() => w.SetWeaponChangeAsync("P-113 Verdict", "damage.ap_direct", "3.000000000000000000000000000000001", false));
    }
    [Fact] public async Task Boolean_false_to_true_then_false_removes_override()
    {
        using var env = new TestEnvironment(); var w = await Workspace(env);
        await w.SetWeaponChangeAsync(Weapon, "weapon.suppressed", "true", false);
        Assert.True(Assert.Single(w.Project!.WeaponChanges).DesiredValue.GetBoolean());
        await w.SetWeaponChangeAsync(Weapon, "weapon.suppressed", "false", false);
        Assert.Empty((await env.Store.LoadAsync(w.Project.Id)).WeaponChanges);
    }
    [Theory] [InlineData("")] [InlineData("-")] [InlineData("1e")] [InlineData("false")] [InlineData("1e100")]
    public async Task Invalid_edits_leave_last_saved_value_untouched(string value)
    {
        using var env = new TestEnvironment(); var w = await Workspace(env);
        await w.SetWeaponChangeAsync(Weapon, Rate, "1100", false);
        await Assert.ThrowsAsync<InvalidDataException>(() => w.SetWeaponChangeAsync(Weapon, Rate, value, false));
        Assert.Equal(1100, Assert.Single((await env.Store.LoadAsync(w.Project!.Id)).WeaponChanges).DesiredValue.GetDouble());
    }
    [Fact] public async Task Reset_uses_baseline_removal_and_shared_gating_remains_explicit()
    {
        using var env = new TestEnvironment(); var w = await Workspace(env);
        await w.SetWeaponChangeAsync("AR-23 Liberator", "projectile.drag", "0.1", false);
        Assert.NotNull(w.BuildError); Assert.False(w.Project!.WeaponChanges[0].SharedAcknowledged);
        await w.SetWeaponChangeAsync("AR-23 Liberator", "projectile.drag", "0.1", true);
        Assert.Null(w.BuildError); Assert.Contains("allow_shared=true", w.LuaPreview);
        await w.ResetWeaponsAsync("AR-23 Liberator", "projectile.drag");
        Assert.Empty((await env.Store.LoadAsync(w.Project.Id)).WeaponChanges);
    }
    [Fact] public async Task Opening_older_project_cleans_noops_but_keeps_real_edits()
    {
        using var env = new TestEnvironment(); var w = await Workspace(env);
        var service = new WeaponChangeService();
        w.Project!.WeaponChanges.Add(service.Create(w.Metadata!, Weapon, Rate, "400", false));
        w.Project.WeaponChanges.Add(service.Create(w.Metadata!, Weapon, "weapon.suppressed", "false", false));
        w.Project.WeaponChanges.Add(service.Create(w.Metadata!, "P-113 Verdict", "projectile.drag", "0.1", false));
        await env.Store.SaveAsync(w.Project); var id = w.Project.Id;
        w.CloseProject(); await w.OpenAsync(id);
        Assert.Equal("projectile.drag", Assert.Single(w.Project!.WeaponChanges).SemanticFieldId);
        Assert.Single((await env.Store.LoadAsync(id)).WeaponChanges);
    }
    [Fact] public async Task Concurrent_field_commits_persist_both_changes()
    {
        using var env = new TestEnvironment(); var w = await Workspace(env);
        await Task.WhenAll(w.SetWeaponChangeAsync(Weapon, Rate, "1100", false),
            w.SetWeaponChangeAsync(Weapon, "weapon.suppressed", "true", false));
        Assert.Equal(2, (await env.Store.LoadAsync(w.Project!.Id)).WeaponChanges.Count);
    }
}
