using System.Text.Json;
using System.Text.Json.Nodes;
using HD2RuntimeGUI.Core.Generation;
using HD2RuntimeGUI.Core.GitHub;
using HD2RuntimeGUI.Core.Metadata;
using HD2RuntimeGUI.Core.Models;
using Xunit;

namespace HD2RuntimeGUI.Tests;

// The HD2Runtime 0.30.0 SDK (fixture sdk-0.30.0.zip, the release candidate at Runtime 893d31f; docs/runtime030-integration-plan.md, Phases 1,
// 2 and 10): it loads fully, the 0.30.0-dev test SDK earlier 1.7.0 builds bundled (fixture sdk-0.30.0-dev.zip) still loads but is never
// downloaded, what 0.30 adds is modelled and either authored with Runtime's own rules or shown read-only with the not-authored reason, and
// exports declare the Runtime their fields need.
public sealed class Runtime030Tests
{
    public const string Version = "0.30.0", Fixture = "sdk-0.30.0.zip", DevVersion = "0.30.0-dev", DevFixture = "sdk-0.30.0-dev.zip";
    public static string FixturePath => Path.Combine(AppContext.BaseDirectory, "Fixtures", Fixture);
    private static async Task<SdkMetadata> Local(TestEnvironment e) => await new SdkCache(e.Paths, e.Reader, e.GitHub) { LocalSdkPath = FixturePath }.GetCurrentAsync();

    [Fact] public async Task The_0_30_0_sdk_loads_fully_as_a_local_sdk_and_as_the_bundled_pin()
    {
        using var e = new TestEnvironment(); var sdk = await Local(e);
        Assert.Equal(Version, sdk.Version); Assert.Null(SemVersion.Parse(sdk.Version).Prerelease);
        Assert.True(sdk.IsPinnedBuild); Assert.Equal(SdkPin.ContentFingerprint, sdk.ContentFingerprint);
        Assert.NotNull(sdk.Stratagems); Assert.NotNull(sdk.SupportAuthoring); Assert.NotNull(sdk.Entities!.Enemies); Assert.NotNull(sdk.Entities.VehicleWeapons);
        Assert.NotNull(sdk.Assets); Assert.NotNull(sdk.AttackOutputs); Assert.NotNull(sdk.Events); Assert.NotNull(sdk.LiveEvidence); Assert.NotNull(sdk.LuaApi);
        Assert.Equal(2406, sdk.Stratagems!.FieldInstances.Length);
        var bundled = await SdkFixtures.Install(e, Version);
        Assert.Equal(sdk.ContentFingerprint, bundled.ContentFingerprint); Assert.True(bundled.IsPinnedBuild);
        // The 0.28.1 SDK still loads unchanged next to it, and so does the 0.30.0-dev test SDK (local or cached), as a build other than the pin.
        Assert.Equal("0.28.1", (await SdkFixtures.Install(e, "0.28.1")).Version);
        var dev = await new SdkCache(e.Paths, e.Reader, e.GitHub) { LocalSdkPath = Path.Combine(AppContext.BaseDirectory, "Fixtures", DevFixture) }.GetCurrentAsync();
        Assert.Equal(DevVersion, dev.Version); Assert.Null(dev.IsPinnedBuild); Assert.NotNull(dev.Stratagems); Assert.NotNull(dev.LuaApi);
        var cached = await SdkFixtures.Install(e, DevVersion);
        Assert.Equal((DevVersion, dev.ContentFingerprint), (cached.Version, cached.ContentFingerprint)); Assert.NotEqual(SdkPin.ContentFingerprint, cached.ContentFingerprint);
    }

