using System.Text.Json;
using System.Text.Json.Nodes;
using Jason.Cli;
using Jason.Contracts.Api;
using Jason.Contracts.Json;

namespace Jason.Cli.Tests.Commands;

public class EventsCommandsTests
{
    private static readonly DateTimeOffset Noon = new(2026, 10, 18, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Sources_sends_the_page()
    {
        using var cli = new CliRun();

        var exit = await cli.RunAsync("events", "sources", "--limit", "5", "--cursor", "abc");

        Assert.Equal(ExitCodes.Success, exit);
        cli.AssertPosted(Operations.EventsSources, "{\"limit\":5,\"cursor\":\"abc\"}");
    }

    [Fact]
    public async Task Backfill_sends_the_account_the_span_the_reason_and_the_actor()
    {
        using var cli = new CliRun();

        var exit = await cli.RunAsync(
            "events",
            "backfill",
            "--source",
            "evs_01K7QN0000000000000000000A",
            "--from",
            "2026-08-01T00:00:00Z",
            "--to",
            "2026-09-01T00:00:00Z",
            "--reason",
            "the weeks nobody read",
            "--actor",
            "human:marta");

        Assert.Equal(ExitCodes.Success, exit);
        cli.AssertPosted(
            Operations.EventsBackfill,
            "{\"source\":\"evs_01K7QN0000000000000000000A\",\"from\":\"2026-08-01T00:00:00Z\",\"to\":\"2026-09-01T00:00:00Z\",\"reason\":\"the weeks nobody read\",\"actor\":{\"type\":\"human\",\"id\":\"marta\"}}");
    }

    /// <summary>A signal names the account and nothing else: the global actor is checked as every verb checks it, and not sent.</summary>
    [Fact]
    public async Task Signal_sends_the_account_alone_though_an_actor_is_given()
    {
        using var cli = new CliRun();

        var exit = await cli.RunAsync("--actor", "human:ada", "events", "signal", "--source", "evs_01K7QN0000000000000000000A");

        Assert.Equal(ExitCodes.Success, exit);
        cli.AssertPosted(Operations.EventsSignal, "{\"source\":\"evs_01K7QN0000000000000000000A\"}");
    }

    [Fact]
    public async Task Signal_with_an_actor_that_is_not_one_is_a_usage_error_and_sends_nothing()
    {
        using var cli = new CliRun();

        var exit = await cli.RunAsync("--actor", "nobody", "events", "signal", "--source", "evs_01K7QN0000000000000000000A");

        Assert.Equal(ExitCodes.Usage, exit);
        Assert.Null(cli.Sent);
    }

    [Fact]
    public async Task Human_mode_renders_a_signal_s_receipt()
    {
        using var cli = new CliRun(JsonSerializer.Serialize(new EventsSignalResponse(Noon, Noon.AddSeconds(40)), JasonJson.Options));

        var exit = await cli.RunAsync("events", "signal", "--source", "evs_01K7QN0000000000000000000A", "--human");

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Contains("2026-10-18 12:00:00 UTC", cli.Text, StringComparison.Ordinal);
        Assert.Contains("2026-10-18 12:00:40 UTC", cli.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("{", cli.Text, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("--source")]
    [InlineData("--from")]
    [InlineData("--to")]
    public async Task Backfill_without_one_end_of_the_span_or_the_account_is_a_usage_error(string missing)
    {
        string[] given = ["--source", "evs_01K7QN0000000000000000000A", "--from", "2026-08-01T00:00:00Z", "--to", "2026-09-01T00:00:00Z"];
        var at = Array.IndexOf(given, missing);
        using var cli = new CliRun();

        var exit = await cli.RunAsync([.. new[] { "events", "backfill" }, .. given.Where((_, index) => index != at && index != at + 1)]);

        Assert.Equal(ExitCodes.Usage, exit);
        Assert.Contains(missing, cli.Error.ToString(), StringComparison.Ordinal);
        Assert.Null(cli.Sent);
    }

    /// <summary>The record for a person: one row an account, what was not read, and the spans asked for again — never a cursor, which the answer does not carry.</summary>
    [Fact]
    public async Task Human_mode_renders_the_accounts_and_their_spans()
    {
        var failed = new EventBackfillDto(
            "evb_01K7QN0000000000000000000B", "evs_01K7QN0000000000000000000A", Noon.AddDays(-200), Noon.AddDays(-190), EventBackfillStatus.Failed,
            Noon.AddDays(-1), new ActorRef(ActorType.Human, "marta"), "the weeks nobody read", 0, 0, 0, Noon.AddDays(-1), Noon.AddDays(-1), "events_unauthorized", "permanent");
        var source = new EventSourceDto(
            "evs_01K7QN0000000000000000000A", "fake-provider", "sha256:a", new JsonObject { ["workspace"] = "west" }, true, true, Noon.AddHours(-1),
            Noon.AddMinutes(-5), Noon.AddMinutes(-5), Noon.AddMinutes(-5), 0, null, null, false, true, Noon.AddMinutes(5), null,
            new EventGapDto(Noon.AddDays(-90), Noon.AddDays(-30), Noon.AddDays(-2)), [failed], Noon.AddDays(-60));
        using var cli = new CliRun(JsonSerializer.Serialize(new Page<EventSourceDto>([source], null), JasonJson.Options));

        var exit = await cli.RunAsync("events", "sources", "--human");

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Contains("NOT READ", cli.Text, StringComparison.Ordinal);
        Assert.Contains("evs_01K7QN0000000000000000000A  fake-provider  yes", cli.Text, StringComparison.Ordinal);
        Assert.Contains("2026-07-20 12:00:00 UTC to 2026-09-18 12:00:00 UTC", cli.Text, StringComparison.Ordinal);
        Assert.Contains("evb_01K7QN0000000000000000000B", cli.Text, StringComparison.Ordinal);
        Assert.Contains("failed, 0 new, 0 already held, events_unauthorized (permanent)", cli.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("{", cli.Text, StringComparison.Ordinal);

        // The binding names the account, and the record for a person never prints it: neither as JSON nor as a value.
        Assert.DoesNotContain("west", cli.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Human_mode_says_when_no_account_is_read()
    {
        using var cli = new CliRun(JsonSerializer.Serialize(new Page<EventSourceDto>([], null), JasonJson.Options));

        var exit = await cli.RunAsync("events", "sources", "--human");

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Contains("no accounts", cli.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Human_mode_renders_a_span_asked_for()
    {
        var asked = new EventBackfillDto(
            "evb_01K7QN0000000000000000000B", "evs_01K7QN0000000000000000000A", Noon.AddDays(-60), Noon.AddDays(-30), EventBackfillStatus.Pending,
            Noon, new ActorRef(ActorType.Human, "marta"), "the weeks nobody read", 0, 0, 0, null, null, null, null);
        using var cli = new CliRun(JsonSerializer.Serialize(asked, JasonJson.Options));

        var exit = await cli.RunAsync("events", "backfill", "--source", "evs_01K7QN0000000000000000000A", "--from", "2026-08-19T12:00:00Z", "--to", "2026-09-18T12:00:00Z", "--human");

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Contains("pending", cli.Text, StringComparison.Ordinal);
        Assert.Contains("the weeks nobody read", cli.Text, StringComparison.Ordinal);
        Assert.Contains("2026-08-19 12:00:00 UTC", cli.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("{", cli.Text, StringComparison.Ordinal);
    }
}
