using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Nodes;
using HD2RuntimeGUI.Core.Generation;
using HD2RuntimeGUI.Core.Models;
using HD2RuntimeGUI.Core.Services;
using Xunit;

namespace HD2RuntimeGUI.Tests;

// In-game options (HD2Runtime 0.25.0 hd2.options / CowboyBingus Mod Options Menu, optional dependency).
public sealed class ModOptionsTests
{
    private const string Liberator = "AR-23 Liberator";
    private static async Task<BuilderWorkspace> Workspace(TestEnvironment e, string resource = "mods/tests/options_mod", string version = "0.25.1")
    {
        var sdk = await SdkFixtures.Install(e, version); var w = e.Workspace();
        await w.CreateAsync(new("Options Mod", "Tests", resource, "0.1.0"), sdk); w.Project!.ExportDirectory = e.Paths.Exports; return w;
    }
    private static async Task<OptionTarget> Damage(BuilderWorkspace w, string value = "110")
    {
        await w.SetObjectScalarAsync(Liberator, "primary", "projectile", null, "damage.standard_damage", value, true);
        return w.OptionTargets.Single(t => t.Domain == "object" && t.DisplayName.Contains("damage", StringComparison.OrdinalIgnoreCase));
    }
    private static async Task<JsonNode> Export(BuilderWorkspace w, string entry)
    {
        await w.ExportAsync(); using var zip = ZipFile.OpenRead(w.LastExport!);
        using var reader = new StreamReader(zip.GetEntry(entry)!.Open()); return JsonNode.Parse(await reader.ReadToEndAsync())!;
    }
    private static int Count(string text, string part) { var n = 0; for (var i = text.IndexOf(part, StringComparison.Ordinal); i >= 0; i = text.IndexOf(part, i + part.Length, StringComparison.Ordinal)) n++; return n; }

    [Fact] public async Task Projects_without_options_generate_exactly_the_same_lua_and_manifest()
    {
        using var e = new TestEnvironment(); var w = await Workspace(e); await Damage(w);
        var lua = w.LuaPreview; var format = w.Project!.FormatVersion;
        Assert.DoesNotContain("hd2.options", lua); Assert.Null((await Export(w, "hd2runtime.json"))["optional"]);
        // Switched on but nothing bound: unchanged. A bound row with the switch off: unchanged.
        await w.SetModOptionsEnabledAsync(true); Assert.Equal(lua, w.LuaPreview); Assert.Null((await Export(w, "hd2runtime.json"))["optional"]);
        await w.SaveOptionRowAsync(w.SuggestOptionRow(w.OptionTargets[0])); Assert.NotEqual(lua, w.LuaPreview);
        await w.SetModOptionsEnabledAsync(false); Assert.Equal(lua, w.LuaPreview); Assert.Null((await Export(w, "hd2runtime.json"))["optional"]);
        Assert.Equal(7, w.Project.FormatVersion); Assert.True(format < 7);
    }

    [Fact] public async Task Old_projects_load_and_save_without_options()
    {
        using var e = new TestEnvironment(); var w = await Workspace(e); await Damage(w);
        var json = await File.ReadAllTextAsync(e.Paths.ProjectFile(w.Project!.Id));
        Assert.DoesNotContain("modOptions", json); Assert.True(w.Project.FormatVersion < 7);
        await w.OpenAsync(w.Project.Id); Assert.Null(w.Project!.ModOptions); Assert.False(w.OptionsEnabled);
    }

