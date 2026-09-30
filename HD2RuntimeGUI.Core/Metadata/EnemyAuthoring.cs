using HD2RuntimeGUI.Core.Localization;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using HD2RuntimeGUI.Core.Storage;

namespace HD2RuntimeGUI.Core.Metadata;

// Enemy and enemy-structure authoring (hd2runtime.enemy.guarded_authoring.v1, EnemyAuthoringCapabilities.json; hd2.enemy(name) /
// hd2.structure(name)), published by unreleased HD2Runtime builds for 0.28.0. Every class is a hash-verified native entity. It carries
// a wiki name only where Runtime proves one; otherwise it keeps its native class name, and wiki candidates stay candidates. Its targets
// are the class itself (main health block), its damage zones and the rows its mounted weapons reach (direct-hit / explosion DamageInfo,
// ProjectileSettings, ExplosionSettings). Only published field instances become controls; ranges, sentinels, read-only reasons, opt-ins
// and shared rows are exactly as published, and nothing is matched by display text.
public sealed record EnemyFieldDefinition(string DisplayName, string Type, string? Unit, string ApiFieldConstant, string? Acknowledgement,
    string? AcknowledgementReason, string Evidence, double[] Range, FieldLiveEvidence? LiveEvidence = null, Dictionary<string, string>? AcknowledgementByKind = null);
