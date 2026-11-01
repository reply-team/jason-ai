using System.Data.Common;
using Jason.Runtime.Hosting;
using Jason.Runtime.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Jason.Runtime.Tests.Persistence;

public class DatabaseMigratorTests
{
    [Fact]
    [Critical(Family.P2, "WAL and a fresh start")]
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
    [Critical(Family.P2, "idempotent migration")]
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
    [Critical(Family.P2, "the pre-migration backup")]
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

    /// <summary>
    /// A migration that fails puts back the copy its start took before it began, so the database is exactly as the start
    /// found it, and the start fails saying which migration failed, where the copy is, and that the version before still
    /// runs on it. A table the first migration makes being there already is the cheapest failure that is certain.
    /// </summary>
    [Fact]
    [Critical(Family.P2, "a failed migration is put back")]
    public void A_migration_that_fails_puts_the_database_back_from_the_backup_this_start_took()
    {
        using var dir = new TempDataDir();
        Legacy(dir, "CREATE TABLE campaigns (x INTEGER); INSERT INTO campaigns VALUES (42);");

        var failed = Assert.Throws<MigrationFailedException>(() => new DatabaseMigrator(dir.Paths).Migrate());

        var backup = Assert.Single(Directory.GetFiles(dir.Paths.BackupsDirectory));
        Assert.Equal(backup, failed.Backup);
        Assert.True(failed.Migration.EndsWith("_InitialCreate", StringComparison.Ordinal), $"The failed migration is named '{failed.Migration}'.");
        foreach (var said in new[] { "did not start", failed.Migration, backup, "as it was before this start", "still runs on it" })
        {
            Assert.True(failed.Message.Contains(said, StringComparison.Ordinal), $"The refusal does not say '{said}': {failed.Message}");
        }

        Assert.NotNull(failed.InnerException);
        Assert.Equal("42", Scalar(dir.Paths.DatabaseFile, "SELECT x FROM campaigns"));
        Assert.Equal(
            "campaigns",
            Scalar(dir.Paths.DatabaseFile, "SELECT group_concat(name) FROM sqlite_master WHERE type = 'table' AND name NOT LIKE 'sqlite_%'"));
    }

    /// <summary>
    /// A start that failed lets go of no backup: the ones earlier starts took are kept beside its own until a migration
    /// succeeds, because one of them may be the only copy of the database as it stood before an upgrade that was stopped
    /// part way.
    /// </summary>
    [Fact]
    public void A_start_that_fails_keeps_every_backup()
    {
        using var dir = new TempDataDir();
        Directory.CreateDirectory(dir.Paths.BackupsDirectory);
        var older = Path.Combine(dir.Paths.BackupsDirectory, "jason-20210101T000000Z-before-0_Old.db");
        File.WriteAllText(older, string.Empty);
        Legacy(dir, "CREATE TABLE campaigns (x INTEGER);");

        var failed = Assert.Throws<MigrationFailedException>(() => new DatabaseMigrator(dir.Paths).Migrate());

        Assert.Equal(
            new[] { failed.Backup, older }.Order(StringComparer.Ordinal),
            Directory.GetFiles(dir.Paths.BackupsDirectory).Order(StringComparer.Ordinal));
        Assert.True(failed.Message.Contains(dir.Paths.BackupsDirectory, StringComparison.Ordinal), $"The refusal does not name where the backups are kept: {failed.Message}");
    }

