using Jason.Contracts.Api;
using Jason.Runtime.Dispatch;
using Jason.Runtime.Journal;
using Microsoft.EntityFrameworkCore;
using static Jason.Runtime.Tests.Dispatch.InboundSyncHarvestTests;

namespace Jason.Runtime.Tests.Dispatch;

/// <summary>
/// A read longer than a page, and where the next read starts. A read's pages share its start; the next read starts
/// from the start of the last complete one less the look-back, so a reply the provider lists late is still read, and a
/// campaign nobody writes to still moves on with the clock.
/// </summary>
public class InboundSyncPagesTests
{
    private static readonly DateTime Noon = new(2026, 10, 6, 12, 0, 0, DateTimeKind.Utc);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>The provider holds more: the next page is asked for at once, from where this one ended, with the same start.</summary>
    [Fact]
    public async Task A_read_of_three_pages_is_three_items_with_one_start()
    {
        using var harness = new DispatchHarness(Noon);
        var first = await StartedAsync(harness);
        await SucceedAsync(harness, first, Page(hasMore: true, Thread("th-1", Noon.AddMinutes(-1)), Thread("th-2", Noon.AddMinutes(-2))));

        Assert.Equal(new InboundReport(0, 2), await harness.SyncAsync(Ct));

        var second = await NewestReadAsync(harness);
        Assert.NotEqual(first.Id, second.Id);
        Assert.Equal(
            """{"input":{"source":"inbound","since":"2026-10-06T10:00:00Z","limit":100,"offset":2}}""",
            second.Context.ToJsonString());
        await AssertReadingAsync(harness, first.CampaignId, item: second.Id, offset: 2, syncedAt: Noon);

        harness.Clock.Advance(TimeSpan.FromSeconds(5));
        await SucceedAsync(harness, second, Page(hasMore: true, Thread("th-3", Noon.AddMinutes(-3))));
        Assert.Equal(1, (await harness.SyncAsync(Ct)).Observed);

        var third = await NewestReadAsync(harness);
        Assert.Equal(3, (int?)third.Context["input"]!["offset"]);
        await AssertReadingAsync(harness, first.CampaignId, item: third.Id, offset: 3, syncedAt: Noon);

        harness.Clock.Advance(TimeSpan.FromSeconds(5));
        await SucceedAsync(harness, third, Page(Thread("th-4", Noon.AddMinutes(-4))));
        Assert.Equal(1, (await harness.SyncAsync(Ct)).Observed);

        await using var db = harness.Open();
        Assert.Equal(3, await db.WorkItems.CountAsync(Ct));
        Assert.Equal(4, await LinesAsync(harness));
        var row = await db.Campaigns.SingleAsync(c => c.Id == first.CampaignId, Ct);
        Assert.Null(row.InboundSyncItemId);
        Assert.Equal(0, row.InboundOffset);

        // The read started at noon, so the next one starts an hour before noon, whenever the last page came back.
        Assert.Equal(Noon.AddHours(-1), row.InboundWatermark);
        Assert.Equal(Noon, row.InboundSyncedAt);
    }

    /// <summary>
    /// A thread that moved while the read was paging is pushed down the provider's order and listed again on the next
    /// page, at the same moment: it is one move, and said once.
    /// </summary>
    [Fact]
    [Critical(Family.N1, "paging edge")]
    public async Task A_thread_shifted_across_a_page_boundary_is_said_once()
    {
        using var harness = new DispatchHarness(Noon);
        var first = await StartedAsync(harness);
        await SucceedAsync(harness, first, Page(hasMore: true, Thread("th-1", Noon.AddMinutes(-1)), Thread("th-2", Noon.AddMinutes(-2))));
        await harness.SyncAsync(Ct);

        var second = await NewestReadAsync(harness);
        await SucceedAsync(harness, second, Page(Thread("th-2", Noon.AddMinutes(-2)), Thread("th-3", Noon.AddMinutes(-3))));
        Assert.Equal(1, (await harness.SyncAsync(Ct)).Observed);

        Assert.Equal(3, await LinesAsync(harness));
    }

