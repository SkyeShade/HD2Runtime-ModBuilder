using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using HD2RuntimeGUI.Core.Generation;
using HD2RuntimeGUI.Core.Metadata;
using HD2RuntimeGUI.Core.Services;
using Xunit;

namespace HD2RuntimeGUI.Tests;

public sealed class Runtime018Tests
{
    private const string Sickle = "LAS-16 Sickle";
    private static async Task<BuilderWorkspace> Workspace(TestEnvironment e)
    {
        File.Delete(e.Paths.CachePath("current.json")); var sdk = await e.Cache.GetCurrentAsync(); var w = e.Workspace();
        await w.CreateAsync(new("Heat", "Tests", "mods/tests/heat", "0.1.0"), sdk); return w;
    }
    private static byte[] Archive(string? omit = null)
    {
        var files = SdkCache.BundledComposition().ToDictionary(x => x.Key, x => x.Value);
        files.Add("metadata.json", SdkCache.BundledMetadata()); files.Add(PlayerWeaponCatalogReader.FileName, SdkCache.BundledCapabilities()); files.Add(PlayerWeaponAmmoCatalogReader.FileName, SdkCache.BundledAmmoCapabilities());
        using var output = new MemoryStream();
        using (var zip = new ZipArchive(output, ZipArchiveMode.Create, true))
            foreach (var (name, bytes) in files.Where(x => x.Key != omit)) { using var stream = zip.CreateEntry(name).Open(); stream.Write(bytes); }
        return output.ToArray();
    }
    [Fact] public async Task Published_018_counts_and_all_contracts_load_and_cache()
    {
        using var e = new TestEnvironment(); e.GitHub.Archive = Archive();
        var sdk = await e.Cache.InstallAsync(FakeGitHub.MakeRelease("0.18.0", e.GitHub.Archive));
        Assert.Equal("0.18.0", (await e.Cache.GetCurrentAsync()).Version);
        Assert.Equal(80, sdk.PlayerWeapons!.Weapons.Count); Assert.Equal(3574, sdk.PlayerWeapons.Summary.FieldInstances);
        var h = sdk.PlayerHeat!; Assert.Equal(49, h.Summary.DirectSemanticFieldsResolved); Assert.Equal(30, h.Summary.WritableFieldInstances);
        Assert.Equal(7, h.Summary.WeaponsWithHeatMechanism); Assert.Equal(5, h.Summary.WeaponsWithWritableHeatFields);
        Assert.Equal(15, sdk.PlayerWeapons.Weapons.Sum(w => w.Fields.Count(f => f.Domain == "heat" && f.Editable)));
        Assert.Equal(15, sdk.PlayerWeapons.Weapons.Sum(w => w.Fields.Count(f => f.Domain == "heatsink" && f.Editable)));
        Assert.Equal(35, sdk.Advanced!.Support.Weapons.Count); Assert.Equal(419, sdk.PlayerWeapons.Summary.Composition!.Magazine.AttachmentOptionsMapped);
        Assert.Equal(21, sdk.PlayerWeapons.Summary.Composition!.FireMode.WritableWeapons); Assert.Equal(130, sdk.PlayerWeapons.Summary.Composition!.Terminal.WritableActions);
        Assert.Equal(0, sdk.PlayerWeapons.Summary.Composition!.Magazine.WritableAttachmentSelections); Assert.Equal(0, h.Summary.WritableHeatsinkOptionOverrides);
        Assert.True(File.Exists(e.Paths.CachePath("0.18.0", PlayerWeaponHeatCatalogReader.FileName)));
    }
    [Fact] public async Task Missing_heat_artifact_preserves_previous_SDK()
    {
        using var e = new TestEnvironment(); e.GitHub.Archive = Archive(PlayerWeaponHeatCatalogReader.FileName);
        await Assert.ThrowsAsync<InvalidDataException>(() => e.Cache.InstallAsync(FakeGitHub.MakeRelease("0.18.0", e.GitHub.Archive)));
        Assert.Equal("0.5.1", (await e.Cache.GetCurrentAsync()).Version); Assert.False(File.Exists(e.Paths.SdkFile("0.18.0")));
    }
    [Theory]
    [InlineData("schemaVersion", "2")] [InlineData("hd2RuntimeVersion", "\"0.17.0\"")]
    [InlineData("weapons", "null")] [InlineData("summary.writableFieldInstances", "31")]
    [InlineData("safety.writes", "1")] [InlineData("summary.writableHeatsinkOptionOverrides", "1")]
    public async Task Malformed_or_incompatible_heat_metadata_rejected(string property, string value)
    {
        using var e = new TestEnvironment(); var w = await Workspace(e);
        var node = JsonNode.Parse(SdkCache.BundledComposition()[PlayerWeaponHeatCatalogReader.FileName])!;
        var path = property.Split('.'); var parent = path.Length == 2 ? node[path[0]]! : node; parent[path[^1]] = JsonNode.Parse(value);
        var error = Record.Exception(() => new PlayerWeaponHeatCatalogReader().Read(Encoding.UTF8.GetBytes(node.ToJsonString()), w.Metadata!.PlayerWeapons!));
        Assert.True(error is InvalidDataException or UnsupportedSdkException);
    }
    [Theory] [InlineData("value", "99")] [InlineData("writable", "false")] [InlineData("offset", "100")]
    public async Task Companion_baseline_permissions_and_owner_must_match_authoring(string property, string value)
    {
        using var e = new TestEnvironment(); var w = await Workspace(e);
        var node = JsonNode.Parse(SdkCache.BundledComposition()[PlayerWeaponHeatCatalogReader.FileName])!;
        node["weapons"]!.AsArray().Single(x => x!["weapon"]!.GetValue<string>() == Sickle)!["fields"]![0]![property] = JsonNode.Parse(value);
        Assert.Throws<InvalidDataException>(() => new PlayerWeaponHeatCatalogReader().Read(Encoding.UTF8.GetBytes(node.ToJsonString()), w.Metadata!.PlayerWeapons!));
    }
    [Theory]
    [InlineData("heat.capacity", "140", "100.0000000")]
    [InlineData("heat.heat_per_shot", "0.5", "1.15")]
    [InlineData("heat.cool_per_second", "12", "8.0")]
    [InlineData("heatsink.starting", "4", "2")]
    [InlineData("heatsink.spare", "5", "3")]
    [InlineData("heatsink.from_supply", "8", "6")]
    public async Task Autosave_reload_and_reset_remove_semantically_equal_heat_override(string field, string value, string baseline)
    {
        using var e = new TestEnvironment(); var w = await Workspace(e); await w.SetWeaponChangeAsync(Sickle, field, value, false);
        Assert.Null(w.BuildError); Assert.Single(w.Project!.WeaponChanges); Assert.Contains("hd2.fields." + field, w.LuaPreview);
        Assert.Contains("target=hd2.weapon('LAS-16 Sickle')", w.LuaPreview);
        var lua = w.LuaPreview; await w.OpenAsync(w.Project.Id); Assert.Equal(lua, w.LuaPreview);
        await w.SetWeaponChangeAsync(Sickle, field, baseline, false); Assert.Empty(w.Project.WeaponChanges);
        await w.OpenAsync(w.Project.Id); Assert.Empty(w.Project.WeaponChanges);
    }
    [Fact] public async Task Heat_and_heatsinks_on_one_component_are_one_deterministic_transaction()
    {
        using var e = new TestEnvironment(); var w = await Workspace(e);
        await w.SetWeaponChangeAsync(Sickle, "heat.capacity", "140", false);
        await w.SetWeaponChangeAsync(Sickle, "heat.cool_per_second", "12", false);
        await w.SetWeaponChangeAsync(Sickle, "heatsink.spare", "5", false);
        var plan = new SemanticOperationPlanner().Plan(w.Project!, w.Metadata!); var op = Assert.Single(plan);
        Assert.Equal("WeaponHeatComponentData", op.Owner.Kind); Assert.Equal(3, op.Changes.Count); Assert.False(op.AllowShared);
        Assert.Equal(new[] { "hd2.fields.heat.capacity", "hd2.fields.heat.cool_per_second", "hd2.fields.heatsink.spare" }, op.Changes.Select(c => c.Field));
        Assert.Equal(1, w.LuaPreview.Split("hd2.ensure(").Length - 1); Assert.Contains("transaction={", w.LuaPreview);
        var lua = w.LuaPreview; w.Project!.WeaponChanges.Reverse(); Assert.Equal(lua, e.Generator.Generate(w.Project, w.Metadata!));
        await w.ExportAsync(); var bytes = await File.ReadAllBytesAsync(w.LastExport!); await w.ExportAsync(); Assert.Equal(bytes, await File.ReadAllBytesAsync(w.LastExport!));
        using var zip = ZipFile.OpenRead(w.LastExport!); Assert.Equal(8, zip.Entries.Count); Assert.DoesNotContain(zip.Entries, entry => entry.Name.Contains("Capabilities"));
    }
    [Theory] [InlineData("LAS-5 Scythe")] [InlineData("LAS-7 Dagger")]
    public async Task Duplicate_heat_weapons_remain_visible_and_fail_closed(string name)
    {
        using var e = new TestEnvironment(); var w = await Workspace(e); var weapon = w.Metadata!.PlayerWeapons!.Weapon(name);
        Assert.True(w.Metadata.PlayerHeat!.Weapons.Single(x => x.Weapon == name).HeatMechanismPresent);
        Assert.True(weapon.OrdinaryWritesBlocked); Assert.All(weapon.Fields.Where(f => f.Domain is "heat" or "heatsink"), f => Assert.False(f.Editable));
        await Assert.ThrowsAsync<InvalidDataException>(() => w.SetWeaponChangeAsync(name, "heat.capacity", "140", false));
        if (name == "LAS-7 Dagger") Assert.Equal(2000, w.Metadata.PlayerWeapons.Field(name, "heat.capacity").CurrentDefault.GetDouble());
    }
    [Fact] public async Task Absent_heat_readonly_warmup_and_attachment_options_are_preserved()
    {
        using var e = new TestEnvironment(); var w = await Workspace(e); var sdk = w.Metadata!;
        Assert.False(sdk.PlayerHeat!.Weapons.Single(x => x.Weapon == "AR-23 Liberator").HeatMechanismPresent);
        Assert.DoesNotContain(sdk.PlayerWeapons!.Weapon("AR-23 Liberator").Fields, f => f.Domain is "heat" or "heatsink");
        foreach (var id in new[] { "heat.warmup", "heat.overheat_cooldown", "heat.cool_per_second_cold", "heatsink.from_ammo_box" })
        {
            var f = sdk.PlayerWeapons.Field(Sickle, id); Assert.False(f.Editable); Assert.NotEmpty(f.Reason!);
            await Assert.ThrowsAsync<InvalidDataException>(() => w.SetWeaponChangeAsync(Sickle, id, "1", false));
        }
        var options = sdk.Composition!.Magazines.Weapons.Single(x => x.Weapon == "LAS-5 Scythe").Categories!.SelectMany(c => c.Options).ToArray();
        Assert.Contains(options, x => x.Name.Contains("Heatsink")); Assert.All(options, x => Assert.False(x.Writable));
        var jar = sdk.PlayerWeapons.Field("JAR-5 Dominator", "weapon.default_fire_mode"); Assert.False(jar.Editable); Assert.Equal(new[] { 2, 3, 0 }, jar.NativeModeVector);
    }
    [Fact] public async Task Project_017_remains_pinned_until_explicit_rebind_without_rewriting_baseline()
    {
        using var e = new TestEnvironment(); e.GitHub.Archive = await File.ReadAllBytesAsync(Path.Combine(AppContext.BaseDirectory, "Fixtures", "sdk-0.17.0.zip"));
        var old = await e.Cache.InstallAsync(FakeGitHub.MakeRelease("0.17.0", e.GitHub.Archive)); var w = e.Workspace();
        await w.CreateAsync(new("Pinned", "Tests", "mods/tests/pinnedheat", "0.1.0"), old);
        await w.SetWeaponChangeAsync("AR-23C Liberator Concussive", "weapon.fire_rate", "1100", false);
        e.GitHub.Archive = Archive(); await e.Cache.InstallAsync(FakeGitHub.MakeRelease("0.18.0", e.GitHub.Archive));
        await w.OpenAsync(w.Project!.Id); Assert.Equal("0.17.0", w.Metadata!.Version); Assert.Null(w.Metadata.PlayerHeat);
        await w.RebindToInstalledSdkAsync(); Assert.Equal("0.18.0", w.Metadata.Version); Assert.NotNull(w.Metadata.PlayerHeat); Assert.Null(w.BuildError);
        var saved = Assert.Single(w.Project.WeaponChanges); Assert.Equal("0.17.0", saved.BaselineSdkVersion); Assert.Equal(400, saved.ExpectedValue.GetInt32()); Assert.Equal(1100, saved.DesiredValue.GetInt32());
    }
    [Fact] public async Task Changed_heat_baseline_requires_review_and_preserves_desired_value()
    {
        using var e = new TestEnvironment(); var w = await Workspace(e); await w.SetWeaponChangeAsync(Sickle, "heat.capacity", "140", false);
        var sdk = w.Metadata!; var changed = sdk with { PlayerWeapons = sdk.PlayerWeapons! with { Weapons = sdk.PlayerWeapons!.Weapons.Select(x => x.Name != Sickle ? x : x with {
            Fields = x.Fields.Select(f => f.SemanticFieldId != "heat.capacity" ? f : f with { CurrentDefault = JsonSerializer.SerializeToElement(120) }).ToArray() }).ToArray() } };
        Assert.Contains("baseline changed", Assert.Throws<InvalidDataException>(() => e.Generator.Generate(w.Project!, changed)).Message);
        Assert.Equal(100, w.Project!.WeaponChanges[0].ExpectedValue.GetDouble()); Assert.Equal(140, w.Project.WeaponChanges[0].DesiredValue.GetInt32());
    }
}
