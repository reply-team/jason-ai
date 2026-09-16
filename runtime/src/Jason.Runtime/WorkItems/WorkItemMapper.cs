using Jason.Contracts.Api;
using Jason.Runtime.Persistence;

namespace Jason.Runtime.WorkItems;

/// <summary>
/// The stored work item in the two shapes the API answers with. <c>eligible</c> and <c>current_attempt_id</c>
/// are computed here rather than stored, so they can never be stale.
/// </summary>
public static class WorkItemMapper
{
    /// <param name="attempts">The attempts to include, or null to omit them entirely; ordered newest first here.</param>
    public static WorkItemDto ToDto(WorkItem item, DateTime now, IReadOnlyList<Attempt>? attempts, bool includeSnapshots)
    {
        ArgumentNullException.ThrowIfNull(item);
        return new WorkItemDto(
            item.PublicId,
            item.Campaign!.PublicId,
            item.Contact?.PublicId,
            item.Kind,
            item.Role,
            item.Operation,
            item.ExecutionProfile,
            item.Status,
            WorkItemQueries.IsEligible(item, now),
            item.Priority,
            Utc(item.NotBefore),
            Utc(item.DueAt),
            Utc(item.RetryAfter),
            item.TimeoutSeconds,
            item.HeartbeatSeconds,
            item.MaxAttempts,
            new ActorRef(item.CreatedByType, item.CreatedById),
            item.Context,
            item.ResultFormat,
            item.Result,
            item.AttemptCount,
            WorkItemQueries.LiveAttempt(item)?.PublicId,
            item.LastError,
            attempts is null ? null : [.. attempts.OrderByDescending(a => a.Number).Select(a => ToDto(a, includeSnapshots))],
            Utc(item.CreatedAt),
            Utc(item.UpdatedAt),
            Utc(item.FinishedAt));
    }

    public static WorkItemSummaryDto ToSummary(WorkItem item, DateTime now)
    {
        ArgumentNullException.ThrowIfNull(item);
        return new WorkItemSummaryDto(
            item.PublicId,
            item.Campaign!.PublicId,
            item.Contact?.PublicId,
            item.Kind,
            item.Role,
            item.Operation,
            item.ExecutionProfile,
            item.Status,
            WorkItemQueries.IsEligible(item, now),
            item.Priority,
            Utc(item.NotBefore),
            Utc(item.DueAt),
            Utc(item.RetryAfter),
            new ActorRef(item.CreatedByType, item.CreatedById),
            item.AttemptCount,
            WorkItemQueries.LiveAttempt(item)?.PublicId,
            item.LastError,
            Utc(item.CreatedAt),
            Utc(item.UpdatedAt),
            Utc(item.FinishedAt));
    }

    public static AttemptDto ToDto(Attempt attempt, bool includeSnapshot)
    {
        ArgumentNullException.ThrowIfNull(attempt);
        return new AttemptDto(
            attempt.PublicId,
            attempt.Number,
            attempt.Status,
            attempt.Command,
            attempt.ExecutionProfile,
            attempt.Error,
            attempt.Launch,
            Utc(attempt.ClaimedAt),
            Utc(attempt.StartedAt),
            Utc(attempt.FinishedAt),
            Utc(attempt.LastHeartbeatAt),
            Utc(attempt.LockUntil),

            // Ten attempts of a 256 KiB context is more than any default read should carry.
            includeSnapshot ? attempt.ContextSnapshot : null,

            // Provenance is small, it is the point of asking, and an agent attempt has none: always sent.
            attempt.Provenance);
    }

    /// <summary>Everything in the database is UTC; SQLite hands the kind back unset.</summary>
    public static DateTimeOffset Utc(DateTime value) => new(DateTime.SpecifyKind(value, DateTimeKind.Utc));

    public static DateTimeOffset? Utc(DateTime? value) => value is { } moment ? Utc(moment) : null;
}
