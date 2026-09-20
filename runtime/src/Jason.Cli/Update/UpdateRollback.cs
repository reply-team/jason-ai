using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Jason.Cli.Commands;
using Jason.Cli.Discovery;
using Jason.Cli.Process;
using Jason.Contracts.Api;
using Jason.Contracts.Json;
using Jason.Contracts.Update;

namespace Jason.Cli.Update;

/// <summary>
/// Putting an update back. The previous executable returns to the install path, and — only if the update's own
/// first start migrated the database — the backup that start wrote is restored with it.
/// </summary>
/// <remarks>
/// <para>
/// This is where rolling a binary back and rolling a database back stop being the same thing. The binary can
/// always go back: it is one rename of a file this update kept. The database cannot, once the new version has
/// been used — restoring a backup then deletes whatever was done since, which is somebody's work. So the
/// database is restored only while the evidence says nothing has happened, and the refusal is not a failure of
/// the rollback: the binary goes back either way, and the message says exactly what was left as it was. Evidence
/// means an update that reached <c>healthy</c>, recorded where the chronicle stood, and a chronicle that still
/// stands there; everything else - a chronicle that moved, a runtime that cannot be asked, an update that never
/// got that far - leaves the database alone.
/// </para>
/// <para>
/// Nothing here opens the database. It renames one file, copies another over a third, and takes two sidecars
/// out of the way — because a WAL written by the new schema against a restored older file is corruption. Those
/// two are the only part of this that cannot be undone by putting a file back, so they are moved aside before
/// anything else moves and deleted only once the database they belonged to is gone.
/// </para>
/// </remarks>
public sealed class UpdateRollback(CliEnvironment env, UpdatePaths update, TimeProvider clock)
{
    /// <summary>Everything the rollback did, in order, for the caller to print.</summary>
    public List<string> Steps { get; } = [];

    public async Task<UpdateLedger> RollBackAsync(CancellationToken cancellationToken)
    {
        var ledger = UpdateLedger.ReadFile(update.Ledger)
            ?? throw new UpdateException(
                UpdateCodes.NothingToRollBack,
                $"There is no record of an update at {update.Ledger}, so there is nothing to put back.");

        if (!File.Exists(ledger.PreviousPath))
        {
            throw new UpdateException(
                UpdateCodes.NothingToRollBack,
                $"The executable this update replaced is not at {ledger.PreviousPath}, so there is nothing to put back.");
        }

        // Asked while the new version is still the one answering: after the swap below there is nothing to ask.
        var chronicle = await ChronicleAsync(ledger, cancellationToken).ConfigureAwait(false);

        await StopAsync(cancellationToken).ConfigureAwait(false);
        Restore(ledger);

        var (restored, unsafeToRestore) = RestoreDatabase(ledger, chronicle);

        // Whatever was decided about the database, the runtime comes back up on the version that is now
        // installed. A refusal below is about the database and never about leaving a machine with nothing running.
        await StartAsync(ledger, cancellationToken).ConfigureAwait(false);

        var back = ledger with { Step = UpdateStep.Complete, ToVersion = ledger.FromVersion, FromVersion = ledger.ToVersion };
        if (restored)
        {
            // The update's own record goes: what it did has been undone, and a second rollback would be putting
            // back a backup that no longer matches anything.
            File.Delete(update.Ledger);
        }
        else
        {
            back.Write(update.Ledger);
        }

        if (unsafeToRestore is { } refusal)
        {
            throw refusal;
        }

        return back;
    }

    /// <summary>The same turning of a filesystem refusal into this product's own, as an update's steps use.</summary>
    private static void OnDisk(string what, string path, string remedy, Action move)
    {
        try
        {
            move();
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            throw new UpdateException(
                UpdateCodes.FileRefused,
                $"{what} failed at '{path}': {error.Message} {remedy}",
                error);
        }
    }

    /// <summary>What the chronicle says about whether this update's version has been used since it came up.</summary>
    private enum Chronicle
    {
        /// <summary>The chronicle is where this update left it: nothing has been recorded since.</summary>
        Unchanged,

        /// <summary>Lines have been written since. Restoring a database from before them would erase work.</summary>
        Moved,

        /// <summary>It could not be read, so neither of the above is known.</summary>
        Unknown,

        /// <summary>
        /// It was never recorded: this update never reached a healthy runtime, so there is no line to compare
        /// against and no way to tell whether the new version has been used.
        /// </summary>
        NeverRecorded,
    }

