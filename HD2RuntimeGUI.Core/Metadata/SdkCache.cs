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

public sealed class SdkCache(AppPaths paths, IMetadataReader reader, IGitHubReleaseClient github) : ISdkCache
{
    private readonly IPlayerWeaponCatalogReader catalogReader = new PlayerWeaponCatalogReader();
    private readonly IPlayerWeaponAmmoCatalogReader ammoReader = new PlayerWeaponAmmoCatalogReader();
    public SdkCache(AppPaths paths, IMetadataReader reader, IGitHubReleaseClient github, IPlayerWeaponCatalogReader catalogReader) : this(paths, reader, github) => this.catalogReader = catalogReader;
    public SdkCache(AppPaths paths, IMetadataReader reader, IGitHubReleaseClient github, IPlayerWeaponCatalogReader catalogReader, IPlayerWeaponAmmoCatalogReader ammoReader)
        : this(paths, reader, github, catalogReader) => this.ammoReader = ammoReader;
    private readonly SemaphoreSlim gate = new(1);
    private readonly Dictionary<SdkRelease, SdkPayload> inspected = new();
    private sealed record SdkPayload(byte[] Metadata, byte[]? Capabilities, byte[]? Ammo);
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
    private SdkMetadata ReadPayload(SdkPayload payload)
    {
        var sdk = reader.Read(payload.Metadata);
        if (Models.SemVersion.Parse(sdk.Version).CompareTo(Models.SemVersion.Parse("0.13.0")) >= 0)
            sdk = sdk with { PlayerWeapons = catalogReader.Read(payload.Capabilities ?? throw new InvalidDataException("SDK is missing its player-weapon capability catalog."), sdk.Version) };
        if (Models.SemVersion.Parse(sdk.Version).CompareTo(Models.SemVersion.Parse("0.14.0")) >= 0)
            sdk = sdk with { PlayerAmmo = ammoReader.Read(payload.Ammo ?? throw new InvalidDataException("SDK is missing its player-weapon ammo capability catalog."), sdk.PlayerWeapons!) };
        return sdk;
    }
    public async Task<SdkMetadata> GetCurrentAsync(CancellationToken ct = default)
    {
        await gate.WaitAsync(ct);
        try
        {
            var pointer = paths.CachePath("current.json");
            if (File.Exists(pointer)) return await GetVersionAsync((await JsonStorage.ReadAsync<CurrentSdk>(pointer, ct)).Version, ct);
            var payload = new SdkPayload(BundledMetadata(), BundledCapabilities(), BundledAmmoCapabilities()); var sdk = ReadPayload(payload);
            await SavePayloadAsync(sdk.Version, payload, ct);
            await JsonStorage.WriteAtomicAsync(pointer, new CurrentSdk(sdk.Version), ct);
            return sdk;
        }
        finally { gate.Release(); }
    }
    public async Task<SdkMetadata> GetVersionAsync(string version, CancellationToken ct = default)
    {
        var file = paths.SdkFile(version);
        if (!File.Exists(file)) throw new InvalidDataException($"SDK {version} is not cached. Restore its metadata before editing this project.");
        if (new FileInfo(file).Length > MetadataReader.MaxBytes) throw new InvalidDataException("SDK metadata too large.");
        var capabilityPath = paths.CachePath(version, PlayerWeaponCatalogReader.FileName);
        if (File.Exists(capabilityPath) && new FileInfo(capabilityPath).Length > PlayerWeaponCatalogReader.MaxBytes) throw new InvalidDataException("Capability catalog too large.");
        var ammoPath = paths.CachePath(version, PlayerWeaponAmmoCatalogReader.FileName);
        if (File.Exists(ammoPath) && new FileInfo(ammoPath).Length > PlayerWeaponAmmoCatalogReader.MaxBytes) throw new InvalidDataException("Ammo catalog too large.");
        var sdk = ReadPayload(new(await File.ReadAllBytesAsync(file, ct), File.Exists(capabilityPath) ? await File.ReadAllBytesAsync(capabilityPath, ct) : null,
            File.Exists(ammoPath) ? await File.ReadAllBytesAsync(ammoPath, ct) : null));
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
        var files = new Dictionary<string, byte[]> { ["metadata.json"] = payload.Metadata };
        if (payload.Capabilities != null) files.Add(PlayerWeaponCatalogReader.FileName, payload.Capabilities);
        if (payload.Ammo != null) files.Add(PlayerWeaponAmmoCatalogReader.FileName, payload.Ammo);
        if (Directory.Exists(destination))
        {
            foreach (var (name, bytes) in files)
            {
                var file = paths.CachePath(version, name);
                if (!File.Exists(file) || !(await File.ReadAllBytesAsync(file, ct)).AsSpan().SequenceEqual(bytes)) throw new InvalidDataException("This SDK version is already cached with different or incomplete metadata.");
            }
            return;
        }
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
            ammo == null ? null : Read(ammo, PlayerWeaponAmmoCatalogReader.MaxBytes));
    }
    public static void ValidateEntryPath(string name)
    {
        if (string.IsNullOrEmpty(name) || name.Length > 240 || name.Contains('\\') || name.StartsWith('/') || name.Contains(':') || name.Any(char.IsControl))
            throw new InvalidDataException("Unsafe SDK ZIP path.");
        foreach (var part in name.TrimEnd('/').Split('/'))
            if (part is "" or "." or ".." || part.EndsWith('.') || part.EndsWith(' ') || part.IndexOfAny(['<', '>', '"', '|', '?', '*']) >= 0)
                throw new InvalidDataException("Unsafe SDK ZIP path.");
    }
}
