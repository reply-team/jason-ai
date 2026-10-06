using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Jason.Runtime.Tests;

/// <summary>
/// Another writer arriving between a service's look and its insert, by construction rather than by timing: the first
/// time the context is about to save, the given statement runs on a connection of its own and commits, so the save that
/// follows meets what it wrote. Later saves pass untouched.
/// </summary>
public sealed class CompetingInsert(string databaseFile, Func<DbContext, string> statement) : SaveChangesInterceptor
{
    private int _fired;

    public bool Fired => _fired == 1;

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(eventData);
        if (Interlocked.Exchange(ref _fired, 1) == 0)
        {
            using var connection = new SqliteConnection($"Data Source={databaseFile}");
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = statement(eventData.Context!);
            command.ExecuteNonQuery();
        }

        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }
}
