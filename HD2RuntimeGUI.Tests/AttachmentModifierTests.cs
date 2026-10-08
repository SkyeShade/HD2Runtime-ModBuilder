using System.Text.Json.Nodes;
using HD2RuntimeGUI.Core.Audit;
using HD2RuntimeGUI.Core.Generation;
using HD2RuntimeGUI.Core.Metadata;
using HD2RuntimeGUI.Core.Models;
using HD2RuntimeGUI.Core.Projects;
using HD2RuntimeGUI.Core.Services;
using Xunit;

namespace HD2RuntimeGUI.Tests;

// HD2Runtime 0.30.0 muzzle, optics and underbarrel stat modifiers (WeaponAttachmentModifierCapabilities.json, sdk/docs/weapon-attachments.md)
// on the bundled SDK: the strict reader, the hd2.weapon_attachment Lua (allow_shared and allow_unverified_effect always, one request per
// definition), published ranges, one shared definition edited from two weapons, save / reopen, an export fixture and the audit.
public sealed class AttachmentModifierTests
{
    private const string Liberator = "AR-23 Liberator", Arbitrator = "AR-11 Arbitrator";
    private const string FlashHider = "weapon-attachment/v1/muzzle/5-5mm-flash-hider/3d6e793c78a01b06";
    private const string Reddot = "weapon-attachment/v1/optics/tube-reddot-2x/184551d23defaf96";
    private const string VerticalGrip = "weapon-attachment/v1/underbarrel/vertical-grip/0222918bcbe339ba";
    private const string MuzzleBreak8 = "weapon-attachment/v1/muzzle/8mm-muzzle-break/10170c032e91884a";
    private const string Sway = "attachment.modifier.sway", Ergonomics = "attachment.ergonomics_modifier";

    private static async Task<BuilderWorkspace> Fresh(TestEnvironment e, string name = "Attachment Modifiers") => (await ExportFixtureTests.Fresh(e, name)).W;
    private static AttachmentModifierCatalog Catalog(BuilderWorkspace w) => w.Metadata!.Entities!.AttachmentModifiers!;
    private static EntityField Field(BuilderWorkspace w, string attachment, string id) => Catalog(w).FieldInstances.Single(f => f.Target.Attachment == attachment && f.SemanticFieldId == id);
    // The generated request whose target is this attachment, each line trimmed.
    private static string Operation(string lua, string attachment) => string.Join('\n', System.Text.RegularExpressions.Regex.Split(lua, @"(?=add\(function\(\) return )")
        .Single(b => b.Contains("target=hd2.weapon_attachment('" + attachment + "'),", StringComparison.Ordinal)).Split('\n').Select(l => l.Trim()));
    private static byte[] Bundled() => SdkFixtures.Entry(SdkPin.Version, AttachmentModifierReader.FileName);

