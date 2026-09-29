using System.Diagnostics;
using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Nodes;
using HD2RuntimeGUI.Core.Generation;
using HD2RuntimeGUI.Core.Metadata;
using HD2RuntimeGUI.Core.Models;
using HD2RuntimeGUI.Core.Projects;
using HD2RuntimeGUI.Core.Services;
using Xunit;

namespace HD2RuntimeGUI.Tests;

// Enemies and enemy structures (hd2.enemy / hd2.structure) from an unreleased HD2Runtime development SDK. The fixture is the committed
// sdk/*.json of HD2Runtime e15d5bf (reports 0.27.0; carries EnemyAuthoringCapabilities.json for 0.28.0), served through the developer-only
// local SDK path, exactly as `--sdk-path <HD2Runtime>\sdk` serves it.
public sealed class EnemyAuthoringTests
{
    public const string Fixture = "sdk-dev-e15d5bf.zip";
    private const string Charger = "enemy/v1/terminids/charger", Spewer = "enemy/v1/terminids/boomer_burrower", Gunship = "enemy/v1/automatons/gunship",
        Fabricator = "enemy/v1/automatons/spawner_factory_conscript_base", Bunker = "enemy/v1/automatons/command_bunker_side", Hunter = "enemy/v1/terminids/hunter_tier_1";
    public static string DevSdk(TestEnvironment e, string name = "dev-sdk")
    {
        var dir = Path.Combine(e.Paths.Root, name);
        if (!Directory.Exists(dir)) ZipFile.ExtractToDirectory(Path.Combine(AppContext.BaseDirectory, "Fixtures", Fixture), dir);
        return dir;
    }
    public static byte[] Catalog(TestEnvironment e) => File.ReadAllBytes(Path.Combine(DevSdk(e), EnemyAuthoringReader.FileName));
    private static BuilderWorkspace Workspace(TestEnvironment e, string sdkPath, out SdkUpdateService updates)
    {
        var cache = new SdkCache(e.Paths, e.Reader, e.GitHub) { LocalSdkPath = sdkPath }; updates = new SdkUpdateService(cache, e.GitHub, e.Paths);
        return new BuilderWorkspace(e.Store, e.Projects, cache, updates, e.Changes, e.Generator, e.Exporter, e.Desktop, e.Desktop, e.Paths);
    }
    public static async Task<(BuilderWorkspace Workspace, SdkMetadata Sdk)> Fresh(TestEnvironment e)
    {
        var w = Workspace(e, DevSdk(e), out var updates); var sdk = (await updates.CheckAsync()).Installed;
        await w.CreateAsync(new("Enemy Tuning", "Tests", "mods/tests/enemy_tuning", "0.1.0"), sdk);
        return (w, sdk);
    }
    private static EntityField Field(SdkMetadata sdk, string semanticId, string path, string? id, string field) =>
        sdk.Entities!.Enemies!.Fields(semanticId).Single(f => f.Target.Path == path && (f.Target.Zone ?? f.Target.Attack) == id && f.SemanticFieldId == field);
    private static EntityField Main(SdkMetadata sdk, string semanticId, string field) => Field(sdk, semanticId, EnemyAuthoringReader.EntityPath, null, field);
    private static EntityField Zone(SdkMetadata sdk, string semanticId, string zone, string field) => Field(sdk, semanticId, EnemyAuthoringReader.ZonePath, zone, field);
    private static EntityField Attack(SdkMetadata sdk, string semanticId, string attack, string field) => Field(sdk, semanticId, EnemyAuthoringReader.AttackPath, attack, field);

