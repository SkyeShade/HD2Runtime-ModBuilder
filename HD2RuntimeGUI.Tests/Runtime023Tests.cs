using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using HD2RuntimeGUI.Core.Generation;
using HD2RuntimeGUI.Core.Metadata;
using HD2RuntimeGUI.Core.Models;
using HD2RuntimeGUI.Core.Services;
using Xunit;

namespace HD2RuntimeGUI.Tests;

// Runtime 0.23.0: Shield Generator Relay base vs shield, hd2.vehicle, vehicle mounts and hd2.backpack (pinned to the published 0.23.0 archive).
public sealed class Runtime023Tests
{
    private const string Relay = "FX-12 Shield Generator Relay", Gunner = "M-102 Gunner FRV", Bastion = "TD-220 Bastion MK XVI", Jump = "LIFT-850 Jump Pack";
    private static async Task<BuilderWorkspace> Workspace(TestEnvironment e, string resource = "mods/tests/runtime023")
    {
        var sdk = await SdkFixtures.Install(e, "0.23.0");
        var w = e.Workspace(); await w.CreateAsync(new("Runtime023", "Tests", resource, "0.1.0"), sdk); return w;
    }
    private static StratagemField Strat(BuilderWorkspace w, string name, string id, string? zone = null) => w.Metadata!.Stratagems!.FieldInstances.Single(f =>
        f.Target.Stratagem == name && f.SemanticFieldId == id && (zone == null || f.Target.Zone == zone));
    private static EntityField Entity(BuilderWorkspace w, string entity, string id, string? zone = null, string? mount = null) => w.Metadata!.Entities!.AllFields.Single(f =>
        f.Target.Entity == entity && f.SemanticFieldId == id && f.Target.Zone == zone && f.Target.Mount == mount);
    private static JsonNode Json(string file) => JsonNode.Parse(SdkFixtures.Entry("0.23.0", file))!;
    private static EntityAuthoring Read(JsonNode vehicles, JsonNode backpacks, JsonNode? stratagems = null) => EntityAuthoringReader.Read(
        Encoding.UTF8.GetBytes(vehicles.ToJsonString()), Encoding.UTF8.GetBytes(backpacks.ToJsonString()), "0.23.0",
        new StratagemCatalogReader().Read(Encoding.UTF8.GetBytes((stratagems ?? Json(StratagemCatalogReader.FileName)).ToJsonString())));

    [Fact] public async Task Published_023_catalogs_load_with_audited_counts()
    {
        using var e = new TestEnvironment(); var w = await Workspace(e); var s = w.Metadata!; var v = s.Entities!.Vehicles; var b = s.Entities.Backpacks;
        Assert.Equal("0.23.0", s.Version); Assert.Equal(1468, s.Stratagems!.FieldInstances.Length); Assert.Equal(1375, s.Stratagems.FieldInstances.Count(f => f.Editable));
        Assert.Equal(11, v.Vehicles.Length); Assert.Equal(9, v.Vehicles.Count(x => x.CallInStratagem.Known)); Assert.Equal(718, v.FieldInstances.Length);
        Assert.Equal(226, v.Vehicles.Sum(x => x.Durability.Zones.Length)); Assert.Equal(678, v.FieldInstances.Count(f => f.Target.Path == "damage_zone"));
        Assert.Equal(22, v.Vehicles.Sum(x => x.Mounts.Length)); Assert.Equal(18, v.Vehicles.Sum(x => x.Mounts.Count(m => m.Swappable))); Assert.Equal(79, v.MountedWeapons.Length);
        Assert.Equal(960, v.FieldInstances.Where(f => f.IsReference).Sum(f => f.AllowedValues!.Length));
        Assert.Equal(13, b.Backpacks.Length); Assert.Equal(31, b.FieldInstances.Length); Assert.Equal(9, b.FieldInstances.Count(f => f.Editable));
        Assert.Equal(22, s.Entities.CallIns.Count); Assert.Equal(9, s.Entities.CallIns.Count(c => c.Value.Resource == "vehicle"));
        Assert.Equal(new Dictionary<string, int> { ["gameplay_proven"] = 64, ["schema_proven"] = 636, ["live_write_verified"] = 2, ["structural_reference"] = 16 },
            v.FieldInstances.GroupBy(f => f.Evidence.Tier).ToDictionary(g => g.Key, g => g.Count()));
    }

