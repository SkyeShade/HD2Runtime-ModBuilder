using System.Text.Json;
using HD2RuntimeGUI.Core.Localization;
using HD2RuntimeGUI.Core.Metadata;
using HD2RuntimeGUI.Core.Models;

namespace HD2RuntimeGUI.Core.Generation;

/// <summary>
/// HD2Runtime 0.30.0 support-weapon charge (docs/support-weapon-api.md "Charge" and "Charge-level shots and explosions"): the charge stages
/// the editor groups the charge.* fields by, the charge-time order Runtime notes (CHARGE ORDER, accepted), fields that write the same native
/// bytes (the PLAS-45 Epoch's full-charge and overcharge explosions share one damage row; a deprecated charge id and the id it aliases), and
/// the overcharge explosion reference, which makes Runtime refuse edits of the weapon's own overcharge explosion row while it names another
/// weapon's. Ranges and meanings stay on the published charge object (SupportField.Charge); nothing here invents a value.
/// </summary>
public static class ChargeFields
{
    public const string Prefix = "charge.", OverchargeExplosion = "charge.overcharge_explosion", OverchargeRole = "overcharge_explosion";
    public const string MinimumTime = "charge.level_1", FullTime = "charge.level_2", OverchargeTime = "charge.level_3";
    // Deprecated since 0.30.0: published as times, they are the projectile speed multipliers at the minimum charge and at full overcharge.
    public const string LegacyMinimum = "charge.minimum_seconds", LegacyMaximum = "charge.maximum_seconds";
    public const string ExplodeAtOvercharge = "charge.explode_at_overcharge", OverchargeLimit = "charge.overcharge_limit_seconds";
    public enum Stage { Minimum, Full, Overcharge, Burst, Deprecated, Other }
    // Editor order: each stage's time first, then what it scales.
    private static readonly string[] Ordered =
    [
        MinimumTime, "charge.speed_multiplier_min", "charge.damage_multiplier_min", "charge.penetration_multiplier_min", "charge.arc_distance_multiplier_min",
        FullTime, "charge.auto_fire_at_full",
        OverchargeTime, "charge.speed_multiplier_overcharge", "charge.damage_multiplier_overcharge", "charge.penetration_multiplier_overcharge",
        "charge.arc_distance_multiplier_overcharge", ExplodeAtOvercharge, OverchargeLimit, OverchargeExplosion,
        "charge.burst_shots", "charge.burst_interval_seconds",
        LegacyMinimum, LegacyMaximum,
    ];
    public static bool Is(SupportField f) => f.Target.Path == "weapon" && f.SemanticFieldId.StartsWith(Prefix, StringComparison.Ordinal);
    public static int Rank(string id) => Array.IndexOf(Ordered, id) is var i and >= 0 ? i : Ordered.Length;
    public static Stage StageOf(string id) => id switch
    {
        MinimumTime or "charge.speed_multiplier_min" or "charge.damage_multiplier_min" or "charge.penetration_multiplier_min" or "charge.arc_distance_multiplier_min" => Stage.Minimum,
        FullTime or "charge.auto_fire_at_full" => Stage.Full,
        OverchargeTime or "charge.speed_multiplier_overcharge" or "charge.damage_multiplier_overcharge" or "charge.penetration_multiplier_overcharge"
            or "charge.arc_distance_multiplier_overcharge" or ExplodeAtOvercharge or OverchargeLimit or OverchargeExplosion => Stage.Overcharge,
        "charge.burst_shots" or "charge.burst_interval_seconds" => Stage.Burst,
        LegacyMinimum or LegacyMaximum => Stage.Deprecated,
        _ => Stage.Other,
    };
    // A deprecated id (0.30.0 publishes deprecated / aliasOf / notices; on older SDKs the two legacy ids are known by name).
    public static bool Deprecated(SupportField f) => f.Charge?.Deprecated == true || f.SemanticFieldId is LegacyMinimum or LegacyMaximum;
    public static bool Dormant(SupportField f) => f.Charge?.Dormant == true || f.Charge?.Notices?.Any(n => n.Kind == "dormant") == true;
    // Writes that can destroy the weapon in the wielder's hands (Runtime's "hazard" acknowledgement reason).
    public static bool Hazard(SupportField f) => f.SemanticFieldId is ExplodeAtOvercharge or OverchargeLimit;
    public static bool IsTime(string id) => id is MinimumTime or FullTime or OverchargeTime;
    // An edit saved against an SDK older than 0.30.0, which published charge.level_* as "Charge level" multipliers and the legacy ids as times.
    public static bool SavedBefore030(SupportChange c) => !AtLeast030(c.BaselineSdkVersion) && !string.IsNullOrEmpty(c.BaselineSdkVersion);
    // Whether an SDK version carries the 0.30.0 behaviour (0.30.0-dev and later); false for null or malformed versions.
    public static bool AtLeast030(string? version)
    {
        if (string.IsNullOrEmpty(version)) return false;
        try { return SemVersion.Parse(version).CompareTo(SemVersion.Parse(RuntimeRequirement.First030)) >= 0; }
        catch (FormatException) { return false; }
    }

