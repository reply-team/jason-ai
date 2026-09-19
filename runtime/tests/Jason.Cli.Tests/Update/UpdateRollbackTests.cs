using Jason.Cli.Update;
using Jason.Contracts.Update;

namespace Jason.Cli.Tests.Update;

/// <summary>
/// Putting an update back: the previous executable always, and the database only while nothing has happened
/// since. This is where rolling a binary back and rolling a database back stop being the same thing.
/// </summary>
public class UpdateRollbackTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_rollback_puts_the_previous_executable_back_and_starts_it()
    {
        using var installation = await UpdatedAsync();

        var back = await installation.Rollback().RollBackAsync(Ct);

        Assert.Equal(installation.From.ToString(), installation.Installed());
        Assert.True(installation.Running, "a rollback left the machine with nothing running");
        Assert.Equal(installation.From, back.ToVersion);
    }

    /// <summary>
    /// PB2's case, and the one that made the amendment: the verb is typed long after the update finished, and
    /// everything it needs comes from the record that update left behind.
    /// </summary>
    [Fact]
    public async Task A_rollback_an_hour_after_a_completed_update_reads_the_record_that_update_left()
    {
        using var installation = await UpdatedAsync(migrates: true);
        Assert.Equal(UpdateStep.Complete, installation.Ledger()!.Step);

        // Nothing is asked of the update itself: it is over. The ledger is the only source.
        var recorded = installation.Ledger()!;
        Assert.NotEmpty(recorded.NewlyApplied);
        Assert.NotNull(recorded.BackupFile);
        Assert.NotNull(recorded.ChronicleId);

        var back = await installation.Rollback().RollBackAsync(Ct);

        Assert.Equal(installation.From.ToString(), installation.Installed());
        Assert.Equal(installation.From, back.ToVersion);
    }

    /// <summary>An update that migrated is undone whole: the backup comes back and its write-ahead log goes.</summary>
    [Fact]
    public async Task An_update_that_migrated_restores_the_backup_and_deletes_the_sidecars()
    {
        using var installation = await UpdatedAsync(migrates: true);
        installation.WriteDatabase("migrated by the new version");
        File.WriteAllText(installation.Paths.DatabaseFile + "-wal", "a log written against the new schema");
        File.WriteAllText(installation.Paths.DatabaseFile + "-shm", "shared memory");

        await installation.Rollback().RollBackAsync(Ct);

        Assert.Equal(FakeInstallation.BackupContent, File.ReadAllText(installation.Paths.DatabaseFile));
        Assert.False(File.Exists(installation.Paths.DatabaseFile + "-wal"), "a WAL from the new schema would be replayed into the restored file");
        Assert.False(File.Exists(installation.Paths.DatabaseFile + "-shm"));
    }

    /// <summary>
    /// And once the new version has been used, the database is not undone: restoring it would erase work
    /// somebody did. The binary still goes back, and the message says what was left and how to finish the job.
    /// </summary>
    [Fact]
    public async Task A_database_written_since_the_migration_refuses_the_restore_and_says_so()
    {
        using var installation = await UpdatedAsync(migrates: true);
        installation.WriteDatabase("work done under the new version");

        // A line in the chronicle that was not there when the update was declared healthy.
        installation.Chronicle = "jrn_01LATERLATERLATERLATERLATER";

        var refused = await Assert.ThrowsAsync<UpdateException>(() => installation.Rollback().RollBackAsync(Ct));

        Assert.Equal(UpdateCodes.RollbackUnsafe, refused.Code);
        Assert.Contains("state/jason.db", refused.Message, StringComparison.Ordinal);

        // The binary went back anyway, the runtime is up, and the database is untouched.
        Assert.Equal(installation.From.ToString(), installation.Installed());
        Assert.True(installation.Running, "the refusal was about the database, not about leaving the machine dead");
        Assert.Equal("work done under the new version", File.ReadAllText(installation.Paths.DatabaseFile));
    }

    /// <summary>
    /// PA3's case: the new version migrated and then never served, so nothing can be asked of it. The backup is
    /// found on disk instead — and the comparison is at the stamps' own resolution, so a backup written in the
    /// same second as the stop still belongs to this update.
    /// </summary>
    [Fact]
    public async Task A_new_binary_that_migrated_and_never_served_is_rolled_back_from_the_backups_directory()
    {
        using var installation = new FakeInstallation();
        installation.WithRuntime();

        // The update gets as far as starting the new version, which migrates and then does not answer.
        installation.StartsButNeverServes = true;
        await Assert.ThrowsAsync<UpdateException>(
            () => installation.Applier().ApplyAsync(new UpdateRequest(installation.Feed, null, TimeSpan.FromSeconds(2)), Ct));

        var ledger = installation.Ledger()!;
        Assert.Null(ledger.BackupFile);
        Assert.NotNull(ledger.StoppedAt);

        // What that start left behind: a backup stamped inside the same second the old runtime was stopped.
        var backup = installation.WriteBackup(ledger.StoppedAt!.Value);
        installation.WriteDatabase("migrated by a version that never served");

        await installation.Rollback().RollBackAsync(Ct);

        Assert.Equal(installation.From.ToString(), installation.Installed());
        Assert.Equal(FakeInstallation.BackupContent, File.ReadAllText(installation.Paths.DatabaseFile));
        Assert.False(File.Exists(backup + "-wal"));
    }

    [Fact]
    public async Task A_rollback_with_no_record_of_an_update_says_there_is_nothing_to_put_back()
    {
        using var installation = new FakeInstallation().WithRuntime();

        var refused = await Assert.ThrowsAsync<UpdateException>(() => installation.Rollback().RollBackAsync(Ct));

        Assert.Equal(UpdateCodes.NothingToRollBack, refused.Code);
    }

    private static async Task<FakeInstallation> UpdatedAsync(bool migrates = false)
    {
        var installation = new FakeInstallation { Migrates = migrates };
        installation.WithRuntime();
        await installation.Applier().ApplyAsync(new UpdateRequest(installation.Feed, null, TimeSpan.FromSeconds(5)), Ct);
        return installation;
    }
}
