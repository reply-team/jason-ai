using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using Jason.Cli;
using Jason.Contracts.Api;
using Jason.Contracts.Json;

namespace Jason.Cli.Tests.Commands;

/// <summary><c>jason reply record</c>: one verb, one operation, every option a field of the request and nothing else.</summary>
public class ReplyCommandsTests
{
    private static readonly DateTimeOffset Moment = new(2026, 9, 14, 10, 0, 0, TimeSpan.Zero);

    private static readonly string[] Required =
    [
        "reply", "record", "cmp_A", "--plugin", "mailbox", "--thread", "thr-1042", "--message", "msg-77",
        "--message-at", "2026-09-14T09:30:00Z", "--category", "interested", "--confidence", "0.9", "--reaction", "draft",
    ];

    [Fact]
    public async Task Record_sends_every_field_and_the_actor()
    {
        using var cli = new CliRun();

        var exit = await cli.RunAsync(
            [.. Required, "--contact", "cnt_A", "--goal", "demo", "--reason", "category rule matched; draft for approval", "--actor", "attempt:att_A"]);

        Assert.Equal(ExitCodes.Success, exit);
        cli.AssertPosted(
            Operations.ReplyRecord,
            "{\"campaign_id\":\"cmp_A\",\"plugin_id\":\"mailbox\",\"thread_id\":\"thr-1042\",\"message_id\":\"msg-77\",\"message_at\":\"2026-09-14T09:30:00Z\",\"category\":\"interested\",\"confidence\":0.9,\"reaction\":\"draft\",\"contact_id\":\"cnt_A\",\"goal_id\":\"demo\",\"reason\":\"category rule matched; draft for approval\",\"actor\":{\"type\":\"attempt\",\"id\":\"att_A\"}}");
    }

    [Fact]
    public async Task Record_sends_only_what_was_given()
    {
        using var cli = new CliRun();

        var exit = await cli.RunAsync(Required);

        Assert.Equal(ExitCodes.Success, exit);
        cli.AssertPosted(
            Operations.ReplyRecord,
            "{\"campaign_id\":\"cmp_A\",\"plugin_id\":\"mailbox\",\"thread_id\":\"thr-1042\",\"message_id\":\"msg-77\",\"message_at\":\"2026-09-14T09:30:00Z\",\"category\":\"interested\",\"confidence\":0.9,\"reaction\":\"draft\"}");
    }

    [Theory]
    [InlineData("--category")]
    [InlineData("--confidence")]
    [InlineData("--reaction")]
    [InlineData("--plugin")]
    [InlineData("--thread")]
    [InlineData("--message")]
    [InlineData("--message-at")]
    public async Task A_missing_required_option_is_a_usage_error_and_nothing_is_sent(string option)
    {
        using var cli = new CliRun();
        var at = Array.IndexOf(Required, option);
        string[] args = [.. Required[..at], .. Required[(at + 2)..]];

        var exit = await cli.RunAsync(args);

        Assert.Equal(ExitCodes.Usage, exit);
        Assert.Contains(option, cli.Error.ToString(), StringComparison.Ordinal);
        Assert.Equal(0, cli.Requests);
    }

    [Fact]
    public async Task A_reaction_outside_the_four_is_a_usage_error_naming_them()
    {
        using var cli = new CliRun();
        var args = (string[])Required.Clone();
        args[Array.IndexOf(args, "draft")] = "maybe";

        var exit = await cli.RunAsync(args);

        Assert.Equal(ExitCodes.Usage, exit);
        Assert.Contains("act, draft, escalate or none", cli.Error.ToString(), StringComparison.Ordinal);
        Assert.Equal(0, cli.Requests);
    }

    [Fact]
    public async Task A_confidence_that_is_not_a_number_is_a_usage_error()
    {
        using var cli = new CliRun();
        var args = (string[])Required.Clone();
        args[Array.IndexOf(args, "0.9")] = "high";

        var exit = await cli.RunAsync(args);

        Assert.Equal(ExitCodes.Usage, exit);
        Assert.Equal(0, cli.Requests);
    }

    [Fact]
    public async Task A_refusal_is_printed_verbatim_and_exits_1()
    {
        const string envelope = "{\"error\":{\"code\":\"validation_failed\",\"message\":\"Validation failed: goal_id: unknown.\",\"retryable\":false}}";
        using var cli = new CliRun(envelope, status: HttpStatusCode.BadRequest);

        var exit = await cli.RunAsync(Required);

        Assert.Equal(ExitCodes.ApiError, exit);
        Assert.Contains("validation_failed", cli.Output.ToString() + cli.Error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Without_a_runtime_the_verb_exits_3()
    {
        using var cli = new CliRun(descriptor: false);

        var exit = await cli.RunAsync(Required);

        Assert.Equal(ExitCodes.RuntimeUnavailable, exit);
    }

    [Fact]
    public async Task Human_mode_renders_the_recorded_line()
    {
        var entry = JsonSerializer.Serialize(
            new JournalEntryDto(
                "jrn_A",
                Moment,
                new ActorRef(ActorType.Attempt, "att_A"),
                "reply_classified",
                "cmp_A",
                "wi_A",
                "att_A",
                "msg-77",
                null,
                JsonNode.Parse("{\"category\":\"interested\",\"reaction\":\"draft\"}"),
                null),
            JasonJson.Options);
        using var cli = new CliRun(entry);

        var exit = await cli.RunAsync([.. Required, "--human"]);

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Contains("jrn_A", cli.Text, StringComparison.Ordinal);
        Assert.Contains("reply_classified", cli.Text, StringComparison.Ordinal);
        Assert.Contains("attempt:att_A", cli.Text, StringComparison.Ordinal);
    }
}
