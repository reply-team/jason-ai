using Jason.Cli;
using Jason.Cli.Process;
using Jason.Contracts.Api;

namespace Jason.Cli.Tests.Commands;

/// <summary>The ui verbs from the side they are typed on: what is sent, what is printed, and that a browser is opened only when the seam allows it.</summary>
public class UiCommandsTests
{
    private const string Opened = "{\"url\":\"http://127.0.0.1:52766/ui/#/attention?t=abc\",\"expires_at\":\"2026-10-16T09:01:00.000Z\"}";

    private const string TwoTokens = "{\"items\":[{\"id\":\"uit_1\",\"issued_to\":\"ada\",\"issued_at\":\"2026-10-03T09:41:00.000Z\",\"last_used_at\":\"2026-10-16T08:59:00.000Z\"},{\"id\":\"uit_2\",\"issued_to\":\"ada\",\"issued_at\":\"2026-10-08T18:20:00.000Z\",\"last_used_at\":null}]}";

    [Fact]
    public async Task Open_asks_for_a_link_prints_the_answer_and_says_the_link_is_the_persons_to_open_when_nothing_opens_browsers()
    {
        using var cli = new CliRun(Opened);
        var exit = await cli.RunAsync("ui", "open");
        Assert.Equal(ExitCodes.Success, exit);
        cli.AssertPosted(Operations.UiOpen, "{}");
        Assert.Equal(Opened, cli.Text.Trim());
        Assert.Contains("open this link", cli.Error.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Open_hands_the_link_to_the_opener_and_says_so_unless_told_not_to()
    {
        var opener = new RecordingOpener();
        using var cli = new CliRun(Opened, opener: opener);
        Assert.Equal(ExitCodes.Success, await cli.RunAsync("ui", "open"));
        Assert.Equal(["http://127.0.0.1:52766/ui/#/attention?t=abc"], opener.Urls);
        Assert.Contains("opened", cli.Error.ToString(), StringComparison.OrdinalIgnoreCase);

        using var quiet = new CliRun(Opened, opener: opener);
        Assert.Equal(ExitCodes.Success, await quiet.RunAsync("ui", "open", "--no-open"));
        Assert.Single(opener.Urls);
    }

    [Fact]
    public async Task Open_deep_links_into_one_approval_or_one_question()
    {
        using var approval = new CliRun(Opened);
        await approval.RunAsync("ui", "open", "--approval", "apr_7");
        approval.AssertPosted(Operations.UiOpen, "{\"approval_id\":\"apr_7\"}");

        using var question = new CliRun(Opened);
        await question.RunAsync("ui", "open", "--question", "dec_7");
        question.AssertPosted(Operations.UiOpen, "{\"decision_id\":\"dec_7\"}");
    }

    [Fact]
    public async Task Open_with_no_module_prints_the_runtimes_refusal_and_exits_1()
    {
        using var cli = new CliRun("{\"error\":{\"code\":\"ui_not_installed\",\"message\":\"No UI is installed.\",\"retryable\":false}}", status: System.Net.HttpStatusCode.NotFound);
        Assert.Equal(ExitCodes.ApiError, await cli.RunAsync("ui", "open"));
        Assert.Contains("ui_not_installed", cli.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Open_without_a_runtime_exits_3()
    {
        using var cli = new CliRun(descriptor: false);
        Assert.Equal(ExitCodes.RuntimeUnavailable, await cli.RunAsync("ui", "open"));
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
