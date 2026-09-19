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

    /// <summary>
    /// A body that dies after the headers. The response arrives, the status is 200, and the bytes stop coming —
    /// a connection dropped mid-transfer, which is an ordinary thing on a train or a hotel network.
    /// </summary>
    /// <remarks>
    /// The reading was outside the guard that turns a network failure into this product's own refusal, so what
    /// came out was a raw IOException: <c>jason update check</c> printed a bare line on stderr with nothing on
    /// stdout, and the runtime's checker logged a code that does not exist. A caller cannot handle what it
    /// cannot name.
    /// </remarks>
    [Fact]
    public async Task A_body_that_dies_after_the_headers_is_unreachable_like_any_other_failure()
    {
        var feed = new UpdateFeed(new HttpClient(new Recording(null, Dying())));

        var refused = await Assert.ThrowsAsync<UpdateFeedException>(() => feed.ReadAsync(UpdateFeed.Default, Ct));

        Assert.Equal("update_feed_unreachable", refused.Code);
        Assert.IsType<IOException>(refused.InnerException);
    }

    /// <summary>
    /// A feed that answers and then drips. The bound on how much is read is a bound on bytes, and bytes are not
    /// the only way to wait for ever: headers arrive, the client's own timeout is satisfied and stops applying,
    /// and one byte a minute would hold the runtime's checker and a person's <c>update check</c> until somebody
    /// pressed Ctrl+C.
    /// </summary>
    [Fact]
    public async Task A_feed_that_never_finishes_sending_is_given_up_on()
    {
        using var client = new HttpClient(new Recording(null, Silent())) { Timeout = TimeSpan.FromMilliseconds(250) };
        var feed = new UpdateFeed(client);
        var clock = System.Diagnostics.Stopwatch.StartNew();

        // Bounded by the test as well as by the code under test: without the deadline this hangs rather than
        // fails, and a test that hangs takes the suite with it instead of reporting what is wrong.
        var refused = await Assert.ThrowsAsync<UpdateFeedException>(
            () => feed.ReadAsync(UpdateFeed.Default, Ct).WaitAsync(TimeSpan.FromSeconds(10), Ct));

        Assert.Equal("update_feed_unreachable", refused.Code);
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(10), $"the read took {clock.Elapsed}, so it was not the deadline that ended it");
    }

    /// <summary>And the caller's own cancellation is still the caller's, not a feed that could not be reached.</summary>
    [Fact]
    public async Task A_caller_who_cancels_is_not_told_the_feed_was_unreachable()
    {
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();
        var feed = new UpdateFeed(new HttpClient(new Recording(null, Silent())));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => feed.ReadAsync(UpdateFeed.Default, cancelled.Token));
    }

    /// <summary>A response whose body throws as soon as it is read.</summary>
    private static HttpResponseMessage Dying() =>
        new(HttpStatusCode.OK) { Content = new StreamContent(new ThrowingStream()) };

    /// <summary>A response whose body never delivers a byte and never ends.</summary>
    private static HttpResponseMessage Silent() =>
        new(HttpStatusCode.OK) { Content = new StreamContent(new SilentStream()) };

    private sealed class ThrowingStream : Stream
    {
        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position { get => 0; set => throw new NotSupportedException(); }

        public override int Read(byte[] buffer, int offset, int count) => throw new IOException("the connection was reset");

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken) =>
            throw new IOException("the connection was reset");

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private sealed class SilentStream : Stream
    {
        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position { get => 0; set => throw new NotSupportedException(); }

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        /// <summary>Never a byte, and never an end: only the token can finish this.</summary>
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken).ConfigureAwait(false);
            return 0;
        }

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
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
