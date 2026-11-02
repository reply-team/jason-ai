using Jason.Contracts.Api;
using Jason.Contracts.Execution;
using Jason.Contracts.Hosts;
using Jason.Runtime.Execution.Hosts;
using Jason.Runtime.Persistence;

namespace Jason.Runtime.Tests.Execution;

/// <summary>
/// The shape one headless Cursor CLI session is started with, and where the program that session runs is on a Windows
/// machine. The shape is composed by the runtime and a profile adds to the end of it; the program is a file that is
/// found by the rule the host's own launcher script uses, since the shim on the search path is a script and a script
/// is never started through a shell.
/// </summary>
public class CursorCliHostTests : IDisposable
{
    private static readonly MemoryFileLocations NoFiles = new(null, null, null);

    private readonly string _directory = Path.Combine(Path.GetTempPath(), "jason-tests", Guid.NewGuid().ToString("N"));

    public CursorCliHostTests() => Directory.CreateDirectory(_directory);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    [Fact]
    public void The_host_is_the_cursor_cli_with_the_command_word_agent_and_reads_agents_md_by_itself()
    {
        IAgentHost host = new CursorCliHost();
        var facts = host.Facts;

        Assert.Equal(AgentHostKind.CursorCli, host.Kind);
        Assert.Equal("agent", facts.CommandWord);
        Assert.Equal(".cursor/cli-config.json", facts.HomeMarker);
        Assert.Equal(".agents/skills", facts.HomeSkills);
        Assert.Equal(".agents/skills", facts.WorkSkills);
        Assert.Null(facts.Instructions);
    }

    [Fact]
    public void The_session_is_headless_streams_what_it_does_is_trusted_and_has_a_session_of_its_own()
    {
        var launch = new CursorCliHost().Compose(Revision("--model", "made-up-model"), ["agent"], NoFiles);

        string[] expected =
            [
                "agent",
                "-p",
                "--output-format",
                "stream-json",
                "--trust",
                "--new-session-id",
                launch.SessionId!,
                "--model",
                "made-up-model",
            ];
        Assert.Equal(expected, launch.Command);
        Assert.True(Guid.TryParseExact(launch.SessionId, "D", out var id));
        Assert.Equal(4, id.ToString("D")[14] - '0');
    }

    /// <summary>
    /// The brief goes on stdin. With no word for a prompt on the command line and stdin not a terminal, the host reads
    /// the whole of stdin as the prompt; a word on the command line makes it ignore stdin altogether, so the one thing
    /// no composed command may carry is a bare word that is not the value of a flag.
    /// </summary>
    [Fact]
    public void No_word_of_the_composed_command_after_the_launch_is_a_prompt()
    {
        string[] launch = ["/opt/agent/node", "/opt/agent/index.js"];

        var composed = new CursorCliHost().Compose(Revision(), launch, NoFiles).Command.Skip(launch.Length).ToArray();

        for (var index = 0; index < composed.Length; index++)
        {
            if (composed[index].StartsWith('-'))
            {
                continue;
            }

            Assert.True(
                index > 0 && composed[index - 1] is "--output-format" or "--new-session-id",
                $"'{composed[index]}' is a bare word, which the host would read as the prompt and so ignore stdin.");
        }
    }

    [Fact]
    public void Every_attempt_has_a_fresh_session_and_the_command_names_it()
    {
        var host = new CursorCliHost();

        var first = host.Compose(Revision(), ["agent"], NoFiles);
        var second = host.Compose(Revision(), ["agent"], NoFiles);

        Assert.NotEqual(first.SessionId, second.SessionId);
        Assert.Contains(first.SessionId!, first.Command);
        Assert.DoesNotContain(first.SessionId!, second.Command);
    }

    [Theory]
    [InlineData("-p")]
    [InlineData("--print")]
    [InlineData("--output-format")]
    [InlineData("--stream-partial-output")]
    [InlineData("-f")]
    [InlineData("--force")]
    [InlineData("--yolo")]
    [InlineData("--auto-review")]
    [InlineData("--trust")]
    [InlineData("--sandbox")]
    [InlineData("--approve-mcps")]
    [InlineData("--workspace")]
    [InlineData("--add-dir")]
    [InlineData("--mode")]
    [InlineData("--plan")]
    [InlineData("--resume")]
    [InlineData("--continue")]
    [InlineData("--new-session-id")]
    [InlineData("--exclude-tools")]
    [InlineData("--api-key")]
    [InlineData("--auth-token")]
    [InlineData("-H")]
    [InlineData("--header")]
    [InlineData("-e")]
    [InlineData("--endpoint")]
    [InlineData("-w")]
    [InlineData("--worktree")]
    [InlineData("--worktree-base")]
    [InlineData("--skip-worktree-setup")]
    [InlineData("--plugin-dir")]
    [InlineData("--disable-project-configs")]
    [InlineData("--statsig-overrides")]
    public void A_flag_that_makes_the_session_interactive_unconfined_or_foreign_is_reserved(string flag)
    {
        Assert.Contains(flag, new CursorCliHost().ReservedFlags);
    }

