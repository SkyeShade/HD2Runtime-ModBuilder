using System.IO.Compression;
using System.Text.RegularExpressions;
using HD2RuntimeGUI.Core.Generation;
using HD2RuntimeGUI.Core.Metadata;
using HD2RuntimeGUI.Core.Models;
using HD2RuntimeGUI.Core.Projects;
using HD2RuntimeGUI.Core.Scripting;
using HD2RuntimeGUI.Core.Services;
using Xunit;

namespace HD2RuntimeGUI.Tests;

// The unreleased HD2Runtime 0.28.0 development SDK (fixture e15d5bf): event scripting and custom Lua, attack outputs through the active
// projectile source, Resupply, the SH-20 shield zone, numeric bounds, the SG-20 Halt's branch-qualified fields and the developer SDK setting.
public sealed class Runtime028IntegrationTests
{
    private const string Liberator = "AR-23 Liberator", Napalm = "output/v1/projectile/eat-700-expendable-napalm", DeEscalator = "output/v1/projectile/gl-52-de-escalator";
    private const string Shield = "SH-20 Ballistic Shield Backpack";
    private const string ShieldZoneArmor = "backpack:sh-20-ballistic-shield-backpack:sh-20-ballistic-shield-backpack-damage-zone-zone-0:zone.armor";
    private const string ShieldMainArmor = "backpack:sh-20-ballistic-shield-backpack:sh-20-ballistic-shield-backpack-backpack:entity.armor";
    private const string MaxigunCapacity = "backpack:m-1000-maxigun-backpack:m-1000-maxigun-backpack-backpack:deposit.capacity";
    private static string[] Ids(string lua) => Regex.Matches(lua, @"\bid='([^']+)'").Select(m => m.Groups[1].Value).ToArray();
    private static async Task<string> ExportedLua(BuilderWorkspace w)
    {
        await w.ExportAsync(); using var zip = ZipFile.OpenRead(w.LastExport!);
        using var reader = new StreamReader(zip.GetEntry("src/addon.lua")!.Open()); return await reader.ReadToEndAsync();
    }

    // ---- custom Lua (src/addon.lua) ----------------------------------------------------------------------------------------------------

    [Fact] public async Task Custom_lua_is_kept_exactly_saved_with_the_project_and_reloaded()
    {
        using var e = new TestEnvironment(); var (w, _) = await EnemyAuthoringTests.Fresh(e);
        var format = w.Project!.FormatVersion; Assert.True(format < 11); Assert.Null(w.Project.CustomLua);
        await w.AddCustomLuaAsync();
        Assert.Equal(11, w.Project.FormatVersion); Assert.Equal(BuilderWorkspace.StarterLua, w.Project.CustomLua!.Source);
        Assert.Equal(BuilderWorkspace.StarterLua, await File.ReadAllTextAsync(w.CustomLuaPath!));
        // Tabs, CRLF, trailing spaces, a long bracket string and non-ASCII text survive byte for byte in the project and its working copy.
        var text = "-- ünïcode · tabs\r\n\tlocal x = [==[\r\n  keep ]] this\r\n]==]   \r\nhd2.mod():log(x)\r\n";
        await w.SaveCustomLuaAsync(text);
        Assert.Equal(text, w.Project.CustomLua.Source); Assert.Equal(text, await File.ReadAllTextAsync(w.CustomLuaPath!));
        Assert.Null(w.CustomLuaSyncIssue());
        await w.OpenAsync(w.Project.Id);
        Assert.Equal(text, w.Project!.CustomLua!.Source); Assert.Equal(11, w.Project.FormatVersion); Assert.True(w.Project.CustomLua.Enabled);
        // Generated output: one require, the generated operations first, then the custom text in its own function (line endings normalized).
        Assert.Null(w.BuildError);
        Assert.Single(Regex.Matches(w.LuaPreview, Regex.Escape("require('mods/skyeshade/hd2runtime')")));
        Assert.Contains("local function addon(...)\n-- ünïcode · tabs\n\tlocal x = [==[\n  keep ]] this\n]==]   \nhd2.mod():log(x)\nend\naddon()\nreturn generated\n", w.LuaPreview);
        // Excluded from the build: the text stays in the project but is not generated.
        await w.SetCustomLuaEnabledAsync(false);
        Assert.DoesNotContain("addon()", w.LuaPreview); Assert.Equal(text, w.Project.CustomLua!.Source);
    }

