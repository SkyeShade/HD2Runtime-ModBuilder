using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using HD2RuntimeGUI.Core.GitHub;
using HD2RuntimeGUI.Core.Localization;
using HD2RuntimeGUI.Core.Models;

namespace HD2RuntimeGUI.Core.Updates;

public sealed record AppReleaseAsset(string Name, string DownloadUrl, long Size, string? Digest);
/// <summary>A stable HD2Runtime ModBuilder release that carries both the Windows package and its update manifest.</summary>
public sealed record AppRelease(string Version, string Tag, string ReleaseUrl, AppReleaseAsset Package, AppReleaseAsset Manifest);

public enum AppUpdateStatus { NotChecked, Checking, UpToDate, UpdateAvailable, Offline, RateLimited, CheckFailed }
public sealed class AppUpdateCheckException(AppUpdateStatus status, string message, Exception? inner = null) : Exception(message, inner)
{
    public AppUpdateStatus Status { get; } = status;
}

public interface IAppReleaseClient
{
    /// <summary>Newest stable release with a valid Windows package, or null when none is published.</summary>
    Task<AppRelease?> GetLatestAsync(CancellationToken ct = default);
    Task<byte[]> DownloadManifestAsync(AppRelease release, CancellationToken ct = default);
    Task DownloadPackageAsync(AppRelease release, string destination, IProgress<long>? progress = null, CancellationToken ct = default);
}

/// <summary>
/// GitHub release contract for SkyeShade/HD2Runtime-ModBuilder: tag v&lt;x.y.z&gt; (stable, not draft or prerelease) with the assets
/// HD2Runtime-ModBuilder-v&lt;x.y.z&gt;-win-x64.zip and modbuilder-update-v2.json. Releases without both assets are ignored.
/// (Releases also carry the format-1 modbuilder-update.json that HD2Runtime ModBuilder 1.0.0 reads.)
/// </summary>
public sealed class AppReleaseClient(HttpClient http) : IAppReleaseClient
{
    public const string ManifestName = "modbuilder-update-v2.json";
    /// <summary>Format-1 manifest read by HD2Runtime ModBuilder 1.0.0 (entry point HD2RuntimeGUI.exe); published for it, not read here.</summary>
    public const string LegacyManifestName = "modbuilder-update.json";
    public const long MaxPackageBytes = 512L * 1024 * 1024, MaxManifestBytes = 64 * 1024;
    public static string ReleasesUrl => $"https://api.github.com/repos/{BuildInfo.Repository}/releases?per_page=20";
    public static string PackageName(string version) => $"HD2Runtime-ModBuilder-v{version}-win-x64.zip";
    public static string ReleasePage(string tag) => $"{BuildInfo.RepositoryUrl}/releases/tag/{tag}";
    public static string ReleasesPage => $"{BuildInfo.RepositoryUrl}/releases";

    public async Task<AppRelease?> GetLatestAsync(CancellationToken ct = default)
    {
        HttpResponseMessage response;
        try { response = await http.SendAsync(GitHubHttp.Request(ReleasesUrl), HttpCompletionOption.ResponseHeadersRead, ct); }
        catch (HttpRequestException e) { throw new AppUpdateCheckException(AppUpdateStatus.Offline, CoreText.Get("Messages.Update.Unreachable"), e); }
        catch (TaskCanceledException e) when (!ct.IsCancellationRequested) { throw new AppUpdateCheckException(AppUpdateStatus.Offline, CoreText.Get("Messages.Update.Timeout"), e); }
        using (response)
        {
            if (response.StatusCode == HttpStatusCode.TooManyRequests || response.StatusCode == HttpStatusCode.Forbidden && response.Headers.TryGetValues("x-ratelimit-remaining", out var left) && left.FirstOrDefault() == "0")
                throw new AppUpdateCheckException(AppUpdateStatus.RateLimited, CoreText.Get("Messages.Update.RateLimited"));
            if (response.StatusCode == HttpStatusCode.NotFound) throw new AppUpdateCheckException(AppUpdateStatus.CheckFailed, CoreText.Get("Messages.Update.ReleaseListNotFound"));
            if (!response.IsSuccessStatusCode) throw new AppUpdateCheckException(AppUpdateStatus.CheckFailed, CoreText.Format("Messages.Update.HttpStatus", (int)response.StatusCode));
            using var buffer = new MemoryStream();
            try
            {
                await GitHubReleaseClient.CopyBoundedAsync(await response.Content.ReadAsStreamAsync(ct), buffer, 4 * 1024 * 1024, ct);
                return ParseLatest(buffer.ToArray());
            }
            catch (Exception e) when (e is JsonException or InvalidDataException or InvalidOperationException or KeyNotFoundException or FormatException)
            { throw new AppUpdateCheckException(AppUpdateStatus.CheckFailed, CoreText.Get("Messages.Update.UnexpectedReleaseList"), e); }
            catch (HttpRequestException e) { throw new AppUpdateCheckException(AppUpdateStatus.Offline, CoreText.Get("Messages.Update.Interrupted"), e); }
        }
    }

