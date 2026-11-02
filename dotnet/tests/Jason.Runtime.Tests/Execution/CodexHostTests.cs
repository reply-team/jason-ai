using Jason.Contracts.Api;
using Jason.Contracts.Execution;
using Jason.Runtime.Execution.Hosts;
using Jason.Runtime.Persistence;

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
    private const string Data = "/home/someone/.jason";

    private const string Executable = "/opt/jason";

    private const string Extraction = "/tmp/.net";

    private static readonly MemoryFileLocations NoFiles = new(null, null, null);

    private static CodexHost Host(bool windows = false) => new(Data, Executable, Extraction, windows);

    [Fact]
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
                "-c", "permissions.jason.filesystem={\":minimal\"=\"read\", \":workspace_roots\"=\"read\", \"/opt/jason\"=\"read\", \"/tmp/.net\"=\"read\", \"/home/someone/.jason/run\"=\"read\"}",
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
    public void A_web_search_the_person_granted_is_live_and_otherwise_off()
    {
        var granted = Host().Compose(Revision(tools: [AgentTool.WebSearch]), ["codex"], NoFiles).Command;
        var withheld = Host().Compose(Revision(), ["codex"], NoFiles).Command;

        Assert.Contains("web_search=\"live\"", granted);
        Assert.DoesNotContain("web_search=\"disabled\"", granted);
        Assert.Contains("web_search=\"disabled\"", withheld);
    }

    [Fact]
    public void Each_memory_file_the_launch_names_is_readable_once_and_nothing_else_of_its_folder()
    {
        var files = new MemoryFileLocations("/home/someone/Jason/AGENTS.md", "/home/someone/Jason/acme/AGENTS.md", "/home/someone/Jason/AGENTS.md");

        var launch = Host().Compose(Revision(), ["codex"], files);

        Assert.Contains(
            "permissions.jason.filesystem={\":minimal\"=\"read\", \":workspace_roots\"=\"read\", \"/opt/jason\"=\"read\", \"/tmp/.net\"=\"read\", \"/home/someone/.jason/run\"=\"read\", \"/home/someone/Jason/AGENTS.md\"=\"read\", \"/home/someone/Jason/acme/AGENTS.md\"=\"read\"}",
            launch.Command);
        Assert.Empty(launch.Unreadable);
    }

    /// <summary>
    /// A path is a TOML string: a backslash, a quote and a control character are escaped, so a Windows path and a folder
    /// named with a quote stay the one path they are rather than ending the value early.
    /// </summary>
    [Fact]
    public void A_windows_path_and_a_quote_in_a_folder_name_are_escaped_as_toml()
    {
        var host = new CodexHost(@"C:\Users\someone\.jason", @"C:\Program Files\Jason", @"C:\Users\someone\AppData\Local\Temp\.net", windows: true);
        var files = new MemoryFileLocations(@"C:\Users\someone\Jason\The ""Big"" One\AGENTS.md", null, null);

        var command = host.Compose(Revision(), ["codex"], files).Command;

        Assert.Contains(
            "permissions.jason.filesystem={\":minimal\"=\"read\", \":workspace_roots\"=\"read\", \"C:\\\\Program Files\\\\Jason\"=\"read\", \"C:\\\\Users\\\\someone\\\\AppData\\\\Local\\\\Temp\\\\.net\"=\"read\", \"C:\\\\Users\\\\someone\\\\.jason\\\\run\"=\"read\", \"C:\\\\Users\\\\someone\\\\Jason\\\\The \\\"Big\\\" One\\\\AGENTS.md\"=\"read\"}",
            command);
    }

    /// <summary>
    /// A file whose path holds a character the sandbox reads as a pattern cannot be named as one file, so its level is
    /// unreadable and no wider rule is guessed at, as on every host.
    /// </summary>
    [Fact]
    public void A_memory_file_whose_path_reads_as_a_pattern_is_left_unreadable()
    {
        var files = new MemoryFileLocations("/home/someone/Jason/AGENTS.md", "/home/someone/Jason/[draft]/AGENTS.md", null);

        var launch = Host().Compose(Revision(), ["codex"], files);

        Assert.Equal(["project"], launch.Unreadable);
        Assert.DoesNotContain(launch.Command, word => word.Contains("[draft]", StringComparison.Ordinal));
    }

    [Fact]
    public void A_path_the_profile_denies_reading_is_denied_in_the_sandbox()
    {
        var revision = Revision(deny: ["Read(//srv/secrets/**)", "Read(~/private/notes.md)", "Write", "WebFetch"]);

        var command = new CodexHost(Data, Executable, Extraction, windows: false, home: "/home/someone").Compose(revision, ["codex"], NoFiles).Command;

        Assert.Contains(
            "permissions.jason.filesystem={\":minimal\"=\"read\", \":workspace_roots\"=\"read\", \"/opt/jason\"=\"read\", \"/tmp/.net\"=\"read\", \"/home/someone/.jason/run\"=\"read\", \"/srv/secrets/**\"=\"deny\", \"/home/someone/private/notes.md\"=\"deny\"}",
            command);
    }

    [Fact]
    public void A_windows_path_in_the_profiles_deny_names_its_drive()
    {
        var revision = Revision(deny: ["Read(//d/Shared/Finance/**)"]);

        var command = new CodexHost(@"C:\j", @"C:\x", @"C:\t", windows: true).Compose(revision, ["codex"], NoFiles).Command;

        Assert.Contains(command, word => word.Contains("\"D:/Shared/Finance/**\"=\"deny\"", StringComparison.Ordinal));
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

    [Fact]
    public void The_profiles_arguments_come_last()
    {
        var command = Host().Compose(Revision(args: ["--model=made-up-model"]), ["/usr/bin/node", "/usr/lib/codex.js"], NoFiles).Command;

        Assert.Equal(["/usr/bin/node", "/usr/lib/codex.js", "exec"], command.Take(3));
        Assert.Equal("--model=made-up-model", command[^1]);
    }

    /// <summary>
    /// Every flag that would change the shape above or undo the sandbox is the runtime's; a model is the person's. The
    /// list was read from <c>codex exec --help</c> of 0.166.0-alpha.5.1.
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
    [InlineData("--dangerously-bypass-approvals-and-sandbox")]
    [InlineData("--dangerously-bypass-hook-trust")]
    [InlineData("-C")]
    [InlineData("--cd")]
    [InlineData("--add-dir")]
    [InlineData("--worktree")]
    [InlineData("--ignore-user-config")]
    [InlineData("--ignore-rules")]
    [InlineData("--strict-config")]
    [InlineData("--json")]
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
    public void The_work_directory_is_left_as_the_runtime_made_it()
    {
        var workDir = Directory.CreateTempSubdirectory("jason-codex-").FullName;
        try
        {
            Host().WriteWorkDirectory(workDir, ["Write", "web_fetch"]);

            Assert.Empty(Directory.EnumerateFileSystemEntries(workDir));
        }
        finally
        {
            Directory.Delete(workDir, recursive: true);
        }
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