    /// <summary>
    /// A backup never takes the place of another: one taken in a second that already named one gets a name of its own, or
    /// a start straight after one that failed would write over the copy that one took.
    /// </summary>
    [Fact]
    public void A_backup_never_takes_the_place_of_another()
    {
        using var dir = new TempDataDir();
        Legacy(dir, "CREATE TABLE campaigns (x INTEGER);");
        Directory.CreateDirectory(dir.Paths.BackupsDirectory);
        var taken = Path.Combine(dir.Paths.BackupsDirectory, "jason-20261022T120000Z-before-20260913233702_InitialCreate.db");
        File.WriteAllText(taken, "taken");
        var noon = new FixedClock(new DateTimeOffset(2026, 10, 22, 12, 0, 0, TimeSpan.Zero));

        var failed = Assert.Throws<MigrationFailedException>(() => new DatabaseMigrator(dir.Paths) { Clock = noon }.Migrate());

        Assert.Equal(Path.Combine(dir.Paths.BackupsDirectory, "jason-20261022T120000Z-before-20260913233702_InitialCreate-2.db"), failed.Backup);
        Assert.Equal("taken", File.ReadAllText(taken));
    }

    /// <summary>
    /// A migration that fails on a database this start made from nothing has no copy to put back and no version that ran on
    /// it: the refusal says that the file held nothing and no data is lost, never that an earlier version still runs or that
    /// a backup exists, and the half-made file is gone so the next start begins from nothing.
    /// </summary>
    [Fact]
    public void A_migration_that_fails_on_a_database_this_start_made_says_it_held_nothing_and_removes_it()
    {
        using var dir = new TempDataDir();
        var interruption = new Interruption("DROP TABLE \"role_notes\"");

        var failed = Assert.Throws<MigrationFailedException>(() => new DatabaseMigrator(dir.Paths) { Interceptors = [interruption] }.Migrate());

        Assert.True(failed.Created, $"The refusal does not say the database was made by this start: {failed.Message}");
        foreach (var said in new[] { "did not start", failed.Migration, dir.Paths.DatabaseFile, "held nothing yet", "no data is lost", "start the runtime again" })
        {
            Assert.True(failed.Message.Contains(said, StringComparison.Ordinal), $"The refusal does not say '{said}': {failed.Message}");
        }

        foreach (var untrue in new[] { "still runs", "put back", "as it was before", "earlier starts took" })
        {
            Assert.False(failed.Message.Contains(untrue, StringComparison.Ordinal), $"The refusal says '{untrue}', which is not true of a new database: {failed.Message}");
        }

        Assert.False(File.Exists(dir.Paths.DatabaseFile), "The half-made database is still there.");
        Assert.False(Directory.Exists(dir.Paths.BackupsDirectory) && Directory.GetFiles(dir.Paths.BackupsDirectory).Length > 0, "A backup was made of a database that did not exist.");

        var report = new DatabaseMigrator(dir.Paths).Migrate();
        Assert.Null(report.BackupFile);
    }

    /// <summary>
    /// A start whose migration fails is refused in that sentence where whoever started it hears it — the writer a detached
    /// start reads — and nothing escapes it.
    /// </summary>
    [Fact]
    public async Task A_start_whose_migration_fails_is_refused_in_its_own_words()
    {
        using var dir = new TempDataDir();
        Legacy(dir, "CREATE TABLE campaigns (x INTEGER);");
        using var refusals = new StringWriter();

        // Bounded, because a runtime that migrated after all would start and run until it was stopped.
        using var bound = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        bound.CancelAfter(TimeSpan.FromSeconds(30));
        var exit = await RuntimeHost.RunAsync(dir.Paths, TestRuntimeOptions.Quiet with { Refusals = refusals }, bound.Token);

        var said = refusals.ToString().Trim();
        Assert.True(exit == 1, $"The start answered {exit}: {said}");
        Assert.StartsWith("The runtime did not start:", said, StringComparison.Ordinal);
        Assert.Contains(Assert.Single(Directory.GetFiles(dir.Paths.BackupsDirectory)), said, StringComparison.Ordinal);
    }

