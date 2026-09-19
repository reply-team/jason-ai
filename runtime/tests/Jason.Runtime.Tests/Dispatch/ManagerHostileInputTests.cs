using Jason.Contracts.Api;
using Jason.Runtime.Configuration;
using Jason.Runtime.Dispatch;
using Jason.Runtime.Domain;
using Jason.Runtime.Journal;
using Jason.Runtime.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Jason.Runtime.Tests.Dispatch;

/// <summary>
/// What the loop does with the states nobody writes on purpose: a chronicle that arrives all at once, a
/// watermark from a database that was restored, a campaign that ends between one read and the next.
/// </summary>
public class ManagerHostileInputTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly DateTime Noon = new(2026, 9, 14, 12, 0, 0, DateTimeKind.Utc);

    /// <summary>
    /// Two thousand failures — an import that went wrong — are read five hundred at a time and reviewed one at
    /// a time. The cap is there so that one campaign's bad afternoon cannot hold a scan while it is counted;
    /// what is not read this time is read the next time there is a review to read it.
    /// </summary>
    [Fact]
    public async Task A_chronicle_that_arrives_all_at_once_is_read_in_bounded_bites()
    {
        using var harness = new DispatchHarness(Noon, manager: new ManagerOptions { MaxEntriesPerScan = 500 });
        var campaign = await SeedAsync(harness);
        await FloodAsync(harness, campaign, 2_000);

        Assert.Equal(1, await harness.SummonAsync(Ct));

        int watermark;
        string first;
        await using (var db = harness.Open())
        {
            var stored = await db.Campaigns.SingleAsync(Ct);
            watermark = stored.ManagerEventWatermark;
            var read = await db.Journal.CountAsync(e => e.Id <= watermark, Ct);
            Assert.Equal(500, read);

            var checkIn = await db.WorkItems.SingleAsync(w => w.Role == ManagerCheckIn.Role, Ct);
            Assert.Equal(500, (int?)checkIn.Context["cause"]!["qualifying_count"]);
            first = checkIn.PublicId;
        }

        // The rest waits for the next review rather than for the next scan.
        Assert.Equal(0, await harness.SummonAsync(Ct));

        await FinishAsync(harness, first);
        Assert.Equal(1, await harness.SummonAsync(Ct));

        await using (var db = harness.Open())
        {
            var next = (await db.Campaigns.SingleAsync(Ct)).ManagerEventWatermark;
            Assert.Equal(1_000, await db.Journal.CountAsync(e => e.Id <= next, Ct));
        }
    }

    /// <summary>
    /// A database restored from a backup can hold a campaign whose watermark is past the last line anybody
    /// wrote. Nothing is read, nothing is summoned, and the watermark is left exactly where it was rather than
    /// being "corrected" backwards into replaying a chronicle that has already been reviewed.
    /// </summary>
    [Fact]
    public async Task A_watermark_past_the_end_of_the_chronicle_summons_nothing_and_is_left_alone()
    {
        using var harness = new DispatchHarness(Noon, manager: new ManagerOptions { ReviewSeconds = 86_400 });
        var campaign = await SeedAsync(harness);
        await FloodAsync(harness, campaign, 3);

        await using (var db = harness.Open())
        {
            var stored = await db.Campaigns.SingleAsync(Ct);
            stored.ManagerEventWatermark = 999_999;
            await db.SaveChangesAsync(Ct);
        }

        Assert.Equal(0, await harness.SummonAsync(Ct));

        await using (var db = harness.Open())
        {
            Assert.Equal(999_999, (await db.Campaigns.SingleAsync(Ct)).ManagerEventWatermark);
            Assert.Empty(await db.WorkItems.Where(w => w.Role == ManagerCheckIn.Role).ToListAsync(Ct));
        }
    }

    /// <summary>
    /// A campaign archived while the scan was deciding about it. The summon re-reads the campaign inside its own
    /// transaction and stops there, so a review is never created for a campaign nobody can act on.
    /// </summary>
    [Fact]
    public async Task A_campaign_that_ends_before_the_check_in_is_written_gets_none()
    {
        using var harness = new DispatchHarness(Noon, manager: new ManagerOptions { ReviewSeconds = 86_400 });
        var campaign = await SeedAsync(harness);
        await FloodAsync(harness, campaign, 1);

        await using (var db = harness.Open())
        {
            var stored = await db.Campaigns.SingleAsync(Ct);
            stored.Status = CampaignStatus.Archived;
            stored.ArchivedAt = Noon;
            await db.SaveChangesAsync(Ct);
        }

        Assert.Equal(0, await harness.SummonAsync(Ct));

        await using (var db = harness.Open())
        {
            Assert.Empty(await db.WorkItems.Where(w => w.Role == ManagerCheckIn.Role).ToListAsync(Ct));

            // And its lines are still there, unaccounted for: a campaign that is over consumes nothing either.
            Assert.Equal(0, (await db.Campaigns.SingleAsync(Ct)).ManagerEventWatermark);
        }
    }

    /// <summary>An empty trigger list is cadence-only, and reads the chronicle without ever finding a cause.</summary>
    [Fact]
    public async Task With_no_triggers_configured_the_chronicle_summons_nothing_but_is_still_consumed()
    {
        using var harness = new DispatchHarness(Noon, manager: new ManagerOptions { Triggers = [], ReviewSeconds = 86_400 });
        var campaign = await SeedAsync(harness);
        await FloodAsync(harness, campaign, 5);

        Assert.Equal(0, await harness.SummonAsync(Ct));

        await using var db = harness.Open();
        Assert.True((await db.Campaigns.SingleAsync(Ct)).ManagerEventWatermark > 0);
    }

    private static async Task<string> SeedAsync(DispatchHarness harness)
    {
        await using var db = harness.Open();
        var campaign = WorkItemFactory.NewCampaign("Work", CampaignStatus.Active, now: Noon);
        campaign.ManagerReviewAnchor = Noon;
        db.Campaigns.Add(campaign);
        await db.SaveChangesAsync(Ct);
        return campaign.PublicId;
    }

    private static async Task FloodAsync(DispatchHarness harness, string campaignPublicId, int lines)
    {
        await using var db = harness.Open();
        var campaign = await db.Campaigns.SingleAsync(c => c.PublicId == campaignPublicId, Ct);
        var writer = new JournalWriter(harness.Clock);
        for (var i = 0; i < lines; i++)
        {
            writer.Append(db, Actors.Dispatcher, JournalKinds.WorkItemFailed, campaign, key: "status");
        }

        await db.SaveChangesAsync(Ct);
    }

    private static async Task FinishAsync(DispatchHarness harness, string workItemPublicId)
    {
        await using var db = harness.Open();
        var item = await db.WorkItems.SingleAsync(w => w.PublicId == workItemPublicId, Ct);
        item.Status = WorkItemStatus.Succeeded;
        item.FinishedAt = Noon;
        await db.SaveChangesAsync(Ct);
    }
}
