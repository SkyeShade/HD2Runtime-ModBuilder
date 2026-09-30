using System.IO.Compression;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using HD2RuntimeGUI.Core.Generation;
using HD2RuntimeGUI.Core.Metadata;
using HD2RuntimeGUI.Core.Models;
using HD2RuntimeGUI.Core.Projects;
using HD2RuntimeGUI.Core.Services;
using Xunit;

namespace HD2RuntimeGUI.Tests;

// 0.28.0 unified projectile hosts (attack-outputs.md: support hosts, mounted hosts, one donor pool) on the frozen SDK fixture: support weapons,
// vehicle mounts and the Guard Dog drone gun swap their fired projectile through the same active-source rule as player weapons, with the
// opt-ins their own field publishes and the exact live-proven donors dropping allow_unverified_effect.
public sealed class ProjectileHostTests
{
    public const string SupportKind = AttackOutputChange.SupportHost, MountKind = AttackOutputChange.VehicleHost, PlayerKind = AttackOutputChange.PlayerHost;
    public const string Eat17 = "EAT-17 Expendable Anti-Tank", Stalwart = "M-105 Stalwart", Patriot = "EXO-45 Patriot Exosuit / right_gun", GuardDog = "AX/AR-23 Guard Dog / gun";
    public const string Gunner = "M-102 Gunner FRV / gun", SuperEarth = "FRV (Super Earth variant) / gun";
    public static string Out(string id) => "output/v1/projectile/" + id;
    public static readonly string Scorcher = Out("plas-1-scorcher"), Apw = Out("apw-1-anti-materiel-rifle"), Talon = Out("las-58-talon"), Eat17Row = Out("eat-17-expendable-anti-tank"),
        Leveller = Out("eat-411-leveller"), Gl21 = Out("gl-21-grenade-launcher"), Napalm = Out("eat-700-expendable-napalm"), PatriotRow = Out("exo-45-patriot-exosuit-right-gun"),
        Penetrator = Out("ar-23p-liberator-penetrator"), GuardDogRow = Out("ax-ar-23-guard-dog-gun"), Mortar = Out("a-m-23-ems-mortar-sentry"), SpareTwin = Out("s-11-speargun-spare-twin");
    public static string[] Ids(string lua) => Regex.Matches(lua, @"\bid='([^']+)'").Select(m => m.Groups[1].Value).ToArray();
    private static string Flat(string s) => Regex.Replace(s, @"\s+", "");
    private static AttackOutputHost Host(SdkMetadata sdk, string kind, string weapon) => ProjectileHosts.Host(sdk, kind, weapon, "primary", out var reason) ?? throw new Xunit.Sdk.XunitException(reason);
    private static AttackOutputDonor Donor(SdkMetadata sdk, AttackOutputHost host, string output) => sdk.AttackOutputs!.Donors(host).Single(d => d.Output.SemanticId == output);

