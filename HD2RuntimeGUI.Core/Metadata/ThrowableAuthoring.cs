using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using HD2RuntimeGUI.Core.Storage;

namespace HD2RuntimeGUI.Core.Metadata;

// Runtime 0.27.0 throwable authoring (hd2runtime.throwable.guarded_authoring.v1, ThrowableAuthoringCapabilities.json, hd2.throwable(name)).
// Every armory throwable is published with its category and family, and its native targets: the ThrowableComponent (inventory counts),
// the ExplosiveComponent (fuse), its explosion settings and DamageInfo, shrapnel / bomblet projectiles, status effects, the knife's direct
// hit, and the deployed entity's health or shield. Only published field instances become controls; ownership and opt-ins are as published.
public sealed record ThrowableIdentity(string Status, string Method, string[] MatchedFacts, string[] Evidence, int AiVariantsWithSameValues);
// A target's published consumers of one native row kind (explosion, damage, projectile, definition).
public sealed record ThrowableScopeBlock(int DirectReferences, int RootEntities, string[] Throwables, int OtherEntities, int SettingsOnlyReferences);
public sealed record ThrowableFieldScope(bool Shared, string Kind, string Note);
public sealed record ThrowableFieldEvidence(string Tier, string Native, string GameplayWriteEffect, JsonElement? WikiFingerprint = null);
public sealed record ThrowableRange(double Min, double Max, bool Integer);
public sealed record ThrowableField(string InstanceKey, string SemanticFieldId, string ApiFieldConstant, string DisplayName, string Type, string? Unit,
    JsonElement Baseline, bool Editable, string? Reason, ThrowableRange? Range, string[] Acknowledgements, string? AcknowledgementReason,
    ThrowableFieldScope Scope, ThrowableFieldEvidence Evidence);
public sealed record ThrowableTarget(string Path, string? Key, string[] Accessor, string Relationship, string? Label, string? LabelConfidence,
    Dictionary<string, ThrowableScopeBlock>? Scope, bool SharedDamageWithParentExplosion, ThrowableField[] Fields);
public sealed record Throwable(string Name, string SemanticId, string Category, string Family, string? Designation, ThrowableIdentity Identity, ThrowableTarget[] Targets)
{
    [JsonIgnore] public int WritableFields => Targets.Sum(t => t.Fields.Count(f => f.Editable));
}
public sealed record ThrowableSafety(int Writes, int ProtectionChanges, string FixtureFallback, string Mode);
public sealed record ThrowableSummary(int Throwables, int Resolved, int WritableThrowables, int FieldInstances, int WritableFieldInstances, int SharedFieldInstances,
    Dictionary<string, int> FieldsByTarget, Dictionary<string, int> WritableByTarget);
public sealed record ThrowableFieldDefinition(string Id, string Type, string ApiFieldConstant);
public sealed record ThrowableCatalogJson(string Contract, int SchemaVersion, string Build, Throwable[] Throwables, ThrowableFieldDefinition[] FieldDefinitions, ThrowableSafety Safety, ThrowableSummary Summary);

public sealed class ThrowableCatalog
{
    public required Throwable[] Throwables { get; init; }
    public required ThrowableSummary Summary { get; init; }
    // Published fields in the shared entity-authoring form (changes, reset, Lua, export and Mod Options reuse that pipeline).
    public required EntityField[] FieldInstances { get; init; }
    public Throwable? Find(string name) => Throwables.FirstOrDefault(t => t.Name == name);
    public (Throwable Throwable, ThrowableTarget Target, ThrowableField Field)? Raw(string instanceKey) =>
        Throwables.SelectMany(t => t.Targets.SelectMany(g => g.Fields.Select(f => (t, g, f)))).FirstOrDefault(x => x.f.InstanceKey == instanceKey) is { t: not null } x ? x : null;
    // The native row a field lives in, from its target's published consumer blocks (see ThrowableAuthoringReader.Block).
    public static ThrowableScopeBlock? Consumers(ThrowableTarget target, ThrowableField field) =>
        target.Scope?.GetValueOrDefault(ThrowableAuthoringReader.Block(target.Path, field.SemanticFieldId));
}

