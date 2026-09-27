using System.Text.Json.Serialization;
namespace HD2RuntimeGUI.Core.Models;

public sealed record ExplosionReference(ProjectileReference? Projectile, string? Phase)
{
    public static ExplosionReference None => new(null, null);
    [JsonIgnore] public bool IsNone => Projectile == null;
    [JsonIgnore] public string Label => IsNone ? "None" : Projectile!.Weapon + " · " + Phase + " explosion";
}
public sealed class CompositionChange
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Weapon { get; set; } = "";
    public string AttackRole { get; set; } = "";
    public string Kind { get; set; } = "projectile";
    public string? Phase { get; set; }
    public ProjectileReference Target { get; set; } = new("", "");
    public WeaponChange? Scalar { get; set; }
    public ExplosionReference? ExpectedExplosion { get; set; }
    public ExplosionReference? DesiredExplosion { get; set; }
    public ExplosionReference? ExplosionTarget { get; set; }
    public string TargetEvidence { get; set; } = "";
    public string ReferenceEvidence { get; set; } = "";
    public string DesiredReferenceEvidence { get; set; } = "";
    public string BaselineSdkVersion { get; set; } = "";
    public bool SharedAcknowledged { get; set; }
    public string SharedEvidence { get; set; } = "";
    public bool Enabled { get; set; } = true;
    public bool EnsureEnabled { get; set; } = true;
    public string Group { get; set; } = "Composition";
    public string? Notes { get; set; }
}
