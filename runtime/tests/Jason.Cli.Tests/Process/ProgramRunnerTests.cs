using System.Diagnostics;
using System.Globalization;
using Jason.Cli.Process;
using Jason.Runtime.Tests;
using Jason.Runtime.Tests.Integration;
using OperatingSystemProcess = System.Diagnostics.Process;

namespace Jason.Cli.Tests.Process;

/// <summary>
/// The one place the CLI runs a program that is not a Jason runtime. Proved against a program every machine
/// that builds this repository has, with no repository and no network in sight.
/// </summary>
public class ProgramRunnerTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly TimeSpan Generous = TimeSpan.FromSeconds(60);

    [Fact]
    public async Task A_program_that_answers_is_read_back_whole()
    {
        var result = await ProgramRunners.ForThisMachine().RunAsync("git", ["--version"], null, Generous, Ct);

        Assert.Equal(0, result.ExitCode);
        Assert.False(result.TimedOut);
        Assert.False(result.NotStarted);
        Assert.Contains("git version", result.StandardOutput, StringComparison.Ordinal);
    }

    /// <summary>
    /// A program that is not there is an answer, not an exception. Every caller of this is reporting on an
    /// installation, and "the thing you asked about is not installed" is the most ordinary answer there is. It says
    /// that the program was never started, so no caller reads the runner's -1 as a code the program returned.
    /// </summary>
    [Fact]
    public async Task A_program_that_is_not_there_answers_rather_than_throwing()
    {
        var result = await ProgramRunners.ForThisMachine()
            .RunAsync("jason-no-such-program-exists", [], null, Generous, Ct);

        Assert.NotEqual(0, result.ExitCode);
        Assert.False(result.TimedOut);
        Assert.True(result.NotStarted);
        Assert.NotEqual(string.Empty, result.StandardError);
    }

    /// <summary>
    /// And one that will not finish is stopped and said to have timed out. A check that hangs is a status verb
    /// that never answers, which is worse than one that says it could not tell.
    /// </summary>
    [Fact]
    [Critical(Family.F4, "a child is bounded")]
    public async Task A_program_that_will_not_finish_is_stopped_and_says_so()
    {
        var started = Stopwatch.StartNew();

        var result = await ProgramRunners.ForThisMachine()
            .RunAsync(Sleeper.Program, Sleeper.ArgumentsFor(seconds: 60), null, TimeSpan.FromMilliseconds(400), Ct);

        started.Stop();
        Assert.True(result.TimedOut, $"The program answered {result.ExitCode} instead of being stopped.");
        Assert.True(
            started.Elapsed < TimeSpan.FromSeconds(20),
            $"The bound was 400 ms and the call took {started.Elapsed}. A bound that is not enforced is not a bound.");
    }

    /// <summary>
    /// A caller that gives up stops what it started. The runner killed its child only when its own bound expired, so
    /// a cancelled <c>git clone</c> kept running with no console to receive Ctrl+C, and went on filling a directory
    /// that the next run would find and trust. The child here starts a grandchild of its own, the way git does, and
    /// both must be gone.
    /// </summary>
    /// <remarks>
    /// The grandchild is known only by the number the program wrote, and on a shared machine that number may belong to
    /// another process by the time it is read. So the grandchild is opened through the proof, a process of its name that
    /// had started by the moment the number was read, and ended only through it; a number the proof does not hold fails
    /// the row, and its process is left alone.
    /// </remarks>
    [Fact]
    public async Task A_caller_that_cancels_stops_the_whole_tree_of_the_program()
    {
        using var tree = new TempPaths();
        var pidFile = Path.Combine(tree.Paths.Root, "grandchild.pid");
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(Ct);

        var run = Task.Run(
            () => ProgramRunners.ForThisMachine().RunAsync(
                ChildThatStartsAGrandchild.Program,
                ChildThatStartsAGrandchild.ArgumentsFor(pidFile),
                null,
                Generous,
                cancel.Token),
            Ct);

        OwnedChild.Held? grandchild = null;
        try
        {
            var (number, read) = await ChildThatStartsAGrandchild.WaitForGrandchildAsync(pidFile, Ct);
            grandchild = Proven(number, read);
            Assert.True(
                grandchild is not null,
                $"Process {number}, the number the program wrote, is not a {ChildThatStartsAGrandchild.GrandchildName} that had started by {read:O}, when it was read: it is gone or another process's, and is left alone.");

            await cancel.CancelAsync();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run);

            Assert.True(
                await HasExitedAsync(grandchild.Process, Ct),
                $"The caller cancelled and the program's own child, process {grandchild.Id}, was still running {TimeSpan.FromSeconds(20)} later.");
        }
        finally
        {
            // The program is the runner's own child and goes with the cancel; the grandchild is ended only as proven.
            await cancel.CancelAsync();
            if (grandchild is not null)
            {
                OwnedChild.End(grandchild);
                grandchild.Dispose();
            }
        }
    }

    /// <summary>
    /// The proof the row above ends its grandchild by refuses a number that changed hands. A process under a number the
    /// test read is not one the test can start, so the row starts a process of the grandchild's name itself and hands it
    /// to the proof twice: read after it began, it is held; read a minute before it began, as a reused number would be,
    /// it is refused, and it is still running afterwards.
    /// </summary>
    [Fact]
    public void A_number_that_changed_hands_is_refused_and_its_process_left_alone()
    {
        var start = new ProcessStartInfo(Sleeper.Program) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true };
        foreach (var argument in Sleeper.ArgumentsFor(seconds: 60))
        {
            start.ArgumentList.Add(argument);
        }

        using var standIn = OperatingSystemProcess.Start(start)!;
        try
        {
            using (var held = Proven(standIn.Id, DateTimeOffset.UtcNow))
            {
                Assert.True(held is not null, $"Process {standIn.Id}, read after it began, is not held: the proof refuses the process it means.");
            }

            var before = new DateTimeOffset(standIn.StartTime.ToUniversalTime(), TimeSpan.Zero) - TimeSpan.FromMinutes(1);
            using (var refused = Proven(standIn.Id, before))
            {
                Assert.True(refused is null, $"Process {standIn.Id}, read a minute before it began, is held: the proof takes a reused number for the grandchild.");
            }

            Assert.False(standIn.HasExited, $"Process {standIn.Id}, which the proof refused, is no longer running.");
        }
        finally
        {
            OwnedChild.End(standIn);
        }
    }

    /// <summary>
    /// The grandchild by the number it wrote, if the process that has that number is one of its name and had started by
    /// the moment the number was read; null, with the process left alone, otherwise.
    /// </summary>
    private static OwnedChild.Held? Proven(int number, DateTimeOffset read) =>
        OwnedChild.Open(number, read, ChildThatStartsAGrandchild.GrandchildName);

    /// <summary>
    /// A kill of the tree that fails part way — the operating system raises every descendant it could not end as one
    /// aggregate — is absorbed by the stop. It escaped, and on a caller's cancel it took the place of the cancellation,
    /// so the update's skills step reported the kill's failure where it should have said "cancelled".
    /// </summary>
    [Fact]
    public async Task A_kill_of_the_tree_that_fails_part_way_does_not_escape_the_stop()
    {
        var failed = new AggregateException(new System.ComponentModel.Win32Exception(5, "Access is denied."));

        var escaped = await Record.ExceptionAsync(
            () => ProgramRunners.StopAsync(() => throw failed, _ => Task.CompletedTask, TimeSpan.FromSeconds(1)));

        Assert.True(escaped is null, $"The stop let {escaped?.GetType().Name} escape: {escaped?.Message}");
    }

    /// <summary>
    /// The stop returns once the program is gone, not as soon as the kill is sent: a kill lands later than it is asked
    /// for, and the caller goes on to read or remove what the program was writing.
    /// </summary>
    [Fact]
    public async Task The_stop_returns_once_the_program_has_exited()
    {
        var gone = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var stop = ProgramRunners.StopAsync(() => { }, token => gone.Task.WaitAsync(token), TimeSpan.FromMinutes(5));

        Assert.False(stop.IsCompleted, "The stop returned before the program it killed had exited.");
        gone.SetResult();
        await stop.WaitAsync(Ct);
    }

    /// <summary>And it waits a bounded while: a program that never goes is given up on, never waited for for good.</summary>
    [Fact]
    public async Task The_stop_gives_up_waiting_at_the_end_of_its_grace()
    {
        var escaped = await Record.ExceptionAsync(
            () => ProgramRunners.StopAsync(() => { }, token => Task.Delay(Timeout.Infinite, token), TimeSpan.Zero).WaitAsync(Ct));

        Assert.True(escaped is null, $"The stop at the end of its grace threw {escaped?.GetType().Name}: {escaped?.Message}");
    }

    /// <summary>Whether a process is gone, given a while for the kill to land.</summary>
    private static async Task<bool> HasExitedAsync(OperatingSystemProcess process, CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(20);
        while (!process.HasExited && DateTime.UtcNow < deadline)
        {
            await Task.Delay(50, cancellationToken);
        }

        return process.HasExited;
    }

    /// <summary>The working directory is the child's, so a caller can run git inside a clone it just made.</summary>
    [Fact]
    public async Task A_working_directory_is_where_the_program_runs()
    {
        using var tree = new TempPaths();

        var result = await ProgramRunners.ForThisMachine()
            .RunAsync("git", ["rev-parse", "--is-inside-work-tree"], tree.Paths.Root, Generous, Ct);

        // Not a repository, so git says so rather than answering about this one.
        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("not a git repository", result.StandardError, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// A program that will not finish, spelled for whichever machine this is.
    /// </summary>
    /// <remarks>
    /// On Windows this waits by pinging the loopback address rather than with <c>timeout /t</c>, which exits
    /// 125 the instant its output is redirected — and this runner redirects, so the obvious spelling makes a
    /// bound look enforced when nothing was ever waiting. Loopback is not the network: nothing leaves this
    /// machine, which is the same line the runtime's own socket tests are on.
    /// </remarks>
    private static class Sleeper
    {
        public static string Program => OperatingSystem.IsWindows() ? "ping" : "sleep";

        public static IReadOnlyList<string> ArgumentsFor(int seconds) =>
            OperatingSystem.IsWindows()
                ? ["-n", (seconds + 1).ToString(CultureInfo.InvariantCulture), "127.0.0.1"]
                : [seconds.ToString(CultureInfo.InvariantCulture)];
    }

    /// <summary>
    /// A program that starts a program of its own, writes that one's process id to a file, and waits for it, spelled
    /// for whichever machine this is. The grandchild never ends by itself within the test.
    /// </summary>
    private static class ChildThatStartsAGrandchild
    {
        public static string Program => OperatingSystem.IsWindows() ? "powershell" : "sh";

        /// <summary>The name of the process the program starts, as the system reports it.</summary>
        public static string GrandchildName => OperatingSystem.IsWindows() ? "PING" : "sleep";

        public static IReadOnlyList<string> ArgumentsFor(string pidFile) =>
            OperatingSystem.IsWindows()
                ? [
                    "-NoProfile",
                    "-NonInteractive",
                    "-Command",
                    "$p = Start-Process ping -ArgumentList '-n','61','127.0.0.1' -PassThru -NoNewWindow; "
                    + $"Set-Content -LiteralPath '{pidFile.Replace("'", "''", StringComparison.Ordinal)}' -Value $p.Id; "
                    + "$p.WaitForExit()",
                ]
                : ["-c", "sleep 60 & echo $! > \"$1\"; wait", "sh", pidFile];

        /// <summary>The number the program wrote, and the moment it was read, which the grandchild had started by.</summary>
        public static async Task<(int Number, DateTimeOffset Read)> WaitForGrandchildAsync(string pidFile, CancellationToken cancellationToken)
        {
            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(60);
            while (DateTime.UtcNow < deadline)
            {
                if (File.Exists(pidFile))
                {
                    string text;
                    try
                    {
                        text = await File.ReadAllTextAsync(pidFile, cancellationToken);
                    }
                    catch (IOException)
                    {
                        text = string.Empty;
                    }

                    if (int.TryParse(text.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var id))
                    {
                        return (id, DateTimeOffset.UtcNow);
                    }
                }

                await Task.Delay(50, cancellationToken);
            }

            throw new TimeoutException($"The program never wrote the process id of its child to '{pidFile}'.");
        }
    }
}