    [Fact]
    public void A_model_is_not_reserved()
    {
        Assert.DoesNotContain("--model", new CursorCliHost().ReservedFlags);
    }

    [Theory]
    [InlineData("not signed in", "Error: Authentication required. Please run 'agent login' first, or set CURSOR_API_KEY environment variable.")]
    [InlineData("a stored sign-in the server refused", "Your stored authentication is invalid. Please log in again.")]
    public void An_end_that_says_the_account_is_not_signed_in_is_a_fixed_sentence(string why, string said)
    {
        Assert.NotNull(why);

        var sentence = new CursorCliHost().Unavailable(1, said);

        Assert.Equal("Cursor CLI is not signed in on this account: run `agent login` as yourself.", sentence);
    }

    [Fact]
    public void An_address_in_the_tail_is_never_carried_into_the_sentence()
    {
        var sentence = new CursorCliHost().Unavailable(
            1, "Authentication required for person@example.test. Please run 'agent login' first.");

        Assert.NotNull(sentence);
        Assert.DoesNotContain("example.test", sentence, StringComparison.Ordinal);
        Assert.DoesNotContain("person@", sentence, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(0, "Please run 'agent login' first")]
    [InlineData(1, "Error: the model is overloaded")]
    [InlineData(1, "")]
    [InlineData(1, null)]
    public void Any_other_end_says_nothing_of_the_host(int exit, string? tail)
    {
        Assert.Null(new CursorCliHost().Unavailable(exit, tail));
    }

    // ---------------------------------------------------------------------------------------------------------
    // Where the program is on Windows: the layout the host's own launcher script reads.
    // ---------------------------------------------------------------------------------------------------------

    [Theory]
    [InlineData("agent.cmd")]
    [InlineData("cursor-agent.cmd")]
    [InlineData("AGENT.CMD")]
    [InlineData("Cursor-Agent.Cmd")]
    public void The_two_launcher_names_the_host_installs_are_its_own(string fileName)
    {
        Assert.True(new CursorCliHost(windows: true).NamesItsLauncher(fileName));
    }

    [Theory]
    [InlineData("agent.ps1")]
    [InlineData("claude.cmd")]
    [InlineData("agent.cmd.exe")]
    [InlineData("promote-staged.cmd")]
    [InlineData("agent")]
    [InlineData("")]
    public void No_other_script_is_the_hosts_launcher(string fileName)
    {
        Assert.False(new CursorCliHost(windows: true).NamesItsLauncher(fileName));
    }

    [Fact]
    public void Off_Windows_no_file_is_a_launcher()
    {
        Assert.False(new CursorCliHost(windows: false).NamesItsLauncher("agent.cmd"));
    }

    [Fact]
    public void The_newest_version_directory_by_its_date_is_the_one_the_launcher_starts()
    {
        var root = InstallRoot("2025.12.31-aaaaaaa", "2026.9.5-bbbbbbb", "2026.10.22-27dcf5f");

        var layout = new CursorCliHost(windows: true).ProgramLayout(Path.Combine(root, "agent.cmd"));

        Assert.Equal(
            [Path.Combine(root, "versions", "2026.10.22-27dcf5f", "node.exe"), Path.Combine(root, "versions", "2026.10.22-27dcf5f", "index.js")],
            layout);
    }

    [Fact]
    public void A_date_is_compared_as_a_number_so_a_month_written_with_one_digit_is_older_than_one_written_with_two()
    {
        var root = InstallRoot("2026.9.5-bbbbbbb", "2026.10.1-ccccccc");

        var layout = new CursorCliHost(windows: true).ProgramLayout(Path.Combine(root, "cursor-agent.cmd"));

        Assert.Equal(Path.Combine(root, "versions", "2026.10.1-ccccccc", "node.exe"), layout![0]);
    }

    [Fact]
    public void A_staged_or_previous_directory_and_a_name_that_is_no_version_are_never_chosen()
    {
        var root = InstallRoot("2026.10.22-27dcf5f", "2026.10.23-ccccccc.new", "2026.10.24.previous", "2026.10.25", "latest");

        var layout = new CursorCliHost(windows: true).ProgramLayout(Path.Combine(root, "agent.cmd"));

        Assert.Equal(Path.Combine(root, "versions", "2026.10.22-27dcf5f", "node.exe"), layout![0]);
    }

    [Fact]
    public void A_version_with_a_time_in_its_name_is_one_the_launcher_reads()
    {
        var root = InstallRoot("2026.10.22-27dcf5f", "2026.10.23-11-22-33-ddddddd");

        var layout = new CursorCliHost(windows: true).ProgramLayout(Path.Combine(root, "agent.cmd"));

        Assert.Equal(Path.Combine(root, "versions", "2026.10.23-11-22-33-ddddddd", "node.exe"), layout![0]);
    }

    [Fact]
    public void Two_versions_of_one_day_are_ordered_by_their_names_so_the_answer_does_not_depend_on_the_listing()
    {
        var root = InstallRoot("2026.10.22-aaaaaaa", "2026.10.22-fffffff", "2026.10.22-bbbbbbb");

        var layout = new CursorCliHost(windows: true).ProgramLayout(Path.Combine(root, "agent.cmd"));

        Assert.Equal(Path.Combine(root, "versions", "2026.10.22-fffffff", "node.exe"), layout![0]);
    }

    [Fact]
    public void A_node_and_an_entry_script_beside_the_launcher_are_what_the_launcher_starts_first()
    {
        var root = InstallRoot("2026.10.22-27dcf5f");
        File.WriteAllText(Path.Combine(root, "node.exe"), string.Empty);
        File.WriteAllText(Path.Combine(root, "index.js"), string.Empty);

        var layout = new CursorCliHost(windows: true).ProgramLayout(Path.Combine(root, "agent.cmd"));

        Assert.Equal([Path.Combine(root, "node.exe"), Path.Combine(root, "index.js")], layout);
    }

    [Fact]
    public void A_node_beside_the_launcher_without_an_entry_script_is_not_the_flat_form()
    {
        var root = InstallRoot("2026.10.22-27dcf5f");
        File.WriteAllText(Path.Combine(root, "node.exe"), string.Empty);

        var layout = new CursorCliHost(windows: true).ProgramLayout(Path.Combine(root, "agent.cmd"));

        Assert.Equal(Path.Combine(root, "versions", "2026.10.22-27dcf5f", "node.exe"), layout![0]);
    }

    [Fact]
    public void The_newest_directory_is_the_only_one_tried_and_without_an_entry_script_there_is_no_answer()
    {
        var root = InstallRoot("2026.10.22-27dcf5f", "2026.10.23-ccccccc");
        File.Delete(Path.Combine(root, "versions", "2026.10.23-ccccccc", "index.js"));

        Assert.Null(new CursorCliHost(windows: true).ProgramLayout(Path.Combine(root, "agent.cmd")));
    }

    [Fact]
    public void Without_the_launchers_own_script_beside_it_there_is_no_answer()
    {
        var root = InstallRoot("2026.10.22-27dcf5f");
        File.Delete(Path.Combine(root, "cursor-agent.ps1"));

        Assert.Null(new CursorCliHost(windows: true).ProgramLayout(Path.Combine(root, "agent.cmd")));
    }

    [Fact]
    public void A_script_the_host_did_not_install_has_no_layout()
    {
        var root = InstallRoot("2026.10.22-27dcf5f");
        File.WriteAllText(Path.Combine(root, "claude.cmd"), string.Empty);

        Assert.Null(new CursorCliHost(windows: true).ProgramLayout(Path.Combine(root, "claude.cmd")));
    }

    [Fact]
    public void A_version_installed_after_the_first_answer_is_the_answer_the_next_time()
    {
        var root = InstallRoot("2026.10.22-27dcf5f");
        var host = new CursorCliHost(windows: true);
        var shim = Path.Combine(root, "agent.cmd");
        Assert.Equal(Path.Combine(root, "versions", "2026.10.22-27dcf5f", "node.exe"), host.ProgramLayout(shim)![0]);

        AddVersion(root, "2026.11.3-eeeeeee");

        Assert.Equal(Path.Combine(root, "versions", "2026.11.3-eeeeeee", "node.exe"), host.ProgramLayout(shim)![0]);
    }

    [Fact]
    public void Off_Windows_there_is_no_layout()
    {
        var root = InstallRoot("2026.10.22-27dcf5f");

        Assert.Null(new CursorCliHost(windows: false).ProgramLayout(Path.Combine(root, "agent.cmd")));
    }

    // ---------------------------------------------------------------------------------------------------------

    /// <summary>An install root as the Windows installer makes one: both launchers, the script they run, and versions.</summary>
    private string InstallRoot(params string[] versions)
    {
        var root = Path.Combine(_directory, "cursor-agent");
        Directory.CreateDirectory(Path.Combine(root, "versions"));
        foreach (var file in new[] { "agent.cmd", "cursor-agent.cmd", "cursor-agent.ps1" })
        {
            File.WriteAllText(Path.Combine(root, file), string.Empty);
        }

        foreach (var version in versions)
        {
            AddVersion(root, version);
        }

        return root;
    }

    private static void AddVersion(string root, string version)
    {
        var directory = Path.Combine(root, "versions", version);
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "node.exe"), string.Empty);
        File.WriteAllText(Path.Combine(directory, "index.js"), string.Empty);
    }

    private static ExecutionProfileRevision Revision(params string[] args) => new()
    {
        Number = 1,
        Host = AgentHostKind.CursorCli,
        Program = "agent",
        Args = [.. args],
    };
}
