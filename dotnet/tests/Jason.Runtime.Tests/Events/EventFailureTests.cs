using System.Text.Json.Nodes;
using Jason.Contracts.Events;
using Jason.Contracts.Operations;
using Jason.Contracts.Plugins;
using Jason.Runtime.Events;
using Jason.Runtime.Plugins.Invocation;
using Jason.Runtime.Tests.Dispatch;
using Microsoft.Extensions.Logging.Abstractions;
using static Jason.Runtime.Tests.Events.EventReaderTests;
using static Jason.Runtime.Tests.Events.EventsBed;
using static Jason.Runtime.Tests.Events.FakeEventsInvoker;

namespace Jason.Runtime.Tests.Events;

/// <summary>
/// How a read that did not end in a page is answered: a table, held row by row on a clock that does not move. A read
/// changes nothing at a provider, so whatever may pass by itself is read again at the doubled cadence; what will not pass
/// waits a day; a cursor the plugin forgot is read again by date at once; and an answer outside the contract stores nothing.
/// </summary>
public class EventFailureTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Transient_and_ambiguous_ends_double_the_wait()
    {
        using var harness = Create();
        var source = await SourceAsync(harness, cursor: "c-1", cursorAt: Noon.AddHours(-1));
        harness.Events.Answer(Failure("events_unreachable", FailureClass.Transient));

        var report = await ScanAndReadAsync(harness);

        Assert.Equal(("events_unreachable", FailureClass.Transient), (report!.Code, report.Class));
        var row = await RowAsync(harness, source.Id);
        Assert.Equal((1, "events_unreachable", "transient", "c-1"), (row.Failures, row.LastCode, row.LastClass, row.Cursor));
        Assert.Equal(Noon.AddSeconds(2 * Cadence), EventSchedule.DueAt(row, readByThisProcess: true, Cadence));

        harness.Clock.Advance(TimeSpan.FromSeconds(2 * Cadence - 1));
        Assert.Equal(0, await harness.EventScanAsync(Ct));
        harness.Clock.Advance(TimeSpan.FromSeconds(1));
        harness.Events.Answer(Protocol("plugin_timeout"));
        Assert.Equal(1, await harness.EventScanAsync(Ct));
        await harness.Reader.ReadNextAsync(Ct);

        row = await RowAsync(harness, source.Id);
        Assert.Equal((2, "plugin_timeout", "ambiguous"), (row.Failures, row.LastCode, row.LastClass));
        Assert.Equal(row.ReadStartedAt!.Value.AddSeconds(4 * Cadence), EventSchedule.DueAt(row, readByThisProcess: true, Cadence));
    }

    [Theory]
    [InlineData("events_unauthorized")]
    [InlineData("events_unsupported")]
    public async Task A_permanent_failure_waits_a_day(string code)
    {
        using var harness = Create();
        var source = await SourceAsync(harness, cursor: "c-1", cursorAt: Noon.AddHours(-1));
        harness.Events.Answer(Failure(code, FailureClass.Permanent));

        await ScanAndReadAsync(harness);

        harness.Clock.Advance(TimeSpan.FromSeconds(86_399));
        Assert.Equal(0, await harness.EventScanAsync(Ct));
        harness.Clock.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(1, await harness.EventScanAsync(Ct));
        Assert.Equal("permanent", (await RowAsync(harness, source.Id)).LastClass);
    }

    [Fact]
    public async Task A_cursor_the_plugin_forgot_is_cleared_and_read_again_by_date_at_once()
    {
        using var harness = Create();
        var stored = Noon.AddDays(-2);
        var source = await SourceAsync(harness, cursor: "c-41", cursorAt: stored);
        harness.Events.Answer(Failure("events_cursor_unknown", FailureClass.Permanent));

        await ScanAndReadAsync(harness);

        var row = await RowAsync(harness, source.Id);
        Assert.Equal((null, stored, true, 1), (row.Cursor, row.CursorAt, row.More, row.Failures));
        Assert.Equal(1, await harness.EventScanAsync(Ct));
        await harness.Reader.ReadNextAsync(Ct);
        Assert.Equal("""{"cursor":null,"since":"2026-10-15T12:00:00Z","until":null,"limit":100}""", harness.Events.Requests[1].Input.ToJsonString());
    }

    /// <summary>A read by date has no cursor to forget: a plugin that answers one forgotten anyway is a permanent failure, and waits a day.</summary>
    [Fact]
    public async Task A_read_by_date_answered_with_a_forgotten_cursor_waits_a_day()
    {
        using var harness = Create();
        var source = await SourceAsync(harness);
        harness.Events.Answer(Failure("events_cursor_unknown", FailureClass.Permanent));

        await ScanAndReadAsync(harness);

        var row = await RowAsync(harness, source.Id);
        Assert.Equal((false, 1, "permanent"), (row.More, row.Failures, row.LastClass));
        harness.Clock.Advance(TimeSpan.FromSeconds(86_399));
        Assert.Equal(0, await harness.EventScanAsync(Ct));
        harness.Clock.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(1, await harness.EventScanAsync(Ct));
    }

    [Fact]
    public async Task A_page_the_contract_accepted_and_the_reader_cannot_read_stores_nothing_and_counts_a_failure()
    {
        using var harness = Create();
        var source = await SourceAsync(harness, cursor: "c-1", cursorAt: Noon.AddHours(-1));
        var open = EventsCatalog.Find("events.read")! with { OutputSchema = new JsonObject { ["type"] = "object" } };
        var reader = new EventReader(harness.Scopes, harness.Clock, NullLogger<EventReader>.Instance, open);
        harness.Events.Answer(Result(new JsonObject
        {
            ["events"] = new JsonArray(new JsonObject { ["kind"] = "not_a_kind", ["occurred_at"] = "2026-10-18T09:00:00Z" }),
            ["cursor"] = "c-2",
            ["has_more"] = false,
        }));

        var report = await reader.ReadAsync(Handed(harness, source), Ct);

        Assert.Equal((EventReadReport.Failed, EventReader.ResultInvalid, FailureClass.Ambiguous), (report.Outcome, report.Code, report.Class));
        var row = await RowAsync(harness, source.Id);
        Assert.Equal(("c-1", 1), (row.Cursor, row.Failures));
        Assert.Equal(0, await StoredAsync(harness));
        Assert.Equal(row.ReadStartedAt!.Value.AddSeconds(2 * Cadence), EventSchedule.DueAt(row, readByThisProcess: true, Cadence));
    }

    [Fact]
    public async Task A_page_the_contract_refuses_stores_nothing_and_counts_a_failure()
    {
        using var harness = Create();
        var source = await SourceAsync(harness, cursor: "c-1", cursorAt: Noon.AddHours(-1));
        var moved = Event("c_2001");
        moved["subject"] = "Re: pricing";
        harness.Events.Answer(Page("c-2", false, moved));

        var report = await ScanAndReadAsync(harness);

        Assert.Equal((EventReader.ResultInvalid, FailureClass.Ambiguous), (report!.Code, report.Class));
        Assert.Equal(("c-1", 1), ((await RowAsync(harness, source.Id)).Cursor, (await RowAsync(harness, source.Id)).Failures));
        Assert.Equal(0, await StoredAsync(harness));
    }

    [Theory]
    [InlineData("rate_limited", FailureClass.Transient, false)]
    [InlineData("events_unauthorized", FailureClass.Transient, false)]
    [InlineData("events_unreachable", FailureClass.Transient, true)]

    // The events family declares no block, so a blocked answer is outside it whatever its code.
    [InlineData("provider_blocked", FailureClass.Blocked, false)]
    [InlineData("events_unreachable", FailureClass.Blocked, false)]

    // The codes passed beside every operation are no events read's: neither a host's own ending nor a reserved code.
    [InlineData(HostOutcomeCodes.PluginException, FailureClass.Permanent, false)]
    [InlineData(UnjudgedOutcomeCodes.ProviderInputInvalid, FailureClass.Validation, false)]
    [InlineData(UnjudgedOutcomeCodes.CapabilityNotOffered, FailureClass.Permanent, false)]
    public async Task A_failure_the_contract_does_not_declare_as_it_came_is_an_answer_outside_it(string code, FailureClass failureClass, bool pinned)
    {
        using var harness = Create();
        await SourceAsync(harness, cursor: "c-1", cursorAt: Noon.AddHours(-1));
        harness.Events.Answer(Failure(code, failureClass, pinned ? new JsonObject { ["campaign"] = "p_1" } : null));

        var report = await ScanAndReadAsync(harness);

        Assert.Equal((EventReader.ResultInvalid, FailureClass.Ambiguous), (report!.Code, report.Class));
    }

    [Fact]
    [Critical(Family.N2, "a read pins nothing")]
    public async Task An_answer_that_pins_an_identifier_stores_nothing()
    {
        using var harness = Create();
        var source = await SourceAsync(harness, cursor: "c-1", cursorAt: Noon.AddHours(-1));
        harness.Events.Answer(Result(
            new JsonObject { ["events"] = new JsonArray(Event("c_2001")), ["cursor"] = "c-2", ["has_more"] = false },
            new JsonObject { ["campaign"] = "p_1" }));

        var report = await ScanAndReadAsync(harness);

        Assert.Equal(EventReader.ResultInvalid, report!.Code);
        Assert.Equal("c-1", (await RowAsync(harness, source.Id)).Cursor);
        Assert.Equal(0, await StoredAsync(harness));
    }

    [Fact]
    public async Task A_stop_ends_the_read_and_writes_nothing()
    {
        using var harness = Create();
        var source = await SourceAsync(harness, cursor: "c-1", cursorAt: Noon.AddHours(-1));
        using var stop = new CancellationTokenSource();
        harness.Events.Answer((_, _) =>
        {
            stop.Cancel();
            return Protocol("plugin_killed");
        });

        await harness.EventScanAsync(Ct);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => harness.Reader.ReadNextAsync(stop.Token));

        var row = await RowAsync(harness, source.Id);
        Assert.Equal((0, (string?)null, (DateTime?)null), (row.Failures, row.LastCode, row.ReadEndedAt));
        Assert.Empty(await ReadLinesAsync(harness));
        Assert.True(harness.Reader.Idle, "A stopped read left the reader busy.");
    }

    private static EventRead Handed(DispatchHarness harness, Jason.Runtime.Persistence.EventSource source)
    {
        var plugins = harness.Resolve<Jason.Runtime.Plugins.Registry.PluginRegistry>().Snapshot;
        return new EventRead(
            source.Id,
            source.PublicId,
            source.PluginId,
            source.BindingIdentity,
            new JsonObject { ["workspace"] = "a" },
            source.Cursor,
            EventSchedule.Plan(source, Noon),
            new PinnedPlugin(plugins.Find(source.PluginId)!, plugins.Id),
            "rts_1");
    }
}
