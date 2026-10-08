using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using HD2RuntimeGUI.Core.Audit;
using HD2RuntimeGUI.Core.Generation;
using HD2RuntimeGUI.Core.Metadata;
using HD2RuntimeGUI.Core.Projects;
using HD2RuntimeGUI.Core.Services;
using Xunit;

namespace HD2RuntimeGUI.Tests;

// The Helldiver type (hd2.helldiver(), HelldiverFieldCapabilities.json, HD2Runtime 0.30.0 development at b3f42ec): the strict reader, the 52
// published fields on the shared entity pipeline, the Lua forms (a transaction on the entity, a patch per zone, enums written as names), range
// refusal, save / reopen, the export fixture P1-helldiver (validated with Runtime's own validator by build-rc) and the capability audit.
public sealed class HelldiverAuthoringTests
{
    private static byte[] Bundled => SdkCache.BundledComposition()[HelldiverAuthoringReader.FileName];
    private static async Task<BuilderWorkspace> Fresh(TestEnvironment e, string name = "P1 Helldiver") => (await ExportFixtureTests.Fresh(e, name)).W;
    private static HelldiverCatalog Catalog(BuilderWorkspace w) => w.Metadata!.Entities!.Helldiver!;
    private static EntityField Field(BuilderWorkspace w, string id, string? zone = null) => Catalog(w).FieldInstances.Single(f => f.SemanticFieldId == id && f.Target.Zone == zone);
    // The generated request (one pcall-isolated block) whose target is this Lua accessor, each line trimmed.
    private static string Operation(string lua, string target) => string.Join('\n', System.Text.RegularExpressions.Regex.Split(lua, @"(?=add\(function\(\) return )")
        .Single(b => b.Contains("target=" + target + ",", StringComparison.Ordinal)).Split('\n').Select(l => l.Trim()));
    private static byte[] Mutate(Action<JsonNode> edit)
    {
        var n = JsonNode.Parse(Bundled)!; edit(n); return JsonSerializer.SerializeToUtf8Bytes(n);
    }
    private static JsonNode Instance(JsonNode n, string id, string? zone = null) => n["fieldInstances"]!.AsArray().Select(f => f!)
        .First(f => (string)f["semanticFieldId"]! == id && (string?)f["target"]!["zone"] == zone);

    [Fact] public void The_reader_accepts_the_bundled_catalog_and_adapts_all_52_fields()
    {
        var c = HelldiverAuthoringReader.Read(Bundled);
        Assert.Equal((52, 27, 6), (c.FieldInstances.Length, c.FieldInstances.Select(f => f.SemanticFieldId).Distinct().Count(), c.Target.Zones.Length));
        Assert.Equal(22, c.Entity.Count()); Assert.All(c.Target.Zones, z => Assert.Equal(5, c.Zone(z.Id).Count()));
        Assert.Equal(52, c.FieldInstances.Select(f => f.InstanceKey).Distinct().Count());
        Assert.All(c.FieldInstances, f =>
        {
            Assert.True(f.Editable); Assert.True(f.AllowSharedRequired); Assert.Equal("allow_unverified_effect", f.Acknowledgement);
            Assert.Equal(("helldiver", "Helldiver"), (f.Target.Resource, f.Target.Entity));
            Assert.StartsWith("helldiver:" + f.SemanticFieldId, f.InstanceKey);
            // One operation (and plan) group per target: the entity, or one zone.
            Assert.Equal(f.OperationGroup, f.PlanGroup); Assert.EndsWith(f.Target.Zone is { } z ? "/zone/" + z : "/entity", f.OperationGroup);
            Assert.Contains(f.Evidence.Tier, HelldiverAuthoringReader.Grades);
        });
        // Ranges and vanilla values as published (sdk/docs/helldiver-fields.md).
        var jog = c.FieldInstances.Single(f => f.SemanticFieldId == "helldiver.speed.jog");
        Assert.Equal(("hd2.fields.helldiver.speed_jog", 3.2, new EntityRange(0, 10, false)), (jog.ApiFieldConstant, jog.CurrentDefault.GetDouble(), jog.Range!));
        Assert.Contains(jog.Notices!, n => n.Kind == HelldiverAuthoringReader.SpeedCapNotice); Assert.Contains(jog.Notices!, n => n.Kind == HelldiverAuthoringReader.PrivateCopyNotice);
        var sprint = c.FieldInstances.Single(f => f.SemanticFieldId == "helldiver.stamina.sprint_duration");
        Assert.Equal((23.0, 1.0, 3600.0), (sprint.CurrentDefault.GetDouble(), sprint.Range!.Min, sprint.Range.Max));
        var explosion = c.FieldInstances.Single(f => f.SemanticFieldId == "entity.explosive_damage_percentage");
        Assert.Equal(("hd2.fields.entity.explosive_damage_percentage", 0.5, null), (explosion.ApiFieldConstant, explosion.CurrentDefault.GetDouble(), explosion.Target.Zone));
        var health = c.FieldInstances.Single(f => f.SemanticFieldId == "zone.health" && f.Target.Zone == "head");
        Assert.Equal(("integer", 85, new EntityRange(1, 100000, true)), (health.Type, health.CurrentDefault.GetInt32(), health.Range!));
        Assert.Contains(health.Notices!, n => n.Kind == HelldiverAuthoringReader.SpawnNotice);
        Assert.Equal([85, 60, 35, 35, 45, 45], c.Target.Zones.OrderBy(z => z.Index).Select(z => c.Zone(z.Id).Single(f => f.SemanticFieldId == "zone.health").CurrentDefault.GetInt32()));
        var multiplier = c.FieldInstances.Single(f => f.SemanticFieldId == "zone.damage_multiplier" && f.Target.Zone == "body");
        Assert.True(multiplier.IsEnum); Assert.Null(multiplier.EffectiveRange); Assert.Equal("normal", multiplier.CurrentDefault.GetString());
        Assert.Equal(["none", "critical", "normal", "reduced", "symbolic"], multiplier.AllowedValues!);
        Assert.Equal(0.25, c.Factor(multiplier.SemanticFieldId, "symbolic"));
        Assert.Equal(["inherit", "critical", "normal", "reduced", "symbolic"], c.FieldInstances.First(f => f.SemanticFieldId == "zone.damage_multiplier_dps").AllowedValues!);
        Assert.Equal(12, c.FieldInstances.Count(f => f.IsEnum));
        Assert.Equal(10.0, c.SpeedCapMetersPerSecond);
    }

