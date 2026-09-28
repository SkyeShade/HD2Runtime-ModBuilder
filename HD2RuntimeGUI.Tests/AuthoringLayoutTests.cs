using HD2RuntimeGUI.Core.Generation;
using HD2RuntimeGUI.Core.Metadata;
using HD2RuntimeGUI.Core.Services;
using Xunit;

namespace HD2RuntimeGUI.Tests;

// Player Weapons authoring layout: one acknowledgement per shared object, inherited by later edits of that object, and the
// sticky authoring summary. Storage and build validation stay per change, exactly as Runtime requires.
public sealed class AuthoringLayoutTests
{
    private static async Task<BuilderWorkspace> Workspace(TestEnvironment e)
    {
        File.Delete(e.Paths.CachePath("current.json")); var sdk = await e.Cache.GetCurrentAsync();
        var w = e.Workspace(); await w.CreateAsync(new("Layout", "Tests", "mods/tests/layout", "0.1.0"), sdk); return w;
    }
    // Weapon-level shared scalars are edited as ordinary weapon fields on SDKs before composition (0.15.0); newer SDKs edit them on objects.
    private static async Task<BuilderWorkspace> WeaponWorkspace(TestEnvironment e)
    {
        var sdk = await SdkFixtures.Install(e, "0.15.0"); var w = e.Workspace(); await w.CreateAsync(new("Layout", "Tests", "mods/tests/layout", "0.1.0"), sdk); return w;
    }
    // A shared weapon-level scalar with at least one sibling field on the same shared object.
    private static (PlayerWeapon Weapon, WeaponCapability[] Fields) SharedObject(BuilderWorkspace w)
    {
        foreach (var weapon in w.Metadata!.PlayerWeapons!.Weapons.Where(x => !x.OrdinaryWritesBlocked))
        {
            var fields = weapon.Fields.Where(f => f.IsPreferred && f.Editable && !f.DerivedReadOnly && f.WriteAccepted && f.AffectsMultipleWeapons
                && f.CurrentDefault.ValueKind == System.Text.Json.JsonValueKind.Number && f.Domain is not ("explosion" or "terminal")
                && (w.Metadata.Advanced == null || !CompositionChangeService.ProjectileOwned(f))).ToArray();
            var group = fields.GroupBy(WeaponAcknowledgements.ScopeKey).FirstOrDefault(g => g.Count() > 1);
            if (group != null) return (weapon, group.ToArray());
        }
        throw new InvalidOperationException("The SDK publishes no shared weapon-level object with two writable scalars.");
    }
    private static string Bumped(WeaponCapability f) => f.Type == "integer" ? (f.CurrentDefault.GetInt64() + 1).ToString() : (f.CurrentDefault.GetDouble() + 1).ToString(System.Globalization.CultureInfo.InvariantCulture);

    [Fact] public async Task One_acknowledgement_per_shared_object_blocks_the_build_until_checked()
    {
        using var e = new TestEnvironment(); var w = await WeaponWorkspace(e); var (weapon, fields) = SharedObject(w);
        await w.SetWeaponChangeAsync(weapon.Name, fields[0].SemanticFieldId, Bumped(fields[0]), false);
        var group = Assert.Single(w.WeaponSharedGroups(weapon.Name));
        Assert.False(group.Acknowledged); Assert.Equal([fields[0].SemanticFieldId], group.FieldIds);
        // The panel lists every affected weapon, including the one being edited.
        Assert.Equal(fields[0].SharedWithWeapons.Append(weapon.Name).Order(StringComparer.Ordinal), group.AffectedWeapons.Order(StringComparer.Ordinal));
        Assert.NotNull(w.BuildError);

        await w.SetWeaponSharedAcknowledgedAsync(weapon.Name, group.Key, true);
        Assert.True(Assert.Single(w.WeaponSharedGroups(weapon.Name)).Acknowledged); Assert.Null(w.BuildError);
        var stored = Assert.Single(w.Project!.WeaponChanges);
        Assert.True(stored.SharedAcknowledged); Assert.Equal(fields[0].WriteScope, stored.AcknowledgedWriteScope);
        Assert.True(WeaponChangeService.SharedAcknowledgementCurrent(fields[0], stored));
        Assert.Contains("allow_shared=true", w.LuaPreview);

        await w.SetWeaponSharedAcknowledgedAsync(weapon.Name, group.Key, false);
        Assert.False(Assert.Single(w.WeaponSharedGroups(weapon.Name)).Acknowledged); Assert.NotNull(w.BuildError);
    }

