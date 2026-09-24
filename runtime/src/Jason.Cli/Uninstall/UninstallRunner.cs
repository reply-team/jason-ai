using System.Globalization;
using Jason.Cli.Autostart;
using Jason.Cli.Commands;
using Jason.Cli.Discovery;
using Jason.Cli.Process;
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
    /// <param name="env">The environment the verb runs in.</param>
    /// <param name="plan">What was read, and shown, before anything is removed.</param>
    /// <param name="options">What was asked.</param>
    /// <param name="cancellationToken">Stops a wait for the runtime; nothing after that point is cancellable.</param>
    /// <param name="beforeTheImageGoes">
    /// Called with the report so far, immediately before the executable step: the caller renders its report
    /// there once and throws it away. After that step a single-file build may no longer be able to load an
    /// assembly it has not loaded yet — see <see cref="IInstallationRemover.RemoveExecutable"/> — and the
    /// report is the one thing still to be written.
    /// </param>
    public static async Task<UninstallReport> RunAsync(
        CliEnvironment env,
        UninstallPlan plan,
        UninstallOptions options,
        CancellationToken cancellationToken,
        Action<UninstallReport>? beforeTheImageGoes = null)
    {
        ArgumentNullException.ThrowIfNull(env);
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(options);

        var done = new List<string>();
        var kept = new List<string>();
        var problems = new List<string>();

        // The one question this verb asks, asked before anything at all is removed. It used to be asked last,
        // after the executable had gone -- so a person who stopped to think at the prompt was looking at a
        // machine already half uninstalled, and the answer had to be read by a process that no longer had its
        // own file to load anything from.
        var purge = options.PurgeData && (!options.Human || options.Yes || Confirmed(env, plan));
        var declined = options.PurgeData && !purge;

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

        try
        {
            // 3. Everything a receipt names, and nothing else.
            Step3RemoveByReceipt(env, plan, options, done, kept, problems);

            // 4. The PATH entry, only where this installer wrote it.
            Step4RemovePathEntry(env, plan, done, kept, problems);

            // Rendered once and thrown away, while this process can still load whatever rendering it needs.
            beforeTheImageGoes?.Invoke(new UninstallReport(plan, [.. done], [.. kept], [.. problems], null, null));

            // 5. The executable, its install directory and what it unpacked, last, because everything above is
            //    run from it.
            Step5RemoveExecutable(env, plan, done, kept, problems);

            // 6. And the data directory, only on the explicit word, after everything else.
            Step6Data(env, plan, purge, declined, done, kept, problems);
        }
        catch (Exception unexpected) when (unexpected is not OperationCanceledException)
        {
            // Never out of here. Everything above this point has already removed something, and an exception
            // that left the verb took the list of what with it: a published build once answered with one line
            // about a type initializer after it had deleted the data directory. So it becomes the last problem
            // in a report that still names every step that did happen.
            problems.Add(
                $"The uninstall stopped part-way, on something it did not expect: {Causes.Line(unexpected)} Every step "
                + "listed as done did happen; nothing after the one that failed was attempted. Run it again to "
                + "finish: what is already gone is not an error the second time.");
        }

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
        bool purge,
        bool declined,
        List<string> done,
        List<string> kept,
        List<string> problems)
    {
        if (declined)
        {
            kept.Add($"The data directory at '{plan.DataDirectory}' was kept: you did not confirm.");
            return;
        }

        if (!purge)
        {
            kept.Add(
                $"The data directory at '{plan.DataDirectory}' is kept: it holds your database, settings, "
                + "plugins, logs and any source this build fetched. --purge-data removes it.");
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
            // Not a bare "nothing named one". This is the muxer case -- `dotnet jason.dll`, which is how a
            // build from source is run -- and a person reading silence here would read it as a clean
            // uninstall of a file that is still on the machine.
            kept.Add(
                "This Jason is running through `dotnet`, so there is no single executable to remove and none "
                + "was: the build it runs from is where it lives, and the muxer belongs to your .NET "
                + "installation rather than to Jason. A published release is one file, and this verb removes "
                + "that one.");
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

        if (plan.InstallDirectory is { Length: > 0 } directory)
        {
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

        RemoveExtractedLibraries(env, plan, done, kept, problems);
    }

    /// <summary>
    /// What a single-file build unpacked on its first run, and the directory holding it if that is now empty.
    /// </summary>
    /// <remarks>
    /// The parent is where every build of this program unpacks, one directory each: earlier versions of this
    /// installation left theirs there, and so would another installation. None of those is this build's to
    /// remove, so the parent goes only if nothing is left in it, and the report says why when it stays.
    /// </remarks>
    private static void RemoveExtractedLibraries(
        CliEnvironment env,
        UninstallPlan plan,
        List<string> done,
        List<string> kept,
        List<string> problems)
    {
        if (plan.ExtractedLibraries is not { } extracted)
        {
            return;
        }

        try
        {
            env.Removes!.RemoveExtractedLibraries(extracted);
            done.Add($"Removed the native libraries this build unpacked, at '{extracted}'.");
        }
        catch (Exception exception) when (exception is RemovalRefused or IOException or UnauthorizedAccessException)
        {
            problems.Add(
                $"'{extracted}' could not be removed: {exception.Message} It holds only native libraries this "
                + "build unpacked; another copy of this build still running would be holding one of them.");
            return;
        }

        if (Path.GetDirectoryName(extracted) is not { Length: > 0 } parent)
        {
            return;
        }

        try
        {
            if (!env.Removes!.RemoveDirectoryIfEmpty(parent) && Directory.Exists(parent))
            {
                kept.Add(
                    $"Kept '{parent}': other builds of this program unpacked their libraries there too — earlier "
                    + "versions of this installation, or another one. Nothing of this build's is left in it.");
            }
        }
        catch (Exception exception) when (exception is RemovalRefused or IOException or UnauthorizedAccessException)
        {
            problems.Add($"'{parent}' could not be removed: {exception.Message}");
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

        if (exit == ExitCodes.Success)
        {
            done.Add("Stopped the runtime.");
            return null;
        }

        // Exit 3 is not "it stopped". `runtime stop` answers 3 for a descriptor that went between the plan and
        // here -- and just as much for a connection refused, a request that timed out and a token the runtime
        // rejected, with the descriptor still on disk and the process still in the table. Reading every 3 as
        // the first carried on and removed the executable and the data directory from under a runtime that was
        // still running. So the process table decides, the way `runtime stop` itself decides "gone".
        var left = exit == ExitCodes.RuntimeUnavailable ? Left(env, plan) : new Runtime(true, plan.RuntimePid, null);
        if (!left.Running)
        {
            done.Add(left.Note ?? "The runtime had already stopped.");
            return null;
        }

        return new RemovalRefused(
            CliErrors.RuntimeStillRunning,
            (left.Pid is { } pid
                ? string.Create(CultureInfo.InvariantCulture, $"The runtime, process {pid}, did not stop, ")
                : "The runtime did not stop, ")
            + "so nothing further was removed and this installation is as it was — "
            + "except the logon registration, which had already been taken away and which "
            + "'jason runtime autostart enable' puts back. Stop the runtime with 'jason runtime stop' — or, where "
            + "it does not answer that either, end that process — and run this again. "
            + (left.Note is { } note ? note + " " : string.Empty)
            + $"It said: {said.ToString().Trim()}");
    }

    /// <summary>
    /// What is left of the runtime after <c>runtime stop</c> could not reach it: a process still in the table —
    /// the one the plan saw, or one a descriptor on disk names now — or nothing.
    /// </summary>
    /// <remarks>
    /// A descriptor whose process has gone is not a runtime — a crash, or a restart of the machine under it,
    /// leaves one behind — and refusing on it would hold up every uninstall on a machine whose runtime once
    /// died, saying it "did not stop" when nothing was running. A descriptor that cannot be read names nothing
    /// that can be asked about, so it is refused on rather than guessed past.
    /// </remarks>
    private static Runtime Left(CliEnvironment env, UninstallPlan plan)
    {
        var processes = env.Processes ?? RuntimeProcessControl.Instance;
        if (plan.RuntimePid is { } planned && processes.IsRunning(planned))
        {
            return new Runtime(true, planned, null);
        }

        if (!File.Exists(env.Paths.DescriptorFile))
        {
            return new Runtime(false, null, null);
        }

        if (new DescriptorReader(env.Paths).Read() is not { } descriptor)
        {
            return new Runtime(
                true,
                null,
                $"The descriptor at '{env.Paths.DescriptorFile}' cannot be read, so whether a runtime is running "
                + "cannot be told; where none is, delete that file.");
        }

        return processes.IsRunning(descriptor.Pid)
            ? new Runtime(true, descriptor.Pid, null)
            : new Runtime(
                false,
                null,
                $"The runtime was not running: the descriptor at '{env.Paths.DescriptorFile}' was left by one whose process has gone.");
    }

    /// <summary>Whether a runtime is still there after the question, which process, and anything worth saying.</summary>
    private sealed record Runtime(bool Running, int? Pid, string? Note);
}
