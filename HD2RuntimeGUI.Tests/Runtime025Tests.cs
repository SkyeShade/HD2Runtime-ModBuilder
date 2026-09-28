using System.IO.Compression;
using HD2RuntimeGUI.Core.Generation;
using HD2RuntimeGUI.Core.Metadata;
using HD2RuntimeGUI.Core.Services;
using Xunit;

namespace HD2RuntimeGUI.Tests;

// Runtime 0.25.0 (unpublished local test build, HD2Runtime 2eefaa9 sdk/), bound through the developer local-SDK override:
// natively traced boosters, resolved support-equipment call-ins and no_call_in equipment. The fixture holds the build's root
// metadata files; replace it with the published SDK zip once 0.25.0 is released.
public sealed class Runtime025Tests
{
    private const string Fixture = "sdk-0.25.0-local.zip";
    private const string C4 = "B/MD C4 Pack", Shotgun = "SG-88 Break-Action Shotgun", Shovel = "CQC-72 Entrenchment Tool";
    private static string FixturePath => Path.Combine(AppContext.BaseDirectory, "Fixtures", Fixture);
    private static (SdkCache Cache, BuilderWorkspace Workspace) Local(TestEnvironment e)
    {
        var cache = new SdkCache(e.Paths, e.Reader, e.GitHub) { LocalSdkPath = FixturePath }; var updates = new SdkUpdateService(cache, e.GitHub, e.Paths);
        return (cache, new BuilderWorkspace(e.Store, e.Projects, cache, updates, e.Changes, e.Generator, e.Exporter, e.Desktop, e.Desktop, e.Paths));
    }
    private static async Task<BuilderWorkspace> Fresh(TestEnvironment e, string resource = "mods/tests/local025")
    {
        var (_, w) = Local(e); await w.CheckUpdatesAsync();
        await w.CreateAsync(new("Local 0.25", "Tests", resource, "0.1.0"), w.SdkStatus!.Installed); return w;
    }
    private static BoosterCatalog Boosters(BuilderWorkspace w) => w.Metadata!.Entities!.Boosters!;
    private static EntityField Booster(BuilderWorkspace w, string booster, string path, string id) =>
        Boosters(w).FieldInstances.Single(f => f.Target.Booster == booster && f.Target.Path == path && f.SemanticFieldId == id);
    private static int Count(string text, string part) { var n = 0; for (var i = text.IndexOf(part, StringComparison.Ordinal); i >= 0; i = text.IndexOf(part, i + part.Length, StringComparison.Ordinal)) n++; return n; }

    [Fact] public async Task Local_025_sdk_binds_as_the_current_sdk_without_github_or_cache_writes()
    {
        using var e = new TestEnvironment(); var (cache, w) = Local(e); var before = Directory.GetFiles(e.Paths.Sdk, "*", SearchOption.AllDirectories);
        await w.CheckUpdatesAsync();
        Assert.Equal("0.25.0", w.SdkStatus!.Installed.Version); Assert.Equal(FixturePath, w.SdkStatus.LocalSource); Assert.False(w.SdkStatus.UpdateAvailable);
        Assert.Contains("Local SDK 0.25.0", w.SdkStatus.Message); Assert.Equal(0, e.GitHub.Checks);
        Assert.Equal(before, Directory.GetFiles(e.Paths.Sdk, "*", SearchOption.AllDirectories));
        Assert.False(Directory.Exists(Path.GetDirectoryName(e.Paths.SdkFile("0.25.0"))));
    }

