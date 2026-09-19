using System.Text.Json.Nodes;
using Jason.Contracts.Api;
using Jason.Runtime.Decisions;
using Jason.Runtime.Domain;
using Jason.Runtime.Journal;
using Jason.Runtime.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Jason.Runtime.Tests.Decisions;

/// <summary>
/// Asking. A role that cannot decide something for itself leaves the question behind and ends its attempt;
/// what is guarded here is who may leave one, and that what it leaves points at rows that exist.
/// </summary>
public class DecisionRaiseTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly DateTime Noon = new(2026, 9, 14, 12, 0, 0, DateTimeKind.Utc);

    /// <summary>
    /// The same fence <c>workitem.set_result</c> stands behind, because it is the same statement: a role whose
    /// lease was lost is told to stop rather than allowed to put a question in somebody's queue.
    /// </summary>
    [Fact]
    public async Task An_attempt_that_is_not_the_live_one_cannot_raise()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var seed = await SeedAsync(db);

        var refused = await Assert.ThrowsAsync<ConflictException>(() => Service(db).RaiseAsync(
            new DecisionRaiseRequest(seed.Item.PublicId, "att_somebody_else", "what now?", null, null, null), Ct));

        Assert.Equal("stale_attempt", refused.Code);
        Assert.Empty(await db.Decisions.ToListAsync(Ct));
    }

    /// <summary>
    /// The row a question becomes: the campaign, the item and the attempt that asked, and one chronicle line
    /// naming it. The line's actor is the attempt, because that is who asked.
    /// </summary>
    [Fact]
    public async Task A_raised_decision_names_the_campaign_the_item_and_the_attempt_and_journals_it()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var seed = await SeedAsync(db);

        var raised = await Service(db).RaiseAsync(
            new DecisionRaiseRequest(
                seed.Item.PublicId,
                seed.Attempt.PublicId,
                "  do we keep calling this account?  ",
                [new DecisionOption("keep going", "two more touches"), new DecisionOption("stop", null)],
                [new DecisionReference(DecisionReferenceKind.WorkItem, seed.Item.PublicId)],
                "the brief says ask before a fourth touch"),
            Ct);

        Assert.StartsWith("dec_", raised.Id, StringComparison.Ordinal);
        Assert.Equal(DecisionStatus.Pending, raised.Status);
        Assert.Equal(seed.Campaign.PublicId, raised.CampaignId);
        Assert.Equal(seed.Item.PublicId, raised.WorkItemId);
        Assert.Equal(seed.Attempt.PublicId, raised.AttemptId);
        Assert.Equal("do we keep calling this account?", raised.Question);
        Assert.Equal("two more touches", raised.Options![0].Detail);
        Assert.Equal(seed.Item.PublicId, raised.References![0].Id);
        Assert.Null(raised.Answer);

        await using var fresh = database.Open();
        var line = await fresh.Journal.AsNoTracking().SingleAsync(e => e.Kind == JournalKinds.DecisionRaised, Ct);
        Assert.Equal(ActorType.Attempt, line.ActorType);
        Assert.Equal(seed.Attempt.PublicId, line.ActorId);
        Assert.Equal(seed.Item.PublicId, line.WorkItemId);
        Assert.Equal(raised.Id, (string?)line.New!["decision_id"]);

        // The question itself is never in the chronicle: the row holds what was asked, and the chronicle is
        // the one table nobody can edit afterwards.
        Assert.DoesNotContain("calling this account", line.New!.ToJsonString(), StringComparison.Ordinal);
    }

    /// <summary>
    /// A reference that does not resolve to this campaign's own rows is refused. P5 asserts that the causal
    /// references still resolve when somebody reads them later, and that is only worth asserting if the
    /// runtime ever looked.
    /// </summary>
    [Fact]
    public async Task A_reference_that_is_not_this_campaigns_own_is_refused()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var seed = await SeedAsync(db);

        var elsewhere = WorkItemFactory.NewCampaign(now: Noon);
        elsewhere.PublicId = "cmp_elsewhere";
        var theirs = WorkItemFactory.NewAiRole(elsewhere, now: Noon);
        db.Campaigns.Add(elsewhere);
        db.WorkItems.Add(theirs);
        await db.SaveChangesAsync(Ct);

        var refused = await Assert.ThrowsAsync<InvalidRequestException>(() => Service(db).RaiseAsync(
            new DecisionRaiseRequest(
                seed.Item.PublicId,
                seed.Attempt.PublicId,
                "what now?",
                null,
                [new DecisionReference(DecisionReferenceKind.WorkItem, theirs.PublicId)],
                null),
            Ct));

        Assert.Equal("decision_reference_unresolved", refused.Code);
        Assert.Contains(theirs.PublicId, refused.Message, StringComparison.Ordinal);
        Assert.Empty(await db.Decisions.ToListAsync(Ct));
    }

    [Fact]
    public async Task A_question_past_the_bound_is_refused_before_anything_is_written()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var seed = await SeedAsync(db);

        var refused = await Assert.ThrowsAsync<ValidationException>(() => Service(db).RaiseAsync(
            new DecisionRaiseRequest(
                seed.Item.PublicId,
                seed.Attempt.PublicId,
                new string('x', DecisionLimits.MaxQuestionLength + 1),
                null,
                null,
                null),
            Ct));

        Assert.Equal("question", refused.Details![0].Field);
        Assert.Equal("too_long", refused.Details![0].Code);
        Assert.Empty(await db.Decisions.ToListAsync(Ct));
    }

    /// <summary>
    /// Two options with the same label. What is recorded when somebody chooses is the label, so two identical
    /// ones would record an answer nobody could read back.
    /// </summary>
    [Fact]
    public async Task Two_options_with_the_same_label_are_refused()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var seed = await SeedAsync(db);

        var refused = await Assert.ThrowsAsync<ValidationException>(() => Service(db).RaiseAsync(
            new DecisionRaiseRequest(
                seed.Item.PublicId,
                seed.Attempt.PublicId,
                "which?",
                [new DecisionOption("stop", null), new DecisionOption("stop", "again")],
                null,
                null),
            Ct));

        Assert.Equal("duplicate", refused.Details![0].Code);
        Assert.Empty(await db.Decisions.ToListAsync(Ct));
    }

    /// <summary>
    /// Every kind in the vocabulary really resolves. Only two of the five were exercised, which is how a kind
    /// that matched nothing — or everything — would have shipped unnoticed: a reference is checked once, at
    /// the moment of asking, and after that nothing looks at it again.
    /// </summary>
    [Fact]
    public async Task Every_reference_kind_resolves_against_this_campaigns_own_rows()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var seed = await SeedAsync(db);
        var (entry, report, approval) = await AlsoInThisCampaignAsync(db, seed);

        var raised = await Service(db).RaiseAsync(
            new DecisionRaiseRequest(
                seed.Item.PublicId,
                seed.Attempt.PublicId,
                "which of these?",
                null,
                [
                    new DecisionReference(DecisionReferenceKind.WorkItem, seed.Item.PublicId),
                    new DecisionReference(DecisionReferenceKind.Attempt, seed.Attempt.PublicId),
                    new DecisionReference(DecisionReferenceKind.JournalEntry, entry),
                    new DecisionReference(DecisionReferenceKind.Report, report),
                    new DecisionReference(DecisionReferenceKind.Approval, approval),
                ],
                null),
            Ct);

        Assert.Equal(5, raised.References!.Count);
    }

    /// <summary>And each of them refuses a row that belongs to somebody else's campaign.</summary>
    [Theory]
    [InlineData(DecisionReferenceKind.Attempt)]
    [InlineData(DecisionReferenceKind.JournalEntry)]
    [InlineData(DecisionReferenceKind.Approval)]
    public async Task A_reference_of_any_kind_that_is_not_this_campaigns_is_refused(DecisionReferenceKind kind)
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var seed = await SeedAsync(db);
        var elsewhere = await SeedAsync(db, "cmp_elsewhere", "att_elsewhere", "wi_elsewhere");
        var (entry, _, approval) = await AlsoInThisCampaignAsync(db, elsewhere);

        var id = kind switch
        {
            DecisionReferenceKind.Attempt => elsewhere.Attempt.PublicId,
            DecisionReferenceKind.JournalEntry => entry,
            _ => approval,
        };

        var refused = await Assert.ThrowsAsync<InvalidRequestException>(() => Service(db).RaiseAsync(
            new DecisionRaiseRequest(seed.Item.PublicId, seed.Attempt.PublicId, "which?", null, [new DecisionReference(kind, id)], null),
            Ct));

        Assert.Equal("decision_reference_unresolved", refused.Code);
    }

    /// <summary>
    /// A reference whose kind was never typed. Left to the serializer it becomes the first kind in the
    /// vocabulary — a work item, because that is where an enum starts — so a caller who forgot the field
    /// would get a lookup against the wrong table rather than an answer about what they left out.
    /// </summary>
    [Fact]
    public async Task A_reference_with_no_kind_is_refused_rather_than_guessed()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var seed = await SeedAsync(db);

        var refused = await Assert.ThrowsAsync<ValidationException>(() => Service(db).RaiseAsync(
            new DecisionRaiseRequest(
                seed.Item.PublicId,
                seed.Attempt.PublicId,
                "which?",
                null,
                [new DecisionReference(null, seed.Item.PublicId)],
                null),
            Ct));

        Assert.Equal("references[0].kind", refused.Details![0].Field);
        Assert.Equal("required", refused.Details[0].Code);
    }

    [Fact]
    public async Task An_option_detail_past_its_bound_is_refused()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var seed = await SeedAsync(db);
        var options = new[] { new DecisionOption("stop", new string('x', DecisionLimits.MaxOptionDetailLength + 1)) };

        var refused = await Assert.ThrowsAsync<ValidationException>(() => Service(db).RaiseAsync(
            new DecisionRaiseRequest(seed.Item.PublicId, seed.Attempt.PublicId, "which?", options, null, null), Ct));

        Assert.Equal("options[0]", refused.Details![0].Field);
        Assert.Equal("too_long", refused.Details[0].Code);
    }

    /// <summary>Raising is a fenced call like any other, so it says the role is still alive.</summary>
    [Fact]
    public async Task Raising_a_question_moves_the_lease_the_way_recording_a_result_does()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var seed = await SeedAsync(db);
        Assert.Null(seed.Attempt.LastHeartbeatAt);

        await Service(db).RaiseAsync(
            new DecisionRaiseRequest(seed.Item.PublicId, seed.Attempt.PublicId, "what now?", null, null, null), Ct);

        await using var fresh = database.Open();
        Assert.Equal(Noon, (await fresh.Attempts.AsNoTracking().SingleAsync(Ct)).LastHeartbeatAt);
    }

    private static DecisionService Service(JasonDbContext db)
    {
        var clock = new FixedClock(Noon);
        return new DecisionService(db, new JournalWriter(clock), clock);
    }

    /// <summary>One row of every other referenceable kind, belonging to the campaign that was seeded.</summary>
    private static async Task<(string Entry, string Report, string Approval)> AlsoInThisCampaignAsync(JasonDbContext db, Seed seed)
    {
        var entry = new JournalWriter(new FixedClock(Noon)).Append(
            db, new ActorRef(ActorType.Human, "ada"), "observation", seed.Campaign, key: "note");

        var report = new Report
        {
            PublicId = "rpt_" + seed.Campaign.PublicId,
            ReporterType = ActorType.Human,
            ReporterId = "ada",
            Effect = "email_sent",
            Tool = "reply-cli",
            CampaignId = seed.Campaign.Id,
            Summary = "sent by hand",
            Assertion = new JsonObject(),
            AssertionHash = "sha256:none",
            ReceivedAt = Noon,
        };

        var approval = new Approval
        {
            PublicId = "apr_" + seed.Campaign.PublicId,
            WorkItemId = seed.Item.Id,
            CampaignId = seed.Campaign.Id,
            Operation = "campaign.enroll",
            Subject = new JsonObject(),
            SubjectHash = "sha256:none",
            Preview = new JsonObject(),
            PluginId = "fake",
            PluginSnapshotId = "snp_1",
            RoutingSnapshotId = "rts_1",
            Reason = "approval_required",
            RequestedAt = Noon,
        };

        db.Reports.Add(report);
        db.Approvals.Add(approval);
        await db.SaveChangesAsync(Ct);
        return (entry.PublicId, report.PublicId, approval.PublicId);
    }

    private static async Task<Seed> SeedAsync(
        JasonDbContext db,
        string? campaignId = null,
        string attemptId = "att_live",
        string? workItemId = null)
    {
        var campaign = WorkItemFactory.NewCampaign(now: Noon);
        if (campaignId is not null)
        {
            campaign.PublicId = campaignId;
        }

        var item = WorkItemFactory.NewAiRole(campaign, now: Noon);
        if (workItemId is not null)
        {
            item.PublicId = workItemId;
        }

        item.Status = WorkItemStatus.Processing;
        var attempt = new Attempt
        {
            PublicId = attemptId,
            WorkItem = item,
            Number = 1,
            Status = AttemptStatus.Running,
            StartedAt = Noon,
            LockUntil = Noon.AddMinutes(30),
        };

        db.Campaigns.Add(campaign);
        db.WorkItems.Add(item);
        db.Attempts.Add(attempt);
        await db.SaveChangesAsync(Ct);
        return new Seed(campaign, item, attempt);
    }

    private sealed record Seed(Campaign Campaign, WorkItem Item, Attempt Attempt);
}
