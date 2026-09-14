using System.Text.Json.Nodes;
using Jason.Contracts.Api;
using Jason.Contracts.Discovery;
using Jason.Contracts.Ids;
using Jason.Runtime.Configuration;
using Jason.Runtime.Domain;
using Jason.Runtime.Execution;
using Jason.Runtime.Journal;
using Jason.Runtime.Persistence;
using Jason.Runtime.WorkItems;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Jason.Runtime.Dispatch;

/// <summary>
/// The third step of a scan, and the only one that hands work out. Everything happens inside one immediate
/// transaction, so two runtimes — or two scans — cannot both claim the same item: the second waits, sees the
/// item already taken and moves on. One item per campaign per scan keeps a busy campaign from starving the
/// others, and never more than the free handler slots, so a queued item's lease cannot expire before it runs.
/// </summary>
public sealed class Claimer(
    JournalWriter journal,
    TimeProvider clock,
    AttemptOutcomes outcomes,
    EntryCommandResolver resolver,
    IOptionsMonitor<DispatcherOptions> options,
    JasonPaths paths,
    ILogger<Claimer> logger)
{
    private static readonly Action<ILogger, string, Exception?> ClaimSkipped = LoggerMessage.Define<string>(
        LogLevel.Information,
        new EventId(1, nameof(ClaimSkipped)),
        "Claim skipped: another writer changed work item {WorkItemId} first");

    /// <summary>
    /// Claims up to <paramref name="freeSlots"/> items. Work that cannot be run at all is failed inside the
    /// same transaction rather than returned: it is finished, not dispatched.
    /// </summary>
    public async Task<IReadOnlyList<ClaimedWork>> ClaimAsync(JasonDbContext db, int freeSlots, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(db);
        if (freeSlots <= 0)
        {
            return [];
        }

        var now = clock.GetUtcNow().UtcDateTime;
        var current = options.CurrentValue;
        var claimed = new List<ClaimedWork>();

        await using var transaction = await db.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
        foreach (var id in await CandidatesAsync(db, now, freeSlots, ct).ConfigureAwait(false))
        {
            var item = await db.WorkItems.WithNavigation().Include(w => w.Attempts)
                .FirstOrDefaultAsync(w => w.Id == id, ct)
                .ConfigureAwait(false);
            if (item is null || item.Status != WorkItemStatus.Created)
            {
                continue;
            }

            var attempt = Claim(item, current, now, await CommandForAsync(db, item, ct).ConfigureAwait(false));
            db.Attempts.Add(attempt);
            journal.Append(
                db,
                Actors.Dispatcher,
                JournalKinds.WorkItemScheduled,
                campaign: null,
                key: "attempt",
                updated: JsonValue.Create(attempt.Number),
                workItem: item,
                attempt: attempt);
            FailClosed(db, item, attempt);

            try
            {
                await db.SaveChangesAsync(ct).ConfigureAwait(false);
            }
            catch (DbUpdateConcurrencyException)
            {
                ClaimSkipped(logger, item.PublicId, null);
                db.ChangeTracker.Clear();
                continue;
            }

            if (item.Status == WorkItemStatus.Scheduled)
            {
                claimed.Add(new ClaimedWork(item.Id, attempt.Id, item.PublicId, attempt.PublicId));
            }
        }

        await transaction.CommitAsync(ct).ConfigureAwait(false);
        return claimed;
    }

    /// <summary>
    /// One item per campaign, the most important first, and never more than the free slots. Raw SQL because the
    /// per-campaign choice is a window function: doing it in memory would either idle a slot or read the whole
    /// backlog.
    /// </summary>
    private static async Task<List<int>> CandidatesAsync(JasonDbContext db, DateTime now, int freeSlots, CancellationToken ct)
    {
        var created = SnakeCaseEnumConverter<WorkItemStatus>.Format(WorkItemStatus.Created);
        var scheduled = SnakeCaseEnumConverter<WorkItemStatus>.Format(WorkItemStatus.Scheduled);
        var processing = SnakeCaseEnumConverter<WorkItemStatus>.Format(WorkItemStatus.Processing);
        var active = SnakeCaseEnumConverter<CampaignStatus>.Format(CampaignStatus.Active);

        return await db.WorkItems.FromSqlInterpolated($"""
            SELECT * FROM (
              SELECT w.*, ROW_NUMBER() OVER (PARTITION BY w.campaign_id ORDER BY w.priority DESC, w.id ASC) AS rn
              FROM work_items w JOIN campaigns c ON c.id = w.campaign_id
              WHERE w.status = {created} AND c.status = {active}
                AND (w.not_before IS NULL OR w.not_before <= {now})
                AND (w.due_at IS NULL OR w.due_at > {now})
                AND (w.retry_after IS NULL OR w.retry_after <= {now})
                AND NOT EXISTS (SELECT 1 FROM work_items o
                                WHERE o.campaign_id = w.campaign_id AND o.status IN ({scheduled}, {processing})))
            WHERE rn = 1 ORDER BY priority DESC, id ASC LIMIT {freeSlots}
            """)
            .AsNoTracking()
            .Select(w => w.Id)
            .ToListAsync(ct)
            .ConfigureAwait(false);
    }

    private async Task<IReadOnlyList<string>?> CommandForAsync(JasonDbContext db, WorkItem item, CancellationToken ct) =>
        item.Kind == WorkItemKind.AiRole ? await resolver.ResolveAsync(db, item.Role!, ct).ConfigureAwait(false) : null;

    /// <summary>Schedules the item and records the attempt that will run it, snapshot and all.</summary>
    private Attempt Claim(WorkItem item, DispatcherOptions current, DateTime now, IReadOnlyList<string>? command)
    {
        WorkItemTransitions.Apply(item, WorkItemStatus.Scheduled, now);
        var limits = EffectiveLimits.For(item, current);
        var number = item.Attempts.Count + 1;
        var attemptId = PublicId.New("att");
        return new Attempt
        {
            PublicId = attemptId,
            WorkItem = item,
            Number = number,
            Command = item.Kind,
            Status = AttemptStatus.Scheduled,
            ExecutionProfile = item.ExecutionProfile,

            // The context as it stands now: an edit after the claim belongs to the next attempt, not to this one.
            ContextSnapshot = item.Context.DeepClone().AsObject(),
            ClaimedAt = now,
            LockUntil = now.AddSeconds(limits.TimeoutSeconds),
            Launch = command is null ? null : new AttemptLaunchDto(command, paths.AttemptWorkDirectory(item.PublicId, attemptId), null, null),
        };
    }

    /// <summary>
    /// Work the runtime cannot perform fails where it is noticed, with the attempt and its snapshot kept so the
    /// manager can see what would have run. A silent skip would leave the item looking claimable forever.
    /// </summary>
    private void FailClosed(JasonDbContext db, WorkItem item, Attempt attempt)
    {
        if (item.Kind == WorkItemKind.ProviderOp)
        {
            outcomes.Fail(
                db,
                item,
                attempt,
                AttemptErrors.NoRoute,
                $"No provider route exists yet for operation '{item.Operation}'.",
                trace: null,
                details: null,
                Actors.Dispatcher);
        }
        else if (attempt.Launch is null)
        {
            outcomes.Fail(
                db,
                item,
                attempt,
                AttemptErrors.RoleNotLaunchable,
                $"Role '{item.Role}' has no entry command and Roles:DefaultEntryCommand is not configured.",
                trace: null,
                details: null,
                Actors.Dispatcher);
        }
    }
}
