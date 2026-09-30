using System.Text.Json;
using System.Text.RegularExpressions;
using HD2RuntimeGUI.Core.Generation;
using HD2RuntimeGUI.Core.Metadata;
using HD2RuntimeGUI.Core.Models;
using HD2RuntimeGUI.Core.Projects;
using HD2RuntimeGUI.Core.Services;
using Xunit;

namespace HD2RuntimeGUI.Tests;

// ModBuilder 1.4.0 weapon composition on the frozen HD2Runtime 0.28.0 SDK: rate-of-fire slots (fire_rate.modes), weapon-function bindings,
// programmable ammunition (function_ammo.projectile), armory presentation (traits / penetration label), typed status references, feeds and
// underbarrels, for player weapons (hd2.weapon) and support weapons (hd2.support_weapon).
public sealed class WeaponCompositionTests
{
    private const string Liberator = "AR-23 Liberator", Concussive = "AR-23C Liberator Concussive", Coyote = "AR-2 Coyote", Tenderizer = "AR-61 Tenderizer",
        Sickle = "LAS-17 Double-Edge Sickle", OneTwo = "AR/GL-21 One-Two", OneTwoLauncher = "AR/GL-21 One-Two / underbarrel", Halt = "SG-20 Halt", Evictor = "GL-15 Evictor";
    private const string Hmg = "MG-206 Heavy Machine Gun", Speargun = "S-11 Speargun", Autocannon = "AC-8 Autocannon", Maxigun = "M-1000 Maxigun", Apw = "APW-1 Anti-Materiel Rifle",
        Laser = "LAS-98 Laser Cannon", MG43 = "MG-43 Machine Gun";
    private const string Hyena = "output/v1/projectile/r-4-hyena", Pacifier = "output/v1/projectile/ar-32-pacifier", ReEducator = "output/v1/projectile/p-35-re-educator",
        SpareTwin = "output/v1/projectile/s-11-speargun-spare-twin", EmsMortar = "output/v1/projectile/a-m-23-ems-mortar-sentry", Scorcher = "output/v1/projectile/plas-1-scorcher",
        DeEscalator = "output/v1/projectile/gl-52-de-escalator";
    private static string[] Ids(string lua) => Regex.Matches(lua, @"\bid='([^']+)'").Select(m => m.Groups[1].Value).ToArray();
    private static SupportField Support(SdkMetadata sdk, string weapon, string field, string? role = null) => sdk.SupportAuthoring!.FieldInstances
        .Single(f => f.SupportWeapon == weapon && f.SemanticFieldId == field && f.Target.AttackRole == role && (role != null || f.Target.Path == "weapon"));
    // The generated request that writes one field (the ensure(...) block containing it).
    private static string Operation(string lua, string field)
    {
        var blocks = Regex.Split(lua, @"(?=add\(function\(\) return )");
        return blocks.Single(b => b.Contains(field, StringComparison.Ordinal));
    }
    // One field's write in a request, as a patch (field=, expect=, value= lines) or as one of a transaction's changes.
    private static void Writes(string op, string field, string expect, string value) =>
        Assert.True(op.Contains($"{{field={field},expect={expect},value={value}}}", StringComparison.Ordinal)
            || Regex.IsMatch(op, $@"field={Regex.Escape(field)},\s*expect={Regex.Escape(expect)},\s*value={Regex.Escape(value)},"), op);

    // ---- rate-of-fire modes -------------------------------------------------------------------------------------------------------------

    [Fact] public async Task Adding_rates_to_the_Liberator_binds_its_selector_in_one_live_proven_transaction()
    {
        using var e = new TestEnvironment(); var (w, sdk) = await EnemyAuthoringTests.Fresh(e);
        var view = w.Selector(CompositionKind.Player, Liberator)!;
        Assert.Equal("addable", view.Rates!.State); Assert.False(view.Rates.SelectorBound); Assert.Equal(["left"], view.Candidates(WeaponFunctions.RateOfFire));
        Assert.Equal([0f, 640f, 0f], view.Rates.Baseline); Assert.Equal(["x", "y", "z"], view.Rates.SlotNames); Assert.Equal("y", view.Rates.DefaultSlot);
        Assert.Contains("Recoil Spring Liberator", view.Rates.OverriddenWhenEquipped);
        await w.SetSelectorAsync(CompositionKind.Player, Liberator, new([450, 700, 950], null, null, null));
        Assert.Null(w.BuildError);
        var op = Operation(w.LuaPreview, "hd2.fields.fire_rate.modes");
        Assert.Contains("target=hd2.weapon('AR-23 Liberator')", op);
        Assert.Contains("{field=hd2.fields.fire_rate.modes,expect={0,640,0},value={450,700,950}}", op);
        Assert.Contains("{field=hd2.fields.weapon_function.left,expect='none',value='rate_of_fire'}", op);
        // AddedFireRateModeTest proved exactly this scope: no acknowledgement.
        Assert.DoesNotContain("allow_unverified", op);
        Assert.Single(Regex.Matches(w.LuaPreview, "hd2.fields.weapon_function.left"));
        Assert.Equal(Ids(w.LuaPreview).Length, Ids(w.LuaPreview).Distinct().Count());
        Assert.Equal(11, w.Project!.FormatVersion);
        // Selector order from the default: y (700) -> z (950) -> x (450).
        Assert.Equal(["y", "z", "x"], FireRateModes.SelectorOrder(w.Selector(CompositionKind.Player, Liberator)!.Rates!.Current));
        // Saved and reopened, the same transaction is generated.
        var lua = w.LuaPreview; await w.OpenAsync(w.Project.Id); Assert.Null(w.BuildError); Assert.Equal(lua, w.LuaPreview);
        // Back to one rate: the binding goes with the extra rates.
        await w.SetSelectorAsync(CompositionKind.Player, Liberator, new([0, 700, 0], null, null, null));
        Assert.DoesNotContain("weapon_function", w.LuaPreview); Assert.Contains("value={0,700,0}", w.LuaPreview);
    }