    [Fact] public async Task Shield_relay_separates_physical_base_body_zone_and_shield_projector()
    {
        using var e = new TestEnvironment(); var w = await Workspace(e); var g = StratagemGraph.Build(w.Metadata!.Stratagems!, Relay);
        var entity = Assert.Single(g.Entities);
        Assert.Equal(["entity.health", "entity.armor", "payload.lifetime"], entity.Stats.Select(f => f.SemanticFieldId));
        var zone = Assert.Single(entity.Zones); Assert.Equal("body_front", zone.Zone.Name); Assert.Equal("zone_0", zone.Zone.ZoneId);
        Assert.Equal(["zone.armor", "zone.health", "zone.affects_main_health"], zone.Fields.Select(f => f.SemanticFieldId));
        var shield = entity.Shield!; Assert.True(shield.Config.SameEntityAsBase); Assert.Equal("unresolved", shield.Config.RuntimeShieldInstance);
        Assert.Equal([15, 4000], shield.Fields.Select(f => f.CurrentDefault.GetDouble()));
        Assert.Equal(["hd2.fields.shield.entity_radius", "hd2.fields.shield.entity_durability"], shield.Fields.Select(f => f.ApiFieldConstant));
        Assert.Contains(shield.Config.BlockedFields, b => b.Field.Contains("recharge"));
        Assert.DoesNotContain(g.AllFields, f => f.SemanticFieldId.Contains("recharge"));
        Assert.All(g.AllFields.Where(f => f.Target.Path != "stratagem" || f.SemanticFieldId == "stratagem.cooldown"), f => Assert.True(f.Editable));
        Assert.Equal(g.AllFields.Count(), w.Metadata.Stratagems.FieldInstances.Count(f => f.Target.Stratagem == Relay));
        Assert.All(w.Metadata.Stratagems.Stratagems.Where(r => r.DeployedEntity != null), r =>
            Assert.Equal(StratagemGraph.Build(w.Metadata.Stratagems, r.Name).AllFields.Count(), w.Metadata.Stratagems.FieldInstances.Count(f => f.Target.Stratagem == r.Name)));
    }
    [Fact] public async Task Shield_relay_edits_generate_base_zone_and_shield_targets()
    {
        using var e = new TestEnvironment(); var w = await Workspace(e);
        await w.SetStratagemAsync(Strat(w, Relay, "shield.radius").InstanceKey, "8"); await w.SetStratagemAsync(Strat(w, Relay, "shield.durability").InstanceKey, "40000");
        await w.SetStratagemAsync(Strat(w, Relay, "payload.lifetime").InstanceKey, "90"); await w.SetStratagemAsync(Strat(w, Relay, "zone.health", "zone_0").InstanceKey, "4500");
        Assert.Null(w.BuildError); var lua = w.LuaPreview;
        Assert.Contains("target=hd2.stratagem('FX-12 Shield Generator Relay'):deployed_entity():shield(),", lua);
        Assert.Contains("{field=hd2.fields.shield.entity_radius,expect=15,value=8}", lua);
        Assert.Contains("target=hd2.stratagem('FX-12 Shield Generator Relay'):deployed_entity():damage_zone('zone_0'),", lua);
        Assert.Contains("field=hd2.fields.payload.entity_lifetime,", lua); Assert.Contains("plan={", lua);
        var change = w.Project!.StratagemChanges.Single(c => c.Path == "damage_zone"); Assert.Equal("zone_0", change.Zone); Assert.Equal("deployed_entity", change.TargetKind);
        await w.OpenAsync(w.Project.Id); Assert.Equal(4, w.Project!.StratagemChanges.Count); Assert.Null(w.BuildError);
    }

