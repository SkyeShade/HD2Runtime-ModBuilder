using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using HD2RuntimeGUI.Core.Storage;

namespace HD2RuntimeGUI.Core.Metadata;

// Runtime 0.26.0 drop-pod payloads (hd2runtime.pod_payload.v1, PodPayloadCapabilities.json). A call-in's hellpod rack has 8 slots;
// slots 1-4 are authored (payload.entity = a reviewed pickup or 'empty') and the rack's spawn_count (1-4) decides how many spawn.
// Replacement pickups come only from Runtime's typed catalog; categories are published, never inferred from names.
public sealed record PodRackConsumer(string Name, string? NativeType, string? StratagemSemanticId, string? Booster, bool AlwaysAvailable);
public sealed record PodSpawnCount(int? Value, bool Writable, int[] Range, string[] Acknowledgements, string? Field);
public sealed record PodSlotItem(string? Pickup, string? Name, string? Category);
// 0.28.0 development SDKs: pickups a user-run test proved in this exact slot (for example the Grenade Box in the Resupply pod).
public sealed record PodRackSlot(int Slot, bool Active, PodSlotItem? Current, bool ApplyDeltas, int RackSide, bool Writable, string? Reason, string[] Acknowledgements,
    string[]? LiveVerifiedPickups = null);
public sealed record PodRack(string Name, string SemanticId, PodRackConsumer[] Consumers, bool Shared, bool Writable, string? Reason, PodSpawnCount SpawnCount,
    int RandomPayloadSize, PodRackSlot[] Slots, string[] ResidentPackages, string? Field)
{
    [JsonIgnore] public IEnumerable<PodRackSlot> AuthoredSlots => Slots.Where(s => s.Slot <= PodPayloadReader.AuthoredSlots);
}
public sealed record PickupEvidence(string[] RackPayloadOf, int VanillaRackCount, bool WorldLoot, bool StandalonePickup, string[] InteractTypes, bool OwnsBackpack, bool OwnsWeaponData);
public sealed record PickupResidency(string? PackageKey, bool AlwaysResident, string Basis);
// 0.27.0: whether Runtime knows and can automatically load the pickup's own package (LIVE_PROVEN / ALWAYS_RESIDENT / UNRESOLVED).
public sealed record PickupPackageDependency(bool Known, bool AlwaysResident, bool AutoLoadSupported, string? Package, string PackageResidency, bool LiveTested, string? Blocker);
// 0.27.0: the rack/pickup pairs a passing live test used (compatibility of other pairs stays unverified).
public sealed record PodLiveVerifiedPair(string Rack, int Slot, string Pickup, string Test, string Project);
public sealed record Pickup(string Name, string SemanticId, string Category, string CategoryLabel, string Compatibility, string CompatibilityBasis,
    PickupEvidence Evidence, PickupResidency Residency, string[] PackageOwners, PickupPackageDependency? PackageDependency = null);
public sealed record PodPayloadSummary(int Racks, int WritableRacks, int SharedRacks, int WritableSlots, int Pickups, Dictionary<string, int> PickupsByCategory);
public sealed record PodPayloadCatalogJson(string Contract, int SchemaVersion, string Hd2RuntimeVersion, Dictionary<string, string> Categories,
    Dictionary<string, string> CompatibilityStates, string PackageRisk, PodRack[] Racks, Pickup[] Pickups, PodPayloadSummary Summary,
    AssetReferenceFamily? PackageResidency = null, string? ResidencyVersusCompatibility = null, PodLiveVerifiedPair[]? LiveVerifiedPairs = null);

