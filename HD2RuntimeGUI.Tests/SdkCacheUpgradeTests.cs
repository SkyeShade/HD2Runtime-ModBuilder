using System.IO.Compression;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using HD2RuntimeGUI.Core;
using HD2RuntimeGUI.Core.GitHub;
using HD2RuntimeGUI.Core.Metadata;
using HD2RuntimeGUI.Core.Services;
using Xunit;

namespace HD2RuntimeGUI.Tests;

// Acquisition and cache regressions for SDK 0.23.1. An older GUI cached 0.23.1 with only the files it knew
// (no MagazineAttachmentCapabilities.json), so the next GUI failed to load it and Settings showed no SDK.
public sealed class SdkCacheUpgradeTests
{
    private const string Magazine = "MagazineAttachmentCapabilities.json";
    private const string Bundled = SdkPin.Version; // The bundled offline SDK; stale-cache completion only uses the identical bundled release.
    private static string Dir(TestEnvironment e, string version) => Path.GetDirectoryName(e.Paths.SdkFile(version))!;
    private static string Current(TestEnvironment e) => (string)JsonNode.Parse(File.ReadAllText(e.Paths.CachePath("current.json")))!["version"]!;
    private static byte[] Archive(Action<ZipArchive> edit)
    {
        using var output = new MemoryStream(); output.Write(SdkFixtures.Archive("0.23.1"));
        using (var zip = new ZipArchive(output, ZipArchiveMode.Update, true)) edit(zip);
        return output.ToArray();
    }

    [Fact] public async Task Fresh_install_of_0231_caches_and_loads_magazine_capabilities()
    {
        using var e = new TestEnvironment();
        var sdk = await SdkFixtures.Install(e, "0.23.1");
        Assert.Equal("0.23.1", sdk.Version); Assert.Equal(43, sdk.Entities!.Attachments!.Attachments.Count());
        Assert.Equal(SdkFixtures.Entry("0.23.1", Magazine), File.ReadAllBytes(Path.Combine(Dir(e, "0.23.1"), Magazine)));
        Assert.Equal(19, Directory.GetFiles(Dir(e, "0.23.1")).Length); // with its LuaLS stub (custom Lua autocomplete)
        Assert.Equal("0.23.1", Current(e));
        e.GitHub.Release = FakeGitHub.MakeRelease("0.23.1", e.GitHub.Archive);
        var status = await e.Updates.CheckAsync(); Assert.True(status.VerifiedOnline); Assert.Equal("0.23.1", status.Installed.Version); Assert.Equal("0.23.1", status.Latest!.Version); Assert.False(status.UpdateAvailable);
    }

    [Fact] public async Task Fresh_offline_first_launch_uses_bundled_0231()
    {
        using var e = new TestEnvironment(); File.Delete(e.Paths.CachePath("current.json")); e.GitHub.Offline = true;
        var status = await e.Updates.CheckAsync();
        Assert.Equal(Bundled, status.Installed.Version); Assert.NotNull(status.Installed.Entities!.Attachments);
        Assert.True(File.Exists(Path.Combine(Dir(e, Bundled), Magazine)));
    }

    [Fact] public async Task Upgrade_from_0230_to_0231_keeps_versions_separate()
    {
        using var e = new TestEnvironment();
        var old = await SdkFixtures.Install(e, "0.23.0"); Assert.Null(old.Entities!.Attachments);
        var before = Directory.GetFiles(Dir(e, "0.23.0")).ToDictionary(Path.GetFileName, File.ReadAllBytes);
        var sdk = await SdkFixtures.Install(e, "0.23.1");
        Assert.Equal("0.23.1", Current(e)); Assert.NotNull(sdk.Entities!.Attachments);
        // The 0.23.0 cache is untouched and never gains 0.23.1 files; each version lives in its own directory.
        Assert.Equal(before.Keys.Order(), Directory.GetFiles(Dir(e, "0.23.0")).Select(Path.GetFileName).Order());
        Assert.False(File.Exists(Path.Combine(Dir(e, "0.23.0"), Magazine)));
        Assert.Null((await e.Cache.GetVersionAsync("0.23.0")).Entities!.Attachments);
        Assert.NotNull((await e.Cache.GetVersionAsync("0.23.1")).Entities!.Attachments);
    }

    [Fact] public async Task Offline_load_of_installed_0231()
    {
        using var e = new TestEnvironment();
        await SdkFixtures.Install(e, "0.23.1"); e.GitHub.Release = FakeGitHub.MakeRelease("0.23.1", e.GitHub.Archive);
        await e.Updates.CheckAsync(); e.GitHub.Offline = true;
        // A restart of the same ModBuilder (no newer bundled SDK to adopt) loads the installed SDK offline.
        var fresh = new SdkCache(e.Paths, e.Reader, e.GitHub) { AdoptNewerBundled = false };
        var status = await new SdkUpdateService(fresh, e.GitHub, e.Paths).CheckAsync();
        Assert.False(status.VerifiedOnline); Assert.Contains("Offline", status.Message);
        Assert.Equal("0.23.1", status.Installed.Version); Assert.Equal("0.23.1", status.Latest!.Version);
        Assert.NotNull(status.Installed.Entities!.Attachments);
    }

