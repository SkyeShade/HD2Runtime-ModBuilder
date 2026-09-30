using HD2RuntimeGUI.Core.Localization;
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
    Dictionary<string, FamilyCoverage> FamilyCoverage, AmmoSummary? Ammo = null, int? SemanticAliasRules = null, int? SemanticAliasInstances = null, CompositionSummary? Composition = null,
    // In-progress Runtime work (0.28.0 development): counts of field instances by write effect. Read, not interpreted by this build.
    JsonElement? Effect = null);
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
    [JsonIgnore] public string Label => GameplayProven ? CoreText.Get("Provenance.GameplayProven") : CurrentLiveOwnershipProven ? CoreText.Get("Provenance.LiveOwnershipProven") : SchemaLabelled ? CoreText.Get("Provenance.SchemaLabelled") : CoreText.Get("Provenance.Experimental");
}
public sealed record FieldBacking(string Kind, string? Component, int Offset, string Storage, int Width,
    int? RecordIndex, int? IndexRow, int? OwnerCount, bool? UniqueOwner, string? Settings,
    int? Group, int? Row, int? RecordType, string? SettingsType, string? Branch, int? ConsumerCount, string? Phase = null,
    // Unreleased Runtime (0.28.0 development): a DamageInfo status slot and the enum a status reference is written as.
    int? StatusSlot = null, string? Enum = null);