public sealed class PodPayloadCatalog
{
    public required PodRack[] Racks { get; init; }
    public required Pickup[] Pickups { get; init; }
    public required IReadOnlyDictionary<string, string> Categories { get; init; }
    public required IReadOnlyDictionary<string, string> CompatibilityStates { get; init; }
    public required string PackageRisk { get; init; }
    // 0.27.0+: automatic package loading for pod payloads, and the live-verified rack/pickup pairs.
    public AssetReferenceFamily? PackageResidency { get; init; }
    public string? ResidencyVersusCompatibility { get; init; }
    public IReadOnlyList<PodLiveVerifiedPair> LiveVerifiedPairs { get; init; } = [];
    public bool LiveVerifiedPair(PodRack rack, PodRackSlot slot, Pickup p) => LiveVerifiedPairs.Any(x => x.Rack == rack.Name && x.Slot == slot.Slot && x.Pickup == p.Name);
    // Slots and spawn counts adapted to the entity authoring pipeline (target resource "pod_rack").
    public required EntityField[] FieldInstances { get; init; }
    public PodRack? Rack(string name) => Racks.FirstOrDefault(r => r.Name == name);
    public Pickup? Pickup(string semanticId) => Pickups.FirstOrDefault(p => p.SemanticId == semanticId);
    // Racks a stratagem or booster delivers, by published consumer identity (stratagem semantic ID or booster name).
    public IEnumerable<PodRack> ForStratagem(string? semanticId) => semanticId == null ? [] : Racks.Where(r => r.Consumers.Any(c => c.StratagemSemanticId == semanticId));
    public IEnumerable<PodRack> ForBooster(string booster) => Racks.Where(r => r.Consumers.Any(c => c.Booster == booster));

    // Compatibility of a pickup in one slot: the slot's vanilla occupant is proven; otherwise the pickup's published state.
    public static string CompatibilityIn(PodRackSlot slot, Pickup p) => slot.Current?.Pickup == p.SemanticId ? "PROVEN_COMPATIBLE" : p.Compatibility;
    // Package risk (published rule): low for the vanilla occupant, an always-resident pickup, one whose package the rack already loads,
    // or (0.27.0+) one whose own package Runtime loads automatically before writing the slot.
    public static bool LowPackageRisk(PodRack rack, PodRackSlot slot, Pickup p) => slot.Current?.Pickup == p.SemanticId || p.Residency.AlwaysResident
        || p.PackageDependency is { AutoLoadSupported: true }
        || p.Residency.PackageKey != null && rack.ResidentPackages.Contains(p.Residency.PackageKey);
}

public static class PodPayloadReader
{
    public const string FileName = "PodPayloadCapabilities.json", Contract = "hd2runtime.pod_payload.v1", Empty = "empty";
    public const int MaxBytes = 4 * 1024 * 1024, AuthoredSlots = 4;
    public static readonly string[] States = ["PROVEN_COMPATIBLE", "SCHEMA_COMPATIBLE", "UNVERIFIED_REFERENCE", "INCOMPATIBLE"];
    private static readonly Regex PickupId = new(@"\Apickup/v1/[a-z0-9-]{1,96}/[0-9a-f]{16}\z", RegexOptions.CultureInvariant);
    private static readonly Regex RackId = new(@"\Apod-rack/v1/[a-z0-9-]{1,96}/[0-9a-f]{16}\z", RegexOptions.CultureInvariant);
    private static readonly JsonSerializerOptions Options = new(JsonStorage.Options) { UnmappedMemberHandling = JsonUnmappedMemberHandling.Skip };
    private static void Check(bool valid, [System.Runtime.CompilerServices.CallerLineNumber] int line = 0) { if (!valid) throw new InvalidDataException($"Inconsistent drop-pod payload capability metadata (check {line})."); }
    public static bool ValidName(string name) => name.Length is > 0 and <= 200 && !name.Any(char.IsControl);
    // A saved payload value: 'empty' or a published pickup semantic ID.
    public static bool ValidValue(JsonElement v) => v.ValueKind == JsonValueKind.String && (v.GetString() == Empty || PickupId.IsMatch(v.GetString()!));