    [Fact] public void Every_field_constant_is_one_the_sdk_stub_publishes()
    {
        var stub = Encoding.UTF8.GetString(SdkCache.BundledComposition()[HD2RuntimeGUI.Core.Scripting.LuaApiIndex.CacheName]);
        foreach (var f in HelldiverAuthoringReader.Read(Bundled).FieldInstances.DistinctBy(f => f.SemanticFieldId))
            Assert.Contains("---@field " + f.ApiFieldConstant.Split('.')[^1] + " \"" + f.SemanticFieldId + "\"", stub);
    }

    public static TheoryData<string> Mutations => ["contract", "summary", "opt-in", "live-tested", "range", "enum", "zone", "duplicate", "accessor", "default"];
    [Theory, MemberData(nameof(Mutations))] public void The_reader_refuses_a_malformed_catalog(string mutation)
    {
        var bytes = Mutate(n =>
        {
            var jog = Instance(n, "helldiver.speed.jog");
            switch (mutation)
            {
                case "contract": n["contract"] = "hd2runtime.helldiver.type_fields.v2"; break;
                case "summary": n["summary"]!["instances"] = 51; break;
                case "opt-in": jog["acknowledgements"] = new JsonArray("allow_shared"); break;
                case "live-tested": jog["liveTested"] = true; break;
                case "range": jog["max"] = 3; break;
                case "enum": Instance(n, "zone.damage_multiplier", "head")["allowedValues"]!.AsArray().Add("immune"); break;
                case "zone": Instance(n, "zone.health", "body")["target"]!["zone"] = "tail"; break;
                case "duplicate": n["fieldInstances"]!.AsArray().Add(jog.DeepClone()); break;
                case "accessor": n["target"]!["accessor"] = "hd2.helldiver('Helldiver')"; break;
                case "default": Instance(n, "zone.damage_multiplier_dps", "head")["currentDefault"] = "none"; break;
            }
        });
        // An unknown contract is an unsupported SDK; anything else is inconsistent metadata.
        if (mutation == "contract") Assert.Throws<UnsupportedSdkException>(() => HelldiverAuthoringReader.Read(bytes));
        else Assert.ThrowsAny<InvalidDataException>(() => HelldiverAuthoringReader.Read(bytes));
    }

