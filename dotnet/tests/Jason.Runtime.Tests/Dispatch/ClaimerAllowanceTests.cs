using System.Text.Json.Nodes;
using Jason.Contracts.Api;
using Jason.Runtime.Approvals;
using Jason.Runtime.Domain;
using Jason.Runtime.Journal;
using Jason.Runtime.Persistence;
using Jason.Runtime.WorkItems;
using Microsoft.EntityFrameworkCore;

namespace Jason.Runtime.Tests.Dispatch;

/// <summary>
/// The allowance at the claim. A search spends the provider's credits, so its first search under an audience — the probe —
/// waits for a person, and after it the person's allowance, named by the audience and capped in people, lets the top-ups
/// go without a question until the people they asked for reach the ceiling. Only the person's acts move the allowance:
/// approving a search under another audience retires it, in the same act.
/// </summary>
public partial class ClaimerTests
{
    /// <summary>
    /// A top-up under the audience the allowance names runs in the claim that finds it: its row is approved, decided by the
    /// person who set the allowance and asked of nobody, and one line names the allowance, the audience and the people the
    /// search asks for, which is what the allowance counts. The count is the number the search asks for however it was
    /// written: <c>50.0</c> is the whole number the schema took it for.
    /// </summary>
    [Fact]
    public async Task Allowance_TopUpUnderIt_RunsUnasked_AndIsCountedInPeople()
    {
        using var harness = SearchHarness();
        using var database = new TestDatabase();
        await using var db = database.Open();
        var spring = await SpringAsync(harness, db);
        var allowance = await AllowAsync(db, spring, 1);
        var topUp = Search(db, spring, new JsonObject { ["audience"] = 1, ["count"] = JsonNode.Parse("50.0") });
        await db.SaveChangesAsync(Ct);

        var run = Assert.Single(await ClaimSettledAsync(harness, db));

        Assert.Equal(topUp.PublicId, run.WorkItemPublicId);
        await using var fresh = database.Open();
        var approval = await fresh.Approvals.AsNoTracking().SingleAsync(a => a.WorkItemId == topUp.Id, Ct);
        Assert.Equal((ApprovalStatus.Approved, ApprovalGate.StandingApproval), (approval.Status, approval.Reason));
        Assert.Equal((ActorType.Human, "operator"), (approval.DecidedByType, approval.DecidedById));
        var software = await fresh.Audiences.AsNoTracking().SingleAsync(a => a.CampaignId == spring.Id && a.Number == 1, Ct);
        var applied = Assert.Single(await fresh.Journal.AsNoTracking().Where(e => e.Kind == JournalKinds.ApprovalStandingApplied).ToListAsync(Ct));
        Assert.Equal((ActorType.System, "dispatcher", allowance.LineId, topUp.PublicId), (applied.ActorType, applied.ActorId, applied.Key, applied.WorkItemId));
        Assert.Equal(
            new JsonObject
            {
                ["id"] = approval.PublicId,
                ["operation"] = "audience.search",
                ["subject_hash"] = approval.SubjectHash,
                ["standing"] = allowance.LineId,
                ["verdict"] = null,
                ["category"] = null,
                ["audience"] = new JsonObject { ["id"] = software.PublicId, ["number"] = 1 },
                ["count"] = 50,
            }.ToJsonString(),
            applied.New!.ToJsonString());
        Assert.Empty(await fresh.Journal.AsNoTracking().Where(e => e.Kind == JournalKinds.ApprovalRequested && e.WorkItemId == topUp.PublicId).ToListAsync(Ct));
        var used = await StandingApprovals.AllowanceAsync(fresh, spring.Id, Ct);
        Assert.Equal((1, 50), (used!.Released, used.People));
    }

