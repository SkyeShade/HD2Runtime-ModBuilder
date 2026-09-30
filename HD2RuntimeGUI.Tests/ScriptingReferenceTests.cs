using System.IO.Compression;
using System.Text.Json;
using System.Text.RegularExpressions;
using HD2RuntimeGUI.Core.Localization;
using HD2RuntimeGUI.Core.Metadata;
using HD2RuntimeGUI.Core.Scripting;
using HD2RuntimeGUI.Core.Services;
using Xunit;

namespace HD2RuntimeGUI.Tests;

// Event scripting against the frozen HD2Runtime 0.28.0 SDK (Fixtures/sdk-0.28.0.zip): every event, payload field, handle and action of
// EventCatalog.json reaches the reference and the pickers with its availability and live evidence; the new player_hit and
// player_damage_dealt events have payload docs and snippets; completion covers every class and member of the LuaLS stub (attack outputs,
// the projectile builder, feeds, underbarrels, linked backpacks, diagnostics, compatibility); the diagnostics reference is built from the
// stub; and nothing ModBuilder generates, inserts or offers turns Runtime telemetry on.
public sealed class ScriptingReferenceTests
{
    private const string Liberator = "AR-23 Liberator", DeEscalator = "output/v1/projectile/gl-52-de-escalator";
    private const string ShieldZoneArmor = "backpack:sh-20-ballistic-shield-backpack:sh-20-ballistic-shield-backpack-damage-zone-zone-0:zone.armor";
    private static JsonElement RawCatalog(TestEnvironment e) => JsonDocument.Parse(File.ReadAllBytes(Path.Combine(EnemyAuthoringTests.DevSdk(e), EventCatalogReader.FileName))).RootElement;
    private static string RawStub(TestEnvironment e) => File.ReadAllText(Path.Combine(EnemyAuthoringTests.DevSdk(e), LuaApiIndex.StubPath));
    private static void Clean(string code, SdkMetadata sdk)
    {
        Assert.Empty(LuaParser.Diagnose(code));
        Assert.Empty(LuaScriptAnalyzer.Analyze(code, sdk.Events, sdk.Entities!.Enemies));
    }
    private static async Task<string> ExportedLua(BuilderWorkspace w)
    {
        await w.ExportAsync(); using var zip = ZipFile.OpenRead(w.LastExport!);
        using var reader = new StreamReader(zip.GetEntry("src/addon.lua")!.Open()); return await reader.ReadToEndAsync();
    }
    private static string Root()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
            if (Directory.Exists(Path.Combine(dir.FullName, "HD2RuntimeGUI", "Components"))) return dir.FullName;
        throw new DirectoryNotFoundException("Repository root not found.");
    }

    // ---- events and actions ------------------------------------------------------------------------------------------------------------

    [Fact] public async Task Every_catalog_event_reaches_the_reference_and_picker_with_its_availability()
    {
        using var e = new TestEnvironment(); var (_, sdk) = await EnemyAuthoringTests.Fresh(e); var catalog = sdk.Events!;
        var raw = RawCatalog(e).GetProperty("events").EnumerateArray().ToArray();
        Assert.Equal(20, raw.Length);
        Assert.Equal(raw.Select(x => x.GetProperty("name").GetString()), catalog.Events.Select(x => x.Name));
        foreach (var r in raw)
        {
            var ev = catalog.Event(r.GetProperty("name").GetString()!)!;
            Assert.Equal(r.GetProperty("status").GetString(), ev.Status);
            var handler = LuaSnippets.Handler(catalog, ev);
            if (ev.IsAvailable)
            {
                // Every payload field (with its type and the SDK's doc) is in the reference and listed in the inserted subscription, which checks clean.
                var payload = r.GetProperty("payload").EnumerateArray().ToArray();
                Assert.Equal(payload.Select(p => (p.GetProperty("name").GetString(), p.GetProperty("type").GetString(), p.GetProperty("doc").GetString())),
                    ev.Payload!.Select(p => ((string?)p.Name, (string?)p.Type, p.Doc)));
                Assert.NotNull(handler); Assert.StartsWith("hd2.events.on('" + ev.Name + "', function(event)\n", handler);
                Assert.All(ev.Payload!, p => Assert.Contains("event." + p.Name, handler));
                Clean(handler!, sdk);
            }
            else
            {
                // Blocked events stay listed with Runtime's reason and offer no subscription; subscribing is flagged.
                Assert.Null(handler); Assert.False(string.IsNullOrWhiteSpace(ev.Reason));
                Assert.Contains(LuaScriptAnalyzer.Analyze("hd2.events.on('" + ev.Name + "', function() end)\n", catalog), d => d.Code == "event" && d.Message.Contains(ev.Reason!));
            }
        }
        Assert.Equal(["entity_damage_pre"], catalog.Events.Where(x => !x.IsAvailable).Select(x => x.Name));
        // Autocomplete offers exactly the subscribable events.
        Assert.Equal(catalog.Events.Where(x => x.IsAvailable).Select(x => x.Name), LuaCompletion.Build(sdk).Strings["events"]);
    }

    [Fact] public async Task Every_catalog_action_is_offered_with_its_limits_proof_and_blocked_reason()
    {
        using var e = new TestEnvironment(); var (_, sdk) = await EnemyAuthoringTests.Fresh(e); var a = sdk.Events!.Actions;
        var raw = RawCatalog(e).GetProperty("actions");
        Assert.Equal(raw.GetProperty("explosions").GetProperty("weapons").EnumerateArray().Select(x => x.GetString()), a.Explosions.Weapons);
        Assert.Equal(raw.GetProperty("projectiles").GetProperty("weapons").EnumerateArray().Select(x => x.GetString()), a.Projectiles.Weapons);
        Assert.Equal(raw.GetProperty("statusEffects").GetProperty("statuses").EnumerateArray().Select(x => x.GetProperty("id").GetString()), a.StatusEffects.Statuses.Select(s => s.Id));
        Assert.True(a.Explosions.HostOnly && a.Explosions.InMissionOnly && a.Projectiles.HostOnly && a.StatusEffects.HostOnly);
        Assert.Equal(("blocked", raw.GetProperty("spawnEntity").GetProperty("reason").GetString()), (a.SpawnEntity.Status, a.SpawnEntity.Reason));
        // Each inserted call names the catalogued value verbatim and checks clean.
        foreach (var name in sdk.Events.ExplosionNames) Clean(LuaSnippets.Calls.Explosion(name), sdk);
        foreach (var weapon in a.Projectiles.Weapons) Clean(LuaSnippets.Calls.Projectile(weapon), sdk);
        foreach (var status in a.StatusEffects.Statuses) Clean(LuaSnippets.Calls.Status(status.Id), sdk);
        foreach (var code in new[] { LuaSnippets.Calls.Heal, LuaSnippets.Calls.PlayerHeal, LuaSnippets.Calls.EquippedWeapon }) Clean(code, sdk);
        // Heal and the weapon in hand are published API (hd2.actions.heal, player:heal, player:equipped_weapon).
        Assert.Contains(sdk.Events.Api.Classes["HD2Actions"].Methods!, m => m.Name == "heal");
        Assert.Contains(sdk.Events.Handles["HD2PlayerHandle"].Methods, m => m.Name == "heal");
        Assert.Contains(sdk.Events.Handles["HD2PlayerHandle"].Methods, m => m.Name == "equipped_weapon");
        // Options as published: projectile position/direction, status buildup and the refused strength.
        Assert.Equal(["direction", "position"], a.Projectiles.Options.Keys.Order());
        Assert.True(a.StatusEffects.Options.TryGetProperty("buildup", out _) && a.StatusEffects.Options.TryGetProperty("strength", out _));
    }

    [Fact] public async Task Live_evidence_is_linked_to_events_actions_and_handle_methods()
    {
        using var e = new TestEnvironment(); var (_, sdk) = await EnemyAuthoringTests.Fresh(e); var live = sdk.LiveEvidence!;
        string[] Families(string subject) => ScriptingReference.Evidence(live, subject).Where(x => x.LiveProven).Select(x => x.Family).ToArray();
        Assert.Equal(["event_action_heal"], Families("hd2.actions.heal"));
        Assert.Equal(["event_damage_source_attribution"], Families("player_damage_dealt"));
        Assert.Equal(["event_weapon_in_hand"], Families("weapon_changed"));
        Assert.Equal(["event_weapon_in_hand"], Families(ScriptingReference.HandleSubject("HD2PlayerHandle", "equipped_weapon")));
        Assert.Equal(["event_player_died_position"], Families("player_died"));
        Assert.Equal(["event_action_explosion_named"], Families(sdk.Events!.Actions.Explosions.Api));
        Assert.Equal(["event_action_projectile"], Families(sdk.Events.Actions.Projectiles.Api));
        Assert.Equal(["event_action_status"], Families(sdk.Events.Actions.StatusEffects.Api));
        // player_hit is not promoted by any live test (the damage-attribution family lists it as not proven).
        Assert.Empty(ScriptingReference.Evidence(live, "player_hit"));
        Assert.Contains("player_hit", live.Family("event_damage_source_attribution")!.NotPromoted!);
        // Every event and event-action family is reachable from a subject the reference or the pickers show.
        var subjects = sdk.Events.Events.Select(x => x.Name).Concat([sdk.Events.Actions.Explosions.Api, sdk.Events.Actions.Projectiles.Api, sdk.Events.Actions.StatusEffects.Api, "hd2.actions.heal"])
            .Concat(sdk.Events.Handles.SelectMany(h => h.Value.Methods.Select(m => ScriptingReference.HandleSubject(h.Key, m.Name)))).ToArray();
        Assert.All(live.Families.Where(f => f.Value.Domain is "events" or "event_actions"), f => Assert.Contains(subjects, s => ScriptingReference.Evidence(live, s).Any(x => x.Family == f.Key)));
    }

    [Fact] public async Task Player_hit_and_player_damage_dealt_document_their_payloads_and_sources()
    {
        using var e = new TestEnvironment(); var (w, sdk) = await EnemyAuthoringTests.Fresh(e); var catalog = sdk.Events!;
        foreach (var (name, count) in new[] { ("player_hit", "hits"), ("player_damage_dealt", "damage") })
        {
            var ev = catalog.Event(name)!;
            Assert.True(ev.IsAvailable); Assert.Equal(("stats", "post", false), (ev.Source, ev.Phase, ev.Hot));
            Assert.Equal(["player", "local_player", count, "total", "sources", "unattributed"], ev.Payload!.Select(p => p.Name));
            Assert.Equal("HD2StatSource[]", ev.Payload!.Single(p => p.Name == "sources").Type);
            // Each source record lists only the fields this event fills.
            var sources = ScriptingReference.Detail(catalog, ev, ev.Payload!.Single(p => p.Name == "sources"));
            Assert.Equal(["type", "name", count], sources.Select(f => f[0]));
            var handler = LuaSnippets.Handler(catalog, ev)!;
            Assert.Contains("for _, source in ipairs(event.sources) do", handler); Assert.Contains("source." + count, handler);
            Assert.DoesNotContain("source.kills", handler);
        }
        Assert.Contains("player_damage_dealt", catalog.Event("player_hit")!.Summary); // thrown knives are reported by player_damage_dealt only
        // Snippets for both events, built from SDK names.
        var snippets = LuaSnippets.For(sdk, w.Project!.ResourceId);
        var accuracy = snippets.Single(s => s.Id == "player-hit-accuracy");
        Assert.Contains("hd2.events.on('player_hit'", accuracy.Code); Assert.Contains("hd2.events.on('player_fired'", accuracy.Code);
        var heal = snippets.Single(s => s.Id == "damage-dealt-heal");
        Assert.NotNull(sdk.Entities!.Throwables!.Find("K-2 Throwing Knife"));
        Assert.Contains("local KNIFE = 'K-2 Throwing Knife'", heal.Code); Assert.Contains("hd2.events.on('player_damage_dealt'", heal.Code);
        Assert.Contains("hd2.actions.heal(25)", heal.Code);
        Assert.Contains("K-2 Throwing Knife", heal.Description);
    }

    // ---- snippets ----------------------------------------------------------------------------------------------------------------------

    [Fact] public async Task Snippets_compile_through_the_lua_parser_and_use_only_published_api()
    {
        using var e = new TestEnvironment(); var (w, sdk) = await EnemyAuthoringTests.Fresh(e);
        var snippets = LuaSnippets.For(sdk, w.Project!.ResourceId);
        Assert.Contains(snippets, s => s.Id == RuntimeDiagnostics.WriteConflictsSnippet);
        foreach (var s in snippets)
        {
            Clean(s.Code, sdk);
            Assert.False(string.IsNullOrWhiteSpace(s.Title)); Assert.False(string.IsNullOrWhiteSpace(s.Description));
            // Every API call a snippet names is in the stub or the catalog; every event it subscribes to is available.
            foreach (var use in s.Uses)
                Assert.True(sdk.Events!.Event(use)?.IsAvailable == true || use == "mod:value" && sdk.LuaApi!.Resolve("HD2ModContext:value") != null || sdk.LuaApi!.Resolve(use) != null, s.Id + ": " + use);
            foreach (Match m in Regex.Matches(s.Code, @"hd2\.events\.on\('(\w+)'")) Assert.True(sdk.Events!.Event(m.Groups[1].Value)!.IsAvailable);
        }
        // Localized titles and descriptions; the code is identical in every UI language.
        using (UiCulture.Use(UiLanguages.Find("zh-Hans")!))
        {
            var zh = LuaSnippets.For(sdk, w.Project.ResourceId);
            Assert.Equal(snippets.Select(s => s.Code), zh.Select(s => s.Code));
            Assert.NotEqual(snippets.Single(s => s.Id == "player-hit-accuracy").Title, zh.Single(s => s.Id == "player-hit-accuracy").Title);
        }
    }

    // ---- completion --------------------------------------------------------------------------------------------------------------------

    [Fact] public async Task Completion_covers_every_class_and_member_of_the_frozen_stub()
    {
        using var e = new TestEnvironment(); var (_, sdk) = await EnemyAuthoringTests.Fresh(e);
        var api = sdk.LuaApi!; var completion = LuaCompletion.Build(sdk); var stub = RawStub(e);
        Assert.Equal("HD2Runtime", completion.Root);
        // Every annotated class, field and function of the stub (nested hd2.compatibility.* included).
        string? current = null;
        foreach (var line in stub.Split('\n').Select(l => l.TrimEnd('\r')))
        {
            Match m;
            if ((m = Regex.Match(line, @"^---@class\s+([A-Za-z_][\w.]*)")).Success) { current = m.Groups[1].Value; Assert.True(completion.Classes.ContainsKey(current), current); }
            else if ((m = Regex.Match(line, @"^---@field\s+([A-Za-z_]\w*)")).Success && current != null) Assert.Contains(completion.Classes[current], x => x.N == m.Groups[1].Value);
            else if ((m = Regex.Match(line, @"^function\s+([A-Za-z_][\w.]*)[.:]([A-Za-z_]\w*)\s*\(")).Success)
            {
                var owner = m.Groups[1].Value == "hd2" ? completion.Root : m.Groups[1].Value.StartsWith("hd2.", StringComparison.Ordinal)
                    ? api.Resolve(m.Groups[1].Value) is { } table ? LuaApiIndex.FirstType(table.Type)! : m.Groups[1].Value : m.Groups[1].Value;
                Assert.True(completion.Classes[owner].Any(x => x.N == m.Groups[2].Value && x.K != "field"), line);
            }
        }
        // The 0.28.0 additions, reachable from hd2.
        var root = completion.Classes[completion.Root];
        Assert.Contains(root, x => x.N == "diagnostics" && x.T == "HD2Diagnostics");
        Assert.Contains(root, x => x.N == "compatibility" && x.T == "hd2.compatibility");
        Assert.Equal(["telemetry", "write_conflicts"], completion.Classes["HD2Diagnostics"].Select(x => x.N));
        Assert.Contains(completion.Classes["hd2.compatibility"], x => x.N == "require_runtime" && x.S == "require_runtime(mod: string, minimum: string, display: string?) → boolean, string?");
        var output = root.Single(x => x.N == "attack_output");
        Assert.Equal(("HD2AttackOutput", "HD2AttackOutputId"), (output.T, output.P));
        Assert.Contains("output/v1/projectile/gl-52-de-escalator", completion.Aliases["HD2AttackOutputId"]); Assert.Contains(Liberator, completion.Aliases["HD2AttackOutputId"]);
        Assert.Contains(root, x => x.N == "attack_outputs");
        var weapon = completion.Classes["HD2Weapon"];
        foreach (var member in new[] { "programmable_ammo", "underbarrel", "feed", "feeds", "fire_rate_modes", "fire_rate_mode", "presentation", "attack", "ammunition" })
            Assert.Contains(weapon, x => x.N == member && x.K == "method");
        Assert.Equal("HD2ProjectileBuilder", weapon.Single(x => x.N == "programmable_ammo").T);
        Assert.Equal("HD2Subweapon", weapon.Single(x => x.N == "underbarrel").T);
        Assert.Equal("\"primary\"|\"alternate\"|\"programmable\"|integer", weapon.Single(x => x.N == "feed").P);
        Assert.Equal(["bases", "describe", "operations", "presentation"], completion.Classes["HD2ProjectileBuilder"].Select(x => x.N));
        Assert.Contains(completion.Classes["HD2Feed"], x => x.N == "projectile"); Assert.Contains(completion.Classes["HD2Feed"], x => x.N == "source");
        Assert.Contains(completion.Classes["HD2Backpack"], x => x.N == "drone" && x.T == "HD2BackpackLinked");
        Assert.Contains(completion.Classes["HD2Backpack"], x => x.N == "energy_shield" && x.T == "HD2BackpackLinked");
        foreach (var host in new[] { "HD2SupportAttack", "HD2SupportWeapon", "HD2VehicleWeapon", "HD2VehicleWeaponAttack" }) Assert.Contains(completion.Classes[host], x => x.N == "projectile_source");
        foreach (var m in new[] { "direct_damage", "impact_explosion", "expiry_explosion", "mode_labels", "mode_icons", "mode_icon_for" })
            Assert.Contains(completion.Classes["HD2AttackOutput"], x => x.N == m);
        Assert.Equal("HD2AttackOutputSlot", completion.Classes["HD2AttackOutput"].Single(x => x.N == "impact_explosion").T);
        // Event payload classes carry the common HD2Event fields (inherited), and their own.
        foreach (var ev in new[] { "player_hit", "player_damage_dealt" })
            foreach (var field in new[] { "event", "time", "frame", "mission", "cause", "sources", "total", "unattributed" })
                Assert.Contains(completion.Classes["HD2Event_" + ev], x => x.N == field);
        Assert.Contains(completion.Classes["HD2StatSource"], x => x.N == "hits"); Assert.Contains(completion.Classes["HD2StatSource"], x => x.N == "damage");
        Assert.Contains(completion.Classes["HD2ScriptValue"], x => x.N == "set"); // HD2ScriptValue : HD2Option
        // Types are read whole (function types, tables, literal unions); multiple returns are listed; class docs stay off the first field.
        Assert.Equal("fun(binding: table)|nil", completion.Classes["HD2BindingSpec"].Single(x => x.N == "on_press").T);
        Assert.Equal("mission_id() → integer, boolean", completion.Classes["HD2Events"].Single(x => x.N == "mission_id").S);
        Assert.Null(completion.Classes["HD2Vector3"].Single(x => x.N == "x").D);
        Assert.Equal("A world position snapshot (metres).", api.ClassDoc("HD2Vector3"));
        // Catalog strings for the first argument of the event and action calls.
        Assert.Contains("player_hit", completion.Strings["events"]); Assert.Contains("player_damage_dealt", completion.Strings["events"]);
        Assert.Contains("Hellbomb", completion.Strings["explosions"]); Assert.Contains("fire", completion.Strings["statuses"]);
    }

    [Fact] public async Task Completion_without_a_stub_falls_back_to_the_event_catalog_api_and_handles()
    {
        using var e = new TestEnvironment(); var (_, sdk) = await EnemyAuthoringTests.Fresh(e);
        var completion = LuaCompletion.Build(sdk with { LuaApi = null });
        Assert.Equal("hd2", completion.Root);
        Assert.Contains(completion.Classes["hd2"], x => x.N == "events" && x.T == "HD2Events");
        Assert.Contains(completion.Classes["HD2PlayerHandle"], x => x.N == "equipped_weapon");
        Assert.Contains(completion.Classes["HD2StatSource"], x => x.N == "damage");
        Assert.Contains(completion.Classes["HD2Actions"], x => x.N == "heal" && x.P == "number");
    }

    // ---- diagnostics -------------------------------------------------------------------------------------------------------------------

    [Fact] public async Task The_diagnostics_reference_is_built_from_the_stub_and_documents_the_opt_in_only()
    {
        using var e = new TestEnvironment(); var (_, sdk) = await EnemyAuthoringTests.Fresh(e); var api = sdk.LuaApi!;
        Assert.True(RuntimeDiagnostics.Available(api));
        var sections = RuntimeDiagnostics.Sections(api);
        Assert.Equal([RuntimeDiagnostics.WriteConflictsId, RuntimeDiagnostics.TelemetryId, RuntimeDiagnostics.LogsId, RuntimeDiagnostics.StatusId], sections.Select(s => s.Id));
        var conflicts = sections[0].Calls.Single();
        Assert.Equal("hd2.diagnostics.write_conflicts() → table[]", conflicts.Signature);
        Assert.Contains("{operation, target, externalChanges, warnings, windowSeconds}", conflicts.Member.Doc);
        Assert.Equal(RuntimeDiagnostics.WriteConflictsSnippet, sections[0].Snippet);
        Assert.Contains("possible write conflict", sections[0].LogLines.Single());
        var telemetry = sections[1];
        Assert.Equal("hd2.diagnostics.telemetry(options: {enabled?: boolean, report_seconds?: number}) → table", telemetry.Calls.Single().Signature);
        Assert.Contains("off by default", telemetry.Calls.Single().Member.Doc);
        Assert.Equal("hd2.diagnostics.telemetry({enabled=true, report_seconds=60})", telemetry.Example);
        Assert.Null(telemetry.Snippet);
        Assert.Equal("mod:log(message: string) → nil", sections[2].Calls.Single().Signature);
        Assert.Contains(sections[2].LogLines, l => l.StartsWith("[ModBuilder] operation skipped: ", StringComparison.Ordinal));
        Assert.Equal(["hd2.events.status", "hd2.actions.status", "hd2.metrics", "hd2.compatibility.status", "hd2.compatibility.incompatible", "HD2Subscription:describe"],
            sections[3].Calls.Select(c => c.Path));
        Assert.All(sections.SelectMany(s => s.Calls), c => Assert.False(string.IsNullOrWhiteSpace(c.Member.Signature)));
        // Every section's UI text exists in English and Simplified Chinese.
        foreach (var key in new[] { "Diagnostics.Intro", "Diagnostics.WriteConflicts.Title", "Diagnostics.WriteConflicts.How.Html", "Diagnostics.WriteConflicts.Reading",
            "Diagnostics.Telemetry.Title", "Diagnostics.Telemetry.Never", "Diagnostics.Telemetry.Report.Html", "Diagnostics.Logs.Title", "Diagnostics.Status.Title", "CustomLua.Tab.Diagnostics" })
        {
            Assert.NotNull(TextResources.Find(key, System.Globalization.CultureInfo.InvariantCulture));
            Assert.NotEqual(TextResources.Find(key, System.Globalization.CultureInfo.InvariantCulture), TextResources.Find(key, UiLanguages.Find("zh-Hans")!.Culture));
        }
        // An SDK without the diagnostics declarations (the published 0.27.0 stub) gets no calls and the reference says so.
        using var old = new TestEnvironment(); var published = await SdkFixtures.Install(old, "0.27.0");
        Assert.False(RuntimeDiagnostics.Available(published.LuaApi));
        Assert.DoesNotContain(RuntimeDiagnostics.Sections(published.LuaApi), s => s.Id is RuntimeDiagnostics.WriteConflictsId or RuntimeDiagnostics.TelemetryId);
    }

    [Fact] public async Task No_code_path_turns_telemetry_on()
    {
        using var e = new TestEnvironment(); var (w, sdk) = await EnemyAuthoringTests.Fresh(e); var catalog = sdk.Events!;
        // A project with generated operations and custom Lua holding every snippet, handler and picker insert.
        await w.SetObjectScalarAsync("SG-20 Halt", "feed_primary", "projectile", null, "damage.primary.standard_damage", "40", true);
        await w.SetEntityAsync(ShieldZoneArmor, "6");
        await w.SetAttackOutputAsync(Liberator, "primary", DeEscalator);
        var inserts = LuaSnippets.For(sdk, w.Project!.ResourceId).Select(s => s.Code)
            .Concat(catalog.Events.Select(x => LuaSnippets.Handler(catalog, x)).OfType<string>())
            .Concat([LuaSnippets.Calls.Heal, LuaSnippets.Calls.PlayerHeal, LuaSnippets.Calls.EquippedWeapon]).ToArray();
        Assert.All(inserts, code => Assert.DoesNotContain("telemetry", code));
        await w.AddCustomLuaAsync();
        await w.SaveCustomLuaAsync(w.Project.CustomLua!.Source + string.Join("\n", inserts));
        Assert.Null(w.BuildError);
        Assert.DoesNotContain("telemetry", w.LuaPreview);
        Assert.DoesNotContain("telemetry", await ExportedLua(w));
        Assert.DoesNotContain("telemetry", BuilderWorkspace.StarterLua);
        // The only telemetry opt-in in ModBuilder's sources is the documented example the diagnostics reference displays (and the
        // analyzer's description of what it warns about); generation, the editor script and the components never contain one.
        var root = Root(); var optIn = new Regex(@"telemetry\s*\(\s*\{");
        var sources = new[] { "HD2RuntimeGUI.Core", "HD2RuntimeGUI" }.SelectMany(d => Directory.EnumerateFiles(Path.Combine(root, d), "*.*", SearchOption.AllDirectories))
            .Where(f => (f.EndsWith(".cs") || f.EndsWith(".razor") || f.EndsWith(".js")) && !f.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar)
                && !f.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar)).ToArray();
        Assert.NotEmpty(sources);
        Assert.Equal(["LuaScriptAnalyzer.cs", "ScriptingReference.cs"], sources.Where(f => optIn.IsMatch(File.ReadAllText(f))).Select(Path.GetFileName).Order(StringComparer.Ordinal));
        // Enabling telemetry in custom Lua is the modder's explicit choice, and the checks warn about it (without blocking the build).
        var found = LuaScriptAnalyzer.Analyze(RuntimeDiagnostics.TelemetryOptIn + "\n", catalog);
        var warning = Assert.Single(found); Assert.Equal(("telemetry", LuaDiagnostic.Warning), (warning.Code, warning.Severity));
        Assert.Contains("hd2.diagnostics.telemetry", warning.Message);
        Assert.Empty(LuaScriptAnalyzer.Analyze("local state = hd2.diagnostics.telemetry()\nhd2.diagnostics.telemetry({enabled = false})\n", catalog));
    }

    // ---- the generated file ------------------------------------------------------------------------------------------------------------

    [Fact] public async Task The_generated_operation_wrapper_and_custom_lua_check_clean()
    {
        using var e = new TestEnvironment(); var (w, sdk) = await EnemyAuthoringTests.Fresh(e);
        await w.SetObjectScalarAsync("SG-20 Halt", "feed_primary", "projectile", null, "damage.primary.standard_damage", "40", true);
        await w.SetAttackOutputAsync(Liberator, "primary", DeEscalator);
        await w.AddCustomLuaAsync();
        await w.SaveCustomLuaAsync(w.Project!.CustomLua!.Source + string.Join("\n", LuaSnippets.For(sdk, w.Project.ResourceId).Select(s => s.Code)));
        Assert.Null(w.BuildError);
        var lua = w.LuaPreview;
        Assert.Contains("local function add(build)\n    local ok,operation=pcall(build)\n", lua);
        Assert.Contains("add(function() return hd2.", lua); Assert.Contains("local function addon(...)\n", lua);
        Assert.Empty(LuaParser.Diagnose(lua));
        Assert.Empty(LuaScriptAnalyzer.Analyze(lua, sdk.Events, sdk.Entities!.Enemies));
        // Without custom Lua the wrapper collects `operations`, and still checks clean.
        await w.SetCustomLuaEnabledAsync(false);
        Assert.Contains("local operations={}\nlocal function add(build)", w.LuaPreview);
        Assert.Empty(LuaScriptAnalyzer.Analyze(w.LuaPreview, sdk.Events, sdk.Entities.Enemies));
    }
}