public static class ThrowableAuthoringReader
{
    public const string FileName = "ThrowableAuthoringCapabilities.json", Contract = "hd2runtime.throwable.guarded_authoring.v1";
    public const int MaxBytes = 4 * 1024 * 1024;
    public const string Resource = "throwable";
    // Every published target and the accessor chain Runtime publishes for it (hd2.throwable(name):<chain>); a status effect adds its key.
    public static readonly IReadOnlyDictionary<string, string[]> Accessors = new Dictionary<string, string[]>
    {
        ["throwable"] = ["throwable"], ["detonation"] = ["detonation"], ["explosion"] = ["explosion"], ["shrapnel"] = ["shrapnel"],
        ["status_effect"] = ["explosion", "status_effect"], ["bomblets"] = ["bomblets"], ["bomblet_explosion"] = ["bomblets", "explosion"],
        ["damage"] = ["damage"], ["entity"] = ["entity"], ["shield"] = ["shield"],
    };
    public static readonly string[] Tiers = ["wiki_exact", "native_member"];
    private static readonly Regex SemanticIdPattern = new(@"\Athrowable/v1/[a-z0-9-]{1,96}/[0-9a-f]{16}\z", RegexOptions.CultureInvariant);
    private static readonly Regex NamePattern = new(@"\A[A-Za-z0-9][A-Za-z0-9 .'/-]{0,95}\z", RegexOptions.CultureInvariant);
    private static readonly Regex KeyPattern = new(@"\A[a-z][a-z0-9-]{0,63}\z", RegexOptions.CultureInvariant);
    private static readonly Regex Api = new(@"\Ahd2\.fields\.[a-z_]+\.[a-z_0-9]+\z", RegexOptions.CultureInvariant);
    // Research annotations (nearest candidates, wiki notes) are not part of the authoring surface.
    private static readonly JsonSerializerOptions Options = new(JsonStorage.Options) { UnmappedMemberHandling = JsonUnmappedMemberHandling.Skip };
    private static void Check(bool valid, [System.Runtime.CompilerServices.CallerLineNumber] int line = 0) { if (!valid) throw new InvalidDataException($"Inconsistent throwable capability metadata (check {line})."); }
    public static bool ValidName(string name) => NamePattern.IsMatch(name);
    public static bool ValidKey(string key) => KeyPattern.IsMatch(key);

    // Which of a target's consumer blocks a field's native row is: projectile settings, the DamageInfo row (direct/explosion damage and the
    // per-hit status strength), the explosion settings, or the shared status definition (duration).
    public static string Block(string path, string semanticFieldId) => semanticFieldId switch
    {
        _ when semanticFieldId.StartsWith("projectile.", StringComparison.Ordinal) => "projectile",
        _ when semanticFieldId.StartsWith("explosion.damage.", StringComparison.Ordinal) || semanticFieldId.StartsWith("damage.", StringComparison.Ordinal) => "damage",
        "status.strength" => "damage",
        "status.duration" => "definition",
        _ when semanticFieldId.StartsWith("explosion.", StringComparison.Ordinal) => "explosion",
        _ => path,
    };

