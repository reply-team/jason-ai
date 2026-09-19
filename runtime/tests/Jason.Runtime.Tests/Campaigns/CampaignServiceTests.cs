using Jason.Contracts.Api;
using Jason.Contracts.Json;
using Jason.Runtime.Campaigns;
using Jason.Runtime.Domain;
using Jason.Runtime.Journal;
using Jason.Runtime.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Jason.Runtime.Tests.Campaigns;

public class CampaignServiceTests
{
    private static readonly DateTimeOffset Noon = new(2026, 9, 14, 12, 0, 0, TimeSpan.Zero);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_new_campaign_is_a_draft_with_an_empty_context_and_one_creation_entry()
    {
        using var database = new TestDatabase();
        using var db = database.Open();
        var clock = new FixedClock(Noon);
        var service = NewService(db, clock);

        var campaign = await service.CreateAsync(new CampaignCreateRequest("  LatAm founders  ", null, null, "kickoff"), Ct);

        Assert.StartsWith("cmp_", campaign.Id, StringComparison.Ordinal);
        Assert.Equal("LatAm founders", campaign.Name);
        Assert.Equal(CampaignStatus.Draft, campaign.Status);
        Assert.Empty(campaign.Context);
        Assert.Equal(Noon, campaign.CreatedAt);
        Assert.Equal(Noon, campaign.UpdatedAt);
        Assert.Null(campaign.ArchivedAt);

        var entry = Assert.Single(await db.Journal.Include(e => e.Campaign).ToListAsync(Ct));
        Assert.Equal(JournalKinds.CampaignCreated, entry.Kind);
        Assert.Equal("name", entry.Key);
        Assert.Null(entry.Old);
        Assert.Equal("LatAm founders", (string?)entry.New);
        Assert.Equal("kickoff", entry.Reason);
        Assert.Equal(ActorType.Human, entry.ActorType);
        Assert.Equal(campaign.Id, entry.Campaign!.PublicId);
    }

