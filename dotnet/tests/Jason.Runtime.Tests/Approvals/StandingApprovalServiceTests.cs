using System.Net;
using System.Text.Json.Nodes;
using Jason.Contracts.Api;
using Jason.Contracts.Ids;
using Jason.Runtime.Approvals;
using Jason.Runtime.Domain;
using Jason.Runtime.Journal;
using Jason.Runtime.Persistence;
using Jason.Runtime.Tests.Journal;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Jason.Runtime.Tests.Approvals;

/// <summary>
/// The three verbs of a campaign's standing approvals. The record is the chronicle — a line per change, by the person who
/// made it — and the newest line for a campaign and a category is what stands; the allowance for the campaign's searches is
/// the same record keyed by its operation, so a campaign holds one.
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
    /// The allowance names the operation, the audience by its number and the ceiling in people: one line by the person,
    /// keyed by the operation, carrying the audience by id and number, the ceiling, the cap per search and the expiry, with
    /// no category and no count of releases. The answer is the allowance in force, nobody searched under it yet.
    /// </summary>
    [Fact]
    public async Task An_allowance_is_one_line_naming_the_audience_and_its_ceiling_in_people()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var campaign = await SeedAsync(db);
        var logistics = await db.Audiences.AsNoTracking().SingleAsync(a => a.Number == 2, Ct);

        var set = await Service(db).SetAsync(Allow(campaign, 2) with { MaxPerSearch = 100, Reason = "the logistics heads answer" }, Ct);

        Assert.Equal(
            (campaign.PublicId, (string?)null, (int?)500, (int?)100, (int?)0, 0, true),
            (set.CampaignId, set.Category, set.MaxPeople, set.MaxPerSearch, set.People, set.Released, set.InForce));
        Assert.Equal(["audience.search"], set.Operations!);
        Assert.Equal(new AudienceRef(logistics.PublicId, 2), set.Audience);
        Assert.Null(set.MaxReleases);
        var line = await db.Journal.AsNoTracking().SingleAsync(e => e.Kind == JournalKinds.StandingApprovalSet, Ct);
        Assert.Equal((set.LineId, ActorType.Human, "operator", campaign.Id, "audience.search"), (line.PublicId, line.ActorType, line.ActorId, line.CampaignId, line.Key));
        Assert.Equal(
            $$"""{"category":null,"operations":["audience.search"],"expires_at":null,"max_releases":null,"audience":{"id":"{{logistics.PublicId}}","number":2},"max_people":500,"max_per_search":100}""",
            line.New!.ToJsonString());
        Assert.Null(line.Old);
        Assert.Equal("the logistics heads answer", line.Reason);
    }

    /// <summary>An audience is named by a number the campaign holds; any other number is refused as the preview refuses it.</summary>
    [Fact]
    public async Task An_allowance_for_an_audience_the_campaign_does_not_hold_is_refused_as_unknown()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var campaign = await SeedAsync(db);

        var refused = await Assert.ThrowsAsync<NotFoundException>(() => Service(db).SetAsync(Allow(campaign, 3), Ct));

        Assert.Equal("audience_unknown", refused.Code);
        Assert.Empty(await StandingLinesAsync(db));
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
    /// One allowance per campaign, and a new one retires the one before it: an unset of it, then the new set, both the
    /// person's, the set replacing nothing. The same allowance sent again after its answer was lost writes nothing.
    /// </summary>
    [Fact]
    public async Task An_allowance_for_another_audience_retires_the_one_before_it_and_the_same_one_again_writes_nothing()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var campaign = await SeedAsync(db);
        var first = await Service(db).SetAsync(Allow(campaign, 1), Ct);

        var again = await Service(db).SetAsync(Allow(campaign, 1), Ct);
        var other = await Service(db).SetAsync(Allow(campaign, 2) with { MaxPeople = 200 }, Ct);

        Assert.Equal(first.LineId, again.LineId);
        var lines = await StandingLinesAsync(db);
        Assert.Equal(
            [(JournalKinds.StandingApprovalSet, first.LineId), (JournalKinds.StandingApprovalUnset, null), (JournalKinds.StandingApprovalSet, other.LineId)],
            lines.Select(e => (e.Kind, e.Kind == JournalKinds.StandingApprovalSet ? e.PublicId : null)));
        Assert.All(lines, e => Assert.Equal(("audience.search", ActorType.Human, "operator"), (e.Key, e.ActorType, e.ActorId)));
        Assert.Equal(lines[0].New!.ToJsonString(), lines[1].Old!.ToJsonString());
        Assert.Null(lines[2].Old);
        Assert.Equal((2, (int?)200), (other.Audience!.Number, other.MaxPeople));
    }

    /// <summary>
    /// An unset is one line whose old value is the record it takes back; a category not set writes nothing, and a category
    /// unset is no longer listed. The allowance is taken back by its operation the same way. A listing is in category order
    /// and answers every category set, an archived campaign's too.
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
        await Service(db).SetAsync(Allow(campaign, 1), Ct);

        var unset = await Service(db).UnsetAsync(new ApprovalUnsetStandingRequest(campaign.PublicId, "not_now", Operator), Ct);
        await Service(db).UnsetAsync(new ApprovalUnsetStandingRequest(campaign.PublicId, "never_set", Operator), Ct);
        var allowance = await Service(db).UnsetAsync(new ApprovalUnsetStandingRequest(campaign.PublicId, null, Operator, Operation: "audience.search"), Ct);
        var page = await Service(db).ListAsync(new ApprovalListStandingRequest(campaign.PublicId, Limit: 1), Ct);
        var next = await Service(db).ListAsync(new ApprovalListStandingRequest(campaign.PublicId, Cursor: page.NextCursor), Ct);

        Assert.Equal(("not_now", false, (string?)null), (unset.Category, unset.InForce, unset.LineId));
        Assert.Equal(((string?)null, false, (string?)null), (allowance.Category, allowance.InForce, allowance.LineId));
        Assert.Equal(["audience.search"], allowance.Operations!);
        var lines = await db.Journal.AsNoTracking().Where(e => e.Kind == JournalKinds.StandingApprovalUnset).OrderBy(e => e.Id).ToListAsync(Ct);
        Assert.Equal([("not_now", (JsonNode?)null), ("audience.search", null)], lines.Select(line => (line.Key, line.New)));
        Assert.Equal("not_now", (string?)lines[0].Old!["category"]);
        Assert.Equal(1, (int)lines[1].Old!["audience"]!["number"]!);
        Assert.Equal(["interested"], page.Items.Select(item => item.Category));
        Assert.Equal(["meeting_booked"], next.Items.Select(item => item.Category));
        Assert.Null(next.NextCursor);
    }

    /// <summary>
    /// Only a person sets or takes back a standing approval, the allowance for the campaign's searches included, and a
    /// request has to name them: a role, a run or a plugin in its own door's scope is refused the way it is refused a
    /// decision, and a request with no actor, or one naming nobody, is refused for want of a name.
    /// </summary>
    [Theory]
    [InlineData(ActorType.Role, "manager", "approval_not_human")]
    [InlineData(ActorType.Attempt, "att_01K52JR0000000000000000001", "approval_not_human")]
    [InlineData(ActorType.Plugin, "acme-forwarder", "approval_not_human")]
    [InlineData(ActorType.Human, "  ", "actor_required")]
    [InlineData(null, null, "actor_required")]
    [Critical(Family.S2, "only a named person sets a standing approval")]
    public async Task Standing_SetOrUnsetByRoleOrAttempt_ApprovalNotHuman_Anonymous_ActorRequired(ActorType? type, string? id, string code)
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var campaign = await SeedAsync(db);
        var actor = type is { } kind ? new ActorRef(kind, id) : null;

        // A plugin is admitted as an actor only in the scope its own door opened, and refused there all the same.
        using var door = type == ActorType.Plugin ? ActorScope.ForPlugin(id!) : null;
        var set = await Assert.ThrowsAsync<InvalidRequestException>(() => Service(db).SetAsync(Set(campaign) with { Actor = actor }, Ct));
        var unset = await Assert.ThrowsAsync<InvalidRequestException>(
            () => Service(db).UnsetAsync(new ApprovalUnsetStandingRequest(campaign.PublicId, "interested", actor), Ct));
        var allow = await Assert.ThrowsAsync<InvalidRequestException>(() => Service(db).SetAsync(Allow(campaign, 1) with { Actor = actor }, Ct));
        var retire = await Assert.ThrowsAsync<InvalidRequestException>(
            () => Service(db).UnsetAsync(new ApprovalUnsetStandingRequest(campaign.PublicId, null, actor, Operation: "audience.search"), Ct));

        Assert.Equal((code, code, code, code), (set.Code, unset.Code, allow.Code, retire.Code));
        Assert.Empty(await StandingLinesAsync(db));
    }

    /// <summary>
    /// An archived campaign's work is over: its standing approvals no longer change, and are still listed, shown not in
    /// force. Approving a search the campaign still has parked changes none of them either: an approval that asks for an
    /// allowance is refused whole, and one that does not leaves the allowance as it was, though its search is under
    /// another audience.
    /// </summary>
    [Fact]
    public async Task Standing_ArchivedCampaign_SetAndUnsetRefused_StillListedNotInForce()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var campaign = await SeedAsync(db);
        Assert.True((await Service(db).SetAsync(Set(campaign), Ct)).InForce);
        Assert.True((await Service(db).SetAsync(Allow(campaign, 1), Ct)).InForce);
        var logistics = await db.Audiences.SingleAsync(a => a.CampaignId == campaign.Id && a.Number == 2, Ct);
        var parked = await ParkedSearchAsync(db, campaign, logistics);
        var other = await ParkedSearchAsync(db, campaign, logistics);
        campaign.Status = CampaignStatus.Archived;
        await db.SaveChangesAsync(Ct);
        var lines = await StandingLinesAsync(db);

        var set = await Assert.ThrowsAsync<ConflictException>(() => Service(db).SetAsync(Set(campaign) with { MaxReleases = 5 }, Ct));
        var unset = await Assert.ThrowsAsync<ConflictException>(
            () => Service(db).UnsetAsync(new ApprovalUnsetStandingRequest(campaign.PublicId, "interested", Operator), Ct));
        var allowed = await Assert.ThrowsAsync<ConflictException>(
            () => Approvals(db).ApproveAsync(new ApprovalDecisionRequest(parked.PublicId, Operator, null, new AllowanceRequest(300)), Ct));
        var approved = await Approvals(db).ApproveAsync(new ApprovalDecisionRequest(other.PublicId, Operator, null), Ct);
        var listed = await Service(db).ListAsync(new ApprovalListStandingRequest(campaign.PublicId), Ct);

        Assert.Equal(("campaign_archived", "campaign_archived", "campaign_archived"), (set.Code, unset.Code, allowed.Code));
        Assert.Equal(ApprovalStatus.Pending, (await db.Approvals.AsNoTracking().SingleAsync(a => a.Id == parked.Id, Ct)).Status);
        Assert.Equal(ApprovalStatus.Approved, approved.Status);
        Assert.Equal(lines.Select(e => e.PublicId), (await StandingLinesAsync(db)).Select(e => e.PublicId));
        Assert.Equal([((string?)null, false), ("interested", false)], listed.Items.Select(item => (item.Category, item.InForce)));
    }

    /// <summary>
    /// What a set is held to, each refusal at its field: the category's rule is the verdict's own. The allowance is held to
    /// its own — the operation alone, an audience, a ceiling in people — and neither takes the other's limits; an unset
    /// names a category or the allowance's operation, never both.
    /// </summary>
    [Theory]
    [InlineData("category blank", "category", "required")]
    [InlineData("category with a line break", "category", "invalid")]
    [InlineData("no operations", "operations", "invalid")]
    [InlineData("an operation not coverable", "operations", "invalid")]
    [InlineData("expiry now", "expires_at", "in_the_past")]
    [InlineData("no releases", "max_releases", "invalid")]
    [InlineData("too many releases", "max_releases", "invalid")]
    [InlineData("reason too long", "reason", "too_long")]
    [InlineData("a category spelled as the allowance's operation", "category", "reserved")]
    [InlineData("a reply naming an audience", "audience", "not_allowed")]
    [InlineData("a reply with a ceiling in people", "max_people", "not_allowed")]
    [InlineData("a reply with a cap per search", "max_per_search", "not_allowed")]
    [InlineData("an allowance with a category", "category", "not_allowed")]
    [InlineData("an allowance beside a reply", "operations", "invalid")]
    [InlineData("an allowance with no audience", "audience", "required")]
    [InlineData("an allowance for audience zero", "audience", "invalid")]
    [InlineData("an allowance with no ceiling", "max_people", "required")]
    [InlineData("an allowance of nobody", "max_people", "invalid")]
    [InlineData("an allowance over the highest ceiling", "max_people", "invalid")]
    [InlineData("an allowance capping searches at nobody", "max_per_search", "invalid")]
    [InlineData("an allowance capping searches above a search", "max_per_search", "invalid")]
    [InlineData("an allowance counting releases", "max_releases", "not_allowed")]
    [InlineData("an allowance expiring now", "expires_at", "in_the_past")]
    [InlineData("an unset naming a category and an operation", "operation", "not_allowed")]
    [InlineData("an unset naming an operation no allowance covers", "operation", "invalid")]
    [InlineData("an unset naming the allowance's operation as a category", "category", "reserved")]
    public async Task A_set_is_refused_at_the_field_it_gets_wrong(string change, string field, string code)
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var campaign = await SeedAsync(db);
        Func<Task> act = change switch
        {
            "category blank" => () => Service(db).SetAsync(Set(campaign) with { Category = "  " }, Ct),
            "category with a line break" => () => Service(db).SetAsync(Set(campaign) with { Category = "interested\nnow" }, Ct),
            "no operations" => () => Service(db).SetAsync(Set(campaign) with { Operations = [] }, Ct),
            "an operation not coverable" => () => Service(db).SetAsync(Set(campaign) with { Operations = ["campaign.enroll"] }, Ct),
            "expiry now" => () => Service(db).SetAsync(Set(campaign) with { ExpiresAt = new DateTimeOffset(Noon) }, Ct),
            "no releases" => () => Service(db).SetAsync(Set(campaign) with { MaxReleases = 0 }, Ct),
            "too many releases" => () => Service(db).SetAsync(Set(campaign) with { MaxReleases = StandingApprovals.MaxReleases + 1 }, Ct),
            "reason too long" => () => Service(db).SetAsync(Set(campaign) with { Reason = new string('r', 2001) }, Ct),
            "a category spelled as the allowance's operation" => () => Service(db).SetAsync(Set(campaign) with { Category = "audience.search" }, Ct),
            "a reply naming an audience" => () => Service(db).SetAsync(Set(campaign) with { Audience = 1 }, Ct),
            "a reply with a ceiling in people" => () => Service(db).SetAsync(Set(campaign) with { MaxPeople = 50 }, Ct),
            "a reply with a cap per search" => () => Service(db).SetAsync(Set(campaign) with { MaxPerSearch = 50 }, Ct),
            "an allowance with a category" => () => Service(db).SetAsync(Allow(campaign, 1) with { Category = "interested" }, Ct),
            "an allowance beside a reply" => () => Service(db).SetAsync(Allow(campaign, 1) with { Operations = ["audience.search", "conversation.reply"] }, Ct),
            "an allowance with no audience" => () => Service(db).SetAsync(Allow(campaign, 1) with { Audience = null }, Ct),
            "an allowance for audience zero" => () => Service(db).SetAsync(Allow(campaign, 0), Ct),
            "an allowance with no ceiling" => () => Service(db).SetAsync(Allow(campaign, 1) with { MaxPeople = null }, Ct),
            "an allowance of nobody" => () => Service(db).SetAsync(Allow(campaign, 1) with { MaxPeople = 0 }, Ct),
            "an allowance over the highest ceiling" => () => Service(db).SetAsync(Allow(campaign, 1) with { MaxPeople = StandingApprovals.MaxPeople + 1 }, Ct),
            "an allowance capping searches at nobody" => () => Service(db).SetAsync(Allow(campaign, 1) with { MaxPerSearch = 0 }, Ct),
            "an allowance capping searches above a search" => () => Service(db).SetAsync(Allow(campaign, 1) with { MaxPerSearch = StandingApprovals.MaxPerSearch + 1 }, Ct),
            "an allowance counting releases" => () => Service(db).SetAsync(Allow(campaign, 1) with { MaxReleases = 5 }, Ct),
            "an allowance expiring now" => () => Service(db).SetAsync(Allow(campaign, 1) with { ExpiresAt = new DateTimeOffset(Noon) }, Ct),
            "an unset naming a category and an operation" => () => Service(db).UnsetAsync(new ApprovalUnsetStandingRequest(campaign.PublicId, "interested", Operator, Operation: "audience.search"), Ct),
            "an unset naming an operation no allowance covers" => () => Service(db).UnsetAsync(new ApprovalUnsetStandingRequest(campaign.PublicId, null, Operator, Operation: "campaign.enroll"), Ct),
            "an unset naming the allowance's operation as a category" => () => Service(db).UnsetAsync(new ApprovalUnsetStandingRequest(campaign.PublicId, "audience.search", Operator), Ct),
            _ => throw new ArgumentOutOfRangeException(nameof(change), change, "no such change"),
        };

        var refused = await Assert.ThrowsAsync<ValidationException>(act);

        Assert.Contains(refused.Details!, detail => detail.Field == field && detail.Code == code);
        Assert.Empty(await StandingLinesAsync(db));
    }

    /// <summary>
    /// The listing says whether each standing approval is still in force: one whose moment has passed, or whose releases
    /// reached its ceiling, is listed and is not in force; the same set again after that is a new line, in force again. The
    /// allowance is listed with how much of it is used — the searches it let go and the people they asked for, its own and
    /// no other campaign's — in force while those people are under its ceiling, and not once they reach it.
    /// </summary>
    [Fact]
    public async Task The_listing_says_what_is_no_longer_in_force()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var campaign = await SeedAsync(db);
        var clock = new FixedClock(new DateTimeOffset(Noon));
        var service = new StandingApprovalService(db, new JournalWriter(clock), clock);
        var allowance = await service.SetAsync(Allow(campaign, 1) with { MaxPeople = 100 }, Ct);
        var elsewhere = await SeedAsync(db);
        await AppliedAsync(db, elsewhere, (await service.SetAsync(Allow(elsewhere, 1), Ct)).LineId!, 70);
        await AppliedAsync(db, campaign, allowance.LineId!, 60);
        var used = Assert.Single((await service.ListAsync(new ApprovalListStandingRequest(campaign.PublicId), Ct)).Items);
        await AppliedAsync(db, campaign, allowance.LineId!, 40);
        var spent = await service.SetAsync(Set(campaign) with { MaxReleases = 1 }, Ct);
        await service.SetAsync(Set(campaign) with { Category = "not_now", ExpiresAt = new DateTimeOffset(Noon.AddHours(1)) }, Ct);
        var released = WorkItemFactory.NewProviderOp(campaign, "conversation.reply", Noon);
        released.Status = WorkItemStatus.Succeeded;
        db.WorkItems.Add(released);
        await db.SaveChangesAsync(Ct);
        new JournalWriter(clock).AppendDocumented(
            db,
            Actors.Dispatcher,
            JournalKinds.ApprovalStandingApplied,
            campaign,
            key: spent.LineId,
            with: new JsonObject { ["standing"] = spent.LineId, ["category"] = "interested" },
            workItem: released);
        await db.SaveChangesAsync(Ct);
        clock.Advance(TimeSpan.FromHours(2));

        var listed = await service.ListAsync(new ApprovalListStandingRequest(campaign.PublicId), Ct);
        var again = await service.SetAsync(Set(campaign) with { MaxReleases = 1 }, Ct);

        Assert.Equal((1, (int?)60, true), (used.Released, used.People, used.InForce));
        Assert.Equal(
            [((string?)null, 2, (int?)100, false), ("interested", 1, null, false), ("not_now", 0, null, false)],
            listed.Items.Select(item => (item.Category, item.Released, item.People, item.InForce)));
        Assert.NotEqual(spent.LineId, again.LineId);
        Assert.Equal((0, true), (again.Released, again.InForce));
    }

    /// <summary>
    /// The exchanges a consumer renders: a standing approval set with its limits, the allowance for the campaign's searches,
    /// the listing with both, each taken back, and the refusals a person meets — a role that tries to set one, an operation
    /// it cannot cover, and an archived campaign.
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
        string asked;
        await using (var scope = api.Resolve<IServiceScopeFactory>().CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<JasonDbContext>();
            var held = await db.Campaigns.SingleAsync(row => row.PublicId == campaign.Id, Ct);
            var logistics = new Audience
            {
                PublicId = PublicId.New("aud"),
                CampaignId = held.Id,
                Number = 1,
                Definition = new JsonObject { ["industries"] = new JsonArray("Logistics") },
                DefinitionHash = "sha256:" + new string('1', 64),
                CreatedAt = Noon,
            };
            db.Audiences.Add(logistics);
            asked = (await ParkedSearchAsync(db, held, logistics)).PublicId;
        }

        var allowance = await api.RecordAsync<StandingApprovalDto>(
            Operations.ApprovalSetStanding,
            "allowance",
            new
            {
                campaign_id = campaign.Id,
                operations = new[] { "audience.search" },
                audience = 1,
                max_people = 500,
                max_per_search = 100,
                expires_at = "2026-12-01T00:00:00Z",
                actor = ada,
                reason = "the logistics heads answer, so top them up",
            },
            Ct);
        var listed = await api.RecordAsync<Page<StandingApprovalDto>>(Operations.ApprovalListStanding, "listed", new { campaign_id = campaign.Id }, Ct);
        var unset = await api.RecordAsync<StandingApprovalDto>(
            Operations.ApprovalUnsetStanding, "unset", new { campaign_id = campaign.Id, category = "not_now", actor = ada, reason = "not now means not now" }, Ct);
        var retired = await api.RecordAsync<StandingApprovalDto>(
            Operations.ApprovalUnsetStanding, "allowance", new { campaign_id = campaign.Id, operation = "audience.search", actor = ada, reason = "enough of this audience" }, Ct);
        var allowed = await api.RecordAsync<ApprovalDto>(
            Operations.ApprovalApprove,
            "approved-and-allowed",
            new { approval_id = asked, allow = new { max_people = 300, max_per_search = 100 }, actor = ada, reason = "the logistics heads answer" },
            Ct);
        var after = Assert.Single((await api.PostOkAsync<Page<StandingApprovalDto>>(Operations.ApprovalListStanding, new { campaign_id = campaign.Id }, Ct)).Items, item => item.Category is null);
        await api.PostOkAsync<CampaignDto>(Operations.CampaignArchive, new { campaign_id = campaign.Id }, Ct);
        var archived = await api.RecordErrorAsync(Operations.ApprovalSetStanding, "archived-campaign", standing, HttpStatusCode.Conflict, Ct);

        Assert.Equal((20, 0, true), (set.MaxReleases, set.Released, set.InForce));
        Assert.Equal("approval_not_human", role.Code);
        Assert.Equal(["operations/invalid"], enrolment.Details!.Select(detail => $"{detail.Field}/{detail.Code}"));
        Assert.Equal(((int?)500, (int?)0, 1, true), (allowance.MaxPeople, allowance.People, allowance.Audience!.Number, allowance.InForce));
        Assert.Equal([null, "interested", "not_now"], listed.Items.Select(item => item.Category));
        Assert.Equal(("not_now", false, (string?)null), (unset.Category, unset.InForce, unset.LineId));
        Assert.Equal(((string?)null, false, (string?)null), (retired.Category, retired.InForce, retired.LineId));
        Assert.Equal((ApprovalStatus.Approved, (int?)300, (int?)100, true), (allowed.Status, after.MaxPeople, after.MaxPerSearch, after.InForce));
        Assert.Equal("campaign_archived", archived.Code);
    }

    /// <summary>
    /// A standing approval set in the desktop's chat carries the chat's mark, and every line that later carries its record
    /// over keeps it on its document: taken back, the unset's <c>old</c> is the set as it was, mark included; replaced from a
    /// terminal, the new set's <c>old</c> is too. Both for a category of reply and for an allowance; each line is checked
    /// against its kind's document as it is saved.
    /// </summary>
    [Theory]
    [InlineData("a category, then taken back")]
    [InlineData("a category, then replaced")]
    [InlineData("an allowance, then taken back")]
    [InlineData("an allowance, then replaced")]
    public async Task A_set_made_in_the_chat_is_carried_over_on_its_document(string then)
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var campaign = await SeedAsync(db);
        var allowance = then.StartsWith("an allowance", StringComparison.Ordinal);
        using (ConfinedScope.Open())
        {
            await Service(db).SetAsync(allowance ? Allow(campaign, 2) : Set(campaign), Ct);
        }

        if (then.EndsWith("taken back", StringComparison.Ordinal))
        {
            await Service(db).UnsetAsync(
                allowance
                    ? new ApprovalUnsetStandingRequest(campaign.PublicId, null, Operator, Operation: "audience.search")
                    : new ApprovalUnsetStandingRequest(campaign.PublicId, "interested", Operator),
                Ct);
        }
        else
        {
            await Service(db).SetAsync(allowance ? Allow(campaign, 2) with { MaxPeople = 300 } : Set(campaign) with { MaxReleases = 5 }, Ct);
        }

        var lines = await StandingLinesAsync(db);
        Assert.Equal("true", lines[0].New![ConfinedScope.Member]?.ToJsonString());
        var carried = lines.Last(line => line.Old is not null);
        Assert.Equal("true", carried.Old![ConfinedScope.Member]?.ToJsonString());
        Assert.Null(lines[^1].New?[ConfinedScope.Member]);
    }

    private static ApprovalSetStandingRequest Set(Campaign campaign) => new(campaign.PublicId, "interested", Actor: Operator);

    /// <summary>
    /// A search under <paramref name="audience"/> parked for a person, as the claim leaves it: awaiting approval, the
    /// audience recorded under its context's reserved key, and a pending decision with the preview the person reads.
    /// </summary>
    private static async Task<Approval> ParkedSearchAsync(JasonDbContext db, Campaign campaign, Audience audience)
    {
        var search = WorkItemFactory.NewProviderOp(campaign, "audience.search", Noon);
        search.Status = WorkItemStatus.AwaitingApproval;
        search.Context = new JsonObject
        {
            ["input"] = new JsonObject { ["audience"] = audience.Number, ["count"] = 100 },
            ["audience"] = new JsonObject { ["id"] = audience.PublicId, ["number"] = audience.Number, ["definition"] = audience.Definition.DeepClone() },
        };
        db.WorkItems.Add(search);
        await db.SaveChangesAsync(Ct);
        var approval = new Approval
        {
            PublicId = PublicId.New(ApprovalGate.IdPrefix),
            WorkItem = search,
            CampaignId = campaign.Id,
            Operation = "audience.search",
            OperationVersion = 1,
            Subject = new JsonObject { ["operation"] = "audience.search", ["work_item"] = search.PublicId },
            SubjectHash = "sha256:" + new string('2', 64),
            Preview = new JsonObject
            {
                ["summary"] = "Search the provider for up to 100 people matching the definition below, for Jason's campaign Spring outreach: a metered search, charged per person found.",
                ["standing_approval"] = new JsonObject
                {
                    ["standing"] = null, ["audience"] = null, ["expires_at"] = null, ["max_people"] = null, ["max_per_search"] = null, ["people"] = null,
                    ["count"] = 100, ["why"] = StandingApprovals.Why.NoStandingApproval,
                },
            },
            PluginId = "stand-in-search",
            BindingIdentity = "sha256:account",
            RouteScope = RouteScope.GlobalDefault,
            PluginSnapshotId = "snp_one",
            RoutingSnapshotId = "rts_one",
            Reason = ApprovalGate.ApprovalRequired,
            RequestedAt = Noon,
        };
        db.Approvals.Add(approval);
        await db.SaveChangesAsync(Ct);
        return approval;
    }

    /// <summary>The allowance for the campaign's audience <paramref name="audience"/>, five hundred people, set by the operator.</summary>
    private static ApprovalSetStandingRequest Allow(Campaign campaign, int audience) =>
        new(campaign.PublicId, null, ["audience.search"], Actor: Operator, Audience: audience, MaxPeople: 500);

    private static StandingApprovalService Service(JasonDbContext db)
    {
        var clock = new FixedClock(new DateTimeOffset(Noon));
        return new StandingApprovalService(db, new JournalWriter(clock), clock);
    }

    private static ApprovalService Approvals(JasonDbContext db)
    {
        var clock = new FixedClock(new DateTimeOffset(Noon));
        return new ApprovalService(db, new JournalWriter(clock), clock);
    }

    private static Task<List<JournalEntry>> StandingLinesAsync(JasonDbContext db) =>
        db.Journal.AsNoTracking()
            .Where(e => e.Kind == JournalKinds.StandingApprovalSet || e.Kind == JournalKinds.StandingApprovalUnset)
            .OrderBy(e => e.Id)
            .ToListAsync(Ct);

    /// <summary>A search let go under the allowance <paramref name="standing"/> set, asking for <paramref name="count"/> people, as the claim says it.</summary>
    private static async Task AppliedAsync(JasonDbContext db, Campaign campaign, string standing, int count)
    {
        var audience = await db.Audiences.AsNoTracking().SingleAsync(a => a.CampaignId == campaign.Id && a.Number == 1, Ct);
        var search = WorkItemFactory.NewProviderOp(campaign, "audience.search", Noon);
        search.Status = WorkItemStatus.Succeeded;
        db.WorkItems.Add(search);
        await db.SaveChangesAsync(Ct);
        new JournalWriter(new FixedClock(new DateTimeOffset(Noon))).AppendDocumented(
            db,
            Actors.Dispatcher,
            JournalKinds.ApprovalStandingApplied,
            campaign,
            key: standing,
            with: new JsonObject
            {
                ["operation"] = "audience.search",
                ["standing"] = standing,
                ["verdict"] = null,
                ["category"] = null,
                ["audience"] = new JsonObject { ["id"] = audience.PublicId, ["number"] = audience.Number },
                ["count"] = count,
            },
            workItem: search);
        await db.SaveChangesAsync(Ct);
    }

    /// <summary>A campaign holding two audiences, numbered 1 and 2, as the claims of their first searches made them.</summary>
    private static async Task<Campaign> SeedAsync(JasonDbContext db)
    {
        var campaign = WorkItemFactory.NewCampaign(now: Noon);
        db.Campaigns.Add(campaign);
        foreach (var (number, industry) in new[] { (1, "Software"), (2, "Logistics") })
        {
            db.Audiences.Add(new Audience
            {
                PublicId = PublicId.New("aud"),
                Campaign = campaign,
                Number = number,
                Definition = new JsonObject { ["industries"] = new JsonArray(industry) },
                DefinitionHash = "sha256:" + new string((char)('0' + number), 64),
                CreatedAt = Noon,
            });
        }

        await db.SaveChangesAsync(Ct);
        return campaign;
    }
}
