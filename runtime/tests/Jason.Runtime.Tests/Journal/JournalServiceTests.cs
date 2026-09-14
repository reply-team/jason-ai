using System.Text.Json.Nodes;
using Jason.Contracts.Api;
using Jason.Runtime.Campaigns;
using Jason.Runtime.Domain;
using Jason.Runtime.Journal;
using Jason.Runtime.Persistence;

namespace Jason.Runtime.Tests.Journal;

public class JournalServiceTests
{
    private static readonly DateTimeOffset Noon = new(2026, 9, 14, 12, 0, 0, TimeSpan.Zero);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task An_appended_entry_gets_a_public_id_and_a_human_actor_by_default()
    {
        using var database = new TestDatabase();
        using var db = database.Open();
        var clock = new FixedClock(Noon);
        var campaigns = NewCampaignService(db, clock);
        var service = NewJournalService(db, clock);
        var campaign = await CreateAsync(campaigns);
        clock.Advance(TimeSpan.FromMinutes(3));

        var entry = await service.AppendAsync(
            new JournalAppendRequest(campaign, "plan_revision", "step_2", JsonValue.Create("send on tuesday"), null, "the buyer asked for a week"),
            Ct);

        Assert.StartsWith("jrn_", entry.Id, StringComparison.Ordinal);
        Assert.Equal(Noon.AddMinutes(3), entry.Ts);
        Assert.Equal(new ActorRef(ActorType.Human), entry.Actor);
        Assert.Equal("plan_revision", entry.Kind);
        Assert.Equal(campaign, entry.CampaignId);
        Assert.Equal("step_2", entry.Key);
        Assert.Null(entry.Old);
        Assert.Equal("send on tuesday", (string?)entry.New);
        Assert.Equal("the buyer asked for a week", entry.Reason);
    }

    [Fact]
    public async Task A_role_actor_is_recorded_exactly_as_it_was_claimed()
    {
        using var database = new TestDatabase();
        using var db = database.Open();
        var clock = new FixedClock(Noon);
        var campaign = await CreateAsync(NewCampaignService(db, clock));
        var service = NewJournalService(db, clock);

        var entry = await service.AppendAsync(
            new JournalAppendRequest(campaign, "observation", null, null, new ActorRef(ActorType.Role, " planner "), null),
            Ct);

        Assert.Equal(new ActorRef(ActorType.Role, "planner"), entry.Actor);
    }

    [Theory]
    [InlineData(JournalKinds.CampaignCreated)]
    [InlineData(JournalKinds.ContextUpdated)]
    [InlineData(JournalKinds.SuppressionRemoved)]
    public async Task A_kind_the_runtime_writes_itself_cannot_be_appended_by_a_caller(string kind)
    {
        using var database = new TestDatabase();
        using var db = database.Open();
        var clock = new FixedClock(Noon);
        var campaign = await CreateAsync(NewCampaignService(db, clock));
        var service = NewJournalService(db, clock);

        var error = await Assert.ThrowsAsync<InvalidRequestException>(
            () => service.AppendAsync(new JournalAppendRequest(campaign, kind, null, null, null, null), Ct));

        Assert.Equal("reserved_kind", error.Code);
        Assert.False(error.Retryable);
    }

    [Theory]
    [InlineData(null, "required")]
    [InlineData("   ", "required")]
    [InlineData("Plan-Revision", "invalid")]
    [InlineData("_leading", "invalid")]
    [InlineData("plan revision", "invalid")]
    public async Task A_kind_must_be_present_and_well_formed(string? kind, string code)
    {
        using var database = new TestDatabase();
        using var db = database.Open();
        var clock = new FixedClock(Noon);
        var campaign = await CreateAsync(NewCampaignService(db, clock));
        var service = NewJournalService(db, clock);

        var error = await Assert.ThrowsAsync<ValidationException>(
            () => service.AppendAsync(new JournalAppendRequest(campaign, kind, null, null, null, null), Ct));

        var detail = Assert.Single(error.Details!);
        Assert.Equal("kind", detail.Field);
        Assert.Equal(code, detail.Code);
    }

    [Fact]
    public async Task Appending_needs_a_campaign_that_exists()
    {
        using var database = new TestDatabase();
        using var db = database.Open();
        var service = NewJournalService(db, new FixedClock(Noon));

        var unknown = await Assert.ThrowsAsync<NotFoundException>(
            () => service.AppendAsync(new JournalAppendRequest("cmp_01JASONNOTHERE", "observation", null, null, null, null), Ct));
        var missing = await Assert.ThrowsAsync<ValidationException>(
            () => service.AppendAsync(new JournalAppendRequest(null, "observation", null, null, null, null), Ct));

        Assert.Equal("campaign_not_found", unknown.Code);
        Assert.Equal("campaign_id", Assert.Single(missing.Details!).Field);
    }

