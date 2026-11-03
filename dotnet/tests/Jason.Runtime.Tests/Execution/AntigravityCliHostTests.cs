using System.Text.Json;
using System.Text.Json.Nodes;
using Jason.Contracts.Api;
using Jason.Contracts.Execution;
using Jason.Contracts.Hosts;
using Jason.Runtime.Execution.Hosts;
using Jason.Runtime.Persistence;

namespace Jason.Runtime.Tests.Execution;

/// <summary>
/// The shape one headless Antigravity CLI session is started with, and what Jason can and cannot say about it. Built from
/// the host's documentation for version 1.4.10 and not yet run on an account: the rows prove the command Jason composes,
/// the one line of standard input around the brief, and the refusals that follow from the host taking its permission rules
/// from the person's own settings and from nowhere a run could carry them.
/// </summary>
public class AntigravityCliHostTests
{
    private static readonly MemoryFileLocations NoFiles = new(null, null, null);

    private static readonly AntigravityCliHost Host = new();

    [Fact]
    public void The_host_is_found_by_the_directory_only_it_keeps_and_reads_skills_from_its_own_root()
    {
        var facts = AgentHosts.Of(AgentHostKind.AntigravityCli);

        Assert.Equal("agy", facts.CommandWord);
        Assert.Equal(".gemini/antigravity-cli", facts.HomeMarker);
        Assert.Equal(HomeMarkerKind.Directory, facts.MarkerKind);
        Assert.Equal(".gemini/antigravity-cli/skills", facts.HomeSkills);
        Assert.Equal(".agents/skills", facts.WorkSkills);

        // AGENTS.md and GEMINI.md are both read by the host itself, so the harness folder carries no import file for it.
        Assert.Null(facts.Instructions);

        // The folder Gemini CLI shares with it is not a marker: it exists on every install of either.
        Assert.NotEqual(".gemini", facts.HomeMarker);
    }

    /// <summary>
    /// The brief is on standard input as a stream-json line, which the host reads when it is told so and ends on when
    /// the stream ends; no print flag, so no prompt word; and no limit of the host's own, so the attempt's limit is the
    /// only one and a run is never cut short with an exit that looks like a success.
    /// </summary>
    [Fact]
    [Critical(Family.W7, "the brief goes on stdin and no word of it on the command line")]
    public void The_session_reads_a_stream_of_lines_answers_in_one_and_is_limited_by_the_attempt_alone()
    {
        var launch = Host.Compose(Revision(), ["agy"], NoFiles);

        Assert.Equal(
            ["agy", "--input-format", "stream-json", "--output-format", "stream-json", "--print-timeout=0s"],
            launch.Command);
    }

    [Fact]
    public void The_profiles_arguments_come_last_and_nothing_else_is_composed_after_them()
    {
        var launch = Host.Compose(Revision("--model=made-up", "--effort=low"), ["/opt/agy/agy"], NoFiles);

        Assert.Equal("/opt/agy/agy", launch.Command[0]);
        Assert.Equal(["--model=made-up", "--effort=low"], launch.Command.TakeLast(2));
    }

    /// <summary>
    /// What the host would do with the session if it were told: act without asking, widen the workspace, take another
    /// project, resume a conversation, or write a log elsewhere. None is composed and none may be added.
    /// </summary>
    [Fact]
    [Critical(Family.S6, "no flag that widens the launched session is composed, and every such flag is reserved")]
    public void Nothing_that_widens_the_session_is_composed_and_every_such_flag_is_reserved()
    {
        var composed = Host.Compose(Revision(), ["agy"], NoFiles).Command;

        string[] widening =
        [
            "--dangerously-skip-permissions", "--mode", "--sandbox", "--add-dir", "--project", "--new-project",
            "--conversation", "-c", "--continue", "-i", "--prompt-interactive", "-p", "--print", "--prompt",
            "--remote-control", "--log-file", "--agent", "--json-schema",
        ];
        Assert.All(widening, flag =>
        {
            Assert.DoesNotContain(flag, composed);
            Assert.Contains(flag, Host.ReservedFlags);
        });
        Assert.Contains("--print-timeout", Host.ReservedFlags);
        Assert.Contains("--input-format", Host.ReservedFlags);
        Assert.Contains("--output-format", Host.ReservedFlags);

        // The model and the effort are the person's to choose.
        Assert.DoesNotContain("--model", Host.ReservedFlags);
        Assert.DoesNotContain("--effort", Host.ReservedFlags);
    }