    [Fact] public async Task An_outside_edit_to_the_working_copy_is_detected_and_never_overwritten_silently()
    {
        using var e = new TestEnvironment(); var (w, _) = await EnemyAuthoringTests.Fresh(e);
        await w.AddCustomLuaAsync(); await w.SaveCustomLuaAsync("local a = 1\n");
        var outside = "local a = 2 -- edited in VS Code\n";
        await File.WriteAllTextAsync(w.CustomLuaPath!, outside);
        Assert.False(w.CustomLuaDisk()!.InSync); Assert.NotNull(w.CustomLuaSyncIssue());
        // Saving over it needs an explicit choice; export is refused until the conflict is resolved.
        var conflict = await Assert.ThrowsAsync<CustomLuaConflictException>(() => w.SaveCustomLuaAsync("local a = 3\n"));
        Assert.Equal(outside, conflict.DiskText); Assert.Equal(outside, await File.ReadAllTextAsync(w.CustomLuaPath!));
        await Assert.ThrowsAsync<InvalidDataException>(w.ExportAsync);
        // Reload takes the outside text; overwrite (explicit) replaces it.
        await w.ReloadCustomLuaAsync();
        Assert.Equal(outside, w.Project!.CustomLua!.Source); Assert.Null(w.CustomLuaSyncIssue());
        await File.WriteAllTextAsync(w.CustomLuaPath!, "local a = 4\n");
        await w.SaveCustomLuaAsync("local a = 5\n", overwriteExternal: true);
        Assert.Equal("local a = 5\n", await File.ReadAllTextAsync(w.CustomLuaPath!)); Assert.Null(w.CustomLuaSyncIssue());
        Assert.Contains("local a = 5", await ExportedLua(w));
        // A deleted working copy comes back from the saved text when the project opens.
        File.Delete(w.CustomLuaPath!); await w.OpenAsync(w.Project.Id);
        Assert.Equal("local a = 5\n", await File.ReadAllTextAsync(w.CustomLuaPath!));
        // Open in the default editor / containing folder.
        await w.OpenCustomLuaExternallyAsync(); Assert.Equal(w.CustomLuaPath, e.Desktop.OpenedFile);
        await w.OpenCustomLuaFolderAsync(); Assert.Equal(w.CustomLuaPath, e.Desktop.OpenedPath); Assert.True(e.Desktop.SelectedFile);
    }

    [Fact] public async Task Custom_lua_syntax_errors_block_the_build_with_their_line_and_duplicates_carry_the_text()
    {
        using var e = new TestEnvironment(); var (w, _) = await EnemyAuthoringTests.Fresh(e);
        await w.AddCustomLuaAsync(); await w.SaveCustomLuaAsync("local ok = 1\nif ok then\n  print('x')\n");
        Assert.NotNull(w.BuildError); Assert.Contains("src/addon.lua line", w.BuildError);
        Assert.Contains(w.CustomLuaDiagnostics(), d => d.Severity == LuaDiagnostic.Error);
        await w.SaveCustomLuaAsync("-- HD2-Addon: nope\nlocal ok = 1\n");
        Assert.NotNull(w.BuildError); // the Runtime builder rejects an addon.lua header
        await w.SaveCustomLuaAsync("local ok = 1\n"); Assert.Null(w.BuildError);
        var source = w.Project!.Id;
        await w.DuplicateAsync(source, new("Copy", "Tests", "mods/tests/copy", "0.1.0"));
        Assert.NotEqual(source, w.Project!.Id); Assert.Equal("local ok = 1\n", w.Project.CustomLua!.Source);
        Assert.Equal("local ok = 1\n", await File.ReadAllTextAsync(w.CustomLuaPath!));
    }

    [Fact] public void The_lua_checker_reports_syntax_errors_and_unknown_catalog_names_without_running_anything()
    {
        Assert.Empty(LuaParser.Diagnose("local t = {1, 2, [3] = 'x'}\nfor i, v in ipairs(t) do goto next ::next:: end\nreturn t\n"));
        var error = Assert.Single(LuaParser.Diagnose("local x = \nreturn"));
        Assert.Equal(LuaDiagnostic.Error, error.Severity); Assert.Equal(2, error.Line);
        Assert.Contains(LuaParser.Diagnose("local function f() return ... end"), d => d.Severity == LuaDiagnostic.Error);
        Assert.Contains(LuaParser.Diagnose("break"), d => d.Severity == LuaDiagnostic.Error);
    }