    [Fact] public async Task Booster_catalog_v2_publishes_19_writable_boosters_and_42_fields()
    {
        using var e = new TestEnvironment(); var w = await Fresh(e); var c = Boosters(w);
        Assert.Equal("hd2runtime.booster.guarded_authoring.v2", c.Contract);
        Assert.Equal(20, c.Boosters.Length); Assert.Equal(19, c.Boosters.Count(b => b.Writable)); Assert.Equal(42, c.FieldInstances.Length);
        Assert.All(c.Boosters, b => Assert.Equal("RESOLVED", b.Identity.Status));
        Assert.Equal(["deployed_entity", "explosion", "granted_stratagem", "status_damage", "status_effect", "tuning"], c.FieldInstances.Select(f => f.Target.Path).Distinct().Order(StringComparer.Ordinal));
        Assert.All(c.FieldInstances, f => { Assert.Equal("allow_unverified_effect", f.Acknowledgement); Assert.True(f.Editable); });
        Assert.Equal(26, c.FieldInstances.Count(f => f.AllowSharedRequired));
        // Hellpod Space Optimization stays read-only: no scalar participates in its effect.
        var hellpod = c.Find("Hellpod Space Optimization")!;
        Assert.False(hellpod.Writable); Assert.Empty(hellpod.FieldInstanceKeys); Assert.NotEmpty(hellpod.BlockedFields);
        Assert.Throws<InvalidDataException>(() => new EntityChangeService().Create(w.Metadata!, "booster:hellpod-space-optimization:tuning:booster.x", "1"));
        await Assert.ThrowsAsync<InvalidDataException>(() => w.SetBoosterAcknowledgedAsync(hellpod.Name, true));
        // Published ranges on the natively traced targets; the 0.24.0 targets publish none.
        Assert.Equal(new EntityRange(0, 4, false, "Multiplies incoming damage; negative values would heal."), Booster(w, "Vitality Enhancement", "tuning", "booster.damage_taken_scale").Range);
        Assert.Null(Booster(w, "Armed Resupply Pods", "deployed_entity", "weapon.fire_rate").Range);
    }

    [Fact] public async Task Tuning_writes_are_range_checked_and_need_the_unverified_effect_acknowledgement()
    {
        using var e = new TestEnvironment(); var w = await Fresh(e); var vitality = Booster(w, "Vitality Enhancement", "tuning", "booster.damage_taken_scale");
        await Assert.ThrowsAsync<InvalidDataException>(() => w.SetEntityAsync(vitality.InstanceKey, "4.5"));
        await Assert.ThrowsAsync<InvalidDataException>(() => w.SetEntityAsync(vitality.InstanceKey, "-0.1"));
        Assert.Empty(w.Project!.EntityChanges);
        await w.SetEntityAsync(vitality.InstanceKey, "4"); await w.SetEntityAsync(vitality.InstanceKey, "0.5");
        Assert.Equal("Acknowledge the unverified booster effect before building.", w.BuildError); Assert.DoesNotContain("hd2.booster", w.LuaPreview);
        await w.SetBoosterAcknowledgedAsync("Vitality Enhancement", true); Assert.Null(w.BuildError);
        var lua = w.LuaPreview;
        Assert.Contains("target=hd2.booster('Vitality Enhancement'):tuning(),", lua); Assert.Contains("field=hd2.fields.booster.damage_taken_scale,", lua);
        Assert.Contains("expect=0.9,", lua); Assert.Contains("value=0.5,", lua); Assert.Contains("allow_unverified_effect=true,", lua); Assert.DoesNotContain("allow_shared", lua);
        // Integer tuning: whole numbers only, inside the published range.
        var budget = Booster(w, "Increased Reinforcement Budget", "tuning", "booster.reinforcements_per_player");
        await Assert.ThrowsAsync<InvalidDataException>(() => w.SetEntityAsync(budget.InstanceKey, "1.5"));
        await Assert.ThrowsAsync<InvalidDataException>(() => w.SetEntityAsync(budget.InstanceKey, "21"));
        await w.SetEntityAsync(budget.InstanceKey, "3");
    }

