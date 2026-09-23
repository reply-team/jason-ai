using System.Globalization;
using Jason.Cli.Autostart;
using Jason.Cli.Commands;
using Jason.Contracts.Skills;

namespace Jason.Cli.Uninstall;

/// <summary>What an uninstall did, and — where it stopped early — what stopped it.</summary>
/// <param name="Done">Each step that really happened, in the order it happened.</param>
/// <param name="Kept">Things deliberately left alone, so the report says so rather than staying silent.</param>
/// <param name="Problems">
/// What could not be removed, where that did not stop the rest. A root whose record cannot be read is not a
/// reason to leave an executable behind, but it is a reason this uninstall is not finished — so the steps
/// carry on and the verb still exits 1, naming what is left.
/// </param>
/// <param name="RefusalCode">The snake_case code a caller branches on, or null where nothing refused.</param>
/// <param name="Refusal">The same in words, with the command that repairs it where there is one.</param>
public sealed record UninstallReport(
    UninstallPlan Plan,
    IReadOnlyList<string> Done,
    IReadOnlyList<string> Kept,
    IReadOnlyList<string> Problems,
    string? RefusalCode,
    string? Refusal)
{
    /// <summary>Whether everything it set out to remove is gone.</summary>
    public bool Completed => RefusalCode is null && Problems.Count == 0;
}

/// <summary>
/// The order of an uninstall, and the refusals between its steps.
/// </summary>
/// <remarks>
/// <para>
/// The order is the safety property rather than a tidiness one. The logon registration goes <b>first</b>, so
/// that a logon part-way through cannot start the thing being removed. The runtime goes second, and a runtime
/// that will not stop ends the whole verb: deleting a binary out from under a live process is how a machine
/// ends up with neither a working installation nor a clean one.
/// </para>
/// <para>
/// Each step reports. A step that refuses stops the steps after it rather than pressing on and leaving a
/// machine in a state nobody has described.
/// </para>
/// </remarks>
public static class UninstallRunner
{
    public static async Task<UninstallReport> RunAsync(
        CliEnvironment env,
        UninstallPlan plan,
        UninstallOptions options,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(env);
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(options);

        var done = new List<string>();
        var kept = new List<string>();
        var problems = new List<string>();

        // 1. The registration, first.
        if (Step1RemoveAutostart(env, plan, done, kept) is { } refusedAt1)
        {
            return new UninstallReport(plan, done, kept, problems, refusedAt1.Code, refusedAt1.Message);
        }

        // 2. The runtime, and only then.
        if (await Step2StopRuntimeAsync(env, plan, options, done, kept, cancellationToken).ConfigureAwait(false) is { } refusedAt2)
        {
            return new UninstallReport(plan, done, kept, problems, refusedAt2.Code, refusedAt2.Message);
        }

        // 3. Everything a receipt names, and nothing else.
        Step3RemoveByReceipt(env, plan, options, done, kept, problems);

        // 4. The PATH entry, only where this installer wrote it.
        Step4RemovePathEntry(env, plan, done, kept, problems);

        // 5. The executable and its install directory, last, because everything above is run from it.
        Step5RemoveExecutable(env, plan, done, kept, problems);

        // 6. And the data directory, only on the explicit word, after everything else.
        Step6Data(env, plan, options, done, kept, problems);

        return new UninstallReport(plan, done, kept, problems, null, null);
    }

    /// <summary>
    /// Takes the logon registration away. Removing one that is not there is not an error, and a machine that
    /// has no way to register anything has nothing to take away either.
    /// </summary>
    private static RemovalRefused? Step1RemoveAutostart(CliEnvironment env, UninstallPlan plan, List<string> done, List<string> kept)
    {
        var registrar = RuntimeAutostartCommands.Registrar(env);
        if (registrar.Platform is AutostartPlatform.Unsupported)
        {
            kept.Add("This machine has no way of starting anything at logon, so there was no registration to remove.");
            return null;
        }

        try
        {
            // Composed the one way this product composes it. A second place that knew how this machine
            // registers things would be a second place to be wrong about how it unregisters them.
            registrar.Remove(RuntimeAutostartCommands.Registration(env, registrar.Platform));
        }
        catch (AutostartException refusal)
        {
            return new RemovalRefused(
                CliErrors.UninstallRefused,
                $"The logon registration could not be removed, so nothing further was: {refusal.Message} "
                + "Remove it with 'jason runtime autostart disable' and run this again.");
        }

        done.Add(plan.AutostartRegistered
            ? "Removed the logon registration."
            : "No logon registration was registered for this account.");

        return null;
    }

