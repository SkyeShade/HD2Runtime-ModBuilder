using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using HD2RuntimeGUI.Core.Generation;
using HD2RuntimeGUI.Core.GitHub;
using HD2RuntimeGUI.Core.Metadata;
using HD2RuntimeGUI.Core.Models;
using HD2RuntimeGUI.Core.Projects;
using HD2RuntimeGUI.Core.Services;
using HD2RuntimeGUI.Core.Storage;

namespace HD2RuntimeGUI.Tests;

public sealed class TestEnvironment : IDisposable
{
    public AppPaths Paths { get; } = new(Path.Combine(Path.GetTempPath(), "HD2RuntimeGUI-tests", Guid.NewGuid().ToString("N")));
    public FakeGitHub GitHub { get; } = new();
    public MetadataReader Reader { get; } = new();
    public SdkMetadata Metadata { get; }
    public SdkCache Cache { get; }
    public JsonProjectStore Store { get; }
    public ProjectService Projects { get; }
    public SdkUpdateService Updates { get; }
    public ChangeService Changes { get; } = new();
    public LuaGenerator Generator { get; }
    public ModExporter Exporter { get; }
    public FakeDesktop Desktop { get; } = new();
    public TestEnvironment()
    {
        Metadata = Reader.Read(LegacyMetadata());
        Directory.CreateDirectory(Path.GetDirectoryName(Paths.SdkFile(Metadata.Version))!);
        File.WriteAllBytes(Paths.SdkFile(Metadata.Version), LegacyMetadata());
        File.WriteAllText(Paths.CachePath("current.json"), "{\"version\":\"0.5.1\"}");
        // The cache stays pinned to the legacy fixture; tests that exercise adopting a newer bundled SDK construct their own cache.
        Cache = new(Paths, Reader, GitHub) { AdoptNewerBundled = false }; Store = new(Paths); Projects = new(Store, Paths); Updates = new(Cache, GitHub, Paths);
        Generator = new(Changes); Exporter = new(Generator);
    }
    public BuilderWorkspace Workspace() => new(Store, Projects, Cache, Updates, Changes, Generator, Exporter, Desktop, Desktop, Paths);
    public static byte[] LegacyMetadata() => File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "legacy-metadata.json"));
    public Task<ModProject> Project() => Projects.CreateAsync(new("JAR-5 AP4", "SkyeShade", "mods/skyeshade/jar5_ap4", "0.1.0"), Metadata);
    public ModChange Jar5(bool ensure = true) => Changes.Create(Metadata, "jar5", "armor_penetration", "4", ensure, "Gameplay");
    public void Dispose() { if (Directory.Exists(Paths.Root)) Directory.Delete(Paths.Root, true); }
}
public sealed class FakeDesktop : IFolderOpener, IProjectFilePicker
{
    public string? OpenedPath, PickedPath, OpenedFile;
    public Task OpenFileAsync(string path) { OpenedFile = path; return Task.CompletedTask; }
    public bool SelectedFile;
    public Task OpenAsync(string path, bool selectFile = false) { OpenedPath = path; SelectedFile = selectFile; return Task.CompletedTask; }
    public Task<string?> PickAsync() => Task.FromResult(PickedPath);
}
public sealed class FakeGitHub : IGitHubReleaseClient
{
    public SdkRelease Release { get; set; }
    public byte[] Archive { get; set; }
    public bool Offline, FailDownload;
    public int Checks;
    public IReadOnlyList<SdkRelease>? Candidates;
    public Dictionary<string, byte[]> CandidateArchives { get; } = [];
    public List<string> Downloads { get; } = [];
    public FakeGitHub()
    {
        Archive = MakeArchive("0.6.0");
        Release = MakeRelease("0.6.0", Archive);
    }
    public static SdkRelease MakeRelease(string version, byte[] bytes) => new(version, $"HD2Runtime-{version}-sdk.zip",
        $"https://github.com/SkyeShade/HD2Runtime/releases/download/v{version}/HD2Runtime-{version}-sdk.zip", bytes.Length,
        "sha256:" + Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(), $"https://github.com/SkyeShade/HD2Runtime/releases/tag/v{version}");
    public static byte[] MakeArchive(string version, string? maliciousEntry = null, int schema = 1)
    {
        var json = JsonNode.Parse(TestEnvironment.LegacyMetadata())!; json["runtime_version"] = version; json["schema_version"] = schema;
        using var output = new MemoryStream();
        using (var zip = new ZipArchive(output, ZipArchiveMode.Create, true))
        {
            using (var writer = new StreamWriter(zip.CreateEntry("metadata.json").Open())) writer.Write(json.ToJsonString());
            if (maliciousEntry != null) using (var writer = new StreamWriter(zip.CreateEntry(maliciousEntry).Open())) writer.Write("malicious");
        }
        return output.ToArray();
    }
    public Task<SdkRelease> GetLatestAsync(CancellationToken ct = default)
    { Checks++; return Offline ? Task.FromException<SdkRelease>(new HttpRequestException("Offline")) : Task.FromResult(Release); }
    public async Task<IReadOnlyList<SdkRelease>> GetReleasesAsync(CancellationToken ct = default) => Candidates ?? [await GetLatestAsync(ct)];
    public async Task DownloadAsync(SdkRelease release, string destination, CancellationToken ct = default)
    { Downloads.Add(release.Version); await File.WriteAllBytesAsync(destination, CandidateArchives.GetValueOrDefault(release.Version) ?? Archive, ct); if (FailDownload) throw new HttpRequestException("Interrupted download"); }
}

// Published SDK archives kept as fixtures so version-specific regressions stay pinned when the bundled SDK moves on.
public static class SdkFixtures
{
    public static byte[] Archive(string version) => File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", $"sdk-{version}.zip"));
    public static byte[] Entry(string version, string name)
    {
        using var zip = System.IO.Compression.ZipFile.OpenRead(Path.Combine(AppContext.BaseDirectory, "Fixtures", $"sdk-{version}.zip"));
        using var input = zip.GetEntry(name)!.Open(); using var output = new MemoryStream(); input.CopyTo(output); return output.ToArray();
    }
    public static async Task<HD2RuntimeGUI.Core.Metadata.SdkMetadata> Install(TestEnvironment e, string version)
    { e.GitHub.Archive = Archive(version); return await e.Cache.InstallAsync(FakeGitHub.MakeRelease(version, e.GitHub.Archive)); }
}
