using HD2RuntimeGUI.Core.Localization;
using HD2RuntimeGUI.Core.Models;

namespace HD2RuntimeGUI.Core.Generation;

/// <summary>Additional files the user packages into every exported mod ZIP (ModProject.PackagedFiles): a file on their computer, read
/// at export, and its path in the ZIP. ModBuilder never edits, converts or names them; it only refuses an export it cannot package as
/// asked (a missing or unreadable source, an unsafe ZIP path, a path that clashes with a generated file or another additional file).</summary>
public static class PackagedFiles
{
    public const int MaxFiles = 32;
    public const long MaxBytes = 64L * 1024 * 1024;
    // The files ModBuilder generates. mod/ is reserved as a whole: the mod manager installs that folder into the game, and it holds only
    // the generated archive. At export the clash check runs against ModExporter's actual entries; this list lets the editor report early.
    public const string ReservedFolder = "mod";
    public static readonly IReadOnlyList<string> Generated = ["README.md", "build-report.json", "hd2runtime.json", "manifest.json", "src/addon.lua",
        $"mod/{GameplayArchive.ArchiveName}", $"mod/{GameplayArchive.ArchiveName}.gpu_resources", $"mod/{GameplayArchive.ArchiveName}.stream"];
    private static readonly HashSet<string> DeviceNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    };

    /// <summary>A destination as typed: surrounding spaces removed, Windows separators turned into '/'.</summary>
    public static string Normalize(string destination) => destination.Trim().Replace('\\', '/');
    /// <summary>The default ZIP path for a newly added source: its file name at the ZIP root.</summary>
    public static string DefaultDestination(string source) => Path.GetFileName(source);
    public static string Name(PackagedFile file) => Path.GetFileName(file.Source) is { Length: > 0 } name ? name : file.Destination;

    /// <summary>Why a destination is not a safe relative ZIP path for an additional file, or null when it is.</summary>
    public static string? DestinationIssue(string destination)
    {
        if (string.IsNullOrWhiteSpace(destination)) return CoreText.Get("PackagedFiles.Destination.Empty");
        string Invalid(string reason) => CoreText.Format("PackagedFiles.Destination.Invalid", destination) + "\n" + CoreText.Get(reason);
        if (destination.StartsWith('/') || destination.StartsWith('\\') || destination.Contains(':') || Path.IsPathRooted(destination))
            return Invalid("PackagedFiles.Destination.Absolute");
        if (destination.Contains('\\')) return Invalid("PackagedFiles.Destination.Backslash");
        if (destination.EndsWith('/')) return Invalid("PackagedFiles.Destination.Folder");
        if (destination.Length > 240) return Invalid("PackagedFiles.Destination.Long");
        var parts = destination.Split('/');
        if (parts.Any(p => p is "." or "..")) return Invalid("PackagedFiles.Destination.Parent");
        if (parts.Any(p => p.Length == 0 || p.EndsWith('.') || p.EndsWith(' ') || p.Any(char.IsControl) || p.IndexOfAny(['<', '>', '"', '|', '?', '*']) >= 0))
            return Invalid("PackagedFiles.Destination.Name");
        if (parts.Any(p => DeviceNames.Contains(p.Split('.')[0].TrimEnd()))) return Invalid("PackagedFiles.Destination.Device");
        if (parts.Length > 1 && parts[0].Equals(ReservedFolder, StringComparison.OrdinalIgnoreCase)) return Invalid("PackagedFiles.Destination.Reserved");
        return null;
    }

    /// <summary>Why a source cannot be read at export, from what can be checked without reading it (null when it looks readable).</summary>
    public static string? SourceIssue(string source)
    {
        if (string.IsNullOrWhiteSpace(source) || !Path.IsPathFullyQualified(source)) return CoreText.Format("PackagedFiles.Source.NotFullPath", source);
        if (!File.Exists(source)) return CoreText.Format("PackagedFiles.Source.Missing", source);
        return null;
    }

    /// <summary>The first problem of each file in the list (null when it can be packaged): its source, its destination, then clashes
    /// with the generated files and with the files before it (paths compare without case, as Windows extracts them). The Arsenal icon
    /// (arsenalIcon) is one of the generated files; a clash with it says so.</summary>
    public static IReadOnlyList<string?> Issues(IReadOnlyList<PackagedFile> files, IEnumerable<string> generated, bool checkSources = true, string? arsenalIcon = null)
    {
        var owned = generated.ToList();
        var issues = new string?[files.Count];
        for (var i = 0; i < files.Count; i++)
            issues[i] = (checkSources ? SourceIssue(files[i].Source) : null) ?? DestinationIssue(files[i].Destination)
                ?? Clash(files[i].Destination, owned, "PackagedFiles.Destination.Generated", arsenalIcon)
                ?? Clash(files[i].Destination, files.Take(i).Select(f => f.Destination), "PackagedFiles.Destination.Duplicate");
        return issues;
    }
    private static string? Clash(string destination, IEnumerable<string> others, string sameKey, string? arsenalIcon = null)
    {
        string Invalid(string key, string other) => CoreText.Format("PackagedFiles.Destination.Invalid", destination) + "\n" + CoreText.Format(key, other);
        foreach (var other in others)
        {
            if (other.Equals(destination, StringComparison.OrdinalIgnoreCase))
                return Invalid(other.Equals(arsenalIcon, StringComparison.OrdinalIgnoreCase) ? "PackagedFiles.Destination.ArsenalIcon" : sameKey, other);
            // One path used as a file and as a folder (README.md and README.md/a.png): the ZIP cannot be extracted.
            if (other.StartsWith(destination + "/", StringComparison.OrdinalIgnoreCase)) return Invalid("PackagedFiles.Destination.FileAndFolder", destination);
            if (destination.StartsWith(other + "/", StringComparison.OrdinalIgnoreCase)) return Invalid("PackagedFiles.Destination.FileAndFolder", other);
        }
        return null;
    }

    /// <summary>Adds the project's additional files to the generated ZIP entries, reading each source as it is now. Refuses the export,
    /// naming the file, when any of them cannot be packaged; nothing is skipped and no generated entry is replaced.</summary>
    public static void AddTo(ModProject project, IDictionary<string, byte[]> entries)
    {
        var files = project.PackagedFiles ?? [];
        if (files.Count > MaxFiles) throw new InvalidDataException(CoreText.Format("PackagedFiles.TooMany", MaxFiles));
        var issues = Issues(files, entries.Keys, arsenalIcon: Arsenal.IconEntry(project));
        for (var i = 0; i < files.Count; i++)
        {
            if (issues[i] is { } issue) throw Refused(files[i], issue);
            var file = files[i];
            entries.Add(file.Destination, ReadSource(file.Source, reason => Refused(file, reason)));
        }
    }
    /// <summary>A source file's bytes exactly as they are now (additional files and the Arsenal icon). A missing, unreadable or too
    /// large file is refused with the reason, through refused.</summary>
    public static byte[] ReadSource(string source, Func<string, InvalidDataException> refused)
    {
        try
        {
            using var stream = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (stream.Length > MaxBytes) throw refused(CoreText.Format("PackagedFiles.Source.TooLarge", source, MaxBytes / (1024 * 1024)));
            var bytes = new byte[stream.Length];
            stream.ReadExactly(bytes);
            if (stream.ReadByte() != -1) throw new IOException(CoreText.Get("PackagedFiles.Source.Changed"));
            return bytes;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw refused(ex is FileNotFoundException or DirectoryNotFoundException
                ? CoreText.Format("PackagedFiles.Source.Missing", source)
                : CoreText.Format("PackagedFiles.Source.Unreadable", source, ex.Message));
        }
    }
    private static InvalidDataException Refused(PackagedFile file, string reason) => new(CoreText.Format("PackagedFiles.Refused", Name(file)) + "\n" + reason);
}
