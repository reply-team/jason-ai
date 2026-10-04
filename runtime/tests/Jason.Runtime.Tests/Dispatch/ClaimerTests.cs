using System.Text.Json.Nodes;
using Jason.Contracts.Api;
using Jason.Contracts.Discovery;
using Jason.Contracts.Ids;
using Jason.Contracts.Operations;
using Jason.Contracts.Plugins;
using Jason.Runtime.Configuration;
using Jason.Runtime.Dispatch;
using Jason.Runtime.Execution;
using Jason.Runtime.Execution.Hosts;
using Jason.Runtime.Journal;
using Jason.Runtime.Persistence;
using Jason.Runtime.Plugins;
using Jason.Runtime.Plugins.Registry;
using Jason.Runtime.Routing;
using Jason.Runtime.Tests.Plugins;
using Jason.Runtime.WorkItems;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Jason.Runtime.Tests.Dispatch;

public class ClaimerTests
{
    private static readonly DateTime Noon = new(2026, 9, 14, 12, 0, 0, DateTimeKind.Utc);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_claim_schedules_the_item_and_records_what_the_attempt_will_run()
    {
        using var harness = new Harness();
        using var database = new TestDatabase();
        await using var db = database.Open();
        var campaign = WorkItemFactory.NewCampaign(now: Noon);
        var item = WorkItemFactory.NewAiRole(campaign, now: Noon, configure: w => w.Context = new JsonObject { ["brief"] = "the original brief" });
        db.Campaigns.Add(campaign);
        db.WorkItems.Add(item);
        await GiveRoleAsync(db, "researcher", "agent-host", "--serve");
        await db.SaveChangesAsync(Ct);

        var claimed = await harness.Claimer.ClaimAsync(db, 4, Ct);

        var work = Assert.Single(claimed);
        Assert.Equal(item.PublicId, work.WorkItemPublicId);
        Assert.Equal(item.Id, work.WorkItemId);
        Assert.Equal(WorkItemStatus.Scheduled, item.Status);

        // The snapshot is the context as it stood at claim: what the executor is told cannot change under it.
        item.Context["brief"] = "a later brief";
        await db.SaveChangesAsync(Ct);

        await using var fresh = database.Open();
        var attempt = await fresh.Attempts.AsNoTracking().SingleAsync(Ct);
        Assert.Equal(work.AttemptPublicId, attempt.PublicId);
        Assert.Equal(1, attempt.Number);
        Assert.Equal(AttemptStatus.Scheduled, attempt.Status);
        Assert.Equal(WorkItemKind.AiRole, attempt.Command);
        Assert.Equal(Noon, attempt.ClaimedAt);
        Assert.Equal(Noon.AddSeconds(3600), attempt.LockUntil);
        Assert.Null(attempt.StartedAt);
        Assert.Equal("the original brief", (string?)attempt.ContextSnapshot["brief"]);
        Assert.Equal(new[] { "agent-host", "--serve" }, attempt.Launch!.EntryCommand);
        Assert.Equal(Path.Combine(harness.Paths.WorkDirectory, item.PublicId, attempt.PublicId), attempt.Launch.WorkDir);
        Assert.Null(attempt.Launch.Pid);

        var entry = Assert.Single(await fresh.Journal.AsNoTracking().Where(e => e.Kind == JournalKinds.WorkItemScheduled).ToListAsync(Ct));
        Assert.Equal(ActorType.System, entry.ActorType);
        Assert.Equal("dispatcher", entry.ActorId);
        Assert.Equal("attempt", entry.Key);
        Assert.Equal(1, (int)entry.New!);
        Assert.Equal(item.PublicId, entry.WorkItemId);
        Assert.Equal(attempt.PublicId, entry.AttemptId);
        Assert.Equal(item.CampaignId, entry.CampaignId);
    }

    [Fact]
    public async Task One_campaign_gives_up_one_item_per_scan_and_gives_up_its_most_important()
    {
        using var harness = new Harness();
        using var database = new TestDatabase();
        await using var db = database.Open();
        var campaign = WorkItemFactory.NewCampaign(now: Noon);
        db.Campaigns.Add(campaign);
        var ordinary = WorkItemFactory.NewAiRole(campaign, now: Noon);
        var urgent = WorkItemFactory.NewAiRole(campaign, now: Noon, configure: w => w.Priority = 5);
        db.WorkItems.AddRange(ordinary, urgent);
        await GiveRoleAsync(db, "researcher", "agent-host");
        await db.SaveChangesAsync(Ct);

        var claimed = await harness.Claimer.ClaimAsync(db, 4, Ct);

        Assert.Equal(urgent.PublicId, Assert.Single(claimed).WorkItemPublicId);
    }

    [Fact]
    public async Task Equal_priority_is_settled_by_the_order_the_work_was_created_in()
    {
        using var harness = new Harness();
        using var database = new TestDatabase();
        await using var db = database.Open();
        var campaign = WorkItemFactory.NewCampaign(now: Noon);
        db.Campaigns.Add(campaign);
        var first = WorkItemFactory.NewAiRole(campaign, now: Noon);
        var second = WorkItemFactory.NewAiRole(campaign, now: Noon);
        db.WorkItems.Add(first);
        await db.SaveChangesAsync(Ct);
        db.WorkItems.Add(second);
        await GiveRoleAsync(db, "researcher", "agent-host");
        await db.SaveChangesAsync(Ct);

        var claimed = await harness.Claimer.ClaimAsync(db, 4, Ct);

        Assert.Equal(first.PublicId, Assert.Single(claimed).WorkItemPublicId);
    }