    [Fact] public async Task Liberator_damage_becomes_a_slider_bound_inside_the_same_ensure()
    {
        using var e = new TestEnvironment(); var w = await Workspace(e); var damage = await Damage(w);
        Assert.True(damage.Eligible); Assert.Equal(90, damage.Baseline); Assert.Equal(110, damage.Desired); Assert.True(damage.Integer);
        await w.SetModOptionsEnabledAsync(true);
        var row = w.SuggestOptionRow(damage);
        // Defaults from the edit: baseline..(2 × edit), default = edited value on an integer step.
        Assert.Equal(ModOptionsService.Slider, row.Kind); Assert.Equal(90, row.Min); Assert.Equal(110, row.Default); Assert.True(row.Max > 110);
        Assert.Equal(0, (row.Default - row.Min) % row.Step); Assert.Equal(Math.Truncate(row.Step), row.Step);
        row.Label = "Liberator Damage"; row.Id = "liberator_damage"; row.Max = 500; row.Step = 10;
        await w.SaveOptionRowAsync(row); Assert.Null(w.BuildError);
        var lua = w.LuaPreview;
        Assert.Contains("local options=hd2.options({id='options_mod',title='Options Mod'})\n", lua);
        Assert.Contains("local enabled=options:toggle({id='enabled',label='Enabled',default=true})\n", lua);
        Assert.Contains("local option_liberator_damage=options:slider({id='liberator_damage',label='Liberator Damage',min=90,max=500,step=10,default=110})\n", lua);
        Assert.Contains("hd2.ensure({\n    enabled=enabled,\n    patch={", lua);
        Assert.Contains("allow_shared=true,", lua); Assert.Contains("expect=90,", lua); Assert.Contains("value=option_liberator_damage,", lua);
        Assert.DoesNotContain("value=110", lua); Assert.Equal(1, Count(lua, "hd2.ensure("));
        // Optional dependency, never a requirement.
        var manifest = await Export(w, "hd2runtime.json");
        Assert.Equal("1.0.0", (string)manifest["optional"]!["mod_options_menu"]!["min_version"]!); Assert.Equal(1, (int)manifest["optional"]!["mod_options_menu"]!["api"]!);
        Assert.Equal(18, (int)manifest["optional"]!["mod_options_menu"]!["bingus_min_release"]!); Assert.Null(manifest["requires"]!["mod_options_menu"]);
        Assert.Contains("Mod Options Menu", (string)(await Export(w, "manifest.json"))["Description"]!);
        // Save/reload keeps the row.
        await w.OpenAsync(w.Project!.Id); Assert.Equal(lua, w.LuaPreview); Assert.Equal(7, w.Project!.FormatVersion);
    }

    [Fact] public async Task Several_bound_fields_of_one_object_share_one_ensure_transaction()
    {
        using var e = new TestEnvironment(); var w = await Workspace(e);
        await w.SetObjectScalarAsync(Liberator, "primary", "projectile", null, "projectile.velocity", "1000", true);
        await w.SetObjectScalarAsync(Liberator, "primary", "projectile", null, "projectile.mass", "5", true);
        await w.SetModOptionsEnabledAsync(true);
        foreach (var t in w.OptionTargets) await w.SaveOptionRowAsync(w.SuggestOptionRow(t));
        var lua = w.LuaPreview; Assert.Null(w.BuildError);
        Assert.Equal(1, Count(lua, "hd2.ensure(")); Assert.Equal(1, Count(lua, "enabled=enabled,\n")); Assert.Contains("transaction={", lua);
        Assert.Equal(2, Count(lua, "value=option_"));
    }

    [Fact] public async Task Enum_fire_mode_becomes_a_choice_with_the_weapons_allowed_values()
    {
        using var e = new TestEnvironment(); var w = await Workspace(e);
        await w.SetWeaponChangeAsync(Liberator, "weapon.default_fire_mode", "2", false);
        await w.SetModOptionsEnabledAsync(true);
        var t = w.OptionTargets.Single(x => x.DisplayName.Contains("fire mode", StringComparison.OrdinalIgnoreCase));
        var row = w.SuggestOptionRow(t);
        Assert.Equal(ModOptionsService.Choice, row.Kind); Assert.Equal([1, 2], row.Values); Assert.Equal(2, row.DefaultIndex);
        await w.SaveOptionRowAsync(row); Assert.Null(w.BuildError);
        Assert.Contains("=options:choice({id='" + row.Id + "',label=", w.LuaPreview); Assert.Contains("choices={'Full Auto','Semi Auto'},values={1,2},default=2", w.LuaPreview);
        row.Kind = ModOptionsService.Slider; row.Min = 1; row.Max = 2; row.Step = 1; row.Default = 2;
        await Assert.ThrowsAsync<InvalidDataException>(() => w.SaveOptionRowAsync(row));
        var bad = w.SuggestOptionRow(t); bad.Values = [1, 7];
        await Assert.ThrowsAsync<InvalidDataException>(() => w.SaveOptionRowAsync(bad));
    }