    public static AppRelease? ParseLatest(byte[] json)
    {
        using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 32 });
        if (doc.RootElement.ValueKind != JsonValueKind.Array) throw new InvalidDataException("The release list is not an array.");
        AppRelease? best = null;
        foreach (var release in doc.RootElement.EnumerateArray())
        {
            if (release.GetProperty("draft").GetBoolean() || release.GetProperty("prerelease").GetBoolean()) continue;
            var tag = release.GetProperty("tag_name").GetString() ?? "";
            if (!tag.StartsWith('v') || StableVersion(tag[1..]) is not { } version) continue;
            if (release.GetProperty("html_url").GetString() != ReleasePage(tag)) throw new InvalidDataException("A release does not belong to the ModBuilder repository.");
            AppReleaseAsset? package = null, manifest = null;
            foreach (var asset in release.GetProperty("assets").EnumerateArray())
            {
                var name = asset.GetProperty("name").GetString();
                if (name != PackageName(version.ToString()) && name != ManifestName) continue;
                var url = asset.GetProperty("browser_download_url").GetString();
                var size = asset.GetProperty("size").GetInt64();
                var digest = asset.TryGetProperty("digest", out var d) && d.ValueKind == JsonValueKind.String ? d.GetString() : null;
                if (url != $"{BuildInfo.RepositoryUrl}/releases/download/{tag}/{name}" || size <= 0 || size > (name == ManifestName ? MaxManifestBytes : MaxPackageBytes)
                    || digest != null && !Regex.IsMatch(digest, @"\Asha256:[a-f0-9]{64}\z")) throw new InvalidDataException($"Release {tag} has an invalid {name} asset.");
                if (name == ManifestName) manifest = new(name, url, size, digest); else package = new(name!, url, size, digest);
            }
            if (package == null || manifest == null) continue;
            if (best == null || version.CompareTo(SemVersion.Parse(best.Version)) > 0) best = new(version.ToString(), tag, ReleasePage(tag), package, manifest);
        }
        return best;
    }

    /// <summary>Canonical x.y.z without prerelease or build suffix, otherwise null.</summary>
    public static SemVersion? StableVersion(string value)
    {
        try { var version = SemVersion.Parse(value); return version.Prerelease == null && version.ToString() == value ? version : null; }
        catch (Exception e) when (e is FormatException or OverflowException) { return null; }
    }

    public async Task<byte[]> DownloadManifestAsync(AppRelease release, CancellationToken ct = default)
    {
        using var output = new MemoryStream();
        await DownloadAsync(release.Manifest, output, null, ct);
        return output.ToArray();
    }

    public async Task DownloadPackageAsync(AppRelease release, string destination, IProgress<long>? progress = null, CancellationToken ct = default)
    {
        await using var file = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        await DownloadAsync(release.Package, file, progress, ct);
    }

    private async Task DownloadAsync(AppReleaseAsset asset, Stream output, IProgress<long>? progress, CancellationToken ct)
    {
        using var response = await GitHubHttp.SendDownloadAsync(http, new Uri(asset.DownloadUrl), "update", ct);
        if (response.StatusCode == HttpStatusCode.NotFound) throw new InvalidDataException(CoreText.Format("Messages.Update.AssetGone", asset.Name));
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength is long length && length != asset.Size) throw new InvalidDataException(CoreText.Format("Messages.Update.AssetUnexpectedSize", asset.Name));
        await using var input = await response.Content.ReadAsStreamAsync(ct);
        var buffer = new byte[81920]; long total = 0; int read;
        while ((read = await input.ReadAsync(buffer, ct)) != 0)
        {
            total += read;
            if (total > asset.Size) throw new InvalidDataException(CoreText.Format("Messages.Update.AssetTooLarge", asset.Name));
            await output.WriteAsync(buffer.AsMemory(0, read), ct);
            progress?.Report(total);
        }
        if (total != asset.Size) throw new InvalidDataException(CoreText.Format("Messages.Update.AssetIncomplete", asset.Name));
    }
}
