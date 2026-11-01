using System.Data;
using System.Globalization;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Jason.Contracts.Discovery;
using Jason.Runtime.Persistence.Migrations;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Migrations;
using Serilog;

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
/// <para>
/// No code survives such a stop, so the start that migrates writes down first which copy it took, the migration it began
/// at and the line the chronicle stood at (<see cref="MarkerFile"/>), and deletes the record once it is done with. A start
/// that finds the record knows the one before it was stopped while migrating: if migrating fails again, it puts that start's
/// copy back — the installation as it stood — but only while the chronicle stands at the line written down, since a line
/// recorded after it would be lost; otherwise it puts back its own and names that copy for a person to decide on.
/// </para>
/// </remarks>
public sealed class DatabaseMigrator(JasonPaths paths)
{
    public const int RetainedBackups = 1;

    /// <summary>The line the chronicle stands at: its newest entry's key, 0 while it holds none.</summary>
    internal const string JournalHighWaterQuery = "SELECT coalesce(max(id), 0) AS \"Value\" FROM journal";

    /// <summary>Added to the context the migrations run on; for a test that stops a migration part way.</summary>
    internal IReadOnlyList<IInterceptor> Interceptors { get; init; } = [];

    /// <summary>What a backup's name is stamped from.</summary>
    internal TimeProvider Clock { get; init; } = TimeProvider.System;

    /// <summary>Where a record this executable cannot act on is said; by its kind alone, never what the record holds.</summary>
    internal ILogger? Log { get; init; }

    /// <summary>The record a start that migrates keeps until it is done: the copy it took, where it began, the chronicle's line.</summary>
    internal string MarkerFile => Path.Combine(paths.StateDirectory, "migration.json");

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

        var (found, stopped) = ReadMarker(db);
        if (found == Found.Unknown)
        {
            // An older executable never deletes the only pointer to the copy a newer one took; nor does it say what the file
            // names, which is the newer executable's to read.
            Log?.Warning("A record of a start stopped while migrating names a migration this version does not know; it is left where it is");
        }
        else if (found == Found.Unreadable)
        {
            Log?.Warning("A record of a start stopped while migrating could not be read; it is left where it is");
        }
        else if (found == Found.Known && pending.Count == 0 && Done(db, stopped!))
        {
            // Nothing to migrate, and every migration its start meant to apply is in the history: that start was stopped after
            // it had migrated, or a later one migrated since, and there is nothing of it to put back. With anything pending it
            // stays whatever the history says, since that start may have been stopped inside a later migration than its first.
            DeleteMarker();
            (found, stopped) = (Found.None, null);
        }

        // Read before this start changes anything: the line the chronicle stands at, for a stopped start's copy to be put
        // back only if nothing has been recorded since that start took it.
        var highWater = pending.Count > 0 && databaseExisted ? JournalHighWater(db) : null;

        string? backup = null;
        var marked = false;

        // The record on disk that this start answers for, as it stands: the one found, or the one this start wrote.
        var record = found is Found.Known or Found.Unknown ? stopped : null;
        if (pending.Count > 0 && databaseExisted)
        {
            backup = BackupBefore(db, pending[0]);
            if (found == Found.Known)
            {
                // The stopped start's copy and line are carried forward, and the later of the two last migrations: a second
                // stopped start must not take the place of the only copy of the installation as it stood, and the record must
                // stand until every migration any stopped start meant to apply is applied.
                stopped = stopped! with { FirstPending = pending[0], LastPending = Later(stopped.LastPending, pending[^1]), WrittenAt = Clock.GetUtcNow() };
                marked = WriteMarker(stopped);
                record = stopped;
            }
            else if (found == Found.None && highWater is { } line)
            {
                var own = new Marker(backup, pending[0], pending[^1], line, Clock.GetUtcNow());
                marked = WriteMarker(own);
                record = marked ? own : null;
            }
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
                if (found == Found.Known)
                {
                    throw StoppedStartFailed(migration, backup, stopped!, highWater, failure);
                }

                try
                {
                    PutBack(backup);
                }
                catch (Exception putting) when (putting is IOException or UnauthorizedAccessException)
                {
                    throw MigrationFailedException.NotPutBack(migration, backup, paths.DatabaseFile, failure, putting);
                }

                if (marked)
                {
                    DeleteMarker();
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
                if (found == Found.Known)
                {
                    DeleteMarker();
                }

                throw MigrationFailedException.MadeByThisStart(migration, paths.DatabaseFile, removed, failure);
            }

            // The record this start is done with goes; one still on disk keeps the copy it names from being let go of.
            string? named = null;
            if (record is not null && (found == Found.Unknown || !Done(db, record)))
            {
                named = record.CleanBackup;
            }
            else if (record is not null && !DeleteMarker())
            {
                Log?.Warning("The record of this start's migration could not be deleted after it succeeded; it and the backup it names are left");
                named = record.CleanBackup;
            }

            // A record that could not be read may name any of them, so none is let go of while it is on disk; the warning said
            // at every start is where a person hears of it.
            if (backup is not null && found != Found.Unreadable)
            {
                Prune(keep: backup, named);
            }
        }