public sealed record EnemyWikiZonePair(string WikiZone, int Zone, bool Unambiguous);
public sealed record EnemyWikiEvidence(string Page, string Kind, string? AnatomyLabel, int ZonesMatched, EnemyWikiZonePair[] ZonePairs);
public sealed record EnemyIdentity(string Basis, string Path, EnemyWikiEvidence? WikiEvidence);
public sealed record EnemyWikiCandidate(string Page, int ZonesMatched, bool KindAgrees, int NativeClassesMatchingPage);
public sealed record EnemyMain(JsonElement Health, JsonElement Constitution, JsonElement ConstitutionRate);
public sealed record EnemyMainZone(JsonElement Armor, JsonElement DurableResistance, JsonElement ExplosiveDamagePercentage);
public sealed record EnemyZone(string Id, int Index, string? Name, string? WikiZone, JsonElement Health, JsonElement Armor, JsonElement AffectsMainHealth,
    JsonElement Constitution, JsonElement DurableResistance, JsonElement ExplosiveDamagePercentage, bool Fatal, bool DownsOnDeath, bool MainHealthCapped)
{
    // The wiki label where Runtime pins one, else the native zone name, else the zone ID. No friendlier label is invented.
    [JsonIgnore] public string Label => WikiZone ?? Name ?? Id;
    // Zone health -1 is Runtime's sentinel for "uses the main health pool" (read-only).
    [JsonIgnore] public bool UsesMainHealth => Health.ValueKind == JsonValueKind.Number && Health.GetDouble() == -1;
}
public sealed record EnemyAttack(string Id, int MountSlot, string Role, string[] WikiAttacks, string[] RowWikiMatches, string[] WikiExplosionOf,
    string[] SharedWithClasses, string? WeaponPath)
{
    // Published attack roles: the row each attack ID names (sdk/docs/enemy-authoring.md, "Attacks").
    public static readonly IReadOnlyDictionary<string, (string Row, string Domain, string Label)> Roles = new Dictionary<string, (string, string, string)>
    {
        ["projectile"] = ("DamageInfo", "damage", "Direct-hit damage"),
        ["spray"] = ("DamageInfo", "damage", "Spray damage"),
        ["explosion_impact"] = ("DamageInfo", "damage", "Impact explosion damage"),
        ["explosion_expiry"] = ("DamageInfo", "damage", "Expiry explosion damage"),
        ["projectile_settings"] = ("ProjectileSettings", "projectile", "Projectile"),
        ["explosion_settings_impact"] = ("ExplosionSettings", "explosion", "Impact explosion"),
        ["explosion_settings_expiry"] = ("ExplosionSettings", "explosion", "Expiry explosion"),
    };
    [JsonIgnore] public string RoleLabel => Roles.TryGetValue(Role, out var r) ? r.Label : Role;
    // The role in the UI language (RoleLabel stays English: Describe() also names mod option targets, which are mod content).
    [JsonIgnore] public string RoleDisplay => Role switch
    {
        "projectile" => CoreText.Get("Enemy.Role.Projectile"), "spray" => CoreText.Get("Enemy.Role.Spray"),
        "explosion_impact" => CoreText.Get("Enemy.Role.ExplosionImpact"), "explosion_expiry" => CoreText.Get("Enemy.Role.ExplosionExpiry"),
        "projectile_settings" => CoreText.Get("Enemy.Role.ProjectileSettings"), "explosion_settings_impact" => CoreText.Get("Enemy.Role.ExplosionSettingsImpact"),
        "explosion_settings_expiry" => CoreText.Get("Enemy.Role.ExplosionSettingsExpiry"), _ => RoleLabel,
    };
    [JsonIgnore] public string Row => Roles.TryGetValue(Role, out var r) ? r.Row : Role;
    // A wiki attack name only when Runtime matched all nine published values on the class's own page; otherwise the published ID.
    [JsonIgnore] public string Title => WikiAttacks.Length > 0 ? string.Join(" / ", WikiAttacks) : Id;
}
public sealed record EnemyClass(string Name, string ClassName, string? WikiName, string[] WikiCandidates, EnemyWikiCandidate[] WikiCandidateEvidence,
    string Kind, string Faction, string SemanticId, string Accessor, EnemyIdentity Identity, EnemyMain Main, EnemyMainZone MainZone,
    bool SharedHealthRecord, string[] HealthRecordConsumers, bool AllowSharedRequired, string BackingObjectId, string PlanGroup,
    EnemyZone[] Zones, bool HasProjectileWeapon, bool MountsEquipment, EnemyAttack[] Attacks)
{
    [JsonIgnore] public bool Named => WikiName != null;
    [JsonIgnore] public bool IsStructure => Kind == EnemyAuthoringReader.Structure;
    public EnemyZone? Zone(string id) => Zones.FirstOrDefault(z => z.Id == id);
    public EnemyAttack? Attack(string id) => Attacks.FirstOrDefault(a => a.Id == id);
}
public sealed record EnemyTargetJson(string Resource, string Enemy, string Path, string? Zone = null, string? Attack = null);
public sealed record EnemyFieldInstance(string InstanceKey, string SemanticFieldId, JsonElement CurrentDefault, bool Editable, EnemyTargetJson Target,
    string OperationGroup, string? Acknowledgement = null, string? AcknowledgementReason = null, string? Reason = null, FieldLiveEvidence? LiveEvidence = null,
    bool? Shared = null, bool? AllowSharedRequired = null, string? BackingObjectId = null, string[]? SharedConsumers = null, bool? DynamicConsumersPossible = null);
public sealed record EnemySummary(int Classes, int Enemies, int Structures, int WikiNamed, int WithWikiCandidates, int SharedHealthRecords,
    int FieldInstances, int WritableFieldInstances, int Proven, int Acknowledged, int Zones, int Attacks, int ClassesWithAttacks, int AttackFieldInstances,
    int AttacksNamedForClass, int AttacksWithRowWikiMatch);
public sealed record EnemyAttackModel(string Chain, string Ids, string WikiAttacks, string RowWikiMatches, string Sharing, string Acknowledgement);
public sealed record EnemyModel(string Component, string Identity, Dictionary<string, EnemyFieldDefinition> Fields, Dictionary<string, string> Sentinels,
    string[] ReadOnlyFlags, EnemyAttackModel Attacks, string AppliesTo);