    [Fact] public async Task Event_catalog_actions_and_the_lua_stub_load_from_the_sdk()
    {
        using var e = new TestEnvironment(); var (w, sdk) = await EnemyAuthoringTests.Fresh(e);
        var events = sdk.Events!;
        Assert.Equal(18, events.Events.Count()); Assert.Equal(17, events.Events.Count(x => x.IsAvailable));
        Assert.False(events.Event("entity_damage_pre")!.IsAvailable);
        var died = events.Event("player_died")!;
        Assert.Contains(died.Payload!, p => p.Name == "local_player" && p.Type == "boolean" && !p.Nullable);
        Assert.Contains(died.Payload!, p => p.Name == "position" && p.Nullable);
        // Actions: host only, with live proof only where the catalog records it.
        var a = events.Actions;
        Assert.True(a.Explosions.HostOnly && a.Projectiles.HostOnly && a.StatusEffects.HostOnly);
        Assert.True(events.ExplosionLiveProven("Hellbomb")); Assert.False(events.ExplosionLiveProven("B-100 Portable Hellbomb"));
        Assert.Contains("R-36 Eruptor", a.Projectiles.LiveProven); Assert.DoesNotContain("R-36 Eruptor", a.Explosions.LiveProvenWeapons);
        Assert.Contains(a.StatusEffects.Statuses, s => s.Id == "fire" && s.LiveProven);
        Assert.Contains(a.StatusEffects.Statuses, s => !s.LiveProven);
        Assert.Equal("blocked", a.SpawnEntity.Status);
        // Autocomplete from the LuaLS stub: the hd2 root class, its members, and catalog strings.
        var completion = LuaCompletion.Build(sdk);
        Assert.Equal("HD2Runtime", completion.Root);
        Assert.Contains(completion.Classes[completion.Root], m => m.N == "events"); Assert.Contains(completion.Classes[completion.Root], m => m.N == "after");
        Assert.Contains("player_died", completion.Strings["events"]); Assert.DoesNotContain("entity_damage_pre", completion.Strings["events"]);
        Assert.Contains("Hellbomb", completion.Strings["explosions"]);
    }

    [Fact] public async Task Snippets_use_the_real_api_and_insert_into_a_project_that_builds_and_exports()
    {
        using var e = new TestEnvironment(); var (w, sdk) = await EnemyAuthoringTests.Fresh(e);
        var snippets = LuaSnippets.For(sdk, w.Project!.ResourceId);
        Assert.Equal(["player-death-explosion", "enemy-death-callback", "kill-stacking", "timer", "keybind", "projectile-spawn", "status-application", "weapon-changed", "heal-on-kill", "mission-counter"],
            snippets.Select(s => s.Id));
        foreach (var s in snippets) Assert.Empty(LuaScriptAnalyzer.Analyze(s.Code, sdk.Events, sdk.Entities!.Enemies));
        // The kill-stacking snippet takes its baseline and field constant from the SDK, and the keybind id from the project.
        var liberatorDamage = sdk.PlayerWeapons!.Field(Liberator, "damage.standard_damage")!;
        Assert.Contains("BASE, STEP, CAP = " + liberatorDamage.CurrentDefault.GetRawText() + ",", snippets.Single(s => s.Id == "kill-stacking").Code);
        Assert.Contains("hd2.fields.damage.player_standard_damage", snippets.Single(s => s.Id == "kill-stacking").Code);
        Assert.Contains("'tests_enemy_tuning.action'", snippets.Single(s => s.Id == "keybind").Code);
        await w.AddCustomLuaAsync();
        await w.SaveCustomLuaAsync(w.Project.CustomLua!.Source + string.Join("\n", snippets.Select(s => s.Code)));
        Assert.Null(w.BuildError);
        var lua = await ExportedLua(w);
        Assert.Contains("hd2.explosions.spawn('Hellbomb', {position = event.position})", lua);
        Assert.Contains("hd2.input.bind('tests_enemy_tuning.action'", lua);
    }

