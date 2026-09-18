using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Jason.Runtime.Persistence;

/// <summary>
/// Second line of defence after the database triggers, for the three tables nothing may rewrite: the runtime
/// itself never updates or deletes a journal entry, an admitted report, or an execution-profile revision.
/// </summary>
public sealed class AppendOnlyInterceptor : SaveChangesInterceptor
{
    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        ArgumentNullException.ThrowIfNull(eventData);
        Check(eventData.Context);
        return result;
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(eventData);
        Check(eventData.Context);
        return ValueTask.FromResult(result);
    }

    private static void Check(DbContext? context)
    {
        if (context is null)
        {
            return;
        }

        if (Touched<JournalEntry>(context))
        {
            throw new InvalidOperationException("Journal entries are append-only; they are never updated or deleted.");
        }

        if (Touched<Report>(context))
        {
            throw new InvalidOperationException("An admitted report is immutable; it is never updated or deleted.");
        }

        if (Touched<ExecutionProfileRevision>(context))
        {
            throw new InvalidOperationException("An execution-profile revision is immutable; an edit appends a new one.");
        }
    }

    private static bool Touched<TEntity>(DbContext context)
        where TEntity : class =>
        context.ChangeTracker.Entries<TEntity>().Any(e => e.State is EntityState.Modified or EntityState.Deleted);
}