    [Fact] public async Task Rates_on_other_weapons_carry_the_effect_opt_in_and_refused_rates_are_never_saved()
    {
        using var e = new TestEnvironment(); var (w, sdk) = await EnemyAuthoringTests.Fresh(e);
        await w.SetSelectorAsync(CompositionKind.Player, Coyote, new([500, 750, 900], null, null, null));
        var op = Operation(w.LuaPreview, "hd2.fields.fire_rate.modes");
        Assert.Contains("allow_unverified_effect=true", op); Assert.Contains("value='rate_of_fire'", op);
        // The default slot is never empty, slots stay within the published range, and a selector needs its rates.
        await Assert.ThrowsAsync<InvalidDataException>(() => w.SetSelectorAsync(CompositionKind.Player, Coyote, new([500, 0, 900], null, null, null)));
        await Assert.ThrowsAsync<InvalidDataException>(() => w.SetSelectorAsync(CompositionKind.Player, Coyote, new([500, 750, 99999], null, null, null)));
        await Assert.ThrowsAsync<InvalidDataException>(() => w.SetSelectorAsync(CompositionKind.Player, Coyote, new([-5, 750, 900], null, null, null)));
        Assert.Contains("value={500,750,900}", w.LuaPreview);
        // A binding without rates is Runtime's SELECTOR_REQUIRED: refused, never saved.
        var binding = WeaponChangeService.Catalog(sdk).Field(Coyote, "weapon_function.left");
        Assert.Throws<InvalidDataException>(() => WeaponSelectorRules.Problem(Coyote, [0, 640, 0], false, null, false, new Dictionary<string, string> { ["left"] = "rate_of_fire" }) is { } p ? throw new InvalidDataException(p) : 0);
        Assert.Equal(WeaponCapability.WeaponFunction, binding.Type);
    }

    [Fact] public async Task A_native_selector_edits_its_rates_alone_and_the_older_fire_rate_folds_into_the_default_slot()
    {
        using var e = new TestEnvironment(); var (w, sdk) = await EnemyAuthoringTests.Fresh(e);
        var view = w.Selector(CompositionKind.Player, Tenderizer)!;
        Assert.Equal("selectable", view.Rates!.State); Assert.True(view.Rates.SelectorBound); Assert.Equal("left", view.Rates.SelectorInput);
        await w.SetSelectorAsync(CompositionKind.Player, Tenderizer, new([0, 650, 900], null, null, null));
        var op = Operation(w.LuaPreview, "hd2.fields.fire_rate.modes");
        Assert.Contains("field=hd2.fields.fire_rate.modes,\n", op); Assert.DoesNotContain("weapon_function", op); Assert.Contains("allow_unverified_effect=true", op);
        // weapon.fire_rate covers the Y slot: editing it now writes the rate modes' default slot instead of a second, conflicting write.
        await w.SetWeaponChangeAsync(Tenderizer, FireRateModes.LegacyField, "700", false);
        Assert.Null(w.BuildError); Assert.DoesNotContain("hd2.fields.weapon.fire_rate", w.LuaPreview); Assert.Contains("value={0,700,900}", w.LuaPreview);
    }

    // ---- programmable ammunition ------------------------------------------------------------------------------------------------------