    // A development SDK snapshot with an edited enemy catalog (summary recomputed as Runtime's generator would).
    private static JsonNode Json(TestEnvironment e) => JsonNode.Parse(Catalog(e))!;
    private static byte[] Bytes(JsonNode n)
    {
        var classes = n["classes"]!.AsArray().Select(c => c!).ToArray(); var fields = n["fieldInstances"]!.AsArray().Select(f => f!).ToArray(); var defs = n["model"]!["fields"]!;
        var editable = fields.Where(f => f["editable"]!.GetValue<bool>()).ToArray(); var s = n["summary"]!;
        s["classes"] = classes.Length; s["enemies"] = classes.Count(c => (string)c["kind"]! == "enemy"); s["structures"] = classes.Count(c => (string)c["kind"]! == "structure");
        s["wikiNamed"] = classes.Count(c => c["wikiName"] != null); s["withWikiCandidates"] = classes.Count(c => c["wikiName"] == null && c["wikiCandidates"]!.AsArray().Count > 0);
        s["fieldInstances"] = fields.Length; s["writableFieldInstances"] = editable.Length; s["zones"] = classes.Sum(c => c["zones"]!.AsArray().Count);
        s["attacks"] = classes.Sum(c => c["attacks"]!.AsArray().Count); s["classesWithAttacks"] = classes.Count(c => c["attacks"]!.AsArray().Count > 0);
        s["attackFieldInstances"] = fields.Count(f => (string)f["target"]!["path"]! == "attack");
        s["acknowledged"] = editable.Count(f => defs[(string)f["semanticFieldId"]!]!["acknowledgement"] != null);
        s["proven"] = editable.Count(f => defs[(string)f["semanticFieldId"]!]!["acknowledgement"] == null);
        return JsonSerializer.SerializeToUtf8Bytes(n);
    }
    private static JsonNode Class(JsonNode n, string className) => n["classes"]!.AsArray().Single(c => (string)c!["className"]! == className)!;
    private static IEnumerable<JsonNode> Instances(JsonNode n, string enemy) => n["fieldInstances"]!.AsArray().Select(f => f!).Where(f => (string)f["target"]!["enemy"]! == enemy);
    private static SdkMetadata With(SdkMetadata sdk, byte[] catalog) => sdk with { Entities = sdk.Entities!.WithEnemies(EnemyAuthoringReader.Read(catalog, sdk.Version)) };
    private static string? Issue(ModProject p, SdkMetadata sdk, EntityChange c) { try { new EntityChangeService().Validate(p, sdk, c); return null; } catch (InvalidDataException x) { return x.Message; } }

    [Fact] public async Task The_development_sdk_publishes_every_enemy_and_structure_class()
    {
        using var e = new TestEnvironment(); var (w, sdk) = await Fresh(e);
        var enemies = sdk.Entities!.Enemies!;
        Assert.Equal("0.27.0", sdk.Version); Assert.Equal(177, enemies.Classes.Length);
        Assert.Equal(138, enemies.Of(EnemyAuthoringReader.Enemy).Count()); Assert.Equal(39, enemies.Of(EnemyAuthoringReader.Structure).Count());
        Assert.Equal(10425, enemies.FieldInstances.Length); Assert.Equal(9106, enemies.FieldInstances.Count(f => f.Editable));
        Assert.Equal(1373, enemies.Classes.Sum(c => c.Zones.Length)); Assert.Equal(169, enemies.Classes.Sum(c => c.Attacks.Length));
        // Factions are Runtime's keys; structures add the neutral faction.
        var factions = enemies.Of(EnemyAuthoringReader.Enemy).GroupBy(c => c.Faction).ToDictionary(g => g.Key, g => g.Count());
        Assert.Equal(new Dictionary<string, int> { ["terminids"] = 42, ["automatons"] = 71, ["illuminate"] = 25 }, factions);
        Assert.Contains(enemies.Of(EnemyAuthoringReader.Structure), c => c.Faction == "neutral");
        // Navigation: first-class destinations with the published counts.
        var nav = Navigation.Build(sdk, w.Project);
        Assert.Equal(138, nav.Single(i => i.Page == "enemies").Count); Assert.Equal(39, nav.Single(i => i.Page == "structures").Count);
        Assert.True(nav.ToList().FindIndex(i => i.Page == "structures") < nav.ToList().FindIndex(i => i.Page == "changes"));
    }

    [Fact] public async Task Native_only_classes_keep_their_native_names_and_wiki_candidates_stay_candidates()
    {
        using var e = new TestEnvironment(); var (_, sdk) = await Fresh(e); var enemies = sdk.Entities!.Enemies!;
        Assert.Equal(21, enemies.Classes.Count(c => c.Named));
        var hunter = enemies.Find(Hunter)!;
        Assert.False(hunter.Named); Assert.Equal("hunter_tier_1", hunter.Name); Assert.Contains("Hunter", hunter.WikiCandidates);
        Assert.Equal("Charger", enemies.Find(Charger)!.Name); Assert.Equal("charger", enemies.Find(Charger)!.ClassName);
        // Search covers the Runtime name, native class, wiki candidates, attack names and the semantic ID.
        Assert.True(enemies.Matches(hunter, "Hunter")); Assert.True(enemies.Matches(hunter, "hunter_tier_1")); Assert.True(enemies.Matches(hunter, Hunter));
        Assert.False(enemies.Matches(hunter, "Charger"));
        Assert.True(enemies.Matches(enemies.Find(Gunship)!, "HEAT Rocket Racks"));
        // Classes without any wiki candidate are still listed (all 138 enemies are selectable, named or not).
        Assert.Contains(enemies.Of(EnemyAuthoringReader.Enemy), c => !c.Named && c.WikiCandidates.Length == 0);
        // Zone labels are Runtime's: wiki label, else native name, else zone ID.
        Assert.Equal("Head", enemies.Find(Charger)!.Zone("zone_0")!.Label);
        Assert.Equal("insides", enemies.Find(Fabricator)!.Zone("zone_0")!.Label);
        // Instance keys are semantic, so they survive a later wiki name.
        Assert.Equal("enemy:" + Charger + "|entity||entity.health", Main(sdk, Charger, "entity.health").InstanceKey);
    }