    [Fact] public void The_release_is_a_download_and_the_development_prerelease_never_is()
    {
        var json = JsonNode.Parse(SdkCache.BundledMetadata())!;
        Assert.Equal(Version, new MetadataReader().Read(SdkCache.BundledMetadata()).Version);
        json["runtime_version"] = DevVersion;
        Assert.Equal(DevVersion, new MetadataReader().Read(JsonSerializer.SerializeToUtf8Bytes(json)).Version);
        json["runtime_version"] = "0.30.0-DEV.01";
        Assert.Throws<InvalidDataException>(() => new MetadataReader().Read(JsonSerializer.SerializeToUtf8Bytes(json)));
        Assert.Throws<InvalidDataException>(() => GitHubReleaseClient.Validate(FakeGitHub.MakeRelease(DevVersion, [1])));
        GitHubReleaseClient.Validate(FakeGitHub.MakeRelease(Version, [1]));
        var releases = """
            [{"draft":false,"prerelease":false,"tag_name":"v0.30.0-dev","html_url":"https://github.com/SkyeShade/HD2Runtime/releases/tag/v0.30.0-dev","assets":[]}]
            """u8.ToArray();
        Assert.Empty(GitHubReleaseClient.ParseReleases(releases));
        Assert.True(SdkCompatibility.IsSupported(Version)); Assert.True(SdkCompatibility.IsSupported(DevVersion)); Assert.True(SdkCompatibility.IsSupported("0.28.1"));
        Assert.False(SdkCompatibility.IsSupported("0.30.1"));
    }

    [Fact] public async Task New_value_types_and_targets_are_modelled_and_read_only_until_they_are_authored()
    {
        using var e = new TestEnvironment(); var sdk = await Local(e); var strategems = sdk.Stratagems!;
        // Call-in code and presentation: typed, with their published rules, authored since Phase 4 (Runtime030StratagemTests).
        var code = strategems.FieldInstances.First(f => f.Type == StratagemField.CalldownCode);
        Assert.Equal((1, 9), (code.MinLength, code.MaxLength)); Assert.Equal(["up", "right", "down", "left"], code.Directions!);
        Assert.True(code.Editable); Assert.NotEqual(AuthoredTypes.NotAuthoredReason, code.Reason); Assert.NotNull(code.AcknowledgementRule);
        var icon = strategems.FieldInstances.First(f => f.SemanticFieldId == "stratagem.presentation.icon");
        Assert.Equal(("image", "image"), (icon.ResourceType, icon.CustomValues)); Assert.True(icon.Editable);
        Assert.Equal(94, strategems.PresentationSources!.Stratagems.Length);
        Assert.False(strategems.PresentationSources.Offers("stratagem.presentation.name_cased", "LIFT-860 Hover Pack"));
        Assert.Equal(8, strategems.EagleAirstrikePatterns!.Values.Length);
        var gas = strategems.Root("Eagle Gas Airstrike")!;
        Assert.Equal(("airstrike", true), (gas.EagleAttack!.Payload, gas.EagleAttack.AllowSharedRequired));
        Assert.Contains("eagle.airstrike_pattern", gas.EagleAttack.WritableFields.Keys);
        var mortar = strategems.Root("A/M-12 Mortar Sentry")!.SentryFields!;
        Assert.Equal("targeting.proximity_range", Assert.Single(mortar.Deferred).Field);
        Assert.Contains(strategems.Stratagems, s => s.BarrageScheduling is { Writable: true, ShellTypes.Length: > 0 });
        // Plain numbers with a published range and opt-in are authored through the stratagem editor (call-in time, orbital pattern, sentries).
        var callIn = strategems.FieldInstances.First(f => f.SemanticFieldId == "stratagem.call_in_time");
        Assert.True(callIn.Editable); Assert.Equal((0d, 60d), (callIn.Min, callIn.Max)); Assert.Equal("allow_unverified_effect", callIn.Acknowledgement);
        var side = strategems.FieldInstances.First(f => f.SemanticFieldId == "targeting.side_range");
        Assert.Equal(-1, Assert.Single(side.SentinelValues!).Value.GetInt32());
        // Support: the overcharge explosion reference and the new charge roles are authored since 1.7.0 (RuntimeChargeTests); charge ranges
        // come from the charge object.
        var support = sdk.SupportAuthoring!.FieldInstances;
        var overcharge = support.First(f => f.IsOverchargeExplosion);
        Assert.True(overcharge.Writable); Assert.Equal(["PLAS-45 Epoch", "RS-422 Railgun"], overcharge.Charge!.AllowedValues!);
        Assert.All(support.Where(f => f.Target.AttackRole is "full_charge" or "full_charge_impact" or "overcharge_explosion"), f => Assert.True(f.Writable));
        var level = support.First(f => f.SemanticFieldId == "charge.level_1" && f.Charge?.Max != null);
        Assert.Equal(("seconds", 0d, 60d), (level.Value.Unit, level.Charge!.Min, level.Charge.Max)); Assert.True(level.Writable);
        Assert.Contains(support, f => f.Charge?.Notices?.Any(n => n.Kind == "dormant") == true);
        Assert.Contains(support, f => f.ChargeLevel is { Level: "partial" });
        // Vehicles: tuning keys and mounted turret motion are listed and (1.7.0, Phase 5) authored; the Hover Pack's launch thrust carries its
        // dormant notice (Runtime030VehicleTests).
        var entities = sdk.Entities!;
        var steering = entities.Vehicles.FieldInstances.First(f => f.SemanticFieldId == "vehicle.steering_response_speed");
        Assert.True(steering.Editable); Assert.Equal("native_consumer_proven", steering.Evidence.Tier);
        Assert.Contains(entities.Vehicles.Vehicles, v => v.TuningFieldInstanceKeys?.Contains(steering.InstanceKey) == true);
        var turret = entities.VehicleWeapons!.FieldInstances.First(f => f.SemanticFieldId == "turret.yaw_speed");
        Assert.True(turret.Editable); Assert.Equal((1d, 720d), (turret.Min, turret.Max));
        var thrust = entities.Backpacks.FieldInstances.First(f => f.Target.Backpack == "LIFT-860 Hover Pack" && f.SemanticFieldId == "jump.vertical_launch_velocity");
        Assert.Equal(EntityNotice.Dormant, Assert.Single(thrust.Notices!).Kind);
        // Projectile donors: offered since Phase 7 (Runtime030DonorTests), none listed as not authored; owners keep their own row.
        var outputs = sdk.AttackOutputs!;
        Assert.Equal(32, outputs.Outputs.Count(o => o.IsDonor));
        Assert.All(outputs.Outputs.Where(o => o.IsDonor), o => Assert.True(o.Slots == null && o.Flight != null && o.Names!.Length > 0 && o.Offered));
        Assert.DoesNotContain(outputs.Unavailable(), u => u.Code == "NOT_AUTHORED");
        // An owner with its own row and donor rows keeps its own row; an owner with donor rows only (SMG-37, GP-31) has no own row to swap from.
        Assert.False(outputs.Own("P-92 Warrant")!.IsDonor); Assert.False(outputs.OwnedBy("AC-8 Autocannon")!.IsDonor);
        Assert.Null(outputs.Own("SMG-37 Defender")); Assert.Null(outputs.OwnedBy("GP-31 Grenade Pistol"));
        Assert.Equal(2, sdk.Assets!.Summary.ObjectivePackage);
        Assert.Contains(sdk.Assets.Objects.Values, o => o.Kind == "projectile_donor");
    }