    [Fact]
    public async Task Every_campaign_gets_a_turn_in_the_same_scan()
    {
        using var harness = new Harness();
        using var database = new TestDatabase();
        await using var db = database.Open();
        var left = WorkItemFactory.NewCampaign("Left", now: Noon);
        var right = WorkItemFactory.NewCampaign("Right", now: Noon);
        db.Campaigns.AddRange(left, right);
        db.WorkItems.AddRange(
            WorkItemFactory.NewAiRole(left, now: Noon),
            WorkItemFactory.NewAiRole(left, now: Noon),
            WorkItemFactory.NewAiRole(right, now: Noon));
        await GiveRoleAsync(db, "researcher", "agent-host");
        await db.SaveChangesAsync(Ct);

        var claimed = await harness.Claimer.ClaimAsync(db, 4, Ct);

        Assert.Equal(2, claimed.Count);
        var campaigns = await db.WorkItems.AsNoTracking()
            .Where(w => w.Status == WorkItemStatus.Scheduled)
            .Select(w => w.CampaignId)
            .ToListAsync(Ct);
        Assert.Equal(new[] { left.Id, right.Id }, campaigns.Order().ToArray());
    }

    [Fact]
    public async Task A_single_free_slot_goes_to_the_most_important_work_anywhere()
    {
        using var harness = new Harness();
        using var database = new TestDatabase();
        await using var db = database.Open();
        var left = WorkItemFactory.NewCampaign("Left", now: Noon);
        var right = WorkItemFactory.NewCampaign("Right", now: Noon);
        db.Campaigns.AddRange(left, right);
        db.WorkItems.Add(WorkItemFactory.NewAiRole(left, now: Noon));
        var urgent = WorkItemFactory.NewAiRole(right, now: Noon, configure: w => w.Priority = 9);
        db.WorkItems.Add(urgent);
        await GiveRoleAsync(db, "researcher", "agent-host");
        await db.SaveChangesAsync(Ct);

        var claimed = await harness.Claimer.ClaimAsync(db, 1, Ct);

        Assert.Equal(urgent.PublicId, Assert.Single(claimed).WorkItemPublicId);
    }

    [Fact]
    public async Task A_campaign_with_work_in_flight_waits_its_turn()
    {
        using var harness = new Harness();
        using var database = new TestDatabase();
        await using var db = database.Open();
        var campaign = WorkItemFactory.NewCampaign(now: Noon);
        db.Campaigns.Add(campaign);
        db.WorkItems.Add(WorkItemFactory.NewAiRole(campaign, now: Noon, configure: w => w.Status = WorkItemStatus.Processing));
        db.WorkItems.Add(WorkItemFactory.NewAiRole(campaign, now: Noon));
        await GiveRoleAsync(db, "researcher", "agent-host");
        await db.SaveChangesAsync(Ct);

        Assert.Empty(await harness.Claimer.ClaimAsync(db, 4, Ct));
    }

    [Fact]
    public async Task A_campaign_that_is_not_active_yields_nothing()
    {
        using var harness = new Harness();
        using var database = new TestDatabase();
        await using var db = database.Open();
        var campaign = WorkItemFactory.NewCampaign(status: CampaignStatus.Paused, now: Noon);
        db.Campaigns.Add(campaign);
        db.WorkItems.Add(WorkItemFactory.NewAiRole(campaign, now: Noon));
        await GiveRoleAsync(db, "researcher", "agent-host");
        await db.SaveChangesAsync(Ct);

        Assert.Empty(await harness.Claimer.ClaimAsync(db, 4, Ct));
    }

    [Theory]
    [InlineData(-1, true)]
    [InlineData(1, false)]
    public async Task A_start_date_one_second_away_decides_the_claim(int offsetSeconds, bool claimable)
    {
        using var harness = new Harness();
        using var database = new TestDatabase();
        await using var db = database.Open();
        var campaign = WorkItemFactory.NewCampaign(now: Noon);
        db.Campaigns.Add(campaign);
        db.WorkItems.Add(WorkItemFactory.NewAiRole(campaign, now: Noon, configure: w => w.NotBefore = Noon.AddSeconds(offsetSeconds)));
        await GiveRoleAsync(db, "researcher", "agent-host");
        await db.SaveChangesAsync(Ct);

        Assert.Equal(claimable ? 1 : 0, (await harness.Claimer.ClaimAsync(db, 4, Ct)).Count);
    }

    [Theory]
    [InlineData(-1, true)]
    [InlineData(1, false)]
    public async Task A_retry_delay_one_second_away_decides_the_claim(int offsetSeconds, bool claimable)
    {
        using var harness = new Harness();
        using var database = new TestDatabase();
        await using var db = database.Open();
        var campaign = WorkItemFactory.NewCampaign(now: Noon);
        db.Campaigns.Add(campaign);
        db.WorkItems.Add(WorkItemFactory.NewAiRole(campaign, now: Noon, configure: w => w.RetryAfter = Noon.AddSeconds(offsetSeconds)));
        await GiveRoleAsync(db, "researcher", "agent-host");
        await db.SaveChangesAsync(Ct);

        Assert.Equal(claimable ? 1 : 0, (await harness.Claimer.ClaimAsync(db, 4, Ct)).Count);
    }

    [Fact]
    public async Task Work_that_is_already_overdue_is_left_to_the_expirer()
    {
        using var harness = new Harness();
        using var database = new TestDatabase();
        await using var db = database.Open();
        var campaign = WorkItemFactory.NewCampaign(now: Noon);
        db.Campaigns.Add(campaign);
        db.WorkItems.Add(WorkItemFactory.NewAiRole(campaign, now: Noon, configure: w => w.DueAt = Noon.AddSeconds(-1)));
        await GiveRoleAsync(db, "researcher", "agent-host");
        await db.SaveChangesAsync(Ct);

        Assert.Empty(await harness.Claimer.ClaimAsync(db, 4, Ct));
    }

    [Fact]
    public async Task A_provider_operation_fails_closed_because_no_route_exists_yet()
    {
        using var harness = new Harness();
        using var database = new TestDatabase();
        await using var db = database.Open();
        var campaign = WorkItemFactory.NewCampaign(now: Noon);
        db.Campaigns.Add(campaign);
        var item = WorkItemFactory.NewProviderOp(campaign, now: Noon);
        db.WorkItems.Add(item);
        await db.SaveChangesAsync(Ct);

        Assert.Empty(await harness.Claimer.ClaimAsync(db, 4, Ct));

        await using var fresh = database.Open();
        var stored = await fresh.WorkItems.AsNoTracking().SingleAsync(Ct);
        Assert.Equal(WorkItemStatus.Failed, stored.Status);
        Assert.Equal(AttemptErrors.NoRoute, stored.LastError!.Code);
        Assert.Equal(1, stored.AttemptCount);
        var attempt = await fresh.Attempts.AsNoTracking().SingleAsync(Ct);
        Assert.Equal(1, attempt.Number);
        Assert.Equal(AttemptStatus.Failed, attempt.Status);
        Assert.False(attempt.Error!.Retriable);
        Assert.Contains("campaign.get", attempt.Error.Message, StringComparison.Ordinal);
        Assert.Null(attempt.Launch);
    }

