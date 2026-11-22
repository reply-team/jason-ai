using System.Text.Json.Nodes;
using Jason.Contracts.Api;
using Jason.Contracts.Ids;
using Jason.Contracts.Plugins;
using Jason.Runtime.Campaigns;
using Jason.Runtime.Configuration;
using Jason.Runtime.Dispatch;
using Jason.Runtime.Domain;
using Jason.Runtime.Execution;
using Jason.Runtime.Journal;
using Jason.Runtime.Persistence;
using Jason.Runtime.Plugins.Registry;
using Jason.Runtime.Routing;
using Jason.Runtime.Tests.Plugins;
using Jason.Runtime.WorkItems;
using Microsoft.EntityFrameworkCore;

namespace Jason.Runtime.Tests.Dispatch;

/// <summary>
/// The line's two checks through the real claim, its real reads and a real database, with <c>Lines:MaxTouches</c> at two:
/// what the ceiling counts — a touch that succeeded, or failed with a last attempt classed ambiguous, and nothing else — and
/// that a refusal is decided before anybody is asked to approve, ends the item failed, and wakes the manager.
/// </summary>
public partial class ClaimerTests
{
    private const string LineProvider = "stand-in-provider";

    /// <summary>
    /// Two touches reached the person, so a third is refused at the claim: failed, permanent, with its attempt kept and no
    /// child, and no approval or question made for it — nobody is asked to approve a touch that could only be refused.
    /// </summary>
    [Fact]
    public async Task The_third_touch_is_refused_at_the_ceiling_before_an_approval_is_asked()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        using var harness = LineHarness();
        var (campaign, contact) = await LineSceneAsync(db);
        await TouchedAsync(db, campaign, contact, WorkItemStatus.Succeeded);
        await TouchedAsync(db, campaign, contact, WorkItemStatus.Succeeded, operation: "campaign.enroll");
        var third = Sending(db, campaign, contact);
        await db.SaveChangesAsync(Ct);

        Assert.Empty(await harness.Claimer.ClaimAsync(db, 4, Ct));

