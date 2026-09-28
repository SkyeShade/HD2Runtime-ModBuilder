using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using HD2RuntimeGUI.Core.Storage;

namespace HD2RuntimeGUI.Core.Metadata;

// Runtime 0.23.1+ magazine attachment authoring (hd2runtime.weapon_attachment.magazine.v1).
// On attachment weapons the magazine values belong to the attachment definition, not the weapon record.
// Weapon-to-option relationships are taken exactly as published; ambiguous or absent options stay unresolved.
public sealed record MagazineValues(int? Capacity, int? StartingMagazines, int? SpareMagazines, int? MagazinesFromSupply);
public sealed record MagazineConsumers(string[] NativeDefaultOf, string[] CatalogCorrelated, string[] CandidateFor, bool ScopeComplete, string? ScopeNote);
public sealed record MagazineAttachmentEvidence(string Tier, string[]? DefaultConsistency = null);
public sealed record MagazineAttachment(string SemanticId, string Name, bool NameUnique, string Slot, MagazineValues Values,
    string[] OtherPatchedEffects, string[] FieldInstanceKeys, MagazineConsumers Consumers, MagazineAttachmentEvidence Evidence);
public sealed record MagazinePublishedEffects(int? Capacity, int? StartingMagazines, int? MaxMagazines);
public sealed record MagazineOption(string Name, bool Default, string Relationship, string? Attachment, string[] Candidates,
    MagazinePublishedEffects PublishedEffects, string? Blocker)
{
    [JsonIgnore] public bool Resolved => Attachment != null;
}
public sealed record MagazineSlot(string Slot, string? DefaultAttachment, string? DefaultRelationship, string? DefaultConsistency,
    bool BaseRecordCapacityIsPlaceholder, MagazineOption[] Options);
public sealed record MagazineWeapon(string Weapon, MagazineSlot MagazineSlot);
public sealed record MagazineSelection(bool Writable, string Reason);
public sealed record MagazineSupersedes(string File, string[] Fields, string Note);
public sealed record MagazineOwnershipModel(string[] Chain, string WriteScope, string[] DistinctFrom, string WeaponCapacityField);
public sealed record MagazineSummary(int MagazineAttachments, int FieldInstances, int WritableFieldInstances, int Weapons, int WeaponsWithNativeDefault,
    int PlaceholderBaseCapacityWeapons, Dictionary<string, int> OptionRelationships, Dictionary<string, int> WritableByTier, bool SelectionWritable);
public sealed record MagazineAttachmentCatalog(string Contract, int SchemaVersion, string Hd2RuntimeVersion, string CanonicalCollection,
    MagazineOwnershipModel OwnershipModel, Dictionary<string, string> Acknowledgements, MagazineSupersedes Supersedes, MagazineSelection Selection,
    MagazineAttachment[] Attachments, MagazineWeapon[] Weapons, EntityField[] FieldInstances, MagazineSummary Summary)
{
    public MagazineAttachment? Attachment(string semanticId) => Attachments.FirstOrDefault(a => a.SemanticId == semanticId);
    public MagazineWeapon? Weapon(string name) => Weapons.FirstOrDefault(w => w.Weapon == name);
    // Resolved attachments for a weapon: the native resource default, then uniquely correlated catalog options.
    public IReadOnlyList<(MagazineAttachment Attachment, MagazineOption? Option, string Relationship)> Resolved(MagazineWeapon w)
    {
        var result = new List<(MagazineAttachment, MagazineOption?, string)>();
        if (w.MagazineSlot.DefaultAttachment is { } id)
            result.Add((Attachment(id)!, w.MagazineSlot.Options.FirstOrDefault(o => o.Attachment == id), w.MagazineSlot.DefaultRelationship!));
        foreach (var o in w.MagazineSlot.Options.Where(o => o.Resolved && o.Attachment != w.MagazineSlot.DefaultAttachment))
            if (result.All(r => r.Item1.SemanticId != o.Attachment)) result.Add((Attachment(o.Attachment!)!, o, o.Relationship));
        return result;
    }
}

