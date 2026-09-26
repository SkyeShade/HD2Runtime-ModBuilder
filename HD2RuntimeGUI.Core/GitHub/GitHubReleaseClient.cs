using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;
using HD2RuntimeGUI.Core.Models;

namespace HD2RuntimeGUI.Core.GitHub;

public sealed record SdkRelease(string Version, string AssetName, string DownloadUrl, long Size, string? Sha256, string ReleaseUrl);
public interface IGitHubReleaseClient
{
    Task<SdkRelease> GetLatestAsync(CancellationToken ct = default);
    async Task<IReadOnlyList<SdkRelease>> GetReleasesAsync(CancellationToken ct = default) => [await GetLatestAsync(ct)];
    Task DownloadAsync(SdkRelease release, string destination, CancellationToken ct = default);
}

public sealed class GitHubReleaseClient(HttpClient http) : IGitHubReleaseClient
{
    public const string Repository = "SkyeShade/HD2Runtime";
    public const int MaxDownloadBytes = 32 * 1024 * 1024;
    public async Task<SdkRelease> GetLatestAsync(CancellationToken ct = default) => (await GetReleasesAsync(ct)).First();
    public async Task<IReadOnlyList<SdkRelease>> GetReleasesAsync(CancellationToken ct = default)
    {
        var releases = new List<SdkRelease>();
        // Bounded pagination: ignore drafts/prereleases and runtime-only assets.
        for (int page = 1; page <= 5; page++)
        {
            using var request = Request($"https://api.github.com/repos/{Repository}/releases?per_page=100&page={page}");
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
            response.EnsureSuccessStatusCode();
            using var buffer = new MemoryStream();
            await CopyBoundedAsync(await response.Content.ReadAsStreamAsync(ct), buffer, 4 * 1024 * 1024, ct);
            var bytes = buffer.ToArray();
            releases.AddRange(ParseReleases(bytes));
            using var doc = JsonDocument.Parse(bytes);
            if (doc.RootElement.GetArrayLength() < 100) break;
        }
        if (releases.Count == 0) throw new InvalidDataException("No stable HD2Runtime SDK release was found.");
        return releases.OrderByDescending(r => SemVersion.Parse(r.Version)).ToArray();
    }

    public static IReadOnlyList<SdkRelease> ParseReleases(byte[] json)
    {
        using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 32 });
        var result = new List<SdkRelease>();
        foreach (var release in doc.RootElement.EnumerateArray())
        {
            if (release.GetProperty("draft").GetBoolean() || release.GetProperty("prerelease").GetBoolean()) continue;
            var tag = release.GetProperty("tag_name").GetString()!;
            SemVersion version;
            try { version = SemVersion.Parse(tag.StartsWith('v') ? tag[1..] : tag); }
            catch (Exception e) when (e is FormatException or OverflowException) { continue; }
            if (version.Prerelease != null) continue;
            var releaseUrl = release.GetProperty("html_url").GetString()!;
            if (releaseUrl != $"https://github.com/{Repository}/releases/tag/{tag}") throw new InvalidDataException("Unexpected release repository.");
            var name = $"HD2Runtime-{version}-sdk.zip";
            foreach (var asset in release.GetProperty("assets").EnumerateArray())
            {
                if (asset.GetProperty("name").GetString() != name) continue;
                var url = asset.GetProperty("browser_download_url").GetString()!;
                var digest = asset.TryGetProperty("digest", out var d) ? d.GetString() : null;
                var item = new SdkRelease(version.ToString(), name, url, asset.GetProperty("size").GetInt64(), digest, releaseUrl);
                Validate(item); result.Add(item);
            }
        }
        return result;
    }

    public static void Validate(SdkRelease release)
    {
        var version = SemVersion.Parse(release.Version);
        if (version.Prerelease != null || version.ToString() != release.Version || release.AssetName != $"HD2Runtime-{version}-sdk.zip" || release.Size <= 0 || release.Size > MaxDownloadBytes)
            throw new InvalidDataException("Invalid SDK release asset.");
        var tag = release.ReleaseUrl.Split('/').Last();
        if (tag != "v" + version && tag != version.ToString()) throw new InvalidDataException("Invalid release tag.");
        if (release.ReleaseUrl != $"https://github.com/{Repository}/releases/tag/{tag}" || release.DownloadUrl != $"https://github.com/{Repository}/releases/download/{tag}/{release.AssetName}")
            throw new InvalidDataException("Unexpected SDK download repository.");
        if (release.Sha256 != null && !Regex.IsMatch(release.Sha256, @"\Asha256:[a-fA-F0-9]{64}\z")) throw new InvalidDataException("Unsupported or malformed asset digest.");
    }

    public async Task DownloadAsync(SdkRelease release, string destination, CancellationToken ct = default)
    {
        Validate(release);
        using var response = await SendDownloadAsync(new Uri(release.DownloadUrl), ct);
        response.EnsureSuccessStatusCode();
        var final = response.RequestMessage!.RequestUri!;
        if (final.Scheme != "https" || (final.Host != "github.com" && final.Host != "release-assets.githubusercontent.com" && final.Host != "objects.githubusercontent.com"))
            throw new InvalidDataException("Unexpected download redirect.");
        if (response.Content.Headers.ContentLength is long size && size != release.Size) throw new InvalidDataException("Asset size mismatch.");
        await using var file = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        await CopyBoundedAsync(await response.Content.ReadAsStreamAsync(ct), file, release.Size, ct);
        if (file.Length != release.Size) throw new InvalidDataException("Incomplete SDK download.");
    }

    private async Task<HttpResponseMessage> SendDownloadAsync(Uri uri, CancellationToken ct)
    {
        for (int redirects = 0; redirects <= 5; redirects++)
        {
            if (uri.Scheme != "https" || (uri.Host != "github.com" && uri.Host != "release-assets.githubusercontent.com" && uri.Host != "objects.githubusercontent.com") || !string.IsNullOrEmpty(uri.UserInfo) || !uri.IsDefaultPort)
                throw new InvalidDataException("Unexpected SDK redirect destination.");
            using var request = Request(uri.AbsoluteUri);
            var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
            if ((int)response.StatusCode is not (301 or 302 or 303 or 307 or 308)) return response;
            var location = response.Headers.Location;
            response.Dispose();
            if (location == null) throw new InvalidDataException("SDK redirect is missing its location.");
            uri = location.IsAbsoluteUri ? location : new Uri(uri, location);
        }
        throw new InvalidDataException("Too many SDK redirects.");
    }

    private static HttpRequestMessage Request(string url)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.UserAgent.ParseAdd("HD2RuntimeGUI/0.1.0");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");
        return request;
    }
    public static async Task CopyBoundedAsync(Stream input, Stream output, long limit, CancellationToken ct)
    {
        using (input)
        {
            var buffer = new byte[81920]; long total = 0; int read;
            while ((read = await input.ReadAsync(buffer, ct)) != 0)
            { total += read; if (total > limit) throw new InvalidDataException("Download exceeds the size limit."); await output.WriteAsync(buffer.AsMemory(0, read), ct); }
        }
    }
}