    /// <summary>
    /// Whether the runtime has written anything since this update was declared healthy. The chronicle is
    /// append-only and every state change writes a line, so a newest id that is not the one recorded means work
    /// has happened — and restoring a database from before it would erase that work.
    /// </summary>
    /// <remarks>
    /// <b>"Cannot tell" is not "no".</b> A rollback typed when the runtime is down — stopped by hand, crashed, a
    /// machine that was rebooted — cannot read the chronicle at all, and this used to answer "nothing has
    /// happened" and restore the backup over a database that may have been used all week. The answer is its own
    /// state now, and it is treated as the dangerous one: the binary still goes back, the database is left as it
    /// stands, and the message says which file to put back by hand for a person who decides they want it.
    /// </remarks>
    private async Task<Chronicle> ChronicleAsync(UpdateLedger ledger, CancellationToken cancellationToken)
    {
        if (ledger.ChronicleId is null)
        {
            // Never recorded, because that line is written at `healthy` — and **never healthy is not never
            // served**. The applier stops nothing when a start times out or when the runtime that came up
            // answers as the wrong build: that runtime is still serving, through the new schema, for as long as
            // it takes somebody to read the message and type this. The same missing id covers a rollback typed
            // while the new version is still migrating, where there is no descriptor yet and so nothing to
            // stop. Reading it as "nothing has happened" is how a backup gets copied over a day's work.
            return Chronicle.NeverRecorded;
        }

        var newest = await NewestChronicleIdAsync(cancellationToken).ConfigureAwait(false);
        if (newest is null)
        {
            return Chronicle.Unknown;
        }

        return string.Equals(newest, ledger.ChronicleId, StringComparison.Ordinal) ? Chronicle.Unchanged : Chronicle.Moved;
    }

    private async Task<string?> NewestChronicleIdAsync(CancellationToken cancellationToken)
    {
        if (new DescriptorReader(env.Paths).Read() is null)
        {
            return null;
        }

        var (_, answer) = await OperationRunner
            .SendAsync(env, Operations.JournalList, new JsonObject { ["limit"] = 1 }, cancellationToken)
            .ConfigureAwait(false);

        if (answer is not { IsSuccess: true })
        {
            return null;
        }

        var page = JsonSerializer.Deserialize<Page<JournalEntryDto>>(answer.Body, JasonJson.Options);
        return page?.Items.Count > 0 ? page.Items[0].Id : null;
    }

    private async Task StopAsync(CancellationToken cancellationToken)
    {
        if (new DescriptorReader(env.Paths).Read() is null)
        {
            return;
        }

        // As in an update, and for the same reason: the stop is a step of this verb, so its own answer stays
        // off the stdout where this rollback's one document goes, and is printed on stderr if it failed.
        using var stopping = new StringWriter();
        if (await RuntimeStopCommand.RunAsync(env with { Out = stopping }, human: false, cancellationToken).ConfigureAwait(false) != ExitCodes.Success)
        {
            env.Error.Write(stopping.ToString());
            throw new UpdateException(
                UpdateCodes.RuntimeUnreachable,
                "The runtime would not stop, so nothing was put back: an executable cannot be replaced under a runtime that is using it.");
        }

        Say("stopped the runtime");
    }

    /// <summary>
    /// The binary half, which always happens: the file at the install path is moved aside and the kept one is
    /// moved in — two renames, and never a write over the file that is there.
    /// </summary>
    /// <remarks>
    /// The file at the install path may be <b>this very program</b>. A person told "`jason update rollback` puts
    /// 0.1.0 back" types it from the PATH, which runs the executable the update installed, and Windows will not
    /// let a running image be overwritten or deleted — but it will let one be renamed. Moving over it with
    /// <c>overwrite</c> therefore failed exactly where the message sends people, after the runtime had already
    /// been stopped, leaving a machine with the new binary in place and nothing running. It is the same rule the
    /// swap in an update follows, for the same reason, and the file moved aside is left under the update
    /// directory when it cannot be deleted — which on Windows it cannot be, because it is still running.
    /// </remarks>
    private void Restore(UpdateLedger ledger)
    {
        if (File.Exists(ledger.InstallPath))
        {
            // Named for the version being moved aside and the moment it was, so that nothing has to be deleted
            // first to make room. Deleting first is what this cannot afford: an image an earlier rollback left
            // here may still be running, and that delete would refuse — after the runtime had been stopped,
            // with nothing put back.
            var aside = Path.Combine(
                update.Replaced,
                $"{ledger.ToVersion}-{clock.GetUtcNow().UtcDateTime:yyyyMMdd'T'HHmmss'Z'}-{Path.GetFileName(ledger.InstallPath)}");
            OnDisk(
                "moving the installed executable aside",
                ledger.InstallPath,
                $"Nothing has been put back yet. {ledger.PreviousPath} is still the executable to restore by hand.",
                () =>
                {
                    Directory.CreateDirectory(update.Replaced);
                    File.Move(ledger.InstallPath, aside);
                });

            try
            {
                File.Delete(aside);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                // It is the program running this rollback, and a running program cannot delete itself. Nothing
                // deletes it afterwards either — no update touches this directory — so it waits there until
                // somebody removes it, which is safe as soon as that version is no longer running.
                Say($"{ledger.ToVersion} is still running, so it waits at {aside} until you delete it");
            }
        }

        OnDisk(
            "putting the previous executable back",
            ledger.InstallPath,
            $"The install path is empty: copy {ledger.PreviousPath} there by hand.",
            () => File.Move(ledger.PreviousPath, ledger.InstallPath));

        Say($"put {ledger.FromVersion} back");
    }

