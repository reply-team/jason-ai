using Jason.Contracts.Api;
using Jason.Contracts.Execution;
using Jason.Contracts.Hosts;
using Jason.Runtime.Execution.Hosts;
using Jason.Runtime.Persistence;

namespace Jason.Runtime.Tests.Execution;

/// <summary>
/// The shape one headless GitHub Copilot CLI session is started with. Built from the host's documentation for version
/// 1.0.105 and not yet run on an account, so a row here pins what the runtime composes and says nothing of what the
/// installed host does with it. The flag spellings below are the documented ones, which is what makes them stable
/// enough to pin.
/// </summary>
public class CopilotCliHostTests
{
    private const string Fixed = "GitHub Copilot CLI is not signed in on this account: run `copilot login` as yourself.";

    private static MemoryFileLocations NoFiles { get; } = new(null, null, null);

    [Fact]
    public void It_answers_for_one_host_and_says_which()
    {
        Assert.Equal(AgentHostKind.CopilotCli, new CopilotCliHost().Kind);
        Assert.Equal("copilot_cli", AgentHosts.Word(AgentHostKind.CopilotCli));
    }

    /// <summary>
    /// The prompt is piped on stdin, which the host reads when it is given no prompt of its own. So the command carries
    /// neither the print flag nor any word of the brief, and the launch comes first and whole.
    /// </summary>
    [Fact]
    [Critical(Family.W7, "the brief goes on stdin and no word of it on the command line")]
    public void The_command_opens_with_the_launch_and_carries_no_prompt_flag()
    {
        string[] launch = ["/opt/copilot/node", "/opt/copilot/index.js"];

        var composed = new CopilotCliHost().Compose(Revision(), launch, NoFiles).Command;

        Assert.Equal(launch, composed.Take(2));
        Assert.DoesNotContain("-p", composed);
        Assert.DoesNotContain("--prompt", composed);
        Assert.DoesNotContain("-i", composed);
    }

    [Fact]
    public void The_session_reports_as_it_goes_and_is_never_interactive()
    {
        var composed = new CopilotCliHost().Compose(Revision(), ["copilot"], NoFiles).Command;

        Assert.Equal("json", composed[composed.ToList().IndexOf("--output-format") + 1]);
        foreach (var flag in new[] { "--no-ask-user", "--no-auto-update", "--no-custom-instructions", "--disable-builtin-mcps" })
        {
            Assert.Single(composed, flag);
        }
    }

    /// <summary>
    /// The session the runtime minted is the one the host is told to use, and nothing the host takes as permission for
    /// everything is composed: not trust of the folder, not the flag that allows every tool, path or address.
    /// </summary>
    [Fact]
    [Critical(Family.S6, "no flag that allows every tool, path or address is composed")]
    public void The_session_is_the_one_minted_here_and_no_flag_allows_everything()
    {
        var host = new CopilotCliHost();

        var first = host.Compose(Revision(), ["copilot"], NoFiles);
        var second = host.Compose(Revision(), ["copilot"], NoFiles);

        var flag = first.Command.ToList().IndexOf("--session-id");
        Assert.True(flag >= 0, "the command names no session");
        Assert.Equal(first.SessionId, first.Command[flag + 1]);
        Assert.True(Guid.TryParseExact(first.SessionId, "D", out _), $"'{first.SessionId}' is not a UUID.");
        Assert.NotEqual(first.SessionId, second.SessionId);
        foreach (var unconfining in new[] { "--trust-folder", "--yolo", "--allow-all", "--allow-all-tools", "--allow-all-paths", "--allow-all-urls", "--add-dir", "--autopilot" })
        {
            Assert.DoesNotContain(unconfining, first.Command);
        }
    }

