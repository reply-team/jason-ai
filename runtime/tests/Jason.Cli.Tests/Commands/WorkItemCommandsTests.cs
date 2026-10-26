using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using Jason.Cli;
using Jason.Contracts.Api;
using Jason.Contracts.Json;
using Jason.Contracts.Plugins;
using Jason.Runtime.Tests;

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
            "{\"type\":\"object\"}",
            "--reason",
            "the plan asks for it",
            "--actor",
            "role:planner");

        Assert.Equal(ExitCodes.Success, exit);
        cli.AssertPosted(
            Operations.WorkItemCreate,
            "{\"campaign_id\":\"cmp_A\",\"kind\":\"ai_role\",\"role\":\"researcher\",\"contact_id\":\"cnt_A\",\"execution_profile\":\"fast\",\"priority\":5,"
                + "\"not_before\":\"2026-09-14T10:00:00Z\",\"due_at\":\"2026-09-15T10:00:00Z\",\"timeout_seconds\":60,\"heartbeat_seconds\":10,\"max_attempts\":2,"
                + "\"context\":{\"icp\":\"founders\"},\"result_format\":{\"type\":\"object\"},\"reason\":\"the plan asks for it\",\"actor\":{\"type\":\"role\",\"id\":\"planner\"}}");
    }

    /// <summary>
    /// Two namespaces use the same kind of words: the canonical operations a provider performs, and the verbs a
    /// person types at Jason. The help for <c>--operation</c> says which one it takes, so `campaign.get` is never
    /// mistaken for `jason campaign get`.
    /// </summary>
    [Fact]
    public async Task The_operation_option_says_it_takes_a_canonical_operation_and_not_a_jason_verb()
    {
        using var dir = new TempPaths();
        var output = new StringWriter();

        var exit = await CliApp.RunAsync(
            ["workitem", "create", "--help"],
            new CliEnvironment(output, new StringWriter(), dir.Paths),
            TestContext.Current.CancellationToken);

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Contains(
            "The canonical operation to perform, for a provider_op item: one of those under docs/contracts/operations, not a Jason verb.",
            string.Join(' ', output.ToString().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Create_carries_an_operation_for_a_provider_item()
    {
        using var cli = new CliRun();

        var exit = await cli.RunAsync("workitem", "create", "cmp_A", "--kind", "provider_op", "--operation", "campaign.get");

        Assert.Equal(ExitCodes.Success, exit);
        cli.AssertPosted(Operations.WorkItemCreate, "{\"campaign_id\":\"cmp_A\",\"kind\":\"provider_op\",\"operation\":\"campaign.get\"}");
    }

    [Fact]
    [Critical(Family.R3, "context.input")]
    public async Task Create_puts_the_operation_arguments_under_the_context_key_that_carries_them()
    {
        using var cli = new CliRun();

        var exit = await cli.RunAsync(
            "workitem",
            "create",
            "cmp_A",
            "--kind",
            "provider_op",
            "--operation",
            "list_membership.add",
            "--input",
            "{\"list\":{\"external_id\":\"lst_7\"},\"channel\":\"email\"}");

        Assert.Equal(ExitCodes.Success, exit);
        cli.AssertPosted(
            Operations.WorkItemCreate,
            "{\"campaign_id\":\"cmp_A\",\"kind\":\"provider_op\",\"operation\":\"list_membership.add\","
                + "\"context\":{\"input\":{\"list\":{\"external_id\":\"lst_7\"},\"channel\":\"email\"}}}");
    }

    [Fact]
    public async Task Create_lays_the_input_into_a_context_given_beside_it()
    {
        using var cli = new CliRun();

        var exit = await cli.RunAsync(
            "workitem",
            "create",
            "cmp_A",
            "--kind",
            "provider_op",
            "--operation",
            "campaign.get",
            "--context",
            "{\"brief\":\"the weekly read\"}",
            "--input",
            "{\"campaign\":{\"external_id\":\"seq_3\"}}");

        Assert.Equal(ExitCodes.Success, exit);
        cli.AssertPosted(
            Operations.WorkItemCreate,
            "{\"campaign_id\":\"cmp_A\",\"kind\":\"provider_op\",\"operation\":\"campaign.get\","
                + "\"context\":{\"brief\":\"the weekly read\",\"input\":{\"campaign\":{\"external_id\":\"seq_3\"}}}}");
    }

    [Fact]
    public async Task Create_lays_the_input_into_a_context_that_came_from_a_file()
    {
        using var file = new TempFile("{\"kind\":\"provider_op\",\"operation\":\"campaign.get\",\"context\":{\"brief\":\"from the file\"}}");
        using var cli = new CliRun();

        var exit = await cli.RunAsync("workitem", "create", "cmp_A", "--file", file.Path, "--input", "{}");

        Assert.Equal(ExitCodes.Success, exit);
        cli.AssertPosted(
            Operations.WorkItemCreate,
            "{\"kind\":\"provider_op\",\"operation\":\"campaign.get\",\"context\":{\"brief\":\"from the file\",\"input\":{}},\"campaign_id\":\"cmp_A\"}");
    }

    /// <summary>
    /// Where a shell will not pass JSON on the command line as written, the wiring check puts the arguments in a file as
    /// <c>{"context":{"input":…}}</c> and gives it with <c>--file</c>. That has to be the same request as <c>--input</c>,
    /// or the page would be offering a second, different act.
    /// </summary>
    [Fact]
    public async Task An_input_in_a_file_body_is_the_same_request_as_the_input_option()
    {
        const string Arguments = "{\"campaign\":{\"external_id\":\"7\"}}";
        using var file = new TempFile("{\"context\":{\"input\":" + Arguments + "}}");
        using var byOption = new CliRun();
        using var byFile = new CliRun();

        string[] common = ["workitem", "create", "cmp_A", "--kind", "provider_op", "--operation", "campaign.get"];
        Assert.Equal(ExitCodes.Success, await byOption.RunAsync([.. common, "--input", Arguments, "--idempotency-key", "wiring-check-read"]));
        Assert.Equal(ExitCodes.Success, await byFile.RunAsync([.. common, "--file", file.Path, "--idempotency-key", "wiring-check-read"]));

        const string Expected =
            "{\"context\":{\"input\":{\"campaign\":{\"external_id\":\"7\"}}},\"campaign_id\":\"cmp_A\",\"kind\":\"provider_op\",\"operation\":\"campaign.get\",\"idempotency_key\":\"wiring-check-read\"}";
        byFile.AssertPosted(Operations.WorkItemCreate, Expected);
        byOption.AssertPosted(Operations.WorkItemCreate, Expected);
    }

    [Fact]
    public async Task An_input_the_context_already_carries_is_a_usage_error()
    {
        using var cli = new CliRun();

        var exit = await cli.RunAsync(
            "workitem",
            "create",
            "cmp_A",
            "--kind",
            "provider_op",
            "--operation",
            "campaign.get",
            "--context",
            "{\"input\":{}}",
            "--input",
            "{}");

        Assert.Equal(ExitCodes.Usage, exit);
        Assert.Contains("--input", cli.Error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_input_beside_a_file_body_whose_context_is_not_an_object_is_a_usage_error()
    {
        using var file = new TempFile("{\"kind\":\"provider_op\",\"operation\":\"campaign.get\",\"context\":[1,2]}");
        using var cli = new CliRun();

        // The arguments have nowhere to be written, and the CLI says so rather than throwing over the body.
        var exit = await cli.RunAsync("workitem", "create", "cmp_A", "--file", file.Path, "--input", "{}");

        Assert.Equal(ExitCodes.Usage, exit);
        Assert.Contains("--input", cli.Error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_input_that_is_not_an_object_is_a_usage_error()
    {
        using var cli = new CliRun();

        var exit = await cli.RunAsync("workitem", "create", "cmp_A", "--kind", "provider_op", "--operation", "campaign.get", "--input", "[1]");

        Assert.Equal(ExitCodes.Usage, exit);
        Assert.Contains("--input", cli.Error.ToString(), StringComparison.Ordinal);
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
    public async Task List_collects_every_filter()
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
            "--since",
            "2026-10-16T09:00:00Z",
            "--until",
            "2026-10-17T00:00:00Z",
            "--order",
            "due",
            "--limit",
            "5",
            "--cursor",
            "d2lfQQ");

        Assert.Equal(ExitCodes.Success, exit);
        cli.AssertPosted(
            Operations.WorkItemList,
            "{\"campaign_id\":\"cmp_A\",\"contact_id\":\"cnt_A\",\"status\":[\"failed\",\"expired\"],\"kind\":\"ai_role\",\"role\":\"researcher\","
                + "\"eligible\":true,\"since\":\"2026-10-16T09:00:00Z\",\"until\":\"2026-10-17T00:00:00Z\",\"order\":\"due\",\"limit\":5,\"cursor\":\"d2lfQQ\"}");
    }

    /// <summary>
    /// The count takes every filter the listing takes and no page; <c>now</c> is this program's clock, sent as a moment,
    /// because the runtime takes moments and nothing else; and for a person it is the number alone.
    /// </summary>
    [Fact]
    public async Task Count_sends_the_lists_filters_reads_now_from_the_clock_and_prints_the_number()
    {
        using var cli = new CliRun("{\"total\":37}");
        var before = DateTimeOffset.UtcNow;

        var exit = await cli.RunAsync("workitem", "count", "--campaign", "cmp_A", "--status", "failed", "--order", "finished", "--since", "now", "--human");

        var after = DateTimeOffset.UtcNow;
        Assert.Equal(ExitCodes.Success, exit);
        Assert.Equal("http://127.0.0.1:5000/v1/" + Operations.WorkItemCount, cli.Url);
        var sent = JsonNode.Parse(cli.Sent!)!.AsObject();
        Assert.Equal(["campaign_id", "order", "since", "status"], sent.Select(p => p.Key).Order(StringComparer.Ordinal));
        var since = (string)sent["since"]!;
        Assert.EndsWith("Z", since, StringComparison.Ordinal);
        var moment = DateTimeOffset.Parse(since, System.Globalization.CultureInfo.InvariantCulture);
        Assert.InRange(moment, before.AddMilliseconds(-1), after);
        Assert.Equal("37", cli.Text.Trim());
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
    public async Task List_and_count_ask_for_the_items_a_block_holds_by_its_id()
    {
        using var list = new CliRun();
        Assert.Equal(ExitCodes.Success, await list.RunAsync("workitem", "list", "--block", "blk_7"));
        list.AssertPosted(Operations.WorkItemList, "{\"provider_block_id\":\"blk_7\"}");

        using var count = new CliRun("{\"total\":2}");
        Assert.Equal(ExitCodes.Success, await count.RunAsync("workitem", "count", "--block", "blk_7"));
        count.AssertPosted(Operations.WorkItemCount, "{\"provider_block_id\":\"blk_7\"}");
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
            "{\"type\":\"object\"}",
            "--reason",
            "the plan changed",
            "--actor",
            "role:planner");

        Assert.Equal(ExitCodes.Success, exit);
        cli.AssertPosted(
            Operations.WorkItemUpdate,
            "{\"work_item_id\":\"wi_A\",\"set\":{\"tone\":\"direct\"},\"unset\":[\"old_pitch\"],\"not_before\":\"2026-09-14T12:00:00Z\",\"priority\":7,"
                + "\"timeout_seconds\":90,\"heartbeat_seconds\":15,\"max_attempts\":4,\"result_format\":{\"type\":\"object\"},"
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
    public async Task Update_names_a_new_execution_profile()
    {
        using var cli = new CliRun();

        var exit = await cli.RunAsync("workitem", "update", "wi_A", "--execution-profile", "fast-claude");

        Assert.Equal(ExitCodes.Success, exit);
        cli.AssertPosted(Operations.WorkItemUpdate, "{\"work_item_id\":\"wi_A\",\"execution_profile\":\"fast-claude\"}");
    }

    /// <summary>Giving it back is the same <c>--clear</c> every other nullable field already uses.</summary>
    [Fact]
    public async Task Update_gives_the_execution_profile_back_through_the_clear_option()
    {
        using var cli = new CliRun();

        var exit = await cli.RunAsync("workitem", "update", "wi_A", "--clear", "execution_profile");

        Assert.Equal(ExitCodes.Success, exit);
        cli.AssertPosted(Operations.WorkItemUpdate, "{\"work_item_id\":\"wi_A\",\"execution_profile\":null}");
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
    [Critical(Family.W2, "the attempt id travels as the fence")]
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
    [Critical(Family.W2, "a wait carries the fence and what it waits for")]
    public async Task Wait_sends_the_fence_the_items_the_release_and_the_moment()
    {
        using var cli = new CliRun();

        var exit = await cli.RunAsync(
            "workitem", "wait", "wi_A", "--attempt", "att_A", "--on", "wi_B", "--on", "wi_C", "--any", "--until", "2026-09-17T10:00:00Z", "--reason", "the research first");

        Assert.Equal(ExitCodes.Success, exit);
        cli.AssertPosted(
            Operations.WorkItemWait,
            "{\"work_item_id\":\"wi_A\",\"attempt_id\":\"att_A\",\"on\":[\"wi_B\",\"wi_C\"],\"mode\":\"any\",\"until\":\"2026-09-17T10:00:00Z\",\"reason\":\"the research first\"}");
    }

    [Fact]
    public async Task Wait_for_a_moment_alone_names_no_items_and_leaves_the_release_to_the_runtime()
    {
        using var cli = new CliRun();

        var exit = await cli.RunAsync("workitem", "wait", "wi_A", "--attempt", "att_A", "--until", "2026-09-17T10:00:00Z");

        Assert.Equal(ExitCodes.Success, exit);
        cli.AssertPosted(Operations.WorkItemWait, "{\"work_item_id\":\"wi_A\",\"attempt_id\":\"att_A\",\"until\":\"2026-09-17T10:00:00Z\"}");
    }

    [Fact]
    public async Task Wait_says_all_when_told_to()
    {
        using var cli = new CliRun();

        var exit = await cli.RunAsync("workitem", "wait", "wi_A", "--attempt", "att_A", "--on", "wi_B", "--all");

        Assert.Equal(ExitCodes.Success, exit);
        cli.AssertPosted(Operations.WorkItemWait, "{\"work_item_id\":\"wi_A\",\"attempt_id\":\"att_A\",\"on\":[\"wi_B\"],\"mode\":\"all\"}");
    }

    [Fact]
    public async Task Wait_for_all_and_any_at_once_is_a_usage_error()
    {
        using var cli = new CliRun();

        var exit = await cli.RunAsync("workitem", "wait", "wi_A", "--attempt", "att_A", "--on", "wi_B", "--all", "--any");

        Assert.Equal(ExitCodes.Usage, exit);
        Assert.Contains("--all and --any", cli.Error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Wait_without_an_attempt_is_a_usage_error()
    {
        using var cli = new CliRun();

        var exit = await cli.RunAsync("workitem", "wait", "wi_A", "--on", "wi_B");

        Assert.Equal(ExitCodes.Usage, exit);
        Assert.Contains("--attempt", cli.Error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Resume_sends_the_id_the_reason_and_who_resumed_it()
    {
        using var cli = new CliRun();

        var exit = await cli.RunAsync("--actor", "human:marta@example.com", "workitem", "resume", "wi_A", "--reason", "go on without the last one");

        Assert.Equal(ExitCodes.Success, exit);
        cli.AssertPosted(
            Operations.WorkItemResume,
            "{\"work_item_id\":\"wi_A\",\"reason\":\"go on without the last one\",\"actor\":{\"type\":\"human\",\"id\":\"marta@example.com\"}}");
    }

    [Fact]
    public async Task Resume_without_an_actor_is_a_usage_error()
    {
        using var cli = new CliRun();

        var exit = await cli.RunAsync("workitem", "resume", "wi_A", "--reason", "go on");

        Assert.Equal(ExitCodes.Usage, exit);
        Assert.Contains("--actor", cli.Error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Human_mode_says_what_a_waiting_item_waits_for_and_until_when()
    {
        var wait = new WaitDto("att_A", [new WaitItemDto("wi_B", WorkItemStatus.Succeeded), new WaitItemDto("wi_C", WorkItemStatus.Processing)], WaitMode.All, Moment.AddDays(3), Moment.AddDays(3), Moment);
        using var cli = new CliRun(Serialize(Item(null) with { Status = WorkItemStatus.Waiting, Wait = wait }));

        var exit = await cli.RunAsync("workitem", "get", "wi_A", "--human");

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Contains(
            """
            Last error:   executor_exited — the process exited with code 1
            Waiting for:  1 of 2 ended, all; until 2026-09-17 10:00:00 UTC
            Wait opened:  2026-09-14 10:00:00 UTC
            Wait ends by: 2026-09-17 10:00:00 UTC
            """.ReplaceLineEndings(),
            cli.Text,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Human_mode_says_in_words_which_block_a_parked_item_waits_on()
    {
        var wait = new WaitDto(null, [], null, null, null, Moment, new WaitBlockedByDto("blk_A", ProviderBlockScope.Account, "billing"));
        using var cli = new CliRun(Serialize(Item(null) with { Status = WorkItemStatus.Waiting, Wait = wait }));

        var exit = await cli.RunAsync("workitem", "get", "wi_A", "--human");

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Contains(
            "Waiting for:  the provider blocked its account (billing); it runs when the block clears or a trial succeeds",
            cli.Text,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Human_mode_lists_a_parked_item_with_the_block_it_waits_on()
    {
        var parked = new WorkItemSummaryDto(
            "wi_A", "cmp_A", null, WorkItemKind.ProviderOp, null, "message.send", null, WorkItemStatus.Waiting, false, 5, null, null, null,
            new ActorRef(ActorType.Human, null), 0, null, null, Moment, Moment, null, "Autumn outreach", null)
        {
            Wait = new WaitSummaryDto(null, null, null, null, Moment, OnCount: 0, EndedCount: 0, new WaitBlockedByDto("blk_A", ProviderBlockScope.Sender, "limit")),
        };
        using var cli = new CliRun(Serialize(new Page<WorkItemSummaryDto>([parked], null)));

        var exit = await cli.RunAsync("workitem", "list", "--human");

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Contains("waiting (the provider blocked its sending account, limit)", cli.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Human_mode_prints_no_wait_lines_for_an_item_that_is_not_waiting()
    {
        using var cli = new CliRun(Serialize(Item(null)));

        var exit = await cli.RunAsync("workitem", "get", "wi_A", "--human");

        Assert.Equal(ExitCodes.Success, exit);
        Assert.DoesNotContain("Wait", cli.Text, StringComparison.Ordinal);
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
            Lineage:      root
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
            Input:        -
            Result:       none
            """.ReplaceLineEndings() + Environment.NewLine,
            cli.Text);
    }

    /// <summary>
    /// A provider block is what a person has to clear at the provider, so the page says what is blocked, why, where
    /// to go and when it may clear, under the error it came with; an item whose error carries no block has no such line.
    /// </summary>
    [Theory]
    [InlineData(
        """{"scope":"account","reason":"billing","link":"https://app.example.com/billing"}""",
        "Blocked:      account · billing · https://app.example.com/billing")]
    [InlineData(
        """{"scope":"sender","sender_external_id":"mbx_1042","reason":"limit","link":"https://app.example.com/mailboxes/1042","clears_at":"2026-10-25T00:00:00Z"}""",
        "Blocked:      sender mbx_1042 · limit · https://app.example.com/mailboxes/1042 · clears 2026-10-25 00:00:00 UTC")]
    [InlineData(
        """{"scope":"account","reason":"other"}""",
        "Blocked:      account · other")]
    public async Task Human_mode_says_what_a_provider_block_holds_under_the_last_error(string block, string expected)
    {
        var lastError = new AttemptErrorDto(
            "provider_blocked", "the provider refused the account", false, Class: FailureClass.Blocked, Block: JsonNode.Parse(block)!.AsObject());
        using var cli = new CliRun(Serialize(Item(null, lastError: lastError)));

        var exit = await cli.RunAsync("workitem", "get", "wi_A", "--human");

        Assert.Equal(ExitCodes.Success, exit);
        var lines = cli.Text.ReplaceLineEndings().Split(Environment.NewLine);
        var at = Array.FindIndex(lines, line => line.StartsWith("Last error:", StringComparison.Ordinal));
        Assert.True(at >= 0 && at + 1 < lines.Length, "no last error line in: " + cli.Text);
        Assert.Equal("Last error:   provider_blocked — the provider refused the account", lines[at]);
        Assert.Equal(expected, lines[at + 1]);
    }

    [Fact]
    public async Task Human_mode_prints_no_block_line_for_an_error_without_one()
    {
        using var cli = new CliRun(Serialize(Item(null)));

        var exit = await cli.RunAsync("workitem", "get", "wi_A", "--human");

        Assert.Equal(ExitCodes.Success, exit);
        Assert.DoesNotContain("Blocked:", cli.Text, StringComparison.Ordinal);
    }

    /// <summary>
    /// Where this item's executor comes from, on the page a person reads before asking why work is stuck. The
    /// inherited line names the profile, the revision it was pinned at, and the attempt it came from, so the
    /// chain can be walked back without a database.
    /// </summary>
    [Fact]
    public async Task Human_mode_says_what_an_item_inherited_and_where_from()
    {
        using var cli = new CliRun(Serialize(Item(null, lineage: new LineageDto(LineageState.Inherited, "fast-claude", 3, "att_B"))));

        var exit = await cli.RunAsync("workitem", "get", "wi_A", "--human");

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Contains("Lineage:      inherited · profile fast-claude r3 · from att_B", cli.Text, StringComparison.Ordinal);
    }

    /// <summary>The one state that will not run: it says so on the item rather than only in the error afterwards.</summary>
    [Fact]
    public async Task Human_mode_shows_an_unresolved_ancestry_as_the_state_it_is()
    {
        using var cli = new CliRun(Serialize(Item(null, lineage: new LineageDto(LineageState.Unresolved))));

        var exit = await cli.RunAsync("workitem", "get", "wi_A", "--human");

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Contains("Lineage:      unresolved", cli.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Human_mode_names_the_arguments_a_provider_operation_was_given()
    {
        var item = Item(null) with
        {
            Kind = WorkItemKind.ProviderOp,
            Role = null,
            Operation = "list_membership.add",
            Context = new JsonObject
            {
                ["brief"] = "the weekly import",
                ["input"] = new JsonObject { ["list"] = new JsonObject { ["external_id"] = "lst_7" }, ["channel"] = "email" },
            },
        };
        using var cli = new CliRun(Serialize(item));

        var exit = await cli.RunAsync("workitem", "get", "wi_A", "--human");

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Contains("Kind:         provider_op (operation list_membership.add)", cli.Text, StringComparison.Ordinal);
        Assert.Contains("Context keys: brief, input", cli.Text, StringComparison.Ordinal);
        Assert.Contains("Input:        list, channel", cli.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("{", cli.Text, StringComparison.Ordinal);
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

    /// <summary>
    /// The two things a person must never confuse sit on the same page: what Jason tried, and what somebody
    /// says they did themselves. They are kept apart by a table of their own and by the sentence above it.
    /// </summary>
    [Fact]
    public async Task Work_item_human_view_lists_external_reports_under_the_attempts_and_prints_no_table_when_there_are_none()
    {
        var attempt = new AttemptDto(
            "att_A", 1, AttemptStatus.Succeeded, WorkItemKind.AiRole, null, null, null,
            Moment.AddMinutes(1), Moment.AddMinutes(1), null, null, Moment.AddHours(1), null);

        using var reported = new CliRun(Serialize(Item([attempt], [Report("rpt_A", "email_sent"), Report("rpt_B", "call_made")])));
        Assert.Equal(ExitCodes.Success, await reported.RunAsync("workitem", "get", "wi_A", "--human"));

        var text = reported.Text;
        Assert.Contains("Reported from outside Jason", text, StringComparison.Ordinal);
        Assert.Contains("rpt_A", text, StringComparison.Ordinal);
        Assert.Contains("rpt_B", text, StringComparison.Ordinal);
        Assert.Contains("email_sent", text, StringComparison.Ordinal);

        // Under the attempts, not among them: the attempt table is printed before the reported effects.
        Assert.True(
            text.IndexOf("att_A", StringComparison.Ordinal) < text.IndexOf("Reported from outside Jason", StringComparison.Ordinal),
            "Externally reported effects belong under the attempts.");

        using var quiet = new CliRun(Serialize(Item([attempt])));
        Assert.Equal(ExitCodes.Success, await quiet.RunAsync("workitem", "get", "wi_A", "--human"));

        Assert.DoesNotContain("Reported from outside Jason", quiet.Text, StringComparison.Ordinal);
        Assert.Contains("att_A", quiet.Text, StringComparison.Ordinal);
    }

    /// <summary>What ran, in the words a person reads: the package, the operation, the route and what it cost.</summary>
    [Fact]
    public async Task Human_mode_shows_what_ran_one_provider_attempt()
    {
        var provenance = new AttemptProvenanceDto(
            "fake-provider",
            "1.2.0",
            "sha256:9f2b7c4d1e6a8b3c5d7e9f0a1b2c3d4e5f60718293a4b5c6d7e8f9a0b1c2d3e4",
            1,
            1,
            "campaign.get",
            1,
            "snp_01K5B7Q2WE5X3M9T0YH4C6RDNA",
            "rts_01K5B7Q2WE5X3M9T0YH4C6RDNB",
            RouteScope.CampaignOperation,
            "sha256:aa11bb22cc33dd44ee55ff6677889900aabbccddeeff00112233445566778899",
            "pin_01K5B7Q2WE5X3M9T0YH4C6RDNC",
            "att_A",
            new OutcomeDiagnostics(412, 0, 2, 7));
        using var cli = new CliRun(Serialize(Item([Attempt(provenance)])));

        var exit = await cli.RunAsync("workitem", "get", "wi_A", "--human");

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Contains("ATTEMPT 1 (att_A) RAN", cli.Text, StringComparison.Ordinal);
        Assert.Contains("Plugin:       fake-provider 1.2.0 · 9f2b7c4d1e6a", cli.Text, StringComparison.Ordinal);
        Assert.Contains("Operation:    campaign.get v1", cli.Text, StringComparison.Ordinal);
        Assert.Contains("Route:        campaign_operation · binding aa11bb22cc33", cli.Text, StringComparison.Ordinal);
        Assert.Contains("Snapshots:    plugins snp_01K5B7Q2WE5X3M9T0YH4C6RDNA · routes rts_01K5B7Q2WE5X3M9T0YH4C6RDNB", cli.Text, StringComparison.Ordinal);
        Assert.Contains("Invocation:   pin_01K5B7Q2WE5X3M9T0YH4C6RDNC · 412 ms · exec 0 · http 2 · log 7", cli.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("{", cli.Text, StringComparison.Ordinal);
    }

    /// <summary>
    /// Whether the provider found or created what the plugin identified ends the invocation line, kind by kind, where the
    /// plugin said; where it said nothing, the line is what it always was.
    /// </summary>
    [Fact]
    public async Task Human_mode_says_on_the_invocation_line_whether_the_provider_found_or_created_the_contact()
    {
        var provenance = new AttemptProvenanceDto(
            "fake-provider", "1.2.0", null, 1, 1, "list_membership.add", 1, null, null, RouteScope.GlobalDefault, null,
            "pin_01K5B7Q2WE5X3M9T0YH4C6RDNC",
            "att_A",
            new OutcomeDiagnostics(412, 0, 2, 7),
            ExternalIdsReturned: new Dictionary<string, string>(StringComparer.Ordinal) { ["contact"] = "p_1001", ["campaign"] = "c_7" },
            ExternalIdOrigins: new Dictionary<string, string>(StringComparer.Ordinal) { ["contact"] = "created" });
        using var cli = new CliRun(Serialize(Item([Attempt(provenance)])));

        var exit = await cli.RunAsync("workitem", "get", "wi_A", "--human");

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Contains("Invocation:   pin_01K5B7Q2WE5X3M9T0YH4C6RDNC · 412 ms · exec 0 · http 2 · log 7 · contact created", cli.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("campaign found", cli.Text, StringComparison.Ordinal);
    }

    /// <summary>An attempt with no record of what ran prints what it always printed, and no empty block.</summary>
    [Fact]
    public async Task Human_mode_adds_nothing_for_an_attempt_that_ran_no_plugin()
    {
        using var cli = new CliRun(Serialize(Item([Attempt(null)])));

        var exit = await cli.RunAsync("workitem", "get", "wi_A", "--human");

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Contains("1  att_A  succeeded", cli.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("RAN", cli.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("Plugin:", cli.Text, StringComparison.Ordinal);
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
                    null,
                    "Autumn outreach",
                    "Ada Lovelace"),
            ],
            "d2lfQQ"));
        using var cli = new CliRun(page);

        var exit = await cli.RunAsync("workitem", "list", "--human");

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Contains("ID    CAMPAIGN         PERSON        KIND     ROLE/OP     STATUS   ELIGIBLE  PRIO  ATTEMPTS  CREATED", cli.Text, StringComparison.Ordinal);
        Assert.Contains("wi_A  Autumn outreach  Ada Lovelace  ai_role  researcher  created  yes       5     0         2026-09-14 10:00:00 UTC", cli.Text, StringComparison.Ordinal);
        Assert.Contains("next cursor: d2lfQQ", cli.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("{", cli.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Human_mode_lists_a_waiting_item_with_what_it_waits_for()
    {
        var waiting = new WorkItemSummaryDto(
            "wi_A", "cmp_A", null, WorkItemKind.AiRole, "planner", null, null, WorkItemStatus.Waiting, false, 5, null, null, null,
            new ActorRef(ActorType.Human, null), 1, null, null, Moment, Moment, null, "Autumn outreach", null)
        {
            Wait = new WaitSummaryDto("att_A", WaitMode.Any, null, Moment.AddDays(14), Moment, OnCount: 1, EndedCount: 1),
        };
        using var cli = new CliRun(Serialize(new Page<WorkItemSummaryDto>([waiting], null)));

        var exit = await cli.RunAsync("workitem", "list", "--human");

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Contains("waiting (1 of 1 ended, any)", cli.Text, StringComparison.Ordinal);
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

    private static WorkItemDto Item(
        IReadOnlyList<AttemptDto>? attempts,
        IReadOnlyList<ReportSummaryDto>? externalReports = null,
        LineageDto? lineage = null,
        AttemptErrorDto? lastError = null) => new(
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
        lastError ?? new AttemptErrorDto("executor_exited", "the process exited with code 1", true),
        attempts,
        externalReports,
        Moment,
        Moment,
        null,

        // The runtime answers with one for every item, so the default here is what a root item actually sends.
        lineage ?? new LineageDto(LineageState.Root));

    private static ReportSummaryDto Report(string id, string effect) => new(
        id,
        Moment,
        new ActorRef(ActorType.Human, "person-1"),
        effect,
        "some-other-cli",
        null,
        "Done by hand, outside the runtime.",
        new ReportCorrelationDto("cmp_A", "cnt_A", "wi_A", null, false, true, false));

    private static string Serialize<T>(T value) => JsonSerializer.Serialize(value, JasonJson.Options);

    private static AttemptDto Attempt(AttemptProvenanceDto? provenance) => new(
        "att_A",
        1,
        AttemptStatus.Succeeded,
        provenance is null ? WorkItemKind.AiRole : WorkItemKind.ProviderOp,
        null,
        null,
        null,
        Moment.AddMinutes(1),
        Moment.AddMinutes(1),
        Moment.AddMinutes(2),
        null,
        Moment.AddHours(1),
        null,
        provenance);
}
