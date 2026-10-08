using System.Diagnostics;
using System.IO.Compression;
using System.Text.Json;
using System.Text.RegularExpressions;
using HD2RuntimeGUI.Core.Localization;
using HD2RuntimeGUI.Core.Metadata;
using HD2RuntimeGUI.Core.Scripting;
using Xunit;

namespace HD2RuntimeGUI.Tests;

// HD2Runtime 0.30.0 scripting (docs/runtime030-integration-plan.md, Phase 8) on the pinned SDK: the 0.30 stub binds whole (the
// callable hd2.enemies table, hd2.resources, the new field constants, hd2.diagnostics.performance, hd2.version_label), completion reaches the
// new APIs in C# and in lua-editor.js, the event catalog's 20 new API classes and actions.pelican are modelled, the gameplay action pickers
// insert calls built from the published signatures (all of which check clean), the weapon sound catalogue is the SDK's byte for byte, the
// new live-evidence families show where they apply, and the live-proven stratagem writes have snippets.
public sealed class Runtime030ScriptingTests
{
    private static Task<(Core.Services.BuilderWorkspace W, SdkMetadata Sdk)> Fresh(TestEnvironment e) => ExportFixtureTests.Fresh(e, "Scripting 030");
    private static byte[] FixtureEntry(string name)
    {
        using var zip = ZipFile.OpenRead(Runtime030Tests.FixturePath); using var stream = zip.GetEntry(name)!.Open(); using var buffer = new MemoryStream();
        stream.CopyTo(buffer); return buffer.ToArray();
    }
    private static void Clean(string code, SdkMetadata sdk)
    {
        Assert.Empty(LuaParser.Diagnose(code));
        Assert.Empty(LuaScriptAnalyzer.Analyze(code, sdk.Events, sdk.Entities!.Enemies));
    }

    // ---- stub and completion ---------------------------------------------------------------------------------------------------------------

    [Fact] public async Task The_030_stub_binds_whole_and_hd2_enemies_is_a_callable_table()
    {
        using var e = new TestEnvironment(); var (_, sdk) = await Fresh(e);
        var api = sdk.LuaApi!; var completion = LuaCompletion.Build(sdk); var stub = System.Text.Encoding.UTF8.GetString(FixtureEntry(LuaApiIndex.StubPath));
        // Every annotated class, field and function of the stub.
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
        // A union type never becomes a class of its own; hd2.enemies is its class and keeps its call.
        Assert.DoesNotContain(completion.Classes.Keys, k => k.Contains('|') || k.Contains("fun(", StringComparison.Ordinal));
        var enemies = completion.Classes[completion.Root].Single(x => x.N == "enemies");
        Assert.Equal("HD2Enemies", LuaApiIndex.FirstType(enemies.T));
        Assert.Equal("enemies(filter?: {kind?: \"enemy\"|\"structure\", faction?: \"terminids\"|\"automatons\"|\"illuminate\"|\"neutral\"}) → string[]", enemies.S);
        Assert.Equal(["spawn_list", "spawn_status", "spawn_weight"], completion.Classes["HD2Enemies"].Select(x => x.N));
        Assert.NotNull(api.Resolve("hd2.enemies.spawn_weight"));
        Assert.Equal("HD2X", LuaApiIndex.FirstType("fun(a: \"x\"|\"y\"): nil|HD2X"));
        Assert.Equal(["HD2Enemies", "fun(filter?: {kind?: \"enemy\"|\"structure\"}): string[]"], LuaApiIndex.Union("HD2Enemies|fun(filter?: {kind?: \"enemy\"|\"structure\"}): string[]"));
        // The other 0.30 additions.
        Assert.Equal("HD2Image", api.Resolve("hd2.resources.image")!.Type); Assert.Equal("HD2Model", api.Resolve("hd2.resources.model")!.Type);
        Assert.Contains(completion.Classes["HD2Image"], x => x.N == "describe");
        Assert.Equal(["acceleration", "deceleration", "turn_speed"], completion.Classes["HD2Fields_rotation"].Select(x => x.N));
        Assert.Contains(completion.Classes["HD2Fields_gore"], x => x.N == "whole_body_gib_damage");
        Assert.Equal("HD2Fields_gore", LuaApiIndex.FirstType(api.Resolve("hd2.fields.gore")!.Type));
        Assert.NotNull(api.Resolve("hd2.fields.stratagem.calldown_code"));
        Assert.Contains(completion.Classes["HD2Diagnostics"], x => x.N == "performance" && x.K == "function");
        Assert.Contains(completion.Classes[completion.Root], x => x.N == "version_label" && x.T == "string");
        foreach (var path in new[] { "hd2.projectiles.homing", "hd2.pelican.spawn", "hd2.sounds.list", "hd2.sounds.describe", "hd2.actions.injure", "hd2.actions.heal_limb",
            "hd2.actions.add_velocity", "hd2.actions.resupply_from_pack", "HD2PlayerHandle:loadout", "HD2PlayerHandle:held_weapon", "HD2PlayerHandle:ammo" })
            Assert.True(api.Resolve(path) != null, path);
    }

