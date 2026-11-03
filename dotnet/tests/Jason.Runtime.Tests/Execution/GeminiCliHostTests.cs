using System.Text.Json.Nodes;
using Jason.Contracts.Api;
using Jason.Contracts.Execution;
using Jason.Contracts.Hosts;
using Jason.Runtime.Execution.Hosts;
using Jason.Runtime.Persistence;

namespace Jason.Runtime.Tests.Execution;

/// <summary>
/// Gemini CLI as a host, from its documentation and source for version 0.66.0 (nothing here reaches an account): the
/// brief on standard input and nothing of the envelope on the command line; an approval mode that asks nobody, so a
/// call the policy does not allow is refused; every rule at the highest policy tier, where a person's own allow rules
/// cannot widen it; and no server, extension or hook of the person's in the run.
/// </summary>
public class GeminiCliHostTests
{
    private static readonly string Root = Path.Combine(Path.GetTempPath(), "jason-gemini-host");

    private static readonly string Client = Path.Combine(Root, "harness", "AGENTS.md");
    private static readonly string Project = Path.Combine(Root, "harness", "projects", "acme", "AGENTS.md");
    private static readonly string Campaign = Path.Combine(Root, "harness", "projects", "acme", "campaigns", "spring", "AGENTS.md");

    private readonly IAgentHost _host = new GeminiCliHost();

    private static ExecutionProfileRevision Revision(string[]? args = null, AgentTool[]? tools = null, string[]? deny = null, string? cli = null) => new()
    {
        Number = 1,
        Host = AgentHostKind.GeminiCli,
        Program = "gemini",
        Args = [.. args ?? []],
        Tools = [.. tools ?? []],
        Deny = [.. deny ?? []],
        CliCommand = cli,
    };

    private HostLaunch Compose(ExecutionProfileRevision? revision = null, MemoryFileLocations? files = null) =>
        _host.Compose(revision ?? Revision(), ["gemini"], files ?? new MemoryFileLocations(null, null, null));

    [Fact]
    public void It_is_the_host_of_its_row_and_takes_a_session_the_runtime_mints()
    {
        Assert.Equal(AgentHostKind.GeminiCli, _host.Kind);
        Assert.Same(AgentHosts.Of(AgentHostKind.GeminiCli), _host.Facts);
        Assert.True(_host.TakesSessionId);
    }

