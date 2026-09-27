using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using HD2RuntimeGUI.Core.Generation;
using HD2RuntimeGUI.Core.Models;
using HD2RuntimeGUI.Core.Storage;

namespace HD2RuntimeGUI.Core.Metadata;

public sealed record AmmoSummary(int Weapons, int EffectiveCapacityResolved, int EffectiveCapacityNativeResolved,
    int CustomizationCorrelated, int NotApplicable, int RoundsFeedWeapons, int DirectMagazineWeapons,
    int DefaultMagazineOptions, int WeaponsWithWritableAmmoFields, int NativeMagazineOptionsCataloged, int SharedDefaultOptionGroups);
public sealed record AmmoLayoutField(int? Offset, string? Storage, string? Derived);
public sealed record AmmoLayout(AmmoLayoutField? Capacity, AmmoLayoutField? StartingMagazines,
    AmmoLayoutField? MagazinesFromSupply, AmmoLayoutField? SpareMagazines, AmmoLayoutField? MagazinesFromAmmoBox,
    AmmoLayoutField? FeedCapacity1, AmmoLayoutField? FeedCapacity2, AmmoLayoutField? SpareRounds,
    AmmoLayoutField? RoundsFromSupply, AmmoLayoutField? StartingRounds, AmmoLayoutField? RoundsFromAmmoBox);
public sealed record AmmoFieldLayout(
    [property: JsonPropertyName("WeaponMagazineComponentData")] AmmoLayout WeaponMagazineComponentData,
    [property: JsonPropertyName("WeaponRoundsComponentData")] AmmoLayout WeaponRoundsComponentData);
public sealed record AmmoOwnershipFindings(string DetachableMagazine, string DefaultCustomization, string RoundsFeed,
    string AmmoBoxRefill, string AlternateOptions);
public sealed record AmmoEvidenceCounts(int DirectMagazineTupleMatches, int DirectMagazineCatalogDiscrepancies,
    int DirectMagazineStartingSupplySpareMatches, int RoundsFeedUnambiguousMatches, int RoundsFeedAmbiguousIdentities,
    int DefaultCustomizationIdentities, int NativeMagazineOptionsCataloged);
public sealed record NativeMagazineOption(string OptionId, string Name, string AddPath, bool CapacityResolved,
    bool SpareMagazinesResolved, bool ApplicableWeaponsResolved, string Reason);
public sealed record AmmoDiscrepancy(string Weapon, string Field, JsonElement Runtime, JsonElement Catalog);
public sealed record EffectiveAmmoCapacity(string Status, JsonElement Value, string? Source);
public sealed record AlternateMagazineOptions(string Status, IReadOnlyList<NativeMagazineOption> Options, string? Reason);
public sealed record AmmoField(JsonElement Value, bool Direct, bool Writable, bool Shared, bool Derived, string? Reason);
public sealed record AmmoFields(AmmoField? Capacity, AmmoField? StartingMagazines, AmmoField? MagazinesFromSupply,
    AmmoField? SpareMagazines, AmmoField? MagazinesFromAmmoBox, AmmoField? FeedCapacity1, AmmoField? FeedCapacity2,
    AmmoField? SpareRounds, AmmoField? RoundsFromSupply, AmmoField? StartingRounds, AmmoField? RoundsFromAmmoBox)
{
    // Schema property names, not a weapon/capability table. Authoring descriptors
    // still supply every control, scalar type, baseline and public semantic ID.
    public IEnumerable<(string Id, AmmoField Field)> Fields(string domain)
    {
        (string Name, AmmoField? Field)[] fields = [(nameof(Capacity), Capacity), (nameof(StartingMagazines), StartingMagazines),
            (nameof(MagazinesFromSupply), MagazinesFromSupply), (nameof(SpareMagazines), SpareMagazines),
            (nameof(MagazinesFromAmmoBox), MagazinesFromAmmoBox), (nameof(FeedCapacity1), FeedCapacity1),
            (nameof(FeedCapacity2), FeedCapacity2), (nameof(SpareRounds), SpareRounds),
            (nameof(RoundsFromSupply), RoundsFromSupply), (nameof(StartingRounds), StartingRounds), (nameof(RoundsFromAmmoBox), RoundsFromAmmoBox)];
        return fields.Where(f => f.Field != null).Select(f => (domain + "." + Regex.Replace(f.Name, "([a-z])([A-Z0-9])", "$1_$2").ToLowerInvariant(), f.Field!));
    }
}
public sealed record AmmoValues(double? Capacity, int? StartingMagazines, int? MagazinesFromSupply, int? SpareMagazines,
    float? FeedCapacity1, float? FeedCapacity2, int? SpareRounds, int? RoundsFromSupply, int? StartingRounds);
