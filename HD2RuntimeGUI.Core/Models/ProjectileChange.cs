namespace HD2RuntimeGUI.Core.Models;

public sealed record ProjectileReference(string Weapon, string AttackRole)
{
    [System.Text.Json.Serialization.JsonIgnore]
    public string Label => Weapon + " · " + AttackRole.Replace('_', ' ') + " projectile";
}
public sealed class ProjectileChange
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Weapon { get; set; } = "";
    public string AttackRole { get; set; } = "";
    public string SemanticFieldId { get; set; } = "attack.projectile";
    public ProjectileReference ExpectedProjectile { get; set; } = new("", "");
    public ProjectileReference ReplacementProjectile { get; set; } = new("", "");
    public string BaselineSdkVersion { get; set; } = "";
    // Opaque evidence digests detect changed SDK identities without persisting IDs,
    // offsets or addresses. Runtime selection always uses semantic handles.
    public string ExpectedEvidence { get; set; } = "";
    public string ReplacementEvidence { get; set; } = "";
    public string CompatibilityClass { get; set; } = "";
    public bool Enabled { get; set; } = true;
    public bool EnsureEnabled { get; set; } = true;
    public string Group { get; set; } = "Composition";
    public string? Notes { get; set; }
}
