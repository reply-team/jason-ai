using System.Text.Json;
using Jason.Contracts.Api;
using Jason.Contracts.Execution;
using Jason.Contracts.Json;
using Jason.Runtime.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Jason.Runtime.Dispatch;

/// <summary>
/// What a role's run is told about the wait before it, composed at the claim from rows: the item's latest wait that an
/// attempt ended in, when that wait was released — the attempt that waited, why the wait ended, and how each item it named
/// stands now. A wait with no attempt of its own, which is what a limit the claim met holds an item in, is passed over: it
/// was no run's, so it has nothing to tell one, and the run after it follows the wait before it. A run that follows no
/// wait, or whose item's latest such wait closed some other way, is told nothing, however many waits were released before
/// it; the number counts only released waits an attempt ended in. The first attempt the claim hands out after a release
/// is recorded on the wait as the one it resumed, once; an attempt that retries after it is told the same, because the
/// wait it follows is still the last one.
/// </summary>
public static class Resumptions
{
    public static async Task<Resumption?> ForClaimAsync(JasonDbContext db, WorkItem item, Attempt attempt, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(attempt);
        if (item.Kind != WorkItemKind.AiRole)
        {
            return null;
        }

        var wait = await db.WorkItemWaits
            .Include(w => w.Attempt)
            .Include(w => w.Items)
            .Where(w => w.WorkItemId == item.Id && w.AttemptId != null)
            .OrderByDescending(w => w.Id)
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);
        if (wait is not { Status: WaitStatus.Released } || wait.Attempt is null || wait.ReleaseReason is not { } because)
        {
            return null;
        }

        wait.ResumedByAttempt ??= attempt;
        var number = await db.WorkItemWaits
            .CountAsync(
                w => w.WorkItemId == item.Id && w.AttemptId != null && w.Status == WaitStatus.Released && w.Id <= wait.Id,
                ct)
            .ConfigureAwait(false);

        return new Resumption(
            wait.Attempt.PublicId,
            number,
            new DateTimeOffset(DateTime.SpecifyKind(wait.OpenedAt, DateTimeKind.Utc)),
            because,
            await ItemsAsync(db, wait, ct).ConfigureAwait(false));
    }

    /// <summary>
    /// The named items in the order the wait named them. Everything but the results is read in one query; the results are
    /// read one at a time, in that order, while they fit, so a wait on a hundred large results costs the 1 MiB and no more.
    /// </summary>
    private static async Task<List<ResumedItem>> ItemsAsync(JasonDbContext db, WorkItemWait wait, CancellationToken ct)
    {
        var ids = wait.Items.OrderBy(i => i.Position).Select(i => i.ChildWorkItemId).ToList();
        if (ids.Count == 0)
        {
            return [];
        }

        var rows = await db.WorkItems
            .AsNoTracking()
            .Where(w => ids.Contains(w.Id))
            .Select(w => new
            {
                w.Id,
                w.PublicId,
                w.Kind,
                w.Role,
                w.Operation,
                w.Status,
                w.FinishedAt,
                w.LastError,
                HasResult = w.Result != null,
            })
            .ToDictionaryAsync(w => w.Id, ct)
            .ConfigureAwait(false);

        var items = new List<ResumedItem>(ids.Count);
        var spent = 0;
        var fits = true;
        foreach (var id in ids)
        {
            var row = rows[id];
            var result = row.HasResult && fits
                ? await db.WorkItems.AsNoTracking().Where(w => w.Id == id).Select(w => w.Result).FirstAsync(ct).ConfigureAwait(false)
                : null;
            if (result is not null)
            {
                spent += JsonSerializer.SerializeToUtf8Bytes(result, JasonJson.Options).Length;
                fits = spent <= Resumption.MaxResultBytes;
                if (!fits)
                {
                    result = null;
                }
            }

            items.Add(new ResumedItem(
                row.PublicId,
                row.Kind,
                row.Role,
                row.Operation,
                row.Status,
                row.FinishedAt is { } finished ? new DateTimeOffset(DateTime.SpecifyKind(finished, DateTimeKind.Utc)) : null,
                row.LastError?.Code,
                result,
                ResultOmitted: row.HasResult && result is null));
        }

        return items;
    }
}
