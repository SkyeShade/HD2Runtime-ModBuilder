using HD2RuntimeGUI.Core.Metadata;
using HD2RuntimeGUI.Core.Services;
using Xunit;

namespace HD2RuntimeGUI.Tests;

// The Support Weapon editor's presentation traits (the Armory labels panel): it edits HD2Runtime's hd2.fields.presentation.traits, the
// weapon's native armory trait IDs, as one guarded operation per weapon, and never the wiki's trait list. The GR-8 case is the published
// Boomstick mod's edit ({'team_weapon','anti_tank'} -> {'expendable'}).
public sealed class PresentationTraitEditorTests
{
    private const string Gr8 = "GR-8 Recoilless Rifle", Ac8 = "AC-8 Autocannon", Eat411 = "EAT-411 Leveller", Field = "hd2.fields.presentation.traits";
    private static async Task<(BuilderWorkspace W, SdkMetadata Sdk)> Fresh(TestEnvironment e, string resource = "mods/tests/presentation_traits")
    {
        var sdk = await SdkFixtures.Install(e, SdkPin.Version); var w = e.Workspace();
        await w.CreateAsync(new("Presentation Traits", "Tests", resource, "0.1.0"), sdk);
        return (w, sdk);
    }
    // Each registered request, from hd2.ensure( to its closing }) end).
    private static string[] Requests(string lua) => [.. lua.Split("add(function() return ")[1..].Select(r => r[..(r.IndexOf("}) end)", StringComparison.Ordinal) + "}) end)".Length)])];
    // The one presentation.traits request for a support weapon (fails when there is none or more than one).
    private static string Traits(string lua, string weapon) => Assert.Single(Requests(lua), r => r.Contains("target=hd2.support_weapon('" + weapon + "')", StringComparison.Ordinal) && r.Contains(Field, StringComparison.Ordinal));
    private static string Flat(string s) => System.Text.RegularExpressions.Regex.Replace(s, @"\s+", " ");

    [Fact] public async Task The_GR8_traits_become_expendable_in_one_guarded_operation()
    {
        using var e = new TestEnvironment(); var (w, _) = await Fresh(e);
        var view = w.Presentation(CompositionKind.Support, Gr8)!.Traits!;
        Assert.True(view.Writable && view.Available); Assert.Equal(Field, view.ApiFieldConstant); Assert.Equal(["team_weapon", "anti_tank"], view.Baseline);
        await w.SetPresentationAsync(CompositionKind.Support, Gr8, ["expendable"], null);
        Assert.Null(w.BuildError);
        // Exactly this request: guarded by the native value, with the opt-in the SDK requires for the field (never shared, so no allow_shared).
        Assert.Equal("""
            hd2.ensure({
                patch={
                    id='support-81eb93077cb035ea91a161dc',
                    target=hd2.support_weapon('GR-8 Recoilless Rifle'),
                    allow_unverified_effect=true,
                    field=hd2.fields.presentation.traits,
                    expect={'team_weapon','anti_tank'},
                    value={'expendable'},
                }
            }) end)
            """.ReplaceLineEndings("\n"), Traits(w.LuaPreview, Gr8));
        Assert.Single(Requests(w.LuaPreview), r => r.Contains("hd2.support_weapon('" + Gr8 + "')", StringComparison.Ordinal));
        Assert.Equal(Field.Replace("hd2.fields.", ""), Assert.Single(w.Project!.SupportChanges).SemanticFieldId);
        // Deterministic: the same Lua when regenerated and after reopening.
        var lua = w.LuaPreview; await w.OpenAsync(w.Project.Id); Assert.Equal(lua, w.LuaPreview);
    }