    /// <summary>
    /// The tool set is the confinement: a tool the host is not offered is unavailable even where something allows it. So
    /// the role is offered what reads, runs the callback and loads its skill, and nothing that writes a file, patches
    /// one, asks a person, or starts another agent.
    /// </summary>
    [Fact]
    [Critical(Family.S6, "the tool set is the confinement: a tool not offered is unavailable")]
    public void The_tool_set_has_no_tool_that_writes_asks_or_starts_another_agent()
    {
        var tools = Tools(new CopilotCliHost(windows: false).Compose(Revision(), ["copilot"], NoFiles));

        Assert.Equal(["view", "glob", "grep", "skill", "bash", "read_bash", "list_bash", "stop_bash"], tools);
        foreach (var forbidden in new[] { "edit", "create", "apply_patch", "task", "ask_user", "write_bash", "write_agent", "list_agents", "read_agent", "memory" })
        {
            Assert.DoesNotContain(forbidden, tools);
        }
    }

    [Fact]
    public void On_windows_the_powershell_tools_are_offered_beside_the_bash_ones_and_nowhere_else()
    {
        var windows = Tools(new CopilotCliHost(windows: true).Compose(Revision(), ["copilot"], NoFiles));
        var elsewhere = Tools(new CopilotCliHost(windows: false).Compose(Revision(), ["copilot"], NoFiles));

        Assert.Equal(["powershell", "read_powershell", "list_powershell", "stop_powershell"], windows.Where(tool => tool.EndsWith("powershell", StringComparison.Ordinal)));
        Assert.DoesNotContain(elsewhere, tool => tool.EndsWith("powershell", StringComparison.Ordinal));
    }

    /// <summary>The fetch tool is offered when the revision grants it, and a search tool never is: the host documents none.</summary>
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void The_fetch_tool_is_offered_only_when_granted_and_no_search_tool_ever_is(bool fetch, bool search)
    {
        var revision = Revision();
        if (fetch)
        {
            revision.Tools.Add(AgentTool.WebFetch);
        }

        if (search)
        {
            revision.Tools.Add(AgentTool.WebSearch);
        }

        var launch = new CopilotCliHost().Compose(revision, ["copilot"], NoFiles);
        var tools = Tools(launch);

        Assert.Equal(fetch, tools.Contains("web_fetch"));
        Assert.DoesNotContain("web_search", tools);
        Assert.DoesNotContain("web_search", Allow(launch));
        Assert.DoesNotContain("web_search", Denied(launch));
    }

    /// <summary>
    /// The callback is allowed as the runtime's command word followed by anything, which the host reads as every
    /// subcommand of it; one rule of the <c>shell</c> kind covers both shell tools, so there is no second one.
    /// </summary>
    [Fact]
    [Critical(Family.S6, "the launched session's allow list")]
    public void The_allow_list_is_the_callback_by_its_word_and_nothing_else()
    {
        var launch = new CopilotCliHost().Compose(Revision(), ["copilot"], NoFiles);

        Assert.Equal([$"shell({ProgramResolver.DefaultCliCommand}:*)"], Allow(launch));
    }

    [Fact]
    public void A_profile_that_names_its_own_command_word_is_the_one_in_the_allow_rule()
    {
        var revision = Revision();
        revision.CliCommand = "jason-dev";

        var launch = new CopilotCliHost().Compose(revision, ["copilot"], NoFiles);

        Assert.Equal(["shell(jason-dev:*)"], Allow(launch));
    }

    /// <summary>
    /// Each memory file the launch names is readable by one rule of its own, the host's <c>kind(argument)</c> form with
    /// the absolute path as the argument, forward slashes on Windows. Documented and not measured: whether such a rule
    /// reaches outside the work directory is not stated by the host's pages.
    /// </summary>
    [Fact]
    [Critical(Family.S6, "the read rules, exactly: a rule for a folder or a pattern beside the files' own turns this red")]
    public void Each_memory_file_the_launch_names_is_readable_by_a_rule_of_its_own()
    {
        var files = new MemoryFileLocations(
            @"C:\Users\Marta\Jason\AGENTS.md",
            @"C:\Users\Marta\Jason\projects\acme-corp\AGENTS.md",
            @"C:\Users\Marta\Jason\projects\acme-corp\campaigns\q3-marketing-leads\AGENTS.md");

        var launch = new CopilotCliHost(windows: true).Compose(Revision(), ["copilot"], files);

        string[] expected =
            [
                "read(C:/Users/Marta/Jason/AGENTS.md)",
                "read(C:/Users/Marta/Jason/projects/acme-corp/AGENTS.md)",
                "read(C:/Users/Marta/Jason/projects/acme-corp/campaigns/q3-marketing-leads/AGENTS.md)",
            ];
        Assert.Equal(expected, Allow(launch).Skip(1));
        Assert.Empty(launch.Unreadable);
    }

