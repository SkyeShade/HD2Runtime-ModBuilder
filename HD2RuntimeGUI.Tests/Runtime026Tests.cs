using HD2RuntimeGUI.Core.Generation;
using HD2RuntimeGUI.Core.Metadata;
using HD2RuntimeGUI.Core.Services;
using Xunit;

namespace HD2RuntimeGUI.Tests;

// HD2Runtime 0.26.0 (published HD2Runtime-0.26.0-sdk.zip): every magazine option, third-person reticles, native fire-mode lists,
// mounted vehicle weapons, generic mission uses, backpack-fed support ammunition and drop-pod payloads.
public sealed class Runtime026Tests
{
    private const string Jar = "JAR-5 Dominator", Apw = "APW-1 Anti-Materiel Rifle", Maxigun = "M-1000 Maxigun", Cremator = "B/FLAM-80 Cremator", Gl28 = "GL-28 Belt-Fed Grenade Launcher";
    private const string EatRack = "EAT-17 Expendable Anti-Tank pod", Surplus = "Surplus EAT Allocation";
    private const string Eat17 = "pickup/v1/eat-17-expendable-anti-tank/aaf1f6d08281fd7a", Leveller = "pickup/v1/eat-411-leveller/bbd78065df92b6eb", AmmoWorld = "pickup/v1/ammo-box-world/e04feddc177e2ee8";
    private static async Task<BuilderWorkspace> Fresh(TestEnvironment e, string version = "0.26.0", string resource = "mods/tests/runtime026")
    {
        var sdk = await SdkFixtures.Install(e, version); var w = e.Workspace();
        await w.CreateAsync(new("Runtime 0.26", "Tests", resource, "0.1.0"), sdk); return w;
    }
    private static EntityAuthoring Entities(BuilderWorkspace w) => w.Metadata!.Entities!;
    private static EntityField VehicleWeapon(BuilderWorkspace w, string weapon, string field, string? attack = null) =>
        Entities(w).VehicleWeapons!.FieldInstances.First(f => f.Target.Weapon == weapon && f.SemanticFieldId == field && (attack == null || f.Target.Attack == attack));
    private static SupportField Support(BuilderWorkspace w, string weapon, string field) =>
        w.Metadata!.SupportAuthoring!.FieldInstances.Single(f => f.SupportWeapon == weapon && f.SemanticFieldId == field && f.Target.AttackRole == null);
    private static StratagemField Uses(BuilderWorkspace w, string stratagem) =>
        w.Metadata!.Stratagems!.FieldInstances.Single(f => f.Target.Stratagem == stratagem && f.SemanticFieldId == StratagemUses.Field);
    // A different valid value: integers move by a step, numbers by a tenth of their magnitude (or 1).
    private static string Bump(EntityField f, int step = 1) => f.Type == "integer" ? (f.CurrentDefault.GetInt32() + step).ToString(System.Globalization.CultureInfo.InvariantCulture)
        : (f.CurrentDefault.GetDouble() * 1.1 + step).ToString("R", System.Globalization.CultureInfo.InvariantCulture);
    private static int Count(string text, string part) { var n = 0; for (var i = text.IndexOf(part, StringComparison.Ordinal); i >= 0; i = text.IndexOf(part, i + part.Length, StringComparison.Ordinal)) n++; return n; }

    [Fact] public async Task Sdk_0260_publishes_every_new_capability()
    {
        using var e = new TestEnvironment(); var sdk = await SdkFixtures.Install(e, "0.26.0");
        Assert.Equal("0.26.0", sdk.Version); Assert.True(sdk.Has026);
        var entities = sdk.Entities!;
        Assert.Equal(11, entities.VehicleWeapons!.Vehicles.Length); Assert.Equal(480, entities.VehicleWeapons.FieldInstances.Length);
        Assert.Equal(56, entities.Pods!.Racks.Length); Assert.Equal(115, sdk.FireModes!.Weapons.Length);
        Assert.Equal(70, sdk.PlayerWeapons!.Weapons.Count(w => w.Fields.Any(c => c.SemanticFieldId == FireModes.ReticleField && c.Editable)));
        Assert.Equal(3, entities.Backpacks.Backpacks.Count(b => b.Feeds != null));
    }

