using System.Diagnostics;
using System.Globalization;
using Jason.Cli.Process;

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
        Assert.Contains("git version", result.StandardOutput, StringComparison.Ordinal);
    }

    /// <summary>
    /// A program that is not there is an answer, not an exception. Every caller of this is reporting on an
    /// installation, and "the thing you asked about is not installed" is the most ordinary answer there is.
    /// </summary>
    [Fact]
    public async Task A_program_that_is_not_there_answers_rather_than_throwing()
    {
        var result = await ProgramRunners.ForThisMachine()
            .RunAsync("jason-no-such-program-exists", [], null, Generous, Ct);

        Assert.NotEqual(0, result.ExitCode);
        Assert.False(result.TimedOut);
        Assert.NotEqual(string.Empty, result.StandardError);
    }

    /// <summary>
    /// And one that will not finish is stopped and said to have timed out. A check that hangs is a status verb
    /// that never answers, which is worse than one that says it could not tell.
    /// </summary>
    [Fact]
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
}
