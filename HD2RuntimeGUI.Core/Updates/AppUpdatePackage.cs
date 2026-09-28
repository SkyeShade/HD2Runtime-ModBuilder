using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using HD2RuntimeModBuilder.Updater;

namespace HD2RuntimeGUI.Core.Updates;

/// <summary>
/// modbuilder-update.json, published next to the release ZIP by scripts/publish-windows.ps1:
/// { "format": 1, "product": "HD2Runtime ModBuilder", "version": "1.0.0", "tag": "v1.0.0",
///   "asset": "HD2Runtime-ModBuilder-v1.0.0-win-x64.zip", "size": 91234567, "sha256": "&lt;64 hex&gt;",
///   "entrypoint": "HD2RuntimeGUI.exe", "updater": "HD2RuntimeModBuilder.Updater.exe", "commit": "&lt;40 hex&gt;" }
/// Paths are fixed by the contract; the manifest cannot name other files.
/// </summary>
public sealed record AppUpdateManifest(int Format, string Product, string Version, string Tag, string Asset, long Size, string Sha256, string Entrypoint, string Updater, string Commit)
{
    private static readonly JsonSerializerOptions Options = new()
    { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow, MaxDepth = 8 };

    public static AppUpdateManifest Parse(byte[] json, AppRelease release)
    {
        AppUpdateManifest? manifest;
        try { manifest = JsonSerializer.Deserialize<AppUpdateManifest>(json, Options); }
        catch (JsonException e) { throw new InvalidDataException("The update manifest is malformed.", e); }
        if (manifest is null || manifest.Format != 1 || manifest.Product != UpdateContract.Product || manifest.Version != release.Version || manifest.Tag != release.Tag
            || manifest.Asset != release.Package.Name || manifest.Size != release.Package.Size || manifest.Entrypoint != UpdateContract.EntryPoint || manifest.Updater != UpdateContract.UpdaterExe
            || !IsHex(manifest.Sha256, 64) || !IsHex(manifest.Commit, 40))
            throw new InvalidDataException("The update manifest does not match the release.");
        // GitHub's own asset digest, when present, must agree with the published manifest.
        if (release.Package.Digest != null && release.Package.Digest != "sha256:" + manifest.Sha256) throw new InvalidDataException("The release package digest does not match the update manifest.");
        return manifest;
    }

    private static bool IsHex(string? value, int length) => value?.Length == length && value.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');
}

public static class AppUpdatePackage
{
    public const long MaxExpandedBytes = 2L * 1024 * 1024 * 1024;

    public static async Task<string> Sha256Async(string path, CancellationToken ct = default)
    {
        await using var stream = File.OpenRead(path);
        return Convert.ToHexStringLower(await SHA256.HashDataAsync(stream, ct));
    }

    /// <summary>Verifies the downloaded ZIP against the manifest, then extracts it into an empty staging directory. Every entry
    /// must be a safe relative path listed in the package inventory with the recorded size and SHA-256.</summary>
    public static async Task<Inventory> StageAsync(string zipPath, AppUpdateManifest manifest, string stagedDirectory, CancellationToken ct = default)
    {
        if (new FileInfo(zipPath).Length != manifest.Size || await Sha256Async(zipPath, ct) != manifest.Sha256)
            throw new InvalidDataException("The downloaded update does not match its published SHA-256.");
        if (Directory.Exists(stagedDirectory) && Directory.EnumerateFileSystemEntries(stagedDirectory).Any()) throw new InvalidOperationException("The staging directory is not empty.");
        Directory.CreateDirectory(stagedDirectory);

        using var zip = ZipFile.OpenRead(zipPath);
        if (zip.Entries.Count > UpdateContract.MaxFiles + 1) throw new InvalidDataException("The update package has too many entries.");
        var entries = new Dictionary<string, ZipArchiveEntry>(StringComparer.OrdinalIgnoreCase); long expanded = 0;
        foreach (var entry in zip.Entries)
        {
            if (entry.FullName.EndsWith('/') && entry.Length == 0) continue; // directory record
            var path = UpdateContract.SafeRelativePath(entry.FullName);
            if (path == null || path != entry.FullName.Replace('\\', '/')) throw new InvalidDataException($"The update package contains an unsafe path: {entry.FullName}");
            if (((entry.ExternalAttributes >> 16) & 0xF000) == 0xA000) throw new InvalidDataException($"The update package contains a link: {entry.FullName}");
            if (!entries.TryAdd(path, entry)) throw new InvalidDataException($"The update package contains a duplicate path: {entry.FullName}");
            if ((expanded += entry.Length) > MaxExpandedBytes) throw new InvalidDataException("The update package expands beyond the size limit.");
        }
        if (!entries.TryGetValue(UpdateContract.InventoryFile, out var inventoryEntry) || inventoryEntry.Length > 4 * 1024 * 1024) throw new InvalidDataException("The update package has no file inventory.");
        var inventoryPath = UpdateContract.Resolve(stagedDirectory, UpdateContract.InventoryFile);
        await ExtractAsync(inventoryEntry, inventoryPath, inventoryEntry.Length, ct);
        var inventory = UpdateJson.ReadInventory(inventoryPath);
        UpdateContract.Validate(inventory, manifest.Version);
        if (entries.Count != inventory.Files.Length + 1 || inventory.Files.Any(f => !entries.ContainsKey(f.Path)))
            throw new InvalidDataException("The update package contents do not match its inventory.");

        foreach (var file in inventory.Files)
        {
            var entry = entries[file.Path];
            if (entry.Length != file.Size) throw new InvalidDataException($"Unexpected size for {file.Path}.");
            var target = UpdateContract.Resolve(stagedDirectory, file.Path);
            if (await ExtractAsync(entry, target, file.Size, ct) != file.Sha256) throw new InvalidDataException($"SHA-256 mismatch for {file.Path}.");
        }
        return inventory;
    }

    private static async Task<string> ExtractAsync(ZipArchiveEntry entry, string target, long size, CancellationToken ct)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        await using var input = entry.Open();
        await using var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        var buffer = new byte[81920]; long total = 0; int read;
        while ((read = await input.ReadAsync(buffer, ct)) != 0)
        {
            if ((total += read) > size) throw new InvalidDataException($"{entry.FullName} expands beyond its recorded size.");
            hash.AppendData(buffer, 0, read);
            await output.WriteAsync(buffer.AsMemory(0, read), ct);
        }
        if (total != size) throw new InvalidDataException($"{entry.FullName} is truncated.");
        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }
}
