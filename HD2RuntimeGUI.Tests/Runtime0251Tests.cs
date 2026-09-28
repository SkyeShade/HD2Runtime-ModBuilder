using System.Text;
using System.Text.Json.Nodes;
using HD2RuntimeGUI.Core.GameAssets;
using HD2RuntimeGUI.Core.Metadata;
using Xunit;

namespace HD2RuntimeGUI.Tests;

// HD2Runtime 0.25.1 (published): stratagem icon identity (uiIcon + uiIconContract), the bundled offline SDK, and automatic
// game-icon import with stale-cache refresh.
public sealed class Runtime0251Tests
{
    private static JsonNode Stratagems() => JsonNode.Parse(SdkFixtures.Entry("0.25.1", StratagemCatalogReader.FileName))!;
    private static StratagemCatalog Read(JsonNode j) => new StratagemCatalogReader().Read(Encoding.UTF8.GetBytes(j.ToJsonString()));

    [Fact] public async Task Published_0251_stratagems_carry_84_resolved_icon_keys()
    {
        using var e = new TestEnvironment(); var sdk = await SdkFixtures.Install(e, "0.25.1"); var c = sdk.Stratagems!;
        Assert.Equal("0.25.1", sdk.Version); Assert.Equal(95, c.Stratagems.Count());
        Assert.All(c.Stratagems, s => Assert.NotNull(s.UiIcon));
        Assert.Equal(84, c.Stratagems.Count(s => s.UiIcon!.State == "resolved")); Assert.Equal(4, c.Stratagems.Count(s => s.UiIcon!.State == "empty_template"));
        Assert.Equal(5, c.Stratagems.Count(s => s.UiIcon!.State == "unbound")); Assert.Equal(2, c.Stratagems.Count(s => s.UiIcon!.State == "no_native_root"));
        // Only resolved keys drive an icon; every other state keeps the glyph and explains why.
        Assert.Equal(84, c.Stratagems.Count(s => GameIconStore.StratagemIconKey(s) != null));
        Assert.All(c.Stratagems.Where(s => s.UiIcon!.State != "resolved"), s => Assert.False(string.IsNullOrWhiteSpace(s.UiIcon!.FallbackReason(c.UiIconContract))));
        Assert.Null(GameIconStore.StratagemIconKey(c.Stratagems.Single(s => s.Name == "M-1000 Maxigun"))); // empty template, key published
        Assert.Equal("StratagemMaxigun", c.Stratagems.Single(s => s.Name == "M-1000 Maxigun").UiIcon!.IconKey);
        Assert.Equal("no_native_root", c.Stratagems.Single(s => s.Name == "SG-88 Break-Action Shotgun").UiIcon!.Blocker!.Kind);
        var contract = c.UiIconContract!;
        Assert.Equal(StratagemUiIcon.Library0, contract.Library); Assert.Equal("DEDA7000592065F14D79D41C5D153EC1041FFF7A58D6A607DEF232ADD7702EAE", contract.LibrarySha256);
        Assert.False(contract.ArtworkPublished); Assert.Equal(19, contract.EmptyTemplates.Length);
    }

    [Theory]
    [InlineData("empty-not-listed")] [InlineData("resolved-empty")] [InlineData("artwork")] [InlineData("library")]
    public void Icon_contract_inconsistencies_fail_closed(string fault)
    {
        var j = Stratagems(); var roots = j["stratagems"]!.AsArray();
        switch (fault)
        {
            case "empty-not-listed": roots.First(r => (string)r!["uiIcon"]!["state"]! == "empty_template")!["uiIcon"]!["iconKey"] = "StratagemNotEmpty"; break;
            case "resolved-empty": roots.First(r => (string)r!["uiIcon"]!["state"]! == "resolved")!["uiIcon"]!["iconKey"] = "StratagemMaxigun"; break;
            case "artwork": j["uiIconContract"]!["artworkPublished"] = true; break;
            case "library": j["uiIconContract"]!["library"] = "content/ui/elsewhere"; break;
        }
        Assert.Throws<InvalidDataException>(() => Read(j));
    }
    [Fact] public void Unknown_icon_contract_is_unsupported()
    {
        var j = Stratagems(); j["uiIconContract"]!["contract"] = "hd2runtime.stratagem.ui_icon.v2";
        Assert.Throws<UnsupportedSdkException>(() => Read(j));
    }