    [Fact]
    public async Task A_routed_provider_operation_is_claimed_with_the_plan_that_will_run_it()
    {
        using var harness = new Harness(
            plugin: TestPlugins.Loaded("stand-in-provider", ["campaign.get"]),
            route: new Route("stand-in-provider", new JsonObject { ["workspace"] = "west" }, "sha256:west"));
        using var database = new TestDatabase();
        await using var db = database.Open();
        var campaign = WorkItemFactory.NewCampaign(now: Noon);
        db.Campaigns.Add(campaign);
        var item = WorkItemFactory.NewProviderOp(campaign, now: Noon);
        item.Context = new JsonObject
        {
            [WorkItemService.InputKey] = new JsonObject { ["campaign"] = new JsonObject { ["external_id"] = "c-7714" } },
        };
        db.WorkItems.Add(item);
        await db.SaveChangesAsync(Ct);

        var work = Assert.Single(await harness.Claimer.ClaimAsync(db, 4, Ct));

        Assert.Equal(WorkItemStatus.Scheduled, item.Status);
        var plan = work.Plan;
        Assert.NotNull(plan);
        Assert.Equal("campaign.get", plan.Contract.Id);
        Assert.Equal("stand-in-provider", plan.Plugin.Manifest.Id);
        Assert.Equal(RouteScope.GlobalDefault, plan.Scope);
        Assert.Equal("sha256:west", plan.BindingIdentity);
        Assert.Equal("c-7714", (string?)plan.Input["args"]!["campaign"]!["external_id"]);
        Assert.Equal(campaign.PublicId, (string?)plan.Input["campaign"]!["id"]);
    }

    /// <summary>
    /// The pins are read inside the claim and they decide the claim: this item names the campaign by nothing but
    /// the identifier Jason already holds, so an unread pin would fail it for an input that is in fact complete.
    /// What another plugin calls the same campaign never travels.
    /// </summary>
    [Fact]
    public async Task The_plan_carries_the_routed_plugin_s_identifiers_and_no_other_plugin_s()
    {
        using var harness = new Harness(
            plugin: TestPlugins.Loaded("stand-in-provider", ["campaign.get"]),
            route: new Route("stand-in-provider", null, null));
        using var database = new TestDatabase();
        await using var db = database.Open();
        var campaign = WorkItemFactory.NewCampaign(now: Noon);
        campaign.ExternalIds.Add(Pin("stand-in-provider", "c-7714"));
        campaign.ExternalIds.Add(Pin("other-provider", "OTHER-1"));
        db.Campaigns.Add(campaign);
        db.WorkItems.Add(WorkItemFactory.NewProviderOp(campaign, now: Noon));
        await db.SaveChangesAsync(Ct);

        var work = Assert.Single(await harness.Claimer.ClaimAsync(db, 4, Ct));

        var identifiers = work.Plan!.Input["campaign"]!["external_ids"]!.AsObject();
        Assert.Equal("c-7714", (string?)identifiers["campaign"]);
        Assert.Single(identifiers);
    }

    /// <summary>The composed input is validated as a whole, and an item it refuses never reaches a child.</summary>
    [Fact]
    public async Task A_provider_operation_whose_composed_input_is_refused_fails_before_anything_starts()
    {
        using var harness = new Harness(
            plugin: TestPlugins.Loaded("stand-in-provider", ["campaign.get"]),
            route: new Route("stand-in-provider", null, null));
        using var database = new TestDatabase();
        await using var db = database.Open();
        var campaign = WorkItemFactory.NewCampaign(now: Noon);
        db.Campaigns.Add(campaign);

        // Nothing names the provider's campaign: no argument, and no pin either.
        var item = WorkItemFactory.NewProviderOp(campaign, now: Noon);
        item.Context = new JsonObject { ["note"] = "what the planner wrote" };
        db.WorkItems.Add(item);
        await db.SaveChangesAsync(Ct);

        Assert.Empty(await harness.Claimer.ClaimAsync(db, 4, Ct));

        await using var fresh = database.Open();
        var attempt = await fresh.Attempts.AsNoTracking().SingleAsync(Ct);
        Assert.Equal(AttemptErrors.InputInvalid, attempt.Error!.Code);
        Assert.Equal(FailureClass.Validation, attempt.Error.Class);
        Assert.False(attempt.Error.Retriable);
        Assert.Equal("/", Assert.Single(attempt.Error.Details!).Field);
        Assert.Null(attempt.Launch);

        // The attempt is kept, and it is kept with what the item said at the moment it was refused.
        Assert.Equal("what the planner wrote", (string?)attempt.ContextSnapshot["note"]);
    }

