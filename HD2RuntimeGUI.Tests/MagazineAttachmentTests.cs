using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using HD2RuntimeGUI.Core.Generation;
using HD2RuntimeGUI.Core.Metadata;
using HD2RuntimeGUI.Core.Services;
using Xunit;

namespace HD2RuntimeGUI.Tests;

// Runtime 0.23.1 magazine attachment authoring (bundled current SDK).
public sealed class MagazineAttachmentTests
{
    private const string Concussive = "AR-23C Liberator Concussive";
    private const string Drum = "weapon-attachment/v1/magazine/rifle-5-5x50mm-drum/fa499a29b375c6cf";
    private static async Task<BuilderWorkspace> Workspace(TestEnvironment e, string resource = "mods/tests/magazines")
    {
        // Pinned to the published 0.24.0 SDK (the bundled offline SDK moves with each release).
        var sdk = await SdkFixtures.Install(e, "0.24.0");
        var w = e.Workspace(); await w.CreateAsync(new("Magazines", "Tests", resource, "0.1.0"), sdk); return w;
    }
    private static MagazineAttachmentCatalog Catalog(BuilderWorkspace w) => w.Metadata!.Entities!.Attachments!;
    private static EntityField Field(BuilderWorkspace w, string attachment, string id) => Catalog(w).FieldInstances.Single(f => f.Target.Attachment == attachment && f.SemanticFieldId == id);
    private static JsonNode Json() => JsonNode.Parse(SdkFixtures.Entry("0.24.0", MagazineAttachmentReader.FileName))!;

