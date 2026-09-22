using Jason.Cli;
using Jason.Cli.Process;
using Jason.Cli.Skills;
using Jason.Cli.Status;
using Jason.Cli.Tests.Autostart;
using Jason.Cli.Tests.Process;

namespace Jason.Cli.Tests.Status;

/// <summary>
/// What keeps <c>jason status</c> from becoming <c>jason doctor</c>.
/// </summary>
/// <remarks>
/// <para>
/// A check may report a fact and the command that repairs it. It may not read a log, and it may not reach the
/// machine except through the one declared runner and with the one published command. Diagnostics — reading
/// logs, explaining failures, probing — is a story of its own, and these three facts are the whole of what
/// stands between the two.
/// </para>
/// <para>
/// The rule is stated the way the code can keep it. "No check runs a probe of its own" would already have been
/// false the day it was written, because one check runs a provider CLI; a rule a reader can see is broken
/// teaches them the rule is decorative. What is true, and worth holding, is that the only program any check
/// runs is the one the caller named, with one argument, through the one declared runner.
/// </para>
/// </remarks>
public partial class StatusDisciplineTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public void No_check_reads_a_log_or_reaches_the_machine_behind_the_runner()
    {
        var sources = Directory.EnumerateFiles(StatusSources(), "*.cs").ToList();
        Assert.NotEmpty(sources);

        foreach (var source in sources)
        {
            var text = File.ReadAllText(source);
            var name = Path.GetFileName(source);

            Assert.True(
                !text.Contains("LogsDirectory", StringComparison.Ordinal),
                $"'{name}' reads the log directory. A check reports a fact and the command that repairs it; "
                + "reading a log to explain a failure is what a diagnostics verb would be for.");

            Assert.True(
                !text.Contains("System.Diagnostics", StringComparison.Ordinal),
                $"'{name}' reaches the process table directly. The one way a check may start a program is "
                + $"{nameof(IProgramRunner)}, which a test can substitute and this guard can read.");
        }
    }

    /// <summary>
    /// And exactly one program is ever asked for — the one the caller named — with exactly one argument. The
    /// scan above cannot see this: a second probe added behind the same runner would pass it silently, which
    /// is precisely how a status verb grows into a diagnostics one.
    /// </summary>
    [Fact]
    public async Task The_only_program_any_check_runs_is_the_one_the_documentation_publishes()
    {
        using var dir = new TempPaths();
        var runner = new FakeProgramRunner(_ => new ProgramResult(1, string.Empty, "not found", false));

        await CliApp.RunAsync(["status", "--provider-cli", "acme-sdr"], Machine(dir, runner), Ct);

        var asked = Assert.Single(runner.Requested);
        Assert.True(
            asked is { Program: "acme-sdr", Arguments: ["--version"] },
            $"A check ran '{asked.Program} {string.Join(' ', asked.Arguments)}'. The only program this verb may "
            + "run is the one the caller named, with --version; anything else is a probe, and a probe is a "
            + "diagnostics verb.");
    }

    /// <summary>
    /// Every repair a check prints is a command this program understands. A status verb whose advice is a
    /// usage error teaches the operator the tool is broken at the moment they most need it not to be — and
    /// this is the guard that will make the day the skills verb arrives the day its repairs become real.
    /// </summary>
    [Fact]
    public async Task Every_repair_a_check_prints_is_a_command_this_program_understands()
    {
        // Every repair this verb can print, read off the source rather than off one run of it. One shape of
        // machine prints three of them; the guard claimed it reached every one, which is how a repair that is
        // itself a usage error shipped green.
        var repairs = Printed();
        Assert.True(repairs.Count >= 4, $"Only {repairs.Count} repairs were found in the source, which is fewer than this verb prints.");

        foreach (var repair in repairs)
        {
            using var typing = new TempPaths();
            var error = new StringWriter();
            var exit = await CliApp.RunAsync(
                [.. repair.Split(' ', StringSplitOptions.RemoveEmptyEntries).Skip(1)],
                new CliEnvironment(new StringWriter(), error, typing.Paths, Autostart: new RecordingRegistrar()),
                Ct);

            Assert.True(
                exit != ExitCodes.Usage,
                $"A check prints '{repair}' as the way to repair it, and this program does not understand it:"
                + $"{Environment.NewLine}{error}");
        }
    }

    /// <summary>
    /// Every repair string this verb can print, taken from the one class that names them and from the checks
    /// themselves. A run only ever prints the repairs its own machine's state calls for.
    /// </summary>
    private static IReadOnlyList<string> Printed()
    {
        var source = File.ReadAllText(Path.Combine(StatusSources(), "StatusChecks.cs"));
        var opens = source.IndexOf("private static class Repair", StringComparison.Ordinal);
        Assert.True(opens > 0, "The class that names every repair is not there any more, so this guard cannot read them.");

        var body = source[opens..];
        var closes = body.IndexOf("\n    }", StringComparison.Ordinal);
        Assert.True(closes > 0, "The class that names every repair has no end, so this guard cannot read it.");

        return [.. Repair().Matches(body[..closes])
            .Select(match => match.Groups[1].Value)
            .Distinct(StringComparer.Ordinal)];
    }

    [System.Text.RegularExpressions.GeneratedRegex("\"(jason [^\"]*)\"")]
    private static partial System.Text.RegularExpressions.Regex Repair();

    private static CliEnvironment Machine(TempPaths dir, IProgramRunner runner, StringWriter? output = null) =>
        new(
            output ?? new StringWriter(),
            new StringWriter(),
            dir.Paths,
            Processes: new FakeProcessControl(),
            Autostart: new RecordingRegistrar(),
            Harnesses: HarnessLocators.At(Path.Combine(dir.Paths.Root, "no-harness-here")),
            Programs: runner,
            SearchPath: Path.Combine(dir.Paths.Root, "nowhere"));

    /// <summary>The checks' own directory, found from the repository root the way every guard here does.</summary>
    private static string StatusSources()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "global.json")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return Path.Combine(directory.FullName, "runtime", "src", "Jason.Cli", "Status");
    }
}