    /// <summary>
    /// A composed input a pattern could not finish measuring is no reason the item cannot run: nothing has happened,
    /// no child exists, and the next scan may measure it in a moment. So the attempt ends transient and the item goes
    /// back to the queue under the ordinary retry rule — the one outcome of the claim's checks that is not final.
    /// </summary>
    [Fact]
    public async Task A_composed_input_a_pattern_could_not_measure_ends_transient_and_the_item_comes_back()
    {
        using var harness = new Harness(
            dispatcher: options => options.RetryDelaySeconds = 30,
            plugin: TestPlugins.Loaded("stand-in-provider", ["campaign.get"]),
            route: new Route("stand-in-provider", null, null),
            timing: TimingOut.Always());
        using var database = new TestDatabase();
        await using var db = database.Open();
        var campaign = WorkItemFactory.NewCampaign(now: Noon);
        db.Campaigns.Add(campaign);
        var item = WorkItemFactory.NewProviderOp(campaign, now: Noon);
        item.Context = new JsonObject
        {
            [WorkItemService.InputKey] = new JsonObject { ["campaign"] = new JsonObject { ["external_id"] = "c-7714" } },
        };
        db.WorkItems.Add(item);
        await db.SaveChangesAsync(Ct);

        Assert.Empty(await harness.Claimer.ClaimAsync(db, 4, Ct));

        await using var fresh = database.Open();
        var attempt = await fresh.Attempts.AsNoTracking().SingleAsync(Ct);
        Assert.Equal((AttemptErrors.ValidationTimedOut, FailureClass.Transient, true), (attempt.Error!.Code, attempt.Error.Class, attempt.Error.Retriable));
        Assert.Null(attempt.Launch);
        var stored = await fresh.WorkItems.AsNoTracking().SingleAsync(Ct);
        Assert.Equal(WorkItemStatus.Created, stored.Status);
        Assert.Equal(1, stored.AttemptCount);
        Assert.Equal(Noon.AddSeconds(30), stored.RetryAfter);
    }