    /// <summary>
    /// The data directory: kept unless the word was said, and the word asks first.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>~/.jason</c> holds the database, the settings, the plugins, the logs and the work directories — the
    /// record of what the operator did, which is theirs. So it is kept by default and the verb says in one
    /// line that it kept it and where, because a person who wanted it gone needs to know it is still there.
    /// </para>
    /// <para>
    /// Last, after everything else, because everything else lives in it or writes to it while it runs.
    /// </para>
    /// </remarks>
    private static void Step6Data(
        CliEnvironment env,
        UninstallPlan plan,
        UninstallOptions options,
        List<string> done,
        List<string> kept,
        List<string> problems)
    {
        if (!options.PurgeData)
        {
            kept.Add(
                $"The data directory at '{plan.DataDirectory}' is kept: it holds your database, settings, "
                + "plugins, logs and any source this build fetched. --purge-data removes it.");
            return;
        }

        if (options.Human && !options.Yes && !Confirmed(env, plan))
        {
            kept.Add($"The data directory at '{plan.DataDirectory}' was kept: you did not confirm.");
            return;
        }

        try
        {
            env.Removes!.RemoveTree(plan.DataDirectory);
            done.Add($"Removed the data directory at '{plan.DataDirectory}'.");
        }
        catch (Exception exception) when (exception is RemovalRefused or IOException or UnauthorizedAccessException)
        {
            problems.Add($"'{plan.DataDirectory}' could not be removed: {exception.Message}");
        }
    }

