using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using HD2RuntimeGUI.Core.Storage;

namespace HD2RuntimeGUI.Core.Metadata;

// Runtime 0.24.0+ guarded Booster authoring (hd2runtime.booster.guarded_authoring.v1, hd2.booster(name)).
// Boosters own no native record: published fields live on the records a booster links to (deployed_entity / status_effect).
// Every booster is listed, including unresolved ones; only published field instances become controls.
public sealed record BoosterIdentity(string? NativeName, string? UiIcon, int? EnumValue, int[] EnumValueCandidates, string[] Evidence, string Status);
public sealed record BoosterRelationship(string Kind, string? Effect = null, string? Evidence = null, string? SharedScope = null, string? Stratagem = null,
    string? StratagemSemanticId = null, string? StratagemAuthoring = null, string? DeployedEntity = null, bool? TurretProjectileShared = null,
    string? Owner = null, int? GatedSusceptibilities = null);
public sealed record Booster(string Name, string SemanticId, BoosterIdentity Identity, BoosterRelationship[] Relationships, bool Writable,
    string[] Targets, string[] FieldInstanceKeys, EntityBlocked[] BlockedFields);
public sealed record BoosterFieldTarget(string Resource, string Path, string[] Accessor);
public sealed record BoosterFieldValue(JsonElement Baseline, JsonElement Expected, string? Unit);
public sealed record BoosterSharedScope(bool Shared, bool RequiresAcknowledgement, bool ReviewedScopeComplete, string Note);
public sealed record BoosterOperation(bool PatchSupported, string TransactionGroupingKey, bool PlanSupported);
public sealed record BoosterFieldEvidence(string Tier, string GameplayWriteEffect, string? Fingerprint = null, string? NativeName = null, string[]? Chain = null);
public sealed record BoosterField(string InstanceKey, string Booster, string SemanticFieldId, string ApiFieldConstant, string DisplayName, string Type,
    string? Unit, BoosterFieldTarget Target, BoosterFieldValue Value, bool Writable, string[] Acknowledgements, string AcknowledgementReason,
    BoosterSharedScope SharedScope, BoosterOperation Operation, BoosterFieldEvidence Evidence);
public sealed record BoosterNativeModel(string Identity, string[] TypeLibraryReferences, string Ownership);
public sealed record BoosterSummary(int Boosters, int WritableBoosters, int FieldInstances, Dictionary<string, int> IdentityStatus, int NativeEnumValues,
    int ResearchWrites, int ProtectionChanges, string FixtureFallback);
public sealed record BoosterCatalog(string Contract, int SchemaVersion, string Hd2RuntimeVersion, string CanonicalCollection, BoosterNativeModel NativeModel,
    Dictionary<string, string> Acknowledgements, Booster[] Boosters, [property: JsonPropertyName("fieldInstances")] BoosterField[] Fields, BoosterSummary Summary)
{
    // Published fields in the shared entity-authoring form (changes, reset, Lua and export reuse the vehicle/backpack pipeline).
    [JsonIgnore] public EntityField[] FieldInstances { get; init; } = [];
    public Booster? Find(string name) => Boosters.FirstOrDefault(b => b.Name == name);
    public BoosterField? Raw(string instanceKey) => Fields.FirstOrDefault(f => f.InstanceKey == instanceKey);
}

public static class BoosterAuthoringReader
{
    public const string FileName = "BoosterAuthoringCapabilities.json", Contract = "hd2runtime.booster.guarded_authoring.v1";
    public const int MaxBytes = 1024 * 1024;
    public static readonly string[] Paths = ["deployed_entity", "status_effect"];
    public static readonly string[] Tiers = ["structural_chain_exact_fingerprint", "unique_exact_multi_value_fingerprint"];
    public static readonly string[] IdentityStatuses = ["RESOLVED", "CANDIDATES", "EFFECT_CATEGORY", "ELIMINATION", "UNRESOLVED"];
    private static readonly Regex SemanticId = new(@"\Abooster/v1/[a-z0-9-]{1,96}/[0-9a-f]{16}\z", RegexOptions.CultureInvariant);
    private static readonly Regex Api = new(@"\Ahd2\.fields\.[a-z_]+\.[a-z_0-9]+\z", RegexOptions.CultureInvariant);
    private static readonly Regex Name = new(@"\A[A-Za-z0-9][A-Za-z0-9 .'/-]{0,95}\z", RegexOptions.CultureInvariant);
    private static readonly JsonSerializerOptions Options = new(JsonStorage.Options) { UnmappedMemberHandling = JsonUnmappedMemberHandling.Skip };
    private static void Check(bool valid) { if (!valid) throw new InvalidDataException("Inconsistent booster capability metadata."); }
    public static bool ValidName(string name) => Name.IsMatch(name);

