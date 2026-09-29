using System.IO.Compression;
using HD2RuntimeGUI.Core.Metadata;
using HD2RuntimeGUI.Core.Services;
using Xunit;

namespace HD2RuntimeGUI.Tests;

// Developer-only local SDK override (HD2RUNTIME_SDK_PATH / --sdk-path): an unpublished Runtime build is validated in memory and
// served for this run, without GitHub and without writing to the SDK cache.
public sealed class LocalSdkTests
{
    private static string Extract(TestEnvironment e, string version)
    {
        var dir = Path.Combine(e.Paths.Root, "local-sdk-" + version);
        ZipFile.ExtractToDirectory(Path.Combine(AppContext.BaseDirectory, "Fixtures", $"sdk-{version}.zip"), dir); return dir;
    }
    private static (SdkCache Cache, SdkUpdateService Updates, BuilderWorkspace Workspace) Local(TestEnvironment e, string path)
    {
        var cache = new SdkCache(e.Paths, e.Reader, e.GitHub) { LocalSdkPath = path }; var updates = new SdkUpdateService(cache, e.GitHub, e.Paths);
        return (cache, updates, new BuilderWorkspace(e.Store, e.Projects, cache, updates, e.Changes, e.Generator, e.Exporter, e.Desktop, e.Desktop, e.Paths));
    }
    private static string[] CacheFiles(TestEnvironment e) => Directory.GetFiles(e.Paths.Sdk, "*", SearchOption.AllDirectories).Select(f => f + ":" + new FileInfo(f).Length).Order().ToArray();

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Local_sdk_directory_or_zip_is_validated_and_served_without_touching_the_cache(bool zip)
    {
        using var e = new TestEnvironment(); var before = CacheFiles(e); var pointer = await File.ReadAllTextAsync(e.Paths.CachePath("current.json"));
        var path = zip ? Path.Combine(AppContext.BaseDirectory, "Fixtures", "sdk-0.24.0.zip") : Extract(e, "0.24.0");
        var (cache, updates, _) = Local(e, path);
        var status = await updates.CheckAsync();
        Assert.Equal("0.24.0", status.Installed.Version); Assert.Equal(Path.GetFullPath(path), status.LocalSource);
        Assert.False(status.VerifiedOnline); Assert.Null(status.Latest); Assert.Contains("Local SDK 0.24.0", status.Message);
        Assert.Equal(0, e.GitHub.Checks);
        Assert.NotNull(status.Installed.Entities!.Boosters); Assert.NotNull(status.Installed.Stratagems);
        Assert.Same(status.Installed, await cache.GetVersionAsync("0.24.0"));
        // The legacy cached SDK is still served for its own version, and nothing was written to the cache.
        Assert.Equal("0.5.1", (await cache.GetVersionAsync("0.5.1")).Version);
        Assert.Equal(before, CacheFiles(e)); Assert.Equal(pointer, await File.ReadAllTextAsync(e.Paths.CachePath("current.json")));
        await Assert.ThrowsAsync<InvalidOperationException>(() => cache.InstallAsync(FakeGitHub.MakeRelease("0.24.0", SdkFixtures.Archive("0.24.0"))));
    }

    [Fact] public async Task An_invalid_local_sdk_fails_full_validation()
    {
        using var e = new TestEnvironment(); var dir = Extract(e, "0.24.0");
        File.Delete(Path.Combine(dir, BoosterAuthoringReader.FileName));
        var (_, updates, _) = Local(e, dir);
        var error = await Assert.ThrowsAsync<InvalidDataException>(() => updates.CheckAsync());
        Assert.Contains("booster", error.Message, StringComparison.OrdinalIgnoreCase);
        await Assert.ThrowsAsync<InvalidDataException>(() => Local(e, Path.Combine(e.Paths.Root, "missing")).Updates.CheckAsync());
    }

    // Loads the real unpublished Runtime build when HD2RUNTIME_SDK_PATH points at it (e.g. HD2Runtime\sdk); otherwise no-op.
    [Fact] public async Task Configured_local_runtime_sdk_loads()
    {
        var path = Environment.GetEnvironmentVariable("HD2RUNTIME_SDK_PATH");
        if (string.IsNullOrWhiteSpace(path)) return;
        using var e = new TestEnvironment(); var (_, updates, _) = Local(e, path);
        var sdk = (await updates.CheckAsync()).Installed;
        Assert.NotNull(sdk.PlayerWeapons); Assert.NotNull(sdk.Entities?.Boosters); Assert.NotNull(sdk.SupportLinks);
        // Development SDKs for 0.28.0 also publish enemy and structure authoring.
        if (File.Exists(Path.Combine(path, EnemyAuthoringReader.FileName))) Assert.NotEmpty(sdk.Entities!.Enemies!.Classes);
    }
}
