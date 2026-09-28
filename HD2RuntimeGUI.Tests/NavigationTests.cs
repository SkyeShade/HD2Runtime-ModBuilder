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
        Assert.Equal(["overview", "player-weapons", "stratagems", "stratagems:support", "stratagems:offensive", "stratagems:defensive", "vehicles", "backpacks", "support", "changes", "lua", "export", "research"],
            nav.Select(i => i.Page));
        var labels = nav.Select(i => i.Label).ToArray();
        // "Vehicles" is now the hd2.vehicle destination (page "vehicles"), never the legacy mapped "vehicle" builder category.
        foreach (var hidden in new[] { "Legacy mapped stratagems", "Equipment", "Weapons" }) Assert.DoesNotContain(hidden, labels);
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
        Assert.Equal(css, c.CssClass); Assert.Equal(count, StratagemCategories.Count(sdk.Stratagems!, c, sdk.Entities));
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
        Assert.Equal(StratagemCategories.Support, StratagemCategories.Of("vehicle")); Assert.Equal(StratagemCategories.Support, StratagemCategories.Of("backpack"));
        Assert.Null(StratagemCategories.Of("unmapped_family"));
    }
}
