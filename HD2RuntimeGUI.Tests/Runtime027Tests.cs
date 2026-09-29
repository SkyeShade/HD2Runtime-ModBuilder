using System.Text;
using System.Text.Json.Nodes;
using HD2RuntimeGUI.Core.Generation;
using HD2RuntimeGUI.Core.Metadata;
using HD2RuntimeGUI.Core.Models;
using HD2RuntimeGUI.Core.Services;
using Xunit;

namespace HD2RuntimeGUI.Tests;

// HD2Runtime 0.27.0 (published HD2Runtime-0.27.0-sdk.zip): automatic asset loading (AssetDependencyCapabilities.json), the per-family
// residency proof, the live-verified cases, and the explicit SDK compatibility pin of this ModBuilder release.
public sealed class Runtime027Tests
{
    private const string StalwartPod = "M-105 Stalwart pod", Mg43Pod = "MG-43 Machine Gun pod", Frv = "M-102 Gunner FRV", Reprimand = "SMG-32 Reprimand", Talon = "LAS-58 Talon";
    private const string Eat700 = "pickup/v1/eat-700-expendable-napalm/bff4b15f31d35c94", GrenadeBox = "pickup/v1/grenade-box/5ad3b36a5d3adbb7",
        AmmoBoxPod = "pickup/v1/ammo-box-pod/096e2d589f69f1ae", SupplyBox = "pickup/v1/supply-box/d0e8fed8c01ceb1f";
    private const string FrvGun = "mounted-weapon/v1/frv-mg/87956cec45a21b90", SupplyFrvGun = "mounted-weapon/v1/m-103-supply-frv-gun-weapon/34d73fd6e0ab1d96",
        SoldierMg = "mounted-weapon/v1/soldier-machinegun/97d452fb6ecd1f68";
    private static async Task<BuilderWorkspace> Fresh(TestEnvironment e, string version = "0.27.0", string resource = "mods/tests/runtime027")
    {
        var sdk = await SdkFixtures.Install(e, version); var w = e.Workspace();
        await w.CreateAsync(new("Runtime 0.27", "Tests", resource, "0.1.0"), sdk); return w;
    }
    private static JsonNode Assets() => JsonNode.Parse(SdkFixtures.Entry("0.27.0", AssetDependencyReader.FileName))!;
    private static AssetDependencyCatalog Read(JsonNode j) => AssetDependencyReader.Read(Encoding.UTF8.GetBytes(j.ToJsonString()));
    private static PodRack Rack(BuilderWorkspace w, string name) => w.Metadata!.Entities!.Pods!.Rack(name)!;
    private static EntityField MountField(BuilderWorkspace w) =>
        w.Metadata!.Entities!.Vehicles.FieldInstances.Single(f => f.Target.Vehicle == Frv && f.IsReference && f.Target.Mount == "slot_0");

    // ---- SDK and asset dependency catalog ----
    [Fact] public async Task Sdk_0270_binds_with_its_asset_dependency_catalog()
    {
        using var e = new TestEnvironment(); var sdk = await SdkFixtures.Install(e, "0.27.0");
        Assert.Equal("0.27.0", sdk.Version); Assert.True(sdk.Has027); var a = sdk.Assets!;
        Assert.Equal(331, a.Objects.Count); Assert.Equal(260, a.Summary.Known); Assert.Equal(71, a.Summary.Unknown);
        Assert.Equal(["pod_payload_pickup", "projectile_reference"], a.Families.Where(f => f.Value.LiveProven).Select(f => f.Key).Order(StringComparer.Ordinal));
        Assert.Equal(AssetDependencyReader.OfflineProven, a.Family(AssetDependencyReader.MountFamily)!.PackageResidency);
        Assert.Equal(90, a.Policy.LoadTimeoutSeconds); Assert.Equal(64, a.Policy.MaxHeldPackages);
        // Live evidence: three passes with nobody carrying the donor item; the vehicle-mount test is inconclusive, not failed.
        Assert.Equal(["A", "B", "D"], a.LiveEvidence.Tests.Where(t => t.Result == "PASS").Select(t => t.Id).Order(StringComparer.Ordinal));
        Assert.All(a.LiveEvidence.Tests, t => Assert.False(t.DonorCarried));
        Assert.Equal("INCONCLUSIVE", a.LiveEvidence.Tests.Single(t => t.Family == AssetDependencyReader.MountFamily).Result);
        var pods = sdk.Entities!.Pods!;
        Assert.Equal(AssetDependencyReader.LiveProven, pods.PackageResidency!.PackageResidency); Assert.Equal(2, pods.LiveVerifiedPairs.Count);
        // Older SDKs have no asset catalog and keep their previous behaviour.
        Assert.Null((await SdkFixtures.Install(e, "0.26.0")).Assets);
    }

