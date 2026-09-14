using Jason.Contracts.Api;
using Jason.Runtime.Domain;
using Jason.Runtime.Execution;
using Jason.Runtime.Persistence;
using Jason.Runtime.WorkItems;
using Microsoft.EntityFrameworkCore;

namespace Jason.Runtime.Dispatch;

/// <summary>
/// What a restart finds. An item that was claimed but never started is given straight back — nobody ran it, so
/// nothing was spent and the attempt is not counted. An item that was already <c>processing</c> is left alone:
/// its executor may well still be alive, and the lease is the only honest way to find out.
/// </summary>
public sealed class StartupRecovery(AttemptOutcomes outcomes)
{
    public async Task<int> RunAsync(JasonDbContext db, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(db);
        var interrupted = await db.WorkItems
            .Where(w => w.Status == WorkItemStatus.Scheduled)
            .Include(w => w.Attempts)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var released = 0;
        foreach (var item in interrupted)
        {
            if (WorkItemQueries.LiveAttempt(item) is not { } attempt)
            {
                continue;
            }

            outcomes.Interrupt(db, item, attempt, Actors.Dispatcher);
            released++;
        }

        if (released > 0)
        {
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
        }

        return released;
    }
}