public sealed record DefaultMagazineOption(string OptionId, string Name, string AddPath, AmmoValues BackingBaseValues,
    AmmoValues Values, bool Writable, string Reason, IReadOnlyList<string> SharedWithWeapons, bool Shared);
public sealed record AmmoResourceValues(string Resource, AmmoValues Values);
public sealed record PlayerWeaponAmmo(string Name, string Slot, IReadOnlyList<string> Resources, bool OrdinaryWritesBlocked,
    string BackingDomain, EffectiveAmmoCapacity EffectiveCapacity, AlternateMagazineOptions AlternateMagazineOptions,
    AmmoFields Fields, bool Shared, bool Writable, IReadOnlyList<string> Diagnostics,
    DefaultMagazineOption? DefaultMagazineOption, IReadOnlyList<AmmoResourceValues>? ResourceValues)
{
    [JsonIgnore] public string? SemanticDomain => BackingDomain switch { "WeaponMagazineComponentData" => "magazine", "WeaponRoundsComponentData" => "rounds", _ => null };
}
public sealed record PlayerWeaponAmmoCatalog(int SchemaVersion, string Hd2RuntimeVersion, BuildFingerprints BuildFingerprints,
    string SourceSnapshot, AmmoSummary Summary, AmmoFieldLayout FieldLayout, AmmoOwnershipFindings OwnershipFindings,
    AmmoEvidenceCounts EvidenceCounts, IReadOnlyList<NativeMagazineOption> NativeMagazineOptions,
    IReadOnlyList<AmmoDiscrepancy> Discrepancies, IReadOnlyList<PlayerWeaponAmmo> Weapons, CatalogSafety Safety);

