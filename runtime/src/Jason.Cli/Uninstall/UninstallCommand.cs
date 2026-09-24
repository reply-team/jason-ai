using System.CommandLine;
using System.Globalization;
using System.Text.Json;
using Jason.Cli.Commands;
using Jason.Contracts.Discovery;
using Jason.Contracts.Json;

namespace Jason.Cli.Uninstall;

/// <summary>
/// <c>jason uninstall</c>: taking this installation off the machine, by receipt.
/// </summary>
/// <remarks>
/// <para>
/// A CLI verb rather than an API operation, and not a noun either. Nothing about removing Jason from a machine
/// is the runtime's to perform or be asked about — a logon registration, an executable, a PATH entry, the
/// files a deployment recorded — so <c>Operations</c> carries no <c>uninstall.*</c> and never will.
/// </para>
/// <para>
/// <b>Skills go only by receipt.</b> Each root a deployment wrote into carries a record listing every path
/// written there; this verb removes those skills and nothing else. A verb that deleted by pattern would
/// eventually delete somebody's own file, and a verb that deletes by receipt cannot. The rest of the
/// installation — a registration, a PATH entry, an executable — has no receipt and goes by rule, each rule
/// written where its step is.
/// </para>
/// <para>
/// <b>And the data directory is the operator's.</b> It holds the database, the settings, the plugins, the logs
/// and the work directories — the record of what they did. It is kept unless <c>--purge-data</c> says
/// otherwise, and the verb says in one line that it kept it and where.
/// </para>
/// </remarks>
public static class UninstallCommand
{
    /// <summary>
    /// What this verb will and will not touch, printed in help. A destructive verb whose boundaries are only
    /// in a design note is one somebody runs without knowing them.
    /// </summary>
    private const string Boundaries =
        """
        Removed, in this order, each step reported:
          1. the logon registration, first, so a logon part-way through cannot start what is going
          2. the runtime — and if it will not stop, nothing further is removed
          3. every skill a deployment recorded, by the paths its record names
          4. the PATH entry, only where the installer wrote it: the marked block in a login profile; on
             Windows, only an entry for a directory that holds nothing but Jason
          5. the executable, its install directory, and the native libraries it unpacked on first run

        Kept unless --purge-data:  the data directory. --purge-data removes what Jason keeps there, and the
                                   directory once nothing else is in it; it prints what will be deleted and
                                   asks before anything at all is removed, unless --yes. A directory holding a
                                   file-system root, your profile, the temporary directory or the installation
                                   is refused.
        Never touched:             a skill there is no receipt for, any other skill in an agent harness,
                                   any provider CLI, any model credential, a PATH entry the installer did
                                   not write.
        This account's, not this installation's: the logon registration and the agent harnesses. Uninstalling
        a second installation on one account takes the first one's registration and harness skills too.

        Exit codes: 0 when everything it set out to remove is gone. 1 when it did not get there: it refused
        (a runtime that would not stop), or something it set out to remove is still there (a record it cannot
        read, a file it could not remove), or it stopped part-way. Every step that did happen is listed
        either way — in the report, or on stderr where the report itself could not be written.
        """;

    public static Command Build(CliEnvironment env)
    {
        ArgumentNullException.ThrowIfNull(env);

        var command = new Command("uninstall", "Take this installation off the machine, by receipt.")
        {
            Description = "Take this installation off the machine, by receipt."
                + Environment.NewLine + Environment.NewLine + Boundaries,
        };

        var human = VerbOptions.Human();
        var dryRun = new Option<bool>("--dry-run") { Description = "Print exactly what would be removed, and change nothing." };
        var purgeData = new Option<bool>("--purge-data") { Description = "Also remove the data directory. Without it, it is kept and the verb says where." };
        var yes = new Option<bool>("--yes") { Description = "Do not ask before removing the data directory." };
        var force = new Option<bool>("--force") { Description = "Remove a recorded file whose bytes you have since changed. Without it such a file is reported and kept." };

        foreach (var option in new Option[] { human, dryRun, purgeData, yes, force })
        {
            command.Options.Add(option);
        }

        command.SetAction((parse, cancellationToken) => RunAsync(
            env,
            new UninstallOptions(
                parse.GetValue(human),
                parse.GetValue(dryRun),
                parse.GetValue(purgeData),
                parse.GetValue(yes),
                parse.GetValue(force)),
            cancellationToken));

        return command;
    }

