using System.Diagnostics;
using System.Text;
using Jason.Contracts.Discovery;
using OperatingSystemProcess = System.Diagnostics.Process;

namespace Jason.Cli.Process;

/// <summary>What a program did: its code, what it wrote, and whether it was stopped for taking too long.</summary>
/// <param name="TimedOut">
/// True when the bound expired and the child was killed. The exit code is then whatever killing it produced
/// and means nothing, so a caller reads this first.
/// </param>
/// <param name="NotStarted">
/// True when the program could not be started at all. The exit code is then -1, which no program returned, and the
/// standard error is the reason the start failed, so a caller reads this first as well.
/// </param>
public sealed record ProgramResult(int ExitCode, string StandardOutput, string StandardError, bool TimedOut, bool NotStarted = false);

/// <summary>
/// The one place the CLI runs a program that is not a Jason runtime: <c>git</c> for a skills source, and the
/// provider CLI a readiness check asks a version of.
/// </summary>
/// <remarks>
/// <para>
/// Bounded, always. A check that hangs is a status verb that never answers, which is worse than one that says
/// it could not tell.
/// </para>
/// <para>
/// Behind an interface, and the second of the two seams here whose null default is a refusal rather than the
/// real thing: three guards in this repository type documented command lines for real, and one of the verbs
/// they will type fetches from a remote. A seam that defaulted to the real runner would make those guards the
/// first tests in this repository to open a socket to the internet.
/// </para>
/// </remarks>
public interface IProgramRunner
{
    Task<ProgramResult> RunAsync(
        string program,
        IReadOnlyList<string> arguments,
        string? workingDirectory,
        TimeSpan timeout,
        CancellationToken cancellationToken);
}

/// <summary>The runners this build knows how to make.</summary>
public static class ProgramRunners
{
    public static IProgramRunner ForThisMachine() => MachineProgramRunner.Instance;

    /// <summary>How long a stopped program's tree is waited for before the stop gives up waiting.</summary>
    internal static TimeSpan StopGrace => TimeSpan.FromSeconds(5);

    /// <summary>
    /// A program stopped, tree and all, whichever way the kill goes.
    /// </summary>
    /// <param name="killTree">The kill of the program and everything it started.</param>
    /// <param name="exited">Completes once the program and whatever holds its output are gone.</param>
    /// <param name="grace">How long that is waited for.</param>
    /// <remarks>
    /// Nothing it meets escapes. On a caller's cancel the stop runs before the cancellation is thrown on, and a kill that
    /// failed took the cancellation's place: the update's skills step then reported the kill's failure rather than that
    /// it was cancelled. A descendant the kill could not end comes back as an aggregate of every one it could not.
    /// </remarks>
    internal static async Task StopAsync(Action killTree, Func<CancellationToken, Task> exited, TimeSpan grace)
    {
        ArgumentNullException.ThrowIfNull(killTree);
        ArgumentNullException.ThrowIfNull(exited);

        try
        {
            // The tree, because the thing that outstayed its bound may be a child of what was started.
            killTree();
        }
        catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException or AggregateException)
        {
            // It ended between the bound expiring and this line, which is the outcome asked for; or a part of the tree
            // could not be ended, which the wait below gives the rest of it the time to.
        }

        // And gone before the caller goes on to read or remove what it was writing, since a kill lands later than it is
        // asked for. The program's exit, and the end of its output, which its descendants hold open until they go too.
        using var waited = new CancellationTokenSource(grace);
        try
        {
            await exited(waited.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Not gone within the grace. Nothing more is done about it here: the kill was sent.
        }
    }

    private sealed class MachineProgramRunner : IProgramRunner
    {
        public static MachineProgramRunner Instance { get; } = new();

        public async Task<ProgramResult> RunAsync(
            string program,
            IReadOnlyList<string> arguments,
            string? workingDirectory,
            TimeSpan timeout,
            CancellationToken cancellationToken)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(program);
            ArgumentNullException.ThrowIfNull(arguments);

            var startInfo = ChildProcess.StartInfo(program, ChildKind.SystemTool);
            startInfo.WorkingDirectory = workingDirectory ?? string.Empty;

            // One by one, so nothing has to be quoted and no shell ever sees them.
            foreach (var argument in arguments)
            {
                startInfo.ArgumentList.Add(argument);
            }

            using var process = new OperatingSystemProcess { StartInfo = startInfo };
            try
            {
                ChildProcess.Start(process);
            }
            catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or InvalidOperationException)
            {
                // Not there, or not runnable. Every caller of this is reporting on an installation, and "that
                // program is not installed" is an answer rather than a failure of this call.
                return new ProgramResult(-1, string.Empty, exception.Message, false, NotStarted: true);
            }

            var output = new StringBuilder();
            var error = new StringBuilder();
            process.OutputDataReceived += (_, line) => Append(output, line.Data);
            process.ErrorDataReceived += (_, line) => Append(error, line.Data);
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            using var bound = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            bound.CancelAfter(timeout);
            try
            {
                await process.WaitForExitAsync(bound.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Either way the child goes, tree and all. A caller that gives up has no more use for it than a bound
                // that expired has, and this child has no console of its own for Ctrl+C to reach: left running, a
                // cancelled clone keeps filling a directory that the next run may find and trust.
                await StopAsync(() => process.Kill(entireProcessTree: true), process.WaitForExitAsync, StopGrace).ConfigureAwait(false);

                if (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }

                return new ProgramResult(-1, output.ToString(), error.ToString(), true);
            }

            return new ProgramResult(process.ExitCode, output.ToString(), error.ToString(), false);
        }

        private static void Append(StringBuilder text, string? line)
        {
            if (line is not null)
            {
                text.AppendLine(line);
            }
        }
    }
}