    [Fact] public async Task Main_health_and_zone_edits_generate_one_guarded_request_per_object()
    {
        using var e = new TestEnvironment(); var (w, sdk) = await Fresh(e);
        await w.SetEntityAsync(Main(sdk, Charger, "entity.health").InstanceKey, "1200");
        await w.SetEntityAsync(Zone(sdk, Charger, "zone_0", "zone.armor").InstanceKey, "3");
        await w.SetEntityAsync(Zone(sdk, Charger, "zone_0", "zone.health").InstanceKey, "900");
        Assert.Null(w.BuildError); Assert.Equal(10, w.Project!.FormatVersion);
        var lua = w.LuaPreview;
        Assert.Contains(Flat("target=hd2.enemy('charger'),\n            field=hd2.fields.entity.health,\n            expect=2400,\n            value=1200,"), Flat(lua));
        Assert.Contains("target=hd2.enemy('charger'):zone('zone_0'),", lua);
        Assert.Contains("{field=hd2.fields.zone.armor,expect=4,value=3},", lua); Assert.Contains("{field=hd2.fields.zone.health,expect=1200,value=900},", lua);
        // The class and its head zone are independent objects, so they are independent requests: no plan bundles them.
        Assert.Equal(2, CountOf(lua, "hd2.ensure(")); Assert.DoesNotContain("operations={", lua);
        // Live-proven enemy health and zone armor need no opt-in.
        Assert.DoesNotContain("allow_shared", lua); Assert.DoesNotContain("allow_unverified_effect", lua);
    }

    [Fact] public async Task Wiki_correlated_members_and_structure_health_carry_allow_unverified_effect()
    {
        using var e = new TestEnvironment(); var (w, sdk) = await Fresh(e);
        var constitution = Zone(sdk, Charger, "zone_0", "zone.durable_resistance");
        Assert.Equal("allow_unverified_effect", constitution.Acknowledgement); Assert.Equal("schema_wiki_correlated", constitution.Evidence.Tier);
        await w.SetEntityAsync(constitution.InstanceKey, "0.5");
        // Structure health is offline-proven only: the instance-level gate Runtime publishes. Structure armor is not gated.
        var fabricator = Main(sdk, Fabricator, "entity.health");
        Assert.Equal("allow_unverified_effect", fabricator.Acknowledgement); Assert.Contains("StructureHealthTest", fabricator.AcknowledgementReason);
        Assert.Null(Zone(sdk, Fabricator, "zone_0", "zone.armor").Acknowledgement);
        Assert.Null(Main(sdk, Charger, "entity.health").Acknowledgement); Assert.Equal("live_proven", Main(sdk, Charger, "entity.health").Evidence.Tier);
        await w.SetEntityAsync(fabricator.InstanceKey, "150");
        await w.SetEntityAsync(Zone(sdk, Fabricator, "zone_0", "zone.armor").InstanceKey, "3");
        Assert.Null(w.BuildError); var lua = w.LuaPreview;
        Assert.Contains(Flat("target=hd2.structure('spawner_factory_conscript_base'),\n            allow_unverified_effect=true,\n            field=hd2.fields.entity.health,"), Flat(lua));
        Assert.Contains(Flat("target=hd2.structure('spawner_factory_conscript_base'):zone('zone_0'),\n            field=hd2.fields.zone.armor,"), Flat(lua));
        Assert.Contains(Flat("target=hd2.enemy('charger'):zone('zone_0'),\n            allow_unverified_effect=true,\n            field=hd2.fields.zone.durable_resistance,"), Flat(lua));
        Assert.Equal("Structures", w.Project!.EntityChanges.Single(c => c.InstanceKey == fabricator.InstanceKey).Group);
    }

