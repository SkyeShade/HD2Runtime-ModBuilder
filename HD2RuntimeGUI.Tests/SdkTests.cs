using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using HD2RuntimeGUI.Core.GitHub;
using HD2RuntimeGUI.Core.Metadata;
using HD2RuntimeGUI.Core.Models;
using HD2RuntimeGUI.Core.Services;
using Xunit;

namespace HD2RuntimeGUI.Tests;

public sealed class SdkTests
{
    [Fact]
    public async Task Latest_compatible_skips_newer_unsupported_schema_without_installing()
    {
        using var env = new TestEnvironment();
        var incompatible = FakeGitHub.MakeArchive("0.7.0", schema: 2);
        env.GitHub.CandidateArchives["0.7.0"] = incompatible;
        env.GitHub.Candidates = [FakeGitHub.MakeRelease("0.7.0", incompatible), env.GitHub.Release];
        var status = await env.Updates.CheckAsync();
        Assert.Equal("0.6.0", status.Latest!.Version); Assert.True(status.VerifiedOnline);
        Assert.Contains("Skipped 1", status.Message); Assert.Equal("0.5.1", (await env.Cache.GetCurrentAsync()).Version);
        Assert.False(File.Exists(env.Paths.SdkFile("0.6.0")));
    }
    [Fact]
    public void Cache_path_traversal_rejected()
    { using var env = new TestEnvironment(); Assert.Throws<InvalidDataException>(() => env.Paths.CachePath("..", "evil.json")); Assert.Throws<FormatException>(() => env.Paths.SdkFile("../../escape")); }
    [Theory]
    [InlineData("0.5.1", "0.6.0", -1)]
    [InlineData("1.10.0", "1.9.9", 1)]
    [InlineData("1.0.0-rc.2", "1.0.0-rc.10", -1)]
    [InlineData("1.0.0-rc.1", "1.0.0", -1)]
    [InlineData("1.0.0+build1", "1.0.0+build2", 0)]
    public void Semantic_versions_compare_numerically(string a, string b, int expected) => Assert.Equal(expected, Math.Sign(SemVersion.Parse(a).CompareTo(SemVersion.Parse(b))));
    [Theory]
    [InlineData("01.0.0")][InlineData("1.2")][InlineData("1.0.0-01")][InlineData("../../1.0.0")]
    public void Invalid_versions_rejected(string value) => Assert.Throws<FormatException>(() => SemVersion.Parse(value));

