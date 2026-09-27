using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using HD2RuntimeGUI.Core.Models;
using HD2RuntimeGUI.Core.Storage;

namespace HD2RuntimeGUI.Core.Metadata;

public sealed record BuildFingerprints(string Exe, string Dll);
public sealed record FamilyCoverage(int Weapons, int WeaponsWithWritableFields, int WeaponsWithProjectileOrDamageWrites);
public sealed record CapabilitySummary(int Weapons, int UniqueWeapons, int DuplicateWeapons,
    int SemanticFieldDefinitions, int WritableSemanticFieldDefinitions, int ReadOnlySemanticFieldDefinitions,
    int DerivedSemanticFieldDefinitions, int FieldInstances, int WritableFieldInstances,
    int WeaponsWithWritableFields, int WeaponsWithProjectileDamageWrites, int WeaponsRestrictedToWeaponLevelWrites,
    Dictionary<string, FamilyCoverage> FamilyCoverage, AmmoSummary? Ammo = null, int? SemanticAliasRules = null, int? SemanticAliasInstances = null);
public sealed record CatalogSafety(bool AddressesInPublicMetadata, int Writes, int ProtectionChanges, string FixtureFallback);
public sealed record SemanticFieldDefinition(string Id,
    [property: JsonPropertyName("display_name")] string DisplayName, string Type, string? Unit,
    bool Derived, bool Writable, string? Reason, string? Component, int? Offset, string? Storage, string? Settings,
    [property: JsonPropertyName("semantic_target")] string? SemanticTarget = null);
public sealed record SemanticAliasRule(string Alias, string Canonical,
    [property: JsonPropertyName("requires_identical_backing")] bool RequiresIdenticalBacking,
    bool Deprecated, int InstanceCount, int WriteAcceptedInstanceCount);
public sealed record DistinctSemanticExample(IReadOnlyList<string> Fields, string Reason);
public sealed record BackingCollisionAudit(int FieldInstancesAudited, int ExactBackingCollisionGroups, int AliasPairInstances,
    int DistinctSemanticPairInstances, int UnclassifiedCollisionPairs, DistinctSemanticExample DistinctSemanticExample);
public sealed record FieldProvenance(
    [property: JsonPropertyName("structural_candidate")] bool StructuralCandidate,
    [property: JsonPropertyName("schema_labelled")] bool SchemaLabelled,
    [property: JsonPropertyName("correlation_proven")] bool CorrelationProven,
    [property: JsonPropertyName("current_live_ownership_proven")] bool CurrentLiveOwnershipProven,
    [property: JsonPropertyName("gameplay_proven")] bool GameplayProven,
    [property: JsonPropertyName("native_consumer_proven")] bool NativeConsumerProven,
    [property: JsonPropertyName("pending_gameplay_confirmation")] bool PendingGameplayConfirmation, string Source)
{
    [JsonIgnore] public string Label => GameplayProven ? "Gameplay proven" : CurrentLiveOwnershipProven ? "Live ownership proven" : SchemaLabelled ? "Schema-labelled" : "Experimental";
}
public sealed record FieldBacking(string Kind, string? Component, int Offset, string Storage, int Width,
    int? RecordIndex, int? IndexRow, int? OwnerCount, bool? UniqueOwner, string? Settings,
    int? Group, int? Row, int? RecordType, string? SettingsType, string? Branch, int? ConsumerCount);