    [Fact] public async Task Support_hosts_take_their_field_opt_ins_and_the_live_proven_pair_needs_none()
    {
        using var e = new TestEnvironment(); var (w, sdk) = await EnemyAuthoringTests.Fresh(e);
        var eat = Host(sdk, SupportKind, Eat17);
        Assert.Equal(SupportKind, eat.HostKind); Assert.Equal(AttackOutputHost.Component, eat.Mechanism); Assert.Equal("explosive_impact", eat.CompatibilityClass);
        Assert.Equal(["allow_unverified_effect"], eat.BaseAcknowledgements); Assert.Contains(Scorcher, eat.LiveProvenValues!); Assert.NotNull(eat.EffectReason);
        // EAT-17 <- PLAS-1 Scorcher (a player donor on a support host) is live-proven: no allow_unverified_* at all.
        var scorcher = Donor(sdk, eat, Scorcher);
        Assert.True(scorcher.Allowed && scorcher.ProvenOnHost && !scorcher.CrossClass); Assert.Empty(scorcher.Acknowledgements);
        // Another same-class donor keeps the support host path's allow_unverified_effect; a cross-class donor adds allow_unverified_reference.
        Assert.Equal(["allow_unverified_effect"], Donor(sdk, eat, Leveller).Acknowledgements);
        var gl21 = Donor(sdk, eat, Gl21); Assert.True(gl21.CrossClass && gl21.Allowed);
        Assert.Equal(["allow_unverified_effect", "allow_unverified_reference"], gl21.Acknowledgements);
        // The host's own row is its baseline, never a donor.
        Assert.DoesNotContain(sdk.AttackOutputs!.Donors(eat), d => d.Output.SemanticId == Eat17Row);

        await w.SetHostOutputAsync(SupportKind, Eat17, "primary", Scorcher);
        var change = Assert.Single(w.Project!.AttackOutputChanges!);
        Assert.Equal(SupportKind, change.HostKind); Assert.Empty(change.Acknowledgements); Assert.Equal(11, w.Project.FormatVersion); Assert.Null(w.BuildError);
        var lua = w.LuaPreview;
        Assert.Contains("target=hd2.support_weapon('EAT-17 Expendable Anti-Tank'):attack('primary'),", lua);
        Assert.Contains("field=hd2.fields.attack.projectile,", lua);
        Assert.Contains("expect=hd2.support_weapon('EAT-17 Expendable Anti-Tank'):attack('primary'):projectile(),", lua);
        Assert.Contains("value=hd2.attack_output('" + Scorcher + "'),", lua);
        Assert.DoesNotContain("allow_unverified", lua); Assert.DoesNotContain("allow_shared", lua);
        // A non-proven support pair carries allow_unverified_effect, with no acknowledgement step.
        await w.SetHostOutputAsync(SupportKind, Eat17, "primary", Leveller);
        Assert.Equal(change.Id, Assert.Single(w.Project.AttackOutputChanges!).Id);
        Assert.Contains("allow_unverified_effect=true", w.LuaPreview); Assert.DoesNotContain("allow_unverified_reference", w.LuaPreview);
        var json = await File.ReadAllTextAsync(e.Paths.ProjectFile(w.Project.Id));
        Assert.Contains("\"hostKind\": \"support_weapon\"", json);
        await w.SetHostOutputAsync(SupportKind, Eat17, "primary", null); Assert.Null(w.Project.AttackOutputChanges);
    }

    [Fact] public async Task The_stalwart_fires_the_amr_round_live_proven()
    {
        using var e = new TestEnvironment(); var (w, sdk) = await EnemyAuthoringTests.Fresh(e);
        var stalwart = Host(sdk, SupportKind, Stalwart);
        Assert.Empty(Donor(sdk, stalwart, Apw).Acknowledgements); Assert.True(Donor(sdk, stalwart, Apw).ProvenOnHost);
        await w.SetHostOutputAsync(SupportKind, Stalwart, "primary", Apw);
        Assert.Null(w.BuildError);
        Assert.Contains(Flat("target=hd2.support_weapon('M-105 Stalwart'):attack('primary'),field=hd2.fields.attack.projectile,expect=hd2.support_weapon('M-105 Stalwart'):attack('primary'):projectile(),value=hd2.attack_output('" + Apw + "'),"), Flat(w.LuaPreview));
    }

