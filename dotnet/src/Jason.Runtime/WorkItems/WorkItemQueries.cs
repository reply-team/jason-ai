using System.Linq.Expressions;
using Jason.Contracts.Api;
using Jason.Contracts.Operations;
using Jason.Runtime.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Jason.Runtime.WorkItems;

/// <summary>
/// The one eligible predicate. A listing filters by it in SQL and the mapper answers with it in memory, from this one
/// expression. The dispatcher's claim writes the same rule in its own SQL, because its per-campaign pick is a window
/// function, and a test holds the three readers to the same answer in every campaign state.
/// </summary>
public static class WorkItemQueries
{
    /// <summary>
    /// The operations whose work runs in a paused campaign, as their contracts declare it. Matched against the item's
    /// operation column, never its context: the dispatcher still reads nothing of what a work item means.
    /// </summary>
    private static readonly string[] RunsInPausedCampaign = [.. OperationCatalog.RunsInPausedCampaign];

    /// <summary>The operations whose work runs in an archived campaign, as their contracts declare it.</summary>
    private static readonly string[] RunsInArchivedCampaign = [.. OperationCatalog.RunsInArchivedCampaign];

    private static readonly Expression<Func<WorkItem, DateTime, bool>> EligibleAt =
        (w, now) => w.Status == WorkItemStatus.Created
            && (w.Campaign!.Status == CampaignStatus.Active
                || (w.Campaign!.Status == CampaignStatus.Paused
                    && w.Operation != null
                    && Enumerable.Contains(RunsInPausedCampaign, w.Operation))
                || (w.Campaign!.Status == CampaignStatus.Archived
                    && w.Operation != null
                    && Enumerable.Contains(RunsInArchivedCampaign, w.Operation)))
            && (w.NotBefore == null || w.NotBefore <= now)
            && (w.DueAt == null || w.DueAt > now)
            && (w.RetryAfter == null || w.RetryAfter <= now);

    private static readonly Func<WorkItem, DateTime, bool> Compiled = EligibleAt.Compile();

    /// <summary>The predicate as a query, with the moment baked in.</summary>
    public static Expression<Func<WorkItem, bool>> Eligible(DateTime now) =>
        Expression.Lambda<Func<WorkItem, bool>>(
            new ParameterReplacer(EligibleAt.Parameters[1], Expression.Constant(now)).Visit(EligibleAt.Body),
            EligibleAt.Parameters[0]);

    /// <summary>The same predicate in memory; the item's campaign has to be loaded.</summary>
    public static bool IsEligible(WorkItem item, DateTime now) => Compiled(item, now);

    /// <summary>Everything the mapper needs: an item without its campaign cannot say whether it is eligible.</summary>
    public static IQueryable<WorkItem> WithNavigation(this IQueryable<WorkItem> query)
    {
        ArgumentNullException.ThrowIfNull(query);
        return query.Include(w => w.Campaign).Include(w => w.Contact);
    }

    /// <summary>The scheduled or running attempt, of which the database allows at most one.</summary>
    public static Attempt? LiveAttempt(WorkItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        return item.Attempts.FirstOrDefault(a => a.Status is AttemptStatus.Scheduled or AttemptStatus.Running);
    }

    private sealed class ParameterReplacer(ParameterExpression parameter, Expression replacement) : ExpressionVisitor
    {
        protected override Expression VisitParameter(ParameterExpression node) => node == parameter ? replacement : node;
    }
}
