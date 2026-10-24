using Jason.Contracts.Api;
using Jason.Runtime.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Jason.Runtime.WorkItems;

/// <summary>
/// The <c>wait</c> block a waiting item is read with: what it waits for, how each named item stands now, and until when.
/// Identifiers, statuses and moments only, read from rows, so a reader learns where the wait is without being told what
/// any of the work is about.
/// </summary>
public static class WaitBlocks
{
    /// <summary>The statuses a named item has ended in, as the scan reads them when it decides a wait is over.</summary>
    private static readonly WorkItemStatus[] Ended = [.. WorkItemTransitions.Terminal];

    /// <summary>The block of a wait just written, from the items it was written with, in the order they were named.</summary>
    public static WaitDto Of(WorkItemWait wait, string? attemptId, IReadOnlyList<WorkItem> named)
    {
        ArgumentNullException.ThrowIfNull(wait);
        ArgumentNullException.ThrowIfNull(named);
        return new WaitDto(
            attemptId,
            [.. named.Select(child => new WaitItemDto(child.PublicId, child.Status))],
            wait.Mode,
            WorkItemMapper.Utc(wait.Until),
            WorkItemMapper.Utc(wait.Deadline),
            WorkItemMapper.Utc(wait.OpenedAt));
    }

    /// <summary>
    /// The listing's blocks of the open waits of a page of items, by item, in one query: the wait's own fields, how many
    /// items it names and how many of them have ended, counted by the database, none of the items themselves.
    /// </summary>
    public static async Task<Dictionary<int, WaitSummaryDto>> SummariesForAsync(JasonDbContext db, IReadOnlyCollection<int> workItemIds, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(workItemIds);
        if (workItemIds.Count == 0)
        {
            return [];
        }

        var rows = await db.WorkItemWaits
            .AsNoTracking()
            .Where(w => workItemIds.Contains(w.WorkItemId) && w.Status == WaitStatus.Open)
            .Select(w => new
            {
                w.WorkItemId,
                AttemptId = w.Attempt == null ? null : w.Attempt.PublicId,
                w.Mode,
                w.Until,
                w.Deadline,
                w.OpenedAt,
                OnCount = w.Items.Count,
                EndedCount = w.Items.Count(i => Ended.Contains(i.ChildWorkItem!.Status)),
            })
            .ToListAsync(ct)
            .ConfigureAwait(false);

        return rows.ToDictionary(
            row => row.WorkItemId,
            row => new WaitSummaryDto(
                row.AttemptId,
                row.Mode,
                WorkItemMapper.Utc(row.Until),
                WorkItemMapper.Utc(row.Deadline),
                WorkItemMapper.Utc(row.OpenedAt),
                row.OnCount,
                row.EndedCount));
    }

    /// <summary>
    /// The blocks of the open waits of a batch of items, by item, in one query: each named item's id and status only,
    /// never its context or its result, so a page of waiting work costs a read of identifiers.
    /// </summary>
    public static async Task<Dictionary<int, WaitDto>> ForAsync(JasonDbContext db, IReadOnlyCollection<int> workItemIds, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(workItemIds);
        if (workItemIds.Count == 0)
        {
            return [];
        }

        var rows = await db.WorkItemWaits
            .AsNoTracking()
            .Where(w => workItemIds.Contains(w.WorkItemId) && w.Status == WaitStatus.Open)
            .Select(w => new
            {
                w.WorkItemId,
                AttemptId = w.Attempt == null ? null : w.Attempt.PublicId,
                w.Mode,
                w.Until,
                w.Deadline,
                w.OpenedAt,
                Named = w.Items
                    .OrderBy(i => i.Position)
                    .Select(i => new { i.ChildWorkItem!.PublicId, i.ChildWorkItem.Status })
                    .ToList(),
            })
            .ToListAsync(ct)
            .ConfigureAwait(false);

        return rows.ToDictionary(
            row => row.WorkItemId,
            row => new WaitDto(
                row.AttemptId,
                [.. row.Named.Select(child => new WaitItemDto(child.PublicId, child.Status))],
                row.Mode,
                WorkItemMapper.Utc(row.Until),
                WorkItemMapper.Utc(row.Deadline),
                WorkItemMapper.Utc(row.OpenedAt)));
    }
}
