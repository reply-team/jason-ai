using System.Text.Json;
using System.Text.Json.Nodes;
using Jason.Cli;
using Jason.Contracts.Api;
using Jason.Contracts.Json;

namespace Jason.Cli.Tests.Commands;

public class JournalCommandsTests
{
    private static readonly DateTimeOffset Moment = new(2026, 9, 14, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Append_sends_the_kind_the_key_and_the_new_value()
    {
        using var cli = new CliRun();

        var exit = await cli.RunAsync(
            "journal",
            "append",
            "cmp_A",
            "--kind",
            "decision",
            "--key",
            "pricing",
            "--new",
            "{\"tier\":\"growth\"}",
            "--reason",
            "the pilot ended",
            "--actor",
            "role:planner");

        Assert.Equal(ExitCodes.Success, exit);
        cli.AssertPosted(
            Operations.JournalAppend,
            "{\"campaign_id\":\"cmp_A\",\"kind\":\"decision\",\"key\":\"pricing\",\"new\":{\"tier\":\"growth\"},\"reason\":\"the pilot ended\",\"actor\":{\"type\":\"role\",\"id\":\"planner\"}}");
    }

    [Fact]
    public async Task Append_without_a_kind_is_a_usage_error()
    {
        using var cli = new CliRun();

        var exit = await cli.RunAsync("journal", "append", "cmp_A");

        Assert.Equal(ExitCodes.Usage, exit);
        Assert.Contains("--kind", cli.Error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_new_value_may_be_any_json_value()
    {
        using var cli = new CliRun();

        var exit = await cli.RunAsync("journal", "append", "cmp_A", "--kind", "observation", "--new", "3");

        Assert.Equal(ExitCodes.Success, exit);
        cli.AssertPosted(Operations.JournalAppend, "{\"campaign_id\":\"cmp_A\",\"kind\":\"observation\",\"new\":3}");
    }

    [Fact]
    public async Task A_new_value_that_is_not_json_is_a_usage_error()
    {
        using var cli = new CliRun();

        var exit = await cli.RunAsync("journal", "append", "cmp_A", "--kind", "observation", "--new", "not json");

        Assert.Equal(ExitCodes.Usage, exit);
        Assert.Contains("--new", cli.Error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Append_lays_the_options_over_a_file_body()
    {
        using var file = new TempFile("{\"kind\":\"from the file\",\"new\":{\"tier\":\"growth\"}}");
        using var cli = new CliRun();

        var exit = await cli.RunAsync("journal", "append", "cmp_A", "--file", file.Path, "--kind", "decision");

        Assert.Equal(ExitCodes.Success, exit);
        cli.AssertPosted(Operations.JournalAppend, "{\"campaign_id\":\"cmp_A\",\"kind\":\"decision\",\"new\":{\"tier\":\"growth\"}}");
    }

    [Fact]
    public async Task List_passes_every_filter()
    {
        using var cli = new CliRun();

        var exit = await cli.RunAsync(
            "journal",
            "list",
            "--campaign",
            "cmp_A",
            "--kind",
            "decision",
            "--since",
            "2026-09-14T10:00:00Z",
            "--limit",
            "5",
            "--cursor",
            "anJuX0E");

        Assert.Equal(ExitCodes.Success, exit);
        cli.AssertPosted(
            Operations.JournalList,
            "{\"campaign_id\":\"cmp_A\",\"kind\":\"decision\",\"since\":\"2026-09-14T10:00:00Z\",\"limit\":5,\"cursor\":\"anJuX0E\"}");
    }

    [Fact]
    public async Task List_without_a_campaign_asks_for_the_whole_chronicle()
    {
        using var cli = new CliRun();

        var exit = await cli.RunAsync("journal", "list");

        Assert.Equal(ExitCodes.Success, exit);
        cli.AssertPosted(Operations.JournalList, "{}");
    }

    [Fact]
    public async Task Human_mode_renders_an_appended_entry()
    {
        var entry = JsonSerializer.Serialize(
            new JournalEntryDto(
                "jrn_A",
                Moment,
                new ActorRef(ActorType.Role, "planner"),
                "decision",
                "cmp_A",
                "pricing",
                null,
                JsonNode.Parse("{\"tier\":\"growth\"}"),
                "the pilot ended"),
            JasonJson.Options);
        using var cli = new CliRun(entry);

        var exit = await cli.RunAsync("journal", "append", "cmp_A", "--kind", "decision", "--human");

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Contains("jrn_A", cli.Text, StringComparison.Ordinal);
        Assert.Contains("decision", cli.Text, StringComparison.Ordinal);
        Assert.Contains("role:planner", cli.Text, StringComparison.Ordinal);
        Assert.Contains("the pilot ended", cli.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Human_mode_renders_the_chronicle_one_line_per_entry()
    {
        var page = JsonSerializer.Serialize(
            new Page<JournalEntryDto>(
                [
                    new JournalEntryDto("jrn_B", Moment, new ActorRef(ActorType.System, null), "campaign_started", "cmp_A", null, null, null, null),
                    new JournalEntryDto("jrn_A", Moment, new ActorRef(ActorType.Role, "planner"), "decision", "cmp_A", "pricing", null, null, "the pilot ended"),
                ],
                "anJuX0E"),
            JasonJson.Options);
        using var cli = new CliRun(page);

        var exit = await cli.RunAsync("journal", "list", "--campaign", "cmp_A", "--human");

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Contains("campaign_started", cli.Text, StringComparison.Ordinal);
        Assert.Contains("system", cli.Text, StringComparison.Ordinal);
        Assert.Contains("role:planner", cli.Text, StringComparison.Ordinal);
        Assert.Contains("pricing", cli.Text, StringComparison.Ordinal);
        Assert.Contains("next cursor: anJuX0E", cli.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("{", cli.Text, StringComparison.Ordinal);
    }
}
