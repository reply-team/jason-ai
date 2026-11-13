using System.Text.Json.Nodes;
using Jason.Contracts.Api;
using Jason.Contracts.Discovery;
using Jason.Contracts.Ids;
using Jason.Contracts.Operations;
using Jason.Contracts.Plugins;
using Jason.Runtime.Configuration;
using Jason.Runtime.Contacts;
using Jason.Runtime.Dispatch;
using Jason.Runtime.Execution;
using Jason.Runtime.Execution.Hosts;
using Jason.Runtime.Journal;
using Jason.Runtime.Json;
using Jason.Runtime.Persistence;
using Jason.Runtime.Plugins;
using Jason.Runtime.Plugins.Registry;
using Jason.Runtime.Routing;
using Jason.Runtime.Tests.Plugins;
using Jason.Runtime.WorkItems;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Jason.Runtime.Tests.Dispatch;

public partial class ClaimerTests
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
    [Critical(Family.W6, "one per campaign per scan")]
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
    [Critical(Family.W6, "slots and priority")]
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
    [Critical(Family.W3, "fail closed without a route")]
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

        // A first attempt follows nothing that may have acted.
        Assert.Null(plan.UnsettledSince);
    }

    /// <summary>
    /// A later attempt is told when the earliest earlier attempt that may have acted was claimed: one that ended
    /// ambiguous, which is how every attempt that lost its answer ends. A provider's own answer of any other class
    /// settled its attempt, so a settled attempt after an unsettled one does not move the moment, and attempts that were
    /// all settled leave it null. An attempt that ended ambiguous and never started, as a claim refusal of an unsettled
    /// send does, ran nothing, so it is not the earliest. The moment travels beside the input and never in it, so what a
    /// person approved is unchanged.
    /// </summary>
    [Theory]
    [InlineData(FailureClass.Ambiguous, FailureClass.Transient, 1, true)]
    [InlineData(FailureClass.Transient, FailureClass.Ambiguous, 2, true)]
    [InlineData(FailureClass.Ambiguous, FailureClass.Ambiguous, 1, true)]
    [InlineData(FailureClass.Transient, FailureClass.Transient, null, true)]
    [InlineData(FailureClass.Ambiguous, FailureClass.Ambiguous, 2, false)]
    [InlineData(FailureClass.Ambiguous, FailureClass.Transient, null, false)]
    public async Task A_later_attempt_is_told_when_the_earliest_attempt_that_may_have_acted_was_claimed(
        FailureClass first, FailureClass second, int? unsettled, bool firstStarted)
    {
        using var harness = new Harness(
            plugin: TestPlugins.Loaded("stand-in-provider", ["campaign.get"]),
            route: new Route("stand-in-provider", null, null));
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

        var claimedAt = new[] { Noon.AddHours(-5), Noon.AddHours(-2) };
        foreach (var (failure, index) in new[] { first, second }.Select((failure, index) => (failure, index)))
        {
            Assert.Single(await harness.Claimer.ClaimAsync(db, 4, Ct));
            var attempt = await db.Attempts.SingleAsync(a => a.Number == index + 1, Ct);
            attempt.Status = AttemptStatus.Failed;
            attempt.ClaimedAt = claimedAt[index];
            attempt.StartedAt = index == 0 && !firstStarted ? null : claimedAt[index];
            attempt.Error = new AttemptErrorDto("provider_answer_lost", "No answer came back.", Retriable: true, Class: failure);
            item.Status = WorkItemStatus.Created;
            await db.SaveChangesAsync(Ct);
        }

        var work = Assert.Single(await harness.Claimer.ClaimAsync(db, 4, Ct));

        Assert.Equal(3, (await db.Attempts.SingleAsync(a => a.PublicId == work.AttemptPublicId, Ct)).Number);
        Assert.Equal(
            unsettled is { } number ? (DateTimeOffset?)new DateTimeOffset(claimedAt[number - 1]) : null,
            work.Plan!.UnsettledSince);
        Assert.False(work.Plan!.Input.ContainsKey("unsettled_since"));
        Assert.Null(work.Plan!.UnsettledReference);
    }

    /// <summary>
    /// A later attempt is told the send the latest earlier attempt that may have acted returned, so it can read that send
    /// before it repeats anything: only an attempt that ended ambiguous counts, as for the moment, so a send an attempt
    /// that settled returned is not taken, and an unsettled attempt that returned none leaves the latest one that did.
    /// An attempt that never started ran nothing, so one ended ambiguous at its claim is not taken either. Each row is the
    /// earlier attempts in order, as their class, or <c>unstarted</c> for one ambiguous and never started, and the send
    /// each returned.
    /// </summary>
    [Theory]
    [InlineData("", null)]
    [InlineData("ambiguous/", null)]
    [InlineData("ambiguous/email-1007", "email-1007")]
    [InlineData("ambiguous/email-1007 ambiguous/email-1009", "email-1009")]
    [InlineData("ambiguous/email-1007 ambiguous/", "email-1007")]
    [InlineData("ambiguous/email-1007 permanent/email-1009", "email-1007")]
    [InlineData("transient/email-1007 ambiguous/", null)]
    [InlineData("transient/email-1007 ambiguous/email-1009", "email-1009")]
    [InlineData("ambiguous/email-1007 unstarted/email-1009", "email-1007")]
    public void A_later_attempt_is_told_the_send_the_latest_attempt_that_may_have_acted_returned(string earlier, string? reference)
    {
        var item = WorkItemFactory.NewProviderOp(WorkItemFactory.NewCampaign(now: Noon), "message.send", Noon);
        foreach (var said in earlier.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var (failure, returned) = (said.Split('/')[0], said.Split('/')[1]);
            var attempt = AttemptOf(item, item.Attempts.Count + 1);
            attempt.Status = AttemptStatus.Failed;
            var unstarted = failure == "unstarted";
            attempt.StartedAt = unstarted ? null : attempt.StartedAt;
            attempt.Error = new AttemptErrorDto("provider_answer_lost", "No answer came back.", Retriable: true, Class: unstarted ? FailureClass.Ambiguous : Enum.Parse<FailureClass>(failure, ignoreCase: true));
            attempt.Provenance = Returned(returned.Length == 0 ? null : new Dictionary<string, string> { ["contact"] = "1001", ["send"] = returned });
            item.Attempts.Add(attempt);
        }

        var next = AttemptOf(item, item.Attempts.Count + 1);

        Assert.Equal(reference, Claimer.UnsettledReference(OperationCatalog.Find("message.send")!, item, next));
    }

    /// <summary>
    /// The send is the work item's own identifier of that kind, as its operation declares it: an operation that declares
    /// none is told none, whatever an earlier attempt of it returned under that name.
    /// </summary>
    [Fact]
    public void An_operation_that_declares_no_send_of_its_own_is_told_none()
    {
        var item = WorkItemFactory.NewProviderOp(WorkItemFactory.NewCampaign(now: Noon), "campaign.get", Noon);
        var earlier = AttemptOf(item, 1);
        earlier.Status = AttemptStatus.Failed;
        earlier.Error = new AttemptErrorDto("provider_answer_lost", "No answer came back.", Retriable: true, Class: FailureClass.Ambiguous);
        earlier.Provenance = Returned(new Dictionary<string, string> { ["send"] = "email-1007" });
        item.Attempts.Add(earlier);

        Assert.Null(Claimer.UnsettledReference(OperationCatalog.Find("campaign.get")!, item, AttemptOf(item, 2)));
        Assert.Equal("email-1007", Claimer.UnsettledReference(OperationCatalog.Find("social_invitation.send")!, item, AttemptOf(item, 2)));
    }

    private static Attempt AttemptOf(WorkItem item, int number) => new()
    {
        PublicId = PublicId.New("att"),
        WorkItem = item,
        Number = number,
        Command = item.Kind,
        Status = AttemptStatus.Scheduled,
        ClaimedAt = Noon.AddHours(number - 10),
        StartedAt = Noon.AddHours(number - 10),
        LockUntil = Noon,
    };

    private static AttemptProvenanceDto Returned(IReadOnlyDictionary<string, string>? identifiers) =>
        new(null, null, null, null, null, null, null, null, null, null, null, ExternalIdsReturned: identifiers);

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

    /// <summary>
    /// A refusal of the claim is said once, under the attempt's own scope, so a log read for the work item finds it:
    /// here a plugin that does not perform the operation. The next two say it for a composed input the operation's
    /// schema refuses and for a pattern that could not finish, which are different answers ended in the same place.
    /// </summary>
    [Fact]
    public async Task An_operation_the_routed_plugin_does_not_perform_is_said_once_at_the_claim()
    {
        using var harness = new Harness(
            plugin: TestPlugins.Loaded("stand-in-provider", ["conversation.get"]),
            route: new Route("stand-in-provider", null, null));
        using var database = new TestDatabase();
        await using var db = database.Open();
        var campaign = WorkItemFactory.NewCampaign(now: Noon);
        db.Campaigns.Add(campaign);
        var item = WorkItemFactory.NewProviderOp(campaign, now: Noon);
        db.WorkItems.Add(item);
        await db.SaveChangesAsync(Ct);

        Assert.Empty(await harness.Claimer.ClaimAsync(db, 4, Ct));

        await AssertSaidOnceAsync(harness, database, campaign, item, AttemptErrors.PluginOperationUnsupported);
    }

    [Fact]
    public async Task A_composed_input_the_operation_refuses_is_said_once_at_the_claim()
    {
        using var harness = new Harness(
            plugin: TestPlugins.Loaded("stand-in-provider", ["campaign.get"]),
            route: new Route("stand-in-provider", null, null));
        using var database = new TestDatabase();
        await using var db = database.Open();
        var campaign = WorkItemFactory.NewCampaign(now: Noon);
        db.Campaigns.Add(campaign);
        var item = WorkItemFactory.NewProviderOp(campaign, now: Noon);
        item.Context = new JsonObject { ["note"] = "what the planner wrote" };
        db.WorkItems.Add(item);
        await db.SaveChangesAsync(Ct);

        Assert.Empty(await harness.Claimer.ClaimAsync(db, 4, Ct));

        await AssertSaidOnceAsync(harness, database, campaign, item, AttemptErrors.InputInvalid);
        Assert.False(harness.Logs.Mentions("what the planner wrote"));
    }

    /// <summary>
    /// The attempt that ends transient at the claim is as much an attempt that ended there as a final one, and the item
    /// going back to the queue does not make the line any less owed: nothing else would say what became of it.
    /// </summary>
    [Fact]
    public async Task A_pattern_that_could_not_finish_is_said_once_at_the_claim_and_the_item_still_comes_back()
    {
        using var harness = new Harness(
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

        await AssertSaidOnceAsync(harness, database, campaign, item, AttemptErrors.ValidationTimedOut);
        await using var fresh = database.Open();
        Assert.Equal(WorkItemStatus.Created, (await fresh.WorkItems.AsNoTracking().SingleAsync(Ct)).Status);
    }

    /// <summary>
    /// Parking is the claim's one answer that is not an end: no attempt exists, so there is no attempt to name and no
    /// line about one. The approval it waits for is where it is read.
    /// </summary>
    [Fact]
    public async Task An_item_parked_for_approval_says_nothing_about_an_attempt()
    {
        using var harness = new Harness(
            plugin: TestPlugins.Loaded("stand-in-provider", ["campaign.enroll"]),
            route: new Route("stand-in-provider", new JsonObject { ["workspace"] = "west" }, "sha256:west"));
        using var database = new TestDatabase();
        await using var db = database.Open();
        var campaign = WorkItemFactory.NewCampaign(now: Noon);
        db.Campaigns.Add(campaign);
        Enrolment(db, campaign);
        await db.SaveChangesAsync(Ct);

        Assert.Empty(await harness.Claimer.ClaimAsync(db, 4, Ct));

        Assert.DoesNotContain(harness.Logs.Lines, line => line.Scope.ContainsKey("AttemptId"));
        Assert.DoesNotContain(harness.Logs.All, message => message.Contains("ended at the claim", StringComparison.Ordinal));
    }

    /// <summary>
    /// The attempt the claim ended has exactly one line, a warning under its own campaign, work item and attempt, that
    /// says the code and nothing else the attempt recorded about why.
    /// </summary>
    private static async Task AssertSaidOnceAsync(Harness harness, TestDatabase database, Campaign campaign, WorkItem item, string code)
    {
        await using var fresh = database.Open();
        var attempt = await fresh.Attempts.AsNoTracking().SingleAsync(Ct);
        Assert.Equal(code, attempt.Error!.Code);

        var line = Assert.Single(harness.Logs.Lines, each => each.Scope.ContainsKey("AttemptId"));
        Assert.Equal(LogLevel.Warning, line.Level);
        Assert.Equal($"Attempt {attempt.PublicId} of work item {item.PublicId} ended at the claim: {code}", line.Message);
        Assert.Equal(1, harness.Logs.Count(line.Message));
        Assert.Equal(campaign.PublicId, line.Scope["CampaignId"]);
        Assert.Equal(item.PublicId, line.Scope["WorkItemId"]);
        Assert.Equal(attempt.PublicId, line.Scope["AttemptId"]);
        Assert.False(harness.Logs.Mentions(attempt.Error.Message));
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
    [Critical(Family.W6, "no double claim")]
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
    [Critical(Family.W6, "BEGIN IMMEDIATE")]
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

    private static ExternalId ContactPin(string pluginId, string value) => new()
    {
        PluginId = pluginId,
        Kind = "contact",
        Value = value,
        RecordedAt = Noon,
        RecordedByAttemptId = PublicId.New("att"),
    };

    /// <summary>A delete with nothing wrong with its input, about a person carrying the one pin given.</summary>
    private static WorkItem Deletion(JasonDbContext db, Campaign campaign, ExternalId pin)
    {
        var contact = new Contact { PublicId = PublicId.New("cnt"), FirstName = "Ada", CreatedAt = Noon, UpdatedAt = Noon };
        contact.Channels.Add(new ContactChannel { Channel = "email", Value = "ada@example.test", IsPrimary = true });
        contact.ExternalIds.Add(pin);
        db.Contacts.Add(contact);

        var item = WorkItemFactory.NewProviderOp(campaign, "contact.delete", Noon);
        item.Contact = contact;
        item.Context = new JsonObject { [WorkItemService.InputKey] = new JsonObject { ["channel"] = "email" } };
        db.WorkItems.Add(item);
        return item;
    }

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
    [Critical(Family.S2, "the gate is before the attempt")]
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
            "Enroll ada@example.test in the provider's campaign for Jason's campaign Flow: start at first_step, first touch authored_delay, "
            + "skip if they already take part. If the provider's campaign is live, its messages start going to this person on that timing.",
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
    [Critical(Family.W3, "a refusal keeps the attempt")]
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
    [Critical(Family.S2, "parked work is never claimed")]
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
    /// A delete works by the routed plugin's pin alone, so a person with none is refused at the claim, before the gate,
    /// and that holds for an item a person has already approved: a decision about a delete nobody can be found for does
    /// not make it one that can run. The item ends with its attempt kept, nothing is handed out, and the decision stays
    /// as the person made it.
    /// </summary>
    [Fact]
    public async Task An_approved_delete_for_a_person_with_no_pin_ends_at_the_claim_and_keeps_its_attempt()
    {
        using var harness = new Harness(
            plugin: TestPlugins.Loaded("stand-in-provider", ["contact.delete"]),
            route: new Route("stand-in-provider", null, null));
        using var database = new TestDatabase();
        await using var db = database.Open();
        var campaign = WorkItemFactory.NewCampaign(now: Noon);
        db.Campaigns.Add(campaign);
        var item = Deletion(db, campaign, ContactPin("stand-in-provider", "48211"));
        await db.SaveChangesAsync(Ct);
        Assert.Empty(await harness.Claimer.ClaimAsync(db, 4, Ct));
        var approval = await Decide(db, ApprovalStatus.Approved);

        // The pin the decision was made about is gone by the time the item is claimed again, which is the state of
        // an item a person approved before the claim asked about the pin at all.
        await db.Database.ExecuteSqlRawAsync("DELETE FROM external_ids", Ct);
        db.ChangeTracker.Clear();

        Assert.Empty(await harness.Claimer.ClaimAsync(db, 4, Ct));

        await using var fresh = database.Open();
        var attempt = Assert.Single(await fresh.Attempts.AsNoTracking().ToListAsync(Ct));
        Assert.Equal(item.Id, attempt.WorkItemId);
        Assert.Equal(AttemptStatus.Failed, attempt.Status);
        Assert.Equal(AttemptErrors.ContactNotLinked, attempt.Error!.Code);
        Assert.Equal(FailureClass.Permanent, attempt.Error.Class);
        Assert.Equal("stand-in-provider", attempt.Provenance!.PluginId);

        // The refusal comes before the gate, so no decision releases this attempt and its provenance names none: the
        // person's decision stays on the approval, and nothing ran under it.
        Assert.Null(attempt.Provenance.ApprovalId);
        Assert.Equal("email", (string?)attempt.ContextSnapshot[WorkItemService.InputKey]!["channel"]);
        Assert.Null(attempt.Launch);
        var failed = await fresh.WorkItems.AsNoTracking().SingleAsync(Ct);
        Assert.Equal(WorkItemStatus.Failed, failed.Status);
        var decided = await fresh.Approvals.AsNoTracking().SingleAsync(Ct);
        Assert.Equal((approval.PublicId, ApprovalStatus.Approved), (decided.PublicId, decided.Status));
    }

    /// <summary>
    /// What another plugin calls the person is not the routed plugin's pin, so a person pinned only there is refused
    /// the same way, at the first claim: no approval is asked for, and the attempt says why.
    /// </summary>
    [Fact]
    public async Task A_delete_for_a_person_pinned_only_at_another_plugin_is_refused_and_never_parked()
    {
        using var harness = new Harness(
            plugin: TestPlugins.Loaded("stand-in-provider", ["contact.delete"]),
            route: new Route("stand-in-provider", null, null));
        using var database = new TestDatabase();
        await using var db = database.Open();
        var campaign = WorkItemFactory.NewCampaign(now: Noon);
        db.Campaigns.Add(campaign);
        Deletion(db, campaign, ContactPin("other-provider", "OTHER-1"));
        await db.SaveChangesAsync(Ct);

        Assert.Empty(await harness.Claimer.ClaimAsync(db, 4, Ct));

        await using var fresh = database.Open();
        Assert.Empty(await fresh.Approvals.AsNoTracking().ToListAsync(Ct));
        var attempt = Assert.Single(await fresh.Attempts.AsNoTracking().ToListAsync(Ct));
        Assert.Equal(AttemptErrors.ContactNotLinked, attempt.Error!.Code);
        Assert.Equal(WorkItemStatus.Failed, (await fresh.WorkItems.AsNoTracking().SingleAsync(Ct)).Status);
    }

    /// <summary>
    /// A pin a later answer disputed is still the pin: the original value stands, and it is what the person approving
    /// is shown. The delete is parked for that decision, exactly as for a pin nobody disputed.
    /// </summary>
    [Fact]
    public async Task A_delete_for_a_person_whose_pin_diverged_is_parked_for_a_decision_about_the_pin()
    {
        using var harness = new Harness(
            plugin: TestPlugins.Loaded("stand-in-provider", ["contact.delete"]),
            route: new Route("stand-in-provider", null, null));
        using var database = new TestDatabase();
        await using var db = database.Open();
        var campaign = WorkItemFactory.NewCampaign(now: Noon);
        db.Campaigns.Add(campaign);
        var pin = ContactPin("stand-in-provider", "48211");
        pin.DivergedValue = "59322";
        pin.DivergedAt = Noon;
        pin.DivergedByAttemptId = PublicId.New("att");
        Deletion(db, campaign, pin);
        await db.SaveChangesAsync(Ct);

        Assert.Empty(await harness.Claimer.ClaimAsync(db, 4, Ct));

        await using var fresh = database.Open();
        Assert.Empty(await fresh.Attempts.AsNoTracking().ToListAsync(Ct));
        Assert.Equal(WorkItemStatus.AwaitingApproval, (await fresh.WorkItems.AsNoTracking().SingleAsync(Ct)).Status);
        var approval = await fresh.Approvals.AsNoTracking().SingleAsync(Ct);
        Assert.Equal("48211", (string?)approval.Subject["input"]!["contacts"]![0]!["external_ids"]!["contact"]);
    }

    /// <summary>
    /// The decision is about a subject, not about an item. An input edited after a person approved it is a
    /// different subject, so the hash no longer matches and the work is parked again — with the reason saying
    /// which of the two things happened — rather than running under a decision nobody made about it.
    /// </summary>
    [Fact]
    [Critical(Family.S2, "a changed input needs a new approval")]
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
    /// What a person approved is what the plugin receives, and the pins the person carries are part of it. A pin learned
    /// by the item's own earlier attempt is such a change as much as an edit is: the next claim composes the input with
    /// it, so the same decision no longer matches and the person decides once more on what the plugin now receives.
    /// </summary>
    [Fact]
    public async Task A_pin_learned_by_an_earlier_attempt_parks_the_item_again_as_a_changed_input()
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
        var work = Assert.Single(await harness.Claimer.ClaimAsync(db, 4, Ct));
        Assert.Equal(item.PublicId, work.WorkItemPublicId);

        // The attempt found the person at the provider, pinned them, and failed in a way that is tried again.
        var attempt = await db.Attempts.SingleAsync(Ct);
        attempt.Status = AttemptStatus.Failed;
        item.Status = WorkItemStatus.Created;
        item.Contact!.ExternalIds.Add(ContactPin("stand-in-provider", "48211"));
        await db.SaveChangesAsync(Ct);

        Assert.Empty(await harness.Claimer.ClaimAsync(db, 4, Ct));

        await using var fresh = database.Open();
        Assert.Equal(WorkItemStatus.AwaitingApproval, (await fresh.WorkItems.AsNoTracking().SingleAsync(Ct)).Status);
        Assert.Single(await fresh.Attempts.AsNoTracking().ToListAsync(Ct));
        var approvals = await fresh.Approvals.AsNoTracking().OrderBy(a => a.Id).ToListAsync(Ct);
        Assert.Equal([ApprovalStatus.Superseded, ApprovalStatus.Pending], approvals.Select(a => a.Status));
        Assert.Equal(first.PublicId, approvals[0].PublicId);
        Assert.Equal("input_changed", approvals[1].Reason);
        Assert.Equal("48211", (string?)approvals[1].Subject["input"]!["contacts"]![0]!["external_ids"]!["contact"]);
    }

    /// <summary>
    /// An approval given before the composed input carried the person's name, company, title and time zone was given about
    /// a document that is no longer the one a plugin would receive. The hash of what would run now is not the hash the
    /// person approved, so the work is asked about again, with the old decision kept as the one a person made, rather than
    /// run under an approval of a subject nobody can show it matches.
    /// </summary>
    [Fact]
    [Critical(Family.S2, "an approval given on an older subject is asked again")]
    public async Task An_approval_made_on_the_older_subject_without_the_person_s_fields_is_asked_again_as_input_changed()
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
        Assert.Empty(await harness.Claimer.ClaimAsync(db, 4, Ct));
        var approved = await Decide(db, ApprovalStatus.Approved);

        // The same subject as it was written before the person's fields were composed into it: every other part of it, the
        // item, the plugin, the arguments and the person's id and channel, is as it was, and the hash is of that document.
        string[] added = ["first_name", "last_name", "company", "title", "time_zone"];
        var older = (JsonObject)approved.Subject.DeepClone();
        var person = (JsonObject)older["input"]!["contacts"]![0]!;
        Assert.True(added.All(person.ContainsKey), $"The person in the subject does not carry the fields: {person.ToJsonString()}");
        foreach (var field in added)
        {
            person.Remove(field);
        }

        approved.Subject = older;
        approved.SubjectHash = CanonicalJson.Hash(older);
        await db.SaveChangesAsync(Ct);

        Assert.Empty(await harness.Claimer.ClaimAsync(db, 4, Ct));

        await using var fresh = database.Open();
        Assert.Equal(WorkItemStatus.AwaitingApproval, (await fresh.WorkItems.AsNoTracking().SingleAsync(Ct)).Status);
        Assert.Empty(await fresh.Attempts.AsNoTracking().ToListAsync(Ct));
        var approvals = await fresh.Approvals.AsNoTracking().OrderBy(a => a.Id).ToListAsync(Ct);
        Assert.Equal(2, approvals.Count);
        Assert.Equal((approved.PublicId, ApprovalStatus.Superseded), (approvals[0].PublicId, approvals[0].Status));
        Assert.Equal((ActorType.Human, "operator", "go ahead"), (approvals[0].DecidedByType, approvals[0].DecidedById, approvals[0].DecisionReason));
        Assert.Equal((ApprovalStatus.Pending, "input_changed"), (approvals[1].Status, approvals[1].Reason));
        Assert.NotEqual(approvals[0].SubjectHash, approvals[1].SubjectHash);
        Assert.True(added.All(((JsonObject)approvals[1].Subject["input"]!["contacts"]![0]!).ContainsKey), "The new question's subject lacks the person's fields.");
    }

    /// <summary>
    /// `confirm_each`: one decision per item, each its own act against a named person. A reply in a thread is parked
    /// before anything exists that could send it, with the line a person reads naming who it reaches and carrying the
    /// whole text; the decision is bound to the composed input that text is part of, and it releases that reply once.
    /// </summary>
    [Fact]
    public async Task A_reply_is_parked_for_its_own_decision_and_runs_under_it_once_a_person_approves()
    {
        using var harness = new Harness(
            plugin: TestPlugins.Loaded("stand-in-provider", ["conversation.reply"]),
            route: new Route("stand-in-provider", null, null));
        using var database = new TestDatabase();
        await using var db = database.Open();
        var campaign = WorkItemFactory.NewCampaign(now: Noon);
        db.Campaigns.Add(campaign);
        var item = Reply(db, campaign, "Thursday at 10 works.");
        await db.SaveChangesAsync(Ct);

        Assert.Empty(await harness.Claimer.ClaimAsync(db, 4, Ct));

        await using (var parked = database.Open())
        {
            Assert.Equal(WorkItemStatus.AwaitingApproval, (await parked.WorkItems.AsNoTracking().SingleAsync(Ct)).Status);
            Assert.Empty(await parked.Attempts.AsNoTracking().ToListAsync(Ct));
            var waiting = await parked.Approvals.AsNoTracking().SingleAsync(Ct);
            Assert.Equal(("conversation.reply", "approval_required"), (waiting.Operation, waiting.Reason));
            Assert.Equal(
                "Reply in the provider's thread 501 to ada@example.test by email, from the account that owns the thread and to nobody else: Thursday at 10 works.",
                (string?)waiting.Preview["summary"]);
            Assert.Equal("Thursday at 10 works.", (string?)waiting.Subject["input"]!["args"]!["body"]);
        }

        var approval = await Decide(db, ApprovalStatus.Approved);

        var work = Assert.Single(await harness.Claimer.ClaimAsync(db, 4, Ct));
        Assert.Equal((item.PublicId, "conversation.reply"), (work.WorkItemPublicId, work.Plan!.Contract.Id));
        await using var fresh = database.Open();
        Assert.Equal(approval.PublicId, Assert.Single(await fresh.Attempts.AsNoTracking().ToListAsync(Ct)).Provenance!.ApprovalId);
    }

    /// <summary>
    /// The text is what a person approved. A reply whose text is edited after the decision is another reply, so it
    /// is parked again for a decision of its own and nothing runs under the first one.
    /// </summary>
    [Fact]
    [Critical(Family.S2, "confirm_each: a changed text needs a new word")]
    public async Task A_reply_whose_text_changed_after_the_decision_is_parked_for_a_new_one()
    {
        using var harness = new Harness(
            plugin: TestPlugins.Loaded("stand-in-provider", ["conversation.reply"]),
            route: new Route("stand-in-provider", null, null));
        using var database = new TestDatabase();
        await using var db = database.Open();
        var campaign = WorkItemFactory.NewCampaign(now: Noon);
        db.Campaigns.Add(campaign);
        var item = Reply(db, campaign, "Thursday at 10 works.");
        await db.SaveChangesAsync(Ct);
        Assert.Empty(await harness.Claimer.ClaimAsync(db, 4, Ct));
        await Decide(db, ApprovalStatus.Approved);

        item.Status = WorkItemStatus.Created;
        item.Context = new JsonObject { [WorkItemService.InputKey] = Replying("Thursday at 11 works.") };
        await db.SaveChangesAsync(Ct);

        Assert.Empty(await harness.Claimer.ClaimAsync(db, 4, Ct));

        await using var fresh = database.Open();
        Assert.Empty(await fresh.Attempts.AsNoTracking().ToListAsync(Ct));
        var approvals = await fresh.Approvals.AsNoTracking().OrderBy(a => a.Id).ToListAsync(Ct);
        Assert.Equal([ApprovalStatus.Superseded, ApprovalStatus.Pending], approvals.Select(a => a.Status));
        Assert.Equal("input_changed", approvals[1].Reason);
        Assert.Equal("Thursday at 11 works.", (string?)approvals[1].Subject["input"]!["args"]!["body"]);
    }

    /// <summary>A reply in a thread with nothing wrong with it, so the only thing that can stop it is a person.</summary>
    private static WorkItem Reply(JasonDbContext db, Campaign campaign, string body)
    {
        var contact = new Contact { PublicId = PublicId.New("cnt"), FirstName = "Ada", CreatedAt = Noon, UpdatedAt = Noon };
        contact.Channels.Add(new ContactChannel { Channel = "email", Value = "ada@example.test", IsPrimary = true });
        db.Contacts.Add(contact);

        var item = WorkItemFactory.NewProviderOp(campaign, "conversation.reply", Noon);
        item.Contact = contact;
        item.Context = new JsonObject { [WorkItemService.InputKey] = Replying(body) };
        db.WorkItems.Add(item);
        return item;
    }

    private static JsonObject Replying(string body) => new()
    {
        ["channel"] = "email",
        ["thread"] = new JsonObject { ["external_id"] = "501" },
        ["body"] = body,
    };

    /// <summary>
    /// A person's decision, exactly as the API verb will make it: the row is decided and the work it was holding
    /// is let back into the queue. These tests are about what the claim does with that, not about the verb.
    /// </summary>
    /// <summary>
    /// The twenty-fifth check parks protective work an open block holds: no attempt, a wait on the block, and the campaign
    /// told once — whichever of its items parks next, the line is not written again, and the block's link is never in it.
    /// </summary>
    [Fact]
    public async Task Protective_work_an_open_block_holds_waits_on_it_with_no_attempt_and_the_campaign_is_told_once()
    {
        using var harness = new Harness(
            plugin: TestPlugins.Loaded(BlockRows.Plugin, ["campaign.pause"]),
            route: new Route(BlockRows.Plugin, null, null));
        using var database = new TestDatabase();
        await using var db = database.Open();
        var campaign = WorkItemFactory.NewCampaign(now: Noon);
        db.Campaigns.Add(campaign);
        var block = OpenBlock(db, campaign, nextTrialAt: Noon.AddHours(1));
        block.Link = "https://billing.example.com/pay";
        var first = Pausing(db, campaign, Noon);
        await db.SaveChangesAsync(Ct);
        Assert.Empty(await harness.Claimer.ClaimAsync(db, 4, Ct));
        var second = Pausing(db, campaign, Noon.AddMinutes(1));
        await db.SaveChangesAsync(Ct);
        Assert.Empty(await harness.Claimer.ClaimAsync(db, 4, Ct));

        await using var fresh = database.Open();
        var held = new[] { first.Id, second.Id };
        var items = await fresh.WorkItems.AsNoTracking().Where(w => held.Contains(w.Id)).ToListAsync(Ct);
        Assert.True(
            items.All(w => w is { Status: WorkItemStatus.Waiting, AttemptCount: 0 }),
            $"The held items are {string.Join(", ", items.Select(w => $"{w.Status}/{w.AttemptCount}"))}.");

        // What a person reads of the block, its link included, is on each held item's last error.
        Assert.True(
            items.All(w => w.LastError is { Code: AttemptErrors.ProviderBlocked, Class: FailureClass.Blocked, Trace: null }
                && (string?)w.LastError.Block?["link"] == "https://billing.example.com/pay" && (string?)w.LastError.Block?["reason"] == "billing"),
            $"The held items' last errors are {string.Join(" / ", items.Select(w => $"{w.LastError?.Code} {w.LastError?.Block?.ToJsonString()}"))}.");
        Assert.Empty(await fresh.Attempts.AsNoTracking().Where(a => held.Contains(a.WorkItemId)).ToListAsync(Ct));
        var waits = await fresh.WorkItemWaits.AsNoTracking().Where(w => held.Contains(w.WorkItemId)).ToListAsync(Ct);
        Assert.True(
            waits.Count == 2 && waits.All(w => w.ProviderBlockId == block.Id && w.Status == WaitStatus.Open),
            $"The waits are {string.Join(", ", waits.Select(w => $"{w.ProviderBlockId}/{w.Status}"))}.");

        var said = Assert.Single(await fresh.Journal.AsNoTracking().Where(e => e.Kind == JournalKinds.ProviderBlocked).ToListAsync(Ct));
        Assert.Equal((block.PublicId, campaign.Id), (said.Key, said.CampaignId));
        Assert.Equal(first.PublicId, (string?)said.New!["work_item_id"]);
        Assert.DoesNotContain("billing.example.com", said.New.ToJsonString(), StringComparison.Ordinal);
    }

    /// <summary>
    /// Work a person approved is still held by a block of its account: the approval releases it, the block parks it, and
    /// the decision stays as the person made it, so the item runs the moment the block clears.
    /// </summary>
    [Fact]
    public async Task Work_a_person_released_still_waits_on_the_block_that_holds_its_account()
    {
        using var harness = new Harness(
            plugin: TestPlugins.Loaded(BlockRows.Plugin, ["campaign.enroll"]),
            route: new Route(BlockRows.Plugin, null, null));
        using var database = new TestDatabase();
        await using var db = database.Open();
        var campaign = WorkItemFactory.NewCampaign(now: Noon);
        db.Campaigns.Add(campaign);
        var item = Enrolment(db, campaign);
        await db.SaveChangesAsync(Ct);
        Assert.Empty(await harness.Claimer.ClaimAsync(db, 4, Ct));
        await Decide(db, ApprovalStatus.Approved);
        var block = OpenBlock(db, campaign, nextTrialAt: Noon.AddHours(1));
        await db.SaveChangesAsync(Ct);

        Assert.Empty(await harness.Claimer.ClaimAsync(db, 4, Ct));

        await using var fresh = database.Open();
        var parked = await fresh.WorkItems.AsNoTracking().SingleAsync(w => w.Id == item.Id, Ct);
        Assert.Equal((WorkItemStatus.Waiting, 0), (parked.Status, parked.AttemptCount));
        Assert.Empty(await fresh.Attempts.AsNoTracking().Where(a => a.WorkItemId == item.Id).ToListAsync(Ct));
        Assert.Equal(block.Id, (await fresh.WorkItemWaits.AsNoTracking().SingleAsync(w => w.WorkItemId == item.Id, Ct)).ProviderBlockId);
        Assert.Equal(ApprovalStatus.Approved, (await fresh.Approvals.AsNoTracking().SingleAsync(Ct)).Status);
        Assert.Equal(block.PublicId, Assert.Single(await fresh.Journal.AsNoTracking().Where(e => e.Kind == JournalKinds.ProviderBlocked).ToListAsync(Ct)).Key);
    }

    /// <summary>A read runs through an open block of its account, and the block is neither tried nor touched by it.</summary>
    [Fact]
    public async Task A_read_runs_through_an_open_block_and_leaves_it_as_it_was()
    {
        using var harness = new Harness(
            plugin: TestPlugins.Loaded(BlockRows.Plugin, ["campaign.get"]),
            route: new Route(BlockRows.Plugin, null, null));
        using var database = new TestDatabase();
        await using var db = database.Open();
        var campaign = WorkItemFactory.NewCampaign(now: Noon);
        db.Campaigns.Add(campaign);
        var block = OpenBlock(db, campaign, nextTrialAt: Noon);
        var item = WorkItemFactory.NewProviderOp(campaign, now: Noon);
        item.Context = new JsonObject
        {
            [WorkItemService.InputKey] = new JsonObject { ["campaign"] = new JsonObject { ["external_id"] = "c-7714" } },
        };
        db.WorkItems.Add(item);
        await db.SaveChangesAsync(Ct);

        var work = Assert.Single(await harness.Claimer.ClaimAsync(db, 4, Ct));

        Assert.Equal(item.PublicId, work.WorkItemPublicId);
        await using var fresh = database.Open();
        var row = await fresh.ProviderBlocks.AsNoTracking().SingleAsync(b => b.Id == block.Id, Ct);
        Assert.Equal((ProviderBlockStatus.Open, Noon), (row.Status, row.NextTrialAt));
        Assert.Null(row.TrialWorkItemId);
        Assert.Empty(await fresh.Journal.AsNoTracking().Where(e => e.Kind == JournalKinds.ProviderBlocked).ToListAsync(Ct));
    }

    /// <summary>
    /// With nothing parked, a block whose trial is due lets the next item it holds through at the claim as its trial, and
    /// only that one: another item the block holds, in the same scan, parks while the trial is in flight.
    /// </summary>
    [Fact]
    public async Task A_due_trial_runs_the_first_item_the_block_holds_and_parks_the_next_while_it_is_in_flight()
    {
        using var harness = new Harness(
            plugin: TestPlugins.Loaded(BlockRows.Plugin, ["campaign.pause"]),
            route: new Route(BlockRows.Plugin, null, null));
        using var database = new TestDatabase();
        await using var db = database.Open();
        var west = WorkItemFactory.NewCampaign(now: Noon);
        var east = WorkItemFactory.NewCampaign(now: Noon);
        db.Campaigns.AddRange(west, east);
        var block = OpenBlock(db, west, nextTrialAt: Noon);
        var trial = Pausing(db, west, Noon);
        var next = Pausing(db, east, Noon.AddMinutes(1));
        await db.SaveChangesAsync(Ct);

        var work = Assert.Single(await harness.Claimer.ClaimAsync(db, 4, Ct));

        Assert.Equal(trial.PublicId, work.WorkItemPublicId);
        await using var fresh = database.Open();
        Assert.Equal(trial.Id, (await fresh.ProviderBlocks.AsNoTracking().SingleAsync(b => b.Id == block.Id, Ct)).TrialWorkItemId);
        Assert.Equal(WorkItemStatus.Scheduled, (await fresh.WorkItems.AsNoTracking().SingleAsync(w => w.Id == trial.Id, Ct)).Status);
        Assert.Equal(WorkItemStatus.Waiting, (await fresh.WorkItems.AsNoTracking().SingleAsync(w => w.Id == next.Id, Ct)).Status);
        Assert.Equal(block.Id, (await fresh.WorkItemWaits.AsNoTracking().SingleAsync(w => w.WorkItemId == next.Id, Ct)).ProviderBlockId);
    }

    /// <summary>An open account block of the stand-in plugin's routes that bind nothing, opened by a failed item of <paramref name="campaign"/>.</summary>
    private static ProviderBlock OpenBlock(JasonDbContext db, Campaign campaign, DateTime nextTrialAt)
    {
        var opener = WorkItemFactory.NewProviderOp(campaign, "message.send", Noon);
        opener.Status = WorkItemStatus.Failed;
        opener.FinishedAt = Noon;
        db.WorkItems.Add(opener);
        var block = BlockRows.Block(opener, Noon, nextTrialAt, binding: EventSource.NoBinding);
        db.ProviderBlocks.Add(block);
        return block;
    }

    /// <summary>A pause of <paramref name="campaign"/>'s provider campaign, which the planner names: protective work.</summary>
    private static WorkItem Pausing(JasonDbContext db, Campaign campaign, DateTime createdAt)
    {
        var item = WorkItemFactory.NewProviderOp(campaign, "campaign.pause", createdAt);
        item.Context = new JsonObject
        {
            [WorkItemService.InputKey] = new JsonObject { ["campaign"] = new JsonObject { ["external_id"] = "sq-1129" } },
        };
        db.WorkItems.Add(item);
        return item;
    }

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
    /// A send an earlier attempt of which lost its answer is claimed again only under a plugin whose manifest lists it
    /// under <c>settles_by_key</c>. Under any other the claim refuses it: the attempt is made and kept with the reason, it
    /// ends ambiguous and final, and nothing is handed out; under the plugin that lists it the repeat is claimed. The
    /// key settles the send only at the account that took it, so a route moved to another account of the same plugin
    /// since the attempt that lost its answer is refused the same way. The send stays unsettled while any earlier
    /// attempt ended ambiguous, so a transient attempt after it, made under the route of today, changes neither answer. And
    /// the key settles only the request it went with, so an item whose arguments were edited since is refused too. An
    /// attempt the claim refused never started, so it ran nothing: one after the lost answer, under another account and
    /// ended ambiguous, is not the attempt a repeat is held to.
    /// </summary>
    [Theory]
    [InlineData(false, null, "settles_by_key")]
    [InlineData(true, null, null)]
    [InlineData(true, "acct-2", "only at the provider and the account that took it")]
    [InlineData(true, null, null, true)]
    [InlineData(true, "acct-2", "only at the provider and the account that took it", true)]
    [InlineData(true, null, "other arguments than the work item carries now", false, true)]
    [InlineData(true, null, null, false, false, true)]
    public async Task A_send_that_lost_its_answer_is_claimed_again_only_under_a_plugin_that_settles_it_by_the_key(bool listed, string? ranUnder, string? refusal, bool thenTransient = false, bool edited = false, bool thenRefused = false)
    {
        var plugin = TestPlugins.Loaded("stand-in-provider", ["message.send"]);
        if (listed)
        {
            plugin = plugin with { Manifest = plugin.Manifest with { KeyedOperations = ["message.send"] } };
        }

        using var harness = new Harness(plugin: plugin, route: new Route("stand-in-provider", null, null));
        using var database = new TestDatabase();
        await using var db = database.Open();
        var campaign = WorkItemFactory.NewCampaign(now: Noon);
        db.Campaigns.Add(campaign);
        var contact = new Contact { PublicId = PublicId.New("cnt"), FirstName = "Ada", CreatedAt = Noon, UpdatedAt = Noon };
        contact.Channels.Add(new ContactChannel { Channel = "email", Value = "ada@example.test", IsPrimary = true });
        db.Contacts.Add(contact);
        var item = WorkItemFactory.NewProviderOp(campaign, "message.send", Noon);
        item.Contact = contact;
        item.Context = new JsonObject
        {
            [WorkItemService.InputKey] = new JsonObject
            {
                ["channel"] = "email",
                ["sender"] = new JsonObject { ["external_id"] = "snd-1", ["address"] = "marta@example.com" },
                ["message"] = new JsonObject { ["format"] = "email", ["subject"] = "Hello", ["body"] = "Hello, Ada." },
            },
        };
        db.WorkItems.Add(item);
        var lost = WorkItemFactory.NewAttempt(item, 1, AttemptStatus.Failed, Noon.AddHours(-1));
        lost.StartedAt = Noon.AddHours(-1);
        lost.Error = new AttemptErrorDto("provider_answer_lost", "No answer came back.", Retriable: true, Class: FailureClass.Ambiguous);
        lost.Provenance = new AttemptProvenanceDto("stand-in-provider", "1.0.0", plugin.Digest, 1, 1, "message.send", 1, null, null, RouteScope.GlobalDefault, ranUnder);
        db.Attempts.Add(lost);
        item.AttemptCount = 1;
        if (edited)
        {
            item.Context = new JsonObject
            {
                [WorkItemService.InputKey] = new JsonObject
                {
                    ["channel"] = "email",
                    ["sender"] = new JsonObject { ["external_id"] = "snd-1", ["address"] = "marta@example.com" },
                    ["message"] = new JsonObject { ["format"] = "email", ["subject"] = "Hello again", ["body"] = "Hello, Ada." },
                },
            };
        }

        if (thenTransient)
        {
            var transient = WorkItemFactory.NewAttempt(item, 2, AttemptStatus.Failed, Noon.AddMinutes(-30));
            transient.Error = new AttemptErrorDto("provider_unavailable", "The provider did not answer in time.", Retriable: true, Class: FailureClass.Transient);
            transient.Provenance = lost.Provenance with { BindingIdentity = null };
            db.Attempts.Add(transient);
            item.AttemptCount = 2;
        }

        if (thenRefused)
        {
            // Refused at its claim while the route bound another account, and so ended ambiguous: it never started.
            var refused = WorkItemFactory.NewAttempt(item, 2, AttemptStatus.Failed, Noon.AddMinutes(-30));
            refused.Error = new AttemptErrorDto("provider_answer_lost", "The key settles the send only at the account that took it.", Retriable: false, Class: FailureClass.Ambiguous);
            refused.Provenance = lost.Provenance with { BindingIdentity = "acct-2" };
            db.Attempts.Add(refused);
            item.AttemptCount = 2;
        }

        await db.SaveChangesAsync(Ct);

        var claimed = await harness.Claimer.ClaimAsync(db, 4, Ct);

        Assert.Empty(claimed);
        if (refusal is null)
        {
            // The send is approved per item, so past the refusal the claim parks it for a person and makes no attempt.
            Assert.NotEqual(WorkItemStatus.Failed, item.Status);
            Assert.Equal(thenTransient || thenRefused ? 2 : 1, await db.Attempts.AsNoTracking().CountAsync(Ct));
            return;
        }

        Assert.Equal(WorkItemStatus.Failed, item.Status);
        var kept = thenTransient ? 3 : 2;
        var attempt = await db.Attempts.AsNoTracking().SingleAsync(a => a.Number == kept, Ct);
        Assert.Equal((AttemptErrors.ProviderAnswerLost, FailureClass.Ambiguous, false), (attempt.Error!.Code, attempt.Error.Class, attempt.Error.Retriable));
        Assert.Contains(refusal, attempt.Error.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// The claim names the input it composed on the attempt it makes, and a repeat of that attempt after a lost answer is
    /// claimed only with an input of the same name. The person's address changed in between is another request, refused at
    /// the claim, ambiguous and final, with the attempt kept, and so is a pin of the person another attempt recorded since. A
    /// keyed send is claimed only in an active campaign, so a campaign paused and resumed in between is active at both
    /// claims, and the pause leaves nothing else in the input: the repeat runs under the decision the person already made.
    /// (That the status itself is left out of the name is held where the check is.) A pin of the person or of the campaign
    /// the lost attempt learned from its own answer is the same request too, though a pin is part of what the person
    /// approves: they are asked again, and the repeat then runs, named with the pin it now carries. That repeat losing its
    /// answer too is repeated in turn, since its claim named the input with the pin already in it.
    /// </summary>
    [Theory]
    [InlineData("nothing", false)]
    [InlineData("the campaign paused and resumed, active at both claims", false)]
    [InlineData("a pin the lost attempt learned", false)]
    [InlineData("a campaign pin the lost attempt learned", false)]
    [InlineData("two answers lost in a row", false)]
    [InlineData("another address", true)]
    [InlineData("a pin another attempt recorded", true)]
    public async Task A_send_that_lost_its_answer_is_claimed_again_only_with_the_input_its_claim_composed(string since, bool refused)
    {
        var plugin = TestPlugins.Loaded("stand-in-provider", ["message.send"]);
        plugin = plugin with { Manifest = plugin.Manifest with { KeyedOperations = ["message.send"] } };
        using var harness = new Harness(plugin: plugin, route: new Route("stand-in-provider", null, null));
        using var database = new TestDatabase();
        await using var db = database.Open();
        var campaign = WorkItemFactory.NewCampaign(now: Noon);
        db.Campaigns.Add(campaign);
        var contact = new Contact { PublicId = PublicId.New("cnt"), FirstName = "Ada", CreatedAt = Noon, UpdatedAt = Noon };
        contact.Channels.Add(new ContactChannel { Channel = "email", Value = "ada@example.test", IsPrimary = true });
        db.Contacts.Add(contact);
        var item = WorkItemFactory.NewProviderOp(campaign, "message.send", Noon);
        item.Contact = contact;
        item.Context = new JsonObject
        {
            [WorkItemService.InputKey] = new JsonObject
            {
                ["channel"] = "email",
                ["sender"] = new JsonObject { ["external_id"] = "snd-1", ["address"] = "marta@example.com" },
                ["message"] = new JsonObject { ["format"] = "email", ["subject"] = "Hello", ["body"] = "Hello, Ada." },
            },
        };
        db.WorkItems.Add(item);
        await db.SaveChangesAsync(Ct);
        Assert.Empty(await harness.Claimer.ClaimAsync(db, 4, Ct));
        await Decide(db, ApprovalStatus.Approved);
        var first = Assert.Single(await harness.Claimer.ClaimAsync(db, 4, Ct));
        var lost = await db.Attempts.SingleAsync(Ct);
        Assert.Equal(CanonicalInput.Digest(first.Plan!.Input), lost.Provenance!.InputDigest);

        // The attempt started and its answer never came back.
        WorkItemTransitions.Apply(item, WorkItemStatus.Processing, Noon);
        lost.StartedAt = Noon;
        lost.Status = AttemptStatus.Failed;
        lost.FinishedAt = Noon;
        lost.Error = new AttemptErrorDto("provider_answer_lost", "No answer came back.", Retriable: true, Class: FailureClass.Ambiguous);
        WorkItemTransitions.Apply(item, WorkItemStatus.Created, Noon);
        await db.SaveChangesAsync(Ct);
        switch (since)
        {
            case "the campaign paused and resumed, active at both claims":
                campaign.Status = CampaignStatus.Paused;
                await db.SaveChangesAsync(Ct);
                campaign.Status = CampaignStatus.Active;
                break;
            case "another address":
                contact.Channels.Single().Value = "ada@another.example.test";
                break;
            case "a pin the lost attempt learned" or "two answers lost in a row" or "a pin another attempt recorded":
                contact.ExternalIds.Add(new ExternalId
                {
                    PluginId = "stand-in-provider",
                    Kind = "contact",
                    Value = "p-88421",
                    RecordedAt = Noon,
                    RecordedByAttemptId = since == "a pin another attempt recorded" ? PublicId.New("att") : lost.PublicId,
                });
                break;
            case "a campaign pin the lost attempt learned":
                campaign.ExternalIds.Add(new ExternalId
                {
                    PluginId = "stand-in-provider",
                    Kind = "campaign",
                    Value = "c-7714",
                    RecordedAt = Noon,
                    RecordedByAttemptId = lost.PublicId,
                });
                break;
        }

        await db.SaveChangesAsync(Ct);

        var claimed = await harness.Claimer.ClaimAsync(db, 4, Ct);
        var learned = since is "a pin the lost attempt learned" or "a campaign pin the lost attempt learned" or "two answers lost in a row";
        if (learned)
        {
            // The pin is in what the person is asked to approve, so the repeat the key settles asks them again first.
            Assert.Empty(claimed);
            Assert.Equal(WorkItemStatus.AwaitingApproval, item.Status);
            await Decide(db, ApprovalStatus.Approved);
            claimed = await harness.Claimer.ClaimAsync(db, 4, Ct);
        }

        await using var fresh = database.Open();
        var repeat = await fresh.Attempts.AsNoTracking().SingleAsync(a => a.Number == 2, Ct);
        if (!refused)
        {
            Assert.Equal(item.PublicId, Assert.Single(claimed).WorkItemPublicId);
            Assert.Equal(
                learned ? CanonicalInput.Digest(Assert.Single(claimed).Plan!.Input) : lost.Provenance.InputDigest,
                repeat.Provenance!.InputDigest);
            if (since == "two answers lost in a row")
            {
                // The repeat lost its answer as well, and learned nothing new: the third claim is named as the second was.
                var second = await db.Attempts.SingleAsync(a => a.Number == 2, Ct);
                WorkItemTransitions.Apply(item, WorkItemStatus.Processing, Noon);
                second.StartedAt = Noon;
                second.Status = AttemptStatus.Failed;
                second.FinishedAt = Noon;
                second.Error = new AttemptErrorDto("provider_answer_lost", "No answer came back.", Retriable: true, Class: FailureClass.Ambiguous);
                WorkItemTransitions.Apply(item, WorkItemStatus.Created, Noon);
                await db.SaveChangesAsync(Ct);

                Assert.Equal(item.PublicId, Assert.Single(await harness.Claimer.ClaimAsync(db, 4, Ct)).WorkItemPublicId);
                await using var after = database.Open();
                var third = await after.Attempts.AsNoTracking().SingleAsync(a => a.Number == 3, Ct);
                Assert.Equal(repeat.Provenance.InputDigest, third.Provenance!.InputDigest);
            }

            return;
        }

        Assert.Empty(claimed);
        Assert.Equal(WorkItemStatus.Failed, (await fresh.WorkItems.AsNoTracking().SingleAsync(Ct)).Status);
        Assert.Equal((AttemptErrors.ProviderAnswerLost, FailureClass.Ambiguous, false), (repeat.Error!.Code, repeat.Error.Class, repeat.Error.Retriable));
        Assert.Contains("another input than the one composed for it now", repeat.Error.Message, StringComparison.Ordinal);
        Assert.Null(repeat.Provenance!.InputDigest);
    }

    /// <summary>
    /// A repeat is compared with the latest attempt that lost its answer, and the pins left out of the input composed now
    /// are the ones that attempt and every later attempt of the item recorded: each later one ran only because the claim
    /// found it the same request, so what it learned is the provider's answer to that request. A repeat that ended
    /// transient after learning the person's pin is repeated in turn, once the person approves the pin. A pin an attempt
    /// before the compared one recorded is not left out, and since the compared attempt was not composed with it, the
    /// repeat is refused: compared with the first lost answer instead, it would have run.
    /// </summary>
    [Theory]
    [InlineData("a later repeat that ended transient learned a pin", false)]
    [InlineData("an attempt before the compared one recorded a pin", true)]
    public async Task A_repeat_leaves_out_the_pins_the_compared_attempt_and_every_later_one_recorded_and_no_earlier_one(string history, bool refused)
    {
        var plugin = TestPlugins.Loaded("stand-in-provider", ["message.send"]);
        plugin = plugin with { Manifest = plugin.Manifest with { KeyedOperations = ["message.send"] } };
        using var harness = new Harness(plugin: plugin, route: new Route("stand-in-provider", null, null));
        using var database = new TestDatabase();
        await using var db = database.Open();
        var campaign = WorkItemFactory.NewCampaign(now: Noon);
        db.Campaigns.Add(campaign);
        var contact = new Contact { PublicId = PublicId.New("cnt"), FirstName = "Ada", CreatedAt = Noon, UpdatedAt = Noon };
        contact.Channels.Add(new ContactChannel { Channel = "email", Value = "ada@example.test", IsPrimary = true });
        db.Contacts.Add(contact);
        var item = WorkItemFactory.NewProviderOp(campaign, "message.send", Noon);
        item.Contact = contact;
        item.Context = new JsonObject
        {
            [WorkItemService.InputKey] = new JsonObject
            {
                ["channel"] = "email",
                ["sender"] = new JsonObject { ["external_id"] = "snd-1", ["address"] = "marta@example.com" },
                ["message"] = new JsonObject { ["format"] = "email", ["subject"] = "Hello", ["body"] = "Hello, Ada." },
            },
        };
        db.WorkItems.Add(item);
        await db.SaveChangesAsync(Ct);
        Assert.Empty(await harness.Claimer.ClaimAsync(db, 4, Ct));
        await Decide(db, ApprovalStatus.Approved);

        // The first attempt starts and its answer never comes back; the second is its repeat, and runs.
        Assert.Single(await harness.Claimer.ClaimAsync(db, 4, Ct));
        var first = await db.Attempts.SingleAsync(a => a.Number == 1, Ct);
        await EndAsync(first, FailureClass.Ambiguous);
        Assert.Single(await harness.Claimer.ClaimAsync(db, 4, Ct));
        var second = await db.Attempts.SingleAsync(a => a.Number == 2, Ct);
        Assert.Equal(first.Provenance!.InputDigest, second.Provenance!.InputDigest);
        if (history == "a later repeat that ended transient learned a pin")
        {
            // The provider found the person and then answered that it was too busy: the first attempt is still the one compared.
            contact.ExternalIds.Add(Pin(second));
            await EndAsync(second, FailureClass.Transient);
        }
        else
        {
            // The second answer is lost too, so it is the one compared, and a pin carries the first attempt's name. The
            // runtime never reaches this itself: a pin is written once, at the end of the attempt that learned it, a late
            // answer is fenced off, and nothing removes a pin, so an earlier attempt's pin is always in the compared input.
            // The row stands for a pin under an earlier attempt's name that the compared input lacks, as after a hand edit
            // of the database, a restore or a pin unlinked some day, and it is the row that tells the compared attempt, the
            // latest, from the first.
            await EndAsync(second, FailureClass.Ambiguous);
            contact.ExternalIds.Add(Pin(first));
            await db.SaveChangesAsync(Ct);
        }

        var claimed = await harness.Claimer.ClaimAsync(db, 4, Ct);
        await using var fresh = database.Open();
        if (!refused)
        {
            // The pin is in what the person is asked to approve, so the repeat asks them again first.
            Assert.Empty(claimed);
            Assert.Equal(WorkItemStatus.AwaitingApproval, item.Status);
            await Decide(db, ApprovalStatus.Approved);
            var repeat = Assert.Single(await harness.Claimer.ClaimAsync(db, 4, Ct));
            var third = await fresh.Attempts.AsNoTracking().SingleAsync(a => a.Number == 3, Ct);
            Assert.Equal(CanonicalInput.Digest(repeat.Plan!.Input), third.Provenance!.InputDigest);
            return;
        }

        Assert.Empty(claimed);
        Assert.Equal(WorkItemStatus.Failed, (await fresh.WorkItems.AsNoTracking().SingleAsync(Ct)).Status);
        var refusal = await fresh.Attempts.AsNoTracking().SingleAsync(a => a.Number == 3, Ct);
        Assert.Equal((AttemptErrors.ProviderAnswerLost, FailureClass.Ambiguous, false), (refusal.Error!.Code, refusal.Error.Class, refusal.Error.Retriable));
        Assert.Contains("another input than the one composed for it now", refusal.Error.Message, StringComparison.Ordinal);

        async Task EndAsync(Attempt attempt, FailureClass ending)
        {
            WorkItemTransitions.Apply(item, WorkItemStatus.Processing, Noon);
            attempt.StartedAt = Noon;
            attempt.Status = AttemptStatus.Failed;
            attempt.FinishedAt = Noon;
            attempt.Error = ending == FailureClass.Ambiguous
                ? new AttemptErrorDto("provider_answer_lost", "No answer came back.", Retriable: true, Class: ending)
                : new AttemptErrorDto("provider_busy", "Too many requests.", Retriable: true, Class: ending);
            WorkItemTransitions.Apply(item, WorkItemStatus.Created, Noon);
            await db.SaveChangesAsync(Ct);
        }

        static ExternalId Pin(Attempt by) => new()
        {
            PluginId = "stand-in-provider",
            Kind = "contact",
            Value = "p-88421",
            RecordedAt = Noon,
            RecordedByAttemptId = by.PublicId,
        };
    }

    /// <summary>
    /// A send whose earlier attempt lost its answer, routed since to a plugin that does not perform it, is refused for
    /// that reason, and the refusal says the earlier attempt may have reached the person: ambiguous and final, so nobody
    /// reads "the plugin has no such operation" as leave to create the send again before finding out at the provider.
    /// </summary>
    [Fact]
    public async Task A_send_routed_after_a_lost_answer_to_a_plugin_without_it_is_refused_as_one_that_may_have_reached_the_person()
    {
        var plugin = TestPlugins.Loaded("stand-in-provider", ["list_membership.add"]);
        using var harness = new Harness(plugin: plugin, route: new Route("stand-in-provider", null, null));
        using var database = new TestDatabase();
        await using var db = database.Open();
        var campaign = WorkItemFactory.NewCampaign(now: Noon);
        db.Campaigns.Add(campaign);
        var contact = new Contact { PublicId = PublicId.New("cnt"), FirstName = "Ada", CreatedAt = Noon, UpdatedAt = Noon };
        contact.Channels.Add(new ContactChannel { Channel = "email", Value = "ada@example.test", IsPrimary = true });
        db.Contacts.Add(contact);
        var item = WorkItemFactory.NewProviderOp(campaign, "message.send", Noon);
        item.Contact = contact;
        item.Context = new JsonObject
        {
            [WorkItemService.InputKey] = new JsonObject
            {
                ["channel"] = "email",
                ["sender"] = new JsonObject { ["external_id"] = "snd-1", ["address"] = "marta@example.com" },
                ["message"] = new JsonObject { ["format"] = "email", ["subject"] = "Hello", ["body"] = "Hello, Ada." },
            },
        };
        db.WorkItems.Add(item);
        var lost = WorkItemFactory.NewAttempt(item, 1, AttemptStatus.Failed, Noon.AddHours(-1));
        lost.StartedAt = Noon.AddHours(-1);
        lost.Error = new AttemptErrorDto("provider_answer_lost", "No answer came back.", Retriable: true, Class: FailureClass.Ambiguous);
        lost.Provenance = new AttemptProvenanceDto("stand-in-provider", "1.0.0", "sha256:earlier", 1, 1, "message.send", 1, null, null, RouteScope.GlobalDefault, null);
        db.Attempts.Add(lost);
        item.AttemptCount = 1;
        await db.SaveChangesAsync(Ct);

        var claimed = await harness.Claimer.ClaimAsync(db, 4, Ct);

        Assert.Empty(claimed);
        Assert.Equal(WorkItemStatus.Failed, item.Status);
        var attempt = await db.Attempts.AsNoTracking().SingleAsync(a => a.Number == 2, Ct);
        Assert.Equal((AttemptErrors.PluginOperationUnsupported, FailureClass.Ambiguous, false), (attempt.Error!.Code, attempt.Error.Class, attempt.Error.Retriable));
        Assert.Contains("An earlier attempt of this work item may have reached the person", attempt.Error.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// A send whose earlier attempt lost its answer, and whose route is gone since, is refused no_route, and the refusal
    /// says the earlier attempt may have reached the person: the claim reads the attempt that may have acted before it
    /// knows there is no route, so the refusal is ambiguous and final like every other one from the route to the arguments.
    /// </summary>
    [Fact]
    public async Task A_send_whose_route_is_gone_after_a_lost_answer_is_refused_as_one_that_may_have_reached_the_person()
    {
        using var harness = new Harness();
        using var database = new TestDatabase();
        await using var db = database.Open();
        var campaign = WorkItemFactory.NewCampaign(now: Noon);
        db.Campaigns.Add(campaign);
        var contact = new Contact { PublicId = PublicId.New("cnt"), FirstName = "Ada", CreatedAt = Noon, UpdatedAt = Noon };
        contact.Channels.Add(new ContactChannel { Channel = "email", Value = "ada@example.test", IsPrimary = true });
        db.Contacts.Add(contact);
        var item = WorkItemFactory.NewProviderOp(campaign, "message.send", Noon);
        item.Contact = contact;
        db.WorkItems.Add(item);
        var lost = WorkItemFactory.NewAttempt(item, 1, AttemptStatus.Failed, Noon.AddHours(-1));
        lost.StartedAt = Noon.AddHours(-1);
        lost.Error = new AttemptErrorDto("provider_answer_lost", "No answer came back.", Retriable: true, Class: FailureClass.Ambiguous);
        lost.Provenance = new AttemptProvenanceDto("stand-in-provider", "1.0.0", "sha256:earlier", 1, 1, "message.send", 1, null, null, RouteScope.GlobalDefault, null);
        db.Attempts.Add(lost);
        item.AttemptCount = 1;
        await db.SaveChangesAsync(Ct);

        var claimed = await harness.Claimer.ClaimAsync(db, 4, Ct);

        Assert.Empty(claimed);
        Assert.Equal(WorkItemStatus.Failed, item.Status);
        var attempt = await db.Attempts.AsNoTracking().SingleAsync(a => a.Number == 2, Ct);
        Assert.Equal((AttemptErrors.NoRoute, FailureClass.Ambiguous, false), (attempt.Error!.Code, attempt.Error.Class, attempt.Error.Retriable));
        Assert.Contains("An earlier attempt of this work item may have reached the person", attempt.Error.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// A stop recorded under the plain spelling of a LinkedIn profile, and a person whose channel was written with a doubled
    /// trailing slash and a query: both are written by the rule that keys a profile by its slug, so they are one key, and
    /// the claim's one question to the real register refuses the work about that person.
    /// </summary>
    [Fact]
    [Critical(Family.F1, "the register at the claim across spellings")]
    public async Task A_stop_under_one_spelling_of_a_profile_refuses_work_about_a_person_stored_under_another()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var clock = new FixedClock(new DateTimeOffset(Noon));
        var register = new SuppressionService(db, new JournalWriter(clock), clock);
        await register.AddAsync(new SuppressionAddRequest("linkedin", "https://www.linkedin.com/in/Marta-Example", null, "said stop"), Ct);
        using var harness = new Harness(
            plugin: TestPlugins.Loaded("stand-in-provider", ["list_membership.add"]),
            route: new Route("stand-in-provider", null, null),
            register: register);
        var campaign = WorkItemFactory.NewCampaign(now: Noon);
        db.Campaigns.Add(campaign);
        var errors = new Jason.Runtime.Domain.ValidationErrors();
        var stored = ChannelValidation.Value("linkedin", "https://www.linkedin.com/in/Marta-Example//?trk=feed", "channels[0].value", errors);
        var contact = new Contact { PublicId = PublicId.New("cnt"), FirstName = "Marta", CreatedAt = Noon, UpdatedAt = Noon };
        contact.Channels.Add(new ContactChannel { Channel = "linkedin", Value = stored!, IsPrimary = true });
        db.Contacts.Add(contact);
        var item = WorkItemFactory.NewProviderOp(campaign, "list_membership.add", Noon);
        item.Contact = contact;
        item.Context = new JsonObject
        {
            [WorkItemService.InputKey] = new JsonObject { ["list"] = new JsonObject { ["external_id"] = "9" }, ["channel"] = "linkedin" },
        };
        db.WorkItems.Add(item);
        await db.SaveChangesAsync(Ct);

        var claimed = await harness.Claimer.ClaimAsync(db, 4, Ct);

        Assert.False(errors.Any);
        Assert.Empty(claimed);
        Assert.Equal(WorkItemStatus.Failed, item.Status);
        Assert.Equal("suppressed", (await db.Attempts.AsNoTracking().SingleAsync(Ct)).Error!.Code);
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
    [Critical(Family.W9, "what runs in a paused campaign")]
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
        private readonly ILoggerFactory _logging;

        public Harness(
            Action<DispatcherOptions>? dispatcher = null,
            RolesOptions? roles = null,
            LoadedPlugin? plugin = null,
            Route? route = null,
            ISuppressionCheck? register = null,
            PatternTiming? timing = null,
            Func<string, RouteSnapshot>? routes = null,
            IReadOnlyList<LoadedPlugin>? others = null)
        {
            _logging = LoggerFactory.Create(builder => builder.AddProvider(Logs));
            var clock = new FixedClock(Noon);
            var settings = TestOptions.Settings(dispatcher);
            var journal = new JournalWriter(clock);
            var plugins = new PluginRegistry(clock);
            if (plugin is not null)
            {
                plugins.Replace(
                    new PluginSnapshot(PublicId.New(PluginProtocol.SnapshotIdPrefix), Noon, SnapshotSource.Startup, [plugin, .. others ?? []]),
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

            if (routes is not null)
            {
                routing.Replace(routes(plugins.Snapshot.Id));
            }

            Claimer = new Claimer(
                journal,
                clock,
                new AttemptOutcomes(journal, clock, settings),
                new EntryCommandResolver(TestOptions.RoleSettings(roles)),
                new AgentLaunchPlanner(
                    new AgentPreflight(),
                    new ProgramResolver(new ThisMachineHasNothing()),
                    [new ClaudeCodeHost()],
                    global::Jason.Runtime.Tests.Harness.TestHarness.Folders(_dir.Home)),
                TestOptions.RoleSettings(roles),
                plugins,
                routing,
                new ExternalIdStore(journal, clock),
                register ?? new NothingIsSuppressed(),
                settings,
                TestOptions.LaunchesSettings(),
                _dir.Paths,
                _logging.CreateLogger<Claimer>(),
                timing);
        }

        public JasonPaths Paths => _dir.Paths;

        public Claimer Claimer { get; }

        /// <summary>What the claim logged, with the scope each line was written under.</summary>
        public RecordingLogs Logs { get; } = new();

        public void Dispose()
        {
            _logging.Dispose();
            _dir.Dispose();
        }

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
