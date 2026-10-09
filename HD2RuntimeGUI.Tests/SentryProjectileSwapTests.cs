using System.Text.RegularExpressions;
using HD2RuntimeGUI.Core.Audit;
using HD2RuntimeGUI.Core.Generation;
using HD2RuntimeGUI.Core.Metadata;
using HD2RuntimeGUI.Core.Models;
using HD2RuntimeGUI.Core.Projects;
using Xunit;

namespace HD2RuntimeGUI.Tests;

// HD2Runtime 0.30.2 sentry and emplacement projectile swaps (attack-outputs.md "Sentry and emplacement hosts"; Runtime example
// examples/projects/SentryProjectileSwapTest): the unified projectile host swap on "<stratagem> / weapon", written through
// hd2.stratagem(name):attack('primary'):projectile_source() with allow_unverified_effect on every donor and allow_unverified_reference on a
// cross-class one. Type-level; the A/MG-43 and A/G-16 stay read-only with Runtime's reason.
public sealed class SentryProjectileSwapTests
{
    private const string Kind = AttackOutputChange.VehicleHost;
    private const string Ac8 = "A/AC-8 Autocannon Sentry / weapon", Mortar = "A/M-12 Mortar Sentry / weapon", Hmg = "E/MG-101 HMG Emplacement / weapon",
        At12 = "E/AT-12 Anti-Tank Emplacement / weapon", GasMortar = "A/GM-17 Gas Mortar Sentry / weapon", Mg43 = "A/MG-43 Machine Gun Sentry / weapon",
        Gatling = "A/G-16 Gatling Sentry / weapon";
    private static readonly string Scorcher = ProjectileHostTests.Scorcher, Talon = ProjectileHostTests.Talon, Apw = ProjectileHostTests.Apw,
        Recoilless = ProjectileHostTests.Out("gr-8-recoilless-rifle"), MortarRound = ProjectileHostTests.Out("a-m-12-mortar-sentry-projectile-346");
    private static string Flat(string s) => Regex.Replace(s, @"\s+", "");
    private static Task<(Core.Services.BuilderWorkspace W, SdkMetadata Sdk)> Fresh(TestEnvironment e, string name = "Sentry Swaps") => ExportFixtureTests.Fresh(e, name);
    private static AttackOutputHost Host(SdkMetadata sdk, string weapon) => ProjectileHosts.Host(sdk, Kind, weapon, "primary", out var reason) ?? throw new Xunit.Sdk.XunitException(reason);
    private static AttackOutputDonor Donor(SdkMetadata sdk, string weapon, string output) => sdk.AttackOutputs!.Donors(Host(sdk, weapon)).Single(d => d.Output.SemanticId == output);
    // The exact guarded patch (from target to value) the swap of a stratagem host generates.
    private static string Patch(string stratagem, string output, params string[] flags) =>
        "target=hd2.stratagem('" + stratagem + "'):attack('primary'):projectile_source().target,\n" + string.Concat(flags.Select(f => f + "=true,\n"))
        + "field=hd2.fields.attack.projectile,\nexpect=hd2.stratagem('" + stratagem + "'):attack('primary'):projectile_source().expect,\nvalue=hd2.attack_output('" + output + "'),";

    [Fact] public async Task The_nine_stratagem_hosts_are_listed_seven_writable_with_their_class_and_opt_in()
    {
        using var e = new TestEnvironment(); var (_, sdk) = await Fresh(e);
        Assert.True(AuthoredTypes.StratagemProjectileHosts);
        var hosts = sdk.Entities!.VehicleWeapons!.StratagemHosts;
        var entries = ProjectileHosts.All(sdk).Where(h => hosts.ContainsKey(h.Source.Weapon)).ToArray();
        Assert.Equal(9, entries.Length);
        Assert.Equal(new[] { At12, Hmg, Ac8, GasMortar, "A/M-23 EMS Mortar Sentry / weapon", Mortar, "A/MLS-4X Rocket Sentry / weapon" }.Order(StringComparer.Ordinal),
            entries.Where(h => h.Writable).Select(h => h.Source.Weapon).Order(StringComparer.Ordinal));
        foreach (var entry in entries.Where(h => h.Writable))
        {
            var sentry = hosts[entry.Source.Weapon]; var host = entry.Host!;
            Assert.Equal((Kind, AttackOutputHost.Component, sentry.CompatibilityClass, true), (host.HostKind, host.Mechanism, host.CompatibilityClass, host.CrossClassHost));
            Assert.Equal(["allow_unverified_effect"], host.BaseAcknowledgements); Assert.Null(host.LiveProvenValues);
            // Every allowed donor carries allow_unverified_effect (no type-level swap is live-tested).
            Assert.All(sdk.AttackOutputs!.Donors(host).Where(d => d.Allowed), d => Assert.Contains("allow_unverified_effect", d.Acknowledgements));
        }
        // The own round is the baseline, never a donor of its own host (restoring it is removing the swap).
        var mortar = entries.Single(h => h.Source.Weapon == Mortar);
        Assert.Equal(MortarRound, mortar.Own!.SemanticId);
        Assert.DoesNotContain(sdk.AttackOutputs!.Donors(mortar.Host!), d => d.Output.SemanticId == MortarRound);
        Assert.Contains(sdk.AttackOutputs.Donors(Host(sdk, GasMortar)), d => d.Output.SemanticId == MortarRound && d.Allowed);
        // The baseline round each host publishes (speed, damage, mass) is what the editor compares a donor with.
        Assert.Equal(new VehicleStratagemHostRound(1021, 216, 900), hosts[Ac8].BaselineRound);
    }