    [Fact] public async Task Sentinels_ranges_and_types_are_enforced_where_the_edit_is_made()
    {
        using var e = new TestEnvironment(); var (w, sdk) = await Fresh(e);
        // The Charger's underside uses the main health pool (-1): read-only with Runtime's reason.
        var underside = Zone(sdk, Charger, "zone_3", "zone.health");
        Assert.False(underside.Editable); Assert.Equal(-1, underside.CurrentDefault.GetInt32());
        Assert.Contains("main health pool", (await Assert.ThrowsAsync<InvalidDataException>(() => w.SetEntityAsync(underside.InstanceKey, "500"))).Message);
        var armor = Zone(sdk, Charger, "zone_0", "zone.armor");
        Assert.Contains("between 0 and 10", (await Assert.ThrowsAsync<InvalidDataException>(() => w.SetEntityAsync(armor.InstanceKey, "11"))).Message);
        await Assert.ThrowsAsync<InvalidDataException>(() => w.SetEntityAsync(armor.InstanceKey, "2.5")); // integer field
        await Assert.ThrowsAsync<InvalidDataException>(() => w.SetEntityAsync(armor.InstanceKey, "four"));
        // "not set" explosive shares are read-only (the explosion resolves through another zone).
        Assert.Contains(sdk.Entities!.Enemies!.FieldInstances, f => f.SemanticFieldId == "zone.explosive_damage_percentage" && !f.Editable && f.CurrentDefault.ValueKind == JsonValueKind.Null);
        // Setting a value back to its baseline removes the edit.
        await w.SetEntityAsync(armor.InstanceKey, "2"); await w.SetEntityAsync(armor.InstanceKey, "4");
        Assert.Empty(w.Project!.EntityChanges); Assert.Null(w.BuildError);
    }

    [Fact] public async Task Attack_rows_cover_direct_damage_projectile_and_explosion_branches_as_shared_rows()
    {
        using var e = new TestEnvironment(); var (w, sdk) = await Fresh(e); var spewer = sdk.Entities!.Enemies!.Find(Spewer)!;
        Assert.Equal("Rupture Spewer", spewer.Name);
        Assert.Equal(["projectile", "explosion_impact", "projectile_settings", "explosion_settings_impact"], spewer.Attacks.Select(a => a.Role));
        Assert.Equal(["Bile Bombard"], spewer.Attack("slot_1")!.WikiAttacks);
        // Each row publishes exactly its own members.
        Assert.Equal(9, sdk.Entities.Enemies.Attack(Spewer, "slot_1").Count()); Assert.All(sdk.Entities.Enemies.Attack(Spewer, "slot_1"), f => Assert.Equal("damage", f.Domain));
        Assert.Equal(["projectile.velocity", "projectile.mass", "projectile.drag", "projectile.gravity", "projectile.pellet_count"],
            sdk.Entities.Enemies.Attack(Spewer, "slot_1_projectile").Select(f => f.SemanticFieldId));
        Assert.Equal(["explosion.inner_radius", "explosion.outer_radius", "explosion.shockwave_radius"],
            sdk.Entities.Enemies.Attack(Spewer, "slot_1_impact_explosion").Select(f => f.SemanticFieldId));
        var direct = Attack(sdk, Spewer, "slot_1", "damage.standard_damage");
        Assert.True(direct.Shared && direct.AllowSharedRequired && direct.DynamicConsumersPossible); Assert.Equal("allow_unverified_effect", direct.Acknowledgement);
        Assert.Contains(direct.SharedConsumers, c => c.Enemy == "enemy/v1/terminids/boomer");
        await w.SetEntityAsync(direct.InstanceKey, "50");
        await w.SetEntityAsync(Attack(sdk, Spewer, "slot_1_impact", "damage.standard_damage").InstanceKey, "20");
        await w.SetEntityAsync(Attack(sdk, Spewer, "slot_1_projectile", "projectile.velocity").InstanceKey, "45");
        await w.SetEntityAsync(Attack(sdk, Spewer, "slot_1_impact_explosion", "explosion.inner_radius").InstanceKey, "2");
        Assert.Null(w.BuildError); var lua = w.LuaPreview;
        foreach (var (attack, field) in new[] { ("slot_1", "damage.player_standard_damage"), ("slot_1_impact", "damage.player_standard_damage"), ("slot_1_projectile", "projectile.velocity"), ("slot_1_impact_explosion", "explosion.inner_radius") })
            Assert.Contains("target=hd2.enemy('boomer_burrower'):attack('" + attack + "'), allow_shared=true, allow_unverified_effect=true, field=hd2.fields." + field + ",", Flat(lua));
        Assert.Equal(4, CountOf(lua, "hd2.ensure("));
        // The project records who else the shared row affects (by semantic ID).
        var saved = w.Project!.EntityChanges.Single(c => c.InstanceKey == direct.InstanceKey);
        Assert.Contains("enemy/v1/terminids/boomer", saved.SharedConsumers!); Assert.Contains(Spewer, saved.SharedConsumers!);
        Assert.Equal("slot_1", saved.Attack); Assert.Equal(Spewer, saved.Entity); Assert.Equal("enemy", saved.Resource);
    }

