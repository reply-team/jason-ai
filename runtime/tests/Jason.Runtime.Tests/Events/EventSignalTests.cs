using Jason.Runtime.Tests.Dispatch;
using Microsoft.EntityFrameworkCore;
using static Jason.Runtime.Tests.Events.EventsBed;
using static Jason.Runtime.Tests.Events.FakeEventsInvoker;

namespace Jason.Runtime.Tests.Events;

/// <summary>
/// A signal, as the scan honours it on a held clock: the account's next read is due at the next scan, never within a
/// minute of the last read's start, a burst of signals is one read, and the read a signal brought forward says so.
/// </summary>
public class EventSignalTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_signal_is_honoured_but_not_within_sixty_seconds_of_the_last_start()
    {
        using var harness = Create();
        await ScanAndReadAsync(harness);
        await SignalAsync(harness, Noon.AddSeconds(10));

        harness.Clock.Advance(TimeSpan.FromSeconds(59));
        Assert.Equal(0, await harness.EventScanAsync(Ct));
        harness.Clock.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(1, await harness.EventScanAsync(Ct));
        await harness.Reader.ReadNextAsync(Ct);

        // The read answered the signal: the next waits its cadence from that read's start.
        Assert.Equal(0, await harness.EventScanAsync(Ct));
        harness.Clock.Advance(TimeSpan.FromSeconds(Cadence - 1));
        Assert.Equal(0, await harness.EventScanAsync(Ct));
        Assert.Equal(2, harness.Events.Requests.Count);
    }

    [Fact]
    public async Task A_burst_of_signals_is_one_read()
    {
        using var harness = Create();
        await ScanAndReadAsync(harness);
        for (var second = 5; second <= 50; second += 5)
        {
            await SignalAsync(harness, Noon.AddSeconds(second));
        }

        harness.Clock.Advance(TimeSpan.FromSeconds(60));
        await ScanAndReadAsync(harness);
        await ScanAndReadAsync(harness);

        Assert.Equal(2, harness.Events.Requests.Count);
    }

    /// <summary>The read a signal brought forward says it was signalled in its line, and the cadence's next read says it was not.</summary>
    [Fact]
    public async Task A_signalled_read_says_so_in_its_line_and_a_read_on_the_cadence_does_not()
    {
        using var harness = Create();
        await ScanAndReadAsync(harness);
        await SignalAsync(harness, Noon.AddSeconds(30));
        harness.Clock.Advance(TimeSpan.FromSeconds(60));
        harness.Events.Answer(Page("c-1", false, EventReaderTests.Event("c_2001")));
        await ScanAndReadAsync(harness);
        harness.Clock.Advance(TimeSpan.FromSeconds(Cadence));
        harness.Events.Answer(Page("c-2", false, EventReaderTests.Event("c_2002")));
        await ScanAndReadAsync(harness);

        var lines = await ReadLinesAsync(harness);

        Assert.Equal(3, lines.Count);
        Assert.Equal(
            [false, true, false],
            lines.Select(line => (bool)line.New!["signaled"]!));
        Assert.Equal([0, 1, 1], lines.Select(line => (int)line.New!["stored"]!));
    }

    /// <summary>
    /// A signal is for the account's own read: a span read while one waits within its minute says it was not signalled,
    /// and leaves the signal for the account's read, which it brings forward once the minute has passed.
    /// </summary>
    [Fact]
    public async Task A_span_read_while_a_signal_waits_is_not_the_signalled_read()
    {
        using var harness = Create();
        await ScanAndReadAsync(harness);
        await BackfillAsync(harness, await SourceOfAsync(harness, "a"));
        await SignalAsync(harness, Noon.AddSeconds(10));
        harness.Clock.Advance(TimeSpan.FromSeconds(30));

        await ScanAndReadAsync(harness);
        var span = (await ReadLinesAsync(harness))[^1];
        harness.Clock.Advance(TimeSpan.FromSeconds(30));
        var own = await harness.EventScanAsync(Ct);

        Assert.Equal(("backfill", false), ((string?)span.New!["mode"], (bool)span.New["signaled"]!));
        Assert.Equal(1, own);
        await harness.Reader.ReadNextAsync(Ct);
        Assert.Null(harness.Events.Requests[^1].Input["until"]);
    }

    private static async Task SignalAsync(DispatchHarness harness, DateTime at)
    {
        await using var db = harness.Open();
        await db.EventSources.ExecuteUpdateAsync(set => set.SetProperty(source => source.SignalAt, at), Ct);
    }
}