    [Fact]
    public void Off_windows_a_memory_file_is_named_by_its_absolute_path_as_it_is()
    {
        var files = new MemoryFileLocations("/home/marta/Jason/AGENTS.md", null, "/home/marta/Jason/projects/acme-corp/campaigns/q3/AGENTS.md");

        var launch = new CopilotCliHost(windows: false).Compose(Revision(), ["copilot"], files);

        Assert.Equal(["read(/home/marta/Jason/AGENTS.md)", "read(/home/marta/Jason/projects/acme-corp/campaigns/q3/AGENTS.md)"], Allow(launch).Skip(1));
    }

    [Fact]
    public void One_file_named_twice_is_one_rule_and_a_level_not_known_is_none()
    {
        const string path = @"C:\Users\Marta\Jason\AGENTS.md";

        var launch = new CopilotCliHost(windows: true).Compose(Revision(), ["copilot"], new MemoryFileLocations(path, path, null));

        Assert.Equal(["read(C:/Users/Marta/Jason/AGENTS.md)"], Allow(launch).Skip(1));
    }

    /// <summary>
    /// The list is comma-separated and the argument is parenthesised, and only the shell and url kinds take wildcards, so a
    /// path holding a comma, a parenthesis or a pattern character is not escaped by guess: it gets no rule, and the level is
    /// reported as one the role cannot read. So does a path the form does not cover.
    /// </summary>
    [Theory]
    [InlineData(true, @"C:\Users\Marta\[Work]\Jason\AGENTS.md")]
    [InlineData(true, @"C:\Users\Marta\Wo*rk\Jason\AGENTS.md")]
    [InlineData(true, @"C:\Users\Marta\Wo?rk\Jason\AGENTS.md")]
    [InlineData(true, @"C:\Clients\Acme (EU) Ltd\AGENTS.md")]
    [InlineData(true, @"C:\Clients\Acme, Inc\AGENTS.md")]
    [InlineData(true, @"\\fileserver\share\Jason\AGENTS.md")]
    [InlineData(true, @"Jason\AGENTS.md")]
    [InlineData(false, "/home/marta/[work]/Jason/AGENTS.md")]
    [InlineData(false, "/home/marta/acme, inc/Jason/AGENTS.md")]
    [InlineData(false, "/home/marta/acme (eu)/Jason/AGENTS.md")]
    [InlineData(false, @"/home/marta/wo\rk/Jason/AGENTS.md")]
    [InlineData(false, "home/marta/Jason/AGENTS.md")]
    public void A_path_the_host_would_read_as_something_else_gets_no_rule_and_is_reported_by_its_level(bool windows, string path)
    {
        var plain = windows ? @"C:\Users\Marta\Jason\projects\acme-corp\AGENTS.md" : "/home/marta/Jason/projects/acme-corp/AGENTS.md";

        var launch = new CopilotCliHost(windows).Compose(Revision(), ["copilot"], new MemoryFileLocations(path, plain, null));

        var reads = Allow(launch).Skip(1).ToList();
        Assert.True(reads.Count == 1, $"'{path}' and '{plain}' gave the rules [{string.Join(", ", reads)}].");
        Assert.EndsWith("/projects/acme-corp/AGENTS.md)", reads[0], StringComparison.Ordinal);
        Assert.Equal(["client"], launch.Unreadable);
    }

    /// <summary>
    /// The deny is the profile's own words as written, then each web tool the revision does not grant by the host's permission
    /// kind, then <c>write</c> — the role has no writing tool and a deny wins even over an allow-everything a profile's
    /// arguments carried. Each word once. What the host composes is reported on the launch; the profile's own is not.
    /// </summary>
    [Fact]
    [Critical(Family.S6, "what the revision does not grant is denied by the host's word, and write always")]
    public void The_deny_is_the_profiles_words_then_what_is_not_granted_then_write()
    {
        var revision = Revision();
        revision.Deny = ["shell(git push)", "write"];

        var launch = new CopilotCliHost().Compose(revision, ["copilot"], NoFiles);

        Assert.Equal(["shell(git push)", "write", "url"], Denied(launch));
        Assert.Equal(["url"], launch.Deny);
    }