    [Fact] public async Task A_same_class_donor_is_written_through_the_stratagem_with_allow_unverified_effect()
    {
        using var e = new TestEnvironment(); var (w, sdk) = await Fresh(e);
        var scorcher = Donor(sdk, Ac8, Scorcher);
        Assert.True(scorcher.Allowed && !scorcher.CrossClass); Assert.Equal(["allow_unverified_effect"], scorcher.Acknowledgements);
        await w.SetHostOutputAsync(Kind, Ac8, "primary", Scorcher);
        Assert.Null(w.BuildError);
        var change = Assert.Single(w.Project!.AttackOutputChanges!);
        Assert.Equal((Kind, Ac8, "primary", AttackOutputHost.Component), (change.HostKind, change.Weapon, change.AttackRole, change.Mechanism));
        Assert.Contains(Flat(Patch("A/AC-8 Autocannon Sentry", Scorcher, "allow_unverified_effect")), Flat(w.LuaPreview));
        Assert.DoesNotContain("allow_unverified_reference", w.LuaPreview); Assert.DoesNotContain("hd2.vehicle(", w.LuaPreview);
        Assert.Equal(Scorcher, w.FiredRow(Kind, Ac8, "primary")!.SemanticId);
        // Bound to the pinned 0.30.3, the export declares the pin; the host's own floor is 0.30.2.
        Assert.Equal(SdkPin.Version, RuntimeRequirement.Minimum(w.Project!, sdk));
        Assert.Contains((RuntimeRequirement.Release0302, "sentry / emplacement projectile host " + Ac8), RuntimeRequirement.Floors(w.Project!, sdk));
    }

    [Fact] public async Task A_cross_class_donor_adds_allow_unverified_reference_and_restoring_the_own_round_writes_nothing()
    {
        using var e = new TestEnvironment(); var (w, sdk) = await Fresh(e);
        // A flat laser round on a mortar sentry (explosive shrapnel): cross-class, both opt-ins.
        var talon = Donor(sdk, Mortar, Talon);
        Assert.True(talon.Allowed && talon.CrossClass); Assert.Equal(["allow_unverified_effect", "allow_unverified_reference"], talon.Acknowledgements);
        await w.SetHostOutputAsync(Kind, Mortar, "primary", Talon);
        Assert.Contains(Flat(Patch("A/M-12 Mortar Sentry", Talon, "allow_unverified_effect", "allow_unverified_reference")), Flat(w.LuaPreview));
        // A stratagem-owned donor (another sentry's round) needs both whatever its class.
        await w.SetHostOutputAsync(Kind, GasMortar, "primary", MortarRound);
        Assert.Contains(Flat(Patch("A/GM-17 Gas Mortar Sentry", MortarRound, "allow_unverified_effect", "allow_unverified_reference")), Flat(w.LuaPreview));
        // Restoring the own round removes the swap: no write, no opt-in. The own round cannot be chosen as a donor.
        await w.SetHostOutputAsync(Kind, Mortar, "primary", null);
        Assert.DoesNotContain("hd2.stratagem('A/M-12 Mortar Sentry')", w.LuaPreview);
        Assert.Equal(MortarRound, ProjectileHosts.Entry(sdk, Kind, Mortar, "primary")!.Own!.SemanticId);
        await Assert.ThrowsAsync<InvalidDataException>(() => w.SetHostOutputAsync(Kind, Mortar, "primary", MortarRound));
        await w.SetHostOutputAsync(Kind, GasMortar, "primary", null);
        Assert.Null(w.Project!.AttackOutputChanges); Assert.DoesNotContain("hd2.stratagem(", w.LuaPreview);
    }