    // ---- Magazines ----
    [Fact] public async Task Every_resolved_magazine_option_is_independently_editable_with_reload_and_ergonomics()
    {
        using var e = new TestEnvironment(); var w = await Fresh(e); var mags = Entities(w).Attachments!;
        Assert.Equal(2, mags.SchemaVersion);
        var liberator = mags.Weapon("AR-23 Liberator")!; var resolved = mags.Resolved(liberator);
        // Every option resolves independently, including one known only from the weapon's unlock list (native name, no catalog name).
        Assert.Equal(["Extended Magazine", "Short Magazine", "Drum Magazine", "Rifle 5,5x50mm. Extended Fastreload"], resolved.Select(r => r.Option!.Label));
        Assert.Equal(["native_resource_default", "catalog_effects_unique", "catalog_effects_unlock_list", "unlock_listed"], resolved.Select(r => r.Relationship));
        Assert.Contains(resolved, r => r.Relationship == "unlock_listed" && r.Option!.Name == null && r.Option.NativeName != null);
        var drum = resolved.Single(r => r.Option!.Label == "Drum Magazine").Attachment;
        Assert.False(drum.SemanticId == liberator.MagazineSlot.DefaultAttachment);
        var fields = mags.FieldInstances.Where(f => f.Target.Attachment == drum.SemanticId).ToArray();
        var capacity = fields.Single(f => f.SemanticFieldId == "attachment.magazine_capacity");
        var reload = fields.Single(f => f.SemanticFieldId.EndsWith("reload_duration", StringComparison.Ordinal));
        var ergonomics = fields.Single(f => f.SemanticFieldId.EndsWith("ergonomics_modifier", StringComparison.Ordinal));
        Assert.Equal("number", reload.Type); Assert.Equal("number", ergonomics.Type);
        await w.SetEntityAsync(capacity.InstanceKey, "90"); await w.SetEntityAsync(reload.InstanceKey, "2.5"); await w.SetEntityAsync(ergonomics.InstanceKey, "10");
        await Assert.ThrowsAsync<InvalidDataException>(() => w.SetEntityAsync(reload.InstanceKey, "25"));
        // The attachment opt-ins are implicit: the edit builds without an acknowledgement.
        Assert.Null(w.BuildError);
        var lua = w.LuaPreview;
        Assert.Contains($"target=hd2.weapon_attachment('{drum.SemanticId}')", lua); Assert.Contains("{field=hd2.fields.attachment.magazine_capacity,expect=60,value=90}", lua);
        Assert.Contains("{field=hd2.fields.attachment.reload_duration,expect=3.5,value=2.5}", lua); Assert.Contains("{field=hd2.fields.attachment.ergonomics_modifier,expect=-15,value=10}", lua);
        Assert.Contains("allow_unverified_effect=true", lua);
        // The equipped magazine never changes: no selection write exists.
        Assert.False(mags.Selection.Writable); Assert.DoesNotContain("attachment_selection", lua);
    }

    [Fact] public async Task Unresolved_heatsink_options_stay_unresolved_without_controls()
    {
        using var e = new TestEnvironment(); var w = await Fresh(e); var mags = Entities(w).Attachments!;
        var scythe = mags.Weapon("LAS-5 Scythe")!;
        Assert.Equal(3, scythe.MagazineSlot.Options.Length);
        Assert.All(scythe.MagazineSlot.Options, o => { Assert.Equal("catalog_effects_absent", o.Relationship); Assert.False(o.Resolved); });
        Assert.Empty(mags.Resolved(scythe));
    }

    // ---- Third-person reticle ----
    [Fact] public async Task Player_reticle_is_a_boolean_that_emits_allow_unverified_effect_without_acknowledgement()
    {
        using var e = new TestEnvironment(); var w = await Fresh(e);
        await w.SetWeaponChangeAsync("AR-23 Liberator", FireModes.ReticleField, "false", false);
        Assert.Null(w.BuildError); // allow_unverified_effect is implicit.
        var lua = w.LuaPreview;
        Assert.Contains("hd2.fields.weapon.third_person_reticle", lua); Assert.Contains("expect=true", lua); Assert.Contains("value=false", lua);
        Assert.Contains("allow_unverified_effect=true", lua);
        // Read-only reticles have no control: the write is refused.
        var machete = w.Metadata!.PlayerWeapons!.Field("CQC-42 Machete", FireModes.ReticleField);
        Assert.False(machete.Editable); Assert.False(string.IsNullOrWhiteSpace(machete.Reason));
        await Assert.ThrowsAnyAsync<Exception>(() => w.SetWeaponChangeAsync("CQC-42 Machete", FireModes.ReticleField, "true", false));
    }