    [Fact] public async Task The_HMG_special_ammunition_pairs_are_live_proven_and_written_with_their_binding()
    {
        using var e = new TestEnvironment(); var (w, sdk) = await EnemyAuthoringTests.Fresh(e);
        var view = w.Selector(CompositionKind.Support, Hmg)!;
        Assert.Equal("addable", view.Projectile!.State); Assert.Equal(FunctionProjectile.None, view.Projectile.Baseline);
        Assert.Equal(["left"], view.Candidates(WeaponFunctions.ProgrammableAmmo)); Assert.Empty(view.Candidates(WeaponFunctions.RateOfFire));
        Assert.All([Hyena, Pacifier, ReEducator], id => Assert.True(view.Projectile.Donors.Single(d => d.Output.SemanticId == id).LiveProven));
        Assert.Equal("output/v1/projectile/mg-206-heavy-machine-gun", view.Projectile.Base!.SemanticId);
        await w.SetSelectorAsync(CompositionKind.Support, Hmg, new(null, null, Hyena, null));
        Assert.Null(w.BuildError);
        var op = Operation(w.LuaPreview, "hd2.fields.function_ammo.projectile");
        Assert.Contains("target=hd2.support_weapon('MG-206 Heavy Machine Gun')", op);
        Assert.Contains("{field=hd2.fields.function_ammo.projectile,expect='none',value=hd2.attack_output('output/v1/projectile/r-4-hyena')}", op);
        Assert.Contains("{field=hd2.fields.weapon_function.left,expect='none',value='programmable_ammo'}", op);
        Assert.DoesNotContain("allow_unverified", op);
        // The HMG's native rate selector (right input) is edited in the same transaction.
        await w.SetSelectorAsync(CompositionKind.Support, Hmg, new([450, 600, 900], null, Hyena, null));
        op = Operation(w.LuaPreview, "hd2.fields.function_ammo.projectile");
        Assert.Contains("hd2.fields.fire_rate.modes,expect={450,600,750},value={450,600,900}", op); Assert.DoesNotContain("allow_unverified", op);
        Assert.Single(Regex.Matches(w.LuaPreview, "hd2.fields.fire_rate.modes")); Assert.Equal(Ids(w.LuaPreview).Length, Ids(w.LuaPreview).Distinct().Count());
        // Any other donor needs both opt-ins.
        await w.SetSelectorAsync(CompositionKind.Support, Hmg, new(null, null, DeEscalator, null));
        op = Operation(w.LuaPreview, "hd2.fields.function_ammo.projectile");
        Assert.Contains("allow_unverified_effect=true", op); Assert.Contains("allow_unverified_reference=true", op);
        Assert.Equal(11, w.Project!.FormatVersion);
        var lua = w.LuaPreview; await w.OpenAsync(w.Project.Id); Assert.Null(w.BuildError); Assert.Equal(lua, w.LuaPreview);
    }

    [Fact] public async Task The_Speargun_takes_its_spare_twin_or_the_EMS_Mortar_shell_only_as_a_function_projectile()
    {
        using var e = new TestEnvironment(); var (w, sdk) = await EnemyAuthoringTests.Fresh(e);
        var view = w.Selector(CompositionKind.Support, Speargun)!;
        Assert.True(view.Projectile!.Donors.Single(d => d.Output.SemanticId == SpareTwin).LiveProven);
        Assert.False(view.Projectile.Donors.Single(d => d.Output.SemanticId == EmsMortar).LiveProven);
        await w.SetSelectorAsync(CompositionKind.Support, Speargun, new(null, null, SpareTwin, null));
        Assert.DoesNotContain("allow_unverified", Operation(w.LuaPreview, "function_ammo"));
        await w.SetSelectorAsync(CompositionKind.Support, Speargun, new(null, null, EmsMortar, null));
        var op = Operation(w.LuaPreview, "function_ammo");
        Assert.Contains("value=hd2.attack_output('output/v1/projectile/a-m-23-ems-mortar-sentry')", op);
        Assert.Contains("allow_unverified_effect=true", op); Assert.Contains("allow_unverified_reference=true", op);
        // A scoped row is never offered as an attack's projectile.
        Assert.DoesNotContain(sdk.AttackOutputs!.Outputs.Where(o => o.AttackReferenceAllowed), o => o.SemanticId is SpareTwin or EmsMortar);
    }

    [Fact] public async Task A_native_programmable_host_swaps_and_restores_its_own_function_projectile()
    {
        using var e = new TestEnvironment(); var (w, sdk) = await EnemyAuthoringTests.Fresh(e);
        var view = w.Selector(CompositionKind.Support, Autocannon)!;
        Assert.Equal("native", view.Projectile!.State); Assert.Equal(FunctionProjectile.Native, view.Projectile.Baseline); Assert.True(view.Projectile.SelectorBound);
        await w.SetSelectorAsync(CompositionKind.Support, Autocannon, new(null, null, Scorcher, null));
        var op = Operation(w.LuaPreview, "function_ammo");
        Assert.Contains("expect=hd2.support_weapon('AC-8 Autocannon'):feed('programmable'):projectile()", op);
        Assert.Contains("value=hd2.attack_output('output/v1/projectile/plas-1-scorcher')", op);
        Assert.DoesNotContain("weapon_function", op);
        // A native function projectile cannot be removed ('none'), only restored.
        await Assert.ThrowsAsync<InvalidDataException>(() => w.SetSelectorAsync(CompositionKind.Support, Autocannon, new(null, null, FunctionProjectile.None, null)));
        await w.SetSelectorAsync(CompositionKind.Support, Autocannon, new(null, null, FunctionProjectile.Native, null));
        Assert.DoesNotContain("function_ammo", w.LuaPreview);
    }