    /// <summary>
    /// A start stopped while it migrated leaves EF Core's migration lock taken, and EF Core waits for a lock it finds taken
    /// without end. The runtime migrates only under its own instance lock, so the row is nobody's: the next start clears it
    /// and migrates, and says that it did.
    /// </summary>
    [Fact]
    public async Task A_migration_lock_a_stopped_start_left_is_cleared_and_the_next_start_migrates()
    {
        using var dir = new TempDataDir();
        await AtAsync(dir, "20260914051837_CampaignsContactsJournal");
        Execute(dir.Paths.DatabaseFile, "INSERT INTO \"__EFMigrationsLock\" (\"Id\", \"Timestamp\") VALUES (1, '2026-10-22 00:00:00+00:00')");
        var commands = new Commands();

        // Bounded, because a start that waited for the lock would wait for ever.
        var report = await Task.Run(() => new DatabaseMigrator(dir.Paths) { Interceptors = [commands] }.Migrate(), TestContext.Current.CancellationToken)
            .WaitAsync(TimeSpan.FromMinutes(2), TestContext.Current.CancellationToken);

        Assert.True(report.StaleMigrationLockCleared, "The start did not say it cleared the lock it found.");
        Assert.Contains(report.NewlyApplied, m => m.EndsWith("_WorkItemsAttemptsRoles", StringComparison.Ordinal));
        Assert.Equal(1, commands.Count(DatabaseMigrator.ClearMigrationLock));
        Assert.Equal("0", Scalar(dir.Paths.DatabaseFile, "SELECT count(*) FROM \"__EFMigrationsLock\""));
    }

    /// <summary>A start that finds the migration lock free deletes nothing from it and says nothing of it.</summary>
    [Fact]
    public async Task A_start_with_no_stale_migration_lock_clears_nothing()
    {
        using var dir = new TempDataDir();
        await AtAsync(dir, "20260914051837_CampaignsContactsJournal");
        Assert.Equal("1", Scalar(dir.Paths.DatabaseFile, "SELECT count(*) FROM sqlite_master WHERE name = '__EFMigrationsLock'"));
        var commands = new Commands();

        var report = new DatabaseMigrator(dir.Paths) { Interceptors = [commands] }.Migrate();

        Assert.False(report.StaleMigrationLockCleared, "The start said it cleared a lock nobody had left.");
        Assert.Contains(report.NewlyApplied, m => m.EndsWith("_WorkItemsAttemptsRoles", StringComparison.Ordinal));
        Assert.Equal(0, commands.Count(DatabaseMigrator.ClearMigrationLock));
    }

    /// <summary>
    /// A start that migrates writes down, before it begins, which copy it took — and deletes the record once its migration
    /// has succeeded, so the next start does not read it as a start stopped part way.
    /// </summary>
    [Fact]
    public async Task The_record_a_start_writes_before_it_migrates_is_gone_once_it_has_succeeded()
    {
        using var dir = new TempDataDir();
        await AtAsync(dir, "20260914051837_CampaignsContactsJournal");
        var watch = new MarkerWatch(dir);

        var report = new DatabaseMigrator(dir.Paths) { Interceptors = [watch] }.Migrate();

        Assert.True(CleanOf(watch.Seen) == report.BackupFile, $"While it migrated, the record was: {watch.Seen ?? "not there"}");
        Assert.False(File.Exists(MarkerOf(dir)), "The record is still there after the migration succeeded.");
    }

    /// <summary>A start that put its own copy back is done with its record: the database is as it found it.</summary>
    [Fact]
    public async Task The_record_is_gone_once_a_start_has_put_its_own_copy_back()
    {
        using var dir = new TempDataDir();
        await AtAsync(dir, "20260914051837_CampaignsContactsJournal");
        var watch = new MarkerWatch(dir, stopAt: "DROP TABLE \"role_notes\"");

        var failed = Assert.Throws<MigrationFailedException>(() => new DatabaseMigrator(dir.Paths) { Interceptors = [watch] }.Migrate());

        Assert.True(failed.Restored && failed.StoppedStart == StoppedStart.None, $"The start did not put its own copy back: {failed.Message}");
        Assert.True(CleanOf(watch.Seen) == failed.Backup, $"While it migrated, the record was: {watch.Seen ?? "not there"}");
        Assert.False(File.Exists(MarkerOf(dir)), "The record is still there after the start put its own copy back.");
    }