    [Fact] public void Asset_dependencies_parse_known_unknown_and_live_tested_objects()
    {
        var a = Read(Assets());
        var eat = a.Pickup(Eat700)!.PackageDependency;
        Assert.True(eat.Known && eat.AutoLoadSupported && eat.LiveTested && eat.PackageLiveLoaded); Assert.Equal("expendable_napalm_launcher", eat.Package);
        var ammo = a.Pickup(AmmoBoxPod)!.PackageDependency;
        Assert.False(ammo.Known || ammo.AutoLoadSupported); Assert.Null(ammo.Package); Assert.False(string.IsNullOrWhiteSpace(ammo.Blocker));
        var soldier = a.MountedWeapon(SoldierMg)!.PackageDependency; Assert.False(soldier.Known);
        var supplyGun = a.MountedWeapon(SupplyFrvGun)!.PackageDependency; Assert.True(supplyGun.AutoLoadSupported); Assert.Equal("ammo_rack_mounted_turret", supplyGun.Package);
    }

    [Theory]
    [InlineData("auto-without-known")] [InlineData("raw-package-id")] [InlineData("unknown-with-package")] [InlineData("live-without-pass")]
    [InlineData("caller-identities")] [InlineData("summary")] [InlineData("key-namespace")]
    public void Inconsistent_asset_metadata_fails_closed(string fault)
    {
        var j = Assets(); var objects = j["objects"]!.AsArray();
        JsonNode Dep(Func<JsonNode, bool> pick) => objects.First(o => pick(o!))!["packageDependency"]!;
        switch (fault)
        {
            case "auto-without-known": var u = Dep(o => !(bool)o["packageDependency"]!["known"]!); u["autoLoadSupported"] = true; break;
            case "raw-package-id": Dep(o => (bool)o["packageDependency"]!["known"]!)["package"] = "package/0123456789abcdef"; break;
            case "unknown-with-package": Dep(o => !(bool)o["packageDependency"]!["known"]!)["package"] = "grenade_box"; break;
            case "live-without-pass": j["referenceFamilies"]!["vehicle_mount"]!["packageResidency"] = "LIVE_PROVEN"; break;
            case "caller-identities": j["safety"]!["callerSuppliedIdentities"] = true; break;
            case "summary": j["summary"]!["known"] = 261; break;
            case "key-namespace": objects.First(o => (string)o!["kind"]! == "mounted_weapon")!["kind"] = "vehicle"; break;
        }
        Assert.Throws<InvalidDataException>(() => Read(j));
    }
    [Fact] public void Unknown_asset_contract_or_object_kind_is_unsupported()
    {
        var j = Assets(); j["contract"] = "hd2runtime.asset_dependencies.v2"; Assert.Throws<UnsupportedSdkException>(() => Read(j));
        j = Assets(); j["objects"]!.AsArray()[0]!["kind"] = "emote"; Assert.Throws<UnsupportedSdkException>(() => Read(j));
    }