    /// <summary>
    /// A search the allowance does not cover parks for a person, as a probe always does, and the preview the person reads
    /// says what the allowance is — its line, its audience, its limits and the people already asked for — how many this
    /// search asks for, and the first thing that failed: there is none; it names another audience, as a new audience's
    /// probe always finds; it expired; the search asks for more than one search may; or the people would pass the ceiling.
    /// </summary>
    [Theory]
    [InlineData(StandingApprovals.Why.NoStandingApproval)]
    [InlineData(StandingApprovals.Why.AnotherAudience)]
    [InlineData(StandingApprovals.Why.Expired)]
    [InlineData(StandingApprovals.Why.OverPerSearch)]
    [InlineData(StandingApprovals.Why.CeilingReached)]
    public async Task Allowance_SearchItDoesNotCover_Parks_PreviewSaysWhy(string why)
    {
        using var harness = SearchHarness();
        using var database = new TestDatabase();
        await using var db = database.Open();
        var spring = await SpringAsync(harness, db);
        var allowance = why switch
        {
            StandingApprovals.Why.NoStandingApproval => null,
            StandingApprovals.Why.Expired => await AllowAsync(db, spring, 1, expiresAt: new DateTimeOffset(Noon.AddMinutes(-1))),
            StandingApprovals.Why.OverPerSearch => await AllowAsync(db, spring, 1, maxPerSearch: 40),
            StandingApprovals.Why.CeilingReached => await AllowAsync(db, spring, 1, maxPeople: 100),
            _ => await AllowAsync(db, spring, 1),
        };
        if (why == StandingApprovals.Why.CeilingReached)
        {
            // Sixty of the hundred, spent by a top-up the allowance let go.
            Search(db, spring, new JsonObject { ["audience"] = 1, ["count"] = 60 });
            await db.SaveChangesAsync(Ct);
            Assert.Single(await ClaimSettledAsync(harness, db));
        }

        var arguments = why is StandingApprovals.Why.NoStandingApproval or StandingApprovals.Why.AnotherAudience
            ? new JsonObject { ["definition"] = InRetail, ["count"] = 50 }
            : new JsonObject { ["audience"] = 1, ["count"] = 50 };
        var search = Search(db, spring, arguments);
        await db.SaveChangesAsync(Ct);

        Assert.Empty(await ClaimSettledAsync(harness, db));

        await using var fresh = database.Open();
        Assert.Equal(WorkItemStatus.AwaitingApproval, (await fresh.WorkItems.AsNoTracking().SingleAsync(w => w.Id == search.Id, Ct)).Status);
        Assert.Empty(await fresh.Attempts.AsNoTracking().Where(a => a.WorkItemId == search.Id).ToListAsync(Ct));
        var waiting = await fresh.Approvals.AsNoTracking().SingleAsync(a => a.WorkItemId == search.Id, Ct);
        Assert.Equal((ApprovalStatus.Pending, ApprovalGate.ApprovalRequired), (waiting.Status, waiting.Reason));
        var software = await fresh.Audiences.AsNoTracking().SingleAsync(a => a.CampaignId == spring.Id && a.Number == 1, Ct);
        Assert.Equal(
            new JsonObject
            {
                ["standing"] = allowance?.LineId,
                ["audience"] = allowance is null ? null : new JsonObject { ["id"] = software.PublicId, ["number"] = 1 },
                ["expires_at"] = why == StandingApprovals.Why.Expired ? "2026-09-14T11:59:00.000Z" : null,
                ["max_people"] = allowance?.MaxPeople,
                ["max_per_search"] = allowance?.MaxPerSearch,
                ["people"] = allowance is null ? null : why == StandingApprovals.Why.CeilingReached ? 60 : 0,
                ["count"] = 50,
                ["why"] = why,
            }.ToJsonString(),
            waiting.Preview["standing_approval"]!.ToJsonString());
        Assert.Single(await fresh.Journal.AsNoTracking().Where(e => e.Kind == JournalKinds.ApprovalRequested && e.WorkItemId == search.PublicId).ToListAsync(Ct));
        Assert.Equal(
            why == StandingApprovals.Why.CeilingReached ? 1 : 0,
            await fresh.Journal.AsNoTracking().CountAsync(e => e.Kind == JournalKinds.ApprovalStandingApplied, Ct));
    }