public interface IPlayerWeaponAmmoCatalogReader
{
    PlayerWeaponAmmoCatalog Read(byte[] bytes, PlayerWeaponCatalog authoring);
}
public sealed class PlayerWeaponAmmoCatalogReader : IPlayerWeaponAmmoCatalogReader
{
    public const string FileName = "PlayerWeaponAmmoCapabilities.json";
    public const int MaxBytes = 2 * 1024 * 1024;
    public PlayerWeaponAmmoCatalog Read(byte[] bytes, PlayerWeaponCatalog authoring)
    {
        try
        {
            if (bytes.Length > MaxBytes) throw new InvalidDataException("Ammo catalog exceeds its size limit.");
            using var doc = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 32 });
            MetadataReader.RejectDuplicates(doc.RootElement);
            if (doc.RootElement.GetProperty("schemaVersion").GetInt32() != 1) throw new UnsupportedSdkException("Unsupported ammo capability schema.");
            var c = JsonSerializer.Deserialize<PlayerWeaponAmmoCatalog>(bytes, JsonStorage.Options) ?? throw new InvalidDataException("Empty ammo catalog.");
            if (c.Hd2RuntimeVersion != authoring.Hd2RuntimeVersion || SemVersion.Parse(c.Hd2RuntimeVersion).CompareTo(SemVersion.Parse("0.14.0")) < 0
                || c.BuildFingerprints != authoring.BuildFingerprints || c.SourceSnapshot != authoring.SourceSnapshot)
                throw new InvalidDataException("Ammo and authoring catalog identities differ.");
            if (c.Safety != authoring.Safety || c.Summary != authoring.Summary.Ammo) throw new InvalidDataException("Ammo safety or summary contract differs.");
            if (c.FieldLayout?.WeaponMagazineComponentData == null || c.FieldLayout.WeaponRoundsComponentData == null
                || c.OwnershipFindings == null || c.EvidenceCounts == null) throw new InvalidDataException("Missing ammo evidence structure.");
            if (c.Weapons.Count != authoring.Weapons.Count || c.Weapons.Select(w => w.Name).Distinct().Count() != c.Weapons.Count)
                throw new InvalidDataException("Invalid ammo weapon identities.");
            foreach (var w in c.Weapons)
            {
                var weapon = authoring.Weapon(w.Name);
                if (w.Slot != weapon.Slot || w.OrdinaryWritesBlocked != weapon.OrdinaryWritesBlocked || !w.Resources.Order().SequenceEqual(weapon.Resources.Order()))
                    throw new InvalidDataException("Ammo weapon identity mismatch.");
                if (w.BackingDomain is not ("none" or "WeaponMagazineComponentData" or "WeaponRoundsComponentData")
                    || w.EffectiveCapacity.Status is not ("NOT_APPLICABLE" or "RESOLVED" or "CORRELATED_CUSTOMIZATION" or "AMBIGUOUS_RUNTIME_IDENTITY"))
                    throw new UnsupportedSdkException("Unsupported ammo ownership model.");
                var fields = w.Fields.Fields(w.SemanticDomain ?? "none").ToArray();
                var capabilities = weapon.Fields.Where(f => f.Domain is "magazine" or "rounds").ToArray();
                if ((w.EffectiveCapacity.Status == "NOT_APPLICABLE" && (fields.Length > 0 || capabilities.Length > 0 || w.Writable))
                    || (w.OrdinaryWritesBlocked && w.Writable) || w.Writable != fields.Any(f => f.Field.Writable))
                    throw new InvalidDataException("Unsafe ammo applicability or writability.");
                foreach (var (id, f) in fields)
                {
                    if (f.Value.ValueKind is not (JsonValueKind.Number or JsonValueKind.Null) || (f.Writable && (!f.Direct || f.Derived || w.DefaultMagazineOption != null || w.OrdinaryWritesBlocked)))
                        throw new InvalidDataException("Unsafe ammo field.");
                    var capability = capabilities.SingleOrDefault(x => x.SemanticFieldId == id);
                    // The companion may retain read-only evidence for a blocked identity
                    // without exposing a semantic descriptor. Never manufacture a control.
                    if (capability == null && !f.Writable && w.OrdinaryWritesBlocked) continue;
                    if (capability == null || capability.Editable != f.Writable || capability.DerivedReadOnly != f.Derived
                        || !WeaponScalar.Equal(capability, capability.CurrentDefault, f.Value)
                        || (f.Writable && f.Shared && !capability.AffectsMultipleWeapons))
                        throw new InvalidDataException("Ammo field disagrees with the authoring capability: " + w.Name + " / " + id);
                }
                if (capabilities.Any(f => !fields.Any(x => x.Id == f.SemanticFieldId))) throw new InvalidDataException("Authoring ammo field has no companion evidence.");
                if (w.DefaultMagazineOption is { } option)
                {
                    if (option.Writable || w.Writable || w.EffectiveCapacity.Status != "CORRELATED_CUSTOMIZATION"
                        || string.IsNullOrWhiteSpace(option.Name) || option.Name.Length > 256 || string.IsNullOrWhiteSpace(option.Reason)
                        || !Regex.IsMatch(option.OptionId, "\\A0x[0-9A-Fa-f]{8}\\z") || !Regex.IsMatch(option.AddPath, "\\A0x[0-9A-Fa-f]{16}\\z")
                        || option.Values == null || option.BackingBaseValues == null
                        || option.SharedWithWeapons.Any(n => !c.Weapons.Any(other => other.Name == n))
                        || option.Shared != (option.SharedWithWeapons.Count > 0)) throw new InvalidDataException("Unsafe customization-owned magazine.");
                }
                if (w.Diagnostics == null || w.Diagnostics.Any(d => string.IsNullOrWhiteSpace(d) || d.Length > 4000) || w.AlternateMagazineOptions.Options == null) throw new InvalidDataException("Missing or invalid ammo evidence.");
            }
            var s = c.Summary;
            if (s.Weapons != c.Weapons.Count || s.NotApplicable != c.Weapons.Count(w => w.EffectiveCapacity.Status == "NOT_APPLICABLE")
                || s.WeaponsWithWritableAmmoFields != c.Weapons.Count(w => w.Writable)
                || s.DefaultMagazineOptions != c.Weapons.Count(w => w.DefaultMagazineOption != null)
                || s.CustomizationCorrelated != c.Weapons.Count(w => w.EffectiveCapacity.Status == "CORRELATED_CUSTOMIZATION")
                || s.EffectiveCapacityNativeResolved != c.Weapons.Count(w => w.EffectiveCapacity.Status == "RESOLVED")
                || s.EffectiveCapacityResolved != s.EffectiveCapacityNativeResolved + s.CustomizationCorrelated
                || s.RoundsFeedWeapons != c.Weapons.Count(w => w.SemanticDomain == "rounds")
                || s.DirectMagazineWeapons != c.Weapons.Count(w => w.SemanticDomain == "magazine" && w.DefaultMagazineOption == null)
                || s.NativeMagazineOptionsCataloged != c.NativeMagazineOptions.Count
                || s.SharedDefaultOptionGroups != c.Weapons.Where(w => w.DefaultMagazineOption?.Shared == true).Select(w => w.DefaultMagazineOption!.OptionId).Distinct().Count())
                throw new InvalidDataException("Ammo catalog summary mismatch.");
            foreach (var d in c.Discrepancies)
                if (!c.Weapons.Any(w => w.Name == d.Weapon) || d.Runtime.ValueKind != JsonValueKind.Number || d.Catalog.ValueKind != JsonValueKind.Number)
                    throw new InvalidDataException("Invalid ammo discrepancy evidence.");
            return c;
        }
        catch (Exception e) when (e is JsonException or NullReferenceException or InvalidOperationException or KeyNotFoundException or FormatException or ArgumentException)
        { throw new InvalidDataException("Malformed ammo capability catalog: " + e.Message, e); }
    }
}
