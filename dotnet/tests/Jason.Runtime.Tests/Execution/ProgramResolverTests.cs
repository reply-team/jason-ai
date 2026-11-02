using Jason.Runtime.Execution.Hosts;
using Jason.Runtime.Tests.Plugins;

namespace Jason.Runtime.Tests.Execution;

/// <summary>
/// Where a profile's program is on this machine. The answer is a file or it is nothing: "nothing" is the reason
/// an attempt is refused before a child exists, so a resolver that guessed would turn a missing host into a
/// failed run.
/// </summary>
public class ProgramResolverTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "jason-tests", Guid.NewGuid().ToString("N"));

    public ProgramResolverTests() => Directory.CreateDirectory(_directory);

    [Fact]
    public void A_path_is_the_program_when_it_is_really_there()
    {
        var program = Executable("host");

        Assert.Equal([program], new ProgramResolver(Search(string.Empty)).Resolve(program));
    }

    [Fact]
    public void A_path_that_is_not_there_resolves_to_nothing()
    {
        var missing = Path.Combine(_directory, "nowhere", "host");

        Assert.Null(new ProgramResolver(Search(_directory)).Resolve(missing));
    }

    [Fact]
    public void A_directory_is_not_a_program()
    {
        Assert.Null(new ProgramResolver(Search(string.Empty)).Resolve(_directory));
    }

    [Fact]
    public void A_bare_name_is_looked_up_on_the_search_path()
    {
        var program = Executable("host");

        var resolved = new ProgramResolver(Search(_directory)).Resolve("host");

        Assert.Equal([program], resolved);
    }

    [Fact]
    public void A_bare_name_nothing_answers_for_resolves_to_nothing()
    {
        Executable("host");

        Assert.Null(new ProgramResolver(Search(_directory)).Resolve("other-host"));
    }

    [Fact]
    public void The_first_directory_of_the_search_path_wins()
    {
        var first = Path.Combine(_directory, "first");
        var second = Path.Combine(_directory, "second");
        Directory.CreateDirectory(first);
        Directory.CreateDirectory(second);
        var winner = Executable("host", first);
        Executable("host", second);

        Assert.Equal([winner], new ProgramResolver(Search(first + Path.PathSeparator + second)).Resolve("host"));
    }

    [Fact]
    public void A_bare_name_carries_no_directory_of_its_own()
    {
        // Nothing may be resolved relative to the runtime's own working directory: what a profile names is a
        // path or a name on the search path, and "host/inner" is neither.
        Executable("inner", Directory.CreateDirectory(Path.Combine(_directory, "host")).FullName);

        Assert.Null(new ProgramResolver(Search(_directory)).Resolve(Path.Combine("host", "inner")));
    }

    [Fact]
    [Critical(Family.F6, "PATHEXT resolution")]
    public void On_Windows_the_platforms_own_extensions_answer_for_a_bare_name()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Windows is the platform whose bare names carry an extension.");
        var program = Executable("host.exe");

        Assert.Equal([program], new ProgramResolver(Search(_directory)).Resolve("host"));
    }

    [Fact]
    public void A_file_nobody_may_run_is_not_a_program()
    {
        // Windows has no execute bit; there a name resolves through the platform's extensions instead.
        Assert.SkipWhen(OperatingSystem.IsWindows(), "The execute bit is a Unix fact.");
        NonExecutable("host");

        Assert.Null(new ProgramResolver(Search(_directory)).Resolve("host"));
    }

    [Fact]
    public void The_default_command_word_is_a_word_and_not_a_path()
    {
        // A test runs under whatever host started it, so the name itself is never the claim; that it is one bare
        // word is, because it goes into an allow rule and is typed by an agent.
        var word = ProgramResolver.DefaultCliCommand;

        Assert.NotEmpty(word);
        Assert.False(word.Contains(Path.DirectorySeparatorChar), $"'{word}' carries a directory separator.");
        Assert.False(word.Contains(Path.AltDirectorySeparatorChar), $"'{word}' carries a directory separator.");
        Assert.False(Path.IsPathRooted(word), $"'{word}' is a path rather than a command word.");
    }

    [Fact]
    public void A_profile_that_names_a_command_word_keeps_it_and_one_that_does_not_gets_the_runtimes_own()
    {
        Assert.Equal("jason-dev", ProgramResolver.CliCommandFor("jason-dev"));
        Assert.Equal(ProgramResolver.DefaultCliCommand, ProgramResolver.CliCommandFor(null));
        Assert.Equal(ProgramResolver.DefaultCliCommand, ProgramResolver.CliCommandFor("   "));
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private string Executable(string name, string? directory = null)
    {
        var file = Path.Combine(directory ?? _directory, OperatingSystem.IsWindows() && !name.EndsWith(".exe", StringComparison.Ordinal) ? name + ".exe" : name);
        File.WriteAllText(file, string.Empty);
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(file, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }

        return file;
    }

    /// <summary>A file that is there and that nobody may run.</summary>
    private void NonExecutable(string name)
    {
        var file = Path.Combine(_directory, name);
        File.WriteAllText(file, "#!/bin/sh");
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(file, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
    }

    /// <summary>
    /// The mainstream way an agent host arrives on Windows: npm writes a <c>.cmd</c> shim, and no <c>.exe</c>
    /// answers for the name anywhere on the search path. Starting the shim would mean handing a command line
    /// to <c>cmd.exe</c> to interpret, which is the shell this runtime composes argument arrays to avoid — so
    /// the shim is read instead, and what runs is the interpreter and entry script it names. The plugin
    /// registry already does exactly this for a plugin's declared program, and a profile's program is resolved
    /// the same way: two answers to "where is this program" would be one answer too many.
    /// </summary>
    [Fact]
    [Critical(Family.F6, "the npm shim on Windows")]
    public void A_host_installed_by_npm_resolves_to_its_interpreter_and_entry_script()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "npm writes a .cmd shim only on Windows.");
        using var programs = new Plugins.TestPrograms();
        var node = programs.AddInterpreter("node.exe");
        var entry = programs.AddNested(@"node_modules\vendor-host\cli.js", "// the host");
        programs.AddNested(
            @"node_modules\vendor-host\package.json",
            """{"name":"vendor-host","bin":{"vendor-host":"cli.js"}}""");
        programs.Add("vendor-host.cmd", Plugins.TestPrograms.NpmShim("vendor-host", "cli.js"));

        var launch = new ProgramResolver(Search(programs.Root)).Resolve("vendor-host");

        Assert.Equal([node, entry], launch);
    }

    /// <summary>
    /// A host installed as a launcher script that starts an interpreter, as Cursor's is on Windows: the script itself is
    /// never started, since that would hand a command line to a shell, but its host knows what the script starts, so the
    /// answer for the script is the interpreter and the entry script. A path to the script names the file the way a
    /// person names it.
    /// </summary>
    [Fact]
    public void A_path_to_the_hosts_own_launcher_script_resolves_to_what_that_script_starts()
    {
        var root = CursorInstall();

        var launch = new ProgramResolver(Search(string.Empty)).Resolve(Path.Combine(root, "agent.cmd"), new CursorCliHost(windows: true));

        Assert.Equal(
            [Path.Combine(root, "versions", "2026.10.22-27dcf5f", "node.exe"), Path.Combine(root, "versions", "2026.10.22-27dcf5f", "index.js")],
            launch);
    }

    [Fact]
    public void A_launcher_script_is_still_no_program_when_no_host_is_asked_about_it()
    {
        var script = Path.Combine(CursorInstall(), "agent.cmd");

        Assert.Null(new ProgramResolver(Search(string.Empty)).Resolve(script));
        Assert.Null(new ProgramResolver(Search(string.Empty)).Resolve(script, null));
    }

    [Fact]
    public void A_script_that_is_not_the_asked_hosts_launcher_is_still_no_program()
    {
        var root = CursorInstall();
        File.WriteAllText(Path.Combine(root, "claude.cmd"), string.Empty);

        Assert.Null(new ProgramResolver(Search(string.Empty)).Resolve(Path.Combine(root, "claude.cmd"), new CursorCliHost(windows: true)));
    }

    [Fact]
    public void A_launcher_script_with_nothing_installed_beside_it_is_no_program()
    {
        var root = CursorInstall();
        Directory.Delete(Path.Combine(root, "versions"), recursive: true);

        Assert.Null(new ProgramResolver(Search(string.Empty)).Resolve(Path.Combine(root, "agent.cmd"), new CursorCliHost(windows: true)));
    }

    [Fact]
    public void A_path_to_a_program_is_answered_the_same_whichever_host_is_asked()
    {
        var program = Executable("host");

        Assert.Equal([program], new ProgramResolver(Search(string.Empty)).Resolve(program, new CursorCliHost(windows: true)));
    }

    [Fact]
    public void On_Windows_a_bare_name_that_only_a_launcher_script_answers_for_resolves_to_what_it_starts()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Windows is the platform whose bare names carry an extension.");
        var root = CursorInstall();
        var search = new TestSearchPath { Path = root, PathExt = ".COM;.EXE;.BAT;.CMD" };

        var launch = new ProgramResolver(search).Resolve("agent", new CursorCliHost(windows: true));

        Assert.Equal(Path.Combine(root, "versions", "2026.10.22-27dcf5f", "node.exe"), launch![0]);
        Assert.Null(new ProgramResolver(search).Resolve("agent"));
    }

    private string CursorInstall()
    {
        var root = Path.Combine(_directory, "cursor-agent");
        var version = Path.Combine(root, "versions", "2026.10.22-27dcf5f");
        Directory.CreateDirectory(version);
        foreach (var file in new[] { "agent.cmd", "cursor-agent.cmd", "cursor-agent.ps1" })
        {
            File.WriteAllText(Path.Combine(root, file), string.Empty);
        }

        File.WriteAllText(Path.Combine(version, "node.exe"), string.Empty);
        File.WriteAllText(Path.Combine(version, "index.js"), string.Empty);
        return root;
    }

    private static TestSearchPath Search(string path) => new() { Path = path };
}
