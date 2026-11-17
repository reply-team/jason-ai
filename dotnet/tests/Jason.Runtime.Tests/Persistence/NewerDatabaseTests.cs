using System.Security.Cryptography;
using System.Text.Json;
using Jason.Contracts;
using Jason.Contracts.Discovery;
using Jason.Runtime.Hosting;
using Jason.Runtime.Persistence;
using Microsoft.Data.Sqlite;

namespace Jason.Runtime.Tests.Persistence;

/// <summary>
/// The guard against a database a newer Jason migrated: a start whose database holds an applied migration this build does
/// not know refuses with <c>data_newer</c> before the migrator, the lock, the log or anything else touches the data
/// directory, so "nothing was changed" is true of every byte in it. EF Core itself ignores history rows it has no class for
/// and would run this build's model over the newer schema.
/// </summary>
public class NewerDatabaseTests
{
    private const string NewerMigration = "29991231000000_FromANewerJason";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    [Critical(Family.P2, "a database a newer Jason migrated is refused before anything is written")]
    public async Task A_database_holding_a_migration_this_build_does_not_know_refuses_the_start_and_changes_nothing()
    {
        using var dir = new TempDataDir();
        new DatabaseMigrator(dir.Paths).Migrate();
        Applied(dir.Paths.DatabaseFile, NewerMigration);
        SqliteConnection.ClearAllPools();

        // The premise that makes the comparison below mean something: no connection of this process holds the database, so
        // its write-ahead log and shared memory are gone, and a read that made either would show.
        Assert.False(File.Exists(dir.Paths.DatabaseFile + "-wal"), "the test's premise: the write-ahead log was checkpointed away.");
        Assert.False(File.Exists(dir.Paths.DatabaseFile + "-shm"), "the test's premise: the shared memory file was removed.");
        var before = Snapshot(dir.Paths.Root);
        using var refusals = new StringWriter();

        // Bounded: a start that is not refused runs in the foreground until it is told to stop, and then answers 0.
        using var bound = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        bound.CancelAfter(TimeSpan.FromMinutes(1));
        var exit = await RuntimeHost.RunAsync(dir.Paths, TestRuntimeOptions.Quiet with { Refusals = refusals }, bound.Token);

        Assert.Equal(1, exit);
        using var envelope = JsonDocument.Parse(refusals.ToString());
        var error = envelope.RootElement.GetProperty("error");
        Assert.Equal("data_newer", error.GetProperty("code").GetString());
        Assert.Equal(DataNewer.Sentence(JasonVersion.Current, dir.Paths.Root), error.GetProperty("message").GetString());
        Assert.False(error.GetProperty("retryable").GetBoolean());

        // Nothing at all: no lock, no log, no folder of the layout, no backup, no migration record, no sidecar of the
        // database, and the database the same bytes.
        Assert.Equal(before, Snapshot(dir.Paths.Root));
    }

    /// <summary>A caller of the start itself, as a test or the desktop's own process would be, is given the refusal by its type.</summary>
    [Fact]
    public async Task The_start_throws_the_refusal_with_its_code_and_envelope()
    {
        using var dir = new TempDataDir();
        new DatabaseMigrator(dir.Paths).Migrate();
        Applied(dir.Paths.DatabaseFile, NewerMigration);

        var refusal = await Assert.ThrowsAsync<DataNewerRefusalException>(() => RuntimeHost.StartAsync(dir.Paths, TestRuntimeOptions.Quiet, Ct));

        Assert.Equal(DataNewer.Code, refusal.Code);
        Assert.Equal(DataNewer.Sentence(JasonVersion.Current, dir.Paths.Root), refusal.Message);
        using var envelope = JsonDocument.Parse(refusal.Envelope);
        Assert.Equal("data_newer", envelope.RootElement.GetProperty("error").GetProperty("code").GetString());
        Assert.False(Directory.Exists(dir.Paths.LogsDirectory), "the refused start wrote a log.");
        Assert.False(Directory.Exists(dir.Paths.RunDirectory), "the refused start took the lock.");
    }

    /// <summary>
    /// What the guard knows is what the migrator applies: the migrations of this build, every one of them, as a fresh
    /// database records them.
    /// </summary>
    [Fact]
    public void The_migrations_known_are_the_ones_this_build_applies()
    {
        using var dir = new TempDataDir();

        var applied = new DatabaseMigrator(dir.Paths).Migrate().AppliedMigrations;

        Assert.Equal(applied.Order(StringComparer.Ordinal), NewerDatabase.Known.Order(StringComparer.Ordinal));
        Assert.Empty(NewerDatabase.Unknown(dir.Paths.DatabaseFile)!);
        Assert.Null(NewerDatabase.Refusal(dir.Paths, "0.49.0"));
    }

    [Fact]
    public void The_unknown_migrations_are_named_and_the_refusal_said_for_the_version_given()
    {
        using var dir = new TempDataDir();
        new DatabaseMigrator(dir.Paths).Migrate();
        Applied(dir.Paths.DatabaseFile, NewerMigration, "29991231000001_AndAnother");

        Assert.Equal([NewerMigration, "29991231000001_AndAnother"], NewerDatabase.Unknown(dir.Paths.DatabaseFile));
        Assert.Equal((DataNewer.Code, DataNewer.Sentence("0.49.0", dir.Paths.Root)), NewerDatabase.Refusal(dir.Paths, "0.49.0"));
    }