    [Fact]
    public async Task A_campaign_created_with_a_context_keeps_it_and_records_the_claimed_role()
    {
        using var database = new TestDatabase();
        using var db = database.Open();
        var service = NewService(db, new FixedClock(Noon));

        var campaign = await service.CreateAsync(
            new CampaignCreateRequest("LatAm", new() { ["icp"] = "founders" }, new ActorRef(ActorType.Role, "planner"), null),
            Ct);

        Assert.Equal("founders", (string?)campaign.Context["icp"]);
        var entry = Assert.Single(await db.Journal.ToListAsync(Ct));
        Assert.Equal(ActorType.Role, entry.ActorType);
        Assert.Equal("planner", entry.ActorId);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task A_campaign_without_a_name_is_refused(string? name)
    {
        using var database = new TestDatabase();
        using var db = database.Open();
        var service = NewService(db, new FixedClock(Noon));

        var error = await Assert.ThrowsAsync<ValidationException>(() => service.CreateAsync(new CampaignCreateRequest(name, null, null, null), Ct));

        var detail = Assert.Single(error.Details!);
        Assert.Equal("name", detail.Field);
        Assert.Equal("required", detail.Code);
    }

    [Fact]
    public async Task A_name_longer_than_the_limit_is_refused_without_echoing_it()
    {
        using var database = new TestDatabase();
        using var db = database.Open();
        var service = NewService(db, new FixedClock(Noon));
        var name = new string('a', 201);

        var error = await Assert.ThrowsAsync<ValidationException>(() => service.CreateAsync(new CampaignCreateRequest(name, null, null, null), Ct));

        var detail = Assert.Single(error.Details!);
        Assert.Equal("name", detail.Field);
        Assert.Equal("too_long", detail.Code);
        Assert.DoesNotContain(name, detail.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(name, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_reason_longer_than_the_limit_is_refused()
    {
        using var database = new TestDatabase();
        using var db = database.Open();
        var service = NewService(db, new FixedClock(Noon));

        var error = await Assert.ThrowsAsync<ValidationException>(() => service.CreateAsync(new CampaignCreateRequest("LatAm", null, null, new string('r', 2001)), Ct));

        var detail = Assert.Single(error.Details!);
        Assert.Equal("reason", detail.Field);
        Assert.Equal("too_long", detail.Code);
    }

    [Fact]
    public async Task An_unknown_campaign_is_not_found_and_a_missing_id_is_a_validation_failure()
    {
        using var database = new TestDatabase();
        using var db = database.Open();
        var service = NewService(db, new FixedClock(Noon));

        var notFound = await Assert.ThrowsAsync<NotFoundException>(() => service.GetAsync(new CampaignGetRequest("cmp_01JASONNOTHERE"), Ct));
        Assert.Equal("campaign_not_found", notFound.Code);

        var missing = await Assert.ThrowsAsync<ValidationException>(() => service.GetAsync(new CampaignGetRequest(null), Ct));
        Assert.Equal("campaign_id", Assert.Single(missing.Details!).Field);
    }

    [Fact]
    public async Task Getting_a_campaign_returns_it_with_its_context()
    {
        using var database = new TestDatabase();
        using var db = database.Open();
        var service = NewService(db, new FixedClock(Noon));
        var created = await service.CreateAsync(new CampaignCreateRequest("LatAm", new() { ["icp"] = "founders" }, null, null), Ct);

        var fetched = await service.GetAsync(new CampaignGetRequest(created.Id), Ct);

        Assert.Equal(created.Id, fetched.Id);
        Assert.Equal("founders", (string?)fetched.Context["icp"]);
    }

    [Fact]
    public async Task Renaming_a_campaign_journals_the_old_and_the_new_name()
    {
        using var database = new TestDatabase();
        using var db = database.Open();
        var clock = new FixedClock(Noon);
        var service = NewService(db, clock);
        var created = await service.CreateAsync(new CampaignCreateRequest("LatAm", null, null, null), Ct);
        clock.Advance(TimeSpan.FromMinutes(5));

        var renamed = await service.UpdateAsync(new CampaignUpdateRequest(created.Id, Optional<string?>.Of("EMEA"), Optional<string?>.Absent, Optional<int?>.Absent, null, "narrowed the region"), Ct);

        Assert.Equal("EMEA", renamed.Name);
        Assert.Equal(Noon, renamed.CreatedAt);
        Assert.Equal(Noon.AddMinutes(5), renamed.UpdatedAt);

        var entry = Assert.Single(await db.Journal.Where(e => e.Kind == JournalKinds.CampaignUpdated).ToListAsync(Ct));
        Assert.Equal("name", entry.Key);
        Assert.Equal("LatAm", (string?)entry.Old);
        Assert.Equal("EMEA", (string?)entry.New);
        Assert.Equal("narrowed the region", entry.Reason);
    }

    [Fact]
    public async Task An_update_that_changes_nothing_writes_nothing()
    {
        using var database = new TestDatabase();
        using var db = database.Open();
        var clock = new FixedClock(Noon);
        var service = NewService(db, clock);
        var created = await service.CreateAsync(new CampaignCreateRequest("LatAm", null, null, null), Ct);
        clock.Advance(TimeSpan.FromMinutes(5));

        var absent = await service.UpdateAsync(new CampaignUpdateRequest(created.Id, Optional<string?>.Absent, Optional<string?>.Absent, Optional<int?>.Absent, null, null), Ct);
        var same = await service.UpdateAsync(new CampaignUpdateRequest(created.Id, Optional<string?>.Of("LatAm"), Optional<string?>.Absent, Optional<int?>.Absent, null, null), Ct);

        Assert.Equal("LatAm", absent.Name);
        Assert.Equal(Noon, absent.UpdatedAt);
        Assert.Equal(Noon, same.UpdatedAt);
        Assert.Single(await db.Journal.ToListAsync(Ct));
    }

    [Fact]
    public async Task A_name_cannot_be_cleared_by_patching_it_to_null()
    {
        using var database = new TestDatabase();
        using var db = database.Open();
        var service = NewService(db, new FixedClock(Noon));
        var created = await service.CreateAsync(new CampaignCreateRequest("LatAm", null, null, null), Ct);

        var error = await Assert.ThrowsAsync<ValidationException>(
            () => service.UpdateAsync(new CampaignUpdateRequest(created.Id, Optional<string?>.Of(null), Optional<string?>.Absent, Optional<int?>.Absent, null, null), Ct));

        Assert.Equal("required", Assert.Single(error.Details!).Code);
    }

    [Fact]
    public async Task Archived_campaigns_are_hidden_from_a_listing_unless_they_are_asked_for()
    {
        using var database = new TestDatabase();
        using var db = database.Open();
        var service = NewService(db, new FixedClock(Noon));
        var kept = await service.CreateAsync(new CampaignCreateRequest("kept", null, null, null), Ct);
        var running = await service.CreateAsync(new CampaignCreateRequest("running", null, null, null), Ct);
        var gone = await service.CreateAsync(new CampaignCreateRequest("gone", null, null, null), Ct);
        await service.StartAsync(new CampaignTransitionRequest(running.Id, null, null), Ct);
        await service.ArchiveAsync(new CampaignTransitionRequest(gone.Id, null, null), Ct);

        var live = await service.ListAsync(new CampaignListRequest(null, null, null), Ct);
        var archived = await service.ListAsync(new CampaignListRequest(CampaignStatus.Archived, null, null), Ct);
        var active = await service.ListAsync(new CampaignListRequest(CampaignStatus.Active, null, null), Ct);

        Assert.Equal([kept.Id, running.Id], live.Items.Select(c => c.Id));
        Assert.Null(live.NextCursor);
        Assert.Equal(gone.Id, Assert.Single(archived.Items).Id);
        Assert.Equal(running.Id, Assert.Single(active.Items).Id);
    }

    [Fact]
    public async Task A_listing_walks_every_campaign_in_creation_order_without_gaps_or_repeats()
    {
        using var database = new TestDatabase();
        using var db = database.Open();
        var service = NewService(db, new FixedClock(Noon));
        var created = new List<string>();
        foreach (var name in new[] { "one", "two", "three", "four", "five" })
        {
            created.Add((await service.CreateAsync(new CampaignCreateRequest(name, null, null, null), Ct)).Id);
        }

        var seen = new List<string>();
        string? cursor = null;
        var pages = 0;
        do
        {
            var page = await service.ListAsync(new CampaignListRequest(null, 2, cursor), Ct);
            seen.AddRange(page.Items.Select(c => c.Id));
            cursor = page.NextCursor;
            pages++;
        }
        while (cursor is not null);

        Assert.Equal(created, seen);
        Assert.Equal(3, pages);
    }

    [Fact]
    public async Task A_summary_carries_the_lifecycle_timestamps()
    {
        using var database = new TestDatabase();
        using var db = database.Open();
        var clock = new FixedClock(Noon);
        var service = NewService(db, clock);
        var created = await service.CreateAsync(new CampaignCreateRequest("LatAm", new() { ["icp"] = "founders" }, null, null), Ct);
        clock.Advance(TimeSpan.FromHours(1));
        await service.ArchiveAsync(new CampaignTransitionRequest(created.Id, null, null), Ct);

        var summary = Assert.Single((await service.ListAsync(new CampaignListRequest(CampaignStatus.Archived, null, null), Ct)).Items);

        Assert.Equal(created.Id, summary.Id);
        Assert.Equal(CampaignStatus.Archived, summary.Status);
        Assert.Equal(Noon, summary.CreatedAt);
        Assert.Equal(Noon.AddHours(1), summary.UpdatedAt);
        Assert.Equal(Noon.AddHours(1), summary.ArchivedAt);
    }

    private static CampaignService NewService(JasonDbContext db, TimeProvider clock) => new(db, new JournalWriter(clock), clock, TestCanceller.New(clock));
}