    [Fact] public async Task The_gib_threshold_follows_its_value_rule_and_charge_fields_their_range()
    {
        using var e = new TestEnvironment(); var sdk = await Local(e);
        var gib = sdk.Entities!.Enemies!.FieldInstances.First(f => f.SemanticFieldId == "gore.whole_body_gib_damage");
        Assert.True(gib.Editable); Assert.Equal("GoreComponent", gib.BackingObjectKind); Assert.Equal("native_code_proven", gib.Evidence.Tier);
        Assert.Equal(-1, gib.EffectiveRange!.Disabled);
        static JsonElement N(double v) => JsonSerializer.SerializeToElement(v);
        EntityScalar.CheckRange(gib, N(-1)); EntityScalar.CheckRange(gib, N(0.5)); EntityScalar.CheckRange(gib, N(100000));
        Assert.Contains("0 < value", Assert.Throws<InvalidDataException>(() => EntityScalar.CheckRange(gib, N(0))).Message);
        Assert.Throws<InvalidDataException>(() => EntityScalar.CheckRange(gib, N(-0.5)));
        Assert.Throws<InvalidDataException>(() => EntityScalar.CheckRange(gib, N(100001)));
        var level = sdk.SupportAuthoring!.FieldInstances.First(f => f.SemanticFieldId == "charge.level_1" && f.Charge?.Max != null);
        var service = new SupportChangeService();
        Assert.Equal("30", service.Create(sdk, level.InstanceKey, "30").DesiredValue.GetRawText());
        Assert.Contains(level.Charge!.RangeReason!, Assert.Throws<InvalidDataException>(() => service.Create(sdk, level.InstanceKey, "61")).Message);
    }