    /// <summary>
    /// The database half, which happens only where this update's own first start migrated and nothing has been
    /// done since.
    /// </summary>
    private (bool Restored, UpdateException? Unsafe) RestoreDatabase(UpdateLedger ledger, Chronicle chronicle)
    {
        var backup = ledger.BackupFile ?? BackupWrittenByThisUpdate(ledger);
        if (ledger.NewlyApplied.Count == 0 && backup is null)
        {
            Say("the database was not migrated by this update, so it is left as it is");
            return (false, null);
        }

        if (backup is null || !File.Exists(backup))
        {
            Say($"this update migrated the database and its backup is not at '{backup ?? "any path it recorded"}', so the database is left as it is");
            return (false, null);
        }

        if (chronicle is not Chronicle.Unchanged)
        {
            var why = chronicle switch
            {
                Chronicle.Moved => "the runtime has recorded work since this update was installed",
                Chronicle.NeverRecorded =>
                    "this update never reached a healthy runtime, so where the chronicle stood was never recorded - "
                    + "and a version that came up and was refused goes on serving until somebody stops it",
                _ => "no runtime was answering, so whether work has been recorded since this update was installed could not be read",
            };

            Say($"the database was left as it is: {why}");
            return (false, new UpdateException(
                UpdateCodes.RollbackUnsafe,
                $"{ledger.FromVersion} is back in place, but the database was left as it is: {why}, "
                + $"and restoring '{Path.GetFileName(backup)}' would erase it. "
                + $"{ledger.FromVersion} may refuse to open a database migrated by {ledger.ToVersion}; to go all the way back, "
                + $"stop the runtime, copy '{backup}' over state/jason.db, delete the -wal and -shm files beside it, and start again."));
        }

        // Copied beside the database and renamed onto it, never copied onto it: a copy that stops halfway — a
        // full disk, a lock, a machine that goes down — would leave a file that is neither the database from
        // before this update nor the one from after it, and nothing can put that right.
        //
        // The sidecars come off before any of that, and they are moved rather than deleted. Both halves of that
        // sentence are paid for. *Before*, because a delete that refused after the backup was already installed
        // left the older file in place with a log written against the new schema beside it — which this
        // product's own page calls corruption — under a refusal that said the database had been left alone.
        // *Moved*, because a write-ahead log found beside a database after a stop is a crash's, and it holds
        // committed transactions: deleting it and then failing the copy would lose them from the migrated
        // database that stays in place.
        var arriving = env.Paths.DatabaseFile + Arriving;
        var aside = new List<(string Sidecar, string Kept)>();
        try
        {
            foreach (var suffix in Sidecars)
            {
                var sidecar = env.Paths.DatabaseFile + suffix;
                if (File.Exists(sidecar))
                {
                    File.Move(sidecar, sidecar + SetAside);
                    aside.Add((sidecar, sidecar + SetAside));
                }
            }

            File.Copy(backup, arriving, overwrite: true);
            File.Move(arriving, env.Paths.DatabaseFile, overwrite: true);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            // What was copied and not renamed goes with it: a file left beside the database, named like the
            // database, is the next person's puzzle. And whatever was set aside goes back, because the database
            // beside it is still the one this update migrated and that log is still its own.
            Discard(arriving);
            var stranded = PutBack(aside);

            // Returned rather than thrown, because the caller starts the runtime before it raises anything: a
            // database that could not be put back is no reason to leave a machine with nothing running.
            Say($"the database could not be restored from {Path.GetFileName(backup)}: {error.Message}");
            return (false, new UpdateException(
                UpdateCodes.FileRefused,
                $"{ledger.FromVersion} is back in place, but the database could not be restored from '{backup}': {error.Message} "
                + "The database is as the update left it. To go the rest of the way, stop the runtime, copy that file over "
                + "state/jason.db, delete the -wal and -shm files beside it, and start again."
                + stranded,
                error));
        }

        // The database they belonged to is gone, so they are of no use to anybody and cannot be replayed into
        // what took its place. A delete that refuses here is worth no refusal of its own: what is left beside
        // the restored database is a file SQLite does not look for, under a name nothing else uses either.
        foreach (var (_, kept) in aside)
        {
            Discard(kept);
        }

        Say($"restored the database from {Path.GetFileName(backup)} and took its write-ahead log away");
        return (true, null);
    }

