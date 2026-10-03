using System.IO.Compression;
using System.Text.Json.Nodes;
using HD2RuntimeGUI.Core.Generation;
using HD2RuntimeGUI.Core.Metadata;
using HD2RuntimeGUI.Core.Models;
using HD2RuntimeGUI.Core.Projects;
using HD2RuntimeGUI.Core.Services;
using Xunit;

namespace HD2RuntimeGUI.Tests;

// ModBuilder 1.6.0: an in-game option on a field without a value change. Saving the option adds an option-only edit (the vanilla value, kept
// for its option); only the option changes the value in game. HD2Runtime is unchanged: an option value equal to `expect` is accepted.
public sealed class OptionOnlyEditTests
{
    private const string Liberator = "AR-23 Liberator", MaelstromMissiles = "TD-110 Maelstrom / slot_3", MaelstromMissilesTwin = "TD-110 Maelstrom / slot_4";
    private static async Task<BuilderWorkspace> Workspace(TestEnvironment e, string version = "0.28.1")
    {
        var sdk = await SdkFixtures.Install(e, version); var w = e.Workspace();
        await w.CreateAsync(new("Option Only", "Tests", "mods/tests/option_only", "0.1.0"), sdk); w.Project!.ExportDirectory = e.Paths.Exports;
        await w.SetModOptionsEnabledAsync(true); return w;
    }
    private static EntityField Missile(BuilderWorkspace w, string field = "damage.primary.standard_damage", string weapon = MaelstromMissiles) =>
        w.Metadata!.Entities!.VehicleWeapons!.FieldInstances.First(f => f.Target.Weapon == weapon && f.SemanticFieldId == field);
    private static ModOptionRow Row(BuilderWorkspace w, OptionTarget t, string id) { var row = w.SuggestOptionRow(t); row.Id = id; row.Label = id.Replace('_', ' '); return row; }
    private static int Count(string text, string part) { var n = 0; for (var i = text.IndexOf(part, StringComparison.Ordinal); i >= 0; i = text.IndexOf(part, i + part.Length, StringComparison.Ordinal)) n++; return n; }
    private static async Task<JsonNode> Export(BuilderWorkspace w, string entry)
    {
        await w.ExportAsync(); using var zip = ZipFile.OpenRead(w.LastExport!);
        using var reader = new StreamReader(zip.GetEntry(entry)!.Open()); return JsonNode.Parse(await reader.ReadToEndAsync())!;
    }

    [Fact] public async Task Maelstrom_missile_damage_becomes_an_option_without_a_value_edit()
    {
        using var e = new TestEnvironment(); var w = await Workspace(e);
        var f = Missile(w); var key = ModOptionsService.EntityKey(f.InstanceKey);
        Assert.Equal(1100, f.CurrentDefault.GetInt32()); Assert.Contains("-- No enabled modifications", w.LuaPreview);
        // No edit yet: no saved target, but a candidate whose edit would keep the vanilla value.
        Assert.Null(w.OptionTarget(key));
        var candidate = w.OptionCandidate(key)!;
        Assert.True(candidate.Eligible); Assert.True(candidate.Active); Assert.Equal(1100, candidate.Baseline); Assert.Equal(1100, candidate.Desired);
        var row = Row(w, candidate, "maelstrom_missile_damage");
        Assert.Equal(ModOptionsService.Slider, row.Kind); Assert.Equal(1100, row.Default); Assert.True(row.Min < 1100); Assert.True(row.Max > 1100);
        Assert.Empty(ModOptionsService.RowIssues(w.Project!.ModOptions!, row, candidate));
        await w.SaveOptionRowAsync(row); Assert.Null(w.BuildError);
        // One option-only edit: expected = desired = vanilla, shown as unedited with its option.
        var saved = Assert.Single(w.Project!.EntityChanges);
        Assert.Equal(f.InstanceKey, saved.InstanceKey); Assert.Equal(1100, saved.ExpectedValue.GetInt32()); Assert.Equal(1100, saved.DesiredValue.GetInt32());
        Assert.True(w.OptionOnly(saved)); Assert.True(w.OptionRowActive(Assert.Single(w.Project.ModOptions!.Rows))); Assert.Null(w.OptionCandidate(key));
        var lua = w.LuaPreview;
        Assert.Contains("local option_maelstrom_missile_damage=options:slider({id='maelstrom_missile_damage',label='maelstrom missile damage',min=" + ModOptionsService.Format(row.Min)
            + ",max=" + ModOptionsService.Format(row.Max) + ",step=" + ModOptionsService.Format(row.Step) + ",default=1100})\n", lua);
        Assert.Contains("hd2.ensure({\n    enabled=enabled,\n", lua); Assert.Contains("hd2.vehicle('TD-110 Maelstrom'):weapon('slot_3')", lua);
        Assert.Contains("expect=1100,", lua); Assert.Contains("value=option_maelstrom_missile_damage", lua); Assert.DoesNotContain("value=1100", lua);
        // The missile damage row is shared (the other pod, the MLS-4X Commando): the option carries the same opt-ins as an edit.
        Assert.Contains("allow_shared=true", lua); Assert.Contains("allow_unverified_effect=true", lua);
        Assert.Equal(1, Count(lua, "hd2.ensure("));
        // Format 13 while the project has an option-only edit; save and reopen keep it.
        Assert.Equal(13, w.Project.FormatVersion);
        Assert.Contains("\"formatVersion\": 13", await File.ReadAllTextAsync(e.Paths.ProjectFile(w.Project.Id)));
        await w.OpenAsync(w.Project.Id); Assert.Single(w.Project!.EntityChanges); Assert.Equal(lua, w.LuaPreview);
        Assert.Equal("1.0.0", (string)(await Export(w, "hd2runtime.json"))["optional"]!["mod_options_menu"]!["min_version"]!);
    }

