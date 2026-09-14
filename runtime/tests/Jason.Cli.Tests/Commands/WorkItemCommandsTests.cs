using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using Jason.Cli;
using Jason.Contracts.Api;
using Jason.Contracts.Json;

namespace Jason.Cli.Tests.Commands;

public class WorkItemCommandsTests
{
    private static readonly DateTimeOffset Moment = new(2026, 9, 14, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Create_sends_every_option_under_its_api_name()
    {
        using var cli = new CliRun();

        var exit = await cli.RunAsync(
            "workitem",
            "create",
            "cmp_A",
            "--kind",
            "ai_role",
            "--role",
            "researcher",
            "--contact",
            "cnt_A",
            "--execution-profile",
            "fast",
            "--priority",
            "5",
            "--not-before",
            "2026-09-14T10:00:00Z",
            "--due-at",
            "2026-09-15T10:00:00Z",
            "--timeout",
            "60",
            "--heartbeat",
            "10",
            "--max-attempts",
            "2",
            "--context",
            "{\"icp\":\"founders\"}",
            "--result-format",
            "{\"shape\":\"summary\"}",
            "--reason",
            "the plan asks for it",
            "--actor",
            "role:planner");

        Assert.Equal(ExitCodes.Success, exit);
        cli.AssertPosted(
            Operations.WorkItemCreate,
            "{\"campaign_id\":\"cmp_A\",\"kind\":\"ai_role\",\"role\":\"researcher\",\"contact_id\":\"cnt_A\",\"execution_profile\":\"fast\",\"priority\":5,"
                + "\"not_before\":\"2026-09-14T10:00:00Z\",\"due_at\":\"2026-09-15T10:00:00Z\",\"timeout_seconds\":60,\"heartbeat_seconds\":10,\"max_attempts\":2,"
                + "\"context\":{\"icp\":\"founders\"},\"result_format\":{\"shape\":\"summary\"},\"reason\":\"the plan asks for it\",\"actor\":{\"type\":\"role\",\"id\":\"planner\"}}");
    }

    [Fact]
    public async Task Create_carries_an_operation_for_a_provider_item()
    {
        using var cli = new CliRun();

        var exit = await cli.RunAsync("workitem", "create", "cmp_A", "--kind", "provider_op", "--operation", "contact.enroll");

        Assert.Equal(ExitCodes.Success, exit);
        cli.AssertPosted(Operations.WorkItemCreate, "{\"campaign_id\":\"cmp_A\",\"kind\":\"provider_op\",\"operation\":\"contact.enroll\"}");
    }

    [Fact]
    public async Task Create_lays_the_options_over_a_file_body()
    {
        using var file = new TempFile("{\"kind\":\"ai_role\",\"role\":\"from the file\",\"context\":{\"icp\":\"founders\"}}");
        using var cli = new CliRun();

        var exit = await cli.RunAsync("workitem", "create", "cmp_A", "--file", file.Path, "--role", "researcher");

        Assert.Equal(ExitCodes.Success, exit);
        cli.AssertPosted(
            Operations.WorkItemCreate,
            "{\"campaign_id\":\"cmp_A\",\"kind\":\"ai_role\",\"role\":\"researcher\",\"context\":{\"icp\":\"founders\"}}");
    }

    [Fact]
    public async Task A_context_that_is_not_an_object_is_a_usage_error()
    {
        using var cli = new CliRun();

        var exit = await cli.RunAsync("workitem", "create", "cmp_A", "--context", "[1]");

        Assert.Equal(ExitCodes.Usage, exit);
        Assert.Contains("--context", cli.Error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Get_carries_the_positional_id_in_the_body()
    {
        using var cli = new CliRun();

        var exit = await cli.RunAsync("workitem", "get", "wi_A");

        Assert.Equal(ExitCodes.Success, exit);
        cli.AssertPosted(Operations.WorkItemGet, "{\"work_item_id\":\"wi_A\"}");
    }

    [Fact]
    public async Task Get_asks_for_the_context_snapshots_only_when_told_to()
    {
        using var cli = new CliRun();

        var exit = await cli.RunAsync("workitem", "get", "wi_A", "--snapshots");

        Assert.Equal(ExitCodes.Success, exit);
        cli.AssertPosted(Operations.WorkItemGet, "{\"work_item_id\":\"wi_A\",\"include_snapshots\":true}");
    }

    [Fact]
    public async Task List_collects_every_status_and_the_eligibility_filter()
    {
        using var cli = new CliRun();

        var exit = await cli.RunAsync(
            "workitem",
            "list",
            "--campaign",
            "cmp_A",
            "--contact",
            "cnt_A",
            "--status",
            "failed",
            "--status",
            "expired",
            "--kind",
            "ai_role",
            "--role",
            "researcher",
            "--eligible",
            "--limit",
            "5",
            "--cursor",
            "d2lfQQ");

        Assert.Equal(ExitCodes.Success, exit);
        cli.AssertPosted(
            Operations.WorkItemList,
            "{\"campaign_id\":\"cmp_A\",\"contact_id\":\"cnt_A\",\"status\":[\"failed\",\"expired\"],\"kind\":\"ai_role\",\"role\":\"researcher\","
                + "\"eligible\":true,\"limit\":5,\"cursor\":\"d2lfQQ\"}");
    }

    [Fact]
    public async Task List_can_ask_for_the_items_the_dispatcher_would_not_claim()
    {
        using var cli = new CliRun();

        var exit = await cli.RunAsync("workitem", "list", "--not-eligible");

        Assert.Equal(ExitCodes.Success, exit);
        cli.AssertPosted(Operations.WorkItemList, "{\"eligible\":false}");
    }

    [Fact]
    public async Task List_without_options_sends_an_empty_body()
    {
        using var cli = new CliRun();

        var exit = await cli.RunAsync("workitem", "list");

        Assert.Equal(ExitCodes.Success, exit);
        cli.AssertPosted(Operations.WorkItemList, "{}");
    }

    [Fact]
    public async Task Asking_for_both_eligibilities_is_a_usage_error()
    {
        using var cli = new CliRun();

        var exit = await cli.RunAsync("workitem", "list", "--eligible", "--not-eligible");

        Assert.Equal(ExitCodes.Usage, exit);
        Assert.Contains("--eligible", cli.Error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Update_patches_the_scalars_and_edits_the_context()
    {
        using var cli = new CliRun();

        var exit = await cli.RunAsync(
            "workitem",
            "update",
            "wi_A",
            "--set",
            "{\"tone\":\"direct\"}",
            "--unset",
            "old_pitch",
            "--not-before",
            "2026-09-14T12:00:00Z",
            "--priority",
            "7",
            "--timeout",
            "90",
            "--heartbeat",
            "15",
            "--max-attempts",
            "4",
            "--result-format",
            "{\"shape\":\"summary\"}",
            "--reason",
            "the plan changed",
            "--actor",
            "role:planner");

        Assert.Equal(ExitCodes.Success, exit);
        cli.AssertPosted(
            Operations.WorkItemUpdate,
            "{\"work_item_id\":\"wi_A\",\"set\":{\"tone\":\"direct\"},\"unset\":[\"old_pitch\"],\"not_before\":\"2026-09-14T12:00:00Z\",\"priority\":7,"
                + "\"timeout_seconds\":90,\"heartbeat_seconds\":15,\"max_attempts\":4,\"result_format\":{\"shape\":\"summary\"},"
                + "\"reason\":\"the plan changed\",\"actor\":{\"type\":\"role\",\"id\":\"planner\"}}");
    }

    [Fact]
    public async Task Update_clears_a_field_with_an_explicit_null()
    {
        using var cli = new CliRun();

        var exit = await cli.RunAsync("workitem", "update", "wi_A", "--clear", "due_at", "--clear", "max_attempts", "--set", "{\"a\":1}");

        Assert.Equal(ExitCodes.Success, exit);
        cli.AssertPosted(Operations.WorkItemUpdate, "{\"work_item_id\":\"wi_A\",\"set\":{\"a\":1},\"due_at\":null,\"max_attempts\":null}");
    }

    [Fact]
    public async Task Clearing_a_field_that_cannot_be_cleared_is_a_usage_error()
    {
        using var cli = new CliRun();

        var exit = await cli.RunAsync("workitem", "update", "wi_A", "--clear", "bogus");

        Assert.Equal(ExitCodes.Usage, exit);
        Assert.Contains("--clear", cli.Error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Cancel_sends_the_id_and_the_reason()
    {
        using var cli = new CliRun();

        var exit = await cli.RunAsync("workitem", "cancel", "wi_A", "--reason", "the campaign was archived");

        Assert.Equal(ExitCodes.Success, exit);
        cli.AssertPosted(Operations.WorkItemCancel, "{\"work_item_id\":\"wi_A\",\"reason\":\"the campaign was archived\"}");
    }

    [Fact]
    public async Task Heartbeat_carries_the_fencing_token()
    {
        using var cli = new CliRun();

        var exit = await cli.RunAsync("workitem", "heartbeat", "wi_A", "--attempt", "att_A");

        Assert.Equal(ExitCodes.Success, exit);
        cli.AssertPosted(Operations.WorkItemHeartbeat, "{\"work_item_id\":\"wi_A\",\"attempt_id\":\"att_A\"}");
    }

    [Fact]
    public async Task Heartbeat_without_an_attempt_is_a_usage_error()
    {
        using var cli = new CliRun();

        var exit = await cli.RunAsync("workitem", "heartbeat", "wi_A");

        Assert.Equal(ExitCodes.Usage, exit);
        Assert.Contains("--attempt", cli.Error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Set_result_sends_the_result_given_on_the_command_line()
    {
        using var cli = new CliRun();

        var exit = await cli.RunAsync("workitem", "set-result", "wi_A", "--attempt", "att_A", "--result", "{\"x\":1}");

        Assert.Equal(ExitCodes.Success, exit);
        cli.AssertPosted(Operations.WorkItemSetResult, "{\"work_item_id\":\"wi_A\",\"attempt_id\":\"att_A\",\"result\":{\"x\":1}}");
    }

    [Fact]
    public async Task Set_result_reads_a_result_file()
    {
        using var file = new TempFile("[1,2,3]");
        using var cli = new CliRun();

        var exit = await cli.RunAsync("workitem", "set-result", "wi_A", "--attempt", "att_A", "--result-file", file.Path);

        Assert.Equal(ExitCodes.Success, exit);
        cli.AssertPosted(Operations.WorkItemSetResult, "{\"work_item_id\":\"wi_A\",\"attempt_id\":\"att_A\",\"result\":[1,2,3]}");
    }

    [Fact]
    public async Task Set_result_reads_standard_input()
    {
        using var cli = new CliRun(standardInput: "{\"summary\":\"done\"}");

        var exit = await cli.RunAsync("workitem", "set-result", "wi_A", "--attempt", "att_A", "--result-file", "-");

        Assert.Equal(ExitCodes.Success, exit);
        cli.AssertPosted(Operations.WorkItemSetResult, "{\"work_item_id\":\"wi_A\",\"attempt_id\":\"att_A\",\"result\":{\"summary\":\"done\"}}");
    }

    [Fact]
    public async Task Set_result_with_both_sources_is_a_usage_error()
    {
        using var file = new TempFile("{}");
        using var cli = new CliRun();

        var exit = await cli.RunAsync("workitem", "set-result", "wi_A", "--attempt", "att_A", "--result", "{}", "--result-file", file.Path);

        Assert.Equal(ExitCodes.Usage, exit);
        Assert.Contains("--result-file", cli.Error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Set_result_without_a_result_is_a_usage_error()
    {
        using var cli = new CliRun();

        var exit = await cli.RunAsync("workitem", "set-result", "wi_A", "--attempt", "att_A");

        Assert.Equal(ExitCodes.Usage, exit);
        Assert.Contains("--result", cli.Error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Complete_sends_the_outcome_and_the_result()
    {
        using var cli = new CliRun();

        var exit = await cli.RunAsync(
            "workitem",
            "complete",
            "wi_A",
            "--attempt",
            "att_A",
            "--status",
            "succeeded",
            "--result",
            "{\"done\":true}",
            "--reason",
            "the brief was answered");

        Assert.Equal(ExitCodes.Success, exit);
        cli.AssertPosted(
            Operations.WorkItemComplete,
            "{\"work_item_id\":\"wi_A\",\"attempt_id\":\"att_A\",\"status\":\"succeeded\",\"result\":{\"done\":true},\"reason\":\"the brief was answered\"}");
    }

    [Fact]
    public async Task Complete_sends_the_error_of_a_failed_attempt()
    {
        using var cli = new CliRun();

        var exit = await cli.RunAsync(
            "workitem",
            "complete",
            "wi_A",
            "--attempt",
            "att_A",
            "--status",
            "failed",
            "--error",
            "{\"code\":\"x\",\"message\":\"y\"}");

        Assert.Equal(ExitCodes.Success, exit);
        cli.AssertPosted(
            Operations.WorkItemComplete,
            "{\"work_item_id\":\"wi_A\",\"attempt_id\":\"att_A\",\"status\":\"failed\",\"error\":{\"code\":\"x\",\"message\":\"y\"}}");
    }

    [Fact]
    public async Task An_api_error_is_printed_verbatim_and_exits_1()
    {
        const string envelope = "{\"error\":{\"code\":\"stale_attempt\",\"message\":\"That attempt is no longer current.\",\"retryable\":false}}";
        using var cli = new CliRun(envelope, status: HttpStatusCode.Conflict);

        var exit = await cli.RunAsync("workitem", "heartbeat", "wi_A", "--attempt", "att_A");

        Assert.Equal(ExitCodes.ApiError, exit);
        Assert.Contains("stale_attempt", cli.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Without_a_descriptor_the_verb_exits_3()
    {
        using var cli = new CliRun(descriptor: false);

        var exit = await cli.RunAsync("workitem", "get", "wi_A");

        Assert.Equal(ExitCodes.RuntimeUnavailable, exit);
        Assert.Contains("\"code\":\"no_descriptor\"", cli.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Human_mode_renders_one_work_item()
    {
        using var cli = new CliRun(Serialize(Item(null)));

        var exit = await cli.RunAsync("workitem", "get", "wi_A", "--human");

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Equal(
            """
            Id:           wi_A
            Campaign:     cmp_A
            Contact:      cnt_A
            Kind:         ai_role (role researcher)
            Status:       created (eligible)
            Priority:     5
            Not before:   -
            Due:          2026-09-14 12:00:00 UTC
            Retry after:  -
            Attempts:     1, current att_A
            Last error:   executor_exited — the process exited with code 1
            Created:      2026-09-14 10:00:00 UTC
            Updated:      2026-09-14 10:00:00 UTC
            Finished:     -
            Context keys: icp, tone
            Result:       none
            """.ReplaceLineEndings() + Environment.NewLine,
            cli.Text);
    }

    [Fact]
    public async Task Human_mode_adds_the_attempts_when_the_response_carries_them()
    {
        var attempt = new AttemptDto(
            "att_A",
            1,
            AttemptStatus.Running,
            WorkItemKind.AiRole,
            null,
            null,
            null,
            Moment.AddMinutes(1),
            Moment.AddMinutes(1),
            null,
            null,
            Moment.AddHours(1),
            null);
        using var cli = new CliRun(Serialize(Item([attempt])));

        var exit = await cli.RunAsync("workitem", "get", "wi_A", "--snapshots", "--human");

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Contains("#  ID     STATUS   CLAIMED", cli.Text, StringComparison.Ordinal);
        Assert.Contains("1  att_A  running  2026-09-14 10:01:00 UTC", cli.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("{", cli.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Human_mode_renders_a_work_item_listing_with_its_cursor()
    {
        var page = Serialize(new Page<WorkItemSummaryDto>(
            [
                new WorkItemSummaryDto(
                    "wi_A",
                    "cmp_A",
                    null,
                    WorkItemKind.AiRole,
                    "researcher",
                    null,
                    null,
                    WorkItemStatus.Created,
                    true,
                    5,
                    null,
                    null,
                    null,
                    new ActorRef(ActorType.Human, null),
                    0,
                    null,
                    null,
                    Moment,
                    Moment,
                    null),
            ],
            "d2lfQQ"));
        using var cli = new CliRun(page);

        var exit = await cli.RunAsync("workitem", "list", "--human");

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Contains("ID    CAMPAIGN  KIND     ROLE/OP     STATUS   ELIGIBLE  PRIO  ATTEMPTS  CREATED", cli.Text, StringComparison.Ordinal);
        Assert.Contains("wi_A  cmp_A     ai_role  researcher  created  yes       5     0         2026-09-14 10:00:00 UTC", cli.Text, StringComparison.Ordinal);
        Assert.Contains("next cursor: d2lfQQ", cli.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("{", cli.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Human_mode_renders_the_heartbeat_answer_as_one_line()
    {
        var response = Serialize(new HeartbeatResponse("wi_A", "att_A", Moment.AddMinutes(5), Moment.AddMinutes(2)));
        using var cli = new CliRun(response);

        var exit = await cli.RunAsync("workitem", "heartbeat", "wi_A", "--attempt", "att_A", "--human");

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Equal(
            "attempt att_A alive; lease until 2026-09-14 10:05:00 UTC; next heartbeat due by 2026-09-14 10:02:00 UTC" + Environment.NewLine,
            cli.Text);
    }

    [Fact]
    public async Task Human_mode_falls_back_to_the_raw_body_when_the_shape_is_unknown()
    {
        using var cli = new CliRun("{\"unexpected\":true}");

        var exit = await cli.RunAsync("workitem", "get", "wi_A", "--human");

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Contains("{\"unexpected\":true}", cli.Text, StringComparison.Ordinal);
    }

    private static WorkItemDto Item(IReadOnlyList<AttemptDto>? attempts) => new(
        "wi_A",
        "cmp_A",
        "cnt_A",
        WorkItemKind.AiRole,
        "researcher",
        null,
        null,
        WorkItemStatus.Created,
        true,
        5,
        null,
        Moment.AddHours(2),
        null,
        null,
        null,
        null,
        new ActorRef(ActorType.Role, "planner"),
        new JsonObject { ["icp"] = "founders", ["tone"] = "plain" },
        null,
        null,
        1,
        "att_A",
        new AttemptErrorDto("executor_exited", "the process exited with code 1", true),
        attempts,
        Moment,
        Moment,
        null);

    private static string Serialize<T>(T value) => JsonSerializer.Serialize(value, JasonJson.Options);
}