    [Fact] public async Task Vehicle_zones_stay_distinct_with_their_own_fields()
    {
        using var e = new TestEnvironment(); var w = await Workspace(e); var v = w.Metadata!.Entities!.Vehicles;
        foreach (var vehicle in v.Vehicles)
        {
            Assert.Equal(vehicle.Durability.Zones.Length, vehicle.Durability.Zones.Select(z => z.ZoneId).Distinct().Count());
            foreach (var zone in vehicle.Durability.Zones)
            {
                var fields = zone.FieldInstanceKeys.Select(k => v.FieldInstances.Single(f => f.InstanceKey == k)).ToArray();
                Assert.Equal(["zone.affects_main_health", "zone.armor", "zone.health"], fields.Select(f => f.SemanticFieldId).Order());
                Assert.All(fields, f => { Assert.Equal(zone.ZoneId, f.Target.Zone); Assert.Equal(vehicle.Name, f.Target.Vehicle); });
            }
        }
        var bastion = v.Find(Bastion)!; Assert.Equal(31, bastion.Durability.Zones.Length);
        Assert.Contains(bastion.Durability.FieldInstanceKeys, k => v.FieldInstances.Single(f => f.InstanceKey == k).Evidence.Tier == "gameplay_proven");
        Assert.All(v.FieldInstances.Where(f => f.Target.Vehicle == "EXO-45 Patriot Exosuit" && !f.IsReference), f => Assert.Equal("schema_proven", f.Evidence.Tier));
    }
    [Fact] public async Task Vehicle_durability_and_zone_edits_generate_one_plan_per_vehicle()
    {
        using var e = new TestEnvironment(); var w = await Workspace(e);
        await w.SetEntityAsync(Entity(w, Bastion, "entity.health").InstanceKey, "16000"); await w.SetEntityAsync(Entity(w, Bastion, "entity.armor").InstanceKey, "5");
        await w.SetEntityAsync(Entity(w, Bastion, "zone.affects_main_health", "zone_3").InstanceKey, "0");
        await w.SetEntityAsync(Entity(w, Bastion, "zone.armor", "zone_4").InstanceKey, "5");
        Assert.Null(w.BuildError); var lua = w.LuaPreview;
        Assert.Equal(1, lua.Split("hd2.ensure(").Length - 1); Assert.Contains("plan={", lua);
        Assert.Contains("target=hd2.vehicle('TD-220 Bastion MK XVI'),", lua); Assert.Contains("{field=hd2.fields.entity.health,expect=8000,value=16000}", lua);
        Assert.Contains("target=hd2.vehicle('TD-220 Bastion MK XVI'):damage_zone('zone_3'),", lua); Assert.Contains("field=hd2.fields.zone.affects_main_health,", lua);
        Assert.Contains("target=hd2.vehicle('TD-220 Bastion MK XVI'):damage_zone('zone_4'),", lua);
        Assert.DoesNotContain("allow_unverified_reference", lua); Assert.DoesNotContain("vehicle-zone/v1", lua);
        await w.OpenAsync(w.Project!.Id); Assert.Equal(6, w.Project!.FormatVersion); Assert.Equal(4, w.Project.EntityChanges.Count);
        var json = File.ReadAllText(e.Paths.ProjectFile(w.Project.Id)); Assert.Contains("\"zone\": \"zone_3\"", json); Assert.DoesNotContain("backing:", json);
        await w.SetEntityAsync(Entity(w, Bastion, "entity.armor").InstanceKey, "4"); Assert.Equal(3, w.Project.EntityChanges.Count);
        await w.ResetEntityAsync("vehicle", Bastion); Assert.Empty(w.Project.EntityChanges);
    }

