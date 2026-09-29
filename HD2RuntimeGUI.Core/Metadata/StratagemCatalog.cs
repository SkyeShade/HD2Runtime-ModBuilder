using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using HD2RuntimeGUI.Core.Storage;

namespace HD2RuntimeGUI.Core.Metadata;

// 0.26.0 maxUses adds mode (finite/unlimited), range, transitions, gameplay-proven values, acknowledgement and caveat.
public sealed record StratagemAvailability(JsonElement Value, bool Writable, string? Reason, string? Field, string? Unit,
    string? Mode = null, double[]? Range = null, string[]? Transitions = null, JsonElement[]? GameplayProvenValues = null, string? Acknowledgement = null, string? Caveat = null);
public sealed record StratagemBlockedField(string Field, string Reason);
// Published semantic identity of a deployed entity. No native component records are exposed.
// Populated native damage zones of a deployed entity (0.23.0+). Distinct zones are never merged.
public sealed record StratagemDamageZone(string ZoneId, string? Name, JsonElement Armor, JsonElement Health, JsonElement AffectsMainHealth, string[] FieldInstances);
// Shield projector configuration that is a component of the same deployed entity as the physical base (0.23.0+).
public sealed record StratagemShieldConfig(string IdentityRole, string Component, bool SameEntityAsBase, string RuntimeShieldInstance,
    StratagemBlockedField[] BlockedFields, string[] FieldInstances);
public sealed record StratagemEntityIdentity(string Kind, string IdentityStatus, string IdentityRole, int FieldCount,
    string[] WeaponBranches, StratagemBlockedField[] BlockedFields, StratagemDamageZone[]? DamageZones = null, StratagemShieldConfig? Shield = null);
public sealed record StratagemDefinition(string Name, string Family, string RootResolution, string? BlockedReason,
    string[] AttackRoles, StratagemAvailability CooldownCapability, StratagemAvailability MaxUses,
    StratagemAvailability CallInTime, int? UsesPerRearm, double? RearmTime,
    StratagemAvailability? BarrageScheduling, StratagemEntityIdentity? DeployedEntity = null,
    bool? MineScopeDeferred = null, bool? MineInstanceResolved = null, string? SemanticId = null, StratagemDelivers? Delivers = null,
    StratagemUiIcon? UiIcon = null, StratagemMine? Mine = null);
// Unreleased Runtime (0.28.0 development): a mine stratagem's resolved explosion (attack "mine", hd2.stratagem(name):mine()),
// with its DamageInfo and status rows as attacks under the "mine" weapon of the deployer.
public sealed record StratagemMine(string ExplosionAttack, bool ExplosionResolved, bool MineEntityDefined, string ExplosionAgreement,
    double? TriggerToDetonationSeconds, string Api)
{
    public const string Weapon = "mine";
}
// Game UI icon identity published by Runtime (after 0.24.0): native StratagemType -> icon key in the game's own icon library.
// Artwork is never in the SDK; tooling reads it from the local game install. Only state "resolved" names usable vector artwork.
// 0.25.1+ also publishes the blocker of a non-resolved icon (kind + reason) and structural provenance.
public sealed record StratagemUiIconBlocker(string Kind, string? Reason = null);
public sealed record StratagemUiIcon(string State, string? NativeType = null, int? NativeTypeValue = null, string? IconKey = null, string? Library = null, string? Reason = null,
    StratagemUiIconBlocker? Blocker = null)
{
    public const string Library0 = "content/ui/shared/resources/generated_icons/stratagem_icons";
    public static readonly string[] States = ["resolved", "empty_template", "unbound", "no_native_root"];
    // Why no icon is shown: Runtime's reason, else its published description of the state.
    public string? FallbackReason(StratagemUiIconContract? contract) => State == "resolved" ? null : Reason ?? Blocker?.Reason ?? contract?.States?.GetValueOrDefault(State);
}
// 0.25.1: the icon identity contract. Keys come from this game icon library (by content hash); emptyTemplates lists the
// library templates without vector artwork. Artwork is never published; tooling reads it from the local install.
public sealed record StratagemUiIconContract(string Contract, int SchemaVersion, Dictionary<string, string>? States, string Library, string LibrarySha256,
    string[] EmptyTemplates, bool ArtworkPublished);
// Descriptive (non-authoring) graph nodes. Schema 1 nodes carry wiki kind/roles; schema 2 defensive nodes carry kind/source/evidence.
public sealed record StratagemBranch(string Id, string Name, string? WikiKind, string[]? SemanticRoles, string? ParentId,
    string[] ChildIds, string Stratagem, string Family, string Correlation, string? Kind = null, string? SourcePath = null,
    string? RelationshipEvidence = null, bool? RelationshipVerified = null)
{
    [JsonIgnore] public string KindLabel => WikiKind ?? Kind ?? "Branch";
}
public sealed record StratagemAttack(string Stratagem, string Family, string Role, string Path, string Kind, string? ParentRole,
    string? Entity = null, string? Weapon = null);
