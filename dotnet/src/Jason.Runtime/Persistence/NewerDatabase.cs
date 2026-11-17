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
/// It only reads, and adds or removes no file. A read-only SQLite connection to a database in write-ahead-log mode makes the
/// log and its shared-memory file where they are not there, and leaves them. So where there is no log beside the database,
/// which is how the last connection to close leaves it, every committed row is in the main file and it is read as an
/// immutable file, which makes nothing, changes nothing and takes no lock. Where a log is there, a runtime holds the database
/// or one ended without closing it, and the log may hold rows the main file does not: it is read through a read-only
/// connection, which reads the log and uses the files already there. That connection does not move the log into the
/// database, even when it is the last to close, so the database and the log keep every byte; SQLite may rebuild the
/// shared-memory file beside them (<c>jason.db-shm</c>), its index of the log, and that file alone can change. Either
/// connection is unpooled and closed before the answer, so a refused start leaves no lock, no log, no folder, no backup and
/// no sidecar, and the database and its log as it found them.
/// </para>
/// <para>
/// What it cannot read it does not refuse: no database is a new installation; a database without a history is migrated from
/// the start; and anything else that goes wrong on the way, a file SQLite cannot open, one that is busy, a path it cannot
/// address, a history row it cannot read or a build whose own migrations cannot be listed, is left to the start as it was
/// before the guard, whose migrator says what it says today. The guard adds a refusal and takes nothing away.
/// </para>
/// </remarks>
public static class NewerDatabase
{
    private const string History = "__EFMigrationsHistory";

    /// <summary>
    /// How long a read waits on a database another connection holds, in seconds: a busy database is left to the start, which
    /// the instance lock refuses where a runtime holds it, rather than held for the half minute a command waits by default.
    /// </summary>
    internal const int BusyTimeoutSeconds = 1;

    private static readonly Lazy<IReadOnlySet<string>> KnownOnce = new(() => Migrations(typeof(JasonDbContext).Assembly));

    /// <summary>The migrations this build applies, by id, as EF Core finds them: every migration of the runtime's context.</summary>
    public static IReadOnlySet<string> Known => KnownOnce.Value;

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
    public static IReadOnlyList<string>? Unknown(string databaseFile) => Unknown(databaseFile, () => Known);

    /// <summary>
    /// The same, against the migrations <paramref name="known"/> gives, which is asked inside the read: a build whose own
    /// list cannot be made is a read that could not be made.
    /// </summary>
    internal static IReadOnlyList<string>? Unknown(string databaseFile, Func<IReadOnlySet<string>> known)
    {
        ArgumentNullException.ThrowIfNull(databaseFile);
        ArgumentNullException.ThrowIfNull(known);
        try
        {
            return Read(databaseFile, known);
        }
        catch (Exception)
        {
            // Whatever it was, the start goes on as it did before the guard: a refusal is only ever said of rows read whole.
            return null;
        }
    }

    /// <summary>The migrations of <paramref name="assembly"/> that belong to the runtime's context, by id, compared by ordinal.</summary>
    internal static IReadOnlySet<string> Migrations(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        Type?[] types;
        try
        {
            types = assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException partly)
        {
            // As EF Core's own discovery does: the types that did load are the ones a migration is found among.
            types = partly.Types;
        }

        return types
            .OfType<Type>()
            .Where(type => type.GetCustomAttribute<DbContextAttribute>()?.ContextType == typeof(JasonDbContext))
            .Select(type => type.GetCustomAttribute<MigrationAttribute>()?.Id)
            .OfType<string>()
            .ToHashSet(StringComparer.Ordinal);
    }

    private static List<string>? Read(string databaseFile, Func<IReadOnlySet<string>> known)
    {
        if (!File.Exists(databaseFile))
        {
            return null;
        }

        var migrations = known();

        // A runtime that begins writing between this look and the open below holds the instance lock this start would then
        // be refused by. A page it changed under an immutable read might make the read return wrong rows or fail, as SQLite
        // says of a file opened immutable that changes; that start is refused by the same lock all the same.
        var logged = File.Exists(databaseFile + "-wal");
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = logged ? databaseFile : new Uri(Path.GetFullPath(databaseFile)).AbsoluteUri + "?immutable=1",
            Mode = SqliteOpenMode.ReadOnly,
            Pooling = false,
            DefaultTimeout = BusyTimeoutSeconds,
        }.ToString();

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
            if (!migrations.Contains(id))
            {
                unknown.Add(id);
            }
        }

        return unknown;
    }
}