    [Fact] public async Task The_patriot_minigun_takes_its_proven_donors_without_opt_ins_and_others_with_both()
    {
        using var e = new TestEnvironment(); var (w, sdk) = await EnemyAuthoringTests.Fresh(e);
        var patriot = Host(sdk, MountKind, Patriot);
        Assert.Equal(["allow_unverified_effect"], patriot.BaseAcknowledgements); Assert.Equal("conventional_plain", patriot.CompatibilityClass);
        // EAT-17 and Scorcher are cross-class, Talon same-class: all three live-proven on this mount.
        foreach (var proven in new[] { Eat17Row, Talon, Scorcher }) { var d = Donor(sdk, patriot, proven); Assert.True(d.Allowed && d.ProvenOnHost); Assert.Empty(d.Acknowledgements); }
        Assert.True(Donor(sdk, patriot, Eat17Row).CrossClass);
        var gl21 = Donor(sdk, patriot, Gl21);
        Assert.True(gl21.CrossClass && !gl21.ProvenOnHost); Assert.Equal(["allow_unverified_effect", "allow_unverified_reference"], gl21.Acknowledgements);
        await w.SetHostOutputAsync(MountKind, Patriot, "primary", Eat17Row);
        Assert.Null(w.BuildError);
        var lua = Flat(w.LuaPreview);
        Assert.Contains(Flat("target=hd2.vehicle('EXO-45 Patriot Exosuit'):weapon('right_gun'):attack('primary'):projectile_source().target,"), lua);
        Assert.Contains(Flat("field=hd2.fields.attack.projectile,expect=hd2.vehicle('EXO-45 Patriot Exosuit'):weapon('right_gun'):attack('primary'),value=hd2.attack_output('" + Eat17Row + "'),"), lua);
        Assert.DoesNotContain("allow_unverified", lua);
        await w.SetHostOutputAsync(MountKind, Patriot, "primary", Gl21);
        Assert.Contains("allow_unverified_effect=true", w.LuaPreview); Assert.Contains("allow_unverified_reference=true", w.LuaPreview);
        // What the minigun fires now, and which rows this project swaps hosts onto.
        Assert.Equal(Gl21, w.FiredRow(MountKind, Patriot, "primary")!.SemanticId);
        Assert.Equal([Patriot], w.HostsSwappedOnto(Gl21));
    }

    [Fact] public async Task The_frv_gun_is_one_weapon_entity_in_two_mounts_and_always_carries_allow_shared()
    {
        using var e = new TestEnvironment(); var (w, sdk) = await EnemyAuthoringTests.Fresh(e);
        var gunner = Host(sdk, MountKind, Gunner);
        Assert.Equal(["allow_shared", "allow_unverified_effect"], gunner.BaseAcknowledgements); Assert.Equal([SuperEarth], gunner.SharedEntity!);
        // The other mount of the same entity fires the same record: it is the host's baseline, not a donor.
        Assert.DoesNotContain(sdk.AttackOutputs!.Donors(gunner), d => d.Output.Owner.Name == SuperEarth);
        var stalwart = Out("m-105-stalwart");
        Assert.Equal(["allow_shared", "allow_unverified_effect"], Donor(sdk, gunner, stalwart).Acknowledgements);
        await w.SetHostOutputAsync(MountKind, Gunner, "primary", stalwart);
        Assert.Contains("allow_shared=true", w.LuaPreview); Assert.Null(w.BuildError);
        // Swapping the Super Earth FRV gun replaces the swap set through the Gunner: one write per entity, resolved where it is made.
        Assert.Equal(stalwart, w.HostOutput(MountKind, SuperEarth, "primary")!.Output);
        await w.SetHostOutputAsync(MountKind, SuperEarth, "primary", Out("mg-206-heavy-machine-gun"));
        var change = Assert.Single(w.Project!.AttackOutputChanges!); Assert.Equal(SuperEarth, change.Weapon);
        Assert.Contains("hd2.vehicle('FRV (Super Earth variant)'):weapon('gun')", w.LuaPreview);
        // Two saved writes on the same entity (an edited project file) block the build with a reason.
        w.Project.AttackOutputChanges!.Add(AttackOutputChangeService.Create(sdk, MountKind, Gunner, "primary", stalwart));
        Assert.Throws<InvalidDataException>(() => AttackOutputChangeService.Operations(w.Project, sdk));
    }