// Entity/weapon are omitted when absent so schema-1 capability evidence stays byte-identical.
public sealed record StratagemTarget(string Resource, string Stratagem, string Path, string? Attack,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Entity = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Weapon = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Zone = null);
// Unreleased Runtime (0.28.0 development): a consumer outside the stratagem catalog (for example the mine entity a minefield
// throws) is published by an opaque consumer ID and a description instead of a stratagem. Omitted when absent, so the evidence of
// catalog consumers stays byte-identical.
public sealed record StratagemConsumer(string? Stratagem, string Path,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? ExternalConsumer = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? SemanticStatus = null)
{
    [JsonIgnore] public string Label => Stratagem ?? SemanticStatus ?? ExternalConsumer ?? "";
}
public sealed record StratagemField(string InstanceKey, string SemanticFieldId, string DisplayName, string Type, string? Unit,
    JsonElement CurrentDefault, bool Editable, string? Reason, StratagemTarget Target, string BackingObjectId,
    string OperationGroup, string PlanGroup, string Requires, bool AllowSharedRequired, bool Shared,
    StratagemConsumer[] SharedConsumers, bool ReviewedScopeComplete, bool DynamicConsumersPossible,
    string Provenance, string BackingObjectKind, string ApiFieldConstant, string Domain, int PlanPhase, string[] DependsOn,
    string? SharedScopeKey = null,
    // 0.26.0 mission uses (type stratagem_uses): current mode, Runtime's unlimited token, range, published transitions and opt-in.
    string? UsesMode = null, string? UnlimitedValue = null, long? NativeUnlimited = null, double? Min = null, double? Max = null,
    string[]? Transitions = null, string? Acknowledgement = null, string? AcknowledgementReason = null, JsonElement[]? GameplayProvenValues = null,
    string? Caveat = null)
{
    // Schema 1 publishes its reviewed scope on the backing identity; schema 2 publishes a separate scope key.
    [JsonIgnore] public string ScopeKey => SharedScopeKey ?? BackingObjectId;
}
public sealed record StratagemDeployedEntityRecord(string Stratagem, string Family, StratagemEntityIdentity Identity,
    string[] FieldInstances, string[] AttackRoles, StratagemBlockedField[] BlockedFields);
public sealed record StratagemBackingObject(string BackingObjectId, string Kind, bool Shared, StratagemConsumer[] SharedConsumers,
    string SharedScopeKey, bool RequiresSharedAcknowledgement, int ReviewedConsumerCount, bool ReviewedScopeComplete,
    bool DynamicConsumersPossible, string[] FieldInstances);
public sealed record StratagemOperationGroup(string OperationGroup, string BackingObjectId, string RuntimeBackingScope,
    StratagemTarget Target, string[] FieldInstances, string[] PlanGroups, string RecommendedApi, bool AllowSharedRequired,
    int Phase, string[] Dependencies);
public sealed record StratagemInstanceAudit(int InternalPromotedInstances, int PublishedCanonicalInstances,
    string[] MissingInstances, string[] UnexpectedInstances, int DuplicateInstanceKeys, bool ExactMatch);
public sealed record StratagemSummary(int OffensiveRootsResolved, int OrbitalRootsResolved, int EagleRootsResolved,
    int SupportRootsResolved, int CooldownWritable, int MaxUsesWritable, int EagleUsesPerRearmWritable,
    int EagleRearmTimeWritable, int FieldInstances, int WritableFieldInstances, int BackingObjectCount,
    int SharedConsumerScopeCount, int ImportedAttackBranches, int NativeBackingBranches,
    int? SharedBackingObjectCount = null, int? SentryRootsResolved = null, int? EmplacementRootsResolved = null,
    int? MineRootsResolved = null, int? DeployedEntitiesResolved = null, int? MineDeploymentEntitiesResolved = null,
    int? MineInstancesResolved = null, int? MineAttackBranchesWritable = null, int? HealthWritable = null,
    int? ArmorWritable = null, int? MountedWeaponsResolved = null, int? MultiWeaponEntities = null, int? AmmoWritable = null,
    int? MountedWeaponsWithAmmo = null, int? FireRateWritable = null, int? HeatWritable = null,
    int? CanonicalBackingObjects = null, int? CanonicalOperationGroups = null,
    Dictionary<string, int>? NativeBranchInstancesByKind = null, Dictionary<string, int>? DefensiveNativeBranchInstancesByKind = null);
