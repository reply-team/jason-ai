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
/// directory, so "nothing was changed" is true of the data: no file is added or removed, and the database and its log keep
/// every byte; where a runtime left a log, SQLite may rebuild its index of it (<c>jason.db-shm</c>), and that file alone
/// moves. EF Core itself ignores history rows it has no class for and would run this build's model over the newer schema.
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
    /// The commonest refusing state: a runtime killed by a shutdown, its database with the write-ahead log and the shared
    /// memory it left, and no connection open. The guard's connection is then the only one and the last to close, which is
    /// where SQLite checkpoints a log and deletes it. The guard reads the newer row in the log, and the database and the log
    /// keep every byte and their last write, and no file is added or removed. The shared memory alone may be rebuilt, being
    /// SQLite's index of the log: measured, it is (its bytes and last write change, its size does not).
    /// </summary>
    [Fact]
    [Critical(Family.P2, "a killed newer runtime's database, log and shared memory are read and left as they were")]
    public void A_killed_runtimes_log_and_shared_memory_are_read_and_left_as_they_were()
    {
        using var source = new TempDataDir();
        using var crashed = new TempTree();
        new DatabaseMigrator(source.Paths).Migrate();
        var copy = Path.Combine(crashed.Root, "jason.db");
        using (var writer = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = source.Paths.DatabaseFile, Pooling = false }.ToString()))
        {
            writer.Open();
            using (var command = writer.CreateCommand())
            {
                command.CommandText = "PRAGMA wal_autocheckpoint=0;";
                command.ExecuteNonQuery();
            }

            Insert(writer, NewerMigration);

            // The three files as a writer that never closed leaves them: copied while it holds them.
            foreach (var suffix in new[] { "", "-wal", "-shm" })
            {
                using var from = new FileStream(source.Paths.DatabaseFile + suffix, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                using var to = File.Create(copy + suffix);
                from.CopyTo(to);
            }
        }

        Assert.True(new FileInfo(copy + "-wal").Length > 0, "the test's premise: the row is in the write-ahead log.");
        Assert.True(File.Exists(copy + "-shm"), "the test's premise: the shared memory a killed writer leaves.");
        var before = Snapshot(crashed.Root);

        Assert.Equal([NewerMigration], NewerDatabase.Unknown(copy));
        SqliteConnection.ClearAllPools();

        var after = Snapshot(crashed.Root);
        Assert.Equal(["jason.db", "jason.db-shm", "jason.db-wal"], after.Keys);
        Assert.Equal(before.Keys, after.Keys);
        Assert.Equal(before["jason.db"], after["jason.db"]);
        Assert.Equal(before["jason.db-wal"], after["jason.db-wal"]);
        Assert.Equal(before["jason.db-shm"].Split(' ')[0], after["jason.db-shm"].Split(' ')[0]);
    }

    /// <summary>
    /// Every migration id a release has shipped, in order. A database an installation holds carries these in its history,
    /// so a build that no longer knew one of them, by a squash, a rename or a delete, would refuse every installation with
    /// a false "changed by a newer Jason". The list is only ever appended to, by the release that ships a new migration.
    /// </summary>
    private static readonly string[] Shipped =
    [
        "20260913233702_InitialCreate",
        "20260914051837_CampaignsContactsJournal",
        "20260914104855_WorkItemsAttemptsRoles",
        "20260915234613_RoutingProvenanceExternalIds",
        "20260917063114_Approvals",
        "20260917203003_Reports",
        "20260918040001_ExecutionProfiles",
        "20260918074712_RoleNotes",
        "20260919040652_CampaignManagerLoop",
        "20260919064112_ManagerEscalation",
        "20261005204228_IdempotencyKeys",
        "20261006011312_InboundSync",
        "20261016181342_UiTokens",
        "20261018190303_EventStore",
        "20261018230454_EventReads",
        "20261019053859_LinkedInSlugKeys",
        "20261019060108_EventBackfills",
        "20261020003554_WorkItemsByFinish",
        "20261020050039_LinkedInOneKey",
        "20261020172838_JournalByPerson",
        "20261022011335_ProjectsAndPreferences",
        "20261023115917_CampaignPeopleIndexes",
        "20261023172016_CampaignWorkByFinish",
        "20261024123426_WorkItemWaits",
        "20261024194108_ProviderBlocks",
        "20261025010407_ProviderBlockClearedBy",
        "20261025231527_ProviderBlockListingIndex",
        "20261026142432_ProfileTools",
        "20261029150321_SentFacts",
        "20261102051342_ResponderWatermark",
        "20261105002401_SubscriptionRunner",
        "20261111143412_Audience",
        "20261113000729_ChannelRoutes",
        "20261113021003_JournalByThread",
        "20261114031653_JournalVerdictsByThread",
        "20261114050917_CampaignSendModes",
        "20261114064450_WorkItemDoubtNotes",
        "20261114084851_ApprovalsAutomaticByDay",
        "20261114190252_ProviderBlockChannels",
        "20261114215710_BounceStep",
        "20261116022841_ProviderSends",
        "20261116053811_DecisionThread",
    ];

    /// <summary>
    /// Migration ids are append-only: this build knows every id a release shipped. And 0.49 adds no migration, so 0.48 runs
    /// on 0.49's data with the same schema: the last migration this build knows is the last one 0.48 shipped. A release that
    /// adds a migration appends its id to <see cref="Shipped"/>.
    /// </summary>
    [Fact]
    [Critical(Family.P2, "every shipped migration id is still known, so no installation is refused as newer by mistake")]
    public void Every_migration_a_release_shipped_is_known_and_none_was_added_since()
    {
        Assert.Subset(NewerDatabase.Known.ToHashSet(StringComparer.Ordinal), Shipped.ToHashSet(StringComparer.Ordinal));
        Assert.Equal(Shipped.Order(StringComparer.Ordinal), Shipped);
        Assert.Equal(Shipped[^1], NewerDatabase.Known.Max(StringComparer.Ordinal));
    }

    /// <summary>
    /// A history row the guard cannot read, here a hand-made table whose id is NULL, is not a refusal and not a crash: the
    /// start goes on as it did before the guard, and the migrator says what it says.
    /// </summary>
    [Fact]
    public void A_history_row_it_cannot_read_is_left_to_the_start()
    {
        using var dir = new TempDataDir();
        Directory.CreateDirectory(dir.Paths.StateDirectory);
        using (var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = dir.Paths.DatabaseFile, Pooling = false }.ToString()))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "CREATE TABLE \"__EFMigrationsHistory\" (\"MigrationId\" TEXT, \"ProductVersion\" TEXT); "
                + "INSERT INTO \"__EFMigrationsHistory\" VALUES (NULL, '99.0.0');";
            command.ExecuteNonQuery();
        }

        Assert.Null(NewerDatabase.Unknown(dir.Paths.DatabaseFile));
        Assert.Null(NewerDatabase.Refusal(dir.Paths, "0.49.0"));
    }

    /// <summary>
    /// A build whose own migrations cannot be listed, as a type that fails to initialise would leave it, refuses nothing:
    /// it cannot tell a newer row from its own.
    /// </summary>
    [Fact]
    public void A_build_whose_migrations_cannot_be_listed_refuses_nothing()
    {
        using var dir = new TempDataDir();
        new DatabaseMigrator(dir.Paths).Migrate();
        Applied(dir.Paths.DatabaseFile, NewerMigration);

        Assert.Null(NewerDatabase.Unknown(dir.Paths.DatabaseFile, () => throw new TypeInitializationException(typeof(NewerDatabase).FullName, null)));
    }

    /// <summary>
    /// A path SQLite's address cannot be made from, as a Windows path that names the device namespace, is not a crash of the
    /// start: the guard falls back, and the start goes on as it did before it.
    /// </summary>
    [Fact]
    public void A_path_it_cannot_make_an_address_of_is_left_to_the_start()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Only a Windows path can name the device namespace.");
        using var dir = new TempDataDir();
        new DatabaseMigrator(dir.Paths).Migrate();
        Applied(dir.Paths.DatabaseFile, NewerMigration);
        SqliteConnection.ClearAllPools();
        var device = @"\\?\" + Path.GetFullPath(dir.Paths.DatabaseFile);
        Assert.True(File.Exists(device), "the test's premise: the path names the database.");

        Assert.Null(NewerDatabase.Unknown(device));
        Assert.Equal(["jason.db"], Directory.EnumerateFileSystemEntries(dir.Paths.StateDirectory).Select(Path.GetFileName));
    }

    /// <summary>
    /// A database another connection holds exclusively is busy: the guard does not wait the half minute a command waits by
    /// default, and leaves it to the start, which the instance lock refuses where a runtime holds it.
    /// </summary>
    [Fact]
    public void A_busy_database_is_left_to_the_start_without_waiting_long()
    {
        using var dir = new TempDataDir();
        new DatabaseMigrator(dir.Paths).Migrate();
        SqliteConnection.ClearAllPools();
        using var holder = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = dir.Paths.DatabaseFile, Pooling = false }.ToString());
        holder.Open();
        using (var command = holder.CreateCommand())
        {
            command.CommandText = "PRAGMA wal_autocheckpoint=0; PRAGMA locking_mode=EXCLUSIVE;";
            command.ExecuteNonQuery();
        }

        Insert(holder, NewerMigration);
        Assert.True(File.Exists(dir.Paths.DatabaseFile + "-wal"), "the test's premise: the database has a write-ahead log.");
        var clock = System.Diagnostics.Stopwatch.StartNew();

        Assert.Null(NewerDatabase.Unknown(dir.Paths.DatabaseFile));
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(10), $"the guard waited {clock.Elapsed} on a busy database.");
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
