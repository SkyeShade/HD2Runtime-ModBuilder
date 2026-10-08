using HD2RuntimeGUI.Core.Generation;
using HD2RuntimeGUI.Core.Metadata;
using HD2RuntimeGUI.Core.Models;
using HD2RuntimeGUI.Core.Services;
using Xunit;

namespace HD2RuntimeGUI.Tests;

// HD2Runtime 0.30.0 projectile donors (docs/runtime030-integration-plan.md, Phase 7; Runtime docs/attack-outputs.md "More donors"): the 32
// projectile_donor outputs (Eagle payloads, orbital shells, sentry and emplacement rounds, weapons' second projectiles) are offered in every
// projectile selector, on player, ammunition, support and mounted hosts and as a programmable-ammunition projectile. None is live-tested, so
// every write carries allow_unverified_reference and allow_unverified_effect (Runtime's require_unverified_donor), emitted unconditionally,
// and the export declares the Runtime that has them.
public sealed class Runtime030DonorTests
{
    private const string Out = "output/v1/projectile/";
    public const string Reprimand = "SMG-32 Reprimand", Arbitrator = "AR-11 Arbitrator", Liberator = "AR-23 Liberator", Stalwart = "M-105 Stalwart",
        Patriot = "EXO-45 Patriot Exosuit / right_gun";
    public const string Bomb = Out + "eagle-500kg-bomb-projectile-239", Precision = Out + "orbital-precision-strike-projectile-100", Hmg = Out + "e-mg-101-hmg-emplacement-projectile-83",
        Gatling = Out + "orbital-gatling-barrage-projectile-77", Warrant = Out + "p-92-warrant-projectile-319", Railcannon = Out + "orbital-railcannon-strike-projectile-277";
    private static readonly string[] Both = ["allow_unverified_effect", "allow_unverified_reference"];
    private static Task<(BuilderWorkspace W, SdkMetadata Sdk)> Fresh(TestEnvironment e) => ExportFixtureTests.Fresh(e, "Donors");
    private static AttackOutputDonor Donor(SdkMetadata sdk, AttackOutputHost host, string output) => sdk.AttackOutputs!.Donors(host).Single(d => d.Output.SemanticId == output);

    [Fact] public async Task Every_donor_is_offered_with_both_opt_ins_on_every_host_kind()
    {
        using var e = new TestEnvironment(); var (w, sdk) = await Fresh(e); var catalog = sdk.AttackOutputs!;
        Assert.True(AuthoredTypes.ProjectileDonors);
        Assert.DoesNotContain(catalog.Unavailable(), u => u.Output.IsDonor);
        // A player component host lists all 32, each with Runtime's refusal or both opt-ins, whatever its class.
        var reprimand = w.OutputHost(Reprimand, "primary", out _)!;
        var donors = catalog.Donors(reprimand).Where(d => d.Output.IsDonor).ToArray();
        Assert.Equal(32, donors.Length);
        Assert.All(donors.Where(d => d.Allowed), d => Assert.Equal(Both, d.Acknowledgements));
        var bomb = Donor(sdk, reprimand, Bomb); Assert.True(bomb.Allowed && bomb.CrossClass && !bomb.ProvenOnHost);
        Assert.Equal(("owner entity loadout package", true), (bomb.Output.Package.Via, bomb.Output.Package.AutoLoad));
        Assert.Equal((500d, 500d, true), (bomb.Output.Flight!.Velocity, bomb.Output.Flight.Mass, bomb.Output.Flight.UnitDriven));
        // The same class still needs both: the donor is not live-tested (the HMG Emplacement round on the Arbitrator).
        var hmg = Donor(sdk, w.OutputHost(Arbitrator, "primary", out _)!, Hmg); Assert.False(hmg.CrossClass); Assert.Equal(Both, hmg.Acknowledgements);
        // An ammunition host adds its row's allow_shared; support and mounted hosts their field's allow_unverified_effect.
        Assert.Equal(["allow_shared", .. Both], Donor(sdk, w.OutputHost(Liberator, "primary", out _)!, Hmg).Acknowledgements);
        Assert.Equal(Both, Donor(sdk, w.ProjectileHost(AttackOutputChange.SupportHost, Stalwart, "primary", out _)!, Precision).Acknowledgements);
        Assert.Equal(Both, Donor(sdk, w.ProjectileHost(AttackOutputChange.VehicleHost, Patriot, "primary", out _)!, Gatling).Acknowledgements);
        // A player weapon's second projectile is never expressed as a classic reference swap (the graph names its first projectile).
        Assert.Null(w.ClassicSource(w.OutputHost(Arbitrator, "primary", out _)!, catalog.Output(Warrant)!));
    }

