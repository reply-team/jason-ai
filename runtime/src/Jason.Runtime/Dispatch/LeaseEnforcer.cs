using System.Globalization;
using Jason.Contracts.Api;
using Jason.Runtime.Configuration;
using Jason.Runtime.Domain;
using Jason.Runtime.Execution;
using Jason.Runtime.Persistence;
using Jason.Runtime.WorkItems;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Jason.Runtime.Dispatch;

/// <summary>
/// The second step of a scan: attempts nobody is looking after any more. An attempt is lost when its whole
/// budget is spent, or when an executor that promised to report went quiet; either way the work item is given
/// back rather than left stuck, and a child that is still ours is stopped so it cannot burn tokens unobserved.
/// </summary>
public sealed class LeaseEnforcer(
    AttemptOutcomes outcomes,
    TimeProvider clock,
    IOptionsMonitor<DispatcherOptions> options,
    RunningAttemptRegistry registry,
    DispatcherStatus status)
{
    public async Task<int> EnforceAsync(JasonDbContext db, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(db);
        var now = clock.GetUtcNow().UtcDateTime;
        var current = options.CurrentValue;

        // The ids first, the rows one at a time: losing a race on one item must not detach the others.
        var live = await db.WorkItems.AsNoTracking()
            .Where(w => w.Status == WorkItemStatus.Scheduled || w.Status == WorkItemStatus.Processing)
            .Select(w => w.Id)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var lost = 0;
        foreach (var id in live)
        {
            var item = await db.WorkItems.WithNavigation().Include(w => w.Attempts)
                .FirstOrDefaultAsync(w => w.Id == id, ct)
                .ConfigureAwait(false);
            if (item is null || WorkItemQueries.LiveAttempt(item) is not { } attempt)
            {
                continue;
            }

            var limits = EffectiveLimits.For(item, current);
            var failure = Verdict(item, attempt, limits, now);
            if (failure is null)
            {
                continue;
            }

            outcomes.Fail(db, item, attempt, failure.Value.Code, failure.Value.Message, trace: null, details: null, Actors.Dispatcher);
            try
            {
                await db.SaveChangesAsync(ct).ConfigureAwait(false);
            }
            catch (DbUpdateConcurrencyException)
            {
                // Someone else — a cancellation, the executor's own completion — got there first and is welcome to it.
                db.ChangeTracker.Clear();
                continue;
            }

            lost++;
            registry.TryKill(attempt.PublicId);
        }

        await StopLingeringChildrenAsync(db, current, now, ct).ConfigureAwait(false);
        return lost;
    }

    /// <summary>Why this attempt is lost, or null while it is still within its budget and still reporting.</summary>
    private (string Code, string Message)? Verdict(WorkItem item, Attempt attempt, EffectiveLimits limits, DateTime now)
    {
        if (attempt.LockUntil < now)
        {
            return (AttemptErrors.LeaseExpired, string.Create(
                CultureInfo.InvariantCulture,
                $"The attempt's lease ran out: its whole budget of {limits.TimeoutSeconds} seconds is spent."));
        }

        // Before the work actually started there is nothing to report, so only the lease applies.
        if (item.Status != WorkItemStatus.Processing || limits.HeartbeatSeconds <= 0)
        {
            return null;
        }

        var deadline = LastSignOfLife(attempt, status.RuntimeStartedAt).AddSeconds(2L * limits.HeartbeatSeconds);
        return now > deadline
            ? (AttemptErrors.HeartbeatMissed, string.Create(
                CultureInfo.InvariantCulture,
                $"No heartbeat arrived for {2 * limits.HeartbeatSeconds} seconds: one interval to report and one of grace."))
            : null;
    }

    /// <summary>
    /// One interval to report and one of grace, counted from whichever came last: the attempt starting, its
    /// last heartbeat, or this runtime starting — a restart cannot hold an executor to beats it never heard.
    /// </summary>
    private static DateTime LastSignOfLife(Attempt attempt, DateTime? runtimeStartedAt)
    {
        var since = attempt.StartedAt ?? attempt.ClaimedAt;
        if (attempt.LastHeartbeatAt is { } beat && beat > since)
        {
            since = beat;
        }

        return runtimeStartedAt is { } started && started > since ? started : since;
    }

    /// <summary>
    /// A process that reported its result through the API and then stayed around. It has had its grace; the
    /// attempt is finished, so nothing it does now can be written down anyway.
    /// </summary>
    private async Task StopLingeringChildrenAsync(JasonDbContext db, DispatcherOptions current, DateTime now, CancellationToken ct)
    {
        var running = registry.AttemptIds.ToList();
        if (running.Count == 0)
        {
            return;
        }

        var grace = now.AddSeconds(-current.ExitGraceSeconds);
        var finished = await db.Attempts.AsNoTracking()
            .Where(a => running.Contains(a.PublicId)
                && a.Status != AttemptStatus.Scheduled
                && a.Status != AttemptStatus.Running
                && a.FinishedAt != null
                && a.FinishedAt < grace)
            .Select(a => a.PublicId)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        foreach (var attemptId in finished)
        {
            registry.TryKill(attemptId);
        }
    }
}