    /// <summary>
    /// The allowance moves only on the person's act, and only away from its audience. While a new audience's probe waits,
    /// the allowance stands and the old audience's top-ups still run under it; a top-up of its own audience that it did not
    /// cover, approved by the person, leaves it as it is; the person's approval of the probe retires it in the same act —
    /// the approval and an unset, in the person's name, saved together — and the old audience's next top-up waits for a
    /// person.
    /// </summary>
    [Fact]
    public async Task Allowance_ApprovingAnotherAudiencesProbe_RetiresItInTheSameAct()
    {
        using var harness = SearchHarness();
        using var database = new TestDatabase();
        await using var db = database.Open();
        var spring = await SpringAsync(harness, db);
        var allowance = await AllowAsync(db, spring, 1, maxPerSearch: 60);
        var probe = Search(db, spring, new JsonObject { ["definition"] = InRetail, ["count"] = 20 });
        await db.SaveChangesAsync(Ct);
        Assert.Empty(await ClaimSettledAsync(harness, db));
        var meanwhile = Search(db, spring, new JsonObject { ["audience"] = 1, ["count"] = 50 });
        await db.SaveChangesAsync(Ct);
        Assert.Equal([meanwhile.PublicId], (await ClaimSettledAsync(harness, db)).Select(run => run.WorkItemPublicId));
        var larger = Search(db, spring, new JsonObject { ["audience"] = 1, ["count"] = 80 });
        await db.SaveChangesAsync(Ct);
        Assert.Empty(await ClaimSettledAsync(harness, db));

        var once = await db.Approvals.AsNoTracking().SingleAsync(a => a.WorkItemId == larger.Id, Ct);
        await Approvals(db).ApproveAsync(new ApprovalDecisionRequest(once.PublicId, Operator, "a larger pull, this once"), Ct);

        await using (var fresh = database.Open())
        {
            Assert.Empty(await fresh.Journal.AsNoTracking().Where(e => e.Kind == JournalKinds.StandingApprovalUnset).ToListAsync(Ct));
            Assert.Equal(allowance.LineId, (await StandingApprovals.AllowanceAsync(fresh, spring.Id, Ct))!.Record.Line.PublicId);
        }

        Assert.Equal([larger.PublicId], (await ClaimSettledAsync(harness, db)).Select(run => run.WorkItemPublicId));
        var asked = await db.Approvals.AsNoTracking().SingleAsync(a => a.WorkItemId == probe.Id, Ct);
        await Approvals(db).ApproveAsync(new ApprovalDecisionRequest(asked.PublicId, Operator, "retail looks right"), Ct);

        await using (var fresh = database.Open())
        {
            var lines = await fresh.Journal.AsNoTracking()
                .Where(e => (e.Kind == JournalKinds.ApprovalApproved && e.WorkItemId == probe.PublicId) || e.Kind == JournalKinds.StandingApprovalUnset)
                .OrderBy(e => e.Id)
                .ToListAsync(Ct);
            Assert.Equal([JournalKinds.ApprovalApproved, JournalKinds.StandingApprovalUnset], lines.Select(e => e.Kind));
            Assert.All(lines, e => Assert.Equal((ActorType.Human, "operator", "retail looks right"), (e.ActorType, e.ActorId, e.Reason)));
            Assert.Equal(("audience.search", (int?)1), (lines[1].Key, (int?)lines[1].Old!["audience"]!["number"]));
            Assert.Null(await StandingApprovals.AllowanceAsync(fresh, spring.Id, Ct));
        }

        Assert.Equal([probe.PublicId], (await ClaimSettledAsync(harness, db)).Select(run => run.WorkItemPublicId));
        var after = Search(db, spring, new JsonObject { ["audience"] = 1, ["count"] = 50 });
        await db.SaveChangesAsync(Ct);
        Assert.Empty(await ClaimSettledAsync(harness, db));
        Assert.Equal(
            StandingApprovals.Why.NoStandingApproval,
            (string?)(await db.Approvals.AsNoTracking().SingleAsync(a => a.WorkItemId == after.Id, Ct)).Preview["standing_approval"]!["why"]);
    }

