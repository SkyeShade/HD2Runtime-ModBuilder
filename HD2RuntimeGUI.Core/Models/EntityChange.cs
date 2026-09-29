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
    // 0.26.0 vehicle weapons (Resource "vehicle_weapon", Entity = the published weapon key "<vehicle> / <mount label>"): the attack role.
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string? Attack { get; init; }
    // 0.26.0 drop-pod racks (Resource "pod_rack", Entity = the published rack name): the rack slot (1-4) of a payload edit.
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public int? Slot { get; init; }
    // 0.27.0 throwables (Resource "throwable", Entity = the published throwable name): the status effect key of a status_effect edit.
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string? Effect { get; init; }
    // Enemies and enemy structures (Resource "enemy" / "structure", Entity = the class's semantic ID; 0.28.0 development SDKs): the reviewed
    // classes that share the edited row when it was saved. Kept with the change so the project records who else a shared edit affects.
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string[]? SharedConsumers { get; init; }
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
