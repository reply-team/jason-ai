using System.Data;
using System.Globalization;
using System.Reflection;
using Jason.Contracts.Discovery;
using Jason.Runtime.Persistence.Migrations;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Jason.Runtime.Persistence;

/// <param name="LinkedInKeys">What the start that re-keyed the stored LinkedIn links left; null on any other start.</param>
/// <param name="StaleMigrationLockCleared">Whether a migration lock a start stopped part way left behind was cleared first.</param>
public sealed record MigrationReport(
    IReadOnlyList<string> AppliedMigrations,
    IReadOnlyList<string> NewlyApplied,
    string? BackupFile,
    LinkedInKeyCounts? LinkedInKeys = null,
    bool StaleMigrationLockCleared = false);

/// <summary>
/// The LinkedIn profile links a database holds, in the register and the contacts' channels together, counted as the
/// re-key left them: every one stored, those that are keys, those left as they stood because the rule refuses them, those
/// left as they stood beside the row or entry that holds their key, and how many keys more than one contact holds. Counts,
/// never a value: they say what a person may want to look at, and the register and the contacts say which.
/// </summary>
public sealed record LinkedInKeyCounts(int Stored, int Keys, int Refused, int LeftBeside, int SharedKeys);

/// <summary>
/// Brings the database to the current schema on every runtime start. An existing database is backed up
/// (online SQLite backup, consistent in WAL mode) before any pending migration runs. Restart is therefore an ordinary
/// start.
/// </summary>
/// <remarks>
/// Two guarantees hold the backups to what they are for. A migration that fails puts back the copy this start took,
/// since nothing has written since it was taken, and the start fails saying so (<see cref="MigrationFailedException"/>):
/// a migration SQLite cannot make in one transaction would otherwise leave a part-migrated database that no later start
/// can migrate. And older backups are let go of only once a migration has succeeded, the newest
/// <see cref="RetainedBackups"/> kept: until then one of them may be the only copy of the installation as it stood before
/// an upgrade that a power cut or an ended process stopped part way. Such a start also leaves EF Core's migration lock
/// taken, which EF Core would wait for without end; the next start clears it before it migrates.
/// </remarks>
public sealed class DatabaseMigrator(JasonPaths paths)
{
    public const int RetainedBackups = 1;

    /// <summary>Added to the context the migrations run on; for a test that stops a migration part way.</summary>
    internal IReadOnlyList<IInterceptor> Interceptors { get; init; } = [];

    /// <summary>What a backup's name is stamped from.</summary>
    internal TimeProvider Clock { get; init; } = TimeProvider.System;

    /// <summary>The links counted, by the rule itself: one pass over each table's LinkedIn rows through its index by channel.</summary>
    internal const string LinkedInCounts =
        """
        SELECT count(*), coalesce(sum(key = value), 0), coalesce(sum(key IS NULL), 0), coalesce(sum(key <> value), 0)
        FROM (
            SELECT value, jason_linkedin_key(value) AS key FROM suppressions WHERE channel = 'linkedin'
            UNION ALL
            SELECT value, jason_linkedin_key(value) FROM contact_channels WHERE channel = 'linkedin'
        )
        """;

    /// <summary>The keys more than one contact holds, read in the order of the index by channel and value.</summary>
    internal const string LinkedInSharedKeys =
        """
        SELECT count(*)
        FROM (
            SELECT value FROM contact_channels WHERE channel = 'linkedin'
            GROUP BY value
            HAVING count(DISTINCT contact_id) > 1 AND jason_linkedin_key(value) = value
        )
        """;

    /// <summary>EF Core's migration lock given back: the row a migration takes before it begins and deletes when it ends.</summary>
    internal const string ClearMigrationLock = "DELETE FROM \"__EFMigrationsLock\";";

    private static readonly string LinkedInOneKeyId = typeof(LinkedInOneKey).GetCustomAttribute<MigrationAttribute>()!.Id;