    /// <summary>A record found where this start made the database from nothing points at a copy of a database that is gone.</summary>
    [Fact]
    public void A_record_found_by_a_start_that_made_the_database_is_deleted()
    {
        using var dir = new TempDataDir();
        Directory.CreateDirectory(dir.Paths.StateDirectory);
        File.WriteAllText(MarkerOf(dir), Marker(Path.Combine(dir.Paths.BackupsDirectory, "jason-20261021T000000Z-before-x.db"), First(dir), Last(dir), 4));
        var interruption = new Interruption("DROP TABLE \"role_notes\"");

        var failed = Assert.Throws<MigrationFailedException>(() => new DatabaseMigrator(dir.Paths) { Interceptors = [interruption] }.Migrate());

        Assert.True(failed.Created, $"The start did not say it made the database: {failed.Message}");
        Assert.False(File.Exists(MarkerOf(dir)), "The record is still there after a start that made the database.");
    }

    /// <summary>
    /// A start that finds nothing to migrate, and a record whose migration the history holds, finds a start stopped after it
    /// had migrated: the database is at this version's head, there is nothing of it to put back, and the record is deleted.
    /// With anything still to migrate the record stays, since that start may have been stopped inside a later migration.
    /// </summary>
    [Fact]
    public void A_start_with_nothing_to_migrate_deletes_a_record_whose_migration_the_history_holds()
    {
        using var dir = new TempDataDir();
        new DatabaseMigrator(dir.Paths).Migrate();
        File.WriteAllText(MarkerOf(dir), Marker(Path.Combine(dir.Paths.BackupsDirectory, "jason-20261021T000000Z-before-x.db"), First(dir), Last(dir), 0));

        var report = new DatabaseMigrator(dir.Paths).Migrate();

        Assert.Empty(report.NewlyApplied);
        Assert.False(File.Exists(MarkerOf(dir)), "The record of a migration the history holds is still there.");
    }

    /// <summary>
    /// A record that names a migration this executable does not have was written by a newer one, and may be the only pointer
    /// to the copy of the installation as it stood: an older executable leaves it where it is, writes none of its own over
    /// it, and says so once in the log, without what the record holds.
    /// </summary>
    [Fact]
    public async Task A_record_naming_a_migration_this_version_does_not_know_is_left_and_said_once()
    {
        using var dir = new TempDataDir();
        await AtAsync(dir, "20260914051837_CampaignsContactsJournal");
        var written = Marker(Path.Combine(dir.Paths.BackupsDirectory, "jason-20991231T000000Z-before-later.db"), "20991231000000_FromALaterVersion", "20991231000000_FromALaterVersion", 3);
        File.WriteAllText(MarkerOf(dir), written);
        var said = new Said();
        using var log = new Serilog.LoggerConfiguration().WriteTo.Sink(said).CreateLogger();

        var report = new DatabaseMigrator(dir.Paths) { Log = log }.Migrate();

        Assert.NotEmpty(report.NewlyApplied);
        Assert.Equal(written, File.ReadAllText(MarkerOf(dir)));
        var line = Assert.Single(said.Lines);
        Assert.Contains("does not know", line, StringComparison.Ordinal);
        Assert.False(line.Contains("2099", StringComparison.Ordinal) || line.Contains("FromALaterVersion", StringComparison.Ordinal), $"The line says what the record holds: {line}");
    }

