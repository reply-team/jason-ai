using System.Net;
using System.Text.Json.Nodes;
using Jason.Contracts.Api;
using Jason.Runtime.Approvals;
using Jason.Runtime.Domain;
using Jason.Runtime.Journal;
using Jason.Runtime.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Jason.Runtime.Tests.Approvals;

/// <summary>
/// The three verbs of a campaign's standing approvals. The record is the chronicle — a line per change, by the person who
/// made it — and the newest line for a campaign and a category is what stands.
/// </summary>
public class StandingApprovalServiceTests
{
    private static readonly DateTime Noon = new(2026, 9, 14, 12, 0, 0, DateTimeKind.Utc);

    private static readonly ActorRef Operator = new(ActorType.Human, "operator");

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>
    /// A set is one line by the person, keyed by the category, carrying the category, the operations, the expiry and the
    /// ceiling and nothing else; with no operations named it covers a reply in the thread alone. The answer is the record
    /// in force, with no release made under it yet.
    /// </summary>
    [Fact]
    public async Task A_set_is_one_line_by_the_person_and_answers_the_record_in_force()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var campaign = await SeedAsync(db);

        var set = await Service(db).SetAsync(Set(campaign) with { MaxReleases = 20, Reason = "  replies to the interested may go  " }, Ct);