    [Fact] public async Task Enemy_attacks_publish_no_status_branch()
    {
        // Runtime reaches the DamageInfo rows, but enemy attacks do not take status references yet: no status control is offered.
        using var e = new TestEnvironment(); var (_, sdk) = await Fresh(e);
        Assert.DoesNotContain(sdk.Entities!.Enemies!.FieldInstances, f => f.Domain == "status" || f.SemanticFieldId.Contains("status", StringComparison.Ordinal));
    }

    [Fact] public async Task One_row_reached_through_two_attacks_is_edited_through_one_of_them_only()
    {
        using var e = new TestEnvironment(); var (w, sdk) = await Fresh(e);
        var left = Attack(sdk, Gunship, "slot_0", "damage.standard_damage"); var right = Attack(sdk, Gunship, "slot_1", "damage.standard_damage");
        Assert.Equal(left.BackingObjectId, right.BackingObjectId);
        await w.SetEntityAsync(left.InstanceKey, "10");
        var error = await Assert.ThrowsAsync<InvalidDataException>(() => w.SetEntityAsync(right.InstanceKey, "12"));
        Assert.Contains("already edited through Gunship · HEAT Rocket Racks (direct-hit damage)", error.Message);
        Assert.Null(w.BuildError); Assert.Single(w.Project!.EntityChanges);
    }

    [Fact] public async Task Structures_use_the_same_editor_model_with_hd2_structure()
    {
        using var e = new TestEnvironment(); var (w, sdk) = await Fresh(e); var bunker = sdk.Entities!.Enemies!.Find(Bunker)!;
        Assert.True(bunker.IsStructure); Assert.Equal("hd2.structure", bunker.Accessor);
        var velocity = Attack(sdk, Bunker, "slot_0_projectile", "projectile.velocity");
        await w.SetEntityAsync(velocity.InstanceKey, "200");
        Assert.Null(w.BuildError);
        Assert.Contains(Flat("target=hd2.structure('command_bunker_side'):attack('slot_0_projectile'),\n            allow_shared=true,\n            allow_unverified_effect=true,"), Flat(w.LuaPreview));
        Assert.Equal(EnemyAuthoringReader.Structure, w.Project!.EntityChanges.Single().Resource);
    }

    [Fact] public async Task Enemy_projects_save_load_and_bind_options()
    {
        using var e = new TestEnvironment(); var (w, sdk) = await Fresh(e);
        await w.SetEntityAsync(Main(sdk, Charger, "entity.health").InstanceKey, "1200");
        await w.SetEntityAsync(Attack(sdk, Spewer, "slot_1", "damage.standard_damage").InstanceKey, "50");
        var id = w.Project!.Id; var lua = w.LuaPreview;
        var json = await File.ReadAllTextAsync(e.Paths.ProjectFile(id));
        Assert.Contains("\"formatVersion\": 10", json); Assert.Contains("\"sharedConsumers\"", json); Assert.Contains("\"entity\": \"" + Spewer + "\"", json);
        var reopened = Workspace(e, DevSdk(e), out _); await reopened.OpenAsync(id);
        Assert.Null(reopened.BuildError); Assert.Equal(2, reopened.Project!.EntityChanges.Count); Assert.Equal(lua, reopened.LuaPreview);
        // Numeric enemy edits can become in-game Mod Options, labelled by class and target.
        var options = ModOptionsService.Targets(reopened.Project, sdk);
        Assert.Contains(options, o => o.Owner == "Charger" && o.DisplayName == "Main health");
        Assert.Contains(options, o => o.Owner == "Rupture Spewer · Bile Bombard (direct-hit damage)");
    }

