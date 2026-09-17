using System.ComponentModel;
using System.Diagnostics;
using Jason.Contracts.Plugins;
using Jason.Runtime.Plugins.Manifest;
using Jason.Runtime.Plugins.Registry;

namespace Jason.Runtime.Tests.Plugins;

/// <summary>
/// What the machine answers for a program a manifest declares. Everything here is a problem of the environment,
/// never of the package: a missing vendor CLI holds its own plugin back and leaves every other plugin alone.
/// </summary>
public class ExecutableResolverTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly IReadOnlyDictionary<string, string> NoEnvironment = new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>
    /// The environment a version check is handed when the program actually has to start. A program that is
    /// itself a .NET application cannot find its own host without the machine's .NET root, and only the base
    /// environment carries it: on a machine whose runtime lives outside the platform's default location — a
    /// macOS build agent, say — an empty environment stops the program before it can answer. This is also the
    /// environment a real vendor CLI is started with, so what these tests prove is what a reload will do.
    /// </summary>
    private static IReadOnlyDictionary<string, string> HostEnvironment =>
        BaseEnvironment.Build(PluginLoader.CurrentEnvironment(), []);

    [Fact]
    public void A_declared_program_resolves_to_the_file_the_search_path_holds()
    {
        var resolver = new ExecutableResolver(new TestSearchPath { Path = TestPlugins.FakeCliDirectory });

        var resolved = resolver.Resolve(new ExecutableRequest(FakeProviderCli.ExecutableName, null, null), 0);

        Assert.Null(resolved.Problem);
        Assert.Equal(FakeProviderCli.ExecutablePath, resolved.Path);

        // Presence is all a resolution answers: running the program is a separate decision the user makes.
        Assert.Null(resolved.Version);
    }

    [Fact]
    public void A_program_nobody_installed_is_a_problem_of_the_machine()
    {
        using var programs = new TestPrograms();
        var resolver = new ExecutableResolver(new TestSearchPath { Path = programs.Root });

        var resolved = resolver.Resolve(new ExecutableRequest("not-installed-anywhere", null, null), 2);

        Assert.Null(resolved.Path);
        Assert.Equal(ProblemCodes.ExecutableMissing, resolved.Problem!.Code);
        Assert.Equal("capabilities.exec.executables[2].name", resolved.Problem.Path);
        Assert.True(resolved.Problem.Environmental);
    }

    [Fact]
    public void An_empty_search_path_holds_nothing()
    {
        var resolver = new ExecutableResolver(new TestSearchPath { Path = null });

        Assert.Equal(ProblemCodes.ExecutableMissing, resolver.Resolve(new ExecutableRequest("provider-cli", null, null), 0).Problem!.Code);
    }

    [Fact]
    public void The_search_path_is_walked_in_order()
    {
        using var first = new TestPrograms();
        using var second = new TestPrograms();
        var wanted = first.AddProgram("provider-cli");
        second.AddProgram("provider-cli");
        var resolver = new ExecutableResolver(new TestSearchPath { Path = first.Root + Path.PathSeparator + second.Root });

        Assert.Equal(wanted, resolver.Resolve(new ExecutableRequest("provider-cli", null, null), 0).Path);
    }

    [Fact]
    public void A_file_that_only_a_shell_could_start_is_refused()
    {
        using var programs = new TestPrograms();
        programs.Add("tool.cmd");
        programs.Add("tool.bat");
        var resolver = new ExecutableResolver(new TestSearchPath { Path = programs.Root });

        var resolved = resolver.Resolve(new ExecutableRequest("tool", null, null), 0);

        Assert.Null(resolved.Path);

        // Starting a .cmd means cmd.exe interprets the arguments — the very shell the invariant excludes. On
        // every other system the name simply does not exist.
        Assert.Equal(
            OperatingSystem.IsWindows() ? ProblemCodes.ExecutableNotRunnable : ProblemCodes.ExecutableMissing,
            resolved.Problem!.Code);
    }

    [Fact]
    public void A_file_nobody_may_execute_is_not_a_program()
    {
        using var programs = new TestPrograms();
        programs.Add("tool" + (OperatingSystem.IsWindows() ? ".txt" : string.Empty));
        var resolver = new ExecutableResolver(new TestSearchPath { Path = programs.Root });

        Assert.Equal(ProblemCodes.ExecutableMissing, resolver.Resolve(new ExecutableRequest("tool", null, null), 0).Problem!.Code);
    }

    [Fact]
    public async Task A_version_command_reports_the_version_the_program_prints()
    {
        var resolver = new ExecutableResolver(new TestSearchPath { Path = TestPlugins.FakeCliDirectory });
        var request = new ExecutableRequest(FakeProviderCli.ExecutableName, null, ["--version"]);

        var result = await resolver.CheckVersionAsync(resolver.Resolve(request, 0), request, 0, HostEnvironment, TimeSpan.FromSeconds(30), Ct);

        Assert.Null(result.Problem);
        Assert.Equal("1.2.3", result.Version);
    }

    [Fact]
    public async Task A_program_older_than_the_manifest_asks_for_is_incompatible()
    {
        var resolver = new ExecutableResolver(new TestSearchPath { Path = TestPlugins.FakeCliDirectory });
        var request = new ExecutableRequest(FakeProviderCli.ExecutableName, "2.0.0", ["--version"]);

        var result = await resolver.CheckVersionAsync(resolver.Resolve(request, 1), request, 1, HostEnvironment, TimeSpan.FromSeconds(30), Ct);

        Assert.Equal(ProblemCodes.ExecutableIncompatible, result.Problem!.Code);
        Assert.Equal("capabilities.exec.executables[1].name", result.Problem.Path);
        Assert.Equal("1.2.3", result.Version);
        Assert.Equal("2.0.0", result.MinVersion);
    }

    [Fact]
    public async Task A_program_new_enough_passes()
    {
        var resolver = new ExecutableResolver(new TestSearchPath { Path = TestPlugins.FakeCliDirectory });
        var request = new ExecutableRequest(FakeProviderCli.ExecutableName, "1.0.0", ["--version"]);

        var result = await resolver.CheckVersionAsync(resolver.Resolve(request, 0), request, 0, HostEnvironment, TimeSpan.FromSeconds(30), Ct);

        Assert.Null(result.Problem);
        Assert.Equal("1.2.3", result.Version);
    }

    [Fact]
    public async Task A_program_that_answers_with_no_version_fails_the_check()
    {
        using var programs = new TestPrograms();
        programs.AddProgram("tool");
        var resolver = new ExecutableResolver(new TestSearchPath { Path = programs.Root });
        var request = new ExecutableRequest("tool", null, ["--version"]);

        var result = await resolver.CheckVersionAsync(resolver.Resolve(request, 0), request, 0, NoEnvironment, TimeSpan.FromSeconds(30), Ct);

        // The file is not a program at all; whether starting it fails or it answers with nothing, the check did
        // not produce a version and the plugin is held back rather than the package blamed.
        Assert.Equal(ProblemCodes.ExecutableVersionCheckFailed, result.Problem!.Code);
        Assert.True(result.Problem.Environmental);
    }

    [Fact]
    public async Task A_program_that_fails_its_version_command_says_what_it_printed()
    {
        var resolver = new ExecutableResolver(new TestSearchPath { Path = TestPlugins.FakeCliDirectory });
        var request = new ExecutableRequest(FakeProviderCli.ExecutableName, null, ["exit", "7"]);

        var result = await resolver.CheckVersionAsync(resolver.Resolve(request, 0), request, 0, HostEnvironment, TimeSpan.FromSeconds(30), Ct);

        // The exit code alone explains nothing; what the program said on its way out is the part a person can act on.
        Assert.Equal(ProblemCodes.ExecutableVersionCheckFailed, result.Problem!.Code);
        Assert.Contains("exit code 7", result.Problem.Message, StringComparison.Ordinal);
        Assert.Contains("exiting", result.Problem.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// The tail of a failing program's output travels into a problem a person reads in <c>plugin list</c>, and a
    /// version check runs with the plugin's granted variables in its environment — so a program that echoes one
    /// back would otherwise leave it in a listing. The redaction exists for that; nothing exercised it.
    /// </summary>
    [Fact]
    public async Task What_a_failing_program_printed_is_redacted_before_it_becomes_a_problem()
    {
        const string granted = "a-granted-credential-value";
        var resolver = new ExecutableResolver(new TestSearchPath { Path = TestPlugins.FakeCliDirectory });
        var request = new ExecutableRequest(FakeProviderCli.ExecutableName, null, ["stderr-exit", "9", granted]);

        var result = await resolver.CheckVersionAsync(
            resolver.Resolve(request, 0),
            request,
            0,
            HostEnvironment,
            TimeSpan.FromSeconds(30),
            Ct,
            new Redactor([granted]));

        Assert.Equal(ProblemCodes.ExecutableVersionCheckFailed, result.Problem!.Code);
        Assert.Contains("exit code 9", result.Problem.Message, StringComparison.Ordinal);
        Assert.Contains(Redactor.Mask, result.Problem.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(granted, result.Problem.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_check_that_runs_out_of_time_answers_rather_than_throws()
    {
        var resolver = new ExecutableResolver(new TestSearchPath { Path = TestPlugins.FakeCliDirectory });
        var request = new ExecutableRequest(FakeProviderCli.ExecutableName, null, ["--version"]);

        var result = await resolver.CheckVersionAsync(resolver.Resolve(request, 0), request, 0, HostEnvironment, TimeSpan.FromMilliseconds(1), Ct);

        // A fast machine may still finish inside a millisecond; what matters is that a reload is never held up
        // by a program that does not answer.
        Assert.True(result.Problem is null || result.Problem.Code == ProblemCodes.ExecutableVersionCheckFailed);
    }

    [Fact]
    public async Task A_program_that_was_never_found_is_never_started()
    {
        using var programs = new TestPrograms();
        var resolver = new ExecutableResolver(new TestSearchPath { Path = programs.Root });
        var request = new ExecutableRequest("not-installed-anywhere", null, ["--version"]);
        var missing = resolver.Resolve(request, 0);

        var result = await resolver.CheckVersionAsync(missing, request, 0, NoEnvironment, TimeSpan.FromSeconds(30), Ct);

        Assert.Equal(ProblemCodes.ExecutableMissing, result.Problem!.Code);
        Assert.Null(result.Version);
    }

    [Fact]
    public async Task The_resultprogram_sees_only_the_environment_it_was_handed()
    {
        var record = Path.Combine(Path.GetTempPath(), "jason-tests", Guid.NewGuid().ToString("N") + ".txt");
        Directory.CreateDirectory(Path.GetDirectoryName(record)!);
        var resolver = new ExecutableResolver(new TestSearchPath { Path = TestPlugins.FakeCliDirectory });
        var request = new ExecutableRequest(FakeProviderCli.ExecutableName, null, ["-v"]);
        var environment = BaseEnvironment.Build(PluginLoader.CurrentEnvironment(), []);
        environment["FAKE_CLI_RECORD_HANDED"] = record;

        var result = await resolver.CheckVersionAsync(resolver.Resolve(request, 0), request, 0, environment, TimeSpan.FromSeconds(30), Ct);

        Assert.Equal("1.2.3", result.Version);
        Assert.Equal("argv=-v", (await File.ReadAllLinesAsync(record, Ct))[0].Split(' ')[0]);
        File.Delete(record);
    }

    // ---------------------------------------------------------------------------------------------------------
    // npm's cmd shim, and nothing else. Every Node-based vendor CLI installed with npm resolves only to a .cmd,
    // which is refused above because starting one means cmd.exe parses the arguments. The way out is not to
    // escape them but to start what the shim would have started — so the resolver reads npm's own template,
    // holds it to the package's own `bin` entry, and hands node the entry script through the argument array.
    // One shim is accepted here; every other case in the corpus is refused with the rule it broke named in the
    // message, because a refusal an operator cannot act on is not much better than a crash.
    // ---------------------------------------------------------------------------------------------------------

    private const string WindowsOnly = "npm writes a .cmd shim only on Windows, and only there is one ever read.";

    [Fact]
    public void A_shim_is_read_again_rather_than_remembered_from_the_last_time()
    {
        // A file that decides which program runs must not be trusted from the last reload: a package updated,
        // replaced or tampered with between two reloads has to be met as it is now. Nothing here caches, and
        // this is what says so — the same resolver, asked twice, answers the file in front of it both times.
        Assert.SkipUnless(OperatingSystem.IsWindows(), WindowsOnly);
        using var programs = new TestPrograms();
        programs.AddInterpreter("node.exe");
        InstallVendorCli(programs);

        var first = Resolve(programs, "vendor");
        Assert.Null(first.Problem);

        // The same shim, rewritten to something npm did not write.
        programs.Add("vendor.cmd", "@ECHO off" + Environment.NewLine + "cmd /c whatever %*" + Environment.NewLine);
        var second = Resolve(programs, "vendor");

        Assert.Equal(ProblemCodes.ExecutableNotRunnable, second.Problem!.Code);
        Assert.Null(second.Path);
    }

    private static ResolvedExecutable Resolve(TestPrograms programs, string name) =>
        new ExecutableResolver(new TestSearchPath { Path = programs.Root }).Resolve(new ExecutableRequest(name, null, null), 0);

    /// <summary>A package tree npm would have written: the entry script, the manifest that declares it, the shim.</summary>
    private static void InstallVendorCli(TestPrograms programs, string package = "vendor-cli", string entry = @"dist\index.js", string? declared = null)
    {
        programs.AddNested(Path.Combine("node_modules", package, entry), "// the entry point");
        programs.AddNested(
            Path.Combine("node_modules", package, "package.json"),
            "{\"name\":\"" + package + "\",\"bin\":{\"vendor\":\"" + (declared ?? entry).Replace('\\', '/') + "\"}}");
        programs.Add("vendor.cmd", TestPrograms.NpmShim(package, entry));
    }

    /// <summary>
    /// A directory junction, made the only way Windows offers one without a privilege this account may not
    /// hold: a symbolic link needs <c>SeCreateSymbolicLink</c>, and what the test is about is the reparse point
    /// rather than which kind of one it is.
    /// </summary>
    private static bool TryJunction(string link, string target)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo("cmd.exe")
            {
                ArgumentList = { "/c", "mklink", "/J", link, target },
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            });

            process!.WaitForExit();
            return process.ExitCode == 0 && Directory.Exists(link);
        }
        catch (Exception exception) when (exception is Win32Exception or IOException or InvalidOperationException)
        {
            return false;
        }
    }

    [Fact]
    public void An_npm_shim_resolves_to_the_interpreter_and_the_entry_script_it_names()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), WindowsOnly);
        using var programs = new TestPrograms();
        var node = programs.AddInterpreter("node.exe");
        InstallVendorCli(programs);

        var resolved = Resolve(programs, "vendor");

        Assert.Null(resolved.Problem);

        // What is started is node, and what it is handed is the entry script — both recorded, neither guessed,
        // and both going through the argument array so no shell ever sees them.
        Assert.Equal(node, resolved.Path);
        Assert.Equal([Path.Combine(programs.Root, "node_modules", "vendor-cli", "dist", "index.js")], resolved.Launch);
    }

    [Theory]
    [InlineData("a file npm did not write at all", "@ECHO off\r\ncmd /c whatever %*\r\n")]
    [InlineData(
        "a template older than the one cmd-shim writes today",
        "@IF EXIST \"%~dp0\\node.exe\" (\r\n  \"%~dp0\\node.exe\"  \"%~dp0\\node_modules\\vendor-cli\\dist\\index.js\" %*\r\n)\r\n")]
    [InlineData(
        "a last line that starts something else entirely",
        "@ECHO off\r\nGOTO start\r\n:find_dp0\r\nSET dp0=%~dp0\r\nEXIT /b\r\n:start\r\nSETLOCAL\r\nCALL :find_dp0\r\n\r\n"
        + "IF EXIST \"%dp0%\\node.exe\" (\r\n  SET \"_prog=%dp0%\\node.exe\"\r\n) ELSE (\r\n  SET \"_prog=node\"\r\n)\r\n\r\n"
        + "endLocal & \"%dp0%\\node_modules\\vendor-cli\\dist\\index.js\" %*\r\n")]
    public void A_shim_that_is_not_npms_is_refused_the_way_it_always_was(string why, string content)
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), WindowsOnly);
        using var programs = new TestPrograms();
        programs.AddInterpreter("node.exe");
        programs.AddNested(@"node_modules\vendor-cli\dist\index.js", "// the entry point");
        programs.AddNested(@"node_modules\vendor-cli\package.json", """{"name":"vendor-cli","bin":{"vendor":"dist/index.js"}}""");
        programs.Add("vendor.cmd", content);

        var resolved = Resolve(programs, "vendor");

        Assert.NotNull(resolved.Problem);
        Assert.Null(resolved.Path);
        Assert.Empty(resolved.Launch);
        Assert.Equal(ProblemCodes.ExecutableNotRunnable, resolved.Problem.Code);
        Assert.True(resolved.Problem.Environmental, why);
        Assert.Contains("is not the shim npm writes", resolved.Problem.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void An_entry_script_outside_the_package_is_refused()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), WindowsOnly);
        using var programs = new TestPrograms();
        programs.AddInterpreter("node.exe");
        programs.Add("evil.js", "// anything at all, and nothing the package declared");

        // The manifest declares exactly what the shim names, so what refuses this is containment and nothing else.
        programs.AddNested(@"node_modules\vendor-cli\package.json", """{"name":"vendor-cli","bin":{"vendor":"../../evil.js"}}""");
        programs.Add("vendor.cmd", TestPrograms.NpmShim("vendor-cli", @"..\..\evil.js"));

        var resolved = Resolve(programs, "vendor");

        Assert.Null(resolved.Path);
        Assert.Equal(ProblemCodes.ExecutableNotRunnable, resolved.Problem!.Code);
        Assert.Contains("outside the package", resolved.Problem.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_shim_naming_an_entry_the_package_does_not_declare_is_refused()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), WindowsOnly);
        using var programs = new TestPrograms();
        programs.AddInterpreter("node.exe");
        InstallVendorCli(programs, declared: @"dist\other.js");

        var resolved = Resolve(programs, "vendor");

        Assert.Null(resolved.Path);
        Assert.Equal(ProblemCodes.ExecutableNotRunnable, resolved.Problem!.Code);
        Assert.Contains("its package does not declare", resolved.Problem.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_shim_for_a_scoped_package_is_refused_and_says_so()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), WindowsOnly);
        using var programs = new TestPrograms();
        programs.AddInterpreter("node.exe");
        programs.AddNested(@"node_modules\@vendor\cli\dist\index.js", "// the entry point");
        programs.AddNested(@"node_modules\@vendor\cli\package.json", """{"name":"@vendor/cli","bin":{"vendor":"dist/index.js"}}""");
        programs.Add("vendor.cmd", TestPrograms.NpmShim(@"@vendor\cli", @"dist\index.js"));

        var resolved = Resolve(programs, "vendor");

        // A scoped package is two directories under node_modules, and the reader knows one. Saying so is the
        // difference between an author reading why and an author guessing.
        Assert.Null(resolved.Path);
        Assert.Equal(ProblemCodes.ExecutableNotRunnable, resolved.Problem!.Code);
        Assert.Contains("scoped package", resolved.Problem.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// No path is ever built out of a segment that has not been read as a package name first. <c>..</c> climbs
    /// out of <c>node_modules</c> and a drive letter leaves the tree altogether, both while still reading as a
    /// single segment; containment refuses each of them on this machine today, but it refuses them by where
    /// they happen to land, and the rule this pins is that they never get that far.
    /// </summary>
    [Theory]
    [InlineData("..")]
    [InlineData("C:")]
    public void A_shim_whose_package_is_not_a_package_name_is_refused(string package)
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), WindowsOnly);
        using var programs = new TestPrograms();
        programs.AddInterpreter("node.exe");
        programs.Add("evil.js", "// outside node_modules altogether");
        programs.Add("package.json", """{"name":"vendor","bin":{"vendor":"evil.js"}}""");
        Directory.CreateDirectory(Path.Combine(programs.Root, "node_modules"));
        programs.Add("vendor.cmd", TestPrograms.NpmShim(package, "evil.js"));

        var resolved = Resolve(programs, "vendor");

        Assert.Null(resolved.Path);
        Assert.Equal(ProblemCodes.ExecutableNotRunnable, resolved.Problem!.Code);
        Assert.Contains("not one npm could have written", resolved.Problem.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_shim_with_no_interpreter_to_run_it_is_refused()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), WindowsOnly);
        using var programs = new TestPrograms();
        InstallVendorCli(programs);

        var resolved = Resolve(programs, "vendor");

        Assert.Null(resolved.Path);
        Assert.Equal(ProblemCodes.ExecutableNotRunnable, resolved.Problem!.Code);
        Assert.Contains("node.exe", resolved.Problem.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// A junction at <c>node_modules\&lt;package&gt;</c> leaves the entry lexically inside the package while the
    /// file lives anywhere on the disk, so containment that compares strings alone would wave it through. The
    /// file the shim names is the file that runs; where it really is has to be what is checked.
    /// </summary>
    [Fact]
    public void A_package_directory_that_is_a_junction_out_of_the_tree_is_refused()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), WindowsOnly);
        using var programs = new TestPrograms();
        using var elsewhere = new TestPrograms();
        programs.AddInterpreter("node.exe");
        elsewhere.AddNested(@"dist\index.js", "// the entry point, somewhere else entirely");
        elsewhere.AddNested("package.json", """{"name":"vendor-cli","bin":{"vendor":"dist/index.js"}}""");
        Directory.CreateDirectory(Path.Combine(programs.Root, "node_modules"));
        Assert.SkipUnless(
            TryJunction(Path.Combine(programs.Root, "node_modules", "vendor-cli"), elsewhere.Root),
            "this machine would not create a directory junction.");
        programs.Add("vendor.cmd", TestPrograms.NpmShim("vendor-cli", @"dist\index.js"));

        var resolved = Resolve(programs, "vendor");

        Assert.Null(resolved.Path);
        Assert.Equal(ProblemCodes.ExecutableNotRunnable, resolved.Problem!.Code);
        Assert.Contains("outside the package", resolved.Problem.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// And the level above: <c>node_modules</c> itself a junction. The package name is a name npm could have
    /// written and every segment under it is too, so nothing about the shim's text says anything is wrong —
    /// what says so is that the directory the name is resolved through leads out of the tree the shim sits in.
    /// This is checked by where the package really is rather than by how its name reads, which is a different
    /// expression from the per-segment one and a different escape.
    /// </summary>
    [Fact]
    public void A_node_modules_that_is_a_junction_out_of_the_tree_is_refused()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), WindowsOnly);
        using var programs = new TestPrograms();
        using var elsewhere = new TestPrograms();
        programs.AddInterpreter("node.exe");
        elsewhere.AddNested(@"vendor-cli\dist\index.js", "// the entry point, in somebody else's tree");
        elsewhere.AddNested(@"vendor-cli\package.json", """{"name":"vendor-cli","bin":{"vendor":"dist/index.js"}}""");
        Assert.SkipUnless(
            TryJunction(Path.Combine(programs.Root, "node_modules"), elsewhere.Root),
            "this machine would not create a directory junction.");
        programs.Add("vendor.cmd", TestPrograms.NpmShim("vendor-cli", @"dist\index.js"));

        var resolved = Resolve(programs, "vendor");

        Assert.Null(resolved.Path);
        Assert.Equal(ProblemCodes.ExecutableNotRunnable, resolved.Problem!.Code);
        Assert.Contains("outside the package", resolved.Problem.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// The same again one level down. Resolving only the package directory would leave a junction at any
    /// directory <em>inside</em> the package unnoticed, and the entry would still be lexically where the shim
    /// said while the file that actually runs sat somewhere else entirely.
    /// </summary>
    [Fact]
    public void A_directory_inside_the_package_that_is_a_junction_out_of_the_tree_is_refused()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), WindowsOnly);
        using var programs = new TestPrograms();
        using var elsewhere = new TestPrograms();
        programs.AddInterpreter("node.exe");
        elsewhere.Add("index.js", "// the entry point, somewhere else entirely");
        programs.AddNested(@"node_modules\vendor-cli\package.json", """{"name":"vendor-cli","bin":{"vendor":"dist/index.js"}}""");
        Assert.SkipUnless(
            TryJunction(Path.Combine(programs.Root, "node_modules", "vendor-cli", "dist"), elsewhere.Root),
            "this machine would not create a directory junction.");
        programs.Add("vendor.cmd", TestPrograms.NpmShim("vendor-cli", @"dist\index.js"));

        var resolved = Resolve(programs, "vendor");

        Assert.Null(resolved.Path);
        Assert.Equal(ProblemCodes.ExecutableNotRunnable, resolved.Problem!.Code);
        Assert.Contains("outside the package", resolved.Problem.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_shim_never_wins_over_a_real_program_later_on_the_search_path()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), WindowsOnly);
        using var first = new TestPrograms();
        using var second = new TestPrograms();
        first.AddInterpreter("node.exe");
        InstallVendorCli(first);
        var exe = second.AddProgram("vendor");
        var resolver = new ExecutableResolver(new TestSearchPath { Path = first.Root + Path.PathSeparator + second.Root });

        var resolved = resolver.Resolve(new ExecutableRequest("vendor", null, null), 0);

        // The shim is the last resort, not the first hit: a perfectly good one in an earlier directory must
        // lose to an .exe in a later one, or installing npm's shim would silently change which program runs.
        Assert.Null(resolved.Problem);
        Assert.Equal(exe, resolved.Path);
        Assert.Empty(resolved.Launch);
    }

    [Fact]
    public async Task A_shim_is_version_checked_through_the_pair_that_will_actually_run()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), WindowsOnly);
        using var programs = new TestPrograms();
        programs.AddInterpreter("node.exe");
        InstallVendorCli(programs);
        var entry = Path.Combine(programs.Root, "node_modules", "vendor-cli", "dist", "index.js");
        var record = Path.Combine(programs.Root, "handed.txt");
        var environment = BaseEnvironment.Build(PluginLoader.CurrentEnvironment(), []);
        environment["FAKE_CLI_RECORD_HANDED"] = record;
        var resolver = new ExecutableResolver(new TestSearchPath { Path = programs.Root });
        var request = new ExecutableRequest("vendor", null, ["--version"]);

        var result = await resolver.CheckVersionAsync(resolver.Resolve(request, 0), request, 0, environment, TimeSpan.FromSeconds(30), Ct);

        Assert.Null(result.Problem);
        Assert.Equal("1.2.3", result.Version);

        // What was checked is what will be started: the interpreter, handed the entry script ahead of the
        // version command, working where the package lives rather than where the interpreter lives.
        var handed = (await File.ReadAllLinesAsync(record, Ct))[0];
        Assert.Contains("argv=" + entry + " --version", handed, StringComparison.Ordinal);
        Assert.Contains("cwd=" + Path.GetDirectoryName(entry), handed, StringComparison.Ordinal);
    }
}
