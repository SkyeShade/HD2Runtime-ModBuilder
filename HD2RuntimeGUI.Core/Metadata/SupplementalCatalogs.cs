using System.Text.Json;
using System.Text.Json.Serialization;
using HD2RuntimeGUI.Core.Storage;

namespace HD2RuntimeGUI.Core.Metadata;

// Runtime 0.28.0 reference catalogs that explain authoring fields published elsewhere: status effects (the keys a status_reference takes),
// armory presentation (trait and penetration labels), weapon feeds (native and programmable ammunition feeds) and the live-evidence
// catalog (which tests removed an acknowledgement). Only their stable identity and the parts ModBuilder shows are read.

// sdk/StatusEffectCatalog.json (hd2runtime.status_effects.v1).
public sealed record StatusEffect(string SemanticId, string Name, string Family, double? Duration, JsonElement? TickDamage, bool Attachable,
    string? AttachableReason, bool SharedGlobal, string? DurationField);
public sealed record StatusEffectCatalogJson(string Contract, int SchemaVersion, string Hd2RuntimeVersion, JsonElement AttachmentModel, StatusEffect[] Statuses);
public sealed class StatusEffectCatalog
{
    public required IReadOnlyDictionary<string, StatusEffect> Statuses { get; init; }
    public required JsonElement AttachmentModel { get; init; }
    public StatusEffect? Find(string semanticId) => Statuses.GetValueOrDefault(semanticId);
    // The status name as published (underscores shown as spaces), or the key.
    public string Label(string semanticId) => Find(semanticId)?.Name.Replace('_', ' ') ?? semanticId;
}

// sdk/WeaponPresentationCapabilities.json (hd2runtime.weapon.presentation.v1).
public sealed record PresentationTrait(string SemanticId, string Label, string? LabelEnGb, int Languages, int Weapons);
public sealed record PenetrationChoice(string Value, string? Label);
public sealed record WeaponPresentationCatalogJson(string Contract, int SchemaVersion, string Hd2RuntimeVersion, string GameplaySeparation, string Refresh, string Stats,
    PresentationTrait[] Traits, PenetrationChoice[] PenetrationChoices);
public sealed class WeaponPresentationCatalog
{
    public required IReadOnlyDictionary<string, PresentationTrait> Traits { get; init; }
    public required PenetrationChoice[] PenetrationChoices { get; init; }
    public required string GameplaySeparation { get; init; }
    public required string Refresh { get; init; }
    public string TraitLabel(string semanticId) => Traits.GetValueOrDefault(semanticId)?.Label ?? semanticId;
    // Two published traits can share a label (two INCENDIARY string IDs): where a list offers both, each also names its published ID.
    public string ChoiceLabel(string semanticId)
    {
        var label = TraitLabel(semanticId);
        return Traits.Values.Count(t => t.Label == label) > 1 ? label + " (" + semanticId + ")" : label;
    }
    public string? PenetrationLabel(string value) => PenetrationChoices.FirstOrDefault(c => c.Value == value)?.Label;
}

// sdk/WeaponFeedCapabilities.json (hd2runtime.weapon.feeds.v1).
public sealed record FeedSelector(string? Function, string? Input, bool Bound, string[]? BindableInputs = null);
public sealed record FeedPresentation(string? Label, string? Icon, string? DisplayName, bool NativeLabel, string? Output = null, string? Note = null);
public sealed record WeaponFeed(string Id, int Index, string Mechanism, bool Native, FeedSelector? Selector, FeedPresentation? Presentation,
    string? AttackRole = null, double? Capacity = null, string? CapacityField = null, string? OwnedBy = null, string? State = null, string? Field = null,
    bool? Writable = null, string? Reason = null, bool? NativeProjectile = null, string? CompatibilityClass = null, string[]? Acknowledgements = null,
    bool? RequiresBinding = null, JsonElement? ProjectileSource = null);