    [Fact] public async Task Apw1_support_reticle_is_gameplay_proven_and_needs_no_acknowledgement()
    {
        using var e = new TestEnvironment(); var w = await Fresh(e); var f = Support(w, Apw, FireModes.ReticleField);
        Assert.Null(f.Operation.Acknowledgement); Assert.False(f.Value.Baseline.GetBoolean());
        await w.SetSupportAsync(f.InstanceKey, "true"); Assert.Null(w.BuildError);
        var lua = w.LuaPreview;
        Assert.Contains("hd2.support_weapon('APW-1 Anti-Materiel Rifle')", lua); Assert.Contains("field=hd2.fields.weapon.third_person_reticle", lua);
        Assert.Contains("value=true", lua); Assert.DoesNotContain("allow_unverified_effect", lua);
        Assert.Equal(14, w.Metadata!.SupportAuthoring!.FieldInstances.Count(x => x.SemanticFieldId == FireModes.ReticleField));
    }

    // ---- Fire modes ----
    [Fact] public async Task Jar5_fire_modes_resolve_and_a_mode_list_change_generates_an_ordered_list()
    {
        using var e = new TestEnvironment(); var w = await Fresh(e);
        var entry = w.Metadata!.FireModes!.Find("player", Jar)!;
        Assert.True(entry.Writable); Assert.Equal(["single", "burst"], entry.NamedModes); Assert.Equal(4, entry.MaxModes);
        await w.SetWeaponChangeAsync(Jar, FireModes.ModesField, "[\"single\",\"burst\",\"automatic\"]", false);
        Assert.Null(w.BuildError); // allow_unverified_effect is implicit.
        var lua = w.LuaPreview;
        Assert.Contains("hd2.fields.fire_mode.modes", lua); Assert.Contains("{'single','burst','automatic'}", lua); Assert.Contains("expect={'single','burst'}", lua);
        Assert.Contains("allow_unverified_effect=true", lua);
        // More than the native four slots, an unknown mode or a duplicate are refused.
        await Assert.ThrowsAnyAsync<Exception>(() => w.SetWeaponChangeAsync(Jar, FireModes.ModesField, "[\"single\",\"burst\",\"automatic\",\"single\"]", false));
        await Assert.ThrowsAnyAsync<Exception>(() => w.SetWeaponChangeAsync(Jar, FireModes.ModesField, "[\"single\",\"full_auto\"]", false));
    }

    [Fact] public async Task Blocked_fire_modes_publish_their_reason_and_have_no_writable_field()
    {
        using var e = new TestEnvironment(); var w = await Fresh(e);
        var scythe = w.Metadata!.FireModes!.Find("player", "LAS-5 Scythe")!; var maxigun = w.Metadata.FireModes.Find("support", Maxigun)!;
        Assert.False(scythe.Writable); Assert.Contains("beam", scythe.Reason); Assert.False(maxigun.Writable);
        Assert.DoesNotContain(w.Metadata.PlayerWeapons!.Weapons.Single(x => x.Name == "LAS-5 Scythe").Fields, c => c.SemanticFieldId == FireModes.ModesField && c.Editable);
        await Assert.ThrowsAnyAsync<Exception>(() => w.SetWeaponChangeAsync("LAS-5 Scythe", FireModes.ModesField, "[\"single\"]", false));
    }

    // ---- Mission uses ----
    [Fact] public async Task Exosuit_finite_to_unlimited_is_gameplay_proven()
    {
        using var e = new TestEnvironment(); var w = await Fresh(e); var f = Uses(w, "EXO-45 Patriot Exosuit");
        Assert.Equal(3, f.CurrentDefault.GetInt32());
        await w.SetStratagemAsync(f.InstanceKey, "\"unlimited\""); Assert.Null(w.BuildError);
        var lua = w.LuaPreview;
        Assert.Contains("field=hd2.fields.stratagem.max_uses", lua); Assert.Contains("expect=3", lua); Assert.Contains("value='unlimited'", lua);
        Assert.DoesNotContain("allow_unverified_effect", lua); Assert.DoesNotContain("4294967295", lua);
        // finite -> finite is published but not gameplay-proven.
        await w.SetStratagemAsync(f.InstanceKey, "5"); Assert.Null(w.BuildError); // the opt-in is implicit Assert.Contains("allow_unverified_effect=true", w.LuaPreview);
    }