    [Fact]
    public async Task An_archived_campaign_still_accepts_entries()
    {
        using var database = new TestDatabase();
        using var db = database.Open();
        var clock = new FixedClock(Noon);
        var campaigns = NewCampaignService(db, clock);
        var campaign = await CreateAsync(campaigns);
        await campaigns.ArchiveAsync(new CampaignTransitionRequest(campaign, null, null), Ct);
        var service = NewJournalService(db, clock);

        var entry = await service.AppendAsync(new JournalAppendRequest(campaign, "retrospective", null, null, null, null), Ct);

        Assert.Equal("retrospective", entry.Kind);
        Assert.Equal(campaign, entry.CampaignId);
    }

    [Fact]
    public async Task A_key_a_value_or_a_reason_beyond_its_limit_is_refused()
    {
        using var database = new TestDatabase();
        using var db = database.Open();
        var clock = new FixedClock(Noon);
        var campaign = await CreateAsync(NewCampaignService(db, clock));
        var service = NewJournalService(db, clock);

        var key = await Assert.ThrowsAsync<ValidationException>(
            () => service.AppendAsync(new JournalAppendRequest(campaign, "observation", new string('k', 201), null, null, null), Ct));
        var value = await Assert.ThrowsAsync<ValidationException>(
            () => service.AppendAsync(new JournalAppendRequest(campaign, "observation", null, JsonValue.Create(new string('v', 64 * 1024)), null, null), Ct));
        var reason = await Assert.ThrowsAsync<ValidationException>(
            () => service.AppendAsync(new JournalAppendRequest(campaign, "observation", null, null, null, new string('r', 2001)), Ct));

        Assert.Equal("key", Assert.Single(key.Details!).Field);
        Assert.Equal("new", Assert.Single(value.Details!).Field);
        Assert.Equal("reason", Assert.Single(reason.Details!).Field);
    }

    [Fact]
    public async Task The_chronicle_reads_newest_first()
    {
        using var database = new TestDatabase();
        using var db = database.Open();
        var clock = new FixedClock(Noon);
        var campaign = await CreateAsync(NewCampaignService(db, clock));
        var service = NewJournalService(db, clock);
        var first = await service.AppendAsync(new JournalAppendRequest(campaign, "observation", null, null, null, null), Ct);
        var second = await service.AppendAsync(new JournalAppendRequest(campaign, "decision", null, null, null, null), Ct);

        var page = await service.ListAsync(new JournalListRequest(campaign, null, null, null, null, null), Ct);

        Assert.Equal([second.Id, first.Id], page.Items.Take(2).Select(e => e.Id));
        Assert.Equal(JournalKinds.CampaignCreated, page.Items[^1].Kind);
        Assert.All(page.Items, entry => Assert.Equal(campaign, entry.CampaignId));
        Assert.Null(page.NextCursor);
    }

    [Fact]
    public async Task The_kind_and_since_filters_narrow_the_chronicle()
    {
        using var database = new TestDatabase();
        using var db = database.Open();
        var clock = new FixedClock(Noon);
        var campaign = await CreateAsync(NewCampaignService(db, clock));
        var service = NewJournalService(db, clock);
        await service.AppendAsync(new JournalAppendRequest(campaign, "observation", "early", null, null, null), Ct);
        clock.Advance(TimeSpan.FromHours(1));
        var late = await service.AppendAsync(new JournalAppendRequest(campaign, "observation", "late", null, null, null), Ct);
        await service.AppendAsync(new JournalAppendRequest(campaign, "decision", null, null, null, null), Ct);

        var byKind = await service.ListAsync(new JournalListRequest(campaign, null, "observation", null, null, null), Ct);
        var since = await service.ListAsync(new JournalListRequest(campaign, null, "observation", Noon.AddMinutes(30), null, null), Ct);

        Assert.Equal(["late", "early"], byKind.Items.Select(e => e.Key));
        Assert.Equal(late.Id, Assert.Single(since.Items).Id);
    }

    [Fact]
    public async Task A_campaign_filter_never_leaks_another_campaigns_or_a_global_entry()
    {
        using var database = new TestDatabase();
        using var db = database.Open();
        var clock = new FixedClock(Noon);
        var campaigns = NewCampaignService(db, clock);
        var mine = await CreateAsync(campaigns);
        var theirs = await CreateAsync(campaigns);
        var service = NewJournalService(db, clock);
        var kept = await service.AppendAsync(new JournalAppendRequest(mine, "observation", null, null, null, null), Ct);
        await service.AppendAsync(new JournalAppendRequest(theirs, "observation", null, null, null, null), Ct);
        new JournalWriter(clock).Append(db, Actors.Runtime, JournalKinds.SuppressionAdded, campaign: null, key: "email");
        await db.SaveChangesAsync(Ct);

        var scoped = await service.ListAsync(new JournalListRequest(mine, null, "observation", null, null, null), Ct);
        var everything = await service.ListAsync(new JournalListRequest(null, null, null, null, null, null), Ct);

        Assert.Equal(kept.Id, Assert.Single(scoped.Items).Id);
        Assert.Single(everything.Items, entry => entry.Kind == JournalKinds.SuppressionAdded && entry.CampaignId is null);
        Assert.Equal(5, everything.Items.Count);
    }

