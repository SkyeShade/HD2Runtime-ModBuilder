using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using HD2RuntimeGUI.Core.Generation;
using HD2RuntimeGUI.Core.Localization;
using HD2RuntimeGUI.Core.Metadata;
using HD2RuntimeGUI.Core.Models;
using HD2RuntimeGUI.Core.Projects;
using HD2RuntimeGUI.Core.Storage;
using Xunit;

namespace HD2RuntimeGUI.Tests;

// ModBuilder 1.4.0 on the frozen HD2Runtime 0.28.0 SDK: backpacks and the entities they deploy or project (Guard Dog drones and their
// weapons, the SH-51 energy barrier), shield/warp/hover packs, mounted-weapon status slots and shared beam/arc rows, Resupply, sentry and
// minefield fields. Runtime opt-ins are implicit: the Lua carries exactly the flags Runtime requires and nothing needs acknowledging.
public sealed class EquipmentTests
{
    private const string GuardDog = "AX/AR-23 Guard Dog", Sh51 = "SH-51 Directional Shield", Sh32 = "SH-32 Shield Generator Pack", Warp = "LIFT-182 Warp Pack", Hover = "LIFT-860 Hover Pack";
    private const string DogGun = "AX/AR-23 Guard Dog / gun", RoverGun = "AX/LAS-5 Rover / gun", K9Gun = "AX/ARC-3 K-9 / gun", HotDogGun = "AX/FLAM-75 Hot Dog / gun",
        SupplyFrvGun = "M-103 Supply FRV / gun", MaelstromCannon = "TD-110 Maelstrom / attach_tank_gun";
    private static EntityField Backpack(SdkMetadata sdk, string backpack, string path, string field, string? linked = null) =>
        sdk.Entities!.Backpacks.FieldInstances.Single(f => f.Target.Backpack == backpack && f.Target.Path == path && f.Target.Linked == linked && f.SemanticFieldId == field);
    private static EntityField Weapon(SdkMetadata sdk, string weapon, string field) => sdk.Entities!.VehicleWeapons!.FieldInstances.Single(f => f.Target.Weapon == weapon && f.SemanticFieldId == field);
    private static StratagemField Stratagem(SdkMetadata sdk, string stratagem, string field) => sdk.Stratagems!.FieldInstances.Single(f => f.Target.Stratagem == stratagem && f.SemanticFieldId == field);
    private static string Flat(string s) => Regex.Replace(s, @"\s+", " ");
    private static int Count(string text, string value) => (text.Length - text.Replace(value, "", StringComparison.Ordinal).Length) / value.Length;
    // The generated operation on one target: its flags and fields, up to the next operation's target.
    private static string Operation(string lua, string target)
    {
        var start = lua.IndexOf("target=" + target + ",", StringComparison.Ordinal);
        Assert.True(start >= 0, "no operation on " + target);
        Assert.True(lua.IndexOf("target=" + target + ",", start + 1, StringComparison.Ordinal) < 0, "two operations on " + target);
        var end = lua.IndexOf("target=", start + 7, StringComparison.Ordinal);
        return end < 0 ? lua[start..] : lua[start..end];
    }
    private static string Root()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
            if (Directory.Exists(Path.Combine(dir.FullName, "HD2RuntimeGUI", "Components"))) return dir.FullName;
        throw new DirectoryNotFoundException("Repository root not found.");
    }

    // ---- backpack-linked entities -------------------------------------------------------------------------------------------------------

    [Fact] public async Task Guard_dog_drone_health_and_body_zone_are_written_through_the_backpack()
    {
        using var e = new TestEnvironment(); var (w, sdk) = await EnemyAuthoringTests.Fresh(e);
        var drone = sdk.Entities!.Backpacks.Find(GuardDog)!.Linked(BackpackLinkedEntity.Drone)!;
        Assert.Equal("deployed_drone", drone.Relationship); Assert.Equal("projectile", drone.WeaponFamily); Assert.NotEmpty(drone.Chain);
        Assert.Equal("zone_0", Assert.Single(drone.DamageZones!).ZoneId);
        var health = Backpack(sdk, GuardDog, "linked", "entity.health", "drone");
        var armor = Backpack(sdk, GuardDog, "linked", "entity.armor", "drone");
        var zoneArmor = Backpack(sdk, GuardDog, "damage_zone", "zone.armor", "drone");
        Assert.True(health.Editable); Assert.True(zoneArmor.Editable); Assert.Equal("allow_unverified_effect", health.Acknowledgement);
        Assert.Equal(100, Assert.Single(health.Correlations!).Published.GetInt32());
        // The drone's default-zone armor is only a fallback: read-only with Runtime's reason.
        Assert.False(armor.Editable); Assert.Contains("fallback", armor.Reason);
        await Assert.ThrowsAsync<InvalidDataException>(() => w.SetEntityAsync(armor.InstanceKey, "3"));

        await w.SetEntityAsync(health.InstanceKey, "250");
        await w.SetEntityAsync(zoneArmor.InstanceKey, "3");
        Assert.Null(w.BuildError); var lua = w.LuaPreview;
        Assert.Contains(Flat("target=hd2.backpack('AX/AR-23 Guard Dog'):drone(), allow_unverified_effect=true, field=hd2.fields.entity.health, expect=100, value=250,"), Flat(lua));
        Assert.Contains(Flat("target=hd2.backpack('AX/AR-23 Guard Dog'):drone():damage_zone('zone_0'), allow_unverified_effect=true, field=hd2.fields.zone.armor, expect=1, value=3,"), Flat(lua));
        Assert.DoesNotContain("allow_shared", lua); Assert.DoesNotContain("hd2.vehicle(", lua);
        // Two targets of one backpack: one plan.
        Assert.Equal(1, Count(lua, "plan={"));
        Assert.All(w.Project!.EntityChanges, c => { Assert.Equal("backpack", c.Resource); Assert.Equal(GuardDog, c.Entity); Assert.Equal("drone", c.Linked); });
        Assert.Equal(11, w.Project.FormatVersion);
        var json = await File.ReadAllTextAsync(e.Paths.ProjectFile(w.Project.Id));
        Assert.Contains("\"linked\": \"drone\"", json); Assert.Contains("\"formatVersion\": 11", json);
        await w.OpenAsync(w.Project.Id);
        Assert.Null(w.BuildError); Assert.Equal(lua, w.LuaPreview); Assert.All(w.Project!.EntityChanges, c => Assert.Equal("drone", c.Linked));
    }

    [Fact] public async Task The_sh51_energy_barrier_body_and_barrier_zone_are_separate_targets()
    {
        using var e = new TestEnvironment(); var (w, sdk) = await EnemyAuthoringTests.Fresh(e);
        var shield = sdk.Entities!.Backpacks.Find(Sh51)!.Linked(BackpackLinkedEntity.EnergyShield)!;
        Assert.Equal("spawned_shield", shield.Relationship); Assert.Null(shield.WeaponFamily); Assert.NotEmpty(shield.Findings!);
        Assert.Equal("body_front", Assert.Single(shield.DamageZones!).Name);
        var rate = Backpack(sdk, Sh51, "linked", "shield.recharge_rate", "energy_shield");
        var zoneHealth = Backpack(sdk, Sh51, "damage_zone", "zone.health", "energy_shield");
        var body = Backpack(sdk, Sh51, "backpack", "entity.health");
        var radius = Backpack(sdk, Sh51, "linked", "shield.radius", "energy_shield");
        Assert.Equal("native_correlated", rate.Evidence.Tier); Assert.Contains("called in", rate.Effect!.Lifecycle);
        Assert.Equal("backpack body", body.Effect!.AppliesTo); Assert.Equal("UNPROVEN", zoneHealth.Effect!.ActiveSource);
        Assert.False(radius.Editable); Assert.Contains("not a sphere", radius.Reason);

        await w.SetEntityAsync(rate.InstanceKey, "450");
        await w.SetEntityAsync(zoneHealth.InstanceKey, "900");
        await w.SetEntityAsync(body.InstanceKey, "600");
        Assert.Null(w.BuildError); var lua = w.LuaPreview;
        Assert.Contains("value=450", Operation(lua, "hd2.backpack('SH-51 Directional Shield'):energy_shield()"));
        Assert.Contains("hd2.fields.shield.recharge_rate", Operation(lua, "hd2.backpack('SH-51 Directional Shield'):energy_shield()"));
        Assert.Contains("hd2.fields.zone.health", Operation(lua, "hd2.backpack('SH-51 Directional Shield'):energy_shield():damage_zone('zone_0')"));
        Assert.Contains("hd2.fields.entity.health", Operation(lua, "hd2.backpack('SH-51 Directional Shield')"));
        Assert.Equal(3, Count(lua, "allow_unverified_effect=true")); Assert.DoesNotContain("allow_shared", lua);
        Assert.Equal(["energy_shield", "energy_shield", null], w.Project!.EntityChanges.OrderBy(c => c.Path == "backpack").Select(c => c.Linked));
    }

    [Fact] public async Task Shield_warp_and_hover_pack_fields_are_authored_with_their_bounds_and_opt_ins()
    {
        using var e = new TestEnvironment(); var (w, sdk) = await EnemyAuthoringTests.Fresh(e);
        var backpacks = sdk.Entities!.Backpacks;
        // Every 0.28.0 backpack field Runtime publishes writable is writable here; the rest keep Runtime's reason.
        Assert.Equal(backpacks.Summary.WritableFieldInstances, backpacks.FieldInstances.Count(f => f.Editable));
        Assert.DoesNotContain(backpacks.FieldInstances, f => f.Reason == AuthoredTypes.NotAuthoredReason);
        Assert.All(backpacks.FieldInstances.Where(f => !f.Editable), f => Assert.False(string.IsNullOrWhiteSpace(f.Reason)));
        Assert.Contains("native_correlated", backpacks.EvidenceTiers.Keys);

        var distance = Backpack(sdk, Warp, "backpack", "warp.distance"); var head = Backpack(sdk, Warp, "backpack", "warp.head_injury_damage");
        Assert.Equal("native_correlated", distance.Evidence.Tier); Assert.Equal(100, distance.EffectiveRange!.Max); Assert.Equal("meters", distance.Unit);
        await Assert.ThrowsAsync<InvalidDataException>(() => w.SetEntityAsync(distance.InstanceKey, "101"));
        await w.SetEntityAsync(distance.InstanceKey, "20"); await w.SetEntityAsync(head.InstanceKey, "0");
        var duration = Backpack(sdk, Hover, "backpack", "hover.duration"); var recharge = Backpack(sdk, Hover, "backpack", "recharge.time");
        Assert.Null(recharge.Acknowledgement); Assert.Equal("allow_unverified_effect", duration.Acknowledgement);
        await w.SetEntityAsync(duration.InstanceKey, "12"); await w.SetEntityAsync(recharge.InstanceKey, "5");
        var delay = Backpack(sdk, Sh32, "backpack", "shield.recharge_delay"); var radius = Backpack(sdk, Sh32, "backpack", "shield.radius");
        await w.SetEntityAsync(delay.InstanceKey, "10"); await w.SetEntityAsync(radius.InstanceKey, "3");
        Assert.Null(w.BuildError); var lua = w.LuaPreview;
        // Warp: one transaction on the backpack, carrying the opt-in its published fields require.
        var warp = Operation(lua, "hd2.backpack('LIFT-182 Warp Pack')");
        Assert.Contains("allow_unverified_effect=true", warp); Assert.Contains("{field=hd2.fields.warp.distance,expect=10,value=20},", warp);
        Assert.Contains("{field=hd2.fields.warp.head_injury_damage,expect=10,value=0},", warp);
        // Hover: the proven recharge and the correlated hover duration are separate operations; only the hover operation carries the opt-in.
        Assert.Contains("plan={", lua);
        Assert.Equal(1, Count(Flat(lua), "field=hd2.fields.recharge.time, expect=11.5, value=5,"));
        Assert.Contains(Flat("allow_unverified_effect=true, field=hd2.fields.hover.duration, expect=6, value=12,"), Flat(lua));
        Assert.DoesNotContain(Flat("allow_unverified_effect=true, field=hd2.fields.recharge.time"), Flat(lua));
        // SH-32: radius (schema-proven) and recharge delay (native-correlated) share one component, so one transaction with the opt-in.
        var sh32 = Operation(lua, "hd2.backpack('SH-32 Shield Generator Pack')");
        Assert.Contains("allow_unverified_effect=true", sh32); Assert.Contains("hd2.fields.shield.recharge_delay", sh32); Assert.Contains("hd2.fields.shield.entity_radius", sh32);
        // Backpack-own settings are not new in format: the project keeps its earlier format.
        Assert.True(w.Project!.FormatVersion < 11); Assert.All(w.Project.EntityChanges, c => Assert.Null(c.Linked));
    }

    // ---- Guard Dog drone weapons --------------------------------------------------------------------------------------------------------

    [Fact] public async Task Guard_dog_drone_weapons_are_written_through_the_carrier_accessor()
    {
        using var e = new TestEnvironment(); var (w, sdk) = await EnemyAuthoringTests.Fresh(e);
        var weapons = sdk.Entities!.VehicleWeapons!;
        var carrier = weapons.CarriedBy(GuardDog)!;
        Assert.Equal("hd2.backpack('AX/AR-23 Guard Dog'):drone():weapon()", carrier.Carrier!.Api);
        Assert.Null(sdk.Entities.Vehicles.Find(GuardDog)); // a backpack, never listed as a vehicle
        var capacity = Weapon(sdk, DogGun, "weapon.capacity");
        Assert.True(capacity.Editable); Assert.Equal("drone", capacity.Target.Linked); Assert.Equal(45, capacity.CurrentDefault.GetInt32());
        // Every carried scalar with an API constant is authored on all five drones.
        Assert.All(weapons.FieldInstances.Where(f => f.Target.Linked != null && f.Type is "number" or "integer" && f.ApiFieldConstant.Length > 0), f => Assert.True(f.Editable));
        Assert.Equal(5, weapons.Vehicles.Count(v => v.Carrier != null));

        await w.SetEntityAsync(capacity.InstanceKey, "200");
        Assert.Null(w.BuildError); var lua = w.LuaPreview;
        Assert.Contains(Flat("target=hd2.backpack('AX/AR-23 Guard Dog'):drone():weapon(), allow_unverified_effect=true, field=hd2.fields.weapon.capacity, expect=45, value=200,"), Flat(lua));
        Assert.DoesNotContain("hd2.vehicle(", lua);
        var change = Assert.Single(w.Project!.EntityChanges);
        Assert.Equal(("vehicle_weapon", DogGun, "drone"), (change.Resource, change.Entity, change.Linked));
        Assert.Equal(11, w.Project.FormatVersion);

        // The drone's bullet damage row is also the M-103 gun's: one value, edited through one target only (refused where the edit is made).
        var dogDamage = Weapon(sdk, DogGun, "damage.primary.standard_damage");
        await w.SetEntityAsync(dogDamage.InstanceKey, "80");
        Assert.Contains("hd2.backpack('AX/AR-23 Guard Dog'):drone():weapon():projectile()", w.LuaPreview);
        var refused = await Assert.ThrowsAsync<InvalidDataException>(() => w.SetEntityAsync(Weapon(sdk, SupplyFrvGun, "damage.primary.standard_damage").InstanceKey, "80"));
        Assert.Contains(DogGun, refused.Message); Assert.Equal(2, w.Project.EntityChanges.Count);
        lua = w.LuaPreview;
        await w.OpenAsync(w.Project.Id); Assert.Null(w.BuildError); Assert.Equal(lua, w.LuaPreview);
    }

    [Fact] public async Task Shared_beam_and_arc_rows_carry_allow_shared_with_their_published_scope()
    {
        using var e = new TestEnvironment(); var (w, sdk) = await EnemyAuthoringTests.Fresh(e);
        var weapons = sdk.Entities!.VehicleWeapons!;
        var length = Weapon(sdk, RoverGun, "beam.primary.length"); var range = Weapon(sdk, K9Gun, "arc.primary.range");
        Assert.Equal("shared_beam", weapons.Published[length.InstanceKey].Scope); Assert.Equal("shared_arc", weapons.Published[range.InstanceKey].Scope);
        Assert.True(length.AllowSharedRequired && range.AllowSharedRequired); Assert.True(length.DynamicConsumersPossible && range.DynamicConsumersPossible);
        Assert.False(string.IsNullOrWhiteSpace(weapons.Scopes["shared_beam"])); Assert.False(string.IsNullOrWhiteSpace(weapons.Scopes["shared_arc"]));
        await w.SetEntityAsync(length.InstanceKey, "60"); await w.SetEntityAsync(range.InstanceKey, "70");
        Assert.Null(w.BuildError); var lua = w.LuaPreview;
        Assert.Contains(Flat("target=hd2.backpack('AX/LAS-5 Rover'):drone():weapon():attack('primary'), allow_shared=true, allow_unverified_effect=true, field=hd2.fields.beam.length, expect=1000, value=60,"), Flat(lua));
        Assert.Contains(Flat("target=hd2.backpack('AX/ARC-3 K-9'):drone():weapon():attack('primary'), allow_shared=true, allow_unverified_effect=true, field=hd2.fields.arc.range, expect=55, value=70,"), Flat(lua));
    }

    // ---- mounted-weapon status slots ----------------------------------------------------------------------------------------------------

    [Fact] public async Task Mounted_weapon_status_slots_take_published_statuses_and_stay_packed()
    {
        using var e = new TestEnvironment(); var (w, sdk) = await EnemyAuthoringTests.Fresh(e);
        var s1 = Weapon(sdk, K9Gun, "damage.primary.status_1_type"); var s2 = Weapon(sdk, K9Gun, "damage.primary.status_2_type");
        var strength2 = Weapon(sdk, K9Gun, "damage.primary.status_2_strength");
        Assert.True(s1.Editable && s2.Editable && strength2.Editable); Assert.Equal("stun_small", s1.CurrentDefault.GetString());
        var choices = MountedStatusSlots.For(sdk, s1);
        Assert.True(choices.AllowNone); Assert.Contains("stun_medium", choices.Statuses); Assert.Contains("stun_small", choices.Statuses);
        Assert.All(choices.Statuses, k => Assert.True(sdk.StatusEffects!.Find(k)!.Attachable || k == "stun_small"));
        // Only attachable statuses: an enemy-only status is refused where the edit is made.
        await Assert.ThrowsAsync<InvalidDataException>(() => w.SetEntityAsync(s1.InstanceKey, "acid_splash"));
        await Assert.ThrowsAsync<InvalidDataException>(() => w.SetEntityAsync(s1.InstanceKey, "Stun Medium"));

        await w.SetEntityAsync(s1.InstanceKey, "stun_medium");
        await w.SetEntityAsync(s2.InstanceKey, "fire"); await w.SetEntityAsync(strength2.InstanceKey, "2");
        Assert.Null(w.BuildError); var lua = w.LuaPreview;
        var op = Operation(lua, "hd2.backpack('AX/ARC-3 K-9'):drone():weapon():attack('primary')");
        Assert.Contains("allow_shared=true", op); Assert.Contains("allow_unverified_effect=true", op);
        Assert.Contains("{field=hd2.fields.damage.status_1_type,expect='stun_small',value='stun_medium'}", lua);
        Assert.Contains("{field=hd2.fields.damage.status_2_type,expect='none',value='fire'}", lua);
        Assert.Contains("{field=hd2.fields.damage.status_2_strength,expect=0,value=2}", lua);
        Assert.Equal(11, w.Project!.FormatVersion);

        // Slots stay packed: slot 1 cannot be cleared while slot 2 applies a status (refused inline, nothing saved).
        var gap = await Assert.ThrowsAsync<InvalidDataException>(() => w.SetEntityAsync(s1.InstanceKey, "none"));
        Assert.Contains("slot 1", gap.Message); Assert.Equal("stun_medium", w.Project.EntityChanges.Single(c => c.InstanceKey == s1.InstanceKey).DesiredValue.GetString());
        await w.ResetEntityAsync(instance: s2.InstanceKey); await w.ResetEntityAsync(instance: strength2.InstanceKey);
        await w.SetEntityAsync(s1.InstanceKey, "none");
        Assert.Null(w.BuildError); Assert.Contains(Flat("field=hd2.fields.damage.status_1_type, expect='stun_small', value='none',"), Flat(w.LuaPreview));
        // ... and then slot 2 cannot gain one after the cleared slot.
        await Assert.ThrowsAsync<InvalidDataException>(() => w.SetEntityAsync(s2.InstanceKey, "fire"));

        // The Hot Dog uses three slots: only the last used one can be cleared.
        var hot1 = Weapon(sdk, HotDogGun, "damage.primary.status_1_type"); var hot3 = Weapon(sdk, HotDogGun, "damage.primary.status_3_type");
        Assert.False(MountedStatusSlots.For(sdk, hot1).AllowNone); Assert.True(MountedStatusSlots.For(sdk, hot3).AllowNone);
        var clear = await Assert.ThrowsAsync<InvalidDataException>(() => w.SetEntityAsync(hot1.InstanceKey, "none"));
        Assert.Contains("slot 1", clear.Message);
        await w.SetEntityAsync(hot3.InstanceKey, "none"); Assert.Null(w.BuildError);

        // A vehicle-mounted weapon's status slot is written through its vehicle.
        var cannon = Weapon(sdk, MaelstromCannon, "damage.primary.status_1_type");
        await w.SetEntityAsync(cannon.InstanceKey, "burning_heavy");
        Assert.Null(w.BuildError);
        Assert.Contains("value='burning_heavy'", Operation(w.LuaPreview, "hd2.vehicle('TD-110 Maelstrom'):weapon('attach_tank_gun'):projectile()"));
        // Explosion status slots without a published API constant stay read-only with the reason.
        var explosion = sdk.Entities!.VehicleWeapons!.FieldInstances.Where(f => f.IsStatusReference && f.ApiFieldConstant.Length == 0).ToArray();
        Assert.NotEmpty(explosion); Assert.All(explosion, f => { Assert.False(f.Editable); Assert.Equal(VehicleWeaponReader.NoApiConstantReason, f.Reason); });
        // Status changes are validated as status keys.
        var p = w.Project; ProjectIdentity.Validate(p);
        var saved = p.EntityChanges.First(c => c.FieldType == WeaponCapability.StatusReference);
        p.EntityChanges[p.EntityChanges.IndexOf(saved)] = saved with { DesiredValue = JsonSerializer.SerializeToElement("Burning Heavy!") };
        Assert.Throws<InvalidDataException>(() => ProjectIdentity.Validate(p));
        p.EntityChanges[p.EntityChanges.FindIndex(c => c.Id == saved.Id)] = saved with { DesiredValue = JsonSerializer.SerializeToElement(3) };
        Assert.Throws<InvalidDataException>(() => ProjectIdentity.Validate(p));
        p.EntityChanges[p.EntityChanges.FindIndex(c => c.Id == saved.Id)] = saved;
        ProjectIdentity.Validate(p);
        // Statuses are never in-game options (they are keys, not numbers).
        Assert.NotNull(ModOptionsService.Targets(p, sdk).Single(t => t.Key == ModOptionsService.EntityKey(saved.InstanceKey)).Blocker);
    }

    // ---- Resupply, sentries and minefields ----------------------------------------------------------------------------------------------

    [Fact] public async Task Resupply_cooldown_and_live_verified_pod_slots_drop_only_the_unverified_reference()
    {
        using var e = new TestEnvironment(); var (w, sdk) = await EnemyAuthoringTests.Fresh(e);
        var resupply = sdk.Stratagems!.Stratagems.Single(s => s.Name == "Resupply");
        Assert.Equal(("mission", "pod_rack"), (resupply.Family, resupply.Delivers!.Kind));
        await w.SetStratagemAsync(Stratagem(sdk, "Resupply", "stratagem.cooldown").InstanceKey, "5");
        var pods = sdk.Entities!.Pods!; var rack = pods.ForStratagem(resupply.SemanticId).Single();
        var grenades = pods.Pickups.Single(p => p.Name == "Grenade Box"); var ammo = pods.Pickups.Single(p => p.Name == "Ammo Box (pod)");
        var slot1 = pods.FieldInstances.Single(f => f.InstanceKey == PodPayloadReader.SlotKey(rack, 1));
        Assert.Equal([grenades.SemanticId], slot1.LiveProvenValues);
        await w.SetEntityAsync(slot1.InstanceKey, grenades.SemanticId);
        await w.SetEntityAsync(PodPayloadReader.SlotKey(rack, 2), ammo.SemanticId);
        await w.SetEntityAsync(PodPayloadReader.SpawnKey(rack), "2");
        Assert.Null(w.BuildError); var lua = w.LuaPreview;
        Assert.Contains(Flat("target=hd2.stratagem('Resupply'), field=hd2.fields.stratagem.definition_cooldown, expect=180, value=5,"), Flat(lua));
        // Slot 1 holds the live-verified Grenade Box: no allow_unverified_reference, but allow_shared stays (the rack is shared).
        var first = Operation(lua, "hd2.pod_rack('Resupply pod'):slot(1)");
        Assert.Contains("allow_shared=true", first); Assert.DoesNotContain("allow_unverified_reference", first);
        Assert.Contains($"value=hd2.pickup('{grenades.SemanticId}')", first);
        Assert.True(EntityScalar.LiveProven(slot1, JsonSerializer.SerializeToElement(grenades.SemanticId)));
        // Another pickup, or the same pickup elsewhere, keeps the acknowledgement.
        var second = Operation(lua, "hd2.pod_rack('Resupply pod'):slot(2)");
        Assert.Contains("allow_shared=true", second); Assert.Contains("allow_unverified_reference=true", second);
        var spawn = Operation(lua, "hd2.pod_rack('Resupply pod')");
        Assert.Contains("allow_shared=true", spawn); Assert.Contains("allow_unverified_effect=true", spawn);
        var stalwart = pods.Rack("M-105 Stalwart pod")!;
        Assert.True(EntityScalar.AcknowledgementRequired(pods.FieldInstances.Single(f => f.InstanceKey == PodPayloadReader.SlotKey(stalwart, 1)), JsonSerializer.SerializeToElement(grenades.SemanticId)));
        await w.OpenAsync(w.Project!.Id); Assert.Null(w.BuildError); Assert.Equal(lua, w.LuaPreview);
    }

    [Fact] public async Task Sentry_speeds_targeting_range_and_minefield_counts_carry_only_the_opt_ins_they_need()
    {
        using var e = new TestEnvironment(); var (w, sdk) = await EnemyAuthoringTests.Fresh(e);
        const string Mg = "A/MG-43 Machine Gun Sentry", Mines = "MD-6 Anti-Personnel Minefield";
        // Live-proven: turn speeds, targeting range and salvos need no opt-in.
        await w.SetStratagemAsync(Stratagem(sdk, Mg, "turret.yaw_speed").InstanceKey, "120");
        await w.SetStratagemAsync(Stratagem(sdk, Mg, "targeting.range").InstanceKey, "90");
        await w.SetStratagemAsync(Stratagem(sdk, Mines, "minefield.salvos").InstanceKey, "2");
        Assert.Null(w.BuildError);
        Assert.DoesNotContain("allow_unverified_effect", w.LuaPreview);
        Assert.Contains("hd2.fields.turret.yaw_speed", Operation(w.LuaPreview, "hd2.stratagem('A/MG-43 Machine Gun Sentry'):deployed_entity():turret()"));
        // Reduce-only salvos (1 to the vanilla 6).
        await Assert.ThrowsAnyAsync<Exception>(() => w.SetStratagemAsync(Stratagem(sdk, Mines, "minefield.salvos").InstanceKey, "7"));
        // Untested members keep allow_unverified_effect.
        await w.SetStratagemAsync(Stratagem(sdk, Mines, "minefield.mines_per_salvo").InstanceKey, "4");
        await w.SetStratagemAsync(Stratagem(sdk, Mg, "turret.pitch_min").InstanceKey, "-30");
        Assert.Null(w.BuildError); var lua = w.LuaPreview;
        Assert.Contains("allow_unverified_effect=true", Operation(lua, "hd2.stratagem('MD-6 Anti-Personnel Minefield'):deployed_entity():minefield()"));
        Assert.Contains("allow_unverified_effect=true", Operation(lua, "hd2.stratagem('A/MG-43 Machine Gun Sentry'):deployed_entity():turret()"));
        Assert.DoesNotContain("allow_unverified_effect", Operation(lua, "hd2.stratagem('A/MG-43 Machine Gun Sentry'):deployed_entity():targeting()"));
    }

    // ---- vehicles, enemies and the catalog as a whole -----------------------------------------------------------------------------------

    [Fact] public async Task Vehicle_mount_scalars_are_authored_and_carried_ones_never_look_like_vehicles()
    {
        using var e = new TestEnvironment(); var (w, sdk) = await EnemyAuthoringTests.Fresh(e);
        var weapons = sdk.Entities!.VehicleWeapons!;
        foreach (var vehicle in new[] { "EXO-45 Patriot Exosuit", "EXO-49 Emancipator Exosuit", "M-102 Gunner FRV", "FRV (Super Earth variant)", "M-103 Supply FRV", "GATER Oil Rig", "TD-110 Maelstrom", "TD-220 Bastion MK XVI" })
        {
            var fields = weapons.FieldInstances.Where(f => VehicleWeaponReader.Split(f.Target.Weapon!).Vehicle == vehicle).ToArray();
            Assert.NotEmpty(fields);
            Assert.All(fields.Where(f => f.Type is "number" or "integer" && f.ApiFieldConstant.Length > 0), f => Assert.True(f.Editable, f.InstanceKey));
            Assert.All(fields, f => Assert.Null(f.Target.Linked));
        }
        // The Patriot's gameplay-proven magazine needs no opt-in; its target is unchanged by 1.4.0.
        var magazine = Weapon(sdk, "EXO-45 Patriot Exosuit / right_gun", "weapon.capacity");
        await w.SetEntityAsync(magazine.InstanceKey, (magazine.CurrentDefault.GetInt32() + 100).ToString(CultureInfo.InvariantCulture));
        Assert.Null(w.BuildError);
        Assert.DoesNotContain("allow_unverified_effect", Operation(w.LuaPreview, "hd2.vehicle('EXO-45 Patriot Exosuit'):weapon('right_gun')"));
        Assert.True(w.Project!.FormatVersion < 11);
        // Enemies and structures stay the frozen catalog's (177 classes: 138 enemies, 39 structures).
        Assert.Equal(177, sdk.Entities.Enemies!.Classes.Length);
        Assert.Equal((138, 39), (sdk.Entities.Enemies.Of(EnemyAuthoringReader.Enemy).Count(), sdk.Entities.Enemies.Of(EnemyAuthoringReader.Structure).Count()));
    }

    [Fact] public async Task Pre_1_4_changes_keep_their_serialization_evidence_and_lua()
    {
        using var e = new TestEnvironment(); var (w, sdk) = await EnemyAuthoringTests.Fresh(e);
        var zone = sdk.Entities!.Backpacks.FieldInstances.Single(f => f.Target.Backpack == "SH-20 Ballistic Shield Backpack" && f.Target.Path == "damage_zone");
        var capacity = Weapon(sdk, "EXO-49 Emancipator Exosuit / left_gun", "weapon.capacity");
        // Targets without a linked entity serialize exactly as before, so their evidence hashes are unchanged.
        foreach (var f in new[] { zone, capacity }) Assert.DoesNotContain("linked", JsonSerializer.Serialize(f.Target, JsonStorage.Options), StringComparison.OrdinalIgnoreCase);
        await w.SetEntityAsync(zone.InstanceKey, "6"); await w.SetEntityAsync(capacity.InstanceKey, "150");
        Assert.Null(w.BuildError); var lua = w.LuaPreview;
        Assert.Contains("target=hd2.backpack('SH-20 Ballistic Shield Backpack'):damage_zone('zone_0'),", lua);
        Assert.Contains("target=hd2.vehicle('EXO-49 Emancipator Exosuit'):weapon('left_gun'),", lua);
        var json = await File.ReadAllTextAsync(e.Paths.ProjectFile(w.Project!.Id));
        Assert.DoesNotContain("\"linked\"", json); Assert.True(w.Project.FormatVersion < 11);
        Assert.True(ProjectIdentity.RequiredFormat(w.Project) < 11);
        // A linked entity is accepted only where it exists.
        var p = w.Project; var saved = p.EntityChanges.Single(c => c.Resource == "vehicle_weapon");
        foreach (var bad in new[] { saved with { Linked = "energy_shield" }, saved with { Linked = "turret" } })
        { p.EntityChanges[p.EntityChanges.FindIndex(c => c.Id == saved.Id)] = bad; Assert.Throws<InvalidDataException>(() => ProjectIdentity.Validate(p)); }
        var backpackChange = p.EntityChanges.Single(c => c.Resource == "backpack");
        p.EntityChanges[p.EntityChanges.FindIndex(c => c.Id == saved.Id)] = saved;
        p.EntityChanges[p.EntityChanges.FindIndex(c => c.Id == backpackChange.Id)] = backpackChange with { Path = "backpack", Zone = null, Linked = "drone" };
        Assert.Throws<InvalidDataException>(() => ProjectIdentity.Validate(p));
        p.EntityChanges[p.EntityChanges.FindIndex(c => c.Id == backpackChange.Id)] = backpackChange;
        ProjectIdentity.Validate(p);
        await w.OpenAsync(p.Id); Assert.Null(w.BuildError); Assert.Equal(lua, w.LuaPreview);
    }

    [Fact] public void The_native_correlated_tier_has_labels_in_every_language()
    {
        var chinese = UiLanguages.Find(UiLanguages.SimplifiedChinese)!.Culture;
        foreach (var key in new[] { "Evidence.Tier.NativeCorrelated", "Evidence.Short.NativeCorrelated" })
        {
            var english = TextResources.Find(key, CultureInfo.InvariantCulture); var translated = TextResources.Find(key, chinese);
            Assert.False(string.IsNullOrWhiteSpace(english)); Assert.False(string.IsNullOrWhiteSpace(translated)); Assert.NotEqual(english, translated);
        }
        Assert.Equal("Native-correlated · effect unproven", TextResources.Get("Evidence.Tier.NativeCorrelated", CultureInfo.InvariantCulture));
        Assert.Equal("原生关联 · 效果未证实", TextResources.Get("Evidence.Tier.NativeCorrelated", chinese));
        // The badge maps the tier to those keys (full and compact label).
        var badge = File.ReadAllText(Path.Combine(Root(), "HD2RuntimeGUI", "Components", "EvidenceBadge.razor"));
        Assert.Contains("\"native_correlated\" => t[\"Evidence.Tier.NativeCorrelated\"]", badge);
        Assert.Contains("\"native_correlated\" => t[\"Evidence.Short.NativeCorrelated\"]", badge);
    }
}