    [Fact] public async Task Two_selectors_competing_for_one_input_are_resolved_where_the_edit_happens()
    {
        using var e = new TestEnvironment(); var (w, _) = await EnemyAuthoringTests.Fresh(e);
        // The Liberator has one free input: rates and programmable ammunition cannot both have it.
        await w.SetSelectorAsync(CompositionKind.Player, Liberator, new([450, 700, 950], null, null, null));
        await Assert.ThrowsAsync<InvalidDataException>(() => w.SetSelectorAsync(CompositionKind.Player, Liberator, new([450, 700, 950], "left", Hyena, "left")));
        Assert.Contains("value='rate_of_fire'", w.LuaPreview); Assert.DoesNotContain("function_ammo", w.LuaPreview);
        // The APW-1 has two free inputs: each selector takes its own, in one transaction.
        await w.SetSelectorAsync(CompositionKind.Support, Apw, new([100, 250, 400], "right", Hyena, null));
        var op = Operation(w.LuaPreview, "hd2.support_weapon('APW-1 Anti-Materiel Rifle')");
        Assert.Contains("{field=hd2.fields.weapon_function.right,expect='none',value='rate_of_fire'}", op);
        Assert.Contains("{field=hd2.fields.weapon_function.left,expect='none',value='programmable_ammo'}", op);
        Assert.Single(Regex.Matches(w.LuaPreview, Regex.Escape("hd2.support_weapon('APW-1 Anti-Materiel Rifle')")));
        // Resetting the rates keeps the programmable ammunition and its binding.
        var rates = Support(w.Metadata!, Apw, FireRateModes.Field);
        await w.ResetSupportAsync(instance: rates.InstanceKey);
        Assert.Null(w.BuildError); op = Operation(w.LuaPreview, "hd2.support_weapon('APW-1 Anti-Materiel Rifle')");
        Assert.DoesNotContain("rate_of_fire", op); Assert.DoesNotContain("fire_rate", op); Assert.Contains("value='programmable_ammo'", op);
        // The group is enabled and disabled as one transaction.
        var ammo = Support(w.Metadata!, Apw, FunctionProjectile.Field);
        await w.ToggleSupportAsync(ammo.InstanceKey);
        Assert.All(w.Project!.SupportChanges.Where(c => c.Weapon == Apw), c => Assert.False(c.Enabled)); Assert.Null(w.BuildError);
        Assert.DoesNotContain("APW-1", w.LuaPreview);
        await w.ToggleSupportAsync(Support(w.Metadata!, Apw, "weapon_function.left").InstanceKey);
        Assert.All(w.Project!.SupportChanges.Where(c => c.Weapon == Apw), c => Assert.True(c.Enabled)); Assert.Contains("APW-1", w.LuaPreview);
    }

    [Fact] public async Task A_player_selector_group_is_toggled_and_reset_together()
    {
        using var e = new TestEnvironment(); var (w, _) = await EnemyAuthoringTests.Fresh(e);
        await w.SetSelectorAsync(CompositionKind.Player, Evictor, new([200, 300, 400], "right", null, null));
        Assert.Contains("{field=hd2.fields.weapon_function.right,expect='none',value='rate_of_fire'}", Operation(w.LuaPreview, "fire_rate.modes"));
        // Disabling one field of the group disables the whole transaction (never a binding without its rates).
        var binding = w.Project!.WeaponChanges.Single(c => c.SemanticFieldId == "weapon_function.right");
        await w.ToggleWeaponChangeAsync(binding.Id);
        Assert.All(w.Project.WeaponChanges.Where(c => c.Weapon == Evictor), c => Assert.False(c.Enabled)); Assert.Null(w.BuildError);
        Assert.False(w.Selector(CompositionKind.Player, Evictor)!.Enabled);
        await w.ToggleSelectorGroupAsync(CompositionKind.Player, Evictor);
        Assert.All(w.Project.WeaponChanges.Where(c => c.Weapon == Evictor), c => Assert.True(c.Enabled));
        // Resetting the binding resets the rates it selects.
        await w.ResetWeaponsAsync(Evictor, "weapon_function.right");
        Assert.DoesNotContain(w.Project.WeaponChanges, c => c.Weapon == Evictor); Assert.Null(w.BuildError);
        // Statuses that share a published name are told apart by their key.
        Assert.NotEqual(w.StatusLabel("gas"), w.StatusLabel("gas_2")); Assert.Equal("Fire", w.StatusLabel("fire"));
    }

    [Fact] public async Task A_saved_selector_that_Runtime_would_refuse_blocks_the_build_with_its_reason()
    {
        using var e = new TestEnvironment(); var (w, sdk) = await EnemyAuthoringTests.Fresh(e);
        await w.SetSelectorAsync(CompositionKind.Player, Coyote, new([500, 750, 900], null, null, null));
        // A project edited outside ModBuilder that lost the binding: SELECTOR_REQUIRED is reported instead of generating a refused write.
        w.Project!.WeaponChanges.RemoveAll(c => c.SemanticFieldId == "weapon_function.left");
        Assert.Contains(w.WeaponIssues, i => i.Message.Contains(Coyote));
        Assert.Throws<InvalidDataException>(() => new LuaGenerator(new ChangeService()).Generate(w.Project, w.Metadata!));
    }

    // ---- presentation -------------------------------------------------------------------------------------------------------------------

