using System.IO.Compression;
using System.Text;
using System.Text.Json;
using HD2RuntimeGUI.Core.Audit;
using HD2RuntimeGUI.Core.CustomContent;
using HD2RuntimeGUI.Core.Generation;
using HD2RuntimeGUI.Core.Metadata;
using HD2RuntimeGUI.Core.Models;
using HD2RuntimeGUI.Core.Scripting;
using Xunit;

namespace HD2RuntimeGUI.Tests;

// HD2Runtime 0.30.1 and 0.30.2 (docs/runtime030-integration-plan.md, "Swapping the bundled SDK" and the build record). The pin is 0.30.3
// (Runtime030Tests.Version), whose SDK is 0.30.2's with the version strings and the custom stratagem uses range changed, so 0.30.2's
// features are tested on it. 0.30.2's eight player-weapon graphs are labelled with its own version (0.30.3's with 0.30.3); 0.30.1 republished 0.30.0's 0.19.0-labelled graphs unchanged. The seven
// weapons 0.30.2 resolved to their proven roots are writable through the existing editors and raise an export to 0.30.2. What 0.30.2 adds
// that ModBuilder does not author yet (status slots on player-weapon explosions, the extended statuses, sentry and emplacement projectile
// hosts) is read, kept read-only and classified in the capability audit; noEffect is read. The renamed sounds are offered in place.
public sealed class Runtime0302Tests
{
    private static readonly string[] Roots = ["GP-31 Grenade Pistol", "P-72 Crisper", "LAS-5 Scythe", "LAS-7 Dagger", "CQC-42 Machete", "CQC-73 Entrenchment Tool", "SMG-37 Defender"];
    private const string Sentry = "A/AC-8 Autocannon Sentry / weapon", Mg43 = "A/MG-43 Machine Gun Sentry / weapon";

    // An SDK archive rewritten entry by entry (the published ZIP is the base; nothing else changes).
    private static byte[] Rewrite(string version, Func<string, byte[], byte[]> entry)
    {
        using var output = new MemoryStream();
        using (var source = ZipFile.OpenRead(Path.Combine(AppContext.BaseDirectory, "Fixtures", "sdk-" + version + ".zip")))
        using (var target = new ZipArchive(output, ZipArchiveMode.Create, true))
            foreach (var e in source.Entries)
            {
                using var s = e.Open(); using var m = new MemoryStream(); s.CopyTo(m);
                using var w = target.CreateEntry(e.FullName).Open(); var bytes = e.FullName.EndsWith('/') ? [] : entry(e.FullName, m.ToArray()); w.Write(bytes);
            }
        return output.ToArray();
    }
    private static readonly string[] Graphs = ["AttachmentOptionCapabilities.json", "ExplosionAuthoringCapabilities.json", "PlayerWeaponFireModeGraph.json",
        "PlayerWeaponHeatCapabilities.json", "PlayerWeaponMagazineOptionGraph.json", "PlayerWeaponProjectileReferenceGraph.json", "PlayerWeaponTerminalActionGraph.json",
        "ProjectileCompositionCapabilities.json"];

