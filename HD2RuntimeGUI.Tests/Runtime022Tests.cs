using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using HD2RuntimeGUI.Core.Generation;
using HD2RuntimeGUI.Core.Metadata;
using HD2RuntimeGUI.Core.Models;
using HD2RuntimeGUI.Core.Services;
using Xunit;

namespace HD2RuntimeGUI.Tests;

// Runtime 0.22 defensive stratagem authoring against the published, bundled 0.22.0 SDK.
public sealed class Runtime022Tests
{
    private const string AntiTank = "E/AT-12 Anti-Tank Emplacement";
    private static async Task<BuilderWorkspace> Workspace(TestEnvironment e, string resource = "mods/tests/defensive")
    {
        var sdk = await SdkFixtures.Install(e, "0.22.1");
        var w = e.Workspace(); await w.CreateAsync(new("Defensive", "Tests", resource, "0.1.0"), sdk); return w;
    }
    private static StratagemCatalog Catalog() => new StratagemCatalogReader().Read(SdkFixtures.Entry("0.22.1", StratagemCatalogReader.FileName));
    private static StratagemField Field(StratagemCatalog c, string name, string id, string? attack = null) => c.FieldInstances.Single(f =>
        f.Target.Stratagem == name && f.SemanticFieldId == id && (attack == null || f.Target.Attack == attack));
    private static StratagemField Field(BuilderWorkspace w, string name, string id, string? attack = null) => Field(w.Metadata!.Stratagems!, name, id, attack);
    private static async Task Edit(BuilderWorkspace w, StratagemField f, string value, bool approve = true)
    { await w.SetStratagemAsync(f.InstanceKey, value); if (approve && f.Shared) await w.SetStratagemApprovalAsync(f.InstanceKey, true); }
    private static bool Defensive(StratagemCatalog c, StratagemField f) => StratagemCatalog.IsDefensive(c.Root(f.Target.Stratagem)!.Family);