    [Fact] public async Task Armory_labels_are_presentation_only_and_the_Concussive_labels_are_live_proven()
    {
        using var e = new TestEnvironment(); var (w, sdk) = await EnemyAuthoringTests.Fresh(e);
        var view = w.Presentation(CompositionKind.Player, Concussive)!;
        Assert.Equal("light", view.Penetration!.Baseline); Assert.Contains("anti_tank", view.Penetration.Choices);
        Assert.Contains("gameplay", view.GameplaySeparation, StringComparison.OrdinalIgnoreCase); Assert.False(string.IsNullOrWhiteSpace(view.Refresh));
        await w.SetPresentationAsync(CompositionKind.Player, Concussive, null, "medium");
        var op = Operation(w.LuaPreview, "presentation.armor_penetration");
        Assert.Contains("target=hd2.weapon('AR-23C Liberator Concussive')", op); Assert.Contains("expect='light'", op); Assert.Contains("value='medium'", op);
        Assert.DoesNotContain("allow_unverified", op); // WeaponPresentationTest: light, medium and heavy on the Concussive
        await w.SetPresentationAsync(CompositionKind.Player, Concussive, null, "anti_tank");
        Assert.Contains("allow_unverified_effect=true", Operation(w.LuaPreview, "presentation.armor_penetration"));
        // Editing the traits takes the label edit into the list (the same five slots are never written twice).
        var traits = w.Presentation(CompositionKind.Player, Concussive)!.Traits!;
        Assert.Contains("anti_tank", traits.Current);
        await w.SetPresentationAsync(CompositionKind.Player, Concussive, [.. traits.Current, "stun"], null);
        Assert.DoesNotContain("presentation.armor_penetration", w.LuaPreview);
        op = Operation(w.LuaPreview, "presentation.traits");
        Assert.Contains("value={'anti_tank','stun'}", op); Assert.Contains("allow_unverified_effect=true", op);
        // And a later label edit is applied to the edited list.
        await w.SetPresentationAsync(CompositionKind.Player, Concussive, null, "heavy");
        Assert.Contains("value={'heavy_armor_penetrating','stun'}", Operation(w.LuaPreview, "presentation.traits"));
        // Unknown, duplicate and sixth traits are refused.
        await Assert.ThrowsAsync<InvalidDataException>(() => w.SetPresentationAsync(CompositionKind.Player, Concussive, ["made_up"], null));
        await Assert.ThrowsAsync<InvalidDataException>(() => w.SetPresentationAsync(CompositionKind.Player, Concussive, ["stun", "stun"], null));
        await Assert.ThrowsAsync<InvalidDataException>(() => w.SetPresentationAsync(CompositionKind.Player, Concussive, ["stun", "heat", "beam", "arc", "guided", "melee"], null));
        Assert.Equal(11, w.Project!.FormatVersion);
    }

    [Fact] public async Task Support_weapons_edit_their_armory_traits_with_the_published_trait_ids()
    {
        using var e = new TestEnvironment(); var (w, sdk) = await EnemyAuthoringTests.Fresh(e);
        var view = w.Presentation(CompositionKind.Support, "40-K Meltagun")!;
        Assert.Equal(["anti_tank"], view.Traits!.Baseline); Assert.Equal(sdk.Presentation!.Traits.Count, view.Traits.Choices.Count);
        await w.SetPresentationAsync(CompositionKind.Support, "40-K Meltagun", ["anti_tank", "heat"], null);
        var op = Operation(w.LuaPreview, "presentation.traits");
        Assert.Contains("target=hd2.support_weapon('40-K Meltagun')", op); Writes(op, "hd2.fields.presentation.traits", "{'anti_tank'}", "{'anti_tank','heat'}");
        Assert.Contains("allow_unverified_effect=true", op);
        await Assert.ThrowsAsync<InvalidDataException>(() => w.SetPresentationAsync(CompositionKind.Support, "40-K Meltagun", ["not_a_trait"], null));
        await w.SetPresentationAsync(CompositionKind.Support, "40-K Meltagun", null, "heavy");
        Assert.Contains("value={'heavy_armor_penetrating','heat'}", Operation(w.LuaPreview, "presentation.traits"));
        var lua = w.LuaPreview; await w.OpenAsync(w.Project!.Id); Assert.Equal(lua, w.LuaPreview);
    }

    // ---- status references --------------------------------------------------------------------------------------------------------------

