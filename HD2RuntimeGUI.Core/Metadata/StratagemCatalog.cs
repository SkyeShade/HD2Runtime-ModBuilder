using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using HD2RuntimeGUI.Core.Storage;

namespace HD2RuntimeGUI.Core.Metadata;

public sealed record StratagemAvailability(JsonElement Value, bool Writable, string? Reason, string? Field, string? Unit);
public sealed record StratagemDefinition(string Name, string Family, string RootResolution, string? BlockedReason,
    string[] AttackRoles, StratagemAvailability CooldownCapability, StratagemAvailability MaxUses,
    StratagemAvailability CallInTime, int? UsesPerRearm, double? RearmTime,
    StratagemAvailability? BarrageScheduling);
public sealed record StratagemBranch(string Id, string Name, string WikiKind, string[] SemanticRoles, string? ParentId,
    string[] ChildIds, string Stratagem, string Family, string Correlation);
public sealed record StratagemAttack(string Stratagem, string Family, string Role, string Path, string Kind, string? ParentRole);
public sealed record StratagemTarget(string Resource, string Stratagem, string Path, string? Attack);
public sealed record StratagemConsumer(string Stratagem, string Path);
public sealed record StratagemField(string InstanceKey, string SemanticFieldId, string DisplayName, string Type, string? Unit,
    JsonElement CurrentDefault, bool Editable, string? Reason, StratagemTarget Target, string BackingObjectId,
    string OperationGroup, string PlanGroup, string Requires, bool AllowSharedRequired, bool Shared,
    StratagemConsumer[] SharedConsumers, bool ReviewedScopeComplete, bool DynamicConsumersPossible,
    string Provenance, string BackingObjectKind, string ApiFieldConstant, string Domain, int PlanPhase, string[] DependsOn);
public sealed record StratagemSummary(int OffensiveRootsResolved, int OrbitalRootsResolved, int EagleRootsResolved,
    int SupportRootsResolved, int CooldownWritable, int MaxUsesWritable, int EagleUsesPerRearmWritable,
    int EagleRearmTimeWritable, int FieldInstances, int WritableFieldInstances, int BackingObjectCount,
    int SharedConsumerScopeCount, int ImportedAttackBranches, int NativeBackingBranches);
