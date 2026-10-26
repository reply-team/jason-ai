using Jason.Cli;
using Jason.Cli.Process;
using Jason.Cli.Status;
using Jason.Cli.Tests.Process;
using Jason.Contracts.Programs;
using Jason.Runtime.Tests.Plugins;

namespace Jason.Cli.Tests.Status;

/// <summary>
/// The provider check finds the program it is named the way Jason finds every program it starts by name, and says
/// which file that was. On Windows an npm-installed CLI is a <c>.cmd</c> shim, which no program may be started
/// through: the check reads it, runs the interpreter and entry script it names, and never starts the shim itself.
/// </summary>
/// <remarks>
/// Each row hands the check a search path of its own, in front of the one the status rows give it, so what is found
/// is what the row wrote and never what the machine running the suite has installed. A file is named as its directory
/// lists it, never in <c>PATHEXT</c>'s case, so every row compares paths exactly.
/// </remarks>
public class ProviderCliCheckTests
{
    private const string WindowsOnly = "npm writes a .cmd shim only on Windows, and only there is a name's extension part of the lookup.";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task An_npm_shim_is_read_and_the_interpreter_it_names_runs_the_entry_script_with_version()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), WindowsOnly);
        using var dir = new TempPaths();
        using var programs = new TestPrograms();
        var node = programs.Add("node.exe");
        var entry = InstallAcmeCli(programs);

        var (check, runner) = await CheckAsync(dir, "acme-sdr", programs.Root);

        var asked = Assert.Single(runner.Requested);
        Assert.Equal(node, asked.Program);
        Assert.Equal([entry, "--version"], asked.Arguments);
        Assert.Equal(CheckState.Ok, check.State);
        Assert.Contains(Path.Combine(programs.Root, "acme-sdr.cmd"), check.Fact, StringComparison.Ordinal);
        Assert.Contains("npm's shim", check.Fact, StringComparison.Ordinal);
        Assert.Contains(entry, check.Fact, StringComparison.Ordinal);
        Assert.Contains(node, check.Fact, StringComparison.Ordinal);
        Assert.Contains("'acme-sdr --version' answered.", check.Fact, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_program_on_the_search_path_is_run_as_the_file_it_resolves_to()
    {
        using var dir = new TempPaths();
        using var programs = new TestPrograms();
        var program = programs.AddProgram("acme-sdr");

        var (check, runner) = await CheckAsync(dir, "acme-sdr", programs.Root);

        var asked = Assert.Single(runner.Requested);
        Assert.Equal(program, asked.Program);
        Assert.Equal(["--version"], asked.Arguments);
        Assert.Equal(CheckState.Ok, check.State);
        Assert.Contains($"'acme-sdr' resolves to {program}.", check.Fact, StringComparison.Ordinal);
    }

    /// <summary>
    /// A file with no extension at all is what npm writes beside its shim for a POSIX shell. The plugin registry finds
    /// no program in it on Windows, and neither does this check: the same rule, so the same answer.
    /// </summary>
    [Fact]
    public async Task A_bare_file_with_no_extension_is_not_found_on_Windows_as_the_plugin_registry_holds()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), WindowsOnly);
        using var dir = new TempPaths();
        using var programs = new TestPrograms();
        programs.Add("acme-sdr");

        var (check, runner) = await CheckAsync(dir, "acme-sdr", programs.Root);

        Assert.Empty(runner.Requested);
        Assert.Equal(CheckState.Absent, check.State);
        Assert.Contains("'acme-sdr' was not found on the search path", check.Fact, StringComparison.Ordinal);
    }

    /// <summary>A shim is the last resort, never the first hit: installing one must not change what a name already ran.</summary>
    [Fact]
    public async Task An_exe_in_a_later_directory_wins_over_npm_shim_in_an_earlier_one()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), WindowsOnly);
        using var dir = new TempPaths();
        using var shims = new TestPrograms();
        using var later = new TestPrograms();
        shims.Add("node.exe");
        InstallAcmeCli(shims);
        var program = later.AddProgram("acme-sdr");

        var (check, runner) = await CheckAsync(dir, "acme-sdr", shims.Root, later.Root);

        var asked = Assert.Single(runner.Requested);
        Assert.Equal(program, asked.Program);
        Assert.Equal(["--version"], asked.Arguments);
        Assert.Contains($"'acme-sdr' resolves to {program}.", check.Fact, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(".bat")]
    [InlineData(".ps1")]
    public async Task A_script_only_a_shell_could_run_is_refused_and_nothing_is_run(string extension)
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), WindowsOnly);
        using var dir = new TempPaths();
        using var programs = new TestPrograms();
        var script = programs.Add("acme-sdr" + extension);

        var (check, runner) = await CheckAsync(dir, "acme-sdr", programs.Root);

        Assert.Empty(runner.Requested);
        Assert.Equal(CheckState.Absent, check.State);
        Assert.Contains($"'acme-sdr' resolves only to {script}, a script a shell would have to interpret", check.Fact, StringComparison.Ordinal);
        Assert.Contains("'acme-sdr --version' was not run", check.Fact, StringComparison.Ordinal);
        Assert.EndsWith(Instead, check.Fact, StringComparison.Ordinal);
    }

    /// <summary>The rule a refused shim broke is the lookup's own words, never a second copy of them.</summary>
    [Fact]
    public async Task A_cmd_that_is_not_npm_shim_is_refused_with_the_rule_it_broke_and_nothing_is_run()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), WindowsOnly);
        using var dir = new TempPaths();
        using var programs = new TestPrograms();
        var shim = programs.Add("acme-sdr.cmd", "@ECHO off\r\ncmd /c whatever %*\r\n");
        var onPath = new ProgramSearchPath(programs.Root, null);
        var refusal = ProgramLookup.Find("acme-sdr", onPath, onPath).Refusal;

        var (check, runner) = await CheckAsync(dir, "acme-sdr", programs.Root);

        Assert.Empty(runner.Requested);
        Assert.Equal(CheckState.Absent, check.State);
        Assert.False(string.IsNullOrEmpty(refusal));
        Assert.Contains($"'acme-sdr' resolves only to {shim}, which {refusal}", check.Fact, StringComparison.Ordinal);
        Assert.Contains("'acme-sdr --version' was not run", check.Fact, StringComparison.Ordinal);
        Assert.EndsWith(Instead, check.Fact, StringComparison.Ordinal);
    }

    /// <summary>
    /// A name given with a script's extension names a file that is there, so "not found" would be false. The check
    /// names the file, refuses it as a script, and says that the name without its extension is what to give.
    /// </summary>
    [Fact]
    public async Task A_name_given_with_a_script_extension_names_the_script_and_the_name_to_give()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), WindowsOnly);
        using var dir = new TempPaths();
        using var programs = new TestPrograms();
        programs.Add("node.exe");
        InstallAcmeCli(programs);

        var (check, runner) = await CheckAsync(dir, "acme-sdr.cmd", programs.Root);

        Assert.Empty(runner.Requested);
        Assert.Equal(CheckState.Absent, check.State);
        Assert.Contains(
            $"'acme-sdr.cmd' resolves only to {Path.Combine(programs.Root, "acme-sdr.cmd")}, a script a shell would have to interpret",
            check.Fact,
            StringComparison.Ordinal);
        Assert.Contains("'acme-sdr.cmd --version' was not run", check.Fact, StringComparison.Ordinal);
        Assert.Contains("give 'acme-sdr', the name without its extension", check.Fact, StringComparison.Ordinal);
        Assert.DoesNotContain("not found", check.Fact, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_name_nothing_on_the_search_path_answers_for_is_not_found_and_nothing_is_run()
    {
        using var dir = new TempPaths();
        using var programs = new TestPrograms();

        var (check, runner) = await CheckAsync(dir, "acme-sdr", programs.Root);

        Assert.Empty(runner.Requested);
        Assert.Equal(CheckState.Absent, check.State);
        Assert.Contains("'acme-sdr' was not found on the search path, so 'acme-sdr --version' was not run.", check.Fact, StringComparison.Ordinal);
        Assert.DoesNotContain("did not answer", check.Fact, StringComparison.Ordinal);
    }

    /// <summary>
    /// Found and failing is a different fact from not found, and a person acts on them differently: one is an
    /// install, the other is the program's own trouble.
    /// </summary>
    [Fact]
    public async Task A_program_that_is_found_and_fails_says_so_apart_from_one_that_is_not_found()
    {
        using var dir = new TempPaths();
        using var programs = new TestPrograms();
        var program = programs.AddProgram("acme-sdr");

        var (check, runner) = await CheckAsync(dir, "acme-sdr", programs.Root, answer: new ProgramResult(3, string.Empty, string.Empty, false));

        Assert.Single(runner.Requested);
        Assert.Equal(CheckState.Absent, check.State);
        Assert.Contains($"'acme-sdr' resolves to {program}.", check.Fact, StringComparison.Ordinal);
        Assert.Contains("'acme-sdr --version' did not answer: exit code 3.", check.Fact, StringComparison.Ordinal);
        Assert.DoesNotContain("not found", check.Fact, StringComparison.Ordinal);
    }

    /// <summary>
    /// A program that could not be started returned no code at all, so the check says it could not be started and
    /// names no exit code: -1 is the runner's, never the program's.
    /// </summary>
    [Fact]
    public async Task A_program_that_could_not_be_started_says_so_and_names_no_exit_code()
    {
        using var dir = new TempPaths();
        using var programs = new TestPrograms();
        var program = programs.AddProgram("acme-sdr");
        var notStarted = new ProgramResult(-1, string.Empty, "The program could not be started.", false, NotStarted: true);

        var (check, runner) = await CheckAsync(dir, "acme-sdr", programs.Root, answer: notStarted);

        Assert.Single(runner.Requested);
        Assert.Equal(CheckState.Absent, check.State);
        Assert.Contains($"'acme-sdr' resolves to {program}. 'acme-sdr --version' could not be started.", check.Fact, StringComparison.Ordinal);
        Assert.DoesNotContain("exit code", check.Fact, StringComparison.Ordinal);
    }

    /// <summary>A value with a directory in it is a path, and is looked up in that directory alone by the same rule.</summary>
    [Fact]
    public async Task A_path_is_looked_up_in_its_own_directory()
    {
        using var dir = new TempPaths();
        using var elsewhere = new TestPrograms();
        using var programs = new TestPrograms();
        var program = elsewhere.AddProgram("acme-sdr");

        var (check, runner) = await CheckAsync(dir, program, programs.Root);

        var asked = Assert.Single(runner.Requested);
        Assert.Equal(program, asked.Program);
        Assert.Equal(["--version"], asked.Arguments);
        Assert.Contains($"resolves to {program}.", check.Fact, StringComparison.Ordinal);
    }

    /// <summary>The directory a path names is the only place it is looked for, so a name the search path holds is not found through it.</summary>
    [Fact]
    public async Task A_path_into_a_directory_without_the_name_is_not_found_there_while_the_search_path_holds_it()
    {
        using var dir = new TempPaths();
        using var elsewhere = new TestPrograms();
        using var programs = new TestPrograms();
        programs.AddProgram("acme-sdr");

        var (check, runner) = await CheckAsync(dir, Path.Combine(elsewhere.Root, "acme-sdr"), programs.Root);

        Assert.Empty(runner.Requested);
        Assert.Equal(CheckState.Absent, check.State);
        Assert.Contains($"'acme-sdr' was not found in {elsewhere.Root}, so", check.Fact, StringComparison.Ordinal);
    }

    /// <summary>
    /// A path into npm's own directory finds the shim there and the interpreter on the search path, where the shim
    /// itself finds it when npm placed none beside it.
    /// </summary>
    [Fact]
    public async Task A_path_to_npm_shim_runs_the_interpreter_on_the_search_path_with_the_entry_script()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), WindowsOnly);
        using var dir = new TempPaths();
        using var npm = new TestPrograms();
        using var nodeDirectory = new TestPrograms();
        var entry = InstallAcmeCli(npm);
        var node = nodeDirectory.Add("node.exe");

        var (check, runner) = await CheckAsync(dir, Path.Combine(npm.Root, "acme-sdr"), nodeDirectory.Root);

        var asked = Assert.Single(runner.Requested);
        Assert.Equal(node, asked.Program);
        Assert.Equal([entry, "--version"], asked.Arguments);
        Assert.Equal(CheckState.Ok, check.State);
        Assert.Contains($"{Path.Combine(npm.Root, "acme-sdr.cmd")}, npm's shim, which runs {entry} with {node}.", check.Fact, StringComparison.Ordinal);
    }

    /// <summary>The clause that ends every refusal: what Jason can run in place of the refused file.</summary>
    private const string Instead = "it can run an .exe, a .com or npm's own shim for 'acme-sdr', or the CLI's own executable given by path.";

    /// <summary>
    /// The status rows' machine with these directories in front of its own search path, so <c>jason</c> still
    /// resolves and every other check reads as it does there.
    /// </summary>
    private static async Task<(StatusCheck Check, FakeProgramRunner Runner)> CheckAsync(
        TempPaths dir, string providerCli, string first, string? second = null, ProgramResult? answer = null)
    {
        var output = new StringWriter();
        StatusCommandTests.Running(dir);
        var runner = new FakeProgramRunner(_ => answer ?? new ProgramResult(0, "acme-sdr 1.2.3", string.Empty, false));
        var machine = StatusCommandTests.Machine(dir, output, runner);
        string[] directories = second is null ? [first, machine.SearchPath!] : [first, second, machine.SearchPath!];

        await CliApp.RunAsync(["status", "--provider-cli", providerCli], machine with { SearchPath = string.Join(Path.PathSeparator, directories) }, Ct);

        return (Assert.Single(StatusCommandTests.Read(output).Checks, check => check.Name == "provider_cli"), runner);
    }

    /// <summary>A package tree npm would have written for <c>acme-sdr</c>: the entry script, the manifest declaring it, the shim.</summary>
    private static string InstallAcmeCli(TestPrograms programs)
    {
        var entry = programs.AddNested(@"node_modules\acme-sdr-cli\dist\index.js", "// the entry point");
        programs.AddNested(@"node_modules\acme-sdr-cli\package.json", "{\"name\":\"acme-sdr-cli\",\"bin\":{\"acme-sdr\":\"dist/index.js\"}}");
        programs.Add("acme-sdr.cmd", TestPrograms.NpmShim("acme-sdr-cli", @"dist\index.js"));
        return entry;
    }
}