    [Fact] public async Task Published_0231_catalog_loads()
    {
        using var e = new TestEnvironment(); var w = await Workspace(e); var c = Catalog(w);
        Assert.Equal("0.24.0", w.Metadata!.Version); Assert.Equal(43, c.Attachments.Length); Assert.Equal(172, c.FieldInstances.Length);
        Assert.Equal(20, c.Weapons.Length); Assert.Equal(14, c.Weapons.Count(x => x.MagazineSlot.BaseRecordCapacityIsPlaceholder));
        Assert.False(c.Selection.Writable); Assert.All(c.FieldInstances, f => { Assert.True(f.AllowSharedRequired); Assert.Equal("allow_unverified_effect", f.Acknowledgement); });
        Assert.Equal(172, w.Metadata.Entities!.AllFields.Count(f => f.Target.Resource == "weapon_attachment"));
    }
    [Fact] public async Task Attachment_weapons_keep_weapon_level_ammo_read_only_and_are_not_aliased()
    {
        using var e = new TestEnvironment(); var w = await Workspace(e); var c = Catalog(w); var players = w.Metadata!.PlayerWeapons!;
        foreach (var entry in c.Weapons)
        {
            var weapon = players.Weapons.Single(p => p.Name == entry.Weapon);
            Assert.All(weapon.Fields.Where(f => f.SemanticFieldId is "weapon.capacity" or "magazine.capacity" or "magazine.starting_magazines" or "magazine.magazines_from_supply" or "magazine.spare_magazines"),
                f => Assert.False(f.Editable));
        }
        await Assert.ThrowsAnyAsync<Exception>(() => w.SetWeaponChangeAsync(Concussive, "weapon.capacity", "90", false));
        Assert.Empty(w.Project!.WeaponChanges);
        // Weapon-owned magazines (31 weapons publish an editable magazine.capacity) are untouched by the attachment model.
        var owned = players.Weapons.Where(p => p.Fields.Any(f => f.SemanticFieldId == "magazine.capacity" && f.Editable)).ToArray();
        Assert.Equal(31, owned.Length); Assert.All(owned, p => Assert.Null(c.Weapon(p.Name)));
        // Attachment fields never target a weapon, and no weapon field targets an attachment.
        Assert.All(c.FieldInstances, f => Assert.StartsWith("hd2.fields.attachment.", f.ApiFieldConstant));
    }
    [Fact] public async Task Concussive_drum_is_the_resolved_default_with_its_real_60_rounds()
    {
        using var e = new TestEnvironment(); var w = await Workspace(e); var c = Catalog(w); var entry = c.Weapon(Concussive)!;
        Assert.True(entry.MagazineSlot.BaseRecordCapacityIsPlaceholder); Assert.Equal(Drum, entry.MagazineSlot.DefaultAttachment);
        var resolved = c.Resolved(entry); var (drum, option, relationship) = Assert.Single(resolved);
        Assert.Equal(Drum, drum.SemanticId); Assert.Equal("Drum Magazine", option!.Name); Assert.Equal("native_resource_default", relationship);
        Assert.Equal(60, drum.Values.Capacity); Assert.Equal(60, Field(w, Drum, "attachment.magazine_capacity").CurrentDefault.GetInt32());
        Assert.Equal(4, drum.Values.StartingMagazines); Assert.Equal(4, drum.FieldInstanceKeys.Length);
        Assert.Contains(Concussive, drum.Consumers.NativeDefaultOf); Assert.False(drum.Consumers.ScopeComplete);
    }
    [Fact] public async Task Ambiguous_and_absent_options_stay_unresolved()
    {
        using var e = new TestEnvironment(); var w = await Workspace(e); var c = Catalog(w); var entry = c.Weapon(Concussive)!;
        foreach (var name in new[] { "Short Magazine", "Extended Magazine" })
        {
            var o = entry.MagazineSlot.Options.Single(x => x.Name == name);
            Assert.False(o.Resolved); Assert.Equal("catalog_effect_fingerprint_ambiguous", o.Relationship); Assert.True(o.Candidates.Length > 1); Assert.False(string.IsNullOrWhiteSpace(o.Blocker));
            Assert.DoesNotContain(c.Resolved(entry), r => r.Option?.Name == name);
        }
        var scythe = c.Weapon("LAS-5 Scythe")!; Assert.Null(scythe.MagazineSlot.DefaultAttachment); Assert.Empty(c.Resolved(scythe));
        Assert.All(scythe.MagazineSlot.Options, o => { Assert.Equal("catalog_effect_fingerprint_absent", o.Relationship); Assert.Empty(o.Candidates); });
        var breaker = c.Resolved(c.Weapon("SG-225 Breaker")!);
        Assert.Equal(3, breaker.Count); Assert.Equal(2, breaker.Count(r => r.Relationship == "catalog_effect_fingerprint_unique"));
    }
    [Fact] public async Task Writes_build_without_acknowledgement_and_emit_both_flags()
    {
        using var e = new TestEnvironment(); var w = await Workspace(e);
        var capacity = Field(w, Drum, "attachment.magazine_capacity"); await w.SetEntityAsync(capacity.InstanceKey, "90");
        // allow_shared and allow_unverified_effect are implicit: the write builds without an acknowledgement.
        Assert.Null(w.BuildError);
        var lua = w.LuaPreview;
        Assert.Contains($"target=hd2.weapon_attachment('{Drum}'),", lua); Assert.Contains("allow_shared=true,", lua); Assert.Contains("allow_unverified_effect=true,", lua);
        Assert.Contains("field=hd2.fields.attachment.magazine_capacity,", lua); Assert.Contains("expect=60,", lua); Assert.Contains("value=90,", lua); Assert.Contains("patch={", lua);
        // Changing the value or adding a field of the same definition: one transaction per definition.
        await w.SetEntityAsync(capacity.InstanceKey, "100"); await w.SetEntityAsync(Field(w, Drum, "attachment.spare_magazines").InstanceKey, "8");
        Assert.Null(w.BuildError); Assert.Contains("transaction={", w.LuaPreview); Assert.Contains("{field=hd2.fields.attachment.magazine_capacity,expect=60,value=100}", w.LuaPreview);
        // The recorded acknowledgement can be toggled but never blocks the build or drops the flags.
        await w.SetAttachmentAcknowledgedAsync(Drum, true); Assert.True(BuilderWorkspace.AttachmentAcknowledged(w.Project, Catalog(w), Drum));
        await w.SetAttachmentAcknowledgedAsync(Drum, false); Assert.Null(w.BuildError);
        Assert.Contains("allow_shared=true,", w.LuaPreview); Assert.Contains("allow_unverified_effect=true,", w.LuaPreview);
        Assert.True(BuilderWorkspace.AttachmentAcknowledged(w.Project, Catalog(w), Drum) == false);
    }
    [Fact] public async Task Raw_identifiers_are_rejected()
    {
        using var e = new TestEnvironment(); var w = await Workspace(e); var svc = new EntityChangeService();
        Assert.Throws<InvalidDataException>(() => svc.Create(w.Metadata!, "attachment:magazine/made-up/0000000000000000:attachment.magazine_capacity", "90"));
        await w.SetEntityAsync(Field(w, Drum, "attachment.magazine_capacity").InstanceKey, "90");
        var bad = w.Project!.EntityChanges.Single() with { Entity = "0xCF5F176E0E322BE1" };
        w.Project.EntityChanges = [bad]; await Assert.ThrowsAsync<InvalidDataException>(() => e.Store.SaveAsync(w.Project));
    }
    [Fact] public async Task Changes_persist_group_reset_and_export_without_native_ids()
    {
        using var e = new TestEnvironment(); var w = await Workspace(e);
        await w.SetEntityAsync(Field(w, Drum, "attachment.magazine_capacity").InstanceKey, "90"); await w.SetAttachmentAcknowledgedAsync(Drum, true);
        await w.OpenAsync(w.Project!.Id); Assert.Null(w.BuildError); Assert.Equal(6, w.Project!.FormatVersion);
        var change = w.Project.EntityChanges.Single(); Assert.Equal("weapon_attachment", change.Resource); Assert.Equal(Drum, change.Entity); Assert.Equal("magazine", change.Path);
        var json = File.ReadAllText(e.Paths.ProjectFile(w.Project.Id)); Assert.DoesNotContain("backing:", json); Assert.DoesNotContain("0x", json);
        await w.ExportAsync(); var first = File.ReadAllBytes(w.LastExport!); await w.OpenAsync(w.Project.Id); await w.ExportAsync(); Assert.Equal(first, File.ReadAllBytes(w.LastExport!));
        foreach (var native in new[] { "0x", "backing:", "operation:", "shared-scope:" }) Assert.DoesNotContain(native, w.LuaPreview);
        await w.ResetEntityAsync("weapon_attachment", Drum); Assert.Empty(w.Project.EntityChanges); Assert.DoesNotContain("weapon_attachment", w.LuaPreview);
    }
    [Fact] public async Task Older_sdks_have_no_attachment_model_and_rebind_keeps_existing_keys()
    {
        using var e = new TestEnvironment(); var old = await SdkFixtures.Install(e, "0.23.0"); Assert.NotNull(old.Entities); Assert.Null(old.Entities!.Attachments);
        var w = e.Workspace(); await w.CreateAsync(new("Pinned", "Tests", "mods/tests/pinned_0230", "0.1.0"), old);
        var armor = old.Entities.Vehicles.FieldInstances.Single(f => f.Target.Vehicle == "TD-220 Bastion MK XVI" && f.SemanticFieldId == "entity.armor");
        await w.SetEntityAsync(armor.InstanceKey, "5");
        await SdkFixtures.Install(e, "0.24.0");
        await w.OpenAsync(w.Project!.Id); Assert.Equal("0.23.0", w.Project!.SdkVersion); Assert.Null(w.Metadata!.Entities!.Attachments);
        await w.RebindToInstalledSdkAsync(); Assert.Equal("0.24.0", w.Project.SdkVersion); Assert.NotNull(w.Metadata!.Entities!.Attachments);
        Assert.Equal(armor.InstanceKey, w.Project.EntityChanges.Single().InstanceKey); Assert.Null(w.BuildError);
        Assert.Null((await SdkFixtures.Install(e, "0.22.1")).Entities);
    }
    // 0.23.2 is a Runtime hotfix: consumed metadata differs from 0.23.1 only in its version line.
    [Fact] public async Task Hotfix_0232_rebinds_0231_attachment_edits_without_review()
    {
        using var e = new TestEnvironment(); var old = await SdkFixtures.Install(e, "0.23.1");
        var w = e.Workspace(); await w.CreateAsync(new("Pinned", "Tests", "mods/tests/pinned_0231", "0.1.0"), old);
        await w.SetEntityAsync(Field(w, Drum, "attachment.magazine_capacity").InstanceKey, "90"); await w.SetAttachmentAcknowledgedAsync(Drum, true);
        var (keys, lua) = (w.Project!.EntityChanges.Select(c => c.InstanceKey).ToArray(), w.LuaPreview);
        await SdkFixtures.Install(e, "0.23.2"); await w.OpenAsync(w.Project.Id); Assert.Equal("0.23.1", w.Project!.SdkVersion);
        await w.RebindToInstalledSdkAsync(); Assert.Equal("0.23.2", w.Project.SdkVersion); Assert.Null(w.BuildError);
        Assert.Equal(keys, w.Project.EntityChanges.Select(c => c.InstanceKey)); Assert.True(BuilderWorkspace.AttachmentAcknowledged(w.Project, Catalog(w), Drum));
        Assert.Equal(lua.Replace("0.23.1", "0.23.2"), w.LuaPreview);
    }

