using System.Text;
using System.Text.Json.Nodes;
using HD2RuntimeGUI.Core.Generation;
using HD2RuntimeGUI.Core.Metadata;
using HD2RuntimeGUI.Core.Services;
using Xunit;

namespace HD2RuntimeGUI.Tests;

// Runtime 0.22.1 support weapon ↔ call-in linkage (structural, semantic-ID based).
public sealed class SupportLinkageTests
{
    private static async Task<BuilderWorkspace> Workspace(TestEnvironment e)
    {
        var sdk = await SdkFixtures.Install(e, "0.22.1");
        var w = e.Workspace(); await w.CreateAsync(new("Support", "Tests", "mods/tests/support_link", "0.1.0"), sdk); return w;
    }
    private static JsonNode Json(string file) => JsonNode.Parse(SdkFixtures.Entry("0.22.1", file))!;
    private static SupportCallInIndex? Link(JsonNode stratagems, JsonNode support) => SupportCallInLinker.Link(
        new StratagemCatalogReader().Read(Encoding.UTF8.GetBytes(stratagems.ToJsonString())),
        new SupportAuthoringReader().Read(Encoding.UTF8.GetBytes(support.ToJsonString()), "0.22.1"));

    [Fact] public async Task Published_linkage_joins_32_pairs_by_semantic_identity()
    {
        using var e = new TestEnvironment(); var w = await Workspace(e); var links = w.Metadata!.SupportLinks!;
        Assert.Equal("0.22.1", w.Metadata.Version); Assert.Equal(32, links.ByStratagem.Count); Assert.Equal(32, links.ByWeapon.Count);
        foreach (var link in links.ByWeapon.Values)
        {
            Assert.Equal(link.Weapon.LinkedStratagem!.SemanticId, link.Stratagem.SemanticId);
            Assert.Equal(link.Stratagem.Delivers!.SemanticId, link.Weapon.SemanticId);
            Assert.Equal(link.Relationship.RelationshipId, link.Weapon.LinkedStratagem.RelationshipId);
            Assert.Equal("support", link.Stratagem.Family); Assert.Equal("reference", link.Relationship.DeliveryGraph.Composition);
        }
        foreach (var name in new[] { "B/MD C4 Pack", "SG-88 Break-Action Shotgun", "CQC-72 Entrenchment Tool" })
        {
            Assert.Null(links.ForWeapon(name)); Assert.Null(links.ForStratagem(name));
            Assert.False(w.Metadata.SupportAuthoring!.Weapons.Single(x => x.Name == name).LinkedStratagem!.Known);
            Assert.False(string.IsNullOrWhiteSpace(w.Metadata.Stratagems!.Root(name)!.Delivers!.Blocker));
        }
        // Unlinked equipment is listed inside Stratagems → Support; there is no separate Support Weapons destination.
        Assert.DoesNotContain(Navigation.Build(w.Metadata, null), i => i.Page == "support"); Assert.Equal(3, SupportEquipment.Unlinked(w.Metadata).Count);
    }
    [Fact] public async Task Linked_weapons_keep_their_write_guards()
    {
        using var e = new TestEnvironment(); var w = await Workspace(e); var links = w.Metadata!.SupportLinks!;
        var ambiguous = links.ByWeapon.Values.Where(l => l.Weapon.IdentityStatus != "UNIQUE").Select(l => l.Weapon.Name).Order().ToArray();
        Assert.Equal(["B/FLAM-80 Cremator", "CQC-20 Breaching Hammer", "EAT-17 Expendable Anti-Tank", "LAS-98 Laser Cannon", "M-105 Stalwart", "MG-206 Heavy Machine Gun", "MG-43 Machine Gun"], ambiguous);
        foreach (var name in ambiguous)
        {
            Assert.False(links.ForWeapon(name)!.Weapon.Writable);
            Assert.DoesNotContain(w.Metadata.SupportAuthoring!.FieldInstances, f => f.SupportWeapon == name);
            Assert.True(w.Metadata.Stratagems!.FieldInstances.Single(f => f.Target.Stratagem == name && f.SemanticFieldId == "stratagem.cooldown").Editable);
        }
    }
    [Fact] public async Task Solo_Silo_delivery_graph_links_call_in_silo_missile_and_both_explosions()
    {
        using var e = new TestEnvironment(); var w = await Workspace(e); var silo = w.Metadata!.SupportLinks!.ForStratagem("MS-11 Solo Silo")!;
        Assert.Equal("deployable_silo", silo.Relationship.Special); var nodes = silo.Relationship.DeliveryGraph.Nodes;
        Assert.Equal(["call_in", "delivery", "attack_entity"], nodes.Take(3).Select(n => n.Node));
        Assert.Equal("missile", nodes.Single(n => n.Node == "attack_entity").Object);
        var explosions = nodes.Where(n => n.Parent == "attack_entity").ToArray();
        Assert.Equal(["detonation", "impact"], explosions.Select(n => n.AttackRole).Order());
        Assert.All(explosions, n => { Assert.Equal("explosion", n.TargetPath); Assert.Equal("RESOLVED", n.State); });
        Assert.Contains(w.Metadata.SupportAuthoring!.FieldInstances, f => f.SupportWeapon == silo.Weapon.Name && f.Target.AttackRole == "detonation");
        Assert.Contains(w.Metadata.SupportAuthoring.FieldInstances, f => f.SupportWeapon == silo.Weapon.Name && f.Target.AttackRole == "impact");
    }
    [Fact] public async Task Merged_entry_keeps_one_edit_state_per_target_and_emits_both_runtime_apis()
    {
        using var e = new TestEnvironment(); var w = await Workspace(e); var link = w.Metadata!.SupportLinks!.ForStratagem("GR-8 Recoilless Rifle")!;
        var cooldown = w.Metadata.Stratagems!.FieldInstances.Single(f => f.Target.Stratagem == link.Stratagem.Name && f.SemanticFieldId == "stratagem.cooldown");
        var sway = w.Metadata.SupportAuthoring!.FieldInstances.Single(f => f.SupportWeapon == link.Weapon.Name && f.SemanticFieldId == "weapon.sway");
        await w.SetStratagemAsync(cooldown.InstanceKey, "300"); await w.SetSupportAsync(sway.InstanceKey, "0.5");
        await w.OpenAsync(w.Project!.Id);
        Assert.Single(w.Project!.StratagemChanges); Assert.Single(w.Project.SupportChanges); Assert.Null(w.BuildError);
        Assert.Contains("hd2.stratagem('GR-8 Recoilless Rifle')", w.LuaPreview); Assert.Contains("hd2.support_weapon('GR-8 Recoilless Rifle')", w.LuaPreview);
        Assert.Contains("field=hd2.fields.stratagem.definition_cooldown", w.LuaPreview); Assert.Contains("value=300", w.LuaPreview);
        Assert.DoesNotContain("support-callin/", w.LuaPreview); Assert.DoesNotContain("semanticId", File.ReadAllText(e.Paths.ProjectFile(w.Project.Id)));
    }
    [Fact] public void Display_names_are_not_link_evidence()
    {
        var s = Json(StratagemCatalogReader.FileName); var sw = Json(SupportAuthoringReader.FileName);
        foreach (var doc in new[] { s, sw })
            foreach (var r in doc["supportCallInLinks"]!["relationships"]!.AsArray()) { r!["stratagemName"] = "renamed"; r["supportWeaponName"] = "renamed"; }
        Assert.Equal(32, Link(s, sw)!.ByWeapon.Count);
    }
    [Theory] [InlineData("forward")] [InlineData("reverse")] [InlineData("one-sided")] [InlineData("collections-differ")] [InlineData("audit")]
    [InlineData("names-required")] [InlineData("graph-foreign-node")] [InlineData("unknown-without-blocker")]
    public void Inconsistent_linkage_fails_closed(string fault)
    {
        var s = Json(StratagemCatalogReader.FileName); var sw = Json(SupportAuthoringReader.FileName);
        var weapon = sw["weapons"]!.AsArray().First(x => (string?)x!["linkedStratagem"]!["state"] == "linked")!;
        var root = s["stratagems"]!.AsArray().First(x => (string?)x!["delivers"]?["semanticId"] == (string?)weapon["semanticId"])!;
        var other = s["stratagems"]!.AsArray().First(x => (string?)x!["family"] == "support" && x != root)!;
        void Both(Action<JsonNode> edit) { edit(s); edit(sw); }
        switch (fault)
        {
            case "forward": weapon["linkedStratagem"]!["semanticId"] = (string?)other["semanticId"]; break;
            case "reverse": root["delivers"]!["relationshipId"] = "support-callin/v1/other/0"; break;
            case "one-sided": Both(d => { var rels = d["supportCallInLinks"]!["relationships"]!.AsArray(); rels.RemoveAt(0); d["supportCallInLinks"]!["audit"]!["relationships"] = rels.Count; }); break;
            case "collections-differ": sw["supportCallInLinks"]!["relationships"]![0]!["confidence"] = "guessed"; break;
            case "audit": Both(d => d["supportCallInLinks"]!["audit"]!["knownLinks"] = 33); break;
            case "names-required": Both(d => d["supportCallInLinks"]!["joinContract"]!["displayNameMatchingRequired"] = true); break;
            case "graph-foreign-node": Both(d => d["supportCallInLinks"]!["relationships"]![0]!["deliveryGraph"]!["nodes"]![1]!["semanticId"] = "support-weapon/v1/other/0"); break;
            case "unknown-without-blocker": sw["weapons"]!.AsArray().First(x => (string?)x!["linkedStratagem"]!["state"] != "linked")!["linkedStratagem"]!["blocker"] = null; break;
        }
        Assert.Throws<InvalidDataException>(() => Link(s, sw));
    }
    [Fact] public void Unknown_linkage_contract_is_unsupported()
    {
        var s = Json(StratagemCatalogReader.FileName); var sw = Json(SupportAuthoringReader.FileName);
        foreach (var d in new[] { s, sw }) d["supportCallInLinks"]!["contract"] = "hd2runtime.support_callin_linkage.v9";
        Assert.Throws<UnsupportedSdkException>(() => Link(s, sw));
    }
    [Fact] public async Task Runtime0220_project_rebinds_to_0221_without_review()
    {
        using var e = new TestEnvironment(); e.GitHub.Archive = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "sdk-0.22.0.zip"));
        var old = await e.Cache.InstallAsync(FakeGitHub.MakeRelease("0.22.0", e.GitHub.Archive)); Assert.Null(old.SupportLinks);
        var w = e.Workspace(); await w.CreateAsync(new("Pinned", "Tests", "mods/tests/pinned_0220", "0.1.0"), old);
        var cooldown = old.Stratagems!.FieldInstances.Single(f => f.Target.Stratagem == "GR-8 Recoilless Rifle" && f.SemanticFieldId == "stratagem.cooldown");
        var sway = old.SupportAuthoring!.FieldInstances.Single(f => f.SupportWeapon == "GR-8 Recoilless Rifle" && f.SemanticFieldId == "weapon.sway");
        await w.SetStratagemAsync(cooldown.InstanceKey, "300"); await w.SetSupportAsync(sway.InstanceKey, "0.5");
        await SdkFixtures.Install(e, "0.22.1"); await w.RebindToInstalledSdkAsync();
        Assert.Equal("0.22.1", w.Project!.SdkVersion); Assert.NotNull(w.Metadata!.SupportLinks); Assert.Null(w.BuildError);
        Assert.Single(w.Project.StratagemChanges); Assert.Single(w.Project.SupportChanges);
        Assert.Null(w.StratagemIssue(w.Project.StratagemChanges.Single()));
    }
}
