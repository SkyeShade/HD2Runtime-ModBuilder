using System.IO.Compression;
using System.Text.Json.Nodes;
using HD2RuntimeGUI.Core.Audit;
using HD2RuntimeGUI.Core.Generation;
using HD2RuntimeGUI.Core.Metadata;
using HD2RuntimeGUI.Core.Services;
using Xunit;

namespace HD2RuntimeGUI.Tests;

// HD2Runtime 0.30.0 vehicles, backpacks and enemies (docs/runtime030-integration-plan.md, Phases 3.2 and 5), on the bundled 0.30.0 SDK:
// vehicle motion tuning, mounted turret motion with its ordered limit pairs, Jump / Hover Pack movement (the launch thrust relabel and the
// Hover Pack's dormant notice) and the enemy gib threshold with its disabled value. Every write carries the opt-in Runtime publishes (never a
// manual acknowledgement), raises requires.hd2runtime.min_version to 0.30.0 and survives save / reopen.
public sealed class Runtime030VehicleTests
{
    private const string Patriot = "EXO-45 Patriot Exosuit", Gunner = "M-102 Gunner FRV", Cannon = "TD-220 Bastion MK XVI / attach_tank_gun",
        SupplyGun = "M-103 Supply FRV / gun", JumpPack = "LIFT-850 Jump Pack", HoverPack = "LIFT-860 Hover Pack", Gib = "gore.whole_body_gib_damage";
    private const string CannonTarget = "hd2.vehicle('TD-220 Bastion MK XVI'):weapon('attach_tank_gun')";

    private static async Task<BuilderWorkspace> Fresh(TestEnvironment e)
    {
        var cache = new SdkCache(e.Paths, e.Reader, e.GitHub); e.GitHub.Offline = true;
        var w = new BuilderWorkspace(e.Store, e.Projects, cache, new SdkUpdateService(cache, e.GitHub, e.Paths), e.Changes, e.Generator, e.Exporter, e.Desktop, e.Desktop, e.Paths);
        await w.InitializeAsync(); Assert.Equal(Runtime030Tests.Version, w.SdkStatus!.Installed.Version);
        await w.CreateAsync(new("Vehicles 0.30", "Tests", "mods/tests/vehicles_030", "0.1.0"), w.SdkStatus.Installed);
        return w;
    }
    private static EntityAuthoring Entities(BuilderWorkspace w) => w.Metadata!.Entities!;
    private static EntityField Vehicle(BuilderWorkspace w, string vehicle, string field) => Entities(w).Vehicles.FieldInstances.Single(f => f.Target.Vehicle == vehicle && f.SemanticFieldId == field);
    private static EntityField Turret(BuilderWorkspace w, string weapon, string field) => Entities(w).VehicleWeapons!.FieldInstances.Single(f => f.Target.Weapon == weapon && f.SemanticFieldId == field);
    private static EntityField Pack(BuilderWorkspace w, string backpack, string field) => Entities(w).Backpacks.FieldInstances.Single(f => f.Target.Backpack == backpack && f.SemanticFieldId == field);
    private static EntityField Gore(BuilderWorkspace w, Func<EntityField, bool> which) => Entities(w).Enemies!.FieldInstances.Where(f => f.SemanticFieldId == Gib).Single(which);
    // The generated request (one pcall-isolated block) whose target is this Lua accessor, each line trimmed.
    private static string Operation(string lua, string target) => string.Join('\n', System.Text.RegularExpressions.Regex.Split(lua, @"(?=add\(function\(\) return )")
        .Single(b => b.Contains("target=" + target + ",", StringComparison.Ordinal)).Split('\n').Select(l => l.Trim()));
    // Each test's export is also an RC export fixture (V1-V4), validated with HD2Runtime's own validator like F1-F10.
    private static async Task<string> MinVersion(BuilderWorkspace w, string fixture)
    {
        var (manifest, _) = await ExportFixtureTests.Export(w, fixture);
        return manifest.RootElement.GetProperty("requires").GetProperty("hd2runtime").GetProperty("min_version").GetString()!;
    }
    private static string? Issue(BuilderWorkspace w, EntityField f) => w.EntityIssue(EntityChangeService.Saved(w.Project, f)!);