    [Fact] public async Task The_guard_dog_gun_is_swapped_through_its_backpack_drone()
    {
        using var e = new TestEnvironment(); var (w, sdk) = await EnemyAuthoringTests.Fresh(e);
        Assert.Equal("AX/AR-23 Guard Dog", ProjectileHosts.Carrier(sdk, GuardDog)); Assert.Null(ProjectileHosts.Carrier(sdk, Patriot));
        var dog = Host(sdk, MountKind, GuardDog);
        Assert.Equal(["allow_unverified_effect"], Donor(sdk, dog, Penetrator).Acknowledgements);
        await w.SetHostOutputAsync(MountKind, GuardDog, "primary", Penetrator);
        Assert.Null(w.BuildError);
        var lua = Flat(w.LuaPreview);
        Assert.Contains(Flat("target=hd2.backpack('AX/AR-23 Guard Dog'):drone():weapon():attack('primary'):projectile_source().target,allow_unverified_effect=true,"), lua);
        Assert.Contains(Flat("expect=hd2.backpack('AX/AR-23 Guard Dog'):drone():weapon():attack('primary'),value=hd2.attack_output('" + Penetrator + "'),"), lua);
        Assert.DoesNotContain("hd2.vehicle('AX/AR-23 Guard Dog')", w.LuaPreview);
        // The backpack lists its drone's projectile source.
        Assert.Equal([GuardDog], ProjectileHosts.SourcesOf(sdk, MountKind, "AX/AR-23 Guard Dog").Select(s => s.Weapon));
    }

    [Fact] public async Task Read_only_hosts_are_listed_with_runtimes_reason_and_refuse_a_swap()
    {
        using var e = new TestEnvironment(); var (w, sdk) = await EnemyAuthoringTests.Fresh(e);
        var all = ProjectileHosts.All(sdk);
        Assert.Equal(sdk.AttackOutputs!.ProjectileSources.Length, all.Count);
        Assert.Equal([PlayerKind, SupportKind, MountKind], all.Select(h => h.Source.HostKind).Distinct());
        // Every host of the host model (46 component hosts: player, support and mounted; 6 ammunition hosts) is writable here.
        Assert.All(sdk.AttackOutputs.HostModel.ComponentHosts.Concat(sdk.AttackOutputs.HostModel.AmmunitionHosts), h => Assert.Contains(all, x => x.Source.Weapon == h && x.Writable));
        Assert.Equal(8, all.Count(h => h.Writable && h.Source.HostKind == SupportKind)); Assert.Equal(9, all.Count(h => h.Writable && h.Source.HostKind == MountKind));
        foreach (var (kind, weapon, reason) in new[] {
            (SupportKind, "AC-8 Autocannon", "WeaponRounds"), (SupportKind, "GR-8 Recoilless Rifle", "weapon function"), (SupportKind, "GL-28 Belt-Fed Grenade Launcher", "not magazine-fed"),
            (SupportKind, "MG-43 Machine Gun", "magazine pattern"), (SupportKind, "FAF-14 Spear", "spawns an entity"), (MountKind, "EXO-55 Breakthrough Exosuit / right_gun", "WeaponRounds"),
            (MountKind, "EXO-45 Patriot Exosuit / left_gun", "ProjectileEntity"), (MountKind, "TD-110 Maelstrom / attach_tank_gun", "magazine pattern"), (MountKind, "TD-220 Bastion MK XVI / attach_tank_gun", "weapon function") })
        {
            var entry = all.Single(h => h.Source.HostKind == kind && h.Source.Weapon == weapon);
            Assert.False(entry.Writable); Assert.Contains(reason, entry.Reason);
            var refused = await Assert.ThrowsAsync<InvalidDataException>(() => w.SetHostOutputAsync(kind, weapon, "primary", Talon));
            Assert.Contains(reason, refused.Message);
        }
        Assert.Contains(all, h => h.Source.Weapon == "AR-23 Liberator" && h.Writable && h.Host!.Mechanism == AttackOutputHost.AmmunitionMechanism);
        Assert.Null(w.Project!.AttackOutputChanges);
    }

