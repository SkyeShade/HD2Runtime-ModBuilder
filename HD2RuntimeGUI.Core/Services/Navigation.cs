using HD2RuntimeGUI.Core.Metadata;

namespace HD2RuntimeGUI.Core.Services;

// Stratagem categories of the public authoring model. Membership comes from published SDK families;
// a family this build does not know is still browsable under "All", never forced into a category.
public sealed record StratagemCategory(string Key, string Label, string CssClass, string[] Families);
public static class StratagemCategories
{
    // Vehicles and backpacks are support stratagems (SDK families "vehicle" / "backpack"). They are listed once, under Support,
    // and a call-in Runtime links to a vehicle or backpack opens that vehicle/backpack editor together with its call-in settings.
    public static readonly StratagemCategory Support = new("support", "Support", "cat-support", ["support", "vehicle", "backpack"]);
    public static readonly StratagemCategory Offensive = new("offensive", "Offensive", "cat-offensive", ["orbital", "eagle"]);
    public static readonly StratagemCategory Defensive = new("defensive", "Defensive", "cat-defensive", ["sentry", "emplacement", "mine"]);
    // Mission stratagems (Resupply, family "mission" in 0.28.0 development SDKs); listed only when the SDK publishes one.
    public static readonly StratagemCategory Mission = new("mission", "Mission", "cat-mission", ["mission"]);
    public static readonly IReadOnlyList<StratagemCategory> All = [Support, Offensive, Defensive, Mission];
    public static StratagemCategory? Of(string family) => All.FirstOrDefault(c => c.Families.Contains(family));
    public static StratagemCategory? Find(string key) => All.FirstOrDefault(c => c.Key == key);
    // Boosters (SDK 0.24.0+) are their own authoring domain, not a stratagem family; yellow alongside the stratagem categories.
    public const string BoosterCssClass = "cat-booster";
    public const string ThrowableCssClass = "cat-throwable";
    public const string EnemyCssClass = "cat-enemy", StructureCssClass = "cat-structure";
    // Every call-in root is listed, including vehicle and backpack call-ins (edited through their Support entry).
    // 0.25.0+: a catalog root Runtime proves has no call-in (no_call_in) is not a stratagem; its equipment is listed as standalone.
    public static bool Listed(StratagemDefinition s, EntityAuthoring? entities = null) => s.Delivers?.IsNoCallIn != true;
    // Support subcategories: family tabs plus "Other / Standalone" for items without a call-in stratagem.
    public const string OtherSupport = "other";
    // Vehicles/backpacks the SDK publishes without a call-in link (native-only), listed under Support → Other / Standalone.
    // 0.26.0 weapon-fed backpacks (Maxigun, Cremator, GL-28) are delivered with their support weapon and authored on its page.
    public static IReadOnlyList<(string Resource, string Name)> EntitiesWithoutCallIn(EntityAuthoring? entities) => entities == null ? [] :
        [.. entities.Vehicles.Vehicles.Where(v => entities.CallInFor("vehicle", v.Name) == null).Select(v => ("vehicle", v.Name)),
         .. entities.Backpacks.Backpacks.Where(b => entities.CallInFor("backpack", b.Name) == null && b.Feeds == null).Select(b => ("backpack", b.Name))];
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
        // hd2.throwable authoring (grenades, knife, mines, shield) appears after Player Weapons when the bound SDK publishes
        // ThrowableAuthoringCapabilities.json (0.27.0+).
        if (entities?.Throwables is { } throwables)
            items.Insert(items.FindIndex(i => i.Page == "player-weapons") + 1, new("throwables", "Throwables", Workspace, throwables.Throwables.Length, StratagemCategories.ThrowableCssClass));
        if (sdk?.Stratagems is { } catalog)
            items.AddRange(StratagemCategories.All.Select(c => new NavItem("stratagems:" + c.Key, c.Label, Workspace, StratagemCategories.Count(catalog, c, entities), c.CssClass, true))
                .Where(i => i.Page != "stratagems:" + StratagemCategories.Mission.Key || i.Count > 0));
        // hd2.vehicle / hd2.backpack authoring (SDK 0.23.0+) lives in Stratagems → Support (Vehicles / Backpacks tabs);
        // the old "vehicles" / "backpacks" pages redirect there.
        // Support equipment lives in Stratagems → Support: linked weapons inside their call-in, unlinked ones in its
        // "Unlinked support equipment" group. Only SDKs without a stratagem catalog (0.17–0.20.x) keep a separate destination.
        if (sdk?.Stratagems == null && sdk?.Advanced?.Support is { } support)
            items.Add(new("support", "Support equipment", Workspace, support.Weapons.Count, StratagemCategories.Support.CssClass));
        // hd2.booster authoring appears only when the bound SDK publishes BoosterAuthoringCapabilities.json (0.24.0+).
        if (entities?.Boosters is { } boosters) items.Add(new("boosters", "Boosters", Workspace, boosters.Boosters.Length, StratagemCategories.BoosterCssClass));
        // hd2.enemy / hd2.structure authoring appears when the bound SDK publishes EnemyAuthoringCapabilities.json (unreleased Runtime 0.28.0
        // development SDKs today). Structures are their own destination, as Runtime publishes them with their own accessor.
        if (entities?.Enemies is { } enemies)
        {
            items.Add(new("enemies", "Enemies", Workspace, enemies.Of(EnemyAuthoringReader.Enemy).Count(), StratagemCategories.EnemyCssClass));
            items.Add(new("structures", "Structures", Workspace, enemies.Of(EnemyAuthoringReader.Structure).Count(), StratagemCategories.StructureCssClass));
        }
        items.Add(new("changes", "Changes", Workspace, project == null ? null : modifications));
        // Hand-written Runtime Lua (src/addon.lua): events, timers, keybinds and gameplay actions. Works with every SDK; the event reference
        // and snippets need an SDK that publishes EventCatalog.json.
        items.Add(new("scripting", "Custom Lua", Workspace));
        items.Add(new("lua", "Lua Preview", Workspace));
        items.Add(new("export", "Build / Export", Workspace));
        items.Add(new("research", "Snapshot Research", Research, RequiresProject: false));
        return items;
    }
}
