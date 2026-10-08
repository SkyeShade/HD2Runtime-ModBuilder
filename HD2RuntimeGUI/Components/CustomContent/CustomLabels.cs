using HD2RuntimeGUI.Core.CustomContent;
using HD2RuntimeGUI.Core.Localization;
using HD2RuntimeGUI.Core.Models;

namespace HD2RuntimeGUI.Components.CustomContent;

/// <summary>Display text for custom content ids (families, beacons, carrier families and groups, severities, areas, graph roles, units). The ids
/// themselves are ModBuilder's or Runtime's; only their labels are translated. A value this build has no label for is shown as written.</summary>
public static class CustomLabels
{
    public static string Family(IUiText t, string family) => family switch
    {
        DeliveryFamily.Support => t["CustomContent.Family.Support"],
        DeliveryFamily.Pod => t["CustomContent.Family.Pod"],
        DeliveryFamily.Expendable => t["CustomContent.Family.Expendable"],
        DeliveryFamily.Variant => t["CustomContent.Family.Variant"],
        DeliveryFamily.Sentry => t["CustomContent.Family.Sentry"],
        DeliveryFamily.Orbital => t["CustomContent.Family.Orbital"],
        DeliveryFamily.Eagle => t["CustomContent.Family.Eagle"],
        DeliveryFamily.Pelican => t["CustomContent.Family.Pelican"],
        DeliveryFamily.Silo => t["CustomContent.Family.Silo"],
        DeliveryFamily.Scripted => t["CustomContent.Family.Scripted"],
        _ => family,
    };
    public static string FamilyHelp(IUiText t, string family) => family switch
    {
        DeliveryFamily.Support => t["CustomContent.Family.Support.Help"],
        DeliveryFamily.Pod => t["CustomContent.Family.Pod.Help"],
        DeliveryFamily.Expendable => t["CustomContent.Family.Expendable.Help"],
        DeliveryFamily.Variant => t["CustomContent.Family.Variant.Help"],
        DeliveryFamily.Sentry => t["CustomContent.Family.Sentry.Help"],
        DeliveryFamily.Orbital => t["CustomContent.Family.Orbital.Help"],
        DeliveryFamily.Eagle => t["CustomContent.Family.Eagle.Help"],
        DeliveryFamily.Pelican => t["CustomContent.Family.Pelican.Help"],
        DeliveryFamily.Silo => t["CustomContent.Family.Silo.Help"],
        _ => t["CustomContent.Family.Scripted.Help"],
    };
    // The glyph a family's card shows (no game art: the families are the Runtime's, not the game's).
    public static string FamilyGlyph(string family) => family switch
    {
        DeliveryFamily.Orbital => "◎", DeliveryFamily.Eagle => "✈", DeliveryFamily.Pelican => "⛟", DeliveryFamily.Support => "▣", DeliveryFamily.Expendable => "➹",
        DeliveryFamily.Pod => "▦", DeliveryFamily.Variant => "⚙", DeliveryFamily.Sentry => "⌖", DeliveryFamily.Silo => "⇡", _ => "{ }",
    };
    // The stratagem category colour a family's icon is drawn in (app.css --cat-*): a support beacon blue, a sentry green, the others red.
    public static string FamilyCss(string family) => family switch
    {
        DeliveryFamily.Support or DeliveryFamily.Pod or DeliveryFamily.Expendable or DeliveryFamily.Variant or DeliveryFamily.Silo => "cat-support",
        DeliveryFamily.Sentry => "cat-defensive",
        _ => "cat-offensive",
    };
    // What Runtime has shown in play for a family (CustomStratagemSchema familyEvidence): live, partial or offline, and its multiplayer status.
    public static string Evidence(IUiText t, string status) => status switch
    {
        "live" => t["CustomContent.Evidence.Live"], "partial" => t["CustomContent.Evidence.Partial"], "offline" => t["CustomContent.Evidence.Offline"], _ => status,
    };
    public static string EvidenceCss(string status) => status switch { "live" => "ready", "partial" => "warning", _ => "error" };
    public static string Multiplayer(IUiText t, string status) => status switch
    {
        "live" => t["CustomContent.Evidence.MpLive"], "host only" => t["CustomContent.Evidence.MpHost"], "pending" => t["CustomContent.Evidence.MpPending"],
        "untested" => t["CustomContent.Evidence.MpUntested"], "unsupported" => t["CustomContent.Evidence.MpUnsupported"], "per mod" => t["CustomContent.Evidence.MpPerMod"],
        _ => status,
    };
    public static string Group(IUiText t, string group) => group switch
    {
        "orbital" => t["CustomContent.Group.Orbital"], "any_red" => t["CustomContent.Group.AnyRed"], "any" => t["CustomContent.Group.Any"],
        "support" => t["CustomContent.Group.Support"], "support_pod" => t["CustomContent.Group.SupportPod"], "expendable" => t["CustomContent.Group.Expendable"],
        "weapon" => t["CustomContent.Group.Weapon"], "sentry" => t["CustomContent.Group.Sentry"], "emplacement" => t["CustomContent.Group.Emplacement"],
        "eagle" => t["CustomContent.Group.Eagle"],
        _ => group,
    };
    public static string Beacon(IUiText t, string beacon) => beacon switch
    {
        CustomCarrier.Offensive => t["CustomContent.Beacon.Offensive"],
        CustomCarrier.Support => t["CustomContent.Beacon.Support"],
        CustomCarrier.Any => t["CustomContent.Beacon.Any"],
        _ => beacon,
    };
    public static string CarrierFamily(IUiText t, string family) => family switch
    {
        "backpack" => t["CustomContent.Carrier.Backpack"], "eagle" => t["CustomContent.Carrier.Eagle"], "emplacement" => t["CustomContent.Carrier.Emplacement"],
        "mine" => t["CustomContent.Carrier.Mine"], "mission" => t["CustomContent.Carrier.Mission"], "orbital" => t["CustomContent.Carrier.Orbital"],
        "sentry" => t["CustomContent.Carrier.Sentry"], "support" => t["CustomContent.Carrier.Support"], "vehicle" => t["CustomContent.Carrier.Vehicle"],
        _ => family,
    };
    public static string Mark(CustomSeverity severity) => severity switch
    {
        CustomSeverity.Pass => "✓", CustomSeverity.Info => "ℹ", CustomSeverity.Experimental => "⚗", CustomSeverity.Warning => "⚠", _ => "✕",
    };
    public static string SeverityCss(CustomSeverity severity) => severity switch
    {
        CustomSeverity.Pass => "pass", CustomSeverity.Info => "info", CustomSeverity.Experimental => "experimental", CustomSeverity.Warning => "warning", _ => "error",
    };
    public static string Area(IUiText t, string area) => area switch
    {
        CustomDiagnostic.Identity => t["CustomContent.Area.Identity"],
        CustomDiagnostic.CallIn => t["CustomContent.Area.CallIn"],
        CustomDiagnostic.Carrier => t["CustomContent.Area.Carrier"],
        CustomDiagnostic.Delivery => t["CustomContent.Area.Delivery"],
        CustomDiagnostic.Assets => t["CustomContent.Area.Assets"],
        CustomDiagnostic.Advanced => t["CustomContent.Area.Advanced"],
        CustomDiagnostic.Multiplayer => t["CustomContent.Area.Multiplayer"],
        CustomDiagnostic.Projectile => t["CustomContent.Area.Projectile"],
        CustomDiagnostic.Assignment => t["CustomContent.Area.Assignment"],
        _ => t["CustomContent.Area.Build"],
    };
    // The editor section a diagnostic area belongs to (its anchor id), so a problem anywhere can be jumped to.
    public static string Section(string area) => area switch
    {
        CustomDiagnostic.Identity => "identity", CustomDiagnostic.CallIn => "callin", CustomDiagnostic.Carrier => "carrier", CustomDiagnostic.Assets => "delivery",
        CustomDiagnostic.Delivery => "delivery", CustomDiagnostic.Advanced => "lua", _ => "checks",
    };
    public static string Role(IUiText t, string role) => role switch
    {
        "icon" => t["CustomContent.Role.Icon"], "weaponIcon" => t["CustomContent.Role.WeaponIcon"], "model" => t["CustomContent.Role.Model"],
        "donor" => t["CustomContent.Role.Donor"], "item" => t["CustomContent.Role.Item"], "pattern" => t["CustomContent.Role.Pattern"],
        "round" => t["CustomContent.Role.Round"], "payload" => t["CustomContent.Role.Payload"], "blast" => t["CustomContent.Role.Blast"], "asset" => t["CustomContent.Role.Asset"],
        "base" => t["CustomProjectiles.Base"], "visual" => t["CustomProjectiles.Component.Visual"], "damage" => t["CustomProjectiles.Component.Damage"],
        "impactExplosion" => t["CustomProjectiles.Component.ImpactExplosion"], "ballistics" => t["CustomProjectiles.Component.Ballistics"],
        "fires" => t["CustomContent.Role.Fires"],
        _ => role,
    };
    // Runtime's published units, shortened; unknown units are shown as published.
    public static string Unit(IUiText t, string? unit) => unit switch
    {
        null => "",
        "seconds" or "seconds from the call-in's arrival" or "seconds over the beacon" => t["CustomContent.Unit.Seconds"],
        "rounds per minute" => t["CustomContent.Unit.Rpm"],
        "milliradians (full width)" => t["CustomContent.Unit.Mrad"],
        "rounds per magazine" => t["CustomContent.Unit.Rounds"],
        "projectiles per weapon" => t["CustomContent.Unit.PerWeapon"],
        "uses per rearm" => t["CustomContent.Unit.Uses"],
        "calls per mission" => t["CustomContent.Unit.Calls"],
        "the pattern record's own units" => t["CustomContent.Unit.PatternUnits"],
        "metres" or "metres above the target's feet" => t["CustomContent.Unit.Metres"],
        "items" or "pod items" => t["CustomContent.Unit.Items"],
        _ => unit,
    };
    public static string Arrow(string direction) => direction switch { "up" => "↑", "down" => "↓", "left" => "←", "right" => "→", _ => "?" };
    public static string ArrowLabel(IUiText t, string direction) => direction switch
    {
        "up" => t["CustomContent.Direction.Up"], "down" => t["CustomContent.Direction.Down"], "left" => t["CustomContent.Direction.Left"],
        "right" => t["CustomContent.Direction.Right"], _ => direction,
    };
    public static string State(IUiText t, CustomSeverity state) => state switch
    {
        CustomSeverity.Error => t["CustomContent.State.Error"], CustomSeverity.Warning => t["CustomContent.State.Warning"], _ => t["CustomContent.State.Ready"],
    };
    public static string StateCss(CustomSeverity state) => state switch { CustomSeverity.Error => "error", CustomSeverity.Warning => "warning", _ => "ready" };
    public static string Number(double? value) => value?.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) ?? "";
}
