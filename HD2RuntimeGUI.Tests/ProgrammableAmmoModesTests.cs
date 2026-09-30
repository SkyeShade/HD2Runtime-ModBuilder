using System.Text.RegularExpressions;
using HD2RuntimeGUI.Core.Generation;
using HD2RuntimeGUI.Core.Localization;
using HD2RuntimeGUI.Core.Metadata;
using HD2RuntimeGUI.Core.Models;
using HD2RuntimeGUI.Core.Services;
using Xunit;

namespace HD2RuntimeGUI.Tests;

// ModBuilder 1.4.0 programmable-ammunition editor as base and alternate modes (BuilderWorkspace.AmmoModes, ProgrammableAmmoEditor), on the
// pinned HD2Runtime 0.28.0 SDK: what each mode card shows and offers for player and support weapons, the add / choose / label / reopen /
// remove round trip, native programmable weapons, the input that switches the alternate mode, where each part's opt-ins are shown, and
// projects saved before the editor was redesigned. The edits go through SetSelectorAsync exactly as the card makes them, so the generated
// Lua is the weapon_selector transaction it always was.
public sealed class ProgrammableAmmoModesTests
{
    private const string Liberator = "AR-23 Liberator", Tenderizer = "AR-61 Tenderizer", MissilePistol = "P-33 Missile Pistol";
    private const string Hmg = "MG-206 Heavy Machine Gun", Autocannon = "AC-8 Autocannon", Apw = "APW-1 Anti-Materiel Rifle";
    private const string Out = "output/v1/projectile/";
    private const string Concussive = Out + "ar-23c-liberator-concussive", Hyena = Out + "r-4-hyena", DeEscalator = Out + "gl-52-de-escalator", Scorcher = Out + "plas-1-scorcher";
    private static Task<(BuilderWorkspace W, Core.Metadata.SdkMetadata Sdk)> Fresh(TestEnvironment e) => ExportFixtureTests.Fresh(e, "Ammo Modes");
    private static AmmoModes Modes(BuilderWorkspace w, string kind, string weapon) => w.AmmoModes(kind, weapon) ?? throw new InvalidOperationException(weapon);
    // The generated request that writes one field (the ensure(...) block containing it).
    private static string Operation(string lua, string text) => Regex.Split(lua, @"(?=add\(function\(\) return )").Single(b => b.Contains(text, StringComparison.Ordinal));
    private static void Input(AmmoModes modes, string state, string? input, params string[] choices)
    { Assert.Equal(state, modes.Input.State); Assert.Equal(input, modes.Input.Input); Assert.Equal(choices, modes.Input.Choices); }
    private static int Operations(string lua) => Regex.Matches(lua, @"^add\(function\(\) return ", RegexOptions.Multiline).Count;

    [Fact] public async Task A_player_weapon_starts_with_its_vanilla_base_mode_and_an_empty_alternate_it_can_add()
    {
        using var e = new TestEnvironment(); var (w, _) = await Fresh(e);
        var modes = Modes(w, CompositionKind.Player, Liberator);
        Assert.True(modes.Base.Vanilla); Assert.Equal(Out + "ar-23-liberator", modes.Base.Row!.SemanticId); Assert.Equal(modes.Base.Own, modes.Base.Row);
        Assert.Equal(AlternateAmmoMode.Absent, modes.Alternate.Mode); Assert.False(modes.Alternate.Configured);
        Assert.True(modes.CanAdd); Assert.False(modes.CanRemove); Assert.False(modes.CanRestoreNative); Assert.Null(modes.Reason);
        Input(modes, AmmoSelectorInput.Pending, "left", "left");
        Assert.Empty(modes.OptIns.Flags);
        // The weapon's own row is its base, never a donor; the donor list is the SDK's (live-proven pairs first).
        Assert.DoesNotContain(modes.Donors, d => d.Output.SemanticId == modes.Base.Row.SemanticId);
        Assert.Contains(modes.Donors, d => d.Output.SemanticId == Concussive);
        Assert.Equal(0, Operations(w.LuaPreview));
    }

