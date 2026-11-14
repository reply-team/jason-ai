using Jason.Contracts.Api;
using Jason.Contracts.Discovery;
using Jason.Contracts.Execution;
using Jason.Runtime.Execution.Hosts;
using Jason.Runtime.Hosting.Modules;
using Jason.Runtime.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace Jason.Runtime.Tests.Execution;

/// <summary>
/// The shape one headless Codex CLI run is started with. Codex confines a run by a sandbox rather than by a list of
/// commands, so what the runtime composes is the sandbox: a permissions profile of its own, named on the command line,
/// that reads only what a role needs, writes nothing, and reaches this machine's loopback and no other address. Every
/// value is held here as the exact text the host is given, since a value the host fails to read as TOML it takes as a
/// string without saying so.
/// </summary>
public class CodexHostTests
{
    private const string Run = "/home/someone/.jason/run";

    private const string Executable = "/home/someone/.local/bin/jason";

    private const string Extraction = "/home/someone/.net/jason/a1b2c3";

    private static readonly MemoryFileLocations NoFiles = new(null, null, null);

    private static CodexHost Host(bool windows = false) => new(Run, Executable, Extraction, windows);

    [Fact]
    [Critical(Family.S6, "the composed command: no configuration, rule, hook or server of the person's reaches a role")]
    public void The_run_is_headless_reports_as_it_goes_and_reads_none_of_the_persons_own_configuration()
    {
        var command = Host().Compose(Revision(), ["codex"], NoFiles).Command;

        string[] expected =
            [
                "codex",
                "exec",
                "--json",
                "--skip-git-repo-check",
                "--ephemeral",
                "--ignore-user-config",
                "--ignore-rules",
                "--strict-config",
                "-c", "approval_policy=\"never\"",
                "-c", "default_permissions=\"jason\"",
                "-c", "permissions.jason.filesystem={\":minimal\"=\"read\", \":workspace_roots\"=\"read\", \"/home/someone/.local/bin/jason\"=\"read\", \"/home/someone/.net/jason/a1b2c3\"=\"read\", \"/home/someone/.jason/run\"=\"read\"}",
                "-c", "permissions.jason.network={enabled=true, allow_local_binding=true}",
                "-c", "features.network_proxy=true",
                "-c", "web_search=\"disabled\"",
                "-c", "project_root_markers=[]",
                "-c", "features.hooks=false",
                "-c", "features.plugins=false",
                "-c", "features.apps=false",
                "-c", "features.memories=false",
                "-c", "mcp_servers={}",
            ];
        Assert.Equal(expected, command);
    }

    /// <summary>
    /// The brief goes on stdin and nowhere else: with no prompt among the arguments the host reads its instructions from
    /// stdin, and a word the host would read as a prompt or a subcommand is never composed.
    /// </summary>
    [Fact]
    [Critical(Family.W7, "the brief goes on stdin and no word of it on the command line")]
    public void No_prompt_is_composed_so_the_host_reads_the_brief_from_stdin()
    {
        var command = Host().Compose(Revision(), ["codex"], NoFiles).Command;

        var afterExec = command.Skip(2).ToList();
        for (var index = 0; index < afterExec.Count; index++)
        {
            if (!afterExec[index].StartsWith('-'))
            {
                Assert.Equal("-c", afterExec[index - 1]);
            }
        }

        Assert.DoesNotContain("-", command);
    }

    /// <summary>
    /// On Windows the sandbox is the elevated one, named on every run: the person's own choice of Windows sandbox is in
    /// the configuration the run does not read, and with none named this host runs a command with no sandbox at all.
    /// Until an administrator has set that sandbox up, every command a role runs is refused, and the run fails closed.
    /// </summary>
    [Fact]
    [Critical(Family.S6, "a run with no sandbox named runs a command with none at all")]
    public void On_windows_every_run_names_the_elevated_sandbox_and_elsewhere_none()
    {
        var windows = new CodexHost(@"C:\j", @"C:\x", @"C:\t", windows: true).Compose(Revision(args: ["--model=made-up-model"]), ["codex"], NoFiles).Command;
        var elsewhere = Host().Compose(Revision(), ["codex"], NoFiles).Command;

        var at = windows.ToList().IndexOf("windows.sandbox=\"elevated\"");
        Assert.True(at > 0, "The elevated sandbox is not named.");
        Assert.Equal("-c", windows[at - 1]);
        Assert.Equal("--model=made-up-model", windows[^1]);
        Assert.DoesNotContain(elsewhere, word => word.StartsWith("windows.", StringComparison.Ordinal));
    }

