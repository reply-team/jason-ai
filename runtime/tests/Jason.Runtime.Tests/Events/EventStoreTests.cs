using Jason.Contracts.Api;
using Jason.Runtime.Events;
using Jason.Runtime.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using static Jason.Runtime.Tests.Events.EventStoreFixture;

namespace Jason.Runtime.Tests.Events;

/// <summary>
/// What storing a page keeps: every event once, whatever it names, and the cursor the page returned — moved with the page
/// it ends and with nothing else, and only from where the read found it.
/// </summary>
public class EventStoreTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>
    /// A page read again — after a crash, or because a range a person asked for overlaps what was read already — adds
    /// nothing and changes nothing: the store reads the keys it holds, and the rows it holds stay as they were stored.
    /// </summary>
    [Fact]
    [Critical(Family.N2, "each event once by key")]
    public async Task A_page_stored_twice_inserts_once_and_changes_no_row()
    {
        using var store = new EventStoreFixture();
        var source = await store.SourceAsync(cursor: "c-1");
        var page = Page("c-2", Moved(), Bounced());

        var first = await store.StoreAsync(source, "c-1", page);
        store.Clock.Advance(TimeSpan.FromMinutes(10));
        var again = await store.StoreAsync(source, "c-2", Page("c-3", Moved(), Bounced()));

        Assert.Equal(new Jason.Runtime.Events.EventStoreResult(2, 0, 0, false), first);
        Assert.Equal(new Jason.Runtime.Events.EventStoreResult(0, 2, 0, false), again);
        await using var db = store.Open();
        var rows = await db.Events.AsNoTracking().OrderBy(e => e.Id).ToListAsync(Ct);
        Assert.True(rows.Count == 2, $"The store holds {rows.Count} events after one page of two was stored twice.");
        Assert.All(rows, row => Assert.Equal(Noon, row.StoredAt));
        Assert.Equal(["thread_moved", "bounce"], rows.Select(row => row.Kind));
        Assert.Equal("c-3", (await db.EventSources.SingleAsync(Ct)).Cursor);
    }

    [Fact]
    public async Task An_event_a_page_lists_twice_is_stored_once()
    {
        using var store = new EventStoreFixture();
        var source = await store.SourceAsync();

        var result = await store.StoreAsync(source, null, Page("c-1", Moved(), Moved()));

        Assert.Equal(new Jason.Runtime.Events.EventStoreResult(1, 1, 0, false), result);
    }

    /// <summary>
    /// Two bindings that name one account are two sources reading the same events, and the store keeps each once: the key
    /// is the plugin's, not the account's. Another plugin's event of the same shape is another provider's, and is kept.
    /// </summary>
    [Fact]
    [Critical(Family.N2, "the dedup key")]
    public async Task The_key_is_per_plugin_and_not_per_source()
    {
        using var store = new EventStoreFixture();
        var one = await store.SourceAsync(binding: "sha256:aaaa");
        var other = await store.SourceAsync(binding: "sha256:bbbb");
        var elsewhere = await store.SourceAsync(plugin: "other-provider");

        await store.StoreAsync(one, null, Page("c-1", Bounced()));
        var twice = await store.StoreAsync(other, null, Page("c-1", Bounced()));
        var foreign = await store.StoreAsync(elsewhere, null, Page("c-1", Bounced()));

        Assert.Equal(0, twice.Stored);
        Assert.Equal(1, foreign.Stored);
        await using var db = store.Open();
        Assert.Equal(["other-provider", Reader], (await db.Events.AsNoTracking().Select(e => e.PluginId).ToListAsync(Ct)).Order(StringComparer.Ordinal));
    }

    /// <summary>An event whose identifiers no campaign's pins match is kept all the same, concerning nobody and telling nobody.</summary>
    [Fact]
    [Critical(Family.N2, "unmatched events are kept")]
    public async Task An_event_no_pin_matches_is_kept_unattributed()
    {
        using var store = new EventStoreFixture();
        var source = await store.SourceAsync();
        await store.CampaignAsync(pins: (Reader, "p_1"));

        var result = await store.StoreAsync(source, null, Page("c-1", Bounced(campaign: "p_9"), Bounced(campaign: null, contact: "c_2")));

        Assert.Equal(2, result.Stored);
        Assert.Equal(0, result.Said);
        await using var db = store.Open();
        Assert.Equal(2, await db.Events.CountAsync(Ct));
        Assert.Equal(0, await db.EventAttributions.CountAsync(Ct));
        Assert.Empty(await store.AllLinesAsync());
    }

    /// <summary>
    /// The cursor moves only with the page it ends: a page that does not land — its write refused halfway — leaves the
    /// cursor where the read found it and stores nothing of the page, so the next read asks for the same events again.
    /// </summary>
    [Fact]
    public async Task A_page_that_does_not_land_leaves_the_cursor_and_stores_nothing()
    {
        using var store = new EventStoreFixture();
        var source = await store.SourceAsync(cursor: "c-1");
        await store.CampaignAsync(pins: (Reader, "p_1"));

        await using (var db = store.Open(new RefuseAttributions()))
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => store.Store.StoreAsync(db, source, "c-1", Page("c-2", Moved(), Bounced()), "evr_x", Ct));
        }

        await using var check = store.Open();
        Assert.Equal("c-1", (await check.EventSources.SingleAsync(Ct)).Cursor);
        Assert.Null((await check.EventSources.SingleAsync(Ct)).CursorAt);
        Assert.Equal(0, await check.Events.CountAsync(Ct));
        Assert.Empty(await store.AllLinesAsync());
    }

    /// <summary>
    /// The cursor the read was handed out with is the fencing token: a page whose source holds another cursor by the
    /// time it is stored — another writer moved it — is not stored, and the cursor that writer left stands.
    /// </summary>
    [Fact]
    [Critical(Family.N2, "guarded cursor")]
    public async Task A_page_whose_source_another_writer_moved_is_not_stored()
    {
        using var store = new EventStoreFixture();
        var source = await store.SourceAsync(cursor: "c-5");

        var result = await store.StoreAsync(source, "c-4", Page("c-6", Bounced()));

        Assert.True(result.Moved);
        await using var db = store.Open();
        Assert.Equal("c-5", (await db.EventSources.SingleAsync(Ct)).Cursor);
        Assert.Equal(0, await db.Events.CountAsync(Ct));
    }

    /// <summary>
    /// The fence holds for a first read too: a read handed out while the source had no cursor, whose source another writer
    /// has given one meanwhile, does not land, and the cursor that writer left stands. No cursor is a value the guard
    /// compares like any other, not a reason to write over whatever is there.
    /// </summary>
    [Fact]
    public async Task A_page_read_with_no_cursor_does_not_land_on_a_source_given_one_meanwhile()
    {
        using var store = new EventStoreFixture();
        var source = await store.SourceAsync(cursor: "c-9");

        var result = await store.StoreAsync(source, null, Page("c-1", Bounced()));

        Assert.True(result.Moved, $"A page read with no cursor landed on a source holding one: {result}.");
        await using var db = store.Open();
        Assert.Equal("c-9", (await db.EventSources.SingleAsync(Ct)).Cursor);
        Assert.Equal(0, await db.Events.CountAsync(Ct));
    }

    /// <summary>A first page, read by date because the source has no cursor yet, gives it its first, and says when.</summary>
    [Fact]
    public async Task A_first_page_gives_a_source_its_first_cursor()
    {
        using var store = new EventStoreFixture();
        var source = await store.SourceAsync();

        var result = await store.StoreAsync(source, null, Page("c-1"));

        Assert.False(result.Moved);
        await using var db = store.Open();
        var stored = await db.EventSources.SingleAsync(Ct);
        Assert.Equal("c-1", stored.Cursor);
        Assert.Equal(Noon, stored.CursorAt);
    }

    /// <summary>
    /// A page of a span asked for again moves the span's own cursor and counts on the span, and leaves the account's
    /// cursor and when it was stored exactly as they were: the account's own reading starts where it stood. What the
    /// account's reading already stored is found held, and stored once.
    /// </summary>
    [Fact]
    [Critical(Family.N2, "backfill never moves the account's cursor")]
    public async Task A_backfill_page_moves_its_own_cursor_and_never_the_source_s()
    {
        using var store = new EventStoreFixture();
        var source = await store.SourceAsync(cursor: "c-41");
        await store.StoreAsync(source, "c-41", Page("c-42", Bounced(contact: "c_1")));
        var backfill = await store.BackfillAsync(source);
        store.Clock.Advance(TimeSpan.FromMinutes(10));

        var first = await store.StoreAsync(source, EventFence.OfBackfill(backfill.Id, null), Page("b-1", Bounced(contact: "c_2", at: Noon.AddDays(-40)), Bounced(contact: "c_3", at: Noon.AddDays(-40))));
        var second = await store.StoreAsync(source, EventFence.OfBackfill(backfill.Id, "b-1"), Page("b-2", Bounced(contact: "c_1"), Bounced(contact: "c_4", at: Noon.AddDays(-35))));

        Assert.Equal(new EventStoreResult(2, 0, 0, false), first);
        Assert.Equal(new EventStoreResult(1, 1, 0, false), second);
        await using var db = store.Open();
        var account = await db.EventSources.AsNoTracking().SingleAsync(Ct);
        Assert.True(
            (account.Cursor, account.CursorAt) == ("c-42", Noon),
            $"The account's cursor is {account.Cursor} stored at {account.CursorAt:O}; a span's pages moved it.");
        var span = await db.EventBackfills.AsNoTracking().SingleAsync(Ct);
        Assert.Equal(("b-2", 3, 1), (span.Cursor, span.Stored, span.Duplicates));
        Assert.Equal(4, await db.Events.CountAsync(Ct));
    }

    /// <summary>
    /// A span's page lands only from where the span's reading stood, and only while the span waits: one that finds its
    /// cursor moved, or the span ended, stores nothing and moves nothing.
    /// </summary>
    [Theory]
    [InlineData("b-0", EventBackfillStatus.Pending)]
    [InlineData("b-1", EventBackfillStatus.Done)]
    [InlineData("b-1", EventBackfillStatus.Failed)]
    public async Task A_backfill_page_whose_span_moved_on_stores_nothing(string held, EventBackfillStatus status)
    {
        using var store = new EventStoreFixture();
        var source = await store.SourceAsync(cursor: "c-41");
        var backfill = await store.BackfillAsync(source, cursor: "b-1", status: status);

        var result = await store.StoreAsync(source, EventFence.OfBackfill(backfill.Id, held), Page("b-2", Bounced()));

        Assert.Equal(new EventStoreResult(0, 0, 0, true), result);
        await using var db = store.Open();
        Assert.Equal(0, await db.Events.CountAsync(Ct));
        var span = await db.EventBackfills.AsNoTracking().SingleAsync(Ct);
        Assert.Equal(("b-1", 0), (span.Cursor, span.Stored));
        Assert.Equal("c-41", (await db.EventSources.AsNoTracking().SingleAsync(Ct)).Cursor);
    }

    /// <summary>Refuses the write that adds the page's attributions: the second half of a page, after its events.</summary>
    private sealed class RefuseAttributions : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(eventData);
            if (eventData.Context!.ChangeTracker.Entries<EventAttribution>().Any())
            {
                throw new InvalidOperationException("refused for the test");
            }

            return ValueTask.FromResult(result);
        }
    }
}
