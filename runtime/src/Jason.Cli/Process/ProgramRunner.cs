using System.Diagnostics;
using System.Text;
using OperatingSystemProcess = System.Diagnostics.Process;

namespace Jason.Cli.Process;

/// <summary>What a program did: its code, what it wrote, and whether it was stopped for taking too long.</summary>
/// <param name="TimedOut">
/// True when the bound expired and the child was killed. The exit code is then whatever killing it produced
/// and means nothing, so a caller reads this first.
/// </param>
public sealed record ProgramResult(int ExitCode, string StandardOutput, string StandardError, bool TimedOut);

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

            var startInfo = new ProcessStartInfo(program)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                WorkingDirectory = workingDirectory ?? string.Empty,
            };

            // One by one, so nothing has to be quoted and no shell ever sees them.
            foreach (var argument in arguments)
            {
                startInfo.ArgumentList.Add(argument);
            }

            using var process = new OperatingSystemProcess { StartInfo = startInfo };
            try
            {
                process.Start();
            }
            catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or InvalidOperationException)
            {
                // Not there, or not runnable. Every caller of this is reporting on an installation, and "that
                // program is not installed" is an answer rather than a failure of this call.
                return new ProgramResult(-1, string.Empty, exception.Message, false);
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
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                Stop(process);
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

        private static void Stop(OperatingSystemProcess process)
        {
            try
            {
                // The tree, because the thing that outstayed its bound may be a child of what was started.
                process.Kill(entireProcessTree: true);
            }
            catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException)
            {
                // It ended between the bound expiring and this line, which is the outcome asked for.
            }
        }
    }
}
