using System.IO.Compression;
using System.Text.Json;
using HD2RuntimeGUI.Core.Metadata;
using HD2RuntimeGUI.Core.Services;
using Xunit;

namespace HD2RuntimeGUI.Tests;

// Export fixtures for the 1.4.0 release candidate, built through the workspace exactly as the app builds them, on the pinned SDK. Each is
// written next to the test binaries (export-fixtures/) and, when HD2_EXPORT_OUT is set, there too; tools/validate-exports.py then runs
// them through HD2Runtime 0.28.0's own validator against the retained snapshot.
public sealed class ExportFixtureTests
{
    public const string Author = "ModBuilder fixtures";
    private static IEnumerable<string> Outputs() => new[] { Path.Combine(AppContext.BaseDirectory, "export-fixtures"), Environment.GetEnvironmentVariable("HD2_EXPORT_OUT") }.OfType<string>();
    public static async Task<(BuilderWorkspace W, SdkMetadata Sdk)> Fresh(TestEnvironment e, string name)
    {
        var sdk = await SdkFixtures.Install(e, SdkPin.Version); var w = e.Workspace();
        await w.CreateAsync(new(name, Author, "mods/modbuilder_fixtures/" + name.ToLowerInvariant().Replace(' ', '_').Replace('-', '_'), "1.0.0"), sdk);
        return (w, sdk);
    }
    // Exports the project and copies the ZIP to every fixture folder; returns the ZIP's hd2runtime.json and addon.lua.
    public static async Task<(JsonDocument Manifest, string Lua)> Export(BuilderWorkspace w, string file)
    {
        Assert.Null(w.BuildError);
        await w.ExportAsync();
        foreach (var dir in Outputs()) { Directory.CreateDirectory(dir); File.Copy(w.LastExport!, Path.Combine(dir, file + ".zip"), true); }
        using var zip = ZipFile.OpenRead(w.LastExport!);
        using var lua = new StreamReader(zip.GetEntry("src/addon.lua")!.Open());
        return (JsonDocument.Parse(zip.GetEntry("hd2runtime.json")!.Open()), await lua.ReadToEndAsync());
    }
    private static void RequiresPinned(JsonDocument manifest) =>
        Assert.Equal(SdkPin.Version, manifest.RootElement.GetProperty("requires").GetProperty("hd2runtime").GetProperty("min_version").GetString());

    [Fact] public async Task F1_simple_rebalance()
    {
        using var e = new TestEnvironment(); var (w, _) = await Fresh(e, "F1 Simple Rebalance");
        await w.SetWeaponChangeAsync("AR-23 Liberator", "weapon.fire_rate", "720", false);
        await w.SetWeaponChangeAsync("AR-23 Liberator", "weapon.ergonomics", "60", false);
        var (manifest, lua) = await Export(w, "F1-simple-rebalance");
        RequiresPinned(manifest); Assert.Contains("hd2.weapon('AR-23 Liberator')", lua);
    }

    [Fact] public async Task F2_projectile_swap()
    {
        using var e = new TestEnvironment(); var (w, sdk) = await Fresh(e, "F2 Projectile Swap");
        // A player component host with its live-proven donor: SMG-32 Reprimand <- EAT-700 Expendable Napalm.
        var output = sdk.AttackOutputs!.Outputs.Single(o => o.Owner.Name == "EAT-700 Expendable Napalm" && o.Family == "projectile").SemanticId;
        await w.SetAttackOutputAsync("SMG-32 Reprimand", "primary", output);
        var (manifest, lua) = await Export(w, "F2-projectile-swap");
        RequiresPinned(manifest); Assert.Contains("hd2.attack_output('" + output + "')", lua);
    }

    [Fact] public async Task F8_event_action_mod()
    {
        using var e = new TestEnvironment(); var (w, _) = await Fresh(e, "F8 Event Action");
        await w.SetWeaponChangeAsync("SMG-32 Reprimand", "weapon.sway", "0.5", false);
        await w.AddCustomLuaAsync();
        await w.SaveCustomLuaAsync("""
            hd2.events.on('player_died', function(event)
                if event.local_player and event.position then
                    hd2.explosions.spawn('Hellbomb', {position = event.position})
                end
            end)
            """);
        var (manifest, lua) = await Export(w, "F8-event-action");
        RequiresPinned(manifest); Assert.Contains("hd2.explosions.spawn('Hellbomb'", lua); Assert.Contains("hd2.events.on('player_died'", lua); Assert.Contains("local function addon(...)", lua);
    }

    [Theory]
    [InlineData("H0-control-no-halt")] [InlineData("H1-halt-all")] [InlineData("H2-halt-damage-only")] [InlineData("H3-halt-sway-only")]
    public async Task Halt_issue_variants(string variant)
    {
        using var e = new TestEnvironment(); var sdk = await SdkFixtures.Install(e, SdkPin.Version);
        var project = HaltRegressionTests.Project(sdk, "Halt " + variant, HaltRegressionTests.Variants(sdk)[variant], Path.Combine(e.Paths.Root, "exports"));
        var zip = await e.Exporter.ExportAsync(project, sdk);
        foreach (var dir in Outputs()) { Directory.CreateDirectory(dir); File.Copy(zip, Path.Combine(dir, "halt-" + variant + ".zip"), true); }
    }

    // Projects saved by ModBuilder 1.3.1, rebound to the pinned SDK and exported (old-project compatibility through Runtime's validator).
    [Theory] [MemberData(nameof(OldProjectCompatibilityTests.Projects), MemberType = typeof(OldProjectCompatibilityTests))]
    public async Task Rebound_1_3_1_projects(string name)
    {
        using var e = new TestEnvironment(); await SdkFixtures.Install(e, "0.27.0"); await SdkFixtures.Install(e, SdkPin.Version);
        var w = e.Workspace(); var project = await e.Store.ImportAsync(Path.Combine(AppContext.BaseDirectory, "Fixtures", "projects-1.3.1", name + ".hd2mod.json"));
        await w.OpenAsync(project.Id); await w.RebindToInstalledSdkAsync();
        Assert.Equal(SdkPin.Version, w.Project!.SdkVersion);
        var (manifest, _) = await Export(w, "compat131-" + name); RequiresPinned(manifest);
    }
}
