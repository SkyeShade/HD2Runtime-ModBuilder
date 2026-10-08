using HD2RuntimeGUI.Core.Generation;
using HD2RuntimeGUI.Core.Localization;
using HD2RuntimeGUI.Core.Metadata;

namespace HD2RuntimeGUI.Components;

// HD2Runtime 0.30.0 support-weapon charge in the UI language: field, stage and charge-level role names. The ids and Runtime's prose
// (meaning, rangeReason, notices, chargeLevel) stay as published; only the words ModBuilder adds are translated. Charge field names are
// ModBuilder's for every SDK, so a project still bound to an older SDK shows charge.level_* as the charge times they always were.
internal static class ChargeText
{
    public static string Label(IUiText t, SupportField f) => f.SemanticFieldId switch
    {
        ChargeFields.MinimumTime => t["Charge.Field.MinimumTime"],
        ChargeFields.FullTime => t["Charge.Field.FullTime"],
        ChargeFields.OverchargeTime => t["Charge.Field.OverchargeTime"],
        "charge.speed_multiplier_min" => t["Charge.Field.SpeedMin"],
        "charge.speed_multiplier_overcharge" => t["Charge.Field.SpeedOvercharge"],
        "charge.damage_multiplier_min" => t["Charge.Field.DamageMin"],
        "charge.damage_multiplier_overcharge" => t["Charge.Field.DamageOvercharge"],
        "charge.penetration_multiplier_min" => t["Charge.Field.PenetrationMin"],
        "charge.penetration_multiplier_overcharge" => t["Charge.Field.PenetrationOvercharge"],
        "charge.arc_distance_multiplier_min" => t["Charge.Field.ArcMin"],
        "charge.arc_distance_multiplier_overcharge" => t["Charge.Field.ArcOvercharge"],
        "charge.auto_fire_at_full" => t["Charge.Field.AutoFire"],
        ChargeFields.ExplodeAtOvercharge => t["Charge.Field.Explode"],
        ChargeFields.OverchargeLimit => t["Charge.Field.Limit"],
        ChargeFields.OverchargeExplosion => t["Charge.Field.Explosion"],
        "charge.burst_shots" => t["Charge.Field.BurstShots"],
        "charge.burst_interval_seconds" => t["Charge.Field.BurstInterval"],
        ChargeFields.LegacyMinimum => t["Charge.Field.LegacyMinimum"],
        ChargeFields.LegacyMaximum => t["Charge.Field.LegacyMaximum"],
        _ => f.Display.Name,
    };
    // Units ModBuilder knows for the charge ids (an older SDK published charge.level_* as multipliers and the legacy ids as seconds).
    public static string? Unit(IUiText t, SupportField f) => f.SemanticFieldId switch
    {
        ChargeFields.MinimumTime or ChargeFields.FullTime or ChargeFields.OverchargeTime or ChargeFields.OverchargeLimit or "charge.burst_interval_seconds" => t["Charge.Unit.Seconds"],
        ChargeFields.LegacyMinimum or ChargeFields.LegacyMaximum => t["Charge.Unit.Multiplier"],
        _ when f.SemanticFieldId.Contains("_multiplier_", StringComparison.Ordinal) => t["Charge.Unit.Multiplier"],
        "charge.burst_shots" => t["Charge.Unit.Shots"],
        _ => f.Value.Unit?.Replace('_', ' '),
    };
    public static string Stage(IUiText t, ChargeFields.Stage stage) => stage switch
    {
        ChargeFields.Stage.Minimum => t["Charge.Stage.Minimum"],
        ChargeFields.Stage.Full => t["Charge.Stage.Full"],
        ChargeFields.Stage.Overcharge => t["Charge.Stage.Overcharge"],
        ChargeFields.Stage.Burst => t["Charge.Stage.Burst"],
        ChargeFields.Stage.Deprecated => t["Charge.Stage.Deprecated"],
        _ => t["Charge.Stage.Other"],
    };
    public static string? StageHint(IUiText t, ChargeFields.Stage stage) => stage switch
    {
        ChargeFields.Stage.Minimum => t["Charge.Stage.Minimum.Hint"],
        ChargeFields.Stage.Full => t["Charge.Stage.Full.Hint"],
        ChargeFields.Stage.Overcharge => t["Charge.Stage.Overcharge.Hint"],
        ChargeFields.Stage.Burst => t["Charge.Stage.Burst.Hint"],
        ChargeFields.Stage.Deprecated => t["Charge.Stage.Deprecated.Hint"],
        _ => null,
    };
    // The charge level that fires an attack row (0.30.0 chargeLevel.level).
    public static string Level(IUiText t, string level) => level switch
    {
        "partial" => t["Charge.Level.Partial"], "full" => t["Charge.Level.Full"], "overcharge" => t["Charge.Level.Overcharge"], _ => level,
    };
    public static string Family(IUiText t, string family) => family switch
    {
        "projectile" => t["Charge.Family.Projectile"], "arc" => t["Charge.Family.Arc"], "beam" => t["Charge.Family.Beam"], _ => family,
    };
    // A field named in an inline prompt: charge fields by their name, attack rows by field and Runtime role.
    public static string Named(IUiText t, SupportField f) => ChargeFields.Is(f) ? Label(t, f)
        : f.Target.AttackRole is { } role ? t.Format("Charge.Named.Role", f.Display.Name, role) : f.Display.Name;
}