    [Fact] public async Task A_weapon_with_several_traits_keeps_every_selected_trait_in_order()
    {
        using var e = new TestEnvironment(); var (w, _) = await Fresh(e);
        Assert.Equal(["team_weapon", "rounds_reload", "light_anti_tank"], w.Presentation(CompositionKind.Support, Ac8)!.Traits!.Baseline);
        await w.SetPresentationAsync(CompositionKind.Support, Ac8, ["team_weapon", "rounds_reload", "light_anti_tank", "heat"], null);
        Assert.Contains("expect={'team_weapon','rounds_reload','light_anti_tank'}, value={'team_weapon','rounds_reload','light_anti_tank','heat'},", Flat(Traits(w.LuaPreview, Ac8)));
        // Any combination, in the order chosen.
        await w.SetPresentationAsync(CompositionKind.Support, Ac8, ["guided", "team_weapon", "stun", "explosive", "heat"], null);
        Assert.Contains("value={'guided','team_weapon','stun','explosive','heat'},", Flat(Traits(w.LuaPreview, Ac8)));
        Assert.Equal(["guided", "team_weapon", "stun", "explosive", "heat"], w.Presentation(CompositionKind.Support, Ac8)!.Traits!.Current);
    }

    [Fact] public async Task Removing_and_adding_one_trait_keeps_the_native_expect_and_one_operation()
    {
        using var e = new TestEnvironment(); var (w, _) = await Fresh(e);
        await w.SetPresentationAsync(CompositionKind.Support, Gr8, ["anti_tank"], null);
        Assert.Contains("expect={'team_weapon','anti_tank'}, value={'anti_tank'},", Flat(Traits(w.LuaPreview, Gr8)));
        await w.SetPresentationAsync(CompositionKind.Support, Gr8, ["anti_tank", "stun"], null);
        Assert.Contains("expect={'team_weapon','anti_tank'}, value={'anti_tank','stun'},", Flat(Traits(w.LuaPreview, Gr8)));
        Assert.Single(w.Project!.SupportChanges);
    }

    [Fact] public async Task The_native_trait_set_is_no_edit()
    {
        using var e = new TestEnvironment(); var (w, _) = await Fresh(e);
        await w.SetPresentationAsync(CompositionKind.Support, Gr8, ["team_weapon", "anti_tank"], null);
        Assert.Empty(w.Project!.SupportChanges); Assert.DoesNotContain(Field, w.LuaPreview);
        // Back to the native set after an edit removes the edit.
        await w.SetPresentationAsync(CompositionKind.Support, Gr8, ["expendable"], null); Assert.Single(w.Project.SupportChanges);
        await w.SetPresentationAsync(CompositionKind.Support, Gr8, ["team_weapon", "anti_tank"], null);
        Assert.Empty(w.Project.SupportChanges); Assert.DoesNotContain(Field, w.LuaPreview);
    }

    [Fact] public async Task The_editable_traits_are_the_native_catalog_not_the_wiki_list()
    {
        using var e = new TestEnvironment(); var (w, sdk) = await Fresh(e);
        var view = w.Presentation(CompositionKind.Support, Gr8)!.Traits!;
        // Choices are exactly the published native trait IDs, each with its published label; the baseline is the weapon's native record.
        Assert.Equal(sdk.Presentation!.Traits.Keys.Order(StringComparer.Ordinal), view.Choices.Order(StringComparer.Ordinal));
        Assert.Equal(sdk.SupportAuthoring!.FieldInstances.Single(f => f.SupportWeapon == Gr8 && f.SemanticFieldId == "presentation.traits").Value.Baseline.EnumerateArray().Select(x => x.GetString()), view.Baseline);
        var wiki = sdk.Advanced!.Support.Weapons[Gr8].Traits;
        Assert.Equal(["Support Weapon", "Backpack", "Anti-Tank", "Stationary Reload"], wiki);
        Assert.All(wiki, t => { Assert.DoesNotContain(t, view.Choices); Assert.DoesNotContain(t, view.Baseline); });
        Assert.Equal(["TEAM WEAPON", "ANTI-TANK"], view.Baseline.Select(sdk.Presentation.TraitLabel));
        // Editing the traits leaves the wiki list as published; a wiki label is not a trait ID.
        await w.SetPresentationAsync(CompositionKind.Support, Gr8, ["expendable"], null);
        Assert.Equal(wiki, w.Metadata!.Advanced!.Support.Weapons[Gr8].Traits);
        await Assert.ThrowsAsync<InvalidDataException>(() => w.SetPresentationAsync(CompositionKind.Support, Gr8, ["Anti-Tank"], null));
    }