    public MigrationReport Migrate()
    {
        Directory.CreateDirectory(paths.StateDirectory);
        var databaseExisted = File.Exists(paths.DatabaseFile);

        var options = new DbContextOptionsBuilder<JasonDbContext>();
        JasonDbContext.Configure(options, paths.DatabaseFile).AddInterceptors(Interceptors);
        using var db = new JasonDbContext(options.Options);
        var pending = db.Database.GetPendingMigrations().ToList();

        string? backup = null;
        if (pending.Count > 0 && databaseExisted)
        {
            backup = BackupBefore(db, pending[0]);
        }

        var staleLockCleared = false;
        if (pending.Count > 0)
        {
            try
            {
                staleLockCleared = ClearStaleMigrationLock(db);
                db.Database.Migrate();
            }
            catch (Exception failure) when (backup is not null)
            {
                var migration = FailedAt(db, pending);
                db.Dispose();
                try
                {
                    PutBack(backup);
                }
                catch (Exception putting) when (putting is IOException or UnauthorizedAccessException)
                {
                    throw MigrationFailedException.NotPutBack(migration, backup, paths.DatabaseFile, failure, putting);
                }

                throw MigrationFailedException.PutBack(migration, backup, paths.BackupsDirectory, failure);
            }
            catch (Exception failure) when (!databaseExisted)
            {
                // This start made the file from nothing, so it holds no one's data and there is no copy to put back; what is
                // left of it would only fail the next start on a table that is already there.
                var migration = FailedAt(db, pending);
                db.Dispose();
                var removed = TryRemoveCreated();
                throw MigrationFailedException.MadeByThisStart(migration, paths.DatabaseFile, removed, failure);
            }

            if (backup is not null)
            {
                Prune(keep: backup);
            }
        }

        // A database made by this start held nothing to re-key, so only one that existed is counted.
        var linkedIn = databaseExisted && pending.Contains(LinkedInOneKeyId, StringComparer.Ordinal) ? CountLinkedInKeys(db) : null;

        db.Database.ExecuteSqlRaw("PRAGMA journal_mode=WAL;");
        var applied = db.Database.GetAppliedMigrations().ToList();
        return new MigrationReport(applied, pending, backup, linkedIn, staleLockCleared);
    }

    /// <summary>
    /// EF Core's migration lock, cleared when a start stopped part way left it taken; whether it was. EF Core waits for a
    /// lock it finds taken, without end, and nothing else would ever give this one back. A lock nobody left is not touched.
    /// </summary>
    private static bool ClearStaleMigrationLock(JasonDbContext db)
    {
        var connection = db.Database.GetDbConnection();
        db.Database.OpenConnection();
        try
        {
            using var held = connection.CreateCommand();
            held.CommandText = "SELECT count(*) FROM sqlite_master WHERE type = 'table' AND name = '__EFMigrationsLock'";
            if (Convert.ToInt32(held.ExecuteScalar(), CultureInfo.InvariantCulture) == 0)
            {
                return false;
            }

            held.CommandText = "SELECT count(*) FROM \"__EFMigrationsLock\"";
            if (Convert.ToInt32(held.ExecuteScalar(), CultureInfo.InvariantCulture) == 0)
            {
                return false;
            }
        }
        finally
        {
            db.Database.CloseConnection();
        }

        // Safe only because the runtime migrates while it holds the instance lock of this data directory (RuntimeHost takes
        // it before the migrator runs), so no other migration can be running against this database: the row is nobody's.
        db.Database.ExecuteSqlRaw(ClearMigrationLock);
        return true;
    }

    private static LinkedInKeyCounts CountLinkedInKeys(JasonDbContext db)
    {
        var connection = db.Database.GetDbConnection();
        db.Database.OpenConnection();
        try
        {
            using var counts = connection.CreateCommand();
            counts.CommandText = LinkedInCounts;
            using var reader = counts.ExecuteReader();
            reader.Read();
            var (stored, keys, refused, beside) = (reader.GetInt32(0), reader.GetInt32(1), reader.GetInt32(2), reader.GetInt32(3));
            reader.Close();

            using var shared = connection.CreateCommand();
            shared.CommandText = LinkedInSharedKeys;
            return new LinkedInKeyCounts(stored, keys, refused, beside, Convert.ToInt32(shared.ExecuteScalar(), CultureInfo.InvariantCulture));
        }
        finally
        {
            db.Database.CloseConnection();
        }
    }

