using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Jason.Cli;
using Jason.Cli.Tests.Folders;
using Jason.Contracts.Api;
using Jason.Contracts.Discovery;
using Jason.Contracts.Json;
using Jason.Runtime.Tests;

namespace Jason.Cli.Tests.Commands;

public class CampaignCommandsTests
{
    private static readonly DateTimeOffset Moment = new(2026, 9, 14, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Create_sends_the_name_and_the_context()
    {
        using var cli = new CliRun();

        var exit = await cli.RunAsync("campaign", "create", "--name", "LatAm", "--context", "{\"icp\":\"founders\"}");

        Assert.Equal(ExitCodes.Success, exit);
        cli.AssertPosted(Operations.CampaignCreate, "{\"name\":\"LatAm\",\"context\":{\"icp\":\"founders\"}}");
    }

    [Fact]
    public async Task Create_lays_the_options_over_a_file_body()
    {
        using var file = new TempFile("{\"name\":\"from the file\",\"context\":{\"icp\":\"founders\"}}");
        using var cli = new CliRun();

        var exit = await cli.RunAsync("campaign", "create", "--file", file.Path, "--name", "LatAm");

        Assert.Equal(ExitCodes.Success, exit);
        cli.AssertPosted(Operations.CampaignCreate, "{\"name\":\"LatAm\",\"context\":{\"icp\":\"founders\"}}");
    }

    [Fact]
    public async Task A_context_that_is_not_an_object_is_a_usage_error()
    {
        using var cli = new CliRun();

        var exit = await cli.RunAsync("campaign", "create", "--name", "LatAm", "--context", "[1]");

        Assert.Equal(ExitCodes.Usage, exit);
        Assert.Contains("--context", cli.Error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_context_that_is_not_json_is_a_usage_error()
    {
        using var cli = new CliRun();

        var exit = await cli.RunAsync("campaign", "create", "--context", "{");

        Assert.Equal(ExitCodes.Usage, exit);
        Assert.Contains("--context", cli.Error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Get_carries_the_positional_id_in_the_body()
    {
        using var cli = new CliRun();

        var exit = await cli.RunAsync("campaign", "get", "cmp_A");

        Assert.Equal(ExitCodes.Success, exit);
        cli.AssertPosted(Operations.CampaignGet, "{\"campaign_id\":\"cmp_A\"}");
    }

    [Fact]
    public async Task List_passes_the_filter_and_the_page()
    {
        using var cli = new CliRun();

        var exit = await cli.RunAsync(
            "campaign", "list", "--status", "active", "--query", "autumn", "--limit", "5", "--cursor", "Y21wX0E", "--include-archived");

        Assert.Equal(ExitCodes.Success, exit);
        cli.AssertPosted(
            Operations.CampaignList, "{\"status\":\"active\",\"query\":\"autumn\",\"limit\":5,\"cursor\":\"Y21wX0E\",\"include_archived\":true}");
    }

    [Fact]
    public async Task List_without_options_sends_an_empty_body()
    {
        using var cli = new CliRun();

        var exit = await cli.RunAsync("campaign", "list");

        Assert.Equal(ExitCodes.Success, exit);
        cli.AssertPosted(Operations.CampaignList, "{}");
    }

    [Fact]
    public async Task Update_sends_the_new_name_and_the_reason()
    {
        using var cli = new CliRun();

        var exit = await cli.RunAsync("campaign", "update", "cmp_A", "--name", "LatAm Q4", "--reason", "renamed after the split");

        Assert.Equal(ExitCodes.Success, exit);
        cli.AssertPosted(Operations.CampaignUpdate, "{\"campaign_id\":\"cmp_A\",\"name\":\"LatAm Q4\",\"reason\":\"renamed after the split\"}");
    }

    [Fact]
    [Critical(Family.R2, "absent vs null from the CLI")]
    public async Task Update_sends_the_execution_profile_and_clears_it_with_an_explicit_null()
    {
        using var cli = new CliRun();

        var exit = await cli.RunAsync("campaign", "update", "cmp_A", "--execution-profile", "local-claude");

        Assert.Equal(ExitCodes.Success, exit);
        cli.AssertPosted(Operations.CampaignUpdate, "{\"campaign_id\":\"cmp_A\",\"execution_profile\":\"local-claude\"}");

        using var clearing = new CliRun();

        var cleared = await clearing.RunAsync("campaign", "update", "cmp_A", "--clear", "execution_profile");

        Assert.Equal(ExitCodes.Success, cleared);
        clearing.AssertPosted(Operations.CampaignUpdate, "{\"campaign_id\":\"cmp_A\",\"execution_profile\":null}");
    }

    /// <summary>
    /// The campaign's own review cadence and its audience threshold, sent and taken away the same way the profile is —
    /// including the difference that makes the whole patch shape worth having: saying nothing leaves the field alone, and
    /// saying null puts the campaign back on the installation's cadence, or stops the counting.
    /// </summary>
    [Theory]
    [InlineData("--review-seconds", "600", "review_seconds")]
    [InlineData("--audience-threshold", "50", "audience_threshold")]
    public async Task Update_sends_a_number_the_dispatcher_reads_and_clears_it_with_an_explicit_null(string option, string value, string field)
    {
        using var cli = new CliRun();

        var exit = await cli.RunAsync("campaign", "update", "cmp_A", option, value);

        Assert.Equal(ExitCodes.Success, exit);
        cli.AssertPosted(Operations.CampaignUpdate, $"{{\"campaign_id\":\"cmp_A\",\"{field}\":{value}}}");

        using var clearing = new CliRun();

        var cleared = await clearing.RunAsync("campaign", "update", "cmp_A", "--clear", field);

        Assert.Equal(ExitCodes.Success, cleared);
        clearing.AssertPosted(Operations.CampaignUpdate, $"{{\"campaign_id\":\"cmp_A\",\"{field}\":null}}");
    }

    [Fact]
    public async Task Update_refuses_to_clear_a_field_that_is_not_one()
    {
        using var cli = new CliRun();

        var exit = await cli.RunAsync("campaign", "update", "cmp_A", "--clear", "name");

        Assert.Equal(ExitCodes.Usage, exit);
        Assert.Contains("--clear", cli.Error.ToString(), StringComparison.Ordinal);
    }

    /// <summary>
    /// A campaign's own send mode: a word sets one, <c>--clear</c> sends null for one, which gives it back to the
    /// installation, and wins over a word given for the same mode; a mode not named is not sent at all.
    /// </summary>
    [Theory]
    [InlineData(new[] { "--cold", "automatic" }, "\"cold\":\"automatic\"")]
    [InlineData(new[] { "--warm", "every_message" }, "\"warm\":\"every_message\"")]
    [InlineData(new[] { "--cold", "automatic", "--clear", "warm" }, "\"cold\":\"automatic\",\"warm\":null")]
    [InlineData(new[] { "--cold", "automatic", "--clear", "cold" }, "\"cold\":null")]
    [InlineData(new[] { "--clear", "cold", "--clear", "warm" }, "\"cold\":null,\"warm\":null")]
    public async Task Set_send_mode_sends_the_modes_named_and_null_for_a_mode_cleared(string[] options, string modes)
    {
        using var cli = new CliRun();

        var exit = await cli.RunAsync(["--actor", "human:Marta", "campaign", "set-send-mode", "cmp_A", .. options, "--reason", "her call"]);

        Assert.Equal(ExitCodes.Success, exit);
        cli.AssertPosted(
            Operations.CampaignSetSendMode,
            $"{{\"campaign_id\":\"cmp_A\",{modes},\"reason\":\"her call\",\"actor\":{{\"type\":\"human\",\"id\":\"Marta\"}}}}");
    }

    [Theory]
    [InlineData(new[] { "--clear", "both" }, "--clear names cold or warm.")]
    [InlineData(new string[0], "campaign set-send-mode needs --cold, --warm or --clear")]
    public async Task Set_send_mode_refuses_a_request_that_names_no_mode_it_knows(string[] options, string said)
    {
        using var cli = new CliRun();

        var exit = await cli.RunAsync(["--actor", "human:Marta", "campaign", "set-send-mode", "cmp_A", .. options]);

        Assert.Equal(ExitCodes.Usage, exit);
        Assert.Contains(said, cli.Error.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("start", Operations.CampaignStart)]
    [InlineData("pause", Operations.CampaignPause)]
    [InlineData("archive", Operations.CampaignArchive)]
    public async Task A_transition_verb_maps_to_its_operation(string verb, string operation)
    {
        using var cli = new CliRun();

        var exit = await cli.RunAsync("campaign", verb, "cmp_A", "--reason", "the quarter ended");

        Assert.Equal(ExitCodes.Success, exit);
        cli.AssertPosted(operation, "{\"campaign_id\":\"cmp_A\",\"reason\":\"the quarter ended\"}");
    }

    [Fact]
    public async Task Update_context_sets_unsets_and_claims_the_actor()
    {
        using var cli = new CliRun();

        var exit = await cli.RunAsync(
            "campaign",
            "update-context",
            "cmp_A",
            "--set",
            "{\"icp\":\"founders\"}",
            "--unset",
            "old_pitch",
            "--unset",
            "older_pitch",
            "--reason",
            "the plan changed",
            "--actor",
            "role:planner");

        Assert.Equal(ExitCodes.Success, exit);
        cli.AssertPosted(
            Operations.CampaignUpdateContext,
            "{\"campaign_id\":\"cmp_A\",\"set\":{\"icp\":\"founders\"},\"unset\":[\"old_pitch\",\"older_pitch\"],\"reason\":\"the plan changed\",\"actor\":{\"type\":\"role\",\"id\":\"planner\"}}");
    }

    [Fact]
    public async Task Add_contacts_wraps_a_file_array_under_contacts()
    {
        using var file = new TempFile("[{\"first_name\":\"Ada\"},{\"contact_id\":\"cnt_B\"}]");
        using var cli = new CliRun();

        var exit = await cli.RunAsync("campaign", "add-contacts", "cmp_A", "--file", file.Path, "--match-by", "email");

        Assert.Equal(ExitCodes.Success, exit);
        cli.AssertPosted(
            Operations.CampaignAddContacts,
            "{\"campaign_id\":\"cmp_A\",\"match_by\":\"email\",\"contacts\":[{\"first_name\":\"Ada\"},{\"contact_id\":\"cnt_B\"}]}");
    }

    [Fact]
    public async Task Add_contacts_reads_standard_input()
    {
        using var cli = new CliRun(standardInput: "[{\"contact_id\":\"cnt_B\"}]");

        var exit = await cli.RunAsync("campaign", "add-contacts", "cmp_A", "--file", "-");

        Assert.Equal(ExitCodes.Success, exit);
        cli.AssertPosted(Operations.CampaignAddContacts, "{\"campaign_id\":\"cmp_A\",\"contacts\":[{\"contact_id\":\"cnt_B\"}]}");
    }

    [Fact]
    public async Task Add_contacts_without_a_file_is_a_usage_error()
    {
        using var cli = new CliRun();

        var exit = await cli.RunAsync("campaign", "add-contacts", "cmp_A");

        Assert.Equal(ExitCodes.Usage, exit);
        Assert.Contains("--file", cli.Error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Remove_contacts_collects_every_contact_option()
    {
        using var cli = new CliRun();

        var exit = await cli.RunAsync("campaign", "remove-contacts", "cmp_A", "--contact", "cnt_A", "--contact", "cnt_B", "--reason", "they asked");

        Assert.Equal(ExitCodes.Success, exit);
        cli.AssertPosted(
            Operations.CampaignRemoveContacts,
            "{\"campaign_id\":\"cmp_A\",\"contact_ids\":[\"cnt_A\",\"cnt_B\"],\"reason\":\"they asked\"}");
    }

    [Theory]
    [InlineData(new[] { "cmp_A", "--state", "excluded", "--limit", "10" }, "{\"campaign_ids\":[\"cmp_A\"],\"state\":\"excluded\",\"limit\":10}")]
    [InlineData(new[] { "cmp_A", "cmp_B", "--summary", "failed", "--query", "ada" }, "{\"campaign_ids\":[\"cmp_A\",\"cmp_B\"],\"summary\":\"failed\",\"query\":\"ada\"}")]
    [InlineData(
        new[] { "--activity", "worked_on", "--since", "2026-10-09T00:00:00Z", "--until", "2026-10-16T09:00:00Z" },
        "{\"activity\":\"worked_on\",\"since\":\"2026-10-09T00:00:00Z\",\"until\":\"2026-10-16T09:00:00Z\"}")]
    public async Task List_contacts_passes_its_filter(string[] options, string posted)
    {
        using var cli = new CliRun();

        var exit = await cli.RunAsync(["campaign", "list-contacts", .. options]);

        Assert.Equal(ExitCodes.Success, exit);
        cli.AssertPosted(Operations.CampaignListContacts, posted);
    }

    /// <summary>Each people read keeps to one audience's people by <c>--audience</c>, the audience's number, sent as <c>audience</c>.</summary>
    [Theory]
    [InlineData("summary", new string[0])]
    [InlineData("activity", new[] { "--since", "2026-10-09T00:00:00Z", "--until", "2026-10-16T09:00:00Z" })]
    [InlineData("activity-by-period", new[] { "--count", "replied", "--period", "day", "--since", "2026-10-09T00:00:00Z", "--until", "2026-10-16T09:00:00Z" })]
    [InlineData("list-contacts", new[] { "--summary", "waiting" })]
    public async Task A_people_read_names_one_audience_by_its_number(string verb, string[] rest)
    {
        using var cli = new CliRun();

        var exit = await cli.RunAsync(["campaign", verb, "cmp_A", "--audience", "2", .. rest]);

        Assert.Equal(ExitCodes.Success, exit);
        var sent = JsonNode.Parse(cli.Sent!)!;
        Assert.Equal(("[\"cmp_A\"]", 2), (sent["campaign_ids"]!.ToJsonString(), sent["audience"]!.GetValue<int>()));
    }

    [Fact]
    public async Task Summary_asks_for_the_campaigns_named_or_for_every_one()
    {
        using var some = new CliRun();
        using var every = new CliRun();

        Assert.Equal(ExitCodes.Success, await some.RunAsync("campaign", "summary", "cmp_A", "cmp_B"));
        Assert.Equal(ExitCodes.Success, await every.RunAsync("campaign", "summary"));

        some.AssertPosted(Operations.CampaignSummary, "{\"campaign_ids\":[\"cmp_A\",\"cmp_B\"]}");
        every.AssertPosted(Operations.CampaignSummary, "{}");
    }

    /// <summary>
    /// Archived contacts are counted unless the flag leaves them out; it is sent only when given, and beside an audience as
    /// on its own.
    /// </summary>
    [Theory]
    [InlineData(new[] { "--exclude-archived-contacts" }, "{\"exclude_archived_contacts\":true}")]
    [InlineData(new[] { "cmp_A", "--exclude-archived-contacts" }, "{\"campaign_ids\":[\"cmp_A\"],\"exclude_archived_contacts\":true}")]
    [InlineData(new[] { "cmp_A", "--audience", "1", "--exclude-archived-contacts" }, "{\"campaign_ids\":[\"cmp_A\"],\"audience\":1,\"exclude_archived_contacts\":true}")]
    public async Task Summary_leaves_out_archived_contacts_when_asked(string[] options, string posted)
    {
        using var cli = new CliRun();

        Assert.Equal(ExitCodes.Success, await cli.RunAsync(["campaign", "summary", .. options]));

        cli.AssertPosted(Operations.CampaignSummary, posted);
    }

    /// <summary>
    /// The list of a campaign's people and the reads behind the activity counts take the same flag as the summary: sent as
    /// <c>exclude_archived_contacts</c> only when given, and nothing is sent without it.
    /// </summary>
    [Theory]
    [InlineData("list-contacts", new[] { "cmp_A", "--exclude-archived-contacts" }, "{\"campaign_ids\":[\"cmp_A\"],\"exclude_archived_contacts\":true}")]
    [InlineData("list-contacts", new[] { "cmp_A" }, "{\"campaign_ids\":[\"cmp_A\"]}")]
    [InlineData(
        "activity",
        new[] { "cmp_A", "--since", "2026-10-09T00:00:00Z", "--until", "2026-10-16T09:00:00Z", "--exclude-archived-contacts" },
        "{\"campaign_ids\":[\"cmp_A\"],\"since\":\"2026-10-09T00:00:00Z\",\"until\":\"2026-10-16T09:00:00Z\",\"exclude_archived_contacts\":true}")]
    [InlineData(
        "activity",
        new[] { "cmp_A", "--since", "2026-10-09T00:00:00Z", "--until", "2026-10-16T09:00:00Z" },
        "{\"campaign_ids\":[\"cmp_A\"],\"since\":\"2026-10-09T00:00:00Z\",\"until\":\"2026-10-16T09:00:00Z\"}")]
    [InlineData(
        "activity-by-period",
        new[] { "--count", "replied", "--period", "day", "--since", "2026-10-09T00:00:00Z", "--until", "2026-10-16T09:00:00Z", "--zone", "UTC", "--exclude-archived-contacts" },
        "{\"count\":\"replied\",\"period\":\"day\",\"since\":\"2026-10-09T00:00:00Z\",\"until\":\"2026-10-16T09:00:00Z\",\"zone\":\"UTC\",\"exclude_archived_contacts\":true}")]
    [InlineData(
        "activity-by-period",
        new[] { "--count", "replied", "--period", "day", "--since", "2026-10-09T00:00:00Z", "--until", "2026-10-16T09:00:00Z", "--zone", "UTC" },
        "{\"count\":\"replied\",\"period\":\"day\",\"since\":\"2026-10-09T00:00:00Z\",\"until\":\"2026-10-16T09:00:00Z\",\"zone\":\"UTC\"}")]
    public async Task The_people_reads_leave_out_archived_contacts_when_asked(string verb, string[] options, string posted)
    {
        using var cli = new CliRun();

        Assert.Equal(ExitCodes.Success, await cli.RunAsync(["campaign", verb, .. options]));

        cli.AssertPosted(verb switch
        {
            "list-contacts" => Operations.CampaignListContacts,
            "activity" => Operations.CampaignActivity,
            _ => Operations.CampaignActivityByPeriod,
        }, posted);
    }

    /// <summary>Each verb that takes the flag says of it what the summary says.</summary>
    [Theory]
    [InlineData("summary")]
    [InlineData("list-contacts")]
    [InlineData("activity")]
    [InlineData("activity-by-period")]
    public async Task Each_people_read_s_help_says_the_same_of_the_flag(string verb)
    {
        using var cli = new CliRun();

        Assert.Equal(ExitCodes.Success, await cli.RunAsync("campaign", verb, "--help"));

        var said = string.Join(" ", cli.Text.Split((char[])['\r', '\n', ' '], StringSplitOptions.RemoveEmptyEntries));
        Assert.True(
            said.Contains("--exclude-archived-contacts Leave out archived contacts, whom contact list shows only with --include-archived; by default they are included.", StringComparison.Ordinal),
            $"The help of campaign {verb} does not say it:{Environment.NewLine}{cli.Text}");
    }

    [Fact]
    public async Task Activity_sends_its_period_and_the_campaigns_named()
    {
        using var cli = new CliRun();

        var exit = await cli.RunAsync("campaign", "activity", "--since", "2026-10-09T00:00:00Z", "--until", "now", "cmp_A");

        Assert.Equal(ExitCodes.Success, exit);
        var sent = JsonNode.Parse(cli.Sent!)!;
        Assert.Equal("[\"cmp_A\"]", sent["campaign_ids"]!.ToJsonString());
        Assert.Equal("2026-10-09T00:00:00Z", sent["since"]!.GetValue<string>());
        Assert.EndsWith("Z", sent["until"]!.GetValue<string>(), StringComparison.Ordinal);
        Assert.NotEqual("now", sent["until"]!.GetValue<string>());
    }

    [Fact]
    public async Task Activity_by_period_sends_its_count_period_window_and_the_zone_named_or_this_machine_s()
    {
        using var named = new CliRun();
        using var unnamed = new CliRun();

        Assert.Equal(ExitCodes.Success, await named.RunAsync(
            "campaign", "activity-by-period", "cmp_A", "--count", "replied", "--period", "day",
            "--since", "2026-10-09T00:00:00Z", "--until", "2026-10-16T09:00:00Z", "--zone", "Europe/Kyiv"));
        Assert.Equal(ExitCodes.Success, await unnamed.RunAsync(
            "campaign", "activity-by-period", "--count", "people", "--period", "hour", "--since", "2026-10-16T00:00:00Z", "--until", "now"));

        named.AssertPosted(
            Operations.CampaignActivityByPeriod,
            "{\"campaign_ids\":[\"cmp_A\"],\"count\":\"replied\",\"period\":\"day\",\"since\":\"2026-10-09T00:00:00Z\",\"until\":\"2026-10-16T09:00:00Z\",\"zone\":\"Europe/Kyiv\"}");
        var zone = JsonNode.Parse(unnamed.Sent!)!["zone"]!.GetValue<string>();
        Assert.True(TimeZoneInfo.FindSystemTimeZoneById(zone).HasIanaId, zone);
    }

    [Fact]
    public async Task Human_mode_renders_the_buckets()
    {
        var series = JsonSerializer.Serialize(
            new CampaignActivityByPeriodResponse(
                ActivityPeriod.Day,
                "UTC",
                [
                    new ActivityBucket(new DateTimeOffset(2026, 10, 12, 0, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 10, 13, 0, 0, 0, TimeSpan.Zero), 1),
                    new ActivityBucket(new DateTimeOffset(2026, 10, 13, 0, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 10, 13, 9, 0, 0, TimeSpan.Zero), 0),
                ]),
            JasonJson.Options);
        using var cli = new CliRun(series);
        var exit = await cli.RunAsync(
            "campaign", "activity-by-period", "--count", "replied", "--period", "day", "--since", "2026-10-12T00:00:00Z", "--until", "2026-10-13T09:00:00Z", "--human");
        Assert.Equal(ExitCodes.Success, exit);
        var lines = cli.Text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        Assert.Equal(["FROM", "TO", "PEOPLE"], Cells(lines[0]));
        Assert.Equal(3, Cells(lines[2]).Length);
        Assert.Equal("1", Cells(lines[2])[^1]);
        Assert.Equal("0", Cells(lines[3])[^1]);
    }

    [Fact]
    public async Task Human_mode_renders_the_summary_with_a_dash_where_no_sequence_is_linked()
    {
        var summary = JsonSerializer.Serialize(
            new CampaignSummaryResponse(
            [
                new CampaignSummaryRow("cmp_A", 22, 3, 3, 2, 2, 6, 2, 6, Removed: 1, SequenceLinked: true),
                new CampaignSummaryRow("cmp_B", 2, 0, 0, 0, 1, 1, 0, 0, Removed: 0, SequenceLinked: false),
            ],
            new CampaignSummaryTotal(22, 3, 3, 2, 3, 7, 2, 6, Removed: 1)),
            JasonJson.Options);
        using var cli = new FolderRun(null, Answer(summary, Campaigns(("cmp_A", "Spring outreach"), ("cmp_B", "Partners"))));

        var exit = await cli.RunAsync("campaign", "summary", "--human");

        Assert.Equal(ExitCodes.Success, exit);
        var lines = cli.Text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        Assert.Equal(["CAMPAIGN", "ID", "PEOPLE", "WAITING", "FAILED", "WORKING", "NEW", "IDLE", "SEQUENCE", "OUT", "REMOVED"], Cells(lines[0]));
        Assert.Equal(["Spring outreach", "cmp_A", "22", "3", "3", "2", "2", "6", "2", "6", "1"], Cells(lines[2]));
        Assert.Equal(["Partners", "cmp_B", "2", "0", "0", "0", "1", "1", "-", "-", "0"], Cells(lines[3]));
        Assert.Equal(["TOTAL", "-", "22", "3", "3", "2", "3", "7", "2", "6", "1"], Cells(lines[^1]));

        // With no campaign linked to a sequence, the total has none to count either.
        var unlinked = JsonSerializer.Serialize(
            new CampaignSummaryResponse(
                [new CampaignSummaryRow("cmp_B", 2, 0, 0, 0, 1, 1, 0, 1, Removed: 0, SequenceLinked: false)],
                new CampaignSummaryTotal(2, 0, 0, 0, 1, 1, 0, 1, Removed: 0)),
            JasonJson.Options);
        using var alone = new FolderRun(null, Answer(unlinked, Campaigns(("cmp_B", "Partners"))));
        Assert.Equal(ExitCodes.Success, await alone.RunAsync("campaign", "summary", "cmp_B", "--human"));
        var only = alone.Text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        Assert.Equal(["TOTAL", "-", "2", "0", "0", "0", "1", "1", "-", "-", "0"], Cells(only[^1]));
    }

    [Fact]
    public async Task Human_mode_renders_the_activity()
    {
        var activity = JsonSerializer.Serialize(
            new CampaignActivityResponse(
                [new CampaignActivityRow("cmp_A", 14, 2, 9, 4, 2, 2, Removed: 1), new CampaignActivityRow("cmp_B", 1, 0, 1, 0, 0, 0, Removed: 0)],
                new CampaignActivityTotal(14, 2, 9, 4, 2, 2, Removed: 1)),
            JasonJson.Options);
        using var cli = new FolderRun(null, Answer(activity, Campaigns(("cmp_A", "Spring outreach"), ("cmp_B", "Partners"))));

        var exit = await cli.RunAsync("campaign", "activity", "--since", "2026-10-09T00:00:00Z", "--until", "2026-10-16T09:00:00Z", "--human");

        Assert.Equal(ExitCodes.Success, exit);
        var lines = cli.Text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        Assert.Equal(["CAMPAIGN", "ID", "PEOPLE", "ADDED", "WORKED ON", "CONTACTED", "REPLIED", "OPTED OUT", "REMOVED"], Cells(lines[0]));
        Assert.Equal(["Spring outreach", "cmp_A", "14", "2", "9", "4", "2", "2", "1"], Cells(lines[2]));
        Assert.Equal(["Partners", "cmp_B", "1", "0", "1", "0", "0", "0", "0"], Cells(lines[3]));
        Assert.Equal(["TOTAL", "-", "14", "2", "9", "4", "2", "2", "1"], Cells(lines[^1]));
    }

    /// <summary>
    /// The counts name each campaign by its id alone, so the human rendering reads the names from the campaign listing,
    /// archived campaigns included, page after page until every campaign it shows is named.
    /// </summary>
    [Theory]
    [InlineData("summary")]
    [InlineData("activity")]
    public async Task Human_mode_reads_the_names_page_after_page_until_every_campaign_is_named(string verb)
    {
        var counts = Counts(verb, "cmp_A", "cmp_B");
        var first = JsonSerializer.Serialize(new Page<CampaignSummaryDto>([Campaign("cmp_A", "Spring outreach")], "Y3Vyc29y"), JasonJson.Options);
        var second = JsonSerializer.Serialize(new Page<CampaignSummaryDto>([Campaign("cmp_B", "Partners")], null), JasonJson.Options);
        using var cli = new FolderRun(null, (operation, body) => operation switch
        {
            Operations.CampaignList when body["cursor"] is null => (HttpStatusCode.OK, first),
            Operations.CampaignList => (HttpStatusCode.OK, second),
            _ => (HttpStatusCode.OK, counts),
        });

        var exit = await cli.RunAsync(Args(verb, "--human"));

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Equal([Operation(verb), Operations.CampaignList, Operations.CampaignList], cli.Operations);
        cli.AssertSent(1, "{\"include_archived\":true,\"limit\":1000}");
        cli.AssertSent(2, "{\"include_archived\":true,\"limit\":1000,\"cursor\":\"Y3Vyc29y\"}");
        var lines = cli.Text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        Assert.Equal(["Spring outreach", "cmp_A"], Cells(lines[2])[..2]);
        Assert.Equal(["Partners", "cmp_B"], Cells(lines[3])[..2]);
    }

    /// <summary>Where the names cannot be read, the counts are still shown: each campaign by its id, with a dash for its name.</summary>
    [Theory]
    [InlineData("summary")]
    [InlineData("activity")]
    public async Task Human_mode_shows_the_counts_when_the_names_cannot_be_read(string verb)
    {
        var counts = Counts(verb, "cmp_A");
        using var cli = new FolderRun(null, (operation, _) => operation == Operations.CampaignList
            ? (HttpStatusCode.InternalServerError, string.Empty)
            : (HttpStatusCode.OK, counts));

        var exit = await cli.RunAsync(Args(verb, "--human"));

        Assert.Equal(ExitCodes.Success, exit);
        var lines = cli.Text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        Assert.Equal(["CAMPAIGN", "ID"], Cells(lines[0])[..2]);
        Assert.Equal(["-", "cmp_A"], Cells(lines[2])[..2]);
    }

    /// <summary>The JSON output is the API's answer as it came, and nothing more is asked for it.</summary>
    [Theory]
    [InlineData("summary")]
    [InlineData("activity")]
    public async Task Json_mode_prints_the_counts_as_they_came_and_asks_nothing_more(string verb)
    {
        var counts = Counts(verb, "cmp_A");
        using var cli = new FolderRun(null, (_, _) => (HttpStatusCode.OK, counts));

        var exit = await cli.RunAsync(Args(verb));

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Equal([Operation(verb)], cli.Operations);
        Assert.Equal(counts, cli.Text.TrimEnd());
    }

    [Fact]
    public async Task Audience_asks_for_one_campaign_s_audience_a_page_at_a_time()
    {
        using var cli = new CliRun();

        var exit = await cli.RunAsync("campaign", "audience", "cmp_A", "--limit", "2", "--cursor", "Mg", "--search-limit", "5");

        Assert.Equal(ExitCodes.Success, exit);
        cli.AssertPosted(Operations.CampaignAudience, "{\"campaign_id\":\"cmp_A\",\"limit\":2,\"cursor\":\"Mg\",\"search_limit\":5}");
    }

    /// <summary>
    /// For people, the reserve and the allowance as lines, then each audience: its definition laid out by the schema the
    /// plugin the campaign's route for searches declares — its properties in the schema's order, each with the side of a
    /// person it filters on — and its searches as a table. The schema is asked once, of <c>operation.describe</c>.
    /// </summary>
    [Fact]
    public async Task Human_mode_renders_the_audience_with_each_definition_laid_out_by_the_plugin_s_schema()
    {
        var described = JsonSerializer.Serialize(new OperationDescriptionDto("audience.search", 1, "reply", "0.15.0", [], Definition()), JasonJson.Options);
        using var cli = new FolderRun(null, (operation, _) => (HttpStatusCode.OK, operation == Operations.OperationDescribe ? described : AudienceView()));

        var exit = await cli.RunAsync("campaign", "audience", "cmp_A", "--human");

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Equal([Operations.CampaignAudience, Operations.OperationDescribe], cli.Operations);
        cli.AssertSent(1, "{\"campaign_id\":\"cmp_A\",\"operation\":\"audience.search\"}");
        var lines = cli.Text.Split('\n').Select(line => line.TrimEnd()).Where(line => line.Length > 0).ToList();
        Assert.Equal(
            [
                "Campaign:   cmp_A",
                "Untouched:  2, threshold 3",
                "Allowance:  audience 2, 50 of 100 people, 50 per search, in force",
                "Let go:     1",
                "Set by:     human:Ada Example, 2026-10-16 09:00:00 UTC",
                "Audience 2  aud_B  made 2026-10-01 10:00:00 UTC  9 landed, 3 searches",
                "  industries (company): Software, Logistics",
                "  jobTitles (person): Head of Sales",
            ],
            lines[..8]);
        Assert.Equal(["SEARCH", "STATE", "ASKED", "FOUND", "LANDED", "BEGAN", "ENDED"], Cells(lines[8]));
        Assert.Equal(["wi_S3", "in_progress", "50", "-", "2", "2026-10-01 11:00:00 UTC", "-"], Cells(lines[10]));
        Assert.Equal(
            [
                "  2 earlier searches not shown.",
                "Audience 1  aud_A  made 2026-10-01 10:00:00 UTC  0 landed, 0 searches",
                "  industries (company): {value: Retail, type: exclude}, {value: Zürich Banking, type: include}",
                "  No search taken.",
            ],
            lines[11..]);
    }

    /// <summary>
    /// The rest a view may say: a campaign that keeps no threshold and holds no allowance, a page that goes on, a definition
    /// member the plugin's schema does not declare, which comes after the declared ones, and one it declares no side for.
    /// </summary>
    [Fact]
    public async Task Human_mode_says_no_threshold_no_allowance_the_next_page_and_what_the_schema_does_not_place()
    {
        var made = new DateTimeOffset(2026, 10, 1, 10, 0, 0, TimeSpan.Zero);
        var view = JsonSerializer.Serialize(
            new CampaignAudienceResponse(
                "cmp_A",
                new AudienceReserve(5, null),
                null,
                [new CampaignAudienceDto("aud_A", 1, made, new JsonObject { ["keywords"] = new JsonArray("fintech"), ["industries"] = new JsonArray("Software") }, 1, 1,
                    [new AudienceSearchDto("wi_S1", "finished", 1, 1, 1, made.AddHours(1), "finished", made.AddHours(2))])],
                "Mg"),
            JasonJson.Options);
        var described = JsonSerializer.Serialize(
            new OperationDescriptionDto(
                "audience.search", 1, "reply", "0.15.0", [],
                new JsonObject { ["schema"] = new JsonObject { ["properties"] = new JsonObject { ["industries"] = new JsonObject() } }, ["keys"] = new JsonObject() }),
            JasonJson.Options);
        using var cli = new FolderRun(null, (operation, _) => (HttpStatusCode.OK, operation == Operations.OperationDescribe ? described : view));

        var exit = await cli.RunAsync("campaign", "audience", "cmp_A", "--human");

        Assert.Equal(ExitCodes.Success, exit);
        var lines = cli.Text.Split('\n').Select(line => line.TrimEnd()).Where(line => line.Length > 0).ToList();
        Assert.Equal(["Campaign:   cmp_A", "Untouched:  5, no threshold", "Allowance:  none", "Audience 1  aud_A  made 2026-10-01 10:00:00 UTC  1 landed, 1 search"], lines[..4]);
        Assert.Equal(["  industries: Software", "  keywords: fintech"], lines[4..6]);
        Assert.Equal(["wi_S1", "finished", "1", "1", "1", "2026-10-01 11:00:00 UTC", "finished, 2026-10-01 12:00:00 UTC"], Cells(lines[8]));
        Assert.Equal("next cursor: Mg", lines[^1]);
    }

    /// <summary>Where the plugin's schema cannot be read, each definition is shown as the runtime stored it, and the rest the same.</summary>
    [Fact]
    public async Task Human_mode_shows_a_definition_as_stored_where_the_schema_cannot_be_read()
    {
        using var cli = new FolderRun(null, (operation, _) => operation == Operations.OperationDescribe
            ? (HttpStatusCode.Conflict, "{\"error\":{\"code\":\"no_route\",\"message\":\"No route.\",\"retryable\":false}}")
            : (HttpStatusCode.OK, AudienceView()));

        var exit = await cli.RunAsync("campaign", "audience", "cmp_A", "--human");

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Contains("  definition: {\"jobTitles\":[\"Head of Sales\"],\"industries\":[\"Software\",\"Logistics\"]}", cli.Text, StringComparison.Ordinal);
        Assert.Contains("Audience 1  aud_A", cli.Text, StringComparison.Ordinal);
    }

    /// <summary>
    /// Spring's view: two untouched against a threshold of three, the allowance on audience 2, audience 2 with three
    /// searches of which the one still followed is listed, audience 1 with none the provider took. Audience 2's definition
    /// names its keys in another order than the plugin's schema declares them.
    /// </summary>
    private static string AudienceView()
    {
        var made = new DateTimeOffset(2026, 10, 1, 10, 0, 0, TimeSpan.Zero);
        var allowance = new StandingApprovalDto(
            "cmp_A", null, ["audience.search"], new AudienceRef("aud_B", 2), null, null, 100, 50, 1, 50, true,
            new ActorRef(ActorType.Human, "Ada Example"), new DateTimeOffset(2026, 10, 16, 9, 0, 0, TimeSpan.Zero), "jrn_A");
        return JsonSerializer.Serialize(
            new CampaignAudienceResponse(
                "cmp_A",
                new AudienceReserve(2, 3),
                allowance,
                [
                    new CampaignAudienceDto(
                        "aud_B", 2, made, new JsonObject { ["jobTitles"] = new JsonArray("Head of Sales"), ["industries"] = new JsonArray("Software", "Logistics") }, 9, 3,
                        [new AudienceSearchDto("wi_S3", "in_progress", 50, null, 2, made.AddHours(1), null, null)]),
                    new CampaignAudienceDto(
                        "aud_A",
                        1,
                        made,
                        new JsonObject
                        {
                            ["industries"] = new JsonArray(
                                new JsonObject { ["value"] = "Retail", ["type"] = "exclude" },
                                new JsonObject { ["value"] = "Zürich Banking", ["type"] = "include" }),
                        },
                        0,
                        0,
                        []),
                ],
                null),
            JasonJson.Options);
    }

    /// <summary>A plugin's declared definition: industries on the company's side, then job titles on the person's.</summary>
    private static JsonObject Definition() => new()
    {
        ["schema"] = new JsonObject
        {
            ["type"] = "object",
            ["properties"] = new JsonObject
            {
                ["industries"] = new JsonObject { ["type"] = "array" },
                ["jobTitles"] = new JsonObject { ["type"] = "array" },
            },
        },
        ["keys"] = new JsonObject
        {
            ["industries"] = new JsonObject { ["side"] = "company" },
            ["jobTitles"] = new JsonObject { ["side"] = "person" },
        },
    };

    private static Func<string, JsonObject, (HttpStatusCode Status, string Body)> Answer(string counts, string campaigns) =>
        (operation, _) => (HttpStatusCode.OK, operation == Operations.CampaignList ? campaigns : counts);

    private static string Campaigns(params (string Id, string Name)[] campaigns) =>
        JsonSerializer.Serialize(
            new Page<CampaignSummaryDto>([.. campaigns.Select(campaign => Campaign(campaign.Id, campaign.Name))], null),
            JasonJson.Options);

    private static CampaignSummaryDto Campaign(string id, string name) =>
        new(id, name, CampaignStatus.Active, "prj_A", "default", Moment, Moment, null);

    private static string Counts(string verb, params string[] ids) => verb == "summary"
        ? JsonSerializer.Serialize(
            new CampaignSummaryResponse(
                [.. ids.Select(id => new CampaignSummaryRow(id, 1, 0, 0, 0, 1, 0, 0, 0, Removed: 0, SequenceLinked: false))],
                new CampaignSummaryTotal(ids.Length, 0, 0, 0, ids.Length, 0, 0, 0, Removed: 0)),
            JasonJson.Options)
        : JsonSerializer.Serialize(
            new CampaignActivityResponse(
                [.. ids.Select(id => new CampaignActivityRow(id, 1, 1, 0, 0, 0, 0, Removed: 0))],
                new CampaignActivityTotal(ids.Length, ids.Length, 0, 0, 0, 0, Removed: 0)),
            JasonJson.Options);

    private static string Operation(string verb) => verb == "summary" ? Operations.CampaignSummary : Operations.CampaignActivity;

    private static string[] Args(string verb, params string[] more) => verb == "summary"
        ? ["campaign", "summary", .. more]
        : ["campaign", "activity", "--since", "2026-10-09T00:00:00Z", "--until", "2026-10-16T09:00:00Z", .. more];

    [Fact]
    public async Task An_unknown_option_is_a_usage_error()
    {
        using var cli = new CliRun();

        var exit = await cli.RunAsync("campaign", "get", "cmp_A", "--bogus");

        Assert.Equal(ExitCodes.Usage, exit);
        Assert.Contains("--bogus", cli.Error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_reserved_actor_is_refused_on_a_read_only_verb()
    {
        using var cli = new CliRun();

        var exit = await cli.RunAsync("campaign", "get", "cmp_A", "--actor", "system");

        Assert.Equal(ExitCodes.Usage, exit);
        Assert.Contains("--actor", cli.Error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Create_names_the_project_the_campaign_is_in()
    {
        using var cli = new CliRun();

        await cli.RunAsync("campaign", "create", "--name", "Q3 leads", "--project", "prj_A");

        cli.AssertPosted("campaign.create", """{"name":"Q3 leads","project_id":"prj_A"}""");
    }

    [Fact]
    public async Task Update_moves_the_campaign_to_another_project()
    {
        using var cli = new CliRun();

        await cli.RunAsync("campaign", "update", "cmp_A", "--project", "prj_B", "--reason", "it belongs there");

        cli.AssertPosted("campaign.update", """{"campaign_id":"cmp_A","project_id":"prj_B","reason":"it belongs there"}""");
    }

    /// <summary>A campaign is always in a project, so the project is not something <c>--clear</c> can take away.</summary>
    [Fact]
    public async Task Update_cannot_clear_the_project()
    {
        using var cli = new CliRun();

        var exit = await cli.RunAsync("campaign", "update", "cmp_A", "--clear", "project_id");

        Assert.Equal(ExitCodes.Usage, exit);
        Assert.Null(cli.Url);
    }

    [Fact]
    public async Task List_keeps_one_project_s_campaigns()
    {
        using var cli = new CliRun();

        await cli.RunAsync("campaign", "list", "--project", "prj_A");

        cli.AssertPosted("campaign.list", """{"project_id":"prj_A"}""");
    }

    [Fact]
    public async Task Human_mode_renders_one_campaign()
    {
        var campaign = JsonSerializer.Serialize(
            new CampaignDto("cmp_A", "LatAm", CampaignStatus.Active, "prj_A", "default", new JsonObject { ["icp"] = "founders", ["tone"] = "plain" }, [], "local-claude", null, Moment, Moment, null, AudienceThreshold: 50),
            JasonJson.Options);
        using var cli = new CliRun(campaign);

        var exit = await cli.RunAsync("campaign", "get", "cmp_A", "--human");

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Contains("cmp_A", cli.Text, StringComparison.Ordinal);
        Assert.Matches("(?m)^Project: +default \\(prj_A\\)\\r?$", cli.Text);
        Assert.Contains("LatAm", cli.Text, StringComparison.Ordinal);
        Assert.Contains("active", cli.Text, StringComparison.Ordinal);
        Assert.Contains("icp, tone", cli.Text, StringComparison.Ordinal);

        // Which host this campaign's agent work uses is part of what the campaign is, so it is shown with it.
        Assert.Contains("Profile:", cli.Text, StringComparison.Ordinal);
        Assert.Contains("local-claude", cli.Text, StringComparison.Ordinal);

        // So is the reserve it keeps, since the dispatcher counts against it.
        Assert.Matches("(?m)^Reserve: +threshold 50 untouched\\r?$", cli.Text);
        Assert.DoesNotContain("{", cli.Text, StringComparison.Ordinal);
    }

    /// <summary>
    /// The runtime keeps two context keys that differ only in case as two keys, and the rendering reads its answer the
    /// same way: a create the runtime performed is shown, both keys listed, rather than refused after the fact.
    /// </summary>
    [Fact]
    public async Task Human_mode_lists_context_keys_that_differ_only_in_case_as_two_keys()
    {
        var campaign = JsonSerializer.Serialize(
            new CampaignDto("cmp_A", "LatAm", CampaignStatus.Draft, "prj_A", "default", new JsonObject { ["region"] = "latam", ["Region"] = "emea" }, [], null, null, Moment, Moment, null),
            JasonJson.Options);
        using var cli = new CliRun(campaign);

        var exit = await cli.RunAsync("campaign", "create", "--name", "LatAm", "--context", """{"region":"latam","Region":"emea"}""", "--human");

        Assert.Equal(ExitCodes.Success, exit);
        cli.AssertPosted(Operations.CampaignCreate, """{"name":"LatAm","context":{"region":"latam","Region":"emea"}}""");
        Assert.Matches("(?m)^Context keys: +region, Region\\r?$", cli.Text);
    }

    /// <summary>
    /// A campaign's name is refused a control character when it is written, but a name written before that rule, or
    /// one that reaches the runtime some other way, is still somebody's text: the view and the listing show a character
    /// that changes how the line reads as U+FFFD, a mark that can be seen, and never write it.
    /// </summary>
    [Theory]
    [InlineData("get")]
    [InlineData("list")]
    public async Task Human_mode_shows_a_character_that_changes_the_line_in_a_campaign_name_as_a_replacement_mark(string verb)
    {
        const string Hostile = "Lat\u001b[2J\bAm‮";
        using var cli = new CliRun(verb == "get"
            ? JsonSerializer.Serialize(new CampaignDto("cmp_A", Hostile, CampaignStatus.Active, "prj_A", "default", [], [], null, null, Moment, Moment, null), JasonJson.Options)
            : JsonSerializer.Serialize(
                new Page<CampaignSummaryDto>([new CampaignSummaryDto("cmp_A", Hostile, CampaignStatus.Draft, "prj_A", "default", Moment, Moment, null)], null),
                JasonJson.Options));

        var exit = await cli.RunAsync(verb == "get" ? ["campaign", "get", "cmp_A", "--human"] : ["campaign", "list", "--human"]);

        Assert.Equal(ExitCodes.Success, exit);
        foreach (var character in new[] { '\u001b', '\b', '‮' })
        {
            Assert.DoesNotContain(character, cli.Text);
        }

        Assert.Contains("Lat�[2J�Am�", cli.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Human_mode_renders_a_campaign_listing_with_its_cursor()
    {
        var page = JsonSerializer.Serialize(
            new Page<CampaignSummaryDto>([new CampaignSummaryDto("cmp_A", "LatAm", CampaignStatus.Draft, "prj_A", "default", Moment, Moment, null)], "Y21wX0E"),
            JasonJson.Options);
        using var cli = new CliRun(page);

        var exit = await cli.RunAsync("campaign", "list", "--human");

        Assert.Equal(ExitCodes.Success, exit);

        // The project is shown by the slug the listing carries: one request, nothing asked again per row.
        Assert.Equal(1, cli.Requests);
        Assert.Matches("(?m)^ID +NAME +STATUS +PROJECT +CREATED", cli.Text);
        Assert.Matches("(?m)^cmp_A +LatAm +draft +default ", cli.Text);
        Assert.Contains("NAME", cli.Text, StringComparison.Ordinal);
        Assert.Contains("cmp_A", cli.Text, StringComparison.Ordinal);
        Assert.Contains("draft", cli.Text, StringComparison.Ordinal);
        Assert.Contains("next cursor: Y21wX0E", cli.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("{", cli.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Human_mode_renders_the_members_of_a_campaign()
    {
        var contact = new ContactDto(
            "cnt_A",
            "Ada",
            "Lovelace",
            "Analytical Engines",
            "Mathematician",
            "Europe/Kyiv",
            [new ChannelDto("email", "ada@example.com", null, true, null)],
            [],
            [],
            Moment,
            Moment,
            null,
            Campaigns: [new ContactCampaignDto("cmp_A", "Spring", MembershipState.Enrolled, Moment, Moment, [CampaignSummaryFilter.New]), new ContactCampaignDto("cmp_B", "Autumn", MembershipState.Enrolled, Moment, Moment, [CampaignSummaryFilter.Idle])]);
        var worked = new MembershipWhy("activity", null, "researcher", Moment, null, null);
        var page = JsonSerializer.Serialize(
            new Page<MembershipItemDto>(
                [
                    new MembershipItemDto(
                        contact,
                        MembershipState.Enrolled,
                        Moment,
                        Moment,
                        worked,
                        [new CountedIn("cmp_B", worked), new CountedIn("cmp_A")],
                        new MembershipSource(new AudienceRef("aud_A", 2), "wi_A", Moment)),
                ],
                null),
            JasonJson.Options);
        using var cli = new CliRun(page);

        var exit = await cli.RunAsync("campaign", "list-contacts", "cmp_A", "cmp_B", "--human");

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Contains("cnt_A", cli.Text, StringComparison.Ordinal);
        Assert.Contains("Ada Lovelace", cli.Text, StringComparison.Ordinal);
        Assert.Contains("enrolled", cli.Text, StringComparison.Ordinal);
        Assert.Contains("ada@example.com", cli.Text, StringComparison.Ordinal);
        Assert.Contains("Autumn, Spring", cli.Text, StringComparison.Ordinal);
        Assert.Contains("researcher", cli.Text, StringComparison.Ordinal);
        Assert.Contains("SOURCE", cli.Text, StringComparison.Ordinal);
        Assert.Contains("audience 2", cli.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("{", cli.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Human_mode_summarizes_an_import_and_names_every_rejected_item()
    {
        var result = JsonSerializer.Serialize(
            new AddContactsResult(
                "cmp_A",
                new AddContactsSummary(2, 1, 1),
                [
                    new AddContactsItemResult(0, AddContactsItemStatus.Added, "cnt_A", true, MembershipState.Enrolled, null),
                    new AddContactsItemResult(1, AddContactsItemStatus.Rejected, null, false, null, new ErrorBody("ambiguous_match", "Two contacts share that address.", false)),
                ]),
            JasonJson.Options);
        using var file = new TempFile("[]");
        using var cli = new CliRun(result);

        var exit = await cli.RunAsync("campaign", "add-contacts", "cmp_A", "--file", file.Path, "--human");

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Contains("added 2, already members 1, rejected 1", cli.Text, StringComparison.Ordinal);
        Assert.Contains("#1: ambiguous_match", cli.Text, StringComparison.Ordinal);
        Assert.Contains("Two contacts share that address.", cli.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("{", cli.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Human_mode_summarizes_a_removal()
    {
        var result = JsonSerializer.Serialize(
            new RemoveContactsResult(
                "cmp_A",
                new RemoveContactsSummary(1, 1, 0, 0),
                [
                    new RemoveContactsItemResult(0, RemoveContactsItemStatus.Removed, "cnt_A", null),
                    new RemoveContactsItemResult(1, RemoveContactsItemStatus.NotMember, "cnt_B", null),
                ]),
            JasonJson.Options);
        using var cli = new CliRun(result);

        var exit = await cli.RunAsync("campaign", "remove-contacts", "cmp_A", "--contact", "cnt_A", "--human");

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Contains("removed 1", cli.Text, StringComparison.Ordinal);
        Assert.Contains("not members 1", cli.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("{", cli.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Human_mode_falls_back_to_the_raw_body_when_the_shape_is_unknown()
    {
        using var cli = new CliRun("{\"unexpected\":true}");

        var exit = await cli.RunAsync("campaign", "get", "cmp_A", "--human");

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Contains("{\"unexpected\":true}", cli.Text, StringComparison.Ordinal);
    }

    /// <summary>A rendered row's cells: columns are set apart by two spaces or more, and a header may hold one.</summary>
    private static string[] Cells(string line) =>
        System.Text.RegularExpressions.Regex.Split(line.Trim(), " {2,}");
}

/// <summary>
/// One CLI invocation against a runtime that is not there: a descriptor pointing at a fake handler, which
/// captures the request the verb composed and answers with the body the test chose.
/// </summary>
internal sealed class CliRun : IDisposable
{
    private static readonly RuntimeDescriptor Live = new("v1", "0.1.0-dev", "rt_LIVE", 77, "http://127.0.0.1:5000", "the-token", DateTimeOffset.UnixEpoch);

    private readonly TempPaths _paths = new();
    private readonly CliEnvironment _environment;

    public CliRun(string response = "{}", string? standardInput = null, HttpStatusCode status = HttpStatusCode.OK, bool descriptor = true, Jason.Cli.Process.IUrlOpener? opener = null, string? accountName = null)
    {
        if (descriptor)
        {
            _paths.WriteDescriptor(Live);
        }

        _environment = new CliEnvironment(
            Output,
            Error,
            _paths.Paths,
            new FakeHandler(request =>
            {
                Url = request.RequestUri!.ToString();
                Sent = request.Content!.ReadAsStringAsync(CancellationToken.None).GetAwaiter().GetResult();
                Requests++;
                return new HttpResponseMessage(status) { Content = new StringContent(response, Encoding.UTF8, "application/json") };
            }),
            standardInput is null ? null : new StringReader(standardInput),
            Opener: opener,
            AccountName: accountName);
    }

    public StringWriter Output { get; } = new();

    public StringWriter Error { get; } = new();

    public string? Url { get; private set; }

    public string? Sent { get; private set; }

    /// <summary>
    /// How many requests the invocation sent: a renderer answers from the one response, never by asking again — except the
    /// counts of campaign summary and activity, whose names it reads from the campaign listing (those rows use FolderRun).
    /// </summary>
    public int Requests { get; private set; }

    public string Text => Output.ToString();

    public Task<int> RunAsync(params string[] args) => CliApp.RunAsync(args, _environment, TestContext.Current.CancellationToken);

    /// <summary>The environment this run hands a verb, for a code path that is reached other than through <see cref="CliApp"/>.</summary>
    public CliEnvironment Environment => _environment;

    /// <summary>The verb reached the right operation with the right body; property order is not part of the contract.</summary>
    public void AssertPosted(string operation, string expectedBody)
    {
        Assert.Equal("http://127.0.0.1:5000/v1/" + operation, Url);
        Assert.NotNull(Sent);
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(expectedBody), JsonNode.Parse(Sent)), $"The body was {Sent}");
    }

    public void Dispose() => _paths.Dispose();
}