    [Fact] public async Task Unknown_events_actions_and_statuses_are_flagged_as_warnings()
    {
        using var e = new TestEnvironment(); var sdk = (await EnemyAuthoringTests.Fresh(e)).Sdk;
        var source = "hd2.events.on('player_dyed', function() end)\nhd2.events.on('entity_damage_pre', function() end)\n"
            + "hd2.explosions.spawn('Not A Bomb', {})\nhd2.projectiles.spawn('Not A Gun', {})\nhd2.status.apply(nil, 'not_a_status')\nhd2.input.bind('nodot', {})\n";
        var found = LuaScriptAnalyzer.Analyze(source, sdk.Events, sdk.Entities!.Enemies);
        Assert.All(found, d => Assert.Equal(LuaDiagnostic.Warning, d.Severity));
        Assert.Equal([1, 2, 3, 4, 5, 6], found.Select(d => d.Line).Order());
        Assert.Empty(LuaScriptAnalyzer.Analyze("hd2.events.on('player_died', function() end)\nhd2.explosions.spawn('Hellbomb', {})\nhd2.status.apply(nil, 'fire')\n", sdk.Events));
    }

    // ---- attack outputs through the active projectile source ---------------------------------------------------------------------------

    [Fact] public async Task The_liberator_takes_its_donors_through_its_ammunition_with_the_proven_pairs_marked()
    {
        using var e = new TestEnvironment(); var (w, sdk) = await EnemyAuthoringTests.Fresh(e);
        var host = w.OutputHost(Liberator, "primary", out _)!;
        Assert.Equal(AttackOutputHost.AmmunitionMechanism, host.Mechanism); Assert.Equal("RIFLE 5,5x50mm. FULL METAL JACKET", host.Ammunition!.Item);
        Assert.Equal("INDIRECT", sdk.AttackOutputs!.Source(Liberator, "primary")!.Status);
        var donors = sdk.AttackOutputs.Donors(host);
        // Proven cross-family compositions need only the ammunition's shared opt-in; other cross-class donors carry both unverified opt-ins.
        foreach (var proven in new[] { Napalm, DeEscalator })
        {
            var d = donors.Single(x => x.Output.SemanticId == proven);
            Assert.True(d.CrossClass && d.ProvenOnHost && d.Allowed); Assert.Equal(["allow_shared"], d.Acknowledgements);
        }
        var eruptor = donors.Single(x => x.Output.SemanticId == "output/v1/projectile/r-36-eruptor");
        Assert.True(eruptor.CrossClass && eruptor.Allowed && !eruptor.ProvenOnHost);
        Assert.Equal(["allow_shared", "allow_unverified_effect", "allow_unverified_reference"], eruptor.Acknowledgements.Order(StringComparer.Ordinal));
        Assert.Equal(["allow_shared"], donors.Single(x => x.Output.SemanticId == "output/v1/projectile/las-58-talon").Acknowledgements);
        // A dormant reference is never offered: the SG-20 Halt's rounds ammunition is read-only with Runtime's reason.
        Assert.Null(w.OutputHost("SG-20 Halt", "feed_primary", out var halt)); Assert.Contains("dormant", halt);
    }

