using Jason.Cli;
using Jason.Cli.Link;
using Jason.Cli.Process;
using Jason.Cli.Tests.Link;
using Jason.Contracts.Api;
using Jason.Runtime.Tests;

namespace Jason.Cli.Tests.Commands;

/// <summary>
/// The ui verbs from the side they are typed on: what is sent, what is printed, and what is handed on. <c>open</c> hands
/// Jason Desktop its own address and asks the runtime nothing; <c>pair</c> is the runtime's <c>ui.open</c> and opens
/// nothing.
/// </summary>
public class UiCommandsTests
{
    private const string Approval = "apr_01JBZ3X7Q9K2M4N6P8R0S2T4V6";

    private const string Question = "dec_01JBZ3X7Q9K2M4N6P8R0S2T4V6";

    private const string Opened = "{\"url\":\"http://127.0.0.1:52766/ui/#/attention?t=abc\",\"expires_at\":\"2026-10-16T09:01:00.000Z\"}";

    private const string TwoTokens = "{\"items\":[{\"id\":\"uit_1\",\"issued_to\":\"ada\",\"issued_at\":\"2026-10-03T09:41:00.000Z\",\"last_used_at\":\"2026-10-16T08:59:00.000Z\"},{\"id\":\"uit_2\",\"issued_to\":\"ada\",\"issued_at\":\"2026-10-08T18:20:00.000Z\",\"last_used_at\":null}]}";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>
    /// With Jason Desktop registered, open hands the system the desktop's own address — what waits, or one item by its
    /// checked id — prints it, and asks no runtime, so it opens with none running too. Breaks if the verb asks the runtime
    /// for a browser's link again, or makes an address of anything but the link table's rows.
    /// </summary>
    [Theory]
    [InlineData(new string[0], "jason-desktop://attention")]
    [InlineData(new[] { "--approval", Approval }, "jason-desktop://attention/approvals/" + Approval)]
    [InlineData(new[] { "--question", Question }, "jason-desktop://attention/questions/" + Question)]
    public async Task Open_hands_jason_desktop_its_address_and_asks_the_runtime_nothing(string[] options, string address)
    {
        var opener = new HandingOpener();
        using var cli = new CliRun(Opened, descriptor: false, opener: opener);
        var env = cli.Environment with { Desktop = FakeRegistration.Present(cli.Environment) };

        var exit = await CliApp.RunAsync(["ui", "open", .. options], env, Ct);

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Equal([address], opener.Urls);
        Assert.Equal(0, cli.Requests);
        Assert.Equal("{\"address\":\"" + address + "\"}", cli.Text.Trim());
        Assert.Equal("Opened in Jason Desktop.", cli.Error.ToString().Trim());
    }