    [Fact] public async Task Older_sdk_without_magazine_capabilities_stays_valid()
    {
        using var e = new TestEnvironment();
        foreach (var version in new[] { "0.22.1", "0.23.0" })
        {
            var sdk = await SdkFixtures.Install(e, version);
            Assert.False(File.Exists(Path.Combine(Dir(e, version), Magazine)));
            Assert.Null(sdk.Entities?.Attachments);
            Assert.Null((await e.Cache.GetVersionAsync(version)).Entities?.Attachments);
        }
    }

    [Fact] public async Task Missing_magazine_file_in_0231_archive_is_rejected_without_installing()
    {
        using var e = new TestEnvironment();
        e.GitHub.Archive = Archive(zip => zip.GetEntry(Magazine)!.Delete());
        var error = await Assert.ThrowsAsync<InvalidDataException>(() => e.Cache.InstallAsync(FakeGitHub.MakeRelease("0.23.1", e.GitHub.Archive)));
        Assert.Equal("SDK is missing magazine attachment capabilities.", error.Message); Assert.Equal(Magazine, error.Data[SdkCache.MissingFileKey]);
        Assert.False(Directory.Exists(Dir(e, "0.23.1"))); Assert.Equal("0.5.1", Current(e));
    }

    [Fact] public async Task Malformed_magazine_file_in_0231_archive_is_rejected_without_installing()
    {
        using var e = new TestEnvironment();
        e.GitHub.Archive = Archive(zip =>
        {
            zip.GetEntry(Magazine)!.Delete();
            using var writer = new StreamWriter(zip.CreateEntry(Magazine).Open(), new UTF8Encoding(false)); writer.Write("{\"contract\":\"hd2runtime.weapon_attachment.magazine.v1\"");
        });
        await Assert.ThrowsAnyAsync<Exception>(() => e.Cache.InstallAsync(FakeGitHub.MakeRelease("0.23.1", e.GitHub.Archive)));
        Assert.False(Directory.Exists(Dir(e, "0.23.1"))); Assert.Equal("0.5.1", Current(e));
    }

    // The reported failure: current.json -> 0.23.1, but its cache directory was written without the magazine file.
    private static async Task<TestEnvironment> StaleCache()
    {
        var e = new TestEnvironment();
        await SdkFixtures.Install(e, Bundled); File.Delete(Path.Combine(Dir(e, Bundled), Magazine));
        return e;
    }

    [Fact] public async Task Stale_0231_cache_is_completed_from_identical_bundle_offline()
    {
        using var e = await StaleCache(); e.GitHub.Offline = true;
        var status = await new SdkUpdateService(new SdkCache(e.Paths, e.Reader, e.GitHub), e.GitHub, e.Paths).CheckAsync();
        Assert.Equal(Bundled, status.Installed.Version); Assert.Equal(43, status.Installed.Entities!.Attachments!.Attachments.Count());
        Assert.Equal(SdkFixtures.Entry(Bundled, Magazine), File.ReadAllBytes(Path.Combine(Dir(e, Bundled), Magazine)));
        Assert.Empty(Directory.GetFiles(Dir(e, Bundled), "*.tmp"));
    }

    [Fact] public async Task Stale_cache_with_different_files_is_never_mixed_with_the_bundle()
    {
        using var e = await StaleCache();
        var vehicle = Path.Combine(Dir(e, Bundled), "VehicleAuthoringCapabilities.json"); File.AppendAllText(vehicle, " ");
        var error = await Assert.ThrowsAsync<IncompleteSdkCacheException>(() => e.Cache.GetVersionAsync(Bundled));
        Assert.Equal(Bundled, error.Version); Assert.Equal(Magazine, error.File); Assert.Contains("older HD2Runtime ModBuilder", error.Message);
        Assert.False(File.Exists(Path.Combine(Dir(e, Bundled), Magazine)));
    }

    [Fact] public async Task Reinstalling_a_stale_version_adds_only_the_missing_file()
    {
        using var e = await StaleCache();
        var others = Directory.GetFiles(Dir(e, Bundled)).ToDictionary(Path.GetFileName, File.ReadAllBytes);
        var sdk = await e.Cache.InstallAsync(FakeGitHub.MakeRelease(Bundled, e.GitHub.Archive));
        Assert.NotNull(sdk.Entities!.Attachments);
        Assert.True(File.Exists(Path.Combine(Dir(e, Bundled), Magazine)));
        foreach (var (name, bytes) in others) Assert.Equal(bytes, File.ReadAllBytes(Path.Combine(Dir(e, Bundled), name!)));
    }