    [Fact] public async Task An_ammunition_host_output_generates_the_active_source_write_and_persists_its_opt_ins()
    {
        using var e = new TestEnvironment(); var (w, sdk) = await EnemyAuthoringTests.Fresh(e);
        await w.SetAttackOutputAsync(Liberator, "primary", Napalm);
        var change = Assert.Single(w.Project!.AttackOutputChanges!);
        Assert.Equal(AttackOutputHost.AmmunitionMechanism, change.Mechanism); Assert.Equal(["allow_shared"], change.Acknowledgements);
        Assert.Equal(11, w.Project.FormatVersion); Assert.Null(w.BuildError);
        Assert.Equal(new(1, 1), w.WeaponSummary(Liberator)); // modified, and shared through the ammunition definition
        var lua = w.LuaPreview;
        Assert.Contains("target=hd2.weapon('AR-23 Liberator'):ammunition(),", lua);
        Assert.Contains("field=hd2.fields.ammunition.projectile,", lua);
        Assert.Contains("expect=hd2.weapon('AR-23 Liberator'):ammunition():projectile(),", lua);
        Assert.Contains("value=hd2.attack_output('" + Napalm + "'),", lua);
        Assert.Contains("allow_shared=true", lua); Assert.DoesNotContain("allow_unverified_reference", lua);
        // Unproven cross-class: every opt-in is written, with no acknowledgement step.
        await w.SetAttackOutputAsync(Liberator, "primary", "output/v1/projectile/r-36-eruptor");
        Assert.Contains("allow_unverified_reference=true", w.LuaPreview); Assert.Contains("allow_unverified_effect=true", w.LuaPreview);
        Assert.Single(w.Project.AttackOutputChanges!);
        // Reopened: the choice, its opt-ins and evidence are kept, and the output is still valid.
        var saved = w.Project.AttackOutputChanges![0];
        await w.OpenAsync(w.Project.Id);
        Assert.Equal(saved.Acknowledgements, w.Project!.AttackOutputChanges![0].Acknowledgements); Assert.Equal(saved.Evidence, w.Project.AttackOutputChanges[0].Evidence);
        Assert.Null(w.AttackOutputIssue(w.Project.AttackOutputChanges[0]));
        Assert.Contains("allow_unverified_reference=true", await ExportedLua(w));
        // Toggle and reset.
        await w.ToggleAttackOutputAsync(saved.Id); Assert.DoesNotContain("hd2.attack_output", w.LuaPreview);
        await w.SetAttackOutputAsync(Liberator, "primary", null); Assert.Null(w.Project.AttackOutputChanges);
    }

    [Fact] public async Task A_same_class_direct_donor_stays_a_classic_reference_swap()
    {
        using var e = new TestEnvironment(); var (w, sdk) = await EnemyAuthoringTests.Fresh(e);
        var host = w.OutputHost("SMG-32 Reprimand", "primary", out _)!;
        Assert.Equal(AttackOutputHost.Component, host.Mechanism);
        var talon = sdk.AttackOutputs!.Donors(host).Single(d => d.Output.SemanticId == "output/v1/projectile/las-58-talon");
        Assert.False(talon.CrossClass); Assert.NotNull(w.ClassicSource(host, talon.Output));
        // A cross-class donor on a component host becomes an attack output written on the attack itself.
        var cross = sdk.AttackOutputs.Donors(host).First(d => d.CrossClass && d.Allowed);
        Assert.Null(w.ClassicSource(host, cross.Output));
        await w.SetAttackOutputAsync("SMG-32 Reprimand", "primary", cross.Output.SemanticId);
        Assert.Null(w.BuildError);
        Assert.Contains("target=hd2.weapon('SMG-32 Reprimand'):attack('primary'),", w.LuaPreview);
        Assert.Contains("field=hd2.fields.attack.projectile,", w.LuaPreview);
    }

    [Fact] public async Task Object_edits_cannot_follow_an_attack_output_and_are_discarded_only_on_request()
    {
        using var e = new TestEnvironment(); var (w, _) = await EnemyAuthoringTests.Fresh(e);
        await w.SetObjectScalarAsync(Liberator, "primary", "projectile", null, "damage.standard_damage", "120", true);
        await Assert.ThrowsAsync<InvalidDataException>(() => w.SetAttackOutputAsync(Liberator, "primary", Napalm));
        Assert.Single(w.Project!.CompositionChanges); Assert.Null(w.Project.AttackOutputChanges);
        await w.SetAttackOutputAsync(Liberator, "primary", Napalm, discardObjectEdits: true);
        Assert.Empty(w.Project.CompositionChanges); Assert.Single(w.Project.AttackOutputChanges!); Assert.Null(w.BuildError);
    }

    [Fact] public async Task Attack_outputs_are_validated_as_semantic_identities()
    {
        using var e = new TestEnvironment(); var (w, sdk) = await EnemyAuthoringTests.Fresh(e);
        await w.SetAttackOutputAsync(Liberator, "primary", Napalm);
        var p = w.Project!; var good = p.AttackOutputChanges![0];
        foreach (var bad in new[] { good with { AttackRole = "Primary!" }, good with { Output = "projectile/eat-700" }, good with { Mechanism = "raw" }, good with { Acknowledgements = ["allow_everything"] } })
        {
            p.AttackOutputChanges = [bad]; Assert.Throws<InvalidDataException>(() => ProjectIdentity.Validate(p));
        }
        p.AttackOutputChanges = [good]; ProjectIdentity.Validate(p);
    }

