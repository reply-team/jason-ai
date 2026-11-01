using Jason.Contracts.Api;
using Jason.Runtime.Campaigns;
using Jason.Runtime.Journal;
using Jason.Runtime.Persistence;
using Jason.Runtime.Tests.Harness;
using Microsoft.EntityFrameworkCore;

namespace Jason.Runtime.Tests.Campaigns;

/// <summary>
/// Where a campaign's inbound read first starts. Its first going live is the moment: what happened in a person's
/// conversations before the campaign existed for them is history, not a reply to it. A pause and a resume move nothing,
/// because a reply that arrived while the campaign was paused is still a reply the manager has not seen.
/// </summary>
public class InboundWatermarkTests
{
    private static readonly DateTime Noon = new(2026, 10, 6, 12, 0, 0, DateTimeKind.Utc);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_draft_has_no_watermark_and_its_first_going_live_sets_it()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var clock = new FixedClock(Noon);
        var campaigns = Campaigns(db, clock);
        var created = await campaigns.CreateAsync(new CampaignCreateRequest("LatAm", null, null, null), Ct);
        Assert.Null((await db.Campaigns.SingleAsync(Ct)).InboundWatermark);

        clock.Advance(TimeSpan.FromHours(3));
        await campaigns.StartAsync(new CampaignTransitionRequest(created.Id, null, null), Ct);

        Assert.Equal(Noon.AddHours(3), (await db.Campaigns.SingleAsync(Ct)).InboundWatermark);
    }

    [Fact]
    public async Task A_pause_and_a_resume_keep_the_watermark_where_it_was()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var clock = new FixedClock(Noon);
        var campaigns = Campaigns(db, clock);
        var created = await campaigns.CreateAsync(new CampaignCreateRequest("LatAm", null, null, null), Ct);
        await campaigns.StartAsync(new CampaignTransitionRequest(created.Id, null, null), Ct);

        clock.Advance(TimeSpan.FromHours(2));
        await campaigns.PauseAsync(new CampaignTransitionRequest(created.Id, null, null), Ct);
        Assert.Equal(Noon, (await db.Campaigns.SingleAsync(Ct)).InboundWatermark);

        clock.Advance(TimeSpan.FromDays(7));
        await campaigns.StartAsync(new CampaignTransitionRequest(created.Id, null, null), Ct);

        Assert.Equal(Noon, (await db.Campaigns.SingleAsync(Ct)).InboundWatermark);
    }

    /// <summary>A campaign that already has one — given by an upgrade, or moved by a read — keeps it when it goes live.</summary>
    [Fact]
    public async Task A_watermark_already_on_record_is_kept_when_the_campaign_goes_live()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var clock = new FixedClock(Noon);
        var campaigns = Campaigns(db, clock);
        var created = await campaigns.CreateAsync(new CampaignCreateRequest("LatAm", null, null, null), Ct);
        await campaigns.StartAsync(new CampaignTransitionRequest(created.Id, null, null), Ct);
        await campaigns.PauseAsync(new CampaignTransitionRequest(created.Id, null, null), Ct);

        var earlier = Noon.AddDays(-1);
        await db.Campaigns.ExecuteUpdateAsync(set => set.SetProperty(c => c.InboundWatermark, earlier), Ct);
        db.ChangeTracker.Clear();

        clock.Advance(TimeSpan.FromHours(1));
        await campaigns.StartAsync(new CampaignTransitionRequest(created.Id, null, null), Ct);

        Assert.Equal(earlier, (await db.Campaigns.SingleAsync(Ct)).InboundWatermark);
    }

    private static CampaignService Campaigns(JasonDbContext db, TimeProvider clock) =>
        new(db, new JournalWriter(clock), clock, TestCanceller.New(clock), TestHarness.FoldersFor(db));
}