public sealed record WeaponCapability(string DisplayName, string SemanticFieldId, string Type, string? Unit,
    JsonElement CurrentDefault, bool Editable, bool DerivedReadOnly, FieldProvenance Provenance,
    double? Min, double? Max, Dictionary<string, JsonElement>? EnumValues, FieldBacking? Backing,
    string WriteScope, IReadOnlyList<string> SharedWithWeapons, bool AffectsMultipleWeapons, string? Reason,
    string? SemanticTarget = null, bool? Canonical = null, bool? Preferred = null, bool? Deprecated = null,
    string? AliasOf = null, bool? AcceptedForWrites = null)
{
    [JsonIgnore] public string Domain => SemanticFieldId.Split('.')[0];
    [JsonIgnore] public bool IsPreferred => AliasOf == null && Canonical != false && Preferred != false && Deprecated != true;
    [JsonIgnore] public bool WriteAccepted => AcceptedForWrites ?? Editable;
    public string Format(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.Null or JsonValueKind.Undefined => "Unavailable",
        JsonValueKind.String => value.GetString()!,
        JsonValueKind.Number when Backing?.Storage == "f32" => ((float)value.GetDouble()).ToString("R", CultureInfo.InvariantCulture),
        _ => value.GetRawText()
    };
}
public sealed record PlayerWeapon(string Name, string Slot, string Category, string Resolution,
    bool OrdinaryWritesBlocked, string? BlockReason, IReadOnlyList<string> Resources,
    IReadOnlyList<string> ImplementationFamilies, IReadOnlyList<WeaponCapability> Fields);
public sealed record PlayerWeaponCatalog(int SchemaVersion, string Hd2RuntimeVersion, BuildFingerprints BuildFingerprints,
    string SourceSnapshot, CapabilitySummary Summary, IReadOnlyList<SemanticFieldDefinition> FieldDefinitions,
    IReadOnlyList<PlayerWeapon> Weapons, CatalogSafety Safety, IReadOnlyList<SemanticAliasRule>? SemanticAliases = null,
    BackingCollisionAudit? BackingCollisionAudit = null)
{
    public PlayerWeapon Weapon(string name) => Weapons.SingleOrDefault(w => w.Name == name) ?? throw new InvalidDataException("Weapon no longer exists in this SDK: " + name);
    public WeaponCapability Field(string weapon, string id) => Weapon(weapon).Fields.SingleOrDefault(f => f.SemanticFieldId == id) ?? throw new InvalidDataException("Field no longer exists in this SDK: " + id);
    public WeaponCapability? FindCanonicalField(string weapon, string id)
    {
        var fields = Weapons.FirstOrDefault(w => w.Name == weapon)?.Fields;
        var field = fields?.FirstOrDefault(f => f.SemanticFieldId == id);
        return field?.AliasOf is { } canonical ? fields!.Single(f => f.SemanticFieldId == canonical) : field;
    }
}