    [Fact] public async Task Mount_replacements_are_published_same_family_weapons_only()
    {
        using var e = new TestEnvironment(); var w = await Workspace(e); var v = w.Metadata!.Entities!.Vehicles;
        foreach (var mount in v.Vehicles.SelectMany(x => x.Mounts.Select(m => (Vehicle: x, Mount: m))))
        {
            var field = v.FieldInstances.SingleOrDefault(f => f.Target.Vehicle == mount.Vehicle.Name && f.Target.Mount == mount.Mount.MountId);
            if (!mount.Mount.Swappable) { Assert.Null(field); Assert.NotEqual("weapon", mount.Mount.CurrentKind); Assert.False(string.IsNullOrWhiteSpace(mount.Mount.BlockedReason)); continue; }
            var family = v.Weapon(mount.Mount.Current!.SemanticId)!.AttackFamily;
            Assert.All(field!.AllowedValues!, id => Assert.Equal(family, v.Weapon(id)!.AttackFamily));
        }
        Assert.Equal(4, v.Vehicles.Sum(x => x.Mounts.Count(m => !m.Swappable)));
        var gun = Entity(w, Gunner, "mount.weapon", mount: "slot_0"); var svc = new EntityChangeService();
        var other = v.MountedWeapons.First(m => m.AttackFamily != v.Weapon(gun.CurrentDefault.GetString()!)!.AttackFamily);
        Assert.Throws<InvalidDataException>(() => svc.Create(w.Metadata, gun.InstanceKey, other.SemanticId));
        Assert.Throws<InvalidDataException>(() => svc.Create(w.Metadata, gun.InstanceKey, "0x1234567890abcdef"));
        Assert.Throws<InvalidDataException>(() => svc.Create(w.Metadata, gun.InstanceKey, "mounted-weapon/v1/made-up/0000000000000000"));
    }
    [Fact] public async Task Mount_swap_requires_acknowledgement_and_emits_allow_unverified_reference()
    {
        using var e = new TestEnvironment(); var w = await Workspace(e);
        var gun = Entity(w, Gunner, "mount.weapon", mount: "slot_0"); Assert.Equal("live_write_verified", gun.Evidence.Tier);
        var gater = w.Metadata!.Entities!.Vehicles.Find("GATER Oil Rig")!.Mounts.Single().Current!.SemanticId;
        Assert.Contains(gater, gun.AllowedValues!);
        await w.SetEntityAsync(gun.InstanceKey, gater);
        Assert.Contains("unverified", w.BuildError); Assert.DoesNotContain("allow_unverified_reference", w.LuaPreview);
        await w.SetEntityReferenceAcknowledgedAsync(gun.InstanceKey, true); Assert.Null(w.BuildError);
        var lua = w.LuaPreview;
        Assert.Contains("target=hd2.vehicle('M-102 Gunner FRV'):mount('slot_0'),", lua); Assert.Contains("allow_unverified_reference=true,", lua);
        Assert.Contains("field=hd2.fields.mount.weapon,", lua); Assert.Contains($"expect='{gun.CurrentDefault.GetString()}',", lua); Assert.Contains($"value='{gater}',", lua);
        // A different replacement needs a fresh acknowledgement.
        var another = gun.AllowedValues!.First(x => x != gater && x != gun.CurrentDefault.GetString());
        await w.SetEntityAsync(gun.InstanceKey, another); Assert.Contains("unverified", w.BuildError);
        await w.SetEntityAsync(gun.InstanceKey, gun.CurrentDefault.GetString()!); Assert.Empty(w.Project!.EntityChanges); Assert.Null(w.BuildError);
    }

    [Fact] public async Task Backpack_fields_follow_published_writability()
    {
        using var e = new TestEnvironment(); var w = await Workspace(e); var b = w.Metadata!.Entities!.Backpacks;
        string[] Writable(string name) => b.FieldInstances.Where(f => f.Target.Backpack == name && f.Editable).Select(f => f.SemanticFieldId).Order().ToArray();
        Assert.Equal(["jump.vertical_launch_velocity", "recharge.time"], Writable(Jump));
        Assert.Equal(["recharge.time"], Writable("LIFT-860 Hover Pack"));
        Assert.Equal(["shield.durability", "shield.radius"], Writable("SH-32 Shield Generator Pack"));
        Assert.Equal(["entity.armor", "entity.health"], Writable("SH-20 Ballistic Shield Backpack"));
        Assert.Equal(["entity.armor", "entity.health"], Writable("SH-51 Directional Shield"));
        foreach (var name in new[] { "B-1 Supply Pack", "AX/AR-23 Guard Dog", "LIFT-182 Warp Pack", "B-100 Portable Hellbomb" }) Assert.Empty(Writable(name));
        Assert.Empty(b.Find("LIFT-182 Warp Pack")!.FieldInstanceKeys); Assert.NotEmpty(b.Find("LIFT-182 Warp Pack")!.BlockedFields);
        var hoverLaunch = Entity(w, "LIFT-860 Hover Pack", "jump.vertical_launch_velocity"); Assert.False(hoverLaunch.Editable); Assert.False(string.IsNullOrWhiteSpace(hoverLaunch.Reason));
        await Assert.ThrowsAsync<InvalidDataException>(() => w.SetEntityAsync(hoverLaunch.InstanceKey, "60"));
        await Assert.ThrowsAsync<InvalidDataException>(() => w.SetEntityAsync(Entity(w, "B-1 Supply Pack", "deposit.capacity").InstanceKey, "8"));
        Assert.All(new[] { "recharge.time", "jump.vertical_launch_velocity" }, id => Assert.Equal("gameplay_proven", Entity(w, Jump, id).Evidence.Tier));
        Assert.Equal("schema_proven", Entity(w, "LIFT-860 Hover Pack", "recharge.time").Evidence.Tier);
    }
    [Fact] public async Task Jump_pack_recreation_matches_published_example_as_a_plan()
    {
        using var e = new TestEnvironment(); var w = await Workspace(e);
        await w.SetEntityAsync(Entity(w, Jump, "recharge.time").InstanceKey, "8"); await w.SetEntityAsync(Entity(w, Jump, "jump.vertical_launch_velocity").InstanceKey, "50");
        Assert.Null(w.BuildError); var lua = w.LuaPreview;
        Assert.Contains("plan={", lua); Assert.Equal(2, lua.Split("target=hd2.backpack('LIFT-850 Jump Pack'),").Length - 1);
        Assert.Contains("field=hd2.fields.recharge.time,", lua); Assert.Contains("expect=15,", lua); Assert.Contains("value=8,", lua);
        Assert.Contains("field=hd2.fields.jump.vertical_launch_velocity,", lua); Assert.Contains("expect=40,", lua); Assert.Contains("value=50,", lua);
    }