    [Fact] public async Task A_support_weapon_uses_the_same_two_modes()
    {
        using var e = new TestEnvironment(); var (w, _) = await Fresh(e);
        var modes = Modes(w, CompositionKind.Support, Hmg);
        Assert.True(modes.Base.Vanilla); Assert.Equal(Out + "mg-206-heavy-machine-gun", modes.Base.Row!.SemanticId);
        Assert.Equal(AlternateAmmoMode.Absent, modes.Alternate.Mode); Assert.True(modes.CanAdd);
        Assert.Equal(AmmoSelectorInput.Pending, modes.Input.State); Assert.Equal("left", modes.Input.Input);
        // The MG-206's own row is shared by six entities: its label editor warns about it.
        Assert.True(modes.Base.Row.Presentation!.Shared);
    }

    [Fact] public async Task Adding_choosing_saving_and_removing_an_alternate_mode_round_trips_to_the_base_only_project()
    {
        using var e = new TestEnvironment(); var (w, _) = await Fresh(e);
        var empty = w.LuaPreview;
        // Choosing a donor in the alternate card: no rate edit, no rate binding, the donor, the free input it binds.
        await w.SetSelectorAsync(CompositionKind.Player, Liberator, new(null, null, Concussive, "left"));
        Assert.Null(w.BuildError);
        var modes = Modes(w, CompositionKind.Player, Liberator);
        Assert.Equal(AlternateAmmoMode.DonorMode, modes.Alternate.Mode); Assert.Equal(Concussive, modes.Alternate.Row!.SemanticId); Assert.Equal(Concussive, modes.Alternate.Donor!.Output.SemanticId);
        Assert.True(modes.CanRemove); Assert.False(modes.CanAdd); Assert.False(modes.CanRestoreNative);
        Assert.Equal(AmmoSelectorInput.Bound, modes.Input.State); Assert.Equal("left", modes.Input.Input);
        Assert.True(modes.Base.Vanilla);
        var op = Operation(w.LuaPreview, "hd2.fields.function_ammo.projectile");
        Assert.Contains("target=hd2.weapon('AR-23 Liberator')", op);
        Assert.Matches(@"field=hd2\.fields\.function_ammo\.projectile,expect=[^\n]*,value=hd2\.attack_output\('" + Regex.Escape(Concussive) + @"'\)", op);
        Assert.Contains("{field=hd2.fields.weapon_function.left,expect='none',value='programmable_ammo'}", op);
        Assert.Equal(1, Operations(w.LuaPreview));

        // Saved and reopened: the same alternate mode, binding and Lua.
        var lua = w.LuaPreview; await w.OpenAsync(w.Project!.Id);
        Assert.Equal(lua, w.LuaPreview);
        modes = Modes(w, CompositionKind.Player, Liberator);
        Assert.Equal(Concussive, modes.Alternate.Token); Assert.Equal(AmmoSelectorInput.Bound, modes.Input.State);

        // Remove alternate mode ('none'): the projectile and its binding go together, and the project is back to base only.
        await w.SetSelectorAsync(CompositionKind.Player, Liberator, new(null, null, FunctionProjectile.None, null));
        Assert.Null(w.BuildError);
        modes = Modes(w, CompositionKind.Player, Liberator);
        Assert.Equal(AlternateAmmoMode.Absent, modes.Alternate.Mode); Assert.True(modes.CanAdd); Assert.Equal(AmmoSelectorInput.Pending, modes.Input.State);
        Assert.Empty(w.Project!.WeaponChanges); Assert.Equal(empty, w.LuaPreview);
    }