// A published live test of a field family (Runtime's LiveEvidenceCatalog), for example the enemy main-health test.
public sealed record FieldLiveEvidence(string Status, string Family, string[] Tests, string Date, string[]? AppliesToKinds = null, string? Target = null, string? Field = null);
public sealed record WeaponCapability(string DisplayName, string SemanticFieldId, string Type, string? Unit,
    JsonElement CurrentDefault, bool Editable, bool DerivedReadOnly, FieldProvenance Provenance,
    double? Min, double? Max, Dictionary<string, JsonElement>? EnumValues, FieldBacking? Backing,
    string WriteScope, IReadOnlyList<string> SharedWithWeapons, bool AffectsMultipleWeapons, string? Reason,
    string? SemanticTarget = null, bool? Canonical = null, bool? Preferred = null, bool? Deprecated = null,
    string? AliasOf = null, bool? AcceptedForWrites = null, string? ReferenceKind = null, string? CompatibilityClass = null, string? ReferenceRole = null, ProjectileSettingsIdentity? ReferenceSettings = null,
    IReadOnlyList<int>? AllowedValues = null, IReadOnlyList<int>? NativeModeVector = null, string? WriteKind = null,
    ProjectileResidency? Residency = null, string? ReferencePhase = null, FieldBacking? ProjectileBacking = null,
    ProjectileSettingsIdentity? ProjectileSettings = null, int? NullSentinel = null, IReadOnlyList<string>? SharedWithResources = null,
    bool? DynamicConsumersPossible = null, int? ExplosionType = null, IReadOnlyList<string>? TerminalPhases = null,
    // 0.26.0 third-person reticle: the native crosshair value and the off/on encoding of the boolean view.
    int? NativeValue = null, string? NativeName = null, string? ReticleState = null, ReticleEncoding? Encoding = null,
    // 0.26.0: Runtime-required opt-in for this field ("allow_unverified_effect") with its reason, and field-level evidence.
    string? Acknowledgement = null, string? AcknowledgementReason = null, WeaponFieldEvidence? Evidence = null,
    // 0.26.0 fire modes: the four native FireMode slots, the modes a write may use, and the in-game selector binding.
    IReadOnlyList<int>? NativeSlots = null, IReadOnlyList<string>? AllowedModes = null, Dictionary<string, int>? ModeValues = null,
    int? MaxModes = null, string? FireModeState = null, FireModeSelector? Selector = null,
    // 0.27.0: the typed API constant of this field (hd2.fields.<domain>.<name>), published for tools.
    string? ApiFieldConstant = null,
    // Unreleased Runtime (0.28.0 development): typed status references (a status key from AllowedReferences, or 'none'), live
    // evidence, the weapon's movement / animation context and the active projectile source. Read, but not authored by this build.
    int? StatusSlot = null, bool? StatusAttach = null, IReadOnlyList<string>? AllowedReferences = null, bool? AllowNone = null,
    FieldLiveEvidence? LiveEvidence = null, JsonElement? Movement = null, WeaponProjectileSource? ProjectileSource = null,
    // In-progress Runtime work (0.28.0 development): whether a write takes effect (active source, instantiation-only, dormant, overridden).
    // Read, not interpreted by this build; Runtime's editable flag still decides what is authored.
    JsonElement? Effect = null)
{
    public const string FireModeSet = "fire_mode_set";
    // A typed status reference (unreleased Runtime 0.28.0 development SDKs). This build shows it read-only.
    public const string StatusReference = "status_reference";
    public const string StatusReferenceReason = "Status references (a status effect chosen by name) are published by this development SDK but are not authored by this ModBuilder build yet.";
    [JsonIgnore] public string Domain => SemanticFieldId.Split('.')[0];
    [JsonIgnore] public bool IsPreferred => AliasOf == null && Canonical != false && Preferred != false && Deprecated != true;
    [JsonIgnore] public bool WriteAccepted => AcceptedForWrites ?? Editable;
    public string Format(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.Null or JsonValueKind.Undefined => "Unavailable",
        JsonValueKind.Array when Type == FireModeSet => FireModes.Text(value),
        JsonValueKind.String => value.GetString()!,
        JsonValueKind.Number when Backing?.Storage == "f32" => ((float)value.GetDouble()).ToString("R", CultureInfo.InvariantCulture),
        _ => value.GetRawText()
    };
}
public sealed record ReticleEncoding(int Off, int On);
// Unreleased Runtime (0.28.0 development): which member an attack's shot actually fires from. Only ACTIVE_DIRECT sources take a
// projectile reference write; INDIRECT (an ammunition delta), AMBIGUOUS and BLOCKED sources keep the selector read-only.
public sealed record WeaponProjectileSource(string Status, string? Mechanism, string? Member)
{
    public const string ActiveDirect = "ACTIVE_DIRECT";
}
public sealed record WeaponFieldEvidence(string? NativeOwner, string? Source, bool? GameplayProven, string? GameplaySource = null);
// Input bindings: a published WeaponFunctionType name ("Firemode", "None"…) or an unnamed native value.
public sealed record FireModeSelector(JsonElement Left, JsonElement Right)
{
    public bool Bound => IsFiremode(Left) || IsFiremode(Right);
    private static bool IsFiremode(JsonElement v) => v.ValueKind == JsonValueKind.String && v.GetString() == "Firemode";
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
    // The SDK archive's own per-entry bound; development catalogs are larger than 8 MB.
    public const int MaxBytes = 16 * 1024 * 1024;
    public PlayerWeaponCatalog Read(byte[] bytes, string sdkVersion)
    {
        try
        {
            if (bytes.Length > MaxBytes) throw new InvalidDataException("Capability catalog exceeds its size limit.");
            using var doc = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 32 });
            MetadataReader.RejectDuplicates(doc.RootElement);
            if (doc.RootElement.GetProperty("schemaVersion").GetInt32() is not (1 or 2)) throw new UnsupportedSdkException("Unsupported player-weapon catalog schema.");
            var c = JsonSerializer.Deserialize<PlayerWeaponCatalog>(StatusReferences(bytes, doc.RootElement), JsonStorage.Options) ?? throw new InvalidDataException("Empty capability catalog.");
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
                    if (!Regex.IsMatch(f.SemanticFieldId, "\\A[a-z][a-z_0-9]*(?:\\.[a-z][a-z_0-9]*)+\\z") || f.SemanticFieldId.Length > 128 || string.IsNullOrWhiteSpace(f.DisplayName) || f.Type is not ("number" or "integer" or "boolean" or "enum" or "projectile_reference" or "explosion_reference" or WeaponCapability.FireModeSet or WeaponCapability.StatusReference)) throw new InvalidDataException("Invalid semantic field.");
                    if (f.Type == WeaponCapability.StatusReference && (f.CurrentDefault.ValueKind != JsonValueKind.String || f.AllowedReferences is not { Count: > 0 } || f.StatusSlot is not (>= 1 and <= 4)))
                        throw new InvalidDataException("Invalid status reference.");
                    if (f.Type == WeaponCapability.FireModeSet) FireModes.ValidateCapability(f);
                    else if (f.Type is not ("projectile_reference" or "explosion_reference") && f.CurrentDefault.ValueKind is (JsonValueKind.Array or JsonValueKind.Object or JsonValueKind.Undefined)) throw new InvalidDataException("Expected a scalar baseline.");
                    if (f.SemanticFieldId == FireModes.ReticleField && (f.Type != "boolean" || f.Editable && (f.Encoding == null || f.NativeValue == null))) throw new InvalidDataException("Invalid third-person reticle field.");
                    if (f.Acknowledgement is not (null or "allow_unverified_effect")) throw new UnsupportedSdkException("Unsupported player-weapon acknowledgement: " + f.Acknowledgement);
                    if (f.Type == "projectile_reference" && f.Domain == "attack" && (f.CurrentDefault.ValueKind != JsonValueKind.Object || f.ReferenceKind != "projectile"
                        || f.SemanticFieldId != "attack." + f.ReferenceRole + ".projectile" || string.IsNullOrWhiteSpace(f.CompatibilityClass))) throw new InvalidDataException("Invalid semantic projectile reference.");
                    var definitionId = Regex.Replace(Regex.Replace(f.SemanticFieldId, @"\.(primary|alternate|feed_primary|feed_alternate|impact|expiry)(?=\.)", ""), @"status_\d+_", "status_");
                    var definition = c.FieldDefinitions.SingleOrDefault(d => d.Id == definitionId) ?? throw new InvalidDataException("Capability has no semantic definition.");
                    if (f.Type != definition.Type || (f.Editable && (!definition.Writable || definition.Derived))) throw new InvalidDataException("Capability disagrees with its semantic definition.");
                    if (f.Provenance == null || f.SharedWithWeapons == null || f.SharedWithWeapons.Any(n => !c.Weapons.Any(other => other.Name == n))) throw new InvalidDataException("Invalid capability evidence or shared ownership.");
                    if (f.Editable && (w.OrdinaryWritesBlocked || f.DerivedReadOnly || f.Backing == null || f.CurrentDefault.ValueKind == JsonValueKind.Null || f.WriteScope == "unknown")) throw new InvalidDataException("Unsafe writable capability.");
                    if (f.Editable && (f.Backing!.Kind is not ("component" or "settings") || f.Backing.Storage is not ("f32" or "u32" or "i32" or "u8" or WeaponCapability.FireModeSet)
                        || f.Backing.Width != (f.Backing.Storage switch { "u8" => 1, WeaponCapability.FireModeSet => 16, _ => 4 }) || (f.Backing.Storage == WeaponCapability.FireModeSet) != (f.Type == WeaponCapability.FireModeSet)))
                        throw new UnsupportedSdkException("Unsupported writable backing type.");
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
            // Status references are validated as published, then kept read-only here: this build has no status-reference editor or Lua form.
            return c.Weapons.Any(w => w.Fields.Any(f => f.Type == WeaponCapability.StatusReference && f.Editable))
                ? c with { Weapons = c.Weapons.Select(w => w with { Fields = w.Fields.Select(f => f.Type == WeaponCapability.StatusReference && f.Editable
                    ? f with { Editable = false, AcceptedForWrites = false, Reason = WeaponCapability.StatusReferenceReason } : f).ToArray() }).ToArray() }
                : c;
        }
        catch (Exception e) when (e is JsonException or NullReferenceException or InvalidOperationException or KeyNotFoundException or FormatException or ArgumentException)
        { throw new InvalidDataException("Malformed player-weapon capability catalog: " + e.Message, e); }
    }
    // A status reference's allowedValues are status keys, not the integer mode values other fields publish; they are read as
    // AllowedReferences. Catalogs without status references are deserialized from the original bytes.
    private static byte[] StatusReferences(byte[] bytes, JsonElement root)
    {
        if (!root.GetProperty("weapons").EnumerateArray().Any(w => w.GetProperty("fields").EnumerateArray().Any(f => f.TryGetProperty("type", out var t) && t.GetString() == WeaponCapability.StatusReference)))
            return bytes;
        var node = System.Text.Json.Nodes.JsonNode.Parse(bytes)!;
        foreach (var w in node["weapons"]!.AsArray())
        foreach (var f in w!["fields"]!.AsArray())
            if (f!["type"]?.GetValue<string>() == WeaponCapability.StatusReference && f.AsObject().Remove("allowedValues", out var values))
                f.AsObject()["allowedReferences"] = values;
        return JsonSerializer.SerializeToUtf8Bytes(node);
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