    [Fact] public async Task Update_0250_to_0251_shows_icons_after_rebind_without_changing_lua()
    {
        using var e = new TestEnvironment(); var old = await SdkFixtures.Install(e, "0.25.0"); var w = e.Workspace();
        await w.CreateAsync(new("Icons", "Tests", "mods/tests/icons_0251", "0.1.0"), old);
        await w.SetWeaponChangeAsync("AR-23 Liberator", "weapon.fire_rate", "700", false);
        Assert.All(w.Metadata!.Stratagems!.Stratagems, s => Assert.Null(s.UiIcon)); var lua = w.LuaPreview;
        await SdkFixtures.Install(e, "0.25.1"); await w.OpenAsync(w.Project!.Id);
        Assert.Equal("0.25.0", w.Project!.SdkVersion); Assert.Equal(lua, w.LuaPreview); // pinned until an explicit rebind
        await w.RebindToInstalledSdkAsync();
        Assert.Equal("0.25.1", w.Project.SdkVersion); Assert.Null(w.BuildError);
        Assert.Equal(84, w.Metadata!.Stratagems!.Stratagems.Count(s => GameIconStore.StratagemIconKey(s) != null));
        Assert.Equal(lua.Replace("0.25.0", "0.25.1"), w.LuaPreview); // same operations
    }

    [Theory] [InlineData("0.24.0")] [InlineData("0.23.2")] [InlineData("0.21.0")]
    public async Task Older_sdks_still_load_and_publish_no_stratagem_icons(string version)
    {
        using var e = new TestEnvironment(); var sdk = await SdkFixtures.Install(e, version);
        Assert.All(sdk.Stratagems!.Stratagems, s => Assert.Null(GameIconStore.StratagemIconKey(s))); Assert.Null(sdk.Stratagems.UiIconContract);
    }

    // ---- Automatic icon import and cache refresh ----
    private static string FakeGame(TestEnvironment e)
    {
        var data = Path.Combine(e.Paths.Root, "game-fat"); Directory.CreateDirectory(data);
        File.WriteAllBytes(Path.Combine(data, "9ba626afa44a3aa3"), ReadabilityPassTests.Archive([]));
        File.WriteAllBytes(Path.Combine(data, "0123456789abcdef"), ReadabilityPassTests.Archive(ReadabilityPassTests.Libraries()));
        return data;
    }

    [Fact] public async Task Auto_import_runs_once_when_an_install_is_found_and_notifies_open_pages()
    {
        using var e = new TestEnvironment(); var data = FakeGame(e);
        var store = new GameIconStore(e.Paths) { Detect = () => data }; var changes = 0; store.Changed += () => changes++;
        Assert.Equal("No game icons are imported yet.", store.AutoImportPlan()!.Value.Reason);
        Assert.True(await store.AutoImportAsync()); Assert.True(changes >= 2); Assert.False(store.Importing); Assert.Null(store.LastError);
        Assert.Equal(GameIconStore.ManifestFormat, store.Manifest!.FormatVersion); Assert.Equal(GameIconStore.GameSignature(data), store.Manifest.GameSignature);
        // A valid cache is never re-extracted.
        var imported = store.Manifest.ImportedAt;
        Assert.Null(store.AutoImportPlan()); Assert.False(await store.AutoImportAsync()); Assert.Equal(imported, store.Manifest.ImportedAt);
        Assert.Null(new GameIconStore(e.Paths) { Detect = () => data }.AutoImportPlan());
        // No install found: nothing to do, glyphs stay.
        using var empty = new TestEnvironment(); Assert.Null(new GameIconStore(empty.Paths) { Detect = () => null }.AutoImportPlan());
    }

