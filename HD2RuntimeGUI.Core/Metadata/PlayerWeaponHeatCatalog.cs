using System.Text.Json;
using System.Text.Json.Serialization;
using HD2RuntimeGUI.Core.Generation;
using HD2RuntimeGUI.Core.Models;
using HD2RuntimeGUI.Core.Storage;

namespace HD2RuntimeGUI.Core.Metadata;

public sealed record HeatSummary(int Weapons, int WeaponsWithHeatMechanism, int WeaponsWithHeatsinkMechanism,
    int DirectSemanticFieldsResolved, int WritableFieldInstances, int WeaponsWithWritableHeatFields,
    int SharedComponentGroups, int HeatsinkOptionIdentities, int WritableHeatsinkOptionOverrides);
public sealed record HeatSafety(int Writes, int ProtectionChanges, string FixtureFallback, bool SnapshotOnly);
public sealed record HeatLayout(string Component, string ComponentType, int RecordSize, int CapacityOffset,
    int HeatPerShotOffset, int HeatPerSecondOffset, int CoolPerSecondOffset, int HotCoolingMultiplierOffset,
    int ColdCoolingMultiplierOffset, int StartingHeatsinksOffset, int HeatsinksFromSupplyOffset, int SpareHeatsinksOffset);
public sealed record HeatFindings(string Cooling, string HeatGeneration, string Inventory, string Capacity, string AttachmentOverrides);
public sealed record HeatOwner(string Component, int RecordIndex, int IndexRow, int OwnerCount, bool UniqueOwner,
    int? ComponentIndex = null, string? ComponentType = null);
public sealed record HeatField(string Id, JsonElement Value, JsonElement WikiValue, bool Writable, string? Reason,
    bool Derived = false, bool? CorrelationMatches = null, int? Offset = null, string? Storage = null,
    HeatOwner? Owner = null, string? WriteScope = null);
public sealed record HeatCatalogEvidence(
    [property: JsonPropertyName("Overheats at")] IReadOnlyList<double> OverheatsAt,
    [property: JsonPropertyName("Warmup")] IReadOnlyList<double> Warmup,
    [property: JsonPropertyName("Heat Per Shot")] IReadOnlyList<double>? HeatPerShot,
    [property: JsonPropertyName("Heat Per Second")] IReadOnlyList<double>? HeatPerSecond,
    [property: JsonPropertyName("Cool Per Sec")] IReadOnlyList<double> CoolPerSec,
    [property: JsonPropertyName("Cooldown After Overheat")] IReadOnlyList<JsonElement> CooldownAfterOverheat,
    [property: JsonPropertyName("Beam Fire Rate")] IReadOnlyList<double>? BeamFireRate,
    [property: JsonPropertyName("Barrels")] IReadOnlyList<double>? Barrels,
    [property: JsonPropertyName("Beams")] IReadOnlyList<double>? Beams,
    int SpareMagazines, int StartingMagazines, int MagsFromSupply, int MagsFromAmmoBox);
public sealed record PlayerWeaponHeat(string Weapon, IReadOnlyList<string> Resources, bool HeatMechanismPresent,
    bool HeatsinkMechanismPresent, IReadOnlyList<HeatField> Fields, string? Reason,
    HeatOwner? ComponentIdentity = null, HeatCatalogEvidence? WikiHeatData = null);
public sealed record PlayerWeaponHeatCatalog(int SchemaVersion, string SourceSnapshot, string Hd2RuntimeVersion,
    BuildFingerprints GameFingerprints, int CatalogWeapons, HeatSafety Safety, string Feature, HeatSummary Summary,
    HeatLayout Layout, HeatFindings Findings, IReadOnlyList<JsonElement> SharedGroups, IReadOnlyList<PlayerWeaponHeat> Weapons);

