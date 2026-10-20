using System.Data;
using System.Globalization;
using System.Reflection;
using Jason.Contracts.Discovery;
using Jason.Runtime.Persistence.Migrations;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Jason.Runtime.Persistence;

/// <param name="LinkedInKeys">What the start that re-keyed the stored LinkedIn links left; null on any other start.</param>
public sealed record MigrationReport(
    IReadOnlyList<string> AppliedMigrations,
    IReadOnlyList<string> NewlyApplied,
    string? BackupFile,
    LinkedInKeyCounts? LinkedInKeys = null);

/// <summary>
/// The LinkedIn profile links a database holds, in the register and the contacts' channels together, counted as the
/// re-key left them: every one stored, those that are keys, those left as they stood because the rule refuses them, those
/// left as they stood beside the row or entry that holds their key, and how many keys more than one contact holds. Counts,
/// never a value: they say what a person may want to look at, and the register and the contacts say which.
/// </summary>
public sealed record LinkedInKeyCounts(int Stored, int Keys, int Refused, int LeftBeside, int SharedKeys);

/// <summary>
/// Brings the database to the current schema on every runtime start. An existing database is backed up
/// (online SQLite backup, consistent in WAL mode) before any pending migration runs; the newest
/// <see cref="RetainedBackups"/> backups are kept. Restart is therefore an ordinary start.
/// </summary>
public sealed class DatabaseMigrator(JasonPaths paths)
{
    public const int RetainedBackups = 1;

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

    private static readonly string LinkedInOneKeyId = typeof(LinkedInOneKey).GetCustomAttribute<MigrationAttribute>()!.Id;

    public MigrationReport Migrate()
    {
        Directory.CreateDirectory(paths.StateDirectory);
        var databaseExisted = File.Exists(paths.DatabaseFile);

        using var db = new JasonDbContext(JasonDbContext.CreateOptions(paths.DatabaseFile));
        var pending = db.Database.GetPendingMigrations().ToList();

        string? backup = null;
        if (pending.Count > 0 && databaseExisted)
        {
            backup = BackupBefore(db, pending[0]);
        }

        if (pending.Count > 0)
        {
            db.Database.Migrate();
        }

        // A database made by this start held nothing to re-key, so only one that existed is counted.
        var linkedIn = databaseExisted && pending.Contains(LinkedInOneKeyId, StringComparer.Ordinal) ? CountLinkedInKeys(db) : null;

        db.Database.ExecuteSqlRaw("PRAGMA journal_mode=WAL;");
        var applied = db.Database.GetAppliedMigrations().ToList();
        return new MigrationReport(applied, pending, backup, linkedIn);
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
        var stamp = DateTime.UtcNow.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture);
        var backupFile = Path.Combine(paths.BackupsDirectory, $"jason-{stamp}-before-{firstPendingMigration}.db");

        var source = (SqliteConnection)db.Database.GetDbConnection();
        var openedHere = source.State != ConnectionState.Open;
        if (openedHere)
        {
            source.Open();
        }

        try
        {
            using var destination = new SqliteConnection($"Data Source={backupFile}");
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

        Prune(keep: backupFile);
        return backupFile;
    }

    private void Prune(string keep)
    {
        var older = Directory.EnumerateFiles(paths.BackupsDirectory, "jason-*-before-*.db")
            .Where(file => !string.Equals(Path.GetFullPath(file), Path.GetFullPath(keep), StringComparison.Ordinal))
            .OrderByDescending(file => Path.GetFileName(file), StringComparer.Ordinal)
            .Skip(RetainedBackups - 1);

        foreach (var file in older)
        {
            File.Delete(file);
        }
    }
}