    /// <summary>
    /// A word that is not a flag would be read by the host as a prompt in some forms, and its handling beside a
    /// stream of lines is not documented, so an argument is a flag with its value joined by an equals sign, and no
    /// double dash is taken.
    /// </summary>
    [Fact]
    public void An_argument_that_is_a_bare_word_or_a_double_dash_is_refused()
    {
        Assert.Null(Host.ArgsProblem([]));
        Assert.Null(Host.ArgsProblem(["--model=made-up", "--effort=low"]));
        Assert.NotNull(Host.ArgsProblem(["--model", "made-up"]));
        Assert.NotNull(Host.ArgsProblem(["bare"]));
        Assert.NotNull(Host.ArgsProblem(["--"]));
    }

    /// <summary>
    /// The host's parser could not be read, so an argument is taken only if it is one of the documented options the person
    /// owns, in long form with its value joined by an equals sign: the model and the effort. The effort's range is not
    /// checked, since the host's own pages disagree on it, only that a value is there.
    /// </summary>
    [Theory]
    [InlineData("--model=made-up")]
    [InlineData("--effort=low")]
    [InlineData("--effort=max")]
    [InlineData("--effort=whatever-the-host-calls-it")]
    public void An_option_the_person_owns_in_its_documented_long_form_is_taken(string word)
    {
        Assert.Null(Host.ArgsProblem([word]));
    }

    /// <summary>
    /// Every other word is refused with one sentence that names the reason: an option the host documents but the person
    /// does not own, one it does not document, one shaped like a switch that skips permissions, a cluster, a glued value,
    /// and the allowed option without its value.
    /// </summary>
    [Theory]
    [InlineData("--disable-slash-commands")]
    [InlineData("--yolo")]
    [InlineData("--dangerously-bypass-approvals-and-sandbox")]
    [InlineData("--force")]
    [InlineData("-pf")]
    [InlineData("-phello")]
    [InlineData("--model")]
    [InlineData("--model=")]
    [InlineData("--effort")]
    [InlineData("--effort=")]
    [InlineData("--Model=made-up")]
    [InlineData("--model-fast=made-up")]
    public void Any_other_word_is_refused_for_the_reason_that_the_parser_is_not_read(string word)
    {
        var problem = Host.ArgsProblem(["--model=made-up", word]);

        Assert.NotNull(problem);
        Assert.Contains(word, problem, StringComparison.Ordinal);
        Assert.Contains(
            "the host's parser is not read, so only the documented options the person owns are taken",
            problem,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// A value that holds a control character, a line break or a NUL, is never a model or an effort name, and a NUL ends a
    /// command line early on Windows. It is refused with the allowlist's sentence; a space is allowed, as a name may hold one.
    /// </summary>
    [Theory]
    [InlineData("--model=a\nb")]
    [InlineData("--model=a\u0000b")]
    [InlineData("--effort=low\tmax")]
    [InlineData("--model=a\u007fb")]
    public void A_value_with_a_control_character_is_refused_for_the_reason_the_parser_is_not_read(string word)
    {
        var problem = Host.ArgsProblem([word]);

        Assert.NotNull(problem);
        Assert.Contains(
            "the host's parser is not read, so only the documented options the person owns are taken",
            problem,
            StringComparison.Ordinal);
        Assert.Null(Host.ArgsProblem(["--model=a b"]));
    }

    /// <summary>
    /// A flag the runtime composes or the host's tables call dangerous keeps its own refusal, the reserved one, which the
    /// profile rules give; the allowlist does not speak for it a second time.
    /// </summary>
    [Fact]
    public void A_reserved_long_flag_is_left_to_the_reserved_refusal_and_not_refused_twice()
    {
        Assert.Contains("--print-timeout", Host.ReservedFlags);
        Assert.Null(Host.ArgsProblem(["--print-timeout=5m"]));
        Assert.Null(Host.ArgsProblem(["--dangerously-skip-permissions"]));
    }

    /// <summary>
    /// A short flag glued to a value or to another short flag is read by a parser as the flags it expands to, which the
    /// reserved list, matched by whole word, cannot see. So every word of one dash and more than one character, and the
    /// bare dash, is refused, whatever the letters; and for each short flag the host reserves, the words that glue a
    /// letter or a value to it are among them.
    /// </summary>
    [Fact]
    public void A_short_flag_glued_to_another_or_to_a_value_and_the_bare_dash_are_refused()
    {
        foreach (var word in new[] { "-pfoo", "-cp", "-ic", "-xy", "-", "-m=made-up", "-1" })
        {
            Assert.NotNull(Host.ArgsProblem([word]));
        }

        Assert.Null(Host.ArgsProblem(["--model=made-up", "--effort=low"]));

        var reservedShort = Host.ReservedFlags.Where(flag => flag.Length == 2 && flag[0] == '-' && flag[1] != '-').ToList();
        Assert.NotEmpty(reservedShort);
        Assert.All(reservedShort, flag =>
        {
            Assert.NotNull(Host.ArgsProblem([flag + "y"]));
            Assert.NotNull(Host.ArgsProblem(["-y" + flag[1]]));
            Assert.NotNull(Host.ArgsProblem([flag + "value"]));
        });
    }

    /// <summary>
    /// The reserved list is the reference table's flags that the runtime composes or that change the session. The permission
    /// mode flag of another host is on it as a guard: the host's modes page lists passing it as a common mistake and says the
    /// host uses its own mode flag, so it is reserved beside it, and what the host does with it is not stated.
    /// </summary>
    [Fact]
    public void The_permission_mode_flag_is_reserved_as_a_guard_beside_the_hosts_own_mode_flag()
    {
        Assert.Contains("--permission-mode", Host.ReservedFlags);
        Assert.Contains("--mode", Host.ReservedFlags);
    }

    /// <summary>The host mints the conversation's id itself, so the runtime names no session and none is recorded.</summary>
    [Fact]
    public void The_host_mints_its_own_conversation_so_no_session_is_given_or_recorded()
    {
        Assert.False(Host.TakesSessionId);
        Assert.Null(Host.Compose(Revision(), ["agy"], NoFiles).SessionId);
    }

    private static readonly string Base = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "jason-tests", "agy-host"));