    [Fact]
    public void A_granted_fetch_is_allowed_by_its_kind_and_not_denied()
    {
        var revision = Revision();
        revision.Tools = [AgentTool.WebFetch];

        var launch = new CopilotCliHost().Compose(revision, ["copilot"], NoFiles);

        Assert.Equal($"shell({ProgramResolver.DefaultCliCommand}:*)", Allow(launch)[0]);
        Assert.Equal("url", Allow(launch)[^1]);
        Assert.DoesNotContain("url", Denied(launch));
        Assert.Empty(launch.Deny);
        Assert.Contains("write", Denied(launch));
    }

    [Fact]
    public void The_profiles_own_arguments_come_last_and_nothing_follows_them()
    {
        var launch = new CopilotCliHost().Compose(Revision("--model=made-up-model", "--no-color"), ["copilot"], NoFiles);

        Assert.Equal(["--model=made-up-model", "--no-color"], launch.Command.TakeLast(2));
        Assert.Equal("copilot", launch.Command[0]);
    }

    /// <summary>Every flag the runtime composes is reserved, and so is each one that would undo the confinement.</summary>
    [Fact]
    public void Every_flag_the_runtime_composes_is_one_a_profile_may_not_set()
    {
        var host = new CopilotCliHost();
        var composed = host.Compose(Revision(), ["copilot"], new MemoryFileLocations(@"C:\Users\Marta\Jason\AGENTS.md", null, null)).Command
            .Skip(1).Where(word => word.StartsWith('-')).Select(word => word.Split('=')[0]).ToList();

        Assert.NotEmpty(composed);
        Assert.DoesNotContain(composed, flag => !host.ReservedFlags.Contains(flag));
    }

    [Theory]
    [InlineData("-p")]
    [InlineData("--prompt")]
    [InlineData("-i")]
    [InlineData("--interactive")]
    [InlineData("--output-format")]
    [InlineData("--session-id")]
    [InlineData("--resume")]
    [InlineData("--continue")]
    [InlineData("--trust-folder")]
    // Critical: a profile cannot allow every tool.
    [InlineData("--allow-all", Traits = ["Category", "Critical", "CriticalFamily", nameof(Family.S6)])]
    [InlineData("--yolo")]
    [InlineData("--allow-all-tools")]
    [InlineData("--allow-all-paths")]
    [InlineData("--allow-all-urls")]
    [InlineData("--allow-tool")]
    [InlineData("--deny-tool")]
    [InlineData("--allow-url")]
    [InlineData("--deny-url")]
    [InlineData("--available-tools")]
    [InlineData("--excluded-tools")]
    [InlineData("--add-dir")]
    [InlineData("--enable-mcp-server")]
    [InlineData("--additional-mcp-config")]
    [InlineData("--autopilot")]
    [InlineData("--config-dir")]
    [InlineData("--sandbox")]
    [InlineData("--remote")]
    public void A_profile_may_not_widen_what_a_launched_session_can_reach(string flag)
    {
        Assert.Contains(flag, new CopilotCliHost().ReservedFlags);
    }

    /// <summary>
    /// A profile may still choose the model and the amount of thought, and nothing about reach rides on them: they are on
    /// the allowlist the host takes arguments from, not outside a list of reserved words, so what they are is stated by
    /// the allowlist and checked there.
    /// </summary>
    [Theory]
    [InlineData("--model=made-up-model")]
    [InlineData("--effort=low")]
    [InlineData("--no-color")]
    public void A_profile_may_still_carry_the_options_that_do_not_change_the_reach(string argument)
    {
        Assert.Null(new CopilotCliHost().ArgsProblem([argument]));
    }

