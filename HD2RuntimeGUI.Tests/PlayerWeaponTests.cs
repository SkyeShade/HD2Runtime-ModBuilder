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

public sealed class PlayerWeaponTests
{
    private static readonly SdkMetadata Sdk = new MetadataReader().Read(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "metadata-0.13.0.json"))) with { PlayerWeapons = new PlayerWeaponCatalogReader().Read(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "player-weapons-0.13.0.json")), "0.13.0") };
    private static readonly WeaponChangeService Changes = new();
    private static ModProject Project(string name) => new() { DisplayName = name, Author = "SkyeShade", ResourceId = "mods/skyeshade/" + name.ToLowerInvariant(), ManagerGuid = ProjectIdentity.ManagerGuid("mods/skyeshade/" + name.ToLowerInvariant()), SdkVersion = Sdk.Version, ExportDirectory = Path.GetTempPath() };
    [Fact] public void Published_catalog_loads_all_identities_and_entries()
    {
        var c = Sdk.PlayerWeapons!;
        Assert.Equal(80, c.Weapons.Count); Assert.Equal(2667, c.Weapons.Sum(w => w.Fields.Count));
        Assert.Equal(73, c.Weapons.Count(w => !w.OrdinaryWritesBlocked)); Assert.Equal(48, c.FieldDefinitions.Count);
        Assert.Equal(38, c.FieldDefinitions.Count(f => f.Writable)); Assert.Equal(10, c.FieldDefinitions.Count(f => !f.Writable));
        Assert.Equal(3, c.FieldDefinitions.Count(f => f.Derived)); Assert.Equal(1772, c.Weapons.Sum(w => w.Fields.Count(f => f.Editable)));
        Assert.Equal(400, c.Field("AR-23C Liberator Concussive", "weapon.fire_rate").CurrentDefault.GetDouble());
        Assert.Equal(15, c.Weapons.SelectMany(w => w.Fields).Where(f => f.Editable && f.AffectsMultipleWeapons).Select(f => (f.Backing!.Settings, f.Backing.Group, f.Backing.Row)).Distinct().Count());
    }
    [Fact] public void Readonly_derived_and_ambiguous_fields_fail_closed()
    {
        foreach (var w in Sdk.PlayerWeapons!.Weapons)
        {
            foreach (var f in w.Fields.Where(f => !f.Editable))
                Assert.Throws<InvalidDataException>(() => Changes.Create(Sdk, w.Name, f.SemanticFieldId, "1", false));
            if (w.OrdinaryWritesBlocked) Assert.All(w.Fields, f => Assert.False(f.Editable));
        }
    }
    [Fact] public void Every_writable_capability_generates_a_published_semantic_constant()
    {
        var api = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "public-api-0.13.0.lua"));
        var constants = new Dictionary<string, string>(); string? domain = null;
        foreach (var line in api.Split('\n'))
        {
            var cls = System.Text.RegularExpressions.Regex.Match(line, @"^---@class HD2Fields_(\w+)");
            if (cls.Success) domain = cls.Groups[1].Value;
            else if (line.StartsWith("---@class")) domain = null;
            var field = System.Text.RegularExpressions.Regex.Match(line, "^---@field (\\w+) \"([^\"]+)\"");
            if (domain != null && field.Success) constants[domain + "." + field.Groups[1].Value] = field.Groups[2].Value;
        }
        foreach (var weapon in Sdk.PlayerWeapons!.Weapons)
        foreach (var field in weapon.Fields.Where(f => f.Editable))
        {
            var p = Project("Audit");
            p.WeaponChanges.Add(Changes.Create(Sdk, weapon.Name, field.SemanticFieldId, field.CurrentDefault.GetRawText(), true));
            var lua = new LuaGenerator(new ChangeService()).Generate(p, Sdk);
            var key = System.Text.RegularExpressions.Regex.Match(lua, @"field=hd2\.fields\.([\w.]+)").Groups[1].Value;
            Assert.True(constants.TryGetValue(key, out var semantic), "No published constant: " + key);
            Assert.Equal(field.SemanticFieldId, semantic);
        }
    }
    [Theory] [InlineData("schema")] [InlineData("version")] [InlineData("identity")] [InlineData("missing")] [InlineData("scalar")]
    public void Invalid_catalog_is_rejected(string kind)
    {
        var node = JsonNode.Parse(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "player-weapons-0.13.0.json")))!;
        if (kind == "schema") node["schemaVersion"] = 2;
        if (kind == "version") node["hd2RuntimeVersion"] = "0.12.0";
        if (kind == "identity") node["weapons"]![0]!["resolution"] = "DUPLICATE";
        if (kind == "missing") node.AsObject().Remove("weapons");
        if (kind == "scalar") node["weapons"]![0]!["fields"]![0]!["currentDefault"] = new JsonObject();
        Assert.ThrowsAny<Exception>(() => new PlayerWeaponCatalogReader().Read(Encoding.UTF8.GetBytes(node.ToJsonString()), "0.13.0"));
    }
    [Fact] public void Shared_approval_is_required_even_for_unnamed_consumers()
    {
        foreach (var named in new[] { true, false })
        {
            var w = Sdk.PlayerWeapons!.Weapons.First(w => w.Fields.Any(f => f.Editable && f.AffectsMultipleWeapons && (f.SharedWithWeapons.Count > 0) == named));
            var f = w.Fields.First(f => f.Editable && f.AffectsMultipleWeapons && (f.SharedWithWeapons.Count > 0) == named);
            var c = Changes.Create(Sdk, w.Name, f.SemanticFieldId, "5", false);
            Assert.Throws<InvalidDataException>(() => Changes.Validate(Sdk, c));
            c = Changes.Create(Sdk, w.Name, f.SemanticFieldId, "5", true); Changes.Validate(Sdk, c);
            var p = Project("Shared"); p.WeaponChanges.Add(c);
            Assert.Contains("allow_shared=true", new LuaGenerator(new ChangeService()).Generate(p, Sdk));
            c.SharedAcknowledged = false;
            Assert.Throws<InvalidDataException>(() => new LuaGenerator(new ChangeService()).Generate(p, Sdk));
        }
    }
    [Theory]
    [InlineData("Concussive1100", "AR-23C Liberator Concussive")]
    [InlineData("VerdictFlatTrajectory", "P-113 Verdict")]
    [InlineData("ReprimandFlatTrajectory", "SMG-32 Reprimand")]
    public async Task Sample_mod_golden_Lua_and_deterministic_packages(string name, string weapon)
    {
        using var env = new TestEnvironment(); var p = Project(name); p.ExportDirectory = env.Paths.Exports;
        if (name == "Concussive1100") p.WeaponChanges.Add(Changes.Create(Sdk, weapon, "weapon.fire_rate", "1100", false));
        else
        {
            p.WeaponChanges.Add(Changes.Create(Sdk, weapon, "projectile.drag", "0.1", false));
            p.WeaponChanges.Add(Changes.Create(Sdk, weapon, "projectile.gravity", "0.2", false));
            Assert.Equal(285, Sdk.PlayerWeapons!.Field(weapon, "projectile.velocity").CurrentDefault.GetDouble());
            Assert.Equal(15, Sdk.PlayerWeapons.Field(weapon, "projectile.mass").CurrentDefault.GetDouble());
        }
        var generator = new LuaGenerator(new ChangeService()); var lua = generator.Generate(p, Sdk);
        Assert.Equal(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Golden", name + ".lua")).Replace("\r\n", "\n"), lua);
        p.WeaponChanges.Reverse(); Assert.Equal(lua, generator.Generate(p, Sdk));
        Assert.Contains("hd2.ensure(", lua); Assert.DoesNotContain("allow_shared", lua); Assert.DoesNotContain("0x", lua);
        var exporter = new ModExporter(generator); var zipPath = await exporter.ExportAsync(p, Sdk); var first = await File.ReadAllBytesAsync(zipPath);
        await exporter.ExportAsync(p, Sdk); Assert.Equal(first, await File.ReadAllBytesAsync(zipPath));
        using var zip = ZipFile.OpenRead(zipPath); Assert.Equal(8, zip.Entries.Count);
        Assert.All(zip.Entries, entry => Assert.DoesNotContain("hd2runtime.lua", entry.FullName));
        using var config = JsonDocument.Parse(zip.GetEntry("hd2runtime.json")!.Open());
        Assert.Equal("0.13.0", config.RootElement.GetProperty("requires").GetProperty("hd2runtime").GetProperty("min_version").GetString());
        Assert.Equal(1, config.RootElement.GetProperty("requires").GetProperty("hd2runtime").GetProperty("api").GetInt32());
        await env.Store.SaveAsync(p); var loaded = await new JsonProjectStore(env.Paths).LoadAsync(p.Id);
        Assert.Equal(lua, generator.Generate(loaded, Sdk));
    }
    [Fact] public void New_sdk_baselines_are_reviewed_without_rewriting_project_values()
    {
        var c = Changes.Create(Sdk, "P-113 Verdict", "projectile.drag", "0.1", false);
        var old = c.ExpectedValue.GetRawText();
        var catalog = Sdk.PlayerWeapons!;
        var changed = catalog with { Weapons = catalog.Weapons.Select(w => w.Name != c.Weapon ? w : w with { Fields = w.Fields.Select(f => f.SemanticFieldId != c.SemanticFieldId ? f : f with { CurrentDefault = JsonSerializer.SerializeToElement(2) }).ToArray() }).ToArray() };
        var issues = Changes.Review(Sdk with { PlayerWeapons = changed }, [c]);
        Assert.Contains("baseline changed", Assert.Single(issues).Message); Assert.Equal(old, c.ExpectedValue.GetRawText()); Assert.Equal(0.1, c.DesiredValue.GetDouble());
        Assert.Single(Changes.Review(Sdk with { PlayerWeapons = catalog with { Weapons = [] } }, [c]));
    }
    [Fact] public void Changed_shared_scope_requires_renewed_approval()
    {
        var c = Changes.Create(Sdk, "AR-23 Liberator", "projectile.drag", "0.1", true);
        c.AcknowledgedWriteScope = "old_scope";
        Assert.Throws<InvalidDataException>(() => Changes.Validate(Sdk, c));
    }
    [Fact] public void Shared_conflicts_and_duplicate_overrides_are_rejected()
    {
        var p = Project("Conflict");
        p.WeaponChanges.Add(Changes.Create(Sdk, "AR-23 Liberator", "projectile.drag", "0.1", true));
        p.WeaponChanges.Add(Changes.Create(Sdk, "AR-23A Liberator Carbine", "projectile.drag", "0.2", true));
        Assert.Throws<InvalidDataException>(() => new LuaGenerator(new ChangeService()).Generate(p, Sdk));
        p.WeaponChanges[1] = Changes.Create(Sdk, "AR-23 Liberator", "projectile.drag", "0.1", true);
        Assert.Throws<InvalidDataException>(() => new LuaGenerator(new ChangeService()).Generate(p, Sdk));
    }
    [Fact] public void Release_parser_identifies_all_four_assets_without_downloading_runtime()
    {
        var names = new[] { "HD2Runtime-0.13.0-runtime.zip", "HD2Runtime-0.13.0-sdk.zip", "HD2Runtime-ModTemplate-0.13.0.zip", "HD2Runtime-0.13.0-example-projects.zip" };
        var json = JsonSerializer.SerializeToUtf8Bytes(new[] { new { draft = false, prerelease = false, tag_name = "v0.13.0", html_url = "https://github.com/SkyeShade/HD2Runtime/releases/tag/v0.13.0", assets = names.Select(name => new { name, size = 123, content_type = "application/zip", browser_download_url = "https://github.com/SkyeShade/HD2Runtime/releases/download/v0.13.0/" + name }) } });
        var release = Assert.Single(HD2RuntimeGUI.Core.GitHub.GitHubReleaseClient.ParseReleases(json));
        Assert.Equal(4, release.Artifacts.Count); Assert.Equal(names[1], release.AssetName);
    }
    [Fact] public async Task Fresh_install_and_failed_update_preserve_both_catalog_and_metadata()
    {
        using var env = new TestEnvironment();
        byte[] Archive(bool corrupt)
        {
            using var output = new MemoryStream();
            using (var zip = new ZipArchive(output, ZipArchiveMode.Create, true))
            {
                using (var s = zip.CreateEntry("metadata.json").Open()) s.Write(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "metadata-0.13.0.json")));
                using (var s = zip.CreateEntry(PlayerWeaponCatalogReader.FileName).Open()) s.Write(corrupt ? "{}"u8.ToArray() : File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "player-weapons-0.13.0.json")));
            }
            return output.ToArray();
        }
        env.GitHub.Archive = Archive(false); var release = FakeGitHub.MakeRelease("0.13.0", env.GitHub.Archive);
        Assert.Equal(80, (await env.Cache.InstallAsync(release)).PlayerWeapons!.Weapons.Count);
        Assert.Equal(2667, (await env.Cache.GetCurrentAsync()).PlayerWeapons!.Summary.FieldInstances);
        env.GitHub.Archive = Archive(true); release = FakeGitHub.MakeRelease("0.13.0", env.GitHub.Archive);
        await Assert.ThrowsAnyAsync<Exception>(() => env.Cache.InstallAsync(release));
        Assert.Equal(80, (await env.Cache.GetCurrentAsync()).PlayerWeapons!.Weapons.Count);
    }
}