    [Fact] public async Task Booster_options_stay_inside_the_published_safe_range_and_keep_acknowledgements()
    {
        using var e = new TestEnvironment(); var w = await Workspace(e);
        var vitality = w.Metadata!.Entities!.Boosters!.FieldInstances.Single(f => f.Target.Booster == "Vitality Enhancement" && f.SemanticFieldId == "booster.damage_taken_scale");
        await w.SetEntityAsync(vitality.InstanceKey, "0.5"); await w.SetBoosterAcknowledgedAsync("Vitality Enhancement", true);
        await w.SetModOptionsEnabledAsync(true);
        var t = w.OptionTarget(ModOptionsService.EntityKey(vitality.InstanceKey))!;
        Assert.Equal(0, t.RangeMin); Assert.Equal(4, t.RangeMax);
        var row = w.SuggestOptionRow(t);
        Assert.Equal(0.5, row.Min); Assert.True(row.Max <= 4); Assert.Equal(0.5, row.Default);
        var wide = w.SuggestOptionRow(t); wide.Max = 5;
        Assert.Contains("safe range", (await Assert.ThrowsAsync<InvalidDataException>(() => w.SaveOptionRowAsync(wide))).Message);
        await w.SaveOptionRowAsync(row); Assert.Null(w.BuildError);
        var lua = w.LuaPreview;
        Assert.Contains("hd2.ensure({\n    enabled=enabled,\n    patch={", lua); Assert.Contains("target=hd2.booster('Vitality Enhancement'):tuning(),", lua);
        Assert.Contains("allow_unverified_effect=true,", lua); Assert.Contains("expect=0.9,", lua); Assert.Contains("value=option_", lua);
        // Unacknowledging still blocks the build, options or not.
        await w.SetBoosterAcknowledgedAsync("Vitality Enhancement", false); Assert.NotNull(w.BuildError);
    }

    [Fact] public async Task Boolean_and_reference_edits_are_not_eligible()
    {
        using var e = new TestEnvironment(); var w = await Workspace(e);
        await w.SetWeaponChangeAsync(Liberator, "weapon.suppressed", "true", false);
        await w.SetModOptionsEnabledAsync(true);
        var t = w.OptionTargets.Single(x => x.Type == "boolean");
        Assert.False(t.Eligible); Assert.Contains("enabled state", t.Blocker);
        await Assert.ThrowsAsync<InvalidDataException>(() => w.SaveOptionRowAsync(new ModOptionRow { Key = t.Key, Id = "x", Label = "X", Min = 0, Max = 1, Step = 1 }));
    }

    [Fact] public async Task Validation_follows_mod_options_menu_limits()
    {
        using var e = new TestEnvironment(); var w = await Workspace(e); var t = await Damage(w);
        await w.SetModOptionsEnabledAsync(true);
        ModOptionRow Row(Action<ModOptionRow> edit) { var r = w.SuggestOptionRow(t); r.Min = 90; r.Max = 500; r.Step = 10; r.Default = 110; edit(r); return r; }
        async Task Rejects(Action<ModOptionRow> edit, string message) =>
            Assert.Contains(message, (await Assert.ThrowsAsync<InvalidDataException>(() => w.SaveOptionRowAsync(Row(edit)))).Message);
        await Rejects(r => r.Default = 115, "sit on a step");
        await Rejects(r => { r.Min = 500; r.Max = 90; }, "min < max");
        await Rejects(r => r.Step = 1000, "min < max");
        await Rejects(r => r.Step = 2.5, "whole numbers");
        await Rejects(r => r.Default = 600, "between min and max");
        await Rejects(r => r.Label = new string('x', 65), "1 to 64 bytes");
        await Rejects(r => r.Label = "two\nlines", "one line");
        await Rejects(r => r.Description = new string('d', 401), "1 to 400 bytes");
        await Rejects(r => r.Id = "enabled", "id must use");
        await Rejects(r => r.Id = "bad id", "id must use");
        await Rejects(r => { r.Kind = ModOptionsService.Choice; r.Choices = ["Only"]; r.Values = [90]; r.DefaultIndex = 1; }, "2 to 16");
        await Rejects(r => { r.Kind = ModOptionsService.Choice; r.Choices = [.. Enumerable.Range(0, 17).Select(i => "c" + i)]; r.Values = [.. Enumerable.Range(0, 17).Select(i => 90.0 + i)]; }, "2 to 16");
        await Rejects(r => { r.Kind = ModOptionsService.Choice; r.Choices = ["Low", "High"]; r.Values = [90, 90.5]; r.DefaultIndex = 1; }, "not accepted");
        // A numeric field may also use a choice of presets.
        await w.SaveOptionRowAsync(Row(r => { r.Kind = ModOptionsService.Choice; r.Choices = ["Stock", "Strong"]; r.Values = [90, 150]; r.DefaultIndex = 2; }));
        Assert.Null(w.BuildError); Assert.Contains("choices={'Stock','Strong'},values={90,150},default=2", w.LuaPreview);
        await Assert.ThrowsAsync<InvalidDataException>(() => w.SaveModOptionsPageAsync("bad page!", "Title", "Enabled", null));
        await Assert.ThrowsAsync<InvalidDataException>(() => w.SaveModOptionsPageAsync("page", new string('t', 41), "Enabled", null));
        await w.SaveModOptionsPageAsync("my_mod", "My Mod", "Mod active", "Off restores every reviewed baseline.");
        Assert.Contains("local options=hd2.options({id='my_mod',title='My Mod'})", w.LuaPreview);
        Assert.Contains("options:toggle({id='enabled',label='Mod active',default=true,description='Off restores every reviewed baseline.'})", w.LuaPreview);
    }