    /// <summary>
    /// A non-interactive run with no prompt flag reads the whole of standard input as the prompt, and a word on the
    /// command line is appended to it. So the command carries no prompt flag and no bare word, and the brief goes on
    /// standard input alone.
    /// </summary>
    [Fact]
    public void The_command_carries_no_prompt_and_asks_for_a_stream_without_asking_anyone()
    {
        var composed = Compose().Command;

        Assert.Equal("gemini", composed[0]);
        Assert.DoesNotContain("-p", composed);
        Assert.DoesNotContain(composed, word => word.StartsWith("--prompt", StringComparison.Ordinal));
        Assert.All(composed.Skip(1), word => Assert.StartsWith("-", word, StringComparison.Ordinal));
        Assert.Contains("--output-format=stream-json", composed);
        Assert.Contains("--approval-mode=default", composed);
        Assert.Contains("--skip-trust", composed);
        Assert.DoesNotContain("--yolo", composed);
        Assert.DoesNotContain(composed, word => word.Contains("yolo", StringComparison.OrdinalIgnoreCase) || word.Contains("auto_edit", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void The_session_is_the_runtimes_own_and_fresh_each_time()
    {
        var first = Compose();
        var second = Compose();

        Assert.True(Guid.TryParse(first.SessionId, out _));
        Assert.Contains($"--session-id={first.SessionId}", first.Command);
        Assert.NotEqual(first.SessionId, second.SessionId);
    }

    /// <summary>
    /// The person's own servers, extensions and hooks are not this role's. The policy is the highest tier's and a
    /// relative directory, since the command is composed knowing no work directory and the host runs in it.
    /// </summary>
    [Fact]
    public void The_run_has_no_server_no_extension_and_the_policy_of_the_work_directory_at_the_highest_tier()
    {
        var composed = Compose().Command;

        Assert.Contains("--extensions=none", composed);
        Assert.Contains(composed, word => word.StartsWith("--allowed-mcp-server-names=", StringComparison.Ordinal) && word.Length > "--allowed-mcp-server-names=".Length);
        Assert.Contains("--admin-policy=.jason-policy", composed);
        Assert.DoesNotContain(composed, word => word.StartsWith("--policy", StringComparison.Ordinal));
    }

    [Fact]
    public void A_profiles_arguments_come_last_and_nothing_follows_them()
    {
        var composed = Compose(Revision(["--model=made-up-model"])).Command;

        Assert.Equal("--model=made-up-model", composed[^1]);
    }

    [Fact]
    public void Every_flag_that_reaches_the_command_is_one_a_profile_cannot_carry()
    {
        var composed = Compose(files: new MemoryFileLocations(Client, Project, Campaign)).Command;

        var flags = composed.Skip(1).Where(word => word.StartsWith('-')).Select(word => word.Split('=')[0]);
        Assert.All(flags, flag => Assert.Contains(flag, _host.ReservedFlags));
    }

    [Theory]
    [InlineData("-p")]
    [InlineData("--prompt")]
    [InlineData("-i")]
    [InlineData("--prompt-interactive")]
    [InlineData("-o")]
    [InlineData("--output-format")]
    [InlineData("--approval-mode")]
    [InlineData("-y")]
    [InlineData("--yolo")]
    [InlineData("--skip-trust")]
    [InlineData("--session-id")]
    [InlineData("--session-file")]
    [InlineData("-r")]
    [InlineData("--resume")]
    [InlineData("--policy")]
    [InlineData("--admin-policy")]
    [InlineData("--allowed-mcp-server-names")]
    [InlineData("--allowed-tools")]
    [InlineData("-e")]
    [InlineData("--extensions")]
    [InlineData("--include-directories")]
    [InlineData("-s")]
    [InlineData("--sandbox")]
    [InlineData("--acp")]
    [InlineData("--experimental-acp")]
    [InlineData("-w")]
    [InlineData("--worktree")]
    [InlineData("--raw-output")]
    public void A_flag_that_would_widen_or_replace_the_shape_is_reserved(string flag)
    {
        Assert.Contains(flag, _host.ReservedFlags);
    }

    [Fact]
    public void A_model_is_not_reserved_since_it_widens_nothing()
    {
        Assert.DoesNotContain("-m", _host.ReservedFlags);
        Assert.DoesNotContain("--model", _host.ReservedFlags);
    }

    /// <summary>
    /// The host's parser reads a word that is not a flag as the prompt, and a value after a flag as a second word, so a
    /// profile's arguments are flags only and their values are joined with an equals sign.
    /// </summary>
    [Theory]
    [InlineData("bare")]
    [InlineData("-")]
    [InlineData("--")]
    [InlineData("-mmodel")]
    [InlineData("-m=other")]
    // The parser expands a short group into its letters, so each of these is -y and another flag, or -y and its value.
    [InlineData("-yo")]
    [InlineData("-oy")]
    [InlineData("-yfoo")]
    [InlineData("-sy")]
    [InlineData("-pfoo")]
    // The parser reads a dot in a flag's name as a path into an object, which it assigns to the flag's own key.
    [InlineData("--include-directories.0=/etc")]
    [InlineData("--approval-mode.x=1")]
    [InlineData("--model.x=1")]
    // The parser strips a leading dash, a trailing dash and an underscore before it matches a name, so each of these is a
    // reserved option under a spelling the reserved list does not hold; a flag's name is letters, digits and single dashes.
    [InlineData("---acp")]
    [InlineData("--acp-")]
    [InlineData("--fake-responses_=x")]
    [InlineData("--record-responses-=p")]
    [InlineData("--delete-session-=id")]
    [InlineData("--prompt-=t")]
    [InlineData("--approval-_mode=x")]
    [InlineData("--approval--mode=x")]
    [InlineData("--approval_mode=x")]
    [InlineData("--=x")]
    [InlineData("--model-")]
    public void Arguments_the_host_would_read_as_a_prompt_a_group_or_a_glued_option_are_refused(string word)
    {
        Assert.NotNull(_host.ArgsProblem(["--model=made-up", word]));
    }

    [Fact]
    public void Long_flags_with_values_joined_by_an_equals_sign_are_accepted_and_a_short_one_stands_alone()
    {
        Assert.Null(_host.ArgsProblem(["--model=made-up", "--debug", "-d"]));
        Assert.Null(_host.ArgsProblem(["--model=gemini-2.5-pro"]));
        Assert.Null(_host.ArgsProblem(["--no-color", "--model=a_b-c=d"]));
        Assert.Null(_host.ArgsProblem([]));
    }

    /// <summary>
    /// The parser sets one option under every spelling it knows: the camel-case twin of a kebab-case name, and the
    /// <c>--no-</c> form of either, which negates it. Each is the same option, so a spelling left out would be launched
    /// after the composed flag, where the last value wins.
    /// </summary>
    [Theory]
    [InlineData("--approvalMode")]
    [InlineData("--APPROVAL-MODE")]
    [InlineData("--Approval-Mode")]
    [InlineData("--promptInteractive")]
    [InlineData("--outputFormat")]
    [InlineData("--rawOutput")]
    [InlineData("--acceptRawOutputRisk")]
    [InlineData("--skipTrust")]
    [InlineData("--sessionId")]
    [InlineData("--sessionFile")]
    [InlineData("--listSessions")]
    [InlineData("--deleteSession")]
    [InlineData("--adminPolicy")]
    [InlineData("--allowedMcpServerNames")]
    [InlineData("--allowedTools")]
    [InlineData("--listExtensions")]
    [InlineData("--includeDirectories")]
    [InlineData("--experimentalAcp")]
    [InlineData("--fakeResponses")]
    [InlineData("--fakeResponsesNonStrict")]
    [InlineData("--recordResponses")]
    [InlineData("--no-yolo")]
    [InlineData("--no-skip-trust")]
    [InlineData("--no-skipTrust")]
    [InlineData("--no-sandbox")]
    [InlineData("--no-approval-mode")]
    [InlineData("--no-approvalMode")]
    [InlineData("--no-include-directories")]
    [InlineData("--no-admin-policy")]
    [InlineData("--no-worktree")]
    [InlineData("--query")]
    [InlineData("--isCommand")]
    [InlineData("--is-command")]
    public void Every_spelling_the_parser_knows_for_a_reserved_flag_is_reserved(string flag)
    {
        Assert.Contains(flag, _host.ReservedFlags);
    }

    /// <summary>The same rule, derived: whatever long flag is reserved has its twin and its negations reserved.</summary>
    [Fact]
    public void Each_reserved_long_flag_has_its_camel_case_twin_and_its_negations_reserved()
    {
        var longs = _host.ReservedFlags.Where(flag => flag.StartsWith("--", StringComparison.Ordinal) && !flag.StartsWith("--no-", StringComparison.Ordinal)).ToList();
        Assert.NotEmpty(longs);
        foreach (var flag in longs)
        {
            var kebab = flag[2..];
            var parts = kebab.Split('-');
            var camel = parts[0] + string.Concat(parts.Skip(1).Select(part => part.Length == 0 ? part : char.ToUpperInvariant(part[0]) + part[1..]));
            Assert.Contains("--" + camel, _host.ReservedFlags);
            Assert.Contains("--no-" + kebab, _host.ReservedFlags);
            Assert.Contains("--no-" + camel, _host.ReservedFlags);
        }
    }

    [Fact]
    public void Each_tool_is_the_hosts_own_name_for_it_and_every_one_is_there()
    {
        Assert.Equal("google_web_search", _host.ToolRule(AgentTool.WebSearch));
        Assert.Equal("web_fetch", _host.ToolRule(AgentTool.WebFetch));
        Assert.All(Enum.GetValues<AgentTool>(), tool => Assert.Null(_host.GrantProblem(tool)));
    }

    [Theory]
    [InlineData("web_fetch")]
    [InlineData("google_web_search")]
    [InlineData("write_file")]
    [InlineData("replace")]
    [InlineData("read_file")]
    [InlineData("run_shell_command(rm)")]
    [InlineData("run_shell_command(git push)")]
    // The rest of the host's built-in list at 0.66.0, and the agents that are tools of it.
    [InlineData("tracker_create_task")]
    [InlineData("tracker_update_task")]
    [InlineData("tracker_get_task")]
    [InlineData("tracker_list_tasks")]
    [InlineData("tracker_add_dependency")]
    [InlineData("tracker_visualize")]
    [InlineData("update_topic")]
    [InlineData("complete_task")]
    [InlineData("invoke_agent")]
    [InlineData("codebase_investigator")]
    [InlineData("cli_help")]
    public void A_deny_is_a_tool_of_the_host_by_its_name_or_a_shell_command_by_its_prefix(string word)
    {
        Assert.Null(_host.DenyProblem(word));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("WebFetch")]
    [InlineData("Bash(rm *)")]
    [InlineData("a_tool_it_does_not_have")]
    [InlineData("run_shell_command()")]
    [InlineData("run_shell_command(rm")]
    [InlineData("run_shell_command(rm\nroot)")]
    public void A_deny_the_host_has_no_rule_for_is_refused_with_the_reason(string word)
    {
        var problem = _host.DenyProblem(word);

        Assert.False(string.IsNullOrWhiteSpace(problem));
    }

    [Fact]
    public void What_the_revision_does_not_grant_is_denied_by_the_hosts_name_and_what_it_grants_is_allowed()
    {
        var none = Compose(Revision(cli: "jason"));
        var fetch = Compose(Revision(tools: [AgentTool.WebFetch], cli: "jason"));
        var both = Compose(Revision(tools: [AgentTool.WebFetch, AgentTool.WebSearch], cli: "jason"));

        Assert.Equal(["google_web_search", "web_fetch"], none.Deny.Order());
        Assert.Equal(["google_web_search"], fetch.Deny);
        Assert.Empty(both.Deny);
        Assert.Equal(["run_shell_command:jason"], none.Allow);
        Assert.Equal(["run_shell_command:jason", "web_fetch"], fetch.Allow);
        Assert.Equal(["run_shell_command:jason", "web_fetch", "google_web_search"], both.Allow);
    }

    [Fact]
    public void The_command_the_role_may_run_is_the_profiles_own_word()
    {
        Assert.Equal(["run_shell_command:jason-next"], Compose(Revision(cli: "jason-next")).Allow);
    }

    // The memory files: the host reads files of the workspace only, so the folders holding the files a launch names
    // are the workspace's too, and nothing beyond them.

    [Fact]
    public void The_folders_included_are_exactly_those_of_the_files_the_launch_names()
    {
        var composed = Compose(files: new MemoryFileLocations(Client, Project, Campaign));

        var included = Included(composed.Command);
        Assert.Equal(
            new[] { Path.GetDirectoryName(Client)!, Path.GetDirectoryName(Project)!, Path.GetDirectoryName(Campaign)! }.Order(StringComparer.Ordinal),
            included.Order(StringComparer.Ordinal));
        Assert.Empty(composed.Unreadable);
    }

    [Fact]
    public void A_level_the_launch_does_not_name_adds_no_folder()
    {
        var composed = Compose(files: new MemoryFileLocations(Client, null, Campaign));

        Assert.Equal(
            new[] { Path.GetDirectoryName(Client)!, Path.GetDirectoryName(Campaign)! }.Order(StringComparer.Ordinal),
            Included(composed.Command).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void A_launch_naming_no_memory_file_includes_no_folder()
    {
        Assert.Empty(Included(Compose().Command));
    }

    [Fact]
    public void Two_files_in_one_folder_include_it_once()
    {
        var other = Path.Combine(Path.GetDirectoryName(Project)!, "AGENTS.md");

        var composed = Compose(files: new MemoryFileLocations(null, Project, other));

        Assert.Equal([Path.GetDirectoryName(Project)!], Included(composed.Command));
    }

    /// <summary>
    /// The host splits a list on a comma, so a folder whose name holds one cannot be named exactly; the level is reported
    /// unreadable rather than included as two other folders. A path that is not absolute has no folder to name either.
    /// </summary>
    [Fact]
    public void A_file_whose_folder_cannot_be_named_exactly_is_reported_unreadable_and_includes_nothing()
    {
        var commaFolder = Path.Combine(Root, "a,b", "AGENTS.md");

        var composed = Compose(files: new MemoryFileLocations(commaFolder, "relative/AGENTS.md", Campaign));

        Assert.Equal(["client", "project"], composed.Unreadable);
        Assert.Equal([Path.GetDirectoryName(Campaign)!], Included(composed.Command));
    }

    /// <summary>
    /// The host trims every folder of a list, so a folder whose path ends in a space would name another folder; and a path
    /// that is rooted but not fully qualified, such as a drive-relative one, has no folder to name.
    /// </summary>
    [Theory]
    [InlineData("trail ")]
    [InlineData("trail  ")]
    public void A_folder_with_whitespace_at_its_end_is_reported_unreadable_and_includes_nothing(string folderName)
    {
        var file = Path.Combine(Root, folderName, "AGENTS.md");

        var composed = Compose(files: new MemoryFileLocations(file, null, Campaign));

        Assert.Equal(["client"], composed.Unreadable);
        Assert.Equal([Path.GetDirectoryName(Campaign)!], Included(composed.Command));
    }

    [Fact]
    public void A_folder_with_whitespace_inside_its_name_is_included_as_it_is()
    {
        var file = Path.Combine(Root, " two words ", "inner", "AGENTS.md");

        var composed = Compose(files: new MemoryFileLocations(file, null, null));

        Assert.Empty(composed.Unreadable);
        Assert.Equal([Path.GetDirectoryName(file)!], Included(composed.Command));
    }

    [Fact]
    public void A_path_rooted_but_not_fully_qualified_is_reported_unreadable()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Only Windows has a rooted path that is not fully qualified.");

        var composed = Compose(files: new MemoryFileLocations("\\foo\\AGENTS.md", "C:foo\\AGENTS.md", Campaign));

        Assert.Equal(["client", "project"], composed.Unreadable);
    }

    private static string[] Included(IReadOnlyList<string> command) =>
        [.. command.Where(word => word.StartsWith("--include-directories=", StringComparison.Ordinal)).Select(word => word["--include-directories=".Length..])];

    // What a stand-in would learn of its end: the documented exit for a failed sign-in, and nothing of the host's words.

    [Fact]
    public void The_documented_exit_for_a_failed_authentication_is_one_fixed_sentence_whatever_the_tail_says()
    {
        var sentence = _host.Unavailable(41, "Please set an Auth method; signed in as someone@example.test");

        Assert.False(string.IsNullOrWhiteSpace(sentence));
        Assert.StartsWith("Gemini CLI could not authenticate", sentence, StringComparison.Ordinal);
        Assert.DoesNotContain("not signed in", sentence, StringComparison.Ordinal);
        Assert.Contains("sign in as yourself", sentence, StringComparison.Ordinal);
        Assert.Contains("antigravity_cli", sentence, StringComparison.Ordinal);
        Assert.DoesNotContain("someone", sentence, StringComparison.Ordinal);
        Assert.Equal(sentence, _host.Unavailable(41, null));
        Assert.Equal(sentence, _host.Unavailable(41, string.Empty));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(42)]
    [InlineData(52)]
    [InlineData(130)]
    [InlineData(-1)]
    public void Any_other_end_says_nothing_of_the_host(int exit)
    {
        Assert.Null(_host.Unavailable(exit, "Authentication failed: login required"));
    }

    // The work directory: one policy file at the highest tier and a settings file that keeps the person's own out.

    [Fact]
    public void The_policy_denies_everything_first_then_allows_what_the_role_needs_and_denies_what_it_may_not()
    {
        using var directory = new TempDirectory();

        _host.WriteWorkDirectory(directory.Path, ["run_shell_command:jason", "web_fetch"], ["google_web_search"]);

        var policy = File.ReadAllText(Path.Combine(directory.Path, ".jason-policy", "jason.toml"));
        Assert.Equal(
            """
            # Written by Jason for this attempt alone. Admin tier: a person's own allow rules cannot widen it.

            [[rule]]
            toolName = "*"
            decision = "deny"
            priority = 1

            [[rule]]
            toolName = "read_file"
            decision = "allow"
            priority = 500

            [[rule]]
            toolName = "read_many_files"
            decision = "allow"
            priority = 500

            [[rule]]
            toolName = "list_directory"
            decision = "allow"
            priority = 500

            [[rule]]
            toolName = "glob"
            decision = "allow"
            priority = 500

            [[rule]]
            toolName = "grep_search"
            decision = "allow"
            priority = 500

            [[rule]]
            toolName = "write_todos"
            decision = "allow"
            priority = 500

            [[rule]]
            toolName = "activate_skill"
            decision = "allow"
            priority = 500

            [[rule]]
            toolName = "run_shell_command"
            commandPrefix = "jason"
            decision = "allow"
            priority = 500

            [[rule]]
            toolName = "web_fetch"
            decision = "allow"
            priority = 500

            [[rule]]
            toolName = "write_file"
            decision = "deny"
            priority = 900

            [[rule]]
            toolName = "replace"
            decision = "deny"
            priority = 900

            [[rule]]
            toolName = "ask_user"
            decision = "deny"
            priority = 900

            [[rule]]
            toolName = "google_web_search"
            decision = "deny"
            priority = 900

            """.ReplaceLineEndings("\n"),
            policy.ReplaceLineEndings("\n"));
    }

    [Fact]
    public void A_deny_of_a_shell_prefix_is_written_as_a_prefix_rule_above_every_allow()
    {
        using var directory = new TempDirectory();

        _host.WriteWorkDirectory(directory.Path, ["run_shell_command:jason"], ["run_shell_command(jason campaign delete)", "read_file"]);

        var policy = File.ReadAllText(Path.Combine(directory.Path, ".jason-policy", "jason.toml")).ReplaceLineEndings("\n");
        Assert.Contains("toolName = \"run_shell_command\"\ncommandPrefix = \"jason campaign delete\"\ndecision = \"deny\"\npriority = 900", policy, StringComparison.Ordinal);
        Assert.Contains("toolName = \"read_file\"\ndecision = \"deny\"\npriority = 900", policy, StringComparison.Ordinal);
        Assert.DoesNotContain("priority = 9000", policy, StringComparison.Ordinal);
    }

    [Fact]
    public void A_word_with_a_quote_or_a_backslash_is_written_escaped_so_it_cannot_end_its_string()
    {
        using var directory = new TempDirectory();

        _host.WriteWorkDirectory(directory.Path, ["run_shell_command:ja\"son"], ["run_shell_command(a\\b\"c)"]);

        var policy = File.ReadAllText(Path.Combine(directory.Path, ".jason-policy", "jason.toml")).ReplaceLineEndings("\n");
        Assert.Contains("commandPrefix = \"ja\\\"son\"", policy, StringComparison.Ordinal);
        Assert.Contains("commandPrefix = \"a\\\\b\\\"c\"", policy, StringComparison.Ordinal);
    }

    [Fact]
    public void The_policy_holds_neither_the_capability_token_nor_any_envelope_field()
    {
        using var directory = new TempDirectory();

        _host.WriteWorkDirectory(directory.Path, ["run_shell_command:jason"], []);

        var text = string.Concat(Directory.EnumerateFiles(directory.Path, "*", SearchOption.AllDirectories).Select(File.ReadAllText));
        Assert.DoesNotContain("token", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("brief", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("envelope", text, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The project's settings override the person's for the keys they name: the instruction file is the one the harness
    /// folders carry, so no file name the person configured is read, and no hook of theirs runs.
    /// </summary>
    [Fact]
    public void The_projects_settings_pin_the_instruction_file_and_turn_hooks_off()
    {
        using var directory = new TempDirectory();

        _host.WriteWorkDirectory(directory.Path, ["run_shell_command:jason"], []);

        var settings = JsonNode.Parse(File.ReadAllText(Path.Combine(directory.Path, ".gemini", "settings.json")))!.AsObject();
        Assert.Equal(["GEMINI.md"], settings["context"]!["fileName"]!.AsArray().Select(name => (string?)name));
        Assert.False((bool)settings["hooksConfig"]!["enabled"]!);
        Assert.Equal(2, settings.Count);
    }

    [Fact]
    public void Writing_the_directory_twice_leaves_the_same_files()
    {
        using var directory = new TempDirectory();

        _host.WriteWorkDirectory(directory.Path, ["run_shell_command:jason"], ["web_fetch"]);
        var first = File.ReadAllText(Path.Combine(directory.Path, ".jason-policy", "jason.toml"));
        _host.WriteWorkDirectory(directory.Path, ["run_shell_command:jason"], ["web_fetch"]);

        Assert.Equal(first, File.ReadAllText(Path.Combine(directory.Path, ".jason-policy", "jason.toml")));
    }

    /// <summary>A control character in a word would end the line a TOML basic string is on, so each is written as its escape.</summary>
    [Fact]
    public void A_control_character_in_a_word_is_written_as_an_escape_and_never_as_itself()
    {
        using var directory = new TempDirectory();

        _host.WriteWorkDirectory(directory.Path, ["run_shell_command:jason"], ["run_shell_command(a\nb\tc\u007Fd)"]);

        var policy = File.ReadAllText(Path.Combine(directory.Path, ".jason-policy", "jason.toml"));
        Assert.Contains("commandPrefix = \"a\\u000Ab\\u0009c\\u007Fd\"\n", policy, StringComparison.Ordinal);
        Assert.DoesNotContain('\t', policy);
        Assert.DoesNotContain('\u007F', policy);
        Assert.DoesNotContain("\nb", policy, StringComparison.Ordinal);
    }

    [Fact]
    public void A_work_directory_written_for_the_host_holds_the_files_where_the_host_reads_them_and_nothing_else()
    {
        using var directory = new TempDirectory();

        _host.WriteWorkDirectory(directory.Path, ["run_shell_command:jason"], []);

        var files = Directory.EnumerateFiles(directory.Path, "*", SearchOption.AllDirectories)
            .Select(file => Path.GetRelativePath(directory.Path, file).Replace('\\', '/'))
            .Order(StringComparer.Ordinal);
        Assert.Equal([".gemini/settings.json", ".jason-policy/jason.toml"], files);
    }

    /// <summary>The constants a host's row declares for the child's environment: the one that keeps a signed-out run from waiting on a browser.</summary>
    [Fact]
    public void The_host_keeps_a_signed_out_run_from_opening_a_browser_by_a_constant_of_its_own()
    {
        Assert.Equal("true", _host.Environment["NO_BROWSER"]);
    }

    /// <summary>
    /// The host adds the folders of an IDE's workspace to the run's own when this variable is set, and an empty value is
    /// read as none; a launch that inherited the person's value would read folders it never named.
    /// </summary>
    [Fact]
    public void The_host_is_given_no_editor_workspace_by_an_empty_constant_so_an_inherited_one_adds_no_folder()
    {
        Assert.Equal(string.Empty, _host.Environment["GEMINI_CLI_IDE_WORKSPACE_PATH"]);
        Assert.Equal(["GEMINI_CLI_IDE_WORKSPACE_PATH", "NO_BROWSER"], _host.Environment.Keys.Order(StringComparer.Ordinal));
    }

    private sealed class TempDirectory : IDisposable
    {
        public TempDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "jason-gemini-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            try
            {
                Directory.Delete(Path, recursive: true);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // The temporary tree is the machine's to clear.
            }
        }
    }
}