    /// <summary>
    /// The verb itself. Public, and taking its options as a record, for the reason <c>RuntimeStopCommand</c>
    /// is: a test has to shorten the wait for a runtime to go, and the repository has exactly one friend
    /// declaration, for a test that could not be written any other way. This one can.
    /// </summary>
    public static async Task<int> RunAsync(CliEnvironment env, UninstallOptions options, CancellationToken cancellationToken)
    {
        // Before anything is read, let alone removed. The seam that touches the machine is the one an
        // environment can forget to name, and a destructive verb that treated "nobody named one" as "there is
        // nothing to do" would report a clean uninstall it never performed.
        if (env.Removes is null)
        {
            return Refuse(
                env,
                options.Human,
                CliErrors.RemoverUnsupported,
                "This environment does not remove anything from this machine, so nothing was removed. "
                + $"The data directory at '{env.Paths.Root}' is untouched, as it is unless --purge-data says otherwise.");
        }

        // Before anything is read, let alone removed. In the shape a person is reading, --purge-data asks;
        // in the shape a script reads, there is nobody to ask, and a verb that blocked on a question nobody
        // could see would hang for ever. So the word has to be said in advance, and this refuses with the
        // whole installation still in place rather than with everything but the data gone.
        // A dry run is not asked: it deletes nothing, so there is nothing to have said in advance.
        if (options.PurgeData && !options.Yes && !options.Human && !options.DryRun)
        {
            return Refuse(
                env,
                options.Human,
                CliErrors.UninstallRefused,
                $"--purge-data deletes what Jason keeps in '{env.Paths.Root}'. Nothing has been removed. "
                + "Add --yes to say so in advance, or run with --human to be asked.");
        }

        // And a data directory that is not one is refused before anything is read. JASON_DATA_DIR may name any
        // directory at all, and the purge used to delete the one it named, recursively, whatever it was.
        if (options.PurgeData
            && DataDirectoryBelt.Refusal(
                env.Paths.Root,
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                Path.GetTempPath(),
                Path.GetDirectoryName(env.InstallPath ?? SelfExecutable.InstalledImage ?? string.Empty)) is { } refusal)
        {
            return Refuse(
                env,
                options.Human,
                CliErrors.UninstallRefused,
                $"--purge-data was refused and nothing has been removed: {refusal} Point JASON_DATA_DIR at the data "
                + "directory itself, or run this without --purge-data to keep it.");
        }

        var plan = UninstallReader.Read(env, options.PurgeData);

        // A person is shown the plan before a byte of it is acted on, because a plan nobody saw is one
        // nobody could have stopped. A caller reading the machine shape gets one document at the end
        // instead, carrying the plan and what became of it: two documents on one stdout is worse than
        // late, and a caller that wants to look before anything happens has --dry-run for exactly that.
        if (options.Human)
        {
            Describe(env, plan, options);
        }

        // The warm-up below is the one line here no unit test holds: the suite runs in a test host whose
        // assemblies never move, so taking it out stays green. CI's step that uninstalls the published executable
        // from its own image is what fails without it -- measured, by taking it out.
        var report = options.DryRun
            ? new UninstallReport(plan, [], ["--dry-run: nothing was removed."], [], null, null)
            : await UninstallRunner
                .RunAsync(
                    env,
                    plan,
                    options,
                    cancellationToken,
                    beforeTheImageGoes: soFar => Report(env with { Out = TextWriter.Null, Error = TextWriter.Null }, soFar, options))
                .ConfigureAwait(false);

        return Reported(env, report, options) && report.Completed ? ExitCodes.Success : ExitCodes.ApiError;
    }

    /// <summary>
    /// The report, or — where it cannot be written — every line of it on stderr, in words, and false.
    /// </summary>
    /// <remarks>
    /// By the time the report is written this verb has removed things. A report that failed to render used to
    /// leave the process with one sentence about a type initializer and nothing about what was already gone;
    /// the lines below need nothing a published build has not long since loaded, so they are what a caller gets
    /// instead. And the exit code is 1, because the document a script reads is not on stdout.
    /// </remarks>
    private static bool Reported(CliEnvironment env, UninstallReport report, UninstallOptions options)
    {
        try
        {
            Report(env, report, options);
            return true;
        }
        catch (Exception unexpected) when (unexpected is not OperationCanceledException)
        {
            env.Error.WriteLine($"The report could not be written: {Causes.Line(unexpected)}");

            env.Error.WriteLine("What this uninstall did:");
            foreach (var line in report.Done)
            {
                env.Error.WriteLine($"  done: {line}");
            }

            foreach (var line in report.Kept)
            {
                env.Error.WriteLine($"  kept: {line}");
            }

            foreach (var line in report.Problems.Append(report.Refusal).OfType<string>())
            {
                env.Error.WriteLine($"  problem: {line}");
            }

            return false;
        }
    }

