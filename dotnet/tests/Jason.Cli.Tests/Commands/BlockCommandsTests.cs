using System.Net;
using Jason.Cli;
using Jason.Contracts.Api;

namespace Jason.Cli.Tests.Commands;

/// <summary>
/// The verbs a person reads the provider's blocks through and clears one with, from the side they are typed on: what is
/// sent, what is printed back to a person, and how a refusal exits.
/// </summary>
public class BlockCommandsTests
{
    private const string OpenSenderBlock = """
        {
          "id": "blk_01K52JR0000000000000000001",
          "plugin_id": "fake-provider",
          "binding_identity": "sha256:account",
          "scope": "sender",
          "sender_external_id": "snd_marta",
          "reason": "billing",
          "link": "https://provider.example.com/billing",
          "clears_at": "2026-09-15T08:00:00+00:00",
          "status": "open",
          "opened_at": "2026-09-14T10:00:00+00:00",
          "last_seen_at": "2026-09-14T11:00:00+00:00",
          "seen_count": 3,
          "next_trial_at": "2026-09-14T12:00:00+00:00",
          "trial_work_item_id": "wi_01K52JR0000000000000000002",
          "opened_by_attempt_id": "att_01K52JR0000000000000000003",
          "held_count": 2,
          "cleared_at": null,
          "cleared_by_attempt_id": null,
          "cleared_by_actor": null
        }
        """;

    private static string AccountBlock(string cleared) => OpenSenderBlock
        .Replace("\"scope\": \"sender\"", "\"scope\": \"account\"", StringComparison.Ordinal)
        .Replace("\"sender_external_id\": \"snd_marta\"", "\"sender_external_id\": null", StringComparison.Ordinal)
        .Replace("\"status\": \"open\"", "\"status\": \"cleared\"", StringComparison.Ordinal)
        .Replace("\"cleared_at\": null", "\"cleared_at\": \"2026-09-14T13:00:00+00:00\"", StringComparison.Ordinal)
        .Replace("\"cleared_by_attempt_id\": null,\n  \"cleared_by_actor\": null", cleared, StringComparison.Ordinal)
        .Replace("\"cleared_by_attempt_id\": null,\r\n  \"cleared_by_actor\": null", cleared, StringComparison.Ordinal);

    [Fact]
    public async Task List_sends_the_statuses_the_plugin_and_the_page_it_was_given()
    {
        using var cli = new CliRun();

        var exit = await cli.RunAsync("block", "list", "--status", "open", "--status", "cleared", "--plugin", "fake-provider", "--limit", "5", "--cursor", "c1");

        Assert.Equal(ExitCodes.Success, exit);
        cli.AssertPosted(Operations.BlockList, "{\"statuses\":[\"open\",\"cleared\"],\"plugin_id\":\"fake-provider\",\"limit\":5,\"cursor\":\"c1\"}");
    }

    [Fact]
    public async Task List_sends_one_status_as_a_list_of_one()
    {
        using var cli = new CliRun();

        await cli.RunAsync("block", "list", "--status", "cleared");

        cli.AssertPosted(Operations.BlockList, "{\"statuses\":[\"cleared\"]}");
    }

    [Fact]
    public async Task List_without_options_sends_an_empty_body_and_lets_the_runtime_say_what_open_means()
    {
        using var cli = new CliRun();

        var exit = await cli.RunAsync("block", "list");

        Assert.Equal(ExitCodes.Success, exit);
        cli.AssertPosted(Operations.BlockList, "{}");
    }

    [Fact]
    public async Task Get_sends_the_block_id()
    {
        using var cli = new CliRun(OpenSenderBlock);

        var exit = await cli.RunAsync("block", "get", "blk_7");

        Assert.Equal(ExitCodes.Success, exit);
        cli.AssertPosted(Operations.BlockGet, "{\"block_id\":\"blk_7\"}");
    }

    [Fact]
    public async Task Clear_sends_the_block_the_reason_and_who_cleared_it()
    {
        using var cli = new CliRun(AccountBlock("\"cleared_by_attempt_id\": null,\n  \"cleared_by_actor\": {\"type\": \"human\", \"id\": \"marta@example.com\"}"));

        var exit = await cli.RunAsync("--actor", "human:marta@example.com", "block", "clear", "blk_7", "--reason", "the invoice is paid");

        Assert.Equal(ExitCodes.Success, exit);
        cli.AssertPosted(
            Operations.BlockClear,
            "{\"block_id\":\"blk_7\",\"reason\":\"the invoice is paid\",\"actor\":{\"type\":\"human\",\"id\":\"marta@example.com\"}}");
    }

    [Fact]
    public async Task Clear_without_an_actor_is_a_usage_error_and_sends_nothing()
    {
        using var cli = new CliRun();

        var exit = await cli.RunAsync("block", "clear", "blk_7", "--reason", "paid");

        Assert.Equal(ExitCodes.Usage, exit);
        Assert.True(cli.Error.ToString().Contains("--actor", StringComparison.Ordinal), $"The usage error does not name --actor: {cli.Error}");
        Assert.True(cli.Requests == 0, $"A clear without an actor still sent {cli.Requests} request(s).");
    }