    [Fact] public async Task Vehicle_tuning_is_written_on_the_vehicle_with_its_opt_in_and_survives_reopen()
    {
        using var e = new TestEnvironment(); var w = await Fresh(e);
        var steering = Vehicle(w, Gunner, "vehicle.steering_response_speed");
        Assert.True(steering.Editable); Assert.Equal(("allow_unverified_effect", (double?)0.05, (double?)50), (steering.Acknowledgement, steering.Min, steering.Max));
        Assert.Contains(steering.InstanceKey, Entities(w).Vehicles.Find(Gunner)!.TuningFieldInstanceKeys!);
        // 7 steering vehicles, 4 Exosuits with three rotation fields: all editable now.
        var tuning = Entities(w).Vehicles.Vehicles.SelectMany(v => v.TuningFieldInstanceKeys ?? []).Select(k => Entities(w).Field(k)!).ToArray();
        Assert.Equal((7, 12), (tuning.Count(f => f.SemanticFieldId == "vehicle.steering_response_speed"), tuning.Count(f => f.Domain == "rotation")));
        Assert.All(tuning, f => Assert.True(f.Editable));
        // Published bounds, with Runtime's reason.
        Assert.Contains("0.05", (await Assert.ThrowsAsync<InvalidDataException>(() => w.SetEntityAsync(steering.InstanceKey, "0.04"))).Message);
        await Assert.ThrowsAsync<InvalidDataException>(() => w.SetEntityAsync(Vehicle(w, Patriot, "rotation.turn_speed").InstanceKey, "721"));

        await w.SetEntityAsync(steering.InstanceKey, "0.5");
        await w.SetEntityAsync(Vehicle(w, Patriot, "rotation.turn_speed").InstanceKey, "20");
        await w.SetEntityAsync(Vehicle(w, Patriot, "rotation.acceleration").InstanceKey, "30");
        Assert.Null(w.BuildError); var lua = w.LuaPreview;
        // The forms of examples/projects/VehicleTuningTest: a patch for the FRV, one transaction for the Patriot's rotation record.
        var frv = Operation(lua, "hd2.vehicle('M-102 Gunner FRV')");
        Assert.Contains("hd2.ensure({\npatch={\n", frv);
        Assert.Contains("allow_unverified_effect=true,\nfield=hd2.fields.vehicle.steering_response_speed,\nexpect=3.1,\nvalue=0.5,\n", frv);
        var patriot = Operation(lua, "hd2.vehicle('EXO-45 Patriot Exosuit')");
        Assert.Contains("hd2.ensure({\ntransaction={\n", patriot);
        Assert.Contains("allow_unverified_effect=true,\nchanges={\n{field=hd2.fields.rotation.acceleration,expect=0,value=30},\n{field=hd2.fields.rotation.turn_speed,expect=65,value=20},\n},\n", patriot);
        Assert.Contains("vehicle rotation.turn_speed", RuntimeRequirement.Floors(w.Project!).Select(f => f.Reason));
        Assert.Equal(Runtime030Tests.Version, await MinVersion(w, "V1-vehicle-tuning"));
        await w.OpenAsync(w.Project!.Id); Assert.Null(w.BuildError); Assert.Equal(lua, w.LuaPreview); Assert.Equal(3, w.Project!.EntityChanges.Count);
    }