// Inputs: the function each input is bound to (a published name, or an unnamed native value).
public sealed record WeaponFeeds(string Kind, string Weapon, WeaponFeed[] Feeds, bool SelectableNatively, Dictionary<string, JsonElement> Inputs);
public sealed record WeaponFeedCatalogJson(string Contract, int SchemaVersion, string Hd2RuntimeVersion, WeaponFeeds[] Weapons);
public sealed class WeaponFeedCatalog
{
    public required IReadOnlyList<WeaponFeeds> Weapons { get; init; }
    public WeaponFeeds? Of(string weapon) => Weapons.FirstOrDefault(w => w.Weapon == weapon);
    // kind: "player" or "support" (a player and a support weapon never share a name today, but the catalog keys them apart).
    public WeaponFeeds? Of(string kind, string weapon) => Weapons.FirstOrDefault(w => w.Kind == kind && w.Weapon == weapon);
}

// sdk/LiveEvidenceCatalog.json (hd2runtime.live_evidence.v1): a family of live tests and what it proved.
public sealed record LiveEvidenceFamily(string Status, string? Domain, string[]? Fields, string? Scope, string[]? Tests, string? AcknowledgementRemoved,
    string[]? Observations, string[]? NotPromoted);
public sealed record LiveEvidenceCatalogJson(string Contract, int SchemaVersion, string Hd2RuntimeVersion, string Build, Dictionary<string, string> StatusLevels,
    Dictionary<string, JsonElement> Families);
public sealed class LiveEvidenceCatalog
{
    public required string Build { get; init; }
    public required IReadOnlyDictionary<string, string> StatusLevels { get; init; }
    public required IReadOnlyDictionary<string, LiveEvidenceFamily> Families { get; init; }
    public LiveEvidenceFamily? Family(string family) => Families.GetValueOrDefault(family);
}

