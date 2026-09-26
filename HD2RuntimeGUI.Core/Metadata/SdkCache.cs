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
    private readonly SemaphoreSlim gate = new(1);
    private readonly Dictionary<SdkRelease, byte[]> inspected = new();
    private sealed record CurrentSdk(string Version);
    public static byte[] BundledMetadata()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("HD2RuntimeGUI.Core.Metadata.Bundled.metadata.json")!;
        using var buffer = new MemoryStream(); stream.CopyTo(buffer); return buffer.ToArray();
    }
    public async Task<SdkMetadata> GetCurrentAsync(CancellationToken ct = default)
    {
        await gate.WaitAsync(ct);
        try
        {
            var pointer = paths.CachePath("current.json");
            if (File.Exists(pointer)) return await GetVersionAsync((await JsonStorage.ReadAsync<CurrentSdk>(pointer, ct)).Version, ct);
            var bundled = BundledMetadata(); var sdk = reader.Read(bundled);
            await JsonStorage.WriteAtomicBytesAsync(paths.SdkFile(sdk.Version), bundled, ct);
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
        var sdk = reader.Read(await File.ReadAllBytesAsync(file, ct));
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
                metadata = ReadArchiveMetadata(staging);
            }
            var sdk = reader.Read(metadata);
            if (sdk.Version != release.Version) throw new InvalidDataException("Release and metadata versions do not match.");
            if (!install) { inspected.Clear(); inspected[release] = metadata; return sdk; }
            var destination = paths.SdkFile(sdk.Version);
            // Versioned metadata is immutable: projects remain pinned to exactly what they were created with.
            if (File.Exists(destination) && !(await File.ReadAllBytesAsync(destination, ct)).AsSpan().SequenceEqual(metadata))
                throw new InvalidDataException("This SDK version is already cached with different metadata.");
            if (!File.Exists(destination)) await JsonStorage.WriteAtomicBytesAsync(destination, metadata, ct);
            await JsonStorage.WriteAtomicAsync(paths.CachePath("current.json"), new CurrentSdk(sdk.Version), ct);
            return sdk;
        }
        finally { if (File.Exists(staging)) File.Delete(staging); gate.Release(); }
    }
    public static byte[] ReadArchiveMetadata(string path)
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
        using var input = metadata.Open(); using var buffer = new MemoryStream();
        var chunk = new byte[8192]; int read;
        while ((read = input.Read(chunk)) > 0) { if (buffer.Length + read > MetadataReader.MaxBytes) throw new InvalidDataException("Metadata is too large."); buffer.Write(chunk, 0, read); }
        return buffer.ToArray();
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
