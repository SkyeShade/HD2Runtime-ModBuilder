using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using HD2RuntimeGUI.Core.Generation;
using HD2RuntimeGUI.Core.Metadata;
using HD2RuntimeGUI.Core.Models;
using HD2RuntimeGUI.Core.Services;
using HD2RuntimeGUI.Core.Storage;
using Xunit;

namespace HD2RuntimeGUI.Tests;

// Runtime 0.21 compatibility: these regressions run against the published 0.21.0 SDK archive, not the bundled current SDK.
public sealed class Runtime021Tests
{
    internal static async Task<SdkMetadata> Install021(TestEnvironment e)
    {
        e.GitHub.Archive = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "sdk-0.21.0.zip"));
        return await e.Cache.InstallAsync(FakeGitHub.MakeRelease("0.21.0", e.GitHub.Archive));
    }
    private static async Task<BuilderWorkspace> Workspace(TestEnvironment e)
    {
        var sdk = await Install021(e);
        var w = e.Workspace(); await w.CreateAsync(new("Stratagem", "Tests", "mods/tests/stratagem", "0.1.0"), sdk); return w;
    }
    private static StratagemField Field(BuilderWorkspace w, string name, string id, string? attack = null) => w.Metadata!.Stratagems!.FieldInstances.Single(f =>
        f.Target.Stratagem == name && f.SemanticFieldId == id && (attack == null || f.Target.Attack == attack));
    private static async Task Edit(BuilderWorkspace w, StratagemField f, string value, bool approve = true)
    { await w.SetStratagemAsync(f.InstanceKey, value); if (approve && f.Shared) await w.SetStratagemApprovalAsync(f.InstanceKey, true); }
    [Fact] public async Task Published_catalog_has_all_canonical_instances_roots_and_shared_scopes()
    {
        using var e = new TestEnvironment(); var w = await Workspace(e); var c = w.Metadata!.Stratagems!;
        Assert.Equal("0.21.0", w.Metadata.Version); Assert.Equal(55, c.Stratagems.Length);
        Assert.Equal(989, c.FieldInstances.Length); Assert.Equal(989, c.FieldInstances.Select(f => f.InstanceKey).Distinct().Count());
        Assert.Equal(936, c.FieldInstances.Count(f => f.Editable)); Assert.Equal(135, c.FieldInstances.Select(f => f.BackingObjectId).Distinct().Count());
        Assert.Equal(81, c.FieldInstances.Where(f => f.Shared).Select(f => f.BackingObjectId).Distinct().Count());
        Assert.Equal(20, c.Stratagems.Count(s => s.Family != "support")); Assert.Equal(12, c.Stratagems.Count(s => s.Family == "orbital"));
        Assert.Equal(8, c.Stratagems.Count(s => s.Family == "eagle")); Assert.Equal(35, c.Stratagems.Count(s => s.Family == "support"));
        Assert.Equal(33, c.Stratagems.Count(s => s.Family == "support" && s.RootResolution == "UNIQUE")); Assert.Equal(55, c.SemanticBranches.Length);
        Assert.Equal(143, c.Attacks.Length); Assert.Equal(53, c.FieldInstances.Count(f => f.SemanticFieldId == "stratagem.cooldown" && f.Editable));
        Assert.DoesNotContain(c.FieldInstances, f => f.SemanticFieldId == "stratagem.max_uses" && f.Editable);
    }
    [Fact] public async Task Every_writable_instance_generates_a_semantic_patch_and_readonly_rejects()
    {
        using var e = new TestEnvironment(); var w = await Workspace(e); var svc = new StratagemChangeService();
        foreach (var f in w.Metadata!.Stratagems!.FieldInstances)
        {
            if (!f.Editable) { Assert.Throws<InvalidDataException>(() => svc.Create(w.Metadata, f.InstanceKey, "1")); continue; }
            var c = svc.Create(w.Metadata, f.InstanceKey, JsonSerializer.Serialize(f.CurrentDefault.GetDouble() + 1));
            w.Project!.StratagemChanges = [c]; w.Project.StratagemApprovals[f.BackingObjectId] = StratagemChangeService.ApprovalEvidence(f);
            svc.Validate(w.Project, w.Metadata, c); var lua = e.Generator.Generate(w.Project, w.Metadata);
            Assert.Contains(f.ApiFieldConstant, lua); Assert.Contains(StratagemLua.Target(f.Target), lua); Assert.DoesNotContain(f.BackingObjectId, lua);
        }
    }
    [Fact] public async Task Orbital_Laser_cooldown_damage_and_ability_are_distinct_plan_operations()
    {
        using var e = new TestEnvironment(); var w = await Workspace(e);
        await Edit(w, Field(w, "Orbital Laser", "stratagem.cooldown"), "180"); Assert.Contains("patch={", w.LuaPreview);
        await Edit(w, Field(w, "Orbital Laser", "damage.standard_damage"), "400", false); Assert.NotNull(w.BuildError);
        await w.SetStratagemApprovalAsync(Field(w, "Orbital Laser", "damage.standard_damage").InstanceKey, true);
        await Edit(w, Field(w, "Orbital Laser", "orbital.duration"), "40");
        Assert.Null(w.BuildError); Assert.Contains("plan={", w.LuaPreview); Assert.Equal(1, w.LuaPreview.Split("hd2.ensure(").Length - 1);
        Assert.Equal(3, w.LuaPreview.Split("target=").Length - 1); Assert.Contains(":attack('beam_damage')", w.LuaPreview);
        Assert.Contains("expect=60", w.LuaPreview); Assert.Contains("value=400", w.LuaPreview);
        Assert.Contains("beam → damage", w.Metadata!.Stratagems!.BranchLabel(Field(w, "Orbital Laser", "damage.standard_damage")));
    }
    [Fact] public async Task Same_object_uses_one_transaction_and_deterministic_order()
    {
        using var e = new TestEnvironment(); var w = await Workspace(e);
        await Edit(w, Field(w, "Orbital Laser", "orbital.duration"), "40");
        await Edit(w, Field(w, "Orbital Laser", "orbital.movement_speed"), "15");
        Assert.Contains("transaction={", w.LuaPreview); Assert.DoesNotContain("plan={", w.LuaPreview);
        var first = w.LuaPreview; w.Project!.StratagemChanges.Reverse(); Assert.Equal(first, e.Generator.Generate(w.Project, w.Metadata!));
    }
    [Fact] public async Task Eagle_rearm_coalesces_across_handles_and_approval_is_per_object()
    {
        using var e = new TestEnvironment(); var w = await Workspace(e); var c = w.Metadata!.Stratagems!;
        var rearm = c.FieldInstances.Where(f => f.SemanticFieldId == "eagle.rearm_time").ToArray(); Assert.Equal(8, rearm.Length);
        Assert.Single(rearm.Select(f => f.BackingObjectId).Distinct()); Assert.All(rearm, f => Assert.Equal(8, f.SharedConsumers.Length));
        Assert.Equal(8, c.FieldInstances.Count(f => f.SemanticFieldId == "eagle.uses_per_rearm" && f.Editable));
        await Edit(w, rearm[0], "90", false); Assert.NotNull(w.BuildError);
        await w.SetStratagemApprovalAsync(rearm[1].InstanceKey, true);
        Assert.All(rearm, f => Assert.True(StratagemChangeService.Approved(w.Project!, f)));
        await Edit(w, rearm[2], "80", false); Assert.Single(w.Project!.StratagemChanges); Assert.Single(w.Project.StratagemApprovals);
        Assert.Equal(80, w.Project.StratagemChanges[0].DesiredValue.GetDouble()); Assert.Null(w.BuildError);
        await Edit(w, Field(w, "Orbital Laser", "damage.standard_damage"), "400", false); Assert.NotNull(w.BuildError);
        await w.SetStratagemAsync(rearm[3].InstanceKey, "150.000000"); Assert.Single(w.Project.StratagemChanges);
        Assert.Equal("Orbital Laser", w.Project.StratagemChanges[0].Stratagem);
    }
    [Theory] [InlineData("SG-88 Break-Action Shotgun")] [InlineData("CQC-72 Entrenchment Tool")]
    public async Task Unresolved_callins_are_visible_without_authoring(string name)
    {
        using var e = new TestEnvironment(); var w = await Workspace(e); var c = w.Metadata!.Stratagems!;
        var root = c.Stratagems.Single(s => s.Name == name); Assert.Equal("UNRESOLVED", root.RootResolution); Assert.NotNull(root.BlockedReason);
        Assert.False(root.CooldownCapability.Writable); Assert.DoesNotContain(c.FieldInstances, f => f.Target.Stratagem == name && f.Editable);
    }
    [Theory] [InlineData("GR-8 Recoilless Rifle")] [InlineData("MS-11 Solo Silo")] [InlineData("MG-43 Machine Gun")]
    public async Task Callin_cooldown_is_separate_from_weapon_authoring(string name)
    {
        using var e = new TestEnvironment(); var w = await Workspace(e); await Edit(w, Field(w, name, "stratagem.cooldown"), "90");
        Assert.Null(w.BuildError); Assert.Empty(w.Project!.SupportChanges); Assert.Contains("hd2.stratagem(", w.LuaPreview); Assert.DoesNotContain("hd2.support_weapon", w.LuaPreview);
    }
    [Fact] public async Task Autosave_exact_float_reset_reload_and_duplicate_preserve_intent()
    {
        using var e = new TestEnvironment(); var w = await Workspace(e); var f = Field(w, "Orbital Laser", "stratagem.cooldown");
        await Edit(w, f, "180"); var id = w.Project!.StratagemChanges.Single().Id;
        await Edit(w, f, "200"); Assert.Equal(id, w.Project.StratagemChanges.Single().Id);
        await w.OpenAsync(w.Project.Id); Assert.Equal(200, w.Project!.StratagemChanges.Single().DesiredValue.GetDouble());
        var copy = await e.Projects.DuplicateAsync(w.Project, new("Copy", "Tests", "mods/tests/stratagem_copy", "0.1.0")); Assert.Single(copy.StratagemChanges);
        await w.SetStratagemAsync(f.InstanceKey, "300.000000"); await w.OpenAsync(w.Project.Id); Assert.Empty(w.Project!.StratagemChanges);
        await Assert.ThrowsAsync<InvalidDataException>(() => w.SetStratagemAsync(f.InstanceKey, "1e")); Assert.Empty(w.Project.StratagemChanges);
        await Edit(w, f, "180"); await w.ResetStratagemAsync("Orbital Laser"); Assert.Empty(w.Project.StratagemChanges);
    }
    [Fact] public async Task Rebind_preserves_baseline_and_invalidates_changed_consumer_approval()
    {
        using var e = new TestEnvironment(); var w = await Workspace(e); var f = Field(w, "Orbital Laser", "damage.standard_damage"); await Edit(w, f, "400");
        var change = w.Project!.StratagemChanges.Single(); var svc = new StratagemChangeService();
        SdkMetadata Replace(StratagemField next) => w.Metadata! with { Stratagems = w.Metadata!.Stratagems! with { FieldInstances = w.Metadata.Stratagems.FieldInstances.Select(x => x.InstanceKey == f.InstanceKey ? next : x).ToArray() } };
        Assert.Throws<InvalidDataException>(() => svc.Validate(w.Project, Replace(f with { CurrentDefault = JsonSerializer.SerializeToElement(70) }), change));
        var changedScope = f with { SharedConsumers = [..f.SharedConsumers, new("New consumer", "attack")] };
        Assert.False(StratagemChangeService.Approved(w.Project, changedScope)); Assert.Throws<InvalidDataException>(() => svc.Validate(w.Project, Replace(changedScope), change));
        Assert.Throws<InvalidDataException>(() => svc.Validate(w.Project, Replace(f with { Editable = false }), change));
        await w.RebindToInstalledSdkAsync(); Assert.Equal(60, change.ExpectedValue.GetInt32()); Assert.Equal(400, change.DesiredValue.GetInt32()); Assert.Null(w.BuildError);
    }
    [Fact] public async Task Repeated_status_branches_remain_separate_and_unsafe_group_is_blocked()
    {
        using var e = new TestEnvironment(); var w = await Workspace(e);
        var f = w.Metadata!.Stratagems!.FieldInstances.Where(f => f.Target.Stratagem == "Orbital Napalm Barrage" && f.SemanticFieldId == "status.strength").Take(2).ToArray();
        Assert.NotEqual(f[0].CurrentDefault.GetDouble(), f[1].CurrentDefault.GetDouble());
        await Edit(w, f[0], "110"); await Edit(w, f[1], "55"); Assert.Equal(2, w.Project!.StratagemChanges.Count);
        Assert.Contains("different branch targets", w.BuildError); await Assert.ThrowsAsync<InvalidDataException>(() => w.ExportAsync());
    }
    [Theory] [InlineData("count")] [InlineData("duplicate")] [InlineData("target")] [InlineData("api")] [InlineData("phase")] [InlineData("scope")]
    public void Malformed_catalog_fails_closed(string fault)
    {
        var json = JsonNode.Parse(SdkCache.BundledComposition()[StratagemCatalogReader.FileName])!; var f = json["fieldInstances"]![0]!;
        switch (fault) {
            case "count": json["summary"]!["fieldInstances"] = 0; break;
            case "duplicate": json["fieldInstances"]!.AsArray().Add(f.DeepClone()); break;
            case "target": f["target"]!["path"] = "raw"; break;
            case "api": f["apiFieldConstant"] = "os.execute('bad')"; break;
            case "phase": f["planPhase"] = 2; break;
            case "scope": f["shared"] = true; break;
        }
        Assert.Throws<InvalidDataException>(() => new StratagemCatalogReader().Read(Encoding.UTF8.GetBytes(json.ToJsonString())));
    }
    [Fact] public async Task Export_is_deterministic_and_contains_semantic_source_only()
    {
        using var e = new TestEnvironment(); var w = await Workspace(e);
        await Edit(w, Field(w, "Orbital Laser", "stratagem.cooldown"), "180"); await Edit(w, Field(w, "Orbital Laser", "damage.standard_damage"), "400");
        var lua = w.LuaPreview; await w.ExportAsync(); var bytes = File.ReadAllBytes(w.LastExport!);
        await w.OpenAsync(w.Project!.Id); await w.ExportAsync(); Assert.Equal(bytes, File.ReadAllBytes(w.LastExport!)); Assert.Equal(lua, w.LuaPreview);
        Assert.DoesNotContain("0x", lua); Assert.DoesNotContain("offset", lua); Assert.DoesNotContain("backingObject", lua);
        using var zip = ZipFile.OpenRead(w.LastExport!); using var reader = new StreamReader(zip.GetEntry("src/addon.lua")!.Open()); Assert.Equal(lua, reader.ReadToEnd());
        Assert.Equal(8, zip.Entries.Count); using var dependency = new StreamReader(zip.GetEntry("hd2runtime.json")!.Open()); Assert.Contains("0.21.0", dependency.ReadToEnd());
        var json = File.ReadAllText(e.Paths.ProjectFile(w.Project.Id)); Assert.Contains("\"targetKind\": \"stratagem\"", json); Assert.DoesNotContain("offset", json);
    }
    private static byte[] CurrentArchive(bool omitStratagem = false)
    {
        var files = SdkCache.BundledComposition().Where(f => !omitStratagem || f.Key != StratagemCatalogReader.FileName).ToDictionary();
        files.Add("metadata.json", SdkCache.BundledMetadata()); files.Add(PlayerWeaponCatalogReader.FileName, SdkCache.BundledCapabilities()); files.Add(PlayerWeaponAmmoCatalogReader.FileName, SdkCache.BundledAmmoCapabilities());
        using var output = new MemoryStream();
        using (var zip = new ZipArchive(output, ZipArchiveMode.Create, true)) foreach (var (name, bytes) in files) { using var stream = zip.CreateEntry(name).Open(); stream.Write(bytes); }
        return output.ToArray();
    }
    [Fact] public async Task Release_install_preserves_0201_pin_until_explicit_rebind()
    {
        using var e = new TestEnvironment(); e.GitHub.Archive = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "sdk-0.20.1.zip"));
        var old = await e.Cache.InstallAsync(FakeGitHub.MakeRelease("0.20.1", e.GitHub.Archive)); var w = e.Workspace();
        await w.CreateAsync(new("Pinned", "Tests", "mods/tests/pinned_0201", "0.1.0"), old);
        var f = old.SupportAuthoring!.FieldInstances.First(f => f.SupportWeapon == "APW-1 Anti-Materiel Rifle" && f.SemanticFieldId == "weapon.sway");
        await w.SetSupportAsync(f.InstanceKey, "0.5");
        e.GitHub.Archive = CurrentArchive(); e.GitHub.Release = FakeGitHub.MakeRelease("0.24.0", e.GitHub.Archive);
        var status = await e.Updates.CheckAsync(); Assert.True(status.UpdateAvailable); Assert.True(status.VerifiedOnline);
        await e.Cache.InstallAsync(e.GitHub.Release); await w.OpenAsync(w.Project!.Id);
        Assert.Equal("0.20.1", w.Project!.SdkVersion); Assert.Null(w.Metadata!.Stratagems);
        await w.RebindToInstalledSdkAsync(); Assert.Equal("0.24.0", w.Project.SdkVersion); Assert.Equal(1468, w.Metadata!.Stratagems!.FieldInstances.Length);
        Assert.Equal("0.20.1", w.Project.SupportChanges.Single().BaselineSdkVersion); Assert.Null(w.BuildError);
    }
    [Fact] public async Task Missing_stratagem_artifact_preserves_installed_cache()
    {
        using var e = new TestEnvironment(); e.GitHub.Archive = CurrentArchive(true);
        await Assert.ThrowsAsync<InvalidDataException>(() => e.Cache.InstallAsync(FakeGitHub.MakeRelease("0.24.0", e.GitHub.Archive)));
        Assert.Equal("0.5.1", (await e.Cache.GetCurrentAsync()).Version);
    }
    [Fact] public void Unknown_stratagem_schema_rejects()
    {
        var json = JsonNode.Parse(SdkCache.BundledComposition()[StratagemCatalogReader.FileName])!; json["schemaVersion"] = 1;
        Assert.Throws<UnsupportedSdkException>(() => new StratagemCatalogReader().Read(Encoding.UTF8.GetBytes(json.ToJsonString())));
    }
}