    [Fact] public async Task Check_reinstalls_an_incomplete_non_bundled_cache_online()
    {
        using var e = new TestEnvironment();
        var real = e.Cache; e.GitHub.Archive = SdkFixtures.Archive("0.23.1"); e.GitHub.Release = FakeGitHub.MakeRelease("0.23.1", e.GitHub.Archive);
        var status = await new SdkUpdateService(new IncompleteOnce(real), e.GitHub, e.Paths).CheckAsync();
        Assert.True(status.VerifiedOnline); Assert.Equal("0.23.1", status.Installed.Version);
        Assert.Contains("Completed the cached SDK 0.23.1", status.Message);
    }

    [Fact] public async Task Check_offline_reports_an_incomplete_cache_clearly()
    {
        using var e = new TestEnvironment(); e.GitHub.Offline = true;
        var error = await Assert.ThrowsAsync<IncompleteSdkCacheException>(() => new SdkUpdateService(new IncompleteOnce(e.Cache), e.GitHub, e.Paths).CheckAsync());
        Assert.Contains(Magazine, error.Message);
        var app = new BuilderWorkspace(e.Store, e.Projects, new IncompleteOnce(e.Cache), new SdkUpdateService(new IncompleteOnce(e.Cache), e.GitHub, e.Paths), e.Changes, e.Generator, e.Exporter, e.Desktop, e.Desktop, e.Paths);
        await Assert.ThrowsAsync<IncompleteSdkCacheException>(app.InitializeAsync);
        Assert.Null(app.SdkStatus); Assert.Contains(Magazine, app.SdkError);
    }

    // Simulates a cached version the bundle cannot complete (any version other than the bundled one).
    private sealed class IncompleteOnce(ISdkCache inner) : ISdkCache
    {
        private bool failed;
        public Task<SdkMetadata> GetCurrentAsync(CancellationToken ct = default)
        { if (!failed) { failed = true; throw new IncompleteSdkCacheException("0.23.1", Magazine); } return inner.GetCurrentAsync(ct); }
        public Task<SdkMetadata> GetVersionAsync(string version, CancellationToken ct = default) => inner.GetVersionAsync(version, ct);
        public Task<SdkMetadata> InstallAsync(SdkRelease release, CancellationToken ct = default) => inner.InstallAsync(release, ct);
        public Task<SdkMetadata> InspectAsync(SdkRelease release, CancellationToken ct = default) => inner.InspectAsync(release, ct);
    }

    [Fact] public void Published_0231_release_json_parses_to_the_sdk_asset()
    {
        const string tag = "v0.23.1", base_ = "https://github.com/SkyeShade/HD2Runtime/releases/download/v0.23.1/";
        string Asset(string name, long size, string digest) => $$"""{"name":"{{name}}","browser_download_url":"{{base_}}{{name}}","size":{{size}},"digest":"sha256:{{digest}}","content_type":"application/zip"}""";
        var json = $$"""
            [{"tag_name":"{{tag}}","draft":false,"prerelease":false,"html_url":"https://github.com/SkyeShade/HD2Runtime/releases/tag/{{tag}}","assets":[
              {{Asset("HD2Runtime-0.23.1-example-projects.zip", 53639, "a2191ebb4cb0f80ce146d43300ef7b20281276f62f7d9e5c48a244685d091f61")}},
              {{Asset("HD2Runtime-0.23.1-runtime.zip", 542847, "90cd07f867c351c382c62521427e21a2d3c452203a63a06bfceb44a311a9fc19")}},
              {{Asset("HD2Runtime-0.23.1-sdk.zip", 914785, "2c7984a625b8cfca81d4f639d85fd5dae8ab9328780572bda022fff3d4e7774f")}}]},
             {"tag_name":"v0.23.0","draft":false,"prerelease":false,"html_url":"https://github.com/SkyeShade/HD2Runtime/releases/tag/v0.23.0","assets":[]}]
            """;
        var release = Assert.Single(GitHubReleaseClient.ParseReleases(Encoding.UTF8.GetBytes(json)));
        Assert.Equal("0.23.1", release.Version); Assert.Equal("HD2Runtime-0.23.1-sdk.zip", release.AssetName);
        Assert.Equal(base_ + "HD2Runtime-0.23.1-sdk.zip", release.DownloadUrl); Assert.Equal(914785, release.Size);
        Assert.Equal("sha256:2c7984a625b8cfca81d4f639d85fd5dae8ab9328780572bda022fff3d4e7774f", release.Sha256);
        Assert.Equal(3, release.Artifacts.Count);
    }

    [Fact] public void Build_identity_includes_the_commit()
    {
        Assert.StartsWith(BuildInfo.Version, BuildInfo.Identity);
        if (BuildInfo.Commit is { } commit)
        {
            Assert.Matches(new Regex(@"\A[0-9a-f]{40}(\.dirty)?\z"), commit);
            Assert.Contains(commit[..12], BuildInfo.Identity);
        }
    }
}