public sealed record EnemyUnresolvedPage(string Page, string Kind, string[] ExactNativeMatches);
public sealed record EnemySafety(bool RuntimeAddresses, bool RawResourceIdentifiers, int WritesDuringGeneration);
public sealed record EnemySource(string Datalibrary, int Writes, string FixtureFallback);
public sealed record EnemyCatalogJson(string Contract, int SchemaVersion, string Hd2RuntimeVersion, EnemySource Source, EnemySummary Summary, EnemyModel Model,
    EnemyUnresolvedPage[] UnresolvedWikiPages, EnemyClass[] Classes, EnemyFieldInstance[] FieldInstances, EnemySafety Safety);

public sealed class EnemyCatalog
{
    public required EnemyClass[] Classes { get; init; }
    public required EnemyModel Model { get; init; }
    public required EnemySummary Summary { get; init; }
    public required EnemyUnresolvedPage[] UnresolvedWikiPages { get; init; }
    public required string Build { get; init; }
    // Published fields in the shared entity-authoring form (changes, reset, Lua, export and Mod Options reuse that pipeline).
    public required EntityField[] FieldInstances { get; init; }
    // Built once per SDK load, so the editor switches classes, zones and attacks without rescanning ten thousand fields per render.
    public required IReadOnlyDictionary<string, EnemyClass> BySemanticId { get; init; }
    public required IReadOnlyDictionary<string, EntityField[]> FieldsByClass { get; init; }
    public required IReadOnlyDictionary<string, string> SearchText { get; init; }
    public EnemyClass? Find(string semanticId) => BySemanticId.GetValueOrDefault(semanticId);
    // Runtime's current name of a class (its proven wiki name or native class name), for consumers bound by semantic ID.
    public string Name(string semanticId) => Find(semanticId)?.Name ?? semanticId;
    public IEnumerable<EnemyClass> Of(string kind) => Classes.Where(c => c.Kind == kind);
    public IReadOnlyList<EntityField> Fields(string semanticId) => FieldsByClass.GetValueOrDefault(semanticId) ?? [];
    public IEnumerable<EntityField> Entity(string semanticId) => Fields(semanticId).Where(f => f.Target.Path == EnemyAuthoringReader.EntityPath);
    public IEnumerable<EntityField> Zone(string semanticId, string zone) => Fields(semanticId).Where(f => f.Target.Path == EnemyAuthoringReader.ZonePath && f.Target.Zone == zone);
    public IEnumerable<EntityField> Attack(string semanticId, string attack) => Fields(semanticId).Where(f => f.Target.Path == EnemyAuthoringReader.AttackPath && f.Target.Attack == attack);
    public bool Matches(EnemyClass c, string search) => search.Length == 0 || SearchText[c.SemanticId].Contains(search, StringComparison.OrdinalIgnoreCase);
    // "<class> · <zone or attack>" for Changes, Mod Options and errors.
    public string Describe(EntityTarget t)
    {
        var c = t.Enemy is { } id ? Find(id) : null;
        var name = c?.Name ?? t.Enemy ?? "";
        return t.Path switch
        {
            EnemyAuthoringReader.ZonePath => name + " · " + (c?.Zone(t.Zone!)?.Label ?? t.Zone),
            EnemyAuthoringReader.AttackPath => name + " · " + (c?.Attack(t.Attack!) is { } a ? a.Title + " (" + a.RoleLabel.ToLowerInvariant() + ")" : t.Attack),
            _ => name,
        };
    }
}