    [Fact] public async Task Player_status_references_are_authored_on_their_projectile_and_heat_levels()
    {
        using var e = new TestEnvironment(); var (w, sdk) = await EnemyAuthoringTests.Fresh(e);
        // LiberatorFireStatus: the direct-hit row is live-proven (no effect opt-in); the row is shared, so allow_shared stays.
        await w.SetObjectScalarAsync(Liberator, "primary", "projectile", null, "damage.status_1_type", "\"fire\"", true);
        await w.SetObjectScalarAsync(Liberator, "primary", "projectile", null, "damage.status_1_strength", "2", true);
        Assert.Null(w.BuildError);
        var op = Operation(w.LuaPreview, "damage.status_1_type");
        Assert.Contains("target=hd2.weapon('AR-23 Liberator'):attack('primary'):projectile()", op);
        Assert.Contains("{field=hd2.fields.damage.status_1_type,expect='none',value='fire'}", op);
        Assert.Contains("{field=hd2.fields.damage.status_1_strength,expect=0,value=2}", op);
        Assert.Contains("allow_shared=true", op); Assert.DoesNotContain("allow_unverified_effect", op);
        // Only attachable statuses (and the slot's own) are accepted.
        await Assert.ThrowsAsync<InvalidDataException>(() => w.SetObjectScalarAsync(Liberator, "primary", "projectile", null, "damage.status_1_type", "\"acid\"", true));
        // LAS-17 heat levels: a self status per level, its threshold and the overheat lock, with Runtime's effect opt-in.
        var heat = sdk.PlayerWeapons!.Weapon(Sickle).Fields.Single(f => f.SemanticFieldId == "heat.level_2_self_status");
        Assert.True(heat.Editable); Assert.Equal(WeaponCapability.StatusReference, heat.Type); Assert.Contains("hotshot_laser_rifle_3", w.StatusChoices(heat).Allowed);
        await w.SetWeaponChangeAsync(Sickle, "heat.level_2_self_status", "\"hotshot_laser_rifle_3\"", false);
        await w.SetWeaponChangeAsync(Sickle, "heat.level_2_threshold", "120", false);
        await w.SetWeaponChangeAsync(Sickle, "heat.overheat_lock", "true", false);
        Assert.Null(w.BuildError);
        op = Operation(w.LuaPreview, "heat.level_2_self_status");
        Assert.Contains("target=hd2.weapon('LAS-17 Double-Edge Sickle')", op); Assert.Contains("allow_unverified_effect=true", op);
        Assert.Contains("{field=hd2.fields.heat.level_2_self_status,expect='hotshot_laser_rifle_2',value='hotshot_laser_rifle_3'}", op);
        Assert.Contains("hd2.fields.heat.level_2_threshold", w.LuaPreview); Assert.Contains("hd2.fields.heat.overheat_lock", w.LuaPreview);
        Assert.Equal(11, w.Project!.FormatVersion);
        var lua = w.LuaPreview; await w.OpenAsync(w.Project.Id); Assert.Null(w.BuildError); Assert.Equal(lua, w.LuaPreview);
    }

    [Fact] public async Task Support_status_references_accept_attachable_statuses_and_keep_the_slots_packed()
    {
        using var e = new TestEnvironment(); var (w, sdk) = await EnemyAuthoringTests.Fresh(e);
        // MaxigunStun: live-proven direct-hit row.
        var type = Support(sdk, Maxigun, "damage.status_1_type", "primary"); var strength = Support(sdk, Maxigun, "damage.status_1_strength", "primary");
        Assert.True(type.Writable); Assert.Contains("stun_medium", w.StatusChoices(type).Allowed); Assert.True(w.StatusChoices(type).AllowNone);
        await w.SetSupportAsync(type.InstanceKey, "\"stun_medium\""); await w.SetSupportAsync(strength.InstanceKey, "2");
        Assert.Null(w.BuildError);
        var op = Operation(w.LuaPreview, "damage.status_1_type");
        Assert.Contains("target=hd2.support_weapon('M-1000 Maxigun'):attack('primary'):projectile()", op);
        Assert.Contains("{field=hd2.fields.damage.status_1_type,expect='none',value='stun_medium'}", op);
        Assert.Contains("allow_shared=true", op); Assert.DoesNotContain("allow_unverified_effect", op);
        await Assert.ThrowsAsync<InvalidDataException>(() => w.SetSupportAsync(type.InstanceKey, "\"electric\""));
        // A flamethrower row: its statuses stay packed, so only the last used slot may be cleared.
        var first = Support(sdk, "FLAM-40 Flamethrower", "damage.status_1_type", "primary"); var last = Support(sdk, "FLAM-40 Flamethrower", "damage.status_3_type", "primary");
        Assert.False(w.StatusChoices(first).AllowNone); Assert.True(w.StatusChoices(last).AllowNone); Assert.Contains("fire", w.StatusChoices(first).Allowed);
        await Assert.ThrowsAsync<InvalidDataException>(() => w.SetSupportAsync(first.InstanceKey, "\"none\""));
        await w.SetSupportAsync(last.InstanceKey, "\"none\"");
        op = Operation(w.LuaPreview, "damage.status_3_type");
        Writes(op, "hd2.fields.damage.status_3_type", "'fire_panic'", "'none'"); Assert.Contains("allow_unverified_effect=true", op);
        Assert.Equal(11, w.Project!.FormatVersion);
    }

    // ---- underbarrels and feeds -------------------------------------------------------------------------------------------------------