    [Fact] public async Task Frv_unlimited_to_finite_emits_allow_unverified_effect_without_acknowledgement_and_unpublished_transitions_are_refused()
    {
        using var e = new TestEnvironment(); var w = await Fresh(e); var f = Uses(w, "M-102 Gunner FRV");
        Assert.True(StratagemUses.IsUnlimited(f.CurrentDefault)); Assert.Equal([StratagemUses.UnlimitedToFinite], f.Transitions);
        await w.SetStratagemAsync(f.InstanceKey, "2"); Assert.Null(w.BuildError); // the opt-in is implicit
        var lua = w.LuaPreview; Assert.Contains("expect='unlimited'", lua); Assert.Contains("value=2", lua); Assert.Contains("allow_unverified_effect=true", lua);
        await Assert.ThrowsAsync<InvalidDataException>(() => w.SetStratagemAsync(f.InstanceKey, "0"));
        await Assert.ThrowsAsync<InvalidDataException>(() => w.SetStratagemAsync(f.InstanceKey, "101"));
    }

    [Fact] public async Task Eagle_max_uses_is_excluded()
    {
        using var e = new TestEnvironment(); var w = await Fresh(e); var f = Uses(w, "Eagle 500kg Bomb");
        Assert.False(f.Editable);
        await Assert.ThrowsAsync<InvalidDataException>(() => w.SetStratagemAsync(f.InstanceKey, "\"unlimited\""));
    }

    // ---- Backpack-fed support ammunition ----
    [Theory]
    [InlineData(Maxigun, "M-1000 Maxigun Backpack", 1000)]
    [InlineData(Cremator, "B/FLAM-80 Cremator Backpack", 500)]
    [InlineData(Gl28, "GL-28 Belt-Fed Grenade Launcher Backpack", 120)]
    public async Task Backpack_fed_support_weapons_write_their_backpack_deposit(string weapon, string backpack, int capacity)
    {
        using var e = new TestEnvironment(); var w = await Fresh(e);
        var link = w.Metadata!.SupportAuthoring!.Weapons.Single(x => x.Name == weapon).AmmoBackpack!;
        Assert.Equal(backpack, link.Backpack); Assert.False(link.WeaponOwnsMagazine);
        var fields = Entities(w).Backpacks.FieldInstances.Where(f => f.Target.Backpack == backpack && f.UiGroup == Backpack.AmmoGroup).ToArray();
        Assert.Equal(["deposit.capacity", "deposit.refill_amount", "deposit.start_amount"], fields.Select(f => f.SemanticFieldId).Order(StringComparer.Ordinal));
        var cap = fields.Single(f => f.SemanticFieldId == "deposit.capacity"); Assert.Equal(capacity, cap.CurrentDefault.GetInt32());
        await w.SetEntityAsync(cap.InstanceKey, (capacity * 2).ToString(System.Globalization.CultureInfo.InvariantCulture));
        Assert.Null(w.BuildError); // the opt-in is implicit
        var lua = w.LuaPreview;
        Assert.Contains($"target=hd2.support_weapon('{weapon}'):backpack(),", lua); Assert.Contains("hd2.fields.deposit.capacity", lua);
        Assert.Contains($"value={capacity * 2},", lua); Assert.Contains("allow_unverified_effect=true", lua);
        // Weapon-fed backpacks are not separate backpack stratagems.
        Assert.DoesNotContain(Entities(w).CallIns.Values, c => c.Entity == backpack);
    }