public static class EnemyAuthoringReader
{
    public const string FileName = "EnemyAuthoringCapabilities.json", Contract = "hd2runtime.enemy.guarded_authoring.v1";
    public const int MaxBytes = 16 * 1024 * 1024;
    public const string Enemy = "enemy", Structure = "structure";
    public const string EntityPath = "entity", ZonePath = "damage_zone", AttackPath = "attack";
    public static readonly string[] Kinds = [Enemy, Structure];
    public static readonly string[] Tiers = ["gameplay_proven_member", "schema_wiki_correlated", "mount_chain_structural", "live_proven"];
    private static readonly Regex SemanticIdPattern = new(@"\Aenemy/v1/[a-z][a-z0-9_]{0,31}/[a-z0-9_]{1,128}\z", RegexOptions.CultureInvariant);
    private static readonly Regex ClassPattern = new(@"\A[a-z0-9][a-z0-9_.]{0,159}\z", RegexOptions.CultureInvariant);
    private static readonly Regex NamePattern = new(@"\A[A-Za-z0-9][A-Za-z0-9 .'/_-]{0,159}\z", RegexOptions.CultureInvariant);
    private static readonly Regex ZonePattern = new(@"\Azone_[0-9]{1,3}\z", RegexOptions.CultureInvariant);
    private static readonly Regex AttackPattern = new(@"\Aslot_[0-9]{1,3}(_[a-z]{1,32}){0,2}\z", RegexOptions.CultureInvariant);
    private static readonly Regex Api = new(@"\Ahd2\.fields\.[a-z_]+\.[a-z_0-9]+\z", RegexOptions.CultureInvariant);
    // Research annotations (attack research counts, wiki source lists) are not part of the authoring surface.
    private static readonly JsonSerializerOptions Options = new(JsonStorage.Options) { UnmappedMemberHandling = JsonUnmappedMemberHandling.Skip };
    private static void Check(bool valid, [System.Runtime.CompilerServices.CallerLineNumber] int line = 0) { if (!valid) throw new InvalidDataException($"Inconsistent enemy capability metadata (check {line})."); }
    public static bool ValidSemanticId(string id) => SemanticIdPattern.IsMatch(id);
    public static bool ValidZone(string? zone) => zone != null && ZonePattern.IsMatch(zone);
    public static bool ValidAttack(string? attack) => attack != null && AttackPattern.IsMatch(attack);
    // The field domain each target path (and attack row) carries.
    private static string? Domain(EnemyClass c, EnemyTargetJson t) => t.Path switch
    {
        EntityPath => "entity", ZonePath => "zone",
        AttackPath => c.Attack(t.Attack!) is { } a && EnemyAttack.Roles.TryGetValue(a.Role, out var r) ? r.Domain : null,
        _ => null,
    };