    [Theory] [InlineData("ambiguous-resolved")] [InlineData("unknown-attachment")] [InlineData("no-effect-ack")] [InlineData("value-mismatch")]
    [InlineData("summary")] [InlineData("unknown-weapon")] [InlineData("default-not-native")] [InlineData("scope-complete")]
    public void Malformed_magazine_metadata_fails_closed(string fault)
    {
        var j = Json(); var concussive = j["weapons"]!.AsArray().First(x => (string?)x!["weapon"] == Concussive)!;
        var shortMag = concussive["magazineSlot"]!["options"]!.AsArray().First(o => (string?)o!["name"] == "Short Magazine")!;
        var field = j["fieldInstances"]![0]!;
        switch (fault)
        {
            case "ambiguous-resolved": shortMag["attachment"] = (string?)shortMag["candidates"]![0]; break;
            case "unknown-attachment": concussive["magazineSlot"]!["defaultAttachment"] = "weapon-attachment/v1/magazine/made-up/0000000000000000"; break;
            case "no-effect-ack": field["acknowledgement"] = null; break;
            case "value-mismatch": field["currentDefault"] = 999; break;
            case "summary": j["summary"]!["placeholderBaseCapacityWeapons"] = 13; break;
            case "unknown-weapon": concussive["weapon"] = "AR-99 Invented"; break;
            case "default-not-native": concussive["magazineSlot"]!["defaultRelationship"] = "catalog_effect_fingerprint_unique"; break;
            case "scope-complete": j["attachments"]![0]!["consumers"]!["scopeComplete"] = true; break;
        }
        var players = new PlayerWeaponCatalogReader().Read(SdkFixtures.Entry("0.24.0", PlayerWeaponCatalogReader.FileName), "0.24.0");
        var ex = Record.Exception(() => MagazineAttachmentReader.Read(Encoding.UTF8.GetBytes(j.ToJsonString()), "0.24.0", players));
        Assert.True(ex is InvalidDataException or UnsupportedSdkException, ex?.GetType().Name);
    }
    [Fact] public void Unknown_contract_is_unsupported()
    {
        var j = Json(); j["contract"] = "hd2runtime.weapon_attachment.magazine.v2";
        var players = new PlayerWeaponCatalogReader().Read(SdkFixtures.Entry("0.24.0", PlayerWeaponCatalogReader.FileName), "0.24.0");
        Assert.Throws<UnsupportedSdkException>(() => MagazineAttachmentReader.Read(Encoding.UTF8.GetBytes(j.ToJsonString()), "0.24.0", players));
    }
    [Fact] public void Published_catalog_reads_cleanly()
    {
        var players = new PlayerWeaponCatalogReader().Read(SdkFixtures.Entry("0.24.0", PlayerWeaponCatalogReader.FileName), "0.24.0");
        Assert.Equal(43, MagazineAttachmentReader.Read(SdkFixtures.Entry("0.24.0", MagazineAttachmentReader.FileName), "0.24.0", players).Attachments.Length);
    }
}