    [Fact] public async Task Apply_once_edits_and_removed_edits_are_handled()
    {
        using var e = new TestEnvironment(); var w = await Workspace(e); var t = await Damage(w);
        await w.SetModOptionsEnabledAsync(true); await w.SaveOptionRowAsync(w.SuggestOptionRow(t));
        w.Project!.CompositionChanges[0].EnsureEnabled = false;
        Assert.Contains(ModOptionsService.Issues(w.Project, w.Metadata!), i => i.Contains("need Ensure"));
        Assert.Throws<InvalidDataException>(() => e.Generator.Generate(w.Project, w.Metadata!));
        w.Project.CompositionChanges[0].EnsureEnabled = true;
        // Resetting the edit leaves a stale row that generates nothing, and the dependency is dropped.
        await w.RemoveCompositionAsync(w.Project.CompositionChanges[0].Id);
        await w.SetWeaponChangeAsync(Liberator, "weapon.fire_rate", "700", false);
        Assert.Single(w.Project.ModOptions!.Rows); Assert.False(w.OptionRowActive(w.Project.ModOptions.Rows[0]));
        Assert.DoesNotContain("hd2.options", w.LuaPreview); Assert.Null((await Export(w, "hd2runtime.json"))["optional"]);
    }

    [Fact] public async Task Rows_register_in_order_and_one_value_cannot_have_two_options()
    {
        using var e = new TestEnvironment(); var w = await Workspace(e); await Damage(w);
        await w.SetWeaponChangeAsync(Liberator, "weapon.fire_rate", "700", false);
        await w.SetModOptionsEnabledAsync(true);
        foreach (var t in w.OptionTargets) await w.SaveOptionRowAsync(w.SuggestOptionRow(t));
        string Order() { var lua = w.LuaPreview; return string.Join(",", w.Project!.ModOptions!.Rows.Select(r => lua.IndexOf("id='" + r.Id + "'", StringComparison.Ordinal))); }
        var first = w.Project!.ModOptions!.Rows[0].Key; var before = w.LuaPreview.IndexOf(w.Project.ModOptions.Rows[0].Id, StringComparison.Ordinal);
        await w.MoveOptionRowAsync(first, 1);
        Assert.Equal(first, w.Project.ModOptions.Rows[1].Key); Assert.True(w.LuaPreview.IndexOf(w.Project.ModOptions.Rows[0].Id, StringComparison.Ordinal) < w.LuaPreview.IndexOf(w.Project.ModOptions.Rows[1].Id, StringComparison.Ordinal));
        Assert.Equal(2, Count(w.LuaPreview, "enabled=enabled,")); // two operations, both under the one master toggle
        Assert.Equal(1, Count(w.LuaPreview, "options:toggle("));
        var bindings = new OptionBindings("", new Dictionary<string, string> { ["a"] = "option_a", ["b"] = "option_b" });
        Assert.Throws<InvalidDataException>(() => bindings.Value(["a", "b"], "1")); Assert.Equal("option_a", bindings.Value(["a", "c"], "1"));
    }