    [Fact] public async Task The_bundled_catalog_loads_with_its_published_counts()
    {
        using var e = new TestEnvironment(); var w = await Fresh(e); var c = Catalog(w);
        Assert.Equal(73, c.Attachments.Length);
        Assert.Equal((37, 17, 19), (c.Attachments.Count(a => a.Slot == "muzzle"), c.Attachments.Count(a => a.Slot == "optics"), c.Attachments.Count(a => a.Slot == "underbarrel")));
        Assert.Equal(44, c.Attachments.Count(a => a.FieldInstanceKeys.Length > 0));
        Assert.Equal(132, c.FieldInstances.Length); Assert.All(c.FieldInstances, f => Assert.True(f.Editable));
        Assert.Equal(AttachmentModifierReader.Modifiers.Values.Order(), c.FieldInstances.Select(f => f.SemanticFieldId).Distinct().Order());
        Assert.Equal(67, c.Weapons.Length); Assert.False(c.Selection.Writable);
        Assert.All(c.FieldInstances, f =>
        {
            Assert.True(f.AllowSharedRequired); Assert.Equal("allow_unverified_effect", f.Acknowledgement); Assert.Equal(f.Target.Path, f.InstanceKey.Split(':', '/')[1]);
            Assert.Equal(f.SemanticFieldId == Ergonomics ? (-100.0, 100.0) : (0.0, 10.0), (f.Min!.Value, f.Max!.Value));
        });
        // The one blocked modifier is shown with its blocker and has no field.
        var blocked = Assert.Single(c.Attachments.SelectMany(a => a.Blocked.Select(m => (a, m))));
        Assert.Equal((MuzzleBreak8, "Mul_ClimbHorizontal", 0.6), (blocked.a.SemanticId, blocked.m.Modifier, blocked.m.Value));
        Assert.Contains("page boundary", blocked.m.Blocker); Assert.DoesNotContain(c.FieldInstances, f => f.Target.Attachment == MuzzleBreak8 && f.SemanticFieldId == "attachment.modifier.climb_horizontal");
        // Magazine and modifier fields share the resource (and ergonomics its field id) but never a key; both are resolvable.
        var entities = w.Metadata!.Entities!; var magazines = entities.Attachments!.FieldInstances;
        Assert.Empty(magazines.Select(f => f.InstanceKey).Intersect(c.FieldInstances.Select(f => f.InstanceKey)));
        Assert.Equal(24, magazines.Count(f => f.SemanticFieldId == Ergonomics)); Assert.Equal(38, c.FieldInstances.Count(f => f.SemanticFieldId == Ergonomics));
        Assert.All(magazines.Where(f => f.SemanticFieldId == Ergonomics), f => Assert.Equal("magazine", entities.Field(f.InstanceKey)!.Target.Path));
        Assert.All(c.FieldInstances, f => Assert.Same(f, entities.Field(f.InstanceKey)));
        Assert.Equal(132, entities.AllFields.Count(f => AttachmentModifierReader.IsModifierPath(f.Target.Resource, f.Target.Path)));
        // Weapon slots: the native default first, then the unlock list.
        var muzzle = c.Weapon(Liberator)!.Slots.Single(s => s.Slot == "muzzle");
        Assert.True(muzzle.Options[0].Default); Assert.Equal(muzzle.DefaultAttachment, muzzle.Options[0].Attachment); Assert.Contains(muzzle.Options, o => o.Attachment == FlashHider && !o.Default);
    }

    public static TheoryData<string> Malformed => new()
    {
        "contract", "version", "shared", "slot-path", "magazine-key", "summary", "option-slot", "blocked-writable", "api-dotted", "range", "weapon", "safety",
        "consumers", "default-order", "duplicate-index",
    };
    [Theory, MemberData(nameof(Malformed))] public async Task Malformed_catalogs_are_refused(string variant)
    {
        using var e = new TestEnvironment(); var sdk = await SdkFixtures.Install(e, SdkPin.Version);
        var root = JsonNode.Parse(Bundled())!; var fields = root["fieldInstances"]!.AsArray(); var attachments = root["attachments"]!.AsArray();
        JsonNode Attachment(string id) => attachments.Single(a => (string)a!["semanticId"]! == id)!;
        JsonNode FieldOf(string id, string field) => fields.Single(f => (string)f!["target"]!["attachment"]! == id && (string)f["semanticFieldId"]! == field)!;
        switch (variant)
        {
            case "contract": root["contract"] = "hd2runtime.weapon_attachment.modifier.v2"; break;
            case "version": root["hd2RuntimeVersion"] = "0.30.0-dev"; break; // another SDK's catalog than the bound 0.30.0
            case "shared": FieldOf(FlashHider, Sway)["allowSharedRequired"] = false; break;
            case "slot-path": FieldOf(FlashHider, Sway)["target"]!["path"] = "optics"; break;
            // A key in the magazine namespace (a different slot in the key than in the target).
            case "magazine-key":
            {
                var f = FieldOf(FlashHider, Sway); var key = ((string)f["instanceKey"]!).Replace("attachment:muzzle/", "attachment:magazine/");
                var keys = Attachment(FlashHider)["fieldInstanceKeys"]!.AsArray(); keys[keys.Select(k => (string)k!).ToList().IndexOf((string)f["instanceKey"]!)] = key; f["instanceKey"] = key; break;
            }
            case "summary": root["summary"]!["fieldInstances"] = 131; break;
            case "option-slot": root["weapons"]!.AsArray().Single(x => (string)x!["weapon"]! == Liberator)!["slots"]!.AsArray().Single(s => (string)s!["slot"]! == "optics")!["options"]!.AsArray()
                .Add(new JsonObject { ["attachment"] = FlashHider, ["name"] = "5,5mm. Flash Hider", ["relationship"] = "unlock_listed" }); break;
            case "blocked-writable": { var m = Attachment(MuzzleBreak8)["modifiers"]!.AsArray().Single(x => (string)x!["modifier"]! == "Mul_ClimbHorizontal")!; m["writable"] = true; m["blocker"] = null; break; }
            case "api-dotted": FieldOf(FlashHider, Sway)["apiFieldConstant"] = "hd2.fields.attachment.modifier.sway"; break;
            case "range": FieldOf(FlashHider, Sway)["max"] = 20; break;
            case "weapon": root["weapons"]!.AsArray().Single(x => (string)x!["weapon"]! == Liberator)!["weapon"] = "AR-99 Unknown"; break;
            case "safety": root["safety"]!["writesDuringGeneration"] = 1; break;
            case "consumers": FieldOf(FlashHider, Sway)["sharedConsumers"]!.AsArray().RemoveAt(0); break;
            case "default-order":
            {
                var options = root["weapons"]!.AsArray().Single(x => (string)x!["weapon"]! == Liberator)!["slots"]!.AsArray().Single(s => (string)s!["slot"]! == "optics")!["options"]!.AsArray();
                var first = options[0]!; options.RemoveAt(0); options.Add(first); break;
            }
            case "duplicate-index": Attachment(FlashHider)["modifiers"]![1]!["index"] = 0; break;
        }
        var bytes = System.Text.Encoding.UTF8.GetBytes(root.ToJsonString());
        var error = Record.Exception(() => AttachmentModifierReader.Read(bytes, sdk.Version, sdk.PlayerWeapons!, sdk.Entities!.Attachments));
        Assert.True(error is InvalidDataException or UnsupportedSdkException, variant + ": " + error?.GetType().Name);
        // The unmodified file is accepted by the same call.
        Assert.Equal(132, AttachmentModifierReader.Read(Bundled(), sdk.Version, sdk.PlayerWeapons!, sdk.Entities!.Attachments).FieldInstances.Length);
    }

