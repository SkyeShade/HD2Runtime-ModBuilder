namespace HD2RuntimeGUI.Core.Models;

// Format 11 (0.28.0 projectile builder): one write on a catalogued projectile row, hd2.attack_output(Output). A row is a shared definition:
// the write changes every entity that fires it (its owner, every host swapped onto it), never one host, and it does not follow a host swap.
//   Field  projectile.direct_damage | projectile.impact_explosion | projectile.expiry_explosion (builder slots), or
//          presentation.mode_label | presentation.mode_icon (the label and HUD icon a weapon-function mode shows for this row).
//   Expect a slot: "<Output>#<slot>" (the row's own handle) or "none"; presentation: the row's reviewed label / icon.
//   Value  a slot: "<donor output>#<slot>" (hd2.attack_output(donor):<slot>()) or "none" (explosions only); presentation: a native
//          label, a native icon or "auto" (the label's exact native icon, else the generic fallback).
// Identities are semantic (output ids and Runtime field / slot names); the opt-ins the write carries are kept with it.
public sealed record OutputRowChange
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Output { get; init; } = "";
    public string OutputName { get; init; } = "";
    public string Field { get; init; } = "";
    public string Expect { get; init; } = "";
    public string Value { get; init; } = "";
    // Display name of a slot donor (the donor output's owner); null for presentation values.
    public string? ValueName { get; init; }
    public string[] Acknowledgements { get; init; } = [];
    public string Evidence { get; init; } = "";
    public string BaselineSdkVersion { get; init; } = "";
    public bool Enabled { get; init; } = true;
    public bool EnsureEnabled { get; init; } = true;
    public string Group { get; init; } = "Composition";
    public string? Notes { get; init; }
}
