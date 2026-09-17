using System.Text.Json.Nodes;
using Jason.Cli;
using Jason.Contracts.Api;
using Jason.Runtime.Persistence;
using Jason.Runtime.Tests.Approvals;

namespace Jason.Runtime.Tests.Integration;

/// <summary>
/// The real CLI against the real runtime in one process: every verb composes its body, posts it over the
/// loopback API through the descriptor the runtime published, and prints what came back. What is asserted here
/// is what an agent driving Jason from a shell actually sees — the JSON on stdout and the exit code.
/// </summary>
public class CliAgainstRuntimeTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_campaign_is_created_described_filled_with_contacts_and_archived_through_the_cli()
    {
        await using var fixture = await RuntimeApiFixture.StartAsync(Ct);

        var created = await OkAsync(fixture, "campaign", "create", "--name", "LatAm", "--context", "{\"icp\":\"founders\"}");
        var campaign = (string)created["id"]!;
        Assert.StartsWith("cmp_", campaign, StringComparison.Ordinal);
        Assert.Equal("draft", (string?)created["status"]);
        Assert.Equal("founders", (string?)created["context"]!["icp"]);

        var described = await OkAsync(fixture, "--actor", "role:planner", "campaign", "update-context", campaign, "--set", "{\"tone\":\"direct\"}");
        Assert.Equal("direct", (string?)described["context"]!["tone"]);
        Assert.Equal("founders", (string?)described["context"]!["icp"]);

        // The chronicle carries both changes, and the second one remembers who asked for it.
        var entries = (await OkAsync(fixture, "journal", "list", "--campaign", campaign))["items"]!.AsArray();
        var creation = Single(entries, "campaign_created");
        Assert.Equal("name", (string?)creation["key"]);
        Assert.Equal("LatAm", (string?)creation["new"]);
        Assert.Equal("human", (string?)creation["actor"]!["type"]);
        var contextChange = Single(entries, "context_updated");
        Assert.Equal("tone", (string?)contextChange["key"]);
        Assert.Equal("direct", (string?)contextChange["new"]);
        Assert.Equal("role", (string?)contextChange["actor"]!["type"]);
        Assert.Equal("planner", (string?)contextChange["actor"]!["id"]);

        // A contact the import will have to recognize rather than duplicate. The address is stored normalized.
        var ana = await OkAsync(fixture, "contact", "create", "--email", "A@B.co", "--first-name", "Ana");
        Assert.Equal("a@b.co", (string?)ana["channels"]!.AsArray()[0]!["value"]);

        var file = Path.Combine(fixture.Paths.Root, "contacts.json");
        await File.WriteAllTextAsync(
            file,
            """
            [
              { "first_name": "Ana", "channels": [{ "channel": "email", "value": "a@b.co" }] },
              { "first_name": "Ben", "channels": [{ "channel": "email", "value": "ben@example.test" }] },
              { "first_name": "Cleo", "channels": [{ "channel": "email", "value": "cleo at example" }] }
            ]
            """,
            Ct);

        var batch = await OkAsync(fixture, "campaign", "add-contacts", campaign, "--file", file, "--match-by", "email");
        Assert.Equal(2, (int)batch["summary"]!["added"]!);
        Assert.Equal(0, (int)batch["summary"]!["already_member"]!);
        Assert.Equal(1, (int)batch["summary"]!["rejected"]!);
        var items = batch["items"]!.AsArray();
        Assert.Equal(3, items.Count);
        Assert.Equal("added", (string?)items[0]!["status"]);
        Assert.Equal((string?)ana["id"], (string?)items[0]!["contact_id"]);
        Assert.False((bool)items[0]!["contact_created"]!);
        Assert.Equal("added", (string?)items[1]!["status"]);
        Assert.True((bool)items[1]!["contact_created"]!);
        Assert.Equal("rejected", (string?)items[2]!["status"]);
        Assert.Equal("validation_failed", (string?)items[2]!["error"]!["code"]);
        Assert.Equal("contacts[2].channels[0].value", (string?)items[2]!["error"]!["details"]!.AsArray()[0]!["field"]);

        var members = await OkAsync(fixture, "campaign", "list-contacts", campaign);
        Assert.Equal(2, members["items"]!.AsArray().Count);

        // The lifecycle. Asking twice for the state a campaign is already in is a retry, not an error.
        Assert.Equal("active", (string?)(await OkAsync(fixture, "campaign", "start", campaign))["status"]);
        Assert.Equal("paused", (string?)(await OkAsync(fixture, "campaign", "pause", campaign))["status"]);
        Assert.Equal("paused", (string?)(await OkAsync(fixture, "campaign", "pause", campaign))["status"]);
        Assert.Equal("archived", (string?)(await OkAsync(fixture, "campaign", "archive", campaign, "--reason", "the quarter is over"))["status"]);

        var refused = await ErrorAsync(fixture, "campaign", "start", campaign);
        Assert.Equal("campaign_archived", (string?)refused["error"]!["code"]);

        // An archived campaign leaves the working list and is found only when asked for by status.
        var open = await OkAsync(fixture, "campaign", "list");
        Assert.DoesNotContain(open["items"]!.AsArray(), item => (string?)item!["id"] == campaign);
        var archived = await OkAsync(fixture, "campaign", "list", "--status", "archived");
        Assert.Contains(archived["items"]!.AsArray(), item => (string?)item!["id"] == campaign);

        // Listing by channel normalizes the value the same way storing it did.
        var found = await OkAsync(fixture, "contact", "list", "--channel", "email", "--value", "A@B.CO");
        Assert.Equal((string?)ana["id"], (string?)Assert.Single(found["items"]!.AsArray())!["id"]);
    }

    [Fact]
    public async Task Suppressing_a_value_and_lifting_it_again_go_through_the_cli()
    {
        await using var fixture = await RuntimeApiFixture.StartAsync(Ct);

        var added = await OkAsync(fixture, "suppression", "add", "--channel", "email", "--value", "A@B.co");
        Assert.StartsWith("sup_", (string)added["id"]!, StringComparison.Ordinal);
        Assert.Equal("a@b.co", (string?)added["value"]);

        var listed = await OkAsync(fixture, "suppression", "list", "--channel", "email");
        Assert.Equal("a@b.co", (string?)Assert.Single(listed["items"]!.AsArray())!["value"]);

        var removed = await OkAsync(fixture, "suppression", "remove", "--channel", "email", "--value", "a@b.co", "--reason", "typo");
        Assert.True((bool)removed["removed"]!);

        var again = await OkAsync(fixture, "suppression", "remove", "--channel", "email", "--value", "a@b.co", "--reason", "typo");
        Assert.False((bool)again["removed"]!);
        Assert.Empty((await OkAsync(fixture, "suppression", "list"))["items"]!.AsArray());
    }

    [Fact]
    public async Task An_api_error_is_printed_verbatim_with_exit_code_one()
    {
        await using var fixture = await RuntimeApiFixture.StartAsync(Ct);

        var blank = await ErrorAsync(fixture, "campaign", "get", " ");
        Assert.Equal("validation_failed", (string?)blank["error"]!["code"]);
        Assert.Equal("campaign_id", (string?)blank["error"]!["details"]!.AsArray()[0]!["field"]);

        var unknown = await ErrorAsync(fixture, "campaign", "get", "cmp_nothing");
        Assert.Equal("campaign_not_found", (string?)unknown["error"]!["code"]);
    }

    [Fact]
    public async Task Roles_and_work_items_are_driven_from_the_command_line()
    {
        await using var host = await FakeHostRuntime.StartAsync(Ct);
        var fixture = host.Fixture;

        // A role the runtime can actually start, added the way a user adds one.
        var role = await OkAsync(
            fixture,
            "role",
            "add",
            "fake",
            "--entry-command",
            "dotnet",
            "--entry-command",
            Execution.FakeAgentHost.Dll,
            "--entry-command",
            "hang");
        Assert.StartsWith("rol_", (string)role["id"]!, StringComparison.Ordinal);
        Assert.False((bool)role["builtin"]!);

        // Nine builtins and the new one, with a header line above them.
        var (_, listed, _) = await RunAsync(fixture, ["role", "list", "--human"]);
        var rows = listed.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(10, rows.Count(row => row.Contains("rol_", StringComparison.Ordinal)));
        Assert.StartsWith("manager", rows[2], StringComparison.Ordinal);
        Assert.StartsWith("fake", rows[^1], StringComparison.Ordinal);

        var campaign = await host.CampaignAsync(Ct);
        var created = await OkAsync(
            fixture,
            "workitem",
            "create",
            campaign,
            "--kind",
            "ai_role",
            "--role",
            "fake",
            "--priority",
            "5",
            "--context",
            "{\"icp\":\"founders\"}");
        var workItem = (string)created["id"]!;
        host.Track(workItem);
        Assert.StartsWith("wi_", workItem, StringComparison.Ordinal);
        Assert.Equal(5, (int)created["priority"]!);
        Assert.Equal("founders", (string?)created["context"]!["icp"]);

        var eligible = await OkAsync(fixture, "workitem", "list", "--campaign", campaign, "--eligible");
        Assert.Equal(workItem, (string?)Assert.Single(eligible["items"]!.AsArray())!["id"]);

        // A planner sharpens the brief; both the field and the context key are written down under its name.
        await OkAsync(fixture, "--actor", "role:planner", "workitem", "update", workItem, "--set", "{\"tone\":\"direct\"}", "--priority", "7");
        var entries = (await OkAsync(fixture, "journal", "list", "--work-item", workItem))["items"]!.AsArray();
        var priority = Single(entries, "workitem_updated");
        Assert.Equal("priority", (string?)priority["key"]);
        Assert.Equal(7, (int)priority["new"]!);
        var tone = Single(entries, "workitem_context_updated");
        Assert.Equal("tone", (string?)tone["key"]);
        Assert.Equal("role", (string?)tone["actor"]!["type"]);
        Assert.Equal("planner", (string?)tone["actor"]!["id"]);

        // With the work claimed and a host holding it open, the executor verbs are the CLI's to drive.
        await host.ScanAsync(Ct);
        var running = await host.WaitForStatusAsync(workItem, WorkItemStatus.Processing, Ct);
        var attempt = running.CurrentAttemptId!;

        var beat = await OkAsync(fixture, "workitem", "heartbeat", workItem, "--attempt", attempt);
        Assert.Equal(attempt, (string?)beat["attempt_id"]);
        await OkAsync(fixture, "workitem", "set-result", workItem, "--attempt", attempt, "--result", "{\"x\":1}");
        var done = await OkAsync(fixture, "workitem", "complete", workItem, "--attempt", attempt, "--status", "succeeded", "--result", "{\"done\":true}");
        Assert.Equal("succeeded", (string?)done["status"]);
        Assert.True((bool)done["result"]!["done"]!);

        // The attempt is over, so the host still holding the work is talking to nobody.
        var stale = await ErrorAsync(fixture, "workitem", "heartbeat", workItem, "--attempt", attempt);
        Assert.Equal("stale_attempt", (string?)stale["error"]!["code"]);
        var terminal = await ErrorAsync(fixture, "workitem", "cancel", workItem);
        Assert.Equal("workitem_terminal", (string?)terminal["error"]!["code"]);

        var (_, rendered, _) = await RunAsync(fixture, ["workitem", "get", workItem, "--snapshots", "--human"]);
        Assert.Contains("Status:", rendered, StringComparison.Ordinal);
        Assert.Contains(attempt, rendered, StringComparison.Ordinal);
        Assert.Contains("succeeded", rendered, StringComparison.Ordinal);
    }

    private static JsonObject Single(JsonArray entries, string kind) =>
        Assert.Single(entries, entry => (string?)entry!["kind"] == kind)!.AsObject();

    /// <summary>
    /// The decision as a person makes it: see what is waiting, read what it would do, answer it, and be told
    /// plainly when somebody has already answered. The work moves with the decision, which is what makes the
    /// answer worth anything.
    /// </summary>
    [Fact]
    public async Task A_person_reads_what_is_waiting_decides_it_and_cannot_decide_it_twice()
    {
        await using var fixture = await RuntimeApiFixture.StartAsync(Ct);
        string approval;
        string workItem;
        await using (var db = new JasonDbContext(JasonDbContext.CreateOptions(fixture.Paths.DatabaseFile)))
        {
            var parked = await ParkedWork.WriteAsync(db, Ct);
            workItem = parked.Item.PublicId;
            approval = parked.Approval.PublicId;
        }

        var waiting = (await OkAsync(fixture, "approval", "list"))["items"]!.AsArray();
        Assert.Equal(approval, (string?)Assert.Single(waiting)!["id"]);

        var read = await OkAsync(fixture, "approval", "get", approval);
        Assert.Equal("campaign.enroll", (string?)read["operation"]);
        Assert.Equal("ada@example.test", (string?)read["preview"]!["contact"]!["value"]);
        Assert.Equal("approval_required", (string?)read["reason"]);

        var decided = await OkAsync(fixture, "--actor", "human:ada@example.test", "approval", "approve", approval, "--reason", "go ahead");
        Assert.Equal("approved", (string?)decided["status"]);
        Assert.Equal("ada@example.test", (string?)decided["decided_by"]!["id"]);

        // The work went back into the queue, and nothing is waiting for a person any more.
        Assert.Equal("created", (string?)(await OkAsync(fixture, "workitem", "get", workItem))["status"]);
        Assert.Empty((await OkAsync(fixture, "approval", "list"))["items"]!.AsArray());

        var again = await ErrorAsync(fixture, "--actor", "human:ada@example.test", "approval", "reject", approval, "--reason", "changed my mind");
        Assert.Equal("approval_not_pending", (string?)again["error"]!["code"]);
    }

    /// <summary>A decision that names nobody is refused by the runtime, whatever the shell wanted to send.</summary>
    [Fact]
    public async Task A_decision_the_cli_sends_without_an_actor_is_refused()
    {
        await using var fixture = await RuntimeApiFixture.StartAsync(Ct);
        string approval;
        await using (var db = new JasonDbContext(JasonDbContext.CreateOptions(fixture.Paths.DatabaseFile)))
        {
            approval = (await ParkedWork.WriteAsync(db, Ct)).Approval.PublicId;
        }

        var refused = await ErrorAsync(fixture, "approval", "approve", approval);

        Assert.Equal("actor_required", (string?)refused["error"]!["code"]);
    }

    private static async Task<JsonObject> OkAsync(RuntimeApiFixture fixture, params string[] args)
    {
        var (exit, output, error) = await RunAsync(fixture, args);
        Assert.Equal(string.Empty, error);
        Assert.Equal(ExitCodes.Success, exit);
        return JsonNode.Parse(output)!.AsObject();
    }

    private static async Task<JsonObject> ErrorAsync(RuntimeApiFixture fixture, params string[] args)
    {
        var (exit, output, _) = await RunAsync(fixture, args);
        Assert.Equal(ExitCodes.ApiError, exit);
        return JsonNode.Parse(output)!.AsObject();
    }

    private static async Task<(int Exit, string Output, string Error)> RunAsync(RuntimeApiFixture fixture, string[] args)
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var exit = await CliApp.RunAsync(args, new CliEnvironment(output, error, fixture.Paths), Ct);
        return (exit, output.ToString().Trim(), error.ToString().Trim());
    }
}
