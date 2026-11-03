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
    [Critical(Family.S6, "the composed command, exactly")]
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
    [Critical(Family.W7, "the brief goes on stdin and no word of it on the command line")]
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
    // Critical: a profile cannot allow every command.
    [InlineData("--yolo", Traits = ["Category", "Critical", "CriticalFamily", nameof(Family.S6)])]
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

    [Theory]
    [InlineData("--base-url")]
    [InlineData("--local-agent-api-key")]
    [InlineData("--enable-bedrock")]
    [InlineData("--enable-local-bedrock")]
    [InlineData("--authless")]
    [InlineData("--agent-endpoint")]
    [InlineData("--website-url")]
    [InlineData("--repo-endpoint")]
    public void A_flag_that_hands_the_session_another_account_provider_or_endpoint_is_reserved(string flag)
    {
        Assert.Contains(flag, new CursorCliHost().ReservedFlags);
    }

    [Theory]
    [InlineData("-k")]
    [InlineData("--insecure")]
    [InlineData("--repo-insecure")]
    public void A_flag_that_loosens_the_hosts_transport_security_is_reserved(string flag)
    {
        Assert.Contains(flag, new CursorCliHost().ReservedFlags);
    }

    [Theory]
    [InlineData("--allowed-tools")]
    [InlineData("--system-prompt")]
    [InlineData("--harness")]
    [InlineData("--exclude-workspace-context")]
    [InlineData("--conversation-history-file")]
    [InlineData("--data-dir")]
    public void A_flag_that_hands_the_session_another_tool_set_prompt_history_or_directory_is_reserved(string flag)
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
    [Critical(Family.S3, "the not-signed-in sentence is fixed and carries none of the host's words")]
    public void An_end_that_says_the_account_is_not_signed_in_is_a_fixed_sentence(string why, string said)
    {
        Assert.NotNull(why);

        var sentence = new CursorCliHost().Unavailable(1, said);

        Assert.Equal("Cursor CLI is not signed in on this account: run `agent login` as yourself.", sentence);
    }

    /// <summary>
    /// The bundle prints the sign-in command in three forms, by how the host was started: its own word, the editor's
    /// word with it, and the local-mode word. Each is the same fixed sentence.
    /// </summary>
    [Theory]
    [InlineData("Authentication required. Please run 'agent login' first.")]
    [InlineData("Authentication required. Please run 'cursor agent login' first.")]
    [InlineData("Authentication required. Please run 'agent-local login' first.")]
    public void The_sign_in_command_is_read_in_each_of_the_three_forms_the_host_prints_it(string said)
    {
        var sentence = new CursorCliHost().Unavailable(1, said);

        Assert.Equal("Cursor CLI is not signed in on this account: run `agent login` as yourself.", sentence);
    }

    [Fact]
    [Critical(Family.S3, "nothing of the host's tail reaches the sentence")]
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

    [Fact]
    public void A_session_is_told_to_the_host_and_recorded()
    {
        IAgentHost host = new CursorCliHost();
        Assert.True(host.TakesSessionId);
    }

    [Fact]
    [Critical(Family.S6, "writing is denied wholly, and a fetch unless granted")]
    public void Writing_is_denied_wholly_and_a_fetch_is_denied_unless_the_revision_grants_it()
    {
        var host = new CursorCliHost();

        Assert.Equal(["Write(**)", "WebFetch(*)"], host.Compose(Revision(), ["agent"], NoFiles).Deny);
        Assert.Equal(["Write(**)"], host.Compose(Revision(AgentTool.WebFetch), ["agent"], NoFiles).Deny);
    }

    [Fact]
    [Critical(Family.S6, "the launched session's allow list, exactly")]
    public void The_callback_the_memory_files_and_a_granted_fetch_are_allowed_and_nothing_else()
    {
        var files = new MemoryFileLocations("C:\\Jason\\AGENTS.md", null, "C:\\Jason\\campaigns\\acme\\AGENTS.md");

        var launch = new CursorCliHost(windows: true).Compose(Revision(AgentTool.WebFetch), ["agent"], files);

        Assert.Equal(
            [$"Shell({ProgramResolver.DefaultCliCommand})", "Read(C:/Jason/AGENTS.md)", "Read(C:/Jason/campaigns/acme/AGENTS.md)", "WebFetch(*)"],
            launch.Allow);
        Assert.Empty(launch.Unreadable);
    }

    [Fact]
    public void The_callback_word_is_the_profiles_when_it_names_one()
    {
        var revision = Revision();
        revision.CliCommand = "jason-dev";

        Assert.Equal(["Shell(jason-dev)"], new CursorCliHost().Compose(revision, ["agent"], NoFiles).Allow);
    }

    [Theory]
    [InlineData("C:\\Jason\\Acme (EU)\\AGENTS.md")]
    [InlineData("C:\\Jason\\notes[1]\\AGENTS.md")]
    [InlineData("C:\\Jason\\what?\\AGENTS.md")]
    [InlineData("C:\\Jason\\*\\AGENTS.md")]
    [InlineData("C:\\Jason\\{a,b}\\AGENTS.md")]
    [InlineData("C:\\Jason\\{x}\\AGENTS.md")]
    public void A_memory_file_whose_path_the_host_would_read_as_a_pattern_is_unreadable_and_says_so(string path)
    {
        var launch = new CursorCliHost(windows: true).Compose(Revision(), ["agent"], new MemoryFileLocations(null, path, null));

        Assert.Equal(["project"], launch.Unreadable);
        Assert.Equal([$"Shell({ProgramResolver.DefaultCliCommand})"], launch.Allow);
    }

    [Fact]
    public void A_file_named_at_two_levels_is_one_rule()
    {
        var files = new MemoryFileLocations("C:\\Jason\\AGENTS.md", "C:\\Jason\\AGENTS.md", null);

        Assert.Equal(
            [$"Shell({ProgramResolver.DefaultCliCommand})", "Read(C:/Jason/AGENTS.md)"],
            new CursorCliHost(windows: true).Compose(Revision(), ["agent"], files).Allow);
    }

    [Fact]
    public void A_fetch_has_a_rule_and_a_search_has_none()
    {
        var host = new CursorCliHost();

        Assert.Equal("WebFetch(*)", host.ToolRule(AgentTool.WebFetch));
        Assert.Null(host.ToolRule(AgentTool.WebSearch));
    }

    [Fact]
    public void A_grant_of_web_search_is_one_jason_cannot_make_true_because_the_persons_own_setting_decides_it()
    {
        var host = new CursorCliHost();

        Assert.Contains("autoAcceptWebSearch", host.GrantProblem(AgentTool.WebSearch), StringComparison.Ordinal);
        Assert.Null(host.GrantProblem(AgentTool.WebFetch));
    }

    [Theory]
    [InlineData("Shell(rm)")]
    [InlineData("Read(**/.env)")]
    [InlineData("Write(**)")]
    [InlineData("WebFetch(*)")]
    [InlineData("WebFetch(domain:example.test)")]
    [InlineData("Mcp(server:tool)")]
    [InlineData("Write")]
    public void The_five_heads_the_host_reads_are_written_as_they_were_given(string word)
    {
        Assert.Null(new CursorCliHost().DenyProblem(word));
    }

    [Theory]
    [InlineData("WebSearch")]
    [InlineData("web_search")]
    [InlineData("WebSearch(*)")]
    [InlineData("Web-Search")]
    public void A_deny_of_web_search_in_either_spelling_is_refused_because_the_host_has_no_way_to_deny_it(string word)
    {
        var problem = new CursorCliHost().DenyProblem(word);

        Assert.NotNull(problem);
        Assert.Contains("autoAcceptWebSearch", problem, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Bash(rm *)")]
    [InlineData("PowerShell(rm)")]
    [InlineData("shell(rm)")]
    [InlineData("Delete")]
    [InlineData("any words at all")]
    [InlineData("Shell(rm")]
    [InlineData("")]
    public void A_deny_that_is_none_of_the_five_heads_is_refused(string word)
    {
        Assert.NotNull(new CursorCliHost().DenyProblem(word));
    }

    [Theory]
    [InlineData("--model=made-up-model")]
    [InlineData("--model=made-up-model", "--list-models")]
    public void Flags_with_their_values_joined_by_an_equals_sign_are_taken(params string[] args)
    {
        Assert.Null(new CursorCliHost().ArgsProblem(args));
    }

    [Theory]
    [InlineData("--model", "made-up-model")]
    [InlineData("summarise the account")]
    [InlineData("--")]
    [InlineData("--model=x", "bare")]
    public void A_bare_word_or_a_double_dash_is_refused_because_a_word_would_be_the_prompt_and_stdin_would_be_ignored(params string[] args)
    {
        var problem = new CursorCliHost().ArgsProblem(args);

        Assert.NotNull(problem);
        Assert.Contains("stdin", problem, StringComparison.Ordinal);
    }

    /// <summary>
    /// The host's parser expands a single-dash word of several letters into one flag per letter, and reads the text after
    /// a short flag that takes a value as that value, so <c>-fp</c> is <c>-f -p</c> and <c>-ehttp://127.0.0.1:9</c> is
    /// <c>-e</c> with an address. A lone dash is read as the prompt. None of them is a word a person could mean, and a
    /// reserved short flag written inside one would slip past a check of whole words.
    /// </summary>
    [Theory]
    [InlineData("-fp")]
    [InlineData("-pf")]
    [InlineData("-ehttp://127.0.0.1:9")]
    [InlineData("-")]
    [InlineData("--model=x", "-fp")]
    public void A_single_dash_word_of_more_than_one_letter_and_a_lone_dash_are_refused(params string[] args)
    {
        var problem = new CursorCliHost().ArgsProblem(args);

        Assert.NotNull(problem);
    }

    [Fact]
    public void No_arguments_are_taken_without_a_problem()
    {
        Assert.Null(new CursorCliHost().ArgsProblem([]));
    }

    /// <summary>
    /// The host reads a project's permissions from <c>.cursor/cli.json</c> in the directory it works in, and it unions
    /// the allow and deny lists of every layer it finds, so what the work directory writes is added to the person's own
    /// and never replaces it. The file is the whole of what is written: the permissions, the file's version key and the editor mode set off, nothing else.
    /// </summary>
    [Fact]
    [Critical(Family.S6, "the permission file is the rules and nothing else")]
    public void The_work_directory_carries_the_allow_and_deny_rules_in_the_hosts_project_permission_file()
    {
        new CursorCliHost().WriteWorkDirectory(_directory, ["Shell(jason)", "Read(C:/Jason/AGENTS.md)"], ["Write(**)", "WebFetch(*)", "Shell(rm)"]);

        var written = File.ReadAllText(Path.Combine(_directory, ".cursor", "cli.json"));
        var document = System.Text.Json.Nodes.JsonNode.Parse(written)!.AsObject();
        Assert.Equal(1, (int)document["version"]!);
        Assert.False((bool)document["editor"]!["vimMode"]!);
        Assert.Equal(["Shell(jason)", "Read(C:/Jason/AGENTS.md)"], document["permissions"]!["allow"]!.AsArray().Select(rule => (string)rule!));
        Assert.Equal(["Write(**)", "WebFetch(*)", "Shell(rm)"], document["permissions"]!["deny"]!.AsArray().Select(rule => (string)rule!));
        Assert.Equal(["editor", "permissions", "version"], document.Select(property => property.Key).Order());
        Assert.Equal(["cli.json"], Directory.GetFiles(Path.Combine(_directory, ".cursor")).Select(Path.GetFileName));
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
    public void A_date_is_compared_as_a_number_so_within_one_year_a_month_written_with_one_digit_is_older_than_one_written_with_two()
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

    /// <summary>
    /// The launcher script matches a name by letters and digits of its own alphabet, where a digit of another script would
    /// read as a digit to a looser rule and then fail to parse as a number: such a name is no version.
    /// </summary>
    [Fact]
    public void A_directory_named_with_digits_of_another_script_is_no_version()
    {
        var root = InstallRoot("2026.10.22-27dcf5f", "٢٠٢٦.10.23-ccccccc");

        var layout = new CursorCliHost(windows: true).ProgramLayout(Path.Combine(root, "agent.cmd"));

        Assert.Equal(Path.Combine(root, "versions", "2026.10.22-27dcf5f", "node.exe"), layout![0]);
    }

    [Fact]
    public void A_hash_in_capital_letters_is_still_a_version_as_the_launcher_script_reads_it()
    {
        var root = InstallRoot("2026.10.22-27dcf5f", "2026.11.01-ABC");

        var layout = new CursorCliHost(windows: true).ProgramLayout(Path.Combine(root, "agent.cmd"));

        Assert.Equal(Path.Combine(root, "versions", "2026.11.01-ABC", "node.exe"), layout![0]);
    }

    [Fact]
    public void A_launcher_that_is_not_there_has_no_layout_though_its_script_and_versions_are()
    {
        var root = InstallRoot("2026.10.22-27dcf5f");
        File.Delete(Path.Combine(root, "agent.cmd"));

        Assert.Null(new CursorCliHost(windows: true).ProgramLayout(Path.Combine(root, "agent.cmd")));
        Assert.NotNull(new CursorCliHost(windows: true).ProgramLayout(Path.Combine(root, "cursor-agent.cmd")));
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

    private static ExecutionProfileRevision Revision(AgentTool tool) => new()
    {
        Number = 1,
        Host = AgentHostKind.CursorCli,
        Program = "agent",
        Tools = [tool],
    };
}