    [Fact] public async Task Enemy_projects_are_format_10_and_projects_without_enemy_edits_keep_their_format()
    {
        using var e = new TestEnvironment(); var (w, sdk) = await Fresh(e);
        Assert.True(w.Project!.FormatVersion < 10);
        var p = w.Project;
        ProjectIdentity.Validate(p);
        var change = new EntityChangeService().Create(sdk, Zone(sdk, Charger, "zone_0", "zone.armor").InstanceKey, "3");
        p.EntityChanges.Add(change); Assert.Equal(10, ProjectIdentity.RequiredFormat(p));
        p.FormatVersion = 10; ProjectIdentity.Validate(p);
        // Enemy targets are validated as semantic identities.
        foreach (var bad in new[] { change with { Entity = "Charger" }, change with { Zone = "head" }, change with { Attack = "slot_0" }, change with { Path = "mount" }, change with { SharedConsumers = [] } })
        {
            p.EntityChanges = [bad]; Assert.Throws<InvalidDataException>(() => ProjectIdentity.Validate(p));
        }
    }

    [Fact] public async Task An_enemy_project_opened_without_enemy_metadata_is_flagged_not_lost()
    {
        using var e = new TestEnvironment(); var (w, sdk) = await Fresh(e);
        await w.SetEntityAsync(Main(sdk, Charger, "entity.health").InstanceKey, "1200");
        var published = await SdkFixtures.Install(e, "0.27.0"); Assert.Null(published.Entities!.Enemies);
        var issue = Issue(w.Project!, published, w.Project!.EntityChanges.Single());
        Assert.Contains(EnemyAuthoringReader.FileName, issue); Assert.Contains("development SDK", issue);
        Assert.Throws<InvalidDataException>(() => new EntityLua(new EntityChangeService()).Operations(w.Project, published));
    }

    [Fact] public async Task A_new_development_snapshot_keeps_edits_that_survive_and_flags_the_rest()
    {
        using var e = new TestEnvironment(); var (w, sdk) = await Fresh(e);
        await w.SetEntityAsync(Main(sdk, Charger, "entity.health").InstanceKey, "1200");
        await w.SetEntityAsync(Zone(sdk, Charger, "zone_16", "zone.armor").InstanceKey, "1");
        await w.SetEntityAsync(Main(sdk, Hunter, "entity.health").InstanceKey, "100");
        // Next snapshot: the hunter class is proven as a wiki name, the Charger loses zone_16 and its main health baseline changes.
        var n = Json(e);
        var charger = Class(n, "charger"); charger["zones"]!.AsArray().Remove(charger["zones"]!.AsArray().Single(z => (string)z!["id"]! == "zone_16"));
        foreach (var f in n["fieldInstances"]!.AsArray().Where(f => (string)f!["target"]!["enemy"]! == "Charger" && (string?)f!["target"]!["zone"] == "zone_16").ToArray()) n["fieldInstances"]!.AsArray().Remove(f);
        Instances(n, "Charger").Single(f => (string)f["semanticFieldId"]! == "entity.health" && (string)f["target"]!["path"]! == "entity")["currentDefault"] = 3000;
        Rename(n, "hunter_tier_1", "Hunter Test");
        var next = Path.Combine(e.Paths.Root, "dev-sdk-next"); Directory.CreateDirectory(next);
        foreach (var file in Directory.GetFiles(DevSdk(e))) File.Copy(file, Path.Combine(next, Path.GetFileName(file)));
        await File.WriteAllBytesAsync(Path.Combine(next, EnemyAuthoringReader.FileName), Bytes(n));
        var reopened = Workspace(e, next, out _); await reopened.OpenAsync(w.Project!.Id);
        var changes = reopened.Project!.EntityChanges;
        // Renamed display label, same semantic ID: the edit is valid and still targets the native class.
        var hunter = changes.Single(c => c.Entity == Hunter);
        Assert.Null(reopened.EntityIssue(hunter)); Assert.Equal("Hunter Test", reopened.Metadata!.Entities!.Enemies!.Find(Hunter)!.Name);
        // Removed zone: missing, never retargeted. Changed baseline: review.
        Assert.Contains("Enemy capability is missing", reopened.EntityIssue(changes.Single(c => c.Zone == "zone_16")));
        Assert.Contains("baseline changed", reopened.EntityIssue(changes.Single(c => c.Entity == Charger && c.Path == "entity")));
        Assert.NotNull(reopened.BuildError);
        // Accepting the new baseline keeps the desired value.
        await reopened.SetEntityAsync(Main(reopened.Metadata, Charger, "entity.health").InstanceKey, "1200", acceptBaseline: true);
        Assert.Null(reopened.EntityIssue(reopened.Project.EntityChanges.Single(c => c.Entity == Charger && c.Path == "entity")));
        await reopened.ResetEntityAsync(instance: changes.Single(c => c.Zone == "zone_16").InstanceKey);
        Assert.Null(reopened.BuildError);
        Assert.Contains("hd2.enemy('hunter_tier_1')", reopened.LuaPreview); Assert.Contains(Flat("expect=3000,\n            value=1200,"), Flat(reopened.LuaPreview));
    }

