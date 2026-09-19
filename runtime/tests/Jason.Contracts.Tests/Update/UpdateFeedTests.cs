using System.Net;
using System.Text;
using Jason.Contracts;
using Jason.Contracts.Update;

namespace Jason.Contracts.Tests.Update;

/// <summary>
/// The one place in this product that opens a socket to somewhere that is not the local runtime. What it sends,
/// where it will send it, and what it does with an answer that is not a manifest.
/// </summary>
public class UpdateFeedTests
{
    private const string Digest = "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>
    /// A person asked "does this send anything about me?" should be able to read the answer in one test. One
    /// GET, one header saying which version is asking, no body, no query, nothing else.
    /// </summary>
    [Fact]
    public async Task The_request_says_which_version_is_asking_and_carries_nothing_else()
    {
        var seen = new List<HttpRequestMessage>();
        var feed = Feed(seen, Ok(Manifest()));

        await feed.ReadAsync(UpdateFeed.Default, Ct);

        var request = Assert.Single(seen);
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Null(request.Content);
        Assert.Equal(UpdateFeed.Default, request.RequestUri);
        Assert.Equal($"jason/{JasonVersion.Current}", request.Headers.UserAgent.ToString());
        Assert.Equal(["User-Agent"], request.Headers.Select(header => header.Key));
    }

    [Fact]
    public async Task What_comes_back_is_the_release_it_describes()
    {
        var manifest = await Feed(null, Ok(Manifest("0.5.0"))).ReadAsync(UpdateFeed.Default, Ct);

        Assert.Equal(SemanticVersion.Parse("0.5.0"), manifest.Version);
    }

    /// <summary>
    /// The default feed is the one URL that resolves for every release there will ever be, which is why the
    /// asset names carry no version.
    /// </summary>
    [Fact]
    public void The_default_feed_is_the_latest_release_of_the_public_repository() =>
        Assert.Equal(
            "https://github.com/reply-team/jason-ai/releases/latest/download/manifest.json",
            UpdateFeed.Default.ToString());

    [Fact]
    public void A_pinned_version_is_read_from_that_release_rather_than_from_the_latest() =>
        Assert.Equal(
            "https://github.com/reply-team/jason-ai/releases/download/v0.2.0/manifest.json",
            UpdateFeed.PinnedFor(UpdateFeed.Default, SemanticVersion.Parse("0.2.0")).ToString());

    /// <summary>
    /// The download is composed from the feed's own directory and a name the manifest was allowed to carry —
    /// never from a URL inside the document. A feed that could name where to fetch from could name anywhere.
    /// </summary>
    [Fact]
    public void A_download_is_built_from_the_feeds_own_base_and_a_validated_name() =>
        Assert.Equal(
            "https://github.com/reply-team/jason-ai/releases/latest/download/jason-win-x64.zip",
            UpdateFeed.DownloadUrlFor(UpdateFeed.Default, ReleaseAssets.For("win-x64")).ToString());

    [Theory]
    [InlineData("../../../evil.zip")]
    [InlineData("https://elsewhere.example/evil.zip")]
    [InlineData("")]
    public void A_download_name_that_is_not_a_file_name_is_refused(string asset) =>
        Assert.Throws<UpdateFeedException>(() => UpdateFeed.DownloadUrlFor(UpdateFeed.Default, asset));

    /// <summary>
    /// https only, with loopback allowed so that a test can stand a stub in front of this without the stub
    /// being the reason the rule is relaxed for everybody.
    /// </summary>
    [Theory]
    [InlineData("https://example.com/manifest.json", true)]
    [InlineData("http://127.0.0.1:5000/manifest.json", true)]
    [InlineData("http://localhost:5000/manifest.json", true)]
    [InlineData("http://[::1]:5000/manifest.json", true)]
    [InlineData("http://example.com/manifest.json", false)]
    [InlineData("http://169.254.169.254/manifest.json", false)]
    [InlineData("ftp://example.com/manifest.json", false)]
    public async Task Only_https_and_loopback_are_ever_fetched(string url, bool allowed)
    {
        var seen = new List<HttpRequestMessage>();
        var feed = Feed(seen, Ok(Manifest()));

        if (allowed)
        {
            await feed.ReadAsync(new Uri(url), Ct);
            Assert.Single(seen);
            return;
        }

        var refused = await Assert.ThrowsAsync<UpdateFeedException>(() => feed.ReadAsync(new Uri(url), Ct));
        Assert.Equal("update_feed_insecure", refused.Code);
        Assert.Empty(seen);
    }