    /// <summary>
    /// A reply the provider listed only after a later one had been read is still said, on the next read: that read
    /// starts an hour behind where the last one started, not at the newest moment it saw.
    /// </summary>
    [Fact]
    public async Task A_reply_the_provider_lists_late_is_said_by_the_next_read()
    {
        using var harness = new DispatchHarness(Noon);
        var first = await StartedAsync(harness);
        await SucceedAsync(harness, first, Page(Thread("th-new", Noon.AddMinutes(-5))));
        await harness.SyncAsync(Ct);

        harness.Clock.Advance(TimeSpan.FromSeconds(900));
        var second = await NextReadAsync(harness);
        Assert.Equal("2026-10-06T11:00:00Z", (string?)second.Context["input"]!["since"]);
        await SucceedAsync(harness, second, Page(Thread("th-new", Noon.AddMinutes(-5)), Thread("th-late", Noon.AddMinutes(-20))));

        Assert.Equal(1, (await harness.SyncAsync(Ct)).Observed);
        await using var db = harness.Open();
        Assert.Contains(
            await db.Journal.Where(entry => entry.Kind == JournalKinds.InboundReplyObserved).ToListAsync(Ct),
            entry => (string?)entry.New!["thread"] == "th-late");
    }

    /// <summary>
    /// The watermark follows the clock, not the replies: a campaign nobody has written to for a week still starts its
    /// next read an hour before its last one, rather than a week back and further each time.
    /// </summary>
    [Fact]
    public async Task A_quiet_campaign_s_watermark_follows_the_clock()
    {
        using var harness = new DispatchHarness(Noon);
        var first = await StartedAsync(harness, watermark: Noon.AddDays(-7));
        await SucceedAsync(harness, first, Page());
        await harness.SyncAsync(Ct);

        harness.Clock.Advance(TimeSpan.FromSeconds(900));
        var second = await NextReadAsync(harness);

        Assert.Equal("2026-10-06T11:00:00Z", (string?)second.Context["input"]!["since"]);
    }

    /// <summary>A watermark later than the read's start less the look-back stays: it never moves back, so never before the go-live.</summary>
    [Fact]
    [Critical(Family.N1, "the inbound watermark")]
    public async Task The_watermark_never_moves_back()
    {
        using var harness = new DispatchHarness(Noon);
        var first = await StartedAsync(harness, watermark: Noon.AddMinutes(-10));
        await SucceedAsync(harness, first, Page());
        await harness.SyncAsync(Ct);

        await using var db = harness.Open();
        Assert.Equal(Noon.AddMinutes(-10), (await db.Campaigns.SingleAsync(c => c.Id == first.CampaignId, Ct)).InboundWatermark);
    }

    /// <summary>With no look-back the next read starts where the last one started, and a reply listed late is not said.</summary>
    [Fact]
    public async Task With_no_look_back_the_next_read_starts_where_the_last_one_did()
    {
        using var harness = new DispatchHarness(Noon);
        harness.Inbound.CurrentValue.LookbackSeconds = 0;
        var first = await StartedAsync(harness);
        await SucceedAsync(harness, first, Page());
        await harness.SyncAsync(Ct);

        await using var db = harness.Open();
        Assert.Equal(Noon, (await db.Campaigns.SingleAsync(c => c.Id == first.CampaignId, Ct)).InboundWatermark);
    }

    /// <summary>A provider that says it holds more and returns nothing would be asked for the same page for ever; the read ends.</summary>
    [Fact]
    public async Task An_empty_page_that_says_more_follow_ends_the_read()
    {
        using var harness = new DispatchHarness(Noon);
        var first = await StartedAsync(harness);
        await SucceedAsync(harness, first, Page(hasMore: true));

        await harness.SyncAsync(Ct);

        await using var db = harness.Open();
        Assert.Equal(1, await db.WorkItems.CountAsync(Ct));
        Assert.Null((await db.Campaigns.SingleAsync(c => c.Id == first.CampaignId, Ct)).InboundSyncItemId);
    }

    private static async Task AssertReadingAsync(DispatchHarness harness, int campaign, int item, int offset, DateTime syncedAt)
    {
        await using var db = harness.Open();
        var row = await db.Campaigns.SingleAsync(c => c.Id == campaign, Ct);
        Assert.Equal(item, row.InboundSyncItemId);
        Assert.Equal(offset, row.InboundOffset);
        Assert.Equal(syncedAt, row.InboundSyncedAt);
        Assert.Equal(Noon.AddHours(-2), row.InboundWatermark);
        Assert.Equal(0, row.InboundFailures);
    }
}