    [Fact] public async Task Each_mode_owns_its_label_and_icon_and_removing_the_alternate_can_take_its_row_labels_with_it()
    {
        using var e = new TestEnvironment(); var (w, _) = await Fresh(e);
        await w.SetSelectorAsync(CompositionKind.Player, Liberator, new(null, null, Concussive, "left"));
        var modes = Modes(w, CompositionKind.Player, Liberator);
        // Base card: the Liberator's row. Alternate card: the donor's row (both shared rows: the label warning is allow_shared).
        await w.SetRowAsync(modes.Base.Row!.SemanticId, OutputRowChangeService.ModeIcon, ModePresentation.Auto);
        await w.SetRowAsync(modes.Alternate.Row!.SemanticId, OutputRowChangeService.ModeLabel, "flak");
        await w.SetRowAsync(modes.Alternate.Row.SemanticId, OutputRowChangeService.ModeIcon, ModePresentation.Auto);
        Assert.Null(w.BuildError);
        Assert.Single(w.ModePresentationChanges(modes.Base.Row.SemanticId)); Assert.Equal(2, w.ModePresentationChanges(Concussive).Count);
        var baseOp = Operation(w.LuaPreview, "hd2.attack_output('" + Out + "ar-23-liberator')");
        Assert.Contains("hd2.fields.presentation.mode_icon", baseOp); Assert.Contains("allow_shared=true", baseOp); Assert.DoesNotContain("mode_label", baseOp);
        var altOp = Operation(w.LuaPreview, "field=hd2.fields.presentation.mode_label");
        Assert.Contains("hd2.attack_output('" + Concussive + "')", altOp); Assert.Contains("value='flak'", altOp);
        Assert.Contains("allow_shared=true", altOp); Assert.Contains("allow_unverified_effect=true", altOp);
        // The programmable projectile write itself never needs allow_shared: it writes the weapon, not the row.
        Assert.DoesNotContain("allow_shared", Operation(w.LuaPreview, "hd2.fields.function_ammo.projectile"));

        // "Keep the label edits": the alternate goes, its row keeps the label (it is still in the build).
        var project = w.Project!.Id;
        await w.SetSelectorAsync(CompositionKind.Player, Liberator, new(null, null, FunctionProjectile.None, null));
        Assert.DoesNotContain("function_ammo", w.LuaPreview); Assert.Contains("value='flak'", w.LuaPreview);
        // "Remove the label edits too": only the mode label and icon of that row go; the base card's icon stays.
        await w.ResetModePresentationAsync(Concussive);
        Assert.Empty(w.ModePresentationChanges(Concussive)); Assert.DoesNotContain(Concussive, w.LuaPreview);
        Assert.Contains("hd2.fields.presentation.mode_icon", w.LuaPreview); Assert.Single(w.Project!.OutputRowChanges!);
        await w.OpenAsync(project); Assert.Single(w.Project!.OutputRowChanges!);
    }

    [Fact] public async Task A_live_proven_donor_carries_no_opt_in_and_any_other_is_warned_in_the_alternate_card()
    {
        using var e = new TestEnvironment(); var (w, _) = await Fresh(e);
        var modes = Modes(w, CompositionKind.Support, Hmg);
        var hyena = modes.Donors.Single(d => d.Output.SemanticId == Hyena); var deEscalator = modes.Donors.Single(d => d.Output.SemanticId == DeEscalator);
        Assert.True(hyena.LiveProven); Assert.Empty(modes.DonorOptIns(hyena));
        Assert.False(deEscalator.LiveProven); Assert.Equal([CompositionOptIns.Effect, CompositionOptIns.Reference], modes.DonorOptIns(deEscalator));

        await w.SetSelectorAsync(CompositionKind.Support, Hmg, new(null, null, Hyena, "left"));
        modes = Modes(w, CompositionKind.Support, Hmg);
        Assert.True(modes.Alternate.LiveProven); Assert.Empty(modes.OptIns.Flags);
        Assert.DoesNotContain("allow_unverified", Operation(w.LuaPreview, "hd2.fields.function_ammo.projectile"));

        await w.SetSelectorAsync(CompositionKind.Support, Hmg, new(null, null, DeEscalator, "left"));
        modes = Modes(w, CompositionKind.Support, Hmg);
        Assert.False(modes.Alternate.LiveProven);
        Assert.Equal([CompositionOptIns.Effect, CompositionOptIns.Reference], modes.OptIns.Flags);
        Assert.Equal(CoreText.Get("Composition.Selector.ReferenceReason"), modes.OptIns.Reasons[CompositionOptIns.Reference]);
        Assert.False(string.IsNullOrWhiteSpace(modes.OptIns.Reasons[CompositionOptIns.Effect]));
        var op = Operation(w.LuaPreview, "hd2.fields.function_ammo.projectile");
        Assert.Contains("allow_unverified_effect=true", op); Assert.Contains("allow_unverified_reference=true", op);
        // No acknowledgement is ever needed: the build is not blocked.
        Assert.Null(w.BuildError);
    }