    [Fact] public async Task A_donor_swap_is_a_guarded_patch_with_both_opt_ins_and_raises_the_required_runtime()
    {
        using var e = new TestEnvironment(); var (w, sdk) = await Fresh(e);
        await w.SetAttackOutputAsync(Reprimand, "primary", Bomb);
        var change = Assert.Single(w.Project!.AttackOutputChanges!);
        Assert.Equal(Both, change.Acknowledgements); Assert.Equal("Eagle 500kg Bomb (projectile 239)", change.OutputName); Assert.Null(w.BuildError);
        // Runtime docs/attack-outputs.md's own example: target the attack, expect its projectile, value hd2.attack_output(donor), both opt-ins.
        Assert.Equal(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Golden", "projectile-donor-swap.lua")).Replace("\r\n", "\n"), w.LuaPreview);
        Assert.Equal(RuntimeRequirement.Release030, RuntimeRequirement.Minimum(w.Project, sdk));
        Assert.Contains((RuntimeRequirement.Release030, "projectile donor " + Bomb), RuntimeRequirement.Floors(w.Project, sdk));
        // A project that declares an older SDK is raised to the Runtime that has the donors (the validator's usesMoreDonors).
        var older = sdk with { Version = "0.28.1" };
        w.Project.SdkVersion = "0.28.1";
        Assert.Equal(RuntimeRequirement.Release030, RuntimeRequirement.Minimum(w.Project, older));
        w.Project.SdkVersion = sdk.Version;
        // Round trip: the saved swap reopens unchanged, validates against the SDK and generates the same Lua.
        var lua = w.LuaPreview; var id = w.Project.Id;
        await w.OpenAsync(id);
        var reopened = Assert.Single(w.Project!.AttackOutputChanges!);
        Assert.Equal((change.Output, change.Evidence, change.BaselineSdkVersion), (reopened.Output, reopened.Evidence, reopened.BaselineSdkVersion)); Assert.Equal(Both, reopened.Acknowledgements);
        AttackOutputChangeService.Validate(sdk, w.Project.AttackOutputChanges![0]);
        Assert.Equal(lua, w.LuaPreview);
        // Restoring the own projectile removes the floor again.
        await w.SetAttackOutputAsync(Reprimand, "primary", null);
        Assert.DoesNotContain(RuntimeRequirement.Floors(w.Project, sdk), f => f.Reason.StartsWith("projectile donor", StringComparison.Ordinal));
    }

    [Fact] public async Task Support_and_mounted_hosts_fire_a_donor_with_both_opt_ins()
    {
        using var e = new TestEnvironment(); var (w, sdk) = await Fresh(e);
        await w.SetHostOutputAsync(AttackOutputChange.SupportHost, Stalwart, "primary", Precision);
        await w.SetHostOutputAsync(AttackOutputChange.VehicleHost, Patriot, "primary", Railcannon);
        Assert.Null(w.BuildError);
        var lua = w.LuaPreview;
        foreach (var donor in new[] { Precision, Railcannon })
        {
            var block = lua.Split("add(function()").Single(b => b.Contains(donor, StringComparison.Ordinal));
            Assert.Contains("allow_unverified_effect=true,", block); Assert.Contains("allow_unverified_reference=true,", block);
            Assert.Contains("value=hd2.attack_output('" + donor + "'),", block);
        }
        // The base mode of a programmable-ammunition card (and the projectile builder's row links) follow the donor row, which has no slots.
        Assert.Equal(Precision, w.FiredRow(AttackOutputChange.SupportHost, Stalwart, "primary")!.SemanticId);
        Assert.Null(w.FiredRow(AttackOutputChange.SupportHost, Stalwart, "primary")!.Slots);
    }

    // An export fixture (build-rc.ps1 validates every export-fixtures ZIP with Runtime's own validator): one donor on each host kind and as a
    // programmable-ammunition projectile.
    [Fact] public async Task D1_projectile_donors_export()
    {
        using var e = new TestEnvironment(); var (w, _) = await ExportFixtureTests.Fresh(e, "D1 Projectile Donors");
        await w.SetAttackOutputAsync(Reprimand, "primary", Bomb);
        await w.SetAttackOutputAsync("JAR-5 Dominator", "primary", Hmg);
        await w.SetHostOutputAsync(AttackOutputChange.SupportHost, Stalwart, "primary", Precision);
        await w.SetHostOutputAsync(AttackOutputChange.VehicleHost, Patriot, "primary", Gatling);
        await w.SetSelectorAsync(CompositionKind.Player, Liberator, new(null, null, Railcannon, w.AmmoModes(CompositionKind.Player, Liberator)!.Input.Input));
        var (manifest, lua) = await ExportFixtureTests.Export(w, "D1-projectile-donors");
        Assert.Equal(RuntimeRequirement.Release030, manifest.RootElement.GetProperty("requires").GetProperty("hd2runtime").GetProperty("min_version").GetString());
        foreach (var donor in new[] { Bomb, Hmg, Precision, Gatling, Railcannon }) Assert.Contains("hd2.attack_output('" + donor + "')", lua);
        Assert.Equal(5, lua.Split("allow_unverified_reference=true").Length - 1);
    }

    [Fact] public async Task A_donor_as_the_programmable_ammunition_projectile_carries_both_opt_ins()
    {
        using var e = new TestEnvironment(); var (w, sdk) = await Fresh(e);
        var modes = w.AmmoModes(CompositionKind.Player, Liberator)!;
        var donor = Assert.Single(modes.Donors, d => d.Output.SemanticId == Bomb);
        Assert.Equal(Both, modes.DonorOptIns(donor));
        await w.SetSelectorAsync(CompositionKind.Player, Liberator, new(null, null, Bomb, modes.Input.Input));
        Assert.Null(w.BuildError);
        var block = w.LuaPreview.Split("add(function()").Single(b => b.Contains(Bomb, StringComparison.Ordinal));
        Assert.Contains("allow_unverified_effect=true,", block); Assert.Contains("allow_unverified_reference=true,", block);
        Assert.Contains((RuntimeRequirement.Release030, "projectile donor " + Bomb), RuntimeRequirement.Floors(w.Project!, sdk));
    }
}
