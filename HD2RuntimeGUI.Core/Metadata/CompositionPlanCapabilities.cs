using System.Text.Json;
using HD2RuntimeGUI.Core.Storage;

namespace HD2RuntimeGUI.Core.Metadata;

public sealed record PlanLimits(int Phases, int Operations, int PhysicalChangesPerPhase);
public sealed record PlanGrouping(string SamePhase, string NewPhaseRequired, string Order);
public sealed record PlanSharedScope(string Granularity, string Rule, string BackingObjectRule, bool ImplicitCrossObjectAuthorization);
public sealed record CompositionPlanCapabilities(int SchemaVersion, string Api, string SemanticTargetModel, string FieldCapabilitySource,
    IReadOnlyList<string> IdentityContract, PlanLimits Limits, IReadOnlyList<string> OperationForms,
    Dictionary<string, string> TargetFromPaths, Dictionary<string, IReadOnlyList<string>> SemanticObjects,
    PlanGrouping Grouping, PlanSharedScope SharedScope, IReadOnlyList<string> Execution, HeatSafety Safety, IReadOnlyList<string>? FieldCapabilitySources = null);
public interface ICompositionPlanCapabilitiesReader { CompositionPlanCapabilities Read(byte[] bytes); }
public sealed class CompositionPlanCapabilitiesReader : ICompositionPlanCapabilitiesReader
{
    public const string FileName = "CompositionPlanCapabilities.json";
    public CompositionPlanCapabilities Read(byte[] bytes)
    {
        try
        {
            if (bytes.Length > 64 * 1024) throw new InvalidDataException("Composition plan contract is too large.");
            using var doc = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 16 }); MetadataReader.RejectDuplicates(doc.RootElement);
            if (doc.RootElement.GetProperty("schemaVersion").GetInt32() != 1) throw new UnsupportedSdkException("Unsupported composition plan schema.");
            var c = JsonSerializer.Deserialize<CompositionPlanCapabilities>(bytes, JsonStorage.Options)!;
            if (c.Api != "hd2.plan" || c.FieldCapabilitySource != PlayerWeaponCatalogReader.FileName
                || c.Safety is not { Writes: 0, ProtectionChanges: 0, FixtureFallback: "disabled", SnapshotOnly: true }
                || c.SharedScope is not { Granularity: "operation", ImplicitCrossObjectAuthorization: false }
                || c.Limits.Phases is < 2 or > 8 || c.Limits.Operations is < 1 or > 64 || c.Limits.PhysicalChangesPerPhase is < 1 or > 128
                || !new[] { "projectile", "terminal.impact", "terminal.expiry" }.All(c.TargetFromPaths.ContainsKey)
                || !new[] { "target + field/expect/value", "target + changes[]", "target_from + field/expect/value", "target_from + changes[]" }.All(c.OperationForms.Contains)
                || !c.SemanticObjects["attack"].Contains("attack.projectile") || !c.SemanticObjects["projectile"].Contains("damage.*")
                || !c.SemanticObjects["projectile"].Contains("projectile.*") || !c.SemanticObjects["terminal"].Contains("terminal.explosion")
                || !c.SemanticObjects["explosion"].Contains("explosion.*") || c.Grouping == null)
                throw new InvalidDataException("Unsupported or unsafe composition plan contract.");
            return c;
        }
        catch (Exception e) when (e is JsonException or NullReferenceException or InvalidOperationException or KeyNotFoundException)
        { throw new InvalidDataException("Malformed composition plan metadata: " + e.Message, e); }
    }
}
