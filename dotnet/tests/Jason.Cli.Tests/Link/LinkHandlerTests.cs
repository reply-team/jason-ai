using Jason.Cli;
using Jason.Cli.Link;
using Jason.Cli.Process;
using Jason.Cli.Tests.Commands;
using Jason.Contracts.Api;

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