    [Fact] public async Task The_page_is_listed_only_with_the_catalog()
    {
        using var e = new TestEnvironment(); var w = await Fresh(e);
        Assert.Contains(Navigation.Build(w.Metadata, w.Project), i => i.Page == "helldiver" && i.Count == 52 && i.Section == Navigation.Workspace);
        var without = w.Metadata! with { Entities = w.Metadata.Entities!.WithHelldiver(null) };
        Assert.DoesNotContain(Navigation.Build(without, w.Project), i => i.Page == "helldiver");
        // The enemy catalog carries the Helldiver catalog through (and back).
        Assert.NotNull(w.Metadata.Entities.WithEnemies(w.Metadata.Entities.Enemies).Helldiver);
    }

    [Fact] public async Task Speeds_zones_and_health_are_one_request_per_target_with_both_opt_ins_and_survive_reopen()
    {
        using var e = new TestEnvironment(); var w = await Fresh(e);
        await w.SetEntityAsync(Field(w, "helldiver.speed.jog").InstanceKey, "4.5");
        await w.SetEntityAsync(Field(w, "helldiver.stamina.recover_delay").InstanceKey, "0.5");
        await w.SetEntityAsync(Field(w, "zone.damage_multiplier", "head").InstanceKey, "reduced");
        await w.SetEntityAsync(Field(w, "zone.durable_resistance", "body").InstanceKey, "0.25");
        await w.SetEntityAsync(Field(w, "zone.damage_multiplier_dps", "body").InstanceKey, "inherit");
        await w.SetEntityAsync(Field(w, "zone.health", "leg_left").InstanceKey, "90");
        Assert.Null(w.BuildError); var lua = w.LuaPreview;
        // The entity: one transaction, both opt-ins, Runtime's field constants.
        var entity = Operation(lua, "hd2.helldiver()");
        Assert.Contains("hd2.ensure({\ntransaction={\n", entity);
        Assert.Contains("target=hd2.helldiver(),\nallow_shared=true,\nallow_unverified_effect=true,\nchanges={\n"
            + "{field=hd2.fields.helldiver.speed_jog,expect=3.2,value=4.5},\n{field=hd2.fields.helldiver.stamina_recover_delay,expect=1.5,value=0.5},\n},\n", entity);
        // A zone enum: a patch on that zone, the name quoted for expect and value.
        var head = Operation(lua, "hd2.helldiver():zone('head')");
        Assert.Contains("hd2.ensure({\npatch={\n", head);
        Assert.Contains("target=hd2.helldiver():zone('head'),\nallow_shared=true,\nallow_unverified_effect=true,\nfield=hd2.fields.zone.damage_multiplier,\nexpect='normal',\nvalue='reduced',\n", head);
        // Two fields of one zone (a number and an enum): one transaction on that zone.
        var body = Operation(lua, "hd2.helldiver():zone('body')");
        Assert.Contains("hd2.ensure({\ntransaction={\n", body);
        Assert.Contains("allow_shared=true,\nallow_unverified_effect=true,\nchanges={\n{field=hd2.fields.zone.damage_multiplier_dps,expect='normal',value='inherit'},\n"
            + "{field=hd2.fields.zone.durable_resistance,expect=0,value=0.25},\n},\n", body);
        // Zone health: an integer, from the next spawn.
        Assert.Contains("target=hd2.helldiver():zone('leg_left'),\nallow_shared=true,\nallow_unverified_effect=true,\nfield=hd2.fields.zone.health,\nexpect=45,\nvalue=90,\n",
            Operation(lua, "hd2.helldiver():zone('leg_left')"));
        Assert.DoesNotContain("hd2.plan", lua);
        // Format 14 (1.7.0) marks Helldiver edits; the project reopens to the same Lua.
        Assert.Equal(14, w.Project!.FormatVersion); Assert.Equal(14, ProjectIdentity.RequiredFormat(w.Project));
        Assert.All(w.Project.EntityChanges, c => Assert.Equal(("helldiver", "Helldiver", "Helldiver"), (c.Resource, c.Entity, c.Group)));
        await w.OpenAsync(w.Project.Id); Assert.Null(w.BuildError); Assert.Equal(lua, w.LuaPreview); Assert.Equal(6, w.Project!.EntityChanges.Count);
        var (manifest, exported) = await ExportFixtureTests.Export(w, "P1-helldiver");
        Assert.Contains("hd2.helldiver():zone('leg_left')", exported);
        Assert.Equal(SdkPin.Version, manifest.RootElement.GetProperty("requires").GetProperty("hd2runtime").GetProperty("min_version").GetString());
    }