    [Fact] public async Task The_0_30_2_graphs_carry_their_own_version_and_0_30_1_republished_the_0_30_0_graphs()
    {
        using var e = new TestEnvironment();
        var pin = await SdkFixtures.Install(e, Runtime030Tests.Version);
        Assert.Equal("0.30.3", pin.Version); Assert.True(pin.IsPinnedBuild); Assert.NotNull(pin.PlayerWeapons); Assert.NotNull(pin.AttackOutputs);
        foreach (var graph in Graphs)
            Assert.Equal("0.30.3", JsonDocument.Parse(SdkFixtures.Entry(Runtime030Tests.Version, graph)).RootElement.GetProperty("hd2RuntimeVersion").GetString());
        // 0.30.2 (the SDK of projects bound by the 1.7.x SDK move) still loads, with its graphs labelled 0.30.2, as a build other than the pin.
        var v0302 = await SdkFixtures.Install(e, Runtime030Tests.Previous);
        Assert.Equal("0.30.2", v0302.Version); Assert.Null(v0302.IsPinnedBuild); Assert.NotNull(v0302.PlayerWeapons); Assert.NotNull(v0302.Composition); Assert.NotNull(v0302.AttackOutputs);
        foreach (var graph in Graphs)
            Assert.Equal("0.30.2", JsonDocument.Parse(SdkFixtures.Entry(Runtime030Tests.Previous, graph)).RootElement.GetProperty("hd2RuntimeVersion").GetString());
        // 0.30.1 changed no SDK content: its JSON is 0.30.0's with the version renamed, and its eight graphs are 0.30.0's bytes (labelled 0.19.0;
        // checked against the published HD2Runtime-0.30.1-sdk.zip, SHA-256 e48689fd…ad4a). They are accepted by their digests.
        var v0301 = Rewrite(Runtime030Tests.Release, (name, bytes) => name.EndsWith(".json", StringComparison.Ordinal)
            ? Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(bytes).Replace("\"0.30.0\"", "\"0.30.1\"", StringComparison.Ordinal)) : bytes);
        e.GitHub.Archive = v0301;
        var sdk0301 = await e.Cache.InstallAsync(FakeGitHub.MakeRelease("0.30.1", v0301));
        Assert.Equal("0.30.1", sdk0301.Version); Assert.NotNull(sdk0301.PlayerWeapons); Assert.NotNull(sdk0301.Composition);
        // A 0.30.2 or 0.30.3 graph labelled 0.19.0 matches no published digest and is refused rather than misread.
        foreach (var version in new[] { Runtime030Tests.Previous, Runtime030Tests.Version })
        {
            var relabelled = Rewrite(version, (name, bytes) => name == "PlayerWeaponFireModeGraph.json"
                ? Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(bytes).Replace("\"hd2RuntimeVersion\": \"" + version + "\"", "\"hd2RuntimeVersion\": \"0.19.0\"", StringComparison.Ordinal)) : bytes);
            e.GitHub.Archive = relabelled;
            await Assert.ThrowsAnyAsync<Exception>(() => e.Cache.InstallAsync(FakeGitHub.MakeRelease(version, relabelled)));
        }
    }

    [Fact] public async Task The_seven_resolved_weapons_are_writable_and_the_scythe_keeps_its_overridden_heat_read_only()
    {
        using var e = new TestEnvironment(); var sdk = await SdkFixtures.Install(e, Runtime030Tests.Version); var pw = sdk.PlayerWeapons!;
        foreach (var name in Roots)
        {
            var weapon = pw.Weapon(name);
            Assert.Equal("UNIQUE", weapon.Resolution); Assert.False(weapon.OrdinaryWritesBlocked); Assert.Single(weapon.Resources);
            Assert.Contains(weapon.Fields, f => f.Editable);
        }
        // 237 published writable; the two weapon.sound fields stay read-only until they are authored (the GP-31's explosion status slot is
        // authored since 1.7.1).
        Assert.Equal(235, Roots.Sum(n => pw.Weapon(n).Fields.Count(f => f.Editable)));
        // The GP-31 and the Dagger read their own values now (the Dagger's old 2000 heat capacity was the other root's).
        Assert.Equal(100d, pw.Field("LAS-7 Dagger", "heat.capacity").CurrentDefault.GetDouble());
        Assert.Equal(4, pw.Field("GP-31 Grenade Pistol", "rounds.starting_rounds").CurrentDefault.GetInt32());
        // The Scythe's default Laser Heatsink overwrites its capacity and heatsinks at every build: read-only with Runtime's reason, although
        // the summary and the heat companion still count them writable (accepted with exactly that difference).
        foreach (var id in new[] { "heat.capacity", "heatsink.starting", "heatsink.from_supply", "heatsink.spare" })
        {
            var f = pw.Field("LAS-5 Scythe", id);
            Assert.False(f.Editable); Assert.StartsWith("A default customization item (", f.Reason);
        }
        Assert.True(pw.Field("LAS-5 Scythe", "heat.heat_per_second").Editable);
        // 3365 counted writable by the summary, 3361 published editable, 3306 authored by this build (weapon sounds not yet).
        Assert.Equal(3365, pw.Summary.WritableFieldInstances); Assert.Equal(3306, pw.Weapons.Sum(w => w.Fields.Count(f => f.Editable)));
        // The ammo companion names the candidate roots each correction chose from.
        var gp31 = sdk.PlayerAmmo!.Weapons.Single(w => w.Name == "GP-31 Grenade Pistol");
        Assert.Equal(2, gp31.CandidateRoots!.Count); Assert.Contains(Assert.Single(gp31.Resources), gp31.CandidateRoots);
        Assert.Contains(sdk.LiveEvidence!.Family("weapon_roots_0_30_2")!.ProvenTargets!, t => t.Target == "LAS-7 Dagger" && t.Field == "heat.capacity");
    }

    [Fact] public async Task What_0_30_2_adds_and_ModBuilder_does_not_author_yet_is_read_only_and_classified()
    {
        using var e = new TestEnvironment(); var sdk = await SdkFixtures.Install(e, Runtime030Tests.Version);
        // Status slots on player-weapon explosions and the 23 extended statuses are authored since 1.7.1 (StatusEffects0302Tests).
        Assert.True(AuthoredTypes.PlayerExplosionStatus); Assert.True(AuthoredTypes.ExtendedStatuses);
        Assert.Equal(26, sdk.PlayerWeapons!.Weapons.SelectMany(w => w.Fields).Count(f => AuthoredTypes.IsPlayerExplosionStatus(f.SemanticFieldId) && f.Editable));
        // Sentry and emplacement projectile hosts: modelled from the vehicle weapon catalog and authored since 1.7.1 (SentryProjectileSwapTests);
        // the A/MG-43 and A/G-16 stay read-only with Runtime's own reason.
        Assert.True(AuthoredTypes.StratagemProjectileHosts);
        var hosts = sdk.Entities.VehicleWeapons.StratagemHosts;
        Assert.Equal((9, 7), (hosts.Count, hosts.Values.Count(h => h.Writable)));
        Assert.Equal("hd2.stratagem('A/AC-8 Autocannon Sentry'):attack('primary'):projectile_source()", hosts[Sentry].Api);
        Assert.True(ProjectileHosts.Entry(sdk, AttackProjectileSource.VehicleKind, Sentry, "primary")!.Writable);
        var mg43 = ProjectileHosts.Entry(sdk, AttackProjectileSource.VehicleKind, Mg43, "primary")!;
        Assert.Equal((false, hosts[Mg43].Reason), (mg43.Writable, mg43.Reason));
        // Backpack-fed ammunition rows: writes accepted but expected to do nothing (noEffect.reason), read.
        var none = sdk.SupportAuthoring.FieldInstances.Where(f => f.NoEffect != null).ToArray();
        Assert.Equal(11, none.Length); Assert.Equal(5, none.Select(f => f.SupportWeapon).Distinct().Count());
        // The capability audit classifies each with its reason, and nothing is unexpectedly missing.
        var report = CapabilityAudit.Run(sdk, CapabilityAuditTests.Files(Runtime030Tests.Version));
        Assert.Empty(report.UnexpectedMissing);
        var host = report.Entries.Single(x => x.Capability == "projectile host (" + CapabilityExemptions.StratagemHostKind + ")" && x.Type == "writable");
        Assert.Equal((CapabilityClass.SUPPORTED_UI, 7), (host.Class, host.Instances));
        Assert.Equal(CapabilityClass.RUNTIME_READ_ONLY, report.Entries.Single(x => x.Capability == "projectile host (" + CapabilityExemptions.StratagemHostKind + ")" && x.Type == "read-only").Class);
        AuditEntry Entry(string area, string capability) => report.Entries.Single(x => x.Area == area && x.Capability == capability);
        Assert.Equal((CapabilityClass.SUPPORTED_UI, 11), (Entry("status_effect", "attachable status (player_attack)").Class, Entry("status_effect", "attachable status (player_attack)").Instances));
        Assert.Equal((CapabilityClass.SUPPORTED_UI, 11), (Entry("support_weapon_notice", "noEffect").Class, Entry("support_weapon_notice", "noEffect").Instances));
    }

    [Fact] public async Task Features_of_0_30_2_raise_the_required_runtime_and_what_0_30_0_released_keeps_its_floor()
    {
        using var e = new TestEnvironment(); var pinned = await SdkFixtures.Install(e, Runtime030Tests.Version);
        var sdk = pinned with { Version = Runtime030Tests.Release };
        ModProject Project() => new() { SdkVersion = Runtime030Tests.Release };
        Assert.Equal(Runtime030Tests.Release, RuntimeRequirement.Minimum(Project(), sdk));
        var roots = Project(); roots.WeaponChanges.Add(new WeaponChange { Weapon = "GP-31 Grenade Pistol", SemanticFieldId = "rounds.starting_rounds" });
        Assert.Equal(RuntimeRequirement.Release0302, RuntimeRequirement.Minimum(roots, sdk));
        Assert.Contains((RuntimeRequirement.Release0302, "GP-31 Grenade Pistol (blocked as a duplicate identity before 0.30.2)"), RuntimeRequirement.Floors(roots, sdk));
        var status = Project(); status.WeaponChanges.Add(new WeaponChange { Weapon = "PLAS-101 Purifier", SemanticFieldId = "explosion.primary.impact.damage.status_1_type" });
        Assert.Equal(RuntimeRequirement.Release0302, RuntimeRequirement.Minimum(status, sdk));
        var host = Project(); host.AttackOutputChanges = [new AttackOutputChange { HostKind = AttackOutputChange.VehicleHost, Weapon = Sentry, AttackRole = "primary" }];
        Assert.Contains((RuntimeRequirement.Release0302, "sentry / emplacement projectile host " + Sentry), RuntimeRequirement.Floors(host, sdk));
        // Custom content keeps the custom stratagem API's 0.30.0 floor; a sound name 0.30.2 added raises it.
        CustomContentSection Pelican(string sound) => new()
        {
            Stratagems = [new CustomStratagem { Id = "gunship", Delivery = new CustomDelivery { Family = DeliveryFamily.Pelican, Pelican = new PelicanDelivery { Gun = new PelicanGun { Sound = sound } } } }],
        };
        var old = new ModProject { SdkVersion = "0.28.1", CustomContent = Pelican("primary/smg37") };
        Assert.Equal(RuntimeRequirement.Release030, CustomContentTargets.Current.MinimumRuntime);
        Assert.Equal(RuntimeRequirement.Release030, RuntimeRequirement.Minimum(old, pinned with { Version = "0.28.1" }));
        var renamed = new ModProject { SdkVersion = "0.28.1", CustomContent = Pelican("seaf/3") };
        Assert.Equal(RuntimeRequirement.Release0302, RuntimeRequirement.Minimum(renamed, pinned with { Version = "0.28.1" }));
        // A project bound to 0.30.2 declares 0.30.2 whatever it writes, and one bound to the pinned 0.30.3 declares 0.30.3.
        Assert.Equal("0.30.2", RuntimeRequirement.Minimum(new ModProject { SdkVersion = "0.30.2" }, pinned with { Version = Runtime030Tests.Previous }));
        Assert.Equal("0.30.3", RuntimeRequirement.Minimum(new ModProject { SdkVersion = "0.30.3" }, pinned));
    }

    [Fact] public void The_renamed_sounds_are_offered_in_place_of_their_old_names()
    {
        Assert.Equal("seaf/3", WeaponSounds.Renamed["primary/smg37/seaf"]); Assert.Equal("other/wep_grenadier_rifle/1", WeaponSounds.Renamed["secondary/gp31/alt"]);
        var target = CustomContentTargets.Current;
        Assert.DoesNotContain("primary/smg37/seaf", target.Schema.Catalogs.Sounds); Assert.Contains("seaf/3", target.Schema.Catalogs.Sounds);
        Assert.Equal("seaf/3", target.RenamedChoice("payload.gun.sound", "primary/smg37/seaf"));
        Assert.Equal("other/wep_grenadier_rifle/1", target.RenamedChoice("payload.weapon.sound", "secondary/gp31/alt"));
        Assert.Null(target.RenamedChoice("payload.gun.sound", "seaf/3")); Assert.Null(target.RenamedChoice("payload.gun.round", "primary/smg37/seaf"));
        Assert.Equal(171, target.Schema.Catalogs.Sounds.Count);
    }
}