    [Fact] public async Task Rates_and_programmable_ammunition_show_their_own_opt_ins_while_the_transaction_carries_both()
    {
        using var e = new TestEnvironment(); var (w, _) = await Fresh(e);
        // The APW-1 has two free inputs: rates bind one, programmable ammunition the other.
        await w.SetSelectorAsync(CompositionKind.Support, Apw, new([100, 250, 400], "right", Hyena, "left"));
        var view = w.Selector(CompositionKind.Support, Apw)!;
        Assert.Equal([CompositionOptIns.Effect], view.RatesOptIns.Flags);
        Assert.Equal([CompositionOptIns.Effect, CompositionOptIns.Reference], view.AmmoOptIns.Flags);
        Assert.Equal([CompositionOptIns.Effect, CompositionOptIns.Reference], view.OptIns);
        Assert.Equal(view.AmmoOptIns.Flags, Modes(w, CompositionKind.Support, Apw).OptIns.Flags);
        var op = Operation(w.LuaPreview, "hd2.support_weapon('APW-1 Anti-Materiel Rifle')");
        Assert.Contains("allow_unverified_effect=true", op); Assert.Contains("allow_unverified_reference=true", op);
        // Without the alternate, only the rates' opt-in remains (and the reference flag leaves the transaction).
        await w.SetSelectorAsync(CompositionKind.Support, Apw, new([100, 250, 400], "right", FunctionProjectile.None, null));
        view = w.Selector(CompositionKind.Support, Apw)!;
        Assert.Empty(view.AmmoOptIns.Flags); Assert.Equal([CompositionOptIns.Effect], view.OptIns);
        Assert.DoesNotContain("allow_unverified_reference", w.LuaPreview);
    }

    [Fact] public async Task The_input_that_switches_the_alternate_mode_follows_the_rate_of_fire_selector()
    {
        using var e = new TestEnvironment(); var (w, _) = await Fresh(e);
        // APW-1: two free inputs to choose from; the alternate mode can be moved to the right one.
        var modes = Modes(w, CompositionKind.Support, Apw);
        Assert.Equal(AmmoSelectorInput.Pending, modes.Input.State); Assert.Equal(["left", "right"], modes.Input.Choices);
        await w.SetSelectorAsync(CompositionKind.Support, Apw, new(null, null, Hyena, "right"));
        modes = Modes(w, CompositionKind.Support, Apw);
        Assert.Equal(AmmoSelectorInput.Bound, modes.Input.State); Assert.Equal("right", modes.Input.Input);
        Assert.Contains("{field=hd2.fields.weapon_function.right,expect='none',value='programmable_ammo'}", w.LuaPreview);

        // Liberator: extra rates take the only input, so choosing a donor has to resolve it (asked in the alternate card).
        await w.SetSelectorAsync(CompositionKind.Player, Liberator, new([450, 700, 950], null, null, null));
        modes = Modes(w, CompositionKind.Player, Liberator);
        Input(modes, AmmoSelectorInput.Held, "left");
        Assert.True(modes.CanAdd);
        // "Use it for the ammunition (keeps only the default rate)": the rates drop to one and the input changes hands in one transaction.
        await w.SetSelectorAsync(CompositionKind.Player, Liberator, new([0, 700, 0], null, Concussive, "left"));
        Assert.Null(w.BuildError);
        modes = Modes(w, CompositionKind.Player, Liberator);
        Assert.Equal(AmmoSelectorInput.Bound, modes.Input.State); Assert.DoesNotContain("rate_of_fire", w.LuaPreview);
    }