public static class MagazineAttachmentReader
{
    public const string FileName = "MagazineAttachmentCapabilities.json", Contract = "hd2runtime.weapon_attachment.magazine.v1";
    public const int MaxBytes = 4 * 1024 * 1024;
    public static readonly string[] Tiers = ["native_owner", "native_owner_effect_consistent", "native_owner_value_consistent"];
    public static readonly string[] Fields = ["attachment.magazine_capacity", "attachment.starting_magazines", "attachment.magazines_from_supply", "attachment.spare_magazines"];
    private static readonly string[] ResolvedRelationships = ["native_resource_default", "catalog_effect_fingerprint_unique"];
    private static readonly string[] UnresolvedRelationships = ["catalog_effect_fingerprint_ambiguous", "catalog_effect_fingerprint_absent"];
    private static readonly Regex SemanticId = new(@"\Aweapon-attachment/v1/magazine/[a-z0-9-]{1,96}/[0-9a-f]{16}\z", RegexOptions.CultureInvariant);
    private static readonly JsonSerializerOptions Options = new(JsonStorage.Options) { UnmappedMemberHandling = JsonUnmappedMemberHandling.Skip };
    private static void Check(bool valid) { if (!valid) throw new InvalidDataException("Inconsistent magazine attachment capability metadata."); }
    private static int? Value(MagazineValues v, string field) => field switch
    {
        "attachment.magazine_capacity" => v.Capacity, "attachment.starting_magazines" => v.StartingMagazines,
        "attachment.magazines_from_supply" => v.MagazinesFromSupply, "attachment.spare_magazines" => v.SpareMagazines, _ => null,
    };
    public static MagazineAttachmentCatalog Read(byte[] bytes, string version, PlayerWeaponCatalog weapons)
    {
        try
        {
            if (bytes.Length > MaxBytes) throw new InvalidDataException("Magazine attachment capability file exceeds size limit.");
            using var doc = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 32 }); MetadataReader.RejectDuplicates(doc.RootElement);
            var root = doc.RootElement;
            if (root.GetProperty("contract").GetString() != Contract || root.GetProperty("schemaVersion").GetInt32() != 1)
                throw new UnsupportedSdkException("Unsupported magazine attachment authoring contract.");
            var safety = root.GetProperty("safety");
            Check(!safety.GetProperty("runtimeAddresses").GetBoolean() && !safety.GetProperty("rawResourceIdentifiers").GetBoolean()
                && safety.GetProperty("writesDuringGeneration").GetInt32() == 0 && safety.GetProperty("protectionChangesDuringGeneration").GetInt32() == 0
                && safety.GetProperty("fixtureFallback").GetString() == "disabled");
            var c = JsonSerializer.Deserialize<MagazineAttachmentCatalog>(bytes, Options)!;
            Check(c.Hd2RuntimeVersion == version && c.CanonicalCollection == "fieldInstances" && c.Acknowledgements.ContainsKey("allow_shared")
                && c.Acknowledgements.ContainsKey("allow_unverified_effect") && !string.IsNullOrWhiteSpace(c.Selection.Reason));
            var attachments = c.Attachments.ToDictionary(a => a.SemanticId, StringComparer.Ordinal);
            Check(attachments.Count == c.Attachments.Length && attachments.Keys.All(SemanticId.IsMatch) && c.FieldInstances.Select(f => f.InstanceKey).Distinct().Count() == c.FieldInstances.Length);
            foreach (var f in c.FieldInstances)
            {
                // Writes must carry both published opt-ins; the consumer scope is explicitly incomplete.
                Check(f.InstanceKey.StartsWith("attachment:", StringComparison.Ordinal) && f.InstanceKey.Length <= 512
                    && f.Target is { Resource: "weapon_attachment", Path: "magazine", Vehicle: null, Backpack: null, Zone: null, Mount: null }
                    && f.Target.Attachment != null && attachments.TryGetValue(f.Target.Attachment, out var a) && a!.FieldInstanceKeys.Contains(f.InstanceKey)
                    && Fields.Contains(f.SemanticFieldId) && f.Type == "integer" && f.Editable && f.Reason == null
                    && f.CurrentDefault.ValueKind == JsonValueKind.Number && f.CurrentDefault.GetInt64() == Value(a.Values, f.SemanticFieldId)
                    && f.Shared && f.AllowSharedRequired && !f.ReviewedScopeComplete && f.Acknowledgement == "allow_unverified_effect"
                    && f.ApiFieldConstant == "hd2.fields." + f.SemanticFieldId && f.PlanPhase == 1 && f.DependsOn.Length == 0 && f.Requires == "patch_or_transaction"
                    && Tiers.Contains(f.Evidence.Tier) && f.Evidence.Tier == a.Evidence.Tier && !string.IsNullOrWhiteSpace(f.Provenance)
                    && !string.IsNullOrWhiteSpace(f.BackingObjectId) && !string.IsNullOrWhiteSpace(f.OperationGroup) && !string.IsNullOrWhiteSpace(f.SharedScopeKey));
                _ = Generation.EntityScalar.Normalize(f, f.CurrentDefault);
            }
            // One attachment definition = one backing object, one operation group (a transaction of up to four fields) and one shared scope.
            foreach (var g in c.FieldInstances.GroupBy(f => f.Target.Attachment))
                Check(g.Select(f => f.BackingObjectId).Distinct().Count() == 1 && g.Select(f => f.OperationGroup).Distinct().Count() == 1
                    && g.Select(f => f.SharedScopeKey).Distinct().Count() == 1 && g.Select(f => f.PlanGroup).Distinct().Count() == 1 && g.Count() <= 4);
            Check(c.FieldInstances.GroupBy(f => f.OperationGroup).All(g => g.Select(f => f.Target.Attachment).Distinct().Count() == 1));
            foreach (var a in c.Attachments)
                Check(a.Slot == "magazine" && !string.IsNullOrWhiteSpace(a.Name) && Tiers.Contains(a.Evidence.Tier)
                    && a.FieldInstanceKeys.Order(StringComparer.Ordinal).SequenceEqual(c.FieldInstances.Where(f => f.Target.Attachment == a.SemanticId).Select(f => f.InstanceKey).Order(StringComparer.Ordinal))
                    && !a.Consumers.ScopeComplete);
            Check(c.Attachments.Where(a => a.NameUnique).Select(a => a.Name).Distinct().Count() == c.Attachments.Count(a => a.NameUnique));
            Check(c.Weapons.Select(w => w.Weapon).Distinct().Count() == c.Weapons.Length);
            foreach (var w in c.Weapons)
            {
                var s = w.MagazineSlot;
                // Weapon identities come from the player-weapon catalog; nothing is matched by attachment names.
                Check(weapons.Weapons.Any(p => p.Name == w.Weapon) && s.Slot == "magazine"
                    && (s.DefaultAttachment == null ? s.DefaultRelationship == null : attachments.ContainsKey(s.DefaultAttachment) && s.DefaultRelationship == "native_resource_default"));
                foreach (var o in s.Options)
                    Check(!string.IsNullOrWhiteSpace(o.Name) && o.Candidates.All(attachments.ContainsKey)
                        && (o.Resolved
                            ? ResolvedRelationships.Contains(o.Relationship) && attachments.ContainsKey(o.Attachment!) && o.Blocker == null
                                && (o.Relationship != "native_resource_default" || o.Attachment == s.DefaultAttachment)
                            : UnresolvedRelationships.Contains(o.Relationship) && !string.IsNullOrWhiteSpace(o.Blocker)
                                && (o.Relationship == "catalog_effect_fingerprint_absent" ? o.Candidates.Length == 0 : o.Candidates.Length > 1)));
            }
            var sum = c.Summary; var options = c.Weapons.SelectMany(w => w.MagazineSlot.Options).ToArray();
            Check(sum.MagazineAttachments == attachments.Count && sum.FieldInstances == c.FieldInstances.Length && sum.WritableFieldInstances == c.FieldInstances.Count(f => f.Editable)
                && sum.Weapons == c.Weapons.Length && sum.WeaponsWithNativeDefault == c.Weapons.Count(w => w.MagazineSlot.DefaultAttachment != null)
                && sum.PlaceholderBaseCapacityWeapons == c.Weapons.Count(w => w.MagazineSlot.BaseRecordCapacityIsPlaceholder)
                && sum.SelectionWritable == c.Selection.Writable
                && Same(sum.WritableByTier, c.FieldInstances.Where(f => f.Editable).GroupBy(f => f.Evidence.Tier).ToDictionary(g => g.Key, g => g.Count()))
                && sum.OptionRelationships.Where(p => p.Key != "native_resource_default").All(p => options.Count(o => o.Relationship == p.Key) == p.Value)
                && sum.OptionRelationships.GetValueOrDefault("native_resource_default") == options.Count(o => o.Relationship == "native_resource_default"));
            return c;
        }
        catch (Exception e) when (e is JsonException or KeyNotFoundException or NullReferenceException or ArgumentException or InvalidOperationException)
        { throw new InvalidDataException("Malformed magazine attachment capability metadata.", e); }
    }
    private static bool Same(Dictionary<string, int> a, Dictionary<string, int> b) => a.Count == b.Count && a.All(p => b.GetValueOrDefault(p.Key) == p.Value);
}