    // ---- Mounted vehicle weapons ----
    [Fact] public async Task Frv_gun_writes_a_mount_local_value_through_its_weapon_target()
    {
        using var e = new TestEnvironment(); var w = await Fresh(e); var capacity = VehicleWeapon(w, "M-103 Supply FRV / gun", "weapon.capacity");
        Assert.Equal("gameplay_proven", capacity.Evidence.Tier); Assert.Null(capacity.Acknowledgement); Assert.False(capacity.AllowSharedRequired);
        await w.SetEntityAsync(capacity.InstanceKey, "240"); Assert.Null(w.BuildError);
        var lua = w.LuaPreview;
        Assert.Contains("hd2.vehicle('M-103 Supply FRV'):weapon('gun')", lua); Assert.Contains("hd2.fields.weapon.capacity", lua); Assert.Contains("value=240,", lua);
        Assert.DoesNotContain("allow_shared", lua); Assert.DoesNotContain("allow_unverified_effect", lua);
        var rate = VehicleWeapon(w, "M-103 Supply FRV / gun", "weapon.fire_rate");
        await w.SetEntityAsync(rate.InstanceKey, "700"); Assert.Null(w.BuildError); // the opt-in is implicit
        Assert.Contains("allow_unverified_effect=true", w.LuaPreview);
    }

    [Fact] public async Task Bastion_cannon_and_hmg_are_separate_weapons_and_shared_rows_need_allow_shared()
    {
        using var e = new TestEnvironment(); var w = await Fresh(e); var bastion = Entities(w).VehicleWeapons!.Find("TD-220 Bastion MK XVI")!;
        Assert.Equal(["attach_tank_gun", "attach_tank_gun_mg"], bastion.Mounts.Select(m => m.Label));
        Assert.All(bastion.Mounts, m => Assert.NotNull(m.Weapon)); Assert.Equal("Main cannon", bastion.Mounts[0].Title);
        var cannon = VehicleWeapon(w, "TD-220 Bastion MK XVI / attach_tank_gun", "weapon.capacity");
        var hmg = VehicleWeapon(w, "TD-220 Bastion MK XVI / attach_tank_gun_mg", "weapon.capacity");
        Assert.NotEqual(cannon.OperationGroup, hmg.OperationGroup);
        await w.SetEntityAsync(cannon.InstanceKey, (cannon.CurrentDefault.GetInt32() + 1).ToString(System.Globalization.CultureInfo.InvariantCulture));
        await w.SetEntityAsync(hmg.InstanceKey, (hmg.CurrentDefault.GetInt32() + 10).ToString(System.Globalization.CultureInfo.InvariantCulture));
        await w.SetEntityAcknowledgedAsync([cannon.InstanceKey, hmg.InstanceKey], true); Assert.Null(w.BuildError);
        var lua = w.LuaPreview;
        Assert.Contains("weapon('attach_tank_gun')", lua); Assert.Contains("weapon('attach_tank_gun_mg')", lua);
        // A shared projectile/damage row names its other consumers and needs allow_shared at its group.
        var shared = Entities(w).VehicleWeapons!.FieldInstances.First(f => f.Target.Weapon == "TD-220 Bastion MK XVI / attach_tank_gun" && f.AllowSharedRequired);
        var published = Entities(w).VehicleWeapons!.Published[shared.InstanceKey]; Assert.NotEqual("weapon_local", published.Scope);
        await w.SetEntityAsync(shared.InstanceKey, Bump(shared));
        // Shared approval is implicit: the shared row builds at once and carries allow_shared.
        Assert.Null(w.BuildError); Assert.Contains("allow_shared=true", w.LuaPreview);
    }

    [Fact] public async Task Maelstrom_mounts_sharing_one_weapon_report_each_other()
    {
        using var e = new TestEnvironment(); var w = await Fresh(e); var maelstrom = Entities(w).VehicleWeapons!.Find("TD-110 Maelstrom")!;
        var slot3 = maelstrom.Mounts.Single(m => m.Label == "slot_3"); var slot4 = maelstrom.Mounts.Single(m => m.Label == "slot_4");
        Assert.Equal(["TD-110 Maelstrom / slot_4"], slot3.Weapon!.Ownership.AlsoMountedAt); Assert.Equal(["TD-110 Maelstrom / slot_3"], slot4.Weapon!.Ownership.AlsoMountedAt);
        Assert.Null(maelstrom.Mounts.Single(m => m.Label == "attach_tank_gun_mg").Weapon);
        Assert.False(string.IsNullOrWhiteSpace(maelstrom.Mounts.Single(m => m.Label == "attach_tank_gun_mg").Reason));
        var sharedMounted = Entities(w).VehicleWeapons!.Published.Values.Where(f => f.Scope == "shared_mounted_weapon" && f.Weapon.StartsWith("TD-110", StringComparison.Ordinal)).ToArray();
        Assert.NotEmpty(sharedMounted); Assert.All(sharedMounted, f => Assert.True(f.AllowSharedRequired));
    }

