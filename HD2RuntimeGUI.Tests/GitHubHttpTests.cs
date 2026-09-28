using System.Net;
using System.Text;
using HD2RuntimeGUI.Core.GitHub;
using Xunit;

namespace HD2RuntimeGUI.Tests;

public sealed class GitHubHttpTests
{
    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<Uri> Visited { get; } = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        { Visited.Add(request.RequestUri!); var response = respond(request); response.RequestMessage = request; return Task.FromResult(response); }
    }
    [Fact]
    public async Task Public_release_request_has_user_agent_and_no_token()
    {
        using var handler = new Handler(request =>
        {
            Assert.Equal("api.github.com", request.RequestUri!.Host);
            Assert.Contains("/repos/SkyeShade/HD2Runtime/releases", request.RequestUri.AbsolutePath);
            Assert.Null(request.Headers.Authorization); Assert.StartsWith("HD2Runtime-ModBuilder/", request.Headers.UserAgent.ToString());
            return new(HttpStatusCode.OK) { Content = new StringContent("""
                [{"draft":false,"prerelease":false,"tag_name":"v0.5.1","html_url":"https://github.com/SkyeShade/HD2Runtime/releases/tag/v0.5.1","assets":[{"name":"HD2Runtime-0.5.1-sdk.zip","size":1234,"browser_download_url":"https://github.com/SkyeShade/HD2Runtime/releases/download/v0.5.1/HD2Runtime-0.5.1-sdk.zip"}]}]
                """) };
        });
        using var http = new HttpClient(handler); var client = new GitHubReleaseClient(http);
        Assert.Equal("0.5.1", (await client.GetLatestAsync()).Version);
    }
    [Fact]
    public async Task Redirect_to_untrusted_host_is_rejected_before_contact()
    {
        using var env = new TestEnvironment();
        using var handler = new Handler(_ => new(HttpStatusCode.Redirect) { Headers = { Location = new Uri("https://attacker.example/sdk.zip") } });
        using var http = new HttpClient(handler); var client = new GitHubReleaseClient(http);
        Directory.CreateDirectory(env.Paths.Root);
        await Assert.ThrowsAsync<InvalidDataException>(() => client.DownloadAsync(env.GitHub.Release, Path.Combine(env.Paths.Root, "download.zip")));
        Assert.Single(handler.Visited); Assert.Equal("github.com", handler.Visited[0].Host);
    }
    [Fact]
    public async Task Download_limit_is_enforced_on_stream_without_content_length()
    {
        using var input = new MemoryStream(Encoding.UTF8.GetBytes("too large")); using var output = new MemoryStream();
        await Assert.ThrowsAsync<InvalidDataException>(() => GitHubReleaseClient.CopyBoundedAsync(input, output, 3, default));
    }
}