public static class SupplementalCatalogReader
{
    public const string StatusFile = "StatusEffectCatalog.json", PresentationFile = "WeaponPresentationCapabilities.json",
        FeedFile = "WeaponFeedCapabilities.json", LiveEvidenceFile = "LiveEvidenceCatalog.json";
    public static readonly string[] FileNames = [StatusFile, PresentationFile, FeedFile, LiveEvidenceFile];
    public const int MaxBytes = 2 * 1024 * 1024;
    private static readonly JsonSerializerOptions Options = new(JsonStorage.Options) { UnmappedMemberHandling = JsonUnmappedMemberHandling.Skip };
    private static void Check(bool valid, string file) { if (!valid) throw new InvalidDataException("Inconsistent " + file + "."); }
    private static T Parse<T>(byte[] bytes, string file, string contract, string version)
    {
        if (bytes.Length > MaxBytes) throw new InvalidDataException(file + " exceeds its size limit.");
        try
        {
            using var doc = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 48 }); MetadataReader.RejectDuplicates(doc.RootElement);
            var root = doc.RootElement;
            if (root.GetProperty("contract").GetString() != contract || root.GetProperty("schemaVersion").GetInt32() != 1)
                throw new UnsupportedSdkException("Unsupported " + file + " contract.");
            Check(root.GetProperty("hd2RuntimeVersion").GetString() == version, file);
            return JsonSerializer.Deserialize<T>(bytes, Options)!;
        }
        catch (Exception e) when (e is JsonException or KeyNotFoundException or InvalidOperationException) { throw new InvalidDataException("Malformed " + file + ".", e); }
    }
    public static StatusEffectCatalog Statuses(byte[] bytes, string version)
    {
        var c = Parse<StatusEffectCatalogJson>(bytes, StatusFile, "hd2runtime.status_effects.v1", version);
        Check(c.Statuses.Length > 0 && c.Statuses.All(s => !string.IsNullOrWhiteSpace(s.SemanticId) && !string.IsNullOrWhiteSpace(s.Name) && s.SemanticId != StatusReference.None)
            && c.Statuses.Select(s => s.SemanticId).Distinct().Count() == c.Statuses.Length && c.Statuses.All(s => s.Attachable || !string.IsNullOrWhiteSpace(s.AttachableReason)), StatusFile);
        return new() { Statuses = c.Statuses.ToDictionary(s => s.SemanticId, StringComparer.Ordinal), AttachmentModel = c.AttachmentModel.Clone() };
    }
    public static WeaponPresentationCatalog Presentation(byte[] bytes, string version)
    {
        var c = Parse<WeaponPresentationCatalogJson>(bytes, PresentationFile, "hd2runtime.weapon.presentation.v1", version);
        Check(c.Traits.Select(t => t.SemanticId).Distinct().Count() == c.Traits.Length && c.Traits.All(t => !string.IsNullOrWhiteSpace(t.Label))
            && c.PenetrationChoices.Any(p => p.Value == "none") && c.PenetrationChoices.Select(p => p.Value).Distinct().Count() == c.PenetrationChoices.Length, PresentationFile);
        return new() { Traits = c.Traits.ToDictionary(t => t.SemanticId, StringComparer.Ordinal), PenetrationChoices = c.PenetrationChoices,
            GameplaySeparation = c.GameplaySeparation, Refresh = c.Refresh };
    }
    public static WeaponFeedCatalog Feeds(byte[] bytes, string version)
    {
        var c = Parse<WeaponFeedCatalogJson>(bytes, FeedFile, "hd2runtime.weapon.feeds.v1", version);
        Check(c.Weapons.Select(w => (w.Kind, w.Weapon)).Distinct().Count() == c.Weapons.Length
            && c.Weapons.All(w => w.Kind is "player" or "support" && w.Feeds.Length > 0 && w.Feeds.Select(f => f.Id).Distinct().Count() == w.Feeds.Length
                && w.Feeds.All(f => f.Mechanism is "projectile" or "rounds_magazine" or "programmable_ammo")), FeedFile);
        return new() { Weapons = c.Weapons };
    }
    public static LiveEvidenceCatalog LiveEvidence(byte[] bytes, string version)
    {
        var c = Parse<LiveEvidenceCatalogJson>(bytes, LiveEvidenceFile, "hd2runtime.live_evidence.v1", version);
        var families = new Dictionary<string, LiveEvidenceFamily>(StringComparer.Ordinal);
        foreach (var (name, family) in c.Families)
        {
            // Families are either one family object or the summary lists of names by status (not read here).
            if (family.ValueKind != JsonValueKind.Object) continue;
            var f = family.Deserialize<LiveEvidenceFamily>(Options)!;
            // acknowledgementRemoved is prose naming the opt-in and its exact scope (the per-field liveProvenValues are what writes use).
            Check(c.StatusLevels.ContainsKey(f.Status), LiveEvidenceFile);
            families.Add(name, f);
        }
        Check(families.Count > 0, LiveEvidenceFile);
        return new() { Build = c.Build, StatusLevels = c.StatusLevels, Families = families };
    }
}

// A typed status reference value (0.28.0): a published status key from the field's allowed list, or 'none' when the slot may be cleared.
// Written as a quoted string (hd2.fields.damage.status_1_type = 'fire').
public static class StatusReference
{
    public const string None = "none";
    public static string Normalize(JsonElement value, IReadOnlyList<string> allowed, bool allowNone)
    {
        if (value.ValueKind != JsonValueKind.String || value.GetString() is not { } key) throw new InvalidDataException(Localization.CoreText.Get("Messages.Build.Status.ChooseStatus"));
        if (key == None ? !allowNone : !allowed.Contains(key)) throw new InvalidDataException(Localization.CoreText.Format("Messages.Build.Status.NotAllowed", key));
        return key;
    }
    public static string Lua(string key) => Generation.LuaGenerator.Quote(key);
}