    // ---- Drop-pod payloads ----
    [Fact] public async Task Pod_pickups_show_asset_state_separately_from_slot_compatibility()
    {
        using var e = new TestEnvironment(); var w = await Fresh(e); var sdk = w.Metadata!; var pods = sdk.Entities!.Pods!;
        var rack = Rack(w, StalwartPod); var slot2 = rack.Slots[1];
        // Vanilla occupant: nothing to show.
        Assert.Equal(AssetState.None, AssetStatus.Pickup(sdk, slot2, pods.Pickup(slot2.Current!.Pickup!)!)!.State);
        // Live-tested replacement: assets live-verified; this rack/slot/pickup is also a published live-verified pair.
        var eat = AssetStatus.Pickup(sdk, slot2, pods.Pickup(Eat700)!)!;
        Assert.Equal(AssetState.LiveVerified, eat.State); Assert.Equal("Assets live-verified", eat.Label); Assert.Equal("expendable_napalm_launcher", eat.Package);
        Assert.True(pods.LiveVerifiedPair(rack, slot2, pods.Pickup(Eat700)!)); Assert.False(pods.LiveVerifiedPair(rack, rack.Slots[0], pods.Pickup(Eat700)!));
        Assert.True(pods.LiveVerifiedPair(Rack(w, Mg43Pod), Rack(w, Mg43Pod).Slots[0], pods.Pickup(GrenadeBox)!));
        // Package loading is Runtime's job now: no package risk for a known dependency, and none for an always-resident pickup.
        Assert.True(PodPayloadCatalog.LowPackageRisk(rack, slot2, pods.Pickup(Eat700)!));
        Assert.Equal(AssetState.AlwaysResident, AssetStatus.Pickup(sdk, slot2, pods.Pickup(SupplyBox)!)!.State);
        // Unknown dependency: an explicit warning with Runtime's blocker; package risk stays.
        var ammo = AssetStatus.Pickup(sdk, slot2, pods.Pickup(AmmoBoxPod)!)!;
        Assert.Equal(AssetState.Unknown, ammo.State); Assert.True(ammo.Warning); Assert.Contains("missing asset", ammo.Description);
        Assert.Contains(ammo.Blocker!, ammo.Description); Assert.False(PodPayloadCatalog.LowPackageRisk(rack, slot2, pods.Pickup(AmmoBoxPod)!));
        // Slot compatibility is unchanged: every slot still carries allow_unverified_reference.
        Assert.All(pods.FieldInstances.Where(f => f.IsPickup && f.Editable), f => Assert.Equal("allow_unverified_reference", f.Acknowledgement));
        // Older SDKs keep the previous package-risk presentation.
        using var old = new TestEnvironment(); var w26 = await Fresh(old, "0.26.0");
        Assert.Null(AssetStatus.Pickup(w26.Metadata!, w26.Metadata!.Entities!.Pods!.Rack(StalwartPod)!.Slots[1], w26.Metadata.Entities.Pods.Pickup(Eat700)!));
    }

    [Fact] public async Task Pod_swaps_generate_the_same_typed_lua_and_keep_the_reference_flag()
    {
        using var e = new TestEnvironment(); var w = await Fresh(e); var rack = Rack(w, StalwartPod);
        await w.SetEntityAsync(PodPayloadReader.SlotKey(rack, 2), Eat700);
        await w.SetEntityAsync(PodPayloadReader.SlotKey(rack, 1), AmmoBoxPod);
        Assert.Null(w.BuildError); var lua = w.LuaPreview;
        Assert.Contains("hd2.pod_rack('M-105 Stalwart pod'):slot(2)", lua); Assert.Contains($"value=hd2.pickup('{Eat700}')", lua);
        Assert.Equal(2, lua.Split("allow_unverified_reference=true").Length - 1);
        // Runtime infers and loads dependencies itself: no preload requests or package identities in generated mods.
        Assert.DoesNotContain("require_assets", lua); Assert.DoesNotContain("package", lua);
    }