    [Fact] public async Task Vehicle_and_backpack_call_ins_are_linked_both_ways_and_not_listed_twice()
    {
        using var e = new TestEnvironment(); var w = await Workspace(e); var s = w.Metadata!; var entities = s.Entities!;
        foreach (var (stratagem, (resource, entity)) in entities.CallIns)
        {
            var root = s.Stratagems!.Root(stratagem)!; Assert.Equal(resource, root.Family); Assert.Equal(resource, root.Delivers!.Kind);
            var semanticId = resource == "vehicle" ? entities.Vehicles.Find(entity)!.SemanticId : entities.Backpacks.Find(entity)!.SemanticId;
            Assert.Equal(semanticId, root.Delivers.SemanticId); Assert.Equal(stratagem, entities.CallInFor(resource, entity));
            Assert.False(StratagemCategories.Listed(root, entities));
        }
        Assert.All(entities.Vehicles.Vehicles.Where(v => v.CatalogSource == "native_only"), v => { Assert.False(v.CallInStratagem.Known); Assert.Null(entities.CallInFor("vehicle", v.Name)); });
        var nav = Navigation.Build(s, null);
        Assert.Equal(73, nav.Single(i => i.Page == "stratagems").Count); Assert.Equal(35, nav.Single(i => i.Page == "stratagems:support").Count);
        Assert.Equal(11, nav.Single(i => i.Page == "vehicles").Count); Assert.Equal(13, nav.Single(i => i.Page == "backpacks").Count);
        // The call-in cooldown stays an hd2.stratagem write even when edited from the vehicle editor.
        await w.SetStratagemAsync(Strat(w, Bastion, "stratagem.cooldown").InstanceKey, "300"); await w.SetEntityAsync(Entity(w, Bastion, "entity.armor").InstanceKey, "5");
        Assert.Contains("target=hd2.stratagem('TD-220 Bastion MK XVI'),", w.LuaPreview); Assert.Contains("target=hd2.vehicle('TD-220 Bastion MK XVI'),", w.LuaPreview);
        Assert.Single(w.Project!.StratagemChanges); Assert.Single(w.Project.EntityChanges);
    }