    /// <summary>
    /// What a launch closes that the person's own settings would otherwise leave to the host: the system temporary
    /// folder, which the host opens to a role unless told not to; the history the host syncs for a run, which its
    /// settings decide when the run says nothing; the agent mode, which a setting can make autopilot; and the shell's
    /// startup file named by BASH_ENV. Each is composed whatever the profile says, once, before the profile's own words.
    /// </summary>
    [Theory]
    [InlineData("--disallow-temp-dir")]
    [InlineData("--sync-session=off")]
    [InlineData("--mode=interactive")]
    [InlineData("--no-bash-env")]
    public void The_launch_closes_what_the_persons_own_settings_would_leave_open(string flag)
    {
        var host = new CopilotCliHost();

        var composed = host.Compose(Revision("--no-color"), ["copilot"], NoFiles).Command;

        Assert.Single(composed, flag);
        Assert.True(composed.ToList().IndexOf(flag) < composed.ToList().IndexOf("--no-color"), $"{flag} comes after the profile's words");
        Assert.Contains(flag.Split('=')[0], host.ReservedFlags);
    }

    /// <summary>
    /// Every flag the host documents that changes what a run reaches, loads or writes is reserved, those composed above
    /// and those the launch has no use for alike, so that no profile argument sets one in a spelling the check made when
    /// a profile is written would take for the person's own.
    /// </summary>
    [Theory]
    [InlineData("--disallow-temp-dir")]
    [InlineData("--sync-session")]
    [InlineData("--secret-env-vars")]
    [InlineData("--context")]
    [InlineData("--no-bash-env")]
    [InlineData("--extension-sdk-path")]
    [InlineData("--disable-mcp-server")]
    [InlineData("--add-github-mcp-tool")]
    [InlineData("--mouse")]
    [InlineData("--banner")]
    [InlineData("--no-banner")]
    [InlineData("--experimental")]
    [InlineData("--no-experimental")]
    public void A_flag_the_host_documents_for_reach_or_loading_is_reserved(string flag)
    {
        Assert.Contains(flag, new CopilotCliHost().ReservedFlags);
    }

    /// <summary>
    /// The web fetch has a rule under the host's permission kind, the word that allows and denies it alike, and the search
    /// tool has the word the host's own examples use. Neither is a tool name of the tool table, which is a different
    /// vocabulary.
    /// </summary>
    [Fact]
    public void The_rule_for_a_tool_is_the_hosts_permission_word()
    {
        var host = new CopilotCliHost();

        Assert.Equal("url", host.ToolRule(AgentTool.WebFetch));
        Assert.Null(host.ToolRule(AgentTool.WebSearch));
    }

    [Fact]
    [Critical(Family.S3, "no file in the work directory, so no token in one")]
    public void Nothing_is_written_into_the_work_directory_because_everything_travels_on_the_command_line()
    {
        var workDir = Path.Combine(Path.GetTempPath(), "jason-tests", "copilot-work", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workDir);
        try
        {
            new CopilotCliHost().WriteWorkDirectory(workDir, [], ["url"]);

            Assert.Empty(Directory.GetFileSystemEntries(workDir));
        }
        finally
        {
            Directory.Delete(workDir, recursive: true);
        }
    }

