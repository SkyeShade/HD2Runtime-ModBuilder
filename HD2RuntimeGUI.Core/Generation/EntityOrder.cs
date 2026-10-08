using System.Globalization;
using System.Text.Json;
using HD2RuntimeGUI.Core.Localization;
using HD2RuntimeGUI.Core.Metadata;
using HD2RuntimeGUI.Core.Models;

namespace HD2RuntimeGUI.Core.Generation;

/// <summary>
/// Ordered field pairs (HD2Runtime 0.30.0 <c>order: [low, high]</c>; today the mounted turret limits turret.pitch_min / pitch_max and
/// turret.yaw_min / yaw_max). Runtime refuses an operation that leaves a pair with low &gt;= high (TURRET_LIMIT_ORDER,
/// domains/player_weapon_writes.lua); a limit the operation does not write keeps its reviewed value. ModBuilder writes every turret field of a
/// mount in one transaction, so a pair is checked with each side's enabled edit, else its baseline, exactly as Runtime checks it.
/// </summary>
public static class EntityOrder
{
    public sealed record Pair(EntityField Low, EntityField High);

    // The other field of a field's published pair: same target, the other semantic ID of its order.
    public static Pair? Of(EntityAuthoring catalog, EntityField f)
    {
        if (f.Order is not [var low, var high]) return null;
        var candidates = f.Target.Resource switch
        {
            "vehicle_weapon" => catalog.VehicleWeapons?.FieldInstances ?? [],
            "vehicle" => catalog.Vehicles.FieldInstances,
            "backpack" => catalog.Backpacks.FieldInstances,
            _ => [],
        };
        EntityField? Find(string id) => id == f.SemanticFieldId ? f : candidates.FirstOrDefault(x => x.Target == f.Target && x.SemanticFieldId == id);
        return Find(low) is { } l && Find(high) is { } h ? new(l, h) : null;
    }
    // The value Runtime checks for one side: the enabled edit's value, else the reviewed baseline.
    public static double Effective(ModProject? p, EntityField f) =>
        (EntityChangeService.Saved(p, f) is { Enabled: true } c ? c.DesiredValue : f.CurrentDefault) is { ValueKind: JsonValueKind.Number } v ? v.GetDouble() : double.NaN;
    public static bool Ordered(double low, double high) => !double.IsFinite(low) || !double.IsFinite(high) || low < high;
    public static bool Ordered(ModProject? p, Pair pair) => Ordered(Effective(p, pair.Low), Effective(p, pair.High));
    // What the pair would be with one side at a new value (the other keeps its effective value).
    public static (double Low, double High) With(ModProject? p, Pair pair, EntityField f, double value) =>
        f.InstanceKey == pair.Low.InstanceKey ? (value, Effective(p, pair.High)) : (Effective(p, pair.Low), value);

    /// <summary>Refuses a project whose enabled edits leave the field's pair unordered (build time; shown inline on both fields).</summary>
    public static void Check(ModProject p, EntityAuthoring catalog, EntityField f)
    {
        if (Of(catalog, f) is not { } pair) return;
        var (low, high) = (Effective(p, pair.Low), Effective(p, pair.High));
        if (!Ordered(low, high)) throw Unordered(pair, low, high);
    }
    /// <summary>One in-game option sample for this field, with the other side at its effective value.</summary>
    public static void CheckValue(ModProject p, EntityAuthoring catalog, EntityField f, double value)
    {
        if (Of(catalog, f) is not { } pair) return;
        var (low, high) = With(p, pair, f, value);
        if (!Ordered(low, high)) throw Unordered(pair, low, high);
    }
    private static InvalidDataException Unordered(Pair pair, double low, double high) => new(CoreText.Format("Messages.Build.Entity.LimitOrder",
        pair.Low.DisplayName, Text(low), pair.High.DisplayName, Text(high)));
    public static string Text(double v) => ((float)v).ToString("R", CultureInfo.InvariantCulture);
}
