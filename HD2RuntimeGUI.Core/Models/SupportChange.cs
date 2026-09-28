using System.Text.Json;

namespace HD2RuntimeGUI.Core.Models;

public sealed record SupportChange
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string InstanceKey { get; init; } = "";
    public string Weapon { get; init; } = "";
    public string? AttackRole { get; init; }
    public string SemanticFieldId { get; init; } = "";
    public string FieldType { get; init; } = "";
    public JsonElement ExpectedValue { get; init; }
    public JsonElement DesiredValue { get; init; }
    public string BaselineSdkVersion { get; init; } = "";
    public string CapabilityEvidence { get; init; } = "";
    // 0.24.0+: explicit acknowledgement of a field Runtime marks allow_unverified_effect (binds the field and its published reason).
    public string? EffectAcknowledgement { get; init; }
    public bool Enabled { get; init; } = true;
    public bool EnsureEnabled { get; init; } = true;
    public string Group { get; init; } = "Support Weapons";
    public string? Notes { get; init; }
}