    [Fact] public async Task Function_ammunition_rows_other_families_and_unloadable_donors_are_never_attack_projectiles()
    {
        using var e = new TestEnvironment(); var (w, sdk) = await EnemyAuthoringTests.Fresh(e);
        var catalog = sdk.AttackOutputs!;
        foreach (var entry in ProjectileHosts.All(sdk).Where(h => h.Writable))
        {
            var donors = catalog.Donors(entry.Host!);
            Assert.DoesNotContain(donors, d => d.Output.SemanticId == Mortar || d.Output.SemanticId == SpareTwin);
            Assert.All(donors, d => Assert.Equal("projectile", d.Output.Family));
            // The Guard Dog gun publishes no package: refused everywhere (ASSET_UNAVAILABLE, after Runtime's cross-class host check).
            if (donors.SingleOrDefault(d => d.Output.SemanticId == GuardDogRow) is { } dog)
            { Assert.False(dog.Allowed); Assert.StartsWith(dog.CrossClass && !entry.Host!.CrossClassHost ? "CROSS_CLASS_HOST_REJECTED" : "ASSET_UNAVAILABLE", dog.Refusal); }
        }
        var refused = await Assert.ThrowsAsync<InvalidDataException>(() => w.SetHostOutputAsync(SupportKind, Stalwart, "primary", GuardDogRow));
        Assert.StartsWith("ASSET_UNAVAILABLE", refused.Message);
        var unavailable = catalog.Unavailable();
        Assert.Equal("OUTPUT_SCOPE", unavailable.Single(u => u.Output.SemanticId == Mortar).Code); Assert.Equal("OUTPUT_SCOPE", unavailable.Single(u => u.Output.SemanticId == SpareTwin).Code);
        var beam = unavailable.Single(u => u.Output.SemanticId == "output/v1/beam/las-98-laser-cannon");
        Assert.Equal("INCOMPATIBLE_OUTPUT_FAMILY", beam.Code); Assert.Equal(beam.Output.BlockedReason, beam.Reason);
        Assert.Equal(["arc", "beam", "melee", "spray"], unavailable.Where(u => u.Code == "INCOMPATIBLE_OUTPUT_FAMILY").Select(u => u.Output.Family).Distinct().Order());
        // A projectile row its owner does not fire (another selector owns the shot) is listed with the published reason.
        var mg43 = unavailable.Single(u => u.Output.SemanticId == Out("mg-43-machine-gun")); Assert.Equal("NOT_SELECTABLE", mg43.Code); Assert.Contains("selector", mg43.Reason);
        // Every catalogued output is either a donor candidate or listed here: nothing is hidden.
        Assert.Equal(catalog.Outputs.Length, catalog.Outputs.Count(o => o.SelectableAsProjectileReference && o.AttackReferenceAllowed) + unavailable.Count);
    }