    [Fact] public async Task A_muzzle_sway_edit_is_a_guarded_patch_with_both_opt_ins()
    {
        using var e = new TestEnvironment(); var w = await Fresh(e);
        await w.SetEntityAsync(Field(w, FlashHider, Sway).InstanceKey, "0.5");
        Assert.Null(w.BuildError); var change = Assert.Single(w.Project!.EntityChanges);
        Assert.Equal(("weapon_attachment", FlashHider, "muzzle"), (change.Resource, change.Entity, change.Path));
        var op = Operation(w.LuaPreview, FlashHider);
        Assert.Contains("hd2.ensure({\npatch={\n", op);
        Assert.Contains("target=hd2.weapon_attachment('" + FlashHider + "'),\nallow_shared=true,\nallow_unverified_effect=true,\nfield=hd2.fields.attachment.modifier_sway,\nexpect=1.1,\nvalue=0.5,\n}", op);
        Assert.Contains("weapon_attachment attachment.modifier.sway", RuntimeRequirement.Floors(w.Project).Select(f => f.Reason));
        // A second modifier of the same definition makes one transaction for that definition.
        await w.SetEntityAsync(Field(w, FlashHider, "attachment.modifier.recoil_horizontal").InstanceKey, "0.5");
        op = Operation(w.LuaPreview, FlashHider);
        Assert.Contains("hd2.ensure({\ntransaction={\n", op);
        Assert.Contains("allow_shared=true,\nallow_unverified_effect=true,\nchanges={\n{field=hd2.fields.attachment.modifier_recoil_horizontal,expect=0.8,value=0.5},\n{field=hd2.fields.attachment.modifier_sway,expect=1.1,value=0.5},\n},\n", op);
    }

    [Fact] public async Task An_optics_ergonomics_edit_targets_the_sight_not_a_magazine()
    {
        using var e = new TestEnvironment(); var w = await Fresh(e);
        var f = Field(w, Reddot, Ergonomics); Assert.Equal("attachment:optics/tube-reddot-2x/184551d23defaf96:attachment.ergonomics_modifier", f.InstanceKey);
        await w.SetEntityAsync(f.InstanceKey, "4");
        var op = Operation(w.LuaPreview, Reddot);
        Assert.Contains("hd2.ensure({\npatch={\n", op);
        Assert.Contains("target=hd2.weapon_attachment('" + Reddot + "'),\nallow_shared=true,\nallow_unverified_effect=true,\nfield=hd2.fields.attachment.ergonomics_modifier,\nexpect=-1,\nvalue=4,\n}", op);
        Assert.DoesNotContain("weapon-attachment/v1/magazine/", w.LuaPreview);
    }

