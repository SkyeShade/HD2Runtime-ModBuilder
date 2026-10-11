using System.Text.Json;
using HD2RuntimeGUI.Core.Audit;
using HD2RuntimeGUI.Core.Generation;
using HD2RuntimeGUI.Core.Metadata;
using HD2RuntimeGUI.Core.Projects;
using HD2RuntimeGUI.Core.Services;
using Xunit;

namespace HD2RuntimeGUI.Tests;

// HD2Runtime 0.30.2 noEffect (sdk/docs/backpack-ammo.md "Not covered"; artifacts/survey/sdk-0.30.2-new-capabilities.md section 4): the team-reload
// weapons' own ammunition rows are 0 natively because the backpack holds the ammunition. Runtime accepts a write (older mods keep working)
// and asks editors to show the rows read-only with its reason. ModBuilder refuses a new edit, keeps and exports an edit saved before
// (ModBuilder 1.7.0) unchanged until it is removed in its row, and points to the backpack the weapon's call-in delivers. Nothing new is saved.
public sealed class NoEffectTests
{
    private static readonly (string Weapon, string Backpack)[] Weapons =
    [
        ("AC-8 Autocannon", "Automatic Cannon Backpack"), ("GR-8 Recoilless Rifle", "Recoilless Rifle Backpack"), ("FAF-14 Spear", "Faf Missile Launcher Backpack"),
        ("RL-77 Airburst Rocket Launcher", "Air Burst Rocket Backpack"), ("StA-X3 W.A.S.P. Launcher", "Faf Missile Launcher Helghast Backpack"),
    ];
    private static readonly string[] Rows =
    [
        "AC-8 Autocannon|rounds.rounds_from_supply", "AC-8 Autocannon|rounds.spare_rounds", "AC-8 Autocannon|rounds.starting_rounds",
        .. Weapons.Skip(1).SelectMany(w => new[] { w.Weapon + "|magazine.spare_magazines", w.Weapon + "|magazine.starting_magazines" }),
    ];
    private const string Autocannon = "AC-8 Autocannon", SpareRounds = "rounds.spare_rounds";
    // 0.30.2 and 0.30.3 publish the notice; 0.31.0 made these rows the weapons' own spares (real fields with allow_unverified_effect).
    private const string Sdk = "0.30.3";

    private static async Task<BuilderWorkspace> Fresh(TestEnvironment e)
    {
        var sdk = await SdkFixtures.Install(e, Sdk); var w = e.Workspace();
        await w.CreateAsync(new("NoEffect", "Tests", "mods/tests/noeffect", "0.1.0"), sdk); return w;
    }
    private static SupportField Field(BuilderWorkspace w, string weapon, string id) => w.Metadata!.SupportAuthoring!.FieldInstances
        .Single(f => f.SupportWeapon == weapon && f.SemanticFieldId == id && f.Target.AttackRole == null);
    // The same SDK without the 0.30.2 notice: what the Lua was before ModBuilder read noEffect.
    private static SdkMetadata WithoutNotice(SdkMetadata sdk) => sdk with { SupportAuthoring = sdk.SupportAuthoring! with
        { FieldInstances = sdk.SupportAuthoring.FieldInstances.Select(f => f with { NoEffect = null }).ToArray() } };
    // A project saved by ModBuilder 1.7.0 with an edit of a no-effect row (1.7.0 offered them as ordinary fields), reopened from disk.
    private static async Task<BuilderWorkspace> SavedWithEdit(TestEnvironment e, string weapon, string id, string value)
    {
        var w = await Fresh(e); var f = Field(w, weapon, id);
        w.Project!.SupportChanges.Add(new SupportChangeService().Create(w.Metadata!, f.InstanceKey, value));
        await e.Store.SaveAsync(w.Project);
        var reopened = e.Workspace(); await reopened.InitializeAsync(); await reopened.OpenAsync(w.Project.Id); return reopened;
    }

    [Fact] public async Task The_eleven_rows_are_read_only_for_new_edits_with_Runtime_reason()
    {
        using var e = new TestEnvironment(); var w = await Fresh(e);
        var inert = w.Metadata!.SupportAuthoring!.FieldInstances.Where(NoEffectRows.Is).ToArray();
        Assert.Equal(Rows.Order(), inert.Select(f => f.SupportWeapon + "|" + f.SemanticFieldId).Order());
        await w.SetModOptionsEnabledAsync(true);
        foreach (var f in inert)
        {
            Assert.False(string.IsNullOrWhiteSpace(f.NoEffect!.Reason)); Assert.Contains("backpack", f.NoEffect.Reason, StringComparison.Ordinal);
            var refused = await Assert.ThrowsAsync<InvalidDataException>(() => w.SetSupportAsync(f.InstanceKey, "5"));
            Assert.Contains(f.NoEffect.Reason, refused.Message); Assert.Contains(f.Display.Name, refused.Message);
            // An in-game option on the unedited row would be a new edit of it: none is offered.
            Assert.Null(w.OptionCandidate(ModOptionsService.SupportKey(f.InstanceKey)));
        }
        Assert.Empty(w.Project!.SupportChanges); Assert.Empty(NoEffectRows.Saved(w.Project, w.Metadata));
        // The same weapon's other ammunition rows (the clip the weapon itself holds) stay editable.
        var capacity = Field(w, Autocannon, "rounds.feed_capacity_1"); Assert.False(NoEffectRows.Is(capacity));
        await w.SetSupportAsync(capacity.InstanceKey, "12");
        Assert.Equal(capacity.InstanceKey, Assert.Single(w.Project.SupportChanges).InstanceKey);
        // The capability audit counts the notice as shown.
        var entry = CapabilityAudit.Run(w.Metadata, CapabilityAuditTests.Files(Sdk)).Entries.Single(x => x.Area == "support_weapon_notice" && x.Capability == "noEffect");
        Assert.Equal((CapabilityClass.SUPPORTED_UI, 11), (entry.Class, entry.Instances)); Assert.False(string.IsNullOrWhiteSpace(entry.Surface));
    }

