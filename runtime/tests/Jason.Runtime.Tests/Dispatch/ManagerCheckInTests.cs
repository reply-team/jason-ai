using System.Text.Json.Nodes;
using Jason.Contracts.Api;
using Jason.Contracts.Operations;
using Jason.Runtime.Configuration;
using Jason.Runtime.Dispatch;
using Jason.Runtime.Journal;
using Jason.Runtime.Persistence;
using Jason.Runtime.WorkItems;
using Microsoft.EntityFrameworkCore;

namespace Jason.Runtime.Tests.Dispatch;

/// <summary>
/// The work item the runtime creates to have a campaign reviewed: whose chain it belongs to, what it is told,
/// and what it is allowed to cost.
/// </summary>
public class ManagerCheckInTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly DateTime Noon = new(2026, 9, 14, 12, 0, 0, DateTimeKind.Utc);

    /// <summary>
    /// The one that decides whether the manager loop keeps a chain or cuts it. The dispatcher creates the row,
    /// so reading lineage from the creating actor would have made every triggered review root work — and root
    /// work resolves to the house default, which means a review of an attempt that ran on somebody's profile
    /// could quietly run on another.
    /// </summary>
    [Fact]
    public async Task A_triggered_check_in_inherits_the_chain_it_is_being_asked_about()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var campaign = Seed(db);
        var failed = FailedRoleAttempt(db, campaign, "local-claude", revision: 3);
        await db.SaveChangesAsync(Ct);

        var item = await ManagerCheckIn.CreateAsync(
            Service(db),
            db,
            campaign,
            new ManagerCause(JournalKinds.WorkItemFailed, "jrn_1", failed.WorkItem!.PublicId, failed.PublicId, DecisionId: null, 1),
            new ManagerOptions(),
            Ct);

        Assert.Equal(LineageState.Inherited, item.LineageState);
        Assert.Equal("local-claude", item.LineageProfileName);
        Assert.Equal(3, item.LineageProfileRevision);
        Assert.Equal(failed.PublicId, item.LineageFromAttemptId);

        // And the actor is still the truth about who made the row. The chain is about the cause; the row is
        // about the runtime.
        Assert.Equal(ActorType.System, item.CreatedByType);
        Assert.Equal("dispatcher", item.CreatedById);
    }

    /// <summary>
    /// A person rejecting an approval leaves a line with a work item and no attempt. The chain the review is
    /// about is that item's own, and handing on its record is what keeps it.
    /// </summary>
    [Fact]
    public async Task A_cause_with_an_item_and_no_attempt_hands_on_that_items_chain()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var campaign = Seed(db);
        var parked = new WorkItem
        {
            PublicId = "wi_parked",
            Campaign = campaign,
            Kind = WorkItemKind.ProviderOp,
            Operation = "campaign.enroll",
            Status = WorkItemStatus.Failed,
            CreatedByType = ActorType.Attempt,
            CreatedById = "att_gone",
            LineageState = LineageState.Inherited,
            LineageProfileName = "local-claude",
            LineageProfileRevision = 2,
            LineageFromAttemptId = "att_ancestor",
            CreatedAt = Noon,
            UpdatedAt = Noon,
        };
        db.WorkItems.Add(parked);
        await db.SaveChangesAsync(Ct);

        var item = await ManagerCheckIn.CreateAsync(
            Service(db),
            db,
            campaign,
            new ManagerCause(JournalKinds.ApprovalRejected, "jrn_2", "wi_parked", null, DecisionId: null, 1),
            new ManagerOptions(),
            Ct);

        Assert.Equal(LineageState.Inherited, item.LineageState);
        Assert.Equal("local-claude", item.LineageProfileName);
        Assert.Equal(2, item.LineageProfileRevision);
        Assert.Equal("att_ancestor", item.LineageFromAttemptId);
    }

    /// <summary>Nothing caused a scheduled review but the clock, and the clock is the runtime itself.</summary>
    [Fact]
    public async Task A_scheduled_check_in_is_root_work()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var campaign = Seed(db);
        await db.SaveChangesAsync(Ct);

        var item = await ManagerCheckIn.CreateAsync(Service(db), db, campaign, cause: null, new ManagerOptions(), Ct);

        Assert.Equal(LineageState.Root, item.LineageState);
        Assert.Null(item.LineageProfileName);
    }

    [Fact]
    public async Task The_brief_says_why_the_manager_was_woken_and_nothing_about_what_it_will_find()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var campaign = Seed(db);
        var failed = FailedRoleAttempt(db, campaign, profile: null, revision: null);
        await db.SaveChangesAsync(Ct);

        var triggered = await ManagerCheckIn.CreateAsync(
            Service(db),
            db,
            campaign,
            new ManagerCause(JournalKinds.WorkItemFailed, "jrn_7", failed.WorkItem!.PublicId, failed.PublicId, DecisionId: null, 4),
            new ManagerOptions(),
            Ct);

        Assert.Equal("triggered", (string?)triggered.Context["review_intent"]);
        Assert.Equal(JournalKinds.WorkItemFailed, (string?)triggered.Context["trigger"]);
        var cause = Assert.IsType<JsonObject>(triggered.Context["cause"]);
        Assert.Equal("jrn_7", (string?)cause["journal_entry_id"]);
        Assert.Equal(failed.PublicId, (string?)cause["attempt_id"]);
        Assert.Equal(4, (int?)cause["qualifying_count"]);

        // Four keys and no fifth: whatever else a review needs, it reads for itself through the CLI.
        Assert.Equal(["review_intent", "allowed_operations", "trigger", "cause"], triggered.Context.Select(pair => pair.Key));

        // The operations are guidance, in the brief where the role reads it — not a permission, which is why
        // nothing else in this wave consults the list.
        var allowed = Assert.IsType<JsonArray>(triggered.Context["allowed_operations"]);
        Assert.Contains("workitem.create", allowed.Select(node => (string?)node));
        Assert.DoesNotContain("campaign.enroll", allowed.Select(node => (string?)node));
    }

    [Fact]
    public async Task A_scheduled_brief_names_no_trigger_and_no_cause()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var campaign = Seed(db);
        await db.SaveChangesAsync(Ct);

        var item = await ManagerCheckIn.CreateAsync(Service(db), db, campaign, cause: null, new ManagerOptions(), Ct);

        Assert.Equal("scheduled", (string?)item.Context["review_intent"]);
        Assert.Null(item.Context["trigger"]);
        Assert.Null(item.Context["cause"]);
    }

    /// <summary>
    /// The three numbers are the loop's own. An hour and three attempts are the right budget for research and
    /// the wrong one for a review: a review either says something now or says nothing worth retrying.
    /// </summary>
    [Fact]
    public async Task A_check_in_carries_the_loops_numbers_and_not_the_kinds()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var campaign = Seed(db);
        await db.SaveChangesAsync(Ct);

        var item = await ManagerCheckIn.CreateAsync(
            Service(db),
            db,
            campaign,
            cause: null,
            // Values the defaults are not, so that a check-in built from the kind's defaults would fail here
            // rather than agree by coincidence.
            new ManagerOptions { TimeoutSeconds = 450, MaxAttempts = 2, Priority = 5 },
            Ct);

        Assert.Equal(450, item.TimeoutSeconds);
        Assert.Equal(2, item.MaxAttempts);
        Assert.Equal(5, item.Priority);
        Assert.Equal(ManagerCheckIn.Role, item.Role);
        Assert.Equal(WorkItemKind.AiRole, item.Kind);
        Assert.Null(item.ExecutionProfile);
    }

    /// <summary>
    /// The runtime writes this shape and the runtime enforces it. A <c>result_format</c> the runtime composed
    /// and then refused would be a review nobody could ever finish.
    /// </summary>
    [Fact]
    public void The_shape_a_review_answers_in_is_one_this_runtime_accepts()
    {
        var problems = SchemaValidator.CheckDialect(Assert.IsType<JsonObject>(ManagerCheckIn.ResultFormat));

        Assert.Empty(problems);
    }

    /// <summary>Created through the one routine, so the chronicle says a work item was created, as for any other.</summary>
    [Fact]
    public async Task Creating_one_writes_the_line_every_work_item_writes()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var campaign = Seed(db);
        await db.SaveChangesAsync(Ct);

        var item = await ManagerCheckIn.CreateAsync(Service(db), db, campaign, cause: null, new ManagerOptions(), Ct);

        var entry = await db.Journal.SingleAsync(e => e.Kind == JournalKinds.WorkItemCreated, Ct);
        Assert.Equal(item.PublicId, entry.WorkItemId);
        Assert.Equal(ActorType.System, entry.ActorType);
        Assert.Equal("dispatcher", entry.ActorId);
    }

    private static WorkItemService Service(JasonDbContext db)
    {
        var clock = new FixedClock(Noon);
        return new WorkItemService(db, new JournalWriter(clock), clock, TestCanceller.New(clock), TestOptions.PluginSettings());
    }

    private static Campaign Seed(JasonDbContext db)
    {
        var campaign = new Campaign
        {
            PublicId = "cmp_A",
            Name = "Work",
            Status = CampaignStatus.Active,
            CreatedAt = Noon,
            UpdatedAt = Noon,
        };
        db.Campaigns.Add(campaign);

        // The manager is one of the nine roles the schema seeds, so nothing here has to invent it — which is
        // itself part of what this wave leans on.
        return campaign;
    }

    private static Attempt FailedRoleAttempt(JasonDbContext db, Campaign campaign, string? profile, int? revision)
    {
        var item = new WorkItem
        {
            PublicId = "wi_failed",
            Campaign = campaign,
            Kind = WorkItemKind.AiRole,
            Role = "researcher",
            Status = WorkItemStatus.Failed,
            CreatedByType = ActorType.Human,
            LineageState = LineageState.Root,
            CreatedAt = Noon,
            UpdatedAt = Noon,
        };
        var attempt = new Attempt
        {
            PublicId = "att_failed",
            WorkItem = item,
            Number = 1,
            Command = WorkItemKind.AiRole,
            Status = AttemptStatus.Failed,
            ClaimedAt = Noon,
            LockUntil = Noon.AddHours(1),
            Provenance = profile is null
                ? null
                : new AttemptProvenanceDto(
                    null, null, null, null, null, null, null, null, null, null, null,
                    Agent: new AgentProvenanceDto(ProfileResolutionSource.CampaignPolicy, ProfileName: profile, ProfileRevision: revision)),
        };
        db.WorkItems.Add(item);
        db.Attempts.Add(attempt);
        return attempt;
    }
}
