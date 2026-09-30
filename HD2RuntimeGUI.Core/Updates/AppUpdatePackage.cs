using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using HD2RuntimeGUI.Core.Localization;
using HD2RuntimeModBuilder.Updater;

namespace HD2RuntimeGUI.Core.Updates;

/// <summary>
/// modbuilder-update-v2.json, published next to the release ZIP by scripts/publish-windows.ps1:
/// { "format": 2, "product": "HD2Runtime ModBuilder", "version": "1.3.1", "tag": "v1.3.1",
///   "asset": "HD2Runtime-ModBuilder-v1.3.1-win-x64.zip", "size": 91234567, "sha256": "&lt;64 hex&gt;",
///   "entrypoint": "HD2RuntimeModBuilder.exe", "updater": "HD2RuntimeModBuilder.Updater.exe", "commit": "&lt;40 hex&gt;" }
/// Paths are fixed by the contract; the manifest cannot name other files. (modbuilder-update.json, format 1 with
/// entrypoint HD2RuntimeGUI.exe, has the same fields and exists only for HD2Runtime ModBuilder 1.0.0.)
/// </summary>
public sealed record AppUpdateManifest(int Format, string Product, string Version, string Tag, string Asset, long Size, string Sha256, string Entrypoint, string Updater, string Commit)
{
    private static readonly JsonSerializerOptions Options = new()
    { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow, MaxDepth = 8 };

    public static AppUpdateManifest Parse(byte[] json, AppRelease release)
    {
        AppUpdateManifest? manifest;
        try { manifest = JsonSerializer.Deserialize<AppUpdateManifest>(json, Options); }
        catch (JsonException e) { throw new InvalidDataException(CoreText.Get("Messages.Update.ManifestMalformed"), e); }
        if (manifest is null || manifest.Format != Format2 || manifest.Product != UpdateContract.Product || manifest.Version != release.Version || manifest.Tag != release.Tag
            || manifest.Asset != release.Package.Name || manifest.Size != release.Package.Size || manifest.Entrypoint != UpdateContract.EntryPoint || manifest.Updater != UpdateContract.UpdaterExe
            || !IsHex(manifest.Sha256, 64) || !IsHex(manifest.Commit, 40))
            throw new InvalidDataException(CoreText.Get("Messages.Update.ManifestMismatch"));
        // GitHub's own asset digest, when present, must agree with the published manifest.
        if (release.Package.Digest != null && release.Package.Digest != "sha256:" + manifest.Sha256) throw new InvalidDataException(CoreText.Get("Messages.Update.DigestMismatch"));
        return manifest;
    }

    public const int Format2 = 2;

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
            throw new InvalidDataException(CoreText.Get("Messages.Update.HashMismatch"));
        if (Directory.Exists(stagedDirectory) && Directory.EnumerateFileSystemEntries(stagedDirectory).Any()) throw new InvalidOperationException(CoreText.Get("Messages.Update.StagingNotEmpty"));
        Directory.CreateDirectory(stagedDirectory);

        using var zip = ZipFile.OpenRead(zipPath);
        if (zip.Entries.Count > UpdateContract.MaxFiles + 1) throw new InvalidDataException(CoreText.Get("Messages.Update.TooManyEntries"));
        var entries = new Dictionary<string, ZipArchiveEntry>(StringComparer.OrdinalIgnoreCase); long expanded = 0;
        foreach (var entry in zip.Entries)
        {
            if (entry.FullName.EndsWith('/') && entry.Length == 0) continue; // directory record
            var path = UpdateContract.SafeRelativePath(entry.FullName);
            if (path == null || path != entry.FullName.Replace('\\', '/')) throw new InvalidDataException(CoreText.Format("Messages.Update.UnsafePath", entry.FullName));
            if (((entry.ExternalAttributes >> 16) & 0xF000) == 0xA000) throw new InvalidDataException(CoreText.Format("Messages.Update.LinkEntry", entry.FullName));
            if (!entries.TryAdd(path, entry)) throw new InvalidDataException(CoreText.Format("Messages.Update.DuplicatePath", entry.FullName));
            if ((expanded += entry.Length) > MaxExpandedBytes) throw new InvalidDataException(CoreText.Get("Messages.Update.ExpandedTooLarge"));
        }
        if (!entries.TryGetValue(UpdateContract.InventoryFile, out var inventoryEntry) || inventoryEntry.Length > 4 * 1024 * 1024) throw new InvalidDataException(CoreText.Get("Messages.Update.NoInventory"));
        var inventoryPath = UpdateContract.Resolve(stagedDirectory, UpdateContract.InventoryFile);
        await ExtractAsync(inventoryEntry, inventoryPath, inventoryEntry.Length, ct);
        var inventory = UpdateJson.ReadInventory(inventoryPath);
        UpdateContract.Validate(inventory, manifest.Version);
        if (entries.Count != inventory.Files.Length + 1 || inventory.Files.Any(f => !entries.ContainsKey(f.Path)))
            throw new InvalidDataException(CoreText.Get("Messages.Update.InventoryMismatch"));

        foreach (var file in inventory.Files)
        {
            var entry = entries[file.Path];
            if (entry.Length != file.Size) throw new InvalidDataException(CoreText.Format("Messages.Update.FileSizeMismatch", file.Path));
            var target = UpdateContract.Resolve(stagedDirectory, file.Path);
            if (await ExtractAsync(entry, target, file.Size, ct) != file.Sha256) throw new InvalidDataException(CoreText.Format("Messages.Update.FileHashMismatch", file.Path));
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
            if ((total += read) > size) throw new InvalidDataException(CoreText.Format("Messages.Update.EntryTooLarge", entry.FullName));
            hash.AppendData(buffer, 0, read);
            await output.WriteAsync(buffer.AsMemory(0, read), ct);
        }
        if (total != size) throw new InvalidDataException(CoreText.Format("Messages.Update.EntryTruncated", entry.FullName));
        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }
}