    [Fact] public async Task Turret_motion_is_one_transaction_per_mount_and_its_limit_pairs_stay_ordered()
    {
        using var e = new TestEnvironment(); var w = await Fresh(e);
        var turret = Entities(w).VehicleWeapons!.FieldInstances.Where(f => f.SemanticFieldId.StartsWith("turret.", StringComparison.Ordinal)).ToArray();
        Assert.Equal(30, turret.Length); Assert.All(turret, f => Assert.True(f.Editable)); Assert.Equal(5, turret.Select(f => f.Target.Weapon).Distinct().Count());
        var yawMin = Turret(w, Cannon, "turret.yaw_min"); var yawMax = Turret(w, Cannon, "turret.yaw_max");
        var pitchMin = Turret(w, Cannon, "turret.pitch_min"); var pitchMax = Turret(w, Cannon, "turret.pitch_max");
        Assert.Equal(["turret.pitch_min", "turret.pitch_max"], pitchMin.Order!);
        var pair = EntityOrder.Of(Entities(w), pitchMax)!;
        Assert.Equal((pitchMin.InstanceKey, pitchMax.InstanceKey), (pair.Low.InstanceKey, pair.High.InstanceKey));
        Assert.Null(EntityOrder.Of(Entities(w), Turret(w, Cannon, "turret.yaw_speed")));
        // Published ranges: speeds 1..720, pitch -90..90.
        await Assert.ThrowsAsync<InvalidDataException>(() => w.SetEntityAsync(Turret(w, SupplyGun, "turret.yaw_speed").InstanceKey, "0"));
        await Assert.ThrowsAsync<InvalidDataException>(() => w.SetEntityAsync(pitchMax.InstanceKey, "91"));

        // examples/projects/VehicleTuningTest: the Bastion cannon's narrow arc and more elevation, in one transaction; the M-103 traverse.
        await w.SetEntityAsync(yawMin.InstanceKey, "-5"); await w.SetEntityAsync(yawMax.InstanceKey, "5"); await w.SetEntityAsync(pitchMax.InstanceKey, "45");
        await w.SetEntityAsync(Turret(w, SupplyGun, "turret.yaw_speed").InstanceKey, "30");
        Assert.Null(w.BuildError); var lua = w.LuaPreview;
        var cannon = Operation(lua, CannonTarget);
        Assert.Contains("hd2.ensure({\ntransaction={\n", cannon); Assert.Contains("\nallow_unverified_effect=true,\n", cannon);
        Assert.Contains("changes={\n{field=hd2.fields.turret.pitch_max,expect=25,value=45},\n{field=hd2.fields.turret.yaw_max,expect=20,value=5},\n{field=hd2.fields.turret.yaw_min,expect=-20,value=-5},\n},", cannon);
        Assert.Contains("allow_unverified_effect=true,\nfield=hd2.fields.turret.yaw_speed,\nexpect=130,\nvalue=30,\n", Operation(lua, "hd2.vehicle('M-103 Supply FRV'):weapon('gun')"));
        Assert.Contains("vehicle_weapon turret.yaw_min", RuntimeRequirement.Floors(w.Project!).Select(f => f.Reason));

        // A crossing edit the user keeps is saved; the build then reports the pair on both limits (not on the other pair).
        await w.SetEntityAsync(pitchMin.InstanceKey, "50");
        Assert.Contains("TURRET_LIMIT_ORDER", w.BuildError); Assert.Contains("TURRET_LIMIT_ORDER", Issue(w, pitchMin)); Assert.Contains("TURRET_LIMIT_ORDER", Issue(w, pitchMax));
        Assert.Null(Issue(w, yawMin)); Assert.False(EntityOrder.Ordered(w.Project, pair));
        // Fixed in place: both limits in one saved edit.
        await w.SetEntitiesAsync([(pitchMin.InstanceKey, "45"), (pitchMax.InstanceKey, "50")]);
        Assert.Null(w.BuildError); Assert.Contains("{field=hd2.fields.turret.pitch_min,expect=-3,value=45}", Operation(w.LuaPreview, CannonTarget));
        // Equal limits are not ordered (Runtime requires low < high); a limit that is switched off keeps its reviewed value.
        await w.SetEntityAsync(pitchMax.InstanceKey, "45"); Assert.Contains("TURRET_LIMIT_ORDER", w.BuildError);
        await w.ToggleEntityAsync(pitchMin.InstanceKey); Assert.Null(w.BuildError);
        await w.ToggleEntityAsync(pitchMin.InstanceKey); await w.ResetEntityAsync(instance: pitchMin.InstanceKey); Assert.Null(w.BuildError);
        // An in-game option sample is checked against the other limit's edited value.
        var option = ModOptionsService.EntityTarget(w.Metadata!, EntityChangeService.Saved(w.Project, yawMin)!, w.Project)!;
        option.Check(-10); Assert.Contains("TURRET_LIMIT_ORDER", Assert.Throws<InvalidDataException>(() => option.Check(5)).Message);
        await w.OpenAsync(w.Project!.Id); Assert.Null(w.BuildError); Assert.Equal(4, w.Project!.EntityChanges.Count);
        Assert.Equal(Runtime030Tests.Version, await MinVersion(w, "V2-turret-motion"));
    }

