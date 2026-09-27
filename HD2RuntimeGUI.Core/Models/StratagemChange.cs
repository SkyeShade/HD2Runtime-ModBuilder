using System.Text.Json;
namespace HD2RuntimeGUI.Core.Models;

public sealed record StratagemChange
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string TargetKind { get; init; } = "stratagem";
    public string Stratagem { get; init; } = "";
    public string Path { get; init; } = "";
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
