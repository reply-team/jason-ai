using Jason.Runtime.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Jason.Runtime.Tests.Persistence;

public class DatabaseMigratorTests
{
    [Fact]
    public void Fresh_database_is_created_migrated_and_in_wal_mode_without_a_backup()
    {
        using var dir = new TempDataDir();

        var report = new DatabaseMigrator(dir.Paths).Migrate();

        Assert.True(File.Exists(dir.Paths.DatabaseFile));
        Assert.EndsWith("_InitialCreate", report.AppliedMigrations[0], StringComparison.Ordinal);
        Assert.Contains(report.AppliedMigrations, m => m.EndsWith("_CampaignsContactsJournal", StringComparison.Ordinal));
        Assert.Equal(report.AppliedMigrations, report.NewlyApplied);
        Assert.Null(report.BackupFile);
        Assert.False(Directory.Exists(dir.Paths.BackupsDirectory) && Directory.EnumerateFiles(dir.Paths.BackupsDirectory).Any());
        Assert.Equal("wal", Scalar(dir.Paths.DatabaseFile, "PRAGMA journal_mode"));
    }

    [Fact]
    public void Second_start_is_a_no_op()
    {
        using var dir = new TempDataDir();
        var first = new DatabaseMigrator(dir.Paths).Migrate();

        var report = new DatabaseMigrator(dir.Paths).Migrate();

        Assert.Equal(first.AppliedMigrations, report.AppliedMigrations);
        Assert.Empty(report.NewlyApplied);
        Assert.Null(report.BackupFile);
    }

    [Fact]
    public void Existing_database_with_pending_migrations_is_backed_up_first()
    {
        using var dir = new TempDataDir();
        Directory.CreateDirectory(dir.Paths.StateDirectory);
        using (var connection = new SqliteConnection($"Data Source={dir.Paths.DatabaseFile}"))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "CREATE TABLE legacy (x INTEGER); INSERT INTO legacy VALUES (42);";
            command.ExecuteNonQuery();
        }

        var report = new DatabaseMigrator(dir.Paths).Migrate();

        Assert.NotNull(report.BackupFile);
        Assert.True(File.Exists(report.BackupFile));
        Assert.Matches(@"jason-\d{8}T\d{6}Z-before-\d+_InitialCreate\.db$", Path.GetFileName(report.BackupFile));
        Assert.Equal("42", Scalar(report.BackupFile!, "SELECT x FROM legacy"));
        Assert.Equal("0", Scalar(report.BackupFile!, "SELECT COUNT(*) FROM sqlite_master WHERE name = 'campaigns'"));
        Assert.Equal("1", Scalar(dir.Paths.DatabaseFile, "SELECT COUNT(*) FROM sqlite_master WHERE name = 'campaigns'"));
    }

    [Fact]
    public void Older_backups_are_pruned_to_the_retention_limit()
    {
        using var dir = new TempDataDir();
        Directory.CreateDirectory(dir.Paths.BackupsDirectory);
        File.WriteAllText(Path.Combine(dir.Paths.BackupsDirectory, "jason-20200101T000000Z-before-0_Old.db"), string.Empty);
        File.WriteAllText(Path.Combine(dir.Paths.BackupsDirectory, "jason-20210101T000000Z-before-0_Old.db"), string.Empty);
        Directory.CreateDirectory(dir.Paths.StateDirectory);
        using (var connection = new SqliteConnection($"Data Source={dir.Paths.DatabaseFile}"))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "CREATE TABLE legacy (x INTEGER);";
            command.ExecuteNonQuery();
        }

        var report = new DatabaseMigrator(dir.Paths).Migrate();

        var remaining = Directory.GetFiles(dir.Paths.BackupsDirectory);
        Assert.Equal(1, DatabaseMigrator.RetainedBackups);
        Assert.Single(remaining);
        Assert.Equal(report.BackupFile, remaining[0]);
    }

    [Fact]
    public async Task A_wave_two_database_migrates_to_head_without_losing_its_chronicle()
    {
        using var dir = new TempDataDir();
        Directory.CreateDirectory(dir.Paths.StateDirectory);
        await using (var db = new JasonDbContext(JasonDbContext.CreateOptions(dir.Paths.DatabaseFile)))
        {
            await db.GetService<IMigrator>().MigrateAsync("20260914051837_CampaignsContactsJournal", TestContext.Current.CancellationToken);
        }

        Execute(dir.Paths.DatabaseFile, "INSERT INTO campaigns (public_id, name, status, context_json, created_at, updated_at) VALUES ('cmp_OLD', 'old', 'active', '{}', '2026-01-01', '2026-01-01')");
        Execute(dir.Paths.DatabaseFile, "INSERT INTO journal (public_id, ts, actor_type, kind, campaign_id) VALUES ('jrn_OLD', '2026-01-01', 'human', 'campaign_created', 1)");

        var report = new DatabaseMigrator(dir.Paths).Migrate();

        Assert.Contains(report.NewlyApplied, m => m.EndsWith("_WorkItemsAttemptsRoles", StringComparison.Ordinal));
        Assert.Equal("1", Scalar(dir.Paths.DatabaseFile, "SELECT COUNT(*) FROM journal WHERE public_id = 'jrn_OLD'"));
        Assert.Equal("9", Scalar(dir.Paths.DatabaseFile, "SELECT COUNT(*) FROM roles"));

        var refused = Assert.Throws<SqliteException>(() => Execute(dir.Paths.DatabaseFile, "UPDATE journal SET reason = 'rewritten' WHERE public_id = 'jrn_OLD'"));
        Assert.Contains("append-only", refused.Message, StringComparison.Ordinal);
    }

    private static void Execute(string databaseFile, string sql)
    {
        using var connection = new SqliteConnection($"Data Source={databaseFile}");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private static string Scalar(string databaseFile, string sql)
    {
        using var connection = new SqliteConnection($"Data Source={databaseFile}");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToString(command.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty;
    }
}
