using Jason.Contracts.Api;
using Jason.Runtime.Events;
using Jason.Runtime.Journal;
using Jason.Runtime.Persistence;
using Microsoft.EntityFrameworkCore;
using static Jason.Runtime.Tests.Events.EventStoreFixture;

namespace Jason.Runtime.Tests.Events;

/// <summary>
/// A span of an account's events nobody will read is said once, in the journal, by the page that skips it: in that
/// page's transaction, so a page that does not land says nothing, and a read tried again says nothing twice.
/// </summary>
public class EventGapTests
{
    private static readonly EventGap Skipped = new(Noon.AddDays(-45), Noon.AddDays(-30));

    [Fact]
    [Critical(Family.N2, "events_gap_observed once")]
    public async Task A_gap_is_said_with_the_first_page_that_lands_and_once()
    {
        using var it = new EventStoreFixture();
        var source = await it.SourceAsync(cursor: "c-1");

        var first = await it.StoreAsync(source, "c-1", Page("c-2", Bounced()), Skipped);
        await it.StoreAsync(source, "c-2", Page("c-3"), Skipped);
        await it.StoreAsync(source, "c-3", Page("c-4", Accepted()), Skipped);
        await it.StoreAsync(source, "c-4", Page("c-5"), Skipped);

        Assert.False(first.Moved, "The first page did not land.");
        var gap = Assert.Single(await GapsAsync(it));
        Assert.Null(gap.CampaignId);
        Assert.Equal(ActorType.System, gap.ActorType);
        Assert.Equal("dispatcher", gap.ActorId);
        Assert.Equal($"gap/{source.PublicId}/2026-09-03T12:00:00.000Z", gap.Key);
        Assert.Equal(
            $$"""{"source":"{{source.PublicId}}","plugin":"reader","from":"2026-09-03T12:00:00.000Z","to":"2026-09-18T12:00:00.000Z"}""",
            gap.New!.ToJsonString());
    }

    [Fact]
    public async Task A_page_that_does_not_land_says_no_gap()
    {
        using var it = new EventStoreFixture();
        var source = await it.SourceAsync(cursor: "c-9");

        var refused = await it.StoreAsync(source, "c-1", Page("c-2", Bounced()), Skipped);

        Assert.True(refused.Moved, "A page read from a cursor the source no longer holds landed.");
        Assert.Empty(await GapsAsync(it));

        await it.StoreAsync(source, "c-9", Page("c-10", Bounced()), Skipped);
        Assert.Single(await GapsAsync(it));
    }

    [Fact]
    public async Task An_empty_page_that_lands_says_the_gap_it_skips()
    {
        using var it = new EventStoreFixture();
        var source = await it.SourceAsync(cursor: "c-1");

        await it.StoreAsync(source, "c-1", Page("c-2"), Skipped);

        Assert.Single(await GapsAsync(it));
    }

    private static async Task<List<JournalEntry>> GapsAsync(EventStoreFixture it)
    {
        await using var db = it.Open();
        return await db.Journal.AsNoTracking().Where(entry => entry.Kind == JournalKinds.EventsGapObserved).ToListAsync();
    }
}
