using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;
using HD2RuntimeGUI.Core.GitHub;
using HD2RuntimeGUI.Core.Storage;

namespace HD2RuntimeGUI.Core.Metadata;

public interface ISdkCache
{
    Task<SdkMetadata> GetCurrentAsync(CancellationToken ct = default);
    Task<SdkMetadata> GetVersionAsync(string version, CancellationToken ct = default);
    Task<SdkMetadata> InstallAsync(SdkRelease release, CancellationToken ct = default);
    Task<SdkMetadata> InspectAsync(SdkRelease release, CancellationToken ct = default);
}

/// <summary>A cached SDK version lacks a file this GUI requires and cannot be completed offline.</summary>
public sealed class IncompleteSdkCacheException(string version, string file) : IOException(
    $"The cached SDK {version} was saved by an older HD2RuntimeGUI and lacks {file}. Check for updates online to reinstall SDK {version}; the missing file is added without changing the cached files.")
{
    public string Version { get; } = version;
    public string File { get; } = file;
}

public sealed class SdkCache(AppPaths paths, IMetadataReader reader, IGitHubReleaseClient github) : ISdkCache
{
    private readonly IPlayerWeaponCatalogReader catalogReader = new PlayerWeaponCatalogReader();
    private readonly IPlayerWeaponAmmoCatalogReader ammoReader = new PlayerWeaponAmmoCatalogReader();
    public SdkCache(AppPaths paths, IMetadataReader reader, IGitHubReleaseClient github, IPlayerWeaponCatalogReader catalogReader) : this(paths, reader, github) => this.catalogReader = catalogReader;
    public SdkCache(AppPaths paths, IMetadataReader reader, IGitHubReleaseClient github, IPlayerWeaponCatalogReader catalogReader, IPlayerWeaponAmmoCatalogReader ammoReader)
        : this(paths, reader, github, catalogReader) => this.ammoReader = ammoReader;
    private readonly IPlayerWeaponCompositionReader compositionReader = new PlayerWeaponCompositionReader();
    private readonly IAdvancedCapabilitiesReader advancedReader = new AdvancedCapabilitiesReader();
    public SdkCache(AppPaths paths, IMetadataReader reader, IGitHubReleaseClient github, IPlayerWeaponCatalogReader catalogReader, IPlayerWeaponAmmoCatalogReader ammoReader, IPlayerWeaponCompositionReader compositionReader)
        : this(paths, reader, github, catalogReader, ammoReader) => this.compositionReader = compositionReader;
    public SdkCache(AppPaths paths, IMetadataReader reader, IGitHubReleaseClient github, IPlayerWeaponCatalogReader catalogReader, IPlayerWeaponAmmoCatalogReader ammoReader, IPlayerWeaponCompositionReader compositionReader, IAdvancedCapabilitiesReader advancedReader)
        : this(paths, reader, github, catalogReader, ammoReader, compositionReader) => this.advancedReader = advancedReader;
    private readonly IPlayerWeaponHeatCatalogReader heatReader = new PlayerWeaponHeatCatalogReader();
    public SdkCache(AppPaths paths, IMetadataReader reader, IGitHubReleaseClient github, IPlayerWeaponCatalogReader catalogReader, IPlayerWeaponAmmoCatalogReader ammoReader, IPlayerWeaponCompositionReader compositionReader, IAdvancedCapabilitiesReader advancedReader, IPlayerWeaponHeatCatalogReader heatReader)
        : this(paths, reader, github, catalogReader, ammoReader, compositionReader, advancedReader) => this.heatReader = heatReader;
    private readonly ICompositionPlanCapabilitiesReader planReader = new CompositionPlanCapabilitiesReader();
    public SdkCache(AppPaths paths, IMetadataReader reader, IGitHubReleaseClient github, IPlayerWeaponCatalogReader catalogReader, IPlayerWeaponAmmoCatalogReader ammoReader, IPlayerWeaponCompositionReader compositionReader, IAdvancedCapabilitiesReader advancedReader, IPlayerWeaponHeatCatalogReader heatReader, ICompositionPlanCapabilitiesReader planReader)
        : this(paths, reader, github, catalogReader, ammoReader, compositionReader, advancedReader, heatReader) => this.planReader = planReader;
    private readonly ISupportAuthoringReader supportReader = new SupportAuthoringReader();
    public SdkCache(AppPaths paths, IMetadataReader reader, IGitHubReleaseClient github, IPlayerWeaponCatalogReader catalogReader, IPlayerWeaponAmmoCatalogReader ammoReader, IPlayerWeaponCompositionReader compositionReader, IAdvancedCapabilitiesReader advancedReader, IPlayerWeaponHeatCatalogReader heatReader, ICompositionPlanCapabilitiesReader planReader, ISupportAuthoringReader supportReader)
        : this(paths, reader, github, catalogReader, ammoReader, compositionReader, advancedReader, heatReader, planReader) => this.supportReader = supportReader;
    private readonly IStratagemCatalogReader stratagemReader = new StratagemCatalogReader();
    public SdkCache(AppPaths paths, IMetadataReader reader, IGitHubReleaseClient github, IPlayerWeaponCatalogReader catalogReader, IPlayerWeaponAmmoCatalogReader ammoReader, IPlayerWeaponCompositionReader compositionReader, IAdvancedCapabilitiesReader advancedReader, IPlayerWeaponHeatCatalogReader heatReader, ICompositionPlanCapabilitiesReader planReader, ISupportAuthoringReader supportReader, IStratagemCatalogReader stratagemReader)
        : this(paths, reader, github, catalogReader, ammoReader, compositionReader, advancedReader, heatReader, planReader, supportReader) => this.stratagemReader = stratagemReader;
    private static IEnumerable<string> GraphFiles => PlayerWeaponCompositionReader.FileNames.Concat(AdvancedCapabilitiesReader.FileNames).Append(PlayerWeaponHeatCatalogReader.FileName).Append(CompositionPlanCapabilitiesReader.FileName).Append(SupportAuthoringReader.FileName).Append(StratagemCatalogReader.FileName).Append(EntityAuthoringReader.VehicleFile).Append(EntityAuthoringReader.BackpackFile).Append(MagazineAttachmentReader.FileName);
    private readonly SemaphoreSlim gate = new(1);
    private readonly Dictionary<SdkRelease, SdkPayload> inspected = new();
    private sealed record SdkPayload(byte[] Metadata, byte[]? Capabilities, byte[]? Ammo, IReadOnlyDictionary<string, byte[]>? Composition = null);
    private sealed record CurrentSdk(string Version);
    public static byte[] BundledMetadata()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("HD2RuntimeGUI.Core.Metadata.Bundled.metadata.json")!;
        using var buffer = new MemoryStream(); stream.CopyTo(buffer); return buffer.ToArray();
    }
    public static byte[] BundledCapabilities()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("HD2RuntimeGUI.Core.Metadata.Bundled." + PlayerWeaponCatalogReader.FileName)!;
        using var buffer = new MemoryStream(); stream.CopyTo(buffer); return buffer.ToArray();
    }
    public static byte[] BundledAmmoCapabilities()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("HD2RuntimeGUI.Core.Metadata.Bundled." + PlayerWeaponAmmoCatalogReader.FileName)!;
        using var buffer = new MemoryStream(); stream.CopyTo(buffer); return buffer.ToArray();
    }
    public static IReadOnlyDictionary<string, byte[]> BundledComposition() => GraphFiles.ToDictionary(n => n, n =>
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("HD2RuntimeGUI.Core.Metadata.Bundled." + n)!;
        using var buffer = new MemoryStream(); stream.CopyTo(buffer); return buffer.ToArray();
    });
    private SdkMetadata ReadPayload(SdkPayload payload)
    {
        var sdk = reader.Read(payload.Metadata);
        if (Models.SemVersion.Parse(sdk.Version).CompareTo(Models.SemVersion.Parse("0.13.0")) >= 0)
            sdk = sdk with { PlayerWeapons = catalogReader.Read(payload.Capabilities ?? throw MissingFile(PlayerWeaponCatalogReader.FileName, "SDK is missing its player-weapon capability catalog."), sdk.Version) };
        if (Models.SemVersion.Parse(sdk.Version).CompareTo(Models.SemVersion.Parse("0.14.0")) >= 0)
            sdk = sdk with { PlayerAmmo = ammoReader.Read(payload.Ammo ?? throw MissingFile(PlayerWeaponAmmoCatalogReader.FileName, "SDK is missing its player-weapon ammo capability catalog."), sdk.PlayerWeapons!) };
        if (Models.SemVersion.Parse(sdk.Version).CompareTo(Models.SemVersion.Parse("0.15.0")) >= 0)
            sdk = sdk with { Composition = compositionReader.Read(payload.Composition ?? throw new InvalidDataException("SDK is missing composition metadata."), sdk.PlayerWeapons!) };
        if (Models.SemVersion.Parse(sdk.Version).CompareTo(Models.SemVersion.Parse("0.17.0")) >= 0)
            sdk = sdk with { Advanced = advancedReader.Read(payload.Composition!, sdk.PlayerWeapons!, sdk.Composition!) };
        if (Models.SemVersion.Parse(sdk.Version).CompareTo(Models.SemVersion.Parse("0.18.0")) >= 0)
            sdk = sdk with { PlayerHeat = heatReader.Read(payload.Composition!.GetValueOrDefault(PlayerWeaponHeatCatalogReader.FileName)
                ?? throw MissingFile(PlayerWeaponHeatCatalogReader.FileName, "SDK is missing its player-weapon heat capability catalog."), sdk.PlayerWeapons!) };
        if (Models.SemVersion.Parse(sdk.Version).CompareTo(Models.SemVersion.Parse("0.19.0")) >= 0)
            sdk = sdk with { Plans = planReader.Read(payload.Composition!.GetValueOrDefault(CompositionPlanCapabilitiesReader.FileName)
                ?? throw MissingFile(CompositionPlanCapabilitiesReader.FileName, "SDK is missing its composition plan capability contract.")) };
        if (Models.SemVersion.Parse(sdk.Version).CompareTo(Models.SemVersion.Parse("0.20.1")) >= 0)
        {
            if (sdk.Plans?.FieldCapabilitySources?.Contains(SupportAuthoringReader.FileName) != true)
                throw new InvalidDataException("Plan contract does not declare support authoring capabilities.");
            sdk = sdk with { SupportAuthoring = supportReader.Read(payload.Composition!.GetValueOrDefault(SupportAuthoringReader.FileName)
                ?? throw MissingFile(SupportAuthoringReader.FileName, "SDK is missing canonical support authoring metadata."), sdk.Version) };
        }
        if (Models.SemVersion.Parse(sdk.Version).CompareTo(Models.SemVersion.Parse("0.21.0")) >= 0)
        {
            if (sdk.Plans?.FieldCapabilitySources?.Contains(StratagemCatalogReader.FileName) != true)
                throw new InvalidDataException("Plan contract does not declare stratagem authoring capabilities.");
            sdk = sdk with { Stratagems = stratagemReader.Read(payload.Composition!.GetValueOrDefault(StratagemCatalogReader.FileName)
                ?? throw MissingFile(StratagemCatalogReader.FileName, "SDK is missing canonical stratagem capabilities.")) };
        }
        if (sdk.Stratagems != null && sdk.SupportAuthoring != null) sdk = sdk with { SupportLinks = SupportCallInLinker.Link(sdk.Stratagems, sdk.SupportAuthoring) };
        if (Models.SemVersion.Parse(sdk.Version).CompareTo(Models.SemVersion.Parse("0.23.0")) >= 0)
            sdk = sdk with { Entities = EntityAuthoringReader.Read(
                payload.Composition!.GetValueOrDefault(EntityAuthoringReader.VehicleFile) ?? throw MissingFile(EntityAuthoringReader.VehicleFile, "SDK is missing vehicle authoring capabilities."),
                payload.Composition!.GetValueOrDefault(EntityAuthoringReader.BackpackFile) ?? throw MissingFile(EntityAuthoringReader.BackpackFile, "SDK is missing backpack authoring capabilities."),
                sdk.Version, sdk.Stratagems ?? throw new InvalidDataException("SDK is missing canonical stratagem capabilities.")) };
        // 0.23.1: magazine values on attachment weapons are owned by attachment definitions (supersedes the older attachment ammo-owner data).
        if (Models.SemVersion.Parse(sdk.Version).CompareTo(Models.SemVersion.Parse("0.23.1")) >= 0)
            sdk = sdk with { Entities = new EntityAuthoring { Vehicles = sdk.Entities!.Vehicles, Backpacks = sdk.Entities.Backpacks, CallIns = sdk.Entities.CallIns,
                Attachments = MagazineAttachmentReader.Read(payload.Composition!.GetValueOrDefault(MagazineAttachmentReader.FileName)
                    ?? throw MissingFile(MagazineAttachmentReader.FileName, "SDK is missing magazine attachment capabilities."), sdk.Version, sdk.PlayerWeapons!) } };
        return sdk;
    }
    public async Task<SdkMetadata> GetCurrentAsync(CancellationToken ct = default)
    {
        await gate.WaitAsync(ct);
        try
        {
            var pointer = paths.CachePath("current.json");
            if (File.Exists(pointer)) return await GetVersionAsync((await JsonStorage.ReadAsync<CurrentSdk>(pointer, ct)).Version, ct);
            var payload = new SdkPayload(BundledMetadata(), BundledCapabilities(), BundledAmmoCapabilities(), BundledComposition()); var sdk = ReadPayload(payload);
            await SavePayloadAsync(sdk.Version, payload, ct);
            await JsonStorage.WriteAtomicAsync(pointer, new CurrentSdk(sdk.Version), ct);
            return sdk;
        }
        finally { gate.Release(); }
    }
    public async Task<SdkMetadata> GetVersionAsync(string version, CancellationToken ct = default)
    {
        try { return await ReadCachedAsync(version, ct); }
        catch (InvalidDataException e) when (e.Data[MissingFileKey] is string missing)
        {
            // An older GUI caches only the metadata files it knows, so a later GUI may need a file that release published but the cache lacks.
            // Complete it only from the byte-identical bundled release; otherwise the release must be reinstalled (InstallAsync merges).
            if (!await CompleteFromBundleAsync(version, ct)) throw new IncompleteSdkCacheException(version, missing);
            return await ReadCachedAsync(version, ct);
        }
    }
    /// <summary>Exception data key naming the required metadata file absent from an SDK payload.</summary>
    public const string MissingFileKey = "HD2RuntimeGUI.MissingSdkFile";
    private static InvalidDataException MissingFile(string file, string message) { var e = new InvalidDataException(message); e.Data[MissingFileKey] = file; return e; }
    private static Dictionary<string, byte[]> Files(SdkPayload payload)
    {
        var files = new Dictionary<string, byte[]> { ["metadata.json"] = payload.Metadata };
        if (payload.Capabilities != null) files.Add(PlayerWeaponCatalogReader.FileName, payload.Capabilities);
        if (payload.Ammo != null) files.Add(PlayerWeaponAmmoCatalogReader.FileName, payload.Ammo);
        if (payload.Composition != null) foreach (var (name, bytes) in payload.Composition) files.Add(name, bytes);
        return files;
    }
    private async Task<bool> CompleteFromBundleAsync(string version, CancellationToken ct)
    {
        var bundled = new SdkPayload(BundledMetadata(), BundledCapabilities(), BundledAmmoCapabilities(), BundledComposition());
        if (reader.Read(bundled.Metadata).Version != version) return false;
        try { await MergeMissingAsync(version, Files(bundled), ct); return true; }
        catch (InvalidDataException) { return false; }
    }
    // Adds files absent from a cached version. Every file already present must be byte-identical, so a different release is never mixed in.
    private async Task MergeMissingAsync(string version, Dictionary<string, byte[]> files, CancellationToken ct)
    {
        var missing = new List<string>();
        foreach (var (name, bytes) in files)
        {
            var file = paths.CachePath(version, name);
            if (!File.Exists(file)) missing.Add(name);
            else if (!(await File.ReadAllBytesAsync(file, ct)).AsSpan().SequenceEqual(bytes)) throw new InvalidDataException($"SDK {version} is already cached with different metadata ({name}).");
        }
        foreach (var name in missing)
        {
            var temp = paths.CachePath(version, name + "." + Guid.NewGuid().ToString("N") + ".tmp");
            try { await File.WriteAllBytesAsync(temp, files[name], ct); ct.ThrowIfCancellationRequested(); File.Move(temp, paths.CachePath(version, name)); }
            finally { if (File.Exists(temp)) File.Delete(temp); }
        }
    }
    private async Task<SdkMetadata> ReadCachedAsync(string version, CancellationToken ct)
    {
        var file = paths.SdkFile(version);
        if (!File.Exists(file)) throw new InvalidDataException($"SDK {version} is not cached. Restore its metadata before editing this project.");
        if (new FileInfo(file).Length > MetadataReader.MaxBytes) throw new InvalidDataException("SDK metadata too large.");
        var capabilityPath = paths.CachePath(version, PlayerWeaponCatalogReader.FileName);
        if (File.Exists(capabilityPath) && new FileInfo(capabilityPath).Length > PlayerWeaponCatalogReader.MaxBytes) throw new InvalidDataException("Capability catalog too large.");
        var ammoPath = paths.CachePath(version, PlayerWeaponAmmoCatalogReader.FileName);
        if (File.Exists(ammoPath) && new FileInfo(ammoPath).Length > PlayerWeaponAmmoCatalogReader.MaxBytes) throw new InvalidDataException("Ammo catalog too large.");
        var graphs = new Dictionary<string, byte[]>();
        foreach (var name in GraphFiles)
        {
            var graph = paths.CachePath(version, name);
            if (!File.Exists(graph)) continue;
            if (new FileInfo(graph).Length > GraphLimit(name)) throw new InvalidDataException("Composition graph too large.");
            graphs.Add(name, await File.ReadAllBytesAsync(graph, ct));
        }
        var sdk = ReadPayload(new(await File.ReadAllBytesAsync(file, ct), File.Exists(capabilityPath) ? await File.ReadAllBytesAsync(capabilityPath, ct) : null,
            File.Exists(ammoPath) ? await File.ReadAllBytesAsync(ammoPath, ct) : null, graphs));
        if (sdk.Version != version) throw new InvalidDataException("Cached SDK identity mismatch.");
        return sdk;
    }
    public Task<SdkMetadata> InstallAsync(SdkRelease release, CancellationToken ct = default) => ValidateReleaseAsync(release, true, ct);
    public Task<SdkMetadata> InspectAsync(SdkRelease release, CancellationToken ct = default) => ValidateReleaseAsync(release, false, ct);
    private async Task<SdkMetadata> ValidateReleaseAsync(SdkRelease release, bool install, CancellationToken ct)
    {
        GitHubReleaseClient.Validate(release);
        await gate.WaitAsync(ct);
        var staging = paths.CachePath("staging-" + Guid.NewGuid().ToString("N") + ".zip");
        try
        {
            Directory.CreateDirectory(paths.Sdk);
            if (!inspected.TryGetValue(release, out var metadata))
            {
                await github.DownloadAsync(release, staging, ct);
                if (new FileInfo(staging).Length != release.Size) throw new InvalidDataException("SDK asset size mismatch.");
                if (release.Sha256 != null)
                {
                    await using var file = File.OpenRead(staging);
                    var digest = Convert.ToHexString(await SHA256.HashDataAsync(file, ct));
                    if (!digest.Equals(release.Sha256[7..], StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("SDK SHA-256 mismatch.");
                }
                metadata = ReadArchivePayload(staging);
            }
            var sdk = ReadPayload(metadata);
            if (sdk.Version != release.Version) throw new InvalidDataException("Release and metadata versions do not match.");
            if (!install) { inspected.Clear(); inspected[release] = metadata; return sdk; }
            await SavePayloadAsync(sdk.Version, metadata, ct);
            await JsonStorage.WriteAtomicAsync(paths.CachePath("current.json"), new CurrentSdk(sdk.Version), ct);
            return sdk;
        }
        finally { if (File.Exists(staging)) File.Delete(staging); gate.Release(); }
    }
    private async Task SavePayloadAsync(string version, SdkPayload payload, CancellationToken ct)
    {
        var destination = Path.GetDirectoryName(paths.SdkFile(version))!;
        var stage = paths.CachePath("staging-" + Guid.NewGuid().ToString("N"));
        var files = Files(payload);
        if (Directory.Exists(destination)) { await MergeMissingAsync(version, files, ct); return; }
        try
        {
            Directory.CreateDirectory(stage);
            foreach (var (name, bytes) in files) await File.WriteAllBytesAsync(Path.Combine(stage, name), bytes, ct);
            ct.ThrowIfCancellationRequested();
            Directory.Move(stage, destination);
        }
        finally { if (Directory.Exists(stage)) Directory.Delete(stage, true); }
    }
    public static byte[] ReadArchiveMetadata(string path) => ReadArchivePayload(path).Metadata;
    private static SdkPayload ReadArchivePayload(string path)
    {
        using var archive = ZipFile.OpenRead(path);
        if (archive.Entries.Count > 2000) throw new InvalidDataException("Too many SDK archive entries.");
        long total = 0; var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in archive.Entries)
        {
            ValidateEntryPath(entry.FullName);
            if (!names.Add(entry.FullName)) throw new InvalidDataException("Duplicate ZIP entry.");
            total = checked(total + entry.Length);
            if (total > 64 * 1024 * 1024 || entry.Length > 16 * 1024 * 1024) throw new InvalidDataException("SDK expanded size limit exceeded.");
            if (((entry.ExternalAttributes >> 16) & 0xF000) == 0xA000) throw new InvalidDataException("SDK symbolic links are not permitted.");
        }
        var metadata = archive.GetEntry("metadata.json") ?? throw new InvalidDataException("SDK must contain root metadata.json.");
        if (metadata.Length > MetadataReader.MaxBytes) throw new InvalidDataException("Metadata is too large.");
        // Never extract scripts or any path supplied by the archive. Only metadata is consumed.
        byte[] Read(ZipArchiveEntry entry, int limit)
        {
            if (entry.Length > limit) throw new InvalidDataException("Metadata is too large.");
            using var input = entry.Open(); using var buffer = new MemoryStream();
            var chunk = new byte[8192]; int read;
            while ((read = input.Read(chunk)) > 0) { if (buffer.Length + read > limit) throw new InvalidDataException("Metadata is too large."); buffer.Write(chunk, 0, read); }
            return buffer.ToArray();
        }
        var catalog = archive.GetEntry(PlayerWeaponCatalogReader.FileName);
        var ammo = archive.GetEntry(PlayerWeaponAmmoCatalogReader.FileName);
        return new(Read(metadata, MetadataReader.MaxBytes), catalog == null ? null : Read(catalog, PlayerWeaponCatalogReader.MaxBytes),
            ammo == null ? null : Read(ammo, PlayerWeaponAmmoCatalogReader.MaxBytes),
            GraphFiles.Where(n => archive.GetEntry(n) != null).ToDictionary(n => n, n => Read(archive.GetEntry(n)!, GraphLimit(n))));
    }
    // Each capability file is bounded by its own reader limit; the canonical catalogs are larger than composition graphs.
    private static int GraphLimit(string name) => name switch
    {
        SupportAuthoringReader.FileName => SupportAuthoringReader.MaxBytes,
        StratagemCatalogReader.FileName => StratagemCatalogReader.MaxBytes,
        EntityAuthoringReader.VehicleFile or EntityAuthoringReader.BackpackFile => EntityAuthoringReader.MaxBytes,
        MagazineAttachmentReader.FileName => MagazineAttachmentReader.MaxBytes,
        _ => PlayerWeaponCompositionReader.MaxBytes,
    };
    public static void ValidateEntryPath(string name)
    {
        if (string.IsNullOrEmpty(name) || name.Length > 240 || name.Contains('\\') || name.StartsWith('/') || name.Contains(':') || name.Any(char.IsControl))
            throw new InvalidDataException("Unsafe SDK ZIP path.");
        foreach (var part in name.TrimEnd('/').Split('/'))
            if (part is "" or "." or ".." || part.EndsWith('.') || part.EndsWith(' ') || part.IndexOfAny(['<', '>', '"', '|', '?', '*']) >= 0)
                throw new InvalidDataException("Unsafe SDK ZIP path.");
    }
}
