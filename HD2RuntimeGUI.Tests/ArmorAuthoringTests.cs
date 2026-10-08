using System.Text.Json.Nodes;
using HD2RuntimeGUI.Core.Audit;
using HD2RuntimeGUI.Core.Generation;
using HD2RuntimeGUI.Core.Metadata;
using HD2RuntimeGUI.Core.Models;
using HD2RuntimeGUI.Core.Projects;
using HD2RuntimeGUI.Core.Services;
using Xunit;

namespace HD2RuntimeGUI.Tests;

// HD2Runtime 0.30.0 armor stats and armor passives (sdk/docs/armor-stats.md, sdk/docs/armor-passives.md) on the bundled SDK: the strict readers,
// kit / class / damage curve edits through the entity pipeline (one guarded transaction per target, both opt-ins always written), and the local
// player's armor values and passive override, which the mod sets at load (no acknowledgement checkbox anywhere: the flags are implicit).
public sealed class ArmorAuthoringTests
{
    private const string Sanctioner = "4DD749C6", Ravager = "1F9BFA78";
    private static byte[] Bundled(string name) => SdkCache.BundledComposition()[name];
    private static ArmorCatalog Read(byte[]? stats = null, byte[]? passives = null) =>
        ArmorAuthoringReader.Read(stats ?? Bundled(PlayerArmorFiles.ArmorStats), passives ?? Bundled(PlayerArmorFiles.ArmorPassives), SdkPin.Version);
    private static ArmorCatalog Armor(BuilderWorkspace w) => w.Metadata!.Entities!.Armor!;
    private static EntityField Field(BuilderWorkspace w, string key) => w.Metadata!.Entities!.Field(key)!;
    // The generated request (one pcall-isolated block) whose target is this Lua accessor, each line trimmed.
    private static string Operation(string lua, string target) => string.Join('\n', System.Text.RegularExpressions.Regex.Split(lua, @"(?=add\(function\(\) return )")
        .Single(b => b.Contains("target=" + target + ",", StringComparison.Ordinal)).Split('\n').Select(l => l.Trim()));

    [Fact] public void The_bundled_catalogs_are_read_with_their_published_counts()
    {
        var c = Read();
        Assert.Equal((135, 1155, 1141, 9, 5), (c.Kits.Length, c.FieldInstances.Length, c.KitFields(Ravager).Count() + c.Kits.Where(k => k.Id != Ravager).Sum(k => c.KitFields(k.Id).Count()),
            c.Classes.Sum(x => c.ClassFields(x.Name).Count()), c.CurveFields.Count()));
        Assert.All(c.FieldInstances, f => { Assert.True(f.Editable); Assert.True(f.AllowSharedRequired); Assert.Equal("allow_unverified_effect", f.Acknowledgement); Assert.NotNull(f.Lifecycle); });
        var torso = c.KitFields(Sanctioner).Single(f => f.UiGroup == "torso");
        Assert.Equal(("enum", "light", "hd2.fields.armor_kit.piece_weight_torso", "armor_kit:4DD749C6:torso"), (torso.Type, torso.CurrentDefault.GetString(), torso.ApiFieldConstant, torso.InstanceKey));
        Assert.Equal(["light", "medium", "heavy"], torso.AllowedValues!);
        var heavy = c.ClassFields("heavy").Single(f => f.SemanticFieldId == "armor_class.rating");
        Assert.Equal((2.0, -1.0, 4.0), (heavy.CurrentDefault.GetDouble(), heavy.EffectiveRange!.Min, heavy.EffectiveRange.Max));
        Assert.Contains(heavy.Notices!, n => n.Kind == "executable_data"); Assert.Contains(heavy.Notices!, n => n.Kind == "not_synced");
        Assert.DoesNotContain(torso.Notices!, n => n.Kind == "executable_data");
        // One request per target: a kit, a class, the curve.
        Assert.All(c.FieldInstances.GroupBy(f => f.OperationGroup), g => Assert.Single(g.Select(f => f.Target).Distinct()));
        Assert.Equal(3, c.ClassFields("light").Select(f => f.OperationGroup).Distinct().Count() + c.ClassFields("medium").Select(f => f.OperationGroup).Distinct().Count() + c.CurveFields.Select(f => f.OperationGroup).Distinct().Count());

        var p = c.Passives!;
        Assert.Equal((32, 411, 135, 4), (p.Passives.Length, p.Kits.Length, p.Kits.Count(k => k.Slot == "armor"), p.LiveArmorSwap.Passives.Length));
        Assert.Equal(["SCOUT", "ENGINEERING KIT", "MED-KIT", "SERVO-ASSISTED"], p.LiveArmorSwap.Passives.Select(x => x.Name));
        Assert.Equal("INVALID_OPTION", p.Find(0)!.SecondRefusal); Assert.Equal("ARMOR_SLOT_ONLY", p.Find(17)!.SecondRefusal); Assert.Equal(30, p.Passives.Count(x => x.AllowedAsSecond));
        // The game's #BONUS / #SIGN placeholders filled from the modifier rows.
        Assert.Equal("Increases throwing range by 30%. Provides +50% limb health.", PassiveText.Description(p.Find(8)!));
        Assert.Equal("Increases initial inventory and holding capacity of stims by +2. Increases stim effect duration by 2s.", PassiveText.Description(p.Find(7)!));
        Assert.Equal("Armor explodes 1.5s after the wearer dies. Increases initial inventory and holding capacity of throwables by 2.", PassiveText.Description(p.Find(17)!));
        Assert.All(p.Passives, x => Assert.DoesNotContain("#", PassiveText.Description(x)));
    }

