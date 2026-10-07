using Jason.Contracts.Api;
using Jason.Runtime.Configuration;
using Jason.Runtime.Execution;
using Jason.Runtime.Journal;
using Jason.Runtime.Logging;
using Jason.Runtime.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Jason.Runtime.Notifications;

/// <summary>
/// The fifth step of a scan, after the claimed work has been handed out: read what was committed since the last read,
/// keep the burst it belongs to, and hand one notification to the sender once the burst has settled. It reads rows the
/// transactions that wrote them had already committed, so a notification follows its commit by construction and a
/// rolled-back line is never there to be read; and it decides nothing about the work it notices.
/// </summary>
/// <remarks>
/// <para>
/// Two reads, both bounded. Once per process, the start finds the last <c>notification_sent</c> line —
/// <c>WHERE "j"."kind" = 'notification_sent' ORDER BY "j"."id" DESC LIMIT 1</c>, one seek on the index over
/// <c>kind</c> — and continues from the line it covers through, found by its public id through that column's unique
/// index. A database that has never notified anybody starts at its newest line and does not walk its history: if
/// anything is pending then, one burst is seeded, so the first start after an upgrade says once what waits.
/// </para>
/// <para>
/// Then, at each scan, a range of the primary key above the cursor — <c>WHERE "j"."id" &gt; @cursor ORDER BY "j"."id"
/// LIMIT @batch</c>. The journal has one writer at a time, so its keys commit in order and a range above the cursor
/// never skips a line that commits later.
/// </para>
/// <para>
/// A cursor far behind the journal's newest line — a start above a watermark that has not moved for a long time, or
/// notifications turned on again after a long time off — is not caught up a batch per scan: a burst split across two
/// batches would go as two pointers. What lies between it and the newest line is folded in by one aggregate over the
/// index on <c>kind</c>, the same at a start and at a scan, and the cursor jumps to that line.
/// </para>
/// </remarks>
public sealed class Notifier(
    NotifierState state,
    INotificationQueue queue,
    NotificationStatus notifications,
    LiveSettings<NotificationsOptions> settings,
    LiveSettings<DispatcherOptions> dispatcher,
    DispatcherStatus status,
    TimeProvider clock,
    ILogger<Notifier> logger)
{
    /// <summary>At most this many lines per scan: a primary-key range, so a runtime catching up after a long time off reads in steps.</summary>
    public const int ReadBatch = 2_000;

    private static readonly Action<ILogger, Exception?> Failed = LoggerMessage.Define(
        LogLevel.Warning,
        new EventId(5, nameof(Failed)),
        "The notifier failed; the scan's work was already handed out");

    // The same event, for a notifier that gave out reading a stored value — a journal line that no longer parses, or holds
    // a key twice or a value of the wrong shape: the column, what kind of failure and where, and no exception, because
    // what a reader quotes is what the line held.
    private static readonly Action<ILogger, string, Exception?> FailedToParse = LoggerMessage.Define<string>(
        LogLevel.Warning,
        new EventId(5, nameof(Failed)),
        "The notifier failed; the scan's work was already handed out: {Failure}");

    /// <summary>The two kinds of line that mean a person is now needed: an approval parked at a claim, and a question a role raised.</summary>
    public static IReadOnlySet<string> Triggers { get; } =
        new HashSet<string>(StringComparer.Ordinal) { JournalKinds.ApprovalRequested, JournalKinds.DecisionRaised };

    /// <returns>1 when a notification was handed over, 0 otherwise.</returns>
    public async Task<int> NotifyAsync(JasonDbContext db, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(db);
        NotificationRequest? handedOver = null;
        try
        {
            var options = settings.Current;
            if (!options.AnyChannel)
            {
                return 0;
            }

            if (!state.Started)
            {
                await StartAsync(db, ct).ConfigureAwait(false);
            }

            await ReadAsync(db, ct).ConfigureAwait(false);

            // Until this runtime has a start to count the grace from, nothing is due.
            if (status.RuntimeStartedAt is not { } startedAt)
            {
                return 0;
            }

            var now = clock.GetUtcNow().UtcDateTime;
            var quiet = NotificationRule.EffectiveQuiet(options, dispatcher.Current);
            var maxWait = TimeSpan.FromSeconds(options.MaxWaitSeconds);
            var floor = TimeSpan.FromMinutes(options.MinIntervalMinutes);
            if (!state.TryHandOver((burst, lastDeliveredAt) => NotificationRule.IsDue(burst, now, startedAt, lastDeliveredAt, quiet, maxWait, floor), out var request))
            {
                return 0;
            }

            handedOver = request;
            queue.Enqueue(request);
            return 1;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // After the dispatch, by its place in the scan: whatever went wrong here, the claimed work is already
            // running. Only a hand-over this call made is taken back; a notification already being sent is the
            // sender's to release.
            if (handedOver is not null)
            {
                state.Return(handedOver);
            }

            if (ParseFailure.Is(ex))
            {
                FailedToParse(logger, ParseFailure.Describe(ex), null);
            }
            else
            {
                Failed(logger, ex);
            }

            db.ChangeTracker.Clear();
            return 0;
        }
    }

    private async Task StartAsync(JasonDbContext db, CancellationToken ct)
    {
        var last = await db.Journal.AsNoTracking()
            .Where(e => e.Kind == JournalKinds.NotificationSent)
            .OrderByDescending(e => e.Id)
            .Select(e => new { e.Id, e.PublicId, e.Ts, e.New })
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);

        if (last is not null)
        {
            // What the status said before this process existed, so system.info answers after a restart.
            if (NotificationLine.Read(last.New) is { } line)
            {
                notifications.Seed(line, new DateTimeOffset(DateTime.SpecifyKind(last.Ts, DateTimeKind.Utc)));
            }

            var (coveredThrough, lastDeliveredAt) = NotificationLine.ReadWatermark(last.New);
            var deliveredThrough = NotificationLine.ReadDeliveredThrough(last.New);
            var covered = coveredThrough is null
                ? null
                : await db.Journal.AsNoTracking()
                    .Where(e => e.PublicId == coveredThrough)
                    .Select(e => new { e.Id, e.PublicId })
                    .FirstOrDefaultAsync(ct)
                    .ConfigureAwait(false);

            // The line it covered through is where reading resumes; a line that names none starts after itself. What
            // lies between there and the newest line is folded in rather than read a batch per scan: a watermark that
            // has not moved for a long time would keep a new approval waiting behind every line since.
            var resume = covered?.Id ?? last.Id;
            var head = await NewestAsync(db, ct).ConfigureAwait(false);
            if (head is null || head.Value.Id <= resume)
            {
                state.Start(resume, covered?.PublicId ?? last.PublicId, lastDeliveredAt, seed: null, deliveredThrough);
                return;
            }

            state.Start(head.Value.Id, head.Value.PublicId, lastDeliveredAt, await FoldAsync(db, resume, head.Value.Id, ct).ConfigureAwait(false), deliveredThrough);
            return;
        }

        var newestLine = await NewestAsync(db, ct).ConfigureAwait(false);

        var waiting = await db.Approvals.AsNoTracking().CountAsync(a => a.Status == ApprovalStatus.Pending, ct).ConfigureAwait(false)
            + await db.Decisions.AsNoTracking().CountAsync(d => d.Status == DecisionStatus.Pending, ct).ConfigureAwait(false);
        var now = clock.GetUtcNow().UtcDateTime;
        // No line was ever written: failed protective work is counted from the newest line now, and history is not walked.
        state.Start(newestLine?.Id ?? 0, newestLine?.PublicId, lastDeliveredAt: null, seed: waiting > 0 ? new Burst(1, now, now) : null, deliveredThrough: newestLine?.PublicId);
    }

    /// <summary>The newest line of the journal: one seek on its primary key.</summary>
    private static async Task<(int Id, string PublicId)?> NewestAsync(JasonDbContext db, CancellationToken ct)
    {
        var newest = await db.Journal.AsNoTracking()
            .OrderByDescending(e => e.Id)
            .Select(e => new { e.Id, e.PublicId })
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);
        return newest is null ? null : (newest.Id, newest.PublicId);
    }

    /// <summary>
    /// The triggers in <c>(after, upTo]</c>, as one burst: one aggregate — a count and the oldest and newest times — over
    /// the index on <c>kind</c>; null when there are none.
    /// </summary>
    private static async Task<Burst?> FoldAsync(JasonDbContext db, int after, int upTo, CancellationToken ct)
    {
        var folded = await db.Journal.AsNoTracking()
            .Where(e => e.Id > after && e.Id <= upTo && (e.Kind == JournalKinds.ApprovalRequested || e.Kind == JournalKinds.DecisionRaised))
            .GroupBy(e => 1)
            .Select(g => new { Lines = g.Count(), Oldest = g.Min(e => e.Ts), Newest = g.Max(e => e.Ts) })
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);
        var failed = await ProtectiveFailureLines.Of(db)
            .Where(e => e.Id > after && e.Id <= upTo)
            .GroupBy(e => 1)
            .Select(g => new { Lines = g.Count(), Oldest = g.Min(e => e.Ts), Newest = g.Max(e => e.Ts) })
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);

        var asked = folded is null || folded.Lines == 0 ? null : new Burst(folded.Lines, folded.Oldest, folded.Newest);
        var protective = failed is null || failed.Lines == 0 ? null : new Burst(failed.Lines, failed.Oldest, failed.Newest);
        return asked is null ? protective
            : protective is null ? asked
            : new Burst(
                asked.Lines + protective.Lines,
                protective.Oldest < asked.Oldest ? protective.Oldest : asked.Oldest,
                protective.Newest > asked.Newest ? protective.Newest : asked.Newest);
    }

    private async Task ReadAsync(JasonDbContext db, CancellationToken ct)
    {
        var cursor = state.Cursor;
        var lines = await db.Journal.AsNoTracking()
            .Where(e => e.Id > cursor)
            .OrderBy(e => e.Id)
            .Take(ReadBatch)
            .Select(e => new { e.Id, e.PublicId, e.Kind, e.Ts })
            .ToListAsync(ct)
            .ConfigureAwait(false);

        if (lines.Count == 0)
        {
            return;
        }

        var newest = lines[^1];

        // A failed or expired line wakes a notification only when its work was protective, which the line alone does not
        // say: the same predicate the sender counts with decides, over this batch.
        var oldest = lines[0].Id;
        var protective = lines.Any(line => ProtectiveFailureLines.Kinds.Contains(line.Kind))
            ? (await ProtectiveFailureLines.Of(db).Where(e => e.Id >= oldest && e.Id <= newest.Id).Select(e => e.Id).ToListAsync(ct).ConfigureAwait(false)).ToHashSet()
            : new HashSet<int>();
        state.Advance(newest.Id, newest.PublicId, lines.Where(line => Triggers.Contains(line.Kind) || protective.Contains(line.Id)).Select(line => line.Ts));
        if (lines.Count < ReadBatch)
        {
            return;
        }

        // A full batch: the journal may be far ahead of the cursor, as after notifications were turned on again. The rest
        // is folded in the way a start folds it, and the cursor jumps to the newest line.
        var head = await NewestAsync(db, ct).ConfigureAwait(false);
        if (head is { } top && top.Id > newest.Id)
        {
            state.Advance(top.Id, top.PublicId, await FoldAsync(db, newest.Id, top.Id, ct).ConfigureAwait(false));
        }
    }
}