    // ---- other 0.28.0 domains ---------------------------------------------------------------------------------------------------------

    [Fact] public async Task Resupply_is_a_mission_stratagem_with_cooldown_uses_and_its_shared_drop_pod()
    {
        using var e = new TestEnvironment(); var (w, sdk) = await EnemyAuthoringTests.Fresh(e);
        var resupply = sdk.Stratagems!.Stratagems.Single(s => s.Name == "Resupply");
        Assert.Equal("mission", resupply.Family);
        Assert.Contains(Navigation.Build(sdk, w.Project), i => i.Page == "stratagems:mission" && i.Count == 1 && i.CssClass == "cat-mission");
        var fields = sdk.Stratagems.FieldInstances.Where(f => f.Target.Stratagem == "Resupply").ToArray();
        await w.SetStratagemAsync(fields.Single(f => f.SemanticFieldId == "stratagem.cooldown").InstanceKey, "120");
        await w.SetStratagemAsync(fields.Single(f => f.SemanticFieldId == "stratagem.max_uses").InstanceKey, "10");
        var pods = sdk.Entities!.Pods!; var rack = Assert.Single(pods.ForStratagem(resupply.SemanticId));
        Assert.True(rack.Shared); Assert.Equal(4, rack.SpawnCount.Value);
        var grenades = pods.Pickups.Single(p => p.Name == "Grenade Box");
        foreach (var slot in rack.Slots.Where(s => s.Writable))
        { Assert.Equal("Supply Box", slot.Current!.Name); Assert.True(pods.LiveVerifiedPair(rack, slot, grenades)); Assert.Contains("allow_shared", slot.Acknowledgements); }
        await w.SetEntityAsync(PodPayloadReader.SlotKey(rack, 1), grenades.SemanticId);
        Assert.Null(w.BuildError); var lua = w.LuaPreview;
        Assert.Contains("hd2.stratagem('Resupply')", lua); Assert.Contains("hd2.pod_rack(", lua);
        Assert.Contains("allow_shared=true", lua); Assert.Contains("allow_unverified_reference=true", lua);
        await w.OpenAsync(w.Project!.Id); Assert.Null(w.BuildError); Assert.Equal(lua, w.LuaPreview);
    }

    [Fact] public async Task The_ballistic_shield_is_armored_through_its_shield_zone_not_its_dormant_default_armor()
    {
        using var e = new TestEnvironment(); var (w, sdk) = await EnemyAuthoringTests.Fresh(e);
        var backpack = sdk.Entities!.Backpacks!.Find(Shield)!;
        Assert.Contains(backpack.DamageZones!, z => z.ZoneId == "zone_0" && z.Name == "shield");
        var main = sdk.Entities.Backpacks.FieldInstances.Single(f => f.InstanceKey == ShieldMainArmor);
        Assert.False(main.Editable); Assert.Equal(FieldEffect.Dormant, main.Effect!.ActiveSource); Assert.Contains("zone.armor", main.Effect.ActiveField);
        await Assert.ThrowsAnyAsync<Exception>(() => w.SetEntityAsync(ShieldMainArmor, "6"));
        var zone = sdk.Entities.Backpacks.FieldInstances.Single(f => f.InstanceKey == ShieldZoneArmor);
        Assert.True(zone.Editable); Assert.NotNull(zone.Effect!.DamageRule);
        await w.SetEntityAsync(ShieldZoneArmor, "6");
        Assert.Null(w.BuildError);
        Assert.Contains("target=hd2.backpack('SH-20 Ballistic Shield Backpack'):damage_zone('zone_0'),", w.LuaPreview);
        Assert.Contains("hd2.fields.zone.armor", w.LuaPreview); Assert.DoesNotContain("hd2.fields.entity.armor", w.LuaPreview);
        await w.OpenAsync(w.Project!.Id); Assert.Null(w.BuildError); Assert.Single(w.Project!.EntityChanges);
    }

