using System.Net;
using System.Text.Json;
using Jason.Contracts.Api;
using Jason.Contracts.Json;
using Jason.Runtime.Campaigns;
using Jason.Runtime.Configuration;
using Jason.Runtime.Dispatch;
using Jason.Runtime.Domain;
using Jason.Runtime.Execution;
using Jason.Runtime.Journal;
using Jason.Runtime.Launches;
using Jason.Runtime.Persistence;
using Jason.Runtime.Tests.Dispatch;
using Jason.Runtime.Tests.Journal;
using Jason.Runtime.WorkItems;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Jason.Runtime.Tests.Campaigns;

/// <summary>
/// A person's word that a campaign's manager reviews it now, <c>campaign.review</c>: the check-in it makes is the summon's
/// own, with the entry the depth names and <c>review_intent: requested</c>; it moves the quick look's anchor as every
/// check-in does and leaves the deep review's grid where it was, so the daily deep review still comes at its moment; its
/// line names the person and their reason and summons nothing; and it is refused, in one published order, inside a run,
/// without a named person, for a campaign that is not there or not active, while a check-in is open, and once the
/// manager's launches of the day are spent.
/// </summary>
public class CampaignReviewTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly DateTime Noon = new(2026, 9, 14, 12, 0, 0, DateTimeKind.Utc);

    private static readonly ActorRef Ada = new(ActorType.Human, "ada");

    /// <summary>
    /// The first refusal: a request from inside a run is refused before its body is read, so before its actor and its
    /// campaign, whatever both are, and nothing is written.
    /// </summary>
    [Fact]
    public async Task From_inside_a_run_it_is_refused_before_its_actor_or_its_campaign_is_read()
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct, clock: new FixedClock(new DateTimeOffset(Noon)));
        var inside = new Dictionary<string, string> { ["Jason-Attempt"] = "att_01K7QN0000000000000000000R" };

        var (status, text) = await api.PostAsync(Operations.CampaignReview, new { campaign_id = "cmp_missing" }, inside, Ct);

        Assert.True(status == HttpStatusCode.Forbidden, $"campaign.review answered {(int)status}: {text}");
        var error = JsonSerializer.Deserialize<ErrorResponse>(text, JasonJson.Options)!.Error;
        Assert.Equal("decided_inside_a_run", error.Code);
        Assert.Contains(Operations.CampaignReview, Jason.Runtime.Api.InsideARun.PersonsWord);
    }

    /// <summary>
    /// The refusals after the run's, each met by a request that would also meet every one after it, so each is shown to come
    /// before the rest: no actor, an actor that is no person, no campaign, a campaign not active, a check-in open, the day's
    /// launches spent. Each leaves nothing written.
    /// </summary>
    [Fact]
    public async Task Each_refusal_comes_in_its_order()
    {
        using var harness = Harness(managerLimit: 1);
        var campaign = await CampaignAsync(harness, CampaignStatus.Paused);
        var open = await CheckInAsync(harness, campaign, WorkItemStatus.Created);
        await LaunchedAsync(harness, campaign, Noon.AddHours(-1));
        var lines = await LinesAsync(harness);

        Assert.Equal(("actor_required", 400), await RefusedAsync(harness, new CampaignReviewRequest("cmp_missing", null, null, null)));
        Assert.Equal(("review_not_human", 400), await RefusedAsync(harness, new CampaignReviewRequest("cmp_missing", null, new ActorRef(ActorType.Role, "manager"), null)));
        Assert.Equal(("review_not_human", 400), await RefusedAsync(harness, new CampaignReviewRequest("cmp_missing", null, new ActorRef(ActorType.Attempt, "att_01K7QN0000000000000000000R"), null)));
        Assert.Equal(("campaign_not_found", 404), await RefusedAsync(harness, new CampaignReviewRequest("cmp_missing", null, Ada, null)));
        Assert.Equal(("campaign_not_active", 409), await RefusedAsync(harness, Request(campaign)));

        await StatusAsync(harness, campaign, CampaignStatus.Active);
        var already = await RefusalAsync(harness, Request(campaign));
        Assert.Equal(("review_already_open", 409), (already.Code, already.StatusCode));
        Assert.Contains(open, already.Message, StringComparison.Ordinal);

        await FinishAsync(harness, open);
        var spent = await RefusalAsync(harness, Request(campaign));
        Assert.Equal((AttemptErrors.LaunchesSpent, 409), (spent.Code, spent.StatusCode));
        Assert.Contains("To go on today, tell your agent: ignore the limit, or run jason launches lift --actor human:<your name>.", spent.Message, StringComparison.Ordinal);

        Assert.Equal(lines, await LinesAsync(harness));
        Assert.Equal(1, await CheckInCountAsync(harness));
    }

    /// <summary>
    /// A limit lowered during the day stands below the runs that started: the refusal says how many started and the limit
    /// as a limit beside them, never the limit as the count, and names the read that shows the limits.
    /// </summary>
    [Fact]
    public async Task The_refusal_says_the_runs_that_started_and_the_limit_as_a_limit()
    {
        using var harness = Harness(managerLimit: 1);
        var campaign = await CampaignAsync(harness, CampaignStatus.Active);
        foreach (var hours in new[] { 4, 3, 2, 1 })
        {
            await LaunchedAsync(harness, campaign, Noon.AddHours(-hours));
        }

        var spent = await RefusalAsync(harness, Request(campaign));

        Assert.Equal(AttemptErrors.LaunchesSpent, spent.Code);
        Assert.Contains(
            $"The manager of campaign '{campaign}' has started 4 runs since the installation's midnight (2026-09-14T00:00:00Z, UTC), "
            + "and the limit Launches:MaxPerDay:manager sets is 1 run a day, so a review asked for now would wait until the next one. "
            + "jason launches list shows the limits as they stand. ",
            spent.Message,
            StringComparison.Ordinal);
    }

    /// <summary>A draft and an archived campaign are refused as a paused one is: only an active campaign is reviewed.</summary>
    [Theory]
    [InlineData(CampaignStatus.Draft)]
    [InlineData(CampaignStatus.Archived)]
    public async Task A_campaign_that_is_not_active_is_refused(CampaignStatus status)
    {
        using var harness = Harness();
        var campaign = await CampaignAsync(harness, status);

        Assert.Equal(("campaign_not_active", 409), await RefusedAsync(harness, Request(campaign)));
    }

    /// <summary>
    /// A check-in held at the manager's daily limit is open, as the summon counts it: a request while it waits is refused
    /// and names it, though the database's one-open index does not see a waiting item.
    /// </summary>
    [Fact]
    public async Task A_check_in_held_at_the_daily_limit_counts_as_open()
    {
        using var harness = Harness();
        var campaign = await CampaignAsync(harness, CampaignStatus.Active);
        var held = await CheckInAsync(harness, campaign, WorkItemStatus.Created);
        await HoldAsync(harness, held);

        var refused = await RefusalAsync(harness, Request(campaign));

        Assert.Equal("review_already_open", refused.Code);
        Assert.Contains(held, refused.Message, StringComparison.Ordinal);
    }

    /// <summary>A day a person lifted holds nothing: the manager's spent launches refuse no review asked for that day.</summary>
    [Fact]
    public async Task A_day_a_person_lifted_refuses_no_review_for_spent_launches()
    {
        using var harness = Harness(managerLimit: 1);
        var campaign = await CampaignAsync(harness, CampaignStatus.Active);
        await LaunchedAsync(harness, campaign, Noon.AddHours(-1));
        await using (var scope = harness.Scopes.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<JasonDbContext>();
            await new LaunchesService(db, new JournalWriter(harness.Clock), harness.Clock, harness.Resolve<InstallationDay>())
                .LiftAsync(new LaunchesLiftRequest(Ada, "a launch day"), Ct);
        }

        var review = await ReviewAsync(harness, Request(campaign));

        Assert.Equal(ManagerCheckIn.Role, review.Role);
    }

    /// <summary>
    /// The item a request makes is the one the summon makes, field by field: a quick look asked for beside a quick look the
    /// cadence summoned, and a deep review asked for beside one the grid summoned. They differ in their campaign, their
    /// moment and the brief's <c>review_intent</c>, and in nothing else; both are root work, and both are check-ins.
    /// </summary>
    [Theory]
    [InlineData(ReviewDepths.Quick, "quick_look")]
    [InlineData(ReviewDepths.Deep, "deep_review")]
    public async Task A_requested_review_is_the_item_the_summon_would_create(string depth, string entry)
    {
        using var harness = Harness();
        var summoned = await CampaignAsync(harness, CampaignStatus.Active, anchor: Noon.AddDays(-1), deepDue: depth == ReviewDepths.Deep ? Noon.AddMinutes(-1) : Noon.AddDays(1));
        var requested = await CampaignAsync(harness, CampaignStatus.Active);
        Assert.Equal(1, await harness.SummonAsync(Ct));

        var answer = await ReviewAsync(harness, Request(requested, depth));

        await using var db = harness.Open();
        var cadence = await db.WorkItems.AsNoTracking().SingleAsync(w => w.Campaign!.PublicId == summoned, Ct);
        var asked = await db.WorkItems.AsNoTracking().SingleAsync(w => w.Campaign!.PublicId == requested, Ct);
        Assert.Equal(asked.PublicId, answer.Id);
        Assert.True(ManagerCheckIn.Is(asked), "A requested review is not a check-in.");
        Assert.Equal(entry, cadence.Entry);
        Assert.Equal(
            (cadence.Kind, cadence.Role, cadence.Operation, cadence.ContactId, cadence.CreatedByType, cadence.CreatedById, cadence.TimeoutSeconds,
                cadence.HeartbeatSeconds, cadence.MaxAttempts, cadence.Priority, cadence.ExecutionProfile, cadence.Status, cadence.Entry),
            (asked.Kind, asked.Role, asked.Operation, asked.ContactId, asked.CreatedByType, asked.CreatedById, asked.TimeoutSeconds,
                asked.HeartbeatSeconds, asked.MaxAttempts, asked.Priority, asked.ExecutionProfile, asked.Status, asked.Entry));
        Assert.Equal(
            (cadence.LineageState, cadence.LineageProfileName, cadence.LineageProfileRevision, cadence.LineageFromAttemptId),
            (asked.LineageState, asked.LineageProfileName, asked.LineageProfileRevision, asked.LineageFromAttemptId));
        Assert.Equal(LineageState.Root, asked.LineageState);
        Assert.Equal(cadence.ResultFormat!.ToJsonString(), asked.ResultFormat!.ToJsonString());
        Assert.Equal(cadence.Context.Select(pair => pair.Key), asked.Context.Select(pair => pair.Key));
        Assert.Equal(cadence.Context["allowed_operations"]!.ToJsonString(), asked.Context["allowed_operations"]!.ToJsonString());
        Assert.Equal(cadence.Context["escalation"]!.ToJsonString(), asked.Context["escalation"]!.ToJsonString());
        Assert.Equal("scheduled", (string?)cadence.Context["review_intent"]);
        Assert.Equal("requested", (string?)asked.Context["review_intent"]);
    }

    /// <summary>With no depth named, the review asked for is a quick look.</summary>
    [Fact]
    public async Task With_no_depth_named_it_is_a_quick_look()
    {
        using var harness = Harness();
        var campaign = await CampaignAsync(harness, CampaignStatus.Active);

        var review = await ReviewAsync(harness, new CampaignReviewRequest(campaign, null, Ada, null));

        Assert.Equal("quick_look", review.Entry);
        Assert.Equal(ReviewDepths.Quick, (await RequestLineAsync(harness)).Key);
    }

    /// <summary>A depth that is neither of the two is a field's problem, refused before anything is read.</summary>
    [Fact]
    public async Task A_depth_that_is_neither_quick_nor_deep_is_refused_at_its_field()
    {
        using var harness = Harness();
        var campaign = await CampaignAsync(harness, CampaignStatus.Active);

        var refused = await RefusalAsync(harness, Request(campaign, "thorough"));

        Assert.Equal("validation_failed", refused.Code);
        Assert.Equal("depth", Assert.Single(refused.Details!).Field);
    }

    /// <summary>
    /// The two clocks: a deep review asked for moves the quick look's anchor, as every check-in does, and leaves the deep
    /// review's grid alone, while a deep review the grid summoned moves the grid on, so the daily one still comes at its
    /// moment beside the one a person asked for.
    /// </summary>
    [Fact]
    public async Task A_requested_deep_review_leaves_the_grid_alone_and_moves_the_quick_anchor_while_a_summoned_one_moves_the_grid()
    {
        using var harness = Harness();
        var due = Noon.AddMinutes(-1);
        var requested = await CampaignAsync(harness, CampaignStatus.Active, anchor: Noon.AddHours(-2), deepDue: due);
        var summoned = await CampaignAsync(harness, CampaignStatus.Active, anchor: Noon.AddHours(-2), deepDue: due);

        var asked = await ReviewAsync(harness, Request(requested, ReviewDepths.Deep));
        Assert.Equal(1, await harness.SummonAsync(Ct));

        await using (var db = harness.Open())
        {
            var read = await db.Campaigns.AsNoTracking().SingleAsync(c => c.PublicId == requested, Ct);
            Assert.Equal(((DateTime?)due, (DateTime?)Noon), (read.DeepReviewDueAt, read.ManagerReviewAnchor));
            var cadence = await db.Campaigns.AsNoTracking().SingleAsync(c => c.PublicId == summoned, Ct);
            Assert.Equal(((DateTime?)due.AddDays(1), (DateTime?)Noon), (cadence.DeepReviewDueAt, cadence.ManagerReviewAnchor));
            Assert.Equal("deep_review", (await db.WorkItems.AsNoTracking().SingleAsync(w => w.Campaign!.PublicId == summoned, Ct)).Entry);
        }

        // The daily deep review still runs: once the one a person asked for has ended, the grid due summons its own, which
        // moves the grid on as a summoned one does.
        await FinishAsync(harness, asked.Id);
        harness.Clock.Now = new DateTimeOffset(Noon.AddMinutes(20));
        Assert.Equal(1, await harness.SummonAsync(Ct));

        await using var after = harness.Open();
        var daily = await after.WorkItems.AsNoTracking()
            .Where(w => w.Campaign!.PublicId == requested && w.PublicId != asked.Id)
            .SingleAsync(Ct);
        Assert.Equal(("deep_review", "scheduled"), (daily.Entry, (string?)daily.Context["review_intent"]));
        Assert.Equal((DateTime?)due.AddDays(1), (await after.Campaigns.AsNoTracking().SingleAsync(c => c.PublicId == requested, Ct)).DeepReviewDueAt);
    }

    /// <summary>A quick look asked for moves the anchor too, and leaves the grid as it was.</summary>
    [Fact]
    public async Task A_requested_quick_look_moves_the_anchor_and_leaves_the_grid()
    {
        using var harness = Harness();
        var grid = Noon.AddHours(10);
        var campaign = await CampaignAsync(harness, CampaignStatus.Active, anchor: Noon.AddHours(-2), deepDue: grid);

        await ReviewAsync(harness, Request(campaign, ReviewDepths.Quick));

        await using var db = harness.Open();
        var read = await db.Campaigns.AsNoTracking().SingleAsync(c => c.PublicId == campaign, Ct);
        Assert.Equal(((DateTime?)Noon, (DateTime?)grid), (read.ManagerReviewAnchor, read.DeepReviewDueAt));
    }

    /// <summary>
    /// Two requests at once make one check-in: the first is answered with it, and the second is refused
    /// <c>review_already_open</c>, naming it. Each runs in a scope and a connection of its own.
    /// </summary>
    [Fact]
    public async Task Two_requests_at_once_give_one_item_and_one_review_already_open()
    {
        using var harness = Harness();
        var campaign = await CampaignAsync(harness, CampaignStatus.Active);

        var outcomes = await Task.WhenAll(
            Task.Run(() => OutcomeAsync(harness, Request(campaign)), Ct),
            Task.Run(() => OutcomeAsync(harness, Request(campaign, ReviewDepths.Deep)), Ct));

        var made = Assert.Single(outcomes, outcome => outcome.Item is not null).Item!;
        var refused = Assert.Single(outcomes, outcome => outcome.Refusal is not null).Refusal!;
        Assert.Equal("review_already_open", refused.Code);
        Assert.Contains(made.Id, refused.Message, StringComparison.Ordinal);
        Assert.Equal(1, await CheckInCountAsync(harness));
        Assert.Equal(1, await RequestLinesAsync(harness));
    }

    /// <summary>
    /// A check-in the summon made just before the request's transaction began is the one open, and the request is refused
    /// naming it: the other writer arrives between the request and its write, by construction rather than by timing.
    /// </summary>
    [Fact]
    public async Task A_check_in_made_by_another_writer_just_before_is_named_by_the_refusal()
    {
        using var harness = Harness();
        var campaign = await CampaignAsync(harness, CampaignStatus.Active);
        const string Other = "wi_01K7QN00000000000000000OTH";
        int campaignId;
        await using (var db = harness.Open())
        {
            campaignId = (await db.Campaigns.SingleAsync(c => c.PublicId == campaign, Ct)).Id;
        }

        var competitor = new CompetingInsert(
            harness.DatabaseFile,
            _ => $$"""
                INSERT INTO work_items (public_id, campaign_id, kind, role, status, priority, created_by_type, created_by_id, context_json, attempt_count, created_at, updated_at, lineage_state)
                VALUES ('{{Other}}', {{campaignId}}, 'ai_role', 'manager', 'created', 0, 'system', 'dispatcher', '{}', 0, '2026-09-14 11:59:00', '2026-09-14 11:59:00', 'root')
                """);

        var refused = await RefusalAsync(harness, Request(campaign), competitor);

        Assert.True(competitor.Fired, "The other writer never arrived.");
        Assert.Equal("review_already_open", refused.Code);
        Assert.Contains(Other, refused.Message, StringComparison.Ordinal);
        Assert.Equal(0, await RequestLinesAsync(harness));
    }

    /// <summary>
    /// The request's line: <c>review_requested</c>, under the person who asked and with their reason, keyed by the depth,
    /// naming the campaign and the check-in, and carrying the check-in and its entry. The check-in's own creation line is
    /// the dispatcher's, as a summoned one's is.
    /// </summary>
    [Fact]
    public async Task The_request_line_names_the_person_and_their_reason()
    {
        using var harness = Harness();
        var campaign = await CampaignAsync(harness, CampaignStatus.Active);

        var review = await ReviewAsync(harness, Request(campaign, ReviewDepths.Deep, "Before the board meeting."));

        var line = await RequestLineAsync(harness);
        Assert.Equal((ActorType.Human, "ada", "Before the board meeting."), (line.ActorType, line.ActorId, line.Reason));
        Assert.Equal(ReviewDepths.Deep, line.Key);
        Assert.Null(line.Old);
        Assert.Equal($$"""{"work_item_id":"{{review.Id}}","entry":"deep_review"}""", line.New!.ToJsonString());
        Assert.Equal(review.Id, line.WorkItemId);
        await using var db = harness.Open();
        Assert.Equal(campaign, (await db.Campaigns.AsNoTracking().SingleAsync(c => c.Id == line.CampaignId, Ct)).PublicId);
        var created = await db.Journal.AsNoTracking().SingleAsync(e => e.WorkItemId == review.Id && e.Kind == JournalKinds.WorkItemCreated, Ct);
        Assert.Equal((ActorType.System, "dispatcher"), (created.ActorType, created.ActorId));
    }

    /// <summary>
    /// The request's line summons nothing: once the check-in it made has ended, a scan with nothing due creates no review.
    /// And the check-in is the summon's one open: while a second request's check-in is open, a line of a trigger kind
    /// summons no review beside it, and summons the next one once it has ended.
    /// </summary>
    [Fact]
    public async Task The_request_summons_nothing_and_its_check_in_is_the_one_open()
    {
        using var harness = Harness();
        var campaign = await CampaignAsync(harness, CampaignStatus.Active, anchor: Noon.AddHours(-1), deepDue: Noon.AddHours(10));
        var first = await ReviewAsync(harness, Request(campaign));
        await FinishAsync(harness, first.Id);

        Assert.Equal(0, await harness.SummonAsync(Ct));

        var second = await ReviewAsync(harness, Request(campaign));
        await TriggerAsync(harness, campaign);
        Assert.Equal(0, await harness.SummonAsync(Ct));

        await FinishAsync(harness, second.Id);
        Assert.Equal(1, await harness.SummonAsync(Ct));
        await using var read = harness.Open();
        var next = await read.WorkItems.AsNoTracking().Where(w => w.Role == ManagerCheckIn.Role).OrderByDescending(w => w.Id).FirstAsync(Ct);
        Assert.Equal("triggered", (string?)next.Context["review_intent"]);
        Assert.Equal(JournalKinds.ExternalEffectReported, (string?)next.Context["trigger"]);
    }

    /// <summary>A line of a default trigger kind about the campaign, as a report of an effect outside Jason writes it.</summary>
    private static async Task TriggerAsync(DispatchHarness harness, string campaignPublicId)
    {
        await using var db = harness.Open();
        var campaign = await db.Campaigns.SingleAsync(c => c.PublicId == campaignPublicId, Ct);
        var (_, item, attempt) = await DocumentedLines.StandInsAsync(db, JournalKinds.ExternalEffectReported, Noon, Ct, campaign);
        DocumentedLines.AppendDocumented(new JournalWriter(harness.Clock), db, Ada, JournalKinds.ExternalEffectReported, campaign, workItem: item, attempt: attempt);
        await db.SaveChangesAsync(Ct);
    }

    private static DispatchHarness Harness(int? managerLimit = null)
    {
        var harness = new DispatchHarness(Noon);
        if (managerLimit is { } limit)
        {
            harness.Launches.CurrentValue = new LaunchesOptions { MaxPerDay = new Dictionary<string, int>(StringComparer.Ordinal) { [ManagerCheckIn.Role] = limit } };
        }

        return harness;
    }

    private static CampaignReviewRequest Request(string campaign, string? depth = null, string? reason = null) =>
        new(campaign, depth, Ada, reason);

    /// <summary>
    /// One request, in a scope of its own, through the service as the runtime composes it: over the scope's context, or,
    /// where <paramref name="competitor"/> is given, over a context of the same database that carries it, which the work
    /// item service the request creates its check-in through shares.
    /// </summary>
    private static async Task<WorkItemDto> ReviewAsync(DispatchHarness harness, CampaignReviewRequest request, CompetingInsert? competitor = null)
    {
        await using var scope = harness.Scopes.CreateAsyncScope();
        var services = scope.ServiceProvider;
        if (competitor is null)
        {
            return await ActivatorUtilities.CreateInstance<CampaignReviews>(services).ReviewAsync(request, Ct);
        }

        var options = new DbContextOptionsBuilder<JasonDbContext>();
        JasonDbContext.Configure(options, harness.DatabaseFile, JournalLines.Strict);
        options.AddInterceptors(competitor);
        await using var competing = new JasonDbContext(options.Options);
        var items = ActivatorUtilities.CreateInstance<WorkItemService>(services, competing);
        return await ActivatorUtilities.CreateInstance<CampaignReviews>(services, competing, items).ReviewAsync(request, Ct);
    }

    private static async Task<(string Code, int)> RefusedAsync(DispatchHarness harness, CampaignReviewRequest request)
    {
        var refused = await RefusalAsync(harness, request);
        return (refused.Code, refused.StatusCode);
    }

    private static async Task<DomainException> RefusalAsync(DispatchHarness harness, CampaignReviewRequest request, CompetingInsert? competitor = null) =>
        await Assert.ThrowsAnyAsync<DomainException>(() => ReviewAsync(harness, request, competitor));

    private static async Task<(WorkItemDto? Item, DomainException? Refusal)> OutcomeAsync(DispatchHarness harness, CampaignReviewRequest request)
    {
        try
        {
            return (await ReviewAsync(harness, request), null);
        }
        catch (DomainException refused)
        {
            return (null, refused);
        }
    }

    private static async Task<string> CampaignAsync(DispatchHarness harness, CampaignStatus status, DateTime? anchor = null, DateTime? deepDue = null)
    {
        await using var db = harness.Open();
        var campaign = WorkItemFactory.NewCampaign(status: status, now: Noon.AddDays(-2));
        campaign.ManagerReviewAnchor = anchor ?? (status == CampaignStatus.Active ? Noon.AddHours(-1) : null);
        campaign.DeepReviewDueAt = deepDue ?? (status == CampaignStatus.Active ? Noon.AddHours(10) : null);
        db.Campaigns.Add(campaign);
        await db.SaveChangesAsync(Ct);
        return campaign.PublicId;
    }

    private static async Task StatusAsync(DispatchHarness harness, string campaign, CampaignStatus status)
    {
        await using var db = harness.Open();
        (await db.Campaigns.SingleAsync(c => c.PublicId == campaign, Ct)).Status = status;
        await db.SaveChangesAsync(Ct);
    }

    /// <summary>A check-in of the campaign, as the summon leaves one, in <paramref name="status"/>.</summary>
    private static async Task<string> CheckInAsync(DispatchHarness harness, string campaignPublicId, WorkItemStatus status)
    {
        await using var db = harness.Open();
        var campaign = await db.Campaigns.SingleAsync(c => c.PublicId == campaignPublicId, Ct);
        var item = WorkItemFactory.NewAiRole(campaign, role: ManagerCheckIn.Role, now: Noon.AddMinutes(-30), configure: w =>
        {
            w.CreatedByType = ActorType.System;
            w.CreatedById = "dispatcher";
            w.Status = status;
        });
        db.WorkItems.Add(item);
        await db.SaveChangesAsync(Ct);
        return item.PublicId;
    }

    /// <summary>Holds a check-in at the manager's daily limit, by the one routine that holds.</summary>
    private static async Task HoldAsync(DispatchHarness harness, string checkIn)
    {
        await using var scope = harness.Scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<JasonDbContext>();
        var item = await db.WorkItems.SingleAsync(w => w.PublicId == checkIn, Ct);
        var day = harness.Resolve<InstallationDay>().Of(Noon);
        scope.ServiceProvider.GetRequiredService<AttemptOutcomes>()
            .Hold(db, item, day.End, Claimer.LaunchesSpent(ManagerCheckIn.Role, 1, 1, day), 1, Actors.Dispatcher);
        await db.SaveChangesAsync(Ct);
    }

    private static async Task FinishAsync(DispatchHarness harness, string itemPublicId)
    {
        await using var db = harness.Open();
        var item = await db.WorkItems.SingleAsync(w => w.PublicId == itemPublicId, Ct);
        item.Status = WorkItemStatus.Succeeded;
        item.FinishedAt = Noon;
        await db.SaveChangesAsync(Ct);
    }

    /// <summary>One finished manager item of the campaign whose one attempt started at <paramref name="started"/>: a launch its day counts.</summary>
    private static async Task LaunchedAsync(DispatchHarness harness, string campaignPublicId, DateTime started)
    {
        await using var db = harness.Open();
        var campaign = await db.Campaigns.SingleAsync(c => c.PublicId == campaignPublicId, Ct);
        var item = WorkItemFactory.NewAiRole(campaign, role: ManagerCheckIn.Role, now: started);
        var run = WorkItemFactory.NewAttempt(item, 1, AttemptStatus.Succeeded, started);
        run.StartedAt = started;
        run.FinishedAt = started.AddMinutes(5);
        item.Status = WorkItemStatus.Succeeded;
        item.AttemptCount = 1;
        db.WorkItems.Add(item);
        await db.SaveChangesAsync(Ct);
    }

    private static async Task<int> LinesAsync(DispatchHarness harness)
    {
        await using var db = harness.Open();
        return await db.Journal.CountAsync(Ct);
    }

    private static async Task<int> CheckInCountAsync(DispatchHarness harness)
    {
        await using var db = harness.Open();
        return await db.WorkItems.CountAsync(w => w.Role == ManagerCheckIn.Role && w.CreatedByType == ActorType.System, Ct);
    }

    private static async Task<int> RequestLinesAsync(DispatchHarness harness)
    {
        await using var db = harness.Open();
        return await db.Journal.CountAsync(e => e.Kind == JournalKinds.ReviewRequested, Ct);
    }

    private static async Task<JournalEntry> RequestLineAsync(DispatchHarness harness)
    {
        await using var db = harness.Open();
        return await db.Journal.AsNoTracking().SingleAsync(e => e.Kind == JournalKinds.ReviewRequested, Ct);
    }
}