    public static BoosterCatalog Read(byte[] bytes, string version)
    {
        try
        {
            if (bytes.Length > MaxBytes) throw new InvalidDataException("Booster capability file exceeds size limit.");
            using var doc = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 32 }); MetadataReader.RejectDuplicates(doc.RootElement);
            var root = doc.RootElement;
            if (root.GetProperty("contract").GetString() != Contract || root.GetProperty("schemaVersion").GetInt32() != 1)
                throw new UnsupportedSdkException("Unsupported booster authoring contract.");
            var safety = root.GetProperty("safety");
            Check(!safety.GetProperty("runtimeAddresses").GetBoolean() && !safety.GetProperty("rawResourceIdentifiers").GetBoolean()
                && safety.GetProperty("writesDuringGeneration").GetInt32() == 0);
            var c = JsonSerializer.Deserialize<BoosterCatalog>(bytes, Options)!;
            Check(c.Hd2RuntimeVersion == version && c.CanonicalCollection == "fieldInstances" && c.Acknowledgements.ContainsKey("allow_unverified_effect")
                && c.Acknowledgements.ContainsKey("allow_shared") && c.Boosters.Length <= 200 && c.Fields.Length <= 1000);
            var boosters = c.Boosters.ToDictionary(b => b.Name, StringComparer.Ordinal);
            Check(boosters.Count == c.Boosters.Length && c.Boosters.Select(b => b.SemanticId).Distinct().Count() == c.Boosters.Length
                && c.Fields.Select(f => f.InstanceKey).Distinct().Count() == c.Fields.Length);
            foreach (var b in c.Boosters)
            {
                var own = c.Fields.Where(f => f.Booster == b.Name).ToArray();
                // Every booster is published with Runtime's identity evidence; unresolved ones carry a blocker instead of fields.
                Check(Name.IsMatch(b.Name) && SemanticId.IsMatch(b.SemanticId) && IdentityStatuses.Contains(b.Identity.Status) && b.Identity.Evidence.Length > 0
                    && b.Identity.Status switch
                    {
                        "RESOLVED" => b.Identity.EnumValue is { } v && b.Identity.EnumValueCandidates.SequenceEqual([v]),
                        "CANDIDATES" => b.Identity.EnumValue == null && b.Identity.EnumValueCandidates.Length > 1,
                        _ => b.Identity.EnumValueCandidates.Length == 0,
                    }
                    && b.Relationships.All(r => !string.IsNullOrWhiteSpace(r.Kind)) && b.BlockedFields.All(x => !string.IsNullOrWhiteSpace(x.Field) && !string.IsNullOrWhiteSpace(x.Reason))
                    && b.FieldInstanceKeys.Order(StringComparer.Ordinal).SequenceEqual(own.Select(f => f.InstanceKey).Order(StringComparer.Ordinal))
                    && b.Targets.Order(StringComparer.Ordinal).SequenceEqual(own.Select(f => f.Target.Path).Distinct().Order(StringComparer.Ordinal))
                    // No writable field depends on an unresolved native value.
                    && (b.Writable ? own.Length > 0 && b.Identity.Status == "RESOLVED" : own.Length == 0 && b.BlockedFields.Length > 0));
            }
            foreach (var f in c.Fields)
            {
                var shared = f.Acknowledgements.Contains("allow_shared");
                Check(f.InstanceKey.StartsWith("booster:", StringComparison.Ordinal) && f.InstanceKey.Length <= 512 && boosters.ContainsKey(f.Booster)
                    && f.Target.Resource == "booster" && Paths.Contains(f.Target.Path) && f.Target.Accessor.SequenceEqual(["booster", f.Target.Path])
                    && f.Type is "integer" or "number" && f.Writable && Api.IsMatch(f.ApiFieldConstant) && f.ApiFieldConstant == "hd2.fields." + f.SemanticFieldId
                    && !string.IsNullOrWhiteSpace(f.DisplayName) && JsonElement.DeepEquals(f.Value.Baseline, f.Value.Expected)
                    // Every booster write requires allow_unverified_effect; allow_shared exactly where the owning record's scope requires it.
                    && f.Acknowledgements.Contains("allow_unverified_effect") && f.Acknowledgements.All(a => a is "allow_unverified_effect" or "allow_shared")
                    && f.Acknowledgements.Distinct().Count() == f.Acknowledgements.Length && !string.IsNullOrWhiteSpace(f.AcknowledgementReason)
                    && shared == (f.SharedScope.Shared && f.SharedScope.RequiresAcknowledgement) && (f.SharedScope.Shared || f.SharedScope.ReviewedScopeComplete)
                    && f.Operation.PatchSupported && f.Operation.PlanSupported && !string.IsNullOrWhiteSpace(f.Operation.TransactionGroupingKey)
                    && Tiers.Contains(f.Evidence.Tier) && !string.IsNullOrWhiteSpace(f.Evidence.GameplayWriteEffect));
            }
            // A transaction group never spans boosters, targets or sharing (Runtime rejects transactions across backing objects).
            Check(c.Fields.GroupBy(f => f.Operation.TransactionGroupingKey).All(g => g.Select(f => (f.Booster, f.Target.Path, f.SharedScope.Shared)).Distinct().Count() == 1 && g.Count() <= 8));
            var s = c.Summary;
            Check(s.Boosters == c.Boosters.Length && s.WritableBoosters == c.Boosters.Count(b => b.Writable) && s.FieldInstances == c.Fields.Length
                && s.ResearchWrites == 0 && s.ProtectionChanges == 0 && s.FixtureFallback == "disabled"
                && IdentityStatuses.All(x => s.IdentityStatus.GetValueOrDefault(x) == c.Boosters.Count(b => b.Identity.Status == x))
                && s.IdentityStatus.Keys.All(IdentityStatuses.Contains));
            var fields = c.Fields.Select(f => Entity(f, boosters[f.Booster])).ToArray();
            foreach (var f in fields) _ = Generation.EntityScalar.Normalize(f, f.CurrentDefault);
            return c with { FieldInstances = fields };
        }
        catch (Exception e) when (e is JsonException or KeyNotFoundException or NullReferenceException or ArgumentException or InvalidOperationException)
        { throw new InvalidDataException("Malformed booster capability metadata.", e); }
    }
    // Plan and approval scopes are per booster target; operation groups are Runtime's transaction groups.
    private static EntityField Entity(BoosterField f, Booster b)
    {
        var scope = "booster-scope/" + b.SemanticId + "/" + f.Target.Path;
        var proof = string.Join("; ", new[] { f.Evidence.Fingerprint, f.Evidence.Chain is { Length: > 0 } chain ? string.Join(" → ", chain) : null }.Where(x => x != null));
        return new(f.InstanceKey, f.SemanticFieldId, f.DisplayName, f.Type, f.Unit ?? f.Value.Unit, f.Value.Baseline.Clone(), true, null,
            new EntityTarget("booster", f.Target.Path, Booster: b.Name), f.Operation.TransactionGroupingKey, f.Operation.TransactionGroupingKey,
            "booster-plan/" + b.SemanticId + "/" + f.Target.Path, "patch_or_transaction", f.Acknowledgements.Contains("allow_shared"), f.SharedScope.Shared, [],
            scope, f.SharedScope.ReviewedScopeComplete, !f.SharedScope.ReviewedScopeComplete, f.Target.Path, f.SemanticFieldId.Split('.')[0], f.ApiFieldConstant, 1, [],
            new EntityEvidence(f.Evidence.Tier, Proof: proof.Length > 0 ? proof : null, NativeOwner: f.Evidence.NativeName, GameplayWriteEffect: f.Evidence.GameplayWriteEffect),
            f.SharedScope.Note, Acknowledgement: "allow_unverified_effect");
    }
}