    [Fact] public async Task Stop_exposing_removes_the_option_only_edit_with_its_option()
    {
        using var e = new TestEnvironment(); var w = await Workspace(e);
        var key = ModOptionsService.EntityKey(Missile(w).InstanceKey);
        await w.SaveOptionRowAsync(Row(w, w.OptionCandidate(key)!, "missiles"));
        Assert.Single(w.Project!.EntityChanges);
        await w.RemoveOptionRowAsync(key);
        Assert.Empty(w.Project.EntityChanges); Assert.Empty(w.Project.ModOptions!.Rows); Assert.Contains("-- No enabled modifications", w.LuaPreview);
        Assert.NotNull(w.OptionCandidate(key));
        // An option on an edited field still keeps its edit when removed.
        var f = Missile(w);
        await w.SetEntityAsync(f.InstanceKey, "1200"); await w.SaveOptionRowAsync(Row(w, w.OptionTarget(key)!, "missiles"));
        await w.RemoveOptionRowAsync(key);
        Assert.Equal(1200, Assert.Single(w.Project.EntityChanges).DesiredValue.GetInt32()); Assert.Contains("value=1200", w.LuaPreview);
    }

    [Fact] public async Task Cancelling_or_a_refused_option_adds_no_edit()
    {
        using var e = new TestEnvironment(); var w = await Workspace(e);
        var key = ModOptionsService.EntityKey(Missile(w).InstanceKey); var row = Row(w, w.OptionCandidate(key)!, "missiles"); row.Default = row.Max + row.Step;
        Assert.Contains("between min and max", (await Assert.ThrowsAsync<InvalidDataException>(() => w.SaveOptionRowAsync(row))).Message);
        Assert.Empty(w.Project!.EntityChanges); Assert.Empty(w.Project.ModOptions!.Rows); Assert.True(w.Project.FormatVersion < 13);
    }

    [Fact] public async Task Options_switched_off_keep_option_only_edits_and_generate_nothing()
    {
        using var e = new TestEnvironment(); var w = await Workspace(e);
        var key = ModOptionsService.EntityKey(Missile(w).InstanceKey);
        await w.SaveOptionRowAsync(Row(w, w.OptionCandidate(key)!, "missiles")); var lua = w.LuaPreview;
        await w.SetModOptionsEnabledAsync(false);
        Assert.Single(w.Project!.EntityChanges); Assert.Contains("-- No enabled modifications", w.LuaPreview); Assert.DoesNotContain("hd2.options", w.LuaPreview);
        Assert.Null((await Export(w, "hd2runtime.json"))["optional"]);
        await w.OpenAsync(w.Project.Id); Assert.Single(w.Project!.EntityChanges);
        await w.SetModOptionsEnabledAsync(true); Assert.Equal(lua, w.LuaPreview);
    }

