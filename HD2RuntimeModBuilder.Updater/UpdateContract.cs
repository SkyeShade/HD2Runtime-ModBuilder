using System.Text.Json;
using System.Text.Json.Serialization;

namespace HD2RuntimeModBuilder.Updater;

// Contract shared by HD2Runtime ModBuilder (compiled into HD2RuntimeGUI.Core as a linked file) and the standalone updater.
// A release package is a flat ZIP of the application directory plus modbuilder-files.json, the inventory of every other file.
public static class UpdateContract
{
    public const string Product = "HD2Runtime ModBuilder";
    public const string EntryPoint = "HD2RuntimeGUI.exe";
    public const string UpdaterExe = "HD2RuntimeModBuilder.Updater.exe";
    public const string InventoryFile = "modbuilder-files.json";
    public const string ResultFile = "app-update-result.json";
    public const string BackupDirectory = ".modbuilder-update-backup";
    public const int MaxFiles = 5000;

    /// <summary>Canonical package-relative path ("wwwroot/app.css"), or null if the path is absolute, escapes, or is otherwise unsafe.</summary>
    public static string? SafeRelativePath(string? path)
    {
        if (string.IsNullOrEmpty(path) || path.Length > 260) return null;
        var normalized = path.Replace('\\', '/');
        if (normalized.StartsWith('/') || normalized.Contains(':') || normalized.EndsWith('/')) return null;
        foreach (var segment in normalized.Split('/'))
            if (segment is "" or "." or ".." || segment.EndsWith('.') || segment.EndsWith(' ') || segment.Any(c => char.IsControl(c) || c is '<' or '>' or '"' or '|' or '?' or '*')) return null;
        if (normalized.StartsWith(BackupDirectory + "/", StringComparison.OrdinalIgnoreCase) || normalized.Equals(BackupDirectory, StringComparison.OrdinalIgnoreCase)) return null;
        return normalized;
    }

    /// <summary>Full path of a package-relative path inside <paramref name="root"/>; throws if it would leave the root.</summary>
    public static string Resolve(string root, string relative)
    {
        var safe = SafeRelativePath(relative) ?? throw new InvalidDataException($"Unsafe package path: {relative}");
        var full = Path.GetFullPath(Path.Combine(root, safe.Replace('/', Path.DirectorySeparatorChar)));
        var prefix = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)) + Path.DirectorySeparatorChar;
        if (!full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException($"Package path escapes its directory: {relative}");
        return full;
    }

    /// <summary>Checks an inventory's shape: product, version, unique safe paths, sizes, SHA-256 digests, and the required executables.</summary>
    public static void Validate(Inventory inventory, string expectedVersion)
    {
        if (inventory.Format != 1 || inventory.Product != Product || inventory.Version != expectedVersion) throw new InvalidDataException("The update inventory does not describe the expected HD2Runtime ModBuilder version.");
        if (inventory.Files is not { Length: > 0 and <= MaxFiles }) throw new InvalidDataException("The update inventory has no files or too many files.");
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in inventory.Files)
        {
            if (SafeRelativePath(file.Path) != file.Path || file.Path.Equals(InventoryFile, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException($"Unsafe path in the update inventory: {file.Path}");
            if (!seen.Add(file.Path)) throw new InvalidDataException($"Duplicate path in the update inventory: {file.Path}");
            if (file.Size < 0 || file.Sha256.Length != 64 || !file.Sha256.All(Uri.IsHexDigit)) throw new InvalidDataException($"Invalid size or digest in the update inventory: {file.Path}");
        }
        if (!seen.Contains(EntryPoint) || !seen.Contains(UpdaterExe)) throw new InvalidDataException("The update package is missing HD2RuntimeGUI.exe or the updater.");
    }

    public static IReadOnlyList<string> Arguments(UpdaterOptions o) =>
        ["--pid", o.ProcessId.ToString(), "--started", o.ProcessStartTicks.ToString(), "--install", o.InstallDirectory, "--staged", o.StagedDirectory, "--version", o.Version, "--result", o.ResultPath];

    public static UpdaterOptions Parse(IReadOnlyList<string> args)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var i = 0; i + 1 < args.Count; i += 2)
        {
            if (!args[i].StartsWith("--", StringComparison.Ordinal) || !values.TryAdd(args[i], args[i + 1])) throw new ArgumentException($"Unexpected updater argument: {args[i]}");
        }
        if (args.Count % 2 != 0) throw new ArgumentException("Updater arguments must be name/value pairs.");
        string Get(string name) => values.TryGetValue(name, out var v) && v.Length > 0 ? v : throw new ArgumentException($"Missing updater argument {name}.");
        string FullDirectory(string name) => Path.IsPathFullyQualified(Get(name)) ? Path.TrimEndingDirectorySeparator(Path.GetFullPath(Get(name))) : throw new ArgumentException($"{name} must be a full path.");
        return new(int.Parse(Get("--pid")), long.Parse(Get("--started")), FullDirectory("--install"), FullDirectory("--staged"), Get("--version"),
            Path.IsPathFullyQualified(Get("--result")) ? Path.GetFullPath(Get("--result")) : throw new ArgumentException("--result must be a full path."));
    }
}

public sealed record UpdaterOptions(int ProcessId, long ProcessStartTicks, string InstallDirectory, string StagedDirectory, string Version, string ResultPath);
public sealed record InventoryEntry(string Path, long Size, string Sha256);
public sealed record Inventory(int Format, string Product, string Version, InventoryEntry[] Files);
public sealed record UpdateResult(string Version, bool Success, string Message, DateTimeOffset CompletedAt);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, WriteIndented = true, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow)]
[JsonSerializable(typeof(Inventory))]
[JsonSerializable(typeof(UpdateResult))]
public sealed partial class UpdateJson : JsonSerializerContext
{
    public static Inventory ReadInventory(string path)
    {
        if (new FileInfo(path).Length > 4 * 1024 * 1024) throw new InvalidDataException("The update inventory is too large.");
        return JsonSerializer.Deserialize(File.ReadAllBytes(path), Default.Inventory) ?? throw new InvalidDataException("The update inventory is empty.");
    }
    public static UpdateResult ReadResult(string path)
    {
        if (new FileInfo(path).Length > 64 * 1024) throw new InvalidDataException("The update result is too large.");
        return JsonSerializer.Deserialize(File.ReadAllBytes(path), Default.UpdateResult) ?? throw new InvalidDataException("The update result is empty.");
    }
    public static void WriteResult(string path, UpdateResult result)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + ".tmp";
        File.WriteAllBytes(temp, JsonSerializer.SerializeToUtf8Bytes(result, Default.UpdateResult));
        File.Move(temp, path, true);
    }
}
