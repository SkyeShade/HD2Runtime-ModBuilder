extern alias updater;
using System.Diagnostics;
using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using HD2RuntimeGUI.Core;
using HD2RuntimeGUI.Core.Storage;
using HD2RuntimeGUI.Core.Updates;
using HD2RuntimeModBuilder.Updater;
using Xunit;
using Installer = updater::HD2RuntimeModBuilder.Updater.UpdateInstaller;
using InstallerOptions = updater::HD2RuntimeModBuilder.Updater.UpdaterOptions;
using InstallerContract = updater::HD2RuntimeModBuilder.Updater.UpdateContract;

namespace HD2RuntimeGUI.Tests;

// HD2Runtime ModBuilder self-update: release discovery, manifest/package verification, staging, and the standalone updater.
// Everything runs against local fakes and temporary folders; nothing contacts GitHub or touches a real installation.
public sealed class AppUpdateTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "HD2RuntimeGUI-tests", "app-update-" + Guid.NewGuid().ToString("N"));
    private string Dir(string name) { var d = Path.Combine(root, name); Directory.CreateDirectory(d); return d; }
    public void Dispose()
    {
        if (!Directory.Exists(root)) return;
        foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)) File.SetAttributes(file, FileAttributes.Normal);
        Directory.Delete(root, true);
    }

    // ---- Release discovery -------------------------------------------------------------------------------------------

    private static string ReleaseJson(string version, bool prerelease = false, bool draft = false, bool package = true, bool manifest = true, string? htmlUrl = null, string? digest = null, bool legacyManifest = true)
    {
        var tag = "v" + version; var assets = new List<string>();
        if (package) assets.Add($$"""{"name":"HD2Runtime-ModBuilder-{{tag}}-win-x64.zip","size":1000,"browser_download_url":"https://github.com/SkyeShade/HD2Runtime-ModBuilder/releases/download/{{tag}}/HD2Runtime-ModBuilder-{{tag}}-win-x64.zip"{{(digest != null ? $",\"digest\":\"{digest}\"" : "")}}}""");
        if (manifest) assets.Add($$"""{"name":"modbuilder-update-v2.json","size":300,"browser_download_url":"https://github.com/SkyeShade/HD2Runtime-ModBuilder/releases/download/{{tag}}/modbuilder-update-v2.json"}""");
        if (legacyManifest) assets.Add($$"""{"name":"modbuilder-update.json","size":300,"browser_download_url":"https://github.com/SkyeShade/HD2Runtime-ModBuilder/releases/download/{{tag}}/modbuilder-update.json"}""");
        return $$"""{"draft":{{(draft ? "true" : "false")}},"prerelease":{{(prerelease ? "true" : "false")}},"tag_name":"{{tag}}","html_url":"{{htmlUrl ?? "https://github.com/SkyeShade/HD2Runtime-ModBuilder/releases/tag/" + tag}}","assets":[{{string.Join(",", assets)}}]}""";
    }
    private static AppRelease? Parse(params string[] releases) => AppReleaseClient.ParseLatest(Encoding.UTF8.GetBytes("[" + string.Join(",", releases) + "]"));

    [Fact] public void Newest_stable_release_with_both_assets_is_chosen()
    {
        var latest = Parse(ReleaseJson("1.0.0"), ReleaseJson("1.2.0"), ReleaseJson("1.1.0"))!;
        Assert.Equal("1.2.0", latest.Version); Assert.Equal("v1.2.0", latest.Tag);
        Assert.Equal("https://github.com/SkyeShade/HD2Runtime-ModBuilder/releases/tag/v1.2.0", latest.ReleaseUrl);
        Assert.Equal("HD2Runtime-ModBuilder-v1.2.0-win-x64.zip", latest.Package.Name);
    }

    [Fact] public void Prereleases_drafts_and_releases_without_the_package_are_ignored()
    {
        Assert.Equal("1.0.0", Parse(ReleaseJson("1.0.0"), ReleaseJson("2.0.0", prerelease: true), ReleaseJson("3.0.0", draft: true))!.Version);
        Assert.Equal("1.0.0", Parse(ReleaseJson("1.0.0"), """{"draft":false,"prerelease":false,"tag_name":"v2.0.0-beta.1","html_url":"x","assets":[]}""")!.Version);
        Assert.Equal("1.0.0", Parse(ReleaseJson("1.0.0"), ReleaseJson("1.1.0", package: false), ReleaseJson("1.2.0", manifest: false))!.Version);
        Assert.Null(Parse(ReleaseJson("1.1.0", package: false)));
        // 1.0.0 releases have only the format-1 manifest: 1.0.1 and later never offer them.
        Assert.Null(Parse(ReleaseJson("1.0.0", manifest: false)));
        Assert.Equal(AppReleaseClient.ManifestName, Parse(ReleaseJson("1.1.0"))!.Manifest.Name);
        Assert.Null(Parse());
    }

    [Fact] public void Releases_outside_the_official_repository_or_with_bad_assets_are_rejected()
    {
        Assert.Throws<InvalidDataException>(() => Parse(ReleaseJson("1.1.0", htmlUrl: "https://github.com/Someone/Fork/releases/tag/v1.1.0")));
        Assert.Throws<InvalidDataException>(() => Parse(ReleaseJson("1.1.0").Replace("github.com/SkyeShade/HD2Runtime-ModBuilder/releases/download", "evil.example/download")));
        Assert.Throws<InvalidDataException>(() => Parse(ReleaseJson("1.1.0", digest: "md5:abc")));
    }

    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public int Calls;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        { Calls++; var response = respond(request); response.RequestMessage = request; return Task.FromResult(response); }
    }

    [Fact] public async Task Github_request_uses_the_modbuilder_repository_and_user_agent()
    {
        using var handler = new Handler(request =>
        {
            Assert.Equal("https://api.github.com/repos/SkyeShade/HD2Runtime-ModBuilder/releases?per_page=20", request.RequestUri!.AbsoluteUri);
            Assert.Equal($"HD2Runtime-ModBuilder/{BuildInfo.Version}", request.Headers.UserAgent.ToString()); Assert.Null(request.Headers.Authorization);
            return new(HttpStatusCode.OK) { Content = new StringContent("[" + ReleaseJson("1.1.0") + "]") };
        });
        Assert.Equal("1.1.0", (await new AppReleaseClient(new HttpClient(handler)).GetLatestAsync())!.Version);
    }

    [Theory]
    [InlineData("offline", AppUpdateStatus.Offline)]
    [InlineData("ratelimit", AppUpdateStatus.RateLimited)]
    [InlineData("429", AppUpdateStatus.RateLimited)]
    [InlineData("malformed", AppUpdateStatus.CheckFailed)]
    [InlineData("notarray", AppUpdateStatus.CheckFailed)]
    [InlineData("500", AppUpdateStatus.CheckFailed)]
    [InlineData("404", AppUpdateStatus.CheckFailed)]
    public async Task Network_failures_are_classified_and_never_thrown_from_a_check(string failure, AppUpdateStatus expected)
    {
        using var handler = new Handler(_ => failure switch
        {
            "offline" => throw new HttpRequestException("No such host is known."),
            "ratelimit" => new HttpResponseMessage(HttpStatusCode.Forbidden) { Headers = { { "x-ratelimit-remaining", "0" } } },
            "429" => new HttpResponseMessage(HttpStatusCode.TooManyRequests),
            "malformed" => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("[{\"draft\":false") },
            "notarray" => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"message\":\"x\"}") },
            "404" => new HttpResponseMessage(HttpStatusCode.NotFound),
            _ => new HttpResponseMessage(HttpStatusCode.InternalServerError)
        });
        var client = new AppReleaseClient(new HttpClient(handler));
        Assert.Equal(expected, (await Assert.ThrowsAsync<AppUpdateCheckException>(() => client.GetLatestAsync())).Status);
        var service = Service(client, "1.0.0");
        await service.CheckOnStartupAsync();
        Assert.Equal(expected, service.Status); Assert.False(service.ShowBanner); Assert.NotNull(service.CheckMessage);
    }

    [Fact] public async Task Package_download_rejects_untrusted_redirects_and_deleted_assets()
    {
        var release = Parse(ReleaseJson("1.1.0"))!;
        using var redirect = new Handler(_ => new(HttpStatusCode.Redirect) { Headers = { Location = new Uri("https://attacker.example/x.zip") } });
        await Assert.ThrowsAsync<InvalidDataException>(() => new AppReleaseClient(new HttpClient(redirect)).DownloadPackageAsync(release, Path.Combine(Dir("dl"), "a.zip")));
        Assert.Equal(1, redirect.Calls);
        using var gone = new Handler(_ => new(HttpStatusCode.NotFound));
        Assert.Contains("no longer available", (await Assert.ThrowsAsync<InvalidDataException>(() => new AppReleaseClient(new HttpClient(gone)).DownloadManifestAsync(release))).Message);
    }

    // ---- Update state, banner and dismissal -------------------------------------------------------------------------------

    private sealed class FakeClient : IAppReleaseClient
    {
        public AppRelease? Latest; public int Checks; public Exception? Failure;
        public byte[] Manifest = []; public string? PackagePath;
        public Task<AppRelease?> GetLatestAsync(CancellationToken ct = default) { Checks++; return Failure != null ? Task.FromException<AppRelease?>(Failure) : Task.FromResult(Latest); }
        public Task<byte[]> DownloadManifestAsync(AppRelease release, CancellationToken ct = default) => Task.FromResult(Manifest);
        public Task DownloadPackageAsync(AppRelease release, string destination, IProgress<long>? progress = null, CancellationToken ct = default)
        { File.Copy(PackagePath ?? throw new HttpRequestException("offline"), destination); progress?.Report(release.Package.Size); return Task.CompletedTask; }
    }
    private sealed class FakeHost(string install) : IAppUpdateHost
    {
        public string InstallDirectory => install;
        public int ProcessId => 4242;
        public long ProcessStartTicks => 638_000_000_000_000_000;
        public List<ProcessStartInfo> Launched = []; public int Exits; public List<Uri> Opened = [];
        public void Launch(ProcessStartInfo command) => Launched.Add(command);
        public void Exit() => Exits++;
        public Task OpenUrlAsync(Uri url) { Opened.Add(url); return Task.CompletedTask; }
    }
    private sealed class Clock : TimeProvider { public DateTimeOffset Now = new(2026, 9, 28, 12, 0, 0, TimeSpan.Zero); public override DateTimeOffset GetUtcNow() => Now; }

    private string DataRoot => Path.Combine(root, "data");
    private FakeHost? host;
    private AppUpdateService Service(IAppReleaseClient client, string current, Clock? clock = null, string? install = null)
    {
        host = new FakeHost(install ?? Dir("install-empty"));
        return new AppUpdateService(client, new AppPaths(DataRoot), host, current, Path.Combine(root, "temp"), clock);
    }
    private static AppRelease Release(string version, long size = 1000, string? digest = null) =>
        new(version, "v" + version, AppReleaseClient.ReleasePage("v" + version),
            new(AppReleaseClient.PackageName(version), $"{BuildInfo.RepositoryUrl}/releases/download/v{version}/{AppReleaseClient.PackageName(version)}", size, digest),
            new(AppReleaseClient.ManifestName, $"{BuildInfo.RepositoryUrl}/releases/download/v{version}/{AppReleaseClient.ManifestName}", 300, null));

    [Theory]
    [InlineData("1.0.0", "1.0.0", AppUpdateStatus.UpToDate)]
    [InlineData("1.1.0", "1.0.0", AppUpdateStatus.UpdateAvailable)]
    [InlineData("1.0.0", "1.1.0", AppUpdateStatus.UpToDate)]
    [InlineData("1.0.1", "1.0.0", AppUpdateStatus.UpdateAvailable)]
    [InlineData("1.0.0", "1.0.0-dev", AppUpdateStatus.UpdateAvailable)]
    public async Task Versions_are_compared_semantically(string latest, string current, AppUpdateStatus expected)
    {
        var service = Service(new FakeClient { Latest = Release(latest) }, current);
        await service.CheckOnStartupAsync();
        Assert.Equal(expected, service.Status);
        Assert.Equal(expected == AppUpdateStatus.UpdateAvailable, service.ShowBanner);
    }

    [Fact] public async Task No_published_release_is_quietly_up_to_date()
    {
        var service = Service(new FakeClient(), "1.0.0");
        await service.CheckOnStartupAsync();
        Assert.Equal(AppUpdateStatus.UpToDate, service.Status); Assert.False(service.ShowBanner); Assert.Null(service.Latest);
    }

    [Fact] public async Task Startup_check_is_cached_for_an_hour_and_manual_checks_always_query()
    {
        var client = new FakeClient { Latest = Release("1.1.0") }; var clock = new Clock();
        await Service(client, "1.0.0", clock).CheckOnStartupAsync();
        var second = Service(client, "1.0.0", clock);
        await second.CheckOnStartupAsync();
        Assert.Equal(1, client.Checks); Assert.Equal(AppUpdateStatus.UpdateAvailable, second.Status); Assert.Equal("1.1.0", second.Latest!.Version);
        await second.CheckAsync(); await second.CheckAsync();
        Assert.Equal(3, client.Checks);
        clock.Now += TimeSpan.FromHours(2);
        await Service(client, "1.0.0", clock).CheckOnStartupAsync();
        Assert.Equal(4, client.Checks);
    }

    [Fact] public async Task Dismissing_hides_that_version_until_a_newer_one_is_released()
    {
        var client = new FakeClient { Latest = Release("1.1.0") }; var clock = new Clock();
        var service = Service(client, "1.0.0", clock);
        var changes = 0; service.Changed += () => changes++;
        await service.CheckOnStartupAsync();
        Assert.True(service.ShowBanner);
        await service.DismissAsync();
        Assert.False(service.ShowBanner); Assert.True(service.UpdateAvailable); Assert.True(changes > 0);
        // Dismissal survives a restart; a newer release shows the banner again.
        var restarted = Service(client, "1.0.0", clock);
        await restarted.CheckOnStartupAsync();
        Assert.False(restarted.ShowBanner);
        client.Latest = Release("1.2.0");
        await restarted.CheckAsync();
        Assert.True(restarted.ShowBanner); Assert.Equal("1.2.0", restarted.Latest!.Version);
    }

    [Fact] public async Task Release_notes_open_only_official_release_pages()
    {
        var service = Service(new FakeClient { Latest = Release("1.1.0") }, "1.0.0");
        await service.CheckAsync();
        Assert.Equal("https://github.com/SkyeShade/HD2Runtime-ModBuilder/releases/tag/v1.1.0", service.ReleaseNotesUrl);
        await service.OpenReleaseNotesAsync();
        Assert.Equal(new Uri(service.ReleaseNotesUrl), Assert.Single(host!.Opened));
        Assert.Throws<InvalidDataException>(() => AppUpdateService.ValidatedReleaseUrl("https://github.com/SkyeShade/HD2Runtime-ModBuilder.evil.example/releases"));
        Assert.Throws<InvalidDataException>(() => AppUpdateService.ValidatedReleaseUrl("http://github.com/SkyeShade/HD2Runtime-ModBuilder/releases/tag/v1.1.0"));
        Assert.Throws<InvalidDataException>(() => AppUpdateService.ValidatedReleaseUrl("https://evil.example/"));
    }

    // ---- Packages: manifest, hash, ZIP safety, staging ------------------------------------------------------------------

    private sealed record Package(string Zip, AppRelease Release, byte[] ManifestJson, AppUpdateManifest Manifest, Dictionary<string, string> Files);

    /// <summary>Builds a release package the way scripts/publish-windows.ps1 does: flat files + modbuilder-files.json, zipped.</summary>
    private Package BuildPackage(string version, Dictionary<string, string>? files = null, Action<ZipArchive>? tamper = null, string? digestOverride = null)
    {
        files ??= new() { [UpdateContract.EntryPoint] = "app " + version, [UpdateContract.LegacyEntryPoint] = "app " + version, [UpdateContract.UpdaterExe] = "updater " + version, ["HD2RuntimeModBuilder.dll"] = "dll " + version, ["wwwroot/app.css"] = "css " + version };
        var stage = Dir("pkg-" + version + "-" + Guid.NewGuid().ToString("N")[..6]);
        var entries = files.Select(f =>
        {
            var path = Path.Combine(stage, f.Key.Replace('/', Path.DirectorySeparatorChar)); Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, f.Value);
            return new InventoryEntry(f.Key, new FileInfo(path).Length, Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path))));
        }).ToArray();
        File.WriteAllBytes(Path.Combine(stage, UpdateContract.InventoryFile), JsonSerializer.SerializeToUtf8Bytes(new Inventory(1, UpdateContract.Product, version, entries), UpdateJson.Default.Inventory));
        var zip = Path.Combine(Dir("zips"), AppReleaseClient.PackageName(version) + Guid.NewGuid().ToString("N")[..6]);
        ZipFile.CreateFromDirectory(stage, zip);
        if (tamper != null) using (var archive = ZipFile.Open(zip, ZipArchiveMode.Update)) tamper(archive);
        var sha = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(zip)));
        var release = Release(version, new FileInfo(zip).Length, digestOverride ?? "sha256:" + sha);
        var manifest = new AppUpdateManifest(2, "HD2Runtime ModBuilder", version, "v" + version, release.Package.Name, release.Package.Size, sha, UpdateContract.EntryPoint, UpdateContract.UpdaterExe, new string('a', 40));
        var json = JsonSerializer.SerializeToUtf8Bytes(manifest, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        return new(zip, release, json, manifest, files);
    }

    [Fact] public async Task A_valid_package_is_verified_and_staged_exactly()
    {
        var package = BuildPackage("1.1.0");
        var manifest = AppUpdateManifest.Parse(package.ManifestJson, package.Release);
        var staged = Path.Combine(root, "staged");
        var inventory = await AppUpdatePackage.StageAsync(package.Zip, manifest, staged);
        Assert.Equal(5, inventory.Files.Length);
        foreach (var file in package.Files) Assert.Equal(file.Value, File.ReadAllText(Path.Combine(staged, file.Key)));
        Assert.True(File.Exists(Path.Combine(staged, UpdateContract.InventoryFile)));
        Assert.Equal(6, Directory.GetFiles(staged, "*", SearchOption.AllDirectories).Length);
    }

    [Fact] public async Task Wrong_hash_or_manifest_is_rejected_before_anything_is_extracted()
    {
        var package = BuildPackage("1.1.0");
        var wrongHash = package.Manifest with { Sha256 = new string('0', 64) };
        var staged = Path.Combine(root, "staged");
        Assert.Contains("SHA-256", (await Assert.ThrowsAsync<InvalidDataException>(() => AppUpdatePackage.StageAsync(package.Zip, wrongHash, staged))).Message);
        Assert.False(Directory.Exists(staged));
        // Manifest fields are fixed by the release contract.
        byte[] Json(AppUpdateManifest m) => JsonSerializer.SerializeToUtf8Bytes(m, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        foreach (var bad in new[] { package.Manifest with { Version = "1.2.0" }, package.Manifest with { Entrypoint = "..\\evil.exe" }, package.Manifest with { Updater = "cmd.exe" },
                     package.Manifest with { Asset = "other.zip" }, package.Manifest with { Size = 1 }, package.Manifest with { Sha256 = "XYZ" }, package.Manifest with { Format = 1 }, package.Manifest with { Entrypoint = UpdateContract.LegacyEntryPoint } })
            Assert.Throws<InvalidDataException>(() => AppUpdateManifest.Parse(Json(bad), package.Release));
        Assert.Throws<InvalidDataException>(() => AppUpdateManifest.Parse("{\"format\":1,\"extra\":true}"u8.ToArray(), package.Release));
        Assert.Throws<InvalidDataException>(() => AppUpdateManifest.Parse("not json"u8.ToArray(), package.Release));
        // GitHub's asset digest must agree with the manifest.
        var mismatched = BuildPackage("1.1.0", digestOverride: "sha256:" + new string('b', 64));
        Assert.Throws<InvalidDataException>(() => AppUpdateManifest.Parse(mismatched.ManifestJson, mismatched.Release));
    }

    [Theory]
    [InlineData("../evil.txt")]
    [InlineData("..\\evil.txt")]
    [InlineData("/abs.txt")]
    [InlineData("C:/Windows/evil.txt")]
    [InlineData("wwwroot/../../evil.txt")]
    [InlineData(".modbuilder-update-backup/x.dll")]
    public async Task Zip_path_traversal_is_rejected(string entryName)
    {
        var package = BuildPackage("1.1.0", tamper: zip => { using var w = new StreamWriter(zip.CreateEntry(entryName).Open()); w.Write("pwned"); });
        var staged = Path.Combine(root, "stage-area", "staged");
        await Assert.ThrowsAsync<InvalidDataException>(() => AppUpdatePackage.StageAsync(package.Zip, package.Manifest, staged));
        Assert.False(File.Exists(Path.Combine(root, "stage-area", "evil.txt"))); Assert.False(File.Exists(Path.Combine(root, "evil.txt")));
    }

    [Fact] public async Task Files_outside_the_inventory_or_with_altered_content_are_rejected()
    {
        var extra = BuildPackage("1.1.0", tamper: zip => { using var w = new StreamWriter(zip.CreateEntry("unlisted.dll").Open()); w.Write("x"); });
        Assert.Contains("inventory", (await Assert.ThrowsAsync<InvalidDataException>(() => AppUpdatePackage.StageAsync(extra.Zip, extra.Manifest, Path.Combine(root, "s1")))).Message);
        var altered = BuildPackage("1.1.0", tamper: zip => { zip.GetEntry("wwwroot/app.css")!.Delete(); using var w = new StreamWriter(zip.CreateEntry("wwwroot/app.css").Open()); w.Write("CSS 1.1.0"); });
        await Assert.ThrowsAsync<InvalidDataException>(() => AppUpdatePackage.StageAsync(altered.Zip, altered.Manifest, Path.Combine(root, "s2")));
        var noUpdater = BuildPackage("1.1.0", files: new() { [UpdateContract.EntryPoint] = "app" });
        var legacyOnly = BuildPackage("1.1.0", files: new() { [UpdateContract.LegacyEntryPoint] = "app", [UpdateContract.UpdaterExe] = "u" });
        Assert.Contains("HD2RuntimeModBuilder.exe", (await Assert.ThrowsAsync<InvalidDataException>(() => AppUpdatePackage.StageAsync(legacyOnly.Zip, legacyOnly.Manifest, Path.Combine(root, "s4")))).Message);
        Assert.Contains("updater", (await Assert.ThrowsAsync<InvalidDataException>(() => AppUpdatePackage.StageAsync(noUpdater.Zip, noUpdater.Manifest, Path.Combine(root, "s3")))).Message);
    }

    // ---- Installing: service side ------------------------------------------------------------------------------------------

    /// <summary>A release installation of <paramref name="version"/> in a fresh folder, with its inventory.</summary>
    private string Install(string version, Dictionary<string, string>? files = null)
    {
        var package = BuildPackage(version, files);
        var install = Dir("install-" + version + "-" + Guid.NewGuid().ToString("N")[..6]);
        ZipFile.ExtractToDirectory(package.Zip, install);
        return install;
    }
    private static Dictionary<string, string> Snapshot(string directory) => Directory.GetFiles(directory, "*", SearchOption.AllDirectories)
        .ToDictionary(f => Path.GetRelativePath(directory, f).Replace('\\', '/'), f => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(f))));

    [Fact] public async Task Install_verifies_stages_and_hands_off_to_the_staged_updater()
    {
        var install = Install("1.0.0"); var package = BuildPackage("1.1.0");
        var client = new FakeClient { Latest = package.Release, Manifest = package.ManifestJson, PackagePath = package.Zip };
        var service = Service(client, "1.0.0", install: install);
        await service.CheckAsync();
        await service.InstallAsync();
        Assert.Null(service.InstallError);
        var command = Assert.Single(host!.Launched); Assert.Equal(1, host.Exits);
        var options = UpdateContract.Parse(command.ArgumentList.ToArray());
        Assert.Equal(Path.Combine(options.StagedDirectory, UpdateContract.UpdaterExe), command.FileName);
        Assert.Equal(options.StagedDirectory, command.WorkingDirectory); Assert.False(command.UseShellExecute); Assert.True(command.CreateNoWindow);
        Assert.StartsWith(Path.Combine(root, "temp"), options.StagedDirectory);
        Assert.Equal((4242, 638_000_000_000_000_000L, install, "1.1.0", Path.Combine(DataRoot, "app-update-result.json")), (options.ProcessId, options.ProcessStartTicks, options.InstallDirectory, options.Version, options.ResultPath));
        Assert.Equal("updater 1.1.0", File.ReadAllText(Path.Combine(options.StagedDirectory, UpdateContract.UpdaterExe)));
        Assert.Equal(options, UpdateContract.Parse(UpdateContract.Arguments(options).ToArray()));
    }

    [Fact] public async Task Download_or_verification_failure_keeps_the_app_running_and_reports_the_error()
    {
        var install = Install("1.0.0"); var before = Snapshot(install);
        var package = BuildPackage("1.1.0");
        var client = new FakeClient { Latest = package.Release, Manifest = package.ManifestJson, PackagePath = BuildPackage("1.1.0", tamper: z => z.CreateEntry("x")).Zip };
        var service = Service(client, "1.0.0", install: install);
        await service.CheckAsync(); await service.InstallAsync();
        Assert.NotNull(service.InstallError); Assert.False(service.Installing); Assert.Empty(host!.Launched); Assert.Equal(0, host.Exits);
        Assert.Equal(before, Snapshot(install));
        Assert.False(Directory.Exists(Path.Combine(root, "temp", "1.1.0")));
        client.PackagePath = null; // offline during download
        await service.InstallAsync();
        Assert.Contains("could not be downloaded", service.InstallError); Assert.Empty(host.Launched);
    }

    [Fact] public async Task Unwritable_installation_offers_the_release_page_instead()
    {
        var install = Install("1.0.0"); var package = BuildPackage("1.1.0");
        File.SetAttributes(Path.Combine(install, UpdateContract.InventoryFile), FileAttributes.ReadOnly);
        Assert.False(AppUpdateService.CanWrite(install));
        var service = Service(new FakeClient { Latest = package.Release, Manifest = package.ManifestJson, PackagePath = package.Zip }, "1.0.0", install: install);
        await service.CheckAsync(); await service.InstallAsync();
        Assert.True(service.InstallNeedsManualDownload); Assert.Contains("cannot write", service.InstallError); Assert.Empty(host!.Launched);
        Assert.True(service.ShowBanner);
        Assert.True(AppUpdateService.CanWrite(Dir("writable")));
    }

    [Fact] public async Task A_development_build_without_an_inventory_does_not_self_update()
    {
        var package = BuildPackage("1.1.0");
        var service = Service(new FakeClient { Latest = package.Release, Manifest = package.ManifestJson, PackagePath = package.Zip }, "1.0.0");
        await service.CheckAsync(); await service.InstallAsync();
        Assert.False(service.IsReleaseInstall); Assert.True(service.InstallNeedsManualDownload); Assert.Empty(host!.Launched);
    }

    // ---- Installing: the standalone updater ---------------------------------------------------------------------------------

    private (InstallerOptions Options, List<ProcessStartInfo> Started) Staged(string install, Package package)
    {
        var staged = Path.Combine(root, "temp-" + Guid.NewGuid().ToString("N")[..6], "staged");
        AppUpdatePackage.StageAsync(package.Zip, package.Manifest, staged).GetAwaiter().GetResult();
        return (new InstallerOptions(4242, 1, install, staged, package.Manifest.Version, Path.Combine(DataRoot, "app-update-result.json")), []);
    }

    [Fact] public void Updater_replaces_app_files_keeps_unrelated_files_and_user_data_and_restarts()
    {
        var install = Install("1.0.0", new() { [UpdateContract.EntryPoint] = "app 1.0.0", [UpdateContract.UpdaterExe] = "u 1.0.0", ["old-only.dll"] = "stale", ["wwwroot/app.css"] = "css 1.0.0", ["gone/deep/file.js"] = "js" });
        File.WriteAllText(Path.Combine(install, "user-notes.txt"), "mine");
        Directory.CreateDirectory(Path.Combine(DataRoot, "Projects", "p1"));
        File.WriteAllText(Path.Combine(DataRoot, "library.json"), "{}"); File.WriteAllText(Path.Combine(DataRoot, "Projects", "p1", "project.hd2mod.json"), "{\"p\":1}");
        var dataBefore = Snapshot(DataRoot);
        var package = BuildPackage("1.1.0");
        var (options, started) = Staged(install, package);
        var result = new Installer(started.Add, (_, _, _) => true).Run(options);

        Assert.True(result.Success, result.Message);
        foreach (var file in package.Files.Where(f => f.Key != UpdateContract.LegacyEntryPoint)) Assert.Equal(file.Value, File.ReadAllText(Path.Combine(install, file.Key)));
        Assert.False(File.Exists(Path.Combine(install, UpdateContract.LegacyEntryPoint))); // the 1.0.0 compatibility copy is never installed
        Assert.False(File.Exists(Path.Combine(install, "old-only.dll"))); Assert.False(Directory.Exists(Path.Combine(install, "gone")));
        Assert.Equal("mine", File.ReadAllText(Path.Combine(install, "user-notes.txt")));
        Assert.False(Directory.Exists(Path.Combine(install, UpdateContract.BackupDirectory)));
        Assert.Equal("1.1.0", UpdateJson.ReadInventory(Path.Combine(install, UpdateContract.InventoryFile)).Version);
        // Restart command: the installed entry point, from the installation folder.
        var restart = Assert.Single(started);
        Assert.Equal(Path.Combine(install, "HD2RuntimeModBuilder.exe"), restart.FileName); Assert.Equal(install, restart.WorkingDirectory); Assert.False(restart.UseShellExecute);
        // User data outside the installation is untouched; only the result file is added for the next start.
        var dataAfter = Snapshot(DataRoot);
        Assert.True(dataAfter.Remove("app-update-result.json")); Assert.Equal(dataBefore, dataAfter);
    }

    [Fact] public void Failed_replacement_restores_the_previous_installation()
    {
        var install = Install("1.0.0"); var before = Snapshot(install);
        var package = BuildPackage("1.1.0");
        var (options, started) = Staged(install, package);
        var copies = 0;
        var result = new Installer(started.Add, (_, _, _) => true) { Attempts = 1, BeforeCopy = _ => { if (++copies == 3) throw new IOException("disk full"); } }.Run(options);

        Assert.False(result.Success); Assert.Contains("previous version was restored", result.Message);
        Assert.Equal(before, Snapshot(install));
        Assert.Equal(Path.Combine(install, "HD2RuntimeModBuilder.exe"), Assert.Single(started).FileName); // the old version is started again
        var written = UpdateJson.ReadResult(options.ResultPath);
        Assert.False(written.Success); Assert.Equal("1.1.0", written.Version);
    }

    [Fact] public void A_locked_application_file_fails_safely()
    {
        var install = Install("1.0.0"); var before = Snapshot(install);
        var (options, started) = Staged(install, BuildPackage("1.1.0"));
        updater::HD2RuntimeModBuilder.Updater.UpdateResult result;
        using (new FileStream(Path.Combine(install, "wwwroot", "app.css"), FileMode.Open, FileAccess.Read, FileShare.None))
            result = new Installer(started.Add, (_, _, _) => true) { Attempts = 2, RetryDelay = TimeSpan.FromMilliseconds(10) }.Run(options);
        Assert.False(result.Success); Assert.Equal(before, Snapshot(install));
    }

    [Fact] public void Updater_does_nothing_if_the_app_does_not_exit_or_the_staged_files_are_incomplete()
    {
        var install = Install("1.0.0"); var before = Snapshot(install);
        var (options, started) = Staged(install, BuildPackage("1.1.0"));
        var result = new Installer(started.Add, (_, _, _) => false).Run(options);
        Assert.False(result.Success); Assert.Empty(started); Assert.Equal(before, Snapshot(install));

        File.WriteAllText(Path.Combine(options.StagedDirectory, "HD2RuntimeModBuilder.dll"), "x");
        result = new Installer(started.Add, (_, _, _) => true).Run(options);
        Assert.False(result.Success); Assert.Contains("verified", result.Message); Assert.Equal(before, Snapshot(install));
        Assert.Single(started); // the unchanged old version is restarted
    }

    [Fact] public async Task End_to_end_update_from_a_local_release()
    {
        var install = Install("1.0.0"); var package = BuildPackage("1.1.0");
        var service = Service(new FakeClient { Latest = package.Release, Manifest = package.ManifestJson, PackagePath = package.Zip }, "1.0.0", install: install);
        await service.CheckOnStartupAsync();
        Assert.True(service.ShowBanner);
        await service.InstallAsync();
        var options = UpdateContract.Parse(Assert.Single(host!.Launched).ArgumentList.ToArray());
        var started = new List<ProcessStartInfo>();
        var result = new Installer(started.Add, (_, _, _) => true).Run(new InstallerOptions(options.ProcessId, options.ProcessStartTicks, options.InstallDirectory, options.StagedDirectory, options.Version, options.ResultPath));
        Assert.True(result.Success);
        Assert.Equal("app 1.1.0", File.ReadAllText(Path.Combine(install, "HD2RuntimeModBuilder.exe")));
        Assert.False(File.Exists(Path.Combine(install, "HD2RuntimeGUI.exe")));
        // Next start of the new version: the result is shown once and the staging area is cleaned up.
        var next = Service(new FakeClient { Latest = package.Release }, "1.1.0", install: install);
        await next.CheckOnStartupAsync();
        Assert.True(next.PreviousResult!.Success); Assert.Contains("1.1.0", next.PreviousResult.Message);
        Assert.False(File.Exists(next.ResultPath)); Assert.False(Directory.Exists(Path.Combine(root, "temp")));
        Assert.Equal(AppUpdateStatus.UpToDate, next.Status); Assert.False(next.ShowBanner);
    }

    // ---- 1.0.0 (HD2RuntimeGUI.exe) -> 1.0.1+ (HD2RuntimeModBuilder.exe) ------------------------------------------------------

    /// <summary>An installation laid out like the published 1.0.0 ZIP: HD2RuntimeGUI.* binaries listed in a 1.0.0 inventory,
    /// the WebView2 cache 1.0.0 created next to its exe, and files the user put there.</summary>
    private string Install100()
    {
        var install = Dir("install-100-" + Guid.NewGuid().ToString("N")[..6]);
        var files = new Dictionary<string, string>
        {
            ["HD2RuntimeGUI.exe"] = "app 1.0.0", ["HD2RuntimeGUI.dll"] = "dll 1.0.0", ["HD2RuntimeGUI.runtimeconfig.json"] = "{}", ["HD2RuntimeGUI.deps.json"] = "{}",
            ["HD2RuntimeGUI.staticwebassets.endpoints.json"] = "{}", ["HD2RuntimeGUI.Core.dll"] = "core 1.0.0", [UpdateContract.UpdaterExe] = "updater 1.0.0", ["wwwroot/app.css"] = "css 1.0.0"
        };
        var entries = files.Select(f =>
        {
            var path = Path.Combine(install, f.Key.Replace('/', Path.DirectorySeparatorChar)); Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, f.Value);
            return new InventoryEntry(f.Key, new FileInfo(path).Length, Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path))));
        }).ToArray();
        File.WriteAllBytes(Path.Combine(install, UpdateContract.InventoryFile), JsonSerializer.SerializeToUtf8Bytes(new Inventory(1, UpdateContract.Product, "1.0.0", entries), UpdateJson.Default.Inventory));
        Directory.CreateDirectory(Path.Combine(install, "HD2RuntimeGUI.exe.WebView2", "EBWebView", "Default"));
        File.WriteAllText(Path.Combine(install, "HD2RuntimeGUI.exe.WebView2", "EBWebView", "Default", "Cache.dat"), "cache");
        File.WriteAllText(Path.Combine(install, "HD2RuntimeGUI-notes.txt"), "my notes");     // user files, even with the old name,
        File.WriteAllText(Path.Combine(install, "HD2RuntimeGUI.backup.zip"), "user backup"); // are not in the inventory: untouched
        return install;
    }

    [Fact] public void Updating_a_1_0_0_installation_replaces_the_HD2RuntimeGUI_binaries()
    {
        var install = Install100();
        Directory.CreateDirectory(Path.Combine(DataRoot, "Projects", "p1"));
        File.WriteAllText(Path.Combine(DataRoot, "library.json"), "{}"); File.WriteAllText(Path.Combine(DataRoot, "Projects", "p1", "project.hd2mod.json"), "{\"p\":1}");
        var dataBefore = Snapshot(DataRoot);
        var package = BuildPackage("1.0.1");
        var (options, started) = Staged(install, package);
        var result = new Installer(started.Add, (_, _, _) => true).Run(options);

        Assert.True(result.Success, result.Message);
        // Old app-owned binaries are gone, the new ones are installed, the 1.0.0 compatibility copy is not.
        foreach (var old in new[] { "HD2RuntimeGUI.exe", "HD2RuntimeGUI.dll", "HD2RuntimeGUI.runtimeconfig.json", "HD2RuntimeGUI.deps.json", "HD2RuntimeGUI.staticwebassets.endpoints.json", "HD2RuntimeGUI.Core.dll" })
            Assert.False(File.Exists(Path.Combine(install, old)), old);
        Assert.Equal("app 1.0.1", File.ReadAllText(Path.Combine(install, "HD2RuntimeModBuilder.exe")));
        Assert.Equal("dll 1.0.1", File.ReadAllText(Path.Combine(install, "HD2RuntimeModBuilder.dll")));
        Assert.Equal("updater 1.0.1", File.ReadAllText(Path.Combine(install, UpdateContract.UpdaterExe)));
        // The installed inventory lists exactly what is installed.
        var inventory = UpdateJson.ReadInventory(Path.Combine(install, UpdateContract.InventoryFile));
        Assert.Equal("1.0.1", inventory.Version);
        Assert.DoesNotContain(inventory.Files, f => f.Path == UpdateContract.LegacyEntryPoint);
        Assert.All(inventory.Files, f => Assert.True(File.Exists(Path.Combine(install, f.Path)), f.Path));
        // User files stay; 1.0.0's WebView2 cache (app-generated, no user data) is removed.
        Assert.Equal("my notes", File.ReadAllText(Path.Combine(install, "HD2RuntimeGUI-notes.txt")));
        Assert.Equal("user backup", File.ReadAllText(Path.Combine(install, "HD2RuntimeGUI.backup.zip")));
        Assert.False(Directory.Exists(Path.Combine(install, UpdateContract.LegacyWebView2Folder)));
        Assert.False(Directory.Exists(Path.Combine(install, UpdateContract.BackupDirectory)));
        // Restart the new entry point; user data is untouched.
        Assert.Equal(Path.Combine(install, "HD2RuntimeModBuilder.exe"), Assert.Single(started).FileName);
        var dataAfter = Snapshot(DataRoot); Assert.True(dataAfter.Remove("app-update-result.json")); Assert.Equal(dataBefore, dataAfter);
    }

    [Fact] public void A_failed_1_0_0_migration_restores_and_restarts_HD2RuntimeGUI_exe()
    {
        var install = Install100(); var before = Snapshot(install);
        var (options, started) = Staged(install, BuildPackage("1.0.1"));
        var copies = 0;
        var result = new Installer(started.Add, (_, _, _) => true) { Attempts = 1, BeforeCopy = _ => { if (++copies == 4) throw new IOException("disk full"); } }.Run(options);
        Assert.False(result.Success);
        Assert.Equal(before, Snapshot(install)); // including the WebView2 cache and the user's files
        Assert.False(File.Exists(Path.Combine(install, "HD2RuntimeModBuilder.exe")));
        Assert.Equal(Path.Combine(install, "HD2RuntimeGUI.exe"), Assert.Single(started).FileName);
    }

    [Fact] public void Restart_command_prefers_the_new_entry_point()
    {
        var install = Dir("restart");
        File.WriteAllText(Path.Combine(install, "HD2RuntimeGUI.exe"), "old");
        Assert.Equal(Path.Combine(install, "HD2RuntimeGUI.exe"), Installer.RestartCommand(install).FileName);
        File.WriteAllText(Path.Combine(install, "HD2RuntimeModBuilder.exe"), "new");
        Assert.Equal(Path.Combine(install, "HD2RuntimeModBuilder.exe"), Installer.RestartCommand(install).FileName);
    }

    [Fact] public void Contract_paths_and_arguments_are_strict()
    {
        foreach (var bad in new[] { "", "..", "a/../b", "/x", "C:x", "a//b", "a/", "con.", "a\u0001b", "a*b", ".modbuilder-update-backup" }) Assert.Null(UpdateContract.SafeRelativePath(bad));
        Assert.Equal("wwwroot/css/app.css", UpdateContract.SafeRelativePath("wwwroot\\css\\app.css"));
        Assert.Throws<ArgumentException>(() => UpdateContract.Parse(["--pid", "1", "--started", "2", "--install", "relative", "--staged", "C:\\s", "--version", "1.1.0", "--result", "C:\\r.json"]));
        Assert.Throws<ArgumentException>(() => UpdateContract.Parse(["--pid", "1", "--pid", "2"]));
        Assert.Equal(UpdateContract.Arguments(new(1, 2, "C:\\a", "C:\\b", "1.1.0", "C:\\r.json")), InstallerContract.Arguments(new(1, 2, "C:\\a", "C:\\b", "1.1.0", "C:\\r.json")));
    }
}