    [Fact] public async Task Published_022_catalog_loads_with_audited_counts()
    {
        using var e = new TestEnvironment(); var w = await Workspace(e); var c = w.Metadata!.Stratagems!;
        Assert.Equal("0.22.1", w.Metadata.Version); Assert.Equal(2, c.SchemaVersion); Assert.Equal("hd2runtime.stratagem.guarded_authoring.v2", c.Contract);
        Assert.Equal(1382, c.FieldInstances.Length); Assert.Equal(1382, c.FieldInstances.Select(f => f.InstanceKey).Distinct().Count());
        Assert.Equal(1311, c.FieldInstances.Count(f => f.Editable));
        var defensive = c.FieldInstances.Where(f => Defensive(c, f)).ToArray();
        Assert.Equal(393, defensive.Length); Assert.Equal(375, defensive.Count(f => f.Editable));
        Assert.Equal(226, c.BackingObjects!.Length); Assert.Equal(327, c.OperationGroups!.Length);
        Assert.Equal(115, c.FieldInstances.Where(f => f.Shared).Select(f => f.ScopeKey).Distinct().Count());
        Assert.Equal(97, defensive.Select(f => f.BackingObjectId).Distinct().Count());
        Assert.Equal(110, defensive.Select(f => f.OperationGroup).Distinct().Count());
        Assert.Equal(40, defensive.Where(f => f.Shared).Select(f => f.ScopeKey).Distinct().Count());
        Assert.Equal(71, c.FieldInstances.Count(f => f.SemanticFieldId == "stratagem.cooldown" && f.Editable));
        Assert.Equal(18, defensive.Count(f => f.SemanticFieldId == "stratagem.cooldown" && f.Editable));
        Assert.Equal(10, c.Stratagems.Count(s => s.Family == "sentry" && s.RootResolution == "UNIQUE"));
        Assert.Equal(4, c.Stratagems.Count(s => s.Family == "emplacement" && s.RootResolution == "UNIQUE"));
        Assert.Equal(4, c.Stratagems.Count(s => s.Family == "mine" && s.RootResolution == "UNIQUE"));
        Assert.Equal(18, c.DeployedEntities!.Length); Assert.Equal(18, c.FieldInstances.Count(f => f.SemanticFieldId == "entity.health" && f.Editable));
        Assert.Equal(18, c.FieldInstances.Count(f => f.SemanticFieldId == "entity.armor" && f.Editable));
        Assert.Equal(12, c.Summary.MountedWeaponsResolved); Assert.Equal(44, c.Summary.AmmoWritable); Assert.Equal(11, c.Summary.MountedWeaponsWithAmmo);
        Assert.Equal(9, c.FieldInstances.Count(f => f.SemanticFieldId == "weapon.fire_rate" && f.Editable)); Assert.Equal(4, c.Summary.HeatWritable);
        Assert.Equal(new Dictionary<string, int> { ["ArcSettings"] = 1, ["BeamSettings"] = 1, ["DamageInfo"] = 19, ["ExplosionSettings"] = 7, ["ProjectileSettings"] = 9, ["StatusEffectSettings"] = 8 },
            c.Attacks.Where(a => StratagemCatalog.IsDefensive(a.Family)).GroupBy(a => a.Kind).OrderBy(g => g.Key).ToDictionary(g => g.Key, g => g.Count()));
        Assert.DoesNotContain(c.Attacks, a => a.Kind.Contains("Spray"));
        Assert.Equal(0, c.Summary.MineInstancesResolved); Assert.Equal(0, c.Summary.MineAttackBranchesWritable); Assert.Equal(0, c.Summary.MultiWeaponEntities);
    }
    [Fact] public void Backing_objects_and_operation_groups_are_distinct_concepts()
    {
        var c = Catalog();
        Assert.NotEqual(c.BackingObjects!.Length, c.OperationGroups!.Length);
        Assert.Equal(42, c.OperationGroups.GroupBy(g => g.BackingObjectId).Count(g => g.Count() > 1));
        Assert.DoesNotContain(c.FieldInstances, f => f.BackingObjectId == f.OperationGroup);
        // Each operation group has one exact target; one backing object can own several target-specific groups.
        Assert.All(c.OperationGroups, g => Assert.All(g.FieldInstances, k => Assert.Equal(g.Target, c.Field(k).Target)));
        var flame = c.FieldInstances.Where(f => f.Target.Stratagem == "A/FLAM-40 Flame Sentry" && f.SemanticFieldId == "status.strength").ToArray();
        Assert.Equal(3, flame.Length); Assert.Single(flame.Select(f => f.BackingObjectId).Distinct()); Assert.Equal(3, flame.Select(f => f.OperationGroup).Distinct().Count());
        // status.strength is presented as status data while its published backing object is the parent DamageInfo.
        Assert.All(flame, f => { Assert.Equal("status", f.Domain); Assert.Equal("DamageInfo", f.BackingObjectKind); });
    }
    [Fact] public async Task Published_022_archive_installs_and_reloads_from_cache()
    {
        using var e = new TestEnvironment(); e.GitHub.Archive = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "sdk-0.22.0.zip"));
        Assert.Equal("8f2aec687f7b04788fd9555a0d6dbfce86da57da3ad6dea1f00c76b4218336d3", Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(e.GitHub.Archive)).ToLowerInvariant());
        var sdk = await e.Cache.InstallAsync(FakeGitHub.MakeRelease("0.22.0", e.GitHub.Archive));
        Assert.Equal(1382, sdk.Stratagems!.FieldInstances.Length);
        Assert.True(new FileInfo(e.Paths.CachePath("0.22.0", StratagemCatalogReader.FileName)).Length > PlayerWeaponCompositionReader.MaxBytes);
        var cached = await e.Cache.GetVersionAsync("0.22.0"); Assert.Equal(1311, cached.Stratagems!.FieldInstances.Count(f => f.Editable));
        Assert.Null(sdk.SupportLinks); Assert.Null(cached.SupportLinks); // 0.22.0 publishes no call-in linkage, so nothing is merged.
    }
    [Theory] [InlineData("native")] [InlineData("hex")] [InlineData("group-target")] [InlineData("scope")] [InlineData("backing-kind")] [InlineData("mine-attack")]
    [InlineData("fire-rate")] [InlineData("audit")] [InlineData("entity-path")] [InlineData("missing-scope")] [InlineData("contract")]
    public void Malformed_022_catalog_fails_closed(string fault)
    {
        var json = JsonNode.Parse(SdkFixtures.Entry("0.22.1", StratagemCatalogReader.FileName))!;
        var fields = json["fieldInstances"]!.AsArray(); var at = fields.First(f => (string?)f!["target"]!["stratagem"] == AntiTank && (string?)f!["semanticFieldId"] == "projectile.mass")!;
        switch (fault)
        {
            case "native": at["offset"] = 12; break;
            case "hex": at["provenance"] = "record 0x1234"; break;
            case "group-target": json["operationGroups"]!.AsArray().First(g => (string?)g!["operationGroup"] == (string?)at["operationGroup"])!["target"]!["attack"] = "primary_damage"; break;
            case "scope": at["sharedScopeKey"] = "shared-scope:0000000000000000"; break;
            case "backing-kind": at["backingObjectKind"] = "DamageInfo"; break;
            case "mine-attack": json["attacks"]!.AsArray().Add(new JsonObject { ["stratagem"] = "MD-6 Anti-Personnel Minefield", ["family"] = "mine", ["role"] = "mine", ["path"] = "mine/explosion", ["kind"] = "ExplosionSettings", ["parentRole"] = null }); break;
            case "fire-rate": json["summary"]!["fireRateWritable"] = 10; break;
            case "audit": json["instanceAudit"]!["exactMatch"] = false; break;
            case "entity-path": at["target"]!["weapon"] = "secondary"; break;
            case "missing-scope": ((JsonObject)at).Remove("sharedScopeKey"); break;
            case "contract": json["contract"] = "hd2runtime.stratagem.guarded_authoring.v1"; break;
        }
        var bytes = Encoding.UTF8.GetBytes(json.ToJsonString());
        Assert.ThrowsAny<Exception>(() => new StratagemCatalogReader().Read(bytes));
        Assert.True(Record.Exception(() => new StratagemCatalogReader().Read(bytes)) is InvalidDataException or UnsupportedSdkException);
    }

    [Theory] [InlineData("", 73)] [InlineData("orbital", 12)] [InlineData("eagle", 8)] [InlineData("support", 35)] [InlineData("sentry", 10)] [InlineData("emplacement", 4)] [InlineData("mine", 4)]
    public void Family_filters_match_published_roots(string family, int count)
    {
        var c = Catalog(); Assert.Equal(count, c.Stratagems.Count(s => StratagemBrowser.Matches(c, s, family)));
        Assert.Contains(StratagemBrowser.Tabs(c), t => t.Family == family && t.Count == count);
    }
    [Fact] public void Tabs_follow_editor_order_and_labels()
    {
        Assert.Equal(["All", "Orbital", "Eagle", "Support Weapons", "Sentries", "Emplacements", "Mines / Deployables"], StratagemBrowser.Tabs(Catalog()).Select(t => t.Label));
        var c = Catalog(); Assert.Contains(c.Stratagems, s => s.Name == AntiTank && StratagemBrowser.Matches(c, s, "emplacement", "anti-tank", "entity", "writable"));
        Assert.Contains(c.Stratagems, s => s.Name == "A/LAS-98 Laser Sentry" && StratagemBrowser.Matches(c, s, "sentry", system: "beam"));
    }

    [Fact] public void Anti_tank_emplacement_graph_is_stratagem_entity_weapon_attacks()
    {
        var c = Catalog(); var g = StratagemGraph.Build(c, AntiTank);
        Assert.Equal(["stratagem.cooldown", "stratagem.max_uses"], g.Definition.Select(f => f.SemanticFieldId));
        var entity = Assert.Single(g.Entities); Assert.Equal("main", entity.Entity); Assert.Equal("Emplacement", entity.Identity.Kind);
        Assert.Equal(["Health", "Armor"], entity.Stats.Select(f => EntityStats.Label(f.SemanticFieldId)));
        Assert.Equal([300, 2], entity.Stats.Select(f => f.CurrentDefault.GetInt32()));
        var weapon = Assert.Single(entity.Weapons); Assert.Equal("primary", weapon.Weapon);
        Assert.Equal(["Ammo", "Weapon"], weapon.Groups.Select(x => x.Title));
        Assert.Equal(30, weapon.Groups[0].Fields.Single(f => f.SemanticFieldId == "weapon.capacity").CurrentDefault.GetInt32());
        Assert.Equal(["Projectile", "Damage", "Explosion (impact)", "Explosion Damage"], weapon.Attacks.Select(a => a.Title));
        var projectile = weapon.Attacks[0].Fields.ToDictionary(f => f.SemanticFieldId, f => f.CurrentDefault.GetDouble());
        Assert.Equal(6500, projectile["projectile.mass"]); Assert.Equal(625, projectile["projectile.velocity"]); Assert.Equal(0.75, projectile["projectile.drag"], 5); Assert.Equal(1, projectile["projectile.gravity"]);
        Assert.Equal([3, 6, 7], weapon.Attacks[2].Fields.Select(f => f.CurrentDefault.GetDouble()));
        Assert.Empty(g.Attacks); Assert.Equal(g.AllFields.Count(), c.FieldInstances.Count(f => f.Target.Stratagem == AntiTank));
        Assert.Contains(g.Blocked, b => b.Field == "projectile.lifetime"); Assert.Contains(g.Blocked, b => b.Field == "deployment lifetime");
    }
    [Theory]
    [InlineData("A/LAS-98 Laser Sentry", new[] { "Heat", "Heatsinks" }, new[] { "Beam", "Damage", "Status · slot 1" })]
    [InlineData("A/ARC-3 Tesla Tower", new[] { "Ammo" }, new[] { "Arc", "Damage", "Status · slot 1" })]
    [InlineData("A/FLAM-40 Flame Sentry", new[] { "Ammo" }, new[] { "Damage", "Status · slot 1", "Status · slot 2", "Status · slot 3" })]
    [InlineData("A/MG-43 Machine Gun Sentry", new[] { "Ammo", "Weapon" }, new[] { "Projectile", "Damage" })]
    public void Sentry_graph_renders_only_published_sections(string name, string[] groups, string[] attacks)
    {
        var weapon = StratagemGraph.Build(Catalog(), name).Entities.Single().Weapons.Single();
        Assert.Equal(groups, weapon.Groups.Select(g => g.Title)); Assert.Equal(attacks, weapon.Attacks.Select(a => a.Title));
    }
    [Fact] public void Every_stratagem_graph_presents_every_instance_exactly_once()
    {
        var c = Catalog();
        foreach (var root in c.Stratagems)
        {
            var g = StratagemGraph.Build(c, root.Name); var shown = g.AllFields.Select(f => f.InstanceKey).ToArray();
            Assert.Equal(shown.Length, shown.Distinct().Count());
            Assert.Equal(c.FieldInstances.Where(f => f.Target.Stratagem == root.Name).Select(f => f.InstanceKey).Order(), shown.Order());
            Assert.All(g.Entities.SelectMany(e => e.Weapons).SelectMany(w => w.Attacks).Concat(g.Attacks), a => Assert.False(string.IsNullOrWhiteSpace(a.Title)));
        }
    }
    [Fact] public void Descriptive_branches_load_both_published_shapes()
    {
        var c = Catalog();
        Assert.Equal(55, c.SemanticBranches.Count(b => b.WikiKind != null && b.SemanticRoles != null));
        Assert.Equal(88, c.SemanticBranches.Count(b => b.Kind != null && b.SourcePath != null && b.RelationshipEvidence != null));
        Assert.All(c.SemanticBranches, b => Assert.False(string.IsNullOrWhiteSpace(b.KindLabel)));
    }
    [Fact] public void Fire_rate_is_only_the_nine_published_weapon_fields()
    {
        var c = Catalog(); var fire = c.FieldInstances.Where(f => f.SemanticFieldId == "weapon.fire_rate").ToArray();
        Assert.Equal(9, fire.Length); Assert.All(fire, f => { Assert.Equal("weapon", f.Target.Path); Assert.Equal("ProjectileWeaponComponentData", f.BackingObjectKind); });
        foreach (var name in new[] { "A/ARC-3 Tesla Tower", "A/LAS-98 Laser Sentry", "A/FLAM-40 Flame Sentry" })
            Assert.DoesNotContain(StratagemGraph.Build(c, name).AllFields, f => f.SemanticFieldId == "weapon.fire_rate" || f.DisplayName.Contains("fire rate", StringComparison.OrdinalIgnoreCase));
    }
    [Fact] public void Mines_expose_roots_and_deployment_entity_only()
    {
        var c = Catalog(); var mines = c.Stratagems.Where(s => s.Family == "mine").ToArray(); Assert.Equal(4, mines.Length);
        foreach (var mine in mines)
        {
            Assert.Equal("DeployableSystem", mine.DeployedEntity!.Kind); Assert.False(mine.MineInstanceResolved); Assert.True(mine.MineScopeDeferred);
            var g = StratagemGraph.Build(c, mine.Name); var entity = Assert.Single(g.Entities);
            Assert.Equal(["entity.health", "entity.armor"], entity.Stats.Select(f => f.SemanticFieldId)); Assert.All(entity.Stats, f => Assert.True(f.Editable));
            Assert.Empty(entity.Weapons); Assert.Empty(g.Attacks); Assert.Empty(mine.AttackRoles);
            Assert.DoesNotContain(g.AllFields, f => f.Domain is "explosion" or "damage" or "status" or "projectile");
            foreach (var blocked in new[] { "mine entity", "mine trigger", "mine distribution", "mine explosion/status" })
                Assert.False(string.IsNullOrWhiteSpace(g.Blocked.Single(b => b.Field == blocked).Reason));
            Assert.True(g.Definition.Single(f => f.SemanticFieldId == "stratagem.cooldown").Editable);
        }
        var grenadier = c.Root("E/GL-21 Grenadier Battlement")!; Assert.Empty(StratagemGraph.Build(c, grenadier.Name).Entities.Single().Weapons);
        Assert.Contains(grenadier.DeployedEntity!.BlockedFields, b => b.Field == "mounted weapon");
    }

    [Fact] public async Task Health_and_armor_edit_persist_reset_and_generate_one_transaction()
    {
        using var e = new TestEnvironment(); var w = await Workspace(e); var c = w.Metadata!.Stratagems!;
        var health = Field(w, AntiTank, "entity.health"); var armor = Field(w, AntiTank, "entity.armor");
        Assert.False(health.Shared); Assert.Equal(health.OperationGroup, armor.OperationGroup);
        await Edit(w, health, "600"); await Edit(w, armor, "3");
        var saved = StratagemChangeService.Saved(w.Project, c, health)!;
        Assert.Equal("deployed_entity", saved.TargetKind); Assert.Equal("main", saved.Entity); Assert.Null(saved.Weapon); Assert.Equal(300, saved.ExpectedValue.GetInt32()); Assert.Equal(600, saved.DesiredValue.GetInt32());
        Assert.Null(w.BuildError); Assert.Contains("transaction={", w.LuaPreview); Assert.DoesNotContain("plan={", w.LuaPreview); Assert.DoesNotContain("allow_shared", w.LuaPreview);
        Assert.Contains("target=hd2.stratagem('E/AT-12 Anti-Tank Emplacement'):deployed_entity()", w.LuaPreview);
        Assert.Contains("{field=hd2.fields.entity.health,expect=300,value=600}", w.LuaPreview); Assert.Contains("{field=hd2.fields.entity.armor,expect=2,value=3}", w.LuaPreview);
        await w.OpenAsync(w.Project!.Id); Assert.Equal(5, w.Project!.FormatVersion); Assert.Equal(2, w.Project.StratagemChanges.Count);
        var json = File.ReadAllText(e.Paths.ProjectFile(w.Project.Id));
        Assert.Contains("\"targetKind\": \"deployed_entity\"", json); Assert.Contains("\"entity\": \"main\"", json);
        Assert.DoesNotContain("backing:", json); Assert.DoesNotContain("operation:", json); Assert.DoesNotContain("offset", json);
        await w.SetStratagemAsync(armor.InstanceKey, "2"); Assert.Single(w.Project.StratagemChanges); Assert.Null(StratagemChangeService.Saved(w.Project, c, armor));
        await w.ResetStratagemAsync(AntiTank); Assert.Empty(w.Project.StratagemChanges);
    }
    public static TheoryData<string, string, string?, string> MountedBranches => new()
    {
        { "A/MG-43 Machine Gun Sentry", "weapon.capacity", null, ":deployed_entity():weapon('primary')," },
        { "A/MG-43 Machine Gun Sentry", "weapon.fire_rate", null, ":deployed_entity():weapon('primary')," },
        { AntiTank, "projectile.mass", "primary", ":weapon('primary'):attack('primary')" },
        { "A/MG-43 Machine Gun Sentry", "damage.standard_damage", "primary_damage", ":attack('primary_damage')" },
        { "A/MLS-4X Rocket Sentry", "explosion.outer_radius", "primary_impact", ":attack('primary_impact')" },
        { "A/LAS-98 Laser Sentry", "beam.length", "primary", ":attack('primary')" },
        { "A/ARC-3 Tesla Tower", "arc.range", "primary", ":attack('primary')" },
        { "A/GM-17 Gas Mortar Sentry", "status.duration", "primary_impact_damage_status_2", ":attack('primary_impact_damage_status_2')" },
        { "A/LAS-98 Laser Sentry", "heat.capacity", null, ":deployed_entity():weapon('primary')," },
    };
    [Theory] [MemberData(nameof(MountedBranches))]
    public async Task Mounted_weapon_branch_edits_generate_graph_targets(string name, string field, string? attack, string target)
    {
        using var e = new TestEnvironment(); var w = await Workspace(e); var f = Field(w, name, field, attack);
        var value = f.Type == "integer" ? (f.CurrentDefault.GetInt64() + 1).ToString() : (f.CurrentDefault.GetDouble() + 0.5).ToString(System.Globalization.CultureInfo.InvariantCulture);
        await Edit(w, f, value, false); var saved = w.Project!.StratagemChanges.Single();
        Assert.Equal("mounted_weapon", saved.TargetKind); Assert.Equal("main", saved.Entity); Assert.Equal("primary", saved.Weapon); Assert.Equal(attack, saved.Attack);
        // Shared approval is implicit: a shared branch builds without approval and carries allow_shared.
        Assert.Null(w.BuildError); Assert.Contains(target, w.LuaPreview); Assert.Contains("field=" + f.ApiFieldConstant, w.LuaPreview);
        Assert.Equal(f.Shared, w.LuaPreview.Contains("allow_shared=true"));
    }
    [Fact] public async Task Every_writable_022_instance_generates_a_semantic_patch_without_native_identity()
    {
        using var e = new TestEnvironment(); var w = await Workspace(e); var svc = new StratagemChangeService();
        foreach (var f in w.Metadata!.Stratagems!.FieldInstances)
        {
            if (!f.Editable) { Assert.Throws<InvalidDataException>(() => svc.Create(w.Metadata, f.InstanceKey, "1")); continue; }
            var c = svc.Create(w.Metadata, f.InstanceKey, JsonSerializer.Serialize(f.CurrentDefault.GetDouble() + 1));
            w.Project!.StratagemChanges = [c]; w.Project.StratagemApprovals.Clear();
            // No approval is recorded: shared writes validate anyway and carry allow_shared.
            svc.Validate(w.Project, w.Metadata, c); var lua = e.Generator.Generate(w.Project, w.Metadata);
            Assert.Equal(f.AllowSharedRequired, lua.Contains("allow_shared=true"));
            Assert.Contains(f.ApiFieldConstant, lua); Assert.Contains(StratagemLua.Target(f.Target), lua);
            Assert.DoesNotContain("backing:", lua); Assert.DoesNotContain("operation:", lua); Assert.DoesNotContain("shared-scope:", lua); Assert.DoesNotContain("0x", lua);
        }
    }

    [Fact] public void Multi_weapon_branches_and_repeated_fields_remain_distinct()
    {
        // Runtime 0.22 publishes no multi-weapon entity; synthesize a second mounted weapon from published E/AT-12 instances.
        var c = Catalog(); var source = c.FieldInstances.Where(f => f.Target.Stratagem == AntiTank && f.Target.Path == "weapon").ToArray();
        var secondary = source.Select(f => f with { InstanceKey = f.InstanceKey + ":secondary", Target = f.Target with { Weapon = "secondary" },
            OperationGroup = f.OperationGroup + ":secondary", BackingObjectId = f.BackingObjectId + ":secondary" }).ToArray();
        var synthetic = c with { FieldInstances = [..c.FieldInstances, ..secondary] };
        var weapons = StratagemGraph.Build(synthetic, AntiTank).Entities.Single().Weapons;
        Assert.Equal(["primary", "secondary"], weapons.Select(x => x.Weapon));
        var capacity = weapons.Select(x => x.Groups.SelectMany(g => g.Fields).Single(f => f.SemanticFieldId == "weapon.capacity")).ToArray();
        Assert.NotEqual(capacity[0].InstanceKey, capacity[1].InstanceKey);
        Assert.Contains(":weapon('secondary')", StratagemLua.Target(capacity[1].Target));
        Assert.Throws<InvalidDataException>(() => StratagemLua.Target(new("stratagem", AntiTank, "attack", "primary", "main", "secondary")));
        Assert.Throws<InvalidDataException>(() => StratagemLua.Target(new("stratagem", AntiTank, "deployed_entity", null, "turret")));
        var p = new ModProject { StratagemChanges = [Change(capacity[0], 60), Change(capacity[1], 90)] };
        Assert.Same(capacity[0], synthetic.Resolve(p.StratagemChanges[0])); Assert.Same(capacity[1], synthetic.Resolve(p.StratagemChanges[1]));
        Assert.Equal("primary", StratagemChangeService.Saved(p, synthetic, capacity[0])!.Weapon);
        Assert.Equal("secondary", StratagemChangeService.Saved(p, synthetic, capacity[1])!.Weapon);
        static StratagemChange Change(StratagemField f, int value) => new() { InstanceKey = f.InstanceKey, TargetKind = StratagemChangeService.TargetKind(f), Stratagem = f.Target.Stratagem,
            Path = f.Target.Path, Entity = f.Target.Entity, Weapon = f.Target.Weapon, SemanticFieldId = f.SemanticFieldId, FieldType = f.Type,
            ExpectedValue = f.CurrentDefault, DesiredValue = JsonSerializer.SerializeToElement(value), BaselineSdkVersion = "0.22.0", CapabilityEvidence = StratagemChangeService.Evidence(f) };
    }
    [Fact] public async Task Status_slots_on_one_damage_object_use_separate_operations_in_one_plan()
    {
        using var e = new TestEnvironment(); var w = await Workspace(e);
        var slots = w.Metadata!.Stratagems!.FieldInstances.Where(f => f.Target.Stratagem == "A/FLAM-40 Flame Sentry" && f.SemanticFieldId == "status.strength").ToArray();
        await Edit(w, slots[0], "5"); await Edit(w, slots[1], "3"); Assert.Equal(2, w.Project!.StratagemChanges.Count);
        Assert.Null(w.BuildError); Assert.Contains("plan={", w.LuaPreview);
        Assert.Contains(":attack('primary_damage_status_1')", w.LuaPreview); Assert.Contains(":attack('primary_damage_status_2')", w.LuaPreview);
        Assert.Equal(1, w.LuaPreview.Split("hd2.ensure(").Length - 1);
        Assert.Single(StratagemLua.SharedOverlaps(w.Project, w.Metadata)); // advisory only; distinct slots apply independently
    }

    [Fact] public async Task Shared_acknowledgement_is_keyed_to_the_exact_scope_lists_consumers_and_never_blocks_the_build()
    {
        using var e = new TestEnvironment(); var w = await Workspace(e); var c = w.Metadata!.Stratagems!;
        var mass = Field(w, AntiTank, "projectile.mass"); var damage = Field(w, AntiTank, "damage.standard_damage", "primary_damage");
        await Edit(w, mass, "7000", false); await Edit(w, damage, "900", false);
        Assert.Null(w.BuildError); Assert.Contains("allow_shared=true", w.LuaPreview); // approval is implicit
        await w.SetStratagemApprovalAsync(mass.InstanceKey, true);
        Assert.Equal([mass.SharedScopeKey!], w.Project!.StratagemApprovals.Keys); Assert.StartsWith("shared-scope:", mass.ScopeKey);
        Assert.False(StratagemChangeService.Approved(w.Project, damage)); Assert.Null(w.BuildError);
        await w.SetStratagemApprovalAsync(damage.InstanceKey, true); Assert.Null(w.BuildError);
        // One scope shared across Laser Sentry, Flame Sentry and three offensive stratagems uses one acknowledgement.
        var laser = Field(w, "A/LAS-98 Laser Sentry", "status.duration"); var flame = Field(w, "A/FLAM-40 Flame Sentry", "status.duration", "primary_damage_status_1");
        Assert.Equal(laser.ScopeKey, flame.ScopeKey); Assert.NotEqual(laser.OperationGroup, flame.OperationGroup);
        var consumers = c.Backing(laser)!.SharedConsumers.Select(x => x.Stratagem).Distinct().ToArray();
        Assert.Equal(["Eagle Napalm Airstrike", "Orbital Napalm Barrage", "A/FLAM-40 Flame Sentry", "A/LAS-98 Laser Sentry"], consumers);
        await w.SetStratagemApprovalAsync(laser.InstanceKey, true); Assert.True(StratagemChangeService.Approved(w.Project, flame));
        await w.SetStratagemApprovalAsync(flame.InstanceKey, false); Assert.False(StratagemChangeService.Approved(w.Project, laser));
    }
    [Fact] public async Task Scope_change_invalidates_acknowledgement()
    {
        using var e = new TestEnvironment(); var w = await Workspace(e); var f = Field(w, AntiTank, "explosion.outer_radius"); await Edit(w, f, "8");
        var change = w.Project!.StratagemChanges.Single(); var svc = new StratagemChangeService(); svc.Validate(w.Project, w.Metadata!, change);
        SdkMetadata Replace(StratagemField next) => w.Metadata! with { Stratagems = w.Metadata!.Stratagems! with { FieldInstances = w.Metadata.Stratagems.FieldInstances.Select(x => x.InstanceKey == f.InstanceKey ? next : x).ToArray() } };
        var wider = f with { SharedConsumers = [..f.SharedConsumers, new("A/MLS-4X Rocket Sentry", "weapon:primary/attack:primary/projectile/impact")] };
        Assert.False(StratagemChangeService.Approved(w.Project, wider)); Assert.Throws<InvalidDataException>(() => svc.Validate(w.Project, Replace(wider), change));
        Assert.False(StratagemChangeService.Approved(w.Project, f with { SharedScopeKey = "shared-scope:ffffffffffffffff" }));
        Assert.False(StratagemChangeService.Approved(w.Project, f with { DynamicConsumersPossible = !f.DynamicConsumersPossible }));
    }

    [Fact] public async Task Scalar_transaction_and_multi_object_plan_match_the_published_proof()
    {
        using var e = new TestEnvironment(); var w = await Workspace(e);
        await Edit(w, Field(w, AntiTank, "stratagem.cooldown"), "360"); Assert.Contains("patch={", w.LuaPreview); Assert.DoesNotContain("changes={", w.LuaPreview);
        await Edit(w, Field(w, AntiTank, "entity.health"), "600");
        await Edit(w, Field(w, AntiTank, "projectile.mass"), "7000");
        await Edit(w, Field(w, AntiTank, "explosion.outer_radius"), "8");
        Assert.Null(w.BuildError); var lua = w.LuaPreview;
        Assert.Equal(1, lua.Split("hd2.ensure(").Length - 1); Assert.Contains("plan={", lua); Assert.Equal(4, lua.Split("target=").Length - 1);
        Assert.Contains("target=hd2.stratagem('E/AT-12 Anti-Tank Emplacement'),", lua);
        Assert.Contains("target=hd2.stratagem('E/AT-12 Anti-Tank Emplacement'):deployed_entity(),", lua);
        Assert.Contains("target=hd2.stratagem('E/AT-12 Anti-Tank Emplacement'):deployed_entity():weapon('primary'):attack('primary'),", lua);
        Assert.Contains("target=hd2.stratagem('E/AT-12 Anti-Tank Emplacement'):deployed_entity():weapon('primary'):attack('primary_impact'),", lua);
        Assert.Equal(2, lua.Split("allow_shared=true").Length - 1);
        foreach (var expected in new[] { "field=hd2.fields.stratagem.definition_cooldown,\n", "expect=180,", "value=360,", "field=hd2.fields.entity.health,", "expect=300,", "value=600,",
            "field=hd2.fields.projectile.mass,", "expect=6500,", "value=7000,", "field=hd2.fields.explosion.outer_radius,", "expect=6,", "value=8," })
            Assert.Contains(expected, lua);
        w.Project!.StratagemChanges.Reverse(); Assert.Equal(lua, e.Generator.Generate(w.Project, w.Metadata!));
    }
    [Fact] public async Task Export_is_deterministic_and_contains_no_native_identifiers()
    {
        using var e = new TestEnvironment(); var w = await Workspace(e);
        await Edit(w, Field(w, "A/MLS-4X Rocket Sentry", "explosion.outer_radius"), "8");
        await Edit(w, Field(w, "A/MLS-4X Rocket Sentry", "explosion.damage.standard_damage", "primary_impact_damage"), "220");
        var lua = w.LuaPreview; await w.ExportAsync(); var bytes = File.ReadAllBytes(w.LastExport!);
        await w.OpenAsync(w.Project!.Id); await w.ExportAsync(); Assert.Equal(bytes, File.ReadAllBytes(w.LastExport!)); Assert.Equal(lua, w.LuaPreview);
        using var zip = ZipFile.OpenRead(w.LastExport!); Assert.Equal(8, zip.Entries.Count);
        foreach (var entry in zip.Entries)
        {
            using var reader = new StreamReader(entry.Open()); var text = reader.ReadToEnd();
            foreach (var native in new[] { "backing:", "operation:", "shared-scope:", "0x", "offset", "recordIndex" }) Assert.DoesNotContain(native, text);
        }
        using var dependency = new StreamReader(zip.GetEntry("hd2runtime.json")!.Open()); Assert.Contains("0.22.1", dependency.ReadToEnd());
    }

    [Fact] public async Task Runtime021_project_rebinds_by_semantic_identity_and_reviews_changed_scopes()
    {
        using var e = new TestEnvironment(); var old = await Runtime021Tests.Install021(e); var w = e.Workspace();
        await w.CreateAsync(new("Pinned", "Tests", "mods/tests/pinned_021", "0.1.0"), old); var c21 = old.Stratagems!;
        var cooldown = Field(c21, "Orbital Laser", "stratagem.cooldown"); var laser = Field(c21, "Orbital Laser", "damage.standard_damage");
        var napalm = Field(c21, "Orbital Napalm Barrage", "status.duration", "delivery_1_projectile_impact_damage_status_1");
        var rearm = c21.FieldInstances.First(f => f.SemanticFieldId == "eagle.rearm_time");
        foreach (var (f, v) in new[] { (cooldown, "180"), (laser, "400"), (napalm, "12"), (rearm, "90") }) await Edit(w, f, v);
        Assert.Null(w.BuildError); var saved = w.Project!.StratagemChanges.ToDictionary(x => x.SemanticFieldId);
        await SdkFixtures.Install(e, "0.22.1");
        await w.OpenAsync(w.Project.Id); Assert.Equal("0.21.0", w.Project!.SdkVersion); Assert.Null(w.BuildError); Assert.Equal(989, w.Metadata!.Stratagems!.FieldInstances.Length);
        await w.RebindToInstalledSdkAsync(); var c22 = w.Metadata!.Stratagems!;
        Assert.Equal("0.22.1", w.Project.SdkVersion); Assert.Equal(4, w.Project.StratagemChanges.Count);
        Assert.All(w.Project.StratagemChanges, x => { Assert.NotNull(c22.Find(x.InstanceKey)); Assert.Equal("0.21.0", x.BaselineSdkVersion); Assert.Equal(saved[x.SemanticFieldId].DesiredValue.GetRawText(), x.DesiredValue.GetRawText()); });
        Assert.Null(w.StratagemIssue(w.Project.StratagemChanges.Single(x => x.SemanticFieldId == "stratagem.cooldown")));
        Assert.Null(w.StratagemIssue(w.Project.StratagemChanges.Single(x => x.SemanticFieldId == "damage.standard_damage")));
        Assert.Null(w.StratagemIssue(w.Project.StratagemChanges.Single(x => x.SemanticFieldId == "eagle.rearm_time")));
        // The napalm status object is now also consumed by two sentries: its changed capability must be reviewed (the stale approval itself no longer blocks).
        var napalm22 = Field(c22, "Orbital Napalm Barrage", "status.duration", "delivery_1_projectile_impact_damage_status_1");
        Assert.Equal(6, napalm22.SharedConsumers.Length); Assert.False(StratagemChangeService.Approved(w.Project, napalm22));
        Assert.Contains("changed", w.StratagemIssue(w.Project.StratagemChanges.Single(x => x.SemanticFieldId == "status.duration")));
        Assert.All(w.Project.StratagemApprovals.Keys, k => Assert.StartsWith("shared-scope:", k));
        await w.SetStratagemAsync(napalm22.InstanceKey, "12", true);
        Assert.Null(w.StratagemIssue(w.Project.StratagemChanges.Single(x => x.SemanticFieldId == "status.duration")));
        Assert.Equal("0.22.1", w.Project.StratagemChanges.Single(x => x.SemanticFieldId == "status.duration").BaselineSdkVersion);
        Assert.False(StratagemChangeService.Approved(w.Project, napalm22)); Assert.Null(w.BuildError); Assert.Contains("allow_shared=true", w.LuaPreview);
        await w.OpenAsync(w.Project.Id); Assert.Null(w.BuildError); Assert.Contains(":attack('delivery_1_projectile_impact_damage_status_1')", w.LuaPreview);
    }
    [Fact] public async Task Stale_instance_key_without_rebind_requires_review_and_accept_repairs_it()
    {
        using var e = new TestEnvironment(); var w = await Workspace(e); var f = Field(w, "Orbital Precision Strike", "stratagem.cooldown");
        await Edit(w, f, "40"); var current = w.Project!.StratagemChanges.Single();
        w.Project.StratagemChanges = [current with { InstanceKey = "stratagem:orbital_precision_strike:stratagem:stratagem.cooldown" }];
        await e.Store.SaveAsync(w.Project); await w.OpenAsync(w.Project.Id);
        Assert.Contains("changed", w.StratagemIssue(w.Project!.StratagemChanges.Single())); Assert.NotNull(w.BuildError);
        Assert.NotNull(StratagemChangeService.Saved(w.Project, w.Metadata!.Stratagems!, f));
        await w.SetStratagemAsync(f.InstanceKey, "40", true); Assert.Equal(f.InstanceKey, w.Project.StratagemChanges.Single().InstanceKey); Assert.Null(w.BuildError);
    }
    [Fact] public async Task Format4_project_json_without_graph_identity_still_loads()
    {
        using var e = new TestEnvironment(); var w = await Workspace(e); await Edit(w, Field(w, "Orbital Laser", "stratagem.cooldown"), "180");
        var path = e.Paths.ProjectFile(w.Project!.Id); var node = JsonNode.Parse(File.ReadAllText(path))!.AsObject(); node["formatVersion"] = 4;
        foreach (var change in node["stratagemChanges"]!.AsArray()) { change!.AsObject().Remove("entity"); change.AsObject().Remove("weapon"); }
        File.WriteAllText(path, node.ToJsonString()); await w.OpenAsync(w.Project.Id);
        Assert.Equal(4, w.Project!.FormatVersion); Assert.Null(w.BuildError); Assert.Contains("value=180", w.LuaPreview);
    }
    [Theory] [InlineData("deployed_entity", "attack")] [InlineData("stratagem", "weapon")] [InlineData("mounted_weapon", "deployed_entity")]
    public async Task Inconsistent_saved_target_kind_is_rejected(string kind, string path)
    {
        using var e = new TestEnvironment(); var w = await Workspace(e); await Edit(w, Field(w, AntiTank, "entity.health"), "600");
        var bad = w.Project!.StratagemChanges.Single() with { TargetKind = kind, Path = path };
        w.Project.StratagemChanges = [bad]; await Assert.ThrowsAsync<InvalidDataException>(() => e.Store.SaveAsync(w.Project));
    }
}