    [Fact] public async Task Exosuit_arms_are_separate_targets_with_their_own_values()
    {
        using var e = new TestEnvironment(); var w = await Fresh(e); var emancipator = Entities(w).VehicleWeapons!.Find("EXO-49 Emancipator Exosuit")!;
        Assert.Equal(["Left arm", "Right arm"], emancipator.Mounts.Select(m => m.Title));
        var left = VehicleWeapon(w, "EXO-49 Emancipator Exosuit / left_gun", "weapon.capacity"); var right = VehicleWeapon(w, "EXO-49 Emancipator Exosuit / right_gun", "weapon.capacity");
        Assert.NotEqual(left.InstanceKey, right.InstanceKey); Assert.NotEqual(left.OperationGroup, right.OperationGroup);
        await w.SetEntityAsync(left.InstanceKey, Bump(left)); await w.SetEntityAsync(right.InstanceKey, Bump(right, 2));
        await w.SetEntityAcknowledgedAsync([left.InstanceKey, right.InstanceKey], true); Assert.Null(w.BuildError);
        var lua = w.LuaPreview;
        Assert.Contains("hd2.vehicle('EXO-49 Emancipator Exosuit'):weapon('left_gun')", lua); Assert.Contains("hd2.vehicle('EXO-49 Emancipator Exosuit'):weapon('right_gun')", lua);
    }

    // ---- Drop-pod payloads ----
    [Fact] public async Task Eat17_rack_is_shared_with_surplus_eat_and_exposes_four_slots_and_a_spawn_count()
    {
        using var e = new TestEnvironment(); var w = await Fresh(e); var pods = Entities(w).Pods!; var rack = pods.Rack(EatRack)!;
        Assert.True(rack.Shared); Assert.Equal(["EAT-17 Expendable Anti-Tank", "Surplus EAT Allocation (granted stratagem)"], rack.Consumers.Select(c => c.Name));
        Assert.Same(rack, pods.ForBooster(Surplus).Single());
        Assert.Same(rack, pods.ForStratagem(w.Metadata!.Stratagems!.Root("EAT-17 Expendable Anti-Tank")!.SemanticId).Single());
        var fields = pods.FieldInstances.Where(f => f.Target.Rack == EatRack).ToArray();
        Assert.Equal(5, fields.Length); Assert.Equal([1, 2, 3, 4], fields.Where(f => f.IsPickup).Select(f => f.Target.Slot!.Value));
        Assert.All(fields, f => Assert.True(f.AllowSharedRequired));
        Assert.Equal(Eat17, fields.Single(f => f.Target.Slot == 1).CurrentDefault.GetString());
        Assert.Equal(PodPayloadReader.Empty, fields.Single(f => f.Target.Slot == 3).CurrentDefault.GetString());
        Assert.DoesNotContain(pods.FieldInstances, f => f.Target.Slot > PodPayloadReader.AuthoredSlots);
    }