    /// <summary>
    /// Prints exactly what will be deleted and asks. Only in the shape a person is reading: a machine shape
    /// that blocked on a question nobody could see would hang a script for ever, so it refuses up front
    /// instead, before anything at all has been removed.
    /// </summary>
    private static bool Confirmed(CliEnvironment env, UninstallPlan plan)
    {
        env.Out.WriteLine();
        env.Out.WriteLine($"About to delete {plan.DataDirectory} and everything in it:");
        foreach (var entry in Contents(plan.DataDirectory))
        {
            env.Out.WriteLine($"  {entry}");
        }

        env.Out.Write("Delete it? [y/N] ");
        var answer = (env.In ?? TextReader.Null).ReadLine();
        return answer is not null && (answer.Trim().Equals("y", StringComparison.OrdinalIgnoreCase)
            || answer.Trim().Equals("yes", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>What is in it, one line each, so the question is about something the person can see.</summary>
    private static IEnumerable<string> Contents(string root)
    {
        IEnumerable<string> entries;
        try
        {
            entries = Directory.Exists(root) ? Directory.EnumerateFileSystemEntries(root).Order(StringComparer.Ordinal) : [];
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return [$"(could not be listed: {exception.Message})"];
        }

        return entries.Select(entry => Path.GetFileName(entry) + (Directory.Exists(entry) ? "/" : string.Empty));
    }

    /// <summary>
    /// The executable and the directory holding it, last of all.
    /// </summary>
    /// <remarks>
    /// Last because every step above it is run from this file. And the one step that can honestly half
    /// succeed: where the file is the image of this very process it is moved aside rather than deleted, and
    /// the report names where it went and the line that removes it. It does not claim a clean uninstall it
    /// did not perform.
    /// </remarks>
    private static void Step5RemoveExecutable(
        CliEnvironment env,
        UninstallPlan plan,
        List<string> done,
        List<string> kept,
        List<string> problems)
    {
        if (plan.Executable is not { } executable)
        {
            kept.Add("Nothing named an executable, so none was removed.");
            return;
        }

        try
        {
            var outcome = env.Removes!.RemoveExecutable(executable);
            done.Add(outcome.Removed ? $"Removed the executable at '{executable}'." : $"Moved the executable out of '{executable}'.");

            if (outcome.Note is { } note)
            {
                kept.Add(note);
            }
        }
        catch (Exception exception) when (exception is RemovalRefused or IOException or UnauthorizedAccessException)
        {
            problems.Add(
                $"'{executable}' could not be removed: {exception.Message} Everything else is gone; remove "
                + "that one file yourself.");
            return;
        }

        if (plan.InstallDirectory is not { Length: > 0 } directory)
        {
            return;
        }

        try
        {
            if (!env.Removes!.RemoveDirectoryIfEmpty(directory) && Directory.Exists(directory))
            {
                kept.Add($"Kept '{directory}': something is in it that this installer did not write.");
            }
        }
        catch (Exception exception) when (exception is RemovalRefused or IOException or UnauthorizedAccessException)
        {
            problems.Add($"'{directory}' could not be removed: {exception.Message}");
        }
    }

    /// <summary>
    /// Takes the install directory off this account's PATH, in the way the installer put it on.
    /// </summary>
    /// <remarks>
    /// Only where this installer wrote it. A directory somebody put on their own PATH is their line in their
    /// own document, and a verb that removed it would be editing something it was never asked to touch.
    /// </remarks>
    private static void Step4RemovePathEntry(
        CliEnvironment env,
        UninstallPlan plan,
        List<string> done,
        List<string> kept,
        List<string> problems)
    {
        if (plan.PathEntry is not { } entry)
        {
            kept.Add("Nothing named an install directory, so no PATH entry was looked for.");
            return;
        }

        try
        {
            var outcome = env.Removes!.RemovePathEntry(entry);
            if (outcome.Removed)
            {
                done.Add($"Took '{entry.Directory}' off the PATH in {string.Join(", ", outcome.Touched)}.");
                return;
            }

            kept.Add(outcome.Note ?? $"'{entry.Directory}' was left on the PATH.");
        }
        catch (Exception exception) when (exception is RemovalRefused or IOException or UnauthorizedAccessException)
        {
            problems.Add(
                $"'{entry.Directory}' could not be taken off the PATH: {exception.Message} "
                + "Remove that line yourself; nothing else was left behind.");
        }
    }

    /// <summary>
    /// Removes what the receipts name, root by root, and nothing else.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The record file goes <b>after</b> the files it names, and only when none of them is left. It is the one
    /// thing that says what Jason put here, so removing it while a recorded file is still on disk would leave
    /// a file nothing can ever account for again.
    /// </para>
    /// <para>
    /// Then the directories, deepest first, and each only if nothing is left in it. A directory holding a file
    /// the operator added stays, whole, and is reported — which is the difference between this verb and a
    /// pattern.
    /// </para>
    /// </remarks>
    private static void Step3RemoveByReceipt(
        CliEnvironment env,
        UninstallPlan plan,
        UninstallOptions options,
        List<string> done,
        List<string> kept,
        List<string> problems)
    {
        var remover = env.Removes!;

        foreach (var unknown in plan.Unknown)
        {
            // Never guessed at. This is the file that says what Jason put here and this build cannot read it;
            // removing what it recognises and reporting a clean uninstall is how the paths it does not
            // recognise become nobody's.
            problems.Add(
                $"Nothing was removed from '{unknown.Root}': {unknown.Reason} Read the record there and "
                + "remove what it names by hand, or leave it.");
        }

        foreach (var root in plan.Roots)
        {
            var removed = 0;
            var remaining = 0;
            var directories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var file in root.Files)
            {
                if (!file.Present)
                {
                    continue;
                }

                if (file.Edited && !options.Force)
                {
                    kept.Add($"Kept '{file.Path}': its contents are not what was installed. --force removes it.");
                    remaining++;
                    continue;
                }

                try
                {
                    remover.RemoveFile(file.Path);
                    removed++;
                    if (Path.GetDirectoryName(file.Path) is { Length: > 0 } directory)
                    {
                        directories.Add(directory);
                    }
                }
                catch (RemovalRefused refusal)
                {
                    problems.Add(refusal.Message);
                    remaining++;
                }
            }

            done.Add(string.Create(
                CultureInfo.InvariantCulture,
                $"Removed {removed} file(s) of {string.Join(", ", root.Packs)} from '{root.Root}'."));

            if (remaining > 0)
            {
                kept.Add($"Kept the record in '{root.Root}': it still names files that are there.");
                continue;
            }

            try
            {
                remover.RemoveFile(Path.Combine(root.Root, SkillsRecord.FileName));
            }
            catch (RemovalRefused refusal)
            {
                problems.Add(refusal.Message);
                continue;
            }

            // Deepest first, so a skill's own directory is offered before the root that holds it.
            foreach (var directory in directories.Append(root.Root).OrderByDescending(path => path.Length))
            {
                if (!remover.RemoveDirectoryIfEmpty(directory) && Directory.Exists(directory))
                {
                    kept.Add($"Kept '{directory}': something is in it that this installer did not write.");
                }
            }
        }
    }

    /// <summary>
    /// Stops the runtime and waits until it really is gone.
    /// </summary>
    /// <remarks>
    /// Through <c>jason runtime stop</c> rather than beside it. That verb already asks <c>system.shutdown</c>
    /// and waits for both signs of life — the descriptor and the pid — and a second implementation of "is it
    /// really gone" is a second thing to be wrong about at the one moment being wrong is expensive. Its
    /// output goes to a writer of this command's own, because it prints an acknowledgement meant for somebody
    /// who typed <c>stop</c>, and what reaches this verb's caller is this verb's own report.
    /// </remarks>
    private static async Task<RemovalRefused?> Step2StopRuntimeAsync(
        CliEnvironment env,
        UninstallPlan plan,
        UninstallOptions options,
        List<string> done,
        List<string> kept,
        CancellationToken cancellationToken)
    {
        if (plan.RuntimePid is null && !File.Exists(env.Paths.DescriptorFile))
        {
            kept.Add("No runtime was running.");
            return null;
        }

        var said = new StringWriter();
        var exit = await RuntimeStopCommand
            .RunAsync(env with { Out = said }, human: false, cancellationToken, options.StopTimeout, options.StopPoll)
            .ConfigureAwait(false);

        if (exit is ExitCodes.Success or ExitCodes.RuntimeUnavailable)
        {
            // Unavailable means the descriptor went between the plan and here, which is a runtime that had
            // already stopped rather than one that would not.
            done.Add(exit == ExitCodes.Success ? "Stopped the runtime." : "The runtime had already stopped.");
            return null;
        }

        return new RemovalRefused(
            CliErrors.RuntimeStillRunning,
            "The runtime did not stop, so nothing further was removed and this installation is as it was — "
            + "except the logon registration, which had already been taken away and which "
            + "'jason runtime autostart enable' puts back. Stop the runtime with 'jason runtime stop' and run "
            + $"this again. It said: {said.ToString().Trim()}");
    }
}