    [Fact] public async Task Jump_and_hover_movement_carries_its_opt_in_and_the_hover_launch_thrust_is_dormant_not_blocked()
    {
        using var e = new TestEnvironment(); var w = await Fresh(e);
        var jump = Entities(w).Backpacks.Find(JumpPack)!; var hover = Entities(w).Backpacks.Find(HoverPack)!;
        Assert.Equal(14, jump.SettingGroups.Single(g => g.Group == "movement").FieldInstanceKeys.Length);
        Assert.Equal(13, Entities(w).Backpacks.FieldInstances.Count(f => f.Target.Backpack == JumpPack && f.SemanticFieldId.StartsWith("jump.", StringComparison.Ordinal) && f.Acknowledgement != null));
        Assert.Equal(7, hover.SettingGroups.Single(g => g.Group == "movement").FieldInstanceKeys.Count(k => Entities(w).Field(k)!.SemanticFieldId.StartsWith("hover.", StringComparison.Ordinal)));
        // The launch thrust: 0.30's label and unit, the same value; gameplay-proven on the Jump Pack (no opt-in), dormant on the Hover Pack.
        var thrust = Pack(w, JumpPack, "jump.vertical_launch_velocity");
        Assert.Equal(("Launch thrust", "meters_per_second_squared", 40d, null), (thrust.DisplayName, thrust.Unit, thrust.CurrentDefault.GetDouble(), thrust.Acknowledgement));
        var dormant = Pack(w, HoverPack, "jump.vertical_launch_velocity");
        Assert.True(dormant.Editable); Assert.Equal(EntityNotice.Dormant, Assert.Single(dormant.Notices!).Kind);

        await w.SetEntityAsync(thrust.InstanceKey, "50");
        Assert.Null(w.BuildError);
        var alone = Operation(w.LuaPreview, "hd2.backpack('LIFT-850 Jump Pack')");
        Assert.Contains("\nfield=hd2.fields.jump.vertical_launch_velocity,\nexpect=40,\nvalue=50,\n", alone); Assert.DoesNotContain("allow_unverified_effect", alone);
        Assert.Empty(RuntimeRequirement.Floors(w.Project!));
        await w.SetEntityAsync(Pack(w, JumpPack, "jump.launch_duration").InstanceKey, "0.8");
        await w.SetEntityAsync(Pack(w, JumpPack, "jump.sustain_thrust").InstanceKey, "80");
        await Assert.ThrowsAsync<InvalidDataException>(() => w.SetEntityAsync(Pack(w, JumpPack, "jump.launch_forward_ratio").InstanceKey, "1.5"));
        // A dormant field is written as published (Runtime logs its DORMANT notice); it never blocks the build.
        await w.SetEntityAsync(dormant.InstanceKey, "60");
        await w.SetEntityAsync(Pack(w, HoverPack, "hover.max_horizontal_speed").InstanceKey, "6");
        Assert.Null(w.BuildError); var lua = w.LuaPreview;
        var jumpOp = Operation(lua, "hd2.backpack('LIFT-850 Jump Pack')");
        Assert.Contains("hd2.ensure({\ntransaction={\n", jumpOp); Assert.Contains("\nallow_unverified_effect=true,\n", jumpOp);
        Assert.Contains("{field=hd2.fields.jump.launch_duration,expect=0.5,value=0.8}", jumpOp); Assert.Contains("{field=hd2.fields.jump.vertical_launch_velocity,expect=40,value=50}", jumpOp);
        var hoverOp = Operation(lua, "hd2.backpack('LIFT-860 Hover Pack')");
        Assert.Contains("\nallow_unverified_effect=true,\n", hoverOp);
        Assert.Contains("{field=hd2.fields.jump.vertical_launch_velocity,expect=40,value=60}", hoverOp); Assert.Contains("{field=hd2.fields.hover.max_horizontal_speed,expect=3.5,value=6}", hoverOp);
        var floors = RuntimeRequirement.Floors(w.Project!).Select(f => f.Reason).ToArray();
        Assert.Contains("backpack jump.launch_duration", floors); Assert.Contains("backpack hover.max_horizontal_speed", floors);
        await w.OpenAsync(w.Project!.Id); Assert.Null(w.BuildError); Assert.Equal(lua, w.LuaPreview);
        Assert.Equal(Runtime030Tests.Version, await MinVersion(w, "V3-jump-hover"));
    }