    [Fact] public async Task Values_outside_the_published_range_are_refused()
    {
        using var e = new TestEnvironment(); var w = await Fresh(e);
        var sway = Field(w, FlashHider, Sway).InstanceKey; var ergonomics = Field(w, Reddot, Ergonomics).InstanceKey;
        foreach (var (key, value, bounds) in new[] { (sway, "10.5", "between 0 and 10"), (sway, "-0.1", "between 0 and 10"), (ergonomics, "101", "between -100 and 100"), (ergonomics, "-100.5", "between -100 and 100") })
            Assert.Contains(bounds, (await Assert.ThrowsAsync<InvalidDataException>(() => w.SetEntityAsync(key, value))).Message);
        Assert.Empty(w.Project!.EntityChanges);
        await w.SetEntityAsync(sway, "10"); await w.SetEntityAsync(ergonomics, "-100"); await w.SetEntityAsync(sway, "0");
        Assert.Equal(2, w.Project.EntityChanges.Count); Assert.Null(w.BuildError);
    }

    [Fact] public async Task One_attachment_edited_from_two_weapons_is_one_change()
    {
        using var e = new TestEnvironment(); var w = await Fresh(e); var c = Catalog(w);
        // The sight both weapons list in their optics slot is one definition with one set of fields.
        string Optic(string weapon) => c.Weapon(weapon)!.Slots.Single(s => s.Slot == "optics").Options.Single(o => o.Attachment == Reddot).Attachment;
        var viaLiberator = c.Fields(c.Attachment(Optic(Liberator))!).Single(); var viaArbitrator = c.Fields(c.Attachment(Optic(Arbitrator))!).Single();
        Assert.Same(viaLiberator, viaArbitrator);
        Assert.Contains(Liberator, c.Attachment(Reddot)!.CompatibleWeapons); Assert.Contains(Arbitrator, c.Attachment(Reddot)!.CompatibleWeapons);
        await w.SetEntityAsync(viaLiberator.InstanceKey, "2");
        await w.SetEntityAsync(viaArbitrator.InstanceKey, "3");
        var change = Assert.Single(w.Project!.EntityChanges); Assert.Equal(3, change.DesiredValue.GetDouble());
        // Both weapons' summaries count the shared edit.
        Assert.All(new[] { Liberator, Arbitrator }, n => Assert.Equal((1, 1), (w.WeaponSummary(n).ModifiedFields, w.WeaponSummary(n).SharedChanges)));
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(w.LuaPreview, System.Text.RegularExpressions.Regex.Escape("target=hd2.weapon_attachment('" + Reddot + "')")));
        // Back to vanilla removes it, from either weapon.
        await w.SetEntityAsync(viaArbitrator.InstanceKey, "-1"); Assert.Empty(w.Project.EntityChanges);
    }

    [Fact] public async Task Edits_survive_save_and_reopen_as_format_14()
    {
        using var e = new TestEnvironment(); var w = await Fresh(e);
        await w.SetEntityAsync(Field(w, FlashHider, Sway).InstanceKey, "0.75");
        await w.SetEntityAsync(Field(w, VerticalGrip, "attachment.modifier.recoil_vertical").InstanceKey, "0.5");
        await w.SetEntityAsync(Field(w, Reddot, Ergonomics).InstanceKey, "2");
        Assert.Equal(14, ProjectIdentity.RequiredFormat(w.Project!)); Assert.Equal(14, w.Project!.FormatVersion);
        var lua = w.LuaPreview; Assert.Null(w.BuildError);
        await w.OpenAsync(w.Project.Id);
        Assert.Null(w.BuildError); Assert.Equal(lua, w.LuaPreview); Assert.Equal(3, w.Project!.EntityChanges.Count);
        Assert.Equal(["muzzle", "optics", "underbarrel"], w.Project.EntityChanges.Select(c => c.Path).Order());
        // A saved modifier edit whose entity is not a definition of its slot is not a valid project.
        var bad = w.Project.EntityChanges[0] with { Path = w.Project.EntityChanges[0].Path == "optics" ? "muzzle" : "optics" };
        var copy = System.Text.Json.JsonSerializer.Deserialize<ModProject>(System.Text.Json.JsonSerializer.Serialize(w.Project, Core.Storage.JsonStorage.Options), Core.Storage.JsonStorage.Options)!;
        copy.EntityChanges[0] = bad;
        Assert.Throws<InvalidDataException>(() => ProjectIdentity.Validate(copy));
    }

    [Fact] public async Task Modifier_edits_take_in_game_options_within_the_published_range()
    {
        using var e = new TestEnvironment(); var w = await Fresh(e);
        var sway = Field(w, FlashHider, Sway);
        await w.SetEntityAsync(sway.InstanceKey, "0.5");
        await w.SetModOptionsEnabledAsync(true);
        var t = w.OptionTarget(ModOptionsService.EntityKey(sway.InstanceKey))!;
        Assert.True(t.Eligible); Assert.Equal((0.0, 10.0), (t.RangeMin!.Value, t.RangeMax!.Value));
        var wide = w.SuggestOptionRow(t); wide.Max = 11;
        await Assert.ThrowsAsync<InvalidDataException>(() => w.SaveOptionRowAsync(wide));
        await w.SaveOptionRowAsync(w.SuggestOptionRow(t)); Assert.Null(w.BuildError);
        Assert.Contains("allow_unverified_effect=true,", w.LuaPreview); Assert.Contains("value=option_", w.LuaPreview);
    }

    // Export fixture A1 (validated with HD2Runtime's own validator by tools/validate-exports.py, like F1-F10): a Flash Hider transaction, the
    // Liberator's default sight and a Vertical Grip, one of them behind an in-game option.
    [Fact] public async Task A1_attachment_modifiers_export()
    {
        using var e = new TestEnvironment(); var w = await Fresh(e, "A1 Attachment Modifiers");
        await w.SetEntityAsync(Field(w, FlashHider, "attachment.modifier.recoil_horizontal").InstanceKey, "0.5");
        await w.SetEntityAsync(Field(w, FlashHider, "attachment.modifier.recoil_vertical").InstanceKey, "0.6");
        await w.SetEntityAsync(Field(w, FlashHider, Ergonomics).InstanceKey, "0");
        await w.SetEntityAsync(Field(w, Reddot, Ergonomics).InstanceKey, "3");
        var grip = Field(w, VerticalGrip, "attachment.modifier.climb_vertical");
        await w.SetEntityAsync(grip.InstanceKey, "0.5");
        await w.SetModOptionsEnabledAsync(true);
        await w.SaveOptionRowAsync(w.SuggestOptionRow(w.OptionTarget(ModOptionsService.EntityKey(grip.InstanceKey))!));
        var (manifest, lua) = await ExportFixtureTests.Export(w, "A1-attachment-modifiers");
        Assert.Equal(SdkPin.Version, manifest.RootElement.GetProperty("requires").GetProperty("hd2runtime").GetProperty("min_version").GetString());
        Assert.Equal(3, System.Text.RegularExpressions.Regex.Matches(lua, @"target=hd2\.weapon_attachment\('weapon-attachment/v1/(muzzle|optics|underbarrel)/").Count);
        Assert.Equal(3, System.Text.RegularExpressions.Regex.Matches(lua, "allow_shared=true,").Count);
        Assert.Equal(3, System.Text.RegularExpressions.Regex.Matches(lua, "allow_unverified_effect=true,").Count);
    }

    [Fact] public async Task The_capability_audit_lists_every_modifier_field_as_authored()
    {
        using var e = new TestEnvironment(); var report = await CapabilityAuditTests.Report(e);
        foreach (var id in AttachmentModifierReader.Modifiers.Values)
        {
            var entry = Assert.Single(report.Entries, x => x.Area == "weapon_attachment" && x.Capability == id && x.Type == "number");
            Assert.Equal(CapabilityClass.SUPPORTED_UI, entry.Class); Assert.Equal(entry.Instances, entry.WritableInstances);
        }
        // Ergonomics counts magazines (24) and modifiers (38) together.
        Assert.Equal(62, report.Entries.Single(x => x.Area == "weapon_attachment" && x.Capability == Ergonomics).Instances);
        Assert.Equal(CapabilityClass.SUPPORTED_UI, report.Files.Single(f => f.Name == AttachmentModifierReader.FileName).Class);
        Assert.Empty(report.UnexpectedMissing);
    }
}
