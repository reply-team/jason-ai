using Jason.Contracts.Api;
using Jason.Runtime.Persistence;

namespace Jason.Runtime.Audiences;

/// <summary>
/// The reads of the searches a campaign's audiences are filled by, each written once, here, so the step, the claim and
/// the landing read a search the same way and a test pins the plan of each.
/// </summary>
internal static class AudienceSearches
{
    /// <summary>The statuses in which work of a search is still under way, and the poll waits for it.</summary>
    public static readonly WorkItemStatus[] Open =
    [
        WorkItemStatus.Created,
        WorkItemStatus.Scheduled,
        WorkItemStatus.Processing,
        WorkItemStatus.AwaitingApproval,
        WorkItemStatus.Waiting,
    ];

    /// <summary>The searches still followed, of the campaigns that run: off the index that holds only those.</summary>
    public static IQueryable<AudienceSearch> InFlight(JasonDbContext db) =>
        db.AudienceSearches.Where(search => search.EndedAs == null && search.Campaign!.Status == CampaignStatus.Active);

    /// <summary>The row of the search a campaign's work names by its work item, in that campaign alone.</summary>
    public static IQueryable<AudienceSearch> Named(JasonDbContext db, int campaignId, string searchWorkItem) =>
        db.AudienceSearches.Where(search => search.CampaignId == campaignId && search.SearchWorkItem!.PublicId == searchWorkItem);

    /// <summary>The operations that act on a search by its work item: a read of its people and a stop.</summary>
    private static readonly string[] OfASearch = [SearchLanding.Status, SearchLanding.Stop];

    /// <summary>
    /// The reads and stops of searches still under way in these campaigns, whoever made them: a campaign's open work by
    /// status.
    /// </summary>
    public static IQueryable<WorkItem> OpenWork(JasonDbContext db, IReadOnlyCollection<int> campaigns) =>
        db.WorkItems.Where(item => campaigns.Contains(item.CampaignId) && Open.Contains(item.Status) && OfASearch.Contains(item.Operation));
}
