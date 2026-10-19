using System.Globalization;
using System.Text.Json.Nodes;
using Jason.Contracts.Events;
using Jason.Contracts.Plugins;
using Jason.Runtime.Dispatch;
using Jason.Runtime.Persistence;

namespace Jason.Runtime.Events;

/// <summary>What a read of an account's events starts from.</summary>
public enum EventReadMode
{
    /// <summary>The cursor the plugin returned with the last page stored.</summary>
    Cursor,

    /// <summary>No read of the account has stored anything yet: the store's depth back from now.</summary>
    FirstStart,

    /// <summary>The cursor is older than the store's depth: the depth back from now, and the span before it is a gap.</summary>
    Gap,

    /// <summary>The plugin no longer knew the cursor: by date, from a day before the cursor was stored.</summary>
    ForgottenCursor,

    /// <summary>A span a person asked for again: from its start to its end, then on from its own cursor, never the account's.</summary>
    Backfill,
}

/// <summary>
/// A span of an account's events nobody will read: from where reading stopped to where reading begins again. Said once,
/// in the journal, with the first page of the read that skips it.
/// </summary>
public sealed record EventGap(DateTime From, DateTime To)
{
    /// <summary>What the gap is said under: the source and where the span starts, so a read tried again says it once.</summary>
    public string Key(string sourcePublicId) => $"gap/{sourcePublicId}/{InboundSync.Instant(new DateTimeOffset(DateTime.SpecifyKind(From, DateTimeKind.Utc)))}";
}

/// <summary>What a read starts from: its mode, the input of its first page, and the gap it skips, if any.</summary>
public sealed record EventReadPlan(EventReadMode Mode, JsonObject Input, EventGap? Gap)
{
    /// <summary>The mode as the <c>events_read</c> line writes it.</summary>
    public string ModeWord => EventSchedule.Word(Mode);
}

/// <summary>
/// When an account's events are read, what a read starts from, and whether the source is healthy enough for the inbound
/// read to leave its campaigns to it. Pure: a source's row and the clock decide, and nothing an event says.
/// </summary>
public static class EventSchedule
{
    /// <summary>How far back a read reaches when it cannot start from a cursor: thirty days.</summary>
    public const int DepthDays = 30;

    /// <summary>The most pages one read takes before it lets the scan hand out the next; a read that stops with more is due at once.</summary>
    public const int PagesPerRead = 20;

    /// <summary>How many events a page asks for: the most the contract allows.</summary>
    public const int PageSize = 100;

    /// <summary>The longest a source waits for its next read, however many in a row have failed, and what a permanent failure waits: a day.</summary>
    public const int MaximumWaitSeconds = 86_400;

    /// <summary>How long a cursor the plugin no longer knows is read back past, by date: a day before it was stored.</summary>
    public static readonly TimeSpan ForgottenCursorLookback = TimeSpan.FromDays(1);

    /// <summary>How soon after a read started a signal can bring the next forward: a minute, so a burst of webhooks is one read.</summary>
    public const int SignalFloorSeconds = 60;

    /// <summary>
    /// When the source's next read is due. At once when this process has not read it yet — a start catches every account
    /// up from its cursor — and when it has never been read. A read that stopped with more to read is due when it ended:
    /// at once, but behind an account that has been due for longer, so one account's backlog takes turns with the others
    /// rather than holding the reader. Otherwise a cadence after the last read started, doubled for each failure in a row
    /// and never more than a day; a permanent or a validation failure waits the day. A signal since the last read started
    /// brings it to the moment the signal came, never to within a minute of that start, whatever the wait was.
    /// </summary>
    public static DateTime DueAt(EventSource source, bool readByThisProcess, int cadenceSeconds)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (!readByThisProcess || source.ReadStartedAt is not { } started)
        {
            return DateTime.MinValue;
        }

        if (source.More)
        {
            return source.ReadEndedAt ?? started;
        }