    /// <summary>
    /// "Approve and allow up to N more people": the person's approval of a search sets the allowance for that search's
    /// audience in the same act, retiring the one before it, and the audience's next top-up runs under it. The approved
    /// search itself is the person's decision, not the allowance's, and is not counted.
    /// </summary>
    [Fact]
    public async Task Allowance_ApproveAndAllow_SetsItForTheSearchsAudience_TopUpsThenRunUnderIt()
    {
        using var harness = SearchHarness();
        using var database = new TestDatabase();
        await using var db = database.Open();
        var spring = await SpringAsync(harness, db);
        await AllowAsync(db, spring, 1);
        var probe = Search(db, spring, new JsonObject { ["definition"] = InRetail, ["count"] = 20 });
        await db.SaveChangesAsync(Ct);
        Assert.Empty(await ClaimSettledAsync(harness, db));

        var asked = await db.Approvals.AsNoTracking().SingleAsync(a => a.WorkItemId == probe.Id, Ct);
        await Approvals(db).ApproveAsync(
            new ApprovalDecisionRequest(asked.PublicId, Operator, "retail works", new AllowanceRequest(300, MaxPerSearch: 100)), Ct);
        Assert.Equal([probe.PublicId], (await ClaimSettledAsync(harness, db)).Select(run => run.WorkItemPublicId));
        var topUp = Search(db, spring, new JsonObject { ["audience"] = 3, ["count"] = 100 });
        await db.SaveChangesAsync(Ct);

        Assert.Equal([topUp.PublicId], (await ClaimSettledAsync(harness, db)).Select(run => run.WorkItemPublicId));

        await using var fresh = database.Open();
        var lines = await fresh.Journal.AsNoTracking()
            .Where(e => e.Kind == JournalKinds.ApprovalApproved || e.Kind == JournalKinds.StandingApprovalSet || e.Kind == JournalKinds.StandingApprovalUnset)
            .OrderBy(e => e.Id)
            .ToListAsync(Ct);
        Assert.Equal(
            [JournalKinds.StandingApprovalSet, JournalKinds.ApprovalApproved, JournalKinds.StandingApprovalUnset, JournalKinds.StandingApprovalSet],
            lines.Select(e => e.Kind));
        Assert.All(lines.Skip(1), e => Assert.Equal((ActorType.Human, "operator", "retail works"), (e.ActorType, e.ActorId, e.Reason)));
        var used = await StandingApprovals.AllowanceAsync(fresh, spring.Id, Ct);
        Assert.Equal((3, (int?)300, (int?)100, 1, 100), (used!.Record.Audience!.Number, used.Record.MaxPeople, used.Record.MaxPerSearch, used.Released, used.People));
        Assert.Equal(lines[3].PublicId, used.Record.Line.PublicId);
    }

    /// <summary>
    /// There is no latest audience: one the allowance moved away from may be allowed again, by the person, and its top-ups
    /// then run under the allowance as they did before.
    /// </summary>
    [Fact]
    public async Task Allowance_SupersededAudienceAllowedAgain_CoversItsTopUps()
    {
        using var harness = SearchHarness();
        using var database = new TestDatabase();
        await using var db = database.Open();
        var spring = await SpringAsync(harness, db);
        await AllowAsync(db, spring, 1);
        await AllowAsync(db, spring, 2);
        var again = await AllowAsync(db, spring, 1, maxPeople: 80);
        var topUp = Search(db, spring, new JsonObject { ["audience"] = 1, ["count"] = 80 });
        await db.SaveChangesAsync(Ct);

        Assert.Equal([topUp.PublicId], (await ClaimSettledAsync(harness, db)).Select(run => run.WorkItemPublicId));

        await using var fresh = database.Open();
        Assert.Equal(again.LineId, (await fresh.Journal.AsNoTracking().SingleAsync(e => e.Kind == JournalKinds.ApprovalStandingApplied, Ct)).Key);
    }

    /// <summary>
    /// A top-up put to a person before the allowance existed, then edited while the question was open: the edit withdraws
    /// the question and the claim asks again, and the allowance that now names its audience answers nothing, because
    /// somebody was already asked about this search.
    /// </summary>
    [Fact]
    public async Task Allowance_TopUpParkedThenEdited_IsAskedAgain_NotReleased()
    {
        using var harness = SearchHarness();
        using var database = new TestDatabase();
        await using var db = database.Open();
        var spring = await SpringAsync(harness, db);
        var topUp = Search(db, spring, new JsonObject { ["audience"] = 1, ["count"] = 50 });
        await db.SaveChangesAsync(Ct);
        Assert.Empty(await ClaimSettledAsync(harness, db));
        Assert.Equal(WorkItemStatus.AwaitingApproval, topUp.Status);

        await AllowAsync(db, spring, 1);
        var clock = new FixedClock(new DateTimeOffset(Noon));
        await new WorkItemService(db, new JournalWriter(clock), clock, TestCanceller.New(clock), TestOptions.PluginSettings()).UpdateAsync(
            new WorkItemUpdateRequest(
                topUp.PublicId, new JsonObject { [WorkItemService.InputKey] = new JsonObject { ["audience"] = 1, ["count"] = 40 } }, null,
                default, default, default, default, default, default, default, default, Operator, null),
            Ct);
        Assert.Equal(WorkItemStatus.Created, topUp.Status);

        Assert.Empty(await ClaimSettledAsync(harness, db));

        await using var fresh = database.Open();
        Assert.Equal(
            [ApprovalStatus.Superseded, ApprovalStatus.Pending],
            await fresh.Approvals.AsNoTracking().Where(a => a.WorkItemId == topUp.Id).OrderBy(a => a.Id).Select(a => a.Status).ToListAsync(Ct));
        Assert.Equal(WorkItemStatus.AwaitingApproval, (await fresh.WorkItems.AsNoTracking().SingleAsync(w => w.Id == topUp.Id, Ct)).Status);
        Assert.Equal(0, await fresh.Journal.AsNoTracking().CountAsync(e => e.Kind == JournalKinds.ApprovalStandingApplied, Ct));
    }