    [Fact] public void The_published_formulas_give_the_vanilla_stats_and_preview_an_edit()
    {
        var c = Read(); var vanilla = ArmorMath.Vanilla(c);
        Assert.All(c.Kits, k => Assert.Equal(k.Vanilla.Rating, ArmorMath.Stats(c, k, k.Weights, vanilla).Rating, 4));
        // docs/armor-stats.md: RS-100 Sanctioner's torso heavy gives rating 50 -> 70 (A 0 -> 0.4, damage x1.25 -> x1.15).
        var sanctioner = c.Kit(Sanctioner)!; var weights = new Dictionary<string, string>(sanctioner.Weights) { ["torso"] = "heavy" };
        var s = ArmorMath.Stats(c, sanctioner, weights, vanilla);
        Assert.Equal((70.0, 0.4, 1.15), (Math.Round(s.Rating, 4), Math.Round(s.ArmorValue, 4), Math.Round(s.DamageMultiplier, 4)));
        // FS-37 Ravager all heavy: rating 150 (armor value 2, damage x0.75).
        var ravager = c.Kit(Ravager)!; var all = ravager.Slots.ToDictionary(x => x, _ => "heavy");
        Assert.Equal((150.0, 0.75), (ArmorMath.Stats(c, ravager, all, vanilla).Rating, ArmorMath.Stats(c, ravager, all, vanilla).DamageMultiplier));
        Assert.Equal(1.0, ArmorMath.Curve(c.DamageCurve.Points, -2)); Assert.Equal(0.65, ArmorMath.Curve(c.DamageCurve.Points, 4));
    }

