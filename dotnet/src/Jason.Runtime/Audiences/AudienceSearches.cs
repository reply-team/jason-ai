using Jason.Contracts.Api;
using Jason.Runtime.Persistence;
using Microsoft.EntityFrameworkCore;

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

    /// <summary>
    /// The work item of a search of the campaign that has not ended, other than <paramref name="itemId"/>: a row still
    /// followed — running, or held at the provider — the oldest first, off the index that holds only those; or else a
    /// search of the campaign running now. Null where there is none.
    /// </summary>
    public static async Task<string?> InTheWayAsync(JasonDbContext db, int campaignId, int itemId, CancellationToken ct) =>
        await Unended(db, campaignId, itemId).FirstOrDefaultAsync(ct).ConfigureAwait(false)
        ?? await Starting(db, campaignId, itemId).FirstOrDefaultAsync(ct).ConfigureAwait(false);

    /// <summary>The work items of a campaign's searches whose rows have not ended, other than <paramref name="itemId"/>'s, the oldest first.</summary>
    internal static IQueryable<string> Unended(JasonDbContext db, int campaignId, int itemId) =>
        db.AudienceSearches
            .Where(search => search.CampaignId == campaignId && search.EndedAs == null && search.SearchWorkItemId != itemId)
            .OrderBy(search => search.Id)
            .Select(search => search.SearchWorkItem!.PublicId);

    /// <summary>A campaign's search items running now, other than <paramref name="itemId"/>: started, with no row yet.</summary>
    internal static IQueryable<string> Starting(JasonDbContext db, int campaignId, int itemId) =>
        db.WorkItems
            .Where(item => item.CampaignId == campaignId && item.Id != itemId && item.Operation == SearchLanding.Search
                && (item.Status == WorkItemStatus.Scheduled || item.Status == WorkItemStatus.Processing))
            .Select(item => item.PublicId);

    /// <summary>
    /// Every search still followed that the provider gave a reference to, of any campaign, an archived one's included, with
    /// its campaign's state and the plugin and the binding its succeeded attempt ran under: what a route change has to leave
    /// a route to. One with no reference is read and stopped nowhere, so no route keeps it. Off the index that holds only
    /// the searches still followed.
    /// </summary>
    public static IQueryable<FollowedSearch> Followed(JasonDbContext db) =>
        db.AudienceSearches
            .Where(search => search.EndedAs == null && search.Reference != null)
            .OrderBy(search => search.CampaignId)
            .ThenBy(search => search.Id)
            .Select(search => new FollowedSearch(
                search.Campaign!.PublicId,
                search.Campaign.Status,
                search.SearchWorkItem!.PublicId,
                search.PluginId,
                search.SearchWorkItem.Attempts
                    .Where(attempt => attempt.Status == AttemptStatus.Succeeded)
                    .OrderByDescending(attempt => attempt.Number)
                    .Select(attempt => attempt.Provenance)
                    .FirstOrDefault()));

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

/// <summary>
/// A search still followed: its campaign and the campaign's state, its work item, the plugin its row names, and its succeeded
/// attempt's provenance.
/// </summary>
internal sealed record FollowedSearch(string Campaign, CampaignStatus Status, string WorkItem, string PluginId, AttemptProvenanceDto? Ran);