    [Fact] public async Task The_One_Two_launcher_is_edited_as_its_own_target_through_its_parent()
    {
        using var e = new TestEnvironment(); var (w, sdk) = await EnemyAuthoringTests.Fresh(e);
        var subs = w.Subweapons(OneTwo);
        var (info, target) = Assert.Single(subs);
        Assert.Equal(OneTwoLauncher, info.Name); Assert.Equal(Subweapon.Underbarrel, info.Kind); Assert.NotEmpty(info.NotExposed!);
        Assert.DoesNotContain(sdk.PlayerWeapons!.Weapons, x => x.Name == OneTwoLauncher);
        Assert.Contains(target.Fields, f => f.SemanticFieldId == "rounds.spare_rounds" && f.Editable);
        await w.SetWeaponChangeAsync(OneTwoLauncher, "rounds.spare_rounds", "10", false);
        await w.SetWeaponChangeAsync(OneTwoLauncher, "weapon.horizontal_spread", "15", false);
        Assert.Null(w.BuildError);
        var change = w.Project!.WeaponChanges.Single(c => c.SemanticFieldId == "rounds.spare_rounds");
        Assert.Equal(OneTwoLauncher, change.Weapon); Assert.Equal(Subweapon.Underbarrel, change.Subweapon);
        var op = Operation(w.LuaPreview, "hd2.fields.rounds.spare_rounds");
        Assert.Contains("target=hd2.weapon('AR/GL-21 One-Two'):underbarrel()", op);
        Assert.Contains("field=hd2.fields.rounds.spare_rounds,\n", op); Assert.Contains("expect=5,\n", op); Assert.Contains("value=10,\n", op);
        Assert.Contains("allow_unverified_effect=true", op);
        Assert.Contains("hd2.weapon('AR/GL-21 One-Two'):underbarrel()", Operation(w.LuaPreview, "hd2.fields.weapon.horizontal_spread"));
        // The parent weapon's own spread is a different target.
        await w.SetWeaponChangeAsync(OneTwo, "weapon.horizontal_spread", "20", false);
        Assert.Contains("target=hd2.weapon('AR/GL-21 One-Two'),", w.LuaPreview);
        Assert.Equal(3, w.WeaponSummary(OneTwo).ModifiedFields);
        Assert.Equal(11, w.Project.FormatVersion);
        var json = await File.ReadAllTextAsync(e.Paths.ProjectFile(w.Project.Id)); Assert.Contains("\"subweapon\": \"underbarrel\"", json);
        var lua = w.LuaPreview; await w.OpenAsync(w.Project.Id); Assert.Null(w.BuildError); Assert.Equal(lua, w.LuaPreview);
        // Resetting the parent weapon resets its underbarrel too.
        await w.ResetWeaponsAsync(OneTwo);
        Assert.Empty(w.Project!.WeaponChanges);
    }

    [Fact] public async Task The_Halt_feeds_are_published_per_feed_and_edited_through_their_own_fields()
    {
        using var e = new TestEnvironment(); var (w, sdk) = await EnemyAuthoringTests.Fresh(e);
        var feeds = sdk.Feeds!.Of(CompositionKind.Player, Halt)!;
        Assert.Equal(["primary", "alternate"], feeds.Feeds.Select(f => f.Id));
        Assert.All(feeds.Feeds, f => { Assert.Equal("rounds_magazine", f.Mechanism); Assert.Equal("magazine", f.Selector!.Function); Assert.True(f.Selector.Bound); });
        Assert.Equal(["rounds.feed_capacity_1", "rounds.feed_capacity_2"], feeds.Feeds.Select(f => f.CapacityField));
        // HaltFeedTest: 4 flechettes and 12 stun rounds, each feed independent.
        await w.SetWeaponChangeAsync(Halt, "rounds.feed_capacity_1", "4", false);
        await w.SetWeaponChangeAsync(Halt, "rounds.feed_capacity_2", "12", false);
        // The alternate (stun) feed's status is its own branch-qualified damage field.
        await w.SetObjectScalarAsync(Halt, "feed_alternate", "projectile", null, "damage.alternate.status_1_type", "\"stun_medium\"", true);
        Assert.Null(w.BuildError);
        var op = Operation(w.LuaPreview, "rounds.feed_capacity_1");
        Assert.Contains("{field=hd2.fields.rounds.feed_capacity_1,expect=8,value=4}", op); Assert.Contains("{field=hd2.fields.rounds.feed_capacity_2,expect=8,value=12}", op);
        op = Operation(w.LuaPreview, "status_1_type");
        Assert.Contains("hd2.weapon('SG-20 Halt'):attack('feed_alternate'):projectile()", op);
        Writes(op, "hd2.fields.damage.alternate_status_1_type", "'stun_large'", "'stun_medium'");
        // The Halt's single penetration label is read-only (one per feed): Runtime's reason is kept, and traits are edited instead.
        var presentation = w.Presentation(CompositionKind.Player, Halt)!;
        Assert.False(presentation.Penetration!.Writable); Assert.False(string.IsNullOrWhiteSpace(presentation.Penetration.Reason)); Assert.True(presentation.Traits!.Writable);
        // Its feed projectiles are overridden by the default customization: read-only with Runtime's reason.
        Assert.All(feeds.Feeds, f => Assert.Contains("customization", f.ProjectileSource!.Value.GetProperty("reason").GetString()));
    }

    // ---- catalog, formats and compatibility --------------------------------------------------------------------------------------------

