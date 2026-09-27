using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Nodes;
using HD2RuntimeGUI.Core.Generation;
using HD2RuntimeGUI.Core.Metadata;
using HD2RuntimeGUI.Core.Models;
using HD2RuntimeGUI.Core.Services;
using HD2RuntimeGUI.Core.Storage;
using Xunit;

namespace HD2RuntimeGUI.Tests;

public sealed class CompositionTests
{
    private static readonly ProjectileChangeService Changes = new();
    private static readonly ProjectileReference Verdict = new("P-113 Verdict", "primary");
    private const string Jar = "JAR-5 Dominator";
    private static byte[] Archive(string? version = null, string? omit = null)
    {
        var files = SdkCache.BundledComposition().ToDictionary(p => p.Key, p => p.Value);
        files.Add("metadata.json", SdkCache.BundledMetadata()); files.Add(PlayerWeaponCatalogReader.FileName, SdkCache.BundledCapabilities()); files.Add(PlayerWeaponAmmoCatalogReader.FileName, SdkCache.BundledAmmoCapabilities());
        using var output = new MemoryStream();
        using (var zip = new ZipArchive(output, ZipArchiveMode.Create, true))
            foreach (var (name, bytes) in files.Where(p => p.Key != omit))
            {
                var json = JsonNode.Parse(bytes)!;
                if (version != null) json[name == "metadata.json" ? "runtime_version" : "hd2RuntimeVersion"] = version;
                using var writer = new StreamWriter(zip.CreateEntry(name).Open()); writer.Write(json.ToJsonString());
            }
        return output.ToArray();
    }
    private static async Task<SdkMetadata> Install(TestEnvironment env, string version = "0.15.0")
    {
        env.GitHub.Archive = Archive(version); return await env.Cache.InstallAsync(FakeGitHub.MakeRelease(version, env.GitHub.Archive));
    }
    private static async Task<BuilderWorkspace> Workspace(TestEnvironment env)
    {
        var sdk = await Install(env); var w = env.Workspace(); await w.CreateAsync(new("ProjectileSwap", "Tests", "mods/tests/projectile_swap", "0.1.0"), sdk); return w;
    }
    [Fact] public async Task Published_015_graphs_load_counts_and_all_reference_fields()
    {
        using var env = new TestEnvironment(); var sdk = await Install(env); var c = sdk.PlayerWeapons!; var g = sdk.Composition!;
        Assert.Equal(80, c.Weapons.Count); Assert.Equal(3094, c.Summary.FieldInstances); Assert.Equal(48, c.Summary.WritableSemanticFieldDefinitions); Assert.Equal(14, c.Summary.ReadOnlySemanticFieldDefinitions);
        Assert.Equal(66, g.Projectiles.Weapons.Count(w => w.Attacks.Count > 0)); Assert.Equal(67, g.Projectiles.Weapons.Sum(w => w.Attacks.Count));
        Assert.Equal(45, g.Projectiles.Weapons.Sum(w => w.Attacks.Count(a => a.WritableReferenceSwap)));
        Assert.Equal(67, c.Weapons.Sum(w => w.Fields.Count(f => f.Type == "projectile_reference")));
        foreach (var name in PlayerWeaponCompositionReader.FileNames) Assert.True(File.Exists(env.Paths.CachePath(sdk.Version, name)));
        Assert.NotNull((await env.Cache.GetVersionAsync("0.15.0")).Composition);
    }
    [Fact] public async Task Sources_are_only_metadata_approved_plain_semantic_handles()
    {
        using var env = new TestEnvironment(); var sdk = await Install(env); var sources = Changes.Sources(sdk, Jar, "primary");
        Assert.Equal(45, sources.Count); Assert.Contains(Verdict, sources); Assert.Contains(new("SG-20 Halt", "feed_primary"), sources);
        Assert.DoesNotContain(new("SG-20 Halt", "feed_alternate"), sources); Assert.DoesNotContain(new("CB-9 Exploding Crossbow", "primary"), sources);
    }
    [Theory] [InlineData("CB-9 Exploding Crossbow", "primary")] [InlineData("SG-20 Halt", "feed_alternate")]
    public async Task Explosive_and_status_sources_and_targets_are_blocked(string weapon, string role)
    {
        using var env = new TestEnvironment(); var sdk = await Install(env);
        Assert.Throws<InvalidDataException>(() => Changes.Create(sdk, Jar, "primary", new(weapon, role)));
        Assert.Throws<InvalidDataException>(() => Changes.Create(sdk, weapon, role, Verdict)); Assert.Empty(Changes.Sources(sdk, weapon, role));
    }
    [Theory] [InlineData("shared")] [InlineData("ambiguous")] [InlineData("ownership")]
    public async Task Unsafe_target_evidence_fails_closed(string kind)
    {
        using var env = new TestEnvironment(); var sdk = await Install(env); var catalog = sdk.PlayerWeapons!;
        var weapon = catalog.Weapon(Jar);
        var changed = weapon with { OrdinaryWritesBlocked = kind == "ambiguous", Fields = weapon.Fields.Select(f => f.Type == "projectile_reference" ? f with { AffectsMultipleWeapons = kind == "shared" } : f).ToArray() };
        var graph = sdk.Composition!;
        if (kind == "ownership") graph = graph with { Projectiles = graph.Projectiles with { Weapons = graph.Projectiles.Weapons.Select(w => w.Weapon == Jar ? w with { Attacks = w.Attacks.Select(a => a with { TargetOwnershipProven = false }).ToArray() } : w).ToArray() } };
        sdk = sdk with { PlayerWeapons = catalog with { Weapons = catalog.Weapons.Select(w => w.Name == Jar ? changed : w).ToArray() }, Composition = graph };
        Assert.Throws<InvalidDataException>(() => Changes.Create(sdk, Jar, "primary", Verdict));
    }
    [Fact] public async Task Autosave_reload_and_return_to_original_remove_override()
    {
        using var env = new TestEnvironment(); var w = await Workspace(env); await w.SetProjectileAsync(Jar, "primary", Verdict);
        var saved = Assert.Single(w.Project!.ProjectileChanges); Assert.Equal(Verdict, saved.ReplacementProjectile); Assert.Null(w.BuildError);
        var id = w.Project.Id; await w.OpenAsync(id); Assert.Equal(Verdict, Assert.Single(w.Project.ProjectileChanges).ReplacementProjectile);
        var json = await File.ReadAllTextAsync(env.Paths.ProjectFile(id)); Assert.DoesNotContain("projectileType", json); Assert.DoesNotContain("offset", json); Assert.DoesNotContain("recordType", json);
        await w.SetProjectileAsync(Jar, "primary", new(Jar, "primary")); Assert.Empty(w.Project.ProjectileChanges);
        await w.OpenAsync(id); Assert.Empty(w.Project.ProjectileChanges); Assert.DoesNotContain("hd2.fields.attack", w.LuaPreview);
    }
    [Fact] public async Task Semantic_Lua_and_ZIP_are_deterministic_with_runtime_dependency()
    {
        using var env = new TestEnvironment(); var w = await Workspace(env); await w.SetProjectileAsync(Jar, "primary", Verdict);
        var lua = w.LuaPreview; Assert.Contains("hd2.ensure", lua); Assert.Contains("field=hd2.fields.attack.projectile", lua);
        Assert.Contains("expect=hd2.weapon('JAR-5 Dominator'):attack('primary'):projectile()", lua);
        Assert.Contains("value=hd2.weapon('P-113 Verdict'):attack('primary'):projectile()", lua);
        Assert.DoesNotContain("177", lua); Assert.DoesNotContain("ffi", lua); Assert.DoesNotContain("offset", lua); Assert.DoesNotContain("allow_shared", lua);
        Assert.Equal(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Golden", "projectile-swap.lua")).Replace("\r\n", "\n"), lua);
        await w.ExportAsync(); var bytes = await File.ReadAllBytesAsync(w.LastExport!); await w.ExportAsync(); Assert.Equal(bytes, await File.ReadAllBytesAsync(w.LastExport!));
        using var zip = ZipFile.OpenRead(w.LastExport!); Assert.Equal(8, zip.Entries.Count);
        Assert.DoesNotContain(zip.Entries, e => e.Name.Contains("Graph") || e.Name.Contains("Capabilities") || e.Name.Contains("hd2runtime.lua"));
        using var source = new StreamReader(zip.Entries.Single(e => e.FullName.EndsWith("src/addon.lua")).Open()); Assert.Contains(lua, source.ReadToEnd());
        using var dependency = new StreamReader(zip.GetEntry("hd2runtime.json")!.Open());
        using var manifest = JsonDocument.Parse(dependency.ReadToEnd()); Assert.Equal("0.15.0", manifest.RootElement.GetProperty("requires").GetProperty("hd2runtime").GetProperty("min_version").GetString());
    }
    [Fact] public async Task Disabled_reference_not_emitted_and_reset_weapon_clears_both_types()
    {
        using var env = new TestEnvironment(); var w = await Workspace(env); await w.SetProjectileAsync(Jar, "primary", Verdict);
        await w.SetWeaponChangeAsync(Jar, "weapon.fire_rate", "300", false); await w.ToggleProjectileAsync(w.Project!.ProjectileChanges[0].Id);
        Assert.DoesNotContain("fields.attack", w.LuaPreview); await w.ResetWeaponsAsync(Jar); Assert.Empty(w.Project.ProjectileChanges); Assert.Empty(w.Project.WeaponChanges);
    }
    [Fact] public async Task Install_preserves_pin_and_explicit_rebind_keeps_semantic_change()
    {
        using var env = new TestEnvironment(); var w = await Workspace(env); await w.SetProjectileAsync(Jar, "primary", Verdict);
        await Install(env, "0.15.1"); await w.OpenAsync(w.Project!.Id); Assert.Equal("0.15.0", w.Metadata!.Version);
        await w.RebindToInstalledSdkAsync(); Assert.Equal("0.15.1", w.Project.SdkVersion); Assert.Equal("0.15.0", w.Project.ProjectileChanges[0].BaselineSdkVersion); Assert.Null(w.BuildError);
    }
    [Fact] public async Task Older_alias_project_stays_pinned_then_rebinds_to_015_without_value_loss()
    {
        using var env = new TestEnvironment();
        env.GitHub.Archive = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "sdk-0.14.1.zip"));
        var old = await env.Cache.InstallAsync(FakeGitHub.MakeRelease("0.14.1", env.GitHub.Archive));
        var w = env.Workspace(); await w.CreateAsync(new("OldAliases", "Tests", "mods/tests/old_aliases", "0.1.0"), old);
        var change = new WeaponChangeService().Create(old, Verdict.Weapon, "magazine.capacity", "15", false); change.SemanticFieldId = "weapon.capacity";
        w.Project!.WeaponChanges.Add(change); await env.Store.SaveAsync(w.Project);
        await Install(env); await w.OpenAsync(w.Project.Id); Assert.Equal("0.14.1", w.Project.SdkVersion); Assert.Null(w.Metadata!.Composition);
        await w.RebindToInstalledSdkAsync(); Assert.Null(w.BuildError); Assert.Equal("magazine.capacity", Assert.Single(w.WeaponGroups).FieldId);
        Assert.Equal("0.14.1", change.BaselineSdkVersion); Assert.Contains("value=15", w.LuaPreview);
    }
    [Fact] public async Task Scalar_service_cannot_author_raw_projectile_references()
    {
        using var env = new TestEnvironment(); var sdk = await Install(env);
        Assert.Throws<InvalidDataException>(() => new WeaponChangeService().Create(sdk, Jar, "attack.primary.projectile", "15", false));
    }
    [Fact] public async Task Duplicate_project_copies_semantic_override_and_noop_import_is_cleaned()
    {
        using var env = new TestEnvironment(); var w = await Workspace(env); await w.SetProjectileAsync(Jar, "primary", Verdict);
        var source = w.Project!; await w.DuplicateAsync(source.Id, new("Duplicate", "Tests", "mods/tests/duplicate", "0.1.0"));
        Assert.Equal(Verdict, Assert.Single(w.Project!.ProjectileChanges).ReplacementProjectile); Assert.NotEqual(source.ProjectileChanges[0].Id, w.Project.ProjectileChanges[0].Id);
        w.Project.ProjectileChanges = [Changes.Create(w.Metadata!, Jar, "primary", new(Jar, "primary"))]; await env.Store.SaveAsync(w.Project);
        await w.OpenAsync(w.Project.Id); Assert.Empty(w.Project.ProjectileChanges); Assert.Empty((await env.Store.LoadAsync(w.Project.Id)).ProjectileChanges);
    }
    [Theory] [InlineData("missing")] [InlineData("baseline")] [InlineData("readonly")] [InlineData("class")] [InlineData("source")] [InlineData("source_baseline")]
    public async Task Rebind_detects_changed_selector_or_source(string mutation)
    {
        using var env = new TestEnvironment(); var sdk = await Install(env); var change = Changes.Create(sdk, Jar, "primary", Verdict); var g = sdk.Composition!;
        sdk = sdk with { Composition = g with { Projectiles = g.Projectiles with { Weapons = g.Projectiles.Weapons.Select(w =>
            w.Weapon == Jar ? w with { Attacks = mutation == "missing" ? [] : w.Attacks.Select(a => mutation switch {
                "baseline" => a with { ProjectileType = a.ProjectileType + 1 }, "readonly" => a with { WritableReferenceSwap = false },
                "class" => a with { CompatibilityClass = "explosive" }, _ => a }).ToArray() }
            : w.Weapon == Verdict.Weapon && mutation == "source" ? w with { Attacks = w.Attacks.Select(a => a with { SourceIdentityResolvable = false }).ToArray() }
            : w.Weapon == Verdict.Weapon && mutation == "source_baseline" ? w with { Attacks = w.Attacks.Select(a => a with { ProjectileType = a.ProjectileType + 1 }).ToArray() } : w).ToArray() } } };
        Assert.Throws<InvalidDataException>(() => Changes.Validate(sdk, change));
    }
    [Fact] public async Task Magazine_fire_mode_and_terminal_views_have_only_readonly_evidence()
    {
        using var env = new TestEnvironment(); var sdk = await Install(env); var g = sdk.Composition!;
        Assert.Equal(19, g.Magazines.Weapons.Count(w => w.DefaultOption != null)); Assert.Equal(52, sdk.PlayerWeapons!.Summary.Composition!.Magazine.NativeOptionIdentities);
        Assert.All(g.Magazines.Weapons.Where(w => w.DefaultOption != null), w => { Assert.False(w.DefaultOption!.Writable); Assert.False(w.DefaultOption.AmmoValueOwnerProven); });
        Assert.All(g.FireModes.Weapons, w => { Assert.False(w.Writable); Assert.Null(w.AllowedModes); }); Assert.Equal(new[] { 1, 2, 3, 5 }, g.FireModes.Weapons.Select(w => w.PrimaryFireModeNativeValue).Distinct().Order());
        var actions = g.TerminalActions.Weapons.SelectMany(w => w.Attacks).SelectMany(a => a.Actions).ToArray(); Assert.Equal(134, actions.Length); Assert.All(actions, a => Assert.False(a.Writable));
        Assert.Equal(13, actions.Count(a => a.Phase == "impact" && a.LinkedExplosionRecord)); Assert.Equal(5, actions.Count(a => a.Phase == "expiry" && a.LinkedExplosionRecord));
    }
    [Theory] [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)]
    public async Task Missing_graph_rejects_update_and_preserves_old_cache(int index)
    {
        using var env = new TestEnvironment(); env.GitHub.Archive = Archive(omit: PlayerWeaponCompositionReader.FileNames[index]);
        await Assert.ThrowsAsync<InvalidDataException>(() => env.Cache.InstallAsync(FakeGitHub.MakeRelease("0.15.0", env.GitHub.Archive)));
        Assert.Equal("0.5.1", (await env.Cache.GetCurrentAsync()).Version);
    }
    [Theory] [InlineData("schemaVersion")] [InlineData("hd2RuntimeVersion")] [InlineData("weapons")]
    public async Task Malformed_composition_is_rejected(string property)
    {
        using var env = new TestEnvironment(); var sdk = await Install(env); var files = SdkCache.BundledComposition().ToDictionary(p => p.Key, p => p.Value); var name = PlayerWeaponCompositionReader.FileNames[0];
        var json = JsonNode.Parse(files[name])!; json[property] = property == "schemaVersion" ? JsonValue.Create(999) : property == "weapons" ? new JsonArray() : JsonValue.Create("0.14.0");
        files[name] = JsonSerializer.SerializeToUtf8Bytes(json);
        Assert.ThrowsAny<Exception>(() => new PlayerWeaponCompositionReader().Read(files, sdk.PlayerWeapons!));
    }
}