    [Fact] public async Task Explosion_status_damage_and_granted_stratagem_targets_generate_runtime_lua()
    {
        using var e = new TestEnvironment(); var w = await Fresh(e);
        await w.SetEntityAsync(Booster(w, "Firebomb Hellpods", "explosion", "explosion.inner_radius").InstanceKey, "3");
        await w.SetEntityAsync(Booster(w, "Firebomb Hellpods", "explosion", "explosion.outer_radius").InstanceKey, "5");
        await w.SetEntityAsync(Booster(w, "Firebomb Hellpods", "explosion", "explosion.damage.standard_damage").InstanceKey, "300");
        await w.SetEntityAsync(Booster(w, "Dead Sprint", "status_damage", "damage.standard_damage").InstanceKey, "2");
        await w.SetEntityAsync(Booster(w, "Surplus EAT Allocation", "granted_stratagem", "stratagem.max_uses").InstanceKey, "4");
        // Shared explosion and status-damage objects need allow_shared as well as allow_unverified_effect.
        Assert.NotNull(w.BuildError);
        foreach (var b in new[] { "Firebomb Hellpods", "Dead Sprint", "Surplus EAT Allocation" }) await w.SetBoosterAcknowledgedAsync(b, true);
        Assert.Null(w.BuildError);
        var lua = w.LuaPreview;
        Assert.Contains("hd2.booster('Firebomb Hellpods'):explosion()", lua);
        Assert.Contains("{field=hd2.fields.explosion.inner_radius,expect=2,value=3},", lua);
        Assert.Contains("field=hd2.fields.explosion.damage_standard_damage,", lua);
        Assert.Contains("hd2.booster('Dead Sprint'):status_damage()", lua); Assert.Contains("field=hd2.fields.damage.player_standard_damage,", lua);
        Assert.Contains("hd2.booster('Surplus EAT Allocation'):granted_stratagem()", lua); Assert.Contains("field=hd2.fields.stratagem.max_uses,", lua);
        Assert.Equal(3, Count(lua, "allow_shared=true,")); // Firebomb radii and damage are two Runtime operation groups, plus Dead Sprint.
        // Firebomb's radii and damage groups on one target form one hd2.plan (as Armed Resupply Pods did in 0.24.0).
        Assert.Equal(1, Count(lua, "plan={"));
    }

    [Fact] public async Task C4_is_linked_and_sg88_cqc72_are_standalone_no_call_in_equipment()
    {
        using var e = new TestEnvironment(); var w = await Fresh(e); var sdk = w.Metadata!;
        var link = sdk.SupportLinks!.ForStratagem(C4)!;
        Assert.Same(link, sdk.SupportLinks.ForWeapon(C4)); Assert.Equal("placed_item", link.Relationship.Special);
        Assert.Contains(link.Relationship.DeliveryGraph.Nodes, n => n.View == null && n.Node == "delivery:thrower");
        Assert.Equal(33, sdk.SupportLinks.ByWeapon.Count);
        foreach (var name in new[] { Shotgun, Shovel })
        {
            var weapon = sdk.SupportAuthoring!.Weapons.Single(x => x.Name == name);
            Assert.True(weapon.LinkedStratagem!.IsNoCallIn); Assert.Null(weapon.LinkedStratagem.Blocker);
            Assert.Equal("world_pickup", weapon.LinkedStratagem.NoCallIn!.Acquisition!.Kind);
            Assert.Null(sdk.SupportLinks.ForWeapon(name));
            // The catalog root is not a callable stratagem, so it is not listed; its equipment is listed once as standalone.
            Assert.False(StratagemCategories.Listed(sdk.Stratagems!.Root(name)!, sdk.Entities));
        }
        Assert.Empty(SupportEquipment.Unlinked(sdk)); Assert.Equal([Shovel, Shotgun], SupportEquipment.Standalone(sdk));
        // No duplicate support entries: each support weapon appears exactly once (merged call-in or standalone).
        var listedSupport = sdk.Stratagems!.Stratagems.Where(s => s.Family == "support" && StratagemCategories.Listed(s, sdk.Entities)).Select(s => s.Name).ToArray();
        Assert.Equal(sdk.SupportAuthoring!.Weapons.Length, listedSupport.Length + SupportEquipment.Standalone(sdk).Count);
        Assert.Empty(listedSupport.Intersect(SupportEquipment.Standalone(sdk)));
    }

