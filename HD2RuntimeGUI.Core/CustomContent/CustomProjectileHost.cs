using HD2RuntimeGUI.Core.Models;

namespace HD2RuntimeGUI.Core.CustomContent;

/// <summary>Where a projectile is chosen for a weapon: a projectile host as the Projectile Builder names it (AttackOutputChange host kinds: a
/// player, support or mounted weapon and its attack), or one of a weapon's programmable-ammunition modes (AmmoMode). Every projectile selector
/// offers the project's custom projectiles through it; the target says which hosts can fire one.</summary>
public sealed record CustomProjectileHost(string Kind, string Weapon, string Role)
{
    public const string AmmoMode = "ammo_mode";
    public static CustomProjectileHost Player(string weapon, string role) => new(AttackOutputChange.PlayerHost, weapon, role);
    public static CustomProjectileHost Of(CustomProjectileAssignment a) => new(a.HostKind, a.Weapon, a.AttackRole);
    public bool Matches(CustomProjectileAssignment a) => a.HostKind == Kind && a.Weapon == Weapon && a.AttackRole == Role;
    /// <summary>The weapon, and its attack when it is not the primary one.</summary>
    public string Label => Weapon + (Role == "primary" ? "" : " · " + Role.Replace('_', ' '));
}

/// <summary>A custom projectile created from a weapon's projectile selector (new, or a copy of the one the weapon fires), until the author
/// assigns it there or cancels. Session state, never saved: the projectile is an ordinary custom projectile in the project from the start,
/// and the weapon keeps what it fires until the flow completes. While nothing refers to it and its id was not set explicitly, the id
/// follows its name.</summary>
public sealed record CustomProjectileDraft(string ProjectileId, CustomProjectileHost Origin, string? CopyOf = null, bool IdFollowsName = true);