        // A database made by this start held nothing to re-key, so only one that existed is counted.
        var linkedIn = databaseExisted && pending.Contains(LinkedInOneKeyId, StringComparer.Ordinal) ? CountLinkedInKeys(db) : null;

        db.Database.ExecuteSqlRaw("PRAGMA journal_mode=WAL;");
        var applied = db.Database.GetAppliedMigrations().ToList();
        return new MigrationReport(applied, pending, backup, linkedIn, staleLockCleared);
    }

    /// <summary>
    /// A migration that failed on what a start stopped while migrating left. That start's copy is put back when all three
    /// hold — its record was read, the copy is there, and the chronicle stands at the line it stood at when the copy was
    /// taken — and this start's own otherwise, so the database is as this start found it and the refusal names that copy.
    /// </summary>
    private MigrationFailedException StoppedStartFailed(string migration, string backup, Marker stopped, long? highWater, Exception failure)
    {
        // A line that could not be read counts as moved: the copy goes back only when it is certain nothing has been recorded
        // since. The write-ahead log says nothing here: after a process ended part way, its frames are the migration's own.
        var outcome = !File.Exists(stopped.CleanBackup) ? StoppedStart.BackupGone
            : highWater != stopped.JournalHighWater ? StoppedStart.ChronicleMoved
            : StoppedStart.PutBack;

        if (outcome == StoppedStart.PutBack)
        {
            try
            {
                PutBack(stopped.CleanBackup);
            }
            catch (Exception putting) when (putting is IOException or UnauthorizedAccessException)
            {
                return MigrationFailedException.StoppedStartNotPutBack(migration, stopped.CleanBackup, paths.DatabaseFile, failure, putting);
            }

            DeleteMarker();
            return MigrationFailedException.StoppedStartPutBack(migration, stopped.CleanBackup, failure);
        }

        try
        {
            PutBack(backup);
        }
        catch (Exception putting) when (putting is IOException or UnauthorizedAccessException)
        {
            return MigrationFailedException.NotPutBack(migration, backup, paths.DatabaseFile, failure, putting);
        }

        return MigrationFailedException.StoppedStartRefused(migration, backup, stopped.CleanBackup, paths.DatabaseFile, paths.BackupsDirectory, outcome, failure);
    }

    /// <summary>The record of a start that migrates, as a start after it finds it.</summary>
    private (Found Found, Marker? Marker) ReadMarker(JasonDbContext db)
    {
        if (!File.Exists(MarkerFile))
        {
            return (Found.None, null);
        }

        Marker? marker;
        try
        {
            marker = Marker.Parse(File.ReadAllText(MarkerFile));
        }
        catch (Exception unreadable) when (unreadable is IOException or UnauthorizedAccessException or JsonException)
        {
            marker = null;
        }

        // A copy outside this installation's backups is not one this runtime took, and is never copied over its database.
        if (marker is null || !InBackups(marker.CleanBackup))
        {
            return (Found.Unreadable, null);
        }

        return db.Database.GetMigrations().Contains(marker.FirstPending, StringComparer.Ordinal) ? (Found.Known, marker) : (Found.Unknown, marker);
    }

    /// <summary>
    /// The record written in one step — a file of its own flushed to the disk, then moved over the name — so a start stopped
    /// while writing it leaves the one before or this one, never half of either; whether it was written.
    /// </summary>
    private bool WriteMarker(Marker marker)
    {
        var writing = MarkerFile + ".writing";
        try
        {
            using (var stream = new FileStream(writing, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                stream.Write(Encoding.UTF8.GetBytes(marker.ToJson()));
                stream.Flush(flushToDisk: true);
            }

            File.Move(writing, MarkerFile, overwrite: true);
            return true;
        }
        catch (Exception refused) when (refused is IOException or UnauthorizedAccessException)
        {
            // Without it this start migrates as starts did before there was one: what a stopped start left is put back by hand.
            Log?.Warning("The record of this start's migration could not be written; were this start stopped while migrating, the next one could not put its backup back");
            return false;
        }
    }

    /// <summary>The record deleted; whether it is gone.</summary>
    private bool DeleteMarker()
    {
        try
        {
            File.Delete(MarkerFile);
            return true;
        }
        catch (Exception held) when (held is IOException or UnauthorizedAccessException)
        {
            // Left, it is read again by the next start: one whose migrations are in the history is deleted then, and one whose
            // are not names a copy that start may still put back, which is what this one did or would have done.
            return false;
        }
    }

    /// <summary>Whether every migration the record's start meant to apply, its first and its last, is one this build has and the history holds.</summary>
    private static bool Done(JasonDbContext db, Marker record)
    {
        var known = db.Database.GetMigrations().ToHashSet(StringComparer.Ordinal);
        var applied = Applied(db);
        return known.Contains(record.FirstPending) && known.Contains(record.LastPending)
            && applied.Contains(record.FirstPending) && applied.Contains(record.LastPending);
    }

    /// <summary>The later of two migrations by their ids, which begin with the moment each was made.</summary>
    private static string Later(string one, string other) => string.CompareOrdinal(one, other) >= 0 ? one : other;

    /// <summary>Whether a file is directly inside this installation's backups directory.</summary>
    private bool InBackups(string file)
    {
        try
        {
            var directory = Path.GetDirectoryName(Path.GetFullPath(file));
            return string.Equals(
                directory,
                Path.TrimEndingDirectorySeparator(Path.GetFullPath(paths.BackupsDirectory)),
                OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
        }
        catch (Exception unusable) when (unusable is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }

    /// <summary>The line the chronicle stands at; null when it cannot be read, which a stopped start's put-back counts as moved.</summary>
    private static long? JournalHighWater(JasonDbContext db)
    {
        try
        {
            return db.Database.SqlQueryRaw<long>(JournalHighWaterQuery).AsEnumerable().Single();
        }
        catch (Exception unreadable) when (unreadable is not OperationCanceledException)
        {
            // Whatever it was, the line is not known to stand still.
            return null;
        }
    }

    /// <summary>The migrations the history holds, as far as it can be read.</summary>
    private static HashSet<string> Applied(JasonDbContext db)
    {
        try
        {
            return db.Database.GetAppliedMigrations().ToHashSet(StringComparer.Ordinal);
        }
        catch (Exception unreadable) when (unreadable is SqliteException or InvalidOperationException)
        {
            return [];
        }
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

    private void Prune(string keep, string? named)
    {
        var older = Directory.EnumerateFiles(paths.BackupsDirectory, "jason-*-before-*.db")
            .Where(file => !string.Equals(Path.GetFullPath(file), Path.GetFullPath(keep), StringComparison.Ordinal))
            .Where(file => named is null || !string.Equals(Path.GetFullPath(file), Path.GetFullPath(named), StringComparison.OrdinalIgnoreCase))
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

    /// <summary>What a start finds of the record a start before it left.</summary>
    private enum Found
    {
        None,
        Known,

        /// <summary>It names a migration this executable does not have: a newer one wrote it, and it is that one's to read.</summary>
        Unknown,

        Unreadable,
    }

    /// <summary>
    /// The record a start that migrates writes before it begins: its copy, the first and the last migration it meant to apply,
    /// and the chronicle's line.
    /// </summary>
    private sealed record Marker(string CleanBackup, string FirstPending, string LastPending, long JournalHighWater, DateTimeOffset WrittenAt)
    {
        public string ToJson() =>
            new JsonObject
            {
                ["clean_backup"] = CleanBackup,
                ["first_pending"] = FirstPending,
                ["last_pending"] = LastPending,
                ["journal_high_water"] = JournalHighWater,
                ["written_at"] = WrittenAt.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture),
            }.ToJsonString();

        /// <summary>The record, or null where any of it is missing or is not what it should be.</summary>
        public static Marker? Parse(string text) =>
            JsonNode.Parse(text) is JsonObject document
            && document["clean_backup"] is JsonValue clean && clean.TryGetValue<string>(out var cleanBackup) && cleanBackup.Length > 0
            && document["first_pending"] is JsonValue first && first.TryGetValue<string>(out var firstPending) && firstPending.Length > 0
            && document["last_pending"] is JsonValue last && last.TryGetValue<string>(out var lastPending) && lastPending.Length > 0
            && document["journal_high_water"] is JsonValue line && line.TryGetValue<long>(out var highWater) && highWater >= 0
            && document["written_at"] is JsonValue written && written.TryGetValue<string>(out var at)
            && DateTimeOffset.TryParse(at, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var writtenAt)
                ? new Marker(cleanBackup, firstPending, lastPending, highWater, writtenAt)
                : null;
    }
}