    private static string Memory(string level) => Path.Combine(Base, "memory", level, "AGENTS.md");

    private static MemoryFileLocations AllFiles => new(Memory("client"), Memory("project"), Memory("campaign"));

    /// <summary>
    /// What the launch gives the role goes through the work directory: the command word that calls back, each memory file
    /// the launch names as a file, and a tool the person granted by the host's own name; and what it refuses, a tool the
    /// profile does not grant. Nothing of it is on the command line.
    /// </summary>
    [Fact]
    [Critical(Family.S6, "the launch allows the callback, the named memory files and the granted tools, and denies the rest of the tools")]
    public void The_launch_allows_the_callback_the_named_files_and_the_granted_tools_and_denies_the_ones_not_granted()
    {
        var launch = Host.Compose(Revision(tools: [AgentTool.WebFetch], cliCommand: "jason"), ["agy"], AllFiles);

        Assert.Equal(
            ["command(jason)", $"view_file({Memory("client")})", $"view_file({Memory("project")})", $"view_file({Memory("campaign")})", "read_url_content"],
            launch.Allow);
        Assert.Equal(["search_web"], launch.Deny);
        Assert.Equal(["agy", "--input-format", "stream-json", "--output-format", "stream-json", "--print-timeout=0s"], launch.Command);
    }

    [Fact]
    public void A_profile_that_grants_nothing_denies_both_web_tools_and_a_launch_that_names_no_file_allows_the_callback_alone()
    {
        var launch = Host.Compose(Revision(cliCommand: "jason"), ["agy"], NoFiles);

        Assert.Equal(["command(jason)"], launch.Allow);
        Assert.Equal(["read_url_content", "search_web"], launch.Deny.Order());
    }