    public static TheoryData<string, string> Malformed() => new()
    {
        { "stats", "contract" }, { "stats", "version" }, { "stats", "safety" }, { "stats", "kit count" }, { "stats", "vanilla rating" }, { "stats", "kit default" },
        { "stats", "kit type" }, { "stats", "class range" }, { "stats", "curve default" }, { "stats", "max changes" }, { "stats", "player range" },
        { "passives", "contract" }, { "passives", "duplicate id" }, { "passives", "live flag" }, { "passives", "kit passive" }, { "passives", "standard issue second" },
        { "passives", "bonus count" },
    };
    [Theory, MemberData(nameof(Malformed))]
    public void Malformed_catalogs_are_refused(string file, string mutation)
    {
        var stats = JsonNode.Parse(Bundled(PlayerArmorFiles.ArmorStats))!; var passives = JsonNode.Parse(Bundled(PlayerArmorFiles.ArmorPassives))!;
        var target = file == "stats" ? stats : passives;
        JsonNode Field(Func<JsonNode, bool> which) => stats["fieldInstances"]!.AsArray().First(f => which(f!))!;
        switch ((file, mutation))
        {
            case (_, "contract"): target["contract"] = target["contract"]!.GetValue<string>() + ".v2"; break;
            case ("stats", "version"): stats["hd2RuntimeVersion"] = "0.29.0"; break;
            case ("stats", "safety"): stats["safety"]!["rawWrites"] = true; break;
            case ("stats", "kit count"): stats["summary"]!["kits"] = 134; break;
            case ("stats", "vanilla rating"): stats["kits"]![0]!["vanilla"]!["rating"] = 75.0; break;
            case ("stats", "kit default"): Field(f => f["target"]!["resource"]!.GetValue<string>() == "armor_kit")["currentDefault"] = "heavy"; break;
            case ("stats", "kit type"): Field(f => f["target"]!["resource"]!.GetValue<string>() == "armor_kit")["type"] = "number"; break;
            case ("stats", "class range"): Field(f => f["target"]!["resource"]!.GetValue<string>() == "armor_class")["max"] = 9.0; break;
            case ("stats", "curve default"): Field(f => f["target"]!["resource"]!.GetValue<string>() == "armor_damage_curve")["currentDefault"] = 0.5; break;
            case ("stats", "max changes"): stats["transaction"]!["maxChanges"] = 4; break;
            case ("stats", "player range"): stats["player"]!["options"]!["armor_bonus"]!["max"] = 5.0; break;
            case ("passives", "duplicate id"): passives["passives"]![1]!["id"] = 0; break;
            case ("passives", "live flag"): passives["passives"]!.AsArray().First(p => p!["id"]!.GetValue<int>() == 3)!["liveProvenArmorSwap"] = true; break;
            case ("passives", "kit passive"): passives["kits"]!.AsArray().First(k => k!["slot"]!.GetValue<string>() == "armor")!["passive"] = 3; break;
            case ("passives", "standard issue second"): passives["passives"]![0]!["asSecond"] = "allowed"; break;
            case ("passives", "bonus count"): passives["passives"]!.AsArray().First(p => p!["id"]!.GetValue<int>() == 8)!["description"] = "Increases throwing range."; break;
        }
        byte[] Bytes(JsonNode n) => System.Text.Encoding.UTF8.GetBytes(n.ToJsonString());
        var e = Assert.ThrowsAny<Exception>(() => Read(Bytes(stats), Bytes(passives)));
        Assert.True(e is InvalidDataException or UnsupportedSdkException, e.ToString());
    }
    [Fact] public void A_duplicate_key_is_refused()
    {
        var text = System.Text.Encoding.UTF8.GetString(Bundled(PlayerArmorFiles.ArmorStats));
        var duplicated = text.Replace("\"status\": \"development\",", "\"status\": \"development\", \"status\": \"development\",");
        Assert.NotEqual(text, duplicated);
        Assert.ThrowsAny<Exception>(() => ArmorAuthoringReader.Read(System.Text.Encoding.UTF8.GetBytes(duplicated), null, SdkPin.Version));
    }