    /// <summary>The weapon's charge.* fields in editor order.</summary>
    public static IReadOnlyList<SupportField> Of(SupportAuthoringCatalog catalog, string weapon) =>
        catalog.FieldInstances.Where(f => f.SupportWeapon == weapon && Is(f)).OrderBy(f => Rank(f.SemanticFieldId)).ThenBy(f => f.SemanticFieldId, StringComparer.Ordinal).ToArray();

    /// <summary>
    /// The other fields of the same weapon that write this field's native bytes: the same field of the same backing object through another
    /// attack role (the Epoch's full_charge_impact and overcharge_explosion damage), or a deprecated charge id and the id it aliases. One
    /// operation may write them: two writes of the same bytes overlap in a transaction or plan, and conflict across operations.
    /// </summary>
    public static IReadOnlyList<SupportField> SameBytes(SupportAuthoringCatalog catalog, SupportField f) => ByObject(catalog).GetValueOrDefault(f.Backing.ObjectKey, [])
        .Where(o => o.InstanceKey != f.InstanceKey && o.SupportWeapon == f.SupportWeapon
            && (o.ApiFieldConstant == f.ApiFieldConstant || f.Charge?.AliasOf == o.SemanticFieldId || o.Charge?.AliasOf == f.SemanticFieldId))
        .ToArray();
    // Fields by backing object, once per loaded catalog (every support row asks while it renders).
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<SupportAuthoringCatalog, Dictionary<string, SupportField[]>> Objects = new();
    private static Dictionary<string, SupportField[]> ByObject(SupportAuthoringCatalog catalog) => Objects.GetValue(catalog,
        c => c.FieldInstances.GroupBy(f => f.Backing.ObjectKey, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.ToArray(), StringComparer.Ordinal));

    /// <summary>The weapon's charge.overcharge_explosion field, when its SDK publishes one.</summary>
    public static SupportField? OverchargeReference(SupportAuthoringCatalog catalog, string weapon) =>
        catalog.FieldInstances.FirstOrDefault(f => f.SupportWeapon == weapon && f.IsOverchargeExplosion);
    /// <summary>Whether a value of the overcharge explosion reference names another weapon's explosion than the reviewed one.</summary>
    public static bool Swaps(SupportField reference, JsonElement value) => !JsonElement.DeepEquals(value, reference.Value.Baseline);
    /// <summary>
    /// The edits Runtime refuses while the weapon's overcharge explosion names another weapon's: attack('overcharge_explosion') resolves its row
    /// through that reference, and a write that would reach a different row is refused (docs/support-weapon-api.md, "Swapped references refuse").
    /// </summary>
    public static bool RefusedWhileSwapped(SupportField f) => f.Target.AttackRole == OverchargeRole && f.Target.Path != "weapon";