    [Fact] public async Task At_most_31_options_plus_the_master_toggle()
    {
        using var e = new TestEnvironment(); var w = await Workspace(e);
        var weapons = w.Metadata!.PlayerWeapons!.Weapons.Where(x => !x.OrdinaryWritesBlocked && x.Fields.Any(f => f.SemanticFieldId == "weapon.fire_rate" && f.Editable)).Take(32).ToArray();
        Assert.Equal(32, weapons.Length);
        foreach (var x in weapons) await w.SetWeaponChangeAsync(x.Name, "weapon.fire_rate", (x.Fields.First(f => f.SemanticFieldId == "weapon.fire_rate").CurrentDefault.GetDouble() + 10).ToString(System.Globalization.CultureInfo.InvariantCulture), false);
        await w.SetModOptionsEnabledAsync(true);
        var targets = w.OptionTargets.Where(t => t.Eligible).ToArray();
        for (var i = 0; i < 31; i++) await w.SaveOptionRowAsync(w.SuggestOptionRow(targets[i]));
        Assert.Contains("at most 32", (await Assert.ThrowsAsync<InvalidDataException>(() => w.SaveOptionRowAsync(w.SuggestOptionRow(targets[31])))).Message);
        Assert.Null(w.BuildError); Assert.Equal(31, Count(w.LuaPreview, "=options:slider("));
    }

    [Fact] public async Task Fallback_follows_the_published_0251_semantics()
    {
        using var e = new TestEnvironment(); var w = await Workspace(e); var t = await Damage(w);
        await w.SetModOptionsEnabledAsync(true); await w.SaveOptionRowAsync(w.SuggestOptionRow(t));
        // Default mode is Runtime's own default (recommended for generated mods): no key, declared defaults apply without the menu.
        Assert.Contains("local options=hd2.options({id='options_mod',title='Options Mod'})\n", w.LuaPreview); Assert.DoesNotContain("fallback", w.LuaPreview);
        Assert.Contains("without it the settings use their defaults.", (string)(await Export(w, "manifest.json"))["Description"]!);
        await w.SetOptionsFallbackAsync(ModOptionsService.FallbackDisable); Assert.Null(w.BuildError);
        Assert.Contains("local options=hd2.options({id='options_mod',title='Options Mod',fallback='disable'})\n", w.LuaPreview);
        Assert.Contains("without it the configurable settings stay inactive.", (string)(await Export(w, "manifest.json"))["Description"]!);
        await w.OpenAsync(w.Project!.Id); Assert.Equal(ModOptionsService.FallbackDisable, w.Project!.ModOptions!.Fallback);
        await Assert.ThrowsAsync<InvalidDataException>(() => w.SetOptionsFallbackAsync("sometimes"));
    }
    [Fact] public async Task Sdk_0250_has_no_fallback_and_keeps_bound_edits_inactive()
    {
        using var e = new TestEnvironment(); var w = await Workspace(e, version: "0.25.0"); var t = await Damage(w);
        await w.SetModOptionsEnabledAsync(true); await w.SaveOptionRowAsync(w.SuggestOptionRow(t));
        Assert.DoesNotContain("fallback", w.LuaPreview); Assert.Null(w.BuildError);
        Assert.Contains("stay inactive", (string)(await Export(w, "manifest.json"))["Description"]!);
        await Assert.ThrowsAsync<InvalidDataException>(() => w.SetOptionsFallbackAsync(ModOptionsService.FallbackDisable));
        w.Project!.ModOptions!.Fallback = ModOptionsService.FallbackDisable;
        Assert.Contains(ModOptionsService.Issues(w.Project, w.Metadata!), i => i.Contains("needs HD2Runtime SDK 0.25.1"));
    }
    [Fact] public async Task Options_require_sdk_025()
    {
        using var e = new TestEnvironment(); var sdk = await SdkFixtures.Install(e, "0.24.0"); var w = e.Workspace();
        await w.CreateAsync(new("Old", "Tests", "mods/tests/old", "0.1.0"), sdk);
        Assert.False(w.OptionsSupported); Assert.Empty(w.OptionTargets);
        await Assert.ThrowsAsync<InvalidDataException>(() => w.SetModOptionsEnabledAsync(true));
    }
}