    [Fact] public async Task Kit_class_and_curve_edits_are_one_guarded_request_per_target_and_survive_reopen()
    {
        using var e = new TestEnvironment(); var (w, _) = await ExportFixtureTests.Fresh(e, "A1 Armor Stats");
        Assert.Contains(Navigation.Build(w.Metadata, w.Project), i => i.Page == "armor" && i.Count == 135);
        var torso = Field(w, "armor_kit:" + Sanctioner + ":torso");
        // Range and value refusals happen where the value is edited; nothing is saved.
        await Assert.ThrowsAsync<InvalidDataException>(() => w.SetEntityAsync(torso.InstanceKey, "ultra"));
        await Assert.ThrowsAsync<InvalidDataException>(() => w.SetEntityAsync("armor_class:heavy:rating", "4.5"));
        await Assert.ThrowsAsync<InvalidDataException>(() => w.SetEntityAsync("armor_damage_curve:at_0", "-0.1"));
        await Assert.ThrowsAsync<InvalidDataException>(() => w.SetEntityAsync("armor_class:light:speed", "1.6"));
        Assert.Empty(w.Project!.EntityChanges);

        await w.SetEntityAsync(torso.InstanceKey, "heavy");
        await w.SetKitWeightAsync(Ravager, "heavy");
        await w.SetEntityAsync("armor_class:heavy:rating", "3");
        await w.SetEntityAsync("armor_damage_curve:at_2", "0.6");
        Assert.Null(w.BuildError); var lua = w.LuaPreview;
        Assert.Equal(14, w.Project!.FormatVersion);
        // A kit piece: a patch on the kit by its id, both opt-ins (docs/armor-stats.md, the Sanctioner example).
        var kit = Operation(lua, "hd2.armor_stats.kit('" + Sanctioner + "')");
        Assert.Contains("hd2.ensure({\npatch={\n", kit);
        Assert.Contains("target=hd2.armor_stats.kit('4DD749C6'),\nallow_shared=true,\nallow_unverified_effect=true,\nfield=hd2.fields.armor_kit.piece_weight_torso,\nexpect='light',\nvalue='heavy',\n", kit);
        // Every Ravager piece heavy: one transaction of nine changes (Runtime's limit is 10 per request).
        var ravager = Operation(lua, "hd2.armor_stats.kit('" + Ravager + "')");
        Assert.Contains("hd2.ensure({\ntransaction={\n", ravager);
        Assert.Equal(9, System.Text.RegularExpressions.Regex.Count(ravager, @"\{field=hd2\.fields\.armor_kit\.piece_weight_[a-z_]+,expect='light',value='heavy'\}"));
        Assert.Contains("{field=hd2.fields.armor_kit.piece_weight_torso,expect='light',value='heavy'},", ravager);
        Assert.Contains("target=hd2.armor_stats.class('heavy'),\nallow_shared=true,\nallow_unverified_effect=true,\nfield=hd2.fields.armor_class.rating,\nexpect=2,\nvalue=3,\n",
            Operation(lua, "hd2.armor_stats.class('heavy')"));
        Assert.Contains("target=hd2.armor_stats.damage_curve(),\nallow_shared=true,\nallow_unverified_effect=true,\nfield=hd2.fields.armor_damage_curve.at_2,\nexpect=0.75,\nvalue=0.6,\n",
            Operation(lua, "hd2.armor_stats.damage_curve()"));
        // The kit preview follows the project: Sanctioner torso heavy with the heavy class at armor value 3 gives A 0.6, rating 80.
        var sanctioner = Armor(w).Kit(Sanctioner)!;
        var preview = ArmorMath.Stats(Armor(w), sanctioner, w.ArmorKitWeights(sanctioner), w.ArmorClassValues(), w.ArmorCurve());
        Assert.Equal((80.0, 0.6), (Math.Round(preview.Rating, 4), Math.Round(preview.ArmorValue, 4)));
        Assert.Equal(0.6, w.ArmorCurve()[3].Damage, 5);
        // A piece set back to its vanilla weight drops the edit.
        await w.SetEntityAsync(torso.InstanceKey, "light"); Assert.Equal(11, w.Project!.EntityChanges.Count);
        await w.SetEntityAsync(torso.InstanceKey, "heavy"); lua = w.LuaPreview;

        await w.OpenAsync(w.Project!.Id); Assert.Null(w.BuildError); Assert.Equal(lua, w.LuaPreview); Assert.Equal(12, w.Project!.EntityChanges.Count);
        var (manifest, exported) = await ExportFixtureTests.Export(w, "A1-armor-stats");
        Assert.Equal(SdkPin.Version, manifest.RootElement.GetProperty("requires").GetProperty("hd2runtime").GetProperty("min_version").GetString());
        Assert.Contains("hd2.armor_stats.damage_curve()", exported);
    }

