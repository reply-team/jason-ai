using System.CommandLine;
using System.Globalization;
using System.Text.Json;
using Jason.Cli.Commands;
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
/// <b>It removes only what a receipt names.</b> Each root a deployment wrote into carries a record listing
/// every path written there; this verb removes those and nothing else. A verb that deleted by pattern would
/// eventually delete somebody's own file, and a verb that deletes by receipt cannot.
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
          4. the PATH entry, only where this installer wrote it
          5. the executable and its install directory

        Kept unless --purge-data:  the data directory. --purge-data prints what will be deleted and asks,
                                   unless --yes.
        Never touched:             anything there is no receipt for, any other skill in an agent harness,
                                   any provider CLI, any model credential, anything outside the paths it names.

        Exit codes: 0 when everything it set out to remove is gone, 1 when it understood and refused — a
        runtime that would not stop, a record it cannot read, a file it could not remove.
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
        if (options.PurgeData && !options.Yes && !options.Human)
        {
            return Refuse(
                env,
                options.Human,
                CliErrors.UninstallRefused,
                $"--purge-data deletes '{env.Paths.Root}' and everything in it. Nothing has been removed. "
                + "Add --yes to say so in advance, or run with --human to be asked.");
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

        var report = options.DryRun
            ? new UninstallReport(plan, [], ["--dry-run: nothing was removed."], [], null, null)
            : await UninstallRunner.RunAsync(env, plan, options, cancellationToken).ConfigureAwait(false);

        Report(env, report, options);
        return report.Completed ? ExitCodes.Success : ExitCodes.ApiError;
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
            ? $"  the data directory at {plan.DataDirectory}, with everything in it"
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
