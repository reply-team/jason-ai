using Jason.Contracts.Api;
using Jason.Runtime.Events;
using Jason.Runtime.Persistence;
using Jason.Runtime.Routing;
using Jason.Runtime.Tests.Dispatch;
using Jason.Runtime.Tests.Plugins;
using static Jason.Runtime.Tests.Events.EventsBed;

namespace Jason.Runtime.Tests.Events;

/// <summary>
/// The scan's events step: every account a route binds at a plugin that reads its events has a source, and the reader is
/// handed the most overdue read — one at a time, from where the source stands, on the cadence the settings say now.
/// </summary>
public class EventScanTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Every_account_a_route_binds_at_a_plugin_that_reads_events_gets_one_source()
    {
        using var harness = new DispatchHarness(Noon);
        Load(harness, EventsPlugin(), TestPlugins.Loaded("silent", operations: ["campaign.get"]));
        Route(
            harness,
            Bound(Reader, "a"),
            new Dictionary<string, Route>(StringComparer.Ordinal) { ["campaign.get"] = Bound("silent", "s") },
            new Dictionary<string, RouteSet>(StringComparer.Ordinal)
            {
                ["cmp_01JB6K8TQ2W9V4MZ0C3Y7H5NRD"] = new(Bound(Reader, "b"), new Dictionary<string, Route>(StringComparer.Ordinal)
                {
                    ["conversation.list"] = new(Reader, null, null),
                    ["campaign.get"] = Bound(Reader, "a"),
                }),
            });

        await harness.EventScanAsync(Ct);
        await harness.EventScanAsync(Ct);

        var rows = await RowsAsync(harness);
        Assert.Equal(
            ["reader|sha256:a", "reader|sha256:b", "reader|none"],
            rows.Select(row => $"{row.PluginId}|{row.BindingIdentity}"));
        Assert.All(rows, row => Assert.StartsWith("evs_", row.PublicId, StringComparison.Ordinal));
    }

    [Fact]
    public async Task An_account_no_route_binds_any_more_keeps_its_source_and_its_cursor_and_is_not_read()
    {
        using var harness = Create("a");
        var gone = await SourceAsync(harness, "gone", cursor: "c-7", cursorAt: Noon.AddHours(-1));

        Assert.Equal(1, await harness.EventScanAsync(Ct));
        await harness.Reader.ReadNextAsync(Ct);
        Assert.Equal(0, await harness.EventScanAsync(Ct));

        var kept = await RowAsync(harness, gone.Id);
        Assert.Equal("c-7", kept.Cursor);
        Assert.Null(kept.ReadStartedAt);
        Assert.DoesNotContain(harness.Events.Requests, request => (string?)request.Binding?["workspace"] == "gone");
    }

    [Fact]
    public async Task A_start_reads_each_account_from_its_stored_cursor_at_its_first_scan()
    {
        using var harness = Create();
        await SourceAsync(harness, cursor: "c-41", cursorAt: Noon.AddHours(-1), started: Noon.AddMinutes(-1), succeeded: Noon.AddMinutes(-1));

        var report = await ScanAndReadAsync(harness);

        Assert.NotNull(report);
        var request = Assert.Single(harness.Events.Requests);
        Assert.Equal("""{"cursor":"c-41","since":null,"until":null,"limit":100}""", request.Input.ToJsonString());
        Assert.Equal("""{"workspace":"a"}""", request.Binding!.ToJsonString());
        Assert.Equal("events.read", request.Operation);
    }

    [Fact]
    public async Task An_edited_cadence_applies_at_the_next_scan()
    {
        using var harness = Create();
        await ScanAndReadAsync(harness);

        harness.Clock.Advance(TimeSpan.FromSeconds(300));
        Assert.Equal(0, await harness.EventScanAsync(Ct));

        harness.Options.CurrentValue.EventsCadenceSeconds = 300;
        Assert.Equal(1, await harness.EventScanAsync(Ct));
    }

    [Fact]
    public async Task One_read_is_handed_over_at_a_time()
    {
        using var harness = new DispatchHarness(Noon);
        Load(harness, EventsPlugin());
        Route(harness, Bound(Reader, "a"), new Dictionary<string, Route>(StringComparer.Ordinal) { ["campaign.get"] = Bound(Reader, "b") });

        Assert.Equal(1, await harness.EventScanAsync(Ct));
        Assert.Equal(0, await harness.EventScanAsync(Ct));
        await harness.Reader.ReadNextAsync(Ct);
        Assert.Equal(1, await harness.EventScanAsync(Ct));
        await harness.Reader.ReadNextAsync(Ct);

        Assert.Equal(["a", "b"], harness.Events.Requests.Select(request => (string?)request.Binding!["workspace"]));
        Assert.True(harness.Reader.Idle, "The reader is not idle after its reads ended.");
    }

    /// <summary>
    /// An account with a backlog of pages takes turns with another account rather than holding the reader: a read that
    /// stopped with more is due from when it ended, and an account overdue for longer goes first.
    /// </summary>
    [Fact]
    public async Task An_account_with_a_backlog_takes_turns_with_the_others()
    {
        using var harness = new DispatchHarness(Noon);
        Load(harness, EventsPlugin());
        Route(harness, Bound(Reader, "a"), new Dictionary<string, Route>(StringComparer.Ordinal) { ["campaign.get"] = Bound(Reader, "b") });
        var page = 0;
        harness.Events.Otherwise = (request, _) => (string?)request.Binding!["workspace"] == "a"
            ? FakeEventsInvoker.Page($"c-{++page}", true)
            : FakeEventsInvoker.Page("c-b", false);

        var turns = new List<string?>();
        for (var read = 1; read <= 5; read++)
        {
            if (read == 4)
            {
                harness.Clock.Advance(TimeSpan.FromSeconds(Cadence + 1));
            }

            await ScanAndReadAsync(harness);
            turns.Add((string?)harness.Events.Requests[^1].Binding!["workspace"]);
        }

        Assert.Equal(["a", "b", "a", "a", "b"], turns);
    }

    [Theory]
    [InlineData(DispatcherState.Draining)]
    [InlineData(DispatcherState.Stopped)]
    public async Task A_dispatcher_that_is_not_running_hands_out_no_read(DispatcherState state)
    {
        using var harness = Create();
        harness.Status.State = state;

        Assert.Equal(0, await harness.EventScanAsync(Ct));
        Assert.Null(await harness.Reader.ReadNextAsync(Ct));
        Assert.Single(await RowsAsync(harness));
    }

    [Fact]
    public async Task A_plugin_that_is_not_usable_here_has_no_account_read()
    {
        using var harness = new DispatchHarness(Noon);
        Load(harness, TestPlugins.Loaded(Reader, operations: ["campaign.get"], events: [1], status: Contracts.Plugins.PluginStatus.Unavailable));
        Route(harness, Bound(Reader, "a"));

        Assert.Equal(0, await harness.EventScanAsync(Ct));
        Assert.Empty(await RowsAsync(harness));
    }

    /// <summary>
    /// In a scan the events step runs before the inbound read: the first question the scan asks of the sources comes
    /// before the first question it asks about the campaigns' inbound reads.
    /// </summary>
    [Fact]
    public async Task The_events_step_runs_before_the_inbound_read_in_a_scan()
    {
        using var harness = Create();
        harness.Commands.Clear();

        await harness.Runner.ScanOnceAsync(Ct);

        var texts = harness.Commands.Texts;
        var events = texts.ToList().FindIndex(text => text.Contains("FROM \"event_sources\"", StringComparison.Ordinal));
        var inbound = texts.ToList().FindIndex(text => text.Contains("\"inbound_watermark\"", StringComparison.Ordinal));
        Assert.True(events >= 0 && inbound >= 0 && events < inbound, $"The sources were first asked about at command {events}, the inbound reads at {inbound}.");
    }

    [Fact]
    public async Task The_scan_hands_the_events_read_over_before_the_inbound_read()
    {
        using var harness = Create();

        var report = await harness.Runner.ScanOnceAsync(Ct);

        Assert.Equal(1, report.EventReads);
        Assert.False(harness.Reader.Idle, "The scan handed no read to the reader.");
        Assert.NotNull(await harness.Reader.ReadNextAsync(Ct));
    }
}
