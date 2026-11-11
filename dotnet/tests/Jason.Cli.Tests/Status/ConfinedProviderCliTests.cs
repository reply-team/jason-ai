using Jason.Cli.Process;
using Jason.Cli.Status;
using Jason.Cli.Tests.Process;
using Jason.Contracts.Execution;
using Jason.Runtime.Tests.Plugins;

namespace Jason.Cli.Tests.Status;

/// <summary>
/// Under <c>JASON_CONFINED</c>, <c>status --provider-cli</c> runs a program named by a bare name only, and looks that name
/// up in the fully qualified entries of the search path alone: a value with a directory in it is not looked up and
/// nothing is started, and a relative or drive-relative entry is skipped, so a working folder is never searched by name.
/// The check says so in the body and <c>status</c> keeps its contract. Unset or empty, nothing changes.
/// </summary>
public class ConfinedProviderCliTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly Dictionary<string, string> Unset = [];

    private static Dictionary<string, string> Set(string value) => new() { [Confinement.Variable] = value };

    public static TheoryData<bool> Separators() => [false, true];

    [Theory]
    [MemberData(nameof(Separators))]
    public async Task Confined_a_path_starts_nothing_and_the_check_says_why(bool alternate)
    {
        using var dir = new TempPaths();
        using var programs = new TestPrograms();
        var program = programs.AddProgram("acme-sdr");
        var value = alternate ? program.Replace(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) : program;

        var confined = await StatusAsync(dir, value, programs.Root, Set("1"));
        var plain = await StatusAsync(dir, provider: null, programs.Root, Unset);

        Assert.Empty(confined.Runner.Requested);
        Assert.Equal(CheckState.Unknown, confined.Check.State);
        Assert.Equal(
            $"This environment cannot run a program named by a path, so '{value} --version' was not run: {Confinement.Variable} is set. "
                + "Name a program on the search path, or run it in a terminal.",
            confined.Check.Fact);
        Assert.Equal(plain.Exit, confined.Exit);
        Assert.Equal(plain.Report.Ready, confined.Report.Ready);
    }

    /// <summary>
    /// A search path entry that is not fully qualified names a folder relative to wherever the CLI runs, so under the
    /// variable it is skipped and the name is not found there; the same entry, unconfined, finds and runs it as before.
    /// </summary>
    [Theory]
    [MemberData(nameof(Separators))]
    public async Task Confined_a_relative_search_path_entry_is_skipped(bool driveRelative)
    {
        Assert.SkipWhen(driveRelative && !OperatingSystem.IsWindows(), "Only Windows has a path rooted on no drive.");
        using var dir = new TempPaths();
        using var programs = new TestPrograms();
        programs.AddProgram("acme-sdr");
        var entry = driveRelative ? programs.Root[2..] : Path.GetRelativePath(Environment.CurrentDirectory, programs.Root);
        Assert.SkipWhen(Path.IsPathFullyQualified(entry), "The temporary folder is on another drive than the working folder.");

        var confined = await StatusAsync(dir, "acme-sdr", entry, Set("1"));
        var plain = await StatusAsync(dir, "acme-sdr", entry, Unset);

        Assert.Empty(confined.Runner.Requested);
        Assert.Equal(CheckState.Absent, confined.Check.State);
        Assert.Single(plain.Runner.Requested);
        Assert.Equal(CheckState.Ok, plain.Check.State);
    }

    /// <summary>A bare name on a fully qualified entry is found and run under the variable as it is without it.</summary>
    [Fact]
    public async Task Confined_a_bare_name_on_a_fully_qualified_entry_runs_as_before()
    {
        using var dir = new TempPaths();
        using var programs = new TestPrograms();
        var program = programs.AddProgram("acme-sdr");

        var confined = await StatusAsync(dir, "acme-sdr", programs.Root, Set("1"));

        Assert.Equal(program, Assert.Single(confined.Runner.Requested).Program);
        Assert.Equal(CheckState.Ok, confined.Check.State);
    }

    /// <summary>Unset and empty are today's behaviour, byte for byte, for a path and for a relative entry alike.</summary>
    [Theory]
    [MemberData(nameof(Separators))]
    public async Task Unset_or_empty_the_check_is_as_before(bool byPath)
    {
        using var dir = new TempPaths();
        using var programs = new TestPrograms();
        var program = programs.AddProgram("acme-sdr");
        var relative = Path.GetRelativePath(Environment.CurrentDirectory, programs.Root);

        var unset = await StatusAsync(dir, byPath ? program : "acme-sdr", byPath ? programs.Root : relative, Unset);
        var empty = await StatusAsync(dir, byPath ? program : "acme-sdr", byPath ? programs.Root : relative, Set(string.Empty));

        Assert.Equal(unset.Output, empty.Output);
        Assert.Equal(unset.Exit, empty.Exit);
        Assert.Equal(Asked(unset), Asked(empty));
        Assert.Equal(program, Assert.Single(unset.Runner.Requested).Program);
    }

    /// <summary>The verb's help says the rule.</summary>
    [Fact]
    public async Task The_help_says_the_rule()
    {
        using var dir = new TempPaths();
        var output = new StringWriter();

        await CliApp.RunAsync(["status", "--help"], new CliEnvironment(output, new StringWriter(), dir.Paths), Ct);

        var text = string.Join(' ', output.ToString().Split((char[])[' ', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries));
        Assert.Contains(
            $"With {Confinement.Variable} set, only a bare name is run, found in a fully qualified entry of PATH; a path is not run.",
            text,
            StringComparison.Ordinal);
    }

    private static List<string> Asked(Status status) =>
        [.. status.Runner.Requested.Select(request => $"{request.Program} {string.Join(' ', request.Arguments)}")];

    private sealed record Status(int Exit, string Output, StatusReport Report, StatusCheck Check, FakeProgramRunner Runner);

    /// <summary>The status rows' machine with <paramref name="first"/> in front of its own search path.</summary>
    private static async Task<Status> StatusAsync(TempPaths dir, string? provider, string first, Dictionary<string, string> variables)
    {
        var output = new StringWriter();
        StatusCommandTests.Running(dir);
        var runner = new FakeProgramRunner(_ => new ProgramResult(0, "acme-sdr 1.2.3", string.Empty, false));
        var machine = StatusCommandTests.Machine(dir, output, runner);
        string[] args = provider is null ? ["status"] : ["status", "--provider-cli", provider];

        var exit = await CliApp.RunAsync(
            args,
            machine with { SearchPath = string.Join(Path.PathSeparator, first, machine.SearchPath!), Variables = variables },
            Ct);

        var report = StatusCommandTests.Read(output);
        return new Status(exit, output.ToString(), report, Assert.Single(report.Checks, check => check.Name == "provider_cli"), runner);
    }
}
