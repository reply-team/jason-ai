using System.Data;
using System.Globalization;
using Jason.Contracts.Discovery;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Jason.Runtime.Persistence;

public sealed record MigrationReport(IReadOnlyList<string> AppliedMigrations, IReadOnlyList<string> NewlyApplied, string? BackupFile);

/// <summary>
/// Brings the database to the current schema on every runtime start. An existing database is backed up
/// (online SQLite backup, consistent in WAL mode) before any pending migration runs; the newest
/// <see cref="RetainedBackups"/> backups are kept. Restart is therefore an ordinary start.
/// </summary>
public sealed class DatabaseMigrator(JasonPaths paths)
{
    public const int RetainedBackups = 1;

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

        db.Database.ExecuteSqlRaw("PRAGMA journal_mode=WAL;");
        var applied = db.Database.GetAppliedMigrations().ToList();
        return new MigrationReport(applied, pending, backup);
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