    // jump.vertical_launch_velocity keeps its id and stored value in 0.30 (Runtime corrected its unit to m/s², "a thrust, not a velocity"), and
    // its capability evidence is unchanged: a 0.28.1 project's edit opens untouched and rebinds to 0.30.0 without review, same value, same write.
    [Fact] public async Task A_0_28_1_launch_velocity_edit_rebinds_to_the_0_30_launch_thrust_with_the_same_value()
    {
        using var e = new TestEnvironment(); var old = await SdkFixtures.Install(e, "0.28.1"); var w0 = e.Workspace();
        await w0.CreateAsync(new("Jump 0.28.1", "Tests", "mods/tests/jump_0281", "0.1.0"), old);
        var field = old.Entities!.Backpacks.FieldInstances.Single(f => f.Target.Backpack == JumpPack && f.SemanticFieldId == "jump.vertical_launch_velocity");
        Assert.Equal(("Vertical launch velocity", "meters_per_second"), (field.DisplayName, field.Unit));
        Assert.DoesNotContain(old.Entities.Vehicles.Vehicles, v => v.TuningFieldInstanceKeys != null);
        await w0.SetEntityAsync(field.InstanceKey, "55");
        var id = w0.Project!.Id; var lua0281 = w0.LuaPreview; Assert.Null(w0.BuildError);
        var saved = File.ReadAllBytes(e.Paths.ProjectFile(id));
        var cache = new SdkCache(e.Paths, e.Reader, e.GitHub); e.GitHub.Offline = true;
        var w = new BuilderWorkspace(e.Store, e.Projects, cache, new SdkUpdateService(cache, e.GitHub, e.Paths), e.Changes, e.Generator, e.Exporter, e.Desktop, e.Desktop, e.Paths);
        await w.InitializeAsync(); await w.OpenAsync(id);
        Assert.Equal("0.28.1", w.Project!.SdkVersion); Assert.Equal(lua0281, w.LuaPreview); Assert.Equal(saved, File.ReadAllBytes(e.Paths.ProjectFile(id)));
        await w.RebindToInstalledSdkAsync();
        Assert.Equal(Runtime030Tests.Version, w.Project!.SdkVersion); Assert.Null(w.BuildError);
        var change = Assert.Single(w.Project.EntityChanges); var thrust = Entities(w).Field(change.InstanceKey)!;
        Assert.Equal((40d, 55d), (change.ExpectedValue.GetDouble(), change.DesiredValue.GetDouble()));
        Assert.Equal(("Launch thrust", "meters_per_second_squared"), (thrust.DisplayName, thrust.Unit));
        Assert.Null(w.EntityIssue(change));
        var op = Operation(w.LuaPreview, "hd2.backpack('LIFT-850 Jump Pack')");
        Assert.Contains("\nfield=hd2.fields.jump.vertical_launch_velocity,\nexpect=40,\nvalue=55,\n", op);
        Assert.Equal(Operation(lua0281, "hd2.backpack('LIFT-850 Jump Pack')"), op);
    }