    [Fact] public async Task Vanilla_typed_into_an_exposed_field_keeps_its_option_and_reset_removes_the_edit()
    {
        using var e = new TestEnvironment(); var w = await Workspace(e, "0.25.1");
        var vanilla = w.Metadata!.PlayerWeapons!.FindCanonicalField(Liberator, "weapon.fire_rate")!.CurrentDefault.GetRawText();
        await w.SetWeaponChangeAsync(Liberator, "weapon.fire_rate", "700", false);
        var key = ModOptionsService.WeaponKey(Liberator, "weapon.fire_rate");
        await w.SaveOptionRowAsync(Row(w, w.OptionTarget(key)!, "liberator_rate"));
        // Typing the vanilla value back: the field becomes an option-only edit, still bound.
        await w.SetWeaponChangeAsync(Liberator, "weapon.fire_rate", vanilla, false);
        Assert.True(WeaponScalar.IsNoOp(w.Metadata, Assert.Single(w.Project!.WeaponChanges))); Assert.Null(w.BuildError);
        Assert.Contains("value=option_liberator_rate", w.LuaPreview); Assert.Equal(13, w.Project.FormatVersion);
        await w.OpenAsync(w.Project.Id); Assert.Single(w.Project!.WeaponChanges); Assert.Contains("value=option_liberator_rate", w.LuaPreview);
        // Reset removes the edit and leaves the option row inactive (as for any edit); exposing the field again brings the row back.
        await w.ResetWeaponsAsync(Liberator, "weapon.fire_rate");
        Assert.Empty(w.Project.WeaponChanges); var row = Assert.Single(w.Project.ModOptions!.Rows); Assert.False(w.OptionRowActive(row));
        Assert.DoesNotContain("hd2.options", w.LuaPreview);
        Assert.NotNull(w.OptionCandidate(key)); Assert.Same(row, w.OptionRow(key));
        await w.SaveOptionRowAsync(Row(w, w.OptionCandidate(key)!, "liberator_rate"));
        Assert.Single(w.Project.WeaponChanges); Assert.Contains("value=option_liberator_rate", w.LuaPreview);
    }

    [Fact] public async Task Every_option_domain_can_expose_an_unedited_field()
    {
        using var e = new TestEnvironment(); var w = await Workspace(e); var sdk = w.Metadata!;
        // A player-weapon field, a projectile field, a support-weapon field and a stratagem field, none of them edited.
        var keys = new List<string> { ModOptionsService.WeaponKey(Liberator, "weapon.ergonomics"), ModOptionsService.ObjectKey(Liberator, "primary", "projectile", null, "projectile.velocity") };
        keys.Add(sdk.SupportAuthoring!.FieldInstances.Where(f => f.SupportWeapon == "MG-43 Machine Gun" && f.Writable).Select(f => ModOptionsService.SupportKey(f.InstanceKey))
            .First(k => w.OptionCandidate(k) != null));
        keys.Add(sdk.Stratagems!.FieldInstances.Where(f => f.Target.Stratagem == "Orbital Precision Strike" && f.Editable).Select(f => ModOptionsService.StratagemKey(f.InstanceKey))
            .First(k => w.OptionCandidate(k) != null));
        for (var i = 0; i < keys.Count; i++)
        {
            var candidate = w.OptionCandidate(keys[i]); Assert.NotNull(candidate); Assert.True(candidate.Eligible, keys[i]); Assert.Equal(candidate.Baseline, candidate.Desired);
            await w.SaveOptionRowAsync(Row(w, candidate, "option_" + i)); Assert.Null(w.BuildError);
        }
        var lua = w.LuaPreview;
        for (var i = 0; i < keys.Count; i++) Assert.Contains("value=option_option_" + i, lua);
        Assert.Single(w.Project!.WeaponChanges); Assert.Single(w.Project.CompositionChanges); Assert.Single(w.Project.SupportChanges); Assert.Single(w.Project.StratagemChanges);
        Assert.True(w.OptionOnly(w.Project.CompositionChanges[0])); Assert.True(w.OptionOnly(w.Project.SupportChanges[0])); Assert.True(w.OptionOnly(w.Project.StratagemChanges[0]));
        Assert.True(ModOptionsService.HasOptionOnlyEdits(w.Project, sdk)); Assert.Equal(13, ProjectIdentity.RequiredFormat(w.Project, sdk));
        // Reopening keeps every option-only edit; removing each option removes its edit.
        await w.OpenAsync(w.Project.Id); Assert.Equal(lua, w.LuaPreview);
        foreach (var key in keys) await w.RemoveOptionRowAsync(key);
        Assert.Empty(w.Project!.WeaponChanges); Assert.Empty(w.Project.CompositionChanges); Assert.Empty(w.Project.SupportChanges); Assert.Empty(w.Project.StratagemChanges);
        Assert.Contains("-- No enabled modifications", w.LuaPreview);
    }

