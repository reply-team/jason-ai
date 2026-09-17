using Jason.Cli;
using Jason.Contracts.Api;

namespace Jason.Cli.Tests.Commands;

/// <summary>
/// The verbs a person decides through, from the side they are typed on: what is sent, and what is printed back
/// when a person asked to read rather than to parse.
/// </summary>
public class ApprovalCommandsTests
{
    private const string OneApproval = """
        {
          "id": "apr_01K52JR0000000000000000001",
          "work_item_id": "wi_01K52JR0000000000000000002",
          "campaign_id": "cmp_01K52JR0000000000000000003",
          "operation": "campaign.enroll",
          "operation_version": 1,
          "status": "pending",
          "reason": "approval_required",
          "subject_hash": "sha256:abc",
          "subject": {"operation": "campaign.enroll"},
          "preview": {
            "intent": "Put one person into a campaign at the provider.",
            "reach": {"value": "act", "conditional": true, "detail": "bookkeeping until the campaign is live"},
            "reversibility": {"value": "irreversible", "conditional": true, "detail": null},
            "cost": {"value": "metered", "conditional": true, "detail": null},
            "campaign": {"id": "cmp_01K52JR0000000000000000003", "name": "Autumn outreach"},
            "contact": {"id": "cnt_1", "name": "Ada", "channel": "email", "value": "ada@example.test"}
          },
          "plugin_id": "reply",
          "binding_identity": "sha256:account",
          "route_scope": "global_default",
          "plugin_snapshot_id": "snp_1",
          "routing_snapshot_id": "rts_1",
          "requested_at": "2026-09-14T10:00:00+00:00",
          "decided_at": null,
          "decided_by": null,
          "decision_reason": null
        }
        """;

    [Fact]
    public async Task List_asks_for_what_is_waiting_and_passes_the_filters_it_was_given()
    {
        using var cli = new CliRun();

        var exit = await cli.RunAsync("approval", "list", "--status", "rejected", "--work-item", "wi_7", "--limit", "5");

        Assert.Equal(ExitCodes.Success, exit);
        cli.AssertPosted(Operations.ApprovalList, "{\"status\":\"rejected\",\"work_item_id\":\"wi_7\",\"limit\":5}");
    }

    [Fact]
    public async Task List_without_options_sends_an_empty_body_and_lets_the_runtime_say_what_pending_means()
    {
        using var cli = new CliRun();

        var exit = await cli.RunAsync("approval", "list");

        Assert.Equal(ExitCodes.Success, exit);
        cli.AssertPosted(Operations.ApprovalList, "{}");
    }

    [Fact]
    public async Task Approve_sends_the_decision_with_the_person_who_made_it()
    {
        using var cli = new CliRun(OneApproval);

        var exit = await cli.RunAsync("--actor", "human:ada@example.test", "approval", "approve", "apr_7", "--reason", "go ahead");

        Assert.Equal(ExitCodes.Success, exit);
        cli.AssertPosted(
            Operations.ApprovalApprove,
            "{\"approval_id\":\"apr_7\",\"reason\":\"go ahead\",\"actor\":{\"type\":\"human\",\"id\":\"ada@example.test\"}}");
    }

    [Fact]
    public async Task Reject_sends_the_decision_with_the_person_who_made_it()
    {
        using var cli = new CliRun(OneApproval);

        var exit = await cli.RunAsync("--actor", "human:ada@example.test", "approval", "reject", "apr_7", "--reason", "not this quarter");

        Assert.Equal(ExitCodes.Success, exit);
        cli.AssertPosted(
            Operations.ApprovalReject,
            "{\"approval_id\":\"apr_7\",\"reason\":\"not this quarter\",\"actor\":{\"type\":\"human\",\"id\":\"ada@example.test\"}}");
    }

    /// <summary>
    /// What a person reads before deciding, in the order the decision is made in: the effect first, then who it
    /// reaches and through which account, and the identifiers last.
    /// </summary>
    [Fact]
    public async Task Get_renders_what_the_decision_would_do_before_anything_about_identifiers()
    {
        using var cli = new CliRun(OneApproval);

        var exit = await cli.RunAsync("approval", "get", "apr_1", "--human");

        Assert.Equal(ExitCodes.Success, exit);
        var text = cli.Text;
        Assert.Contains("Operation:     campaign.enroll", text, StringComparison.Ordinal);
        Assert.Contains("Intent:        Put one person into a campaign at the provider.", text, StringComparison.Ordinal);
        Assert.Contains("Reach:         act (conditional)", text, StringComparison.Ordinal);
        Assert.Contains("Undo:          irreversible (conditional)", text, StringComparison.Ordinal);
        Assert.Contains("Contact:       Ada (cnt_1) · email ada@example.test", text, StringComparison.Ordinal);
        Assert.Contains("Account:       sha256:account", text, StringComparison.Ordinal);
        Assert.True(text.IndexOf("Intent:", StringComparison.Ordinal) < text.IndexOf("Id:", StringComparison.Ordinal));
    }

    [Fact]
    public async Task List_renders_a_table_of_what_is_waiting()
    {
        using var cli = new CliRun($$"""{"items": [{{OneApproval}}], "next_cursor": null}""");

        var exit = await cli.RunAsync("approval", "list", "--human");

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Contains("REQUESTED", cli.Text, StringComparison.Ordinal);
        Assert.Contains("campaign.enroll", cli.Text, StringComparison.Ordinal);
        Assert.Contains("pending", cli.Text, StringComparison.Ordinal);
    }
}
