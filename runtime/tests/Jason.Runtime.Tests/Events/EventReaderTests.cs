using System.Text.Json.Nodes;
using Jason.Runtime.Events;
using Jason.Runtime.Journal;
using Jason.Runtime.Persistence;
using Jason.Runtime.Tests.Dispatch;
using Microsoft.EntityFrameworkCore;
using static Jason.Runtime.Tests.Events.EventsBed;
using static Jason.Runtime.Tests.Events.FakeEventsInvoker;

namespace Jason.Runtime.Tests.Events;

/// <summary>
/// One read of an account, page by page: each page stored in its own transaction, the next asked for from the cursor the
/// last returned, at most twenty to a read, and the source's row and the journal told how it went.
/// </summary>
public class EventReaderTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_pages_of_a_read_chain_their_cursors_and_each_lands_on_its_own()
    {
        using var harness = Create();
        var source = await SourceAsync(harness, cursor: "c-1", cursorAt: Noon.AddHours(-1));
        harness.Events.Answer(
            Page("c-2", true, Event("c_2001")),
            Page("c-3", true, Event("c_2002")),
            Page("c-4", false, Event("c_2003")));

        var report = await ScanAndReadAsync(harness);

        Assert.Equal(new EventReadReport(EventReadReport.Succeeded, 3, 3, 0, 0, null, null, false), report);
        Assert.Equal(["c-1", "c-2", "c-3"], harness.Events.Requests.Select(request => (string?)request.Input["cursor"]));
        Assert.All(harness.Events.Requests, request => Assert.Equal(harness.Events.Requests[0].CorrelationId, request.CorrelationId));
        var row = await RowAsync(harness, source.Id);
        Assert.Equal(("c-4", Noon, Noon, 0, false), (row.Cursor, row.CursorAt, row.SucceededAt, row.Failures, row.More));
        Assert.Equal(3, await StoredAsync(harness));
    }

    [Fact]
    public async Task The_cursor_moves_only_with_the_page_that_lands()
    {
        using var harness = Create();
        var source = await SourceAsync(harness, cursor: "c-1", cursorAt: Noon.AddHours(-1));
        harness.Events.Answer(Page("c-2", true, Event("c_2001")), Page("c-3", false, Event("c_2002")));
        harness.InterfereOnceBeforeSaving(
            context => context.ChangeTracker.Entries<ProviderEvent>().Any(entry => (string?)entry.Entity.Data["contact"] == "c_2002"),
            () => throw new InvalidOperationException("the database went away"));

        var report = await ScanAndReadAsync(harness);

        Assert.Equal(new EventReadReport(EventReadReport.Failed, 2, 1, 0, 0, EventReader.NotStored, Contracts.Plugins.FailureClass.Ambiguous, false), report);
        var row = await RowAsync(harness, source.Id);
        Assert.Equal(("c-2", 1), (row.Cursor, row.Failures));
        Assert.Equal(1, await StoredAsync(harness));
    }

    [Fact]
    public async Task A_read_stops_at_twenty_pages_and_the_next_is_due_at_once()
    {
        using var harness = Create();
        var source = await SourceAsync(harness, cursor: "c-0", cursorAt: Noon.AddHours(-1));
        for (var page = 1; page <= 25; page++)
        {
            harness.Events.Answer(Page($"c-{page}", true));
        }

        var report = await ScanAndReadAsync(harness);

        Assert.Equal((EventReadReport.Succeeded, EventSchedule.PagesPerRead, true), (report!.Outcome, report.Pages, report.More));
        var row = await RowAsync(harness, source.Id);
        Assert.Equal(("c-20", true), (row.Cursor, row.More));
        Assert.Equal(1, await harness.EventScanAsync(Ct));
        Assert.Equal("c-20", (string?)(await harness.Reader.ReadNextAsync(Ct) is not null ? harness.Events.Requests[20].Input["cursor"] : null));
    }

    [Fact]
    public async Task A_page_that_says_more_and_hands_back_its_own_cursor_ends_the_read()
    {
        using var harness = Create();
        var source = await SourceAsync(harness, cursor: "c-1", cursorAt: Noon.AddHours(-1));
        harness.Events.Answer(Page("c-1", true));

        var report = await ScanAndReadAsync(harness);

        Assert.Equal((EventReadReport.Succeeded, 1, false), (report!.Outcome, report.Pages, report.More));
        Assert.False((await RowAsync(harness, source.Id)).More, "A page that moved nothing left the source with more to read.");
    }

    [Fact]
    public async Task A_read_that_skips_a_gap_says_it_once_with_its_first_page()
    {
        using var harness = Create();
        var stored = Noon.AddDays(-45);
        var source = await SourceAsync(harness, cursor: "c-1", cursorAt: stored);
        harness.Events.Answer(Page("c-2", true, Event("c_2001")), Page("c-3", false));

        await ScanAndReadAsync(harness);

        Assert.Equal("2026-09-18T12:00:00Z", (string?)harness.Events.Requests[0].Input["since"]);
        Assert.Null(harness.Events.Requests[0].Input["cursor"]);
        await using var db = harness.Open();
        var gap = await db.Journal.SingleAsync(entry => entry.Kind == JournalKinds.EventsGapObserved, Ct);
        Assert.Equal($"gap/{source.PublicId}/2026-09-03T12:00:00.000Z", gap.Key);
    }

    [Fact]
    public async Task A_read_from_a_cursor_that_found_nothing_writes_no_line_and_one_that_did_writes_one()
    {
        using var harness = Create();
        await SourceAsync(harness, cursor: "c-SECRETMARK", cursorAt: Noon.AddHours(-1));

        await ScanAndReadAsync(harness);
        Assert.Empty(await ReadLinesAsync(harness));

        harness.Clock.Advance(TimeSpan.FromSeconds(Cadence));
        harness.Events.Answer(Page("c-SECRETMARK2", false, Event("c_2001")));
        await ScanAndReadAsync(harness);

        var line = Assert.Single(await ReadLinesAsync(harness));
        Assert.Null(line.CampaignId);
        var said = line.New!.AsObject();
        Assert.Equal(("cursor", 1, 1, 0, "succeeded"), ((string?)said["mode"], (int)said["pages"]!, (int)said["stored"]!, (int)said["duplicates"]!, (string?)said["outcome"]));
        Assert.Equal(("sha256:a", "reader", "1.0.0", "sha256:0"), ((string?)said["binding_identity"], (string?)said["plugin"], (string?)said["plugin_version"], (string?)said["plugin_digest"]));
        Assert.StartsWith("evr_", (string?)said["read_id"], StringComparison.Ordinal);
        Assert.DoesNotContain("SECRETMARK", line.New.ToJsonString(), StringComparison.Ordinal);
        Assert.DoesNotContain("workspace", line.New.ToJsonString(), StringComparison.Ordinal);
    }

    /// <summary>
    /// A read takes time on a clock that moves: what the source keeps as its last success is when that read started, and
    /// the health window the inbound read asks about is measured from there, not from when the read ended.
    /// </summary>
    [Fact]
    public async Task The_health_window_runs_from_when_the_read_that_succeeded_started()
    {
        using var harness = Create();
        var source = await SourceAsync(harness, cursor: "c-1", cursorAt: Noon.AddHours(-1));
        harness.Events.Answer((_, _) =>
        {
            harness.Clock.Advance(TimeSpan.FromMinutes(5));
            return Page("c-2", false, Event("c_2001"));
        });

        await ScanAndReadAsync(harness);

        var row = await RowAsync(harness, source.Id);
        Assert.Equal((Noon, Noon.AddMinutes(5), Noon), (row.ReadStartedAt, row.ReadEndedAt, row.SucceededAt));
        Assert.True(EventSchedule.IsHealthy(row, Cadence, 14_400, Noon.AddSeconds(2 * Cadence)), "Not healthy two cadences after the read started.");
        Assert.False(EventSchedule.IsHealthy(row, Cadence, 14_400, Noon.AddSeconds(2 * Cadence + 1)), "Still healthy past two cadences from the read's start, measured from its end.");
    }

    [Fact]
    public async Task A_first_start_is_journaled_even_when_it_finds_nothing()
    {
        using var harness = Create();

        await ScanAndReadAsync(harness);

        var line = Assert.Single(await ReadLinesAsync(harness));
        Assert.Equal(("first_start", 0), ((string?)line.New!["mode"], (int)line.New["stored"]!));
    }

    internal static JsonObject Event(string contact, DateTime? at = null) => new()
    {
        ["kind"] = "bounce",
        ["id"] = null,
        ["occurred_at"] = EventStoreFixture.Stamp(at ?? Noon.AddHours(-3)),
        ["contact"] = contact,
        ["campaign"] = "p_1",
        ["message"] = "m_88",
        ["bounce"] = "hard",
    };
}