    [Fact]
    public async Task A_listing_walks_every_entry_with_no_gaps_and_no_repeats()
    {
        using var database = new TestDatabase();
        using var db = database.Open();
        var clock = new FixedClock(Noon);
        var campaign = await CreateAsync(NewCampaignService(db, clock));
        var service = NewJournalService(db, clock);
        var appended = new List<string>();
        for (var index = 0; index < 7; index++)
        {
            appended.Add((await service.AppendAsync(new JournalAppendRequest(campaign, "observation", null, null, null, null), Ct)).Id);
        }

        appended.Reverse();

        var seen = new List<string>();
        string? cursor = null;
        var pages = 0;
        do
        {
            var page = await service.ListAsync(new JournalListRequest(campaign, null, "observation", null, 3, cursor), Ct);
            seen.AddRange(page.Items.Select(e => e.Id));
            cursor = page.NextCursor;
            pages++;
        }
        while (cursor is not null);

        Assert.Equal(appended, seen);
        Assert.Equal(3, pages);
    }

    [Fact]
    public async Task The_chronicle_narrows_to_one_work_item()
    {
        using var database = new TestDatabase();
        using var db = database.Open();
        var clock = new FixedClock(Noon);
        var campaignId = await CreateAsync(NewCampaignService(db, clock));
        var service = NewJournalService(db, clock);
        var campaign = db.Campaigns.Single(c => c.PublicId == campaignId);
        var writer = new JournalWriter(clock);

        var mine = NewWorkItem(db, campaign, "wi_MINE");
        var other = NewWorkItem(db, campaign, "wi_OTHER");
        var attempt = new Attempt
        {
            PublicId = "att_A",
            WorkItem = mine,
            Number = 1,
            Command = WorkItemKind.AiRole,
            Status = AttemptStatus.Running,
            ClaimedAt = Noon.UtcDateTime,
            LockUntil = Noon.UtcDateTime.AddHours(1),
        };
        db.Attempts.Add(attempt);
        await db.SaveChangesAsync(Ct);

        writer.Append(db, Actors.Runtime, JournalKinds.WorkItemScheduled, campaign: null, key: "attempt", workItem: mine, attempt: attempt);
        writer.Append(db, Actors.Runtime, JournalKinds.WorkItemExpired, campaign: null, key: "due_at", workItem: other);
        await db.SaveChangesAsync(Ct);

        var page = await service.ListAsync(new JournalListRequest(null, "wi_MINE", null, null, null, null), Ct);

        var entry = Assert.Single(page.Items);
        Assert.Equal(JournalKinds.WorkItemScheduled, entry.Kind);
        Assert.Equal("wi_MINE", entry.WorkItemId);
        Assert.Equal("att_A", entry.AttemptId);
        Assert.Equal(campaignId, entry.CampaignId);

        // Combines with the other filters rather than replacing them.
        Assert.Empty((await service.ListAsync(new JournalListRequest(campaignId, "wi_MINE", JournalKinds.WorkItemExpired, null, null, null), Ct)).Items);
        Assert.Single((await service.ListAsync(new JournalListRequest(campaignId, "wi_MINE", JournalKinds.WorkItemScheduled, null, null, null), Ct)).Items);
    }

    private static WorkItem NewWorkItem(JasonDbContext db, Campaign campaign, string publicId)
    {
        var item = new WorkItem
        {
            PublicId = publicId,
            Campaign = campaign,
            Kind = WorkItemKind.AiRole,
            Role = "researcher",
            CreatedAt = Noon.UtcDateTime,
            UpdatedAt = Noon.UtcDateTime,
        };
        db.WorkItems.Add(item);
        db.SaveChanges();
        return item;
    }

    private static async Task<string> CreateAsync(CampaignService campaigns) =>
        (await campaigns.CreateAsync(new CampaignCreateRequest("LatAm", null, null, null), Ct)).Id;

    private static CampaignService NewCampaignService(JasonDbContext db, TimeProvider clock) => new(db, new JournalWriter(clock), clock, TestCanceller.New(clock));

    private static JournalService NewJournalService(JasonDbContext db, TimeProvider clock) => new(db, new JournalWriter(clock));
}