    [Fact]
    [Critical(Family.S6, "a search is off unless the revision grants it")]
    public void A_web_search_the_person_granted_is_live_and_otherwise_off()
    {
        var granted = Host().Compose(Revision(tools: [AgentTool.WebSearch]), ["codex"], NoFiles).Command;
        var withheld = Host().Compose(Revision(), ["codex"], NoFiles).Command;

        Assert.Contains("web_search=\"live\"", granted);
        Assert.DoesNotContain("web_search=\"disabled\"", granted);
        Assert.Contains("web_search=\"disabled\"", withheld);
    }

    [Fact]
    [Critical(Family.S6, "the read entries, exactly: a folder or a pattern beside the files' own turns this red")]
    public void Each_memory_file_the_launch_names_is_readable_once_and_nothing_else_of_its_folder()
    {
        var files = new MemoryFileLocations("/home/someone/Jason/AGENTS.md", "/home/someone/Jason/acme/AGENTS.md", "/home/someone/Jason/AGENTS.md");

        var launch = Host().Compose(Revision(), ["codex"], files);

        Assert.Contains(
            "permissions.jason.filesystem={\":minimal\"=\"read\", \":workspace_roots\"=\"read\", \"/home/someone/.local/bin/jason\"=\"read\", \"/home/someone/.net/jason/a1b2c3\"=\"read\", \"/home/someone/.jason/run\"=\"read\", \"/home/someone/Jason/AGENTS.md\"=\"read\", \"/home/someone/Jason/acme/AGENTS.md\"=\"read\"}",
            launch.Command);
        Assert.Empty(launch.Unreadable);
    }

    /// <summary>
    /// A path is a TOML string: a backslash, a quote and a control character are escaped, so a folder named with a quote
    /// stays the one path it is rather than ending the value early. On Windows each path is written as a drive and
    /// forward slashes, the form a deny's is, so no backslash is left to escape.
    /// </summary>
    [Fact]
    public void A_windows_path_is_written_with_forward_slashes_and_a_quote_in_a_folder_name_is_escaped_as_toml()
    {
        var host = new CodexHost(@"C:\Users\someone\.jason\run", @"C:\Program Files\Jason", @"C:\Users\someone\AppData\Local\Temp\.net", windows: true);
        var files = new MemoryFileLocations(@"C:\Users\someone\Jason\The ""Big"" One\AGENTS.md", null, null);

        var command = host.Compose(Revision(), ["codex"], files).Command;

        Assert.Contains(
            "permissions.jason.filesystem={\":minimal\"=\"read\", \":workspace_roots\"=\"read\", \"C:/Program Files/Jason\"=\"read\", \"C:/Users/someone/AppData/Local/Temp/.net\"=\"read\", \"C:/Users/someone/.jason/run\"=\"read\", \"C:/Users/someone/Jason/The \\\"Big\\\" One/AGENTS.md\"=\"read\"}",
            command);
    }

    /// <summary>
    /// A file whose path holds a character the sandbox reads as a pattern cannot be named as one file, so its level is
    /// unreadable and no wider rule is guessed at, as on every host.
    /// </summary>
    [Fact]
    [Critical(Family.S6, "a path that reads as a pattern composes no entry")]
    public void A_memory_file_whose_path_reads_as_a_pattern_is_left_unreadable()
    {
        var files = new MemoryFileLocations("/home/someone/Jason/AGENTS.md", "/home/someone/Jason/[draft]/AGENTS.md", null);

        var launch = Host().Compose(Revision(), ["codex"], files);

        Assert.Equal(["project"], launch.Unreadable);
        Assert.DoesNotContain(launch.Command, word => word.Contains("[draft]", StringComparison.Ordinal));
    }

