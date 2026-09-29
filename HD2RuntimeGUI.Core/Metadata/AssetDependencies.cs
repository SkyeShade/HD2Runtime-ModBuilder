using System.Text.Json;
using System.Text.Json.Serialization;
using HD2RuntimeGUI.Core.Storage;

namespace HD2RuntimeGUI.Core.Metadata;

// Runtime 0.27.0 automatic asset loading (hd2runtime.asset_dependencies.v1, AssetDependencyCapabilities.json). For every semantic
// object that a reference swap can point at, Runtime publishes whether it knows the package holding the object's assets and can
// load it automatically before the write (status waiting_for_assets; ASSET_UNAVAILABLE if it cannot). Package residency is a
// separate question from slot/gameplay compatibility, which each domain keeps answering (allow_unverified_reference).
// Package identities are never published or inferred here: only the short package name, when Runtime recovered it.
public sealed record AssetDependency(bool Known, bool AutoLoadSupported, string? Derivation, string? Package, bool? PackageNamed,
    bool LiveTested, bool PackageLiveLoaded, string? Blocker);
public sealed record AssetObject(string Key, string Label, string Kind, AssetDependency PackageDependency);
public sealed record AssetFamilyTest(string Id, string Project, string Result);
public sealed record AssetReferenceFamily(string Description, string PackageResidency, string Basis, string? LiveBuild, AssetFamilyTest[] LiveTests)
{
    [JsonIgnore] public bool LiveProven => PackageResidency == AssetDependencyReader.LiveProven;
}
public sealed record AssetLiveTest(string Id, string Project, string Family, string Result, bool DonorCarried, string[] Observations);
public sealed record AssetLiveEvidence(string Recorded, string RuntimeCommit, string Control, AssetLiveTest[] Tests);
public sealed record AssetLoadPolicy(string Retain, int MaxHeldPackages, int LoadTimeoutSeconds, double PollSeconds, double RefcountFillLimit);
public sealed record AssetSummary(int SemanticObjects, int Known, int OwnPackage, int HolderPackage, int StratagemPackage, int Unknown, int Packages,
    int UnnamedPackages, int LiveTestedObjects, int LiveLoadedPackages, string[] LiveProvenFamilies);
public sealed record AssetSafety(bool ArbitraryPackages, bool CallerSuppliedIdentities, int Writes);
public sealed record AssetDependencyCatalogJson(string Contract, int SchemaVersion, string Build, string[] Model, string ResidencyVersusCompatibility,
    Dictionary<string, AssetReferenceFamily> ReferenceFamilies, AssetLiveEvidence LiveEvidence, AssetLoadPolicy Policy, AssetSummary Summary,
    AssetObject[] Objects, AssetSafety Safety);

public sealed class AssetDependencyCatalog
{
    public required IReadOnlyDictionary<string, AssetObject> Objects { get; init; }
    public required IReadOnlyDictionary<string, AssetReferenceFamily> Families { get; init; }
    public required AssetLiveEvidence LiveEvidence { get; init; }
    public required AssetLoadPolicy Policy { get; init; }
    public required string ResidencyVersusCompatibility { get; init; }
    public required AssetSummary Summary { get; init; }
    public AssetObject? Pickup(string semanticId) => Objects.GetValueOrDefault("pickup/" + semanticId);
    public AssetObject? MountedWeapon(string semanticId) => Objects.GetValueOrDefault("mounted_weapon/" + semanticId);
    public AssetReferenceFamily? Family(string family) => Families.GetValueOrDefault(family);
}

public static class AssetDependencyReader
{
    public const string FileName = "AssetDependencyCapabilities.json", Contract = "hd2runtime.asset_dependencies.v1";
    public const int MaxBytes = 4 * 1024 * 1024;
    public const string LiveProven = "LIVE_PROVEN", OfflineProven = "OFFLINE_PROVEN";
    public const string PodPayloadFamily = "pod_payload_pickup", ProjectileFamily = "projectile_reference", ExplosionFamily = "explosion_reference", MountFamily = "vehicle_mount";
    public static readonly string[] Families = [PodPayloadFamily, ProjectileFamily, ExplosionFamily, MountFamily];
    // Every published object kind and the key namespace it lives in (keys are "<namespace>/<semantic id or name>").
    private static readonly Dictionary<string, string> KindNamespaces = new()
    {
        ["backpack"] = "backpack", ["mounted_weapon"] = "mounted_weapon", ["player_weapon"] = "player_weapon", ["support_weapon"] = "support_weapon",
        ["throwable"] = "throwable", ["vehicle"] = "vehicle", ["pickup_support_weapon"] = "pickup", ["pickup_backpack"] = "pickup", ["pickup_ammo"] = "pickup",
        ["pickup_grenade"] = "pickup", ["pickup_stim"] = "pickup", ["pickup_supply"] = "pickup",
        // 0.28.0 development SDKs: explosions event scripts request (hd2.explosions.spawn), such as the Hellbombs.
        ["explosion"] = "explosion",
    };
    private static readonly JsonSerializerOptions Options = new(JsonStorage.Options) { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow };
    private static void Check(bool valid, [System.Runtime.CompilerServices.CallerLineNumber] int line = 0) { if (!valid) throw new InvalidDataException($"Inconsistent asset dependency metadata (check {line})."); }

