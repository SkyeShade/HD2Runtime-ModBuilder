using System.IO.Compression;
using System.Text;
using System.Text.Json.Nodes;
using HD2RuntimeGUI.Core.Generation;
using HD2RuntimeGUI.Core.Metadata;
using HD2RuntimeGUI.Core.Services;
using Xunit;

namespace HD2RuntimeGUI.Tests;

public sealed class Runtime019Tests
{
    private const string Concussive = "AR-23C Liberator Concussive", Eruptor = "R-36 Eruptor", Verdict = "P-113 Verdict", Jar = "JAR-5 Dominator";
    private static async Task<BuilderWorkspace> Workspace(TestEnvironment e, string name = "plans")
    {
        e.GitHub.Archive = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "sdk-0.19.0.zip")); var sdk = await e.Cache.InstallAsync(FakeGitHub.MakeRelease("0.19.0", e.GitHub.Archive)); var w = e.Workspace();
        await w.CreateAsync(new(name, "Tests", "mods/skyeshade/" + name.ToLowerInvariant(), "0.1.0"), sdk); return w;
    }
    private static IReadOnlyList<PlannedSemanticOperation> Plan(BuilderWorkspace w) => new SemanticOperationPlanner().Plan(w.Project!, w.Metadata!);
    private static async Task Damage(BuilderWorkspace w)
    {
        await w.SetObjectScalarAsync(Concussive, "primary", "projectile", null, "damage.push_force", "30", true);
        foreach (var lane in new[] { "direct", "slight", "large", "extreme" }) await w.SetObjectScalarAsync(Concussive, "primary", "projectile", null, "damage.ap_" + lane, "3", false);
    }
    private static Task Terminal(BuilderWorkspace w, string phase, bool acknowledge = true) => w.SetTerminalAsync(Concussive, "primary", phase, w.ExplosionSources.First(s => s.Projectile?.Weapon == Eruptor), acknowledge);
    private static int Count(string text, string token) => text.Split(token).Length - 1;
    [Fact] public async Task SDK019_loads_plan_contract_without_changing_catalogs()
    {
        using var e = new TestEnvironment(); var w = await Workspace(e); var sdk = w.Metadata!;
        Assert.Equal("0.19.0", sdk.Version); Assert.Equal("hd2.plan", sdk.Plans!.Api); Assert.Equal(64, sdk.Plans.Limits.Operations);
        Assert.Equal(3574, sdk.PlayerWeapons!.Summary.FieldInstances); Assert.Equal(30, sdk.PlayerHeat!.Summary.WritableFieldInstances);
        Assert.Equal(35, sdk.Advanced!.Support.Weapons.Count); Assert.Equal(419, sdk.PlayerWeapons.Summary.Composition!.Magazine.AttachmentOptionsMapped);
    }
    [Theory] [InlineData("schemaVersion", "2")] [InlineData("api", "\"hd2.unpublished\"")]
    [InlineData("targetFromPaths", "{}")] [InlineData("sharedScope.implicitCrossObjectAuthorization", "true")]
    [InlineData("limits.phases", "999")] [InlineData("safety.writes", "1")]
    public void Unsupported_or_unsafe_plan_contract_is_rejected(string property, string value)
    {
        var node = JsonNode.Parse(SdkCache.BundledComposition()[CompositionPlanCapabilitiesReader.FileName])!;
        var path = property.Split('.'); (path.Length == 2 ? node[path[0]]! : node)[path[^1]] = JsonNode.Parse(value);
        var error = Record.Exception(() => new CompositionPlanCapabilitiesReader().Read(Encoding.UTF8.GetBytes(node.ToJsonString())));
        Assert.True(error is InvalidDataException or UnsupportedSdkException);
    }
    [Fact] public async Task Missing_plan_contract_rejects_019_install_and_preserves_cache()
    {
        using var e = new TestEnvironment(); using var output = new MemoryStream();
        using (var zip = new ZipArchive(output, ZipArchiveMode.Create, true))
        {
            using var fixture = ZipFile.OpenRead(Path.Combine(AppContext.BaseDirectory, "Fixtures", "sdk-0.19.0.zip"));
            foreach (var entry in fixture.Entries.Where(e => e.FullName != CompositionPlanCapabilitiesReader.FileName))
            { using var source = entry.Open(); using var stream = zip.CreateEntry(entry.FullName).Open(); source.CopyTo(stream); }
        }
        e.GitHub.Archive = output.ToArray(); await Assert.ThrowsAsync<InvalidDataException>(() => e.Cache.InstallAsync(FakeGitHub.MakeRelease("0.19.0", e.GitHub.Archive)));
        Assert.Equal("0.5.1", (await e.Cache.GetCurrentAsync()).Version);
    }
    [Fact] public async Task Exact_Concussive_has_one_composition_plan_and_separate_weapon_patch()
    {
        using var e = new TestEnvironment(); var w = await Workspace(e, "ConcussiveComposition019"); await Damage(w);
        await Terminal(w, "impact"); await Terminal(w, "expiry", false);
        Assert.Null(w.BuildError); Assert.Equal(1, Count(w.LuaPreview, "plan={")); Assert.Equal(1, Count(w.LuaPreview, "hd2.ensure("));
        var ops = Plan(w); Assert.Equal(3, ops.Count); Assert.Equal(5, ops.Single(o => o.Changes.Count == 5).Changes.Count);
        Assert.Equal(2, ops.Where(o => o.Family.StartsWith("terminal:")).Count());
        Assert.Single(ops.Where(o => o.Family.StartsWith("terminal:")).Select(o => o.Owner).Distinct());
        await w.SetWeaponChangeAsync(Concussive, "weapon.fire_rate", "1100", false);
        Assert.Equal(2, Count(w.LuaPreview, "hd2.ensure(")); Assert.Equal(1, Count(w.LuaPreview, "plan={")); Assert.Contains("expect=400", w.LuaPreview);
        Assert.Equal(await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "Golden", "ConcussiveComposition019.lua")), w.LuaPreview);
        await w.ExportAsync(); Assert.True(File.Exists(w.LastExport));
    }
    [Fact] public async Task Physics_and_damage_are_two_objects_inside_one_plan()
    {
        using var e = new TestEnvironment(); var w = await Workspace(e);
        foreach (var (field, value) in new[] { ("projectile.velocity", "350"), ("projectile.drag", "0.1"), ("damage.ap_direct", "3"), ("damage.push_force", "30") })
            await w.SetObjectScalarAsync(Concussive, "primary", "projectile", null, field, value, true);
        Assert.Null(w.BuildError); var ops = Plan(w); Assert.Equal(2, ops.Count); Assert.All(ops, o => Assert.Equal(2, o.Changes.Count));
        Assert.NotEqual(ops[0].Owner, ops[1].Owner); Assert.Equal(1, Count(w.LuaPreview, "hd2.ensure(")); Assert.Contains("plan={", w.LuaPreview);
    }
    [Fact] public async Task Impact_and_expiry_and_physics_share_one_plan_without_racing_ensures()
    {
        using var e = new TestEnvironment(); var w = await Workspace(e); await Terminal(w, "impact"); await Terminal(w, "expiry", false);
        await w.SetObjectScalarAsync(Concussive, "primary", "projectile", null, "projectile.velocity", "350", true);
        Assert.Null(w.BuildError); Assert.Equal(3, Plan(w).Count); Assert.Single(Plan(w).Select(o => o.Owner).Distinct());
        Assert.Equal(1, Count(w.LuaPreview, "hd2.ensure(")); Assert.Contains("terminal_action('impact')", w.LuaPreview); Assert.Contains("terminal_action('expiry')", w.LuaPreview);
    }
    [Fact] public async Task Explosion_settings_and_explosion_damage_are_separate_operations_in_one_plan()
    {
        using var e = new TestEnvironment(); var w = await Workspace(e);
        foreach (var (field, value) in new[] { ("outer_radius", "10"), ("inner_radius", "5"), ("damage.standard_damage", "500"), ("damage.ap_direct", "5") })
            await w.SetObjectScalarAsync(Eruptor, "primary", "explosion", "impact", "explosion.primary.impact." + field, value, false);
        Assert.Null(w.BuildError); Assert.Equal(2, Plan(w).Count); Assert.All(Plan(w), o => Assert.Equal(2, o.Changes.Count));
        Assert.Equal(1, Count(w.LuaPreview, "hd2.ensure(")); Assert.Contains("plan={", w.LuaPreview);
    }
    [Fact] public async Task Swap_then_replacement_physics_uses_fresh_target_from_and_matches_golden()
    {
        using var e = new TestEnvironment(); var w = await Workspace(e, "ProjectileSwapAndTune019");
        await w.SetProjectileAsync(Verdict, "primary", new(Jar, "primary"));
        await w.SetObjectScalarAsync(Verdict, "primary", "projectile", null, "projectile.velocity", "350", true);
        await w.SetObjectScalarAsync(Verdict, "primary", "projectile", null, "projectile.drag", "0.2", false);
        Assert.Null(w.BuildError); Assert.Equal(2, Plan(w).Count); Assert.Equal(1, Count(w.LuaPreview, "target_from={")); Assert.Contains("path='projectile'", w.LuaPreview);
        Assert.True(w.LuaPreview.IndexOf("hd2.fields.attack.projectile", StringComparison.Ordinal) < w.LuaPreview.IndexOf("target_from", StringComparison.Ordinal));
        Assert.Contains("expect=180,value=350", w.LuaPreview); Assert.Contains("expect=0,value=0.2", w.LuaPreview);
        Assert.Equal(await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "Golden", "ProjectileSwapAndTune019.lua")), w.LuaPreview);
        Assert.DoesNotContain("0x", w.LuaPreview); await w.ExportAsync();
    }
    [Fact] public async Task Swap_plus_physics_damage_and_both_terminals_resolve_new_object_in_second_phase()
    {
        using var e = new TestEnvironment(); var w = await Workspace(e);
        await w.SetProjectileAsync(Verdict, "primary", new(Jar, "primary"));
        await w.SetObjectScalarAsync(Verdict, "primary", "projectile", null, "projectile.velocity", "350", true);
        await w.SetObjectScalarAsync(Verdict, "primary", "projectile", null, "damage.ap_direct", "4", true);
        var source = w.ExplosionSources.First(s => s.Projectile?.Weapon == Eruptor);
        await w.SetTerminalAsync(Verdict, "primary", "impact", source, true); await w.SetTerminalAsync(Verdict, "primary", "expiry", source, false);
        Assert.Null(w.BuildError); Assert.Equal(1, Count(w.LuaPreview, "hd2.ensure(")); Assert.Equal(4, Count(w.LuaPreview, "target_from={"));
        Assert.Contains("path='terminal.impact'", w.LuaPreview); Assert.Contains("path='terminal.expiry'", w.LuaPreview);
        Assert.All(w.Project!.CompositionChanges, c => Assert.Equal(Jar, c.Target.Weapon));
    }
    [Fact] public async Task Approval_is_per_exact_scope_and_does_not_authorize_other_operations()
    {
        using var e = new TestEnvironment(); var w = await Workspace(e);
        await w.SetObjectScalarAsync(Concussive, "primary", "projectile", null, "damage.push_force", "30", false);
        await w.SetObjectScalarAsync(Concussive, "primary", "projectile", null, "damage.ap_direct", "3", false);
        await Terminal(w, "impact", false); Assert.NotNull(w.BuildError);
        var damage = w.Project!.CompositionChanges.First(c => c.Scalar != null);
        await w.SetCompositionApprovalAsync(damage.Id, true);
        Assert.All(w.Project.CompositionChanges.Where(c => c.Scalar != null), c => Assert.True(c.SharedAcknowledged));
        Assert.False(w.Project.CompositionChanges.Single(c => c.Kind == "terminal").SharedAcknowledged); Assert.NotNull(w.BuildError);
        await w.SetCompositionApprovalAsync(w.Project.CompositionChanges.Single(c => c.Kind == "terminal").Id, true); Assert.Null(w.BuildError);
        await w.SetCompositionApprovalAsync(damage.Id, false); Assert.NotNull(w.BuildError);
        Assert.All(w.Project.CompositionChanges.Where(c => c.Scalar != null), c => Assert.False(c.SharedAcknowledged));
    }
    [Fact] public async Task Rebind_changed_consumer_scope_invalidates_object_approval()
    {
        using var e = new TestEnvironment(); var w = await Workspace(e); await Damage(w); await Terminal(w, "impact");
        var sdk = w.Metadata!; var changed = sdk with { PlayerWeapons = sdk.PlayerWeapons! with { Weapons = sdk.PlayerWeapons!.Weapons.Select(x => x.Name != Concussive ? x : x with {
            Fields = x.Fields.Select(f => f.Domain != "damage" ? f : f with { WriteScope = "shared_changed_scope" }).ToArray() }).ToArray() } };
        Assert.Throws<InvalidDataException>(() => e.Generator.Generate(w.Project!, changed));
    }
    [Fact] public async Task Simple_scalars_keep_patch_or_transaction_and_unrelated_weapons_stay_independent()
    {
        using var e = new TestEnvironment(); var w = await Workspace(e); await w.SetWeaponChangeAsync(Concussive, "weapon.fire_rate", "1100", false);
        Assert.Contains("patch={", w.LuaPreview); Assert.DoesNotContain("plan={", w.LuaPreview);
        await Damage(w); Assert.Contains("transaction={", w.LuaPreview); Assert.DoesNotContain("plan={", w.LuaPreview);
        await w.SetObjectScalarAsync(Verdict, "primary", "projectile", null, "damage.ap_direct", "4", true);
        Assert.DoesNotContain("plan={", w.LuaPreview); Assert.Equal(3, Count(w.LuaPreview, "hd2.ensure("));
    }
    [Fact] public async Task Plan_reload_and_output_order_are_byte_stable_and_reset_removes_noops()
    {
        using var e = new TestEnvironment(); var w = await Workspace(e); await Damage(w); await Terminal(w, "impact"); await Terminal(w, "expiry", false);
        var lua = w.LuaPreview; w.Project!.CompositionChanges.Reverse(); await e.Store.SaveAsync(w.Project); await w.OpenAsync(w.Project.Id); Assert.Equal(lua, w.LuaPreview);
        await w.ExportAsync(); var bytes = await File.ReadAllBytesAsync(w.LastExport!); await w.ExportAsync(); Assert.Equal(bytes, await File.ReadAllBytesAsync(w.LastExport!));
        await w.SetObjectScalarAsync(Concussive, "primary", "projectile", null, "damage.push_force", "60", true);
        Assert.DoesNotContain(w.Project.CompositionChanges, c => c.Scalar?.SemanticFieldId == "damage.push_force"); Assert.Null(w.BuildError);
    }
    [Fact] public async Task Nonpersistent_composition_uses_hd2_plan()
    {
        using var e = new TestEnvironment(); var w = await Workspace(e); await Terminal(w, "impact"); await Terminal(w, "expiry", false);
        foreach (var c in w.Project!.CompositionChanges) c.EnsureEnabled = false;
        var lua = e.Generator.Generate(w.Project, w.Metadata!); Assert.Contains("return hd2.plan({", lua); Assert.DoesNotContain("hd2.ensure", lua);
    }
    [Fact] public async Task Weapon_scalar_sharing_a_selector_component_joins_the_plan()
    {
        using var e = new TestEnvironment(); var w = await Workspace(e);
        await w.SetProjectileAsync(Verdict, "primary", new(Jar, "primary"));
        await w.SetWeaponChangeAsync(Verdict, "weapon.fire_rate", "600", false);
        Assert.Null(w.BuildError); Assert.Single(Plan(w).Select(o => o.Owner).Distinct());
        Assert.Equal(1, Count(w.LuaPreview, "hd2.ensure(")); Assert.Contains("plan={", w.LuaPreview);
        Assert.Contains("hd2.fields.attack.projectile", w.LuaPreview); Assert.Contains("hd2.fields.weapon.fire_rate", w.LuaPreview);
    }
    [Fact] public async Task Mixed_persistence_and_published_plan_limits_fail_before_export()
    {
        using var e = new TestEnvironment(); var w = await Workspace(e); await Damage(w); await Terminal(w, "impact");
        var sdk = w.Metadata!; var limited = sdk with { Plans = sdk.Plans! with { Limits = sdk.Plans.Limits with { Operations = 1 } } };
        Assert.Contains("operation limit", Assert.Throws<InvalidDataException>(() => e.Generator.Generate(w.Project!, limited)).Message);
        w.Project!.CompositionChanges[0].EnsureEnabled = false;
        Assert.Contains("mixed persistence", Assert.Throws<InvalidDataException>(() => e.Generator.Generate(w.Project, sdk)).Message);
    }
    [Fact] public async Task Old_018_project_stays_blocked_until_explicit_rebind_preserving_semantic_values()
    {
        using var e = new TestEnvironment(); var current = await Workspace(e); var sdk = current.Metadata!;
        e.GitHub.Archive = await File.ReadAllBytesAsync(Path.Combine(AppContext.BaseDirectory, "Fixtures", "sdk-0.18.0.zip"));
        var old = await e.Cache.InstallAsync(FakeGitHub.MakeRelease("0.18.0", e.GitHub.Archive)); var w = e.Workspace();
        await w.CreateAsync(new("Old", "Tests", "mods/tests/oldplan", "0.1.0"), old); await Damage(w); await Terminal(w, "impact"); await Terminal(w, "expiry", false); Assert.NotNull(w.BuildError);
        File.WriteAllText(e.Paths.CachePath("current.json"), "{\"version\":\"0.19.0\"}");
        await w.OpenAsync(w.Project!.Id); Assert.Equal("0.18.0", w.Metadata!.Version); Assert.NotNull(w.BuildError);
        await w.RebindToInstalledSdkAsync(); Assert.Equal(sdk.Version, w.Metadata.Version); Assert.Null(w.BuildError);
        Assert.All(w.Project.CompositionChanges, c => Assert.Equal("0.18.0", c.BaselineSdkVersion)); Assert.Contains("expect=60,value=30", w.LuaPreview);
    }
}
