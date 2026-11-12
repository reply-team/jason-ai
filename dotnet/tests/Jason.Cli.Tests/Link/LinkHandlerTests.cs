using System.Net;
using Jason.Cli;
using Jason.Cli.Link;
using Jason.Cli.Process;
using Jason.Cli.Tests.Commands;
using Jason.Cli.Tests.Human;
using Jason.Cli.Tests.Session;
using Jason.Contracts.Api;
using Jason.Contracts.Discovery;

namespace Jason.Cli.Tests.Link;

/// <summary>
/// What a click on a <c>jason:</c> link does, from the side the shell starts: one argument, read against the link table,
/// and the same <c>ui.open</c> a person's <c>jason ui open</c> sends. A link the table does not know asks nothing.
/// </summary>
public sealed class LinkHandlerTests
{
    private const string Approval = "apr_01JBZ3X7Q9K2M4N6P8R0S2T4V6";

    private const string Question = "dec_01JBZ3X7Q9K2M4N6P8R0S2T4V6";

    private const string Opened = "{\"url\":\"http://127.0.0.1:52766/ui/#/attention?t=abc\",\"expires_at\":\"2026-10-16T09:01:00.000Z\"}";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("jason://attention", "{}")]
    [InlineData("jason://attention/", "{}")]
    [InlineData("JASON://Attention", "{}")]
    [InlineData("jason://attention/approvals/" + Approval, "{\"approval_id\":\"" + Approval + "\"}")]
    [InlineData("jason://attention/approvals/" + Approval + "/", "{\"approval_id\":\"" + Approval + "\"}")]
    [InlineData("jason://attention/questions/" + Question, "{\"decision_id\":\"" + Question + "\"}")]
    public async Task A_link_opens_the_ui_as_jason_ui_open_does(string link, string body)
    {
        var opener = new RecordingOpener();
        using var cli = new CliRun(Opened, opener: opener);

        var exit = await LinkHandler.RunAsync([link], cli.Environment, Ct);

        Assert.Equal(ExitCodes.Success, exit);
        cli.AssertPosted(Operations.UiOpen, body);
        Assert.Equal(["http://127.0.0.1:52766/ui/#/attention?t=abc"], opener.Urls);
    }

    /// <summary>A link the table does not know is a usage error said in one line that never repeats it, and asks the runtime nothing.</summary>
    [Theory]
    [InlineData("jason://attention/approvals/apr_SENTINEL")]
    [InlineData("jason://elsewhere/SENTINEL")]
    [InlineData("jason://attention/SENTINEL")]
    [InlineData("jason://attention/approvals/" + Approval + "/SENTINEL")]
    [InlineData("jason://attention/questions/" + Approval + "?SENTINEL")]
    [InlineData("jason://attention/approvals/" + Approval + "%2FSENTINEL")]
    public async Task A_link_the_table_does_not_know_is_refused_without_being_repeated(string link)
    {
        var opener = new RecordingOpener();
        using var cli = new CliRun(Opened, opener: opener);

        var exit = await LinkHandler.RunAsync([link], cli.Environment, Ct);

        Assert.Equal(ExitCodes.Usage, exit);
        Assert.Equal(0, cli.Requests);
        Assert.Empty(opener.Urls);
        Assert.Empty(cli.Text);
        var error = cli.Error.ToString();
        Assert.Single(error.Split('\n', StringSplitOptions.RemoveEmptyEntries));
        Assert.DoesNotContain("SENTINEL", error, StringComparison.Ordinal);
    }

    /// <summary>The shell hands a link as one argument; anything else was not started by a click.</summary>
    [Fact]
    public async Task Anything_but_one_link_is_a_usage_error()
    {
        using var cli = new CliRun(Opened);

        Assert.Equal(ExitCodes.Usage, await LinkHandler.RunAsync(["jason://attention", "--human"], cli.Environment, Ct));
        Assert.Equal(ExitCodes.Usage, await LinkHandler.RunAsync([], cli.Environment, Ct));
        Assert.Equal(0, cli.Requests);
    }

    [Fact]
    public async Task No_ui_installed_is_the_runtime_s_refusal_and_exits_1()
    {
        using var cli = new CliRun("{\"error\":{\"code\":\"ui_not_installed\",\"message\":\"No UI is installed.\",\"retryable\":false}}", status: System.Net.HttpStatusCode.NotFound);

        Assert.Equal(ExitCodes.ApiError, await LinkHandler.RunAsync(["jason://attention"], cli.Environment, Ct));
    }

    [Fact]
    public async Task No_runtime_exits_3_as_ui_open_does()
    {
        using var cli = new CliRun(descriptor: false);

        Assert.Equal(ExitCodes.RuntimeUnavailable, await LinkHandler.RunAsync(["jason://attention/approvals/" + Approval], cli.Environment, Ct));
    }

    /// <summary>
    /// A Copy prompt link puts on the clipboard exactly what <c>--prompt</c> prints for the same exchanges, the published
    /// text, and prints nothing: the item is read by its kind's own get, and no page is opened.
    /// </summary>
    [Theory]
    [InlineData("jason://attention/approvals/" + Approval + "/prompt", "approval.get", "pending-with-preview", "approval.txt", "{\"approval_id\":\"" + Approval + "\"}")]
    [InlineData("jason://attention/questions/" + Question + "/prompt", "decision.get", "pending-with-options", "question.txt", "{\"decision_id\":\"" + Question + "\"}")]
    public async Task A_prompt_link_copies_the_published_prompt_and_prints_nothing(string link, string operation, string fixture, string published, string body)
    {
        using var run = new PromptRuntime(CopyPromptTests.Fixture(operation, fixture), CopyPromptTests.Fixture("campaign.get", "in-project-with-folder"));
        var clipboard = new RecordingClipboard();

        var exit = await LinkHandler.RunAsync([link], run.Environment(clipboard), Ct);

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Equal([operation, "campaign.get"], run.Asked);
        Assert.Equal(body, run.Bodies[0]);
        var text = File.ReadAllText(Path.Combine(CopyPromptTests.RepositoryRoot(), "docs", "prompts", published)).Replace("\r\n", "\n", StringComparison.Ordinal);
        Assert.Equal([text.TrimEnd('\n')], clipboard.Texts.Select(copied => copied.TrimEnd('\n')));
        Assert.Empty(run.Output.ToString());
        Assert.Empty(run.Opener.Urls);
    }

    /// <summary>Where nothing can write the clipboard, nothing is asked and nothing is copied.</summary>
    [Fact]
    public async Task A_prompt_link_with_no_clipboard_asks_nothing_and_exits_1()
    {
        using var run = new PromptRuntime(CopyPromptTests.Fixture("approval.get", "pending-with-preview"));

        var exit = await LinkHandler.RunAsync(["jason://attention/approvals/" + Approval + "/prompt"], run.Environment(clipboard: null), Ct);

        Assert.Equal(ExitCodes.ApiError, exit);
        Assert.Empty(run.Asked);
        Assert.Single(run.Error.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries));
        Assert.Empty(run.Output.ToString());
    }

    [Fact]
    public async Task A_clipboard_that_refuses_exits_1_and_says_why()
    {
        using var run = new PromptRuntime(CopyPromptTests.Fixture("approval.get", "pending-with-preview"), CopyPromptTests.Fixture("campaign.get", "in-project-with-folder"));
        var clipboard = new RecordingClipboard { Refusal = "clipboard_busy" };

        var exit = await LinkHandler.RunAsync(["jason://attention/approvals/" + Approval + "/prompt"], run.Environment(clipboard), Ct);

        Assert.Equal(ExitCodes.ApiError, exit);
        Assert.Contains("clipboard_busy", run.Error.ToString(), StringComparison.Ordinal);
        Assert.Empty(run.Output.ToString());
    }

    /// <summary>An item the runtime does not know is its refusal, and the clipboard keeps what it held.</summary>
    [Fact]
    public async Task An_item_the_runtime_refuses_copies_nothing_and_exits_1()
    {
        using var run = new PromptRuntime(item: null);
        var clipboard = new RecordingClipboard();

        var exit = await LinkHandler.RunAsync(["jason://attention/questions/" + Question + "/prompt"], run.Environment(clipboard), Ct);

        Assert.Equal(ExitCodes.ApiError, exit);
        Assert.Equal(["decision.get"], run.Asked);
        Assert.Empty(clipboard.Texts);
        Assert.Empty(run.Output.ToString());
    }

    /// <summary>An answer that names no item to compose a prompt for copies nothing.</summary>
    [Fact]
    public async Task An_answer_the_prompt_cannot_read_copies_nothing_and_exits_1()
    {
        using var run = new PromptRuntime(item: "{}");
        var clipboard = new RecordingClipboard();

        var exit = await LinkHandler.RunAsync(["jason://attention/approvals/" + Approval + "/prompt"], run.Environment(clipboard), Ct);

        Assert.Equal(ExitCodes.ApiError, exit);
        Assert.Empty(clipboard.Texts);
    }

    [Fact]
    public async Task A_prompt_link_with_no_runtime_exits_3()
    {
        using var run = new PromptRuntime(item: null, descriptor: false);
        var clipboard = new RecordingClipboard();

        var exit = await LinkHandler.RunAsync(["jason://attention/approvals/" + Approval + "/prompt"], run.Environment(clipboard), Ct);

        Assert.Equal(ExitCodes.RuntimeUnavailable, exit);
        Assert.Empty(clipboard.Texts);
    }

    /// <summary>
    /// A session link starts what <c>jason session open</c> starts for the same item: the item read by its kind's own get,
    /// the host the preference names in one new terminal at the campaign's folder, the brief as its first input, and no
    /// page opened.
    /// </summary>
    [Theory]
    [InlineData("jason://attention/approvals/" + Approval + "/session", "approval.get", "{\"approval_id\":\"" + Approval + "\"}", "approval.txt")]
    [InlineData("jason://attention/questions/" + Question + "/session", "decision.get", "{\"decision_id\":\"" + Question + "\"}", "question.txt")]
    public async Task A_session_link_starts_the_session_jason_session_open_starts(string link, string operation, string body, string published)
    {
        using var run = new SessionOpenerTests.SessionRuntime();
        var claude = run.Programs.AddProgram("claude");

        var exit = await run.Link(link);

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Equal([operation, "campaign.get", "preference.list"], run.Asked);
        Assert.Equal(body, run.Bodies[0]);
        var launch = Assert.Single(run.Terminals.Launches);
        var text = File.ReadAllText(Path.Combine(CopyPromptTests.RepositoryRoot(), "docs", "prompts", published)).Replace("\r\n", "\n", StringComparison.Ordinal).TrimEnd('\n');
        Assert.Equal([run.ConsoleHost, "--", claude, text.Replace(SessionOpenerTests.SessionRuntime.RecordedFolder, run.CampaignFolder, StringComparison.Ordinal)], launch.Argv);
        Assert.Equal(run.CampaignFolder, launch.WorkingDirectory);
    }

    /// <summary>An item that no longer waits starts nothing from a link either.</summary>
    [Fact]
    public async Task A_session_link_on_an_item_that_no_longer_waits_starts_nothing()
    {
        using var run = new SessionOpenerTests.SessionRuntime();
        var decided = System.Text.Json.Nodes.JsonNode.Parse(run.Item!)!;
        decided["status"] = "approved";
        run.Item = decided.ToJsonString();
        run.Programs.AddProgram("claude");

        var exit = await run.Link("jason://attention/approvals/" + Approval + "/session");

        Assert.Equal(ExitCodes.ApiError, exit);
        Assert.Empty(run.Terminals.Launches);
    }

    /// <summary>
    /// Where the desktop registered its own scheme for this account and the program it names is there, a link that opens
    /// the UI opens the desktop instead: its address made from the parsed link, never from the link's text, handed to the
    /// same opener the browser's address goes to, and the runtime asked nothing.
    /// </summary>
    [Theory]
    [InlineData("jason://attention", "jason-desktop://attention")]
    [InlineData("JASON://Attention/", "jason-desktop://attention")]
    [InlineData("jason://attention/approvals/" + Approval, "jason-desktop://attention/approvals/" + Approval)]
    [InlineData("jason://attention/approvals/" + Approval + "/", "jason-desktop://attention/approvals/" + Approval)]
    [InlineData("jason://attention/questions/" + Question, "jason-desktop://attention/questions/" + Question)]
    public async Task A_link_opens_the_desktop_where_it_is_registered_and_present(string link, string address)
    {
        var opener = new HandingOpener(UrlHandOff.Handed);
        using var cli = new CliRun(Opened, opener: opener);
        var env = cli.Environment with { Desktop = new FakeRegistration(Quoted(Program(cli.Environment))) };

        var exit = await LinkHandler.RunAsync([link], env, Ct);

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Equal([address], opener.Urls);
        Assert.Equal(0, cli.Requests);
        Assert.Empty(cli.Text);
        Assert.Single(cli.Error.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries));
    }

    /// <summary>The registration as Electron writes it for a path with nothing to quote is the desktop's too.</summary>
    [Fact]
    public async Task An_unquoted_program_path_is_read_as_the_desktop_s()
    {
        var opener = new HandingOpener(UrlHandOff.Handed);
        using var cli = new CliRun(Opened, opener: opener);
        var program = Path.Combine(cli.Environment.Paths.Root, "Jason.exe");
        File.WriteAllText(program, "not a program");
        var env = cli.Environment with { Desktop = new FakeRegistration(program + " -- \"%1\"") };

        Assert.Equal(ExitCodes.Success, await LinkHandler.RunAsync(["jason://attention"], env, Ct));
        Assert.Equal(["jason-desktop://attention"], opener.Urls);
    }

    /// <summary>
    /// No registration, a program that is gone, or a command that is not the desktop's form: the browser UI opens as it
    /// always has, and nothing taken from the registry is started or handed on.
    /// </summary>
    [Theory]
    [InlineData("none")]
    [InlineData("gone")]
    [InlineData("relative")]
    [InlineData("empty")]
    [InlineData("other-form")]
    [InlineData("unclosed-quote")]
    public async Task Without_the_desktop_registered_and_present_the_browser_ui_opens(string registration)
    {
        var opener = new HandingOpener(UrlHandOff.Handed);
        using var cli = new CliRun(Opened, opener: opener);
        var present = Program(cli.Environment);
        var command = registration switch
        {
            "none" => null,
            "gone" => Quoted(Path.Combine(cli.Environment.Paths.Root, "removed", "Jason.exe")),
            "relative" => "\"Jason.exe\" -- \"%1\"",
            "empty" => string.Empty,
            "other-form" => "\"" + present + "\" --inspect \"%1\"",
            _ => "\"" + present + " -- \"%1\"",
        };
        var env = cli.Environment with { Desktop = new FakeRegistration(command) };

        var exit = await LinkHandler.RunAsync(["jason://attention/approvals/" + Approval], env, Ct);

        Assert.Equal(ExitCodes.Success, exit);
        cli.AssertPosted(Operations.UiOpen, "{\"approval_id\":\"" + Approval + "\"}");
        Assert.Equal(["http://127.0.0.1:52766/ui/#/attention?t=abc"], opener.Urls);
        Assert.DoesNotContain("desktop", cli.Error.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// An opener that does not hand the desktop's address on — it did not start, it exited with a failure, or it did not
    /// finish in time — opens the browser UI as before, and says so on stderr by the failure's code.
    /// </summary>
    [Theory]
    [InlineData(UrlHandOff.NotStarted, "opener_not_started")]
    [InlineData(UrlHandOff.Failed, "opener_failed")]
    [InlineData(UrlHandOff.TimedOut, "opener_timed_out")]
    public async Task A_desktop_the_opener_could_not_reach_falls_back_to_the_browser_ui(UrlHandOff failure, string code)
    {
        var opener = new HandingOpener(failure);
        using var cli = new CliRun(Opened, opener: opener);
        var env = cli.Environment with { Desktop = new FakeRegistration(Quoted(Program(cli.Environment))) };

        var exit = await LinkHandler.RunAsync(["jason://attention/questions/" + Question], env, Ct);

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Equal(["jason-desktop://attention/questions/" + Question, "http://127.0.0.1:52766/ui/#/attention?t=abc"], opener.Urls);
        cli.AssertPosted(Operations.UiOpen, "{\"decision_id\":\"" + Question + "\"}");
        Assert.Contains(code, cli.Error.ToString(), StringComparison.Ordinal);
    }

    /// <summary>A Copy prompt link opens no page, so a registered desktop changes nothing about it.</summary>
    [Fact]
    public async Task A_prompt_link_hands_nothing_to_the_desktop()
    {
        using var run = new PromptRuntime(CopyPromptTests.Fixture("approval.get", "pending-with-preview"), CopyPromptTests.Fixture("campaign.get", "in-project-with-folder"));
        var clipboard = new RecordingClipboard();
        var baseline = run.Environment(clipboard);
        var env = baseline with { Desktop = new FakeRegistration(Quoted(Program(baseline))) };

        Assert.Equal(ExitCodes.Success, await LinkHandler.RunAsync(["jason://attention/approvals/" + Approval + "/prompt"], env, Ct));
        Assert.Empty(run.Opener.Urls);
        Assert.Single(clipboard.Texts);
    }

    /// <summary>The desktop's program, made in a folder with a space, as an installation under the profile can be.</summary>
    private static string Program(CliEnvironment env)
    {
        var program = Path.Combine(env.Paths.Root, "Jason Desktop", "Jason.exe");
        Directory.CreateDirectory(Path.GetDirectoryName(program)!);
        File.WriteAllText(program, "not a program");
        return program;
    }

    /// <summary>The command as the desktop registers it: the quoted path, <c>--</c>, and the quoted address.</summary>
    private static string Quoted(string program) => "\"" + program + "\" -- \"%1\"";

    private sealed class FakeRegistration(string? command) : IDesktopRegistration
    {
        public string? Command() => command;
    }

    /// <summary>Hands the desktop's address on with the outcome it was given, and a browser's address as started.</summary>
    private sealed class HandingOpener(UrlHandOff desktop) : IUrlOpener
    {
        public List<string> Urls { get; } = [];

        public bool Open(string url)
        {
            Urls.Add(url);
            return true;
        }

        public UrlHandOff HandOn(string url, TimeSpan wait)
        {
            Assert.True(wait > TimeSpan.Zero);
            Urls.Add(url);
            return desktop;
        }
    }

    /// <summary>A runtime that answers the item's get and the campaign's, and refuses every other read.</summary>
    private sealed class PromptRuntime(string? item, string? campaign = null, bool descriptor = true) : IDisposable
    {
        private static readonly RuntimeDescriptor Live = new("v1", "0.1.0-dev", "rt_LIVE", 77, "http://127.0.0.1:5000", "the-token", DateTimeOffset.UnixEpoch);

        private readonly TempPaths _dir = new();

        public List<string> Asked { get; } = [];

        public List<string> Bodies { get; } = [];

        public StringWriter Output { get; } = new();

        public StringWriter Error { get; } = new();

        public RecordingOpener Opener { get; } = new();

        public CliEnvironment Environment(IClipboard? clipboard)
        {
            if (descriptor)
            {
                _dir.WriteDescriptor(Live);
            }

            var handler = new FakeHandler(request =>
            {
                var operation = request.RequestUri!.AbsolutePath.Split('/')[^1];
                Asked.Add(operation);
                Bodies.Add(request.Content?.ReadAsStringAsync(CancellationToken.None).GetAwaiter().GetResult() ?? string.Empty);
                var answer = operation switch
                {
                    "approval.get" or "decision.get" => item,
                    "campaign.get" => campaign,
                    _ => null,
                };
                return answer is null
                    ? new HttpResponseMessage(HttpStatusCode.NotFound) { Content = new StringContent("{\"error\":{\"code\":\"not_found\",\"message\":\"Not found.\",\"retryable\":false}}") }
                    : new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(answer, System.Text.Encoding.UTF8, "application/json") };
            });
            return new CliEnvironment(Output, Error, _dir.Paths, handler, Opener: Opener, Clipboard: clipboard);
        }

        public void Dispose() => _dir.Dispose();
    }

    private sealed class RecordingClipboard : IClipboard
    {
        public List<string> Texts { get; } = [];

        public string? Refusal { get; init; }

        public bool TrySetText(string text, out string? problem)
        {
            problem = Refusal;
            if (Refusal is null)
            {
                Texts.Add(text);
            }

            return Refusal is null;
        }
    }

    private sealed class RecordingOpener : IUrlOpener
    {
        public List<string> Urls { get; } = [];

        public bool Open(string url)
        {
            Urls.Add(url);
            return true;
        }
    }
}
