using HD2RuntimeGUI.Core.Generation;

namespace HD2RuntimeGUI.Core.Services;

// Weapon editor helpers: one acknowledgement per shared object, and the compact authoring summary.
public sealed partial class BuilderWorkspace
{
    public IReadOnlyList<SharedAckGroup> WeaponSharedGroups(string weapon) => WeaponAcknowledgements.Groups(WeaponGroups, weapon);

    // Sets (or clears) Runtime's allow_shared acknowledgement on every saved edit of one shared object. Each change keeps its own
    // acknowledgement record, exactly as a per-field acknowledgement would have stored it.
    public async Task SetWeaponSharedAcknowledgedAsync(string weapon, string scopeKey, bool acknowledged)
    {
        foreach (var group in WeaponGroups.Where(g => g.Weapon == weapon && g.Conflict == null && g.Field is { AffectsMultipleWeapons: true }
            && WeaponAcknowledgements.ScopeKey(g.Field) == scopeKey).ToArray())
        {
            var change = group.Representative;
            await SetWeaponChangeAsync(weapon, group.FieldId, group.Field!.Format(change.DesiredValue), acknowledged, change.Group, change.Notes);
        }
    }

    public WeaponAuthoringSummary WeaponSummary(string weapon)
    {
        if (Project == null || Metadata == null) return new(0, 0);
        // Edits of the weapon's sub-targets (its underbarrel) count with it.
        var groups = WeaponGroups.Where(g => g.Weapon == weapon || Metadata.PlayerWeapons?.FindSubweapon(g.Weapon)?.SubweaponOf == weapon).ToArray();
        var composition = Project.CompositionChanges.Where(c => c.Weapon == weapon && c.Enabled).ToArray();
        var modified = groups.Count(g => g.Conflict != null || FieldPresentationRules.Modified(g)) + composition.Length
            + Project.ProjectileChanges.Count(c => c.Weapon == weapon);
        // Attack outputs (0.28.0 development SDKs); one written through shared ammunition counts as shared.
        var outputs = Project.AttackOutputChanges?.Where(c => c.Weapon == weapon).ToArray() ?? [];
        modified += outputs.Length;
        var shared = groups.Count(g => g.Field?.AffectsMultipleWeapons == true) + composition.Count(c =>
        { try { return CompositionChangeService.Capability(Metadata, c).AffectsMultipleWeapons; } catch (InvalidDataException) { return false; } })
            + outputs.Count(c => c.Acknowledgements.Contains("allow_shared"));
        // Edited magazine attachments owned by this weapon are shared definitions.
        if (Metadata.Entities?.Attachments is { } attachments && attachments.Weapon(weapon) is { } entry)
            foreach (var (attachment, _, _) in attachments.Resolved(entry))
            {
                var edited = attachment.FieldInstanceKeys.Any(k => Project.EntityChanges.Any(c => c.InstanceKey == k));
                if (!edited) continue;
                modified += attachment.FieldInstanceKeys.Count(k => Project.EntityChanges.Any(c => c.InstanceKey == k)); shared++;
            }
        return new(modified, shared);
    }
}

// Presentation-independent "modified" rule for a saved weapon change group (value differs from its SDK baseline).
public static class FieldPresentationRules
{
    public static bool Modified(WeaponChangeGroup g) => g.Field is { } f && f.Format(g.Representative.DesiredValue) != f.Format(f.CurrentDefault);
}