    /// <summary>--no-open prints the address for an agent to hand on and hands nothing to the system; --human prints the address alone.</summary>
    [Fact]
    public async Task Open_with_no_open_prints_the_address_and_hands_nothing_on()
    {
        var opener = new HandingOpener();
        using var cli = new CliRun(Opened, opener: opener);
        var env = cli.Environment with { Desktop = FakeRegistration.Present(cli.Environment) };

        Assert.Equal(ExitCodes.Success, await CliApp.RunAsync(["ui", "open", "--approval", Approval, "--no-open"], env, Ct));
        Assert.Equal(ExitCodes.Success, await CliApp.RunAsync(["ui", "open", "--no-open", "--human"], env, Ct));

        Assert.Empty(opener.Urls);
        Assert.Equal(0, cli.Requests);
        Assert.Equal(["{\"address\":\"jason-desktop://attention/approvals/" + Approval + "\"}", "jason-desktop://attention"], cli.Text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
    }

    /// <summary>
    /// Where no Jason Desktop is registered, or its registration names a program that is gone, open hands nothing on — no
    /// browser either — and refuses in one sentence and a code of its own, with or without --no-open. Breaks if a browser's
    /// link comes back as the fallback, or an address is printed that nothing here opens.
    /// </summary>
    [Theory]
    [InlineData("none", false)]
    [InlineData("gone", false)]
    [InlineData("none", true)]
    public async Task Without_jason_desktop_open_opens_nothing_and_refuses_in_words(string registration, bool noOpen)
    {
        var opener = new HandingOpener();
        using var cli = new CliRun(Opened, opener: opener);
        var env = cli.Environment with
        {
            Desktop = registration == "none" ? null : new FakeRegistration(Path.Combine(cli.Environment.Paths.Root, "removed", "Jason.exe")),
        };

        var exit = await CliApp.RunAsync(noOpen ? ["ui", "open", "--no-open"] : ["ui", "open"], env, Ct);

        Assert.Equal(ExitCodes.ApiError, exit);
        Assert.Empty(opener.Urls);
        Assert.Equal(0, cli.Requests);
        Assert.Contains("\"code\":\"" + CliErrors.DesktopNotInstalled + "\"", cli.Text, StringComparison.Ordinal);
        Assert.Equal(DesktopLinks.NotInstalled, cli.Error.ToString().Trim());
    }

    /// <summary>
    /// A hand-on the system's opener did not complete opens nothing else: one that did not start or failed is a refusal said
    /// by its code; one still running at the wait's end may yet open the desktop, so it is said and not refused.
    /// </summary>
    [Theory]
    [InlineData(UrlHandOff.NotStarted, "opener_not_started", ExitCodes.ApiError)]
    [InlineData(UrlHandOff.Failed, "opener_failed", ExitCodes.ApiError)]
    [InlineData(UrlHandOff.TimedOut, "opener_timed_out", ExitCodes.Success)]
    public async Task A_hand_on_that_does_not_complete_is_said_by_its_code_and_opens_nothing_else(UrlHandOff outcome, string code, int expected)
    {
        var opener = new HandingOpener(outcome);
        using var cli = new CliRun(Opened, opener: opener);
        var env = cli.Environment with { Desktop = FakeRegistration.Present(cli.Environment) };

        Assert.Equal(expected, await CliApp.RunAsync(["ui", "open", "--question", Question], env, Ct));

        Assert.Equal(["jason-desktop://attention/questions/" + Question], opener.Urls);
        Assert.Equal(0, cli.Requests);
        Assert.Contains(code, cli.Error.ToString(), StringComparison.Ordinal);
    }

    /// <summary>An id of the other kind or of no id's shape, or both options at once, is a usage error and hands nothing on.</summary>
    [Theory]
    [InlineData("--approval", Question)]
    [InlineData("--question", Approval)]
    [InlineData("--approval", "apr_7")]
    [InlineData("--approval", "apr_01JBZ3X7Q9K2M4N6P8R0S2T4V6/../../feed")]
    public async Task Open_refuses_an_id_that_is_not_one_of_its_kind(string option, string id)
    {
        var opener = new HandingOpener();
        using var cli = new CliRun(Opened, opener: opener);
        var env = cli.Environment with { Desktop = FakeRegistration.Present(cli.Environment) };

        Assert.Equal(ExitCodes.Usage, await CliApp.RunAsync(["ui", "open", option, id], env, Ct));
        Assert.Equal(ExitCodes.Usage, await CliApp.RunAsync(["ui", "open", "--approval", Approval, "--question", Question], env, Ct));

        Assert.Empty(opener.Urls);
        Assert.Empty(cli.Text);
    }

    /// <summary>
    /// With a build of one's own installed, --web is the runtime's ui.open: the link pairs one browser once and goes to the
    /// default browser, or is printed under --no-open. Breaks if the door for a web interface of one's own closes with the
    /// browser's other doors.
    /// </summary>
    [Fact]
    public async Task Open_web_with_a_build_of_ones_own_pairs_a_browser_and_opens_it()
    {
        var opener = new HandingOpener();
        using var cli = new CliRun(Opened, opener: opener);
        OwnBuild(cli.Environment);

        Assert.Equal(ExitCodes.Success, await cli.RunAsync("ui", "open", "--web", "--approval", Approval));
        cli.AssertPosted(Operations.UiOpen, "{\"approval_id\":\"" + Approval + "\"}");
        Assert.Equal(Opened, cli.Text.Trim());
        Assert.Equal(["http://127.0.0.1:52766/ui/#/attention?t=abc"], opener.Urls);
        Assert.Contains("browser", cli.Error.ToString(), StringComparison.Ordinal);

        using var quiet = new CliRun(Opened, opener: opener);
        OwnBuild(quiet.Environment);
        Assert.Equal(ExitCodes.Success, await quiet.RunAsync("ui", "open", "--web", "--no-open"));
        quiet.AssertPosted(Operations.UiOpen, "{}");
        Assert.Single(opener.Urls);
    }

    /// <summary>
    /// With no build of one's own, --web asks the runtime nothing and opens no browser, and says so in words: the
    /// release's own build is never opened in a browser. Breaks if --web becomes the old ui open again.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Open_web_without_a_build_of_ones_own_opens_nothing_and_refuses_in_words(bool defaultBuild)
    {
        var opener = new HandingOpener();
        using var cli = new CliRun(Opened, opener: opener);
        if (defaultBuild)
        {
            Build(cli.Environment.Paths.UiDefaultDirectory);
        }

        Assert.Equal(ExitCodes.ApiError, await cli.RunAsync("ui", "open", "--web"));

        Assert.Equal(0, cli.Requests);
        Assert.Empty(opener.Urls);
        Assert.Contains("\"code\":\"" + CliErrors.CustomUiNotInstalled + "\"", cli.Text, StringComparison.Ordinal);
        Assert.Equal($"No web UI of your own is installed ({cli.Environment.Paths.UiCustomDirectory}); Jason runs in Jason Desktop.", cli.Error.ToString().Trim());
    }

    /// <summary>
    /// Under JASON_CONFINED, set on every chat turn of Jason Desktop, the whole ui noun stays refused, its two new forms
    /// with it: open, whose window the chat already is, and pair, which hands out a link with the runtime's rights. Breaks if
    /// the chat can open a page or get a pairing link through either.
    /// </summary>
    [Theory]
    [InlineData("open")]
    [InlineData("open", "--web", "--no-open")]
    [InlineData("pair")]
    public async Task Confined_the_new_ui_verbs_are_refused_with_the_rest_of_the_noun(params string[] verb)
    {
        var opener = new HandingOpener();
        using var cli = new CliRun(Opened, opener: opener);
        OwnBuild(cli.Environment);
        var env = cli.Environment with
        {
            Desktop = FakeRegistration.Present(cli.Environment),
            Variables = new Dictionary<string, string> { ["JASON_CONFINED"] = "1" },
        };

        Assert.Equal(ExitCodes.ApiError, await CliApp.RunAsync(["ui", .. verb], env, Ct));

        Assert.Empty(opener.Urls);
        Assert.Equal(0, cli.Requests);
        Assert.Equal(ConfinedVerbs.Sentence, cli.Error.ToString().Trim());
    }

    /// <summary>A build of one's own, as the runtime serves one: an index and a manifest in the custom directory.</summary>
    private static void OwnBuild(CliEnvironment env) => Build(env.Paths.UiCustomDirectory);

    private static void Build(string directory)
    {
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, Jason.Contracts.Ui.UiAssetLayout.IndexFile), "<!doctype html>");
        File.WriteAllText(Path.Combine(directory, Jason.Contracts.Ui.UiAssetLayout.ManifestFile), "{}");
    }

    /// <summary>pair is the runtime's ui.open: it prints the link that pairs one page once and opens nothing, whatever opener is there.</summary>
    [Fact]
    public async Task Pair_asks_for_a_link_prints_the_answer_and_opens_nothing()
    {
        var opener = new HandingOpener();
        using var cli = new CliRun(Opened, opener: opener);
        var env = cli.Environment with { Desktop = FakeRegistration.Present(cli.Environment) };

        Assert.Equal(ExitCodes.Success, await CliApp.RunAsync(["ui", "pair"], env, Ct));

        cli.AssertPosted(Operations.UiOpen, "{}");
        Assert.Equal(Opened, cli.Text.Trim());
        Assert.Empty(opener.Urls);
    }

    [Fact]
    public async Task Pair_deep_links_into_one_approval_or_one_question()
    {
        using var approval = new CliRun(Opened);
        await approval.RunAsync("ui", "pair", "--approval", "apr_7");
        approval.AssertPosted(Operations.UiOpen, "{\"approval_id\":\"apr_7\"}");

        using var question = new CliRun(Opened);
        await question.RunAsync("ui", "pair", "--question", "dec_7");
        question.AssertPosted(Operations.UiOpen, "{\"decision_id\":\"dec_7\"}");
    }

    [Fact]
    public async Task Pair_with_no_module_prints_the_runtimes_refusal_and_exits_1()
    {
        using var cli = new CliRun("{\"error\":{\"code\":\"ui_not_installed\",\"message\":\"No UI is installed.\",\"retryable\":false}}", status: System.Net.HttpStatusCode.NotFound);
        Assert.Equal(ExitCodes.ApiError, await cli.RunAsync("ui", "pair"));
        Assert.Contains("ui_not_installed", cli.Text, StringComparison.Ordinal);
    }

    [Fact]
    [Critical(Family.R2, "exit 3 for the UI verb")]
    public async Task Pair_without_a_runtime_exits_3()
    {
        using var cli = new CliRun(descriptor: false);
        Assert.Equal(ExitCodes.RuntimeUnavailable, await cli.RunAsync("ui", "pair"));
    }

    [Fact]
    public async Task Tokens_lists_and_renders_for_people()
    {
        using var cli = new CliRun(TwoTokens);
        Assert.Equal(ExitCodes.Success, await cli.RunAsync("ui", "tokens", "--human"));
        cli.AssertPosted(Operations.UiTokens, "{}");
        Assert.Contains("uit_1", cli.Text, StringComparison.Ordinal);
        Assert.Contains("PAIRED", cli.Text, StringComparison.Ordinal);
        Assert.Contains("never", cli.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Revoke_takes_one_id_or_all_and_refuses_neither_and_both()
    {
        using var one = new CliRun("{\"revoked\":1}");
        Assert.Equal(ExitCodes.Success, await one.RunAsync("ui", "revoke", "uit_1"));
        one.AssertPosted(Operations.UiRevoke, "{\"token_id\":\"uit_1\"}");

        using var all = new CliRun("{\"revoked\":2}");
        Assert.Equal(ExitCodes.Success, await all.RunAsync("ui", "revoke", "--all"));
        all.AssertPosted(Operations.UiRevoke, "{\"all\":true}");

        using var neither = new CliRun();
        Assert.Equal(ExitCodes.Usage, await neither.RunAsync("ui", "revoke"));
        using var both = new CliRun();
        Assert.Equal(ExitCodes.Usage, await both.RunAsync("ui", "revoke", "uit_1", "--all"));
    }
}