public sealed record StratagemCatalog(int SchemaVersion, string Contract, string CanonicalCollection,
    StratagemDefinition[] Stratagems, StratagemBranch[] SemanticBranches, StratagemAttack[] Attacks,
    StratagemField[] FieldInstances, StratagemSummary Summary)
{
    public StratagemField Field(string key) => FieldInstances.SingleOrDefault(f => f.InstanceKey == key)
        ?? throw new InvalidDataException("Stratagem capability is missing. Review or reset this modification.");
    public string BranchLabel(StratagemField f) => f.Target.Path == "eagle_rearm" ? "Eagle Shared System"
        : f.Target.Path == "stratagem" ? "Stratagem" : Attacks.Single(a => a.Stratagem == f.Target.Stratagem && a.Role == f.Target.Attack).Path.Replace("/", " → ");
}
public interface IStratagemCatalogReader { StratagemCatalog Read(byte[] bytes); }
public sealed class StratagemCatalogReader : IStratagemCatalogReader
{
    public const string FileName = "StratagemAuthoringCapabilities.json";
    public const int MaxBytes = 8 * 1024 * 1024;
    public StratagemCatalog Read(byte[] bytes)
    {
        try
        {
            if (bytes.Length > MaxBytes) throw new InvalidDataException("Stratagem catalog exceeds size limit.");
            using var doc = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 32 });
            MetadataReader.RejectDuplicates(doc.RootElement);
            var options = new JsonSerializerOptions(JsonStorage.Options) { UnmappedMemberHandling = JsonUnmappedMemberHandling.Skip };
            var c = JsonSerializer.Deserialize<StratagemCatalog>(bytes, options)!;
            void Check(bool value) { if (!value) throw new InvalidDataException("Inconsistent or unsupported stratagem capability metadata."); }
            if (c.SchemaVersion != 1 || c.Contract != "hd2runtime.stratagem.guarded_authoring.v1" || c.CanonicalCollection != "fieldInstances")
                throw new UnsupportedSdkException("Unsupported stratagem authoring schema.");
            var safety = doc.RootElement.GetProperty("safety");
            Check(safety.GetProperty("writes").GetInt32() == 0 && safety.GetProperty("protectionChanges").GetInt32() == 0 && safety.GetProperty("fixtureFallback").GetString() == "disabled");
            var roots = c.Stratagems.ToDictionary(w => w.Name, StringComparer.Ordinal);
            Check(roots.Count <= 200 && c.FieldInstances.Length <= 4000 && c.FieldInstances.Select(f => f.InstanceKey).Distinct().Count() == c.FieldInstances.Length);
            foreach (var f in c.FieldInstances)
            {
                Check(f.InstanceKey.StartsWith("stratagem:", StringComparison.Ordinal) && f.InstanceKey.Length <= 512
                    && f.Target.Resource == "stratagem" && roots.ContainsKey(f.Target.Stratagem)
                    && f.Target.Path is "stratagem" or "attack" or "eagle_rearm"
                    && Regex.IsMatch(f.ApiFieldConstant, @"\Ahd2\.fields\.[a-z_]+\.[a-z_0-9]+\z")
                    && f.Type is "number" or "integer" or "boolean" && !string.IsNullOrWhiteSpace(f.DisplayName)
                    && !string.IsNullOrWhiteSpace(f.BackingObjectId) && !string.IsNullOrWhiteSpace(f.OperationGroup) && !string.IsNullOrWhiteSpace(f.PlanGroup)
                    && f.PlanPhase == 1 && f.DependsOn.Length == 0 && f.Requires == "patch_or_transaction"
                    && f.AllowSharedRequired == f.Shared && f.SharedConsumers.Length > 0);
                Check(f.Target.Path == "attack" ? f.Target.Attack != null && c.Attacks.Count(a => a.Stratagem == f.Target.Stratagem && a.Role == f.Target.Attack) == 1 : f.Target.Attack == null);
                if (f.Editable) { Check(roots[f.Target.Stratagem].RootResolution == "UNIQUE"); _ = Generation.StratagemScalar.Normalize(f, f.CurrentDefault); }
                else Check(!string.IsNullOrWhiteSpace(f.Reason));
            }
            foreach (var group in c.FieldInstances.GroupBy(f => f.OperationGroup)) Check(group.Select(f => f.BackingObjectId).Distinct().Count() == 1);
            foreach (var group in c.FieldInstances.GroupBy(f => f.BackingObjectId))
                Check(group.Select(Generation.StratagemChangeService.ApprovalEvidence).Distinct().Count() == 1);
            Check(c.Summary.FieldInstances == c.FieldInstances.Length && c.Summary.WritableFieldInstances == c.FieldInstances.Count(f => f.Editable)
                && c.Summary.BackingObjectCount == c.FieldInstances.Select(f => f.BackingObjectId).Distinct().Count()
                && c.Summary.SharedConsumerScopeCount == c.FieldInstances.Where(f => f.Shared).Select(f => f.BackingObjectId).Distinct().Count()
                && c.Summary.OffensiveRootsResolved == roots.Values.Count(w => w.RootResolution == "UNIQUE" && w.Family != "support")
                && c.Summary.SupportRootsResolved == roots.Values.Count(w => w.RootResolution == "UNIQUE" && w.Family == "support")
                && c.Summary.ImportedAttackBranches == c.SemanticBranches.Length && c.Summary.NativeBackingBranches == c.Attacks.Length);
            return c;
        }
        catch (Exception e) when (e is JsonException or NullReferenceException or InvalidOperationException or KeyNotFoundException or ArgumentException)
        { throw new InvalidDataException("Malformed stratagem capabilities.", e); }
    }
}
