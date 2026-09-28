using System.Text.Json;
namespace HD2RuntimeGUI.Core.Models;

// Semantic stratagem intent. Entity/weapon/attack are published graph identities (for example "main"/"primary"),
// never native component, record or resource identifiers.
public sealed record StratagemChange
{
    public Guid Id { get; init; } = Guid.NewGuid();
    // "stratagem", "deployed_entity" or "mounted_weapon" (mounted weapon settings and its attack branches).
    public string TargetKind { get; init; } = "stratagem";
    public string Stratagem { get; init; } = "";
    public string Path { get; init; } = "";
    public string? Entity { get; init; }
    public string? Weapon { get; init; }
    public string? Zone { get; init; }
    public string? Attack { get; init; }
    public string InstanceKey { get; init; } = "";
    public string SemanticFieldId { get; init; } = "";
    public string FieldType { get; init; } = "";
    public JsonElement ExpectedValue { get; init; }
    public JsonElement DesiredValue { get; init; }
    public string BaselineSdkVersion { get; init; } = "";
    public string CapabilityEvidence { get; init; } = "";
    public bool Enabled { get; init; } = true;
    public bool EnsureEnabled { get; init; } = true;
    public string Group { get; init; } = "Stratagems";
    public string? Notes { get; init; }
}