    [Fact] public async Task Project_0221_rebinds_to_0230_without_changing_saved_keys()
    {
        using var e = new TestEnvironment(); var old = await SdkFixtures.Install(e, "0.22.1"); Assert.Null(old.Entities);
        var w = e.Workspace(); await w.CreateAsync(new("Pinned", "Tests", "mods/tests/pinned_0221", "0.1.0"), old);
        var health = old.Stratagems!.FieldInstances.Single(f => f.Target.Stratagem == Relay && f.SemanticFieldId == "entity.health");
        var sway = old.SupportAuthoring!.FieldInstances.Single(f => f.SupportWeapon == "GR-8 Recoilless Rifle" && f.SemanticFieldId == "weapon.sway");
        await w.SetStratagemAsync(health.InstanceKey, "900"); await w.SetSupportAsync(sway.InstanceKey, "0.5");
        var keys = (w.Project!.StratagemChanges.Single().InstanceKey, w.Project.SupportChanges.Single().InstanceKey);
        await SdkFixtures.Install(e, "0.23.0");
        await w.OpenAsync(w.Project.Id); Assert.Equal("0.22.1", w.Project!.SdkVersion); Assert.Null(w.Metadata!.Entities); Assert.Equal(5, w.Project.FormatVersion);
        await w.RebindToInstalledSdkAsync();
        Assert.Equal("0.23.0", w.Project.SdkVersion); Assert.NotNull(w.Metadata!.Entities); Assert.Null(w.BuildError);
        Assert.Equal(keys, (w.Project.StratagemChanges.Single().InstanceKey, w.Project.SupportChanges.Single().InstanceKey));
        Assert.Null(w.StratagemIssue(w.Project.StratagemChanges.Single())); Assert.Empty(w.Project.EntityChanges);
    }
    [Fact] public async Task Legacy_format5_project_without_entity_properties_loads()
    {
        using var e = new TestEnvironment(); var w = await Workspace(e); await w.SetStratagemAsync(Strat(w, Relay, "entity.health").InstanceKey, "900");
        var path = e.Paths.ProjectFile(w.Project!.Id); var node = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        node.Remove("entityChanges"); node.Remove("entityApprovals"); foreach (var c in node["stratagemChanges"]!.AsArray()) c!.AsObject().Remove("zone");
        File.WriteAllText(path, node.ToJsonString()); await w.OpenAsync(w.Project.Id);
        Assert.Empty(w.Project!.EntityChanges); Assert.Null(w.BuildError); Assert.Contains("value=900", w.LuaPreview);
    }