    [Fact]
    public async Task A_role_nothing_can_launch_fails_closed_and_says_where_to_configure_it()
    {
        using var harness = new Harness();
        using var database = new TestDatabase();
        await using var db = database.Open();
        var campaign = WorkItemFactory.NewCampaign(now: Noon);
        db.Campaigns.Add(campaign);
        db.WorkItems.Add(WorkItemFactory.NewAiRole(campaign, now: Noon));
        await db.SaveChangesAsync(Ct);

        Assert.Empty(await harness.Claimer.ClaimAsync(db, 4, Ct));

        await using var fresh = database.Open();
        var stored = await fresh.WorkItems.AsNoTracking().SingleAsync(Ct);
        Assert.Equal(WorkItemStatus.Failed, stored.Status);
        Assert.Equal(AttemptErrors.RoleNotLaunchable, stored.LastError!.Code);
        var attempt = await fresh.Attempts.AsNoTracking().SingleAsync(Ct);
        Assert.False(attempt.Error!.Retriable);
        Assert.Contains("researcher", attempt.Error.Message, StringComparison.Ordinal);
        Assert.Contains("Roles:DefaultEntryCommand", attempt.Error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task One_configured_command_is_enough_to_run_a_builtin_role()
    {
        using var harness = new Harness(roles: new RolesOptions { DefaultEntryCommand = ["dotnet", "host.dll"] });
        using var database = new TestDatabase();
        await using var db = database.Open();
        var campaign = WorkItemFactory.NewCampaign(now: Noon);
        db.Campaigns.Add(campaign);
        db.WorkItems.Add(WorkItemFactory.NewAiRole(campaign, now: Noon));
        await db.SaveChangesAsync(Ct);

        Assert.Single(await harness.Claimer.ClaimAsync(db, 4, Ct));

        await using var fresh = database.Open();
        var attempt = await fresh.Attempts.AsNoTracking().SingleAsync(Ct);
        Assert.Equal(new[] { "dotnet", "host.dll" }, attempt.Launch!.EntryCommand);
    }

    [Fact]
    public async Task Two_scans_over_one_item_produce_one_attempt()
    {
        using var harness = new Harness();
        using var other = new Harness();
        using var database = new TestDatabase();
        await using (var seed = database.Open())
        {
            var campaign = WorkItemFactory.NewCampaign(now: Noon);
            seed.Campaigns.Add(campaign);
            seed.WorkItems.Add(WorkItemFactory.NewAiRole(campaign, now: Noon));
            await GiveRoleAsync(seed, "researcher", "agent-host");
            await seed.SaveChangesAsync(Ct);
        }

        await using var left = database.Open();
        await using var right = database.Open();
        var results = await Task.WhenAll(
            Task.Run(() => harness.Claimer.ClaimAsync(left, 4, Ct), Ct),
            Task.Run(() => other.Claimer.ClaimAsync(right, 4, Ct), Ct));

        Assert.Equal(1, results.Sum(r => r.Count));
        await using var fresh = database.Open();
        Assert.Single(await fresh.Attempts.AsNoTracking().ToListAsync(Ct));
    }

    [Fact]
    public async Task The_claim_transaction_locks_the_database_for_writing_from_the_start()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();

        await using var transaction = await db.Database.BeginTransactionAsync(Ct);

        using var contender = new SqliteConnection($"Data Source={database.File};Default Timeout=1");
        contender.Open();
        using var insert = contender.CreateCommand();
        insert.CommandText =
            "INSERT INTO roles (public_id, name, builtin, entry_command_json, profile_defaults_json, created_at, updated_at) " +
            "VALUES ('rol_probe', 'probe', 0, '[]', '{}', '2026-09-14 12:00:00', '2026-09-14 12:00:00')";

        var started = DateTimeOffset.UtcNow;
        var refused = Assert.Throws<SqliteException>(() => insert.ExecuteNonQuery());

        // 5 = SQLITE_BUSY: the write lock the claim transaction already holds.
        Assert.Equal(5, refused.SqliteErrorCode);
        Assert.True(DateTimeOffset.UtcNow - started < TimeSpan.FromSeconds(5));
        await transaction.RollbackAsync(Ct);
    }

    private static ExternalId Pin(string pluginId, string value) => new()
    {
        PluginId = pluginId,
        Kind = "campaign",
        Value = value,
        RecordedAt = Noon,
        RecordedByAttemptId = PublicId.New("att"),
    };

    private static async Task GiveRoleAsync(JasonDbContext db, string name, params string[] entryCommand)
    {
        var role = await db.Roles.SingleAsync(r => r.Name == name, Ct);
        role.EntryCommand = [.. entryCommand];
    }

    /// <summary>
    /// An operation a person has to approve is not refused and is not run: it waits, as a row that says exactly
    /// what is being approved. And it waits without an attempt, because an attempt is a run — an item parked
    /// three times that had spent three of its attempts would be given up on for being patient.
    /// </summary>
    [Fact]
    public async Task An_operation_that_needs_approval_is_parked_before_any_attempt_exists()
    {
        using var harness = new Harness(
            plugin: TestPlugins.Loaded("stand-in-provider", ["campaign.enroll"]),
            route: new Route("stand-in-provider", new JsonObject { ["workspace"] = "west" }, "sha256:west"));
        using var database = new TestDatabase();
        await using var db = database.Open();
        var campaign = WorkItemFactory.NewCampaign(now: Noon);
        db.Campaigns.Add(campaign);
        var item = Enrolment(db, campaign);
        await db.SaveChangesAsync(Ct);

        Assert.Empty(await harness.Claimer.ClaimAsync(db, 4, Ct));

        await using var fresh = database.Open();
        var parked = await fresh.WorkItems.AsNoTracking().SingleAsync(Ct);
        Assert.Equal(WorkItemStatus.AwaitingApproval, parked.Status);
        Assert.Equal(0, parked.AttemptCount);
        Assert.Empty(await fresh.Attempts.AsNoTracking().ToListAsync(Ct));

        var approval = await fresh.Approvals.AsNoTracking().SingleAsync(Ct);
        Assert.StartsWith("apr_", approval.PublicId, StringComparison.Ordinal);
        Assert.Equal(ApprovalStatus.Pending, approval.Status);
        Assert.Equal("approval_required", approval.Reason);
        Assert.Equal("campaign.enroll", approval.Operation);
        Assert.Equal("stand-in-provider", approval.PluginId);
        Assert.Equal("sha256:west", approval.BindingIdentity);
        Assert.Equal(item.Id, approval.WorkItemId);
        Assert.StartsWith("sha256:", approval.SubjectHash, StringComparison.Ordinal);

        // What is being approved is the document the plugin would have received, not a summary of it.
        Assert.Equal("ada@example.test", (string?)approval.Subject["input"]!["contacts"]![0]!["channels"]![0]!["value"]);

        // And what a person reads says what it would do, to whom, and through which account.
        Assert.Equal("act", (string?)approval.Preview["reach"]!["value"]);
        Assert.Equal("ada@example.test", (string?)approval.Preview["contact"]!["value"]);
        Assert.Equal("sha256:west", (string?)approval.Preview["binding_identity"]);

        // It leads with the line the operation's own template renders from that same document, the values it
        // singles out beside it, and neither of them is part of what is approved.
        Assert.Equal(
            "Enroll Ada — (ada@example.test) in campaign Flow at the provider: start at first_step, first touch authored_delay, "
            + "skip if they already take part. If the campaign is live there, its messages start going to this person on that timing.",
            (string?)approval.Preview["summary"]);
        Assert.Equal("ada@example.test", (string?)approval.Preview["highlights"]!["/contacts/0/channels/0/value"]);
        Assert.DoesNotContain(approval.Subject, member => member.Key is "preview" or "summary" or "highlights");

        var lines = await fresh.Journal.AsNoTracking()
            .Where(e => e.WorkItemId == item.PublicId)
            .OrderBy(e => e.Id)
            .Select(e => e.Kind)
            .ToListAsync(Ct);
        Assert.Equal(["workitem_awaiting_approval", "approval_requested"], lines);
    }

    /// <summary>
    /// Only a park makes no attempt. Everything else the pre-flight decides is a refusal, and a refusal is a
    /// thing that happened to the work: it keeps its attempt, its provenance, its coded failure and its one
    /// against the attempt budget. Both halves are here, in one scan, so neither can quietly become the other.
    /// </summary>
    [Fact]
    public async Task A_refusal_keeps_its_attempt_and_a_park_makes_none()
    {
        using var harness = new Harness(
            plugin: TestPlugins.Loaded("stand-in-provider", ["campaign.enroll"]),
            route: new Route("stand-in-provider", null, null));
        using var database = new TestDatabase();
        await using var db = database.Open();

        var parking = WorkItemFactory.NewCampaign(now: Noon);
        var refusing = WorkItemFactory.NewCampaign("Other", now: Noon);
        db.Campaigns.Add(parking);
        db.Campaigns.Add(refusing);
        var parked = Enrolment(db, parking);

        // The same route, an operation this package does not perform: refused, and never handed to another.
        var refused = WorkItemFactory.NewProviderOp(refusing, now: Noon);
        db.WorkItems.Add(refused);
        await db.SaveChangesAsync(Ct);

        Assert.Empty(await harness.Claimer.ClaimAsync(db, 4, Ct));

        await using var fresh = database.Open();
        var attempt = Assert.Single(await fresh.Attempts.AsNoTracking().ToListAsync(Ct));
        Assert.Equal(refused.Id, attempt.WorkItemId);
        Assert.Equal(AttemptErrors.PluginOperationUnsupported, attempt.Error!.Code);
        Assert.Equal("stand-in-provider", attempt.Provenance!.PluginId);

        var failed = await fresh.WorkItems.AsNoTracking().SingleAsync(w => w.Id == refused.Id, Ct);
        Assert.Equal(WorkItemStatus.Failed, failed.Status);
        Assert.Equal(1, failed.AttemptCount);

        var waiting = await fresh.WorkItems.AsNoTracking().SingleAsync(w => w.Id == parked.Id, Ct);
        Assert.Equal(WorkItemStatus.AwaitingApproval, waiting.Status);
        Assert.Equal(0, waiting.AttemptCount);
        Assert.Null(waiting.LastError);
    }

    /// <summary>
    /// The guarantee the gate makes, at the boundary that makes it: parked work is not claimable, and no number
    /// of scans turns waiting into running.
    /// </summary>
    [Fact]
    public async Task An_item_awaiting_approval_is_never_claimed_however_many_scans_pass()
    {
        using var harness = new Harness(
            plugin: TestPlugins.Loaded("stand-in-provider", ["campaign.enroll"]),
            route: new Route("stand-in-provider", null, null));
        using var database = new TestDatabase();
        await using var db = database.Open();
        var campaign = WorkItemFactory.NewCampaign(now: Noon);
        db.Campaigns.Add(campaign);
        Enrolment(db, campaign);
        await db.SaveChangesAsync(Ct);

        for (var scan = 0; scan < 3; scan++)
        {
            Assert.Empty(await harness.Claimer.ClaimAsync(db, 4, Ct));
        }

        await using var fresh = database.Open();
        Assert.Equal(WorkItemStatus.AwaitingApproval, (await fresh.WorkItems.AsNoTracking().SingleAsync(Ct)).Status);
        Assert.Empty(await fresh.Attempts.AsNoTracking().ToListAsync(Ct));

        // One decision, not one per scan: a person is asked once about one piece of work.
        Assert.Single(await fresh.Approvals.AsNoTracking().ToListAsync(Ct));
    }

    /// <summary>
    /// A campaign runs one thing at a time, and the rule keys on what is being worked on. Work waiting for a
    /// person is not being worked on, so the rest of its campaign keeps moving — otherwise one unanswered
    /// decision would quietly stop everything else.
    /// </summary>
    [Fact]
    public async Task A_parked_item_does_not_stop_the_rest_of_its_campaign()
    {
        using var harness = new Harness(
            plugin: TestPlugins.Loaded("stand-in-provider", ["campaign.enroll", "campaign.get"]),
            route: new Route("stand-in-provider", null, null));
        using var database = new TestDatabase();
        await using var db = database.Open();
        var campaign = WorkItemFactory.NewCampaign(now: Noon);
        db.Campaigns.Add(campaign);
        Enrolment(db, campaign);
        var reading = WorkItemFactory.NewProviderOp(campaign, now: Noon);
        reading.Context = new JsonObject
        {
            [WorkItemService.InputKey] = new JsonObject { ["campaign"] = new JsonObject { ["external_id"] = "c-7714" } },
        };
        db.WorkItems.Add(reading);
        await db.SaveChangesAsync(Ct);

        Assert.Empty(await harness.Claimer.ClaimAsync(db, 4, Ct));
        var work = Assert.Single(await harness.Claimer.ClaimAsync(db, 4, Ct));

        Assert.Equal(reading.PublicId, work.WorkItemPublicId);
        Assert.Equal(WorkItemStatus.Scheduled, reading.Status);
    }

    /// <summary>
    /// What a person approved is what runs, and the attempt says which decision released it — so the chronicle
    /// reads decision, then act, rather than leaving somebody to line up two timestamps.
    /// </summary>
    [Fact]
    public async Task An_approved_subject_is_claimed_and_its_attempt_names_the_approval_that_released_it()
    {
        using var harness = new Harness(
            plugin: TestPlugins.Loaded("stand-in-provider", ["campaign.enroll"]),
            route: new Route("stand-in-provider", null, null));
        using var database = new TestDatabase();
        await using var db = database.Open();
        var campaign = WorkItemFactory.NewCampaign(now: Noon);
        db.Campaigns.Add(campaign);
        var item = Enrolment(db, campaign);
        await db.SaveChangesAsync(Ct);
        Assert.Empty(await harness.Claimer.ClaimAsync(db, 4, Ct));

        var approval = await Decide(db, ApprovalStatus.Approved);

        var work = Assert.Single(await harness.Claimer.ClaimAsync(db, 4, Ct));

        Assert.Equal(item.PublicId, work.WorkItemPublicId);
        Assert.Equal("campaign.enroll", work.Plan!.Contract.Id);

        await using var fresh = database.Open();
        var attempt = Assert.Single(await fresh.Attempts.AsNoTracking().ToListAsync(Ct));
        Assert.Equal(approval.PublicId, attempt.Provenance!.ApprovalId);
        Assert.Equal(ApprovalStatus.Approved, (await fresh.Approvals.AsNoTracking().SingleAsync(Ct)).Status);
    }

    /// <summary>
    /// The decision is about a subject, not about an item. An input edited after a person approved it is a
    /// different subject, so the hash no longer matches and the work is parked again — with the reason saying
    /// which of the two things happened — rather than running under a decision nobody made about it.
    /// </summary>
    [Fact]
    public async Task An_input_changed_after_the_decision_is_parked_again_and_says_which_hash_moved()
    {
        using var harness = new Harness(
            plugin: TestPlugins.Loaded("stand-in-provider", ["campaign.enroll"]),
            route: new Route("stand-in-provider", null, null));
        using var database = new TestDatabase();
        await using var db = database.Open();
        var campaign = WorkItemFactory.NewCampaign(now: Noon);
        db.Campaigns.Add(campaign);
        var item = Enrolment(db, campaign);
        await db.SaveChangesAsync(Ct);
        Assert.Empty(await harness.Claimer.ClaimAsync(db, 4, Ct));
        var first = await Decide(db, ApprovalStatus.Approved);

        // The planner edits the enrollment after it was approved: another campaign at the provider.
        item.Status = WorkItemStatus.Created;
        item.Context = new JsonObject { [WorkItemService.InputKey] = Enrol("sq-2240") };
        await db.SaveChangesAsync(Ct);

        Assert.Empty(await harness.Claimer.ClaimAsync(db, 4, Ct));

        await using var fresh = database.Open();
        Assert.Equal(WorkItemStatus.AwaitingApproval, (await fresh.WorkItems.AsNoTracking().SingleAsync(Ct)).Status);
        Assert.Empty(await fresh.Attempts.AsNoTracking().ToListAsync(Ct));

        var approvals = await fresh.Approvals.AsNoTracking().OrderBy(a => a.Id).ToListAsync(Ct);
        Assert.Equal(2, approvals.Count);
        Assert.Equal(first.PublicId, approvals[0].PublicId);
        Assert.Equal(ApprovalStatus.Superseded, approvals[0].Status);

        // A decision the work outgrew is still a decision somebody made: only its status moves. The dispatcher
        // writing itself over the approver here would leave the person's name in the chronicle and nowhere a
        // reader of approval.get would ever look.
        Assert.Equal(ActorType.Human, approvals[0].DecidedByType);
        Assert.Equal("operator", approvals[0].DecidedById);
        Assert.Equal("go ahead", approvals[0].DecisionReason);
        Assert.Equal(ApprovalStatus.Pending, approvals[1].Status);
        Assert.Equal("input_changed", approvals[1].Reason);
        Assert.NotEqual(approvals[0].SubjectHash, approvals[1].SubjectHash);
        Assert.Contains("approval_superseded", await fresh.Journal.AsNoTracking().Select(e => e.Kind).ToListAsync(Ct));
    }

    /// <summary>
    /// A person's decision, exactly as the API verb will make it: the row is decided and the work it was holding
    /// is let back into the queue. These tests are about what the claim does with that, not about the verb.
    /// </summary>
    private static async Task<Approval> Decide(JasonDbContext db, ApprovalStatus status)
    {
        var approval = await db.Approvals.Include(a => a.WorkItem).SingleAsync(a => a.Status == ApprovalStatus.Pending, Ct);
        approval.Status = status;
        approval.DecidedAt = Noon;
        approval.DecidedByType = ActorType.Human;
        approval.DecidedById = "operator";
        approval.DecisionReason = "go ahead";
        if (status == ApprovalStatus.Approved)
        {
            WorkItemTransitions.Apply(approval.WorkItem!, WorkItemStatus.Created, Noon);
        }

        await db.SaveChangesAsync(Ct);
        return approval;
    }

    /// <summary>
    /// The register is asked only where its answer decides something. The record that somebody said stop declares
    /// that the register does not apply, so a person the register holds is claimed for it without the question being
    /// put; work that would reach them asks, and is refused.
    /// </summary>
    [Theory]
    [InlineData("optout.record", 0, null)]
    [InlineData("list_membership.add", 1, "suppressed")]
    public async Task The_register_is_asked_only_where_its_answer_can_refuse_the_work(string operation, int asked, string? refused)
    {
        var register = new Harness.EverybodyIsSuppressed();
        using var harness = new Harness(
            plugin: TestPlugins.Loaded("stand-in-provider", [operation]),
            route: new Route("stand-in-provider", null, null),
            register: register);
        using var database = new TestDatabase();
        await using var db = database.Open();
        var campaign = WorkItemFactory.NewCampaign(now: Noon);
        db.Campaigns.Add(campaign);
        var contact = new Contact { PublicId = PublicId.New("cnt"), FirstName = "Ada", CreatedAt = Noon, UpdatedAt = Noon };
        contact.Channels.Add(new ContactChannel { Channel = "email", Value = "ada@example.test", IsPrimary = true });
        db.Contacts.Add(contact);
        var item = WorkItemFactory.NewProviderOp(campaign, operation, Noon);
        item.Contact = contact;
        item.Context = new JsonObject
        {
            [WorkItemService.InputKey] = operation == "optout.record"
                ? new JsonObject
                {
                    ["channel"] = "email",
                    ["statement"] = "please take me off your list",
                    ["arrived_via"] = "reply",
                    ["said_at"] = "2026-10-03T08:14:00Z",
                    ["learned_at"] = "2026-10-03T08:20:00Z",
                }
                : new JsonObject { ["list"] = new JsonObject { ["external_id"] = "9" }, ["channel"] = "email" },
        };
        db.WorkItems.Add(item);
        await db.SaveChangesAsync(Ct);

        var claimed = await harness.Claimer.ClaimAsync(db, 4, Ct);

        Assert.Equal(asked, register.Asked);
        if (refused is null)
        {
            Assert.Single(claimed);
            Assert.Equal(WorkItemStatus.Scheduled, item.Status);
        }
        else
        {
            Assert.Empty(claimed);
            Assert.Equal(WorkItemStatus.Failed, item.Status);
            Assert.Equal(refused, (await db.Attempts.AsNoTracking().SingleAsync(Ct)).Error!.Code);
        }
    }

    /// <summary>
    /// The claim itself, not only the query beneath it: in a paused campaign it schedules the provider's pause and the
    /// record that somebody said stop, and holds an add, which is control and auto like them and still waits. Each sits
    /// in a paused campaign of its own, so the one-per-campaign rule cannot hide a disagreement.
    /// </summary>
    [Fact]
    public async Task In_a_paused_campaign_the_claim_schedules_what_runs_there_and_holds_the_rest()
    {
        using var harness = new Harness(
            plugin: TestPlugins.Loaded("stand-in-provider", ["campaign.pause", "optout.record", "list_membership.add"]),
            route: new Route("stand-in-provider", null, null));
        using var database = new TestDatabase();
        await using var db = database.Open();
        var pause = PausedItem(db, "campaign.pause", null, new JsonObject { ["campaign"] = new JsonObject { ["external_id"] = "7" } });
        var optout = PausedItem(db, "optout.record", Person(db), new JsonObject
        {
            ["channel"] = "email",
            ["statement"] = "please take me off your list",
            ["arrived_via"] = "reply",
            ["said_at"] = "2026-10-03T08:14:00Z",
            ["learned_at"] = "2026-10-03T08:20:00Z",
        });
        var add = PausedItem(db, "list_membership.add", Person(db), new JsonObject
        {
            ["list"] = new JsonObject { ["external_id"] = "9" },
            ["channel"] = "email",
        });
        await db.SaveChangesAsync(Ct);

        var claimed = await harness.Claimer.ClaimAsync(db, 4, Ct);

        Assert.Equal(
            new[] { pause.PublicId, optout.PublicId }.Order(StringComparer.Ordinal),
            claimed.Select(work => work.WorkItemPublicId).Order(StringComparer.Ordinal));
        Assert.Equal(WorkItemStatus.Scheduled, pause.Status);
        Assert.Equal(WorkItemStatus.Scheduled, optout.Status);
        Assert.Equal(WorkItemStatus.Created, add.Status);
    }

    /// <summary>
    /// The campaign's choice is made among the work its status lets through, before the most important is picked: a
    /// paused campaign whose most important items wait offers the pause behind them, and nothing at all while one of its
    /// items is in flight.
    /// </summary>
    [Fact]
    public async Task A_paused_campaign_offers_the_work_that_runs_there_behind_more_important_work_that_waits()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var campaign = WorkItemFactory.NewCampaign("paused", CampaignStatus.Paused, Noon);
        db.Campaigns.Add(campaign);
        var enroll = WorkItemFactory.NewProviderOp(campaign, "campaign.enroll", Noon);
        enroll.Priority = 10;
        var role = WorkItemFactory.NewAiRole(campaign, now: Noon, configure: item => item.Priority = 9);
        var pause = WorkItemFactory.NewProviderOp(campaign, "campaign.pause", Noon);
        db.WorkItems.AddRange(enroll, role, pause);
        await db.SaveChangesAsync(Ct);

        Assert.Equal([pause.Id], await Claimer.CandidatesAsync(db, Noon, freeSlots: 4, Ct));

        db.WorkItems.Add(WorkItemFactory.NewAiRole(campaign, now: Noon, configure: item => item.Status = WorkItemStatus.Processing));
        await db.SaveChangesAsync(Ct);

        Assert.Empty(await Claimer.CandidatesAsync(db, Noon, freeSlots: 4, Ct));
    }

