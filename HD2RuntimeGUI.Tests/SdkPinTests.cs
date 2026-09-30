using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using HD2RuntimeGUI.Core.Metadata;
using HD2RuntimeGUI.Core.Services;
using Xunit;

namespace HD2RuntimeGUI.Tests;

// ModBuilder 1.4.2 is built against HD2Runtime 0.28.1 (1.4.0 and 1.4.1: 0.28.0). The fixture sdk-<SdkPin.Version>.zip is that release asset; the
// bundled SDK is its files byte for byte, a newer bundled SDK becomes current on upgrade, and a same-version build that differs (a local
// sdk/ folder of a later Runtime commit) is always reported as a different build.
public sealed class SdkPinTests
{
    private static string Fixture => Path.Combine(AppContext.BaseDirectory, "Fixtures", "sdk-" + SdkPin.Version + ".zip");
    private static string Current(TestEnvironment e) => (string)JsonNode.Parse(File.ReadAllText(e.Paths.CachePath("current.json")))!["version"]!;

    [Fact] public void The_fixture_is_the_pinned_release_asset_and_the_bundle_is_its_files()
    {
        Assert.Equal(SdkPin.ArchiveSha256, Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Fixture))).ToLowerInvariant());
        Assert.Equal(SdkPin.Version, SdkCache.BundledVersion.Value);
        Assert.Equal(SdkPin.Version, SdkCompatibility.NewestSupportedVersion);
        using var zip = ZipFile.OpenRead(Fixture);
        byte[] Entry(string name) { using var s = zip.GetEntry(name)!.Open(); using var m = new MemoryStream(); s.CopyTo(m); return m.ToArray(); }
        var bundled = SdkCache.BundledComposition().ToDictionary();
        bundled["metadata.json"] = SdkCache.BundledMetadata();
        bundled[PlayerWeaponCatalogReader.FileName] = SdkCache.BundledCapabilities();
        bundled[PlayerWeaponAmmoCatalogReader.FileName] = SdkCache.BundledAmmoCapabilities();
        foreach (var (name, bytes) in bundled)
            Assert.Equal(Entry(name == Core.Scripting.LuaApiIndex.CacheName ? Core.Scripting.LuaApiIndex.StubPath : name), bytes);
        // Every file ModBuilder reads from the release is bundled (nothing falls back to an older SDK).
        Assert.Contains(EventCatalogReader.FileName, bundled.Keys); Assert.Contains(AttackOutputReader.FileName, bundled.Keys);
        Assert.Contains(EnemyAuthoringReader.FileName, bundled.Keys); Assert.Contains(Core.Scripting.LuaApiIndex.CacheName, bundled.Keys);
        Assert.Equal(SdkPin.ContentFingerprint, SdkPin.Fingerprint(bundled));
    }

    [Fact] public async Task The_published_release_installs_as_the_pinned_build()
    {
        using var e = new TestEnvironment();
        var sdk = await SdkFixtures.Install(e, SdkPin.Version);
        Assert.Equal(SdkPin.Version, sdk.Version); Assert.Equal(SdkPin.ApiVersion, sdk.ApiVersion);
        Assert.True(sdk.IsPinnedBuild); Assert.Equal(SdkPin.ContentFingerprint, sdk.ContentFingerprint);
        Assert.True((await e.Cache.GetVersionAsync(SdkPin.Version)).IsPinnedBuild);
        // Older SDKs have nothing to compare against.
        Assert.Null((await SdkFixtures.Install(e, "0.27.0")).IsPinnedBuild);
    }

    [Fact] public async Task A_fresh_start_uses_the_bundled_pinned_sdk()
    {
        using var e = new TestEnvironment(); File.Delete(e.Paths.CachePath("current.json")); e.GitHub.Offline = true;
        var status = await e.Updates.CheckAsync();
        Assert.Equal(SdkPin.Version, status.Installed.Version); Assert.True(status.Installed.IsPinnedBuild);
    }

    [Fact] public async Task An_upgrade_adopts_the_newer_bundled_sdk_and_keeps_older_projects_on_theirs()
    {
        using var e = new TestEnvironment();
        var old = await SdkFixtures.Install(e, "0.27.0"); var w = e.Workspace();
        await w.CreateAsync(new("Made with 1.3", "Tests", "mods/tests/made_with_13", "0.1.0"), old);
        Assert.Equal("0.27.0", Current(e));
        // The new ModBuilder starts: its bundled SDK is newer than the cached current one, so new projects use it.
        var upgraded = new SdkCache(e.Paths, e.Reader, e.GitHub); e.GitHub.Offline = true;
        var status = await new SdkUpdateService(upgraded, e.GitHub, e.Paths).CheckAsync();
        Assert.Equal(SdkPin.Version, status.Installed.Version); Assert.Equal(SdkPin.Version, Current(e)); Assert.True(status.Installed.IsPinnedBuild);
        // The older SDK stays cached and the older project stays pinned to it.
        Assert.Equal("0.27.0", (await upgraded.GetVersionAsync("0.27.0")).Version);
        var reopened = new BuilderWorkspace(e.Store, e.Projects, upgraded, new SdkUpdateService(upgraded, e.GitHub, e.Paths), e.Changes, e.Generator, e.Exporter, e.Desktop, e.Desktop, e.Paths);
        await reopened.OpenAsync(w.Project!.Id); Assert.Equal("0.27.0", reopened.Project!.SdkVersion); Assert.Null(reopened.BuildError);
        // A later start does not switch again (current is already the bundled version).
        var again = await new SdkCache(e.Paths, e.Reader, e.GitHub).GetCurrentAsync(); Assert.Equal(SdkPin.Version, again.Version);
    }

    [Fact] public async Task A_local_build_with_the_pinned_version_but_other_content_is_never_taken_for_the_pin()
    {
        using var e = new TestEnvironment();
        var dir = Path.Combine(e.Paths.Root, "local-sdk"); ZipFile.ExtractToDirectory(Fixture, dir);
        var exact = new SdkCache(e.Paths, e.Reader, e.GitHub) { LocalSdkPath = dir };
        var status = await new SdkUpdateService(exact, e.GitHub, e.Paths).CheckAsync();
        Assert.True(status.Installed.IsPinnedBuild); Assert.Contains("byte-identical to the pinned HD2Runtime " + SdkPin.Version + " release", status.Message);
        // Same version, one catalog differs (a later development build of the pinned version).
        var events = Path.Combine(dir, EventCatalogReader.FileName); File.AppendAllText(events, "\n");
        var other = new SdkCache(e.Paths, e.Reader, e.GitHub) { LocalSdkPath = dir };
        status = await new SdkUpdateService(other, e.GitHub, e.Paths).CheckAsync();
        Assert.Equal(SdkPin.Version, status.Installed.Version); Assert.False(status.Installed.IsPinnedBuild);
        Assert.Contains("is not the pinned HD2Runtime " + SdkPin.Version + " release", status.Message);
        // A local SDK is never cached, so it cannot replace the pinned release for later runs.
        Assert.False(Directory.Exists(Path.GetDirectoryName(e.Paths.SdkFile(SdkPin.Version))));
    }
}