    [Fact]
    [Critical(Family.S6, "a deny of reading holds in the sandbox, and takes a read's place")]
    public void A_path_the_profile_denies_reading_is_denied_in_the_sandbox()
    {
        var revision = Revision(deny: ["Read(//srv/secrets/**)", "Read(~/private/notes.md)", "Write", "WebFetch"]);

        var command = new CodexHost(Run, Executable, Extraction, windows: false, home: "/home/someone").Compose(revision, ["codex"], NoFiles).Command;

        Assert.Contains(
            "permissions.jason.filesystem={\":minimal\"=\"read\", \":workspace_roots\"=\"read\", \"/home/someone/.local/bin/jason\"=\"read\", \"/home/someone/.net/jason/a1b2c3\"=\"read\", \"/home/someone/.jason/run\"=\"read\", \"/srv/secrets/**\"=\"deny\", \"/home/someone/private/notes.md\"=\"deny\"}",
            command);
    }

    /// <summary>
    /// A deny wins over a read, and a path is listed once: where the profile denies a path the run would otherwise read —
    /// a memory file the launch names, by the home or from the root, or the executable calling home starts — the deny
    /// takes the read's place rather than being dropped beside it.
    /// </summary>
    [Theory]
    [InlineData("Read(~/Jason/AGENTS.md)", "/home/someone/Jason/AGENTS.md")]
    [InlineData("Read(//home/someone/Jason/AGENTS.md)", "/home/someone/Jason/AGENTS.md")]
    [InlineData("Read(//home/someone/.local/bin/jason)", "/home/someone/.local/bin/jason")]
    public void A_deny_of_a_path_the_run_would_read_takes_the_reads_place(string word, string path)
    {
        var files = new MemoryFileLocations("/home/someone/Jason/AGENTS.md", null, null);

        var command = new CodexHost(Run, Executable, Extraction, windows: false, home: "/home/someone")
            .Compose(Revision(deny: [word]), ["codex"], files).Command;

        var filesystem = Assert.Single(command, item => item.StartsWith("permissions.jason.filesystem=", StringComparison.Ordinal));
        Assert.Contains($"\"{path}\"=\"deny\"", filesystem, StringComparison.Ordinal);
        Assert.DoesNotContain($"\"{path}\"=\"read\"", filesystem, StringComparison.Ordinal);
        Assert.Equal(1, filesystem.Split($"\"{path}\"=").Length - 1);
    }

    /// <summary>
    /// The same replacement on Windows, where the platform spells a path with backslashes, and a drive in either case,
    /// while a deny is written as an upper-case drive and forward slashes: both are written in the deny's one form, so
    /// they are one key and the deny takes the read's place rather than being listed beside it.
    /// </summary>
    [Theory]
    [InlineData(@"C:\Users\someone\Jason\AGENTS.md", "Read(~/Jason/AGENTS.md)", "C:/Users/someone/Jason/AGENTS.md")]
    [InlineData(@"c:\Users\someone\Jason\AGENTS.md", "Read(~/Jason/AGENTS.md)", "C:/Users/someone/Jason/AGENTS.md")]
    [InlineData(@"C:\Users\someone\Jason\AGENTS.md", "Read(//c/Users/someone/Jason/AGENTS.md)", "C:/Users/someone/Jason/AGENTS.md")]
    [InlineData(@"C:\Users\someone\Jason\AGENTS.md", "Read(//C/Users/someone/Jason/AGENTS.md)", "C:/Users/someone/Jason/AGENTS.md")]
    public void On_windows_a_deny_of_a_path_the_run_would_read_takes_the_reads_place(string memoryFile, string word, string path)
    {
        var files = new MemoryFileLocations(memoryFile, null, null);

        var command = new CodexHost(@"C:\j", @"C:\x", @"C:\t", windows: true, home: @"C:\Users\someone")
            .Compose(Revision(deny: [word]), ["codex"], files).Command;

        var filesystem = Assert.Single(command, item => item.StartsWith("permissions.jason.filesystem=", StringComparison.Ordinal));
        Assert.Contains($"\"{path}\"=\"deny\"", filesystem, StringComparison.Ordinal);
        Assert.DoesNotContain($"\"{path}\"=\"read\"", filesystem, StringComparison.Ordinal);
        Assert.Equal(1, filesystem.Split($"\"{path}\"=").Length - 1);
        Assert.DoesNotContain("Users\\\\someone", filesystem, StringComparison.Ordinal);
    }

