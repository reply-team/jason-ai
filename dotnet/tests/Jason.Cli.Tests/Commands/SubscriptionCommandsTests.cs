using System.Text.Json;
using System.Text.Json.Nodes;
using Jason.Cli;
using Jason.Cli.Human;
using Jason.Contracts.Api;
using Jason.Contracts.Json;

namespace Jason.Cli.Tests.Commands;

public class SubscriptionCommandsTests
{
    private static readonly DateTimeOffset Noon = new(2026, 10, 18, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task List_sends_the_page()
    {
        using var cli = new CliRun();

        var exit = await cli.RunAsync("subscription", "list", "--limit", "5", "--cursor", "abc");

        Assert.Equal(ExitCodes.Success, exit);
        cli.AssertPosted(Operations.SubscriptionList, "{\"limit\":5,\"cursor\":\"abc\"}");
    }

    [Fact]
    public async Task Runs_sends_the_kind_and_the_page()
    {
        using var cli = new CliRun();

        var exit = await cli.RunAsync("subscription", "runs", "--kind", "campaign_created", "--limit", "20");

        Assert.Equal(ExitCodes.Success, exit);
        cli.AssertPosted(Operations.SubscriptionRuns, "{\"kind\":\"campaign_created\",\"limit\":20}");
    }

    [Fact]
    public async Task Runs_without_a_kind_is_a_usage_error_and_sends_nothing()
    {
        using var cli = new CliRun();

        var exit = await cli.RunAsync("subscription", "runs");

        Assert.Equal(ExitCodes.Usage, exit);
        Assert.Null(cli.Sent);
    }

    [Fact]
    public async Task Rewind_sends_the_kind_the_line_the_reason_and_the_person()
    {
        using var cli = new CliRun();

        var exit = await cli.RunAsync(
            "--actor",
            "human:marta",
            "subscription",
            "rewind",
            "--kind",
            "campaign_created",
            "--line",
            "jrn_01K7QN0000000000000000000A",
            "--reason",
            "the forwarder was down");

        Assert.Equal(ExitCodes.Success, exit);
        cli.AssertPosted(
            Operations.SubscriptionRewind,
            "{\"kind\":\"campaign_created\",\"line\":\"jrn_01K7QN0000000000000000000A\",\"reason\":\"the forwarder was down\",\"actor\":{\"type\":\"human\",\"id\":\"marta\"}}");
    }

    /// <summary>Which of line and since is given is the runtime's to check, in one place, with a field detail; the CLI sends what it was given.</summary>
    [Fact]
    public async Task Rewind_sends_a_moment_as_it_was_given()
    {
        using var cli = new CliRun();

        var exit = await cli.RunAsync("--actor", "human:marta", "subscription", "rewind", "--kind", "campaign_created", "--since", "2026-10-18T09:00:00+02:00");

        Assert.Equal(ExitCodes.Success, exit);
        cli.AssertPosted(
            Operations.SubscriptionRewind,
            "{\"kind\":\"campaign_created\",\"since\":\"2026-10-18T09:00:00+02:00\",\"actor\":{\"type\":\"human\",\"id\":\"marta\"}}");
    }

    [Fact]
    public async Task Human_mode_renders_the_kinds()
    {
        var page = new Page<SubscriptionKindDto>(
            [
                new SubscriptionKindDto(
                    "campaign_created",
                    ["journal-forwarder", "crm-mirror"],
                    "jrn_01K7QN0000000000000000000A",
                    "jrn_01K7QN0000000000000000000C",
                    2,
                    ["crm-mirror"],
                    new SubscriptionInFlightDto("sbr_01K7QN0000000000000000000A", "jrn_01K7QN0000000000000000000B", Noon, null),
                    null),
            ],
            null);
        using var cli = new CliRun(JsonSerializer.Serialize(page, JasonJson.Options));

        var exit = await cli.RunAsync("subscription", "list", "--human");

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Contains("campaign_created", cli.Text, StringComparison.Ordinal);
        Assert.Contains("journal-forwarder, crm-mirror", cli.Text, StringComparison.Ordinal);
        Assert.Contains("sbr_01K7QN0000000000000000000A", cli.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("{", cli.Text, StringComparison.Ordinal);
    }

    /// <summary>An envelope is a plugin's own text: the human view lists its keys and never prints what they hold.</summary>
    [Fact]
    public async Task Human_mode_lists_an_envelope_s_keys_and_never_its_values()
    {
        var run = new SubscriptionRunDto(
            "sbr_01K7QN0000000000000000000A",
            "campaign_created",
            "jrn_01K7QN0000000000000000000A",
            SubscriptionRunStatus.Succeeded,
            Noon,
            Noon.AddSeconds(2),
            [new JsonObject { ["plugin_id"] = "journal-forwarder", ["outcome"] = "succeeded" }],
            null,
            new JsonObject { ["forwarded_to"] = "sentinel-envelope-value" });
        using var cli = new CliRun(JsonSerializer.Serialize(new Page<SubscriptionRunDto>([run], null), JasonJson.Options));

        var exit = await cli.RunAsync("subscription", "runs", "--kind", "campaign_created", "--human");

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Contains("forwarded_to", cli.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("sentinel-envelope-value", cli.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Human_mode_renders_a_rewound_kind()
    {
        var kind = new SubscriptionKindDto("campaign_created", ["journal-forwarder"], null, "jrn_01K7QN0000000000000000000C", 3, [], null, null);
        using var cli = new CliRun(JsonSerializer.Serialize(kind, JasonJson.Options));

        var exit = await cli.RunAsync("--actor", "human:marta", "subscription", "rewind", "--kind", "campaign_created", "--since", "2026-10-18T00:00:00Z", "--human");

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Contains("Lag:", cli.Text, StringComparison.Ordinal);
        Assert.Contains("jrn_01K7QN0000000000000000000C", cli.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("{", cli.Text, StringComparison.Ordinal);
    }

    /// <summary>A kind at rest says so in both human views, by the moment, and a kind that does not rest shows nothing there.</summary>
    [Fact]
    public async Task Human_mode_says_until_when_a_kind_rests()
    {
        var kind = new SubscriptionKindDto("campaign_created", ["journal-forwarder"], null, "jrn_01K7QN0000000000000000000C", 3, [], null, Noon.AddSeconds(30));
        using var cli = new CliRun(JsonSerializer.Serialize(new Page<SubscriptionKindDto>([kind], null), JasonJson.Options));

        Assert.Equal(ExitCodes.Success, await cli.RunAsync("subscription", "list", "--human"));

        Assert.Contains("RESTS UNTIL", cli.Text, StringComparison.Ordinal);
        Assert.Contains(RenderText.Moment(Noon.AddSeconds(30)), cli.Text, StringComparison.Ordinal);

        using var rewound = new CliRun(JsonSerializer.Serialize(kind, JasonJson.Options));
        await rewound.RunAsync("--actor", "human:marta", "subscription", "rewind", "--kind", "campaign_created", "--since", "2026-10-18T00:00:00Z", "--human");
        Assert.Contains("Rests until:", rewound.Text, StringComparison.Ordinal);
    }
}