    /// <summary>What happened, in the shape the caller asked for.</summary>
    private static void Report(CliEnvironment env, UninstallReport report, UninstallOptions options)
    {
        if (!options.Human)
        {
            env.Out.WriteLine(JsonSerializer.Serialize(report, JasonJson.Options));
            return;
        }

        foreach (var line in report.Done)
        {
            env.Out.WriteLine($"  {line}");
        }

        foreach (var line in report.Kept)
        {
            env.Out.WriteLine($"  {line}");
        }

        foreach (var problem in report.Problems)
        {
            env.Error.WriteLine(problem);
        }

        if (report.Refusal is { } refusal)
        {
            env.Error.WriteLine(refusal);
        }
    }

    /// <summary>
    /// The plan, in the shape the caller asked for: the compact JSON an agent reads, or the lines a person
    /// does. Both name every root, including the ones nothing may guess at.
    /// </summary>
    private static void Describe(CliEnvironment env, UninstallPlan plan, UninstallOptions options)
    {
        if (!options.Human)
        {
            env.Out.WriteLine(JsonSerializer.Serialize(plan, JasonJson.Options));
            return;
        }

        env.Out.WriteLine(options.DryRun ? "This would be removed:" : "Removing:");

        if (plan.AutostartRegistered)
        {
            env.Out.WriteLine("  the logon registration" + (plan.AutostartArtifact is { } document ? $" ({document})" : string.Empty));
        }

        if (plan.RuntimePid is { } pid)
        {
            env.Out.WriteLine(string.Create(CultureInfo.InvariantCulture, $"  the running runtime, pid {pid}"));
        }

        foreach (var root in plan.Roots)
        {
            env.Out.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"  {root.Files.Count(file => file.Present && !file.Edited)} file(s) of {string.Join(", ", root.Packs)} in {root.Root}"));
        }

        if (plan.PathEntry is { Ours: true } entry)
        {
            env.Out.WriteLine($"  '{entry.Directory}' off the PATH");
        }

        if (plan.Executable is { } executable)
        {
            env.Out.WriteLine($"  the executable at {executable}");
        }
        else
        {
            // Said here as well as in the report, because a dry run never reaches the report: the plan is the
            // whole of what a person sees, and a list with no executable in it reads as one that forgot. It sat
            // under the unpacked libraries' branch, so a build that had unpacked none printed it beside the
            // executable it had just named.
            env.Out.WriteLine(
                "  no executable: this Jason is not a published single file - it is running through `dotnet`, or "
                + "from a build's own output - so there is no one file to remove. The build it runs from is where "
                + "it lives.");
        }

        if (plan.PreviousExecutable is { } previous)
        {
            env.Out.WriteLine($"  the executable an earlier install replaced, at {previous}");
        }

        if (plan.ExtractedLibraries is { } extracted)
        {
            env.Out.WriteLine($"  the native libraries it unpacked, at {extracted}");
        }

        foreach (var unknown in plan.Unknown)
        {
            env.Out.WriteLine($"  NOT touched: {unknown.Root} — {unknown.Reason}");
        }

        if (plan.Edited > 0)
        {
            env.Out.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"  kept: {plan.Edited} file(s) whose contents are no longer what was installed. --force removes them."));
        }

        env.Out.WriteLine(plan.PurgesData
            ? $"  what Jason keeps in the data directory at {plan.DataDirectory}, and the directory once nothing else is in it"
            : $"The data directory at {plan.DataDirectory} is kept. --purge-data removes it.");
    }

    /// <summary>
    /// The one shape a refusal takes: the error envelope on stdout by default, a sentence under
    /// <c>--human</c>, and exit 1 — understood, and declined.
    /// </summary>
    private static int Refuse(CliEnvironment env, bool human, string code, string message)    {
        env.Out.WriteLine(human ? message : CliErrors.Serialize(code, message, retryable: false));
        return ExitCodes.ApiError;
    }
}

/// <summary>What was asked of this verb, as parsed.</summary>
/// <param name="PurgeData">The explicit word for the data directory. Nothing else stands for it.</param>
/// <param name="Force">Remove a recorded file whose bytes no longer match the record's digest.</param>
/// <param name="StopTimeout">How long the runtime is given to go. Null is the stop verb's own wait.</param>
/// <param name="StopPoll">How often it is asked. Null is the stop verb's own interval.</param>
public sealed record UninstallOptions(
    bool Human,
    bool DryRun,
    bool PurgeData,
    bool Yes,
    bool Force,
    TimeSpan? StopTimeout = null,
    TimeSpan? StopPoll = null);
