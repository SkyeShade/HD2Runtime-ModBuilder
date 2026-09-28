using HD2RuntimeGUI.Core.GameAssets;
using HD2RuntimeGUI.Core.Metadata;
using HD2RuntimeGUI.Core.Services;
using Xunit;

namespace HD2RuntimeGUI.Tests;

// HD2Runtime ModBuilder: Vehicles and Backpacks live under Stratagems → Support (by SDK family and published call-in links),
// and the visible branding uses one set of assets and theme tokens.
public sealed class ModBuilderIaTests
{
    private static async Task<SdkMetadata> Current(TestEnvironment e) { File.Delete(e.Paths.CachePath("current.json")); return await e.Cache.GetCurrentAsync(); }
    private static string Root()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
            if (Directory.Exists(Path.Combine(dir.FullName, "HD2RuntimeGUI", "wwwroot"))) return dir.FullName;
        throw new DirectoryNotFoundException("Repository root not found.");
    }

    [Fact] public async Task Support_holds_weapons_vehicles_backpacks_and_standalone_items_once()
    {
        using var e = new TestEnvironment(); var sdk = await Current(e); var c = sdk.Stratagems!; var entities = sdk.Entities!;
        Assert.Equal("0.25.1", sdk.Version);
        var support = c.Stratagems.Where(s => StratagemCategories.Support.Families.Contains(s.Family) && StratagemCategories.Listed(s, entities)).ToArray();
        Assert.Equal(55, support.Length);
        Assert.Equal(33, support.Count(s => s.Family == "support")); Assert.Equal(9, support.Count(s => s.Family == "vehicle")); Assert.Equal(13, support.Count(s => s.Family == "backpack"));
        // Membership is published: every vehicle/backpack call-in root is linked by Runtime to the entity of its own family.
        Assert.Equal(22, entities.CallIns.Count);
        Assert.All(entities.CallIns, p => Assert.Equal(p.Value.Resource, c.Root(p.Key)!.Family));
        Assert.All(support.Where(s => s.Family is "vehicle" or "backpack"), s => Assert.True(entities.CallIns.ContainsKey(s.Name)));
        // Other / Standalone: native-only vehicles and no-call-in equipment; nothing is listed twice.
        Assert.Equal([("vehicle", "FRV (Super Earth variant)"), ("vehicle", "GATER Oil Rig")], StratagemCategories.EntitiesWithoutCallIn(entities).OrderBy(x => x.Name, StringComparer.Ordinal));
        Assert.Equal(2, SupportEquipment.Standalone(sdk).Count); Assert.Empty(SupportEquipment.Unlinked(sdk));
        Assert.Empty(StratagemCategories.EntitiesWithoutCallIn(entities).Select(x => x.Name).Intersect(support.Select(s => s.Name)));
        // Their real stratagem icons come from the published uiIcon of the call-in root.
        Assert.Equal(7, support.Count(s => s.Family == "vehicle" && GameIconStore.StratagemIconKey(s) != null));
        Assert.Equal(10, support.Count(s => s.Family == "backpack" && GameIconStore.StratagemIconKey(s) != null));
        // Sidebar: no separate Vehicles / Backpacks destinations.
        Assert.DoesNotContain(Navigation.Build(sdk, null), i => i.Page is "vehicles" or "backpacks");
        Assert.Equal(["Support Weapons", "Vehicles", "Backpacks"], StratagemBrowser.Tabs(c).Where(t => t.Family is "support" or "vehicle" or "backpack").Select(t => t.Label));
    }

    [Fact] public void Branding_uses_the_modbuilder_assets_and_central_tokens()
    {
        var root = Path.Combine(Root(), "HD2RuntimeGUI");
        var icon = File.ReadAllText(Path.Combine(root, "Resources", "AppIcon", "appicon.svg"));
        Assert.Contains("fill=\"#FDD00E\"", icon); Assert.Contains("M174 190L240 250L174 310", icon); Assert.Contains("rx=\"112\"", icon);
        Assert.Equal(icon, File.ReadAllText(Path.Combine(root, "wwwroot", "brand", "modbuilder-icon.svg")));
        Assert.Contains("<title>HD2Runtime ModBuilder</title>", File.ReadAllText(Path.Combine(root, "wwwroot", "index.html")));
        Assert.Contains("Title = \"HD2Runtime ModBuilder\"", File.ReadAllText(Path.Combine(root, "App.xaml.cs")));
        Assert.True(File.Exists(Path.Combine(root, "wwwroot", "brand", "fonts", "BigShouldersDisplay-latin.woff2")));
        Assert.Contains("SIL Open Font License", File.ReadAllText(Path.Combine(root, "wwwroot", "brand", "fonts", "BigShouldersDisplay-OFL.txt")));
        var mark = File.ReadAllText(Path.Combine(root, "Components", "BrandMark.razor"));
        Assert.Contains("var(--brand-yellow)", mark); Assert.DoesNotContain("#FDD00E", mark, StringComparison.OrdinalIgnoreCase);
        // Storage and identity are unchanged: only visible branding moved.
        Assert.Contains("\"HD2RuntimeGUI\")", File.ReadAllText(Path.Combine(root, "MauiProgram.cs")));
        Assert.Contains("<ApplicationId>dev.skyeshade.hd2runtimegui</ApplicationId>", File.ReadAllText(Path.Combine(root, "HD2RuntimeGUI.csproj")));
    }

    [Fact] public void No_game_artwork_is_part_of_the_repository()
    {
        var root = Root();
        var images = Directory.GetFiles(Path.Combine(root, "HD2RuntimeGUI"), "*.*", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}") && !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
            .Where(f => Path.GetExtension(f) is ".svg" or ".png" or ".ico").Select(f => Path.GetRelativePath(root, f).Replace('\\', '/')).Order(StringComparer.Ordinal).ToArray();
        Assert.All(images, f => Assert.True(f.StartsWith("HD2RuntimeGUI/Resources/", StringComparison.Ordinal) || f.StartsWith("HD2RuntimeGUI/wwwroot/brand/", StringComparison.Ordinal), f));
    }
}