    /// <summary>
    /// A record that cannot be written leaves this start migrating as starts did before there was one, and the log says so
    /// once, without the reason the file system gave.
    /// </summary>
    [Fact]
    public async Task A_record_that_cannot_be_written_is_said_once_and_the_start_migrates()
    {
        using var dir = new TempDataDir();
        await AtAsync(dir, "20260914051837_CampaignsContactsJournal");
        Directory.CreateDirectory(MarkerOf(dir) + ".writing");
        var said = new Said();
        using var log = new Serilog.LoggerConfiguration().WriteTo.Sink(said).CreateLogger();

        var report = new DatabaseMigrator(dir.Paths) { Log = log }.Migrate();

        Assert.NotEmpty(report.NewlyApplied);
        Assert.False(File.Exists(MarkerOf(dir)), "A record was written after all.");
        var line = Assert.Single(said.Lines);
        Assert.Contains("could not be written", line, StringComparison.Ordinal);
        Assert.False(line.Contains("denied", StringComparison.OrdinalIgnoreCase) || line.Contains(dir.Paths.Root, StringComparison.Ordinal), $"The line carries the cause: {line}");
    }

    /// <summary>
    /// A record that cannot be read names no copy anybody can act on, and may still be what a newer start meant to leave: it
    /// is left exactly as it is, never written over, and the log says once that it could not be read, without its text.
    /// </summary>
    [Fact]
    public async Task A_record_that_cannot_be_read_is_left_as_it_is_and_said_once()
    {
        using var dir = new TempDataDir();
        await AtAsync(dir, "20260914051837_CampaignsContactsJournal");
        var whole = Marker(Path.Combine(dir.Paths.BackupsDirectory, "jason-20261021T000000Z-before-x.db"), First(dir), Last(dir), 4);
        var truncated = whole[..(whole.Length / 2)];
        File.WriteAllText(MarkerOf(dir), truncated);
        var said = new Said();
        using var log = new Serilog.LoggerConfiguration().WriteTo.Sink(said).CreateLogger();

        var report = new DatabaseMigrator(dir.Paths) { Log = log }.Migrate();

        Assert.NotEmpty(report.NewlyApplied);
        Assert.Equal(truncated, File.ReadAllText(MarkerOf(dir)));
        var line = Assert.Single(said.Lines);
        Assert.Contains("could not be read", line, StringComparison.Ordinal);
        Assert.False(line.Contains("jason-2026", StringComparison.Ordinal) || line.Contains("clean_backup", StringComparison.Ordinal), $"The line says what the record holds: {line}");
    }

    /// <summary>
    /// A record names the last migration its start meant to apply as well as the first. A start with nothing to migrate
    /// whose history holds the first, but which does not know the last, may be standing on a database a newer start was
    /// stopped inside: the record stays, the pointer to the copy from before that start with it.
    /// </summary>
    [Fact]
    public void A_record_whose_last_migration_this_version_does_not_know_survives_a_start_with_nothing_to_migrate()
    {
        using var dir = new TempDataDir();
        new DatabaseMigrator(dir.Paths).Migrate();
        var written = Marker(Path.Combine(dir.Paths.BackupsDirectory, "jason-20261021T000000Z-before-x.db"), First(dir), "20991231000000_FromALaterVersion", 0);
        File.WriteAllText(MarkerOf(dir), written);

        var report = new DatabaseMigrator(dir.Paths).Migrate();

        Assert.Empty(report.NewlyApplied);
        Assert.Equal(written, File.ReadAllText(MarkerOf(dir)));
    }