    /// <summary>
    /// An allowance is a person's word, so a set line that carries an attempt is a run's and allows nothing, whoever it
    /// names: a top-up under the audience it names parks, its preview saying <c>no_standing_approval</c>.
    /// </summary>
    [Fact]
    public async Task Allowance_ASetLineCarryingAnAttempt_AllowsNothing()
    {
        using var harness = SearchHarness();
        using var database = new TestDatabase();
        await using var db = database.Open();
        var spring = await SpringAsync(harness, db);
        var software = await db.Audiences.AsNoTracking().SingleAsync(a => a.CampaignId == spring.Id && a.Number == 1, Ct);
        var work = WorkItemFactory.NewAiRole(spring, role: "manager", now: Noon);
        work.Status = WorkItemStatus.Processing;
        var run = WorkItemFactory.NewAttempt(work, 1, AttemptStatus.Running, Noon);
        db.WorkItems.Add(work);
        await db.SaveChangesAsync(Ct);
        new JournalWriter(new FixedClock(new DateTimeOffset(Noon.AddHours(-1)))).Append(
            db,
            Operator,
            JournalKinds.StandingApprovalSet,
            spring,
            key: StandingApprovals.Searches,
            updated: StandingApprovals.AllowanceLine(new AudienceRef(software.PublicId, 1), maxPeople: 500, maxPerSearch: null, expiresAt: null),
            attempt: run);
        var topUp = Search(db, spring, new JsonObject { ["audience"] = 1, ["count"] = 50 });
        await db.SaveChangesAsync(Ct);

        Assert.Empty(await ClaimSettledAsync(harness, db));

        await using var fresh = database.Open();
        var waiting = await fresh.Approvals.AsNoTracking().SingleAsync(a => a.WorkItemId == topUp.Id, Ct);
        Assert.Equal((ApprovalStatus.Pending, StandingApprovals.Why.NoStandingApproval), (waiting.Status, (string?)waiting.Preview["standing_approval"]!["why"]));
    }

    private static JsonObject InRetail =>new() { ["industries"] = new JsonArray("Retail"), ["titles"] = new JsonArray("Store Manager") };

    /// <summary>A campaign whose first two searches made its audiences 1 (software) and 2 (logistics), each probe still waiting.</summary>
    private static async Task<Campaign> SpringAsync(Harness harness, JasonDbContext db)
    {
        var spring = WorkItemFactory.NewCampaign("Spring", now: Noon);
        db.Campaigns.Add(spring);
        await HeldAsync(harness, db, spring, InSoftware, InLogistics);
        return spring;
    }

    /// <summary>The person's allowance for the campaign's audience <paramref name="number"/>, set an hour before the claim.</summary>
    private static Task<StandingApprovalDto> AllowAsync(
        JasonDbContext db, Campaign campaign, int number, int maxPeople = 500, int? maxPerSearch = null, DateTimeOffset? expiresAt = null)
    {
        var clock = new FixedClock(new DateTimeOffset(Noon.AddHours(-1)));
        return new StandingApprovalService(db, new JournalWriter(clock), clock).SetAsync(
            new ApprovalSetStandingRequest(
                campaign.PublicId, null, [StandingApprovals.Searches], expiresAt, Actor: Operator, Audience: number, MaxPeople: maxPeople, MaxPerSearch: maxPerSearch),
            Ct);
    }

    private static ApprovalService Approvals(JasonDbContext db)
    {
        var clock = new FixedClock(new DateTimeOffset(Noon));
        return new ApprovalService(db, new JournalWriter(clock), clock);
    }
}
