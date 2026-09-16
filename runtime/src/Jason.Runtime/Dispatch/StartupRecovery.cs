using Jason.Contracts.Api;
using Jason.Runtime.Domain;
using Jason.Runtime.Execution;
using Jason.Runtime.Persistence;
using Jason.Runtime.WorkItems;
using Microsoft.EntityFrameworkCore;

namespace Jason.Runtime.Dispatch;

/// <summary>
/// What a restart finds. An item that was claimed but never started is given straight back whatever kind of
/// work it is — the handler commits the item <c>processing</c> and the attempt started before it launches
/// anything, so an attempt still holding no start ran nothing, asked nobody and is not counted. Past that
/// commit the honest answer is A1's: an end nobody answered for is ambiguous, after which the operation's own
/// contract says whether it may be attempted again. An item that was already <c>processing</c> is left alone
/// either way: its executor may well still be alive, and the lease is the only honest way to find out.
/// </summary>
public sealed class StartupRecovery(AttemptOutcomes outcomes, UnansweredEnd unanswered)
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

            // The claim is not the launch: the handler commits the item processing and the attempt started
            // before it runs anything, so an attempt still holding no start ran nothing and asked nobody.
            if (attempt.StartedAt is not null && unanswered.Verdict(item) is { } end)
            {
                outcomes.Fail(
                    db,
                    item,
                    attempt,
                    AttemptErrors.Interrupted,
                    "A restart found this attempt still out, and nothing answered for it.",
                    trace: null,
                    details: null,
                    Actors.Dispatcher,
                    failureClass: end.Class,
                    retriable: end.Retriable);
            }
            else
            {
                outcomes.Interrupt(db, item, attempt, Actors.Dispatcher);
            }

            released++;
        }

        if (released > 0)
        {
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
        }

        return released;
    }
}
