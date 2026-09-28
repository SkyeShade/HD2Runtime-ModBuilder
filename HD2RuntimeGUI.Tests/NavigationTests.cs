using HD2RuntimeGUI.Core.Metadata;
using HD2RuntimeGUI.Core.Services;
using Xunit;

namespace HD2RuntimeGUI.Tests;

public sealed class NavigationTests
{
    private static async Task<SdkMetadata> Current(TestEnvironment e) { File.Delete(e.Paths.CachePath("current.json")); return await e.Cache.GetCurrentAsync(); }

    [Fact] public async Task Sidebar_lists_only_public_authoring_destinations()
    {
        using var e = new TestEnvironment(); var sdk = await Current(e);
        var nav = Navigation.Build(sdk, null);
        Assert.Equal(["overview", "player-weapons", "stratagems", "stratagems:support", "stratagems:offensive", "stratagems:defensive", "support", "changes", "lua", "export", "research"],
            nav.Select(i => i.Page));
        var labels = nav.Select(i => i.Label).ToArray();
        foreach (var hidden in new[] { "Vehicles", "Legacy mapped stratagems", "Equipment", "Weapons" }) Assert.DoesNotContain(hidden, labels);
        Assert.DoesNotContain(nav, i => sdk.Builders.ContainsKey(i.Page));
        Assert.Equal(Navigation.Research, nav.Single(i => i.Page == "research").Section);
        Assert.All(nav.Where(i => i.Page != "research"), i => Assert.Equal(Navigation.Workspace, i.Section));
    }
    [Fact] public async Task Navigation_is_complete_without_a_project_and_uses_neutral_counts()
    {
        using var e = new TestEnvironment(); var sdk = await Current(e);
        var none = Navigation.Build(sdk, null, 5); var legacyOnly = Navigation.Build(null, null);
        Assert.Contains(none, i => i.Page == "player-weapons" && i.Count == 80);
        Assert.Contains(none, i => i.Page == "stratagems" && i.Count == 73);
        Assert.Null(none.Single(i => i.Page == "changes").Count);
        Assert.Contains(legacyOnly, i => i.Page == "player-weapons"); Assert.Contains(legacyOnly, i => i.Page == "stratagems");
        var w = e.Workspace(); await w.CreateAsync(new("Nav", "Tests", "mods/tests/nav", "0.1.0"), sdk);
        Assert.Equal(5, Navigation.Build(sdk, w.Project, 5).Single(i => i.Page == "changes").Count);
        Assert.Equal(none.Select(i => i.Page), Navigation.Build(sdk, w.Project, 5).Select(i => i.Page));
    }
    [Theory] [InlineData("support", "cat-support", 35)] [InlineData("offensive", "cat-offensive", 20)] [InlineData("defensive", "cat-defensive", 18)]
    public async Task Categories_count_published_families_and_use_semantic_classes(string key, string css, int count)
    {
        using var e = new TestEnvironment(); var sdk = await Current(e); var c = StratagemCategories.Find(key)!;
        Assert.Equal(css, c.CssClass); Assert.Equal(count, StratagemCategories.Count(sdk.Stratagems!, c));
        Assert.Contains(Navigation.Build(sdk, null), i => i.Page == "stratagems:" + key && i.Count == count && i.CssClass == css && i.Child);
    }
    [Fact] public async Task Every_published_stratagem_belongs_to_exactly_one_category()
    {
        using var e = new TestEnvironment(); var c = (await Current(e)).Stratagems!;
        Assert.All(c.Stratagems, s => Assert.Single(StratagemCategories.All, x => x.Families.Contains(s.Family)));
        Assert.Contains(c.Stratagems, s => s.Family == "orbital" && StratagemCategories.Of(s.Family) == StratagemCategories.Offensive);
        Assert.Contains(c.Stratagems, s => s.Family == "eagle" && StratagemCategories.Of(s.Family) == StratagemCategories.Offensive);
        foreach (var name in new[] { "A/MG-43 Machine Gun Sentry", "E/AT-12 Anti-Tank Emplacement", "MD-6 Anti-Personnel Minefield", "FX-12 Shield Generator Relay" })
            Assert.Equal(StratagemCategories.Defensive, StratagemCategories.Of(c.Root(name)!.Family));
        Assert.Equal(StratagemCategories.Support, StratagemCategories.Of(c.Root("GR-8 Recoilless Rifle")!.Family));
        Assert.Null(StratagemCategories.Of("vehicle"));
    }
    // The merged support editor is blocked: Runtime 0.22 publishes no support weapon ↔ call-in identity.
    // This test documents the published state; if Runtime starts publishing a link, it fails and the merge can be revisited.
    [Fact] public async Task Runtime_does_not_publish_support_weapon_call_in_identity()
    {
        using var e = new TestEnvironment(); var sdk = await Current(e);
        var weapons = sdk.SupportAuthoring!.Weapons;
        Assert.Equal(35, weapons.Length); Assert.All(weapons, w => Assert.NotNull(w.LinkedStratagem));
        Assert.Equal(34, weapons.Count(w => !w.LinkedStratagem!.Known));
        var silo = weapons.Single(w => w.LinkedStratagem!.Known); Assert.Equal("MS-11 Solo Silo", silo.Name); Assert.Equal("support_weapon_delivery", silo.LinkedStratagem!.Kind);
        var json = System.Text.Json.Nodes.JsonNode.Parse(SdkCache.BundledComposition()[SupportAuthoringReader.FileName])!;
        Assert.All(json["weapons"]!.AsArray(), w => Assert.Equal(w!["linkedStratagem"]!["known"]!.GetValue<bool>() ? 2 : 1, w["linkedStratagem"]!.AsObject().Count));
        var support = sdk.Stratagems!.Stratagems.Where(s => s.Family == "support").ToArray();
        Assert.All(support, s => { Assert.Empty(s.AttackRoles); Assert.Null(s.DeployedEntity); });
        Assert.DoesNotContain(sdk.Stratagems.FieldInstances, f => support.Any(s => s.Name == f.Target.Stratagem) && f.Target.Path != "stratagem");
    }
}