    [Fact] public async Task The_editor_completes_the_030_apis()
    {
        using var e = new TestEnvironment(); var (_, sdk) = await Fresh(e);
        // lua-editor.js itself, under Node (the tools already need it): what the popup offers at the end of each text.
        string node;
        try { using var probe = Process.Start(new ProcessStartInfo("node", "--version") { RedirectStandardOutput = true, UseShellExecute = false })!; probe.WaitForExit(); node = "node"; }
        catch (System.ComponentModel.Win32Exception) { return; } // no Node on this machine: the C# completion data is checked above
        var root = AppContext.BaseDirectory; while (!Directory.Exists(Path.Combine(root, "HD2RuntimeGUI", "wwwroot"))) root = Path.GetDirectoryName(root)!;
        var data = Path.Combine(e.Paths.Root, "completion.json");
        File.WriteAllText(data, JsonSerializer.Serialize(LuaCompletion.Build(sdk), new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        var script = Path.Combine(e.Paths.Root, "complete.js");
        File.WriteAllText(script, """
            const fs = require('fs'); global.window = {}; global.document = {};
            eval(fs.readFileSync(process.argv[2], 'utf8'));
            const data = JSON.parse(fs.readFileSync(process.argv[3], 'utf8'));
            const out = {};
            for (const text of JSON.parse(process.argv[4])) { const c = window.hd2LuaEditor.completions(text, text.length, data); out[text] = c ? c.items.map(i => i.n) : null; }
            console.log(JSON.stringify(out));
            """);
        string[] texts = ["hd2.enemies.", "hd2.resources.", "hd2.diagnostics.", "hd2.fields.gore.", "hd2.fields.rotation.", "hd2.pelican.", "hd2.sounds.", "hd2.projectiles.",
            "local p = hd2.local_player()\np:", "hd2.ver"];
        var start = new ProcessStartInfo(node) { RedirectStandardOutput = true, UseShellExecute = false };
        foreach (var arg in new[] { Path.Combine(root, "HD2RuntimeGUI", "wwwroot", "lua-editor.js"), data, JsonSerializer.Serialize(texts) }) start.ArgumentList.Add(arg);
        script = script.Replace('\\', '/'); start.ArgumentList.Insert(0, script);
        using var run = Process.Start(start)!; var output = await run.StandardOutput.ReadToEndAsync(); await run.WaitForExitAsync();
        var result = JsonSerializer.Deserialize<Dictionary<string, string[]?>>(output)!;
        Assert.Contains("spawn_weight", result["hd2.enemies."]!);
        Assert.Equal(["image", "model"], result["hd2.resources."]!.Where(n => n is "image" or "model"));
        Assert.Contains("performance", result["hd2.diagnostics."]!);
        Assert.Contains("whole_body_gib_damage", result["hd2.fields.gore."]!);
        Assert.Contains("turn_speed", result["hd2.fields.rotation."]!);
        Assert.Contains("spawn", result["hd2.pelican."]!); Assert.Contains("list", result["hd2.sounds."]!); Assert.Contains("homing", result["hd2.projectiles."]!);
        foreach (var m in new[] { "loadout", "held_weapon", "ammo", "injure", "heal_limb", "add_velocity", "resupply_from_pack" }) Assert.Contains(m, result["local p = hd2.local_player()\np:"]!);
        Assert.Contains("version_label", result["hd2.ver"]!);
    }

    // ---- event catalog, live evidence ----------------------------------------------------------------------------------------------------

    [Fact] public async Task The_event_catalog_api_classes_and_the_pelican_action_reach_the_reference()
    {
        using var e = new TestEnvironment(); var (_, sdk) = await Fresh(e); var catalog = sdk.Events!;
        // b3f42ec adds the explosion event (source explosions, observed from the explosion queue).
        Assert.Equal(21, catalog.Events.Length);
        Assert.Contains(catalog.Events, ev => ev.Name == "explosion");
        foreach (var c in new[] { "HD2HomingOptions", "HD2HomingHandle", "HD2SpawnWeightOptions", "HD2SpawnWeightHandle", "HD2Enemies", "HD2PelicanOptions", "HD2PelicanEvent", "HD2Pelican",
            "HD2Pelicans", "HD2PelicanGun", "HD2PelicanOrbit", "HD2WeaponSound", "HD2SoundFilter", "HD2Sounds", "HD2CustomStratagemCarrier", "HD2CustomStratagemSpec", "HD2CustomStratagemCall",
            "HD2CustomStratagemWeapon", "HD2CustomStratagems", "HD2Ownership" })
            Assert.True(catalog.Api.Classes.ContainsKey(c), c);
        var pelican = catalog.Actions.Pelican!;
        Assert.Equal(("hd2.pelican.spawn", true, true, 4, 120d, 0.25, false), (pelican.Api, pelican.HostOnly, pelican.InMissionOnly, pelican.MaxActive, pelican.MaxHoverSeconds, pelican.RateLimit.PerSecond, pelican.LiveProven));
        // Methods are linked to their hd2 path, also where only the stub declares the field (hd2.enemies).
        Assert.Equal("hd2.enemies.spawn_weight", ScriptingReference.ApiPath(catalog, sdk.LuaApi, "HD2Enemies", "spawn_weight"));
        Assert.Equal("hd2.pelican.spawn", ScriptingReference.ApiPath(catalog, sdk.LuaApi, "HD2Pelicans", "spawn"));
        // Without a stub, completion falls back to the catalog's API classes, the new ones included.
        Assert.Contains(LuaCompletion.Build(sdk with { LuaApi = null }).Classes["HD2Pelicans"], x => x.N == "spawn");
    }

    [Fact] public async Task New_live_evidence_families_show_where_they_apply()
    {
        using var e = new TestEnvironment(); var (_, sdk) = await Fresh(e); var live = sdk.LiveEvidence!;
        string[] Families(string subject) => [.. ScriptingReference.Evidence(live, subject).Select(x => x.Family)];
        Assert.Equal([GameplayActions.HomingEvidence], Families(GameplayActions.Homing));
        Assert.Equal([GameplayActions.SpawnWeightEvidence], Families(GameplayActions.SpawnWeight));
        Assert.All(ScriptingReference.Evidence(live, GameplayActions.Homing).Concat(ScriptingReference.Evidence(live, GameplayActions.SpawnWeight)), x => Assert.False(x.LiveProven));
        Assert.Equal(["custom_icon_resource_family", "stratagem_presentation_custom_image"], Families("hd2.resources.image"));
        Assert.Equal(["custom_stratagem_weapon_variant"], Families("hd2.resources.model"));
        Assert.All(ScriptingReference.Evidence(live, "hd2.resources.image"), x => Assert.True(x.LiveProven));
        // The donor family is shown with every donor's detail; the stratagem field families come with their fields (StratagemFieldEditor).
        Assert.Equal("pending", live.Family(AttackOutput.DonorEvidence)!.Status);
        var proven = sdk.Stratagems!.FieldInstances.Where(f => f.LiveEvidence is { Status: "live_proven" }).Select(f => f.LiveEvidence!.Family).ToHashSet();
        Assert.Contains("stratagem_calldown_code", proven); Assert.Contains("stratagem_presentation", proven);
        // The 0.28 event subjects keep exactly their families.
        Assert.Equal(["event_action_heal"], Families("hd2.actions.heal"));
    }

    // ---- gameplay action pickers, sounds, snippets, diagnostics ----------------------------------------------------------------------------

    [Fact] public async Task Gameplay_action_inserts_are_built_from_the_published_signatures_and_check_clean()
    {
        using var e = new TestEnvironment(); var (_, sdk) = await Fresh(e); var api = sdk.LuaApi!;
        var homing = GameplayActions.Options(sdk, GameplayActions.HomingOptions);
        Assert.Equal(["target", "turn_rate", "cone", "range", "arm_distance", "aim_height", "retarget", "multiplayer", "label"], homing.Select(o => o.Name));
        var turn = homing.Single(o => o.Name == "turn_rate");
        Assert.Equal((1d, 1440d, "90"), (turn.Min, turn.Max, turn.Default)); Assert.Equal("1..1440", turn.OutOfRange(2000)); Assert.Null(turn.OutOfRange(120));
        Assert.Equal(["enemy", "friendly"], homing.Single(o => o.Name == "target").Choices);
        Assert.True(homing.Single(o => o.Name == "retarget").IsBoolean);
        Assert.Equal((-5d, 10d), (homing.Single(o => o.Name == "aim_height").Min, homing.Single(o => o.Name == "aim_height").Max));
        var call = GameplayActions.HomingCall("EAT-17 Expendable Anti-Tank", homing, new Dictionary<string, string> { ["target"] = "enemy", ["turn_rate"] = "120", ["retarget"] = "false", ["cone"] = "" });
        Assert.StartsWith("local homing = hd2.projectiles.homing('EAT-17 Expendable Anti-Tank', {target = 'enemy', turn_rate = 120, retarget = false})\n", call);
        Clean(call, sdk);
        // Spawn weights always carry the opt-in Runtime requires; the multiplier range comes from the call's doc.
        var spawn = GameplayActions.Option(new LuaMember("multiplier", "field", null, "number", GameplayActions.Call(api, GameplayActions.SpawnWeight)!.Doc));
        Assert.Equal((0d, 10d), (spawn.Min, spawn.Max));
        var weight = GameplayActions.SpawnWeightCall("enemy/v1/terminids/charger", 2, [7, 8]);
        Assert.Contains("hd2.enemies.spawn_weight('enemy/v1/terminids/charger', 2, {allow_unverified_effect = true, difficulties = {7, 8}})", weight);
        Clean(weight, sdk);
        // Limbs from the signature, the velocity limit from the doc.
        var injure = GameplayActions.Call(api, GameplayActions.Injure);
        Assert.Equal(["head", "chest", "l_hand", "r_hand", "l_knee", "r_knee"], GameplayActions.ParameterLiterals(injure, "limb"));
        Assert.Equal(25, GameplayActions.MaxSpeed(GameplayActions.Call(api, GameplayActions.AddVelocity)));
        Assert.Equal(["primary", "secondary", "support"], GameplayActions.ParameterLiterals(GameplayActions.Call(api, "HD2PlayerHandle:ammo"), "slot"));
        var sounds = WeaponSounds.For(api);
        foreach (var code in new[] { GameplayActions.InjureCall("l_knee", 20), GameplayActions.HealLimbCall("l_knee"), GameplayActions.HealLimbCall(null), GameplayActions.AddVelocityCall(0, 0, 8),
            GameplayActions.ResupplyCall(), GameplayActions.PlayerReadCall("ammo", "primary"), GameplayActions.PelicanCall(30, sounds.First(s => s.Kind == "loop").Name),
            GameplayActions.PelicanCall(null, null), GameplayActions.SoundCall(sounds[0].Name) }.Concat(GameplayActions.PlayerReads.Select(r => GameplayActions.PlayerReadCall(r))))
            Clean(code, sdk);
        Assert.Contains("hd2.pelican.spawn({position = here, hover = 30, gun = {sound = '", GameplayActions.PelicanCall(30, sounds.First(s => s.Kind == "loop").Name));
    }

    [Fact] public async Task The_sound_catalogue_is_the_sdk_file_and_is_offered_only_with_hd2_sounds()
    {
        using var e = new TestEnvironment(); var (_, sdk) = await Fresh(e);
        using (var stream = typeof(WeaponSounds).Assembly.GetManifestResourceStream(WeaponSounds.Resource)!) using (var buffer = new MemoryStream())
        { stream.CopyTo(buffer); Assert.Equal(FixtureEntry("WeaponSoundCatalogue.json"), buffer.ToArray()); }
        var sounds = WeaponSounds.For(sdk.LuaApi);
        Assert.Equal(171, sounds.Count); Assert.Equal(19, sounds.Count(s => s.Kind == "loop"));
        Assert.Single(sounds, s => s.PelicanDefault);
        Assert.Contains(sounds, s => s.Name == "sentry/gatling" && s.Stratagem != null);
        // SDK 0.28.1 has no hd2.sounds: no picker.
        var older = await SdkFixtures.Install(e, "0.28.1");
        Assert.Empty(WeaponSounds.For(older.LuaApi));
    }

    [Fact] public async Task Live_proven_stratagem_writes_and_performance_have_snippets_that_check_clean()
    {
        using var e = new TestEnvironment(); var (w, sdk) = await Fresh(e);
        var snippets = LuaSnippets.For(sdk, w.Project!.ResourceId);
        var code = snippets.Single(s => s.Id == "stratagem-calldown-code");
        Assert.Contains("target = hd2.stratagem('Orbital 120mm HE Barrage'),", code.Code);
        Assert.Contains("field = hd2.fields.stratagem.calldown_code, expect = {'right', 'right', 'down', 'left', 'right', 'down'}, value = {'up', 'up', 'down', 'down'}}", code.Code);
        Assert.DoesNotContain("allow_unverified", code.Code);
        var presentation = snippets.Single(s => s.Id == "stratagem-presentation");
        foreach (var field in new[] { "presentation_name", "presentation_name_cased", "presentation_description", "presentation_icon" })
            Assert.Contains("{field = hd2.fields.stratagem." + field + ", expect = 'Orbital 120mm HE Barrage', value = 'Orbital Gas Strike'}", presentation.Code);
        Assert.Contains(snippets, s => s.Id == RuntimeDiagnostics.PerformanceSnippet);
        foreach (var s in snippets)
        {
            Clean(s.Code, sdk);
            foreach (var use in s.Uses)
                Assert.True(sdk.Events!.Event(use)?.IsAvailable == true || use == "mod:value" && sdk.LuaApi!.Resolve("HD2ModContext:value") != null || sdk.LuaApi!.Resolve(use) != null, s.Id + ": " + use);
        }
        using (UiCulture.Use(UiLanguages.Find("zh-Hans")!))
        {
            var zh = LuaSnippets.For(sdk, w.Project.ResourceId);
            Assert.Equal(snippets.Select(s => s.Code), zh.Select(s => s.Code));
            Assert.NotEqual(code.Title, zh.Single(s => s.Id == code.Id).Title);
        }
        // The diagnostics reference lists the performance report with Runtime's PERFORMANCE lines.
        var section = RuntimeDiagnostics.Sections(sdk.LuaApi).Single(s => s.Id == RuntimeDiagnostics.PerformanceId);
        Assert.Equal(RuntimeDiagnostics.Performance, Assert.Single(section.Calls).Path);
        Assert.All(section.LogLines, l => Assert.Contains("PERFORMANCE:", l));
    }
}