    [Fact] public async Task One_donor_pool_serves_every_direction()
    {
        using var e = new TestEnvironment(); var (w, sdk) = await EnemyAuthoringTests.Fresh(e);
        var reprimand = w.OutputHost("SMG-32 Reprimand", "primary", out _)!;
        var stalwart = Host(sdk, SupportKind, Stalwart); var patriot = Host(sdk, MountKind, Patriot);
        // primary -> primary, support -> primary (live-proven cross-class composition), mounted -> primary.
        Assert.True(Donor(sdk, reprimand, Talon).Allowed);
        var napalm = Donor(sdk, reprimand, Napalm); Assert.True(napalm.CrossClass && napalm.ProvenOnHost); Assert.Empty(napalm.Acknowledgements);
        Assert.True(Donor(sdk, reprimand, Out("m-103-supply-frv-gun")).Allowed);
        // primary -> support, support -> support, mounted -> support.
        Assert.True(Donor(sdk, stalwart, Penetrator).Allowed); Assert.True(Donor(sdk, stalwart, Apw).Allowed);
        var mounted = Donor(sdk, stalwart, PatriotRow); Assert.True(mounted.Allowed && !mounted.CrossClass); Assert.Equal(["allow_unverified_effect"], mounted.Acknowledgements);
        // primary -> mounted, support -> mounted, stratagem-owned rows only as a programmable-ammunition projectile.
        Assert.True(Donor(sdk, patriot, Penetrator).Allowed); Assert.True(Donor(sdk, patriot, Out("mg-206-heavy-machine-gun")).Allowed);
        await w.SetAttackOutputAsync("SMG-32 Reprimand", "primary", Napalm);
        await w.SetHostOutputAsync(SupportKind, Stalwart, "primary", PatriotRow);
        await w.SetHostOutputAsync(MountKind, Patriot, "primary", Penetrator);
        Assert.Null(w.BuildError); Assert.Equal(3, w.Project!.AttackOutputChanges!.Count);
        Assert.Contains("value=hd2.attack_output('" + PatriotRow + "'),", w.LuaPreview);
        var ids = Ids(w.LuaPreview); Assert.Equal(ids.Distinct().Count(), ids.Length);
    }

    [Fact] public async Task Player_outputs_keep_their_saved_json_ids_and_evidence()
    {
        using var e = new TestEnvironment(); var (w, sdk) = await EnemyAuthoringTests.Fresh(e);
        await w.SetAttackOutputAsync("AR-23 Liberator", "primary", Napalm);
        var change = w.Project!.AttackOutputChanges![0];
        Assert.Null(change.HostKind); Assert.True(change.IsPlayer);
        Assert.DoesNotContain("hostKind", await File.ReadAllTextAsync(e.Paths.ProjectFile(w.Project.Id)));
        Assert.Contains("id='output-" + SupportChangeService.Hash(w.Project.ResourceId + "\nAR-23 Liberator\nprimary")[..24] + "'", w.LuaPreview);
        // The evidence shape of player hosts is the one saved before unified hosts existed.
        var (host, donor) = AttackOutputChangeService.Resolve(sdk, "AR-23 Liberator", "primary", Napalm);
        Assert.Equal(SupportChangeService.Hash(System.Text.Json.JsonSerializer.Serialize(new
        {
            host.Weapon, host.Role, host.Mechanism, host.CompatibilityClass, Ammunition = host.Ammunition?.SemanticId, AmmunitionItem = host.Ammunition?.Item,
            donor.Output.SemanticId, OutputClass = donor.Output.CompatibilityClass, donor.Output.Kind, donor.Output.Owner, donor.Output.Package, donor.CrossClass, donor.Acknowledgements,
        })), change.Evidence);
        // A player reset leaves support and mounted swaps alone.
        await w.SetHostOutputAsync(SupportKind, Stalwart, "primary", Apw);
        await w.ResetWeaponsAsync();
        Assert.Equal(SupportKind, Assert.Single(w.Project.AttackOutputChanges!).HostKind);
    }

    [Fact] public async Task Host_swaps_reopen_duplicate_and_validate_as_semantic_identities()
    {
        using var e = new TestEnvironment(); var (w, sdk) = await EnemyAuthoringTests.Fresh(e);
        await w.SetHostOutputAsync(SupportKind, Eat17, "primary", Scorcher);
        await w.SetHostOutputAsync(MountKind, GuardDog, "primary", Penetrator);
        var lua = w.LuaPreview; var saved = w.Project!.AttackOutputChanges!.ToArray();
        await w.OpenAsync(w.Project.Id);
        Assert.Equal(lua, w.LuaPreview); Assert.Null(w.BuildError); Assert.Equal(11, w.Project!.FormatVersion);
        Assert.Equal(saved.Select(c => (c.HostKind, c.Weapon, c.Evidence)), w.Project.AttackOutputChanges!.Select(c => (c.HostKind, c.Weapon, c.Evidence)));
        Assert.All(w.Project.AttackOutputChanges!, c => Assert.Null(w.AttackOutputIssue(c)));
        var p = w.Project; var good = p.AttackOutputChanges!.ToList();
        foreach (var bad in new[] { good[0] with { HostKind = "stratagem" }, good[1] with { Weapon = "AX/AR-23 Guard Dog" }, good[0] with { Mechanism = "ammunition" } })
        {
            p.AttackOutputChanges = [bad]; Assert.Throws<InvalidDataException>(() => ProjectIdentity.Validate(p));
        }
        p.AttackOutputChanges = good; ProjectIdentity.Validate(p);
        await w.DuplicateAsync(p.Id, new("Copy", "Tests", "mods/tests/copy_hosts", "0.1.0"));
        Assert.Equal(good.Select(c => c.Evidence), w.Project!.AttackOutputChanges!.Select(c => c.Evidence));
        Assert.DoesNotContain(w.Project.AttackOutputChanges!, c => good.Any(g => g.Id == c.Id));
    }

