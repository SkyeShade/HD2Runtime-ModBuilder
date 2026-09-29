namespace HD2RuntimeGUI.Core.Models;

// Format 11: a player attack that fires another catalogued output (hd2.attack_output), written through the attack's active projectile
// source: its own reference ("component", ACTIVE_DIRECT attacks) or its default ammunition's projectile ("ammunition", INDIRECT weapons
// such as the AR-23 Liberator). Identities are semantic; the opt-ins the write carries are kept with it.
public sealed record AttackOutputChange
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Weapon { get; init; } = "";
    public string AttackRole { get; init; } = "";
    public string Mechanism { get; init; } = "";
    public string Output { get; init; } = "";
    public string OutputName { get; init; } = "";
    // Runtime opt-ins the write carries (allow_shared, allow_unverified_effect, allow_unverified_reference), as published for this pair.
    public string[] Acknowledgements { get; init; } = [];
    public string Evidence { get; init; } = "";
    public string BaselineSdkVersion { get; init; } = "";
    public bool Enabled { get; init; } = true;
    public bool EnsureEnabled { get; init; } = true;
    public string Group { get; init; } = "Composition";
    public string? Notes { get; init; }
}