    [Fact] public async Task Trait_edits_keep_the_existing_guards()
    {
        using var e = new TestEnvironment(); var (w, _) = await Fresh(e);
        // The penetration label lives in the same five slots: with the traits edited it is folded into the list, never a second write.
        await w.SetPresentationAsync(CompositionKind.Support, Gr8, ["expendable"], null);
        await w.SetPresentationAsync(CompositionKind.Support, Gr8, null, "heavy");
        Assert.Contains("value={'expendable','heavy_armor_penetrating'},", Flat(Traits(w.LuaPreview, Gr8)));
        Assert.DoesNotContain("presentation.armor_penetration", w.LuaPreview);
        // Unknown, duplicate and sixth traits are refused; the field is not shared, so no allow_shared is ever written for it.
        await Assert.ThrowsAsync<InvalidDataException>(() => w.SetPresentationAsync(CompositionKind.Support, Gr8, ["not_a_trait"], null));
        await Assert.ThrowsAsync<InvalidDataException>(() => w.SetPresentationAsync(CompositionKind.Support, Gr8, ["stun", "stun"], null));
        await Assert.ThrowsAsync<InvalidDataException>(() => w.SetPresentationAsync(CompositionKind.Support, Gr8, ["stun", "heat", "beam", "arc", "guided", "melee"], null));
        Assert.DoesNotContain("allow_shared", Traits(w.LuaPreview, Gr8));
        Assert.Contains("value={'expendable','heavy_armor_penetrating'},", Flat(Traits(w.LuaPreview, Gr8)));
    }

    [Fact] public async Task A_trait_field_Runtime_publishes_only_as_blocked_is_unavailable_with_its_reason()
    {
        using var e = new TestEnvironment(); var (w, sdk) = await Fresh(e);
        var reason = sdk.SupportAuthoring!.Weapons.Single(x => x.Name == Eat411).BlockedFields.Single(b => b.Field == "presentation.traits").Reason;
        var view = w.Presentation(CompositionKind.Support, Eat411)!;
        Assert.False(view.Traits!.Available); Assert.False(view.Traits.Writable); Assert.Equal(reason, view.Traits.Reason); Assert.Empty(view.Traits.Current);
        Assert.True(view.Penetration!.Blocked);
        Assert.Equal(reason, (await Assert.ThrowsAsync<InvalidDataException>(() => w.SetPresentationAsync(CompositionKind.Support, Eat411, ["expendable"], null))).Message);
        Assert.Empty(w.Project!.SupportChanges);
    }

    [Fact] public async Task Older_SDK_projects_keep_their_binding()
    {
        using var e = new TestEnvironment();
        // SDK 0.27.0 publishes no presentation traits: nothing to edit, and the project stays on 0.27.0.
        var old = await SdkFixtures.Install(e, "0.27.0"); var w0 = e.Workspace();
        await w0.CreateAsync(new("Traits 0.27.0", "Tests", "mods/tests/traits_0270", "0.1.0"), old);
        Assert.Null(w0.Presentation(CompositionKind.Support, Gr8));
        await Assert.ThrowsAsync<InvalidDataException>(() => w0.SetPresentationAsync(CompositionKind.Support, Gr8, ["expendable"], null));
        Assert.Equal("0.27.0", w0.Project!.SdkVersion);
        // A project bound to SDK 0.28.0 edits the trait field on 0.28.0 and stays bound to it.
        var sdk028 = await SdkFixtures.Install(e, "0.28.0"); var w = e.Workspace();
        await w.CreateAsync(new("Traits 0.28.0", "Tests", "mods/tests/traits_0280", "0.1.0"), sdk028);
        await w.SetPresentationAsync(CompositionKind.Support, Gr8, ["expendable"], null);
        Assert.Equal("0.28.0", w.Project!.SdkVersion); Assert.Equal("0.28.0", (await e.Store.LoadAsync(w.Project.Id)).SdkVersion);
        Assert.Contains("expect={'team_weapon','anti_tank'}, value={'expendable'},", Flat(Traits(w.LuaPreview, Gr8)));
    }
}