    [Fact] public async Task A_mount_sharing_the_exposed_weapon_is_refused_where_it_is_exposed()
    {
        using var e = new TestEnvironment(); var w = await Workspace(e);
        var first = Missile(w, "weapon.capacity"); var second = Missile(w, "weapon.capacity", MaelstromMissilesTwin);
        Assert.Equal(first.BackingObjectId, second.BackingObjectId);
        await w.SaveOptionRowAsync(Row(w, w.OptionCandidate(ModOptionsService.EntityKey(first.InstanceKey))!, "missile_pods"));
        // The two missile pods are one mounted weapon: its magazine is one value with one edit, so the twin's option is refused naming the
        // first pod, in the option editor, and nothing is saved.
        var twin = ModOptionsService.EntityKey(second.InstanceKey);
        var refused = await Assert.ThrowsAsync<InvalidDataException>(() => w.SaveOptionRowAsync(Row(w, w.OptionCandidate(twin)!, "twin")));
        Assert.Contains("slot_3", refused.Message);
        Assert.Single(w.Project!.EntityChanges); Assert.Single(w.Project.ModOptions!.Rows); Assert.Null(w.BuildError);
        // Their damage rows are separate targets (each its own backing row): each pod can have its own option.
        await w.SaveOptionRowAsync(Row(w, w.OptionCandidate(ModOptionsService.EntityKey(Missile(w, weapon: MaelstromMissilesTwin).InstanceKey))!, "twin_damage"));
        Assert.Equal(2, w.Project.EntityChanges.Count); Assert.Null(w.BuildError);
    }

    [Fact] public async Task Unedited_fields_that_cannot_be_options_offer_none()
    {
        using var e = new TestEnvironment(); var w = await Workspace(e, "0.25.1");
        Assert.Null(w.OptionCandidate(ModOptionsService.WeaponKey(Liberator, "weapon.suppressed"))); // boolean
        Assert.Null(w.OptionCandidate(ModOptionsService.WeaponKey(Liberator, "weapon.no_such_field")));
        Assert.Null(w.OptionCandidate(ModOptionsService.EntityKey("no-such-instance")));
        Assert.Null(w.OptionCandidate("weapon:malformed"));
        await Assert.ThrowsAsync<InvalidDataException>(() => w.SaveOptionRowAsync(new ModOptionRow { Key = ModOptionsService.WeaponKey(Liberator, "weapon.suppressed"), Id = "x", Label = "X", Min = 0, Max = 1, Step = 1 }));
        Assert.Empty(w.Project!.WeaponChanges);
    }

    [Fact] public async Task Projects_without_option_only_edits_keep_their_format_and_lua()
    {
        using var e = new TestEnvironment(); var w = await Workspace(e);
        var f = Missile(w); var key = ModOptionsService.EntityKey(f.InstanceKey);
        await w.SetEntityAsync(f.InstanceKey, "1200"); await w.SaveOptionRowAsync(Row(w, w.OptionTarget(key)!, "missiles"));
        Assert.True(w.Project!.FormatVersion < 13); Assert.False(ModOptionsService.HasOptionOnlyEdits(w.Project, w.Metadata!));
        Assert.False(w.OptionOnly(w.Project.EntityChanges[0]));
        // A vanilla value without an option is still no edit at all.
        await w.SetEntityAsync(Missile(w, "weapon.capacity").InstanceKey, Missile(w, "weapon.capacity").CurrentDefault.GetRawText());
        Assert.Single(w.Project.EntityChanges);
    }

    [Fact] public void Option_keys_parse_back_to_their_fields_and_choices_start_from_vanilla()
    {
        static void Parses(string key, string domain, params string[] parts) { var parsed = ModOptionsService.ParseKey(key); Assert.Equal(domain, parsed.Domain); Assert.Equal(parts, parsed.Parts); }
        Parses(ModOptionsService.WeaponKey("AR-23 Liberator", "weapon.fire_rate"), "weapon", "AR-23 Liberator", "weapon.fire_rate");
        Parses(ModOptionsService.ObjectKey("AR-23 Liberator", "primary", "explosion", "impact", "explosion.inner_radius"), "object", "AR-23 Liberator", "primary", "explosion", "impact", "explosion.inner_radius");
        Parses(ModOptionsService.ObjectKey("W", "primary", "projectile", null, "projectile.velocity"), "object", "W", "primary", "projectile", "", "projectile.velocity");
        Parses(ModOptionsService.EntityKey("vehicle_weapon:a|b:c"), "entity", "vehicle_weapon:a|b:c");
        Parses("no-domain", ""); Parses("weapon:no-bar", "weapon");
        var t = new OptionTarget("k", "entity", "Owner", "Damage", "integer", null, 1100, 1100, true, true, null, null, null, null, _ => { });
        var slider = new ModOptionRow { Min = 550, Max = 2200, Step = 50, Default = 1100 };
        Assert.Equal((1100.0, 2200.0), ModOptionsService.ChoicePreset(t, slider));
        Assert.Equal((1100.0, 1300.0), ModOptionsService.ChoicePreset(t with { Desired = 1300 }, slider));
    }
}
