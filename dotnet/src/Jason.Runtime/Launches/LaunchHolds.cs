using Jason.Contracts.Api;
using Jason.Runtime.Execution;
using Jason.Runtime.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Jason.Runtime.Launches;

/// <summary>
/// The items that wait on a daily limit of launches: a wait with no attempt and no block, open on a waiting item whose
/// last error is the hold's, under either word. One definition, which a lift lets go, a read of the limits counts and a
/// notification says. The error is a converted column, so the word is read in memory rather than in the query.
/// </summary>
public static class LaunchHolds
{
    /// <summary>The open waits a hold at a daily limit could be, before the word of the error is read.</summary>
    public static IQueryable<WorkItemWait> Waits(JasonDbContext db)
    {
        ArgumentNullException.ThrowIfNull(db);
        return db.WorkItemWaits.Where(w => w.Status == WaitStatus.Open && w.AttemptId == null && w.ProviderBlockId == null
            && w.WorkItem!.Status == WorkItemStatus.Waiting);
    }

    /// <summary>How many items wait on a daily limit now, in every campaign, or in the one <paramref name="campaignId"/> names.</summary>
    public static async Task<int> CountAsync(JasonDbContext db, int? campaignId, CancellationToken ct)
    {
        var waits = Waits(db).AsNoTracking();
        if (campaignId is { } campaign)
        {
            waits = waits.Where(w => w.WorkItem!.CampaignId == campaign);
        }

        var errors = await waits.Select(w => w.WorkItem!.LastError).ToListAsync(ct).ConfigureAwait(false);
        return errors.Count(error => AttemptErrors.IsDailyHold(error?.Code));
    }
}