    [Fact] public async Task Surplus_eat_payload_edits_generate_slots_spawn_count_and_every_opt_in()
    {
        using var e = new TestEnvironment(); var w = await Fresh(e); var pods = Entities(w).Pods!; var rack = pods.Rack(EatRack)!;
        var spawn = pods.FieldInstances.Single(f => f.InstanceKey == PodPayloadReader.SpawnKey(rack));
        var slot1 = pods.FieldInstances.Single(f => f.InstanceKey == PodPayloadReader.SlotKey(rack, 1));
        var slot2 = pods.FieldInstances.Single(f => f.InstanceKey == PodPayloadReader.SlotKey(rack, 2));
        await w.SetEntityAsync(spawn.InstanceKey, "4");
        await Assert.ThrowsAsync<InvalidDataException>(() => w.SetEntityAsync(spawn.InstanceKey, "5"));
        await w.SetEntityAsync(slot1.InstanceKey, Leveller); await w.SetEntityAsync(slot2.InstanceKey, AmmoWorld);
        // Every opt-in (shared rack, unverified reference and effect) is implicit: the edits build without acknowledgement.
        Assert.Null(w.BuildError);
        var lua = w.LuaPreview;
        Assert.Contains("hd2.pod_rack('EAT-17 Expendable Anti-Tank pod'):slot(1)", lua); Assert.Contains("hd2.pod_rack('EAT-17 Expendable Anti-Tank pod'):slot(2)", lua);
        Assert.Contains($"value=hd2.pickup('{Leveller}')", lua); Assert.Contains($"value=hd2.pickup('{AmmoWorld}')", lua);
        Assert.Contains($"expect=hd2.pickup('{Eat17}')", lua);
        Assert.Contains("hd2.fields.payload.spawn_count", lua); Assert.Contains("value=4,", lua);
        Assert.Equal(2, Count(lua, "allow_unverified_reference=true")); Assert.True(Count(lua, "allow_shared=true") >= 3); Assert.Contains("allow_unverified_effect=true", lua);
        // Package risk: the world ammo box is not resident in this rack; the Leveller's package isn't either; the vanilla EAT is.
        var s2 = rack.Slots[1];
        Assert.False(PodPayloadCatalog.LowPackageRisk(rack, s2, pods.Pickup(AmmoWorld)!)); Assert.True(PodPayloadCatalog.LowPackageRisk(rack, s2, pods.Pickup(Eat17)!));
        Assert.Equal("PROVEN_COMPATIBLE", PodPayloadCatalog.CompatibilityIn(s2, pods.Pickup(Eat17)!));
        Assert.Equal("UNVERIFIED_REFERENCE", PodPayloadCatalog.CompatibilityIn(s2, pods.Pickup(AmmoWorld)!));
        Assert.Equal("SCHEMA_COMPATIBLE", PodPayloadCatalog.CompatibilityIn(s2, pods.Pickup(Leveller)!));
    }

    [Fact] public async Task Pod_slots_can_be_emptied_duplicated_and_restored_without_an_acknowledgement()
    {
        using var e = new TestEnvironment(); var w = await Fresh(e); var pods = Entities(w).Pods!; var rack = pods.Rack("EAT-411 Leveller pod")!;
        var slot1 = pods.FieldInstances.Single(f => f.InstanceKey == PodPayloadReader.SlotKey(rack, 1));
        var slot2 = pods.FieldInstances.Single(f => f.InstanceKey == PodPayloadReader.SlotKey(rack, 2));
        await w.SetEntityAsync(slot1.InstanceKey, PodPayloadReader.Empty); await w.SetEntityAsync(slot2.InstanceKey, rack.Slots[0].Current!.Pickup!);
        await w.SetEntityAcknowledgedAsync([slot1.InstanceKey, slot2.InstanceKey], true); Assert.Null(w.BuildError);
        var lua = w.LuaPreview;
        Assert.Contains("value='empty'", lua); Assert.Contains("expect='empty'", lua); Assert.DoesNotContain("allow_shared", lua);
        Assert.Equal(2, Count(lua, "allow_unverified_reference=true"));
        // Restoring the vanilla occupant is a no-op and leaves no change behind.
        await w.SetEntityAsync(slot1.InstanceKey, rack.Slots[0].Current!.Pickup!);
        Assert.DoesNotContain(w.Project!.EntityChanges, c => c.InstanceKey == slot1.InstanceKey);
        // Anything outside Runtime's pickup catalog is refused.
        await Assert.ThrowsAsync<InvalidDataException>(() => w.SetEntityAsync(slot1.InstanceKey, "pickup/v1/not-published/0000000000000000"));
    }

    [Fact] public async Task Read_only_racks_publish_no_writable_fields()
    {
        using var e = new TestEnvironment(); var w = await Fresh(e); var pods = Entities(w).Pods!;
        foreach (var rack in pods.Racks.Where(r => !r.Writable))
        {
            Assert.False(string.IsNullOrWhiteSpace(rack.Reason));
            Assert.All(pods.FieldInstances.Where(f => f.Target.Rack == rack.Name), f => Assert.False(f.Editable));
        }
    }