    [Fact]
    public async Task A_file_url_is_not_a_feed()
    {
        var refused = await Assert.ThrowsAsync<UpdateFeedException>(
            () => Feed(null, Ok(Manifest())).ReadAsync(new Uri("file:///c:/manifest.json"), Ct));

        Assert.Equal("update_feed_insecure", refused.Code);
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.Forbidden)]
    public async Task A_page_that_does_not_answer_with_a_manifest_is_unreachable_rather_than_invalid(HttpStatusCode status)
    {
        var refused = await Assert.ThrowsAsync<UpdateFeedException>(
            () => Feed(null, new HttpResponseMessage(status)).ReadAsync(UpdateFeed.Default, Ct));

        Assert.Equal("update_feed_unreachable", refused.Code);
    }

    [Fact]
    public async Task A_machine_that_is_offline_is_unreachable()
    {
        var feed = new UpdateFeed(new HttpClient(new Throwing(new HttpRequestException("no such host is known"))));

        var refused = await Assert.ThrowsAsync<UpdateFeedException>(() => feed.ReadAsync(UpdateFeed.Default, Ct));

        Assert.Equal("update_feed_unreachable", refused.Code);
    }

    [Fact]
    public async Task A_request_that_times_out_is_unreachable_too()
    {
        var feed = new UpdateFeed(new HttpClient(new Throwing(new TaskCanceledException("the request timed out"))));

        var refused = await Assert.ThrowsAsync<UpdateFeedException>(() => feed.ReadAsync(UpdateFeed.Default, Ct));

        Assert.Equal("update_feed_unreachable", refused.Code);
    }

    [Fact]
    public async Task A_page_that_answers_with_something_else_is_invalid()
    {
        var refused = await Assert.ThrowsAsync<UpdateFeedException>(
            () => Feed(null, Ok("<html>not here</html>")).ReadAsync(UpdateFeed.Default, Ct));

        Assert.Equal("update_feed_invalid", refused.Code);
    }

    /// <summary>
    /// A feed that answers with a gigabyte is answered with a refusal rather than with memory. The body is read
    /// to one byte past the bound and no further.
    /// </summary>
    [Fact]
    public async Task A_body_past_the_bound_is_refused_without_being_held()
    {
        var enormous = "{\"pad\":\"" + new string('p', UpdateManifest.MaxBytes * 2) + "\"}";

        var refused = await Assert.ThrowsAsync<UpdateFeedException>(
            () => Feed(null, Ok(enormous)).ReadAsync(UpdateFeed.Default, Ct));

        Assert.Equal("update_feed_invalid", refused.Code);
        Assert.Contains("longer than", refused.Message, StringComparison.Ordinal);
    }

    private static UpdateFeed Feed(List<HttpRequestMessage>? seen, HttpResponseMessage answer) =>
        new(new HttpClient(new Recording(seen, answer)));

    private static HttpResponseMessage Ok(string body) =>
        new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private static string Manifest(string version = "0.2.0") =>
        $$"""
        {
          "schema": 1,
          "version": "{{version}}",
          "published_at": "2026-09-19T08:00:00Z",
          "release_notes_url": null,
          "min_upgrade_from": null,
          "artifacts": {
            "win-x64": { "asset": "jason-win-x64.zip", "sha256": "{{Digest}}", "size": 114254557 }
          }
        }
        """;

    private sealed class Recording(List<HttpRequestMessage>? seen, HttpResponseMessage answer) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            seen?.Add(request);
            return Task.FromResult(answer);
        }
    }

    private sealed class Throwing(Exception error) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw error;
    }
}
