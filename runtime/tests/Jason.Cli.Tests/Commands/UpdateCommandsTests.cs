using System.Net;
using System.Text;
using System.Text.Json;
using Jason.Cli.Tests.Process;
using Jason.Contracts.Api;
using Jason.Contracts.Json;
using Jason.Contracts.Update;

namespace Jason.Cli.Tests.Commands;

/// <summary>
/// <c>jason update check</c> asks the release feed itself and never the runtime: the question has to be
/// answerable on a machine whose runtime will not start, and the answer is the same rule the runtime's own
/// unattended check runs. The feed here is the one handler every CLI request goes through, dispatching on the
/// address — which is how a test sees both what was asked and what was not.
/// </summary>
public class UpdateCommandsTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Check_prints_what_the_feed_says_and_never_calls_the_runtime()
    {
        using var dir = new TempPaths();

        // A runtime is right there to be asked, and is not.
        dir.WriteDescriptor(RuntimeVerbs.Descriptor("rt_LIVE"));
        var asked = new List<Uri>();
        var (env, stdout, stderr) = RuntimeVerbs.Environment(dir, FeedOnly(asked, () => Ok(Manifest("0.2.0"))), new FakeProcessControl());

        var exit = await CliApp.RunAsync(["update", "check"], env, Ct);

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Equal(string.Empty, stderr.ToString());
        var answer = JsonSerializer.Deserialize<UpdateCheckResponse>(stdout.ToString(), JasonJson.Options)!;
        Assert.True(answer.Available);
        Assert.Equal("0.2.0", answer.Latest);
        Assert.Equal(SemanticVersion.Current.ToString(), answer.Current);
        Assert.Equal("https://example.test/notes", answer.ReleaseNotesUrl);
        Assert.Equal([UpdateFeed.Default], asked);

        // One line of compact snake_case JSON, like every other verb's answer.
        Assert.DoesNotContain('\n', stdout.ToString().TrimEnd());
        Assert.Contains("\"checked_at\":", stdout.ToString(), StringComparison.Ordinal);
        Assert.Contains("\"release_notes_url\":", stdout.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_version_no_newer_than_this_one_is_not_available()
    {
        using var dir = new TempPaths();
        var (env, stdout, _) = RuntimeVerbs.Environment(dir, FeedOnly([], () => Ok(Manifest(SemanticVersion.Current.ToString()))), new FakeProcessControl());

        var exit = await CliApp.RunAsync(["update", "check"], env, Ct);

        Assert.Equal(ExitCodes.Success, exit);
        var answer = JsonSerializer.Deserialize<UpdateCheckResponse>(stdout.ToString(), JasonJson.Options)!;
        Assert.False(answer.Available);
        Assert.Equal(answer.Current, answer.Latest);
    }

    [Fact]
    public async Task A_feed_that_cannot_be_reached_is_an_error_with_a_code()
    {
        using var dir = new TempPaths();
        var (env, stdout, _) = RuntimeVerbs.Environment(dir, new FakeHandler(_ => throw new HttpRequestException("connection refused")), new FakeProcessControl());

        var exit = await CliApp.RunAsync(["update", "check"], env, Ct);

        Assert.Equal(ExitCodes.ApiError, exit);
        var error = RuntimeVerbs.Envelope(stdout.ToString());
        Assert.Equal("update_feed_unreachable", error.Code);
        Assert.True(error.Retryable);
    }

    [Fact]
    public async Task A_feed_that_answers_something_other_than_a_manifest_is_an_error_with_a_code()
    {
        using var dir = new TempPaths();
        var (env, stdout, _) = RuntimeVerbs.Environment(dir, FeedOnly([], () => Ok("this is not a manifest")), new FakeProcessControl());

        var exit = await CliApp.RunAsync(["update", "check"], env, Ct);

        Assert.Equal(ExitCodes.ApiError, exit);
        var error = RuntimeVerbs.Envelope(stdout.ToString());
        Assert.Equal("update_feed_invalid", error.Code);
        Assert.False(error.Retryable);
    }

    [Fact]
    public async Task Another_feed_is_asked_when_named_and_refused_when_it_is_plain_http_on_a_network()
    {
        using var dir = new TempPaths();
        var asked = new List<Uri>();
        var local = new Uri("http://127.0.0.1:9/manifest.json");
        var (env, stdout, _) = RuntimeVerbs.Environment(dir, FeedOnly(asked, () => Ok(Manifest("0.2.0")), local), new FakeProcessControl());

        Assert.Equal(ExitCodes.Success, await CliApp.RunAsync(["update", "check", "--feed", local.ToString()], env, Ct));
        Assert.Equal([local], asked);

        // Refused before anything is sent: the address the handler would have seen never arrives.
        stdout.GetStringBuilder().Clear();
        Assert.Equal(ExitCodes.ApiError, await CliApp.RunAsync(["update", "check", "--feed", "http://example.com/manifest.json"], env, Ct));
        Assert.Equal("update_feed_insecure", RuntimeVerbs.Envelope(stdout.ToString()).Code);
        Assert.Equal([local], asked);
    }

    [Fact]
    public async Task A_feed_that_is_not_an_address_is_a_usage_error()
    {
        using var dir = new TempPaths();
        var (env, _, stderr) = RuntimeVerbs.Environment(dir, RuntimeVerbs.NeverCalled(), new FakeProcessControl());

        var exit = await CliApp.RunAsync(["update", "check", "--feed", "not an address"], env, Ct);

        Assert.Equal(ExitCodes.Usage, exit);
        Assert.Contains("--feed", stderr.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Human_mode_says_in_a_sentence_that_something_newer_exists()
    {
        using var dir = new TempPaths();
        var (env, stdout, _) = RuntimeVerbs.Environment(dir, FeedOnly([], () => Ok(Manifest("0.2.0"))), new FakeProcessControl());

        var exit = await CliApp.RunAsync(["update", "check", "--human"], env, Ct);

        Assert.Equal(ExitCodes.Success, exit);
        var text = stdout.ToString();
        Assert.Contains("0.2.0", text, StringComparison.Ordinal);
        Assert.Contains(SemanticVersion.Current.ToString(), text, StringComparison.Ordinal);
        Assert.Contains("available", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("https://example.test/notes", text, StringComparison.Ordinal);
        Assert.DoesNotContain("{", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Human_mode_says_in_a_sentence_that_this_is_the_newest()
    {
        using var dir = new TempPaths();
        var (env, stdout, _) = RuntimeVerbs.Environment(dir, FeedOnly([], () => Ok(Manifest(SemanticVersion.Current.ToString()))), new FakeProcessControl());

        var exit = await CliApp.RunAsync(["update", "check", "--human"], env, Ct);

        Assert.Equal(ExitCodes.Success, exit);
        var text = stdout.ToString();
        Assert.Contains("up to date", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(SemanticVersion.Current.ToString(), text, StringComparison.Ordinal);
        Assert.DoesNotContain("{", text, StringComparison.Ordinal);
    }

    /// <summary>
    /// A handler that answers the feed and fails the test for anything else, the runtime included: an
    /// exception nothing in the verb catches reaches the top of the CLI as a non-zero exit and a line on stderr.
    /// </summary>
    private static HttpMessageHandler FeedOnly(List<Uri> asked, Func<HttpResponseMessage> answer, Uri? feed = null) =>
        new FakeHandler(request =>
        {
            if (request.RequestUri != (feed ?? UpdateFeed.Default))
            {
                throw new InvalidOperationException($"nothing but the feed may be asked here, and {request.RequestUri} was");
            }

            asked.Add(request.RequestUri!);
            return answer();
        });

    private static HttpResponseMessage Ok(string body) =>
        new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private static string Manifest(string version) =>
        $$$"""
        {"schema":1,"version":"{{{version}}}","published_at":"2026-09-19T08:00:00Z",
         "artifacts":{"win-x64":{"asset":"jason-win-x64.zip","sha256":"{{{new string('a', 64)}}}","size":1}},
         "release_notes_url":"https://example.test/notes"}
        """;
}