    [Theory] [InlineData("foreign-family")] [InlineData("unknown-weapon")] [InlineData("zone-missing")] [InlineData("unknown-tier")] [InlineData("reverse-link")]
    [InlineData("summary")] [InlineData("audit")] [InlineData("non-weapon-control")] [InlineData("readonly-without-reason")]
    public void Malformed_vehicle_or_backpack_metadata_fails_closed(string fault)
    {
        var v = Json(EntityAuthoringReader.VehicleFile); var b = Json(EntityAuthoringReader.BackpackFile); JsonNode? s = null;
        var mountField = v["fieldInstances"]!.AsArray().First(f => (string?)f!["type"] == "mounted_weapon_reference")!;
        var weapons = v["mountedWeapons"]!.AsArray();
        switch (fault)
        {
            case "foreign-family":
                var family = (string?)weapons.First(x => (string?)x!["semanticId"] == (string?)mountField["currentDefault"])!["attackFamily"];
                var foreign = (string?)weapons.First(x => (string?)x!["attackFamily"] != family)!["semanticId"];
                mountField["allowedValues"]!.AsArray().Add(foreign);
                var vehicle = v["vehicles"]!.AsArray().First(x => (string?)x!["name"] == (string?)mountField["target"]!["vehicle"])!;
                vehicle["mounts"]!.AsArray().First(m => (string?)m!["mountId"] == (string?)mountField["target"]!["mount"])!["allowedReplacements"]!.AsArray().Add(foreign); break;
            case "unknown-weapon": mountField["currentDefault"] = "mounted-weapon/v1/invented/0000000000000000"; break;
            case "zone-missing": v["fieldInstances"]!.AsArray().First(f => (string?)f!["target"]!["path"] == "damage_zone")!["target"]!["zone"] = "zone_999"; break;
            case "unknown-tier": b["fieldInstances"]![0]!["evidence"]!["tier"] = "rumoured"; break;
            case "reverse-link": s = Json(StratagemCatalogReader.FileName); s["stratagems"]!.AsArray().First(x => (string?)x!["name"] == Jump)!["delivers"]!["semanticId"] = "backpack/v1/other/0"; break;
            case "summary": v["summary"]!["swappableMountSlots"] = 19; break;
            case "audit": b["instanceAudit"]!["exactMatch"] = false; break;
            case "non-weapon-control":
                var rack = v["vehicles"]!.AsArray().SelectMany(x => x!["mounts"]!.AsArray()).First(m => (bool)m!["swappable"]! == false)!;
                rack["swappable"] = true; break;
            case "readonly-without-reason": b["fieldInstances"]!.AsArray().First(f => (bool)f!["editable"]! == false)!["reason"] = null; break;
        }
        Assert.ThrowsAny<Exception>(() => Read(v, b, s));
        Assert.True(Record.Exception(() => Read(v, b, s)) is InvalidDataException or UnsupportedSdkException);
    }
    [Fact] public void Unknown_vehicle_contract_is_unsupported()
    {
        var v = Json(EntityAuthoringReader.VehicleFile); v["contract"] = "hd2runtime.vehicle.guarded_authoring.v2";
        Assert.Throws<UnsupportedSdkException>(() => Read(v, Json(EntityAuthoringReader.BackpackFile)));
    }
    [Fact] public async Task Export_includes_entity_calls_and_no_native_identifiers()
    {
        using var e = new TestEnvironment(); var w = await Workspace(e);
        var gun = Entity(w, Gunner, "mount.weapon", mount: "slot_0"); await w.SetEntityAsync(gun.InstanceKey, gun.AllowedValues!.First(x => x != gun.CurrentDefault.GetString()));
        await w.SetEntityReferenceAcknowledgedAsync(gun.InstanceKey, true); await w.SetEntityAsync(Entity(w, "SH-32 Shield Generator Pack", "shield.radius").InstanceKey, "3");
        await w.ExportAsync(); var first = File.ReadAllBytes(w.LastExport!); await w.OpenAsync(w.Project!.Id); await w.ExportAsync();
        Assert.Equal(first, File.ReadAllBytes(w.LastExport!));
        foreach (var native in new[] { "0x", "backing:", "operation:", "package:", "offset" }) Assert.DoesNotContain(native, w.LuaPreview);
        Assert.Contains("hd2.backpack('SH-32 Shield Generator Pack')", w.LuaPreview); Assert.Contains("allow_unverified_reference=true", w.LuaPreview);
    }
}
public sealed class Runtime023ArchiveTests
{
    [Fact] public async Task Published_023_archive_installs_and_reloads_from_cache()
    {
        using var e = new TestEnvironment(); var archive = SdkFixtures.Archive("0.23.0");
        Assert.Equal("96e8eac4a711682ba58a5fd2fd29b7a280d813eb36401fa1999c6486e34e6129", Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(archive)).ToLowerInvariant());
        var sdk = await SdkFixtures.Install(e, "0.23.0");
        Assert.Equal(11, sdk.Entities!.Vehicles.Vehicles.Length); Assert.Equal(13, sdk.Entities.Backpacks.Backpacks.Length);
        var cached = await e.Cache.GetVersionAsync("0.23.0"); Assert.Equal(718, cached.Entities!.Vehicles.FieldInstances.Length);
    }
}
public sealed class Runtime0231CompatibilityTests
{
    // 0.23.1 republishes the 0.23.0 capability files unchanged (version line only) and adds MagazineAttachmentCapabilities.json,
    // which this GUI does not consume yet. The release must be recognised as compatible rather than breaking the update check.
    [Fact] public async Task Published_0231_archive_is_inspectable_and_installs()
    {
        using var e = new TestEnvironment(); var archive = SdkFixtures.Archive("0.23.1");
        Assert.Equal("2c7984a625b8cfca81d4f639d85fd5dae8ab9328780572bda022fff3d4e7774f", Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(archive)).ToLowerInvariant());
        e.GitHub.Archive = archive; e.GitHub.Release = FakeGitHub.MakeRelease("0.23.1", archive);
        var status = await e.Updates.CheckAsync(); Assert.True(status.UpdateAvailable); Assert.Equal("0.23.1", status.Latest!.Version);
        var sdk = await e.Cache.InstallAsync(e.GitHub.Release); Assert.Equal(718, sdk.Entities!.Vehicles.FieldInstances.Length); Assert.Equal(1468, sdk.Stratagems!.FieldInstances.Length);
    }
}
