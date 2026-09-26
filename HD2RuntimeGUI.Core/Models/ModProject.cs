using System.Text.Json.Nodes;

namespace HD2RuntimeGUI.Core.Models;

public sealed class ModProject
{
    public int FormatVersion { get; set; } = 1;
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
