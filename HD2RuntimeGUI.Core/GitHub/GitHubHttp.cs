using System.Net.Http.Headers;

namespace HD2RuntimeGUI.Core.GitHub;

/// <summary>Public, token-free GitHub requests shared by the SDK and application release clients.</summary>
public static class GitHubHttp
{
    private static readonly string[] DownloadHosts = ["github.com", "release-assets.githubusercontent.com", "objects.githubusercontent.com"];

    public static HttpRequestMessage Request(string url)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.UserAgent.ParseAdd(BuildInfo.UserAgent);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");
        return request;
    }

    public static bool TrustedDownload(Uri uri) =>
        uri.Scheme == "https" && DownloadHosts.Contains(uri.Host) && string.IsNullOrEmpty(uri.UserInfo) && uri.IsDefaultPort;

    /// <summary>Follows at most five redirects, each validated against GitHub's release download hosts before it is requested.</summary>
    public static async Task<HttpResponseMessage> SendDownloadAsync(HttpClient http, Uri uri, string subject, CancellationToken ct)
    {
        for (int redirects = 0; redirects <= 5; redirects++)
        {
            if (!TrustedDownload(uri)) throw new InvalidDataException($"Unexpected {subject} redirect destination.");
            using var request = Request(uri.AbsoluteUri);
            var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
            if ((int)response.StatusCode is not (301 or 302 or 303 or 307 or 308)) return response;
            var location = response.Headers.Location;
            response.Dispose();
            if (location == null) throw new InvalidDataException($"{subject} redirect is missing its location.");
            uri = location.IsAbsoluteUri ? location : new Uri(uri, location);
        }
        throw new InvalidDataException($"Too many {subject} redirects.");
    }
}