    private static byte[] Releases(params (string Version, bool Draft, bool Prerelease, string Repository)[] items) => JsonSerializer.SerializeToUtf8Bytes(items.Select(x => new
    {
        tag_name = "v" + x.Version, draft = x.Draft, prerelease = x.Prerelease,
        html_url = $"https://github.com/{x.Repository}/releases/tag/v{x.Version}",
        assets = new[] { new { name = $"HD2Runtime-{x.Version}-sdk.zip", size = 1234,
            browser_download_url = $"https://github.com/{x.Repository}/releases/download/v{x.Version}/HD2Runtime-{x.Version}-sdk.zip", digest = "sha256:" + new string('a', 64) } }
    }));
    [Fact]
    public void Release_parser_filters_drafts_and_prereleases_and_orders_semantically()
    {
        var parsed = GitHubReleaseClient.ParseReleases(Releases(("0.5.1", false, false, GitHubReleaseClient.Repository), ("0.7.0", true, false, GitHubReleaseClient.Repository), ("0.8.0-rc.1", false, true, GitHubReleaseClient.Repository), ("0.6.0", false, false, GitHubReleaseClient.Repository)));
        Assert.Equal(2, parsed.Count);
        Assert.Equal("0.6.0", parsed.MaxBy(x => SemVersion.Parse(x.Version))!.Version);
    }
    [Fact]
    public void Unexpected_repository_rejected() => Assert.Throws<InvalidDataException>(() => GitHubReleaseClient.ParseReleases(Releases(("0.6.0", false, false, "attacker/HD2Runtime"))));
    [Fact]
    public async Task Current_sdk_detects_newer_release()
    {
        using var env = new TestEnvironment(); var status = await env.Updates.CheckAsync();
        Assert.True(status.VerifiedOnline); Assert.True(status.UpdateAvailable); Assert.Equal("0.5.1", status.Installed.Version);
        env.GitHub.Release = FakeGitHub.MakeRelease("0.5.1", env.GitHub.Archive);
        Assert.False((await env.Updates.CheckAsync()).UpdateAvailable);
    }
    [Fact]
    public async Task Ignore_this_time_is_single_use_and_does_not_disable_next_check()
    {
        using var env = new TestEnvironment(); var first = await env.Updates.BeginCreationAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => env.Updates.ResolveCreationAsync(first, UpdateDecision.UseInstalled));
        var sdk = await env.Updates.ResolveCreationAsync(first, UpdateDecision.IgnoreThisTime);
        Assert.Equal("0.5.1", sdk.Version);
        await Assert.ThrowsAsync<InvalidOperationException>(() => env.Updates.ResolveCreationAsync(first, UpdateDecision.IgnoreThisTime));
        Assert.True((await env.Updates.BeginCreationAsync()).Status.UpdateAvailable); Assert.Equal(2, env.GitHub.Checks);
    }
    [Fact]
    public async Task Update_installation_validates_then_switches_and_retains_old_version()
    {
        using var env = new TestEnvironment(); var ticket = await env.Updates.BeginCreationAsync();
        Assert.Equal("0.6.0", (await env.Updates.ResolveCreationAsync(ticket, UpdateDecision.InstallUpdate)).Version);
        Assert.Equal("0.6.0", (await env.Cache.GetCurrentAsync()).Version);
        Assert.Equal("0.5.1", (await env.Cache.GetVersionAsync("0.5.1")).Version);
        Assert.Empty(Directory.GetFiles(env.Paths.Sdk, "staging-*"));
    }
    [Theory]
    [InlineData("download")][InlineData("digest")][InlineData("schema")][InlineData("version")]
    public async Task Failed_update_preserves_current_sdk(string mode)
    {
        using var env = new TestEnvironment(); await env.Cache.GetCurrentAsync();
        if (mode == "download") env.GitHub.FailDownload = true;
        if (mode == "digest") env.GitHub.Release = env.GitHub.Release with { Sha256 = "sha256:" + new string('0', 64) };
        if (mode == "schema") { env.GitHub.Archive = FakeGitHub.MakeArchive("0.6.0", schema: 2); env.GitHub.Release = FakeGitHub.MakeRelease("0.6.0", env.GitHub.Archive); }
        if (mode == "version") { env.GitHub.Archive = FakeGitHub.MakeArchive("0.7.0"); env.GitHub.Release = FakeGitHub.MakeRelease("0.6.0", env.GitHub.Archive); }
        await Assert.ThrowsAnyAsync<Exception>(() => env.Cache.InstallAsync(env.GitHub.Release));
        Assert.Equal("0.5.1", (await env.Cache.GetCurrentAsync()).Version);
        Assert.Empty(Directory.GetFiles(env.Paths.Sdk, "staging-*"));
    }
    [Fact]
    public async Task Offline_uses_cached_release_and_sdk()
    {
        using var env = new TestEnvironment(); await env.Updates.CheckAsync(); env.GitHub.Offline = true;
        var status = await env.Updates.CheckAsync();
        Assert.False(status.VerifiedOnline); Assert.Equal("0.6.0", status.Latest!.Version); Assert.Equal("0.5.1", status.Installed.Version);
        Assert.Contains("Offline", status.Message);
    }
    [Fact]
    public async Task First_launch_offline_uses_bundled_metadata()
    { using var env = new TestEnvironment(); env.GitHub.Offline = true; var status = await env.Updates.CheckAsync(); Assert.Null(status.Latest); Assert.Equal("0.5.1", status.Installed.Version); }
    [Theory]
    [InlineData("schema_version", 2)][InlineData("api_version", 2)]
    public void Unsupported_metadata_rejected(string key, int version)
    { var json = JsonNode.Parse(SdkCache.BundledMetadata())!; json[key] = version; Assert.Throws<UnsupportedSdkException>(() => new MetadataReader().Read(Encoding.UTF8.GetBytes(json.ToJsonString()))); }
    [Theory]
    [InlineData("{}")][InlineData("{broken}")][InlineData("{\"schema_version\":1,\"schema_version\":2}")]
    public void Malformed_metadata_rejected(string json) => Assert.Throws<InvalidDataException>(() => new MetadataReader().Read(Encoding.UTF8.GetBytes(json)));
    [Fact]
    public void Metadata_cannot_inject_lua_method_names()
    { var json = JsonNode.Parse(SdkCache.BundledMetadata())!; json["builders"]!["weapon);os.execute('x')"] = "weapon"; Assert.Throws<InvalidDataException>(() => new MetadataReader().Read(Encoding.UTF8.GetBytes(json.ToJsonString()))); }
    [Theory]
    [InlineData("../evil")][InlineData("/evil")][InlineData("C:/evil")][InlineData("sdk/../../evil")][InlineData("sdk\\evil")][InlineData("sdk/file:stream")]
    public async Task Malicious_zip_entry_rejected_without_installing(string entry)
    {
        using var env = new TestEnvironment(); await env.Cache.GetCurrentAsync();
        env.GitHub.Archive = FakeGitHub.MakeArchive("0.6.0", entry); env.GitHub.Release = FakeGitHub.MakeRelease("0.6.0", env.GitHub.Archive);
        await Assert.ThrowsAsync<InvalidDataException>(() => env.Cache.InstallAsync(env.GitHub.Release)); Assert.Equal("0.5.1", (await env.Cache.GetCurrentAsync()).Version);
    }
}
