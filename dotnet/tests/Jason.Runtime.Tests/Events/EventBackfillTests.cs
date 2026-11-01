using System.Text.Json.Nodes;
using Jason.Contracts.Api;
using Jason.Contracts.Plugins;
using Jason.Runtime.Events;
using Jason.Runtime.Journal;
using Jason.Runtime.Persistence;
using Jason.Runtime.Tests.Dispatch;
using Microsoft.EntityFrameworkCore;
using static Jason.Runtime.Tests.Events.EventsBed;
using static Jason.Runtime.Tests.Events.FakeEventsInvoker;

namespace Jason.Runtime.Tests.Events;

/// <summary>
/// A span of an account read again on a person's request: from its start to its end, then on from its own cursor, each
/// page landing on the store's keys — and nothing of the account's own reading touched, neither its cursor nor what says
/// when it is read next or whether it is healthy. A span ends when it has been read to its end, or on an answer repeating
/// cannot change; anything that may pass leaves it waiting.
/// </summary>
public class EventBackfillTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Reads_its_span_and_leaves_the_account_s_reading_as_it_was()
    {
        using var harness = Create();
        harness.Events.Answer(Page("c-1", false, ReaderEvent("c_1", Noon.AddDays(-40))));
        await ScanAndReadAsync(harness);
        var before = await SourceOfAsync(harness, "a");
        var backfill = await BackfillAsync(harness, before);
        harness.Clock.Advance(TimeSpan.FromMinutes(1));
        harness.Events.Answer(
            Page("b-1", true, ReaderEvent("c_2", Noon.AddDays(-50))),
            Page("b-2", false, ReaderEvent("c_1", Noon.AddDays(-40)), ReaderEvent("c_3", Noon.AddDays(-35))));

        var report = await ScanAndReadAsync(harness);

        Assert.Equal(new EventReadReport(EventReadReport.Succeeded, 2, 2, 1, 0, null, null, false), report);
        Assert.Equal(
            ["""{"cursor":null,"since":"2026-08-19T12:00:00.000Z","until":"2026-09-18T12:00:00.000Z","limit":100}""",
             """{"cursor":"b-1","since":null,"until":null,"limit":100}"""],
            harness.Events.Requests.Skip(1).Select(request => request.Input.ToJsonString()));

        var after = await RowAsync(harness, before.Id);
        Assert.True(
            (after.Cursor, after.CursorAt, after.ReadStartedAt, after.ReadEndedAt, after.SucceededAt, after.Failures, after.LastCode, after.More)
                == (before.Cursor, before.CursorAt, before.ReadStartedAt, before.ReadEndedAt, before.SucceededAt, before.Failures, before.LastCode, before.More),
            $"The span moved the account's reading: cursor {after.Cursor} at {after.CursorAt:O}, started {after.ReadStartedAt:O}, succeeded {after.SucceededAt:O}, failures {after.Failures}.");

        var span = await BackfillRowAsync(harness, backfill.Id);
        Assert.Equal((EventBackfillStatus.Done, "b-2", 2, 1, 0), (span.Status, span.Cursor, span.Stored, span.Duplicates, span.Failures));
        Assert.Equal((Noon.AddMinutes(1), Noon.AddMinutes(1)), (span.AttemptedAt, span.EndedAt));
        Assert.Equal(3, await StoredAsync(harness));

        var line = (await ReadLinesAsync(harness))[^1];
        Assert.Equal(
            ("backfill", backfill.PublicId, "done", 2, 1),
            ((string?)line.New!["mode"], (string?)line.New["backfill"], (string?)line.New["backfill_status"], (int)line.New["stored"]!, (int)line.New["duplicates"]!));
        await using var db = harness.Open();
        Assert.False(await db.Journal.AnyAsync(entry => entry.Kind == JournalKinds.EventsGapObserved, Ct), "A span said a gap.");
    }

    /// <summary>A span with more to read than twenty pages waits on from its own cursor, and its next read takes up there.</summary>
    [Fact]
    public async Task Twenty_pages_and_it_waits_on_from_its_own_cursor()
    {
        using var harness = Create();
        await ScanAndReadAsync(harness);
        var backfill = await BackfillAsync(harness, await SourceOfAsync(harness, "a"));
        for (var page = 1; page <= 21; page++)
        {
            harness.Events.Answer(Page($"b-{page}", page < 21));
        }

        var first = await ScanAndReadAsync(harness);
        var waiting = await BackfillRowAsync(harness, backfill.Id);
        var second = await ScanAndReadAsync(harness);

        Assert.Equal((EventSchedule.PagesPerRead, true), (first!.Pages, first.More));
        Assert.Equal((EventBackfillStatus.Pending, "b-20", 0), (waiting.Status, waiting.Cursor, waiting.Failures));
        Assert.Equal((1, false), (second!.Pages, second.More));
        Assert.Equal("b-20", (string?)harness.Events.Requests[^1].Input["cursor"]);
        Assert.Equal(EventBackfillStatus.Done, (await BackfillRowAsync(harness, backfill.Id)).Status);
    }

    /// <summary>
    /// A failure that may pass leaves the span waiting, counted on the span alone, and it is read again from where it stood
    /// once its doubled wait has passed; the account's own reading is not told.
    /// </summary>
    [Theory]
    [InlineData("events_unreachable", FailureClass.Transient)]
    [InlineData("plugin_timeout", null)]
    public async Task A_failure_that_may_pass_leaves_it_waiting_and_counts_on_it_alone(string code, FailureClass? failureClass)
    {
        using var harness = Create();
        await ScanAndReadAsync(harness);
        var source = await SourceOfAsync(harness, "a");
        var backfill = await BackfillAsync(harness, source, cursor: "b-3");
        harness.Events.Answer(failureClass is { } declared ? Failure(code, declared) : Protocol(code));

        await ScanAndReadAsync(harness);

        var span = await BackfillRowAsync(harness, backfill.Id);
        Assert.Equal((EventBackfillStatus.Pending, "b-3", 1, code), (span.Status, span.Cursor, span.Failures, span.LastCode));
        Assert.Null(span.EndedAt);
        var account = await RowAsync(harness, source.Id);
        Assert.Equal((0, (string?)null), (account.Failures, account.LastCode));

        // A second before its doubled wait has passed, the account's own read — due since its cadence — goes, and the span waits.
        harness.Clock.Advance(TimeSpan.FromSeconds((2 * Cadence) - 1));
        await ScanAndReadAsync(harness);
        Assert.Null(harness.Events.Requests[^1].Input["until"]);
        Assert.Equal(0, await harness.EventScanAsync(Ct));
        harness.Clock.Advance(TimeSpan.FromSeconds(1));
        harness.Events.Answer(Page("b-4", false));
        await ScanAndReadAsync(harness);
        Assert.Equal("b-3", (string?)harness.Events.Requests[^1].Input["cursor"]);
        var done = await BackfillRowAsync(harness, backfill.Id);
        Assert.Equal((EventBackfillStatus.Done, 0, (string?)null), (done.Status, done.Failures, done.LastCode));
    }

    /// <summary>
    /// An answer repeating cannot change ends the span failed, with its code and class, and frees its place: a permanent or
    /// a validation failure, and an answer outside the contract.
    /// </summary>
    [Theory]
    [InlineData("permanent")]
    [InlineData("validation")]
    [InlineData("outside")]
    public async Task An_answer_repeating_cannot_change_ends_it_failed(string how)
    {
        using var harness = Create();
        await ScanAndReadAsync(harness);

        // A validation failure is the runtime's own half of the contract refusing the input before a child exists: here a
        // cursor with spaces in it. An answer outside the contract is a declared code with a class it does not declare.
        var backfill = await BackfillAsync(harness, await SourceOfAsync(harness, "a"), cursor: how == "validation" ? "b a d" : null);
        if (how != "validation")
        {
            harness.Events.Answer(Failure("events_unauthorized", how == "permanent" ? FailureClass.Permanent : FailureClass.Transient));
        }

        await ScanAndReadAsync(harness);

        var span = await BackfillRowAsync(harness, backfill.Id);
        var expected = how switch
        {
            "permanent" => ("events_unauthorized", "permanent"),
            "validation" => (EventReader.InputInvalid, "validation"),
            _ => (EventReader.ResultInvalid, "ambiguous"),
        };
        Assert.Equal((EventBackfillStatus.Failed, expected.Item1, expected.Item2), (span.Status, span.LastCode, span.LastClass));
        Assert.Equal(Noon, span.EndedAt);
        Assert.Equal(0, await harness.EventScanAsync(Ct));
    }

    /// <summary>
    /// A cursor of the span's own that the plugin no longer knows is cleared, and the span is read again from its start by
    /// date at once: what it finds again lands on the store's keys. Answered to a read by date, the same code ends it.
    /// </summary>
    [Fact]
    public async Task A_forgotten_cursor_reads_its_span_again_from_its_start()
    {
        using var harness = Create();
        await ScanAndReadAsync(harness);
        var backfill = await BackfillAsync(harness, await SourceOfAsync(harness, "a"), cursor: "b-9");
        harness.Events.Answer(Failure(EventReader.CursorUnknown, FailureClass.Permanent), Failure(EventReader.CursorUnknown, FailureClass.Permanent));

        await ScanAndReadAsync(harness);
        var cleared = await BackfillRowAsync(harness, backfill.Id);
        await ScanAndReadAsync(harness);
        var ended = await BackfillRowAsync(harness, backfill.Id);

        Assert.Equal((EventBackfillStatus.Pending, (string?)null, 0), (cleared.Status, cleared.Cursor, cleared.Failures));
        Assert.Equal("2026-08-19T12:00:00.000Z", (string?)harness.Events.Requests[^1].Input["since"]);
        Assert.Equal((EventBackfillStatus.Failed, EventReader.CursorUnknown, "permanent"), (ended.Status, ended.LastCode, ended.LastClass));
    }

    /// <summary>
    /// A plugin that forgets every cursor of a span's own would send it back to its start at every idle scan, two children
    /// each time, for ever. Starting again is free only while it finds something new: a second forgotten cursor with
    /// nothing new stored since the last start again counts as a failure, and the span waits its doubling — still
    /// waiting, its place kept, since a cause that may pass is no reason to end it.
    /// </summary>
    [Fact]
    public async Task A_plugin_that_forgets_every_cursor_of_a_span_is_read_on_a_doubling_wait_and_not_without_bound()
    {
        using var harness = Create();
        harness.Events.Otherwise = (request, _) =>
            (string?)request.Input["cursor"] is { } cursor && cursor.StartsWith("b-", StringComparison.Ordinal) ? Failure(EventReader.CursorUnknown, FailureClass.Permanent)
            : request.Input["until"] is not null ? Page("b-1", true, ReaderEvent("c_1", Noon.AddDays(-40)))
            : Page((string?)request.Input["cursor"] ?? "c-0", false);
        await ScanAndReadAsync(harness);
        var backfill = await BackfillAsync(harness, await SourceOfAsync(harness, "a"));

        for (var scan = 0; scan < 10; scan++)
        {
            await ScanAndReadAsync(harness);
        }

        // The first start again found an event it had not stored, and was free; the second found nothing new, and waits.
        Assert.True(harness.Events.Requests.Count == 5, $"{harness.Events.Requests.Count} pages were asked for in ten scans, not 5.");
        var waiting = await BackfillRowAsync(harness, backfill.Id);
        Assert.Equal((EventBackfillStatus.Pending, 1, EventReader.CursorUnknown, (string?)null), (waiting.Status, waiting.Failures, waiting.LastCode, waiting.Cursor));

        // Its doubled wait over, the account's own read goes first; then the span, whose next forgetting doubles the wait again.
        harness.Clock.Advance(TimeSpan.FromSeconds(2 * Cadence));
        for (var scan = 0; scan < 10; scan++)
        {
            await ScanAndReadAsync(harness);
        }

        Assert.True(harness.Events.Requests.Count == 8, $"{harness.Events.Requests.Count} pages were asked for, not 8.");
        Assert.Equal((EventBackfillStatus.Pending, 2), ((await BackfillRowAsync(harness, backfill.Id)).Status, (await BackfillRowAsync(harness, backfill.Id)).Failures));
    }

    /// <summary>
    /// The same bound where the plugin forgets only the cursor it returned with its twentieth page: twenty pages again of
    /// what the span had stored before it was sent back are not something new, so its next forgetting counts.
    /// </summary>
    [Fact]
    public async Task A_plugin_that_forgets_a_span_s_cursor_after_twenty_pages_is_read_on_a_doubling_wait_too()
    {
        using var harness = Create();
        harness.Events.Otherwise = (request, _) =>
            (string?)request.Input["cursor"] is { } cursor && cursor.StartsWith("b-", StringComparison.Ordinal)
                ? cursor == "b-20" ? Failure(EventReader.CursorUnknown, FailureClass.Permanent) : Page($"b-{int.Parse(cursor[2..], System.Globalization.CultureInfo.InvariantCulture) + 1}", true)
            : request.Input["until"] is not null ? Page("b-1", true, ReaderEvent("c_1", Noon.AddDays(-40)))
            : Page((string?)request.Input["cursor"] ?? "c-0", false);
        await ScanAndReadAsync(harness);
        var backfill = await BackfillAsync(harness, await SourceOfAsync(harness, "a"));

        for (var scan = 0; scan < 10; scan++)
        {
            await ScanAndReadAsync(harness);
        }

        // Twenty pages that found one event, a free start again, twenty pages that found nothing new, then a counted one.
        Assert.True(harness.Events.Requests.Count == 1 + 20 + 1 + 20 + 1, $"{harness.Events.Requests.Count} pages were asked for in ten scans, not 43.");
        var waiting = await BackfillRowAsync(harness, backfill.Id);
        Assert.Equal((EventBackfillStatus.Pending, 1, EventReader.CursorUnknown), (waiting.Status, waiting.Failures, waiting.LastCode));
    }

    /// <summary>
    /// What a span finds is said as the account's own reading says it: once to a campaign live when it happened, never
    /// again when that reading said it already, and not at all to a campaign that was not live yet.
    /// </summary>
    [Fact]
    public async Task What_it_finds_tells_a_live_campaign_once()
    {
        using var harness = Create();
        await CampaignAsync(harness, eventsFrom: Noon.AddDays(-45));
        harness.Events.Answer(Page("c-1", false, ReaderEvent("c_1", Noon.AddDays(-20))));
        await ScanAndReadAsync(harness);
        await BackfillAsync(harness, await SourceOfAsync(harness, "a"), from: Noon.AddDays(-50), to: Noon.AddDays(-10));
        harness.Events.Answer(Page(
            "b-1",
            false,
            ReaderEvent("c_1", Noon.AddDays(-20)),
            ReaderEvent("c_2", Noon.AddDays(-40)),
            ReaderEvent("c_3", Noon.AddDays(-48))));

        var report = await ScanAndReadAsync(harness);

        Assert.Equal((2, 1, 1), (report!.Stored, report.Duplicates, report.Said));
        await using var db = harness.Open();
        var said = await db.Journal.AsNoTracking()
            .Where(entry => entry.Kind == JournalKinds.ProviderEventObserved)
            .OrderBy(entry => entry.Id)
            .Select(entry => entry.New!)
            .ToListAsync(Ct);
        Assert.Equal(
            ["2026-09-28T12:00:00.000Z", "2026-09-08T12:00:00.000Z"],
            said.Select(line => (string?)line["occurred_at"]));
    }

    /// <summary>A span read first in this process is not the account's own reading having happened: a start still reads the account at once.</summary>
    [Fact]
    public async Task A_backfill_is_not_this_process_reading_the_account()
    {
        using var harness = Create();
        var source = await SourceAsync(harness, cursor: "c-41", cursorAt: Noon.AddHours(-1), started: Noon.AddMinutes(-1), succeeded: Noon.AddMinutes(-1));
        var backfill = await BackfillAsync(harness, source);
        var read = new EventRead(
            source.Id,
            source.PublicId,
            source.PluginId,
            source.BindingIdentity,
            new JsonObject { ["workspace"] = "a" },
            null,
            EventSchedule.Plan(await BackfillRowAsync(harness, backfill.Id)),
            new Jason.Runtime.Plugins.Invocation.PinnedPlugin(harness.Resolve<Jason.Runtime.Plugins.Registry.PluginRegistry>().Snapshot.Find(Reader)!, "snp_1"),
            "rts_1",
            new EventBackfillRef(backfill.Id, backfill.PublicId));

        await harness.Reader.ReadAsync(read, Ct);

        Assert.False(harness.Reader.HasRead(source.Id), "A span counted as this process reading the account.");
    }

    private static JsonObject ReaderEvent(string contact, DateTime at) => EventReaderTests.Event(contact, at);

    /// <summary>A live campaign holding the provider campaign the events name, pinned at the reader.</summary>
    private static async Task CampaignAsync(DispatchHarness harness, DateTime eventsFrom)
    {
        await using var db = harness.Open();
        var campaign = WorkItemFactory.NewCampaign(now: Noon.AddDays(-60));
        campaign.EventsFrom = eventsFrom;
        db.Campaigns.Add(campaign);
        db.ExternalIds.Add(new ExternalId
        {
            Campaign = campaign,
            PluginId = Reader,
            Kind = "campaign",
            Value = "p_1",
            RecordedAt = Noon.AddDays(-60),
            RecordedByAttemptId = "att_01JB6K8TQ2W9V4MZ0C3Y7H5NRD",
        });
        await db.SaveChangesAsync(Ct);
    }
}
