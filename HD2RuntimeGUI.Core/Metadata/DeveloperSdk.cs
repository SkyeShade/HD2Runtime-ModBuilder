using System.Text.Json;
using HD2RuntimeGUI.Core.Localization;
using HD2RuntimeGUI.Core.Storage;

namespace HD2RuntimeGUI.Core.Metadata;

// Developer-only: a local HD2Runtime SDK (an unpublished Runtime build's sdk/ directory or SDK zip) remembered in Settings and bound from
// the next start. The command line (--sdk-path) and HD2RUNTIME_SDK_PATH still take precedence. A local SDK is read in memory and never
// cached; exports built with it require its version, and nothing of the Runtime itself is ever packaged.
public sealed record DeveloperSettings(string? LocalSdkPath = null);
public sealed record LocalSdkChoice(string Path, string Source)
{
    public const string CommandLine = "--sdk-path", Environment = "HD2RUNTIME_SDK_PATH", Settings = "Settings";
}

// The local SDK this run bound (null: published SDKs).
public sealed record ActiveLocalSdk(LocalSdkChoice? Choice);

public static class DeveloperSdk
{
    public const string FileName = "developer.json";
    public static string SettingsPath(AppPaths paths) => Path.Combine(paths.Root, FileName);

    // An unreadable settings file never blocks start-up: the published SDKs are used instead.
    public static DeveloperSettings Load(AppPaths paths)
    {
        try
        {
            var file = SettingsPath(paths);
            if (!File.Exists(file) || new FileInfo(file).Length > 64 * 1024) return new();
            return JsonSerializer.Deserialize<DeveloperSettings>(File.ReadAllBytes(file), JsonStorage.Options) ?? new();
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException) { return new(); }
    }
    public static Task SaveAsync(AppPaths paths, DeveloperSettings settings) => JsonStorage.WriteAtomicAsync(SettingsPath(paths), settings);

    // --sdk-path <path> or --sdk-path=<path>.
    public static string? Argument(IReadOnlyList<string> args)
    {
        for (var i = 0; i < args.Count; i++)
        {
            if (args[i].StartsWith("--sdk-path=", StringComparison.OrdinalIgnoreCase)) return args[i]["--sdk-path=".Length..];
            if (args[i].Equals("--sdk-path", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Count) return args[i + 1];
        }
        return null;
    }
    // Which local SDK this run binds, if any: command line, then the environment, then Settings. A remembered path that no longer exists
    // is ignored so ModBuilder still starts on the published SDKs (Settings reports it).
    public static LocalSdkChoice? Resolve(IReadOnlyList<string> args, string? environment, DeveloperSettings settings)
    {
        if (Argument(args) is { Length: > 0 } arg) return new(System.IO.Path.GetFullPath(arg), LocalSdkChoice.CommandLine);
        if (!string.IsNullOrWhiteSpace(environment)) return new(System.IO.Path.GetFullPath(environment), LocalSdkChoice.Environment);
        return settings.LocalSdkPath is { Length: > 0 } saved && Normalize(saved, out var path) == null ? new(path!, LocalSdkChoice.Settings) : null;
    }
    /// <summary>Checks a chosen path and returns the SDK location to bind (an HD2Runtime checkout resolves to its sdk/ folder), or the reason it
    /// cannot be used.</summary>
    public static string? Normalize(string input, out string? path)
    {
        path = null;
        if (string.IsNullOrWhiteSpace(input)) return CoreText.Get("Messages.LocalSdk.ChoosePath");
        string full;
        try { full = System.IO.Path.GetFullPath(input.Trim().Trim('"')); } catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException) { return CoreText.Get("Messages.LocalSdk.InvalidPath"); }
        if (File.Exists(full))
        {
            if (!full.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)) return CoreText.Get("Messages.LocalSdk.NotZip");
            path = full; return null;
        }
        if (!Directory.Exists(full)) return CoreText.Get("Messages.LocalSdk.PathMissing");
        if (File.Exists(System.IO.Path.Combine(full, "metadata.json"))) { path = full; return null; }
        var sdk = System.IO.Path.Combine(full, "sdk");
        if (File.Exists(System.IO.Path.Combine(sdk, "metadata.json"))) { path = sdk; return null; }
        return CoreText.Get("Messages.LocalSdk.NoMetadata");
    }
    /// <summary>The Runtime commit a local sdk/ folder was generated in, read from the checkout's .git (display only), or null.</summary>
    public static string? RuntimeCommit(string sdkPath)
    {
        try
        {
            if (!Directory.Exists(sdkPath)) return null;
            var git = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(sdkPath).TrimEnd(System.IO.Path.DirectorySeparatorChar))!, ".git");
            if (!Directory.Exists(git)) return null;
            var head = File.ReadAllText(System.IO.Path.Combine(git, "HEAD")).Trim();
            if (!head.StartsWith("ref: ", StringComparison.Ordinal)) return Short(head);
            var name = head[5..];
            var loose = System.IO.Path.Combine(git, name.Replace('/', System.IO.Path.DirectorySeparatorChar));
            if (File.Exists(loose)) return Short(File.ReadAllText(loose).Trim());
            var packed = System.IO.Path.Combine(git, "packed-refs");
            if (!File.Exists(packed)) return null;
            return File.ReadLines(packed).Select(l => l.Split(' ')).Where(p => p.Length == 2 && p[1] == name).Select(p => Short(p[0])).FirstOrDefault();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException) { return null; }
        static string? Short(string hash) => hash.Length >= 7 && hash.All(Uri.IsHexDigit) ? hash[..7] : null;
    }
}