    /// <summary>
    /// A newer runtime still running, or one that ended with its last transactions in the write-ahead log, has history rows
    /// the main file does not hold yet: they are read as committed, never skipped.
    /// </summary>
    [Fact]
    public void A_row_still_in_the_write_ahead_log_is_read()
    {
        using var dir = new TempDataDir();
        new DatabaseMigrator(dir.Paths).Migrate();
        using var writer = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = dir.Paths.DatabaseFile, Pooling = false }.ToString());
        writer.Open();
        using (var command = writer.CreateCommand())
        {
            command.CommandText = "PRAGMA wal_autocheckpoint=0;";
            command.ExecuteNonQuery();
        }

        Insert(writer, NewerMigration);
        var files = Directory.EnumerateFileSystemEntries(dir.Paths.StateDirectory).Order(StringComparer.Ordinal).ToList();

        Assert.True(new FileInfo(dir.Paths.DatabaseFile + "-wal").Length > 0, "the test's premise: the row is in the write-ahead log.");
        Assert.Equal([NewerMigration], NewerDatabase.Unknown(dir.Paths.DatabaseFile));
        Assert.Equal(files, Directory.EnumerateFileSystemEntries(dir.Paths.StateDirectory).Order(StringComparer.Ordinal));
    }

    /// <summary>
    /// A database with no write-ahead log is read by its address as a file, so a data directory whose path holds what an
    /// address escapes is read all the same: a space, a hash, a percent sign and a letter outside ASCII.
    /// </summary>
    [Fact]
    public void A_data_directory_whose_path_an_address_escapes_is_read()
    {
        using var tree = new TempTree();
        var paths = new JasonPaths(Path.Combine(tree.Root, "my data #1 100% été"), 1);
        new DatabaseMigrator(paths).Migrate();
        Applied(paths.DatabaseFile, NewerMigration);
        SqliteConnection.ClearAllPools();
        Assert.False(File.Exists(paths.DatabaseFile + "-wal"), "the test's premise: the database has no write-ahead log.");

        Assert.Equal([NewerMigration], NewerDatabase.Unknown(paths.DatabaseFile));
        Assert.Equal(["jason.db"], Directory.EnumerateFileSystemEntries(paths.StateDirectory).Select(Path.GetFileName));
    }

    /// <summary>
    /// What the guard cannot read it leaves to the start as it was before the guard: no database is a new installation, one
    /// with no history is migrated from the start, and a file that is not a database is the migrator's to refuse in its own
    /// words. The guard writes none of them.
    /// </summary>
    [Fact]
    public void No_database_no_history_and_no_database_file_are_not_refused_and_not_written()
    {
        using var dir = new TempDataDir();
        Assert.Null(NewerDatabase.Unknown(dir.Paths.DatabaseFile));
        Assert.Null(NewerDatabase.Refusal(dir.Paths, "0.49.0"));
        Assert.False(File.Exists(dir.Paths.DatabaseFile), "the guard made a database.");

        Directory.CreateDirectory(dir.Paths.StateDirectory);
        using (var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = dir.Paths.DatabaseFile, Pooling = false }.ToString()))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "CREATE TABLE legacy (x INTEGER);";
            command.ExecuteNonQuery();
        }

        Assert.Empty(NewerDatabase.Unknown(dir.Paths.DatabaseFile)!);
        Assert.Null(NewerDatabase.Refusal(dir.Paths, "0.49.0"));

        File.WriteAllText(dir.Paths.DatabaseFile, "this is not a database, and is long enough to be read as a header by SQLite.");
        var before = File.ReadAllBytes(dir.Paths.DatabaseFile);
        Assert.Null(NewerDatabase.Unknown(dir.Paths.DatabaseFile));
        Assert.Null(NewerDatabase.Refusal(dir.Paths, "0.49.0"));
        Assert.Equal(before, File.ReadAllBytes(dir.Paths.DatabaseFile));
        Assert.Equal(["jason.db"], Directory.EnumerateFileSystemEntries(dir.Paths.StateDirectory).Select(Path.GetFileName));
    }

    /// <summary>Rows written as EF Core writes its history, by a connection that is closed again at once.</summary>
    private static void Applied(string databaseFile, params string[] migrations)
    {
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = databaseFile, Pooling = false }.ToString());
        connection.Open();
        foreach (var migration in migrations)
        {
            Insert(connection, migration);
        }
    }

    private static void Insert(SqliteConnection connection, string migration)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO \"__EFMigrationsHistory\" (\"MigrationId\", \"ProductVersion\") VALUES ($id, '99.0.0');";
        command.Parameters.AddWithValue("$id", migration);
        command.ExecuteNonQuery();
    }

    /// <summary>Every folder and file under the data directory, each file by its size, its last write and its digest.</summary>
    private static SortedDictionary<string, string> Snapshot(string root)
    {
        var entries = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var path in Directory.EnumerateFileSystemEntries(root, "*", SearchOption.AllDirectories))
        {
            var name = Path.GetRelativePath(root, path);
            if (Directory.Exists(path))
            {
                entries[name] = "folder";
                continue;
            }

            var file = new FileInfo(path);
            entries[name] = $"{file.Length} {file.LastWriteTimeUtc:O} {Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)))}";
        }

        return entries;
    }
}