    [Fact] public async Task A_saved_edit_is_kept_and_exports_exactly_as_before()
    {
        using var e = new TestEnvironment(); var w = await SavedWithEdit(e, Autocannon, SpareRounds, "20");
        var f = Field(w, Autocannon, SpareRounds); var saved = Assert.Single(w.Project!.SupportChanges);
        Assert.Equal((f.InstanceKey, 20), (saved.InstanceKey, saved.DesiredValue.GetInt32()));
        Assert.Equal(saved, Assert.Single(NoEffectRows.Saved(w.Project, w.Metadata)));
        // No build blocker and no issue on the row: Runtime accepts the write.
        Assert.Null(w.BuildError); Assert.Null(w.SupportIssue(saved));
        // The generated Lua is byte for byte what it was without the notice, and it writes the saved value.
        Assert.Equal(new LuaGenerator(new ChangeService()).Generate(w.Project, WithoutNotice(w.Metadata!)), w.LuaPreview);
        Assert.Contains("hd2.fields.rounds.spare_rounds", w.LuaPreview); Assert.Contains("value=20", w.LuaPreview);
        // Accepting a changed baseline re-saves the value the edit holds; a different value is a new edit and is refused.
        await w.SetSupportAsync(f.InstanceKey, "20", true); Assert.Equal(20, Assert.Single(w.Project.SupportChanges).DesiredValue.GetInt32());
        await Assert.ThrowsAsync<InvalidDataException>(() => w.SetSupportAsync(f.InstanceKey, "30"));
        Assert.Equal(20, Assert.Single(w.Project.SupportChanges).DesiredValue.GetInt32());
        // Nothing new is saved: the project format is what the same project needs without the edit.
        var format = w.Project.FormatVersion;
        Assert.Equal(ProjectIdentity.RequiredFormat(w.Project), ProjectIdentity.RequiredFormat(new() { SupportChanges = [] }));
        await w.ExportAsync(); Assert.NotNull(w.LastExport); Assert.Equal(format, w.Project.FormatVersion);
    }

    [Fact] public async Task Removing_the_saved_edit_works_and_it_cannot_be_added_again()
    {
        using var e = new TestEnvironment(); var w = await SavedWithEdit(e, "GR-8 Recoilless Rifle", "magazine.spare_magazines", "4");
        var f = Field(w, "GR-8 Recoilless Rifle", "magazine.spare_magazines");
        Assert.Contains("hd2.fields.magazine.spare_magazines", w.LuaPreview);
        await w.ResetSupportAsync(instance: f.InstanceKey);
        Assert.Empty(w.Project!.SupportChanges); Assert.Empty(NoEffectRows.Saved(w.Project, w.Metadata));
        Assert.DoesNotContain("spare_magazines", w.LuaPreview); Assert.Null(w.BuildError);
        await Assert.ThrowsAsync<InvalidDataException>(() => w.SetSupportAsync(f.InstanceKey, "4"));
        var reopened = e.Workspace(); await reopened.InitializeAsync(); await reopened.OpenAsync(w.Project.Id);
        Assert.Empty(reopened.Project!.SupportChanges);
    }

    [Fact] public async Task The_backpack_link_resolves_for_each_of_the_five_weapons()
    {
        using var e = new TestEnvironment(); var sdk = await SdkFixtures.Install(e, Sdk);
        foreach (var (weapon, backpack) in Weapons)
        {
            var home = NoEffectRows.Home(sdk, weapon);
            Assert.NotNull(home);
            // The weapon's own call-in, its hellpod rack, and the active slot that delivers the backpack (slot 2, next to the weapon in slot 1).
            Assert.Equal((weapon, sdk.SupportLinks!.ForWeapon(weapon)!.Stratagem.Name, backpack, weapon + " pod", 2, false),
                (home!.Weapon, home.Stratagem, home.Backpack, home.Rack, home.Slot, home.Authorable));
            var rack = sdk.Entities!.Pods!.Rack(home.Rack!)!;
            Assert.Contains(rack, sdk.Entities.Pods.ForStratagem(sdk.SupportLinks.ForWeapon(weapon)!.Stratagem.SemanticId));
            Assert.True(rack.AuthoredSlots.Single(s => s.Slot == 2).Writable); // shown by the drop-pod editor on the stratagem's page
            Assert.Equal($"[data-pod-rack=\"{weapon} pod\"] [data-pod-slot=\"2\"]", home.Reveal);
            // Runtime keeps these team-reload backpacks' ammunition read-only: no backpack ammunition to edit.
            Assert.Null(sdk.SupportAuthoring!.Weapons.Single(w => w.Name == weapon).AmmoBackpack);
            Assert.Null(sdk.Entities.Backpacks.Find(backpack));
        }
        // Where the SDK publishes the backpack's ammunition (the Maxigun), the link edits it instead.
        var maxigun = NoEffectRows.Home(sdk, "M-1000 Maxigun")!;
        Assert.Equal(("M-1000 Maxigun Backpack", true, "[data-backpack-ammo=\"M-1000 Maxigun Backpack\"]"), (maxigun.Backpack, maxigun.Authorable, maxigun.Reveal));
        Assert.Null(NoEffectRows.Home(sdk, "No such weapon"));
    }
}