    private static WorkItem PausedItem(JasonDbContext db, string operation, Contact? contact, JsonObject arguments)
    {
        var campaign = WorkItemFactory.NewCampaign("paused " + operation, CampaignStatus.Paused, Noon);
        db.Campaigns.Add(campaign);
        var item = WorkItemFactory.NewProviderOp(campaign, operation, Noon);
        item.Contact = contact;
        item.Context = new JsonObject { [WorkItemService.InputKey] = arguments };
        db.WorkItems.Add(item);
        return item;
    }

    private static Contact Person(JasonDbContext db)
    {
        var contact = new Contact { PublicId = PublicId.New("cnt"), FirstName = "Ada", CreatedAt = Noon, UpdatedAt = Noon };
        contact.Channels.Add(new ContactChannel { Channel = "email", Value = $"ada.{contact.PublicId.ToLowerInvariant()}@example.test", IsPrimary = true });
        db.Contacts.Add(contact);
        return contact;
    }

    /// <summary>An enrollment with nothing wrong with it, so the only thing that can stop it is a person.</summary>
    private static WorkItem Enrolment(JasonDbContext db, Campaign campaign)
    {
        var contact = new Contact { PublicId = PublicId.New("cnt"), FirstName = "Ada", CreatedAt = Noon, UpdatedAt = Noon };
        contact.Channels.Add(new ContactChannel { Channel = "email", Value = "ada@example.test", IsPrimary = true });
        db.Contacts.Add(contact);

        var item = WorkItemFactory.NewProviderOp(campaign, "campaign.enroll", Noon);
        item.Contact = contact;
        item.Context = new JsonObject { [WorkItemService.InputKey] = Enrol() };
        db.WorkItems.Add(item);
        return item;
    }