    [Fact] public async Task Published_bounds_are_enforced_with_their_reason()
    {
        using var e = new TestEnvironment(); var (w, sdk) = await EnemyAuthoringTests.Fresh(e);
        var capacity = sdk.Entities!.Backpacks!.FieldInstances.Single(f => f.InstanceKey == MaxigunCapacity);
        Assert.Equal(1023, capacity.EffectiveRange!.Max); Assert.Contains("10 bits", capacity.EffectiveRange.Reason);
        var refused = await Assert.ThrowsAsync<InvalidDataException>(() => w.SetEntityAsync(MaxigunCapacity, "1024"));
        Assert.Contains("1023", refused.Message);
        await w.SetEntityAsync(MaxigunCapacity, "1023");
        Assert.Null(w.BuildError); Assert.Contains("value=1023", w.LuaPreview);
    }

    [Fact] public async Task The_halt_writes_each_feed_with_its_branch_qualified_field_constant()
    {
        using var e = new TestEnvironment(); var (w, _) = await EnemyAuthoringTests.Fresh(e);
        Assert.Contains(w.ObjectFields("SG-20 Halt", "feed_primary", "projectile"), f => f.SemanticFieldId == "damage.primary.standard_damage");
        Assert.Contains(w.ObjectFields("SG-20 Halt", "feed_alternate", "projectile"), f => f.SemanticFieldId == "damage.alternate.standard_damage");
        Assert.DoesNotContain(w.ObjectFields("SG-20 Halt", "feed_primary", "projectile"), f => f.SemanticFieldId.Contains(".alternate."));
        await w.SetObjectScalarAsync("SG-20 Halt", "feed_primary", "projectile", null, "damage.primary.standard_damage", "40", true);
        await w.SetObjectScalarAsync("SG-20 Halt", "feed_alternate", "projectile", null, "damage.alternate.standard_damage", "300", true);
        Assert.Null(w.BuildError); var lua = w.LuaPreview;
        Assert.Contains("target=hd2.weapon('SG-20 Halt'):attack('feed_primary'):projectile(),", lua);
        Assert.Contains("field=hd2.fields.damage.primary_standard_damage,", lua);
        Assert.Contains("field=hd2.fields.damage.alternate_standard_damage,", lua);
        Assert.DoesNotContain("hd2.fields.damage.player_standard_damage", lua);
        // Single-feed weapons keep the generic constant their target publishes.
        await w.SetObjectScalarAsync(Liberator, "primary", "projectile", null, "damage.standard_damage", "100", true);
        Assert.Contains("field=hd2.fields.damage.player_standard_damage,", w.LuaPreview);
    }

    // The published 0.27.0 SDK already publishes the branch-qualified constants; with them the Halt's feed edits also work on Runtime
    // 0.27.0, where the generic name failed ("field is not exposed for SG-20 Halt", HD2Runtime user report 2026-09-29).
    [Fact] public async Task Halt_projects_on_the_published_0_27_sdk_use_the_branch_qualified_field()
    {
        using var e = new TestEnvironment(); var sdk = await SdkFixtures.Install(e, "0.27.0"); var w = e.Workspace();
        await w.CreateAsync(new("Halt", "Tests", "mods/tests/halt027", "0.1.0"), sdk);
        await w.SetObjectScalarAsync("SG-20 Halt", "feed_primary", "projectile", null, "damage.primary.standard_damage", "40", true);
        Assert.Null(w.BuildError);
        Assert.Contains("field=hd2.fields.damage.primary_standard_damage,", w.LuaPreview);
        Assert.DoesNotContain("hd2.fields.damage.player_standard_damage", w.LuaPreview);
    }

    [Fact] public async Task Every_generated_operation_is_emitted_once()
    {
        using var e = new TestEnvironment(); var (w, sdk) = await EnemyAuthoringTests.Fresh(e);
        await w.SetObjectScalarAsync("SG-20 Halt", "feed_primary", "projectile", null, "damage.primary.standard_damage", "40", true);
        await w.SetEntityAsync(ShieldZoneArmor, "6");
        await w.SetAttackOutputAsync(Liberator, "primary", DeEscalator);
        await w.AddCustomLuaAsync();
        Assert.Null(w.BuildError);
        var ids = Ids(w.LuaPreview); Assert.Equal(ids.Distinct().Count(), ids.Length); Assert.True(ids.Length >= 3);
    }

    // ---- compatibility ----------------------------------------------------------------------------------------------------------------

