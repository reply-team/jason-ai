using Jason.Contracts.Api;
using Jason.Runtime.Journal;
using Jason.Runtime.Persistence;

namespace Jason.Runtime.WorkItems;

/// <summary>
/// The one table that decides how a work item may move. Nothing else in the runtime judges a transition: a
/// service checks the caller-facing rules first (is this finished? is the caller allowed?) and then asks here,
/// so an illegal pair reaching <see cref="Apply"/> is a bug rather than a bad request.
/// </summary>
public static class WorkItemTransitions
{
    private static readonly Dictionary<WorkItemStatus, WorkItemStatus[]> Legal = new()
    {
        [WorkItemStatus.Created] = [WorkItemStatus.Scheduled, WorkItemStatus.Cancelled, WorkItemStatus.Expired],
        [WorkItemStatus.Scheduled] = [WorkItemStatus.Processing, WorkItemStatus.Created, WorkItemStatus.Cancelled, WorkItemStatus.Failed],
        [WorkItemStatus.Processing] = [WorkItemStatus.Succeeded, WorkItemStatus.Failed, WorkItemStatus.Created, WorkItemStatus.Cancelled],
        [WorkItemStatus.Expired] = [WorkItemStatus.Created, WorkItemStatus.Cancelled],
    };

    /// <summary>Succeeded, failed and cancelled items are never left again.</summary>
    public static IReadOnlySet<WorkItemStatus> Final { get; } =
        new HashSet<WorkItemStatus> { WorkItemStatus.Succeeded, WorkItemStatus.Failed, WorkItemStatus.Cancelled };

    /// <summary>Finished: a finish time is set, the result is frozen and no attempt is live. Expired is finished but reopenable.</summary>
    public static IReadOnlySet<WorkItemStatus> Terminal { get; } =
        new HashSet<WorkItemStatus> { WorkItemStatus.Succeeded, WorkItemStatus.Failed, WorkItemStatus.Cancelled, WorkItemStatus.Expired };

    public static bool IsLegal(WorkItemStatus from, WorkItemStatus to) =>
        Legal.TryGetValue(from, out var targets) && Array.IndexOf(targets, to) >= 0;

    /// <summary>
    /// Applies a legal transition and nothing else: status and update time always, the finish time when the
    /// item becomes finished or stops being expired, and the dispatcher's retry moment when it no longer means
    /// anything. Throws on an illegal pair.
    /// </summary>
    public static void Apply(WorkItem item, WorkItemStatus to, DateTime now)
    {
        ArgumentNullException.ThrowIfNull(item);
        var from = item.Status;
        if (!IsLegal(from, to))
        {
            throw new InvalidOperationException($"A work item cannot go from {from} to {to}.");
        }

        item.Status = to;
        item.UpdatedAt = now;

        if (Terminal.Contains(to))
        {
            item.FinishedAt = now;
        }
        else if (from == WorkItemStatus.Expired)
        {
            item.FinishedAt = null;
        }

        if (to is WorkItemStatus.Scheduled or WorkItemStatus.Cancelled)
        {
            item.RetryAfter = null;
        }
    }

    /// <summary>The reserved journal kind that records the transition.</summary>
    public static string JournalKind(WorkItemStatus from, WorkItemStatus to) => to switch
    {
        WorkItemStatus.Scheduled => JournalKinds.WorkItemScheduled,
        WorkItemStatus.Processing => JournalKinds.WorkItemProcessing,
        WorkItemStatus.Succeeded => JournalKinds.WorkItemSucceeded,
        WorkItemStatus.Failed => JournalKinds.WorkItemFailed,
        WorkItemStatus.Cancelled => JournalKinds.WorkItemCancelled,
        WorkItemStatus.Expired => JournalKinds.WorkItemExpired,
        WorkItemStatus.Created when from == WorkItemStatus.Expired => JournalKinds.WorkItemReopened,
        WorkItemStatus.Created => JournalKinds.WorkItemReleased,
        _ => throw new InvalidOperationException($"No journal kind records {from} to {to}."),
    };
}