    [Fact] public async Task Attacks_becoming_read_only_or_unavailable_and_shared_ownership_changes_need_review()
    {
        using var e = new TestEnvironment(); var (w, sdk) = await Fresh(e);
        var direct = Attack(sdk, Spewer, "slot_1", "damage.standard_damage"); var impact = Attack(sdk, Spewer, "slot_1_impact", "damage.standard_damage");
        var velocity = Attack(sdk, Spewer, "slot_1_projectile", "projectile.velocity");
        await w.SetEntityAsync(direct.InstanceKey, "50"); await w.SetEntityAsync(impact.InstanceKey, "20"); await w.SetEntityAsync(velocity.InstanceKey, "45");
        var p = w.Project!; EntityChange Saved(EntityField f) => p.EntityChanges.Single(c => c.InstanceKey == f.InstanceKey);
        // Unchanged snapshot: every edit stays valid.
        var same = With(sdk, Bytes(Json(e)));
        Assert.All(p.EntityChanges, c => Assert.Null(Issue(p, same, c)));
        var n = Json(e);
        // The direct-hit row becomes read-only.
        var row = Instances(n, "Rupture Spewer").Single(f => (string?)f["target"]!["attack"] == "slot_1" && (string)f["semanticFieldId"]! == "damage.standard_damage");
        row["editable"] = false; row["reason"] = "Mount link unresolved in this build.";
        // The impact explosion attack disappears from the class.
        var spewer = Class(n, "boomer_burrower"); spewer["attacks"]!.AsArray().Remove(spewer["attacks"]!.AsArray().Single(a => (string)a!["id"]! == "slot_1_impact"));
        foreach (var f in Instances(n, "Rupture Spewer").Where(f => (string?)f["target"]!["attack"] == "slot_1_impact").ToArray()) n["fieldInstances"]!.AsArray().Remove(f);
        // The projectile row gains a reviewed consumer (another class now reaches it).
        var projectileRow = (string)Instances(n, "Rupture Spewer").First(f => (string?)f["target"]!["attack"] == "slot_1_projectile")["backingObjectId"]!;
        foreach (var f in n["fieldInstances"]!.AsArray().Select(f => f!).Where(f => (string?)f["backingObjectId"] == projectileRow)) f["sharedConsumers"]!.AsArray().Add("Charger");
        foreach (var a in n["classes"]!.AsArray().SelectMany(c => c!["attacks"]!.AsArray()).Where(a => (string)a!["id"]! == "slot_1_projectile" && a!["sharedWithClasses"]!.AsArray().Any(x => (string)x! == "Rupture Spewer")))
            a!["sharedWithClasses"]!.AsArray().Add("Charger");
        var next = With(sdk, Bytes(n));
        Assert.Contains("Mount link unresolved", Issue(p, next, Saved(direct)));
        Assert.Contains("Enemy capability is missing", Issue(p, next, Saved(impact)));
        Assert.Contains("shared ownership changed", Issue(p, next, Saved(velocity)));
    }

    [Fact] public async Task Shared_health_records_carry_allow_shared_when_Runtime_publishes_them()
    {
        // No class in this snapshot shares a health record; the ownership model is still honoured if one does.
        using var e = new TestEnvironment(); var (w, sdk) = await Fresh(e);
        var n = Json(e); var hunter = Class(n, "hunter_tier_1");
        hunter["sharedHealthRecord"] = true; hunter["allowSharedRequired"] = true; hunter["healthRecordConsumers"]!.AsArray().Add("hunter_tier_2");
        n["summary"]!["sharedHealthRecords"] = 1;
        var shared = With(sdk, Bytes(n)); var f = Main(shared, Hunter, "entity.health");
        Assert.True(f.Shared && f.AllowSharedRequired); Assert.Contains(f.SharedConsumers, c => c.Enemy == "enemy/v1/terminids/hunter_tier_2");
        var p = w.Project!; p.EntityChanges.Add(new EntityChangeService().Create(shared, f.InstanceKey, "100"));
        Assert.Contains("hunter_tier_2", string.Join(",", p.EntityChanges.Single().SharedConsumers!));
        var lua = string.Join("\n", new EntityLua(new EntityChangeService()).Operations(p, shared));
        Assert.Contains(Flat("target=hd2.enemy('hunter_tier_1'),\n    allow_shared=true,"), Flat(lua));
    }