    [Fact] public async Task A_native_programmable_weapon_replaces_and_restores_its_alternate_mode_but_never_removes_it()
    {
        using var e = new TestEnvironment(); var (w, _) = await Fresh(e);
        var modes = Modes(w, CompositionKind.Support, Autocannon);
        Assert.Equal(AlternateAmmoMode.NativeMode, modes.Alternate.Mode); Assert.Equal("FLAK", modes.Alternate.Native!.DisplayName); Assert.Equal("ammo_flak", modes.Alternate.Native.Icon);
        Assert.Equal("APHET", modes.Base.Native!.DisplayName); Assert.True(modes.Base.Vanilla);
        Input(modes, AmmoSelectorInput.Native, "left");
        Assert.False(modes.CanAdd); Assert.False(modes.CanRemove); Assert.False(modes.CanRestoreNative);

        await w.SetSelectorAsync(CompositionKind.Support, Autocannon, new(null, null, Scorcher, null));
        modes = Modes(w, CompositionKind.Support, Autocannon);
        Assert.Equal(AlternateAmmoMode.DonorMode, modes.Alternate.Mode); Assert.True(modes.CanRestoreNative); Assert.False(modes.CanRemove);
        Assert.Equal(AmmoSelectorInput.Native, modes.Input.State);
        var op = Operation(w.LuaPreview, "function_ammo");
        Assert.Contains("expect=hd2.support_weapon('AC-8 Autocannon'):feed('programmable'):projectile()", op); Assert.DoesNotContain("weapon_function", op);
        await Assert.ThrowsAsync<InvalidDataException>(() => w.SetSelectorAsync(CompositionKind.Support, Autocannon, new(null, null, FunctionProjectile.None, null)));

        await w.SetSelectorAsync(CompositionKind.Support, Autocannon, new(null, null, FunctionProjectile.Native, null));
        Assert.Equal(AlternateAmmoMode.NativeMode, Modes(w, CompositionKind.Support, Autocannon).Alternate.Mode); Assert.DoesNotContain("function_ammo", w.LuaPreview);
    }

    [Fact] public async Task Read_only_weapons_show_their_modes_with_Runtimes_reason_and_no_actions()
    {
        using var e = new TestEnvironment(); var (w, _) = await Fresh(e);
        // P-33: a native programmable function that spawns an entity (not a projectile reference).
        var pistol = Modes(w, CompositionKind.Player, MissilePistol);
        Assert.False(pistol.Writable); Assert.NotNull(pistol.Reason); Assert.Equal(AlternateAmmoMode.NativeMode, pistol.Alternate.Mode);
        Assert.Equal("Non-Guided", pistol.Alternate.Native!.DisplayName);
        Assert.False(pistol.CanAdd || pistol.CanRemove || pistol.CanRestoreNative); Assert.Empty(pistol.Donors);
        // Tenderizer: no programmable ammunition can be added.
        var tenderizer = Modes(w, CompositionKind.Player, Tenderizer);
        Assert.Equal(AlternateAmmoMode.Absent, tenderizer.Alternate.Mode); Assert.False(tenderizer.CanAdd); Assert.NotNull(tenderizer.Reason);
    }

    [Fact] public async Task A_projectile_swap_changes_what_the_base_mode_fires()
    {
        using var e = new TestEnvironment(); var (w, _) = await Fresh(e);
        var host = w.ProjectileHost(AttackOutputChange.SupportHost, Hmg, "primary", out _)!;
        var donor = w.Metadata!.AttackOutputs!.Donors(host).First(d => d.Allowed && !d.CrossClass).Output;
        await w.SetHostOutputAsync(AttackOutputChange.SupportHost, Hmg, "primary", donor.SemanticId);
        var modes = Modes(w, CompositionKind.Support, Hmg);
        Assert.False(modes.Base.Vanilla); Assert.Equal(donor.SemanticId, modes.Base.Row!.SemanticId); Assert.Equal(Out + "mg-206-heavy-machine-gun", modes.Base.Own!.SemanticId);
        // Disabled, the swap no longer applies: the base mode fires the weapon's own row again.
        await w.ToggleAttackOutputAsync(w.HostOutput(AttackOutputChange.SupportHost, Hmg, "primary")!.Id);
        Assert.True(Modes(w, CompositionKind.Support, Hmg).Base.Vanilla);
    }