    [Fact] public async Task A_new_edit_of_an_acknowledged_shared_object_inherits_the_acknowledgement()
    {
        using var e = new TestEnvironment(); var w = await WeaponWorkspace(e); var (weapon, fields) = SharedObject(w);
        await w.SetWeaponChangeAsync(weapon.Name, fields[0].SemanticFieldId, Bumped(fields[0]), false);
        Assert.False(WeaponAcknowledgements.Inherited(w.WeaponGroups, weapon.Name, fields[1]));
        await w.SetWeaponSharedAcknowledgedAsync(weapon.Name, w.WeaponSharedGroups(weapon.Name)[0].Key, true);
        Assert.True(WeaponAcknowledgements.Inherited(w.WeaponGroups, weapon.Name, fields[1]));
        // The editor passes the inherited acknowledgement; the second edit joins the same group, and each change stores its own.
        await w.SetWeaponChangeAsync(weapon.Name, fields[1].SemanticFieldId, Bumped(fields[1]), WeaponAcknowledgements.Inherited(w.WeaponGroups, weapon.Name, fields[1]));
        var group = Assert.Single(w.WeaponSharedGroups(weapon.Name));
        Assert.True(group.Acknowledged); Assert.Equal(2, group.FieldIds.Count); Assert.Null(w.BuildError);
        Assert.All(w.Project!.WeaponChanges, c => Assert.True(c.SharedAcknowledged));
        // Other weapons' shared objects are separate groups.
        Assert.Empty(w.WeaponSharedGroups("AR-23A Liberator Carbine" == weapon.Name ? "AR-23 Liberator" : "AR-23A Liberator Carbine"));
    }

    [Fact] public async Task Summary_counts_modified_shared_and_required_acknowledgements()
    {
        using var e = new TestEnvironment(); var w = await WeaponWorkspace(e); var (weapon, fields) = SharedObject(w);
        Assert.Equal(new WeaponAuthoringSummary(0, 0, 0), w.WeaponSummary(weapon.Name));
        await w.SetWeaponChangeAsync(weapon.Name, fields[0].SemanticFieldId, Bumped(fields[0]), false);
        await w.SetWeaponChangeAsync(weapon.Name, fields[1].SemanticFieldId, Bumped(fields[1]), false);
        Assert.Equal(new WeaponAuthoringSummary(2, 2, 1), w.WeaponSummary(weapon.Name));
        await w.SetWeaponSharedAcknowledgedAsync(weapon.Name, w.WeaponSharedGroups(weapon.Name)[0].Key, true);
        Assert.Equal(new WeaponAuthoringSummary(2, 2, 0), w.WeaponSummary(weapon.Name));
    }

    [Fact] public async Task Summary_includes_the_weapons_magazine_attachment_acknowledgement()
    {
        using var e = new TestEnvironment(); var w = await Workspace(e); var catalog = w.Metadata!.Entities!.Attachments!;
        var entry = catalog.Weapons.First(x => catalog.Resolved(x).Count > 0);
        var attachment = catalog.Resolved(entry)[0].Attachment;
        var field = catalog.FieldInstances.First(f => f.Target.Attachment == attachment.SemanticId && f.Editable);
        await w.SetEntityAsync(field.InstanceKey, (field.CurrentDefault.GetInt32() + 5).ToString());
        Assert.Equal(new WeaponAuthoringSummary(1, 1, 1), w.WeaponSummary(entry.Weapon)); Assert.NotNull(w.BuildError);
        await w.SetAttachmentAcknowledgedAsync(attachment.SemanticId, true);
        Assert.Equal(new WeaponAuthoringSummary(1, 1, 0), w.WeaponSummary(entry.Weapon)); Assert.Null(w.BuildError);
    }
}