    public static ThrowableCatalog Read(byte[] bytes)
    {
        try
        {
            if (bytes.Length > MaxBytes) throw new InvalidDataException("Throwable capability file exceeds size limit.");
            using var doc = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 32 }); MetadataReader.RejectDuplicates(doc.RootElement);
            var root = doc.RootElement;
            if (root.GetProperty("contract").GetString() != Contract || root.GetProperty("schemaVersion").GetInt32() != 1)
                throw new UnsupportedSdkException("Unsupported throwable authoring contract.");
            var c = JsonSerializer.Deserialize<ThrowableCatalogJson>(bytes, Options)!;
            Check(c.Safety.Writes == 0 && c.Safety.ProtectionChanges == 0 && c.Safety.FixtureFallback == "disabled");
            Check(c.Throwables.Select(t => t.Name).Distinct().Count() == c.Throwables.Length && c.Throwables.Select(t => t.SemanticId).Distinct().Count() == c.Throwables.Length);
            if (!c.Throwables.SelectMany(t => t.Targets).All(g => Accessors.ContainsKey(g.Path))) throw new UnsupportedSdkException("Unsupported throwable target.");
            var keys = new HashSet<string>(StringComparer.Ordinal);
            var definitions = c.FieldDefinitions.ToDictionary(d => d.Id, StringComparer.Ordinal);
            foreach (var t in c.Throwables)
            {
                Check(ValidName(t.Name) && SemanticIdPattern.IsMatch(t.SemanticId) && !string.IsNullOrWhiteSpace(t.Category) && !string.IsNullOrWhiteSpace(t.Family));
                // Only resolved identities carry writable fields.
                Check(t.Identity.Status == "RESOLVED" || t.Targets.All(g => g.Fields.All(f => !f.Editable)));
                Check(t.Targets.Select(g => (g.Path, g.Key)).Distinct().Count() == t.Targets.Length && t.Targets.Any(g => g.Path == "throwable"));
                foreach (var g in t.Targets)
                {
                    // The Lua target is derived from the path; it must be exactly the accessor chain Runtime publishes.
                    var chain = g.Path == "status_effect" ? [.. Accessors[g.Path], g.Key ?? ""] : Accessors[g.Path];
                    Check(g.Accessor.SequenceEqual(chain) && (g.Path == "status_effect") == (g.Key != null) && (g.Key == null || ValidKey(g.Key)));
                    foreach (var f in g.Fields)
                    {
                        Check(keys.Add(f.InstanceKey) && f.InstanceKey.StartsWith(Resource + ":", StringComparison.Ordinal) && f.InstanceKey.Length <= 256);
                        // The typed API constant is the one the published field definition names (constants are not derived from IDs).
                        Check(Api.IsMatch(f.ApiFieldConstant) && definitions.TryGetValue(f.SemanticFieldId, out var def) && def.ApiFieldConstant == f.ApiFieldConstant && def.Type == f.Type
                            && !string.IsNullOrWhiteSpace(f.DisplayName) && f.Type is "integer" or "number" && f.Baseline.ValueKind == JsonValueKind.Number);
                        Check(f.Acknowledgements.All(a => a is "allow_shared" or "allow_unverified_effect") && f.Acknowledgements.Contains("allow_shared") == f.Scope.Shared
                            && f.Scope.Kind is "throwable_local" or "settings_definition" && f.Scope.Shared == (f.Scope.Kind == "settings_definition") && Tiers.Contains(f.Evidence.Tier));
                        // Every writable field carries Runtime's unverified-effect opt-in and a range the baseline lies in; read-only ones say why.
                        Check(f.Editable
                            ? f.Acknowledgements.Contains("allow_unverified_effect") && f.Range is { } r && r.Min <= r.Max && r.Integer == (f.Type == "integer")
                                && f.Baseline.GetDouble() >= r.Min && f.Baseline.GetDouble() <= r.Max && f.Reason == null
                            : !string.IsNullOrWhiteSpace(f.Reason));
                        // A shared row's consumers must be published under its block.
                        Check(!f.Scope.Shared || ThrowableCatalog.Consumers(g, f) is { Throwables.Length: > 0 } b && b.Throwables.Contains(t.Name));
                    }
                }
            }
            var s = c.Summary; var all = c.Throwables.SelectMany(t => t.Targets.SelectMany(g => g.Fields.Select(f => (g, f)))).ToArray();
            Check(s.Throwables == c.Throwables.Length && s.Resolved == c.Throwables.Count(t => t.Identity.Status == "RESOLVED")
                && s.WritableThrowables == c.Throwables.Count(t => t.WritableFields > 0) && s.FieldInstances == all.Length
                && s.WritableFieldInstances == all.Count(x => x.f.Editable) && s.SharedFieldInstances == all.Count(x => x.f.Scope.Shared)
                && s.FieldsByTarget.All(p => all.Count(x => x.g.Path == p.Key) == p.Value) && s.FieldsByTarget.Values.Sum() == all.Length);
            return new() { Throwables = c.Throwables, Summary = c.Summary, FieldInstances = c.Throwables.SelectMany(t => t.Targets.SelectMany(g => g.Fields.Select(f => Adapt(t, g, f)))).ToArray() };
        }
        catch (Exception e) when (e is JsonException or KeyNotFoundException or NullReferenceException or ArgumentException or InvalidOperationException or FormatException)
        { throw new InvalidDataException("Malformed throwable capability metadata.", e); }
    }

    // A throwable-local record belongs to one throwable target. A shared settings row is identified by its published consumer block, so the
    // same row reached through two throwables gets one owner and is never written twice with different values.
    public static string OwnerKey(Throwable t, ThrowableTarget g, ThrowableField f)
    {
        if (!f.Scope.Shared) return "throwable-local:" + t.SemanticId + "|" + g.Path + "|" + g.Key;
        var block = Block(g.Path, f.SemanticFieldId); var b = ThrowableCatalog.Consumers(g, f)!;
        return "throwable-row:" + block + "|" + (block == "definition" ? g.Key + "|" : "") + string.Join(",", b.Throwables.Order(StringComparer.Ordinal))
            + "|" + b.DirectReferences + "/" + b.RootEntities + "/" + b.OtherEntities + "/" + b.SettingsOnlyReferences;
    }

    private static EntityField Adapt(Throwable t, ThrowableTarget g, ThrowableField f)
    {
        var owner = OwnerKey(t, g, f);
        var target = new EntityTarget(Resource, g.Path, Throwable: t.Name, Effect: g.Key);
        // One transaction per throwable target and native row; all of a throwable's operations form one plan.
        var operation = "throwable-op:" + t.SemanticId + "|" + g.Path + "|" + g.Key + "|" + Block(g.Path, f.SemanticFieldId);
        var consumers = ThrowableCatalog.Consumers(g, f);
        return new EntityField(f.InstanceKey, f.SemanticFieldId, f.DisplayName, f.Type, f.Unit, f.Baseline.Clone(), f.Editable, f.Reason, target,
            owner, operation, "throwable-plan:" + t.SemanticId, "patch_or_transaction", f.Acknowledgements.Contains("allow_shared"), f.Scope.Shared, [], owner,
            ReviewedScopeComplete: !f.Scope.Shared, DynamicConsumersPossible: f.Scope.Shared, BackingObjectKind: g.Relationship,
            Domain: f.SemanticFieldId.Split('.')[0], ApiFieldConstant: f.ApiFieldConstant, PlanPhase: 1, DependsOn: [],
            Evidence: new EntityEvidence(f.Evidence.Tier, NativeOwner: f.Evidence.Native, GameplayWriteEffect: f.Evidence.GameplayWriteEffect),
            Provenance: "ThrowableAuthoringCapabilities (" + f.Scope.Kind + (consumers != null ? ", " + consumers.RootEntities + " consumer entities" : "") + ")",
            Acknowledgement: f.Acknowledgements.Contains("allow_unverified_effect") ? "allow_unverified_effect" : null,
            Range: f.Range is { } r ? new EntityRange(r.Min, r.Max, r.Integer) : null, AcknowledgementReason: f.AcknowledgementReason);
    }
}