    public static PodPayloadCatalog Read(byte[] bytes, string version)
    {
        try
        {
            if (bytes.Length > MaxBytes) throw new InvalidDataException("Drop-pod payload capability file exceeds size limit.");
            using var doc = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 32 }); MetadataReader.RejectDuplicates(doc.RootElement);
            var root = doc.RootElement;
            if (root.GetProperty("contract").GetString() != Contract || root.GetProperty("schemaVersion").GetInt32() != 1)
                throw new UnsupportedSdkException("Unsupported drop-pod payload contract.");
            var safety = root.GetProperty("safety");
            Check(!safety.GetProperty("runtimeAddresses").GetBoolean() && !safety.GetProperty("rawResourceIdentifiers").GetBoolean()
                && safety.GetProperty("writesDuringGeneration").GetInt32() == 0);
            var c = JsonSerializer.Deserialize<PodPayloadCatalogJson>(bytes, Options)!;
            Check(c.Hd2RuntimeVersion == version && States.All(c.CompatibilityStates.ContainsKey) && c.Categories.Count > 0 && !string.IsNullOrWhiteSpace(c.PackageRisk));
            var pickups = c.Pickups.ToDictionary(p => p.SemanticId, StringComparer.Ordinal);
            // Only typed replacement pickups are offered: never incompatible entities, never an unknown category.
            Check(pickups.Count == c.Pickups.Length && c.Pickups.Select(p => p.Name).Distinct().Count() == c.Pickups.Length
                && c.Pickups.All(p => PickupId.IsMatch(p.SemanticId) && ValidName(p.Name) && c.Categories.ContainsKey(p.Category)
                    && p.Compatibility is "SCHEMA_COMPATIBLE" or "UNVERIFIED_REFERENCE" && !string.IsNullOrWhiteSpace(p.Residency.Basis)));
            Check(c.Racks.Select(r => r.Name).Distinct().Count() == c.Racks.Length && c.Racks.Select(r => r.SemanticId).Distinct().Count() == c.Racks.Length);
            foreach (var r in c.Racks)
            {
                Check(RackId.IsMatch(r.SemanticId) && ValidName(r.Name) && r.Consumers.Length > 0 && r.Shared == (r.Consumers.Length > 1)
                    && (r.Writable ? r.Reason == null && r.RandomPayloadSize == 0 : !string.IsNullOrWhiteSpace(r.Reason))
                    && r.Slots.Select(s => s.Slot).SequenceEqual(Enumerable.Range(1, r.Slots.Length)) && r.Slots.Length <= 8
                    && r.SpawnCount.Range is [1, 4] && (!r.SpawnCount.Writable || r.Writable && r.SpawnCount.Value is >= 1 and <= 4
                        && r.SpawnCount.Acknowledgements.Contains("allow_unverified_effect") && r.SpawnCount.Acknowledgements.Contains("allow_shared") == r.Shared));
                foreach (var s in r.Slots)
                    Check((!s.Writable || r.Writable && s.Slot <= AuthoredSlots && s.Acknowledgements.Contains("allow_unverified_reference")
                            && s.Acknowledgements.Contains("allow_shared") == r.Shared && s.Reason == null)
                        && (s.Current?.Pickup is not { } item || pickups.ContainsKey(item))
                        && (!s.Writable || s.Current == null || s.Current.Pickup != null));
            }
            var sum = c.Summary;
            Check(sum.Racks == c.Racks.Length && sum.WritableRacks == c.Racks.Count(r => r.Writable) && sum.SharedRacks == c.Racks.Count(r => r.Shared)
                && sum.WritableSlots == c.Racks.Sum(r => r.Slots.Count(s => s.Writable)) && sum.Pickups == pickups.Count
                && sum.PickupsByCategory.All(p => c.Pickups.Count(x => x.Category == p.Key) == p.Value));
            // 0.27.0 asset loading: every pickup states its dependency; auto-loading needs a known package; live pairs name real slots.
            var loaded = c.PackageResidency != null;
            Check(loaded == (c.LiveVerifiedPairs != null) && loaded == c.Pickups.All(p => p.PackageDependency != null) && (!loaded || c.Pickups.All(p => p.PackageDependency!.PackageResidency is "LIVE_PROVEN" or "OFFLINE_PROVEN" or "ALWAYS_RESIDENT" or "UNRESOLVED"
                && (!p.PackageDependency.AutoLoadSupported || p.PackageDependency.Known) && (p.PackageDependency.Known || p.PackageDependency.AlwaysResident || !string.IsNullOrWhiteSpace(p.PackageDependency.Blocker))
                && p.PackageDependency.AlwaysResident == p.Residency.AlwaysResident)));
            var byName = c.Pickups.ToDictionary(p => p.Name, StringComparer.Ordinal);
            Check((c.LiveVerifiedPairs ?? []).All(x => c.Racks.FirstOrDefault(r => r.Name == x.Rack) is { } r && r.Slots.Any(s => s.Slot == x.Slot && s.Writable)
                && byName.TryGetValue(x.Pickup, out var p) && p.PackageDependency!.LiveTested));
            var fields = c.Racks.SelectMany(r => Adapt(r, c)).ToArray();
            return new() { Racks = c.Racks, Pickups = c.Pickups, Categories = c.Categories, CompatibilityStates = c.CompatibilityStates, PackageRisk = c.PackageRisk, FieldInstances = fields,
                PackageResidency = c.PackageResidency, ResidencyVersusCompatibility = c.ResidencyVersusCompatibility, LiveVerifiedPairs = c.LiveVerifiedPairs ?? [] };
        }
        catch (Exception e) when (e is JsonException or KeyNotFoundException or NullReferenceException or ArgumentException or InvalidOperationException)
        { throw new InvalidDataException("Malformed drop-pod payload capability metadata.", e); }
    }

    public static string SlotKey(PodRack r, int slot) => "pod_rack:" + r.SemanticId + ":slot-" + slot;
    public static string SpawnKey(PodRack r) => "pod_rack:" + r.SemanticId + ":spawn_count";

    // One operation per slot and one for the spawn count, all in one plan per rack; a shared rack is one shared scope.
    // Each slot is its own native value (its own backing object), so distinct slots never look like one value reached twice.
    private static IEnumerable<EntityField> Adapt(PodRack r, PodPayloadCatalogJson c)
    {
        var owner = "pod-rack:" + r.SemanticId; var plan = "pod-rack-plan:" + r.SemanticId; var scope = "pod-rack-scope:" + r.SemanticId;
        var tier = new EntityEvidence("structural_reference");
        var provenance = "PodPayloadCapabilities: hellpod rack delivered by " + string.Join(", ", r.Consumers.Select(x => x.Name));
        var offered = c.Pickups.Select(p => p.SemanticId).Prepend(Empty).ToArray();
        foreach (var s in r.AuthoredSlots)
            yield return new EntityField(SlotKey(r, s.Slot), "payload.entity", "Slot " + s.Slot, EntityField.PickupType, null,
                JsonSerializer.SerializeToElement(s.Current?.Pickup ?? Empty), s.Writable, s.Reason ?? (s.Writable ? null : r.Reason ?? "Read-only slot."),
                new EntityTarget("pod_rack", "slot", Rack: r.Name, Slot: s.Slot), owner + ":slot-" + s.Slot, "pod-rack-op:" + r.SemanticId + ":slot-" + s.Slot, plan, "patch_or_transaction",
                r.Shared, r.Shared, [], scope, !r.Shared, false, "HellpodRackComponent", "payload", "hd2.fields.payload.entity", 1, [], tier, provenance,
                AllowedValues: offered, Acknowledgement: "allow_unverified_reference", ValueKind: "pickup_semantic_id", ResidencyWarning: c.PackageRisk);
        if (r.SpawnCount.Value is int count)
            yield return new EntityField(SpawnKey(r), "payload.spawn_count", "Spawn count", "integer", "slots", JsonSerializer.SerializeToElement(count),
                r.SpawnCount.Writable, r.SpawnCount.Writable ? null : r.Reason ?? "Read-only spawn count.", new EntityTarget("pod_rack", "rack", Rack: r.Name),
                owner, "pod-rack-op:" + r.SemanticId + ":spawn", plan, "patch_or_transaction", r.Shared, r.Shared, [], scope, !r.Shared, false,
                "HellpodRackComponent", "payload", r.SpawnCount.Field ?? "hd2.fields.payload.spawn_count", 1, [], tier, provenance,
                Acknowledgement: "allow_unverified_effect", Range: new EntityRange(r.SpawnCount.Range[0], r.SpawnCount.Range[1], true, "Racks spawn their first 1-4 slots."));
    }
}
