using HD2RuntimeGUI.Core.Metadata;

namespace HD2RuntimeGUI.Core.Services;

// Stratagem categories of the public authoring model. Membership comes from published SDK families;
// a family this build does not know is still browsable under "All", never forced into a category.
public sealed record StratagemCategory(string Key, string Label, string CssClass, string[] Families);
public static class StratagemCategories
{
    // Vehicles and backpacks are support stratagems. When Runtime publishes their call-in link they are edited in the
    // Vehicles / Backpacks areas together with the call-in, so the Stratagems list does not repeat them.
    public static readonly StratagemCategory Support = new("support", "Support", "cat-support", ["support", "vehicle", "backpack"]);
    public static readonly StratagemCategory Offensive = new("offensive", "Offensive", "cat-offensive", ["orbital", "eagle"]);
    public static readonly StratagemCategory Defensive = new("defensive", "Defensive", "cat-defensive", ["sentry", "emplacement", "mine"]);
    public static readonly IReadOnlyList<StratagemCategory> All = [Support, Offensive, Defensive];
    public static StratagemCategory? Of(string family) => All.FirstOrDefault(c => c.Families.Contains(family));
    public static StratagemCategory? Find(string key) => All.FirstOrDefault(c => c.Key == key);
    // Boosters (SDK 0.24.0+) are their own authoring domain, not a stratagem family; yellow alongside the stratagem categories.
    public const string BoosterCssClass = "cat-booster";
    // A root is listed under Stratagems unless it is a call-in whose vehicle/backpack editor already includes it.
    // 0.25.0+: a catalog root Runtime proves has no call-in (no_call_in) is not a stratagem; its equipment is listed as standalone.
    public static bool Listed(StratagemDefinition s, EntityAuthoring? entities) => entities?.CallIns.ContainsKey(s.Name) != true && s.Delivers?.IsNoCallIn != true;
    public static int Count(StratagemCatalog catalog, StratagemCategory category, EntityAuthoring? entities = null)
        => catalog.Stratagems.Count(s => category.Families.Contains(s.Family) && Listed(s, entities));
}

// Sidebar model. Only public authoring destinations appear; legacy mapped-resource categories
// (legacy vehicles, equipment, legacy stratagems) are deliberately absent. Destinations stay visible without a project.
public sealed record NavItem(string Page, string Label, string Section, int? Count = null, string? CssClass = null, bool Child = false,
    bool RequiresProject = true);
public static class Navigation
{
    public const string Workspace = "WORKSPACE", Research = "DEVELOPER / RESEARCH";
    public static IReadOnlyList<NavItem> Build(SdkMetadata? sdk, Models.ModProject? project, int? modifications = null)
    {
        var entities = sdk?.Entities;
        var items = new List<NavItem>
        {
            new("overview", "Overview", Workspace),
            new("player-weapons", "Player Weapons", Workspace, sdk?.PlayerWeapons?.Weapons.Count),
            new("stratagems", "Stratagems", Workspace, sdk?.Stratagems?.Stratagems.Count(s => StratagemCategories.Listed(s, entities))),
        };
        if (sdk?.Stratagems is { } catalog)
            items.AddRange(StratagemCategories.All.Select(c => new NavItem("stratagems:" + c.Key, c.Label, Workspace, StratagemCategories.Count(catalog, c, entities), c.CssClass, true)));
        // hd2.vehicle / hd2.backpack authoring (SDK 0.23.0+). Linked call-in cooldowns are edited inside these editors.
        if (entities != null)
        {
            items.Add(new("vehicles", "Vehicles", Workspace, entities.Vehicles.Vehicles.Length, StratagemCategories.Support.CssClass));
            items.Add(new("backpacks", "Backpacks", Workspace, entities.Backpacks.Backpacks.Length, StratagemCategories.Support.CssClass));
        }
        // Support equipment lives in Stratagems → Support: linked weapons inside their call-in, unlinked ones in its
        // "Unlinked support equipment" group. Only SDKs without a stratagem catalog (0.17–0.20.x) keep a separate destination.
        if (sdk?.Stratagems == null && sdk?.Advanced?.Support is { } support)
            items.Add(new("support", "Support equipment", Workspace, support.Weapons.Count, StratagemCategories.Support.CssClass));
        // hd2.booster authoring appears only when the bound SDK publishes BoosterAuthoringCapabilities.json (0.24.0+).
        if (entities?.Boosters is { } boosters) items.Add(new("boosters", "Boosters", Workspace, boosters.Boosters.Length, StratagemCategories.BoosterCssClass));
        items.Add(new("changes", "Changes", Workspace, project == null ? null : modifications));
        items.Add(new("lua", "Lua Preview", Workspace));
        items.Add(new("export", "Build / Export", Workspace));
        items.Add(new("research", "Snapshot Research", Research, RequiresProject: false));
        return items;
    }
}
