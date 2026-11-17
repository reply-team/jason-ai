using System.Reflection;
using Jason.Contracts.Discovery;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Jason.Runtime.Persistence;

/// <summary>
/// The guard against a database a newer Jason migrated: applied migrations this build does not know. EF Core passes over a
/// history row it has no migration for, so without this an older build would start and run its own model over the newer
/// schema. The runtime asks it first of all, beside the layout stamp, and refuses the start with <see cref="DataNewer.Code"/>.
/// </summary>
/// <remarks>
/// <para>
/// It only reads, and leaves no file behind. A read-only SQLite connection to a database in write-ahead-log mode makes the
/// log and its shared-memory file where they are not there, and leaves them. So where there is no log beside the database,
/// which is how the last connection to close leaves it, every committed row is in the main file and it is read as an
/// immutable file, which makes nothing and takes no lock. Where a log is there, a runtime holds the database or one ended
/// without closing it, and the log may hold rows the main file does not: it is read through a read-only connection, which
/// reads the log and uses the files already there. Either connection is unpooled and closed before the answer, so a refused
/// start leaves every byte of the data directory as it found it: no lock, no log, no folder, no backup, no sidecar.
/// </para>
/// <para>
/// What it cannot read it does not refuse: no database is a new installation; a database without a history is migrated from
/// the start; and a file SQLite cannot open, or one that is busy, is left to the migrator, which says what it says today. The
/// guard adds a refusal and takes nothing away.
/// </para>
/// </remarks>
public static class NewerDatabase
{
    private const string History = "__EFMigrationsHistory";

    /// <summary>The migrations this build applies, by id, as EF Core finds them: every migration of the runtime's context.</summary>
    public static IReadOnlySet<string> Known { get; } = typeof(JasonDbContext).Assembly.GetTypes()
        .Where(type => type.GetCustomAttribute<DbContextAttribute>()?.ContextType == typeof(JasonDbContext))
        .Select(type => type.GetCustomAttribute<MigrationAttribute>()?.Id)
        .OfType<string>()
        .ToHashSet(StringComparer.Ordinal);

    /// <summary>
    /// The refusal of a start on <paramref name="paths"/>, said for a Jason of version <paramref name="current"/>, where the
    /// database holds a migration this build does not know; null where the start goes on.
    /// </summary>
    public static (string Code, string Message)? Refusal(JasonPaths paths, string current)
    {
        ArgumentNullException.ThrowIfNull(paths);
        return Unknown(paths.DatabaseFile) is { Count: > 0 } ? (DataNewer.Code, DataNewer.Sentence(current, paths.Root)) : null;
    }

    /// <summary>
    /// The applied migrations of the database at <paramref name="databaseFile"/> this build does not know, in the order of their ids,
    /// which begin with the moment each was made: empty where it knows every one, or the database has no history; null where there is no database or it
    /// could not be read.
    /// </summary>
    public static IReadOnlyList<string>? Unknown(string databaseFile)
    {
        ArgumentNullException.ThrowIfNull(databaseFile);
        if (!File.Exists(databaseFile))
        {
            return null;
        }

        // A runtime that begins writing between this look and the open below holds the instance lock this start would then
        // be refused by; and a page it changes under an immutable read fails the read, which is left to the migrator.
        var logged = File.Exists(databaseFile + "-wal");
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = logged ? databaseFile : new Uri(Path.GetFullPath(databaseFile)).AbsoluteUri + "?immutable=1",
            Mode = SqliteOpenMode.ReadOnly,
            Pooling = false,
        }.ToString();

        try
        {
            using var connection = new SqliteConnection(connectionString);
            connection.Open();
            using (var exists = connection.CreateCommand())
            {
                exists.CommandText = "SELECT count(*) FROM sqlite_master WHERE type = 'table' AND name = $name;";
                exists.Parameters.AddWithValue("$name", History);
                if (Convert.ToInt64(exists.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture) == 0)
                {
                    return [];
                }
            }

            using var applied = connection.CreateCommand();
            applied.CommandText = $"SELECT \"MigrationId\" FROM \"{History}\" ORDER BY \"MigrationId\";";
            using var reader = applied.ExecuteReader();
            var unknown = new List<string>();
            while (reader.Read())
            {
                var id = reader.GetString(0);
                if (!Known.Contains(id))
                {
                    unknown.Add(id);
                }
            }

            return unknown;
        }
        catch (Exception unread) when (unread is SqliteException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
