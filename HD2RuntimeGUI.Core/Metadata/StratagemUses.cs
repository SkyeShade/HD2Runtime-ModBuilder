using HD2RuntimeGUI.Core.Localization;
using System.Text.Json;

namespace HD2RuntimeGUI.Core.Metadata;

// Runtime 0.26.0 generic mission uses (hd2.fields.stratagem.max_uses): Runtime's own 'unlimited' (native 0xFFFFFFFF, never a large
// finite number) or a finite count in the published range. Only the transitions Runtime publishes for the stratagem are offered.
// Eagles use the same native field for uses per rearm (hd2.fields.eagle.uses_per_rearm), so max_uses is read-only for them.
public sealed record UsesControl(bool Unlimited, bool CanUnlimited, bool CanLimited, bool CountEditable, int Count);

public static class StratagemUses
{
    public const string Type = "stratagem_uses", Unlimited = "unlimited", Field = "stratagem.max_uses";
    public const string FiniteToUnlimited = "finite_to_unlimited", UnlimitedToFinite = "unlimited_to_finite", FiniteToFinite = "finite_to_finite";

    public static bool IsUnlimited(JsonElement v) => v.ValueKind == JsonValueKind.String && v.GetString() == Unlimited;

    // 'unlimited', or an integer inside the published range.
    public static JsonElement Normalize(StratagemField f, JsonElement v)
    {
        if (IsUnlimited(v)) return JsonSerializer.SerializeToElement(Unlimited);
        if (v.ValueKind == JsonValueKind.Number && v.TryGetDecimal(out var n) && n == decimal.Truncate(n)
            && (f.Min is not double min || n >= (decimal)min) && (f.Max is not double max || n <= (decimal)max))
            return JsonSerializer.SerializeToElement((int)n);
        throw new InvalidDataException($"Mission uses are Unlimited or a whole number from {f.Min ?? 1} to {f.Max ?? 100}.");
    }
    // The move from the baseline to the desired value must be one of the stratagem's published transitions.
    public static void CheckTransition(StratagemField f, JsonElement expected, JsonElement desired)
    {
        var from = IsUnlimited(expected); var to = IsUnlimited(desired);
        if (JsonElement.DeepEquals(Normalize(f, expected), Normalize(f, desired))) return;
        var transition = (from, to) switch { (false, true) => FiniteToUnlimited, (true, false) => UnlimitedToFinite, (false, false) => FiniteToFinite, _ => "" };
        if (f.Transitions?.Contains(transition) != true)
            throw new InvalidDataException(transition switch
            {
                FiniteToUnlimited => CoreText.Get("Messages.Stratagem.Uses.NoUnlimited"),
                UnlimitedToFinite => CoreText.Get("Messages.Stratagem.Uses.NoLimit"),
                _ => CoreText.Get("Messages.Stratagem.Uses.NoChange"),
            });
    }
    // allow_unverified_effect is required unless the desired value is one Runtime lists as gameplay-proven (Exosuit 3 -> unlimited).
    public static bool EffectRequired(StratagemField f, JsonElement desired) => f.Acknowledgement == "allow_unverified_effect"
        && !(f.GameplayProvenValues ?? []).Any(p => JsonElement.DeepEquals(p, IsUnlimited(desired) ? JsonSerializer.SerializeToElement(Unlimited) : desired));
    // Editor state for the compact [Unlimited ▼] / [Limited ▼] [N] control: which modes the published transitions allow from the
    // baseline, the count shown when Limited, and whether that count may change (unlimited -> finite accepts any count in range;
    // finite -> finite needs its own transition). Choosing Limited from Unlimited proposes the baseline count, or the range minimum.
    public static UsesControl Control(StratagemField f, JsonElement current)
    {
        var baseUnlimited = IsUnlimited(f.CurrentDefault); var unlimited = IsUnlimited(current);
        var canUnlimited = baseUnlimited || f.Transitions?.Contains(FiniteToUnlimited) == true;
        var canLimited = !baseUnlimited || f.Transitions?.Contains(UnlimitedToFinite) == true;
        var countEditable = baseUnlimited ? canLimited : f.Transitions?.Contains(FiniteToFinite) == true;
        var count = !unlimited ? current.GetInt32() : baseUnlimited ? (int)(f.Min ?? 1) : f.CurrentDefault.GetInt32();
        return new(unlimited, canUnlimited, canLimited, countEditable, count);
    }
    public static string Lua(JsonElement v) => IsUnlimited(v) ? "'" + Unlimited + "'" : v.GetRawText();
    public static string Text(JsonElement v) => IsUnlimited(v) ? "Unlimited" : v.GetRawText();
}