        Assert.Equal(
            (campaign.PublicId, "interested", (int?)20, 0, true),
            (set.CampaignId, set.Category, set.MaxReleases, set.Released, set.InForce));
        Assert.Equal(["conversation.reply"], set.Operations!);
        Assert.Null(set.ExpiresAt);
        Assert.Equal(Operator, set.SetBy);
        Assert.Equal(new DateTimeOffset(Noon), set.SetAt);
        var line = await db.Journal.AsNoTracking().SingleAsync(e => e.Kind == JournalKinds.StandingApprovalSet, Ct);
        Assert.Equal((set.LineId, ActorType.Human, "operator", campaign.Id, "interested"), (line.PublicId, line.ActorType, line.ActorId, line.CampaignId, line.Key));
        Assert.Equal("""{"category":"interested","operations":["conversation.reply"],"expires_at":null,"max_releases":20}""", line.New!.ToJsonString());
        Assert.Null(line.Old);
        Assert.Equal("replies to the interested may go", line.Reason);
    }

    /// <summary>
    /// The same set sent again, after its answer was lost, is the record already in force and writes nothing; a different
    /// one replaces it whole, its old value the record it replaced.
    /// </summary>
    [Fact]
    public async Task Standing_SameSetTwice_OneLine_AndAnotherReplacesIt()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var campaign = await SeedAsync(db);
        var first = await Service(db).SetAsync(Set(campaign), Ct);

        var again = await Service(db).SetAsync(Set(campaign), Ct);
        var wider = await Service(db).SetAsync(Set(campaign) with { Operations = ["message.send", "conversation.reply", "message.send"] }, Ct);

        Assert.Equal(first.LineId, again.LineId);
        Assert.Equal(["conversation.reply", "message.send"], wider.Operations!);
        var lines = await db.Journal.AsNoTracking().Where(e => e.Kind == JournalKinds.StandingApprovalSet).OrderBy(e => e.Id).ToListAsync(Ct);
        Assert.Equal([first.LineId, wider.LineId], lines.Select(e => e.PublicId));
        Assert.Equal(lines[0].New!.ToJsonString(), lines[1].Old!.ToJsonString());
    }

    /// <summary>
    /// An unset is one line whose old value is the record it takes back; a category not set writes nothing, and a category
    /// unset is no longer listed. A listing is in category order and answers every category set, an archived campaign's too.
    /// </summary>
    [Fact]
    public async Task An_unset_takes_the_record_back_and_the_listing_answers_what_stands()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var campaign = await SeedAsync(db);
        await Service(db).SetAsync(Set(campaign) with { Category = "not_now" }, Ct);
        await Service(db).SetAsync(Set(campaign), Ct);
        await Service(db).SetAsync(Set(campaign) with { Category = "meeting_booked" }, Ct);

        var unset = await Service(db).UnsetAsync(new ApprovalUnsetStandingRequest(campaign.PublicId, "not_now", Operator), Ct);
        await Service(db).UnsetAsync(new ApprovalUnsetStandingRequest(campaign.PublicId, "never_set", Operator), Ct);
        var page = await Service(db).ListAsync(new ApprovalListStandingRequest(campaign.PublicId, Limit: 1), Ct);
        var next = await Service(db).ListAsync(new ApprovalListStandingRequest(campaign.PublicId, Cursor: page.NextCursor), Ct);

        Assert.Equal(("not_now", false, (string?)null), (unset.Category, unset.InForce, unset.LineId));
        var line = await db.Journal.AsNoTracking().SingleAsync(e => e.Kind == JournalKinds.StandingApprovalUnset, Ct);
        Assert.Equal(("not_now", (JsonNode?)null), (line.Key, line.New));
        Assert.Equal("not_now", (string?)line.Old!["category"]);
        Assert.Equal(["interested"], page.Items.Select(item => item.Category));
        Assert.Equal(["meeting_booked"], next.Items.Select(item => item.Category));
        Assert.Null(next.NextCursor);
    }

    /// <summary>
    /// Only a person sets or takes back a standing approval, and a request has to name them: a role or a run is refused
    /// the way it is refused a decision, and a request with no actor, or one naming nobody, is refused for want of a name.
    /// </summary>
    [Theory]
    [InlineData(ActorType.Role, "manager", "approval_not_human")]
    [InlineData(ActorType.Attempt, "att_01K52JR0000000000000000001", "approval_not_human")]
    [InlineData(ActorType.Human, "  ", "actor_required")]
    [InlineData(null, null, "actor_required")]
    [Critical(Family.S2, "only a named person sets a standing approval")]
    public async Task Standing_SetOrUnsetByRoleOrAttempt_ApprovalNotHuman_Anonymous_ActorRequired(ActorType? type, string? id, string code)
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var campaign = await SeedAsync(db);
        var actor = type is { } kind ? new ActorRef(kind, id) : null;

        var set = await Assert.ThrowsAsync<InvalidRequestException>(() => Service(db).SetAsync(Set(campaign) with { Actor = actor }, Ct));
        var unset = await Assert.ThrowsAsync<InvalidRequestException>(
            () => Service(db).UnsetAsync(new ApprovalUnsetStandingRequest(campaign.PublicId, "interested", actor), Ct));

        Assert.Equal((code, code), (set.Code, unset.Code));
        Assert.Empty(await db.Journal.AsNoTracking().Where(e => e.Kind == JournalKinds.StandingApprovalSet || e.Kind == JournalKinds.StandingApprovalUnset).ToListAsync(Ct));
    }

    /// <summary>An archived campaign's work is over: its standing approvals no longer change, and are still listed.</summary>
    [Fact]
    public async Task Standing_ArchivedCampaign_SetAndUnsetRefused_StillListed()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var campaign = await SeedAsync(db);
        await Service(db).SetAsync(Set(campaign), Ct);
        campaign.Status = CampaignStatus.Archived;
        await db.SaveChangesAsync(Ct);

        var set = await Assert.ThrowsAsync<ConflictException>(() => Service(db).SetAsync(Set(campaign) with { MaxReleases = 5 }, Ct));
        var unset = await Assert.ThrowsAsync<ConflictException>(
            () => Service(db).UnsetAsync(new ApprovalUnsetStandingRequest(campaign.PublicId, "interested", Operator), Ct));
        var listed = await Service(db).ListAsync(new ApprovalListStandingRequest(campaign.PublicId), Ct);

        Assert.Equal(("campaign_archived", "campaign_archived"), (set.Code, unset.Code));
        Assert.Equal(["interested"], listed.Items.Select(item => item.Category));
    }

    /// <summary>What a set is held to, each refusal at its field: the category's rule is the verdict's own.</summary>
    [Theory]
    [InlineData("category blank", "category", "required")]
    [InlineData("category with a line break", "category", "invalid")]
    [InlineData("no operations", "operations", "invalid")]
    [InlineData("an operation not coverable", "operations", "invalid")]
    [InlineData("expiry now", "expires_at", "in_the_past")]
    [InlineData("no releases", "max_releases", "invalid")]
    [InlineData("too many releases", "max_releases", "invalid")]
    [InlineData("reason too long", "reason", "too_long")]
    public async Task A_set_is_refused_at_the_field_it_gets_wrong(string change, string field, string code)
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var campaign = await SeedAsync(db);
        var request = change switch
        {
            "category blank" => Set(campaign) with { Category = "  " },
            "category with a line break" => Set(campaign) with { Category = "interested\nnow" },
            "no operations" => Set(campaign) with { Operations = [] },
            "an operation not coverable" => Set(campaign) with { Operations = ["campaign.enroll"] },
            "expiry now" => Set(campaign) with { ExpiresAt = new DateTimeOffset(Noon) },
            "no releases" => Set(campaign) with { MaxReleases = 0 },
            "too many releases" => Set(campaign) with { MaxReleases = StandingApprovals.MaxReleases + 1 },
            "reason too long" => Set(campaign) with { Reason = new string('r', 2001) },
            _ => throw new ArgumentOutOfRangeException(nameof(change), change, "no such change"),
        };

        var refused = await Assert.ThrowsAsync<ValidationException>(() => Service(db).SetAsync(request, Ct));

        Assert.Contains(refused.Details!, detail => detail.Field == field && detail.Code == code);
        Assert.Empty(await db.Journal.AsNoTracking().Where(e => e.Kind == JournalKinds.StandingApprovalSet).ToListAsync(Ct));
    }

    /// <summary>
    /// The listing says whether each standing approval is still in force: one whose moment has passed, or whose releases
    /// reached its ceiling, is listed and is not in force; the same set again after that is a new line, in force again.
    /// </summary>
    [Fact]
    public async Task The_listing_says_what_is_no_longer_in_force()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var campaign = await SeedAsync(db);
        var clock = new FixedClock(new DateTimeOffset(Noon));
        var service = new StandingApprovalService(db, new JournalWriter(clock), clock);
        var spent = await service.SetAsync(Set(campaign) with { MaxReleases = 1 }, Ct);
        await service.SetAsync(Set(campaign) with { Category = "not_now", ExpiresAt = new DateTimeOffset(Noon.AddHours(1)) }, Ct);
        new JournalWriter(clock).Append(db, Actors.Dispatcher, JournalKinds.ApprovalStandingApplied, campaign, key: spent.LineId, updated: new JsonObject());
        await db.SaveChangesAsync(Ct);
        clock.Advance(TimeSpan.FromHours(2));

        var listed = await service.ListAsync(new ApprovalListStandingRequest(campaign.PublicId), Ct);
        var again = await service.SetAsync(Set(campaign) with { MaxReleases = 1 }, Ct);

        Assert.Equal(
            [("interested", 1, false), ("not_now", 0, false)],
            listed.Items.Select(item => (item.Category, item.Released, item.InForce)));
        Assert.NotEqual(spent.LineId, again.LineId);
        Assert.Equal((0, true), (again.Released, again.InForce));
    }

    /// <summary>
    /// The exchanges a consumer renders: a standing approval set with its limits, the listing, one taken back, and the
    /// refusals a person meets — a role that tries to set one, an operation it cannot cover, and an archived campaign.
    /// </summary>
    [Fact]
    public async Task The_exchanges_are_recorded()
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct, clock: new FixedClock(new DateTimeOffset(Noon)));
        var campaign = await api.PostOkAsync<CampaignDto>(Operations.CampaignCreate, new { name = "Spring outreach" }, Ct);
        var ada = new { type = "human", id = "ada" };
        var standing = new
        {
            campaign_id = campaign.Id,
            category = "interested",
            operations = new[] { "conversation.reply", "message.send" },
            expires_at = "2026-12-01T00:00:00Z",
            max_releases = 20,
            actor = ada,
            reason = "the interested may hear back at once",
        };

        var set = await api.RecordAsync<StandingApprovalDto>(Operations.ApprovalSetStanding, "set", standing, Ct);
        var role = await api.RecordErrorAsync(
            Operations.ApprovalSetStanding, "role-refused", standing with { actor = new { type = "role", id = "responder" } }, HttpStatusCode.BadRequest, Ct);
        var enrolment = await api.RecordErrorAsync(
            Operations.ApprovalSetStanding, "operation-not-coverable", standing with { operations = new[] { "campaign.enroll" } }, HttpStatusCode.BadRequest, Ct);
        await api.PostOkAsync<StandingApprovalDto>(
            Operations.ApprovalSetStanding, new { campaign_id = campaign.Id, category = "not_now", actor = ada }, Ct);
        var listed = await api.RecordAsync<Page<StandingApprovalDto>>(Operations.ApprovalListStanding, "listed", new { campaign_id = campaign.Id }, Ct);
        var unset = await api.RecordAsync<StandingApprovalDto>(
            Operations.ApprovalUnsetStanding, "unset", new { campaign_id = campaign.Id, category = "not_now", actor = ada, reason = "not now means not now" }, Ct);
        await api.PostOkAsync<CampaignDto>(Operations.CampaignArchive, new { campaign_id = campaign.Id }, Ct);
        var archived = await api.RecordErrorAsync(Operations.ApprovalSetStanding, "archived-campaign", standing, HttpStatusCode.Conflict, Ct);

        Assert.Equal((20, 0, true), (set.MaxReleases, set.Released, set.InForce));
        Assert.Equal("approval_not_human", role.Code);
        Assert.Equal(["operations/invalid"], enrolment.Details!.Select(detail => $"{detail.Field}/{detail.Code}"));
        Assert.Equal(["interested", "not_now"], listed.Items.Select(item => item.Category));
        Assert.Equal(("not_now", false, (string?)null), (unset.Category, unset.InForce, unset.LineId));
        Assert.Equal("campaign_archived", archived.Code);
    }

    private static ApprovalSetStandingRequest Set(Campaign campaign) => new(campaign.PublicId, "interested", Actor: Operator);

    private static StandingApprovalService Service(JasonDbContext db)
    {
        var clock = new FixedClock(new DateTimeOffset(Noon));
        return new StandingApprovalService(db, new JournalWriter(clock), clock);
    }

    private static async Task<Campaign> SeedAsync(JasonDbContext db)
    {
        var campaign = WorkItemFactory.NewCampaign(now: Noon);
        db.Campaigns.Add(campaign);
        await db.SaveChangesAsync(Ct);
        return campaign;
    }
}
