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
    [Critical(Family.B1, "no shell")]
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

        // The file is not a program at all; the check did not produce a version and the plugin is held back rather
        // than the package blamed.
        Assert.Equal(ProblemCodes.ExecutableVersionCheckFailed, result.Problem!.Code);
        Assert.True(result.Problem.Environmental);

        // On Windows the start itself is refused: the loader takes the file for no valid image. That refusal is the
        // whole of what a person is shown, an error code and no box on the desktop, because the door starts every
        // child with the loader's error boxes off (ChildProcessTests holds the mode in force inside the start).
        if (OperatingSystem.IsWindows())
        {
            Assert.Contains("could not be started", result.Problem.Message, StringComparison.Ordinal);
        }
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
            "{\"name\":\"" + package.Replace('\\', '/') + "\",\"bin\":{\"vendor\":\"" + (declared ?? entry).Replace('\\', '/') + "\"}}");
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

    /// <summary>
    /// A scoped package is two directories under <c>node_modules</c>, the scope and the name, and its shim is read the
    /// way an unscoped one is, through the same lookup every caller asks.
    /// </summary>
    [Fact]
    public void A_shim_for_a_scoped_package_resolves_to_the_interpreter_and_the_entry_script_it_names()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), WindowsOnly);
        using var programs = new TestPrograms();
        var node = programs.AddInterpreter("node.exe");
        InstallVendorCli(programs, package: @"@vendor\cli");

        var resolved = Resolve(programs, "vendor");

        Assert.Null(resolved.Problem);
        Assert.Equal(node, resolved.Path);
        Assert.Equal([Path.Combine(programs.Root, "node_modules", "@vendor", "cli", "dist", "index.js")], resolved.Launch);
    }

    /// <summary>
    /// A bare <c>bin</c> string names one command, and npm names it after the package without its scope: the package
    /// <c>@vendor/vendor</c> answers for <c>vendor</c>, and only a package whose own name is the scope and the directory
    /// the shim names does.
    /// </summary>
    [Theory]
    [InlineData("@vendor/vendor", true)]
    [InlineData("@other/vendor", false)]
    [InlineData("vendor", false)]
    public void A_scoped_package_whose_bin_is_a_bare_string_answers_for_its_name_without_the_scope(string packageName, bool read)
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), WindowsOnly);
        using var programs = new TestPrograms();
        programs.AddInterpreter("node.exe");
        programs.AddNested(@"node_modules\@vendor\vendor\cli.js", "// the entry point");
        programs.AddNested(@"node_modules\@vendor\vendor\package.json", "{\"name\":\"" + packageName + "\",\"bin\":\"cli.js\"}");
        programs.Add("vendor.cmd", TestPrograms.NpmShim(@"@vendor\vendor", "cli.js"));

        var resolved = Resolve(programs, "vendor");

        Assert.Equal(read, resolved.Problem is null);
        if (!read)
        {
            Assert.Contains("its package does not declare", resolved.Problem!.Message, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// Each of a scoped install's two directories reads as a name before any path is built from it: the scope as
    /// <c>@</c> and a package name, the package as a package name. <c>..</c> climbs, a drive letter leaves the tree, a
    /// bare <c>@</c> is no scope, a scope inside a scope is a directory npm never writes, and a scope with nothing
    /// under it names no package; each is refused with its rule, never by where it would have landed.
    /// </summary>
    [Theory]
    [InlineData(@"@..\cli", "names a scope whose name is not one npm could have written")]
    [InlineData(@"@C:\cli", "names a scope whose name is not one npm could have written")]
    [InlineData(@"@\cli", "names a scope whose name is not one npm could have written")]
    [InlineData(@"@vendor\..", "names a package whose name is not one npm could have written")]
    [InlineData(@"@vendor\C:", "names a package whose name is not one npm could have written")]
    [InlineData(@"@vendor\@other", "names a package whose name is not one npm could have written")]
    [InlineData("@vendor", "names a scope and no package under it")]
    public void A_shim_whose_scope_or_scoped_package_is_not_a_name_is_refused(string package, string rule)
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), WindowsOnly);
        using var programs = new TestPrograms();
        programs.AddInterpreter("node.exe");
        programs.Add("evil.js", "// outside node_modules altogether");
        programs.Add("package.json", """{"name":"vendor","bin":{"vendor":"evil.js"}}""");
        Directory.CreateDirectory(Path.Combine(programs.Root, "node_modules", "@vendor"));
        programs.Add("vendor.cmd", TestPrograms.NpmShim(package, "evil.js"));

        var resolved = Resolve(programs, "vendor");

        Assert.Null(resolved.Path);
        Assert.Equal(ProblemCodes.ExecutableNotRunnable, resolved.Problem!.Code);
        Assert.Contains(rule, resolved.Problem.Message, StringComparison.Ordinal);
    }

    // ---------------------------------------------------------------------------------------------------------
    // The same shim for a target with no shebang: cmd-shim then writes no interpreter at all, and the shim starts an
    // executable the package ships. What starts is that executable, by itself and with nothing in front of it; the
    // shim is still only read.
    // ---------------------------------------------------------------------------------------------------------

    /// <summary>A package that ships its own executable, declared by its <c>bin</c>, and the shim npm writes for it.</summary>
    private static string InstallNativeCli(TestPrograms programs, string target = @"bin\vendor.exe", string? shimTarget = null)
    {
        var file = programs.AddNested(Path.Combine("node_modules", "@vendor", "cli", target), "the binary");
        programs.AddNested(
            @"node_modules\@vendor\cli\package.json",
            "{\"name\":\"@vendor/cli\",\"bin\":{\"vendor\":\"" + (shimTarget ?? target).Replace('\\', '/') + "\"}}");
        programs.Add("vendor.cmd", TestPrograms.NpmNativeShim(@"@vendor\cli", shimTarget ?? target));
        return file;
    }

    [Theory]
    [InlineData(@"bin\vendor.exe")]
    [InlineData(@"bin\vendor.COM")]
    public void A_shim_with_no_interpreter_resolves_to_the_executable_it_names_and_nothing_in_front_of_it(string target)
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), WindowsOnly);
        using var programs = new TestPrograms();
        var file = InstallNativeCli(programs, target);

        var resolved = Resolve(programs, "vendor");

        Assert.Null(resolved.Problem);
        Assert.Equal(file, resolved.Path);
        Assert.Empty(resolved.Launch);
    }

    /// <summary>
    /// Only a file the search path itself would count as a program image counts here, by its name: an <c>.exe</c> or a
    /// <c>.com</c>. A <c>.cmd</c>, a <c>.bat</c> or a <c>.ps1</c> stays a shell's, and so does anything else.
    /// </summary>
    [Theory]
    [InlineData(@"bin\vendor.cmd")]
    [InlineData(@"bin\vendor.bat")]
    [InlineData(@"bin\vendor.ps1")]
    [InlineData(@"bin\vendor.js")]
    [InlineData(@"bin\vendor")]
    [InlineData(@"bin\vendor.EXE.cmd")]
    [InlineData(@"bin\vendor.")]
    public void A_shim_with_no_interpreter_whose_target_is_not_a_program_image_is_refused(string target)
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), WindowsOnly);
        using var programs = new TestPrograms();
        InstallNativeCli(programs, target);

        var resolved = Resolve(programs, "vendor");

        Assert.Null(resolved.Path);
        Assert.Equal(ProblemCodes.ExecutableNotRunnable, resolved.Problem!.Code);
        Assert.Contains("Jason starts programs, never shells", resolved.Problem.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// The target is held inside the package's own tree exactly as the interpreter form's entry is: a package segment
    /// that climbs or names a drive is no name, and an entry that climbs out of the package is outside it.
    /// </summary>
    [Theory]
    [InlineData("..", "evil.exe", "names a package whose name is not one npm could have written")]
    [InlineData("C:", "evil.exe", "names a package whose name is not one npm could have written")]
    [InlineData(@"@..\cli", "evil.exe", "names a scope whose name is not one npm could have written")]
    [InlineData(@"@vendor\cli", @"..\..\..\evil.exe", "outside the package")]
    [InlineData("..", "evil.cmd", "names a package whose name is not one npm could have written")]
    [InlineData(@"@vendor\cli", @"..\..\..\evil.cmd", "outside the package")]
    public void A_shim_with_no_interpreter_whose_target_leaves_the_package_is_refused(string package, string target, string rule)
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), WindowsOnly);
        using var programs = new TestPrograms();
        programs.Add("evil.exe", "outside node_modules altogether");
        programs.Add("package.json", """{"name":"vendor","bin":{"vendor":"evil.exe"}}""");
        programs.AddNested(@"node_modules\@vendor\cli\package.json", """{"name":"@vendor/cli","bin":{"vendor":"../../../evil.exe"}}""");
        programs.Add("vendor.cmd", TestPrograms.NpmNativeShim(package, target));

        var resolved = Resolve(programs, "vendor");

        Assert.Null(resolved.Path);
        Assert.Equal(ProblemCodes.ExecutableNotRunnable, resolved.Problem!.Code);
        Assert.Contains(rule, resolved.Problem.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// An executable the disk does not hold is a program that is not there, the same answer a name nothing answers for
    /// gets, rather than a shim that broke a rule.
    /// </summary>
    [Fact]
    public void A_shim_with_no_interpreter_whose_executable_is_not_there_is_not_found()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), WindowsOnly);
        using var programs = new TestPrograms();
        File.Delete(InstallNativeCli(programs));

        var resolved = Resolve(programs, "vendor");

        Assert.Null(resolved.Path);
        Assert.Equal(ProblemCodes.ExecutableMissing, resolved.Problem!.Code);
        // The file is named as the search path's walk spelled it, with PATHEXT's case, as every refusal here names it.
        Assert.Equal("'vendor' resolves to 'vendor.cmd', npm's shim, whose executable is not there.", resolved.Problem.Message, ignoreCase: true);
    }

    [Fact]
    public void A_shim_with_no_interpreter_naming_a_target_its_package_does_not_declare_is_refused()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), WindowsOnly);
        using var programs = new TestPrograms();
        InstallNativeCli(programs);
        programs.AddNested(@"node_modules\@vendor\cli\package.json", """{"name":"@vendor/cli","bin":{"vendor":"bin/other.exe"}}""");

        var resolved = Resolve(programs, "vendor");

        Assert.Null(resolved.Path);
        Assert.Contains("its package does not declare", resolved.Problem!.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// The form with no interpreter is read only as cmd-shim writes it, the eight lines above the call and nothing
    /// else: a line added anywhere makes it a file this reader does not know.
    /// </summary>
    [Fact]
    public void A_shim_with_no_interpreter_and_a_line_npm_never_writes_is_refused()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), WindowsOnly);
        using var programs = new TestPrograms();
        InstallNativeCli(programs);
        programs.Add("vendor.cmd", TestPrograms.NpmNativeShim(@"@vendor\cli", @"bin\vendor.exe").Replace("CALL :find_dp0\r\n", "CALL :find_dp0\r\ncmd /c whatever\r\n", StringComparison.Ordinal));

        var resolved = Resolve(programs, "vendor");

        Assert.Null(resolved.Path);
        Assert.Contains("is not the shim npm writes", resolved.Problem!.Message, StringComparison.Ordinal);
    }

    /// <summary>The eight lines above the call are cmd-shim's own, each as it writes it: one altered is a file this reader does not know.</summary>
    [Fact]
    public void A_shim_with_no_interpreter_whose_head_line_npm_never_writes_is_refused()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), WindowsOnly);
        using var programs = new TestPrograms();
        InstallNativeCli(programs);
        programs.Add("vendor.cmd", TestPrograms.NpmNativeShim(@"@vendor\cli", @"bin\vendor.exe").Replace("SETLOCAL\r\n", "SETLOCAL EnableDelayedExpansion\r\n", StringComparison.Ordinal));

        var resolved = Resolve(programs, "vendor");

        Assert.Null(resolved.Path);
        Assert.Equal(ProblemCodes.ExecutableNotRunnable, resolved.Problem!.Code);
        Assert.Contains("is not the shim npm writes", resolved.Problem.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// cmd.exe expands a <c>%NAME%</c> in the shim's call before it starts anything, so a target holding one would be a
    /// file other than the one the line spells. npm never writes such a target, in either form, and a line holding one is
    /// not the shim npm writes.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void A_shim_whose_target_holds_a_variable_cmd_would_expand_is_not_the_shim_npm_writes(bool throughNode)
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), WindowsOnly);
        using var programs = new TestPrograms();
        if (throughNode)
        {
            programs.AddInterpreter("node.exe");
            programs.AddNested(@"node_modules\@vendor\cli\dist\%X%.js", "// the entry point");
            programs.AddNested(@"node_modules\@vendor\cli\package.json", """{"name":"@vendor/cli","bin":{"vendor":"dist/%X%.js"}}""");
            programs.Add("vendor.cmd", TestPrograms.NpmShim(@"@vendor\cli", @"dist\%X%.js"));
        }
        else
        {
            InstallNativeCli(programs, @"bin\%X%.exe");
        }

        var resolved = Resolve(programs, "vendor");

        Assert.Null(resolved.Path);
        Assert.Equal(ProblemCodes.ExecutableNotRunnable, resolved.Problem!.Code);
        Assert.Contains("is not the shim npm writes", resolved.Problem.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// cmd.exe also expands a <c>!NAME!</c> where delayed expansion is on, which the account's Command Processor settings
    /// can turn on for every shim, so a target holding a <c>!</c> is held as one holding a <c>%</c> is, in either form.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void A_shim_whose_target_holds_a_delayed_variable_is_not_the_shim_npm_writes(bool throughNode)
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), WindowsOnly);
        using var programs = new TestPrograms();
        if (throughNode)
        {
            programs.AddInterpreter("node.exe");
            programs.AddNested(@"node_modules\@vendor\cli\dist\!X!.js", "// the entry point");
            programs.AddNested(@"node_modules\@vendor\cli\package.json", """{"name":"@vendor/cli","bin":{"vendor":"dist/!X!.js"}}""");
            programs.Add("vendor.cmd", TestPrograms.NpmShim(@"@vendor\cli", @"dist\!X!.js"));
        }
        else
        {
            InstallNativeCli(programs, @"bin\!X!.exe");
        }

        var resolved = Resolve(programs, "vendor");

        Assert.Null(resolved.Path);
        Assert.Equal(ProblemCodes.ExecutableNotRunnable, resolved.Problem!.Code);
        Assert.Contains("is not the shim npm writes", resolved.Problem.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// A <c>%</c> in the package or the scope segment of the form with no interpreter is a variable cmd.exe would expand
    /// before it started anything, as it is in the entry, and no name npm could have written: the files it spells are on
    /// disk and declared, and the shim is still refused.
    /// </summary>
    [Theory]
    [InlineData("v%X%", "names a package whose name is not one npm could have written")]
    [InlineData(@"@v%X%\cli", "names a scope whose name is not one npm could have written")]
    public void A_shim_with_no_interpreter_whose_package_or_scope_holds_a_variable_is_refused(string package, string rule)
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), WindowsOnly);
        using var programs = new TestPrograms();
        programs.AddNested(Path.Combine("node_modules", package, "bin", "vendor.exe"), "the binary");
        programs.AddNested(
            Path.Combine("node_modules", package, "package.json"),
            "{\"name\":\"" + package.Replace('\\', '/') + "\",\"bin\":{\"vendor\":\"bin/vendor.exe\"}}");
        programs.Add("vendor.cmd", TestPrograms.NpmNativeShim(package, @"bin\vendor.exe"));

        var resolved = Resolve(programs, "vendor");

        Assert.Null(resolved.Path);
        Assert.Equal(ProblemCodes.ExecutableNotRunnable, resolved.Problem!.Code);
        Assert.Contains(rule, resolved.Problem.Message, StringComparison.Ordinal);
    }

    /// <summary>The call of the form with no interpreter ends at <c>%*</c>: a command after it is a line npm never writes.</summary>
    [Fact]
    public void A_shim_with_no_interpreter_with_text_after_its_arguments_is_refused()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), WindowsOnly);
        using var programs = new TestPrograms();
        InstallNativeCli(programs);
        programs.Add("vendor.cmd", TestPrograms.NpmNativeShim(@"@vendor\cli", @"bin\vendor.exe").Replace("%*\r\n", "%* & whatever\r\n", StringComparison.Ordinal));

        var resolved = Resolve(programs, "vendor");

        Assert.Null(resolved.Path);
        Assert.Equal(ProblemCodes.ExecutableNotRunnable, resolved.Problem!.Code);
        Assert.Contains("is not the shim npm writes", resolved.Problem.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// The package's own directory a junction out of the tree, for the shim that starts an executable: the target
    /// would read as inside <c>node_modules\@vendor\cli</c> while the executable lived anywhere.
    /// </summary>
    [Fact]
    [Critical(Family.X2, "a junction cannot leave the package")]
    public void A_package_directory_that_is_a_junction_out_of_the_tree_is_refused_for_a_shim_with_no_interpreter()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), WindowsOnly);
        using var programs = new TestPrograms();
        using var elsewhere = new TestPrograms();
        elsewhere.AddNested(@"bin\vendor.exe", "the binary, in somebody else's tree");
        elsewhere.AddNested("package.json", """{"name":"@vendor/cli","bin":{"vendor":"bin/vendor.exe"}}""");
        Directory.CreateDirectory(Path.Combine(programs.Root, "node_modules", "@vendor"));
        Assert.SkipUnless(
            TryJunction(Path.Combine(programs.Root, "node_modules", "@vendor", "cli"), elsewhere.Root),
            "this machine would not create a directory junction.");
        programs.Add("vendor.cmd", TestPrograms.NpmNativeShim(@"@vendor\cli", @"bin\vendor.exe"));

        var resolved = Resolve(programs, "vendor");

        Assert.Null(resolved.Path);
        Assert.Equal(ProblemCodes.ExecutableNotRunnable, resolved.Problem!.Code);
        Assert.Contains("outside the package", resolved.Problem.Message, StringComparison.Ordinal);
    }

    /// <summary>The same junction for a scoped package's shim that hands an entry script to node.</summary>
    [Fact]
    [Critical(Family.X2, "a junction cannot leave the package")]
    public void A_scoped_package_directory_that_is_a_junction_out_of_the_tree_is_refused()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), WindowsOnly);
        using var programs = new TestPrograms();
        using var elsewhere = new TestPrograms();
        programs.AddInterpreter("node.exe");
        elsewhere.AddNested(@"dist\index.js", "// the entry point, in somebody else's tree");
        elsewhere.AddNested("package.json", """{"name":"@vendor/cli","bin":{"vendor":"dist/index.js"}}""");
        Directory.CreateDirectory(Path.Combine(programs.Root, "node_modules", "@vendor"));
        Assert.SkipUnless(
            TryJunction(Path.Combine(programs.Root, "node_modules", "@vendor", "cli"), elsewhere.Root),
            "this machine would not create a directory junction.");
        programs.Add("vendor.cmd", TestPrograms.NpmShim(@"@vendor\cli", @"dist\index.js"));

        var resolved = Resolve(programs, "vendor");

        Assert.Null(resolved.Path);
        Assert.Equal(ProblemCodes.ExecutableNotRunnable, resolved.Problem!.Code);
        Assert.Contains("outside the package", resolved.Problem.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// The scope's directory is one more place a junction could stand: the entry would still read as inside
    /// <c>node_modules\@vendor\cli</c> while the file lived anywhere, so the package's real directory is followed
    /// through the scope as well.
    /// </summary>
    [Fact]
    [Critical(Family.X2, "a junction cannot leave the package")]
    public void A_scope_directory_that_is_a_junction_out_of_the_tree_is_refused()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), WindowsOnly);
        using var programs = new TestPrograms();
        using var elsewhere = new TestPrograms();
        programs.AddInterpreter("node.exe");
        elsewhere.AddNested(@"cli\dist\index.js", "// the entry point, in somebody else's tree");
        elsewhere.AddNested(@"cli\package.json", """{"name":"@vendor/cli","bin":{"vendor":"dist/index.js"}}""");
        Directory.CreateDirectory(Path.Combine(programs.Root, "node_modules"));
        Assert.SkipUnless(
            TryJunction(Path.Combine(programs.Root, "node_modules", "@vendor"), elsewhere.Root),
            "this machine would not create a directory junction.");
        programs.Add("vendor.cmd", TestPrograms.NpmShim(@"@vendor\cli", @"dist\index.js"));

        var resolved = Resolve(programs, "vendor");

        Assert.Null(resolved.Path);
        Assert.Equal(ProblemCodes.ExecutableNotRunnable, resolved.Problem!.Code);
        Assert.Contains("outside the package", resolved.Problem.Message, StringComparison.Ordinal);
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
    [Critical(Family.X2, "a junction cannot leave the package")]
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
    [Critical(Family.F6, "shim precedence")]
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