    /// <summary>The two files SQLite keeps beside a database, which a restore must not leave behind.</summary>
    private static readonly string[] Sidecars = ["-wal", "-shm"];

    /// <summary>Where the backup is copied to before it is renamed onto the database.</summary>
    private const string Arriving = ".restoring";

    /// <summary>What a sidecar is called while the database beside it is being replaced.</summary>
    private const string SetAside = ".rolling";

    /// <summary>
    /// Puts the sidecars back, for a restore that did not happen, and says which would not go. The database
    /// beside them is still the one this update migrated, so its log is still its own — and a person told that
    /// the database was left alone has to be told if its log was not.
    /// </summary>
    private static string PutBack(List<(string Sidecar, string Kept)> aside)
    {
        var stranded = new List<string>();
        foreach (var (sidecar, kept) in aside)
        {
            try
            {
                File.Move(kept, sidecar);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                stranded.Add(kept);
            }
        }

        return stranded.Count == 0
            ? string.Empty
            : $" Its write-ahead log was moved aside first and could not be moved back: {string.Join(", ", stranded)}. "
              + $"Rename it back, without the '{SetAside}', before starting anything on that database.";
    }

    /// <summary>A leftover that nobody should have to reason about, gone as far as the filesystem allows.</summary>
    private static void Discard(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            // There is nothing useful to say about a temporary file that will not go: the refusal being
            // raised is about the database, and it is the one a person has to read.
        }
    }

    /// <summary>
    /// The backup this update's first start wrote, found on disk where the runtime could not be asked.
    /// </summary>
    /// <remarks>
    /// A new binary that migrates and then never serves is exactly the case a rollback exists for, and it is
    /// also the case where <c>system.info</c> cannot answer: the migration happens before the host listens. So
    /// the backups directory is the second source — a backup whose stamp is at or after the moment this update
    /// stopped the old runtime was written by the start that came next, which is this update's. The comparison
    /// is at the stamps' own resolution: they carry whole seconds, so a stop and a backup inside the same second
    /// must count as "after", not "before".
    /// </remarks>
    private string? BackupWrittenByThisUpdate(UpdateLedger ledger)
    {
        if (ledger.StoppedAt is not { } stopped || !Directory.Exists(env.Paths.BackupsDirectory))
        {
            return null;
        }

        var floor = new DateTimeOffset(
            stopped.UtcDateTime.Year,
            stopped.UtcDateTime.Month,
            stopped.UtcDateTime.Day,
            stopped.UtcDateTime.Hour,
            stopped.UtcDateTime.Minute,
            stopped.UtcDateTime.Second,
            TimeSpan.Zero);

        return Directory.EnumerateFiles(env.Paths.BackupsDirectory, "jason-*-before-*.db")
            .Select(file => (File: file, Stamp: StampOf(file)))
            .Where(candidate => candidate.Stamp >= floor)
            .OrderByDescending(candidate => candidate.Stamp)
            .Select(candidate => candidate.File)
            .FirstOrDefault();
    }

    /// <summary>The moment in a backup's name, or the beginning of time where the name is not one of ours.</summary>
    private static DateTimeOffset StampOf(string file)
    {
        var name = Path.GetFileName(file);
        const string Prefix = "jason-";
        var end = name.IndexOf("-before-", StringComparison.Ordinal);
        if (!name.StartsWith(Prefix, StringComparison.Ordinal) || end < 0)
        {
            return DateTimeOffset.MinValue;
        }

        return DateTimeOffset.TryParseExact(
            name[Prefix.Length..end],
            "yyyyMMdd'T'HHmmss'Z'",
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out var stamp)
            ? stamp
            : DateTimeOffset.MinValue;
    }

    private async Task StartAsync(UpdateLedger ledger, CancellationToken cancellationToken)
    {
        var processes = env.Processes ?? RuntimeProcessControl.Instance;
        using var child = processes.Launch(env.Paths, [ledger.InstallPath]);

        var waited = TimeSpan.Zero;
        var limit = TimeSpan.FromSeconds(60);
        var poll = TimeSpan.FromMilliseconds(200);
        while (waited < limit)
        {
            if (new DescriptorReader(env.Paths).Read() is not null)
            {
                Say($"started {ledger.FromVersion}");
                return;
            }

            await Task.Delay(poll, clock, cancellationToken).ConfigureAwait(false);
            waited += poll;
        }

        Say($"{ledger.FromVersion} is back in place but no runtime answered; start one with `jason runtime start`");
    }

    private void Say(string what) => Steps.Add(what);
}
