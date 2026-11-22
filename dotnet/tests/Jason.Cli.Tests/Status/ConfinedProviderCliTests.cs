using Jason.Cli.Process;
using Jason.Cli.Status;
using Jason.Cli.Tests.Process;
using Jason.Contracts.Execution;
using Jason.Runtime.Tests.Plugins;

namespace Jason.Cli.Tests.Status;

/// <summary>
/// <c>status --provider-cli</c> looks a bare name up in the fully qualified entries of the search path alone, with
/// <c>JASON_CONFINED</c> and without it: a relative or drive-relative entry is skipped, so a working folder is never searched
/// by name. Under the variable it also runs a program named by a bare name only: a path, a value naming a drive among
/// them, is not looked up and nothing is started. The check says so in the body and <c>status</c> keeps its contract.
/// Unset and empty are the same.
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
    /// A chat turn of the desktop, in every mode, sets <c>JASON_CHAT_TURN</c>: under it, as under <c>JASON_CONFINED</c>, a
    /// path starts nothing, and the check names the chat's variable.
    /// </summary>
    [Theory]
    [MemberData(nameof(Separators))]
    public async Task In_a_chat_turn_a_path_starts_nothing_and_the_check_says_why(bool alternate)
    {
        using var dir = new TempPaths();
        using var programs = new TestPrograms();
        var program = programs.AddProgram("acme-sdr");
        var value = alternate ? program.Replace(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) : program;

        var turn = await StatusAsync(dir, value, programs.Root, new Dictionary<string, string> { [Confinement.ChatTurnVariable] = "1" });

        Assert.Empty(turn.Runner.Requested);
        Assert.Equal(CheckState.Unknown, turn.Check.State);
        Assert.Equal(
            $"This environment cannot run a program named by a path, so '{value} --version' was not run: {Confinement.ChatTurnVariable} is set. "
                + "Name a program on the search path, or run it in a terminal.",
            turn.Check.Fact);
    }

    /// <summary>
    /// A search path entry that is not fully qualified names a folder relative to wherever the CLI runs, so it is skipped
    /// with the variable and without it: the name is not found there, nothing is run, and the check says the same both
    /// ways. The plain entry is a folder made in the current folder and named by its name alone, so the program stands
    /// where the entry would find it were the entry read, on every system.
    /// </summary>
    [Theory]
    [MemberData(nameof(Separators))]
    public async Task A_relative_search_path_entry_is_skipped_with_the_variable_and_without_it(bool driveRelative)
    {
        Assert.SkipWhen(driveRelative && !OperatingSystem.IsWindows(), "Only Windows has a path rooted on no drive.");
        using var dir = new TempPaths();
        using var programs = new TestPrograms();
        using var current = new CurrentFolderFiles();
        string entry;
        if (driveRelative)
        {
            programs.AddProgram("acme-sdr");
            entry = programs.Root[2..];
            Assert.SkipWhen(
                !string.Equals(Path.GetPathRoot(Environment.CurrentDirectory), Path.GetPathRoot(programs.Root), StringComparison.OrdinalIgnoreCase),
                "The temporary folder is on another drive than the working folder.");
        }
        else
        {
            current.AddProgramInAFolder("acme-sdr");
            entry = current.Name;
        }

        Assert.False(Path.IsPathFullyQualified(entry), $"'{entry}' is a full path.");

        var confined = await StatusAsync(dir, "acme-sdr", entry, Set("1"));
        var plain = await StatusAsync(dir, "acme-sdr", entry, Unset);

        Assert.Empty(confined.Runner.Requested);
        Assert.Empty(plain.Runner.Requested);
        Assert.Equal(CheckState.Absent, confined.Check.State);
        Assert.Equal(CheckState.Absent, plain.Check.State);
        Assert.StartsWith("'acme-sdr' was not found on the search path, so 'acme-sdr --version' was not run.", plain.Check.Fact, StringComparison.Ordinal);
        Assert.Equal(plain.Check.Fact, confined.Check.Fact);
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

    /// <summary>Unset and empty are the same, byte for byte, for a path and for a bare name on a fully qualified entry alike.</summary>
    [Theory]
    [MemberData(nameof(Separators))]
    public async Task Unset_and_empty_are_the_same(bool byPath)
    {
        using var dir = new TempPaths();
        using var programs = new TestPrograms();
        var program = programs.AddProgram("acme-sdr");

        var unset = await StatusAsync(dir, byPath ? program : "acme-sdr", programs.Root, Unset);
        var empty = await StatusAsync(dir, byPath ? program : "acme-sdr", programs.Root, Set(string.Empty));

        Assert.Equal(unset.Output, empty.Output);
        Assert.Equal(unset.Exit, empty.Exit);
        Assert.Equal(Asked(unset), Asked(empty));
        Assert.Equal(program, Assert.Single(unset.Runner.Requested).Program);
    }

    public static TheoryData<bool> CurrentFolderForms() => [false, true];

    /// <summary>
    /// A value with a drive in it names a folder too: <c>C:acme-sdr</c> is <c>acme-sdr</c> in the current folder of drive
    /// C, which under the chat is the chat's folder. Under the variable it gets the path answer and nothing is looked up,
    /// though a program of that name stands on the search path and, apart, a program or npm's shim for it stands in the
    /// current folder. Windows alone has a drive; the win-x64 leg of CI runs this row.
    /// </summary>
    [Theory]
    [MemberData(nameof(CurrentFolderForms))]
    public async Task Confined_a_name_with_a_drive_is_a_path_and_starts_nothing(bool shim)
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Only Windows names a drive.");
        using var dir = new TempPaths();
        using var programs = new TestPrograms();
        using var current = new CurrentFolderFiles();
        programs.AddProgram(current.Name);
        programs.Add("node.exe");
        if (shim)
        {
            current.AddShim();
        }
        else
        {
            current.AddProgram();
        }

        var value = current.DriveRelative;
        var confined = await StatusAsync(dir, value, programs.Root, Set("1"));

        Assert.Empty(confined.Runner.Requested);
        Assert.Equal(CheckState.Unknown, confined.Check.State);
        Assert.Equal(
            $"This environment cannot run a program named by a path, so '{value} --version' was not run: {Confinement.Variable} is set. "
                + "Name a program on the search path, or run it in a terminal.",
            confined.Check.Fact);
    }

    /// <summary>
    /// Without the variable the same value is a path as well: it is looked for in the folder it names, the current folder
    /// of its drive, and the search path is not read for it. Windows alone has a drive; the win-x64 leg of CI runs this
    /// row.
    /// </summary>
    [Fact]
    public async Task Unconfined_a_name_with_a_drive_is_looked_for_as_a_path()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Only Windows names a drive.");
        using var dir = new TempPaths();
        using var programs = new TestPrograms();
        using var current = new CurrentFolderFiles();
        var program = current.AddProgram();

        var plain = await StatusAsync(dir, current.DriveRelative, programs.Root, Unset);

        Assert.Equal(program, Assert.Single(plain.Runner.Requested).Program, ignoreCase: true);
        Assert.Equal(CheckState.Ok, plain.Check.State);
        Assert.DoesNotContain("It read the CLI's PATH", plain.Check.Fact, StringComparison.Ordinal);
    }

    /// <summary>
    /// Files of one name made in the process's current folder for one row and taken off after it. The name is new to
    /// every row, so rows running beside each other never meet, and nothing moves the current folder itself.
    /// </summary>
    private sealed class CurrentFolderFiles : IDisposable
    {
        private readonly string _folder = Directory.GetCurrentDirectory();
        private readonly List<string> _made = [];

        public string Name { get; } = "acme-sdr-" + Guid.NewGuid().ToString("N");

        /// <summary>The name with the current folder's drive before it, and no separator anywhere.</summary>
        public string DriveRelative => $"{_folder[0]}:{Name}";

        public string AddProgram() => Write(Name + ".exe", "not a real program");

        /// <summary>
        /// A program called <paramref name="program"/> in a folder of this row's name in the current folder, so the entry
        /// <see cref="Name"/>, relative, names that folder: an <c>.exe</c> on Windows, a file with the execute bit elsewhere.
        /// </summary>
        public string AddProgramInAFolder(string program)
        {
            var folder = Path.Combine(_folder, Name);
            Directory.CreateDirectory(folder);
            _made.Add(folder);
            var file = Path.Combine(folder, program + (OperatingSystem.IsWindows() ? ".exe" : string.Empty));
            File.WriteAllText(file, "not a real program");
            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(file, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            }

            return file;
        }

        public string AddShim()
        {
            var package = Path.Combine(_folder, "node_modules", Name + "-cli");
            Directory.CreateDirectory(Path.Combine(package, "dist"));
            _made.Add(package);
            File.WriteAllText(Path.Combine(package, "dist", "index.js"), "// the entry point");
            File.WriteAllText(Path.Combine(package, "package.json"), $"{{\"name\":\"{Name}-cli\",\"bin\":{{\"{Name}\":\"dist/index.js\"}}}}");
            return Write(Name + ".cmd", TestPrograms.NpmShim(Name + "-cli", @"dist\index.js"));
        }

        private string Write(string fileName, string content)
        {
            var path = Path.Combine(_folder, fileName);
            File.WriteAllText(path, content);
            _made.Add(path);
            return path;
        }

        public void Dispose()
        {
            foreach (var made in _made)
            {
                try
                {
                    if (Directory.Exists(made))
                    {
                        Directory.Delete(made, recursive: true);
                    }
                    else
                    {
                        File.Delete(made);
                    }
                }
                catch (IOException)
                {
                }
            }

            try
            {
                Directory.Delete(Path.Combine(_folder, "node_modules"));
            }
            catch (IOException)
            {
                // Another row's package is still there, or the folder was never made.
            }
        }
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
            $"With {Confinement.Variable} or {Confinement.ChatTurnVariable} set, only a bare name is run, found in a fully qualified entry of PATH; a path is not run.",
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