    [Fact] public async Task A_project_saved_by_the_previous_release_candidate_opens_with_its_alternate_mode_and_the_same_transaction()
    {
        // Saved by the 1.4.0 RC before the mode cards (runtime028 desktop smoke: MG-206 rates and the live-proven Hyena), with the addon.lua that
        // RC exported for it.
        using var e = new TestEnvironment(); await SdkFixtures.Install(e, SdkPin.Version);
        var folder = Path.Combine(AppContext.BaseDirectory, "Fixtures", "projects-1.4.0-rc");
        var w = e.Workspace(); var project = await e.Store.ImportAsync(Path.Combine(folder, "runtime028-smoke.hd2mod.json"));
        await w.OpenAsync(project.Id);
        Assert.Equal(11, w.Project!.FormatVersion); Assert.Equal(SdkPin.Version, w.Project.SdkVersion);
        var modes = Modes(w, CompositionKind.Support, Hmg);
        Assert.Equal(AlternateAmmoMode.DonorMode, modes.Alternate.Mode); Assert.Equal(Hyena, modes.Alternate.Token); Assert.True(modes.Alternate.LiveProven);
        Input(modes, AmmoSelectorInput.Bound, "left", "left");
        Assert.True(modes.CanRemove); Assert.Empty(modes.OptIns.Flags);
        var exported = File.ReadAllText(Path.Combine(folder, "runtime028-smoke.rc-export.lua")).Replace("\r\n", "\n");
        const string target = "target=hd2.support_weapon('MG-206 Heavy Machine Gun')";
        Assert.Equal(Operation(exported, target).TrimEnd(), Operation(w.LuaPreview.Replace("\r\n", "\n"), target).TrimEnd());
    }

    [Fact] public async Task A_1_3_1_project_rebound_to_the_pinned_SDK_starts_with_base_only_modes()
    {
        using var e = new TestEnvironment(); await SdkFixtures.Install(e, "0.27.0"); await SdkFixtures.Install(e, SdkPin.Version);
        var w = e.Workspace(); var project = await e.Store.ImportAsync(Path.Combine(AppContext.BaseDirectory, "Fixtures", "projects-1.3.1", "P3-support-weapon.hd2mod.json"));
        await w.OpenAsync(project.Id); var lua = w.LuaPreview;
        await w.RebindToInstalledSdkAsync();
        var modes = Modes(w, CompositionKind.Support, Hmg);
        Assert.Equal(AlternateAmmoMode.Absent, modes.Alternate.Mode); Assert.True(modes.CanAdd);
        Assert.DoesNotContain("function_ammo", w.LuaPreview); Assert.DoesNotContain("function_ammo", lua);
    }

    [Fact] public void Player_and_support_weapons_render_the_one_mode_card_editor()
    {
        var components = Path.Combine(Root(), "HD2RuntimeGUI", "Components");
        string Read(string name) => File.ReadAllText(Path.Combine(components, name));
        // One editor, embedded by the weapon-functions panel that both the player and the support editors use.
        Assert.Contains("<ProgrammableAmmoEditor ", Read("WeaponFunctionsPanel.razor"));
        Assert.Contains("<WeaponFunctionsPanel ", Read("PlayerWeaponsEditor.razor")); Assert.Contains("<WeaponFunctionsPanel ", Read("SupportAuthoringEditor.razor"));
        Assert.Single(Directory.GetFiles(components, "*.razor", SearchOption.AllDirectories), f => File.ReadAllText(f).Contains("<ProgrammableAmmoEditor ", StringComparison.Ordinal));
        var editor = Read("ProgrammableAmmoEditor.razor");
        foreach (var hook in new[] { "data-ammo-mode=\"base\"", "data-ammo-mode=\"alternate\"", "data-ammo-add", "data-ammo-remove", "data-function-ammo-native", "data-ammo-donor-picker", "Compact=\"true\"" })
            Assert.Contains(hook, editor);
        // No acknowledgement checkbox anywhere in the editor: opt-ins are warnings only.
        Assert.DoesNotContain("type=\"checkbox\"", editor);
        // The cards sit side by side in a wide panel and stack in a narrow one.
        var css = File.ReadAllText(Path.Combine(Root(), "HD2RuntimeGUI", "wwwroot", "app.css"));
        Assert.Contains(".ammo-modes { container-type:inline-size }", css);
        Assert.Matches(@"@container \(min-width:\d+px\) \{ \.ammo-mode-cards \{ grid-template-columns:minmax\(0, \d+fr\) minmax\(0, \d+fr\) \} \}", css);
    }
    private static string Root()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
            if (Directory.Exists(Path.Combine(dir.FullName, "HD2RuntimeGUI", "Components"))) return dir.FullName;
        throw new DirectoryNotFoundException("Repository root not found.");
    }
}