    [Fact] public async Task Values_outside_the_published_range_or_names_are_refused_where_they_are_typed()
    {
        using var e = new TestEnvironment(); var w = await Fresh(e);
        // The game caps the target speed at 10 m/s; the reviewed range ends there.
        await Assert.ThrowsAsync<InvalidDataException>(() => w.SetEntityAsync(Field(w, "helldiver.speed.sprint").InstanceKey, "10.5"));
        await Assert.ThrowsAsync<InvalidDataException>(() => w.SetEntityAsync(Field(w, "helldiver.stamina.sprint_duration").InstanceKey, "0"));
        await Assert.ThrowsAsync<InvalidDataException>(() => w.SetEntityAsync(Field(w, "zone.health", "head").InstanceKey, "12.5"));
        await Assert.ThrowsAsync<InvalidDataException>(() => w.SetEntityAsync(Field(w, "zone.health", "head").InstanceKey, "0"));
        // 'none' is not a damage-over-time multiplier (Runtime refuses it); names are exact.
        await Assert.ThrowsAsync<InvalidDataException>(() => w.SetEntityAsync(Field(w, "zone.damage_multiplier_dps", "arm_left").InstanceKey, "none"));
        await Assert.ThrowsAsync<InvalidDataException>(() => w.SetEntityAsync(Field(w, "zone.damage_multiplier", "arm_left").InstanceKey, "1.5"));
        Assert.Empty(w.Project!.EntityChanges);
        // At the bound is accepted; back to vanilla removes the edit.
        await w.SetEntityAsync(Field(w, "helldiver.speed.sprint").InstanceKey, "10"); Assert.Single(w.Project.EntityChanges);
        await w.SetEntityAsync(Field(w, "helldiver.speed.sprint").InstanceKey, "5.5"); Assert.Empty(w.Project.EntityChanges);
    }

    [Fact] public async Task A_saved_change_with_an_unknown_zone_or_name_is_refused_on_open()
    {
        using var e = new TestEnvironment(); var w = await Fresh(e);
        await w.SetEntityAsync(Field(w, "zone.damage_multiplier", "head").InstanceKey, "critical");
        var c = w.Project!.EntityChanges.Single();
        ProjectIdentity.Validate(w.Project);
        foreach (var bad in new[] { c with { Zone = "Head!" }, c with { Entity = "Player" }, c with { DesiredValue = JsonSerializer.SerializeToElement(1.5) }, c with { Path = "zone" } })
        {
            w.Project.EntityChanges = [bad];
            Assert.Throws<InvalidDataException>(() => ProjectIdentity.Validate(w.Project));
        }
    }

    [Fact] public async Task Numbers_get_in_game_options_and_enums_explain_why_not()
    {
        using var e = new TestEnvironment(); var w = await Fresh(e);
        await w.SetEntityAsync(Field(w, "helldiver.speed.sprint").InstanceKey, "8");
        await w.SetEntityAsync(Field(w, "zone.damage_multiplier", "head").InstanceKey, "reduced");
        var sprint = ModOptionsService.EntityTarget(w.Metadata!, w.Project!.EntityChanges.Single(c => c.SemanticFieldId == "helldiver.speed.sprint"), w.Project)!;
        Assert.Null(sprint.Blocker); Assert.Equal((0.0, 10.0), (sprint.RangeMin, sprint.RangeMax)); sprint.Check(9);
        Assert.Throws<InvalidDataException>(() => sprint.Check(11));
        Assert.NotNull(ModOptionsService.EntityTarget(w.Metadata!, w.Project.EntityChanges.Single(c => c.SemanticFieldId == "zone.damage_multiplier"), w.Project)!.Blocker);
    }

    [Fact] public async Task The_capability_audit_lists_every_helldiver_field_as_authored()
    {
        using var e = new TestEnvironment(); var w = await Fresh(e);
        var report = CapabilityAudit.Run(w.Metadata!, SdkCache.BundledComposition());
        var entries = report.Entries.Where(x => x.Area == "helldiver").ToArray();
        Assert.Equal(27, entries.Length);
        Assert.All(entries, x => { Assert.Equal(CapabilityClass.SUPPORTED_UI, x.Class); Assert.Equal(x.Instances, x.WritableInstances); });
        Assert.Equal(CapabilityClass.SUPPORTED_UI, report.Files.Single(f => f.Name == HelldiverAuthoringReader.FileName).Class);
    }
}