        await using var fresh = database.Open();
        var attempt = await fresh.Attempts.AsNoTracking().SingleAsync(a => a.WorkItemId == third.Id, Ct);
        Assert.Equal((AttemptErrors.TouchCeilingReached, FailureClass.Permanent), (attempt.Error!.Code, attempt.Error.Class));
        Assert.False(attempt.Error.Retriable);
        Assert.Contains("Lines:MaxTouches allows in this campaign, 2;", attempt.Error.Message, StringComparison.Ordinal);
        Assert.Null(attempt.Launch);
        Assert.Equal(WorkItemStatus.Failed, (await fresh.WorkItems.AsNoTracking().SingleAsync(w => w.Id == third.Id, Ct)).Status);
        Assert.Empty(await fresh.Approvals.AsNoTracking().ToListAsync(Ct));
        Assert.Empty(await fresh.Decisions.AsNoTracking().ToListAsync(Ct));
        Assert.False(await fresh.Journal.AnyAsync(e => e.Kind == JournalKinds.ApprovalRequested, Ct));
    }

    /// <summary>
    /// A touch whose last attempt lost its answer may have reached the person, so it counts; one the provider refused
    /// permanently reached nobody, so it does not, and the touch after it goes on to the person's approval.
    /// </summary>
    [Theory]
    [InlineData(FailureClass.Ambiguous, AttemptErrors.TouchCeilingReached)]
    [InlineData(FailureClass.Permanent, null)]
    [InlineData(FailureClass.Validation, null)]
    public async Task A_failed_touch_counts_only_where_its_last_attempt_may_have_reached_the_person(FailureClass failed, string? refused)
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        using var harness = LineHarness();
        var (campaign, contact) = await LineSceneAsync(db);
        await TouchedAsync(db, campaign, contact, WorkItemStatus.Succeeded);
        await TouchedAsync(db, campaign, contact, WorkItemStatus.Failed, failed);
        var next = Sending(db, campaign, contact);
        await db.SaveChangesAsync(Ct);

        Assert.Empty(await harness.Claimer.ClaimAsync(db, 4, Ct));

        await using var fresh = database.Open();
        var row = await fresh.WorkItems.AsNoTracking().SingleAsync(w => w.Id == next.Id, Ct);
        if (refused is null)
        {
            Assert.Equal(WorkItemStatus.AwaitingApproval, row.Status);
            Assert.Single(await fresh.Approvals.AsNoTracking().Where(a => a.WorkItemId == next.Id).ToListAsync(Ct));
        }
        else
        {
            Assert.Equal(refused, await RefusalAsync(fresh, next));
        }
    }

    /// <summary>
    /// Only the last attempt of a failed touch decides: an attempt that lost its answer, then a repeat the provider refused
    /// for good, is a touch that the provider says did not happen, and the reverse is one that may have.
    /// </summary>
    [Theory]
    [InlineData(FailureClass.Ambiguous, FailureClass.Permanent, null)]
    [InlineData(FailureClass.Permanent, FailureClass.Ambiguous, AttemptErrors.TouchCeilingReached)]
    public async Task A_failed_touch_is_counted_by_its_last_attempt(FailureClass first, FailureClass last, string? refused)
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        using var harness = LineHarness();
        var (campaign, contact) = await LineSceneAsync(db);
        await TouchedAsync(db, campaign, contact, WorkItemStatus.Succeeded);
        var twice = await TouchedAsync(db, campaign, contact, WorkItemStatus.Failed, first);
        var repeated = WorkItemFactory.NewAttempt(twice, 2, AttemptStatus.Failed, Noon.AddHours(-1));
        repeated.FinishedAt = Noon.AddMinutes(-50);
        repeated.Error = new AttemptErrorDto(last == FailureClass.Ambiguous ? "provider_answer_lost" : "recipient_rejected", "The provider answered so.", false, Class: last);
        twice.AttemptCount = 2;
        twice.LastError = repeated.Error;
        db.Attempts.Add(repeated);
        var next = Sending(db, campaign, contact);
        await db.SaveChangesAsync(Ct);

        Assert.Empty(await harness.Claimer.ClaimAsync(db, 4, Ct));

        await using var fresh = database.Open();
        var row = await fresh.WorkItems.AsNoTracking().SingleAsync(w => w.Id == next.Id, Ct);
        Assert.Equal(refused is null ? WorkItemStatus.AwaitingApproval : WorkItemStatus.Failed, row.Status);
        if (refused is not null)
        {
            Assert.Equal(refused, await RefusalAsync(fresh, next));
        }
    }

    /// <summary>
    /// What the ceiling counts is the person's touches in this campaign and nothing else: another person's, the person's in
    /// another campaign, work that reaches nobody, and touches still open or that expired count for nothing.
    /// </summary>
    [Fact]
    public async Task Only_the_person_s_own_touches_in_this_campaign_that_ended_are_counted()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        using var harness = LineHarness();
        var (campaign, contact) = await LineSceneAsync(db);
        var (elsewhere, other) = await LineSceneAsync(db, "cnt_01K52JR0000000000000000002");
        await TouchedAsync(db, campaign, contact, WorkItemStatus.Succeeded);
        await TouchedAsync(db, elsewhere, contact, WorkItemStatus.Succeeded);
        await TouchedAsync(db, campaign, other, WorkItemStatus.Succeeded);
        await TouchedAsync(db, campaign, contact, WorkItemStatus.Succeeded, operation: "list_membership.add");
        await TouchedAsync(db, campaign, contact, WorkItemStatus.Succeeded, operation: "conversation.reply");
        await TouchedAsync(db, campaign, contact, WorkItemStatus.Expired);
        await TouchedAsync(db, campaign, contact, WorkItemStatus.AwaitingApproval);
        await TouchedAsync(db, campaign, contact, WorkItemStatus.Cancelled);

        Assert.Equal(1, await LineTouches.OthersAsync(db, campaign.Id, contact.Id, workItemId: 0, Ct));
    }

    /// <summary>
    /// The item being claimed is never counted against itself, whatever its own attempts were: an earlier attempt of it that
    /// lost its answer is the repeat's own concern (check 23), not one more touch.
    /// </summary>
    [Fact]
    public async Task The_item_s_own_earlier_attempt_is_not_counted_against_it()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        using var harness = LineHarness();
        var (campaign, contact) = await LineSceneAsync(db);
        await TouchedAsync(db, campaign, contact, WorkItemStatus.Succeeded);
        var repeat = Sending(db, campaign, contact);
        repeat.AttemptCount = 1;
        var lost = WorkItemFactory.NewAttempt(repeat, 1, AttemptStatus.Failed, Noon.AddHours(-1));
        lost.Error = new AttemptErrorDto("provider_answer_lost", "The answer was lost.", true, Class: FailureClass.Ambiguous);
        db.Attempts.Add(lost);
        await db.SaveChangesAsync(Ct);

        Assert.Empty(await harness.Claimer.ClaimAsync(db, 4, Ct));

        await using var fresh = database.Open();
        var refusal = (await fresh.Attempts.AsNoTracking().Where(a => a.WorkItemId == repeat.Id).OrderByDescending(a => a.Number).FirstAsync(Ct)).Error!;
        Assert.Equal(AttemptErrors.ProviderAnswerLost, refusal.Code);

        // And the count itself leaves the item out even where the item has ended as one that would count.
        var ended = await TouchedAsync(db, campaign, contact, WorkItemStatus.Succeeded);
        Assert.Equal(1, await LineTouches.OthersAsync(db, campaign.Id, contact.Id, ended.Id, Ct));
        Assert.Equal(2, await LineTouches.OthersAsync(db, campaign.Id, contact.Id, workItemId: 0, Ct));
    }

    /// <summary>
    /// A person whose line in this campaign was ended is refused a touch at the claim, and a reply in their thread is not:
    /// the reply carries on their conversation, and only the person reopens the line.
    /// </summary>
    [Fact]
    public async Task A_finished_person_is_refused_a_touch_and_not_a_reply()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        using var harness = LineHarness();
        var (campaign, contact) = await LineSceneAsync(db, state: MembershipState.Finished);
        var send = Sending(db, campaign, contact);
        await db.SaveChangesAsync(Ct);

        Assert.Empty(await harness.Claimer.ClaimAsync(db, 4, Ct));

        await using (var fresh = database.Open())
        {
            Assert.Equal(AttemptErrors.LineFinished, await RefusalAsync(fresh, send));
            var error = (await fresh.Attempts.AsNoTracking().SingleAsync(a => a.WorkItemId == send.Id, Ct)).Error!;
            Assert.Equal("The line with this person in this campaign was ended; only the person reopens it.", error.Message);
            Assert.Empty(await fresh.Approvals.AsNoTracking().ToListAsync(Ct));
        }

        var reply = WorkItemFactory.NewProviderOp(campaign, "conversation.reply", Noon);
        reply.Contact = contact;
        reply.Context = new JsonObject
        {
            [WorkItemService.InputKey] = new JsonObject
            {
                ["channel"] = "email",
                ["thread"] = new JsonObject { ["external_id"] = "th-1" },
                ["body"] = "Thank you, Tuesday works.",
            },
        };
        db.WorkItems.Add(reply);
        await db.SaveChangesAsync(Ct);

        Assert.Empty(await harness.Claimer.ClaimAsync(db, 4, Ct));

        await using var after = database.Open();
        Assert.Equal(WorkItemStatus.AwaitingApproval, (await after.WorkItems.AsNoTracking().SingleAsync(w => w.Id == reply.Id, Ct)).Status);
    }

    /// <summary>The ceiling is read once a scan: one raised between two scans lets the next touch through to the person's approval.</summary>
    [Fact]
    public async Task A_ceiling_raised_between_scans_applies_from_the_next_scan()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        using var harness = LineHarness();
        var (campaign, contact) = await LineSceneAsync(db);
        await TouchedAsync(db, campaign, contact, WorkItemStatus.Succeeded);
        await TouchedAsync(db, campaign, contact, WorkItemStatus.Succeeded);
        var refused = Sending(db, campaign, contact);
        await db.SaveChangesAsync(Ct);
        Assert.Empty(await harness.Claimer.ClaimAsync(db, 4, Ct));

        harness.Lines.CurrentValue = new LinesOptions { MaxTouches = 3 };
        var next = Sending(db, campaign, contact);
        await db.SaveChangesAsync(Ct);
        Assert.Empty(await harness.Claimer.ClaimAsync(db, 4, Ct));

        await using var fresh = database.Open();
        Assert.Equal(AttemptErrors.TouchCeilingReached, await RefusalAsync(fresh, refused));
        Assert.Equal(WorkItemStatus.AwaitingApproval, (await fresh.WorkItems.AsNoTracking().SingleAsync(w => w.Id == next.Id, Ct)).Status);
    }

    /// <summary>
    /// No notice of its own: the refusal ends the item failed and writes <c>workitem_failed</c> as the dispatcher, about the
    /// touch and not about a check-in, so the summon's filter of a check-in's own lines keeps it, and the next summon wakes
    /// the manager with it, who ends the line.
    /// </summary>
    [Theory]
    [InlineData(MembershipState.Finished, AttemptErrors.LineFinished)]
    [InlineData(MembershipState.Enrolled, AttemptErrors.TouchCeilingReached)]
    public async Task A_line_s_refusal_wakes_the_manager_through_workitem_failed(MembershipState state, string code)
    {
        using var harness = new DispatchHarness(Noon);
        harness.Lines.CurrentValue = new LinesOptions { MaxTouches = 2 };
        RoutedToTheLineProvider(harness);
        string touch;
        await using (var db = harness.Open())
        {
            var (campaign, contact) = await LineSceneAsync(db, state: state);
            await TouchedAsync(db, campaign, contact, WorkItemStatus.Succeeded);
            await TouchedAsync(db, campaign, contact, WorkItemStatus.Succeeded);
            touch = Sending(db, campaign, contact).PublicId;
            await db.SaveChangesAsync(Ct);
        }

        Assert.Empty(await harness.ClaimAsync(Ct));

        var failed = Assert.Single(await harness.ReadJournalAsync(touch, Ct), e => e.Kind == JournalKinds.WorkItemFailed);
        Assert.Equal((Actors.Dispatcher.Type, Actors.Dispatcher.Id), (failed.ActorType, failed.ActorId));
        Assert.Equal(code, (string?)failed.New!["code"]);

        Assert.Equal(1, await harness.SummonAsync(Ct));
        await using var read = harness.Open();
        var checkIn = await read.WorkItems.AsNoTracking().SingleAsync(w => w.Role == ManagerCheckIn.Role, Ct);
        Assert.Equal(JournalKinds.WorkItemFailed, (string?)checkIn.Context["trigger"]);
        Assert.Equal(touch, (string?)checkIn.Context["cause"]!["work_item_id"]);
    }

    /// <summary>The claim's harness with a provider that touches and replies, and a ceiling of two.</summary>
    private Harness LineHarness()
    {
        var harness = new Harness(
            plugin: TestPlugins.Loaded(LineProvider, ["message.send", "campaign.enroll", "conversation.reply"]),
            route: new Route(LineProvider, null, null));
        harness.Lines.CurrentValue = new LinesOptions { MaxTouches = 2 };
        return harness;
    }

    /// <summary>The same provider loaded and routed by default in a whole dispatcher's composition.</summary>
    private static void RoutedToTheLineProvider(DispatchHarness harness)
    {
        var plugins = harness.Resolve<PluginRegistry>();
        plugins.Replace(
            new PluginSnapshot(PublicId.New(PluginProtocol.SnapshotIdPrefix), Noon, SnapshotSource.Startup, [TestPlugins.Loaded(LineProvider, ["message.send"])]),
            new ReloadReport(Noon, SnapshotSource.Startup, Activated: true, [], []));
        harness.Resolve<RouteRegistry>().Replace(new RouteSnapshot(
            PublicId.New(RouteSnapshot.IdPrefix),
            Noon,
            plugins.Snapshot.Id,
            new RouteSet(new Route(LineProvider, null, null), RouteSet.Empty.Operations),
            RouteSnapshot.Empty(Noon, plugins.Snapshot.Id).Campaigns));
    }

    /// <summary>An active campaign and a person who is its member in <paramref name="state"/>, reachable by email.</summary>
    private static async Task<(Campaign Campaign, Contact Contact)> LineSceneAsync(
        JasonDbContext db,
        string contactId = "cnt_01K52JR0000000000000000001",
        MembershipState state = MembershipState.Enrolled)
    {
        var campaign = WorkItemFactory.NewCampaign(now: Noon.AddDays(-7));
        var contact = new Contact { PublicId = contactId, FirstName = "Marta", CreatedAt = Noon.AddDays(-7), UpdatedAt = Noon.AddDays(-7) };
        contact.Channels.Add(new ContactChannel { Channel = "email", Value = $"{contactId[^4..].ToLowerInvariant()}@example.test", IsPrimary = true });
        db.Campaigns.Add(campaign);
        db.Contacts.Add(contact);
        db.CampaignContacts.Add(new CampaignContact { Campaign = campaign, Contact = contact, State = state, AddedAt = Noon.AddDays(-7), UpdatedAt = Noon.AddDays(-1) });
        await db.SaveChangesAsync(Ct);
        return (campaign, contact);
    }

    /// <summary>
    /// Earlier work of <paramref name="operation"/> about the person, ended or left <paramref name="status"/>, with one attempt;
    /// a failed one ended with <paramref name="failed"/>.
    /// </summary>
    private static async Task<WorkItem> TouchedAsync(
        JasonDbContext db,
        Campaign campaign,
        Contact contact,
        WorkItemStatus status,
        FailureClass failed = FailureClass.Permanent,
        string operation = "message.send")
    {
        var item = WorkItemFactory.NewProviderOp(campaign, operation, Noon.AddHours(-3));
        item.Contact = contact;
        item.Status = status;
        item.Context = new JsonObject { [WorkItemService.InputKey] = new JsonObject { ["channel"] = "email" } };
        db.WorkItems.Add(item);
        if (status is WorkItemStatus.Succeeded or WorkItemStatus.Failed)
        {
            item.FinishedAt = Noon.AddHours(-2);
            item.AttemptCount = 1;
            var attempt = WorkItemFactory.NewAttempt(item, 1, status == WorkItemStatus.Succeeded ? AttemptStatus.Succeeded : AttemptStatus.Failed, Noon.AddHours(-3));
            attempt.FinishedAt = Noon.AddHours(-2);
            if (status == WorkItemStatus.Failed)
            {
                attempt.Error = new AttemptErrorDto(
                    failed == FailureClass.Ambiguous ? "provider_answer_lost" : "recipient_rejected",
                    "The provider answered so.",
                    Retriable: false,
                    Class: failed);
                item.LastError = attempt.Error;
            }

            db.Attempts.Add(attempt);
        }
        else if (status is WorkItemStatus.Expired or WorkItemStatus.Cancelled)
        {
            item.FinishedAt = Noon.AddHours(-2);
        }

        await db.SaveChangesAsync(Ct);
        return item;
    }
}