public interface IPlayerWeaponHeatCatalogReader
{
    PlayerWeaponHeatCatalog Read(byte[] bytes, PlayerWeaponCatalog authoring);
}
public sealed class PlayerWeaponHeatCatalogReader : IPlayerWeaponHeatCatalogReader
{
    public const string FileName = "PlayerWeaponHeatCapabilities.json";
    public const int MaxBytes = 2 * 1024 * 1024;
    public PlayerWeaponHeatCatalog Read(byte[] bytes, PlayerWeaponCatalog authoring)
    {
        try
        {
            if (bytes.Length > MaxBytes) throw new InvalidDataException("Heat catalog exceeds its size limit.");
            using var doc = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 32 });
            MetadataReader.RejectDuplicates(doc.RootElement);
            if (doc.RootElement.GetProperty("schemaVersion").GetInt32() != 1) throw new UnsupportedSdkException("Unsupported heat capability schema.");
            var c = JsonSerializer.Deserialize<PlayerWeaponHeatCatalog>(bytes, JsonStorage.Options) ?? throw new InvalidDataException("Empty heat catalog.");
            if (!PublishedArtifactVersion.Matches(FileName, bytes, c.Hd2RuntimeVersion, authoring.Hd2RuntimeVersion) || SemVersion.Parse(c.Hd2RuntimeVersion).CompareTo(SemVersion.Parse("0.18.0")) < 0
                || c.GameFingerprints != authoring.BuildFingerprints || c.SourceSnapshot != authoring.SourceSnapshot
                || c.Feature != "player_weapon_heat_capabilities") throw new InvalidDataException("Heat and authoring catalog identities differ.");
            if (c.Safety is not { Writes: 0, ProtectionChanges: 0, FixtureFallback: "disabled", SnapshotOnly: true }
                || c.Summary.WritableHeatsinkOptionOverrides != 0) throw new InvalidDataException("Unsafe heat capability contract.");
            // The released contract contains no shared-group structure. Do not guess a future one.
            if (c.SharedGroups.Count != 0 || c.Summary.SharedComponentGroups != 0) throw new UnsupportedSdkException("Unsupported heat shared-group contract.");
            if (c.Layout?.Component != "WeaponHeatComponentData" || c.Findings == null
                || c.CatalogWeapons != authoring.Weapons.Count || c.Weapons.Count != c.CatalogWeapons
                || c.Weapons.Select(w => w.Weapon).Distinct().Count() != c.Weapons.Count) throw new InvalidDataException("Invalid heat catalog structure.");
            foreach (var w in c.Weapons)
            {
                var weapon = authoring.Weapon(w.Weapon);
                var fields = weapon.Fields.Where(f => f.Domain is "heat" or "heatsink").ToArray();
                if (!w.Resources.Order().SequenceEqual(weapon.Resources.Order()) || fields.Length != w.Fields.Count
                    || w.Fields.Select(f => f.Id).Distinct().Count() != w.Fields.Count
                    || w.HeatMechanismPresent != fields.Any(f => f.Domain == "heat")
                    || w.HeatsinkMechanismPresent != fields.Any(f => f.Domain == "heatsink")) throw new InvalidDataException("Heat applicability or identity mismatch.");
                if (w.Fields.Count > 0 && (w.ComponentIdentity?.Component != c.Layout.Component || w.WikiHeatData == null)) throw new InvalidDataException("Missing heat ownership/evidence.");
                foreach (var f in w.Fields)
                {
                    var a = fields.SingleOrDefault(a => a.SemanticFieldId == f.Id);
                    if (a == null || a.Editable != f.Writable || a.DerivedReadOnly != f.Derived
                        || !WeaponScalar.Equal(a, a.CurrentDefault, f.Value)
                        || f.Value.ValueKind is not (JsonValueKind.Number or JsonValueKind.Null)
                        || (!f.Writable && string.IsNullOrWhiteSpace(f.Reason))) throw new InvalidDataException("Heat capability disagrees with authoring metadata: " + w.Weapon + " / " + f.Id);
                    if (f.Owner is { } owner && (a.Backing is not { Kind: "component" } b || b.Component != owner.Component
                        || b.RecordIndex != owner.RecordIndex || b.IndexRow != owner.IndexRow || b.OwnerCount != owner.OwnerCount
                        || b.UniqueOwner != owner.UniqueOwner || b.Offset != f.Offset || b.Storage != f.Storage || a.WriteScope != f.WriteScope
                        || owner.RecordIndex != w.ComponentIdentity!.RecordIndex || owner.IndexRow != w.ComponentIdentity.IndexRow))
                        throw new InvalidDataException("Heat backing evidence mismatch.");
                    if (f.Writable && (f.Owner == null || f.Derived || weapon.OrdinaryWritesBlocked || f.CorrelationMatches != true))
                        throw new InvalidDataException("Unsafe writable heat field.");
                }
            }
            var s = c.Summary;
            if (s != authoring.Summary.Composition?.Heat) throw new InvalidDataException("Heat and authoring summaries differ.");
            if (s.Weapons != c.Weapons.Count || s.WeaponsWithHeatMechanism != c.Weapons.Count(w => w.HeatMechanismPresent)
                || s.WeaponsWithHeatsinkMechanism != c.Weapons.Count(w => w.HeatsinkMechanismPresent)
                || s.DirectSemanticFieldsResolved != c.Weapons.Sum(w => w.Fields.Count(f => f.Owner != null))
                || s.WritableFieldInstances != c.Weapons.Sum(w => w.Fields.Count(f => f.Writable))
                || s.WeaponsWithWritableHeatFields != c.Weapons.Count(w => w.Fields.Any(f => f.Writable))) throw new InvalidDataException("Heat summary mismatch.");
            return c;
        }
        catch (Exception e) when (e is JsonException or NullReferenceException or InvalidOperationException or KeyNotFoundException or FormatException or ArgumentException)
        { throw new InvalidDataException("Malformed heat capability catalog: " + e.Message, e); }
    }
}
