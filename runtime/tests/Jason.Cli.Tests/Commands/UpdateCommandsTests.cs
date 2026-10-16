using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Jason.Cli.Tests.Installation;
using Jason.Cli.Tests.Process;
using Jason.Contracts.Api;
using Jason.Contracts.Json;
using Jason.Contracts.Update;
using Jason.Runtime.Tests;

namespace Jason.Cli.Tests.Commands;

/// <summary>
/// <c>jason update check</c> asks the release feed itself and never the runtime: the question has to be
/// answerable on a machine whose runtime will not start, and the answer is the same rule the runtime's own
/// unattended check runs. The feed here is the one handler every CLI request goes through, dispatching on the
/// address — which is how a test sees both what was asked and what was not.
/// </summary>
public class UpdateCommandsTests
{
    /// <summary>
    /// The release after this build, worked out from the build's own version. A number written here stops being
    /// newer at the release it names: a pull request builds <c>-dev</c>, which is older than that release, but the
    /// release itself is built with the bare version, and its tests are the ones that must pass.
    /// </summary>
    private static readonly string Newer = new SemanticVersion(SemanticVersion.Current.Major, SemanticVersion.Current.Minor + 1, 0, null).ToString();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>
    /// The two verbs that change the files beside the executable say so in their own descriptions: an update puts the new
    /// executable in place with its licence files, and a rollback puts the old one back with its own.
    /// </summary>
    [Theory]
    [InlineData("apply", "Install the newest release: download it, stop this machine's runtime, put the new executable and its licence files in place and start it again.")]
    [InlineData("rollback", "Put the version this machine updated from back: the executable it replaced with its licence files, and - if that update migrated the database - the backup it wrote.")]
    public async Task The_update_verbs_say_that_the_licence_files_move_with_the_executable(string verb, string description)
    {
        using var dir = new TempPaths();
        var output = new StringWriter();

        await CliApp.RunAsync(["update", verb, "--help"], new CliEnvironment(output, new StringWriter(), dir.Paths), Ct);

        Assert.Contains(description, System.Text.RegularExpressions.Regex.Replace(output.ToString(), @"\s+", " "), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Check_prints_what_the_feed_says_and_never_calls_the_runtime()
    {
        using var dir = new TempPaths();

        // A runtime is right there to be asked, and is not.
        dir.WriteDescriptor(RuntimeVerbs.Descriptor("rt_LIVE"));
        var asked = new List<Uri>();
        var (env, stdout, stderr) = RuntimeVerbs.Environment(dir, FeedOnly(asked, () => Ok(Manifest(Newer))), new FakeProcessControl());

        var exit = await CliApp.RunAsync(["update", "check"], env, Ct);

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Equal(string.Empty, stderr.ToString());
        var answer = JsonSerializer.Deserialize<UpdateCheckResponse>(stdout.ToString(), JasonJson.Options)!;
        Assert.True(answer.Available);
        Assert.Equal(Newer, answer.Latest);
        Assert.Equal(SemanticVersion.Current.ToString(), answer.Current);
        Assert.Equal("https://example.test/notes", answer.ReleaseNotesUrl);
        Assert.Equal([UpdateFeed.Default], asked);

        // One line of compact snake_case JSON, like every other verb's answer.
        Assert.DoesNotContain('\n', stdout.ToString().TrimEnd());
        Assert.Contains("\"checked_at\":", stdout.ToString(), StringComparison.Ordinal);
        Assert.Contains("\"release_notes_url\":", stdout.ToString(), StringComparison.Ordinal);
    }

    /// <summary>
    /// The default feed is on the product's own address, which answers every request with a redirect to where the
    /// release is stored. So the client a real CLI sends through, with no handler of a test's own, follows it: the
    /// address is asked first, then the place it named, and the answer is the release found there.
    /// </summary>
    [Fact]
    public async Task The_check_follows_the_address_to_the_release_it_redirects_to()
    {
        using var dir = new TempPaths();
        using var tree = new TempTree();
        var store = tree.NewDirectory("store");
        File.WriteAllText(Path.Combine(store, ReleaseAssets.Manifest), Manifest(Newer));
        using var address = new LoopbackReleases(store);
        var output = new StringWriter();
        var error = new StringWriter();

        var exit = await CliApp.RunAsync(
            ["update", "check", "--feed", $"{address.Releases}/latest/download/{ReleaseAssets.Manifest}"],
            new CliEnvironment(output, error, dir.Paths),
            Ct);

        Assert.True(exit == ExitCodes.Success, output.ToString() + error.ToString());
        Assert.Equal(Newer, JsonSerializer.Deserialize<UpdateCheckResponse>(output.ToString(), JasonJson.Options)!.Latest);
        Assert.Equal(
            ["GET /releases/latest/download/manifest.json", "GET /store/releases/latest/download/manifest.json"],
            address.Asked);
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
        var (env, stdout, _) = RuntimeVerbs.Environment(dir, FeedOnly(asked, () => Ok(Manifest(Newer)), local), new FakeProcessControl());

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
        var (env, stdout, _) = RuntimeVerbs.Environment(dir, FeedOnly([], () => Ok(Manifest(Newer))), new FakeProcessControl());

        var exit = await CliApp.RunAsync(["update", "check", "--human"], env, Ct);

        Assert.Equal(ExitCodes.Success, exit);
        var text = stdout.ToString();
        Assert.Contains(Newer, text, StringComparison.Ordinal);
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
    /// The answer names the command that updates this installation. On Jason's own that is <c>jason update apply</c>, and
    /// a person reading the sentence is told so.
    /// </summary>
    [Fact]
    public async Task Check_names_jason_update_apply_for_jasons_own_installation()
    {
        using var dir = new TempPaths();
        var (env, stdout, _) = RuntimeVerbs.Environment(dir, FeedOnly([], () => Ok(Manifest(Newer))), new FakeProcessControl());

        Assert.Equal(ExitCodes.Success, await CliApp.RunAsync(["update", "check"], env, Ct));
        Assert.Equal("jason update apply", (string?)JsonNode.Parse(stdout.ToString())?["upgrade_command"]);

        stdout.GetStringBuilder().Clear();
        Assert.Equal(ExitCodes.Success, await CliApp.RunAsync(["update", "check", "--human"], env, Ct));
        Assert.Contains("`jason update apply`", stdout.ToString(), StringComparison.Ordinal);
    }

    /// <summary>
    /// Under a package manager the answer names the manager's own upgrade, as one command: the field is what an agent
    /// runs between stopping the runtime and starting it, which the operating skill says, not the field.
    /// </summary>
    [Theory]
    [InlineData("Homebrew", "brew upgrade jason")]
    [InlineData("winget", "winget upgrade Reply.Jason")]
    public async Task Check_names_the_managers_upgrade_as_one_command(string manager, string upgrade)
    {
        using var dir = new TempPaths();
        using var layouts = new ManagedLayouts();
        var (env, stdout, _) = RuntimeVerbs.Environment(dir, FeedOnly([], () => Ok(Manifest(Newer))), new FakeProcessControl());

        Assert.Equal(ExitCodes.Success, await CliApp.RunAsync(["update", "check"], Managed(env, layouts, manager), Ct));

        Assert.Equal(upgrade, (string?)JsonNode.Parse(stdout.ToString())?["upgrade_command"]);
    }

    /// <summary>
    /// And a person is given the three lines to type, never one: the runtime stopped, the manager's upgrade, the runtime
    /// started. Never <c>jason update apply</c>, which refuses such an installation.
    /// </summary>
    [Theory]
    [InlineData("Homebrew", "brew upgrade jason")]
    [InlineData("winget", "winget upgrade Reply.Jason")]
    public async Task Human_mode_gives_the_managers_upgrade_as_three_lines(string manager, string upgrade)
    {
        using var dir = new TempPaths();
        using var layouts = new ManagedLayouts();
        var (env, stdout, _) = RuntimeVerbs.Environment(dir, FeedOnly([], () => Ok(Manifest(Newer))), new FakeProcessControl());

        Assert.Equal(ExitCodes.Success, await CliApp.RunAsync(["update", "check", "--human"], Managed(env, layouts, manager), Ct));

        var text = stdout.ToString();
        Assert.Contains(string.Join(Environment.NewLine, "  jason runtime stop", $"  {upgrade}", "  jason runtime start"), text, StringComparison.Ordinal);
        Assert.Contains(manager, text, StringComparison.Ordinal);
        Assert.DoesNotContain("jason update apply", text, StringComparison.Ordinal);
    }

    /// <summary>The environment, naming a planted installation that <paramref name="manager"/> owns, with its record.</summary>
    private static CliEnvironment Managed(CliEnvironment env, ManagedLayouts layouts, string manager)
    {
        if (manager == "Homebrew")
        {
            return env with { InstallPath = layouts.Homebrew().Executable, Managers = new PlantedRecords() };
        }

        var winget = layouts.Winget();
        return env with { InstallPath = winget.Executable, Managers = new PlantedRecords(winget.Registration()) };
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