    [Fact] public async Task Fresh_025_project_saves_reloads_and_exports()
    {
        using var e = new TestEnvironment(); var w = await Fresh(e);
        await w.SetEntityAsync(Booster(w, "Stamina Enhancement", "tuning", "booster.stamina_scale").InstanceKey, "2");
        await w.SetBoosterAcknowledgedAsync("Stamina Enhancement", true);
        await w.SetWeaponChangeAsync("AR-23 Liberator", "weapon.fire_rate", "700", false);
        Assert.Equal("0.25.0", w.Project!.SdkVersion); Assert.Null(w.BuildError);
        var lua = w.LuaPreview; var id = w.Project.Id;
        await w.OpenAsync(id); Assert.Equal(lua, w.LuaPreview); Assert.Equal(2, w.Project!.EntityChanges.Count + w.Project.WeaponChanges.Count);
        w.Project.ExportDirectory = e.Paths.Exports; await w.ExportAsync();
        using var archive = ZipFile.OpenRead(w.LastExport!);
        Assert.Contains(archive.Entries, x => { using var r = new StreamReader(x.Open()); return r.ReadToEnd().Contains("hd2.booster('Stamina Enhancement'):tuning()", StringComparison.Ordinal); });
    }

    [Fact] public async Task Existing_024_project_still_opens_and_rebinds_to_the_local_025_sdk()
    {
        using var e = new TestEnvironment(); await SdkFixtures.Install(e, "0.24.0");
        var old = e.Workspace(); await old.CreateAsync(new("Old 0.24", "Tests", "mods/tests/old024", "0.1.0"), await e.Cache.GetVersionAsync("0.24.0"));
        await old.SetWeaponChangeAsync("AR-23 Liberator", "weapon.fire_rate", "700", false);
        var rate = old.Metadata!.Entities!.Boosters!.FieldInstances.Single(f => f.Target.Booster == "Armed Resupply Pods" && f.SemanticFieldId == "weapon.fire_rate");
        await old.SetEntityAsync(rate.InstanceKey, "900"); await old.SetBoosterAcknowledgedAsync("Armed Resupply Pods", true); Assert.Null(old.BuildError);
        var id = old.Project!.Id; var oldLua = old.LuaPreview;

        // With the local 0.25.0 override active, the 0.24.0 project still opens against its own cached SDK, unchanged.
        var (_, w) = Local(e); await w.CheckUpdatesAsync(); await w.OpenAsync(id);
        Assert.Equal("0.24.0", w.Project!.SdkVersion); Assert.Equal("0.24.0", w.Metadata!.Version); Assert.Equal(oldLua, w.LuaPreview);
        // Explicit rebind to the local SDK keeps both edits; Lua targets the same fields.
        await w.RebindToInstalledSdkAsync();
        Assert.Equal("0.25.0", w.Project!.SdkVersion); Assert.Equal(1, w.Project.WeaponChanges.Count); Assert.Single(w.Project.EntityChanges);
        if (w.BuildError != null)
        {
            // Changed booster evidence is surfaced for review, never silently accepted.
            Assert.Contains("capability", w.BuildError, StringComparison.OrdinalIgnoreCase);
            await w.SetEntityAsync(w.Project.EntityChanges[0].InstanceKey, "900", true); await w.SetBoosterAcknowledgedAsync("Armed Resupply Pods", true);
        }
        Assert.Null(w.BuildError);
        Assert.Contains("hd2.booster('Armed Resupply Pods'):deployed_entity()", w.LuaPreview); Assert.Contains("value=700,", w.LuaPreview);
        await w.OpenAsync(id); Assert.Equal("0.25.0", w.Project!.SdkVersion); Assert.Null(w.BuildError);
    }
}
