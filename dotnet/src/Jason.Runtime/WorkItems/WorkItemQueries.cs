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

    /// <summary>
    /// The operations that open or carry on a line with a person, as their contracts declare it: a paused person's are not
    /// candidates. Matched against the operation column, as the campaign's rule is.
    /// </summary>
    private static readonly string[] Touches = [.. OperationCatalog.Touches];

    /// <summary>
    /// The predicate. Its last campaign rule is a person's: a touch of a person whose membership in the item's campaign is
    /// <c>paused</c> waits, as a paused campaign's work does, with nothing written. In memory it reads the person's
    /// memberships, which <see cref="WithNavigation"/> loads, the held ones alone; a person not loaded holds nothing there.
    /// </summary>
    private static readonly Expression<Func<WorkItem, DateTime, bool>> EligibleAt =
        (w, now) => w.Status == WorkItemStatus.Created
            && (w.Campaign!.Status == CampaignStatus.Active
                || (w.Campaign!.Status == CampaignStatus.Paused
                    && w.Operation != null
                    && Enumerable.Contains(RunsInPausedCampaign, w.Operation))
                || (w.Campaign!.Status == CampaignStatus.Archived
                    && w.Operation != null
                    && Enumerable.Contains(RunsInArchivedCampaign, w.Operation)))
            && !(w.Operation != null
                && Enumerable.Contains(Touches, w.Operation)
                && w.Contact != null
                && w.Contact.Memberships.Any(m => m.CampaignId == w.CampaignId && m.State == MembershipState.Paused))
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

    /// <summary>
    /// Everything the mapper needs: an item without its campaign cannot say whether it is eligible, nor one without its
    /// person's held memberships whether its touch waits. Only the held ones are loaded: a person is held in few campaigns.
    /// A filtered include cannot name the item's own campaign, so the held memberships of every campaign come; and where a
    /// caller loads the attempts too, the item carries two collections, so the query is split, one statement per
    /// collection, rather than one statement multiplying attempts by memberships, and EF Core has nothing to warn of.
    /// </summary>
    public static IQueryable<WorkItem> WithNavigation(this IQueryable<WorkItem> query)
    {
        ArgumentNullException.ThrowIfNull(query);
        return query
            .AsSplitQuery()
            .Include(w => w.Campaign)
            .Include(w => w.Contact)
            .ThenInclude(c => c!.Memberships.Where(m => m.State == MembershipState.Paused));
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
