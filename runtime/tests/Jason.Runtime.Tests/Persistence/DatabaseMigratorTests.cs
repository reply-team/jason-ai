using Jason.Runtime.Persistence;
using Microsoft.Data.Sqlite;

namespace Jason.Runtime.Tests.Persistence;

public class DatabaseMigratorTests
{
    [Fact]
    public void Fresh_database_is_created_migrated_and_in_wal_mode_without_a_backup()
    {
        using var dir = new TempDataDir();

        var report = new DatabaseMigrator(dir.Paths).Migrate();

        Assert.True(File.Exists(dir.Paths.DatabaseFile));
        Assert.Single(report.AppliedMigrations);
        Assert.EndsWith("_InitialCreate", report.AppliedMigrations[0], StringComparison.Ordinal);
        Assert.Equal(report.AppliedMigrations, report.NewlyApplied);
        Assert.Null(report.BackupFile);
        Assert.False(Directory.Exists(dir.Paths.BackupsDirectory) && Directory.EnumerateFiles(dir.Paths.BackupsDirectory).Any());
        Assert.Equal("wal", Scalar(dir.Paths.DatabaseFile, "PRAGMA journal_mode"));
    }

    [Fact]
    public void Second_start_is_a_no_op()
    {
        using var dir = new TempDataDir();
        new DatabaseMigrator(dir.Paths).Migrate();

        var report = new DatabaseMigrator(dir.Paths).Migrate();

        Assert.Single(report.AppliedMigrations);
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

    private static string Scalar(string databaseFile, string sql)
    {
        using var connection = new SqliteConnection($"Data Source={databaseFile}");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToString(command.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty;
    }
}
