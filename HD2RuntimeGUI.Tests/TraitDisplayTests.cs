using HD2RuntimeGUI.Core.Services;
using Xunit;

namespace HD2RuntimeGUI.Tests;

// Armory trait display on the frozen 0.28.0 SDK. The armory labels ModBuilder shows (Armory labels panel) are the weapon's native
// LoadoutEntry trait IDs as Runtime publishes them (presentation.traits, presentation.armor_penetration). The support-weapon header also
// shows the wiki's own trait list (SupportWeaponCapabilities), which is a different source: it is labelled as the wiki's and never feeds the
// armory labels. Regression: the M-1000 Maxigun's header showed wiki traits (cut to three) as if they were its traits, above an empty
// native list.
public sealed class TraitDisplayTests
{
    private const string Maxigun = "M-1000 Maxigun", Halt = "SG-20 Halt";
    public static TheoryData<string, string, string[], string, string> Native => new()
    {
        // kind, weapon, native trait IDs in slot order, penetration label, penetration state
        { CompositionKind.Support, Maxigun, [], "none", "none" },
        { CompositionKind.Support, "40-K Meltagun", ["anti_tank"], "anti_tank", "single" },
        { CompositionKind.Support, "FLAM-40 Flamethrower", ["incendiary_7d01a114"], "none", "none" },
        { CompositionKind.Support, "EAT-17 Expendable Anti-Tank", ["anti_tank", "expendable"], "anti_tank", "single" },
        { CompositionKind.Player, "AR-11 Arbitrator", ["light_armor_penetrating"], "light", "single" },
        { CompositionKind.Player, Halt, ["light_armor_penetrating", "medium_armor_penetrating", "rounds_reload", "stun"], "none", "blocked" },
    };

    [Theory, MemberData(nameof(Native))]
    public async Task Armory_labels_are_the_native_trait_set_in_slot_order(string kind, string weapon, string[] traits, string penetration, string state)
    {
        using var e = new TestEnvironment(); var (w, sdk) = await EnemyAuthoringTests.Fresh(e);
        var view = w.Presentation(kind, weapon)!;
        Assert.Equal(traits, view.Traits!.Baseline); Assert.Equal(traits, view.Traits.Current); Assert.False(view.Traits.Modified);
        Assert.Equal(penetration, view.Penetration!.Baseline); Assert.Equal(state, view.Penetration.State);
        // Every native trait has its published label (nothing falls back to an ID or is invented).
        Assert.All(traits, t => Assert.Equal(sdk.Presentation!.Traits[t].Label, sdk.Presentation.TraitLabel(t)));
        // A blocked penetration label has no single value to show (the Halt shows two penetration labels): only the reason is shown.
        Assert.Equal(state == "blocked", view.Penetration.Blocked);
        if (view.Penetration.Blocked) Assert.Contains("LIGHT ARMOR PENETRATING, MEDIUM ARMOR PENETRATING", view.Penetration.Reason);
    }

    [Fact] public async Task The_Maxigun_keeps_its_native_armory_labels_apart_from_the_wiki_traits()
    {
        using var e = new TestEnvironment(); var (w, sdk) = await EnemyAuthoringTests.Fresh(e);
        // The wiki lists three traits for the Maxigun; its native loadout record lists none. Both are published; they are shown apart.
        Assert.Equal(["Support Weapon", "Medium Armor Penetrating", "Backpack"], sdk.Advanced!.Support.Weapons[Maxigun].Traits);
        var view = w.Presentation(CompositionKind.Support, Maxigun)!;
        Assert.Empty(view.Traits!.Current); Assert.Equal("none", view.Penetration!.Current); Assert.False(view.Penetration.Blocked);
        // Controls: where the two sources agree, the native label is the wiki trait's own string.
        Assert.Contains("Anti-Tank", sdk.Advanced.Support.Weapons["40-K Meltagun"].Traits);
        Assert.Equal("ANTI-TANK", sdk.Presentation!.TraitLabel(w.Presentation(CompositionKind.Support, "40-K Meltagun")!.Traits!.Current.Single()));
        Assert.Contains("Incendiary", sdk.Advanced.Support.Weapons["FLAM-40 Flamethrower"].Traits);
        Assert.Equal("INCENDIARY", sdk.Presentation.TraitLabel(w.Presentation(CompositionKind.Support, "FLAM-40 Flamethrower")!.Traits!.Current.Single()));
    }

    [Fact] public async Task Two_traits_with_the_same_label_are_told_apart_by_their_published_ids()
    {
        using var e = new TestEnvironment(); var (_, sdk) = await EnemyAuthoringTests.Fresh(e); var catalog = sdk.Presentation!;
        Assert.Equal("INCENDIARY", catalog.TraitLabel("incendiary_7d01a114")); Assert.Equal("INCENDIARY", catalog.TraitLabel("incendiary_a5f02451"));
        Assert.Equal("INCENDIARY (incendiary_7d01a114)", catalog.ChoiceLabel("incendiary_7d01a114"));
        Assert.Equal("INCENDIARY (incendiary_a5f02451)", catalog.ChoiceLabel("incendiary_a5f02451"));
        Assert.Equal("STUN", catalog.ChoiceLabel("stun"));
        // Every choice the add-trait list offers is distinct.
        Assert.Equal(catalog.Traits.Count, catalog.Traits.Keys.Select(catalog.ChoiceLabel).Distinct().Count());
    }
}