public sealed record StratagemCatalog(int SchemaVersion, string Contract, string CanonicalCollection,
    StratagemDefinition[] Stratagems, StratagemBranch[] SemanticBranches, StratagemAttack[] Attacks,
    StratagemField[] FieldInstances, StratagemSummary Summary,
    StratagemDeployedEntityRecord[]? DeployedEntities = null, StratagemBackingObject[]? BackingObjects = null,
    StratagemOperationGroup[]? OperationGroups = null, StratagemInstanceAudit? InstanceAudit = null,
    SupportCallInLinkage? SupportCallInLinks = null, StratagemUiIconContract? UiIconContract = null)
{
    public static readonly string[] DefensiveFamilies = ["sentry", "emplacement", "mine"];
    public static bool IsDefensive(string family) => DefensiveFamilies.Contains(family);
    public StratagemField Field(string key) => FieldInstances.SingleOrDefault(f => f.InstanceKey == key)
        ?? throw new InvalidDataException("Stratagem capability is missing. Review or reset this modification.");
    public StratagemField? Find(string key) => FieldInstances.FirstOrDefault(f => f.InstanceKey == key);
    public static bool SameSemanticTarget(StratagemField f, string stratagem, string path, string? entity, string? weapon, string? attack, string field, string? zone = null)
        => f.Target.Stratagem == stratagem && f.Target.Path == path && f.Target.Entity == entity && f.Target.Weapon == weapon && f.Target.Zone == zone
            && f.Target.Attack == attack && f.SemanticFieldId == field;
    // Canonical instance keys may be renamed between SDK releases. The saved semantic target
    // (stratagem, graph path, entity, weapon, attack and field) is the stable identity; it must match exactly one instance.
    public StratagemField? Resolve(Models.StratagemChange c)
    {
        if (Find(c.InstanceKey) is { } exact) return exact;
        var matches = FieldInstances.Where(f => SameSemanticTarget(f, c.Stratagem, c.Path, c.Entity, c.Weapon, c.Attack, c.SemanticFieldId, c.Zone)).Take(2).ToArray();
        return matches.Length == 1 ? matches[0] : null;
    }
    // Components of the "main" deployed entity addressed as hd2.stratagem(name):deployed_entity():<path>(): the shield (0.23.0) and, in
    // unreleased Runtime (0.28.0 development) SDKs, the sentry turret, its targeting sensor and a minefield's thrower.
    public static readonly string[] EntityComponents = ["shield", "turret", "targeting", "minefield"];
    public StratagemDefinition? Root(string name) => Stratagems.FirstOrDefault(s => s.Name == name);
    public StratagemAttack? AttackOf(StratagemField f) => f.Target.Attack == null ? null : Attacks.SingleOrDefault(a => a.Stratagem == f.Target.Stratagem && a.Role == f.Target.Attack);
    public StratagemBackingObject? Backing(StratagemField f) => BackingObjects?.SingleOrDefault(b => b.BackingObjectId == f.BackingObjectId);
    public string BranchLabel(StratagemField f) => f.Target.Path switch
    {
        "eagle_rearm" => "Eagle Shared System",
        "stratagem" => "Stratagem",
        "deployed_entity" => "Deployed Entity",
        "damage_zone" => "Damage Zone · " + f.Target.Zone,
        "shield" => "Shield",
        "turret" => "Turret", "targeting" => "Targeting", "minefield" => "Minefield",
        "weapon" => "Mounted Weapon · " + StratagemGraph.Title(f.Target.Weapon!),
        _ => AttackOf(f)!.Path.Replace("/", " → "),
    };
}
public interface IStratagemCatalogReader { StratagemCatalog Read(byte[] bytes); }
public sealed class StratagemCatalogReader : IStratagemCatalogReader
{
    public const string FileName = "StratagemAuthoringCapabilities.json";
    public const int MaxBytes = 16 * 1024 * 1024;
    private static readonly Regex Identifier = new(@"\A[a-z][a-z0-9_]{0,63}\z", RegexOptions.CultureInvariant);
    // The public contract must never carry native layout or record identity.
    private static readonly HashSet<string> NativeProperties = new(StringComparer.OrdinalIgnoreCase)
    { "offset", "address", "recordIndex", "indexRow", "nativeIdentity", "recordType", "row", "width", "storage", "base", "owner",
      "rootLink", "rootProjectiles", "componentId", "resourceId", "nativeResource", "backing", "graph", "payload", "payloads" };
    public StratagemCatalog Read(byte[] bytes)
    {
        try
        {
            if (bytes.Length > MaxBytes) throw new InvalidDataException("Stratagem catalog exceeds size limit.");
            using var doc = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 32 });
            MetadataReader.RejectDuplicates(doc.RootElement);
            var options = new JsonSerializerOptions(JsonStorage.Options) { UnmappedMemberHandling = JsonUnmappedMemberHandling.Skip };
            var c = JsonSerializer.Deserialize<StratagemCatalog>(bytes, options)!;
            void Check(bool value, [System.Runtime.CompilerServices.CallerLineNumber] int line = 0) { if (!value) throw new InvalidDataException($"Inconsistent or unsupported stratagem capability metadata (check {line})."); }
            var v2 = (c.SchemaVersion, c.Contract) switch
            {
                (1, "hd2runtime.stratagem.guarded_authoring.v1") => false,
                (2, "hd2runtime.stratagem.guarded_authoring.v2") => true,
                _ => throw new UnsupportedSdkException("Unsupported stratagem authoring schema."),
            };
            if (c.CanonicalCollection != "fieldInstances") throw new UnsupportedSdkException("Unsupported stratagem authoring schema.");
            var safety = doc.RootElement.GetProperty("safety");
            Check(safety.GetProperty("writes").GetInt32() == 0 && safety.GetProperty("protectionChanges").GetInt32() == 0 && safety.GetProperty("fixtureFallback").GetString() == "disabled");
            if (v2) RejectNativeIdentifiers(doc.RootElement);
            var roots = c.Stratagems.ToDictionary(w => w.Name, StringComparer.Ordinal);
            Check(roots.Count <= 400 && c.FieldInstances.Length <= 8000 && c.FieldInstances.Select(f => f.InstanceKey).Distinct().Count() == c.FieldInstances.Length);
            Check(c.SemanticBranches.All(b => !string.IsNullOrWhiteSpace(b.Id) && !string.IsNullOrWhiteSpace(b.Name) && roots.ContainsKey(b.Stratagem) && b.ChildIds != null
                && (b.WikiKind != null && b.SemanticRoles != null || v2 && b.Kind != null && b.SourcePath != null)));
            Check(c.Attacks.GroupBy(a => (a.Stratagem, a.Role)).All(g => g.Count() == 1) && c.Attacks.All(a => roots.ContainsKey(a.Stratagem)));
            string[] paths = v2 ? ["stratagem", "attack", "eagle_rearm", "deployed_entity", "weapon", "damage_zone", .. StratagemCatalog.EntityComponents] : ["stratagem", "attack", "eagle_rearm"];
            foreach (var f in c.FieldInstances)
            {
                Check(f.InstanceKey.StartsWith("stratagem:", StringComparison.Ordinal) && f.InstanceKey.Length <= 512
                    && f.Target.Resource == "stratagem" && roots.ContainsKey(f.Target.Stratagem)
                    && paths.Contains(f.Target.Path)
                    && Regex.IsMatch(f.ApiFieldConstant, @"\Ahd2\.fields\.[a-z_]+\.[a-z_0-9]+\z")
                    && (f.Type is "number" or "integer" or "boolean" || f.Type == StratagemUses.Type && f.SemanticFieldId == StratagemUses.Field && f.Target.Path == "stratagem"
                        && (f.Editable
                            ? f.UnlimitedValue == StratagemUses.Unlimited && f.NativeUnlimited == uint.MaxValue && f.Transitions is { Length: > 0 }
                                && f.Transitions.All(t => t is StratagemUses.FiniteToUnlimited or StratagemUses.UnlimitedToFinite or StratagemUses.FiniteToFinite)
                                && f.UsesMode == (StratagemUses.IsUnlimited(f.CurrentDefault) ? "unlimited" : "finite") && f.Acknowledgement is null or "allow_unverified_effect"
                            // Eagles: the same native field is uses per rearm, so mission uses are read-only with Runtime's reason.
                            : f.CurrentDefault.ValueKind == JsonValueKind.Null && f.UsesMode == null && (f.Transitions ?? []).Length == 0))
                    && !string.IsNullOrWhiteSpace(f.DisplayName)
                    && !string.IsNullOrWhiteSpace(f.BackingObjectId) && !string.IsNullOrWhiteSpace(f.OperationGroup) && !string.IsNullOrWhiteSpace(f.PlanGroup)
                    && f.PlanPhase == 1 && f.DependsOn.Length == 0 && f.Requires == "patch_or_transaction"
                    && f.AllowSharedRequired == f.Shared && f.SharedConsumers.Length > 0);
                var attack = f.Target.Attack == null ? null : c.Attacks.SingleOrDefault(a => a.Stratagem == f.Target.Stratagem && a.Role == f.Target.Attack);
                Check(f.Target.Path == "attack" ? attack != null : f.Target.Attack == null);
                // Deployed targets must carry the published entity/weapon path of their graph node, never an inferred one.
                Check(f.Target.Path switch
                {
                    "deployed_entity" => f.Target.Entity != null && f.Target.Weapon == null && f.Target.Zone == null,
                    _ when StratagemCatalog.EntityComponents.Contains(f.Target.Path) => f.Target.Entity != null && f.Target.Weapon == null && f.Target.Zone == null,
                    "damage_zone" => f.Target.Entity != null && f.Target.Weapon == null && f.Target.Zone != null,
                    "weapon" => f.Target.Entity != null && f.Target.Weapon != null && f.Target.Zone == null,
                    "attack" => f.Target.Zone == null && f.Target.Entity == attack!.Entity && f.Target.Weapon == attack.Weapon && (f.Target.Entity == null) == (f.Target.Weapon == null),
                    _ => f.Target.Entity == null && f.Target.Weapon == null && f.Target.Zone == null,
                });
                Check((f.Target.Entity == null || Identifier.IsMatch(f.Target.Entity)) && (f.Target.Weapon == null || Identifier.IsMatch(f.Target.Weapon))
                    && (f.Target.Attack == null || Identifier.IsMatch(f.Target.Attack)) && (f.Target.Zone == null || Identifier.IsMatch(f.Target.Zone)));
                if (f.Target.Entity != null) Check(roots[f.Target.Stratagem].DeployedEntity != null);
                if (v2) Check(!string.IsNullOrWhiteSpace(f.SharedScopeKey));
                if (f.Editable) { Check(roots[f.Target.Stratagem].RootResolution == "UNIQUE"); _ = Generation.StratagemScalar.Normalize(f, f.CurrentDefault); }
                else Check(!string.IsNullOrWhiteSpace(f.Reason));
            }
            foreach (var group in c.FieldInstances.GroupBy(f => f.OperationGroup)) Check(group.Select(f => f.BackingObjectId).Distinct().Count() == 1);
            foreach (var group in c.FieldInstances.GroupBy(f => f.ScopeKey))
                Check(group.Select(Generation.StratagemChangeService.ApprovalEvidence).Distinct().Count() == 1);
            Check(c.Summary.FieldInstances == c.FieldInstances.Length && c.Summary.WritableFieldInstances == c.FieldInstances.Count(f => f.Editable)
                && c.Summary.BackingObjectCount == c.FieldInstances.Select(f => f.BackingObjectId).Distinct().Count()
                && c.Summary.SharedConsumerScopeCount == c.FieldInstances.Where(f => f.Shared).Select(f => f.ScopeKey).Distinct().Count()
                && c.Summary.OffensiveRootsResolved == roots.Values.Count(w => w.RootResolution == "UNIQUE" && w.Family is "orbital" or "eagle")
                && c.Summary.SupportRootsResolved == roots.Values.Count(w => w.RootResolution == "UNIQUE" && w.Family == "support")
                && c.Summary.ImportedAttackBranches == c.SemanticBranches.Length && c.Summary.NativeBackingBranches == c.Attacks.Length);
            if (v2) ValidateV2(c, roots, ok => Check(ok));
            else Check(c.Stratagems.All(s => s.DeployedEntity == null) && c.FieldInstances.All(f => f.Target.Entity == null));
            // Optional icon identity: only a known state; a named icon must be a plain template key in the stratagem icon library.
            Check(c.Stratagems.All(s => s.UiIcon is null || StratagemUiIcon.States.Contains(s.UiIcon.State)
                && (s.UiIcon.IconKey is null ? s.UiIcon.State is "unbound" or "no_native_root"
                    : Regex.IsMatch(s.UiIcon.IconKey, @"\A[A-Za-z][A-Za-z0-9]{0,63}\z") && s.UiIcon.Library == StratagemUiIcon.Library0)));
            // 0.25.1 contract: when published it names the same library, never publishes artwork, and its empty-template list agrees
            // with every icon state (an empty template never counts as resolved artwork).
            if (c.UiIconContract is { } contract)
            {
                if (contract.Contract != "hd2runtime.stratagem.ui_icon.v1" || contract.SchemaVersion != 1) throw new UnsupportedSdkException("Unsupported stratagem icon identity contract.");
                Check(contract.Library == StratagemUiIcon.Library0 && !contract.ArtworkPublished && Regex.IsMatch(contract.LibrarySha256, @"\A[0-9A-Fa-f]{64}\z")
                    && (contract.States == null || contract.States.Keys.All(StratagemUiIcon.States.Contains))
                    && c.Stratagems.All(s => s.UiIcon is null || s.UiIcon.State switch
                    {
                        "resolved" => !contract.EmptyTemplates.Contains(s.UiIcon.IconKey),
                        "empty_template" => s.UiIcon.IconKey is { } k && contract.EmptyTemplates.Contains(k),
                        _ => true,
                    }));
            }
            return c;
        }
        catch (Exception e) when (e is JsonException or NullReferenceException or InvalidOperationException or KeyNotFoundException or ArgumentException)
        { throw new InvalidDataException("Malformed stratagem capabilities.", e); }
    }
    // Keys of count dictionaries such as summary.writableByDomain are published domain names (for example "payload"), not record properties.
    private static readonly HashSet<string> NameKeyedCounts = new(StringComparer.Ordinal) { "writableByDomain" };
    private static void RejectNativeIdentifiers(JsonElement node, bool keysAreNames = false)
    {
        if (node.ValueKind == JsonValueKind.Object)
            foreach (var p in node.EnumerateObject())
            {
                if (NativeProperties.Contains(p.Name) && !(keysAreNames && p.Value.ValueKind == JsonValueKind.Number))
                    throw new InvalidDataException("Stratagem capabilities expose native identifiers.");
                RejectNativeIdentifiers(p.Value, NameKeyedCounts.Contains(p.Name));
            }
        else if (node.ValueKind == JsonValueKind.Array) foreach (var child in node.EnumerateArray()) RejectNativeIdentifiers(child);
        // 0xFFFFFFFF is the documented unlimited mission-use value (0.26.0 max_uses prose), not an address or resource identifier.
        else if (node.ValueKind == JsonValueKind.String && Regex.IsMatch(Regex.Replace(node.GetString()!, @"\b0xFFFFFFFF\b", ""), @"\b0x[0-9a-fA-F]+\b"))
            throw new InvalidDataException("Stratagem capabilities expose native identifiers.");
    }
    private static void ValidateV2(StratagemCatalog c, Dictionary<string, StratagemDefinition> roots, Action<bool> Check)
    {
        Check(c.DeployedEntities != null && c.BackingObjects != null && c.OperationGroups != null && c.InstanceAudit != null);
        var fields = c.FieldInstances.ToDictionary(f => f.InstanceKey, StringComparer.Ordinal);
        static string Consumers(IEnumerable<StratagemConsumer> list) => string.Join("\n", list.Select(x => x.Stratagem + "\u001f" + x.Path + "\u001f" + x.ExternalConsumer).Order(StringComparer.Ordinal));
        var audit = c.InstanceAudit!;
        Check(audit.ExactMatch && audit.DuplicateInstanceKeys == 0 && audit.MissingInstances.Length == 0 && audit.UnexpectedInstances.Length == 0
            && audit.InternalPromotedInstances == c.FieldInstances.Length && audit.PublishedCanonicalInstances == c.FieldInstances.Length);

        // Backing semantic objects and operation groups are distinct published concepts; each is validated on its own terms.
        var backings = c.BackingObjects!.ToDictionary(b => b.BackingObjectId, StringComparer.Ordinal);
        Check(backings.Count == c.BackingObjects!.Length && backings.Count == c.FieldInstances.Select(f => f.BackingObjectId).Distinct().Count());
        foreach (var b in c.BackingObjects!)
        {
            Check(b.FieldInstances.Length > 0 && b.FieldInstances.Distinct().Count() == b.FieldInstances.Length
                && b.ReviewedConsumerCount == b.SharedConsumers.Length && b.RequiresSharedAcknowledgement == b.Shared && !string.IsNullOrWhiteSpace(b.SharedScopeKey));
            foreach (var key in b.FieldInstances)
            {
                var f = fields[key];
                Check(f.BackingObjectId == b.BackingObjectId && f.BackingObjectKind == b.Kind && f.SharedScopeKey == b.SharedScopeKey && f.Shared == b.Shared
                    && f.ReviewedScopeComplete == b.ReviewedScopeComplete && f.DynamicConsumersPossible == b.DynamicConsumersPossible
                    && Consumers(f.SharedConsumers) == Consumers(b.SharedConsumers));
            }
        }
        Check(c.FieldInstances.All(f => backings[f.BackingObjectId].FieldInstances.Contains(f.InstanceKey)));
        Check(c.FieldInstances.GroupBy(f => f.ScopeKey).All(g => g.Select(f => f.BackingObjectId).Distinct().Count() == 1));
        var groups = c.OperationGroups!.ToDictionary(g => g.OperationGroup, StringComparer.Ordinal);
        Check(groups.Count == c.OperationGroups!.Length && groups.Count == c.FieldInstances.Select(f => f.OperationGroup).Distinct().Count());
        foreach (var g in c.OperationGroups!)
        {
            Check(backings.TryGetValue(g.BackingObjectId, out var b) && g.RuntimeBackingScope == b!.Kind && g.Phase == 1 && g.Dependencies.Length == 0
                && g.FieldInstances.Length is > 0 and <= 32 && g.FieldInstances.Distinct().Count() == g.FieldInstances.Length
                && g.RecommendedApi == (g.FieldInstances.Length > 1 ? "hd2.transaction" : "hd2.patch"));
            foreach (var key in g.FieldInstances)
            {
                var f = fields[key];
                // Operation groups are target-specific: one transaction target per group.
                Check(f.OperationGroup == g.OperationGroup && f.BackingObjectId == g.BackingObjectId && f.Target == g.Target
                    && g.PlanGroups.Contains(f.PlanGroup) && f.PlanPhase == g.Phase && f.AllowSharedRequired == g.AllowSharedRequired);
            }
        }
        Check(c.FieldInstances.All(f => groups[f.OperationGroup].FieldInstances.Contains(f.InstanceKey)));

        var deployed = c.DeployedEntities!.ToDictionary(e => e.Stratagem, StringComparer.Ordinal);
        Check(deployed.Count == c.DeployedEntities!.Length && deployed.Count == roots.Values.Count(r => r.DeployedEntity != null));
        foreach (var root in roots.Values)
        {
            Check(IsDefensive(root.Family) == (root.DeployedEntity != null) && (root.Family == "mine") == (root.MineScopeDeferred == true || root.Mine is { ExplosionResolved: true })
                && (root.Mine == null || root.Family == "mine" && root.MineScopeDeferred == false && root.Mine.ExplosionAttack == StratagemMine.Weapon));
            if (root.DeployedEntity is not { } identity) { Check(root.MineInstanceResolved == null); continue; }
            var e = deployed[root.Name];
            // Zone and shield instances are listed by identity.damageZones / identity.shield (checked in ZonesAndShieldMatch).
            var owned = c.FieldInstances.Where(f => f.Target.Stratagem == root.Name && f.Target.Entity != null && f.Target.Path != "damage_zone" && !StratagemCatalog.EntityComponents.Contains(f.Target.Path)).Select(f => f.InstanceKey).Order(StringComparer.Ordinal);
            var weapons = c.FieldInstances.Where(f => f.Target.Stratagem == root.Name && f.Target.Weapon != null).Select(f => f.Target.Weapon!)
                .Concat(c.Attacks.Where(a => a.Stratagem == root.Name && a.Weapon != null).Select(a => a.Weapon!))
                .Where(w => root.Mine is not { ExplosionResolved: true } || w != StratagemMine.Weapon).Distinct().Order(StringComparer.Ordinal);
            Check(e.Family == root.Family && JsonEquals(e.Identity, identity) && JsonEquals(e.BlockedFields, identity.BlockedFields)
                && e.FieldInstances.Order(StringComparer.Ordinal).SequenceEqual(owned)
                && e.AttackRoles.Order(StringComparer.Ordinal).SequenceEqual(c.Attacks.Where(a => a.Stratagem == root.Name).Select(a => a.Role).Order(StringComparer.Ordinal))
                && identity.WeaponBranches.Order(StringComparer.Ordinal).SequenceEqual(weapons) && identity.WeaponBranches.All(Identifier.IsMatch)
                && identity.FieldCount == c.FieldInstances.Count(f => f.Target.Stratagem == root.Name && f.Target.Path == "deployed_entity")
                && ZonesAndShieldMatch(c, root.Name, identity)
                && identity.BlockedFields.All(b => !string.IsNullOrWhiteSpace(b.Field) && !string.IsNullOrWhiteSpace(b.Reason)));
            // Mines resolve their deployment system only. Unresolved mine attacks must never become controls.
            if (root.Family == "mine" && root.MineInstanceResolved != true && root.Mine is not { ExplosionResolved: true })
                Check(c.Attacks.All(a => a.Stratagem != root.Name) && c.FieldInstances.All(f => f.Target.Stratagem != root.Name || f.Target.Path is "stratagem" or "deployed_entity"));
            // A resolved mine explosion is published only under the deployer's "mine" weapon, never as a sentry-style mounted weapon.
            else if (root.Mine is { ExplosionResolved: true })
                Check(c.Attacks.Where(a => a.Stratagem == root.Name).All(a => a.Entity == "main" && a.Weapon == StratagemMine.Weapon)
                    && c.Attacks.Any(a => a.Stratagem == root.Name && a.Role == StratagemMine.Weapon && a.ParentRole == null));
        }
        Check(c.Attacks.All(a => (a.Entity == null) == (a.Weapon == null) && (a.Entity == null || roots[a.Stratagem].DeployedEntity != null)));

        var s = c.Summary; var unique = roots.Values.Where(r => r.RootResolution == "UNIQUE").ToArray();
        var writable = c.FieldInstances.Where(f => f.Editable).ToArray();
        var ammo = writable.Where(f => f.Target.Path == "weapon" && (f.SemanticFieldId == "weapon.capacity" || f.Domain is "magazine" or "rounds")).ToArray();
        var mounted = c.FieldInstances.Where(f => f.Target.Weapon != null).Select(f => (f.Target.Stratagem, f.Target.Entity, f.Target.Weapon))
            .Concat(c.Attacks.Where(a => a.Weapon != null).Select(a => (a.Stratagem, a.Entity, a.Weapon))).Distinct().ToArray();
        Dictionary<string, int> Kinds(IEnumerable<StratagemAttack> attacks) => attacks.GroupBy(a => a.Kind).ToDictionary(g => g.Key, g => g.Count());
        Check(s.SentryRootsResolved == unique.Count(r => r.Family == "sentry") && s.EmplacementRootsResolved == unique.Count(r => r.Family == "emplacement")
            && s.MineRootsResolved == unique.Count(r => r.Family == "mine") && s.DeployedEntitiesResolved == deployed.Count
            && s.MineDeploymentEntitiesResolved == c.DeployedEntities!.Count(e => e.Family == "mine")
            && s.MineInstancesResolved == roots.Values.Count(r => r.MineInstanceResolved == true)
            // Writable mine attack branches (distinct attack roles; one branch carries several fields once mine explosions resolve).
            && s.MineAttackBranchesWritable == writable.Where(f => roots[f.Target.Stratagem].Family == "mine" && f.Target.Path == "attack").Select(f => (f.Target.Stratagem, f.Target.Attack)).Distinct().Count()
            && s.HealthWritable == writable.Count(f => f.SemanticFieldId == "entity.health") && s.ArmorWritable == writable.Count(f => f.SemanticFieldId == "entity.armor")
            && s.MountedWeaponsResolved == mounted.Length && s.MultiWeaponEntities == mounted.GroupBy(m => (m.Stratagem, m.Entity)).Count(g => g.Count() > 1)
            && s.AmmoWritable == ammo.Length && s.MountedWeaponsWithAmmo == ammo.Select(f => (f.Target.Stratagem, f.Target.Entity, f.Target.Weapon)).Distinct().Count()
            && s.FireRateWritable == writable.Count(f => f.SemanticFieldId == "weapon.fire_rate") && s.HeatWritable == writable.Count(f => f.Domain == "heat")
            && s.CanonicalBackingObjects == backings.Count && s.CanonicalOperationGroups == groups.Count
            && s.SharedBackingObjectCount == c.BackingObjects!.Count(b => b.Shared)
            && s.CooldownWritable == writable.Count(f => f.SemanticFieldId == "stratagem.cooldown")
            && s.NativeBranchInstancesByKind != null && s.NativeBranchInstancesByKind.OrderBy(x => x.Key).SequenceEqual(Kinds(c.Attacks).OrderBy(x => x.Key))
            && s.DefensiveNativeBranchInstancesByKind != null
            && s.DefensiveNativeBranchInstancesByKind.OrderBy(x => x.Key).SequenceEqual(Kinds(c.Attacks.Where(a => IsDefensive(a.Family))).OrderBy(x => x.Key)));
    }
    // Zone and shield instances must be exactly the ones the deployed entity publishes for each zone / its shield projector.
    private static bool ZonesAndShieldMatch(StratagemCatalog c, string name, StratagemEntityIdentity identity)
    {
        var zones = identity.DamageZones ?? [];
        IEnumerable<string> Keys(Func<StratagemField, bool> match) => c.FieldInstances.Where(f => f.Target.Stratagem == name && match(f)).Select(f => f.InstanceKey).Order(StringComparer.Ordinal);
        return zones.Select(z => z.ZoneId).Distinct().Count() == zones.Length && zones.All(z => Identifier.IsMatch(z.ZoneId))
            && zones.All(z => z.FieldInstances.Order(StringComparer.Ordinal).SequenceEqual(Keys(f => f.Target.Path == "damage_zone" && f.Target.Zone == z.ZoneId)))
            && Keys(f => f.Target.Path == "damage_zone").Count() == zones.Sum(z => z.FieldInstances.Length)
            && (identity.Shield == null ? !Keys(f => f.Target.Path == "shield").Any()
                : identity.Shield.SameEntityAsBase && identity.Shield.FieldInstances.Order(StringComparer.Ordinal).SequenceEqual(Keys(f => f.Target.Path == "shield"))
                    && identity.Shield.BlockedFields.All(b => !string.IsNullOrWhiteSpace(b.Field) && !string.IsNullOrWhiteSpace(b.Reason)));
    }
    private static bool IsDefensive(string family) => StratagemCatalog.IsDefensive(family);
    private static bool JsonEquals<T>(T a, T b) => JsonSerializer.Serialize(a) == JsonSerializer.Serialize(b);
}
