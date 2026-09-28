using HD2RuntimeGUI.Core.Metadata;

namespace HD2RuntimeGUI.Core.Services;

// Stratagem categories of the public authoring model. Membership comes from published SDK families;
// a family this build does not know is still browsable under "All", never forced into a category.
public sealed record StratagemCategory(string Key, string Label, string CssClass, string[] Families);
public static class StratagemCategories
{
    public static readonly StratagemCategory Support = new("support", "Support", "cat-support", ["support"]);
    public static readonly StratagemCategory Offensive = new("offensive", "Offensive", "cat-offensive", ["orbital", "eagle"]);
    public static readonly StratagemCategory Defensive = new("defensive", "Defensive", "cat-defensive", ["sentry", "emplacement", "mine"]);
    public static readonly IReadOnlyList<StratagemCategory> All = [Support, Offensive, Defensive];
    public static StratagemCategory? Of(string family) => All.FirstOrDefault(c => c.Families.Contains(family));
    public static StratagemCategory? Find(string key) => All.FirstOrDefault(c => c.Key == key);
    public static int Count(StratagemCatalog catalog, StratagemCategory category) => catalog.Stratagems.Count(s => category.Families.Contains(s.Family));
}

// Sidebar model. Only public authoring destinations appear; legacy mapped-resource categories
// (vehicles, equipment, legacy stratagems) are deliberately absent. Destinations stay visible without a project.
public sealed record NavItem(string Page, string Label, string Section, int? Count = null, string? CssClass = null, bool Child = false,
    bool RequiresProject = true);
public static class Navigation
{
    public const string Workspace = "WORKSPACE", Research = "DEVELOPER / RESEARCH";
    public static IReadOnlyList<NavItem> Build(SdkMetadata? sdk, Models.ModProject? project, int? modifications = null)
    {
        var items = new List<NavItem>
        {
            new("overview", "Overview", Workspace),
            new("player-weapons", "Player Weapons", Workspace, sdk?.PlayerWeapons?.Weapons.Count),
            new("stratagems", "Stratagems", Workspace, sdk?.Stratagems?.Stratagems.Length),
        };
        if (sdk?.Stratagems is { } catalog)
            items.AddRange(StratagemCategories.All.Select(c => new NavItem("stratagems:" + c.Key, c.Label, Workspace, StratagemCategories.Count(catalog, c), c.CssClass, true)));
        // Weapons with a published call-in link are edited inside their support stratagem; this destination lists the rest.
        // Without linkage metadata (SDK 0.22.0 and older) every support weapon keeps its standalone editor.
        if (sdk?.SupportAuthoring is { } support)
            items.Add(new("support", "Support Weapons", Workspace, support.Weapons.Length - (sdk.SupportLinks?.ByWeapon.Count ?? 0), StratagemCategories.Support.CssClass));
        items.Add(new("changes", "Changes", Workspace, project == null ? null : modifications));
        items.Add(new("lua", "Lua Preview", Workspace));
        items.Add(new("export", "Build / Export", Workspace));
        items.Add(new("research", "Snapshot Research", Research, RequiresProject: false));
        return items;
    }
}
