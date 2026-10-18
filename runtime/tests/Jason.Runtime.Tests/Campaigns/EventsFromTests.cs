using Jason.Contracts.Api;
using Jason.Runtime.Campaigns;
using Jason.Runtime.Journal;
using Jason.Runtime.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Jason.Runtime.Tests.Campaigns;

/// <summary>
/// From when a provider's events are said in a campaign's chronicle. Its first going live is the moment, as it is for the
/// inbound read: what happened at the provider before the campaign was live is history, not a reply to it. A pause and a
/// resume move nothing, so an event that came in during a pause is said.
/// </summary>
public class EventsFromTests
{
    private static readonly DateTime Noon = new(2026, 10, 18, 12, 0, 0, DateTimeKind.Utc);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_draft_has_none_and_its_first_going_live_sets_it()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var clock = new FixedClock(Noon);
        var campaigns = Campaigns(db, clock);
        var created = await campaigns.CreateAsync(new CampaignCreateRequest("LatAm", null, null, null), Ct);
        Assert.Null((await db.Campaigns.SingleAsync(Ct)).EventsFrom);

        clock.Advance(TimeSpan.FromHours(3));
        await campaigns.StartAsync(new CampaignTransitionRequest(created.Id, null, null), Ct);

        Assert.Equal(Noon.AddHours(3), (await db.Campaigns.SingleAsync(Ct)).EventsFrom);
    }

    [Fact]
    public async Task A_pause_and_a_resume_keep_it_where_it_was()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var clock = new FixedClock(Noon);
        var campaigns = Campaigns(db, clock);
        var created = await campaigns.CreateAsync(new CampaignCreateRequest("LatAm", null, null, null), Ct);
        await campaigns.StartAsync(new CampaignTransitionRequest(created.Id, null, null), Ct);

        clock.Advance(TimeSpan.FromHours(2));
        await campaigns.PauseAsync(new CampaignTransitionRequest(created.Id, null, null), Ct);
        clock.Advance(TimeSpan.FromDays(7));
        await campaigns.StartAsync(new CampaignTransitionRequest(created.Id, null, null), Ct);

        Assert.Equal(Noon, (await db.Campaigns.SingleAsync(Ct)).EventsFrom);
    }

    /// <summary>A campaign an upgrade gave the moment keeps it when it goes live again.</summary>
    [Fact]
    public async Task One_already_on_record_is_kept_when_the_campaign_goes_live()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var clock = new FixedClock(Noon);
        var campaigns = Campaigns(db, clock);
        var created = await campaigns.CreateAsync(new CampaignCreateRequest("LatAm", null, null, null), Ct);
        await campaigns.StartAsync(new CampaignTransitionRequest(created.Id, null, null), Ct);
        await campaigns.PauseAsync(new CampaignTransitionRequest(created.Id, null, null), Ct);

        var earlier = Noon.AddDays(-1);
        await db.Campaigns.ExecuteUpdateAsync(set => set.SetProperty(c => c.EventsFrom, earlier), Ct);
        db.ChangeTracker.Clear();

        clock.Advance(TimeSpan.FromHours(1));
        await campaigns.StartAsync(new CampaignTransitionRequest(created.Id, null, null), Ct);

        Assert.Equal(earlier, (await db.Campaigns.SingleAsync(Ct)).EventsFrom);
    }

    private static CampaignService Campaigns(JasonDbContext db, TimeProvider clock) =>
        new(db, new JournalWriter(clock), clock, TestCanceller.New(clock));
}
