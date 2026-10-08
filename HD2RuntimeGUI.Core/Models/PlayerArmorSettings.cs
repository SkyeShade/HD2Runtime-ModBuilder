using System.Text.Json.Serialization;

namespace HD2RuntimeGUI.Core.Models;

// The local player's own armor values and passives (HD2Runtime 0.30.0, solo only; sdk/docs/armor-stats.md "The local player" and
// sdk/docs/armor-passives.md "The write API"). Not field patches: the generated mod calls hd2.armor_stats.player():set at load and after
// every respawn, and hd2.player_passives.set once (its handle keeps the override in place). Each member is omitted when not set.
public sealed class PlayerArmorSettings
{
    // Added to the armor value of every hit on the local player (-1..3 in the bound SDK).
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? ArmorBonus { get; set; }
    // The worn armor's stamina factor F: scales stamina drain and regen (0.1..3 in the bound SDK).
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? StaminaFactor { get; set; }
    // The passive (game id) that replaces the armor passive; null keeps the kit's own.
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? PassiveArmor { get; set; }
    // The passive (game id) added in the helmet slot; null: none (second = false).
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? PassiveSecond { get; set; }
    [JsonIgnore] public bool HasStats => ArmorBonus != null || StaminaFactor != null;
    [JsonIgnore] public bool HasPassives => PassiveArmor != null || PassiveSecond != null;
    [JsonIgnore] public bool IsEmpty => !HasStats && !HasPassives;
}
