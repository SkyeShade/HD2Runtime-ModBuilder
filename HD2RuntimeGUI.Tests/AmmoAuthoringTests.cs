using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using HD2RuntimeGUI.Core.Generation;
using HD2RuntimeGUI.Core.Metadata;
using HD2RuntimeGUI.Core.Models;
using HD2RuntimeGUI.Core.Projects;
using HD2RuntimeGUI.Core.Storage;
using Xunit;

namespace HD2RuntimeGUI.Tests;

public sealed class AmmoAuthoringTests
{
    private static readonly PlayerWeaponCatalog Catalog = new PlayerWeaponCatalogReader().Read(Release014(PlayerWeaponCatalogReader.FileName), "0.14.0");
    private static readonly PlayerWeaponAmmoCatalog Ammo = new PlayerWeaponAmmoCatalogReader().Read(Release014(PlayerWeaponAmmoCatalogReader.FileName), Catalog);
    private static readonly SdkMetadata Sdk = new MetadataReader().Read(Release014("metadata.json")) with { PlayerWeapons = Catalog, PlayerAmmo = Ammo };
    private static readonly WeaponChangeService Changes = new();
    internal static byte[] Release014(string name)
    {
        using var zip = ZipFile.OpenRead(Path.Combine(AppContext.BaseDirectory, "Fixtures", "sdk-0.14.0.zip"));
        using var stream = zip.GetEntry(name)!.Open(); using var output = new MemoryStream(); stream.CopyTo(output); return output.ToArray();
    }
    private static ModProject Project(string name, params WeaponChange[] changes)
    {
        var resource = "mods/skyeshade/" + name.ToLowerInvariant();
        return new() { DisplayName = name, Author = "SkyeShade", ResourceId = resource, ManagerGuid = ProjectIdentity.ManagerGuid(resource), SdkVersion = Sdk.Version, ExportDirectory = Path.GetTempPath(), WeaponChanges = changes.ToList() };
    }
    private static byte[] Fixture(string name) => File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", name));
    private static byte[] Archive(bool old = false, byte[]? ammo = null, bool omitAmmo = false, string? extra = null)
    {
        using var output = new MemoryStream();
        using (var zip = new ZipArchive(output, ZipArchiveMode.Create, true))
        {
            void Add(string name, byte[] bytes) { using var s = zip.CreateEntry(name).Open(); s.Write(bytes); }
            Add("metadata.json", old ? Fixture("metadata-0.13.0.json") : Release014("metadata.json"));
            Add(PlayerWeaponCatalogReader.FileName, old ? Fixture("player-weapons-0.13.0.json") : Release014(PlayerWeaponCatalogReader.FileName));
            if (!old && !omitAmmo) Add(PlayerWeaponAmmoCatalogReader.FileName, ammo ?? Release014(PlayerWeaponAmmoCatalogReader.FileName));
            if (extra != null) Add(extra, "{}"u8.ToArray());
        }
        return output.ToArray();
    }
    private static async Task<SdkMetadata> Install(TestEnvironment env, bool old = false)
    {
        env.GitHub.Archive = Archive(old);
        return await env.Cache.InstallAsync(FakeGitHub.MakeRelease(old ? "0.13.0" : "0.14.0", env.GitHub.Archive));
    }
    [Fact] public void Published_metadata_counts_and_cross_file_merge()
    {
        Assert.Equal(80, Catalog.Weapons.Count); Assert.Equal(3027, Catalog.Summary.FieldInstances);
        Assert.Equal(1966, Catalog.Summary.WritableFieldInstances); Assert.Equal(60, Catalog.FieldDefinitions.Count);
        Assert.Equal(47, Catalog.Summary.WritableSemanticFieldDefinitions); Assert.Equal(13, Catalog.Summary.ReadOnlySemanticFieldDefinitions);
        Assert.Equal(6, Catalog.Summary.DerivedSemanticFieldDefinitions);
        var fields = Catalog.Weapons.SelectMany(w => w.Fields).Where(f => f.Domain is "magazine" or "rounds").ToArray();
        Assert.Equal(360, fields.Length); Assert.Equal(194, fields.Count(f => f.Editable));
        Assert.Equal(80, Ammo.Weapons.Count); Assert.Equal(45, Ammo.Summary.WeaponsWithWritableAmmoFields);
        Assert.Equal(19, Ammo.Summary.DefaultMagazineOptions); Assert.Equal(13, Ammo.Summary.NotApplicable);
    }
    [Theory]
    [InlineData("magazine.capacity", 10, 15)]
    [InlineData("magazine.spare_magazines", 10, 8)]
    [InlineData("magazine.starting_magazines", 6, 8)]
    [InlineData("magazine.magazines_from_supply", 10, 8)]
    public void Detachable_magazine_fields_use_guarded_semantics(string id, int baseline, int desired)
    {
        var c = Changes.Create(Sdk, "P-113 Verdict", id, desired.ToString(), false);
        Changes.Validate(Sdk, c); Assert.Equal(baseline, c.ExpectedValue.GetInt32());
        var p = Project("Ammo", c);
        var lua = new LuaGenerator(new ChangeService()).Generate(p, Sdk);
        Assert.Contains("field=hd2.fields." + id, lua); Assert.Contains("hd2.weapon('P-113 Verdict')", lua);
        Assert.Contains("expect=" + baseline, lua); Assert.Contains("value=" + desired, lua);
        Assert.Contains("hd2.ensure", lua); Assert.Contains("patch=", lua); Assert.DoesNotContain("0x", lua);
    }
    [Fact] public void Rounds_feed_keeps_dual_feed_and_derived_values_distinct()
    {
        var w = Catalog.Weapon("SG-8 Punisher");
        Assert.Equal(7, w.Fields.Count(f => f.Domain == "rounds"));
        Assert.Equal(8, Catalog.Field(w.Name, "rounds.feed_capacity_1").CurrentDefault.GetDouble());
        Assert.Equal(8, Catalog.Field(w.Name, "rounds.feed_capacity_2").CurrentDefault.GetDouble());
        Assert.Equal(16, Catalog.Field(w.Name, "rounds.capacity").CurrentDefault.GetInt32());
        foreach (var id in new[] { "rounds.feed_capacity_1", "rounds.feed_capacity_2", "rounds.spare_rounds", "rounds.starting_rounds", "rounds.rounds_from_supply" })
            Changes.Validate(Sdk, Changes.Create(Sdk, w.Name, id, "20", false));
    }
    [Theory]
    [InlineData("SG-8 Punisher", "rounds.capacity", 16)]
    [InlineData("SG-8 Punisher", "rounds.rounds_from_ammo_box", 30)]
    [InlineData("P-113 Verdict", "magazine.magazines_from_ammo_box", 5)]
    public void Derived_values_are_baseline_only_and_not_authorable(string weapon, string id, int baseline)
    {
        var f = Catalog.Field(weapon, id); Assert.True(f.DerivedReadOnly); Assert.False(f.Editable);
        Assert.Equal(baseline, f.CurrentDefault.GetInt32()); Assert.Contains("Derived", f.Reason);
        Assert.Throws<InvalidDataException>(() => Changes.Create(Sdk, weapon, id, "20", false));
    }
    [Fact] public void Customization_defaults_and_shared_group_remain_readonly()
    {
        var presets = Ammo.Weapons.Where(w => w.DefaultMagazineOption != null).ToArray(); Assert.Equal(19, presets.Length);
        foreach (var w in presets)
        {
            Assert.False(w.DefaultMagazineOption!.Writable); Assert.False(w.Writable);
            Assert.All(Catalog.Weapon(w.Name).Fields.Where(f => f.Domain == "magazine"), f => Assert.False(f.Editable));
            Assert.Throws<InvalidDataException>(() => Changes.Create(Sdk, w.Name, "magazine.capacity", "80", true));
            Assert.DoesNotContain(Catalog.Weapon(w.Name).Fields, f => f.SemanticFieldId.Contains("option"));
        }
        Assert.Equal(60, Catalog.Field("AR-23C Liberator Concussive", "magazine.capacity").CurrentDefault.GetInt32());
        Assert.Equal(new[] { "AR-23 Liberator", "AR-23P Liberator Penetrator", "AR-59 Suppressor" }, presets.Where(w => w.Shared).Select(w => w.Name).Order().ToArray());
        Assert.All(presets.Where(w => w.Shared), w => Assert.Equal(2, w.DefaultMagazineOption!.SharedWithWeapons.Count));
    }
    [Fact] public void Shared_write_gate_is_generic_for_ammo_descriptors()
    {
        // Synthetic future writable shared descriptor tests the existing gate.
        // Published 0.14 shared default options above must remain read-only.
        var weapon = Catalog.Weapon("P-113 Verdict"); var field = Catalog.Field(weapon.Name, "magazine.capacity");
        var shared = field with { AffectsMultipleWeapons = true, WriteScope = "shared_component", SharedWithWeapons = ["JAR-5 Dominator"], Backing = field.Backing! with { ConsumerCount = 2 } };
        var sdk = Sdk with { PlayerWeapons = Catalog with { Weapons = Catalog.Weapons.Select(w => w.Name == weapon.Name ? w with { Fields = w.Fields.Select(f => f == field ? shared : f).ToArray() } : w).ToArray() } };
        var c = Changes.Create(sdk, weapon.Name, field.SemanticFieldId, "15", false);
        Assert.Throws<InvalidDataException>(() => Changes.Validate(sdk, c));
        var p = Project("SharedAmmo", Changes.Create(sdk, weapon.Name, field.SemanticFieldId, "15", true));
        Assert.Contains("allow_shared=true", new LuaGenerator(new ChangeService()).Generate(p, sdk));
    }
    [Fact] public void Not_applicable_and_ambiguous_identities_fail_closed()
    {
        foreach (var w in Ammo.Weapons.Where(w => w.EffectiveCapacity.Status == "NOT_APPLICABLE"))
            Assert.DoesNotContain(Catalog.Weapon(w.Name).Fields, f => f.Domain is "magazine" or "rounds");
        var gp = Ammo.Weapons.Single(w => w.Name == "GP-31 Grenade Pistol");
        Assert.True(gp.OrdinaryWritesBlocked); Assert.Equal("AMBIGUOUS_RUNTIME_IDENTITY", gp.EffectiveCapacity.Status);
        Assert.Equal(2, gp.ResourceValues!.Count); Assert.NotEmpty(gp.Diagnostics);
        foreach (var f in Catalog.Weapon(gp.Name).Fields.Where(f => f.Domain == "rounds"))
        { Assert.Equal(JsonValueKind.Null, f.CurrentDefault.ValueKind); Assert.Throws<InvalidDataException>(() => Changes.Create(Sdk, gp.Name, f.SemanticFieldId, "8", true)); }
    }
    [Theory] [InlineData("AR-11 Arbitrator", 45, 4)] [InlineData("AR/GL-21 One-Two", 40, 1)]
    public void Catalog_disagreements_keep_authoritative_baseline(string name, int runtime, int catalog)
    {
        Assert.Equal(runtime, Catalog.Field(name, "magazine.capacity").CurrentDefault.GetInt32());
        var d = Assert.Single(Ammo.Discrepancies, d => d.Weapon == name);
        Assert.Equal(runtime, d.Runtime.GetInt32()); Assert.Equal(catalog, d.Catalog.GetInt32());
    }
    [Fact] public async Task Ammo_autosave_reload_and_return_to_baseline()
    {
        using var env = new TestEnvironment(); var sdk = await Install(env); var w = env.Workspace();
        await w.CreateAsync(new("Ammo", "Tests", "mods/tests/ammo", "0.1.0"), sdk);
        await w.SetWeaponChangeAsync("P-113 Verdict", "magazine.capacity", "15", false);
        await w.SetWeaponChangeAsync("SG-8 Punisher", "rounds.feed_capacity_1", "12", false);
        var id = w.Project!.Id; w.CloseProject(); await w.OpenAsync(id);
        Assert.Equal(2, w.Project!.WeaponChanges.Count); Assert.Contains("hd2.fields.magazine.capacity", w.LuaPreview);
        await w.SetWeaponChangeAsync("P-113 Verdict", "magazine.capacity", "10.000", false);
        await w.SetWeaponChangeAsync("SG-8 Punisher", "rounds.feed_capacity_1", "8.0000000", false);
        w.CloseProject(); await w.OpenAsync(id); Assert.Empty(w.Project!.WeaponChanges);
    }
    [Fact] public async Task Existing_013_project_remains_pinned_until_explicit_rebind()
    {
        using var env = new TestEnvironment(); var old = await Install(env, old: true); var w = env.Workspace();
        await w.CreateAsync(new("Pinned", "Tests", "mods/tests/pinned", "0.1.0"), old);
        await w.SetWeaponChangeAsync("AR-23C Liberator Concussive", "weapon.fire_rate", "1100", false);
        var before = JsonSerializer.Serialize(w.Project!.WeaponChanges, JsonStorage.Options); var id = w.Project.Id;
        await Install(env); w.CloseProject(); await w.OpenAsync(id);
        Assert.Equal("0.13.0", w.Project!.SdkVersion); Assert.Null(w.Metadata!.PlayerAmmo);
        Assert.DoesNotContain(w.Metadata.PlayerWeapons!.Weapon("P-113 Verdict").Fields, f => f.Domain == "magazine");
        await w.RebindToInstalledSdkAsync(); Assert.Equal("0.14.0", w.Project.SdkVersion); Assert.NotNull(w.Metadata.PlayerAmmo);
        Assert.Equal(before, JsonSerializer.Serialize(w.Project.WeaponChanges, JsonStorage.Options));
        await w.SetWeaponChangeAsync("P-113 Verdict", "magazine.capacity", "15", false);
        w.CloseProject(); await w.OpenAsync(id); Assert.Equal(2, w.Project!.WeaponChanges.Count);
    }
    [Theory] [InlineData("missing")] [InlineData("schema")] [InlineData("version")] [InlineData("baseline")]
    [InlineData("writable")] [InlineData("shared")] [InlineData("zip-slip")] [InlineData("malformed")] [InlineData("preset")]
    public async Task Invalid_ammo_update_preserves_previous_sdk(string defect)
    {
        using var env = new TestEnvironment(); await Install(env, old: true);
        var node = JsonNode.Parse(Release014(PlayerWeaponAmmoCatalogReader.FileName))!;
        if (defect == "schema") node["schemaVersion"] = 99;
        if (defect == "version") node["hd2RuntimeVersion"] = "0.15.0";
        if (defect == "baseline") node["weapons"]![0]!["fields"]!["capacity"]!["value"] = 999;
        if (defect == "writable") node["weapons"]!.AsArray().Single(w => w!["name"]!.GetValue<string>() == "GP-31 Grenade Pistol")!["writable"] = true;
        if (defect == "shared") node["weapons"]![0]!["fields"]!["capacity"]!["shared"] = true;
        if (defect == "preset") node["weapons"]!.AsArray().First(w => w!["defaultMagazineOption"] != null)!["defaultMagazineOption"]!["name"] = null;
        env.GitHub.Archive = Archive(ammo: defect == "malformed" ? "{}"u8.ToArray() : Encoding.UTF8.GetBytes(node.ToJsonString()), omitAmmo: defect == "missing", extra: defect == "zip-slip" ? "../escape" : null);
        await Assert.ThrowsAnyAsync<Exception>(() => env.Cache.InstallAsync(FakeGitHub.MakeRelease("0.14.0", env.GitHub.Archive)));
        Assert.Equal("0.13.0", (await env.Cache.GetCurrentAsync()).Version);
        Assert.False(Directory.Exists(Path.GetDirectoryName(env.Paths.SdkFile("0.14.0"))));
    }
    [Fact] public async Task Fresh_offline_cache_contains_all_three_published_metadata_files()
    {
        using var env = new TestEnvironment(); File.Delete(env.Paths.CachePath("current.json")); env.GitHub.Offline = true;
        var sdk = await env.Cache.GetCurrentAsync(); Assert.Equal("0.25.1", sdk.Version); Assert.NotNull(sdk.PlayerAmmo);
        Assert.Equal(SdkCache.BundledAmmoCapabilities(), await File.ReadAllBytesAsync(env.Paths.CachePath(sdk.Version, PlayerWeaponAmmoCatalogReader.FileName)));
        Assert.NotNull((await env.Cache.GetVersionAsync(sdk.Version)).PlayerAmmo);
    }
    [Theory] [InlineData("VerdictMagazine")] [InlineData("PunisherDualFeed")]
    public async Task Ammo_samples_have_golden_Lua_deterministic_ZIPs_and_014_dependency(string name)
    {
        using var env = new TestEnvironment(); using var samples = JsonDocument.Parse(Fixture("player-weapon-ammo.json"));
        var sample = samples.RootElement.EnumerateArray().Single(s => s.GetProperty("name").GetString() == name);
        var weapon = sample.GetProperty("weapon").GetString()!;
        var p = Project(name, sample.GetProperty("changes").EnumerateArray().Select(c => Changes.Create(Sdk, weapon,
            c.GetProperty("field").GetString()!, c.GetProperty("value").GetRawText(), false)).ToArray());
        p.ExportDirectory = env.Paths.Exports;
        var generator = new LuaGenerator(new ChangeService()); var lua = generator.Generate(p, Sdk);
        Assert.Equal(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Golden", name + ".lua")).Replace("\r\n", "\n"), lua);
        p.WeaponChanges.Reverse(); Assert.Equal(lua, generator.Generate(p, Sdk));
        Assert.Contains("transaction=", lua); Assert.DoesNotContain("allow_shared", lua); Assert.DoesNotContain("0x", lua);
        var exporter = new ModExporter(generator); var file = await exporter.ExportAsync(p, Sdk); var first = await File.ReadAllBytesAsync(file);
        await exporter.ExportAsync(p, Sdk); Assert.Equal(first, await File.ReadAllBytesAsync(file));
        using var zip = ZipFile.OpenRead(file); Assert.Equal(8, zip.Entries.Count);
        Assert.All(zip.Entries, e => Assert.False(e.FullName.Contains("Capabilities") || e.FullName.Contains("hd2runtime.lua") || e.FullName.Contains("snapshot")));
        using var config = JsonDocument.Parse(zip.GetEntry("hd2runtime.json")!.Open());
        Assert.Equal("0.14.0", config.RootElement.GetProperty("requires").GetProperty("hd2runtime").GetProperty("min_version").GetString());
        using var source = new StreamReader(zip.GetEntry("src/addon.lua")!.Open()); Assert.Equal(lua, await source.ReadToEndAsync());
    }
    [Fact] public void Every_writable_ammo_field_generates_a_published_API_constant()
    {
        var api = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "public-api-0.14.0.lua"));
        foreach (var w in Catalog.Weapons)
        foreach (var f in w.Fields.Where(f => f.Editable && f.Domain is "magazine" or "rounds"))
        {
            var parts = f.SemanticFieldId.Split('.');
            Assert.Contains($"---@field {parts[1]} \"{f.SemanticFieldId}\"", api);
            var p = Project("AuditAmmo", Changes.Create(Sdk, w.Name, f.SemanticFieldId, f.CurrentDefault.GetRawText(), true));
            Assert.Contains("hd2.fields." + f.SemanticFieldId, new LuaGenerator(new ChangeService()).Generate(p, Sdk));
        }
    }
}
