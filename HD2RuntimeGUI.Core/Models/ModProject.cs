using System.Text.Json.Nodes;

namespace HD2RuntimeGUI.Core.Models;

public sealed class ModProject
{
    public int FormatVersion { get; set; } = 4;
    public Guid Id { get; set; } = Guid.NewGuid();
    public string DisplayName { get; set; } = "";
    public string Author { get; set; } = "";
    public string ResourceId { get; set; } = "";
    public Guid ManagerGuid { get; set; }
    public string Version { get; set; } = "0.1.0";
    public string Description { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset ModifiedAt { get; set; } = DateTimeOffset.UtcNow;
    public string SdkVersion { get; set; } = "";
    public int RuntimeApi { get; set; } = 1;
    public string ExportDirectory { get; set; } = "";
    public List<ModChange> Changes { get; set; } = [];
    public List<WeaponChange> WeaponChanges { get; set; } = [];
    public List<ProjectileChange> ProjectileChanges { get; set; } = [];
    public List<CompositionChange> CompositionChanges { get; set; } = [];
    public List<SupportChange> SupportChanges { get; set; } = [];
    public Dictionary<string, string> SupportApprovals { get; set; } = [];
    public List<StratagemChange> StratagemChanges { get; set; } = [];
    public Dictionary<string, string> StratagemApprovals { get; set; } = [];
    public List<EntityChange> EntityChanges { get; set; } = [];
    public Dictionary<string, string> EntityApprovals { get; set; } = [];
    // Format 7: in-game options. Omitted when never configured, so older projects save unchanged.
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public ModOptionsSettings? ModOptions { get; set; }
    // Format 11: hand-written Runtime Lua (src/addon.lua). Omitted until a project adds it.
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public CustomLuaSettings? CustomLua { get; set; }
    // Format 11: attacks that fire another catalogued output (hd2.attack_output). Omitted until a project has one.
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public List<AttackOutputChange>? AttackOutputChanges { get; set; }
    // Format 11: projectile-builder slot and mode-presentation writes on catalogued rows (hd2.attack_output). Omitted until a project has one.
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public List<OutputRowChange>? OutputRowChanges { get; set; }
    // Format 12: files the user adds to every exported mod ZIP (a thumbnail, a preview image), read from their own location at export.
    // Omitted until a project has one, so older projects save and export unchanged.
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public List<PackagedFile>? PackagedFiles { get; set; }
    // Format 12: how HD2Arsenal presents the mod in its library (manifest.json Description and IconPath). Omitted until configured.
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public ArsenalPresentation? Arsenal { get; set; }
}

// One additional file of the exported mod ZIP: a file on the user's computer (full path, read at export) and where it goes in the ZIP
// (relative, '/' between folders). Never part of the generated gameplay files.
public sealed class PackagedFile
{
    public string Source { get; set; } = "";
    public string Destination { get; set; } = "";
}

// What HD2Arsenal reads from manifest.json for its library entry: Description (plain text, shown before ModBuilder's dependency line)
// and IconPath (an image in the ZIP). Icon is the image's full path on the user's computer, read at every export.
public sealed class ArsenalPresentation
{
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string? Description { get; set; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string? Icon { get; set; }
}

// Project-owned overrides; SDK objects are never changed. No runtime addresses are persisted.
public sealed class WeaponChange
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Weapon { get; set; } = "";
    public string SemanticFieldId { get; set; } = "";
    public string FieldType { get; set; } = "";
    public System.Text.Json.JsonElement ExpectedValue { get; set; }
    public System.Text.Json.JsonElement DesiredValue { get; set; }
    public bool SharedAcknowledged { get; set; }
    public string? AcknowledgedWriteScope { get; set; }
    public int? AcknowledgedConsumerCount { get; set; }
    public List<string> AcknowledgedAffectedWeapons { get; set; } = [];
    public string BaselineSdkVersion { get; set; } = "";
    public bool Enabled { get; set; } = true;
    public bool EnsureEnabled { get; set; } = true;
    public string Group { get; set; } = "Gameplay";
    public string? Notes { get; set; }
    // 0.26.0: acknowledgement of Runtime's allow_unverified_effect opt-in for this field (reticle, fire modes). Omitted when absent.
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string? EffectAcknowledgement { get; set; }
    // Format 11: the kind of sub-target ("underbarrel") when Weapon names a weapon's nested sub-target by its published name
    // ("AR/GL-21 One-Two / underbarrel", hd2.weapon('AR/GL-21 One-Two'):underbarrel()). Omitted for ordinary weapons.
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string? Subweapon { get; set; }
}

public sealed class ModChange
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Domain { get; set; } = "";
    public string Target { get; set; } = "";
    public string Field { get; set; } = "";
    public JsonNode? ExpectedValue { get; set; }
    public JsonNode? NewValue { get; set; }
    public string FieldType { get; set; } = "";
    public string Confidence { get; set; } = "";
    public bool EnsureEnabled { get; set; } = true;
    public bool Enabled { get; set; } = true;
    public string Group { get; set; } = "Gameplay";
}

public sealed record ProjectSummary(Guid Id, string DisplayName, string ResourceId, string SdkVersion, DateTimeOffset ModifiedAt);
public sealed record CreateProjectRequest(string DisplayName, string Author, string ResourceId, string Version, string Description = "");