    private string BackupBefore(JasonDbContext db, string firstPendingMigration)
    {
        Directory.CreateDirectory(paths.BackupsDirectory);
        var stamp = Clock.GetUtcNow().UtcDateTime.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture);
        var stem = Path.Combine(paths.BackupsDirectory, $"jason-{stamp}-before-{firstPendingMigration}");

        // A backup never takes the place of another: a start in the same second as one that failed would otherwise write
        // over the copy that one took, which may be the last of the installation as it stood.
        var backupFile = stem + ".db";
        for (var taken = 2; File.Exists(backupFile); taken++)
        {
            backupFile = string.Create(CultureInfo.InvariantCulture, $"{stem}-{taken}.db");
        }

        var source = (SqliteConnection)db.Database.GetDbConnection();
        var openedHere = source.State != ConnectionState.Open;
        if (openedHere)
        {
            source.Open();
        }

        try
        {
            // Not pooled: a pooled connection would hold the copy open after this, and it may have to be read back or let go of.
            using var destination = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = backupFile, Pooling = false }.ToString());
            destination.Open();
            source.BackupDatabase(destination);
        }
        finally
        {
            if (openedHere)
            {
                source.Close();
            }
        }

        return backupFile;
    }

    /// <summary>The first pending migration the history does not hold after the failure, as far as it can be read.</summary>
    private static string FailedAt(JasonDbContext db, List<string> pending)
    {
        try
        {
            var applied = db.Database.GetAppliedMigrations().ToHashSet(StringComparer.Ordinal);
            return pending.FirstOrDefault(migration => !applied.Contains(migration)) ?? pending[^1];
        }
        catch (Exception unreadable) when (unreadable is SqliteException or InvalidOperationException)
        {
            return pending[0];
        }
    }

    /// <summary>
    /// The copy this start took, put back in place of the database: every connection of this process to it let go of, the
    /// copy staged beside it, its write-ahead log and shared memory deleted — a log written after the copy, replayed into
    /// it, is corruption — and the staged copy moved over it in one step.
    /// </summary>
    private void PutBack(string backup)
    {
        using (var connection = new SqliteConnection(JasonDbContext.ConnectionString(paths.DatabaseFile)))
        {
            SqliteConnection.ClearPool(connection);
        }

        var staged = paths.DatabaseFile + ".restoring";
        File.Copy(backup, staged, overwrite: true);
        File.Delete(paths.DatabaseFile + "-wal");
        File.Delete(paths.DatabaseFile + "-shm");
        File.Move(staged, paths.DatabaseFile, overwrite: true);
    }

    /// <summary>The database this start made from nothing, let go of and deleted with its log files; whether it is gone.</summary>
    private bool TryRemoveCreated()
    {
        try
        {
            using (var connection = new SqliteConnection(JasonDbContext.ConnectionString(paths.DatabaseFile)))
            {
                SqliteConnection.ClearPool(connection);
            }

            File.Delete(paths.DatabaseFile + "-wal");
            File.Delete(paths.DatabaseFile + "-shm");
            File.Delete(paths.DatabaseFile);
            return true;
        }
        catch (Exception removing) when (removing is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private void Prune(string keep)
    {
        var older = Directory.EnumerateFiles(paths.BackupsDirectory, "jason-*-before-*.db")
            .Where(file => !string.Equals(Path.GetFullPath(file), Path.GetFullPath(keep), StringComparison.Ordinal))
            .OrderByDescending(file => Path.GetFileName(file), StringComparer.Ordinal)
            .Skip(RetainedBackups - 1);

        foreach (var file in older)
        {
            try
            {
                File.Delete(file);
            }
            catch (Exception held) when (held is IOException or UnauthorizedAccessException)
            {
                // Something still holds it open. The migration has succeeded, so this start goes on, and the backup is
                // left for the next start that migrates to let go of.
            }
        }
    }
}
