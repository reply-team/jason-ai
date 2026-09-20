using System.Diagnostics;
using Jason.Cli.Update;
using Jason.Contracts.Update;
using Jason.Runtime.Tests;

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
    /// "Never healthy" is not "never served". An update whose new version came up, migrated, and then failed
    /// its health check leaves that runtime <b>running</b> — neither the start's timeout nor the version
    /// refusal stops it — and it goes on recording work through the new schema for as long as it takes somebody
    /// to read the message. Nothing recorded where the chronicle stood, because that is written at
    /// <c>healthy</c>; a rollback that read the missing id as "nothing has happened" copied the backup over
    /// that work and exited 0.
    /// </summary>
    /// <remarks>
    /// The same missing id covers the narrower case of a rollback typed while the new version is still
    /// migrating: no descriptor yet, so nothing is stopped, and the copy would land on a database mid-migration.
    /// A missing id is now its own answer — "this was never recorded" — and it is treated as the dangerous one.
    /// </remarks>
    [Fact]
    public async Task A_rollback_after_an_update_that_never_reached_healthy_leaves_the_database_alone()
    {
        using var installation = new FakeInstallation();
        installation.WithRuntime();

        // The new version comes up and answers as something else, which is what a health check is for. The
        // applier refuses the update - and leaves that runtime serving.
        installation.ServesVersion = installation.From.ToString();
        var failed = await Assert.ThrowsAsync<UpdateException>(
            () => installation.Applier().ApplyAsync(new UpdateRequest(installation.Feed, null, TimeSpan.FromSeconds(2)), Ct));

        Assert.Equal(UpdateCodes.NotHealthy, failed.Code);
        Assert.True(installation.Running, "the applier left nothing running, so this test is not the case it describes");

        // Nothing recorded where the chronicle stood, because that happens at `healthy`. What the start did
        // before it answered wrongly is on disk: it migrated the database and backed it up.
        var ledger = installation.Ledger()!;
        Assert.Null(ledger.ChronicleId);
        var backup = installation.WriteBackup(ledger.StoppedAt!.Value);

        // And it is used, because it is up: work recorded after the update failed.
        installation.WriteDatabase("work recorded by a version that was never declared healthy");
        var before = File.ReadAllBytes(installation.Paths.DatabaseFile);

        var refused = await Assert.ThrowsAsync<UpdateException>(() => installation.Rollback().RollBackAsync(Ct));

        Assert.Equal(UpdateCodes.RollbackUnsafe, refused.Code);
        Assert.Contains(Path.GetFileName(backup), refused.Message, StringComparison.Ordinal);
        Assert.Equal(before, File.ReadAllBytes(installation.Paths.DatabaseFile));

        // The half that is always safe still happened.
        Assert.Equal(installation.From.ToString(), installation.Installed());
    }

    /// <summary>
    /// PA3's case: the new version migrated and then never served, so nothing can be asked of it. The backup is
    /// found on disk instead — and the comparison is at the stamps' own resolution, so a backup written in the
    /// same second as the stop still belongs to this update.
    /// </summary>
    /// <remarks>
    /// Finding it is not restoring it. This update never reached a healthy runtime, so nothing recorded where
    /// the chronicle stood, and "never served" cannot be told from "served and was used" by anything on this
    /// machine: the same state is reached by a new version that answered as the wrong build and kept running.
    /// So the backups directory earns the backup its name in the refusal, and a person puts it back knowing
    /// what they are choosing.
    /// </remarks>
    [Fact]
    public async Task A_new_binary_that_migrated_and_never_served_names_the_backup_it_found_and_restores_nothing()
    {
        using var installation = new FakeInstallation();
        installation.WithRuntime();

        // The update gets as far as starting the new version, which migrates and then does not answer. Both
        // waits for it - the update's own and the rollback's below - are on a clock this test moves.
        installation.StartsButNeverServes = true;
        var clock = new FixedClock(new DateTimeOffset(2026, 9, 20, 4, 30, 0, TimeSpan.Zero));
        var applying = installation.Applier(clock).ApplyAsync(new UpdateRequest(installation.Feed, null, TimeSpan.FromSeconds(2)), Ct);
        await DriveAsync(clock, applying);
        await Assert.ThrowsAsync<UpdateException>(() => applying);

        var ledger = installation.Ledger()!;
        Assert.Null(ledger.BackupFile);
        Assert.NotNull(ledger.StoppedAt);

        // What that start left behind: a backup stamped inside the same second the old runtime was stopped.
        var backup = installation.WriteBackup(ledger.StoppedAt!.Value);
        installation.WriteDatabase("migrated by a version that never served");

        // Nothing this rollback starts will answer either, so its wait for one is moved by this test rather
        // than by a minute of somebody's afternoon.
        var rolling = installation.Rollback(clock).RollBackAsync(Ct);
        await DriveAsync(clock, rolling);

        var refused = await Assert.ThrowsAsync<UpdateException>(() => rolling);

        // Found, named, and left where it is - and the executable went back, as it always does.
        Assert.Equal(UpdateCodes.RollbackUnsafe, refused.Code);
        Assert.Contains(Path.GetFileName(backup), refused.Message, StringComparison.Ordinal);
        Assert.Equal("migrated by a version that never served", File.ReadAllText(installation.Paths.DatabaseFile));
        Assert.Equal(installation.From.ToString(), installation.Installed());
    }

    /// <summary>
    /// A rollback that cannot ask whether work has been done does not assume the answer it prefers. With the
    /// runtime down — stopped by hand, crashed, rebooted — the chronicle cannot be read, so restoring a backup
    /// over a database that has been used since would erase work with nothing to warn anybody.
    /// </summary>
    /// <remarks>
    /// This is the shape of the defect: "has the chronicle moved?" answered *no* when the real answer was
    /// "cannot tell". The binary still goes back, because that half is always safe; the database is left exactly
    /// as it stands and the refusal names the backup, so a person who does want yesterday's database can put it
    /// back by hand knowing what they are choosing.
    /// </remarks>
    [Fact]
    public async Task A_rollback_that_cannot_ask_whether_work_was_done_leaves_the_database_alone()
    {
        using var installation = await UpdatedAsync(migrates: true);
        installation.WriteDatabase("work done under the new version");
        var before = File.ReadAllBytes(installation.Paths.DatabaseFile);

        // And now nobody is there to ask.
        installation.RuntimeGoesAway();

        var refused = await Assert.ThrowsAsync<UpdateException>(() => installation.Rollback().RollBackAsync(Ct));

        Assert.Equal(UpdateCodes.RollbackUnsafe, refused.Code);
        Assert.Contains("before-20260920000000_Next.db", refused.Message, StringComparison.Ordinal);
        Assert.Equal(before, File.ReadAllBytes(installation.Paths.DatabaseFile));

        // The half that is always safe still happened.
        Assert.Equal(installation.From.ToString(), installation.Installed());
    }

    /// <summary>
    /// A write-ahead log that will not move stops the restore before the database moves, and the message is
    /// true when it says the database was left as it is.
    /// </summary>
    /// <remarks>
    /// The sidecars used to be deleted <em>after</em> the backup was already installed, inside the same try.
    /// A delete that refused there left the file from before the update in place with a log written against the
    /// new schema beside it - which this product's own page calls corruption - while the refusal said the
    /// database was untouched, the ledger was written back claiming no restore, and a runtime was started on
    /// the pair. They are dealt with first now, and a refusal here happens with nothing moved at all.
    /// </remarks>
    [Fact]
    public async Task A_sidecar_that_will_not_move_stops_the_restore_before_the_database_does()
    {
        using var installation = await UpdatedAsync(migrates: true);
        installation.WriteDatabase("migrated by the new version");
        var log = installation.Paths.DatabaseFile + "-wal";
        File.WriteAllText(log, "a log written against the new schema");
        var before = File.ReadAllBytes(installation.Paths.DatabaseFile);

        // Nothing is renamed onto a directory with a file in it, on any platform this ships to.
        Directory.CreateDirectory(log + ".rolling");
        File.WriteAllText(Path.Combine(log + ".rolling", "occupied"), "not empty");

        var refused = await Assert.ThrowsAsync<UpdateException>(() => installation.Rollback().RollBackAsync(Ct));

        Assert.Equal(UpdateCodes.FileRefused, refused.Code);
        Assert.Equal(before, File.ReadAllBytes(installation.Paths.DatabaseFile));
        Assert.Equal("a log written against the new schema", File.ReadAllText(log));

        // The half that is always safe still happened, and the machine is not left dead.
        Assert.Equal(installation.From.ToString(), installation.Installed());
        Assert.True(installation.Running, "the refusal was about the database, not about leaving the machine dead");
    }

    /// <summary>
    /// The defect itself, in the shape a machine really reaches it: a runtime is still holding the write-ahead
    /// log open when the rollback gets to it. The backup was installed first and the delete then refused, so
    /// the database from before the update sat there with a log written against the new schema beside it - and
    /// the refusal a person read said the database had been left exactly as it was.
    /// </summary>
    /// <remarks>
    /// Windows only, because this is a Windows fault: an open file cannot be renamed or deleted there, and on
    /// Unix both succeed while the handle stays valid. The two tests beside this one carry the same rule on
    /// every platform, built out of directories rather than handles.
    /// </remarks>
    [Fact]
    public async Task A_log_still_held_open_stops_the_restore_before_the_database_moves()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Only Windows refuses to rename or delete a file that is open, which is what this is about.");

        using var installation = await UpdatedAsync(migrates: true);
        installation.WriteDatabase("migrated by the new version");
        var log = installation.Paths.DatabaseFile + "-wal";
        File.WriteAllText(log, "a log written against the new schema");
        var before = File.ReadAllBytes(installation.Paths.DatabaseFile);

        UpdateException refused;
        using (var held = new FileStream(log, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            refused = await Assert.ThrowsAsync<UpdateException>(() => installation.Rollback().RollBackAsync(Ct));
        }

        Assert.Equal(UpdateCodes.FileRefused, refused.Code);

        // The sentence the refusal makes, made true: nothing moved.
        Assert.Equal(before, File.ReadAllBytes(installation.Paths.DatabaseFile));
        Assert.Equal("a log written against the new schema", File.ReadAllText(log));
        Assert.Equal(installation.From.ToString(), installation.Installed());
    }

    /// <summary>
    /// And a copy that fails after the sidecars were set aside puts them back: the database beside them is
    /// still the migrated one, and that log is still its own.
    /// </summary>
    /// <remarks>
    /// This is a guard on the order the test above forces, not on anything the old code did: with no aside
    /// there was nothing to put back. Removing the unwind makes it red, which is what it is for - a log left at
    /// <c>jason.db-wal.rolling</c> is invisible to SQLite, so the committed transactions in it are gone from a
    /// database that is still the one they belong to.
    /// </remarks>
    [Fact]
    public async Task A_copy_that_fails_puts_the_write_ahead_log_back()
    {
        using var installation = await UpdatedAsync(migrates: true);
        installation.WriteDatabase("migrated by the new version");
        var log = installation.Paths.DatabaseFile + "-wal";
        File.WriteAllText(log, "a log written against the new schema");
        var before = File.ReadAllBytes(installation.Paths.DatabaseFile);

        // The backup is copied beside the database before it is renamed onto it; a directory at that name
        // refuses the copy, after the sidecars have been moved out of the way.
        var arriving = installation.Paths.DatabaseFile + ".restoring";
        Directory.CreateDirectory(arriving);
        File.WriteAllText(Path.Combine(arriving, "occupied"), "not empty");

        var refused = await Assert.ThrowsAsync<UpdateException>(() => installation.Rollback().RollBackAsync(Ct));

        Assert.Equal(UpdateCodes.FileRefused, refused.Code);
        Assert.Equal(before, File.ReadAllBytes(installation.Paths.DatabaseFile));
        Assert.Equal("a log written against the new schema", File.ReadAllText(log));
        Assert.False(File.Exists(log + ".rolling"), "the log was set aside and left there");
    }

    /// <summary>
    /// The record this rollback leaves behind is written with a filesystem call like every other, and it
    /// refuses for the same reasons — so it refuses the same way: with a code, and never in place of the
    /// refusal about the database, which is the message somebody is actually waiting for.
    /// </summary>
    [Fact]
    public async Task A_ledger_that_cannot_be_written_back_does_not_replace_the_refusal_about_the_database()
    {
        using var installation = await UpdatedAsync(migrates: true);
        installation.WriteDatabase("work done under the new version");
        installation.Chronicle = "jrn_01LATERLATERLATERLATERLATER";

        // The name the ledger's own write renames from. A directory there refuses it on every platform.
        Directory.CreateDirectory(installation.Update.Ledger + ".writing");
        File.WriteAllText(Path.Combine(installation.Update.Ledger + ".writing", "occupied"), "not empty");

        var refused = await Assert.ThrowsAsync<UpdateException>(() => installation.Rollback().RollBackAsync(Ct));

        // The database's refusal, not the bookkeeping file's - and the bookkeeping file is named in it too.
        Assert.Equal(UpdateCodes.RollbackUnsafe, refused.Code);
        Assert.Contains("before-20260920000000_Next.db", refused.Message, StringComparison.Ordinal);
        Assert.Contains(installation.Update.Ledger, refused.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// And when there is no other refusal to make way for, the ledger's own is raised with a code of its own
    /// rather than as a bare line from the filesystem.
    /// </summary>
    [Fact]
    public async Task A_record_that_cannot_be_removed_is_refused_with_a_code()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "A read-only file in a writable directory deletes happily on Unix, so there is nothing to refuse there.");

        using var installation = await UpdatedAsync(migrates: true);
        installation.WriteDatabase("migrated by the new version");

        // The update was undone whole, so its record is deleted - and this one will not be.
        File.SetAttributes(installation.Update.Ledger, FileAttributes.ReadOnly);
        var rollback = installation.Rollback();

        var refused = await Assert.ThrowsAsync<UpdateException>(() => rollback.RollBackAsync(Ct));

        Assert.Equal(UpdateCodes.FileRefused, refused.Code);
        Assert.Contains(installation.Update.Ledger, refused.Message, StringComparison.Ordinal);

        // What the rollback did is still there to be printed beside the refusal.
        Assert.Contains(rollback.Steps, step => step.StartsWith("restored the database", StringComparison.Ordinal));

        File.SetAttributes(installation.Update.Ledger, FileAttributes.Normal);
    }

    /// <summary>
    /// The image a rollback moves aside is named for the version and the moment, and that name may already be
    /// taken: an earlier rollback of the same version, in the same moment, left one there and could not delete
    /// it because that version was still running. A name it cannot have is not a reason to refuse — least of
    /// all here, after the runtime has been stopped and before anything has been put back.
    /// </summary>
    /// <remarks>
    /// Writing over the file instead would be the wrong repair twice over: it is exactly the image that may
    /// still be running, and Windows refuses to write over a running one anyway, which is the fault this whole
    /// rename exists to avoid.
    /// </remarks>
    [Fact]
    public async Task A_rollback_whose_name_is_already_taken_takes_another_one()
    {
        using var installation = await UpdatedAsync();
        var at = new DateTimeOffset(2026, 9, 20, 4, 30, 0, TimeSpan.Zero);

        Directory.CreateDirectory(installation.Update.Replaced);
        var earlier = Path.Combine(
            installation.Update.Replaced,
            UpdateRollback.AsideName(installation.To, at, ReleaseAssets.ExecutableName));
        const string Image = "an image an earlier rollback moved aside and could not delete";
        File.WriteAllText(earlier, Image);

        await new UpdateRollback(installation.Env, installation.Update, new FixedClock(at)).RollBackAsync(Ct);

        Assert.Equal(installation.From.ToString(), installation.Installed());
        Assert.Equal(Image, File.ReadAllText(earlier));
    }

    /// <summary>
    /// A rollback starts the version it put back and waits a minute for it to answer. When nothing ever does,
    /// it says so and leaves the machine to somebody — and the minute it waits is a minute on the clock it was
    /// handed, which a test moves in microseconds.
    /// </summary>
    [Fact]
    public async Task A_rollback_whose_runtime_never_answers_gives_up_on_the_clock_it_was_given()
    {
        using var installation = await UpdatedAsync();

        // Whatever is started from here on comes up and never listens.
        installation.StartsButNeverServes = true;

        var clock = new FixedClock(new DateTimeOffset(2026, 9, 20, 4, 30, 0, TimeSpan.Zero));
        var elapsed = Stopwatch.StartNew();
        var rollback = installation.Rollback(clock);
        var rolling = rollback.RollBackAsync(Ct);
        await DriveAsync(clock, rolling);

        await rolling;
        Assert.Equal(installation.From.ToString(), installation.Installed());
        Assert.Contains(rollback.Steps, step => step.Contains("no runtime answered", StringComparison.Ordinal));
        Assert.True(elapsed.Elapsed < TimeSpan.FromSeconds(10), $"the rollback waited on the real clock for {elapsed.Elapsed}");
    }

    /// <summary>
    /// Moves the clock a verb is asleep on until it stops being asleep. Nothing else moves it, so the minute an
    /// update or a rollback waits for a runtime that never answers passes here in about as long as it takes to
    /// say so.
    /// </summary>
    private static async Task DriveAsync(FixedClock clock, Task waiting)
    {
        for (var tick = 0; !waiting.IsCompleted && tick < 100_000; tick++)
        {
            clock.Advance(TimeSpan.FromSeconds(1));
            await Task.Yield();
        }

        Assert.True(waiting.IsCompleted, "the wait for a runtime to answer never ended");
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
