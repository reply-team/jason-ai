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
/// the rollback: the binary goes back either way, and the message says exactly what was left as it was.
/// </para>
/// <para>
/// Nothing here opens the database. It renames one file, copies another over a third, and deletes two
/// sidecars — because a WAL written by the new schema against a restored older file is corruption, and the two
/// sidecars are the only part of this that cannot be undone by putting a file back.
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
            // Nothing was recorded, which happens when the update never got as far as a healthy runtime — and a
            // runtime that never served wrote nothing through the new schema either, so there is nothing a
            // restore could erase.
            return Chronicle.Unchanged;
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

        if (await RuntimeStopCommand.RunAsync(env, human: false, cancellationToken).ConfigureAwait(false) != ExitCodes.Success)
        {
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
            Directory.CreateDirectory(update.Replaced);
            var aside = Path.Combine(update.Replaced, Path.GetFileName(ledger.InstallPath));
            OnDisk(
                "moving the installed executable aside",
                ledger.InstallPath,
                $"Nothing has been put back yet. {ledger.PreviousPath} is still the executable to restore by hand.",
                () =>
                {
                    File.Delete(aside);
                    File.Move(ledger.InstallPath, aside);
                });

            try
            {
                File.Delete(aside);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                // It is the program that is running this rollback. It goes when the next update needs the name.
                Say($"{ledger.ToVersion} is still running, so it waits under {update.Replaced} rather than being deleted");
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
            var why = chronicle == Chronicle.Moved
                ? "the runtime has recorded work since this update was installed"
                : "no runtime was answering, so whether work has been recorded since this update was installed could not be read";

            Say($"the database was left as it is: {why}");
            return (false, new UpdateException(
                UpdateCodes.RollbackUnsafe,
                $"{ledger.FromVersion} is back in place, but the database was left as it is: {why}, "
                + $"and restoring '{Path.GetFileName(backup)}' would erase it. "
                + $"{ledger.FromVersion} may refuse to open a database migrated by {ledger.ToVersion}; to go all the way back, "
                + $"stop the runtime, copy '{backup}' over state/jason.db, delete the -wal and -shm files beside it, and start again."));
        }

        File.Copy(backup, env.Paths.DatabaseFile, overwrite: true);

        // A write-ahead log written against the new schema, replayed into a restored older file, is corruption.
        // These two are the only part of a rollback that putting a file back cannot undo.
        foreach (var sidecar in new[] { env.Paths.DatabaseFile + "-wal", env.Paths.DatabaseFile + "-shm" })
        {
            File.Delete(sidecar);
        }

        Say($"restored the database from {Path.GetFileName(backup)} and deleted its write-ahead log");
        return (true, null);
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