    public static AssetDependencyCatalog Read(byte[] bytes)
    {
        try
        {
            if (bytes.Length > MaxBytes) throw new InvalidDataException("Asset dependency file exceeds size limit.");
            using var doc = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 32 }); MetadataReader.RejectDuplicates(doc.RootElement);
            var root = doc.RootElement;
            if (root.GetProperty("contract").GetString() != Contract || root.GetProperty("schemaVersion").GetInt32() != 1)
                throw new UnsupportedSdkException("Unsupported asset dependency contract.");
            var c = JsonSerializer.Deserialize<AssetDependencyCatalogJson>(bytes, Options)!;
            // Runtime publishes no raw package identities and accepts none from callers.
            Check(!c.Safety.ArbitraryPackages && !c.Safety.CallerSuppliedIdentities && c.Safety.Writes == 0);
            if (!c.Objects.All(o => KindNamespaces.ContainsKey(o.Kind))) throw new UnsupportedSdkException("Unsupported asset dependency object kind.");
            var objects = new Dictionary<string, AssetObject>(StringComparer.Ordinal);
            foreach (var o in c.Objects)
            {
                var d = o.PackageDependency;
                Check(objects.TryAdd(o.Key, o) && o.Key.StartsWith(KindNamespaces[o.Kind] + "/", StringComparison.Ordinal) && o.Key.Length <= 256
                    && !string.IsNullOrWhiteSpace(o.Label) && !o.Label.Any(char.IsControl));
                // Auto-loading needs a known dependency; an unknown one names no package and states why.
                Check(!d.AutoLoadSupported || d.Known);
                Check(d.Known ? d.Blocker == null && d.Derivation is not null && d.PackageNamed is not null
                    : !d.AutoLoadSupported && d.Package == null && d.Derivation == null && !string.IsNullOrWhiteSpace(d.Blocker));
                Check(d.PackageNamed != false || d.Package == null);
                Check(d.Package == null || d.Package.Length <= 128 && System.Text.RegularExpressions.Regex.IsMatch(d.Package, @"\A[a-z0-9_]+\z"));
                Check(!d.LiveTested || d.PackageLiveLoaded && d.Known);
            }
            var tests = c.LiveEvidence.Tests.ToDictionary(t => t.Id, StringComparer.Ordinal);
            Check(Families.All(c.ReferenceFamilies.ContainsKey) && c.ReferenceFamilies.Count == Families.Length);
            foreach (var (name, f) in c.ReferenceFamilies)
            {
                Check(f.PackageResidency is LiveProven or OfflineProven && !string.IsNullOrWhiteSpace(f.Basis));
                Check(f.LiveTests.All(t => tests.TryGetValue(t.Id, out var e) && e.Family == name && e.Result == t.Result && e.Project == t.Project));
                // Live-proven means at least one passing live test in which nobody carried the donor item.
                Check(!f.LiveProven || f.LiveTests.Any(t => t.Result == "PASS" && !tests[t.Id].DonorCarried));
            }
            Check(c.LiveEvidence.Tests.All(t => c.ReferenceFamilies.ContainsKey(t.Family) && t.Result is "PASS" or "FAIL" or "INCONCLUSIVE"));
            var p = c.Policy; Check(p.MaxHeldPackages > 0 && p.LoadTimeoutSeconds > 0 && p.PollSeconds > 0 && p.RefcountFillLimit is > 0 and < 1);
            var s = c.Summary; var all = c.Objects;
            Check(s.SemanticObjects == all.Length && s.Known == all.Count(o => o.PackageDependency.Known) && s.Unknown == all.Count(o => !o.PackageDependency.Known)
                && s.LiveTestedObjects == all.Count(o => o.PackageDependency.LiveTested)
                && s.LiveProvenFamilies.Order(StringComparer.Ordinal).SequenceEqual(c.ReferenceFamilies.Where(f => f.Value.LiveProven).Select(f => f.Key).Order(StringComparer.Ordinal)));
            return new() { Objects = objects, Families = c.ReferenceFamilies, LiveEvidence = c.LiveEvidence, Policy = c.Policy,
                ResidencyVersusCompatibility = c.ResidencyVersusCompatibility, Summary = c.Summary };
        }
        catch (Exception e) when (e is JsonException or KeyNotFoundException or NullReferenceException or ArgumentException or InvalidOperationException)
        { throw new InvalidDataException("Malformed asset dependency metadata.", e); }
    }
}