    [Fact] public async Task Stale_icon_caches_are_refreshed_automatically()
    {
        using var e = new TestEnvironment(); var data = FakeGame(e); var store = new GameIconStore(e.Paths) { Detect = () => data };
        await store.AutoImportAsync(); var first = store.Manifest!.ImportedAt;
        // The game was updated (archive index changed).
        File.SetLastWriteTimeUtc(Path.Combine(data, "9ba626afa44a3aa3"), DateTime.UtcNow.AddMinutes(5));
        Assert.Equal("The game files changed since the icons were imported.", store.AutoImportPlan()!.Value.Reason);
        Assert.True(await store.AutoImportAsync()); Assert.True(store.Manifest!.ImportedAt > first); Assert.Null(store.AutoImportPlan());
        // Missing icon files.
        File.Delete(Directory.GetFiles(Path.Combine(store.Folder, GameIconStore.Stratagem), "*.svg")[0]);
        Assert.Equal("Imported icon files are missing.", new GameIconStore(e.Paths) { Detect = () => data }.AutoImportPlan()!.Value.Reason);
        // A format 1 cache (older GUI, no change detection) is refreshed once.
        await store.ImportAsync(data);
        var manifestFile = Path.Combine(store.Folder, "manifest.json"); var json = JsonNode.Parse(File.ReadAllText(manifestFile))!;
        json["formatVersion"] = 1; json.AsObject().Remove("gameSignature"); File.WriteAllText(manifestFile, json.ToJsonString());
        Assert.Equal("The icon cache predates game-change detection.", new GameIconStore(e.Paths) { Detect = () => data }.AutoImportPlan()!.Value.Reason);
    }

    [Fact] public async Task Turning_automatic_import_off_is_respected_and_failures_keep_glyphs()
    {
        using var e = new TestEnvironment(); var data = FakeGame(e); var store = new GameIconStore(e.Paths) { Detect = () => data };
        store.Clear(); store.DisableAutoImport();
        Assert.Null(store.AutoImportPlan()); Assert.False(await store.AutoImportAsync()); Assert.Null(store.Manifest);
        store.EnableAutoImport(); Assert.NotNull(store.AutoImportPlan());
        // A broken game archive: the import fails, nothing is written, the error is reported and authoring is unaffected.
        File.WriteAllBytes(Path.Combine(data, "0123456789abcdef"), [1, 2, 3]);
        Assert.False(await store.AutoImportAsync()); Assert.NotNull(store.LastError); Assert.Null(store.Manifest); Assert.False(store.Importing);
        // The previous good cache survives a failed refresh.
        var good = FakeGame(e); await store.ImportAsync(good); var before = store.Manifest!.ImportedAt;
        File.WriteAllBytes(Path.Combine(good, "0123456789abcdef"), [1, 2, 3]); File.SetLastWriteTimeUtc(Path.Combine(good, "9ba626afa44a3aa3"), DateTime.UtcNow.AddMinutes(9));
        Assert.False(await store.AutoImportAsync()); Assert.Equal(before, store.Manifest!.ImportedAt); Assert.True(store.Count(GameIconStore.Stratagem) > 0);
    }

    [Fact] public void Steam_libraries_are_read_from_libraryfolders_vdf()
    {
        const string vdf = "\"libraryfolders\"\n{\n\t\"0\"\n\t{\n\t\t\"path\"\t\t\"C:\\\\Program Files (x86)\\\\Steam\"\n\t}\n\t\"1\"\n\t{\n\t\t\"path\"\t\t\"D:\\\\SteamLibrary\"\n\t\t\"apps\" { \"553850\" \"1\" }\n\t}\n}\n";
        Assert.Equal([@"C:\Program Files (x86)\Steam", @"D:\SteamLibrary"], GameIconStore.SteamLibraries(vdf));
    }
}
