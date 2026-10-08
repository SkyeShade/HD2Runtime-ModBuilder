using System.Text.Json;
using HD2RuntimeGUI.Core.Generation;
using HD2RuntimeGUI.Core.Metadata;
using HD2RuntimeGUI.Core.Models;

namespace HD2RuntimeGUI.Core.Services;

/// <summary>Why a support edit replaces another saved edit (the field editor asks before it does).</summary>
public enum SupportReplacement
{
    // Both write the same native bytes (ChargeFields.SameBytes): one write changes both, so one edit is kept.
    SameBytes,
    // An overcharge explosion reference naming another weapon: Runtime refuses this weapon's own overcharge explosion edits.
    OverchargeRowEdit,
    // An overcharge explosion edit while the reference names another weapon: the reference goes back to the weapon's own.
    OverchargeReference,
}
public sealed record SupportReplaced(SupportField Field, SupportChange Change, SupportReplacement Reason);

// HD2Runtime 0.30.0 support-weapon charge (docs/runtime030-integration-plan.md, Phase 6): conflicts an edit causes are resolved where it is
// made. The editor asks with SupportReplacedBy, then saves with SetSupportReplacingAsync; a deprecated charge id's edit moves to the id
// that names what it writes (MoveSupportChangeAsync).
public sealed partial class BuilderWorkspace
{
    /// <summary>Saved edits that saving <paramref name="value"/> on this field would replace (empty when the edit conflicts with nothing).</summary>
    public IReadOnlyList<SupportReplaced> SupportReplacedBy(string instance, string value)
    {
        if (Project is not { } p || Metadata?.SupportAuthoring is not { } catalog || catalog.FieldInstances.FirstOrDefault(f => f.InstanceKey == instance) is not { } field) return [];
        JsonElement parsed;
        try { using var d = JsonDocument.Parse(value); parsed = SupportScalar.Normalize(field, d.RootElement); }
        catch (Exception e) when (e is JsonException or InvalidDataException or InvalidOperationException) { return []; }
        // Back to vanilla is a reset: it writes nothing, so it replaces nothing.
        if (SupportScalar.Equal(field, parsed, field.Value.Baseline)) return [];
        var result = new List<SupportReplaced>();
        SupportChange? Saved(SupportField f) => p.SupportChanges.FirstOrDefault(c => c.InstanceKey == f.InstanceKey);
        foreach (var other in ChargeFields.SameBytes(catalog, field)) if (Saved(other) is { } c) result.Add(new(other, c, SupportReplacement.SameBytes));
        if (field.IsOverchargeExplosion && ChargeFields.Swaps(field, parsed))
            foreach (var row in catalog.FieldInstances.Where(f => f.SupportWeapon == field.SupportWeapon && ChargeFields.RefusedWhileSwapped(f)))
                if (Saved(row) is { } c) result.Add(new(row, c, SupportReplacement.OverchargeRowEdit));
        if (ChargeFields.RefusedWhileSwapped(field) && ChargeFields.OverchargeReference(catalog, field.SupportWeapon) is { } reference
            && Saved(reference) is { } swap && ChargeFields.Swaps(reference, swap.DesiredValue))
            result.Add(new(reference, swap, SupportReplacement.OverchargeReference));
        return result;
    }

    /// <summary>Saves the value and removes the saved edits it replaces (SupportReplacedBy), in one project change.</summary>
    public Task SetSupportReplacingAsync(string instance, string value)
    {
        var replaced = SupportReplacedBy(instance, value).Select(r => r.Change.InstanceKey).ToHashSet(StringComparer.Ordinal);
        return EditSupportAsync(p =>
        {
            p.SupportChanges.RemoveAll(c => replaced.Contains(c.InstanceKey));
            ApplySupportValue(p, instance, value, false);
        });
    }

    /// <summary>
    /// Moves a saved edit to another field of the same weapon with the same value, replacing that field's own edit: a deprecated charge id
    /// (charge.minimum_seconds, written as a time before 0.30.0) to the speed multiplier it writes, or to the charge time it was meant as.
    /// </summary>
    public Task MoveSupportChangeAsync(string from, string to) => EditSupportAsync(p =>
    {
        var saved = p.SupportChanges.SingleOrDefault(c => c.InstanceKey == from) ?? throw new InvalidOperationException("No saved edit to move.");
        var target = SupportChangeService.Catalog(Metadata!).Field(to);
        if (target.SupportWeapon != saved.Weapon) throw new InvalidOperationException("Edits move only within one weapon.");
        p.SupportChanges.RemoveAll(c => c.InstanceKey == from || c.InstanceKey == to);
        ApplySupportValue(p, to, saved.DesiredValue.GetRawText(), false);
        var moved = p.SupportChanges.SingleOrDefault(c => c.InstanceKey == to);
        if (moved != null) p.SupportChanges = p.SupportChanges.Select(c => c.InstanceKey == to ? c with { Enabled = saved.Enabled, Notes = saved.Notes, Group = saved.Group } : c).ToList();
    });

    /// <summary>The saved edit of another field that writes this field's bytes, when there is one (it is what Runtime writes for both).</summary>
    public (SupportField Field, SupportChange Change)? SupportWrittenThrough(SupportField field)
    {
        if (Project is not { } p || Metadata?.SupportAuthoring is not { } catalog) return null;
        foreach (var other in ChargeFields.SameBytes(catalog, field))
            if (p.SupportChanges.FirstOrDefault(c => c.InstanceKey == other.InstanceKey && c.Enabled) is { } c) return (other, c);
        return null;
    }

    /// <summary>The weapon's charge times after the project's edits (null without all three published times).</summary>
    public ChargeTimes? SupportChargeTimes(string weapon) => Metadata?.SupportAuthoring is { } catalog && Project is { } p
        ? ChargeFields.Times(catalog, p.SupportChanges, weapon) : null;
}
