using System.IO.Compression;
using System.Text.Json;
using HD2RuntimeGUI.Core.Generation;
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

    private const string Out = "output/v1/projectile/";

    [Fact] public async Task F3_support_projectile_swap()
    {
        using var e = new TestEnvironment(); var (w, _) = await Fresh(e, "F3 Support Projectile Swap");
        // Live-proven support pairs: EAT-17 <- PLAS-1 Scorcher, M-105 Stalwart <- APW-1.
        await w.SetHostOutputAsync(Core.Models.AttackOutputChange.SupportHost, "EAT-17 Expendable Anti-Tank", "primary", Out + "plas-1-scorcher");
        await w.SetHostOutputAsync(Core.Models.AttackOutputChange.SupportHost, "M-105 Stalwart", "primary", Out + "apw-1-anti-materiel-rifle");
        var (manifest, lua) = await Export(w, "F3-support-projectile-swap");
        RequiresPinned(manifest); Assert.Contains("hd2.support_weapon('EAT-17 Expendable Anti-Tank'):attack('primary')", lua);
    }

    [Fact] public async Task F4_programmable_ammo()
    {
        using var e = new TestEnvironment(); var (w, _) = await Fresh(e, "F4 Programmable Ammo");
        // MG-206: X/Y/Z rates and the Incendiary (R-4 Hyena) function projectile; S-11: the Stun spare twin with labels and auto icons.
        await w.SetSelectorAsync(CompositionKind.Support, "MG-206 Heavy Machine Gun", new([450, 600, 900], null, Out + "r-4-hyena", null));
        await w.SetSelectorAsync(CompositionKind.Support, "S-11 Speargun", new(null, null, Out + "s-11-speargun-spare-twin", null));
        await w.SetRowAsync(Out + "s-11-speargun-spare-twin", OutputRowChangeService.ModeLabel, "stun");
        await w.SetRowAsync(Out + "s-11-speargun-spare-twin", OutputRowChangeService.ModeIcon, "auto");
        var (manifest, lua) = await Export(w, "F4-programmable-ammo");
        RequiresPinned(manifest); Assert.Contains("hd2.fields.function_ammo.projectile", lua); Assert.Contains("hd2.fields.fire_rate.modes", lua);
        Assert.Contains("hd2.fields.presentation.mode_label", lua);
    }

    [Fact] public async Task F5_projectile_slot_composition()
    {
        using var e = new TestEnvironment(); var (w, _) = await Fresh(e, "F5 Slot Composition");
        // Liberator fires LAS-58 Talon (ammunition host); the Talon row (the donor row) gets the GL-21 grenade blast: the live-proven chain.
        await w.SetAttackOutputAsync("AR-23 Liberator", "primary", Out + "las-58-talon");
        await w.SetRowAsync(Out + "las-58-talon", OutputRowChangeService.ImpactExplosion, OutputRowChangeService.Handle(Out + "gl-21-grenade-launcher", AttackOutputSlots.ImpactExplosionKey));
        var (manifest, lua) = await Export(w, "F5-slot-composition");
        RequiresPinned(manifest); Assert.Contains("hd2.attack_output('" + Out + "las-58-talon')", lua); Assert.Contains("hd2.fields.projectile.impact_explosion", lua);
    }

    [Fact] public async Task F6_patriot_mounted_swap()
    {
        using var e = new TestEnvironment(); var (w, _) = await Fresh(e, "F6 Patriot Mounted Swap");
        await w.SetHostOutputAsync(Core.Models.AttackOutputChange.VehicleHost, "EXO-45 Patriot Exosuit / right_gun", "primary", Out + "eat-17-expendable-anti-tank");
        // The Patriot's own bullet row (host-owned row) keeps its bullet but gains an impact explosion.
        await w.SetRowAsync(Out + "exo-45-patriot-exosuit-right-gun", OutputRowChangeService.ImpactExplosion, OutputRowChangeService.Handle(Out + "gl-21-grenade-launcher", AttackOutputSlots.ImpactExplosionKey));
        var (manifest, lua) = await Export(w, "F6-patriot-mounted-swap");
        RequiresPinned(manifest); Assert.Contains("hd2.vehicle('EXO-45 Patriot Exosuit'):weapon('right_gun')", lua);
    }

    [Fact] public async Task F7_one_two_underbarrel()
    {
        using var e = new TestEnvironment(); var (w, _) = await Fresh(e, "F7 One-Two Underbarrel");
        await w.SetWeaponChangeAsync("AR/GL-21 One-Two / underbarrel", "weapon.horizontal_spread", "15", false);
        await w.SetWeaponChangeAsync("AR/GL-21 One-Two / underbarrel", "rounds.spare_rounds", "10", false);
        await w.SetWeaponChangeAsync("AR/GL-21 One-Two / underbarrel", "rounds.starting_rounds", "8", false);
        await w.SetWeaponChangeAsync("AR/GL-21 One-Two", "weapon.ergonomics", "60", false);
        var (manifest, lua) = await Export(w, "F7-one-two-underbarrel");
        RequiresPinned(manifest); Assert.Contains("hd2.weapon('AR/GL-21 One-Two'):underbarrel()", lua); Assert.Contains("hd2.weapon('AR/GL-21 One-Two')", lua);
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

    // 1.6.0: in-game options on unedited fields (option-only edits: expect = vanilla, value = the option). Runtime validates every option
    // sample at declaration, including the vanilla default.
    [Fact] public async Task F9_option_only_edits()
    {
        using var e = new TestEnvironment(); var (w, sdk) = await Fresh(e, "F9 Option Only");
        await w.SetModOptionsEnabledAsync(true);
        var missiles = sdk.Entities!.VehicleWeapons!.FieldInstances.First(f => f.Target.Weapon == "TD-110 Maelstrom / slot_3" && f.SemanticFieldId == "damage.primary.standard_damage");
        var keys = new[] { ModOptionsService.EntityKey(missiles.InstanceKey), ModOptionsService.WeaponKey("AR-23 Liberator", "weapon.ergonomics"),
            ModOptionsService.ObjectKey("AR-23 Liberator", "primary", "projectile", null, "projectile.velocity"),
            sdk.Stratagems!.FieldInstances.Where(f => f.Target.Stratagem == "Orbital Precision Strike" && f.Editable).Select(f => ModOptionsService.StratagemKey(f.InstanceKey)).First(k => w.OptionCandidate(k) != null) };
        for (var i = 0; i < keys.Length; i++)
        {
            var row = w.SuggestOptionRow(w.OptionCandidate(keys[i])!); row.Id = "option_only_" + i; row.Label = "Option only " + i;
            await w.SaveOptionRowAsync(row);
        }
        var (manifest, lua) = await Export(w, "F9-option-only");
        RequiresPinned(manifest); Assert.Equal(13, w.Project!.FormatVersion);
        Assert.Contains("hd2.vehicle('TD-110 Maelstrom'):weapon('slot_3')", lua); Assert.Contains("expect=1100,", lua);
        for (var i = 0; i < keys.Length; i++) Assert.Contains("value=option_option_only_" + i, lua);
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
