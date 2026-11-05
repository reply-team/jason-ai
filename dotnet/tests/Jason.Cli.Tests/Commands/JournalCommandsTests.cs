using System.Text.Json;
using System.Text.Json.Nodes;
using Jason.Cli;
using Jason.Contracts.Api;
using Jason.Contracts.Journal;
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
            "--work-item",
            "wi_A",
            "--kind",
            "decision",
            "--kind",
            "pricing",
            "--contact",
            "cnt_A",
            "--since",
            "2026-09-14T10:00:00Z",
            "--limit",
            "5",
            "--cursor",
            "anJuX0E");

        Assert.Equal(ExitCodes.Success, exit);
        cli.AssertPosted(
            Operations.JournalList,
            "{\"campaign_id\":\"cmp_A\",\"work_item_id\":\"wi_A\",\"contact_id\":\"cnt_A\",\"kinds\":[\"decision\",\"pricing\"],\"since\":\"2026-09-14T10:00:00Z\",\"limit\":5,\"cursor\":\"anJuX0E\"}");
    }

    [Fact]
    public async Task List_can_follow_one_work_item_through_the_chronicle()
    {
        using var cli = new CliRun();

        var exit = await cli.RunAsync("journal", "list", "--work-item", "wi_A");

        Assert.Equal(ExitCodes.Success, exit);
        cli.AssertPosted(Operations.JournalList, "{\"work_item_id\":\"wi_A\"}");
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
                null,
                null,
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
                    new JournalEntryDto("jrn_B", Moment, new ActorRef(ActorType.System, null), "campaign_started", "cmp_A", null, null, null, null, null, null),
                    new JournalEntryDto("jrn_A", Moment, new ActorRef(ActorType.Role, "planner"), "decision", "cmp_A", null, null, "pricing", null, null, "the pilot ended", "cnt_A", "LatAm", "Ada Lovelace"),
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
        Assert.Contains("PERSON", cli.Text, StringComparison.Ordinal);
        Assert.Contains("Ada Lovelace", cli.Text, StringComparison.Ordinal);
        Assert.Contains("next cursor: anJuX0E", cli.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("{", cli.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Kinds_passes_the_limit_and_the_cursor()
    {
        using var cli = new CliRun();

        var exit = await cli.RunAsync("journal", "kinds", "--limit", "2", "--cursor", "Y2FtcGFpZ25fYXJjaGl2ZWQ");

        Assert.Equal(ExitCodes.Success, exit);
        cli.AssertPosted(Operations.JournalKinds, "{\"limit\":2,\"cursor\":\"Y2FtcGFpZ25fYXJjaGl2ZWQ\"}");
    }

    [Fact]
    public async Task Kinds_without_options_asks_for_the_first_page()
    {
        using var cli = new CliRun();

        var exit = await cli.RunAsync("journal", "kinds");

        Assert.Equal(ExitCodes.Success, exit);
        cli.AssertPosted(Operations.JournalKinds, "{}");
    }

    [Fact]
    public async Task Human_mode_renders_one_row_per_kind_and_never_the_schema()
    {
        var page = JsonSerializer.Serialize(
            new Page<JournalKindDto>(
                [
                    new JournalKindDto(
                        "campaign_created",
                        1,
                        "0.1.0",
                        "A campaign was made.",
                        true,
                        new Dictionary<string, JournalKindEntityDto> { ["campaign"] = new(["/campaign_id"], JournalEntityPresence.Always, false) },
                        new Dictionary<string, string>(),
                        new JsonObject { ["type"] = "object", ["properties"] = new JsonObject() }),
                    new JournalKindDto(
                        "events_read",
                        1,
                        "0.12.0",
                        "An account's provider events were read.",
                        false,
                        new Dictionary<string, JournalKindEntityDto>(),
                        new Dictionary<string, string> { ["/new/signaled"] = "0.13.0" },
                        new JsonObject { ["type"] = "object", ["properties"] = new JsonObject() }),
                ],
                "ZXZlbnRzX3JlYWQ"),
            JasonJson.Options);
        using var cli = new CliRun(page);

        var exit = await cli.RunAsync("journal", "kinds", "--human");

        Assert.Equal(ExitCodes.Success, exit);
        var lines = cli.Text.ReplaceLineEndings().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        Assert.Contains(lines, line => line.StartsWith("KIND", StringComparison.Ordinal) && line.Contains("SINCE", StringComparison.Ordinal) && line.Contains("SUBSCRIBABLE", StringComparison.Ordinal) && line.Contains("SUMMARY", StringComparison.Ordinal));
        Assert.Contains(lines, line => line.StartsWith("campaign_created", StringComparison.Ordinal) && line.Contains("0.1.0", StringComparison.Ordinal) && line.Contains("yes", StringComparison.Ordinal) && line.Contains("A campaign was made.", StringComparison.Ordinal));
        Assert.Contains(lines, line => line.StartsWith("events_read", StringComparison.Ordinal) && line.Contains("0.12.0", StringComparison.Ordinal) && line.Contains("no", StringComparison.Ordinal) && line.Contains("An account's provider events were read.", StringComparison.Ordinal));
        Assert.Contains("next cursor: ZXZlbnRzX3JlYWQ", cli.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("properties", cli.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("/campaign_id", cli.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("{", cli.Text, StringComparison.Ordinal);
    }
}