    [Fact] public async Task Every_published_composition_type_is_authored_and_read_only_fields_keep_their_reason()
    {
        using var e = new TestEnvironment(); var (_, sdk) = await EnemyAuthoringTests.Fresh(e);
        var player = sdk.PlayerWeapons!.Weapons.SelectMany(w => w.Fields).Concat(sdk.PlayerWeapons.Subweapons!.SelectMany(s => s.Fields)).ToArray();
        foreach (var type in AuthoredTypes.Composition)
        {
            Assert.Contains(player, f => f.Type == type && f.Editable);
            Assert.DoesNotContain(player, f => f.Type == type && f.Reason == AuthoredTypes.NotAuthoredReason);
        }
        Assert.All(player.Where(f => AuthoredTypes.Composition.Contains(f.Type) && !f.Editable), f => Assert.False(string.IsNullOrWhiteSpace(f.Reason)));
        var support = sdk.SupportAuthoring!.FieldInstances;
        // (Support projectile-host swaps, attack.projectile, are not part of this slice and stay read-only with the not-authored reason.)
        Assert.All(support.Where(f => AuthoredTypes.Composition.Contains(f.Value.Type)), f => Assert.NotEqual(AuthoredTypes.NotAuthoredReason, f.BlockedReason));
        foreach (var type in AuthoredTypes.Composition) Assert.Contains(support, f => f.Value.Type == type && f.Writable);
        // Other new support fields are ordinary scalars: recoil multipliers, beam fire rate and stationary firing.
        Assert.Contains(support, f => f.SupportWeapon == Maxigun && f.SemanticFieldId == "weapon.recoil_multiplier_horizontal" && f.Writable);
        Assert.Contains(support, f => f.SupportWeapon == Laser && f.SemanticFieldId == "beam.fire_rate" && f.Writable);
        Assert.Contains(support, f => f.SemanticFieldId == "weapon.stationary_while_firing" && f.Writable);
    }

    [Fact] public async Task New_scalar_fields_generate_with_their_opt_ins()
    {
        using var e = new TestEnvironment(); var (w, sdk) = await EnemyAuthoringTests.Fresh(e);
        await w.SetSupportAsync(Support(sdk, Maxigun, "weapon.recoil_multiplier_horizontal").InstanceKey, "0.5");
        await w.SetSupportAsync(Support(sdk, Laser, "beam.fire_rate").InstanceKey, "120");
        await w.SetSupportAsync(Support(sdk, MG43, "weapon.stationary_while_firing").InstanceKey, "true");
        Assert.Null(w.BuildError);
        Assert.Contains("allow_unverified_effect=true", Operation(w.LuaPreview, "hd2.fields.weapon.recoil_multiplier_horizontal"));
        Assert.Contains("value=120", Operation(w.LuaPreview, "hd2.fields.beam.fire_rate"));
        Assert.Contains("value=true", Operation(w.LuaPreview, "hd2.fields.weapon.stationary_while_firing"));
        // None of these is a 1.4.0 value type: the project keeps its earlier format.
        Assert.True(w.Project!.FormatVersion < 11);
    }

    [Fact] public async Task Projects_without_composition_edits_keep_their_format_and_1_3_projects_still_open()
    {
        using var e = new TestEnvironment(); var (w, sdk) = await EnemyAuthoringTests.Fresh(e);
        await w.SetWeaponChangeAsync(Liberator, "weapon.ergonomics", "70", false);
        var format = w.Project!.FormatVersion; Assert.True(format < 11);
        var json = await File.ReadAllTextAsync(e.Paths.ProjectFile(w.Project.Id)); Assert.DoesNotContain("subweapon", json);
        Assert.Equal(format, ProjectIdentity.RequiredFormat(w.Project) is var r && r > format ? r : format);
        // A 1.3.x project (format 11, no 1.4.0 fields) opens and builds unchanged.
        using var old = new TestEnvironment(); var published = await SdkFixtures.Install(old, "0.27.0");
        var ow = old.Workspace(); await ow.CreateAsync(new("Old", "Tests", "mods/tests/old", "0.1.0"), published);
        await ow.SetWeaponChangeAsync(Liberator, "weapon.ergonomics", "70", false);
        ow.Project!.FormatVersion = 11; await ow.SaveDetailsAsync("Old", "Tests", "0.1.0", "");
        var oldLua = ow.LuaPreview; Assert.Null(ow.BuildError);
        await ow.OpenAsync(ow.Project.Id); Assert.Equal(oldLua, ow.LuaPreview); Assert.Null(ow.Project!.WeaponChanges.Single().Subweapon);
        // Saved composition values are validated by shape when the project loads.
        var p = w.Project; p.WeaponChanges.Add(new WeaponChange { Weapon = Liberator, SemanticFieldId = FireRateModes.Field, FieldType = WeaponCapability.FireRateSet,
            ExpectedValue = JsonSerializer.SerializeToElement(new[] { 0, 640, 0 }), DesiredValue = JsonSerializer.SerializeToElement(new[] { 1, 2 }), BaselineSdkVersion = sdk.Version });
        Assert.Throws<InvalidDataException>(() => ProjectIdentity.Validate(p));
    }
}