    /// <summary>
    /// A record that could not be deleted after its migration succeeded is said once in the log, without its text, and the
    /// copy it names is not let go of with the older backups: a record still on disk keeps the copy it points to.
    /// </summary>
    [Fact]
    public async Task A_record_that_cannot_be_deleted_after_a_success_is_said_and_its_copy_kept()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "A file held open without leave to delete it refuses its delete on Windows alone; elsewhere the delete goes through.");
        using var dir = new TempDataDir();
        await AtAsync(dir, "20260914051837_CampaignsContactsJournal");
        Directory.CreateDirectory(dir.Paths.BackupsDirectory);
        var clean = Path.Combine(dir.Paths.BackupsDirectory, "jason-20200101T000000Z-before-x.db");
        File.WriteAllText(clean, "the copy the record names");
        string pending;
        using (var db = new JasonDbContext(JasonDbContext.CreateOptions(dir.Paths.DatabaseFile)))
        {
            pending = db.Database.GetPendingMigrations().First();
        }

        File.WriteAllText(MarkerOf(dir), Marker(clean, pending, Last(dir), long.Parse(Scalar(dir.Paths.DatabaseFile, DatabaseMigrator.JournalHighWaterQuery), System.Globalization.CultureInfo.InvariantCulture)));
        var said = new Said();
        using var log = new Serilog.LoggerConfiguration().WriteTo.Sink(said).CreateLogger();
        using var held = new MarkerHeld(dir);

        var report = new DatabaseMigrator(dir.Paths) { Log = log, Interceptors = [held] }.Migrate();

        Assert.NotEmpty(report.NewlyApplied);
        var line = Assert.Single(said.Lines);
        Assert.Contains("could not be deleted", line, StringComparison.Ordinal);
        Assert.False(line.Contains("jason-2020", StringComparison.Ordinal), $"The line says what the record holds: {line}");
        Assert.True(File.Exists(clean), "The copy a record still on disk names was let go of.");
    }

    /// <summary>
    /// While a record that cannot be read is on disk, nobody can tell which backup it pointed to, so a start whose migration
    /// succeeds lets go of none of them: every backup is kept and the record is left as it is until a person looks.
    /// </summary>
    [Fact]
    public async Task No_backup_is_let_go_of_while_a_record_that_cannot_be_read_is_on_disk()
    {
        using var dir = new TempDataDir();
        await AtAsync(dir, "20260914051837_CampaignsContactsJournal");
        Directory.CreateDirectory(dir.Paths.BackupsDirectory);
        var older = new[] { "jason-20200101T000000Z-before-0_Old.db", "jason-20210101T000000Z-before-0_Old.db" }
            .Select(name => Path.Combine(dir.Paths.BackupsDirectory, name)).ToList();
        foreach (var file in older)
        {
            File.WriteAllText(file, "an older backup");
        }

        var whole = Marker(older[0], First(dir), Last(dir), 4);
        var truncated = whole[..(whole.Length / 2)];
        File.WriteAllText(MarkerOf(dir), truncated);

        var report = new DatabaseMigrator(dir.Paths).Migrate();

        Assert.NotEmpty(report.NewlyApplied);
        Assert.Equal(
            older.Append(report.BackupFile!).Order(StringComparer.Ordinal),
            Directory.GetFiles(dir.Paths.BackupsDirectory).Order(StringComparer.Ordinal));
        Assert.Equal(truncated, File.ReadAllText(MarkerOf(dir)));
    }

    [Fact]
    [Critical(Family.P2, "the oldest database reaches head")]
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

    /// <summary>A database at the migration named, as an installation that last ran the version that made it holds one.</summary>
    private static async Task AtAsync(TempDataDir dir, string migration)
    {
        Directory.CreateDirectory(dir.Paths.StateDirectory);
        await using var db = new JasonDbContext(JasonDbContext.CreateOptions(dir.Paths.DatabaseFile));
        await db.GetService<IMigrator>().MigrateAsync(migration, TestContext.Current.CancellationToken);
    }

    /// <summary>Stops the migration where a command's text holds the words.</summary>
    private sealed class Interruption(string words) : DbCommandInterceptor
    {
        public override InterceptionResult<int> NonQueryExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<int> result) =>
            command.CommandText.Contains(words, StringComparison.Ordinal)
                ? throw new InvalidOperationException("Interrupted where the test stops the migration.")
                : result;
    }

    /// <summary>
    /// What the record held when the migration ran its first command that changes something, and optionally the migration
    /// stopped where a command's text holds the words.
    /// </summary>
    private sealed class MarkerWatch(TempDataDir dir, string? stopAt = null) : DbCommandInterceptor
    {
        public string? Seen { get; private set; }

        private bool _read;

        public override InterceptionResult<int> NonQueryExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<int> result)
        {
            if (!_read)
            {
                _read = true;
                Seen = File.Exists(MarkerOf(dir)) ? File.ReadAllText(MarkerOf(dir)) : null;
            }

            return stopAt is not null && command.CommandText.Contains(stopAt, StringComparison.Ordinal)
                ? throw new InvalidOperationException("Interrupted where the test stops the migration.")
                : result;
        }
    }

    /// <summary>Every line the migrator logged, rendered.</summary>
    private sealed class Said : Serilog.Core.ILogEventSink
    {
        private readonly System.Collections.Concurrent.ConcurrentQueue<string> _lines = new();

        public IReadOnlyList<string> Lines => [.. _lines];

        public void Emit(Serilog.Events.LogEvent logEvent) => _lines.Enqueue(logEvent.RenderMessage(System.Globalization.CultureInfo.InvariantCulture));
    }

    private static string MarkerOf(TempDataDir dir) => new DatabaseMigrator(dir.Paths).MarkerFile;

    /// <summary>A record as a start that migrates writes it, by hand.</summary>
    private static string Marker(string clean, string firstPending, string lastPending, long highWater) =>
        new System.Text.Json.Nodes.JsonObject
        {
            ["clean_backup"] = clean,
            ["first_pending"] = firstPending,
            ["last_pending"] = lastPending,
            ["journal_high_water"] = highWater,
            ["written_at"] = "2026-10-21T00:00:00Z",
        }.ToJsonString();

    /// <summary>The copy a record names, or null where there is no record.</summary>
    private static string? CleanOf(string? record) =>
        record is null ? null : System.Text.Json.Nodes.JsonNode.Parse(record)!["clean_backup"]!.GetValue<string>();

    /// <summary>The last migration this build has.</summary>
    private static string Last(TempDataDir dir)
    {
        using var db = new JasonDbContext(JasonDbContext.CreateOptions(dir.Paths.DatabaseFile));
        return db.Database.GetMigrations().Last();
    }

    /// <summary>Holds the record open from the migration's first command on, without leave to delete it, until disposed.</summary>
    private sealed class MarkerHeld(TempDataDir dir) : DbCommandInterceptor, IDisposable
    {
        private FileStream? _held;

        public override InterceptionResult<int> NonQueryExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<int> result)
        {
            _held ??= new FileStream(MarkerOf(dir), FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            return result;
        }

        public void Dispose() => _held?.Dispose();
    }

    /// <summary>The first migration this build has.</summary>
    private static string First(TempDataDir dir)
    {
        using var db = new JasonDbContext(JasonDbContext.CreateOptions(dir.Paths.DatabaseFile));
        return db.Database.GetMigrations().First();
    }

    /// <summary>Every command the migrator's context ran that changes something, by its text.</summary>
    private sealed class Commands : DbCommandInterceptor
    {
        private readonly System.Collections.Concurrent.ConcurrentQueue<string> _texts = new();

        public int Count(string text) => _texts.Count(seen => string.Equals(seen, text, StringComparison.Ordinal));

        public override InterceptionResult<int> NonQueryExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<int> result)
        {
            _texts.Enqueue(command.CommandText);
            return result;
        }
    }

    /// <summary>A database that holds something already, as one made before Jason's first migration would.</summary>
    private static void Legacy(TempDataDir dir, string sql)
    {
        Directory.CreateDirectory(dir.Paths.StateDirectory);
        Execute(dir.Paths.DatabaseFile, sql);
    }

    /// <remarks>
    /// On the runtime's own connection string, whose pool the migrator lets go of before it puts a backup back: a pooled
    /// connection on another string would still hold the file open, and Windows refuses to replace a file held open.
    /// </remarks>
    private static void Execute(string databaseFile, string sql)
    {
        using var connection = new SqliteConnection(JasonDbContext.ConnectionString(databaseFile));
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private static string Scalar(string databaseFile, string sql)
    {
        using var connection = new SqliteConnection(JasonDbContext.ConnectionString(databaseFile));
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToString(command.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty;
    }
}