    [Fact] public async Task Malformed_or_unsafe_enemy_metadata_fails_closed()
    {
        using var e = new TestEnvironment(); var (_, sdk) = await Fresh(e);
        void Rejects(Action<JsonNode> edit) { var n = Json(e); edit(n); Assert.ThrowsAny<Exception>(() => EnemyAuthoringReader.Read(Bytes(n), sdk.Version)); }
        Rejects(n => n["safety"]!["runtimeAddresses"] = true);
        Rejects(n => n["hd2RuntimeVersion"] = "0.26.0");
        Rejects(n => Class(n, "charger")["accessor"] = "hd2.structure");
        Rejects(n => Class(n, "charger")["name"] = "charger_renamed");
        Rejects(n => Instances(n, "Charger").First(f => (bool)f["editable"]!)["currentDefault"] = 99999999);
        Rejects(n => Instances(n, "Rupture Spewer").First(f => (string)f["target"]!["path"]! == "attack")["allowSharedRequired"] = false);
        Rejects(n => Instances(n, "Charger").First(f => (bool)f["editable"]! == false)["reason"] = null);
        Rejects(n => n["model"]!["fields"]!["zone.armor"]!["apiFieldConstant"] = "zone.armor");
        Rejects(n => Instances(n, "Charger").First(f => (string)f["target"]!["path"]! == "damage_zone")["target"]!["zone"] = "zone_99");
    }

    [Fact] public async Task The_catalog_is_parsed_once_and_indexed_for_the_editor()
    {
        using var e = new TestEnvironment(); var bytes = Catalog(e);
        var clock = Stopwatch.StartNew(); var catalog = EnemyAuthoringReader.Read(bytes, "0.27.0"); clock.Stop();
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(10), "enemy catalog read took " + clock.Elapsed);
        // Per-class field arrays and search text are built once; the editor reuses them on every render.
        Assert.Same(catalog.Fields(Charger), catalog.Fields(Charger)); Assert.Equal(6 + 6 * 17, catalog.Fields(Charger).Count);
        var (_, sdk) = await Fresh(e);
        clock.Restart(); foreach (var f in sdk.Entities!.Enemies!.FieldInstances) Assert.Same(f, sdk.Entities.Field(f.InstanceKey)); clock.Stop();
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(2), "10,425 indexed field lookups took " + clock.Elapsed);
    }

    // Proves a class under a new wiki name the way Runtime's generator would: name, wiki evidence and every consumer reference.
    private static void Rename(JsonNode n, string className, string wikiName)
    {
        var c = Class(n, className); var old = (string)c["name"]!;
        c["name"] = wikiName; c["wikiName"] = wikiName;
        if (!c["wikiCandidates"]!.AsArray().Any(x => (string)x! == wikiName)) { c["wikiCandidates"]!.AsArray().Add(wikiName); c["wikiCandidateEvidence"]!.AsArray().Add(new JsonObject { ["page"] = wikiName, ["zonesMatched"] = 2, ["kindAgrees"] = true, ["nativeClassesMatchingPage"] = 1 }); }
        c["identity"]!["wikiEvidence"] = new JsonObject { ["page"] = wikiName, ["kind"] = "enemy", ["anatomyLabel"] = null, ["zonesMatched"] = 2, ["zonePairs"] = new JsonArray() };
        static void Replace(JsonArray a, string from, string to) { for (var i = 0; i < a.Count; i++) if ((string)a[i]! == from) a[i] = to; }
        foreach (var x in n["classes"]!.AsArray().Select(x => x!)) { Replace(x["healthRecordConsumers"]!.AsArray(), old, wikiName); foreach (var a in x["attacks"]!.AsArray()) Replace(a!["sharedWithClasses"]!.AsArray(), old, wikiName); }
        foreach (var f in n["fieldInstances"]!.AsArray().Select(f => f!))
        {
            if ((string)f["target"]!["enemy"]! == old) f["target"]!["enemy"] = wikiName;
            if (f["sharedConsumers"] is JsonArray consumers) Replace(consumers, old, wikiName);
        }
    }
    private static string Flat(string s) => System.Text.RegularExpressions.Regex.Replace(s, @"\s+", " ");
    private static int CountOf(string text, string value) => (text.Length - text.Replace(value, "", StringComparison.Ordinal).Length) / value.Length;
}