    /// <summary>The weapon's charge times after the project's enabled edits (each at its reviewed value otherwise), or null without all three.</summary>
    public static ChargeTimes? Times(SupportAuthoringCatalog catalog, IEnumerable<SupportChange> changes, string weapon)
    {
        double? Time(string id)
        {
            if (catalog.FieldInstances.FirstOrDefault(f => f.SupportWeapon == weapon && f.SemanticFieldId == id && f.Target.Path == "weapon") is not { } f) return null;
            var value = changes.FirstOrDefault(c => c.Enabled && c.InstanceKey == f.InstanceKey)?.DesiredValue ?? f.Value.Baseline;
            return value.ValueKind == JsonValueKind.Number ? (float)value.GetDouble() : null;
        }
        return Time(MinimumTime) is { } min && Time(FullTime) is { } full && Time(OverchargeTime) is { } over ? new(min, full, over) : null;
    }

    /// <summary>
    /// Saved edits Runtime would refuse or overlap: two enabled edits of the same native bytes, or an edit of a weapon's own overcharge explosion
    /// while its overcharge explosion reference names another weapon's. Each edit involved is listed with the message for its row.
    /// </summary>
    public static IReadOnlyList<(Guid Id, string Message)> Issues(SdkMetadata sdk, IReadOnlyList<SupportChange> changes)
    {
        if (sdk.SupportAuthoring is not { } catalog) return [];
        var relevant = changes.Where(c => c.Enabled && Relevant(c)).ToArray();
        if (relevant.Length == 0) return [];
        var fields = relevant.Select(c => (Change: c, Field: catalog.FieldInstances.FirstOrDefault(f => f.InstanceKey == c.InstanceKey))).Where(x => x.Field != null).ToArray();
        var result = new List<(Guid, string)>();
        foreach (var (change, field) in fields)
        {
            var same = SameBytes(catalog, field!).Select(o => o.InstanceKey).ToHashSet(StringComparer.Ordinal);
            foreach (var other in fields.Where(x => same.Contains(x.Field!.InstanceKey)))
                result.Add((change.Id, CoreText.Format("Messages.Build.Support.SameBytes", Label(field!), Label(other.Field!), field!.SupportWeapon)));
            if (RefusedWhileSwapped(field!) && OverchargeReference(catalog, field!.SupportWeapon) is { } reference
                && fields.FirstOrDefault(x => x.Field!.InstanceKey == reference.InstanceKey) is { Change: { } swap } && Swaps(reference, swap.DesiredValue))
            {
                var message = CoreText.Format("Messages.Build.Support.OverchargeSwapped", field.SupportWeapon, swap.DesiredValue.GetString(), Label(field));
                result.Add((change.Id, message)); result.Add((swap.Id, message));
            }
        }
        return result.DistinctBy(r => (r.Item1, r.Item2)).ToArray();
    }
    // Only charge fields and the 0.30.0 roles take part (keeps SupportIssue cheap on large projects).
    public static bool Relevant(SupportChange c) => c.SemanticFieldId.StartsWith(Prefix, StringComparison.Ordinal)
        || c.AttackRole is { } role && AuthoredTypes.SupportRoles030.Contains(role);
    // Runtime identifiers in build messages: the field and its attack role (as hd2.support_weapon(name):attack(role) names it).
    private static string Label(SupportField f) => f.Target.AttackRole is { } role ? f.Display.Name + " · " + role : f.SemanticFieldId;
}

/// <summary>A weapon's charge times in seconds: minimum, full and overcharge. Runtime expects them increasing and notes it when they are not.</summary>
public sealed record ChargeTimes(double Minimum, double Full, double Overcharge)
{
    public bool FullNotAboveMinimum => Full <= Minimum;
    public bool OverchargeNotAboveFull => Overcharge <= Full;
    public bool Increasing => !FullNotAboveMinimum && !OverchargeNotAboveFull;
}
