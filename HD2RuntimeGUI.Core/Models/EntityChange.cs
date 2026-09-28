using System.Text.Json;
namespace HD2RuntimeGUI.Core.Models;

// Semantic vehicle/backpack intent (Runtime 0.23.0+). Targets are published names and slot identities (zone_N / slot_N);
// mount values are published mounted-weapon semantic IDs, never native resources or addresses.
public sealed record EntityChange
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Resource { get; init; } = "";      // "vehicle" or "backpack"
    public string Entity { get; init; } = "";        // published vehicle/backpack name
    public string Path { get; init; } = "";          // entity, damage_zone, mount or backpack
    public string? Zone { get; init; }
    public string? Mount { get; init; }
    public string InstanceKey { get; init; } = "";
    public string SemanticFieldId { get; init; } = "";
    public string FieldType { get; init; } = "";
    public JsonElement ExpectedValue { get; init; }
    public JsonElement DesiredValue { get; init; }
    public string BaselineSdkVersion { get; init; } = "";
    public string CapabilityEvidence { get; init; } = "";
    // Explicit acknowledgement of Runtime's unverified-reference/package-residency warning for this exact replacement.
    public string? ReferenceAcknowledgement { get; init; }
    public bool Enabled { get; init; } = true;
    public bool EnsureEnabled { get; init; } = true;
    public string Group { get; init; } = "Vehicles & backpacks";
    public string? Notes { get; init; }
}
