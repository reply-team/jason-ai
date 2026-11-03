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

    /// <summary>
    /// npm's shim for an executable the package ships names no interpreter: the executable runs by itself, and the fact
    /// says both the shim the name came to and the file that ran, since the two are not the same file.
    /// </summary>
    [Fact]
    public async Task An_npm_shim_with_no_interpreter_runs_the_executable_it_names_and_says_which()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), WindowsOnly);
        using var dir = new TempPaths();
        using var programs = new TestPrograms();
        var executable = programs.AddNested(@"node_modules\@acme\sdr\bin\acme-sdr.exe", "the binary");
        programs.AddNested(@"node_modules\@acme\sdr\package.json", "{\"name\":\"@acme/sdr\",\"bin\":{\"acme-sdr\":\"bin/acme-sdr.exe\"}}");
        programs.Add("acme-sdr.cmd", TestPrograms.NpmNativeShim(@"@acme\sdr", @"bin\acme-sdr.exe"));

        var (check, runner) = await CheckAsync(dir, "acme-sdr", programs.Root);

        var asked = Assert.Single(runner.Requested);
        Assert.Equal(executable, asked.Program);
        Assert.Equal(["--version"], asked.Arguments);
        Assert.Equal(CheckState.Ok, check.State);
        Assert.Contains($"'acme-sdr' resolves to {Path.Combine(programs.Root, "acme-sdr.cmd")}, npm's shim, which starts {executable}.", check.Fact, StringComparison.Ordinal);
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
        Assert.EndsWith($"{Instead} {WhoseSearchPath}", check.Fact, StringComparison.Ordinal);
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
        Assert.EndsWith($"{Instead} {WhoseSearchPath}", check.Fact, StringComparison.Ordinal);
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

    /// <summary>What the check says of the search path it read: the CLI process's own, which the runtime's may not be.</summary>
    private const string WhoseSearchPath = "It read the CLI's PATH; the runtime's PATH may differ, see the install page.";

    /// <summary>
    /// The check looks a bare name up on the PATH of the process that runs <c>jason status</c>, and a runtime that
    /// started at logon or from another shell may hold another one. So every answer that came of that lookup says whose
    /// PATH it was, found or not, answering or not.
    /// </summary>
    [Fact]
    public async Task A_name_looked_up_on_the_search_path_says_it_read_the_CLI_s_PATH_whatever_the_answer_was()
    {
        using var dir = new TempPaths();
        using var programs = new TestPrograms();
        using var empty = new TestPrograms();
        programs.AddProgram("acme-sdr");

        var (found, _) = await CheckAsync(dir, "acme-sdr", programs.Root);
        var (failed, _) = await CheckAsync(dir, "acme-sdr", programs.Root, answer: new ProgramResult(3, string.Empty, string.Empty, false));
        var (notStarted, _) = await CheckAsync(dir, "acme-sdr", programs.Root, answer: new ProgramResult(-1, string.Empty, "The program could not be started.", false, NotStarted: true));
        var (timedOut, _) = await CheckAsync(dir, "acme-sdr", programs.Root, answer: new ProgramResult(-1, string.Empty, string.Empty, true));
        var (missing, _) = await CheckAsync(dir, "acme-sdr-not-installed", empty.Root);

        Assert.Equal(CheckState.Ok, found.State);
        Assert.EndsWith(WhoseSearchPath, found.Fact, StringComparison.Ordinal);
        Assert.EndsWith(WhoseSearchPath, failed.Fact, StringComparison.Ordinal);
        Assert.EndsWith(WhoseSearchPath, notStarted.Fact, StringComparison.Ordinal);
        Assert.EndsWith(WhoseSearchPath, timedOut.Fact, StringComparison.Ordinal);
        Assert.Equal(CheckState.Absent, missing.State);
        Assert.EndsWith(WhoseSearchPath, missing.Fact, StringComparison.Ordinal);
    }

    /// <summary>
    /// A name the lookup refused still came of the PATH, so it says whose; a path names the directory it is looked up in
    /// and reads no PATH for the program, so it says nothing of one.
    /// </summary>
    [Fact]
    public async Task A_path_to_the_program_reads_no_PATH_and_says_nothing_of_one()
    {
        using var dir = new TempPaths();
        using var elsewhere = new TestPrograms();
        using var programs = new TestPrograms();
        var program = elsewhere.AddProgram("acme-sdr");

        var (found, _) = await CheckAsync(dir, program, programs.Root);
        var (missing, _) = await CheckAsync(dir, Path.Combine(programs.Root, "acme-sdr"), programs.Root);

        Assert.Equal(CheckState.Ok, found.State);
        Assert.DoesNotContain("PATH", found.Fact, StringComparison.Ordinal);
        Assert.Equal(CheckState.Absent, missing.State);
        Assert.DoesNotContain("PATH", missing.Fact, StringComparison.Ordinal);
    }

    /// <summary>The shim's interpreter is taken from the search path even when the shim itself was named by path.</summary>
    [Fact]
    public async Task A_path_to_npm_shim_says_whose_PATH_its_interpreter_was_found_on()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), WindowsOnly);
        using var dir = new TempPaths();
        using var npm = new TestPrograms();
        using var nodeDirectory = new TestPrograms();
        InstallAcmeCli(npm);
        nodeDirectory.Add("node.exe");

        var (check, _) = await CheckAsync(dir, Path.Combine(npm.Root, "acme-sdr"), nodeDirectory.Root);

        Assert.Equal(CheckState.Ok, check.State);
        Assert.EndsWith(WhoseSearchPath, check.Fact, StringComparison.Ordinal);
    }

    /// <summary>
    /// A shim given by path takes its interpreter from the search path only when none stands beside it, so only then is a
    /// PATH read: with no interpreter anywhere the lookup still went through it before it refused.
    /// </summary>
    [Fact]
    public async Task A_path_to_npm_shim_with_no_interpreter_anywhere_says_it_read_the_PATH_before_refusing()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), WindowsOnly);
        using var dir = new TempPaths();
        using var npm = new TestPrograms();
        using var empty = new TestPrograms();
        InstallAcmeCli(npm);

        var (check, runner) = await CheckAsync(dir, Path.Combine(npm.Root, "acme-sdr"), empty.Root);

        Assert.Empty(runner.Requested);
        Assert.Equal(CheckState.Absent, check.State);
        Assert.Contains("neither beside it nor on the search path", check.Fact, StringComparison.Ordinal);
        Assert.EndsWith(WhoseSearchPath, check.Fact, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_path_to_npm_shim_with_its_interpreter_beside_it_reads_no_PATH_and_says_nothing_of_one()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), WindowsOnly);
        using var dir = new TempPaths();
        using var npm = new TestPrograms();
        using var nodeDirectory = new TestPrograms();
        InstallAcmeCli(npm);
        var node = npm.Add("node.exe");
        nodeDirectory.Add("node.exe");

        var (check, runner) = await CheckAsync(dir, Path.Combine(npm.Root, "acme-sdr"), nodeDirectory.Root);

        Assert.Equal(node, Assert.Single(runner.Requested).Program);
        Assert.Equal(CheckState.Ok, check.State);
        Assert.DoesNotContain("PATH", check.Fact, StringComparison.Ordinal);
    }

    /// <summary>A name is looked up on the PATH however it ends, so a script named by its extension and a shim by its bare name say whose.</summary>
    [Fact]
    public async Task A_name_that_resolves_to_a_script_or_to_npm_shim_says_whose_PATH_it_read()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), WindowsOnly);
        using var dir = new TempPaths();
        using var scripts = new TestPrograms();
        using var npm = new TestPrograms();
        scripts.Add("acme-sdr.cmd");
        npm.Add("node.exe");
        InstallAcmeCli(npm);

        var (script, _) = await CheckAsync(dir, "acme-sdr.cmd", scripts.Root);
        var (shim, _) = await CheckAsync(dir, "acme-sdr", npm.Root);

        Assert.Contains("a script a shell would have to interpret", script.Fact, StringComparison.Ordinal);
        Assert.EndsWith(WhoseSearchPath, script.Fact, StringComparison.Ordinal);
        Assert.Equal(CheckState.Ok, shim.State);
        Assert.EndsWith(WhoseSearchPath, shim.Fact, StringComparison.Ordinal);
    }

    /// <summary>
    /// A path to a file the lookup refuses without reading a PATH, a script a shell would run and a .cmd that is not
    /// npm's shim, names no PATH: the refusal comes of the directory alone.
    /// </summary>
    [Theory]
    [InlineData("acme-sdr.bat", "@ECHO off")]
    [InlineData("acme-sdr.cmd", "@ECHO off")]
    public async Task A_path_to_a_file_refused_without_reading_a_PATH_says_nothing_of_one(string file, string content)
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), WindowsOnly);
        using var dir = new TempPaths();
        using var programs = new TestPrograms();
        programs.Add(file, content + Environment.NewLine + "cmd /c whatever %*" + Environment.NewLine);

        var (check, runner) = await CheckAsync(dir, Path.Combine(programs.Root, "acme-sdr"), programs.Root);

        Assert.Empty(runner.Requested);
        Assert.Equal(CheckState.Absent, check.State);
        Assert.Contains("was not run", check.Fact, StringComparison.Ordinal);
        Assert.DoesNotContain("PATH", check.Fact, StringComparison.Ordinal);
    }

    /// <summary>Where the environment cannot run programs nothing was looked up, so no PATH is named.</summary>
    [Fact]
    public async Task Where_programs_cannot_be_run_no_PATH_is_named()
    {
        using var dir = new TempPaths();
        var output = new StringWriter();
        StatusCommandTests.Running(dir);
        var machine = StatusCommandTests.Machine(dir, output);

        await CliApp.RunAsync(["status", "--provider-cli", "acme-sdr"], machine with { Programs = null }, Ct);

        var check = Assert.Single(StatusCommandTests.Read(output).Checks, check => check.Name == "provider_cli");
        Assert.Equal(CheckState.Unknown, check.State);
        Assert.Contains("This environment cannot run programs", check.Fact, StringComparison.Ordinal);
        Assert.DoesNotContain("PATH", check.Fact, StringComparison.Ordinal);
    }

    /// <summary>The person's own line: the table the human view prints carries the same words as the body's fact.</summary>
    [Fact]
    public async Task The_human_line_says_whose_PATH_was_read()
    {
        using var dir = new TempPaths();
        using var programs = new TestPrograms();
        programs.AddProgram("acme-sdr");
        var output = new StringWriter();
        StatusCommandTests.Running(dir);
        var machine = StatusCommandTests.Machine(dir, output, new FakeProgramRunner(_ => new ProgramResult(0, "acme-sdr 1.2.3", string.Empty, false)));

        await CliApp.RunAsync(["status", "--provider-cli", "acme-sdr", "--human"], machine with { SearchPath = string.Join(Path.PathSeparator, programs.Root, machine.SearchPath!) }, Ct);

        var line = Assert.Single(output.ToString().Split('\n'), row => row.StartsWith("provider_cli", StringComparison.Ordinal));
        Assert.Contains(WhoseSearchPath, line, StringComparison.Ordinal);
    }

    /// <summary>With no program named nothing was looked up, so there is no PATH to name.</summary>
    [Fact]
    public async Task With_no_provider_named_no_PATH_is_named()
    {
        using var dir = new TempPaths();
        var output = new StringWriter();
        StatusCommandTests.Running(dir);

        await CliApp.RunAsync(["status"], StatusCommandTests.Machine(dir, output, new FakeProgramRunner()), Ct);

        var provider = Assert.Single(StatusCommandTests.Read(output).Checks, check => check.Name == "provider_cli");
        Assert.DoesNotContain("PATH", provider.Fact, StringComparison.Ordinal);
    }

    /// <summary>The verb's own help says it too, since it is where an agent reads what the check means.</summary>
    [Fact]
    public async Task Help_says_whose_PATH_the_provider_check_reads()
    {
        using var dir = new TempPaths();
        var output = new StringWriter();

        await CliApp.RunAsync(["status", "--help"], new CliEnvironment(output, new StringWriter(), dir.Paths), Ct);

        var text = string.Join(' ', output.ToString().Split((char[])[' ', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries));
        Assert.Contains("the CLI's PATH; the runtime's PATH may differ, see the install page", text, StringComparison.Ordinal);
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