    public static EnemyCatalog Read(byte[] bytes, string version)
    {
        try
        {
            if (bytes.Length > MaxBytes) throw new InvalidDataException("Enemy capability file exceeds size limit.");
            using var doc = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 32 }); MetadataReader.RejectDuplicates(doc.RootElement);
            var root = doc.RootElement;
            if (root.GetProperty("contract").GetString() != Contract || root.GetProperty("schemaVersion").GetInt32() != 1)
                throw new UnsupportedSdkException("Unsupported enemy authoring contract.");
            var c = JsonSerializer.Deserialize<EnemyCatalogJson>(bytes, Options)!;
            Check(c.Hd2RuntimeVersion == version && !c.Safety.RuntimeAddresses && !c.Safety.RawResourceIdentifiers && c.Safety.WritesDuringGeneration == 0
                && c.Source.Writes == 0 && c.Source.FixtureFallback == "disabled" && !string.IsNullOrWhiteSpace(c.Source.Datalibrary));
            // Field definitions: typed API constants, types, ranges and opt-ins as published.
            foreach (var (id, d) in c.Model.Fields)
                Check(Api.IsMatch(d.ApiFieldConstant) && d.Type is "integer" or "number" && !string.IsNullOrWhiteSpace(d.DisplayName) && Tiers.Contains(d.Evidence)
                    && d.Range is [var min, var max] && min <= max && d.Acknowledgement is null or "allow_unverified_effect"
                    && (d.Acknowledgement == null) == (d.AcknowledgementReason == null) && id.Split('.')[0] is "entity" or "zone" or "damage" or "projectile" or "explosion"
                    && (d.AcknowledgementByKind ?? []).All(p => Kinds.Contains(p.Key) && p.Value == "allow_unverified_effect"));
            var classes = new Dictionary<string, EnemyClass>(StringComparer.Ordinal);
            var names = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var x in c.Classes)
            {
                // Identity: native class first; the Runtime name is the proven wiki name or the native class name.
                Check(ValidSemanticId(x.SemanticId) && x.SemanticId.StartsWith("enemy/v1/" + x.Faction + "/", StringComparison.Ordinal) && ClassPattern.IsMatch(x.ClassName)
                    && NamePattern.IsMatch(x.Name) && x.Name == (x.WikiName ?? x.ClassName) && Kinds.Contains(x.Kind)
                    && x.Accessor == (x.Kind == Structure ? "hd2.structure" : "hd2.enemy") && classes.TryAdd(x.SemanticId, x)
                    && x.Identity.Basis == "hash-verified resource path" && !string.IsNullOrWhiteSpace(x.Identity.Path)
                    && (x.WikiName == null || x.WikiCandidates.Contains(x.WikiName) && x.Identity.WikiEvidence?.Page == x.WikiName)
                    && x.WikiCandidateEvidence.Select(e => e.Page).Order(StringComparer.Ordinal).SequenceEqual(x.WikiCandidates.Order(StringComparer.Ordinal))
                    && x.AllowSharedRequired == x.SharedHealthRecord && x.HealthRecordConsumers.Contains(x.Name)
                    && x.HealthRecordConsumers.Length == 1 != x.SharedHealthRecord);
                // Runtime resolves a class by its name or its native class name; both must name this class only.
                Check(names.TryAdd(x.Name, x.SemanticId) && (x.ClassName == x.Name || names.TryAdd(x.ClassName, x.SemanticId)));
                Check(x.Zones.Select(z => z.Id).Distinct().Count() == x.Zones.Length && x.Zones.All(z => z.Id == "zone_" + z.Index && ValidZone(z.Id))
                    && x.Attacks.Select(a => a.Id).Distinct().Count() == x.Attacks.Length
                    && x.Attacks.All(a => ValidAttack(a.Id) && a.Id.StartsWith("slot_" + a.MountSlot, StringComparison.Ordinal) && EnemyAttack.Roles.ContainsKey(a.Role)
                        && a.SharedWithClasses.Length > 0));
            }
            // Wiki names are identities: one class per wiki name, never also another class's native name.
            Check(c.Classes.Where(x => x.Named).Select(x => x.WikiName).Distinct().Count() == c.Classes.Count(x => x.Named));
            var keys = new HashSet<string>(StringComparer.Ordinal);
            var byName = c.Classes.ToDictionary(x => x.Name, StringComparer.Ordinal);
            var adapted = new List<EntityField>(c.FieldInstances.Length);
            foreach (var f in c.FieldInstances)
            {
                Check(keys.Add(f.InstanceKey) && f.InstanceKey.StartsWith(Enemy + ":", StringComparison.Ordinal) && f.InstanceKey.Length <= 512
                    && f.Target.Resource == Enemy && byName.TryGetValue(f.Target.Enemy, out var x) && c.Model.Fields.TryGetValue(f.SemanticFieldId, out var d));
                var cls = byName[f.Target.Enemy]; var def = c.Model.Fields[f.SemanticFieldId];
                Check(f.Target.Path switch
                {
                    EntityPath => f.Target.Zone == null && f.Target.Attack == null,
                    ZonePath => f.Target.Attack == null && cls.Zone(f.Target.Zone ?? "") != null,
                    AttackPath => f.Target.Zone == null && cls.Attack(f.Target.Attack ?? "") != null,
                    _ => false,
                } && Domain(cls, f.Target) == f.SemanticFieldId.Split('.')[0]);
                // Writable fields carry a baseline inside the published range; read-only ones say why (sentinels, flags, attack chains).
                Check(f.Editable
                    ? f.CurrentDefault.ValueKind == JsonValueKind.Number && f.CurrentDefault.GetDouble() >= def.Range[0] && f.CurrentDefault.GetDouble() <= def.Range[1]
                        && (def.Type != "integer" || f.CurrentDefault.GetDouble() == Math.Floor(f.CurrentDefault.GetDouble())) && f.Reason == null
                    : !string.IsNullOrWhiteSpace(f.Reason) && f.CurrentDefault.ValueKind is JsonValueKind.Number or JsonValueKind.Null);
                // Opt-ins: the definition's own, or the per-kind gate Runtime publishes on the instance (structure health).
                Check(f.Acknowledgement == null
                    ? f.AcknowledgementReason == null
                    : f.Acknowledgement == def.AcknowledgementByKind?.GetValueOrDefault(cls.Kind) && !string.IsNullOrWhiteSpace(f.AcknowledgementReason) && def.Acknowledgement == null);
                Check(!f.Editable || def.AcknowledgementByKind?.GetValueOrDefault(cls.Kind) is not { } gate || def.Acknowledgement != null || f.Acknowledgement == gate);
                // Attack rows are global settings rows: shared, with their reviewed consumers and possible other users. Health records are per class.
                if (f.Target.Path == AttackPath)
                {
                    var a = cls.Attack(f.Target.Attack!)!;
                    Check(f.Shared == true && f.AllowSharedRequired == true && !string.IsNullOrWhiteSpace(f.BackingObjectId) && f.DynamicConsumersPossible == true
                        && f.SharedConsumers is { Length: > 0 } consumers && consumers.SequenceEqual(a.SharedWithClasses) && consumers.All(byName.ContainsKey) && consumers.Contains(cls.Name));
                }
                else Check(f.Shared == null && f.AllowSharedRequired == null && f.BackingObjectId == null && f.SharedConsumers == null);
                Check(f.LiveEvidence == null || f.Editable && def.LiveEvidence is { } live && live.Family == f.LiveEvidence.Family && (live.AppliesToKinds ?? Kinds).Contains(cls.Kind));
                adapted.Add(Adapt(cls, f, def, byName));
            }
            // Every class publishes its six entity fields, six fields per zone and fields for every attack it lists.
            var perClass = adapted.GroupBy(f => f.Target.Enemy!).ToDictionary(g => g.Key, g => g.ToArray(), StringComparer.Ordinal);
            foreach (var x in c.Classes)
            {
                var own = perClass.GetValueOrDefault(x.SemanticId) ?? [];
                Check(own.Count(f => f.Target.Path == EntityPath) == c.Model.Fields.Keys.Count(k => k.StartsWith("entity.", StringComparison.Ordinal))
                    && x.Zones.All(z => own.Count(f => f.Target.Zone == z.Id) == c.Model.Fields.Keys.Count(k => k.StartsWith("zone.", StringComparison.Ordinal)))
                    && x.Attacks.All(a => own.Any(f => f.Target.Attack == a.Id)));
            }
            var s = c.Summary; var editable = c.FieldInstances.Where(f => f.Editable).ToArray();
            Check(s.Classes == c.Classes.Length && s.Enemies == c.Classes.Count(x => x.Kind == Enemy) && s.Structures == c.Classes.Count(x => x.Kind == Structure)
                && s.WikiNamed == c.Classes.Count(x => x.Named) && s.WithWikiCandidates == c.Classes.Count(x => !x.Named && x.WikiCandidates.Length > 0)
                && s.SharedHealthRecords == c.Classes.Count(x => x.SharedHealthRecord) && s.FieldInstances == c.FieldInstances.Length
                && s.WritableFieldInstances == editable.Length && s.Zones == c.Classes.Sum(x => x.Zones.Length) && s.Attacks == c.Classes.Sum(x => x.Attacks.Length)
                && s.ClassesWithAttacks == c.Classes.Count(x => x.Attacks.Length > 0) && s.AttackFieldInstances == c.FieldInstances.Count(f => f.Target.Path == AttackPath)
                // The summary counts definition-level opt-ins; the structure-health gate is published on its instances.
                && s.Acknowledged == editable.Count(f => c.Model.Fields[f.SemanticFieldId].Acknowledgement != null)
                && s.Proven == editable.Count(f => c.Model.Fields[f.SemanticFieldId].Acknowledgement == null));
            var fields = adapted.ToArray();
            return new()
            {
                Classes = c.Classes, Model = c.Model, Summary = c.Summary, UnresolvedWikiPages = c.UnresolvedWikiPages, Build = c.Source.Datalibrary,
                FieldInstances = fields, BySemanticId = classes, FieldsByClass = perClass,
                SearchText = c.Classes.ToDictionary(x => x.SemanticId, x => string.Join("\n", new[] { x.Name, x.ClassName, x.SemanticId, x.Faction, x.Kind }
                    .Concat(x.WikiCandidates).Concat(x.Attacks.SelectMany(a => a.WikiAttacks))), StringComparer.Ordinal),
            };
        }
        catch (Exception e) when (e is JsonException or KeyNotFoundException or NullReferenceException or ArgumentException or InvalidOperationException or FormatException)
        { throw new InvalidDataException("Malformed enemy capability metadata.", e); }
    }

    // ModBuilder's instance key is built from stable semantic identities (class semantic ID, target, field). Runtime's own instance key embeds
    // the class's display name, which changes when a wiki name is proven later; the semantic ID does not.
    public static string Key(string kind, string semanticId, string path, string? zoneOrAttack, string field) =>
        kind + ":" + semanticId + "|" + path + "|" + (zoneOrAttack ?? "") + "|" + field;

    private static EntityField Adapt(EnemyClass c, EnemyFieldInstance f, EnemyFieldDefinition d, IReadOnlyDictionary<string, EnemyClass> byName)
    {
        var attack = f.Target.Attack is { } id ? c.Attack(id) : null;
        var target = new EntityTarget(c.Kind, f.Target.Path, Zone: f.Target.Zone, Attack: f.Target.Attack, Enemy: c.SemanticId, EnemyClass: c.ClassName);
        // One patch/transaction per target object (the class, one zone or one attack row): independent objects are never bundled into one plan.
        var operation = "enemy-op:" + c.SemanticId + "|" + f.Target.Path + "|" + (f.Target.Zone ?? f.Target.Attack ?? "");
        var backing = attack != null ? f.BackingObjectId! : c.BackingObjectId;
        // Consumers are published by Runtime name, which changes when a wiki name is proven later; they are bound by class semantic ID.
        var consumers = (attack != null ? f.SharedConsumers! : c.HealthRecordConsumers).Select(n => new EntityConsumer(Enemy: byName[n].SemanticId)).ToArray();
        var shared = attack != null ? f.Shared == true : c.SharedHealthRecord;
        var dynamic = attack != null && f.DynamicConsumersPossible == true;
        var live = f.LiveEvidence;
        return new EntityField(Key(c.Kind, c.SemanticId, f.Target.Path, f.Target.Zone ?? f.Target.Attack, f.SemanticFieldId), f.SemanticFieldId, d.DisplayName, d.Type, d.Unit,
            f.CurrentDefault.Clone(), f.Editable, f.Reason, target, backing, operation, operation, "patch_or_transaction",
            attack != null ? f.AllowSharedRequired == true : c.AllowSharedRequired, shared, consumers, backing, ReviewedScopeComplete: !dynamic,
            DynamicConsumersPossible: dynamic, BackingObjectKind: attack?.Row ?? "HealthComponent", Domain: f.SemanticFieldId.Split('.')[0],
            ApiFieldConstant: d.ApiFieldConstant, PlanPhase: 1, DependsOn: [],
            Evidence: new EntityEvidence(live != null ? "live_proven" : d.Evidence, ProvenOn: live?.Tests, Proof: live == null ? null : live.Family + " (" + live.Date + ")"),
            Provenance: "EnemyAuthoringCapabilities (" + d.Evidence.Replace('_', ' ') + (attack != null ? ", " + attack.Row + " row" : ", HealthComponent") + ")",
            Acknowledgement: f.Acknowledgement ?? d.Acknowledgement, Range: new EntityRange(d.Range[0], d.Range[1], d.Type == "integer"),
            AcknowledgementReason: f.AcknowledgementReason ?? d.AcknowledgementReason);
    }
}