    [Fact]
    public void A_windows_path_in_the_profiles_deny_names_its_drive()
    {
        var revision = Revision(deny: ["Read(//d/Shared/Finance/**)"]);

        var command = new CodexHost(@"C:\j", @"C:\x", @"C:\t", windows: true).Compose(revision, ["codex"], NoFiles).Command;

        Assert.Contains(command, word => word.Contains("\"D:/Shared/Finance/**\"=\"deny\"", StringComparison.Ordinal));
    }

    /// <summary>
    /// A deny of a path from the home is written in the one form every deny word is written in on Windows, a drive and
    /// forward slashes, whichever way the platform spells the home and whether it ends in a separator: the home's own
    /// backslashes are not carried into a path whose rest is forward slashes.
    /// </summary>
    [Theory]
    [InlineData(@"C:\Users\someone", "C:/Users/someone/private/notes.md")]
    [InlineData(@"C:\Users\someone\", "C:/Users/someone/private/notes.md")]
    [InlineData("C:/Users/someone", "C:/Users/someone/private/notes.md")]
    public void A_windows_deny_from_the_home_is_one_form_with_forward_slashes(string home, string path)
    {
        var revision = Revision(deny: ["Read(~/private/notes.md)"]);

        var command = new CodexHost(@"C:\j", @"C:\x", @"C:\t", windows: true, home: home).Compose(revision, ["codex"], NoFiles).Command;

        var filesystem = Assert.Single(command, item => item.StartsWith("permissions.jason.filesystem=", StringComparison.Ordinal));
        Assert.Contains($"\"{path}\"=\"deny\"", filesystem, StringComparison.Ordinal);
        Assert.DoesNotContain("someone\\private", filesystem, StringComparison.Ordinal);
    }

    /// <summary>
    /// Where the runtime holds no data directory of its own — a composition of the dispatcher alone, in a test — the
    /// descriptor's folder is not named rather than guessed.
    /// </summary>
    [Fact]
    public void Without_a_data_directory_no_descriptor_folder_is_named()
    {
        var command = new CodexHost(null, null, null, windows: false).Compose(Revision(), ["codex"], NoFiles).Command;

        Assert.Contains("permissions.jason.filesystem={\":minimal\"=\"read\", \":workspace_roots\"=\"read\"}", command);
    }

    /// <summary>
    /// A single-file build is one file that unpacks into a folder of its own, so calling home reads that file and that
    /// folder and nothing else beside them: not the folder the file is in, which may be a shared one such as
    /// <c>~/.local/bin</c>, and not every other bundle under <c>.net</c>.
    /// </summary>
    [Fact]
    public void Calling_home_from_a_single_file_build_reads_its_file_and_the_folder_it_unpacked_into()
    {
        var reads = CodexHost.CallHomeReads(
            "/home/someone/.local/bin/jason", "/home/someone/.local/bin", "/home/someone/.net/jason/a1b2c3", "/home/someone/.net");

        Assert.Equal(("/home/someone/.local/bin/jason", "/home/someone/.net/jason/a1b2c3"), reads);
    }

    /// <summary>Any other build is a folder of many files, read whole, beside the base a build unpacks under by rule.</summary>
    [Fact]
    public void Calling_home_from_a_build_of_many_files_reads_its_folder_and_the_base_by_rule()
    {
        Assert.Equal(("/src/bin/Debug", "/home/someone/.net"), CodexHost.CallHomeReads(null, "/src/bin/Debug", null, "/home/someone/.net"));
        Assert.Equal(
            ("/home/someone/.local/bin", "/home/someone/.net"),
            CodexHost.CallHomeReads("/home/someone/.local/bin/jason", "/home/someone/.local/bin", null, "/home/someone/.net"));
    }

    /// <summary>
    /// The host the runtime registers names the descriptor's folder of the data directory it keeps, by the one rule that
    /// names it, and what calling home reads of the build running the suite: no single-file build, so its folder, and the
    /// base a build unpacks under by rule, which outside Windows is under the home and never the temporary folder.
    /// </summary>
    [Fact]
    public void The_registered_host_reads_the_descriptors_folder_and_this_build_as_it_is()
    {
        var paths = new JasonPaths(Path.Combine(Path.GetTempPath(), "jason-codex-registration"), 1);
        var services = new ServiceCollection();
        services.AddSingleton(paths);
        services.AddDispatcherModule();
        using var provider = services.BuildServiceProvider();
        var host = provider.GetServices<IAgentHost>().Single(candidate => candidate.Kind == AgentHostKind.Codex);

        var filesystem = Assert.Single(
            host.Compose(Revision(), ["codex"], NoFiles).Command,
            word => word.StartsWith("permissions.jason.filesystem=", StringComparison.Ordinal));

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var unpackBase = Environment.GetEnvironmentVariable(ExtractedLibraries.BaseVariable) is { Length: > 0 } configured
            ? configured
            : OperatingSystem.IsWindows()
                ? Path.GetTempPath().TrimEnd('\\') + "\\.net"
                : home.TrimEnd('/') + "/.net";
        Assert.Contains(Toml(paths.RunDirectory) + "=\"read\"", filesystem, StringComparison.Ordinal);
        Assert.Contains(Toml(ProgramResolver.ExecutableDirectory!) + "=\"read\"", filesystem, StringComparison.Ordinal);
        Assert.Contains(Toml(unpackBase) + "=\"read\"", filesystem, StringComparison.Ordinal);
        if (!OperatingSystem.IsWindows())
        {
            Assert.DoesNotContain(Toml(Path.Combine(Path.GetTempPath(), ".net")), filesystem, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// A path as the host's TOML string carries it, between quotes: on Windows in the form every filesystem entry is written
    /// in, forward slashes and an upper-case drive, and elsewhere as it is.
    /// </summary>
    private static string Toml(string path)
    {
        if (OperatingSystem.IsWindows())
        {
            path = path.Replace('\\', '/');
            path = path.Length >= 2 && path[1] == ':' ? char.ToUpperInvariant(path[0]) + path[1..] : path;
        }

        return "\"" + path + "\"";
    }

    [Fact]
    public void The_profiles_arguments_come_last()
    {
        var command = Host().Compose(Revision(args: ["--model=made-up-model"]), ["/usr/bin/node", "/usr/lib/codex.js"], NoFiles).Command;

        Assert.Equal(["/usr/bin/node", "/usr/lib/codex.js", "exec"], command.Take(3));
        Assert.Equal("--model=made-up-model", command[^1]);
    }

    /// <summary>
    /// Every flag that would change the shape above or undo the sandbox is the runtime's; a model is the person's. The
    /// list is read from the parser's declarations at the measured tag, every long name, short name, alias and visible
    /// alias, never from its help, which prints no alias: <c>--yolo</c> is a second name for
    /// <c>--dangerously-bypass-approvals-and-sandbox</c> that the help never shows.
    /// </summary>
    [Theory]
    [InlineData("exec")]
    [InlineData("-c")]
    [InlineData("--config")]
    [InlineData("--enable")]
    [InlineData("--disable")]
    [InlineData("-s")]
    [InlineData("--sandbox")]
    [InlineData("-a")]
    [InlineData("--ask-for-approval")]
    [InlineData("--approve-for-me")]
    [InlineData("--not-so-yolo")]
    // Critical: a profile cannot lift the sandbox.
    [InlineData("--dangerously-bypass-approvals-and-sandbox", Traits = ["Category", "Critical", "CriticalFamily", nameof(Family.S6)])]
    [InlineData("--yolo")]
    [InlineData("--dangerously-bypass-hook-trust")]
    [InlineData("-C")]
    [InlineData("--cd")]
    [InlineData("--add-dir")]
    [InlineData("--worktree")]
    [InlineData("--ignore-user-config")]
    [InlineData("--ignore-rules")]
    [InlineData("--strict-config")]
    [InlineData("--json")]
    [InlineData("--experimental-json")]
    [InlineData("-o")]
    [InlineData("--output-last-message")]
    [InlineData("--output-schema")]
    [InlineData("-p")]
    [InlineData("--profile")]
    [InlineData("-P")]
    [InlineData("--permission-profile")]
    [InlineData("-i")]
    [InlineData("--image")]
    [InlineData("--search")]
    [InlineData("--remote")]
    public void A_flag_of_the_composed_shape_or_one_that_would_undo_the_sandbox_is_reserved(string flag)
    {
        Assert.Contains(flag, Host().ReservedFlags);
    }

    [Theory]
    [InlineData("-m")]
    [InlineData("--model")]
    [InlineData("--color")]
    public void A_model_and_the_colour_of_the_output_are_the_persons(string flag)
    {
        Assert.DoesNotContain(flag, Host().ReservedFlags);
    }

    [Fact]
    [Critical(Family.S3, "no file in the work directory, so no token in one")]
    public void The_work_directory_is_left_as_the_runtime_made_it()
    {
        var workDir = Directory.CreateTempSubdirectory("jason-codex-").FullName;
        try
        {
            Host().WriteWorkDirectory(workDir, [], ["Write", "WebFetch"]);

            Assert.Empty(Directory.EnumerateFileSystemEntries(workDir));
        }
        finally
        {
            Directory.Delete(workDir, recursive: true);
        }
    }

    /// <summary>
    /// This host mints the id of its own run and cannot be told one, so a launch names none and the provenance records
    /// none, rather than an id the host never saw. The thread the host minted stays readable in what it printed.
    /// </summary>
    [Fact]
    public void A_run_names_no_session_since_the_host_mints_its_own()
    {
        var launch = Host().Compose(Revision(), ["codex"], NoFiles);

        Assert.False(Host().TakesSessionId);
        Assert.Null(launch.SessionId);
        Assert.Empty(launch.Allow);
        Assert.Empty(launch.Deny);
    }

    /// <summary>
    /// The host searches the web and has no tool of its own that fetches a page, so a grant of a fetch is one it does not
    /// have and a deny of one holds already. A search is taken away by the deny word a profile writes for it.
    /// </summary>
    [Fact]
    public void A_search_is_the_one_tool_the_host_has_and_a_fetch_is_not_one()
    {
        Assert.Equal("WebSearch", Host().ToolRule(AgentTool.WebSearch));
        Assert.Null(Host().ToolRule(AgentTool.WebFetch));
        Assert.Null(((IAgentHost)Host()).GrantProblem(AgentTool.WebSearch));
        Assert.Null(((IAgentHost)Host()).GrantProblem(AgentTool.WebFetch));
    }

    /// <summary>A denied search stays off even where the revision grants it, since the host's deny wins over the grant.</summary>
    [Fact]
    public void A_search_the_profile_denies_stays_off_even_where_it_is_granted()
    {
        var command = Host().Compose(Revision(deny: ["WebSearch"], tools: [AgentTool.WebSearch]), ["codex"], NoFiles).Command;

        Assert.Contains("web_search=\"disabled\"", command);
        Assert.DoesNotContain("web_search=\"live\"", command);
    }

    /// <summary>
    /// What a deny of a write, a search, a fetch or a read means here: no write root is granted, a search is switched
    /// off, no fetch tool exists and the network reaches loopback alone, and a read is a path the sandbox denies.
    /// </summary>
    [Theory]
    [InlineData("Write")]
    [InlineData("Edit")]
    [InlineData("MultiEdit")]
    [InlineData("NotebookEdit")]
    [InlineData("WebSearch")]
    [InlineData("WebFetch")]
    [InlineData("Read(//srv/secrets/**)")]
    [InlineData("Read(~/private/notes.md)")]
    public void A_deny_the_sandbox_holds_is_taken_as_the_person_wrote_it(string word)
    {
        Assert.Null(Host().DenyProblem(word));
    }

    /// <summary>
    /// Codex confines a run by its sandbox rather than by a list of tools or commands, so it reads no rule by any other
    /// word: a deny of one could only be dropped at a launch, and it is refused where the profile is written instead,
    /// saying so. Never that a command runs inside the sandbox whatever its name, which is not true of an MCP tool.
    /// </summary>
    [Theory]
    [InlineData("Bash")]
    [InlineData("Bash(curl *)")]
    [InlineData("PowerShell(Invoke-WebRequest *)")]
    [InlineData("Shell(rm *)")]
    [InlineData("Glob")]
    [InlineData("Grep")]
    [InlineData("Task")]
    [InlineData("mcp__made_up__send")]
    [InlineData("WebFetch(domain:example.com)")]
    [InlineData("WebSearch(competitors)")]
    [InlineData("Write(//srv/**)")]
    [InlineData("web_search")]
    public void A_deny_the_sandbox_cannot_hold_is_refused_with_its_reason(string word)
    {
        var problem = Host().DenyProblem(word);

        Assert.Equal("codex reads no rule by that word; it confines a run by its sandbox, not by a list of tools.", problem);
    }

    /// <summary>A read is denied by a path the sandbox can name, and a word that names none is refused with the forms that do.</summary>
    [Theory]
    [InlineData("Read")]
    [InlineData("Read(notes/**)")]
    [InlineData("Read(./notes.md)")]
    public void A_deny_of_a_read_the_sandbox_cannot_name_is_refused_with_the_forms_it_can(string word)
    {
        Assert.Contains("Read(//", Host().DenyProblem(word) ?? "", StringComparison.Ordinal);
    }

    [Fact]
    public void On_windows_a_read_deny_names_a_drive()
    {
        var host = new CodexHost(@"C:\j", @"C:\x", @"C:\t", windows: true);

        Assert.Null(host.DenyProblem("Read(//d/Shared/Finance/**)"));
        Assert.Null(host.DenyProblem("Read(~/private/notes.md)"));
        Assert.False(string.IsNullOrWhiteSpace(host.DenyProblem("Read(//srv/secrets/**)")));
    }

    /// <summary>
    /// The host reads a bare word as the prompt, which would replace the brief on stdin, or as a subcommand; and it reads
    /// <c>-cfoo</c> as <c>-c foo</c>, which no reserved flag would catch. So a profile's arguments are flags, each value
    /// joined to its flag by <c>=</c>.
    /// </summary>
    [Theory]
    [InlineData("--model=made-up-model")]
    [InlineData("-m=made-up-model")]
    [InlineData("--oss")]
    [InlineData("--color=never")]
    [InlineData("--model=made-up-model", "--color=never")]
    public void Arguments_that_are_flags_with_their_values_joined_are_taken(params string[] args)
    {
        Assert.Null(Host().ArgsProblem(args));
    }

    [Theory]
    [InlineData("--model", "made-up-model")]
    [InlineData("say hello")]
    [InlineData("resume")]
    [InlineData("-cweb_search=\"live\"")]
    [InlineData("-mmade-up-model")]
    [InlineData("--")]
    [InlineData("-")]
    [InlineData(" --model=made-up-model")]
    public void Arguments_the_host_would_read_as_a_prompt_or_another_flag_are_refused(params string[] args)
    {
        Assert.False(string.IsNullOrWhiteSpace(Host().ArgsProblem(args)));
    }

    [Fact]
    public void An_end_without_a_report_is_read_as_any_other()
    {
        Assert.Null(Host().Unavailable(1, "Error: unexpected status 401 Unauthorized"));
    }

    private static ExecutionProfileRevision Revision(string[]? args = null, string[]? deny = null, AgentTool[]? tools = null) => new()
    {
        Number = 1,
        Host = AgentHostKind.Codex,
        Program = "codex",
        Args = [.. args ?? []],
        Deny = [.. deny ?? []],
        Tools = [.. tools ?? []],
    };
}