    /// <summary>The hook approves each memory file as a file, so no named level is left unreadable.</summary>
    [Fact]
    public void No_memory_file_the_launch_names_is_unreadable()
    {
        Assert.Empty(Host.Compose(Revision(), ["agy"], AllFiles).Unreadable);
        Assert.Empty(Host.Compose(Revision(), ["agy"], new(null, null, Memory("campaign"))).Unreadable);
        Assert.Empty(Host.Compose(Revision(), ["agy"], NoFiles).Unreadable);
    }

    private static string HookCommand(string workDir)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(workDir, ".agents", "hooks.json")));
        return document.RootElement.EnumerateObject().Single().Value.GetProperty("PreToolUse")[0].GetProperty("hooks")[0].GetProperty("command").GetString()!;
    }

    /// <summary>
    /// The one file written is the workspace hook: a pre-tool-use hook whose matcher is the hooked set and whose command is
    /// the command word's own hook mode with the rules in base64url, so no path or quote reaches a shell. Nothing is
    /// written anywhere else, and nothing of the launch envelope or the capability token is in it.
    /// </summary>
    [Fact]
    [Critical(Family.S6, "the work directory gets exactly one hook file, whose command line holds the word and the rules and nothing secret")]
    public void The_work_directory_gets_exactly_the_hook_file_with_the_rules_in_its_command()
    {
        var launch = Host.Compose(Revision(tools: [AgentTool.WebSearch], cliCommand: "jason"), ["agy"], AllFiles);
        using var directory = new TempDirectory();

        Host.WriteWorkDirectory(directory.Path, launch.Allow, launch.Deny);

        var files = Directory.GetFiles(directory.Path, "*", SearchOption.AllDirectories).Select(file => Path.GetRelativePath(directory.Path, file).Replace('\\', '/')).ToList();
        Assert.Equal([".agents/hooks.json"], files);

        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory.Path, ".agents", "hooks.json")));
        var hook = Assert.Single(document.RootElement.EnumerateObject());
        var events = Assert.Single(hook.Value.EnumerateObject());
        Assert.Equal("PreToolUse", events.Name);
        var entry = Assert.Single(events.Value.EnumerateArray());
        var handler = Assert.Single(entry.GetProperty("hooks").EnumerateArray());
        Assert.Equal("command", handler.GetProperty("type").GetString());
        Assert.Equal(10, handler.GetProperty("timeout").GetInt32());

        var words = HookCommand(directory.Path).Split(' ');
        Assert.Equal(["jason", "host-hook", "antigravity-cli"], words[..3]);
        Assert.Equal(4, words.Length);
        Assert.Matches("^[A-Za-z0-9_-]+$", words[3]);

        var rules = AntigravityHookRules.Decode(words[3]);
        Assert.NotNull(rules);
        Assert.Equal("jason", rules.Cli);
        Assert.Equal([Memory("client"), Memory("project"), Memory("campaign")], rules.Read);
        Assert.Equal(["search_web"], rules.Approve);
        Assert.Equal(["read_url_content"], rules.Deny);
        Assert.Equal(Path.Combine(directory.Path, "result.json"), rules.Write);
    }

    /// <summary>The matcher holds the tools the hook decides about, any browser tool, and what the profile denies by name, and no other tool.</summary>
    [Fact]
    public void The_matcher_is_the_gated_set_and_the_tools_the_profile_denies_by_name()
    {
        using var directory = new TempDirectory();
        Host.WriteWorkDirectory(directory.Path, ["command(jason)"], ["list_dir", "search_web"]);

        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory.Path, ".agents", "hooks.json")));
        var matcher = document.RootElement.EnumerateObject().Single().Value.GetProperty("PreToolUse")[0].GetProperty("matcher").GetString()!;
        var pattern = new System.Text.RegularExpressions.Regex(matcher);

        string[] hooked =
        [
            "run_command", "view_file", "write_to_file", "replace_file_content", "multi_replace_file_content", "ask_question",
            "read_url_content", "search_web", "browser_navigate", "browser_click", "list_dir",
            "invoke_subagent", "define_subagent", "manage_subagents", "send_message", "schedule", "manage_task", "ask_permission", "generate_image",
        ];
        Assert.All(hooked, tool => Assert.Matches(pattern, tool));
        string[] left = ["find_by_name", "grep_search", "list_permissions", "run_commands", "xrun_command", "my_browser_x", "browser"];
        Assert.All(left, tool => Assert.DoesNotMatch(pattern, tool));
    }

    /// <summary>
    /// The file and the function agree: the command in the file, handed one call of the role's own callback, approves it,
    /// and the one file the role may write is its result.
    /// </summary>
    [Fact]
    public void The_rules_in_the_file_approve_the_callback_a_named_file_and_the_result_and_nothing_else()
    {
        var launch = Host.Compose(Revision(cliCommand: "jason"), ["agy"], new(null, null, Memory("campaign")));
        using var directory = new TempDirectory();
        Host.WriteWorkDirectory(directory.Path, launch.Allow, launch.Deny);
        var rules = HookCommand(directory.Path).Split(' ')[3];

        static string Call(string tool, string name, string value) =>
            new JsonObject { ["toolCall"] = new JsonObject { ["name"] = tool, ["args"] = new JsonObject { [name] = value } } }.ToJsonString();

        Assert.Equal("auto_approve", AntigravityHook.Decide(rules, Call("run_command", "CommandLine", "jason workitem complete wi_1 --attempt att_1 --status succeeded --result-file result.json"), false).Decision);
        Assert.Equal("auto_approve", AntigravityHook.Decide(rules, Call("view_file", "AbsolutePath", Memory("campaign")), false).Decision);
        Assert.Equal("auto_approve", AntigravityHook.Decide(rules, Call("write_to_file", "TargetFile", Path.Combine(directory.Path, "result.json")), false).Decision);
        Assert.Equal("deny", AntigravityHook.Decide(rules, Call("write_to_file", "TargetFile", Path.Combine(directory.Path, ".agents", "hooks.json")), false).Decision);
        Assert.Equal("deny", AntigravityHook.Decide(rules, Call("read_url_content", "Url", "https://example.test/"), false).Decision);
        Assert.Equal("allow", AntigravityHook.Decide(rules, Call("view_file", "AbsolutePath", Memory("client")), false).Decision);
    }

    /// <summary>A fetch and a search have the host's own tool names, which the hook is told to approve or to refuse.</summary>
    [Fact]
    public void A_fetch_and_a_search_have_the_hosts_tool_names()
    {
        Assert.Equal("read_url_content", Host.ToolRule(AgentTool.WebFetch));
        Assert.Equal("search_web", Host.ToolRule(AgentTool.WebSearch));
    }

    /// <summary>A grant is a tool the hook approves, so neither is refused where a profile is written.</summary>
    [Theory]
    [InlineData(AgentTool.WebFetch)]
    [InlineData(AgentTool.WebSearch)]
    public void A_grant_of_either_tool_is_taken(AgentTool tool)
    {
        Assert.Null(Host.GrantProblem(tool));
    }

    /// <summary>A deny is the host's name of a tool; any other word, a rule of the host's own form among them, is refused.</summary>
    [Theory]
    [InlineData("list_dir")]
    [InlineData("grep_search")]
    [InlineData("find_by_name")]
    [InlineData("invoke_subagent")]
    [InlineData("generate_image")]
    [InlineData("read_url_content")]
    [InlineData("search_web")]
    public void A_deny_of_a_tool_name_of_the_hooks_table_is_taken(string word)
    {
        Assert.Null(Host.DenyProblem(word));
    }

    [Theory]
    [InlineData("web_fetch")]
    [InlineData("read_url(*)")]
    [InlineData("command(rm)")]
    [InlineData("write_file(.env)")]
    [InlineData("list_dir(/tmp)")]
    [InlineData("any words at all")]
    [InlineData("")]
    [InlineData("LIST_DIR")]
    [InlineData("browser_.*")]
    public void A_deny_of_any_other_word_is_refused_saying_what_is_taken(string word)
    {
        var problem = Host.DenyProblem(word);

        Assert.NotNull(problem);
        Assert.Contains("tool name", problem, StringComparison.Ordinal);
    }

    /// <summary>The callback and the memory files are the two things a role cannot work without, and both go through these tools, so a profile cannot deny them.</summary>
    [Theory]
    [InlineData("run_command")]
    [InlineData("view_file")]
    [InlineData("write_to_file")]
    public void A_deny_of_the_tool_the_callback_or_the_memory_files_go_through_is_refused_saying_so(string word)
    {
        var problem = Host.DenyProblem(word);

        Assert.NotNull(problem);
        Assert.Contains(word, problem, StringComparison.Ordinal);
        Assert.Contains("callback", problem, StringComparison.Ordinal);
    }

    /// <summary>
    /// The host says it is not signed in when it has no sign-in or one that expired, in words its pages give. An end that
    /// is not one of those, a model error or a command that failed among them, is an ordinary end.
    /// </summary>
    [Theory]
    [InlineData(1, "Error: authentication required")]
    [InlineData(1, "AUTHENTICATION REQUIRED. Run agy to sign in")]
    [InlineData(1, "Authentication expired. Please log back in.")]
    [InlineData(2, "authentication required")]
    public void A_non_zero_end_that_says_the_sign_in_is_missing_or_expired_is_the_hosts_one_sentence(int exit, string tail)
    {
        Assert.Equal(AntigravityCliHost.NotSignedIn, Host.Unavailable(exit, tail));
    }

    [Theory]
    [InlineData(0, "authentication required")]
    [InlineData(1, "")]
    [InlineData(1, null)]
    [InlineData(3, "AGY_ERROR: the model service answered 500")]
    [InlineData(1, "command failed: git push")]
    public void Any_other_end_says_nothing(int exit, string? tail)
    {
        Assert.Null(Host.Unavailable(exit, tail));
    }

    /// <summary>The sentence is Jason's own: not a byte of the host's words, an address or an id of the person's among them.</summary>
    [Fact]
    [Critical(Family.S3, "the not-signed-in sentence carries nothing of what the host wrote")]
    public void The_sentence_carries_nothing_the_tail_said()
    {
        var one = Host.Unavailable(1, "authentication required for person@example.test (plan 7f3a9c-made-up)");
        var other = Host.Unavailable(1, "authentication required for someone.else@example.org (plan 0b11e2-made-up)");

        Assert.Equal(one, other);
        Assert.DoesNotContain("@", one, StringComparison.Ordinal);
        Assert.DoesNotContain("example", one, StringComparison.Ordinal);
        Assert.DoesNotContain("7f3a9c", one, StringComparison.Ordinal);
        Assert.Contains("agy", one!, StringComparison.Ordinal);
    }

    /// <summary>
    /// The one line of standard input: a user event of the stream, the brief as its content, the serializer's escapes and
    /// one line feed at its end and nowhere inside it, however the brief is written.
    /// </summary>
    [Fact]
    public void The_brief_is_one_user_event_line_whose_content_is_the_brief_byte_for_byte()
    {
        const string Brief = "{\"a\":\"say \\\"hi\\\"\",\"b\":\"C:\\\\path\\\\x\",\"c\":\"two\nlines\r\nhere\",\"d\":\"\u00fcn\u00ef \u2014 \u65e5\u672c \ud83d\ude00\"}";

        var framed = Host.FrameStdin(Brief);

        Assert.EndsWith("\n", framed, StringComparison.Ordinal);
        Assert.DoesNotContain('\n', framed[..^1]);
        Assert.DoesNotContain('\r', framed);
        using var line = JsonDocument.Parse(framed);
        Assert.Equal("user", line.RootElement.GetProperty("event").GetString());
        Assert.Equal(Brief, line.RootElement.GetProperty("message").GetProperty("content").GetString());
        Assert.Equal(["event", "message"], line.RootElement.EnumerateObject().Select(property => property.Name));
    }

    private static ExecutionProfileRevision Revision(string[]? args = null, List<AgentTool>? tools = null, string? cliCommand = null) => new()
    {
        Number = 1,
        Host = AgentHostKind.AntigravityCli,
        Program = "agy",
        Args = [.. args ?? []],
        Tools = tools ?? [],
        CliCommand = cliCommand,
    };

    private static ExecutionProfileRevision Revision(params string[] args) => Revision(args, null);

    private sealed class TempDirectory : IDisposable
    {
        public string Path { get; } = Directory.CreateDirectory(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "jason-tests", "agy", Guid.NewGuid().ToString("N"))).FullName;

        public void Dispose()
        {
            try
            {
                Directory.Delete(Path, recursive: true);
            }
            catch (IOException)
            {
                // The temporary tree is the machine's to clear.
            }
        }
    }
}