    /// <summary>
    /// What a person reads to clear a block, in words: what is blocked, why, where it is cleared, whether it clears by
    /// itself, how long and how often it has held, what it holds, and its next trial.
    /// </summary>
    [Fact]
    public async Task Get_says_in_words_what_is_blocked_why_and_where_it_is_cleared()
    {
        using var cli = new CliRun(OpenSenderBlock);

        var exit = await cli.RunAsync("block", "get", "blk_1", "--human");

        Assert.Equal(ExitCodes.Success, exit);
        foreach (var shown in (string[])[
            "Blocked:       sender snd_marta",
            "Reason:        billing",
            "Link:          https://provider.example.com/billing",
            "Clears at:     2026-09-15 08:00:00 UTC",
            "Status:        open",
            "Opened:        2026-09-14 10:00:00 UTC",
            "Last seen:     2026-09-14 11:00:00 UTC",
            "Seen:          3 times",
            "Holds:         2 items",
            "Next trial:    2026-09-14 12:00:00 UTC",
            "Trial item:    wi_01K52JR0000000000000000002",
            "Opened by:     att_01K52JR0000000000000000003",
            "Id:            blk_01K52JR0000000000000000001"])
        {
            Assert.True(cli.Text.Contains(shown, StringComparison.Ordinal), $"The block does not say '{shown}':{Environment.NewLine}{cli.Text}");
        }

        Assert.True(!cli.Text.Contains("Cleared", StringComparison.Ordinal), $"An open block speaks of a clear:{Environment.NewLine}{cli.Text}");
    }

    [Fact]
    public async Task Get_of_a_block_a_person_cleared_says_the_account_and_who_cleared_it_when()
    {
        using var cli = new CliRun(AccountBlock("\"cleared_by_attempt_id\": null,\n  \"cleared_by_actor\": {\"type\": \"human\", \"id\": \"marta@example.com\"}"));

        var exit = await cli.RunAsync("block", "get", "blk_1", "--human");

        Assert.Equal(ExitCodes.Success, exit);
        foreach (var shown in (string[])["Blocked:       the account", "Status:        cleared", "Cleared:       2026-09-14 13:00:00 UTC", "Cleared by:    human:marta@example.com"])
        {
            Assert.True(cli.Text.Contains(shown, StringComparison.Ordinal), $"The cleared block does not say '{shown}':{Environment.NewLine}{cli.Text}");
        }
    }

    [Fact]
    public async Task Get_of_a_block_an_attempt_got_past_names_the_attempt()
    {
        using var cli = new CliRun(AccountBlock("\"cleared_by_attempt_id\": \"att_01K52JR0000000000000000009\",\n  \"cleared_by_actor\": null"));

        var exit = await cli.RunAsync("block", "get", "blk_1", "--human");

        Assert.Equal(ExitCodes.Success, exit);
        Assert.True(
            cli.Text.Contains("Cleared by:    attempt att_01K52JR0000000000000000009", StringComparison.Ordinal),
            $"The cleared block does not name its attempt:{Environment.NewLine}{cli.Text}");
    }

    [Fact]
    public async Task List_renders_a_table_of_the_blocks()
    {
        using var cli = new CliRun($$"""{"items": [{{OpenSenderBlock}}], "next_cursor": null}""");

        var exit = await cli.RunAsync("block", "list", "--human");

        Assert.Equal(ExitCodes.Success, exit);
        foreach (var shown in (string[])["OPENED", "BLOCKED", "HELD", "blk_01K52JR0000000000000000001", "sender snd_marta", "billing", "open"])
        {
            Assert.True(cli.Text.Contains(shown, StringComparison.Ordinal), $"The table does not show '{shown}':{Environment.NewLine}{cli.Text}");
        }
    }

    [Theory]
    [InlineData("block_not_open", HttpStatusCode.Conflict, "clear")]
    [InlineData("block_not_human", HttpStatusCode.BadRequest, "clear")]
    [InlineData("actor_required", HttpStatusCode.BadRequest, "clear")]
    [InlineData("block_not_found", HttpStatusCode.NotFound, "clear")]
    [InlineData("block_not_found", HttpStatusCode.NotFound, "get")]
    public async Task A_refusal_is_printed_verbatim_and_exits_1(string code, HttpStatusCode status, string verb)
    {
        using var cli = new CliRun($$$"""{"error":{"code":"{{{code}}}","message":"No.","retryable":false}}""", status: status);

        var exit = verb == "clear"
            ? await cli.RunAsync("--actor", "human:marta@example.com", "block", "clear", "blk_7")
            : await cli.RunAsync("block", "get", "blk_7");

        Assert.Equal(ExitCodes.ApiError, exit);
        Assert.True(cli.Text.Contains(code, StringComparison.Ordinal), $"The refusal printed:{Environment.NewLine}{cli.Text}");
    }

    /// <summary>Each verb's help publishes its contract: what it asks for, what it changes, and what it leaves to the next scan.</summary>
    [Theory]
    [InlineData("list", new[] { "--status", "--plugin", "--limit", "--cursor", "open blocks only" })]
    [InlineData("get", new[] { "<block-id>", "the link" })]
    [InlineData("clear", new[] { "<block-id>", "--reason", "--actor", "next scan", "blocked again" })]
    public async Task Each_verb_s_help_states_its_contract(string verb, string[] shown)
    {
        using var cli = new CliRun();

        var exit = await cli.RunAsync("block", verb, "--help");

        Assert.Equal(ExitCodes.Success, exit);
        foreach (var words in shown)
        {
            Assert.True(cli.Text.Contains(words, StringComparison.Ordinal), $"The help of block {verb} does not say '{words}':{Environment.NewLine}{cli.Text}");
        }
    }
}