    [Fact] public void The_required_runtime_mirrors_the_runtime_validator()
    {
        var sdk = new MetadataReader().Read(SdkCache.BundledMetadata()) with { Version = "0.28.1" };
        var project = new ModProject { SdkVersion = "0.28.1" };
        Assert.Equal("0.28.1", RuntimeRequirement.Minimum(project, sdk));
        project.StratagemChanges.Add(new StratagemChange { SemanticFieldId = "stratagem.cooldown" });
        Assert.Equal("0.28.1", RuntimeRequirement.Minimum(project, sdk));
        project.StratagemChanges.Add(new StratagemChange { SemanticFieldId = "weapon.horizontal_spread" });
        Assert.Equal(Version, RuntimeRequirement.Minimum(project, sdk));
        Assert.Equal((Version, "stratagem weapon.horizontal_spread"), Assert.Single(RuntimeRequirement.Floors(project)));
        project.StratagemChanges.Clear();
        project.SupportChanges.Add(new SupportChange { SemanticFieldId = "damage.standard_damage", AttackRole = "overcharge_explosion" });
        Assert.Equal(Version, RuntimeRequirement.Minimum(project, sdk));
        project.SupportChanges[0] = project.SupportChanges[0] with { Enabled = false };
        Assert.Equal("0.28.1", RuntimeRequirement.Minimum(project, sdk));
        project.EntityChanges.Add(new EntityChange { Resource = "enemy", SemanticFieldId = "gore.whole_body_gib_damage" });
        project.WeaponChanges.Add(new WeaponChange { SemanticFieldId = "projectile.primary.velocity" });
        Assert.Equal(["enemy gore.whole_body_gib_damage"], RuntimeRequirement.Floors(project).Select(f => f.Reason));
        Assert.Equal("projectile.velocity", RuntimeRequirement.Generic("projectile.primary.velocity"));
        // The wrapper: the plain one for an older binding that needs nothing newer; the SemVer one otherwise, still declaring the bound SDK.
        var older = new ModProject { SdkVersion = "0.28.1", ResourceId = "mods/tests/wrap" };
        Assert.Equal(ModExporter.Wrap(older, "return 1"), ModExporter.WrapFor(older, "return 1", "0.28.1"));
        var raised = ModExporter.WrapFor(older, "return 1", Version);
        Assert.Contains("local x,y,z=version('0.28.1')", raised); Assert.Contains("local required_runtime='0.30.0'", raised);
        var bound = new ModProject { SdkVersion = Version, ResourceId = "mods/tests/wrap" };
        Assert.Equal(ModExporter.Wrap(bound, "return 1", Version), ModExporter.WrapFor(bound, "return 1", Version));
        // A project still bound to the 0.30.0-dev test SDK: the SemVer wrapper (the plain one refuses a prerelease), its own SDK declared, and
        // 0.30.0 required as soon as it writes a 0.30 field (the released validator names 0.30.0; the released Runtime loads 0.30.0-dev mods).
        var devSdk = sdk with { Version = DevVersion };
        var dev = new ModProject { SdkVersion = DevVersion, ResourceId = "mods/tests/wrap" };
        Assert.Equal(DevVersion, RuntimeRequirement.Minimum(dev, devSdk));
        Assert.Equal(ModExporter.Wrap(dev, "return 1", DevVersion), ModExporter.WrapFor(dev, "return 1", DevVersion));
        dev.StratagemChanges.Add(new StratagemChange { SemanticFieldId = "weapon.horizontal_spread" });
        Assert.Equal(Version, RuntimeRequirement.Minimum(dev, devSdk));
        var devRaised = ModExporter.WrapFor(dev, "return 1", Version);
        Assert.Contains("local x,y,z=version('0.30.0-dev')", devRaised); Assert.Contains("local required_runtime='0.30.0'", devRaised);
    }
}