    [Fact] public async Task A_new_snapshot_that_withdraws_a_live_proven_donor_flags_the_swap_for_review_where_it_is_made()
    {
        using var e = new TestEnvironment(); var (w, sdk) = await EnemyAuthoringTests.Fresh(e);
        await w.SetHostOutputAsync(MountKind, Patriot, "primary", Talon);
        Assert.Empty(w.Project!.AttackOutputChanges![0].Acknowledgements);
        // Next snapshot: the Talon is no longer live-proven on the Patriot minigun.
        var file = Path.Combine(EnemyAuthoringTests.DevSdk(e), VehicleWeaponReader.FileName);
        var n = JsonNode.Parse(await File.ReadAllBytesAsync(file))!;
        var field = n["fieldInstances"]!.AsArray().Single(f => (string)f!["weapon"]! == Patriot && (string)f!["type"]! == "projectile_reference")!;
        foreach (var list in new[] { field["liveProvenValues"]!.AsArray(), field["liveEvidence"]!["values"]!.AsArray() }) list.Remove(list.Single(v => (string)v! == Talon));
        var reopened = await Reopen(e, w.Project.Id, (VehicleWeaponReader.FileName, System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(n)));
        var change = reopened.Project!.AttackOutputChanges![0];
        Assert.NotNull(reopened.AttackOutputIssue(change)); Assert.NotNull(reopened.BuildError);
        // Re-choosing the donor (the inline "accept current SDK") picks up the published opt-in.
        await reopened.SetHostOutputAsync(MountKind, Patriot, "primary", Talon);
        Assert.Equal(["allow_unverified_effect"], reopened.Project.AttackOutputChanges![0].Acknowledgements);
        Assert.Null(reopened.BuildError); Assert.Contains("allow_unverified_effect=true", reopened.LuaPreview);
    }

    // Opens a saved project against a copy of the development SDK with some files replaced (the next Runtime snapshot).
    public static async Task<BuilderWorkspace> Reopen(TestEnvironment e, Guid project, params (string File, byte[] Bytes)[] files)
    {
        var next = Path.Combine(e.Paths.Root, "dev-sdk-" + Guid.NewGuid().ToString("N")[..8]); Directory.CreateDirectory(next);
        foreach (var file in Directory.GetFiles(EnemyAuthoringTests.DevSdk(e))) File.Copy(file, Path.Combine(next, Path.GetFileName(file)));
        foreach (var (file, bytes) in files) await File.WriteAllBytesAsync(Path.Combine(next, file), bytes);
        var cache = new SdkCache(e.Paths, e.Reader, e.GitHub) { LocalSdkPath = next }; var updates = new SdkUpdateService(cache, e.GitHub, e.Paths);
        var w = new BuilderWorkspace(e.Store, e.Projects, cache, updates, e.Changes, e.Generator, e.Exporter, e.Desktop, e.Desktop, e.Paths);
        await w.OpenAsync(project); return w;
    }
}