    private static JsonObject Enrol(string campaignId = "sq-1129") => new()
    {
        ["campaign"] = new JsonObject { ["external_id"] = campaignId },
        ["channel"] = "email",
        ["collision"] = "skip",
        ["start"] = new JsonObject { ["position"] = "first_step" },
        ["first_touch"] = "authored_delay",
    };

    private sealed class Harness : IDisposable
    {
        private readonly TempDataDir _dir = new();

        public Harness(
            Action<DispatcherOptions>? dispatcher = null,
            RolesOptions? roles = null,
            LoadedPlugin? plugin = null,
            Route? route = null,
            ISuppressionCheck? register = null,
            PatternTiming? timing = null)
        {
            var clock = new FixedClock(Noon);
            var settings = TestOptions.Settings(dispatcher);
            var journal = new JournalWriter(clock);
            var plugins = new PluginRegistry(clock);
            if (plugin is not null)
            {
                plugins.Replace(
                    new PluginSnapshot(PublicId.New(PluginProtocol.SnapshotIdPrefix), Noon, SnapshotSource.Startup, [plugin]),
                    new ReloadReport(Noon, SnapshotSource.Startup, Activated: true, [], []));
            }

            var routing = new RouteRegistry(clock, plugins);
            if (route is not null)
            {
                routing.Replace(new RouteSnapshot(
                    PublicId.New(RouteSnapshot.IdPrefix),
                    Noon,
                    plugins.Snapshot.Id,
                    new RouteSet(route, RouteSet.Empty.Operations),
                    RouteSnapshot.Empty(Noon, plugins.Snapshot.Id).Campaigns));
            }

            Claimer = new Claimer(
                journal,
                clock,
                new AttemptOutcomes(journal, clock, settings),
                new EntryCommandResolver(TestOptions.RoleSettings(roles)),
                new AgentLaunchPlanner(
                    new AgentPreflight(),
                    new ProgramResolver(new ThisMachineHasNothing()),
                    [new ClaudeCodeHost()]),
                TestOptions.RoleSettings(roles),
                plugins,
                routing,
                new ExternalIdStore(journal, clock),
                register ?? new NothingIsSuppressed(),
                settings,
                _dir.Paths,
                NullLogger<Claimer>.Instance,
                timing);
        }

        public JasonPaths Paths => _dir.Paths;

        public Claimer Claimer { get; }

        public void Dispose() => _dir.Dispose();

        /// <summary>
        /// No profile is configured in these tests, so nothing reaches the resolver; a search path that answers
        /// nothing keeps that true rather than letting a machine's own PATH decide what they prove.
        /// </summary>
        private sealed class ThisMachineHasNothing : ISearchPath
        {
            public string? Path => null;

            public string? PathExt => null;
        }

        /// <summary>A register that holds everybody, and counts how often it was asked.</summary>
        internal sealed class EverybodyIsSuppressed : ISuppressionCheck
        {
            public int Asked { get; private set; }

            public Task<bool> IsSuppressedAsync(JasonDbContext db, string channel, string value, CancellationToken cancellationToken)
            {
                Asked++;
                return Task.FromResult(true);
            }
        }

        /// <summary>The register these tests are not about; the suppression check itself is tested where it lives.</summary>
        private sealed class NothingIsSuppressed : ISuppressionCheck
        {
            public Task<bool> IsSuppressedAsync(JasonDbContext db, string channel, string value, CancellationToken cancellationToken) =>
                Task.FromResult(false);
        }
    }
}