    // ---- Mod Options ----
    [Fact] public async Task Mod_options_accept_finite_uses_and_spawn_counts_but_never_pickups_reticles_or_unlimited()
    {
        using var e = new TestEnvironment(); var w = await Fresh(e); var pods = Entities(w).Pods!; var rack = pods.Rack("EAT-411 Leveller pod")!;
        await w.SetEntityAsync(PodPayloadReader.SpawnKey(rack), "3"); await w.SetEntityAsync(PodPayloadReader.SlotKey(rack, 2), Leveller);
        await w.SetStratagemAsync(Uses(w, "EXO-45 Patriot Exosuit").InstanceKey, "5"); await w.SetStratagemAsync(Uses(w, "EXO-49 Emancipator Exosuit").InstanceKey, "\"unlimited\"");
        await w.SetWeaponChangeAsync("AR-23 Liberator", FireModes.ReticleField, "false", false);
        var targets = ModOptionsService.Targets(w.Project!, w.Metadata!);
        var spawn = targets.Single(t => t.Key == ModOptionsService.EntityKey(PodPayloadReader.SpawnKey(rack)));
        Assert.True(spawn.Eligible); Assert.Equal(1, spawn.RangeMin); Assert.Equal(4, spawn.RangeMax);
        Assert.False(targets.Single(t => t.Key == ModOptionsService.EntityKey(PodPayloadReader.SlotKey(rack, 2))).Eligible);
        var finite = targets.Single(t => t.Key == ModOptionsService.StratagemKey(Uses(w, "EXO-45 Patriot Exosuit").InstanceKey));
        Assert.True(finite.Eligible); Assert.True(finite.Integer); Assert.Equal(1, finite.RangeMin); Assert.Equal(100, finite.RangeMax);
        finite.Check(7); Assert.ThrowsAny<Exception>(() => finite.Check(0));
        Assert.False(targets.Single(t => t.Key == ModOptionsService.StratagemKey(Uses(w, "EXO-49 Emancipator Exosuit").InstanceKey)).Eligible);
        Assert.False(targets.Single(t => t.Key == ModOptionsService.WeaponKey("AR-23 Liberator", FireModes.ReticleField)).Eligible);
    }

    // ---- Compatibility ----
    [Fact] public async Task A_0251_project_rebinds_to_0260_with_its_edits_and_lua_intact()
    {
        using var e = new TestEnvironment(); var old = await SdkFixtures.Install(e, "0.25.1"); var w = e.Workspace();
        Assert.False(old.Has026); Assert.Null(old.Entities!.VehicleWeapons); Assert.Null(old.Entities.Pods); Assert.Null(old.FireModes);
        await w.CreateAsync(new("Compat", "Tests", "mods/tests/compat026", "0.1.0"), old);
        await w.SetWeaponChangeAsync("AR-23 Liberator", "weapon.fire_rate", "700", false);
        var gunner = w.Metadata!.Stratagems!.FieldInstances.First(f => f.Target.Stratagem == "M-102 Gunner FRV" && f.Editable && f.Type == "number");
        await w.SetStratagemAsync(gunner.InstanceKey, (gunner.CurrentDefault.GetDouble() + 1).ToString(System.Globalization.CultureInfo.InvariantCulture));
        var format = w.Project!.FormatVersion; var lua = w.LuaPreview; Assert.Null(w.BuildError);
        Assert.True(format < 8);
        await SdkFixtures.Install(e, "0.26.0"); await w.OpenAsync(w.Project.Id);
        Assert.Equal("0.25.1", w.Project!.SdkVersion); Assert.Equal(lua, w.LuaPreview);
        await w.RebindToInstalledSdkAsync();
        Assert.Equal("0.26.0", w.Project.SdkVersion); Assert.Null(w.BuildError); Assert.Equal(format, w.Project.FormatVersion);
        Assert.Equal(lua.Replace("0.25.1", "0.26.0"), w.LuaPreview);
        // A 0.26 edit marks the project as format 8; reopening keeps every edit.
        await w.SetStratagemAsync(Uses(w, "EXO-45 Patriot Exosuit").InstanceKey, "\"unlimited\"");
        Assert.Equal(8, w.Project.FormatVersion);
        var id = w.Project.Id; await w.OpenAsync(id);
        Assert.Equal(2, w.Project!.StratagemChanges.Count); Assert.Single(w.Project.WeaponChanges); Assert.Null(w.BuildError);
    }
}
