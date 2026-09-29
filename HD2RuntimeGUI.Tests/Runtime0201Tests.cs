using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using HD2RuntimeGUI.Core.Generation;
using HD2RuntimeGUI.Core.Metadata;
using HD2RuntimeGUI.Core.Models;
using HD2RuntimeGUI.Core.Services;
using Xunit;

namespace HD2RuntimeGUI.Tests;

public sealed class Runtime0201Tests
{
    private static async Task<BuilderWorkspace> Workspace(TestEnvironment e)
    {
        // Pinned to the published 0.23.2 support catalog (0.20.1–0.23.x model); Runtime024Tests covers the 0.24.0 expansion.
        var sdk = await SdkFixtures.Install(e, "0.23.2");
        var w = e.Workspace(); await w.CreateAsync(new("Support", "Tests", "mods/tests/support", "0.1.0"), sdk); return w;
    }
    private static SupportField Field(BuilderWorkspace w, string weapon, string field) => w.Metadata!.SupportAuthoring!.FieldInstances.Single(f => f.SupportWeapon == weapon && f.SemanticFieldId == field);
    private static async Task Edit(BuilderWorkspace w, SupportField f, string value, bool approve = true)
    { await w.SetSupportAsync(f.InstanceKey, value); if (approve && f.SharedScope.RequiresAcknowledgement) await w.SetSupportApprovalAsync(f.InstanceKey, true); }
    [Fact] public async Task Canonical_catalog_has_complete_instances_objects_operations_and_scopes()
    {
        using var e = new TestEnvironment(); var w = await Workspace(e); var c = w.Metadata!.SupportAuthoring!;
        Assert.Equal("0.23.2", w.Metadata.Version); Assert.Equal(35, c.Weapons.Length); Assert.Equal(27, c.Weapons.Count(w => w.Writable));
        Assert.Equal(828, c.FieldInstances.Length); Assert.Equal(828, c.FieldInstances.Select(f => f.InstanceKey).Distinct().Count());
        Assert.Equal(146, c.BackingObjects.Length); Assert.Equal(155, c.OperationGroups.Length);
        Assert.Equal(81, c.FieldInstances.Where(f => f.SharedScope.Shared).Select(f => f.SharedScope.ScopeKey).Distinct().Count());
        Assert.Equal(16, c.FieldInstances.GroupBy(f => (f.SupportWeapon, f.SemanticFieldId)).Count(g => g.Count() > 1));
        Assert.Equal(828, c.Weapons.Sum(w => w.FieldInstanceKeys.Length));
        Assert.Equal(3574, w.Metadata.PlayerWeapons!.Summary.FieldInstances);
    }
    [Fact] public async Task Every_canonical_instance_can_create_a_typed_change_and_all_27_identities_generate()
    {
        using var e = new TestEnvironment(); var w = await Workspace(e); var svc = new SupportChangeService();
        foreach (var f in w.Metadata!.SupportAuthoring!.FieldInstances)
        {
            var value = JsonSerializer.Serialize(f.Value.Baseline.GetDouble() + 1);
            var c = svc.Create(w.Metadata, f.InstanceKey, value); w.Project!.SupportApprovals[f.SharedScope.ScopeKey] = SupportChangeService.ApprovalEvidence(f);
            svc.Validate(w.Project, w.Metadata, c); Assert.Equal(f.Target.AttackRole, c.AttackRole);
            w.Project.SupportChanges = [c]; Assert.Contains(f.ApiFieldConstant, e.Generator.Generate(w.Project, w.Metadata));
        }
        foreach (var weapon in w.Metadata.SupportAuthoring.Weapons.Where(w => w.Writable))
        {
            var f = w.Metadata.SupportAuthoring.Field(weapon.FieldInstanceKeys[0]);
            w.Project!.SupportChanges = [svc.Create(w.Metadata, f.InstanceKey, JsonSerializer.Serialize(f.Value.Baseline.GetDouble() + 1))];
            var lua = e.Generator.Generate(w.Project, w.Metadata); Assert.Contains("hd2.support_weapon(", lua); Assert.DoesNotContain("0x", lua);
        }
    }
    [Theory] [InlineData("MG-43 Machine Gun")] [InlineData("EAT-17 Expendable Anti-Tank")] [InlineData("LAS-98 Laser Cannon")]
    [InlineData("B/FLAM-80 Cremator")] [InlineData("CQC-20 Breaching Hammer")] [InlineData("CQC-72 Entrenchment Tool")]
    [InlineData("M-105 Stalwart")] [InlineData("MG-206 Heavy Machine Gun")]
    public async Task Duplicates_remain_visible_and_unavailable(string name)
    {
        using var e = new TestEnvironment(); var w = await Workspace(e); var c = w.Metadata!.SupportAuthoring!; var weapon = c.Weapons.Single(w => w.Name == name);
        Assert.False(weapon.Writable); Assert.Empty(weapon.FieldInstanceKeys); Assert.NotEmpty(weapon.BlockedFields);
        Assert.True(w.Metadata.Advanced!.Support.Weapons.ContainsKey(name)); Assert.DoesNotContain(c.FieldInstances, f => f.SupportWeapon == name);
    }
    [Theory] [InlineData("ARC-3 Arc Thrower", "arc")] [InlineData("ARC-3 Arc Thrower", "charge")]
    [InlineData("B/MD C4 Pack", "explosion")] [InlineData("MS-11 Solo Silo", "explosion")]
    [InlineData("LAS-99 Quasar Cannon", "heat")] [InlineData("LAS-99 Quasar Cannon", "heatsink")]
    [InlineData("40-K Meltagun", "beam")] [InlineData("TX-41 Sterilizer", "status")]
    [InlineData("APW-1 Anti-Materiel Rifle", "magazine")]
    public async Task Family_controls_come_from_canonical_metadata(string weapon, string domain)
    {
        using var e = new TestEnvironment(); var w = await Workspace(e);
        var f = w.Metadata!.SupportAuthoring!.FieldInstances.First(f => f.SupportWeapon == weapon && f.Display.Domain == domain);
        await Edit(w, f, JsonSerializer.Serialize(f.Value.Baseline.GetDouble() + 1)); Assert.Null(w.BuildError);
        Assert.Contains(f.ApiFieldConstant, w.LuaPreview);
    }
    [Fact] public async Task Same_object_transaction_multi_object_plan_and_scope_isolation()
    {
        using var e = new TestEnvironment(); var w = await Workspace(e); const string weapon = "GR-8 Recoilless Rifle";
        var velocity = Field(w, weapon, "projectile.velocity"); var drag = Field(w, weapon, "projectile.drag"); var blast = Field(w, weapon, "explosion.outer_radius");
        await Edit(w, velocity, "350", false); Assert.Null(w.BuildError); Assert.Contains("allow_shared=true", w.LuaPreview); // approval is implicit
        await w.SetSupportApprovalAsync(velocity.InstanceKey, true); await Edit(w, drag, "0.2", false);
        Assert.Null(w.BuildError); Assert.Contains("transaction={", w.LuaPreview); Assert.Single(w.Project!.SupportApprovals);
        await Edit(w, blast, "10", false); Assert.Null(w.BuildError);
        await w.SetSupportApprovalAsync(blast.InstanceKey, true); Assert.Null(w.BuildError); Assert.Contains("plan={", w.LuaPreview);
        Assert.Equal(1, w.LuaPreview.Split("hd2.ensure(").Length - 1); Assert.Equal(2, w.LuaPreview.Split("target=").Length - 1);
        await w.SetSupportApprovalAsync(velocity.InstanceKey, false); Assert.Null(w.BuildError); Assert.Contains("allow_shared=true", w.LuaPreview); Assert.True(SupportChangeService.Approved(w.Project, blast));
    }
    [Fact] public async Task Solo_Silo_and_status_repeated_fields_keep_separate_branches()
    {
        using var e = new TestEnvironment(); var w = await Workspace(e);
        foreach (var weapon in new[] { "MS-11 Solo Silo", "TX-41 Sterilizer" })
        {
            await w.ResetSupportAsync(); var repeated = w.Metadata!.SupportAuthoring!.FieldInstances.Where(f => f.SupportWeapon == weapon).GroupBy(f => f.SemanticFieldId).First(g => g.Count() > 1).ToArray();
            foreach (var f in repeated) await Edit(w, f, JsonSerializer.Serialize(f.Value.Baseline.GetDouble() + 1));
            Assert.Equal(2, w.Project!.SupportChanges.Count); Assert.Null(w.BuildError); Assert.Contains("plan={", w.LuaPreview);
            foreach (var f in repeated) Assert.Contains(":attack('" + f.Target.AttackRole + "')", w.LuaPreview);
        }
    }
    [Fact] public async Task Autosave_updates_removes_float_equivalent_and_reload_has_no_noops()
    {
        using var e = new TestEnvironment(); var w = await Workspace(e); var f = Field(w, "APW-1 Anti-Materiel Rifle", "weapon.sway");
        await Edit(w, f, "0.5"); var id = w.Project!.SupportChanges.Single().Id;
        await Edit(w, f, "0.25"); Assert.Equal(id, w.Project.SupportChanges.Single().Id);
        await w.OpenAsync(w.Project.Id); Assert.Equal(0.25, w.Project!.SupportChanges.Single().DesiredValue.GetDouble());
        await w.SetSupportAsync(f.InstanceKey, f.Value.Baseline.GetDouble().ToString("F7", System.Globalization.CultureInfo.InvariantCulture)); Assert.Empty(w.Project.SupportChanges);
        await w.OpenAsync(w.Project.Id); Assert.Empty(w.Project!.SupportChanges);
        await Assert.ThrowsAsync<InvalidDataException>(() => w.SetSupportAsync(f.InstanceKey, "1e")); Assert.Empty(w.Project.SupportChanges);
    }
    [Fact] public async Task Rebind_preserves_values_and_requires_baseline_and_scope_review()
    {
        using var e = new TestEnvironment(); var w = await Workspace(e); var f = Field(w, "GR-8 Recoilless Rifle", "projectile.velocity");
        await Edit(w, f, "350"); var saved = w.Project!.SupportChanges.Single(); var service = new SupportChangeService();
        var nextField = f with { Value = f.Value with { Baseline = JsonSerializer.SerializeToElement(251) } };
        SdkMetadata With(SupportField changed) => w.Metadata! with { SupportAuthoring = w.Metadata!.SupportAuthoring! with { FieldInstances = w.Metadata.SupportAuthoring.FieldInstances.Select(x => x.InstanceKey == changed.InstanceKey ? changed : x).ToArray() } };
        Assert.Throws<InvalidDataException>(() => service.Validate(w.Project, With(nextField), saved));
        nextField = f with { Value = f.Value with { Type = "boolean", Baseline = JsonSerializer.SerializeToElement(true) } };
        Assert.Throws<InvalidDataException>(() => service.Validate(w.Project, With(nextField), saved));
        Assert.False(SupportChangeService.NoOp(With(nextField), saved));
        nextField = f with { SharedScope = f.SharedScope with { ScopeKey = "support-scope/v1/changed" } };
        Assert.False(SupportChangeService.Approved(w.Project, nextField)); Assert.Throws<InvalidDataException>(() => service.Validate(w.Project, With(nextField), saved));
        Assert.Equal(250, saved.ExpectedValue.GetDouble()); Assert.Equal(350, saved.DesiredValue.GetDouble());
        await w.RebindToInstalledSdkAsync(); Assert.Null(w.BuildError); Assert.Equal(saved, w.Project.SupportChanges.Single());
    }
    [Fact] public async Task Output_and_ZIP_are_deterministic_semantic_and_exclude_runtime()
    {
        using var e = new TestEnvironment(); var w = await Workspace(e);
        await Edit(w, Field(w, "GR-8 Recoilless Rifle", "projectile.velocity"), "350"); await Edit(w, Field(w, "GR-8 Recoilless Rifle", "explosion.outer_radius"), "10");
        var lua = w.LuaPreview; await w.ExportAsync(); var zip = File.ReadAllBytes(w.LastExport!);
        w.Project!.SupportChanges.Reverse(); await e.Store.SaveAsync(w.Project); await w.OpenAsync(w.Project.Id); await w.ExportAsync();
        Assert.Equal(lua, w.LuaPreview); Assert.Equal(zip, File.ReadAllBytes(w.LastExport!));
        Assert.DoesNotContain("0x", lua); Assert.DoesNotContain("offset", lua); Assert.Contains(":projectile()", lua); Assert.Contains(":explosion()", lua);
        using var archive = ZipFile.OpenRead(w.LastExport!); Assert.Equal(8, archive.Entries.Count);
        Assert.DoesNotContain(archive.Entries, x => x.FullName.Contains("Capabilities") || x.FullName.Contains("snapshot"));
        Assert.Contains(archive.Entries, x => { using var r = new StreamReader(x.Open()); return r.ReadToEnd().Contains("0.23.2"); });
    }
    [Theory] [InlineData("missing")] [InlineData("duplicate")] [InlineData("group")] [InlineData("phase")] [InlineData("api")] [InlineData("scope")]
    public void Invalid_canonical_metadata_is_rejected(string mode)
    {
        var node = JsonNode.Parse(SdkCache.BundledComposition()[SupportAuthoringReader.FileName])!; var list = node["fieldInstances"]!.AsArray(); var first = list[0]!;
        switch (mode) {
            case "missing": list.RemoveAt(0); break; case "duplicate": list.Add(first.DeepClone()); break;
            case "group": first["backing"]!["objectKey"] = "invented"; break;
            case "phase": first["operation"]!["phase"] = 2; break;
            case "api": first["apiFieldConstant"] = "os.execute('bad')"; break;
            case "scope": first["sharedScope"]!["scopeKey"] = "invented"; break;
        }
        Assert.Throws<InvalidDataException>(() => new SupportAuthoringReader().Read(Encoding.UTF8.GetBytes(node.ToJsonString()), "0.27.0"));
    }
    private static byte[] Archive(bool omitSupport = false)
    {
        var files = SdkCache.BundledComposition().Where(f => !omitSupport || f.Key != SupportAuthoringReader.FileName).ToDictionary();
        files.Add("metadata.json", SdkCache.BundledMetadata()); files.Add(PlayerWeaponCatalogReader.FileName, SdkCache.BundledCapabilities()); files.Add(PlayerWeaponAmmoCatalogReader.FileName, SdkCache.BundledAmmoCapabilities());
        using var output = new MemoryStream(); using (var zip = new ZipArchive(output, ZipArchiveMode.Create, true))
            foreach (var (name, bytes) in files) { using var s = zip.CreateEntry(name).Open(); s.Write(bytes); }
        return output.ToArray();
    }
    [Fact] public async Task SDK_install_rebind_preserves_019_pin_until_explicit_action()
    {
        using var e = new TestEnvironment(); e.GitHub.Archive = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "sdk-0.19.0.zip"));
        var old = await e.Cache.InstallAsync(FakeGitHub.MakeRelease("0.19.0", e.GitHub.Archive)); var w = e.Workspace();
        await w.CreateAsync(new("Old project", "Tests", "mods/tests/old_support", "0.1.0"), old);
        await w.SetWeaponChangeAsync("AR-23C Liberator Concussive", "weapon.fire_rate", "1100", false);
        e.GitHub.Archive = Archive(); await e.Cache.InstallAsync(FakeGitHub.MakeRelease("0.27.0", e.GitHub.Archive));
        await w.OpenAsync(w.Project!.Id); Assert.Equal("0.19.0", w.Project!.SdkVersion); Assert.Null(w.Metadata!.SupportAuthoring);
        await w.RebindToInstalledSdkAsync(); Assert.Equal("0.27.0", w.Project.SdkVersion); Assert.Equal(1008, w.Metadata!.SupportAuthoring!.FieldInstances.Length);
        Assert.Equal("0.19.0", w.Project.WeaponChanges.Single().BaselineSdkVersion); Assert.Null(w.BuildError);
    }
    [Fact] public async Task Missing_canonical_artifact_preserves_previous_install()
    {
        using var e = new TestEnvironment(); e.GitHub.Archive = Archive(true);
        await Assert.ThrowsAsync<InvalidDataException>(() => e.Cache.InstallAsync(FakeGitHub.MakeRelease("0.27.0", e.GitHub.Archive)));
        Assert.Equal("0.5.1", (await e.Cache.GetCurrentAsync()).Version);
    }
    [Fact] public async Task Reused_player_artifacts_allow_only_verified_exact_bytes()
    {
        using var e = new TestEnvironment(); var w = await Workspace(e); var files = SdkCache.BundledComposition().ToDictionary();
        var name = PlayerWeaponCompositionReader.FileNames[0];
        files[name] = Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(files[name]) + " ");
        Assert.Throws<InvalidDataException>(() => new PlayerWeaponCompositionReader().Read(files, w.Metadata!.PlayerWeapons!));
    }
    [Fact] public async Task Rounds_ammo_and_shared_damage_fields_group_by_published_owner()
    {
        using var e = new TestEnvironment(); var w = await Workspace(e); var c = w.Metadata!.SupportAuthoring!;
        foreach (var domain in new[] { "rounds", "damage" })
        {
            await w.ResetSupportAsync(); var fields = c.FieldInstances.Where(f => f.Display.Domain == domain).GroupBy(f => f.Operation.TransactionGroupingKey).First(g => g.Count() > 1).Take(2).ToArray();
            foreach (var f in fields) await Edit(w, f, JsonSerializer.Serialize(f.Value.Baseline.GetDouble() + 1));
            Assert.Null(w.BuildError); Assert.Contains("transaction={", w.LuaPreview); Assert.DoesNotContain("plan={", w.LuaPreview);
        }
    }
    [Fact] public async Task Duplicate_project_retains_semantic_intent_and_exact_scope_approval()
    {
        using var e = new TestEnvironment(); var w = await Workspace(e); var f = Field(w, "GR-8 Recoilless Rifle", "projectile.velocity"); await Edit(w, f, "350");
        var copy = await e.Projects.DuplicateAsync(w.Project!, new("Copy", "Tests", "mods/tests/support_copy", "0.1.0"));
        Assert.NotEqual(w.Project!.SupportChanges.Single().Id, copy.SupportChanges.Single().Id); Assert.True(SupportChangeService.Approved(copy, f));
        Assert.Contains("hd2.support_weapon", e.Generator.Generate(copy, w.Metadata!));
        await w.ResetSupportAsync(f.SupportWeapon); Assert.Empty(w.Project.SupportChanges); Assert.Single(copy.SupportChanges);
    }
    [Theory] [InlineData("RecoillessTuning020", "GR-8 Recoilless Rifle", "projectile.velocity", "350", "explosion.outer_radius", "10")]
    [InlineData("ArcThrowerTuning020", "ARC-3 Arc Thrower", "arc.range", "75", null, null)]
    [InlineData("C4Explosion020", "B/MD C4 Pack", "explosion.outer_radius", "15", "explosion.damage.standard_damage", "1500")]
    [InlineData("SupportAMRTuning020", "APW-1 Anti-Materiel Rifle", "weapon.ergonomics", "80", "weapon.sway", "0.5")]
    public async Task Samples_match_reviewed_semantic_Lua(string name, string weapon, string field, string value, string? field2, string? value2)
    {
        using var e = new TestEnvironment(); var w = await Workspace(e); w.Project!.ResourceId = "mods/skyeshade/" + name.ToLowerInvariant();
        w.Project.ManagerGuid = HD2RuntimeGUI.Core.Projects.ProjectIdentity.ManagerGuid(w.Project.ResourceId);
        await Edit(w, Field(w, weapon, field), value); if (field2 != null) await Edit(w, Field(w, weapon, field2), value2!);
        if (name == "SupportAMRTuning020") await Edit(w, Field(w, weapon, "projectile.velocity"), "1100");
        Assert.Null(w.BuildError); Assert.Equal(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Golden", name + ".lua")).Replace("\r\n", "\n"), w.LuaPreview);
        await w.ExportAsync(); var first = File.ReadAllBytes(w.LastExport!); await w.OpenAsync(w.Project.Id); await w.ExportAsync(); Assert.Equal(first, File.ReadAllBytes(w.LastExport!));
    }
}
