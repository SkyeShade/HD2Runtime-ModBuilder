using System.Text.Json;
using HD2RuntimeGUI.Core.Metadata;

namespace HD2RuntimeGUI.Core.Services;

// HD2Runtime 0.30.2 status attach tiers (StatusEffectCatalog attachTier, attachmentModel.attachTiers): every status picker groups its statuses
// by tier, and an extended status (enemy_slot or other_system) is shown with its tier's published text. The write's allow_unverified_effect
// is implicit (CompositionOptIns.Extended); the export requires 0.30.2 (RuntimeRequirement.ExtendedStatuses0302).

/// <summary>One group of a status picker: the statuses of one attach tier, in the picker's order; Tier is null for statuses without one
/// (an older catalog, or a slot's own status that is not attachable).</summary>
public sealed record StatusGroup(string? Tier, IReadOnlyList<string> Keys);
/// <summary>An extended status chosen in a slot: its tier, the tier's published text and the duration its definition stores.</summary>
public sealed record ExtendedStatusNotice(string Key, string Tier, string? TierText, double? Duration);

public sealed partial class BuilderWorkspace
{
    /// <summary>The statuses grouped by attach tier in the catalog's tier order (player_attack, enemy_slot, other_system); statuses without a
    /// tier come first, ungrouped. One ungrouped group when the catalog publishes no tiers.</summary>
    public IReadOnlyList<StatusGroup> StatusGroups(IEnumerable<string> keys)
    {
        var list = keys.ToArray();
        if (Metadata?.StatusEffects is not { } catalog || catalog.Tiers.Count == 0) return [new(null, list)];
        string? Tier(string key) => catalog.Find(key)?.AttachTier is { } t && catalog.Tiers.Contains(t) ? t : null;
        var groups = new List<StatusGroup>();
        if (list.Where(k => Tier(k) == null).ToArray() is { Length: > 0 } untiered) groups.Add(new(null, untiered));
        foreach (var tier in catalog.Tiers)
            if (list.Where(k => Tier(k) == tier).ToArray() is { Length: > 0 } keysOfTier) groups.Add(new(tier, keysOfTier));
        return groups;
    }
    /// <summary>The tier's published text (attachmentModel.attachTiers), or null.</summary>
    public string? StatusTierText(string tier) => Metadata?.StatusEffects?.TierText(tier);
    /// <summary>The notice for an extended status (0.30.2 tiers enemy_slot and other_system), or null for any other value.</summary>
    public ExtendedStatusNotice? ExtendedStatus(string? key) => key != null && Metadata?.StatusEffects?.Find(key) is { Extended: true } s
        ? new(key, s.AttachTier!, StatusTierText(s.AttachTier!), s.Duration) : null;
    public ExtendedStatusNotice? ExtendedStatus(JsonElement? value) => value is { ValueKind: JsonValueKind.String } v ? ExtendedStatus(v.GetString()) : null;
}
