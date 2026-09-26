using System.Text.Json;
using System.Text.Json.Serialization;

namespace HD2RuntimeGUI.Core.Storage;

public sealed class AppPaths(string root)
{
    public string Root { get; } = Path.GetFullPath(root);
    public string Projects => Path.Combine(Root, "Projects");
    public string Sdk => Path.Combine(Root, "Sdk");
    public string Exports => Path.Combine(Root, "Exports");
    public string Library => Path.Combine(Root, "library.json");
    public string ProjectDirectory(Guid id) => Path.Combine(Projects, id.ToString("D"));
    public string ProjectFile(Guid id) => Path.Combine(ProjectDirectory(id), "project.hd2mod.json");
    public string SdkFile(string version)
    {
        var parsed = Models.SemVersion.Parse(version);
        if (version != parsed.ToString()) throw new InvalidDataException("SDK version must be canonical.");
        return CachePath(version, "metadata.json");
    }
    public string CachePath(params string[] segments)
    {
        var path = Path.GetFullPath(Path.Combine([Sdk, ..segments]));
        if (!path.StartsWith(Sdk + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("SDK path escaped its cache.");
        for (var part = path; part != null && part.Length >= Root.Length; part = Path.GetDirectoryName(part))
            if ((File.Exists(part) || Directory.Exists(part)) && (File.GetAttributes(part) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("SDK cache must not contain symbolic links or directory junctions.");
        return path;
    }
}

public static class JsonStorage
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow, MaxDepth = 64
    };

    public static async Task WriteAtomicAsync<T>(string path, T value, CancellationToken ct = default)
        => await WriteAtomicBytesAsync(path, JsonSerializer.SerializeToUtf8Bytes(value, Options), ct);

    public static async Task WriteAtomicBytesAsync(string path, byte[] bytes, CancellationToken ct = default)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await File.WriteAllBytesAsync(temp, bytes, ct);
            File.Move(temp, path, true);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }

    public static async Task<T> ReadAsync<T>(string path, CancellationToken ct = default)
    {
        if (new FileInfo(path).Length > 8 * 1024 * 1024) throw new InvalidDataException("JSON file exceeds the size limit.");
        using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<T>(stream, Options, ct) ?? throw new InvalidDataException("Empty JSON document.");
    }
}
