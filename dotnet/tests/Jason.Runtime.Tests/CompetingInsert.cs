using System.Data.Common;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Jason.Runtime.Tests;

/// <summary>
/// Another writer arriving between a service's look and its insert, by construction rather than by timing: the first
/// time the context is about to begin a transaction or to save, whichever comes first, with the statement ready — one
/// that answers null is not ready yet, as one made from the row being inserted is not before that row is added — the
/// statement runs on a connection of its own and commits, so the save that follows meets what it wrote. Later ones pass
/// untouched. A service
/// that writes inside a transaction of its own holds the database from its first statement, so the other writer arrives
/// before that transaction begins, which is where the window between the look and the insert then closes.
/// </summary>
public sealed class CompetingInsert(string databaseFile, Func<DbContext, string?> statement) : SaveChangesInterceptor, IDbTransactionInterceptor
{
    private int _fired;

    public bool Fired => _fired == 1;

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(eventData);
        Fire(eventData.Context!);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    public ValueTask<InterceptionResult<DbTransaction>> TransactionStartingAsync(
        DbConnection connection,
        TransactionStartingEventData eventData,
        InterceptionResult<DbTransaction> result,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(eventData);
        Fire(eventData.Context!);
        return ValueTask.FromResult(result);
    }

    private void Fire(DbContext context)
    {
        if (_fired == 0 && statement(context) is { } ready && Interlocked.Exchange(ref _fired, 1) == 0)
        {
            using var connection = new SqliteConnection($"Data Source={databaseFile}");
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = ready;
            command.ExecuteNonQuery();
        }
    }
}