    /// <summary>
    /// The documented recommendation for a person who is not signed in is the command <c>copilot login</c>, which is what
    /// a startup error says. An end with a non-zero exit whose stderr tail names it is the host saying it cannot run on
    /// this machine, and the sentence is the host's own, fixed: the tail can hold the person's address or login and is
    /// never carried. Not measured: the host's pages state no exit code and no text for a signed-out run.
    /// </summary>
    [Fact]
    [Critical(Family.S3, "the not-signed-in sentence is fixed and carries none of the host's words")]
    public void A_non_zero_end_naming_the_login_command_is_a_fixed_sentence_and_carries_none_of_the_tail()
    {
        const string Tail = "Error: no authentication found for person@example.test (account 7f3a9c-made-up). Run `copilot login` to sign in.";

        var sentence = new CopilotCliHost().Unavailable(1, Tail);

        Assert.Equal(Fixed, sentence);
        Assert.DoesNotContain("example.test", sentence, StringComparison.Ordinal);
        Assert.DoesNotContain("7f3a9c", sentence, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(0, "Run copilot login to sign in")]
    [InlineData(1, "Error: the model made-up-model is not available on this plan")]
    [InlineData(1, null)]
    [InlineData(1, "")]
    [InlineData(2, "usage: unknown option")]
    [InlineData(1, "run mycopilot login again")]
    [InlineData(1, "see copilot logins in the docs")]
    public void Any_other_end_is_not_this_hosts_to_say(int exitCode, string? tail)
    {
        Assert.Null(new CopilotCliHost().Unavailable(exitCode, tail));
    }

    [Theory]
    [InlineData("Please run COPILOT LOGIN first")]
    [InlineData("run `Copilot   Login`")]
    public void The_command_is_found_whatever_its_case_and_the_ends_of_its_words_are_one_space_or_more(string tail)
    {
        Assert.Equal(Fixed, new CopilotCliHost().Unavailable(1, tail));
    }

    /// <summary>
    /// The host has no search tool in its documented tool table, so a search has no rule: a grant of it is refused where a
    /// profile is written, and a deny of it is the person's word, kept, since it holds anyway. The fetch has one.
    /// </summary>
    [Fact]
    public void A_search_has_no_rule_on_this_host_and_a_fetch_has_one_and_no_grant_is_unmakeable()
    {
        IAgentHost host = new CopilotCliHost();

        Assert.Null(host.ToolRule(AgentTool.WebSearch));
        Assert.NotNull(host.ToolRule(AgentTool.WebFetch));
        Assert.All(Enum.GetValues<AgentTool>(), tool => Assert.Null(host.GrantProblem(tool)));
    }

    /// <summary>
    /// The deny list is comma-separated and a rule is one of the host's five kinds (memory, read, shell, url, write, in
    /// that case) and an argument in parentheses. A word whose head is any other word is read by the host as an MCP
    /// server's name and denies nothing the role is offered, so it is refused where the profile is written rather than
    /// stored as a rule: a wrong-case kind, another host's word for a tool, the vocabulary word of a tool the host has.
    /// The two search words are the exception, kept as the person's own: the host has no search tool, so the deny holds
    /// anyway.
    /// </summary>
    [Theory]
    [InlineData("shell(git push)", false)]
    [InlineData("shell", false)]
    [InlineData("write", false)]
    [InlineData("read(.env)", false)]
    [InlineData("memory", false)]
    [InlineData("url(example.test)", false)]
    [InlineData("web_search", false)]
    [InlineData("WebSearch", false)]
    [InlineData("shell(git push),write", true)]
    [InlineData("a, b", true)]
    [InlineData("   ", true)]
    [InlineData("web_fetch", true)]
    [InlineData("WebFetch", true)]
    [InlineData("Shell(rm:*)", true)]
    [InlineData("Write", true)]
    [InlineData("Bash(rm:*)", true)]
    [InlineData("bash", true)]
    [InlineData("made-up-server", true)]
    [InlineData("made-up-server(some_tool)", true)]
    [InlineData("(shell)", true)]
    public void A_deny_word_is_one_rule_of_a_kind_the_host_reads_or_it_is_refused(string word, bool refused)
    {
        Assert.Equal(refused, new CopilotCliHost().DenyProblem(word) is not null);
    }

    [Theory]
    [InlineData("web_fetch")]
    [InlineData("WebFetch")]
    public void A_deny_by_the_name_of_the_fetch_tool_is_refused_naming_the_word_the_host_reads(string word)
    {
        var problem = new CopilotCliHost().DenyProblem(word);

        Assert.NotNull(problem);
        Assert.Contains("url", problem, StringComparison.Ordinal);
    }

    [Fact]
    public void A_deny_of_the_five_kinds_names_them_when_it_is_refused()
    {
        var problem = new CopilotCliHost().DenyProblem("made-up-server");

        Assert.NotNull(problem);
        foreach (var kind in new[] { "memory", "read", "shell", "url", "write" })
        {
            Assert.Contains(kind, problem, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// The host's parser is closed and was not read, so a flag the documentation does not name cannot be ruled out as a
    /// hidden alias of one the runtime composes, nor as a cluster or a glued value. A profile's arguments are therefore an
    /// allowlist of the documented options a person owns, each as one argument: <c>--name=value</c>, or the bare flag for
    /// the two that take none. Anything else is refused, however it is spelled.
    /// </summary>
    [Theory]
    [InlineData("--model=made-up-model", false)]
    [InlineData("--model=made up", false)]
    [InlineData("--model=a\nb", true)]
    [InlineData("--model=a\rb", true)]
    [InlineData("--model=a\tb", true)]
    [InlineData("--model=a\0b", true)]
    [InlineData("--model=a\u007fb", true)]
    [InlineData("--effort=high\n", true)]
    [InlineData("--effort=high", false)]
    [InlineData("--reasoning-effort=low", false)]
    [InlineData("--log-level=debug", true)]
    [InlineData("--log-dir=made-up-folder", true)]
    [InlineData("--max-ai-credits=5", false)]
    [InlineData("--no-color", false)]
    [InlineData("--disable-client-telemetry", false)]
    [InlineData("--model", true)]
    [InlineData("--model=", true)]
    [InlineData("--no-color=1", true)]
    [InlineData("--MODEL=made-up-model", true)]
    [InlineData("--model made-up-model", true)]
    [InlineData("made-up-model", true)]
    [InlineData("--unknown-option", true)]
    [InlineData("--unknown-option=1", true)]
    [InlineData("--yolo", true)]
    [InlineData("--", true)]
    [InlineData("-", true)]
    [InlineData("-1", true)]
    [InlineData("-p", true)]
    [InlineData("-pfoo", true)]
    [InlineData("-pi", true)]
    [InlineData("-1x", true)]
    [InlineData("-Cfolder", true)]
    [InlineData("", true)]
    public void Only_a_documented_option_a_person_owns_is_taken_as_an_argument(string arg, bool refused)
    {
        var problem = new CopilotCliHost().ArgsProblem(["--model=made-up-model", arg]);

        Assert.Equal(refused, problem is not null);
        if (refused)
        {
            Assert.Contains("the host's parser is not read, so only the documented options the person owns are taken", problem, StringComparison.Ordinal);
            Assert.Contains("--model=<value>", problem, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void A_fresh_session_is_given_and_nothing_is_allowed_through_the_work_directory()
    {
        IAgentHost host = new CopilotCliHost();

        Assert.True(host.TakesSessionId);
        Assert.Empty(host.Compose(Revision(), ["copilot"], NoFiles).Allow);
        Assert.Empty(host.Compose(Revision(), ["copilot"], new MemoryFileLocations(@"C:\Users\Marta\Jason\AGENTS.md", null, null)).Allow);
    }

    /// <summary>
    /// The callback is allowed by one rule: the host reads the allow list by commas, so a command word is one rule only
    /// while it holds none, which the profile's validation holds for every host. With the default word and with a word of
    /// dots, dashes and underscores the list carries exactly one shell rule, the one that names the word.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("my_jason-2.exe")]
    public void The_allow_list_carries_exactly_one_shell_rule_naming_the_command_word(string? word)
    {
        var revision = Revision();
        revision.CliCommand = word;

        var shell = Allow(new CopilotCliHost().Compose(revision, ["copilot"], NoFiles)).Where(rule => rule.StartsWith("shell", StringComparison.Ordinal)).ToList();

        Assert.Equal([$"shell({word ?? ProgramResolver.CliCommandFor(null)}:*)"], shell);
    }

    private static List<string> Tools(HostLaunch launch) => Value(launch, "--available-tools");

    private static List<string> Allow(HostLaunch launch) => Value(launch, "--allow-tool");

    private static List<string> Denied(HostLaunch launch) => Value(launch, "--deny-tool");

    /// <summary>The comma-separated words of the one flag of that name, joined to it by an equals sign.</summary>
    private static List<string> Value(HostLaunch launch, string flag)
    {
        var prefix = flag + "=";
        var words = launch.Command.Where(word => word.StartsWith(prefix, StringComparison.Ordinal)).ToList();
        Assert.True(words.Count == 1, $"'{flag}' appears {words.Count} times.");
        return [.. words[0][prefix.Length..].Split(',', StringSplitOptions.RemoveEmptyEntries)];
    }

    private static ExecutionProfileRevision Revision(params string[] args) => new()
    {
        Number = 1,
        Host = AgentHostKind.CopilotCli,
        Program = "copilot",
        Args = [.. args],
    };
}