        DateTime due;
        if (source.Failures > 0 && FinalClass(source.LastClass))
        {
            due = started.AddSeconds(MaximumWaitSeconds);
        }
        else
        {
            // Doubling past the cap only overflows: thirty-two doublings of a minute are far beyond a day.
            var wait = Math.Min(cadenceSeconds * Math.Pow(2, Math.Clamp(source.Failures, 0, 32)), MaximumWaitSeconds);
            due = started.AddSeconds(wait);
        }

        if (Signalled(source) && source.SignalAt is { } signal)
        {
            var floor = started.AddSeconds(SignalFloorSeconds);
            var signalled = signal > floor ? signal : floor;
            return signalled < due ? signalled : due;
        }

        return due;
    }

    /// <summary>
    /// Whether somebody asked for the account to be read sooner since its last read started — a signal that read has not
    /// answered yet. The read it brings forward says so in its line.
    /// </summary>
    public static bool Signalled(EventSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        return source.SignalAt is { } signal && (source.ReadStartedAt is not { } started || signal > started);
    }

    public static bool IsDue(EventSource source, bool readByThisProcess, int cadenceSeconds, DateTime now) =>
        now >= DueAt(source, readByThisProcess, cadenceSeconds);

    /// <summary>The sources that are due, the most overdue first and, among those due as long, the oldest source first.</summary>
    public static IReadOnlyList<EventSource> MostOverdueFirst(
        IEnumerable<EventSource> sources,
        Func<EventSource, bool> readByThisProcess,
        int cadenceSeconds,
        DateTime now)
    {
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentNullException.ThrowIfNull(readByThisProcess);
        return
        [
            .. sources
                .Select(source => (Source: source, Due: DueAt(source, readByThisProcess(source), cadenceSeconds)))
                .Where(entry => now >= entry.Due)
                .OrderBy(entry => entry.Due)
                .ThenBy(entry => entry.Source.Id)
                .Select(entry => entry.Source),
        ];
    }

    /// <summary>
    /// What the next read of the source starts from. A cursor stored within the depth is read from; a cursor older than
    /// that is not, and neither is anything before the depth — the span from the cursor to the depth is a gap. A source
    /// never read starts at the depth and owes nothing before it. A cursor the plugin forgot is read again by date from a
    /// day before it was stored, or from the depth when that is later, and the span before the depth is a gap.
    /// </summary>
    public static EventReadPlan Plan(EventSource source, DateTime now)
    {
        ArgumentNullException.ThrowIfNull(source);
        var depth = Second(now.AddDays(-DepthDays));
        if (source.Cursor is { } cursor)
        {
            if (source.CursorAt is not { } stored || now - stored <= TimeSpan.FromDays(DepthDays))
            {
                return new EventReadPlan(EventReadMode.Cursor, Input(cursor, since: null), Gap: null);
            }

            return new EventReadPlan(EventReadMode.Gap, Input(cursor: null, depth), new EventGap(stored, depth));
        }

        if (source.CursorAt is { } forgotten)
        {
            var anchor = forgotten - ForgottenCursorLookback;
            return anchor >= depth
                ? new EventReadPlan(EventReadMode.ForgottenCursor, Input(cursor: null, Second(anchor)), Gap: null)
                : new EventReadPlan(EventReadMode.ForgottenCursor, Input(cursor: null, depth), new EventGap(anchor, depth));
        }

        return new EventReadPlan(EventReadMode.FirstStart, Input(cursor: null, depth), Gap: null);
    }

    /// <summary>
    /// What the next read of a span asked for again starts from: its start and its end, to the millisecond, before any
    /// page of it has landed; then its own cursor, whose chain the plugin keeps within the span. Never the account's
    /// cursor, and never a gap: a span says nothing about what the account's own reading reached.
    /// </summary>
    public static EventReadPlan Plan(EventBackfill backfill)
    {
        ArgumentNullException.ThrowIfNull(backfill);
        var input = backfill.Cursor is { } cursor
            ? Next(cursor)
            : new JsonObject
            {
                ["cursor"] = null,
                ["since"] = Instant(backfill.From),
                ["until"] = Instant(backfill.To),
                ["limit"] = PageSize,
            };
        return new EventReadPlan(EventReadMode.Backfill, input, Gap: null);
    }

    /// <summary>
    /// When a span's next read is due — once no account's own read is: at once, unless its last read ended in a failure
    /// that may pass, when it waits the account's doubling from when that read started, never more than a day.
    /// </summary>
    public static DateTime BackfillDueAt(EventBackfill backfill, int cadenceSeconds)
    {
        ArgumentNullException.ThrowIfNull(backfill);
        if (backfill.Failures == 0)
        {
            return DateTime.MinValue;
        }

        var wait = Math.Min(cadenceSeconds * Math.Pow(2, Math.Clamp(backfill.Failures, 0, 32)), MaximumWaitSeconds);
        return (backfill.AttemptedAt ?? backfill.RequestedAt).AddSeconds(wait);
    }

    /// <summary>
    /// Whether a read's end ends the span it read: an answer that repeating cannot change — a permanent or a validation
    /// failure, or an answer outside the contract, which for one span is the same answer each time. Anything that may pass
    /// leaves it waiting; with no way to call a span off, one that could never succeed must not hold its place for ever.
    /// </summary>
    public static bool EndsABackfill(string code, FailureClass failureClass) =>
        failureClass is FailureClass.Permanent or FailureClass.Validation || string.Equals(code, EventReader.ResultInvalid, StringComparison.Ordinal);

    /// <summary>The input of a page read from a cursor: the next page of a read, after the first.</summary>
    public static JsonObject Next(string cursor)
    {
        ArgumentException.ThrowIfNullOrEmpty(cursor);
        return Input(cursor, since: null);
    }

    /// <summary>
    /// Whether the inbound read may leave a campaign routed to this source's plugin to the events: only while the source
    /// is healthy — its last read ended in success and started within the shorter of two cadences and the inbound read's
    /// look-back — and never before its first read that succeeded. A source failing, or not read for a while, hands its
    /// campaigns back to the inbound read, whose next read then starts no later than that read that succeeded: the skip
    /// left the watermark at most the window after it, less the look-back. A look-back of 0 never leaves a campaign to the
    /// events.
    /// </summary>
    public static bool IsHealthy(EventSource? source, int cadenceSeconds, int lookbackSeconds, DateTime now)
    {
        var window = Math.Min(2L * cadenceSeconds, lookbackSeconds);
        return window > 0 && source is { SucceededAt: { } succeeded, Failures: 0 } && now - succeeded <= TimeSpan.FromSeconds(window);
    }

    /// <summary>The mode's word, as the <c>events_read</c> line writes it.</summary>
    public static string Word(EventReadMode mode) => mode switch
    {
        EventReadMode.Cursor => "cursor",
        EventReadMode.FirstStart => "first_start",
        EventReadMode.Gap => "gap",
        EventReadMode.ForgottenCursor => "forgotten_cursor",
        EventReadMode.Backfill => "backfill",
        _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "No such mode."),
    };

    /// <summary>A failure class that repeating will not change, as the source's row writes it: it waits the day.</summary>
    private static bool FinalClass(string? word) =>
        word is not null && Enum.TryParse<FailureClass>(word, ignoreCase: true, out var failure) && !EventsContract.Retriable(failure);

    private static JsonObject Input(string? cursor, DateTime? since) => new()
    {
        ["cursor"] = cursor,
        ["since"] = since is { } moment ? moment.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture) : null,
        ["until"] = null,
        ["limit"] = PageSize,
    };

    /// <summary>A moment as the runtime writes every timestamp: UTC, to the millisecond.</summary>
    private static string Instant(DateTime moment) => InboundSync.Instant(new DateTimeOffset(DateTime.SpecifyKind(moment, DateTimeKind.Utc)));

    /// <summary>A moment to the second, rounded down, so a read never starts later than it says.</summary>
    private static DateTime Second(DateTime moment) =>
        new(moment.Ticks - (moment.Ticks % TimeSpan.TicksPerSecond), DateTimeKind.Utc);
}
