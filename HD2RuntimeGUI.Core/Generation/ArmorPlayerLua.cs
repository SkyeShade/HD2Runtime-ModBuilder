using System.Globalization;
using HD2RuntimeGUI.Core.Localization;
using HD2RuntimeGUI.Core.Metadata;
using HD2RuntimeGUI.Core.Models;

namespace HD2RuntimeGUI.Core.Generation;

/// <summary>
/// The local player's own armor values and passive override (ModProject.PlayerArmor; HD2Runtime 0.30.0 development, solo only). These are
/// calls a mod makes, not field patches:
/// - hd2.player_passives.set({armor, second, allow_unverified_effect}) once at load (sdk/docs/armor-passives.md "The write API"). Its handle keeps
///   the override in place by itself: it waits for the player's record, re-writes the slots after every armor or helmet change, suspends while
///   another player is present and resumes. allow_unverified_effect is left out only for an armor-slot swap alone to a live-proven passive.
/// - hd2.armor_stats.player():set({armor_bonus, stamina_factor, allow_unverified_effect = true}) at load and again on every local
///   player_spawned (sdk/docs/armor-stats.md "The local player"): the game writes the kit's stamina factor again at every armor apply (a respawn
///   or an armor change), and a set made before the player has an avatar is refused. describe() first, so a load in the menus logs no refusal.
/// A refusal never raises: Runtime logs it (NOT_SOLO with several players, UNEXPECTED_STATE when a status effect holds the member).
/// </summary>
public static class ArmorPlayerLua
{
    public static IReadOnlyList<string> Operations(ModProject project, SdkMetadata sdk)
    {
        if (project.PlayerArmor is not { } armor || armor.IsEmpty) return [];
        Validate(armor, sdk);
        var catalog = sdk.Entities!.Armor!;
        var output = new List<string>();
        if (armor.HasPassives) output.Add(PassiveCall(armor, catalog.Passives!));
        if (armor.HasStats) output.Add(PlayerCall(armor));
        return output;
    }
    public static string PassiveCall(PlayerArmorSettings armor, ArmorPassiveCatalog passives)
    {
        var parts = new List<string>();
        if (armor.PassiveArmor is { } a) parts.Add("armor=" + LuaGenerator.Quote(passives.Find(a)!.Name));
        if (armor.PassiveSecond is { } s) parts.Add("second=" + LuaGenerator.Quote(passives.Find(s)!.Name));
        if (NeedsUnverified(armor, passives)) parts.Add("allow_unverified_effect=true");
        return "hd2.player_passives.set({" + string.Join(",", parts) + "})";
    }
    public static string PlayerCall(PlayerArmorSettings armor)
    {
        var parts = new List<string>();
        if (armor.ArmorBonus is { } b) parts.Add("armor_bonus=" + Number(b));
        if (armor.StaminaFactor is { } f) parts.Add("stamina_factor=" + Number(f));
        parts.Add("allow_unverified_effect=true");
        return "(function()\n"
            + "    local player=hd2.armor_stats.player()\n"
            + "    local function apply()\n"
            + "        if not player:describe() then return nil end\n"
            + "        return player:set({" + string.Join(",", parts) + "})\n"
            + "    end\n"
            + "    -- The game applies the kit's own stamina factor again at every respawn: set the values again then.\n"
            + "    hd2.events.on('player_spawned',function(e) if e.local_player then apply() end end)\n"
            + "    return apply()\n"
            + "end)()";
    }
    // Every override but an armor-slot swap alone to a live-proven passive (SCOUT, ENGINEERING KIT, MED-KIT, SERVO-ASSISTED) is unverified.
    public static bool NeedsUnverified(PlayerArmorSettings armor, ArmorPassiveCatalog passives) =>
        !(armor.PassiveSecond == null && armor.PassiveArmor is { } a && passives.LiveProvenArmorSwap(a));
    public static string Number(double v) => v.ToString("R", CultureInfo.InvariantCulture);

    /// <summary>What hd2.armor_stats.player():set and hd2.player_passives.set would refuse before any game state is read: a value outside the
    /// published range, an unknown passive, STANDARD ISSUE or an armor-slot-only passive as the second passive, or the same passive twice.</summary>
    public static void Validate(PlayerArmorSettings armor, SdkMetadata sdk)
    {
        var catalog = sdk.Entities?.Armor ?? throw new InvalidDataException(CoreText.Get("Messages.Build.Armor.SdkMissing"));
        void Range(double? value, string option, string label)
        {
            if (value is not { } v) return;
            var r = catalog.Range("player." + option);
            if (!double.IsFinite(v) || v < r[0] || v > r[1])
                throw new InvalidDataException(CoreText.Format("Messages.Build.Armor.PlayerRange", label, r[0].ToString(CultureInfo.InvariantCulture), r[1].ToString(CultureInfo.InvariantCulture)));
        }
        Range(armor.ArmorBonus, "armor_bonus", "armor_bonus");
        Range(armor.StaminaFactor, "stamina_factor", "stamina_factor");
        if (!armor.HasPassives) return;
        var passives = catalog.Passives ?? throw new InvalidDataException(CoreText.Get("Messages.Build.Armor.PassivesMissing"));
        if (armor.PassiveArmor is { } a && passives.Find(a) == null || armor.PassiveSecond is { } s0 && passives.Find(s0) == null)
            throw new InvalidDataException(CoreText.Get("Messages.Build.Armor.UnknownPassive"));
        if (armor.PassiveSecond is { } s)
        {
            var second = passives.Find(s)!;
            if (!second.AllowedAsSecond) throw new InvalidDataException(CoreText.Format("Messages.Build.Armor.SecondRefused", second.Name, second.SecondRefusal));
            if (s == armor.PassiveArmor) throw new InvalidDataException(CoreText.Get("Messages.Build.Armor.SamePassive"));
        }
    }
}
