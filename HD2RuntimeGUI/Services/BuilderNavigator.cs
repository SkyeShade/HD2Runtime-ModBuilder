using HD2RuntimeGUI.Core.CustomContent;

namespace HD2RuntimeGUI.Services;

/// <summary>Lets an editor anywhere in the page tree ask the shell (Home) to change page, so a projectile selector, however deeply it is
/// nested, can open the Custom Projectile Builder and the builder can return to the weapon a custom projectile was chosen or created for.</summary>
public sealed class BuilderNavigator
{
    /// <summary>Open a custom projectile in the Custom Projectile Builder; From: the weapon attack it was opened from (to return to).</summary>
    public event Action<string, CustomProjectileHost?>? CustomProjectileRequested;
    /// <summary>Return to a weapon attack's projectile selector.</summary>
    public event Action<CustomProjectileHost>? HostRequested;
    public void OpenCustomProjectile(string id, CustomProjectileHost? from = null) => CustomProjectileRequested?.Invoke(id, from);
    public void ReturnTo(CustomProjectileHost host) => HostRequested?.Invoke(host);
    /// <summary>Open a stratagem on the Stratagems page and bring one of its elements into view (a support weapon's backpack, NoEffectRows).</summary>
    public event Action<StratagemOpenRequest>? StratagemRequested;
    private int opened;
    public void OpenStratagem(string stratagem, string? reveal = null) => StratagemRequested?.Invoke(new(stratagem, reveal, Interlocked.Increment(ref opened)));
}

/// <summary>A stratagem to show and the element to scroll to; Id makes a repeated request (the same backpack twice) a new one.</summary>
public sealed record StratagemOpenRequest(string Stratagem, string? Reveal, int Id);