    [Fact] public async Task Player_values_and_the_passive_override_are_set_by_the_mod_with_their_opt_ins_and_survive_reopen()
    {
        using var e = new TestEnvironment(); var (w, _) = await ExportFixtureTests.Fresh(e, "A2 Armor Player");
        // Published ranges and the passive refusals, inline; nothing is saved.
        await Assert.ThrowsAsync<InvalidDataException>(() => w.SetPlayerArmorBonusAsync(3.5));
        await Assert.ThrowsAsync<InvalidDataException>(() => w.SetPlayerStaminaFactorAsync(0.05));
        await Assert.ThrowsAsync<InvalidDataException>(() => w.SetPlayerPassivesAsync(8, 17));   // INTEGRATED EXPLOSIVES: ARMOR_SLOT_ONLY
        await Assert.ThrowsAsync<InvalidDataException>(() => w.SetPlayerPassivesAsync(8, 8));    // SAME_PASSIVE
        await Assert.ThrowsAsync<InvalidDataException>(() => w.SetPlayerPassivesAsync(8, 0));    // STANDARD ISSUE as second: use none
        await Assert.ThrowsAsync<InvalidDataException>(() => w.SetPlayerPassivesAsync(4, null)); // unused id
        Assert.Null(w.Project!.PlayerArmor);

        // A live-proven armor-slot swap alone needs no allow_unverified_effect (liveArmorSwap).
        await w.SetPlayerPassivesAsync(8, null);
        Assert.Contains("add(function() return hd2.player_passives.set({armor='SERVO-ASSISTED'}) end)\n", w.LuaPreview);
        Assert.Equal(14, w.Project!.FormatVersion);
        // With a second passive (the helmet slot), or another armor passive, it is unverified.
        await w.SetPlayerPassivesAsync(8, 2);
        Assert.Contains("add(function() return hd2.player_passives.set({armor='SERVO-ASSISTED',second='SCOUT',allow_unverified_effect=true}) end)\n", w.LuaPreview);
        await w.SetPlayerPassivesAsync(null, 7);
        Assert.Contains("add(function() return hd2.player_passives.set({second='MED-KIT',allow_unverified_effect=true}) end)\n", w.LuaPreview);
        await w.SetPlayerPassivesAsync(17, null);
        Assert.Contains("add(function() return hd2.player_passives.set({armor='INTEGRATED EXPLOSIVES',allow_unverified_effect=true}) end)\n", w.LuaPreview);

        await w.SetPlayerArmorBonusAsync(1); await w.SetPlayerStaminaFactorAsync(0.5);
        Assert.Null(w.BuildError); var lua = w.LuaPreview;
        Assert.Contains("add(function() return (function()\n"
            + "    local player=hd2.armor_stats.player()\n"
            + "    local function apply()\n"
            + "        if not player:describe() then return nil end\n"
            + "        return player:set({armor_bonus=1,stamina_factor=0.5,allow_unverified_effect=true})\n"
            + "    end\n"
            + "    -- The game applies the kit's own stamina factor again at every respawn: set the values again then.\n"
            + "    hd2.events.on('player_spawned',function(e) if e.local_player then apply() end end)\n"
            + "    return apply()\n"
            + "end)() end)\n", lua);
        await w.OpenAsync(w.Project!.Id); Assert.Null(w.BuildError); Assert.Equal(lua, w.LuaPreview);
        Assert.Equal((1.0, 0.5, 17, (int?)null), (w.Project!.PlayerArmor!.ArmorBonus, w.Project.PlayerArmor.StaminaFactor, w.Project.PlayerArmor.PassiveArmor, w.Project.PlayerArmor.PassiveSecond));
        var (manifest, exported) = await ExportFixtureTests.Export(w, "A2-armor-player");
        Assert.Equal(SdkPin.Version, manifest.RootElement.GetProperty("requires").GetProperty("hd2runtime").GetProperty("min_version").GetString());
        Assert.Contains("hd2.player_passives.set(", exported);

        // Clearing everything removes the section again.
        await w.ResetPlayerArmorAsync(); Assert.Null(w.Project!.PlayerArmor); Assert.DoesNotContain("player_passives", w.LuaPreview);
    }

    [Fact] public void A_project_with_player_armor_needs_format_14()
    {
        var p = new ModProject { FormatVersion = 13, Id = Guid.NewGuid(), PlayerArmor = new() { ArmorBonus = 1 } };
        Assert.Equal(14, ProjectIdentity.RequiredFormat(p));
        Assert.Throws<InvalidDataException>(() => ProjectIdentity.Validate(p));
        p.FormatVersion = 14; p.PlayerArmor = new();
        Assert.Throws<InvalidDataException>(() => ProjectIdentity.Validate(p));
        p.PlayerArmor = new() { PassiveArmor = 8, PassiveSecond = 8 };
        Assert.Throws<InvalidDataException>(() => ProjectIdentity.Validate(p));
    }

    [Fact] public async Task The_audit_lists_every_armor_field_and_both_files_as_supported()
    {
        using var e = new TestEnvironment(); var (w, sdk) = await ExportFixtureTests.Fresh(e, "A3 Armor Audit");
        var report = CapabilityAudit.Run(sdk, new Dictionary<string, byte[]> { [PlayerArmorFiles.ArmorStats] = [], [PlayerArmorFiles.ArmorPassives] = [] });
        var armor = report.Entries.Where(x => x.Area is "armor_kit" or "armor_class" or "armor_damage_curve" or "armor_player" or "player_passives").ToArray();
        Assert.Equal(1155, armor.Where(x => x.Area.StartsWith("armor_", StringComparison.Ordinal) && x.Area != "armor_player").Sum(x => x.Instances));
        Assert.All(armor, x => Assert.Equal(CapabilityClass.SUPPORTED_UI, x.Class));
        Assert.Equal(["armor_bonus", "stamina_factor"], armor.Where(x => x.Area == "armor_player").Select(x => x.Capability));
        Assert.Equal(["armor", "second"], armor.Where(x => x.Area == "player_passives").Select(x => x.Capability));
        Assert.All(report.Files, f => Assert.Equal(CapabilityClass.SUPPORTED_UI, f.Class));
        _ = w;
    }
}
