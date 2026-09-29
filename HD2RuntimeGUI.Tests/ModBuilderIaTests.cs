using HD2RuntimeGUI.Core;
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
        Assert.Equal("0.27.0", sdk.Version);
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

    [Fact] public void Executable_and_release_metadata_use_the_public_product_name()
    {
        var root = Root(); var csproj = File.ReadAllText(Path.Combine(root, "HD2RuntimeGUI", "HD2RuntimeGUI.csproj"));
        Assert.Equal("HD2Runtime ModBuilder", BuildInfo.ProductName);
        foreach (var property in new[] { "ApplicationTitle", "Product", "AssemblyTitle" }) Assert.Contains($"<{property}>HD2Runtime ModBuilder</{property}>", csproj);
        Assert.Contains("<AssemblyName>HD2RuntimeModBuilder</AssemblyName>", csproj); // HD2RuntimeModBuilder.exe since 1.0.1
        Assert.Contains("<AssemblyName>HD2RuntimeModBuilder.Core</AssemblyName>", File.ReadAllText(Path.Combine(root, "HD2RuntimeGUI.Core", "HD2RuntimeGUI.Core.csproj")));
        Assert.Equal("HD2RuntimeModBuilder.Core", typeof(BuildInfo).Assembly.GetName().Name);
        // Renaming the assembly keeps the embedded SDK resource names (root namespace HD2RuntimeGUI.Core).
        Assert.Contains("HD2RuntimeGUI.Core.Metadata.Bundled.metadata.json", typeof(BuildInfo).Assembly.GetManifestResourceNames());
        Assert.Contains("href=\"HD2RuntimeModBuilder.styles.css\"", File.ReadAllText(Path.Combine(root, "HD2RuntimeGUI", "wwwroot", "index.html")));
        Assert.StartsWith("HD2Runtime-ModBuilder/", BuildInfo.UserAgent);
        var publish = File.ReadAllText(Path.Combine(root, "scripts", "publish-windows.ps1"));
        Assert.Contains("HD2Runtime-ModBuilder-v$version-win-x64", publish); Assert.Contains("modbuilder-update.json", publish); Assert.Contains("resizetizer", publish);
        Assert.Contains("$product = 'HD2Runtime ModBuilder'", publish); Assert.Contains("ProductName -ne $product", publish); Assert.Contains("git status --porcelain", publish);
        Assert.Contains("HD2RuntimeModBuilder.Updater.exe", publish); Assert.Contains("Assert-ReticleIcon", publish);
    }

    [Fact] public void Release_uses_the_renamed_repository_and_documents_itself()
    {
        var root = Root();
        Assert.Contains("<Hd2RuntimeGuiVersion>1.3.0</Hd2RuntimeGuiVersion>", File.ReadAllText(Path.Combine(root, "Directory.Build.props")));
        Assert.Equal("1.3.0", BuildInfo.Version);
        Assert.Equal("https://github.com/SkyeShade/HD2Runtime-ModBuilder", BuildInfo.RepositoryUrl);
        Assert.Equal("HD2Runtime-ModBuilder/1.3.0", BuildInfo.UserAgent);
        var readme = File.ReadAllText(Path.Combine(root, "README.md"));
        Assert.Contains("# HD2Runtime ModBuilder\n\n**Visual authoring for [HD2Runtime]", readme.ReplaceLineEndings("\n"));
        Assert.Contains("git clone https://github.com/SkyeShade/HD2Runtime-ModBuilder.git", readme);
        Assert.Contains("HD2Runtime-ModBuilder-v1.3.0-win-x64.zip", readme); Assert.Contains("HD2RuntimeModBuilder.exe", readme);
        Assert.True(readme.IndexOf("## For developers", StringComparison.Ordinal) > readme.IndexOf("## Download", StringComparison.Ordinal));
        Assert.True(File.Exists(Path.Combine(root, "docs", "release-notes", "v1.0.0.md"))); Assert.True(File.Exists(Path.Combine(root, "docs", "release-notes", "v1.0.1.md"))); Assert.True(File.Exists(Path.Combine(root, "docs", "release-notes", "v1.1.0.md"))); Assert.True(File.Exists(Path.Combine(root, "docs", "release-notes", "v1.1.1.md"))); Assert.True(File.Exists(Path.Combine(root, "docs", "release-notes", "v1.1.2.md"))); Assert.True(File.Exists(Path.Combine(root, "docs", "release-notes", "v1.2.0.md"))); Assert.True(File.Exists(Path.Combine(root, "docs", "release-notes", "v1.3.0.md")));
        Assert.Contains("modbuilder-update-v2.json", File.ReadAllText(Path.Combine(root, "docs", "app-updates.md")));
        // Active (non-historical) sources never point at the old repository.
        var active = Directory.GetFiles(root, "*.*", SearchOption.AllDirectories)
            .Where(f => Path.GetExtension(f) is ".cs" or ".razor" or ".md" or ".ps1" or ".props" or ".csproj" or ".json" or ".html")
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}") && !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                && !f.Contains($"{Path.DirectorySeparatorChar}artifacts{Path.DirectorySeparatorChar}") && !Path.GetFileName(f).StartsWith("runtime0", StringComparison.Ordinal) && !f.EndsWith("verification.md", StringComparison.Ordinal));
        var oldRepository = "SkyeShade/" + "HD2RuntimeGUI"; // split so this test does not match itself
        Assert.All(active, f => Assert.DoesNotContain(oldRepository, File.ReadAllText(f)));
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