public interface IPlayerWeaponCatalogReader { PlayerWeaponCatalog Read(byte[] bytes, string sdkVersion); }
public sealed class PlayerWeaponCatalogReader : IPlayerWeaponCatalogReader
{
    public const string FileName = "PlayerWeaponAuthoringCapabilities.json";
    public const int MaxBytes = 8 * 1024 * 1024;
    public PlayerWeaponCatalog Read(byte[] bytes, string sdkVersion)
    {
        try
        {
            if (bytes.Length > MaxBytes) throw new InvalidDataException("Capability catalog exceeds its size limit.");
            using var doc = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 32 });
            MetadataReader.RejectDuplicates(doc.RootElement);
            if (doc.RootElement.GetProperty("schemaVersion").GetInt32() is not (1 or 2)) throw new UnsupportedSdkException("Unsupported player-weapon catalog schema.");
            var c = JsonSerializer.Deserialize<PlayerWeaponCatalog>(bytes, JsonStorage.Options) ?? throw new InvalidDataException("Empty capability catalog.");
            if (c.Hd2RuntimeVersion != sdkVersion || SemVersion.Parse(sdkVersion).CompareTo(SemVersion.Parse("0.13.0")) < 0) throw new InvalidDataException("Capability catalog version mismatch.");
            if (!Regex.IsMatch(c.BuildFingerprints.Exe, "\\A[A-Fa-f0-9]{64}\\z") || !Regex.IsMatch(c.BuildFingerprints.Dll, "\\A[A-Fa-f0-9]{64}\\z")) throw new InvalidDataException("Invalid catalog fingerprints.");
            if (c.Safety.AddressesInPublicMetadata || c.Safety.Writes != 0 || c.Safety.ProtectionChanges != 0 || c.Safety.FixtureFallback != "disabled") throw new InvalidDataException("Unexpected authoring safety contract.");
            if (c.Weapons.Count is < 1 or > 10000 || c.Weapons.Select(w => w.Name).Distinct().Count() != c.Weapons.Count) throw new InvalidDataException("Invalid weapon identities.");
            if (c.FieldDefinitions.Count is < 1 or > 1000 || c.FieldDefinitions.Select(d => d.Id).Distinct().Count() != c.FieldDefinitions.Count) throw new InvalidDataException("Invalid semantic definitions.");
            foreach (var w in c.Weapons)
            {
                if (string.IsNullOrWhiteSpace(w.Name) || w.Name.Length > 256 || w.Slot is not ("primary" or "secondary") || w.Fields.Count > 256 || w.Fields.Select(f => f.SemanticFieldId).Distinct().Count() != w.Fields.Count) throw new InvalidDataException("Invalid weapon entry.");
                if (w.Resolution is not ("UNIQUE" or "DUPLICATE") || (w.Resolution != "UNIQUE" && !w.OrdinaryWritesBlocked) || (w.Resolution == "UNIQUE" && w.Resources.Count != 1)) throw new InvalidDataException("Ambiguous weapon identity is not blocked.");
                foreach (var f in w.Fields)
                {
                    if (!Regex.IsMatch(f.SemanticFieldId, "\\A[a-z][a-z_0-9]*(?:\\.[a-z][a-z_0-9]*)+\\z") || f.SemanticFieldId.Length > 128 || string.IsNullOrWhiteSpace(f.DisplayName) || f.Type is not ("number" or "integer" or "boolean" or "enum")) throw new InvalidDataException("Invalid semantic field.");
                    if (f.CurrentDefault.ValueKind is JsonValueKind.Array or JsonValueKind.Object or JsonValueKind.Undefined) throw new InvalidDataException("Expected a scalar baseline.");
                    var definitionId = Regex.Replace(Regex.Replace(f.SemanticFieldId, @"\.(primary|alternate)\.", "."), @"status_\d+_", "status_");
                    var definition = c.FieldDefinitions.SingleOrDefault(d => d.Id == definitionId) ?? throw new InvalidDataException("Capability has no semantic definition.");
                    if (f.Type != definition.Type || (f.Editable && (!definition.Writable || definition.Derived))) throw new InvalidDataException("Capability disagrees with its semantic definition.");
                    if (f.Provenance == null || f.SharedWithWeapons == null || f.SharedWithWeapons.Any(n => !c.Weapons.Any(other => other.Name == n))) throw new InvalidDataException("Invalid capability evidence or shared ownership.");
                    if (f.Editable && (w.OrdinaryWritesBlocked || f.DerivedReadOnly || f.Backing == null || f.CurrentDefault.ValueKind == JsonValueKind.Null || f.WriteScope == "unknown")) throw new InvalidDataException("Unsafe writable capability.");
                    if (f.Editable && (f.Backing!.Kind is not ("component" or "settings") || f.Backing.Storage is not ("f32" or "u32" or "i32" or "u8") || f.Backing.Width != (f.Backing.Storage == "u8" ? 1 : 4))) throw new UnsupportedSdkException("Unsupported writable backing type.");
                    // Shared damage consumers can include unnamed/non-player consumers.
                    if ((!f.AffectsMultipleWeapons && f.SharedWithWeapons.Count > 0) || f.AffectsMultipleWeapons != f.WriteScope.StartsWith("shared_", StringComparison.Ordinal)) throw new InvalidDataException("Inconsistent shared-write scope.");
                }
            }
            var s = c.Summary;
            if (c.SchemaVersion == 2) ValidateAliases(c);
            else if (c.SemanticAliases != null || c.BackingCollisionAudit != null || c.Summary.SemanticAliasRules != null
                || c.Weapons.SelectMany(w => w.Fields).Any(f => f.AliasOf != null || f.SemanticTarget != null || f.Canonical != null || f.Preferred != null || f.Deprecated != null || f.AcceptedForWrites != null))
                throw new InvalidDataException("Alias metadata requires capability schema v2.");
            if (s.Weapons != c.Weapons.Count || s.FieldInstances != c.Weapons.Sum(w => w.Fields.Count) || s.WritableFieldInstances != c.Weapons.Sum(w => w.Fields.Count(f => f.Editable)) || s.DuplicateWeapons != c.Weapons.Count(w => w.OrdinaryWritesBlocked) || s.UniqueWeapons + s.DuplicateWeapons != s.Weapons || s.SemanticFieldDefinitions != c.FieldDefinitions.Count || s.WritableSemanticFieldDefinitions != c.FieldDefinitions.Count(f => f.Writable) || s.ReadOnlySemanticFieldDefinitions != c.FieldDefinitions.Count(f => !f.Writable) || s.DerivedSemanticFieldDefinitions != c.FieldDefinitions.Count(f => f.Derived)) throw new InvalidDataException("Capability catalog summary mismatch.");
            return c;
        }
        catch (Exception e) when (e is JsonException or NullReferenceException or InvalidOperationException or KeyNotFoundException or FormatException or ArgumentException)
        { throw new InvalidDataException("Malformed player-weapon capability catalog: " + e.Message, e); }
    }
    private static void ValidateAliases(PlayerWeaponCatalog c)
    {
        if (SemVersion.Parse(c.Hd2RuntimeVersion).CompareTo(SemVersion.Parse("0.14.1")) < 0 || c.SemanticAliases == null || c.BackingCollisionAudit == null)
            throw new InvalidDataException("Missing schema-v2 alias contract.");
        if (c.SemanticAliases.Select(r => r.Alias).Distinct().Count() != c.SemanticAliases.Count) throw new InvalidDataException("Duplicate semantic alias rules.");
        foreach (var w in c.Weapons)
        foreach (var f in w.Fields)
        {
            if (string.IsNullOrWhiteSpace(f.SemanticTarget) || f.Canonical == null || f.Preferred == null || f.Deprecated == null || f.AcceptedForWrites == null)
                throw new InvalidDataException("Incomplete schema-v2 field identity.");
            if (f.AliasOf == null)
            {
                if (!f.IsPreferred || f.Editable != f.AcceptedForWrites) throw new InvalidDataException("Invalid canonical field contract.");
                continue;
            }
            var target = w.Fields.SingleOrDefault(t => t.SemanticFieldId == f.AliasOf);
            var rule = c.SemanticAliases.SingleOrDefault(r => r.Alias == f.SemanticFieldId && r.Canonical == f.AliasOf);
            if (target == null || rule == null || target.AliasOf != null || !target.IsPreferred || f.Canonical != false || f.Preferred != false
                || f.Deprecated != true || f.Editable || !rule.Deprecated || f.Type != target.Type || f.SemanticTarget != target.SemanticTarget
                || f.AcceptedForWrites != target.AcceptedForWrites || (rule.RequiresIdenticalBacking && f.Backing != target.Backing))
                throw new InvalidDataException("Invalid semantic alias relationship.");
        }
        var aliases = c.Weapons.SelectMany(w => w.Fields).Where(f => f.AliasOf != null).ToArray();
        foreach (var rule in c.SemanticAliases)
            if (rule.InstanceCount != aliases.Count(f => f.SemanticFieldId == rule.Alias) || rule.WriteAcceptedInstanceCount != aliases.Count(f => f.SemanticFieldId == rule.Alias && f.WriteAccepted))
                throw new InvalidDataException("Semantic alias rule counts differ.");
        if (c.Summary.SemanticAliasRules != c.SemanticAliases.Count || c.Summary.SemanticAliasInstances != aliases.Length
            || c.BackingCollisionAudit.FieldInstancesAudited != c.Summary.FieldInstances || c.BackingCollisionAudit.AliasPairInstances != aliases.Length)
            throw new InvalidDataException("Semantic alias summary differs.");
    }
}