    [Fact] public async Task The_machine_gun_and_gatling_sentries_are_read_only_with_runtimes_reason()
    {
        using var e = new TestEnvironment(); var (w, sdk) = await Fresh(e);
        foreach (var weapon in new[] { Mg43, Gatling })
        {
            var reason = sdk.Entities!.VehicleWeapons!.StratagemHosts[weapon].Reason!;
            Assert.Contains("magazine names each round", reason);
            var entry = ProjectileHosts.Entry(sdk, Kind, weapon, "primary")!;
            Assert.Equal((false, reason), (entry.Writable, entry.Reason));
            var refused = await Assert.ThrowsAsync<InvalidDataException>(() => w.SetHostOutputAsync(Kind, weapon, "primary", Talon));
            Assert.Contains(reason, refused.Message);
        }
        Assert.Null(w.Project!.AttackOutputChanges);
        // Custom projectiles: the Runtime target binds only player hosts and says so for these.
        var custom = Core.CustomContent.CustomContentTargets.Current.DescribeProjectileHost(new Core.CustomContent.CustomProjectileHost(Kind, Ac8, "primary"), w.Project, sdk);
        Assert.Equal(Core.CustomContent.CustomFieldStatus.Unavailable, custom.Status); Assert.False(string.IsNullOrWhiteSpace(custom.Reason));
        // The capability audit: the seven writable hosts are edited in the stratagem editor, the two read-only ones keep Runtime's reason.
        var report = CapabilityAudit.Run(sdk, CapabilityAuditTests.Files(Runtime030Tests.Version));
        var host = "projectile host (" + CapabilityExemptions.StratagemHostKind + ")";
        Assert.Equal((CapabilityClass.SUPPORTED_UI, 7), report.Entries.Where(x => x.Capability == host && x.Type == "writable").Select(x => (x.Class, x.Instances)).Single());
        Assert.Equal((CapabilityClass.RUNTIME_READ_ONLY, 2), report.Entries.Where(x => x.Capability == host && x.Type == "read-only").Select(x => (x.Class, x.Instances)).Single());
    }

    [Fact] public async Task Sentry_swaps_save_reopen_and_keep_project_format_14()
    {
        using var e = new TestEnvironment(); var (w, sdk) = await Fresh(e);
        await w.SetHostOutputAsync(Kind, Ac8, "primary", Scorcher);
        await w.SetHostOutputAsync(Kind, Hmg, "primary", Apw);
        var lua = w.LuaPreview; var saved = w.Project!.AttackOutputChanges!.ToArray();
        await w.OpenAsync(w.Project.Id);
        Assert.Null(w.BuildError); Assert.Equal(lua, w.LuaPreview);
        Assert.Equal(saved.Select(c => (c.HostKind, c.Weapon, c.Output, c.Evidence, string.Join(",", c.Acknowledgements))),
            w.Project!.AttackOutputChanges!.Select(c => (c.HostKind, c.Weapon, c.Output, c.Evidence, string.Join(",", c.Acknowledgements))));
        Assert.All(w.Project.AttackOutputChanges!, c => Assert.Null(w.AttackOutputIssue(c)));
        ProjectIdentity.Validate(w.Project);
        Assert.True(w.Project.FormatVersion <= 14); Assert.True(ProjectIdentity.RequiredFormat(w.Project) <= 14);
        // Toggled off it is kept but not written.
        await w.ToggleAttackOutputAsync(w.Project.AttackOutputChanges!.Single(c => c.Weapon == Hmg).Id);
        Assert.DoesNotContain("hd2.stratagem('E/MG-101 HMG Emplacement')", w.LuaPreview);
    }

    [Fact] public async Task A_build_without_the_hosts_refuses_a_saved_sentry_swap_with_its_reason()
    {
        // ModBuilder 1.7.0 (project format 14, HD2Runtime 0.30.0 SDK) resolves a vehicle_weapon swap through the same host lookup: the 0.30.0
        // catalogue publishes no source for "<stratagem> / weapon", so the build stops with the host named (nothing dropped or mis-written).
        using var e = new TestEnvironment(); var (w, sdk) = await Fresh(e);
        await w.SetHostOutputAsync(Kind, Ac8, "primary", Scorcher);
        var older = await SdkFixtures.Install(e, Runtime030Tests.Release);
        Assert.Null(older.AttackOutputs!.Source(Kind, Ac8, "primary"));
        var refused = Assert.Throws<InvalidDataException>(() => AttackOutputChangeService.Operations(w.Project!, older));
        Assert.Contains(Ac8, refused.Message);
    }

    // Copied with the F1-F9 and S1 fixtures (build-rc.ps1 validates them with Runtime's own validator): both families, same- and cross-class
    // donors, a stratagem-owned donor.
    [Fact] public async Task S2_sentry_projectile_swap_export_fixture()
    {
        using var e = new TestEnvironment(); var (w, sdk) = await Fresh(e, "S2 Sentry Projectile Swap");
        await w.SetHostOutputAsync(Kind, Hmg, "primary", Apw);
        await w.SetHostOutputAsync(Kind, At12, "primary", Recoilless);
        await w.SetHostOutputAsync(Kind, Ac8, "primary", Scorcher);
        await w.SetHostOutputAsync(Kind, Mortar, "primary", Talon);
        await w.SetHostOutputAsync(Kind, GasMortar, "primary", MortarRound);
        var (manifest, lua) = await ExportFixtureTests.Export(w, "S2-sentry-projectile-swap");
        Assert.Equal(SdkPin.Version, manifest.RootElement.GetProperty("requires").GetProperty("hd2runtime").GetProperty("min_version").GetString());
        Assert.Equal(5, Regex.Matches(lua, @"hd2\.stratagem\('[^']+'\):attack\('primary'\):projectile_source\(\)\.target").Count);
        Assert.Contains(Flat(Patch("E/AT-12 Anti-Tank Emplacement", Recoilless, "allow_unverified_effect")), Flat(lua));
    }
}