    [Fact] public async Task Projects_without_the_new_features_keep_their_format_and_older_projects_still_open()
    {
        using var e = new TestEnvironment(); var (w, sdk) = await EnemyAuthoringTests.Fresh(e);
        await w.SetEntityAsync(ShieldZoneArmor, "6");
        var format = w.Project!.FormatVersion; Assert.True(format < 11);
        var json = await File.ReadAllTextAsync(e.Paths.ProjectFile(w.Project.Id));
        Assert.DoesNotContain("customLua", json); Assert.DoesNotContain("attackOutputChanges", json);
        await w.OpenAsync(w.Project.Id); Assert.Equal(format, w.Project!.FormatVersion); Assert.Null(w.BuildError);
        // A project written by the published 0.27.0 SDK opens unchanged in this build.
        using var old = new TestEnvironment(); var published = await SdkFixtures.Install(old, "0.27.0");
        var ow = old.Workspace(); await ow.CreateAsync(new("Old", "Tests", "mods/tests/old", "0.1.0"), published);
        Assert.Null(published.Events); Assert.Null(published.AttackOutputs); Assert.NotNull(published.LuaApi);
        await ow.SetObjectScalarAsync(Liberator, "primary", "projectile", null, "damage.standard_damage", "100", true);
        var oldLua = ow.LuaPreview; Assert.Null(ow.BuildError);
        await ow.OpenAsync(ow.Project!.Id); Assert.Equal(oldLua, ow.LuaPreview); Assert.Null(ow.Project!.CustomLua); Assert.Null(ow.Project.AttackOutputChanges);
    }

    // ---- developer SDK setting --------------------------------------------------------------------------------------------------------

    [Fact] public async Task A_remembered_local_sdk_is_used_after_the_command_line_and_environment()
    {
        using var e = new TestEnvironment();
        var checkout = Path.Combine(e.Paths.Root, "HD2Runtime"); var sdk = Path.Combine(checkout, "sdk");
        Directory.CreateDirectory(sdk); await File.WriteAllTextAsync(Path.Combine(sdk, "metadata.json"), "{}");
        // An HD2Runtime checkout resolves to its sdk folder; a missing path or a non-SDK folder is refused with a reason.
        Assert.Null(DeveloperSdk.Normalize(checkout, out var normalized)); Assert.Equal(sdk, normalized);
        Assert.NotNull(DeveloperSdk.Normalize(Path.Combine(e.Paths.Root, "nowhere"), out _));
        Assert.NotNull(DeveloperSdk.Normalize(e.Paths.Root, out _)); Assert.NotNull(DeveloperSdk.Normalize("", out _));
        Assert.Null(DeveloperSdk.Load(e.Paths).LocalSdkPath);
        await DeveloperSdk.SaveAsync(e.Paths, new(sdk));
        var saved = DeveloperSdk.Load(e.Paths); Assert.Equal(sdk, saved.LocalSdkPath);
        Assert.Equal(new LocalSdkChoice(sdk, LocalSdkChoice.Settings), DeveloperSdk.Resolve(["app.exe"], null, saved));
        Assert.Equal(LocalSdkChoice.Environment, DeveloperSdk.Resolve(["app.exe"], checkout, saved)!.Source);
        Assert.Equal(LocalSdkChoice.CommandLine, DeveloperSdk.Resolve(["app.exe", "--sdk-path=" + checkout], sdk, saved)!.Source);
        Assert.Equal(Path.GetFullPath(checkout), DeveloperSdk.Resolve(["app.exe", "--sdk-path", checkout], null, saved)!.Path);
        // A remembered path that disappeared is ignored, so the published SDKs are used.
        Directory.Delete(checkout, true); Assert.Null(DeveloperSdk.Resolve(["app.exe"], null, saved));
        // The Runtime commit shown next to a local SDK is read from its checkout.
        Directory.CreateDirectory(Path.Combine(checkout, ".git", "refs", "heads")); Directory.CreateDirectory(sdk);
        await File.WriteAllTextAsync(Path.Combine(checkout, ".git", "HEAD"), "ref: refs/heads/main\n");
        await File.WriteAllTextAsync(Path.Combine(checkout, ".git", "refs", "heads", "main"), "e15d5bf0123456789abcdef0123456789abcdef0\n");
        Assert.Equal("e15d5bf", DeveloperSdk.RuntimeCommit(sdk));
        await File.WriteAllTextAsync(DeveloperSdk.SettingsPath(e.Paths), "{ not json");
        Assert.Null(DeveloperSdk.Load(e.Paths).LocalSdkPath);
    }
}