    // ---- Vehicle mounts ----
    [Fact] public async Task Mount_swaps_show_asset_state_and_keep_allow_unverified_reference()
    {
        using var e = new TestEnvironment(); var w = await Fresh(e); var sdk = w.Metadata!; var f = MountField(w);
        Assert.Equal(FrvGun, f.CurrentDefault.GetString()); Assert.Equal("allow_unverified_reference", f.Acknowledgement);
        Assert.Equal(AssetState.None, AssetStatus.MountedWeapon(sdk, FrvGun, FrvGun)!.State);
        var known = AssetStatus.MountedWeapon(sdk, FrvGun, SupplyFrvGun)!;
        Assert.Equal(AssetState.AutoLoaded, known.State); Assert.Equal("ammo_rack_mounted_turret", known.Package);
        // The loader is proven offline for mounts only; the inconclusive live test is not presented as a failure.
        Assert.False(known.FamilyProof!.LiveProven); Assert.Contains("proven offline", known.FamilyProofText); Assert.DoesNotContain("fail", known.FamilyProofText, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(AssetState.Unknown, AssetStatus.MountedWeapon(sdk, FrvGun, SoldierMg)!.State);
        await w.SetEntityAsync(f.InstanceKey, SupplyFrvGun); Assert.Null(w.BuildError);
        var lua = w.LuaPreview; Assert.Contains("allow_unverified_reference=true", lua); Assert.DoesNotContain("require_assets", lua);
    }

    // ---- Projectile and explosion references ----
    [Fact] public async Task Talon_becomes_a_supported_projectile_source_with_live_verified_assets()
    {
        using var e = new TestEnvironment(); var w = await Fresh(e); var sdk = w.Metadata!;
        Assert.Contains(new ProjectileReference(Talon, "primary"), w.ProjectileSources(Reprimand, "primary"));
        var talon = AssetStatus.ProjectileSource(sdk, Talon, "primary", baseline: false)!;
        Assert.Equal(AssetState.LiveVerified, talon.State); Assert.Equal("laser_pistol", talon.Package); Assert.True(talon.FamilyProof!.LiveProven);
        Assert.Equal(AssetState.None, AssetStatus.ProjectileSource(sdk, Reprimand, "primary", baseline: true)!.State);
        await w.SetProjectileAsync(Reprimand, "primary", new(Talon, "primary")); Assert.Null(w.BuildError);
        var lua = w.LuaPreview; Assert.Contains(Talon, lua); Assert.DoesNotContain("require_assets", lua);
        // Before 0.27.0 the Talon was rejected as a source because its assets needed the source weapon equipped.
        using var old = new TestEnvironment(); var w26 = await Fresh(old, "0.26.0");
        Assert.DoesNotContain(new ProjectileReference(Talon, "primary"), w26.ProjectileSources(Reprimand, "primary"));
    }
    [Fact] public async Task A_source_with_an_unresolved_package_reads_as_assets_unknown()
    {
        using var e = new TestEnvironment(); var w = await Fresh(e); var sdk = w.Metadata!;
        var gp31 = AssetStatus.ProjectileSource(sdk, "GP-31 Grenade Pistol", "primary", baseline: false)!;
        Assert.Equal(AssetState.Unknown, gp31.State); Assert.Contains("cannot name the package", gp31.Description);
        var explosion = AssetStatus.ProjectileSource(sdk, Talon, "primary", baseline: false, explosion: true)!;
        Assert.Equal(AssetDependencyReader.ExplosionFamily, explosion.Family); Assert.False(explosion.FamilyProof!.LiveProven);
    }

    // ---- Waiting for assets / ASSET_UNAVAILABLE (ModBuilder has no live game view; the editors explain Runtime's behaviour) ----
    [Fact] public async Task Asset_states_explain_waiting_and_asset_unavailable_from_the_published_policy()
    {
        using var e = new TestEnvironment(); var w = await Fresh(e); var sdk = w.Metadata!; var pods = sdk.Entities!.Pods!;
        var auto = AssetStatus.MountedWeapon(sdk, FrvGun, SupplyFrvGun)!;
        Assert.Contains("waiting_for_assets", auto.InGame); Assert.Contains("90 s", auto.InGame); Assert.Contains("ASSET_UNAVAILABLE", auto.InGame);
        Assert.Contains("original reference stays", auto.InGame);
        // No wait and no rejection text for states Runtime does not load.
        Assert.Null(AssetStatus.Pickup(sdk, Rack(w, StalwartPod).Slots[1], pods.Pickup(AmmoBoxPod)!)!.InGame);
        Assert.Null(AssetStatus.Pickup(sdk, Rack(w, StalwartPod).Slots[1], pods.Pickup(SupplyBox)!)!.InGame);
    }

    // ---- Existing projects ----
    [Fact] public async Task A_0260_project_rebinds_to_0270_without_new_reviews_and_with_identical_lua()
    {
        using var e = new TestEnvironment(); var old = await SdkFixtures.Install(e, "0.26.0"); var w = e.Workspace();
        await w.CreateAsync(new("Rebind 027", "Tests", "mods/tests/rebind027", "0.1.0"), old);
        var rack = old.Entities!.Pods!.Rack(StalwartPod)!;
        await w.SetEntityAsync(PodPayloadReader.SlotKey(rack, 2), Eat700);
        await w.SetEntityAsync(PodPayloadReader.SpawnKey(rack), "3");
        await w.SetEntityAsync(MountField(w).InstanceKey, SupplyFrvGun);
        await w.SetProjectileAsync("P-113 Verdict", "primary", new("JAR-5 Dominator", "primary"));
        await w.SetObjectScalarAsync("AR-23C Liberator Concussive", "primary", "projectile", null, "projectile.velocity", "350", true);
        await w.SetTerminalAsync("AR-23C Liberator Concussive", "primary", "impact", w.ExplosionSources.First(s => s.Projectile?.Weapon == "R-36 Eruptor"), true);
        await w.SetWeaponChangeAsync("AR-23 Liberator", "weapon.fire_rate", "700", false);
        Assert.Null(w.BuildError); var lua = w.LuaPreview; var format = w.Project!.FormatVersion;
        var entityEvidence = w.Project.EntityChanges.Select(c => c.CapabilityEvidence).ToArray();
        var projectileEvidence = w.Project.ProjectileChanges.Select(c => c.ReplacementEvidence).ToArray();
        await SdkFixtures.Install(e, "0.27.0"); await w.OpenAsync(w.Project.Id);
        Assert.Equal("0.26.0", w.Project!.SdkVersion); Assert.Equal(lua, w.LuaPreview); // unchanged until an explicit rebind
        await w.RebindToInstalledSdkAsync();
        Assert.Equal("0.27.0", w.Project.SdkVersion); Assert.Null(w.BuildError); Assert.Equal(format, w.Project.FormatVersion);
        Assert.All(w.Project.EntityChanges, c => Assert.Null(w.EntityIssue(c)));
        Assert.All(w.Project.ProjectileChanges, c => Assert.Null(w.ProjectileIssue(c)));
        Assert.All(w.Project.CompositionChanges, c => Assert.Null(w.CompositionIssue(c)));
        Assert.Equal(lua.Replace("0.26.0", "0.27.0"), w.LuaPreview);
        // The pod/mount residency prose and the projectile residency class did change; the evidence was carried over, not ignored.
        Assert.NotEqual(entityEvidence, w.Project.EntityChanges.Select(c => c.CapabilityEvidence).ToArray());
        Assert.NotEqual(projectileEvidence, w.Project.ProjectileChanges.Select(c => c.ReplacementEvidence).ToArray());
        // Reopening keeps the refreshed evidence.
        await w.OpenAsync(w.Project.Id); Assert.Null(w.BuildError);
    }

    // ---- SDK compatibility pin ----
    [Fact] public async Task A_newer_sdk_release_is_reported_but_never_downloaded_or_offered()
    {
        using var e = new TestEnvironment(); await SdkFixtures.Install(e, "0.26.0");
        var r28 = FakeGitHub.MakeRelease("0.28.0", Encoding.UTF8.GetBytes("future"));
        var r27 = FakeGitHub.MakeRelease("0.27.0", SdkFixtures.Archive("0.27.0"));
        e.GitHub.Candidates = [r28, r27]; e.GitHub.CandidateArchives["0.27.0"] = SdkFixtures.Archive("0.27.0");
        var w = e.Workspace(); await w.CheckUpdatesAsync(); var status = w.SdkStatus!;
        Assert.Equal("0.27.0", status.Latest!.Version); Assert.True(status.UpdateAvailable); Assert.Equal("0.28.0", status.NewerUnsupported);
        Assert.Contains("needs a newer HD2Runtime ModBuilder", status.Message); Assert.DoesNotContain("0.28.0", e.GitHub.Downloads);
        Assert.Equal(SdkCompatibility.NewestSupportedVersion, "0.27.0"); Assert.False(SdkCompatibility.IsSupported("0.28.0")); Assert.True(SdkCompatibility.IsSupported("0.5.1"));
    }
    [Fact] public async Task A_release_this_version_cannot_read_is_skipped_instead_of_breaking_the_check()
    {
        using var e = new TestEnvironment(); await SdkFixtures.Install(e, "0.26.0");
        // A same-version-range release whose metadata the strict readers reject must not make the installed SDK unavailable.
        var broken = SdkFixtures.Archive("0.26.0"); var r27 = FakeGitHub.MakeRelease("0.27.0", broken);
        e.GitHub.Candidates = [r27, FakeGitHub.MakeRelease("0.26.0", broken)]; e.GitHub.CandidateArchives["0.27.0"] = broken;
        var w = e.Workspace(); await w.CheckUpdatesAsync();
        Assert.Null(w.SdkError); Assert.Equal("0.26.0", w.SdkStatus!.Installed.Version); Assert.Equal("0.26.0", w.SdkStatus.Latest!.Version);
        Assert.Contains("Skipped 1 incompatible release", w.SdkStatus.Message);
    }
}