    [Fact] public async Task The_gib_threshold_is_switched_off_with_its_disabled_value_and_applies_at_hit_time()
    {
        using var e = new TestEnvironment(); var w = await Fresh(e);
        var gore = Entities(w).Enemies!.FieldInstances.Where(f => f.SemanticFieldId == Gib).ToArray();
        Assert.Equal(23, gore.Length); Assert.All(gore, f => Assert.True(f.Editable && f.EffectiveRange!.Disabled == -1 && f.Acknowledgement == "allow_unverified_effect"));
        var hunter = Gore(w, f => f.Target.EnemyClass == "hunter_base"); var guard = Gore(w, f => f.CurrentDefault.GetDouble() == -1);
        // -1 disables bursting; 0 and other non-positive values are refused with Runtime's value rule.
        Assert.Contains("0 < value", (await Assert.ThrowsAsync<InvalidDataException>(() => w.SetEntityAsync(hunter.InstanceKey, "0"))).Message);
        await Assert.ThrowsAsync<InvalidDataException>(() => w.SetEntityAsync(hunter.InstanceKey, "100001"));
        await w.SetEntityAsync(hunter.InstanceKey, "-1");
        await w.SetEntityAsync(guard.InstanceKey, "600");
        Assert.Null(w.BuildError); var lua = w.LuaPreview;
        Assert.Contains("\nallow_unverified_effect=true,\nfield=hd2.fields.gore.whole_body_gib_damage,\nexpect=500,\nvalue=-1,\n", Operation(lua, "hd2.enemy('hunter_base')"));
        Assert.Contains("\nfield=hd2.fields.gore.whole_body_gib_damage,\nexpect=-1,\nvalue=600,\n", Operation(lua, "hd2.enemy('" + guard.Target.EnemyClass + "')"));
        Assert.Contains("enemy gore.whole_body_gib_damage", RuntimeRequirement.Floors(w.Project!).Select(f => f.Reason));
        // Back to vanilla removes the edit.
        await w.SetEntityAsync(guard.InstanceKey, "-1"); Assert.Single(w.Project!.EntityChanges);
        lua = w.LuaPreview;
        await w.OpenAsync(w.Project!.Id); Assert.Null(w.BuildError); Assert.Equal(lua, w.LuaPreview);
        Assert.Equal(Runtime030Tests.Version, await MinVersion(w, "V4-gib-threshold"));
    }

    [Fact] public async Task The_capability_audit_lists_the_new_vehicle_fields_as_authored()
    {
        using var e = new TestEnvironment(); var w = await Fresh(e);
        var report = CapabilityAudit.Run(w.Metadata!);
        foreach (var (area, id) in new[] { ("vehicle", "vehicle.steering_response_speed"), ("vehicle", "rotation.turn_speed"), ("vehicle_weapon", "turret.pitch_min"),
            ("vehicle_weapon", "turret.yaw_speed"), ("backpack", "jump.sustain_thrust"), ("enemy", Gib) })
        {
            var entry = Assert.Single(report.Entries, x => x.Area == area && x.Capability == id);
            Assert.Equal(CapabilityClass.SUPPORTED_UI, entry.Class); Assert.Equal(entry.Instances, entry.WritableInstances);
        }
        Assert.DoesNotContain(CapabilityExemptions.Entries.Keys, k => k.Contains("turret.", StringComparison.Ordinal) || k.Contains("rotation.", StringComparison.Ordinal) || k.Contains("steering", StringComparison.Ordinal));
    }
}
