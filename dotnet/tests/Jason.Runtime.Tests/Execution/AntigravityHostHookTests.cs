using System.Text.Json;
using System.Text.Json.Nodes;
using Jason.Runtime.Execution.Hosts;

namespace Jason.Runtime.Tests.Execution;

/// <summary>
/// The decision the <c>host-hook</c> mode gives for one Antigravity CLI tool call: a function of the rules the work
/// directory's hook file carries and the one JSON document the host writes on standard input, and of one look at what
/// stands at the result path. The paths are compared as text; the rows about that one look make what stands there in a
/// temporary folder of their own.
/// </summary>
public sealed class AntigravityHostHookTests
{
    private const string Cli = "jason";

    private static readonly string Root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "jason-tests", "hook-rows"));

    private static string WorkDir => Path.Combine(Root, "work");

    private static string ResultFile => Path.Combine(WorkDir, "result.json");

    private static string MemoryFile(string name) => Path.Combine(Root, "memory", name, "AGENTS.md");

    private static string Rules(string[]? approve = null, string[]? deny = null, string cli = Cli, string? write = null) =>
        AntigravityHookRules.Encode(new AntigravityHookRules(cli, [MemoryFile("client"), MemoryFile("campaign")], approve ?? [], deny ?? [], write ?? ResultFile));

    /// <summary>The error Windows gives a symbolic link it refuses for want of a privilege: <c>ERROR_PRIVILEGE_NOT_HELD</c>.</summary>
    private const int PrivilegeNotHeld = unchecked((int)0x80070522);

    /// <summary>A work directory of its own in a temporary tree, and the decision for a write of its result file.</summary>
    private static string WriteOfTheResult(TempTree tree, Action<string> plant)
    {
        var work = tree.NewDirectory("work");
        var result = Path.Combine(work, "result.json");
        plant(result);
        return Decide(CallWith("write_to_file", new JsonObject { ["TargetFile"] = result, ["Overwrite"] = true }), Rules(write: result), OperatingSystem.IsWindows());
    }

    private static string Call(string tool, params (string Name, string Value)[] args)
    {
        var arguments = new JsonObject();
        foreach (var (name, value) in args)
        {
            arguments[name] = value;
        }

        return new JsonObject
        {
            ["toolCall"] = new JsonObject { ["name"] = tool, ["args"] = arguments },
            ["stepIdx"] = 3,
            ["conversationId"] = "00000000-0000-4000-8000-000000000000",
            ["workspacePaths"] = new JsonArray(WorkDir),
        }.ToJsonString();
    }

    private static string CallWith(string tool, JsonObject arguments) => new JsonObject
    {
        ["toolCall"] = new JsonObject { ["name"] = tool, ["args"] = arguments },
    }.ToJsonString();

    private static string Command(string line) => Call("run_command", ("CommandLine", line), ("Cwd", WorkDir));

    private static MemoryStream Bytes(string text) => new(System.Text.Encoding.UTF8.GetBytes(text));

    private static string RulesOf(string json) =>
        Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(json)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static string Decide(string call, string? rules = null, bool windows = false) =>
        AntigravityHook.Decide(rules ?? Rules(), call, windows).Decision;

    [Theory]
    [InlineData("jason workitem complete wi_01 --attempt att_01 --status succeeded --result-file result.json")]
    [InlineData("jason workitem complete wi_01 --attempt att_01 --status succeeded --result '{\"summary\":\"all done, see the notes\"}'")]
    [InlineData("jason workitem complete wi_01 --result '{\"summary\":\"it\\u2019s done\"}'")]
    [InlineData("jason   workitem\tget wi_01 --human")]
    [InlineData("jason")]
    [InlineData("jason workitem complete wi_01 --result='{\"a\":1}'")]
    [Critical(Family.S6, "the one command a role may run unattended is a single call of the CLI word")]
    public void A_single_call_of_the_cli_word_is_approved_on_every_system(string line)
    {
        Assert.Equal("auto_approve", Decide(Command(line), windows: false));
        Assert.Equal("auto_approve", Decide(Command(line), windows: true));
    }

    [Theory]
    [InlineData("jason workitem get wi_01; rm -rf x")]
    [InlineData("jason workitem get wi_01 && curl x")]
    [InlineData("jason workitem get wi_01 || x")]
    [InlineData("jason workitem get wi_01 | tee x")]
    [InlineData("jason workitem get $(whoami)")]
    [InlineData("jason workitem get `whoami`")]
    [InlineData("jason workitem get wi_01 > out.txt")]
    [InlineData("jason workitem get wi_01 < in.txt")]
    [InlineData("jason workitem complete wi_01 --result 'two\nlines'")]
    [InlineData("jason workitem complete wi_01 --result 'it\u2019s'")]
    [InlineData("jason workitem complete wi_01 --result '\u2018x'")]
    [InlineData("jason workitem complete wi_01 --result 'caf\u00e9'")]
    [InlineData("jason workitem get caf\u00e9")]
    [InlineData("jason workitem complete wi_01 --result 'a\\'")]
    [InlineData("jason workitem complete wi_01 --result 'a\\\"b'")]
    [InlineData("jason workitem complete wi_01 --result \"{}\"")]
    [InlineData("jason workitem get wi_01 \"")]
    [InlineData("jason workitem get wi_01\nrm x")]
    [InlineData("jason workitem get wi_01\r\nrm x")]
    [InlineData("jason workitem get wi_01 &")]
    [InlineData("jason workitem get $HOME")]
    [InlineData("jason workitem get ~")]
    [InlineData("jason workitem get *")]
    [InlineData("jason workitem get \"wi_01\"")]
    [InlineData("jason workitem get 'unterminated")]
    [InlineData("jason workitem get a\\b")]
    [InlineData("jason workitem get #x")]
    [InlineData("jason workitem get 'a'b'c")]
    [InlineData("jasonx workitem get wi_01")]
    [InlineData("/other/jason workitem get wi_01")]
    [InlineData("./jason workitem get wi_01")]
    [InlineData("'jason' workitem get wi_01")]
    [InlineData("env jason workitem get wi_01")]
    [InlineData("rm jason")]
    [InlineData("")]
    [InlineData("   ")]
    [Critical(Family.S6, "anything but a single plain call of the CLI word is handed to the person's own rules")]
    public void Anything_that_is_not_one_plain_call_is_delegated_to_the_persons_rules(string line)
    {
        Assert.Equal("allow", Decide(Command(line), windows: false));
        Assert.Equal("allow", Decide(Command(line), windows: true));
    }

    [Fact]
    public void The_word_is_compared_exactly_and_without_case_only_on_windows()
    {
        Assert.Equal("allow", Decide(Command("JASON workitem get wi_01"), windows: false));
        Assert.Equal("auto_approve", Decide(Command("JASON workitem get wi_01"), windows: true));
        Assert.Equal("allow", Decide(Command("jason workitem get wi_01"), Rules(cli: "other"), windows: false));
    }

    [Fact]
    public void A_word_that_is_not_a_bare_token_approves_nothing()
    {
        var rules = Rules(cli: "C:/Program Files/jason");

        Assert.Equal("allow", Decide(Command("C:/Program Files/jason workitem get x"), rules));
        Assert.Equal("allow", Decide(Command("C:/Program"), rules));
    }

    /// <summary>Windows' shell does not take a single quote as one, so the characters it reads as syntax are refused wherever they stand.</summary>
    [Theory]
    [InlineData("jason workitem complete wi_01 --result '{\"a\":\"x & y\"}'")]
    [InlineData("jason workitem complete wi_01 --result 'a | b'")]
    [InlineData("jason workitem complete wi_01 --result 'a > b'")]
    [InlineData("jason workitem complete wi_01 --result 'a < b'")]
    [InlineData("jason workitem complete wi_01 --result 'a ^ b'")]
    [InlineData("jason workitem complete wi_01 --result '100%'")]
    [InlineData("jason workitem complete wi_01 --result 'done!'")]
    public void A_quoted_span_that_windows_would_read_as_syntax_is_delegated_there_and_approved_elsewhere(string line)
    {
        Assert.Equal("allow", Decide(Command(line), windows: true));
        Assert.Equal("auto_approve", Decide(Command(line), windows: false));
    }

    [Fact]
    public void A_view_of_a_named_memory_file_is_approved_and_any_other_path_is_delegated()
    {
        Assert.Equal("auto_approve", Decide(Call("view_file", ("AbsolutePath", MemoryFile("client")))));
        Assert.Equal("auto_approve", Decide(Call("view_file", ("AbsolutePath", MemoryFile("campaign")), ("StartLine", "1"))));
        Assert.Equal("allow", Decide(Call("view_file", ("AbsolutePath", MemoryFile("project")))));
        Assert.Equal("allow", Decide(Call("view_file", ("AbsolutePath", Path.Combine(Root, "memory", "client", "AGENTS.md.bak")))));
        Assert.Equal("allow", Decide(Call("view_file", ("AbsolutePath", Path.Combine(Root, "memory", "client", "..", "client", "..", "other", "x")))));
        Assert.Equal("allow", Decide(Call("view_file", ("AbsolutePath", "AGENTS.md"))));
        Assert.Equal("allow", Decide(Call("view_file", ("AbsolutePath", ""))));
        Assert.Equal("deny", Decide(Call("view_file")));
    }

    [Fact]
    public void A_path_is_compared_after_it_is_normalised_so_a_dot_dot_form_of_a_named_file_is_the_file()
    {
        var detour = Path.Combine(Root, "memory", "client", "..", "client", "AGENTS.md");

        Assert.Equal("auto_approve", Decide(Call("view_file", ("AbsolutePath", detour))));
    }

    [Fact]
    public void Case_and_slash_variants_of_a_named_file_are_the_file_only_on_windows()
    {
        var upper = MemoryFile("client").ToUpperInvariant();

        Assert.Equal("auto_approve", Decide(Call("view_file", ("AbsolutePath", upper)), windows: true));
        if (!OperatingSystem.IsWindows())
        {
            Assert.Equal("allow", Decide(Call("view_file", ("AbsolutePath", upper)), windows: false));
        }
    }

    [Fact]
    [Critical(Family.S6, "a role writes exactly one file, its result, and nothing else")]
    public void The_one_write_a_role_may_make_is_the_result_file_of_its_work_directory()
    {
        Assert.Equal("auto_approve", Decide(Call("write_to_file", ("TargetFile", ResultFile), ("CodeContent", "{}"))));

        Assert.Equal("deny", Decide(Call("write_to_file", ("TargetFile", Path.Combine(WorkDir, ".agents", "hooks.json")))));
        Assert.Equal("deny", Decide(Call("write_to_file", ("TargetFile", Path.Combine(WorkDir, "result.json.bak")))));
        Assert.Equal("deny", Decide(Call("write_to_file", ("TargetFile", Path.Combine(WorkDir, "sub", "result.json")))));
        Assert.Equal("deny", Decide(Call("write_to_file", ("TargetFile", Path.Combine(WorkDir, "..", "result.json")))));
        Assert.Equal("deny", Decide(Call("write_to_file", ("TargetFile", MemoryFile("client")))));
        Assert.Equal("deny", Decide(Call("write_to_file", ("TargetFile", "sub/result.json"))));
        Assert.Equal("deny", Decide(Call("write_to_file")));
    }

    /// <summary>A relative path is the work directory's, which is where the host runs.</summary>
    [Fact]
    public void A_relative_path_of_the_result_is_read_against_the_work_directory()
    {
        Assert.Equal("auto_approve", Decide(Call("write_to_file", ("TargetFile", "result.json"))));
        Assert.Equal("auto_approve", Decide(Call("write_to_file", ("TargetFile", "./result.json"))));
        Assert.Equal("deny", Decide(Call("write_to_file", ("TargetFile", "../result.json"))));
    }

    /// <summary>
    /// A role whose first result the runtime refuses writes the file again, so the result file is approved with or without
    /// <c>Overwrite</c>: the path pins the target and the hook refuses a link or a directory there, so a planted link
    /// gains nothing but a race against the role's own write. Every other write, overwriting or not, stays denied.
    /// </summary>
    [Fact]
    [Critical(Family.S6, "the result file is written as often as the role needs, and no other file is")]
    public void A_second_write_of_the_result_file_is_approved()
    {
        Assert.Equal("auto_approve", Decide(CallWith("write_to_file", new JsonObject { ["TargetFile"] = ResultFile, ["Overwrite"] = false })));
        Assert.Equal("auto_approve", Decide(CallWith("write_to_file", new JsonObject { ["TargetFile"] = ResultFile, ["Overwrite"] = true })));
        Assert.Equal("auto_approve", Decide(CallWith("write_to_file", new JsonObject { ["TargetFile"] = "result.json", ["Overwrite"] = true })));
        Assert.Equal("deny", Decide(CallWith("write_to_file", new JsonObject { ["TargetFile"] = Path.Combine(WorkDir, "other.json"), ["Overwrite"] = true })));
        Assert.Equal("deny", Decide(CallWith("write_to_file", new JsonObject { ["TargetFile"] = Path.Combine(WorkDir, "other.json"), ["Overwrite"] = false })));
    }

    /// <summary>Nothing at the result path, or a plain file the role wrote before: the write is approved.</summary>
    [Fact]
    [Critical(Family.S6, "the result file is written where nothing or a plain file stands at its path")]
    public void Nothing_or_a_plain_file_at_the_result_path_is_written()
    {
        using var tree = new TempTree();

        Assert.Equal("auto_approve", WriteOfTheResult(tree, _ => { }));
        Assert.Equal("auto_approve", WriteOfTheResult(tree, result => File.WriteAllText(result, "{\"summary\":\"a first try\"}")));
    }

    /// <summary>A directory at the result path is not the result file, and a write there is denied.</summary>
    [Fact]
    public void A_directory_at_the_result_path_is_denied()
    {
        using var tree = new TempTree();

        Assert.Equal("deny", WriteOfTheResult(tree, result => Directory.CreateDirectory(result)));
    }

    /// <summary>A symbolic link at the result path would carry the host's write to wherever it points, so it is denied.</summary>
    [Fact]
    [Critical(Family.S6, "a link at the result path is never written through")]
    public void A_symbolic_link_at_the_result_path_is_denied()
    {
        using var tree = new TempTree();
        var elsewhere = Path.Combine(tree.NewDirectory("elsewhere"), "settings.json");
        File.WriteAllText(elsewhere, "{}");

        var linked = true;
        var decision = WriteOfTheResult(tree, result => linked = TryLink(result, elsewhere));
        Assert.SkipUnless(linked, NoLinkPrivilege);

        Assert.Equal("deny", decision);
    }

    /// <summary>
    /// A symbolic link whose target is not there is a link all the same: the host's write would make the file it points
    /// to, so the link is refused before anything else about the entry is read.
    /// </summary>
    [Fact]
    public void A_dangling_symbolic_link_at_the_result_path_is_denied()
    {
        using var tree = new TempTree();
        var missing = Path.Combine(tree.NewDirectory("elsewhere"), "missing.json");

        var linked = true;
        var decision = WriteOfTheResult(tree, result => linked = TryLink(result, missing));
        Assert.SkipUnless(linked, NoLinkPrivilege);

        Assert.Equal("deny", decision);
    }

    private const string NoLinkPrivilege = "A file symbolic link needs a privilege this account does not hold (ERROR_PRIVILEGE_NOT_HELD).";

    /// <summary>Makes a file symbolic link, or answers false where this Windows account may not make one, and only there.</summary>
    private static bool TryLink(string link, string target)
    {
        try
        {
            File.CreateSymbolicLink(link, target);
            return true;
        }
        catch (IOException refused) when (OperatingSystem.IsWindows() && refused.HResult == PrivilegeNotHeld)
        {
            return false;
        }
    }

    /// <summary>A directory junction at the result path is a link too, and the one Windows makes without a privilege.</summary>
    [Fact]
    public void A_junction_at_the_result_path_is_denied()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "A directory junction is made only on Windows.");
        using var tree = new TempTree();
        var elsewhere = tree.NewDirectory("elsewhere");

        var junction = true;
        var decision = WriteOfTheResult(tree, result => junction = Plugins.ExecutableResolverTests.TryJunction(result, elsewhere));
        Assert.SkipUnless(junction, "this machine would not create a directory junction.");

        Assert.Equal("deny", decision);
    }

    /// <summary>A command is approved only as a foreground one in the work directory.</summary>
    [Fact]
    [Critical(Family.S6, "the callback is a foreground command of the work directory")]
    public void A_command_that_persists_or_runs_elsewhere_is_delegated()
    {
        JsonObject Args(string name, JsonNode? value) => new() { ["CommandLine"] = "jason workitem get wi_01", [name] = value };

        Assert.Equal("auto_approve", Decide(CallWith("run_command", new JsonObject { ["CommandLine"] = "jason workitem get wi_01" })));
        Assert.Equal("auto_approve", Decide(CallWith("run_command", Args("RunPersistent", false))));
        Assert.Equal("auto_approve", Decide(CallWith("run_command", Args("Cwd", WorkDir))));
        Assert.Equal("allow", Decide(CallWith("run_command", Args("RunPersistent", true))));
        Assert.Equal("allow", Decide(CallWith("run_command", Args("RunPersistent", "false"))));
        Assert.Equal("allow", Decide(CallWith("run_command", Args("Cwd", Path.Combine(Root, "elsewhere")))));
        Assert.Equal("allow", Decide(CallWith("run_command", Args("Cwd", null))));
    }

    [Fact]
    public void A_slash_form_of_the_result_and_of_a_memory_file_is_the_file_only_on_windows()
    {
        var slashed = ResultFile.Replace('\\', '/');

        Assert.Equal("auto_approve", Decide(Call("write_to_file", ("TargetFile", slashed)), windows: true));
        Assert.Equal("auto_approve", Decide(Call("view_file", ("AbsolutePath", MemoryFile("client").Replace('\\', '/'))), windows: true));
        if (!OperatingSystem.IsWindows())
        {
            // Elsewhere a backslash is a character of a name, not a separator, so the other slash is another file.
            static string Backslashed(string path) => "/" + path[1..].Replace('/', '\\');

            Assert.Equal("deny", Decide(Call("write_to_file", ("TargetFile", Backslashed(ResultFile))), windows: false));
            Assert.Equal("allow", Decide(Call("view_file", ("AbsolutePath", Backslashed(MemoryFile("client")))), windows: false));
        }
    }

    [Theory]
    [InlineData("invoke_subagent")]
    [InlineData("define_subagent")]
    [InlineData("manage_subagents")]
    [InlineData("send_message")]
    [InlineData("schedule")]
    [InlineData("manage_task")]
    [InlineData("ask_permission")]
    [InlineData("generate_image")]
    [Critical(Family.S6, "a sub-agent, a message, a schedule, a task, a permission request and an image are never allowed to a role")]
    public void A_tool_that_outlives_the_attempt_or_asks_for_more_is_denied_whatever_it_names(string tool)
    {
        Assert.Equal("deny", Decide(Call(tool)));
        Assert.Equal("deny", Decide(Call(tool), Rules(approve: [tool])));
    }

    [Theory]
    [InlineData("replace_file_content")]
    [InlineData("multi_replace_file_content")]
    [InlineData("ask_question")]
    [Critical(Family.S6, "an edit of a file and a question to the person are never allowed to a role")]
    public void An_edit_of_a_file_and_a_question_are_denied_whatever_they_name(string tool)
    {
        Assert.Equal("deny", Decide(Call(tool, ("TargetFile", ResultFile))));
        Assert.Equal("deny", Decide(Call(tool), Rules(approve: [tool])));
    }

    [Fact]
    public void A_fetch_is_approved_where_granted_and_denied_where_not()
    {
        var call = Call("read_url_content", ("Url", "https://example.test/page"));

        Assert.Equal("auto_approve", Decide(call, Rules(approve: ["read_url_content"])));
        Assert.Equal("deny", Decide(call, Rules()));
        Assert.Equal("deny", Decide(call, Rules(approve: ["read_url_content"], deny: ["read_url_content"])));
    }

    [Fact]
    public void A_search_is_approved_where_granted_and_denied_where_not()
    {
        var call = Call("search_web", ("query", "made-up words"));

        Assert.Equal("auto_approve", Decide(call, Rules(approve: ["search_web"])));
        Assert.Equal("deny", Decide(call, Rules()));
    }

    /// <summary>A browser the host drives loads pages too, so it goes with the fetch: refused unless the fetch is granted, and then left to the person's rules.</summary>
    [Fact]
    public void The_browser_tools_follow_the_fetch()
    {
        var call = Call("browser_navigate", ("Url", "https://example.test/"));

        Assert.Equal("deny", Decide(call, Rules()));
        Assert.Equal("allow", Decide(call, Rules(approve: ["read_url_content"])));
        Assert.Equal("deny", Decide(call, Rules(approve: ["read_url_content"], deny: ["read_url_content"])));
    }

    [Fact]
    public void A_tool_the_profile_denies_by_name_is_denied_and_any_other_is_delegated()
    {
        var rules = Rules(deny: ["list_dir", "grep_search"]);

        Assert.Equal("deny", Decide(Call("list_dir", ("DirectoryPath", WorkDir)), rules));
        Assert.Equal("deny", Decide(Call("grep_search", ("Query", "x")), rules));
        Assert.Equal("allow", Decide(Call("find_by_name", ("Pattern", "*")), rules));
    }

    [Fact]
    public void A_denied_name_wins_over_an_approved_one()
    {
        Assert.Equal("deny", Decide(Command("jason workitem get wi_01"), Rules(deny: ["run_command"])));
        Assert.Equal("deny", Decide(Call("view_file", ("AbsolutePath", MemoryFile("client"))), Rules(deny: ["view_file"])));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("{}")]
    [InlineData("{\"toolCall\":null}")]
    [InlineData("{\"toolCall\":{\"name\":42}}")]
    [InlineData("{\"toolCall\":{\"name\":\"\"}}")]
    [InlineData("{\"toolCall\":{\"name\":\"run_command\"}}")]
    [InlineData("{\"toolCall\":{\"name\":\"run_command\",\"args\":[]}}")]
    [InlineData("{\"toolCall\":{\"name\":\"run_command\",\"args\":{\"CommandLine\":7}}}")]
    [Critical(Family.S6, "an input the hook cannot read is a refusal, never a pass")]
    public void An_input_that_cannot_be_read_is_denied(string input)
    {
        Assert.Equal("deny", Decide(input));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("!!!not-base64!!!")]
    [InlineData("e30")]
    [InlineData("bm90IGpzb24")]
    [Critical(Family.S6, "rules the hook cannot read are a refusal, never a pass")]
    public void Rules_that_cannot_be_read_deny_every_call(string? rules)
    {
        Assert.Equal("deny", Decide(Command("jason workitem get wi_01"), rules ?? "", windows: false));
        Assert.Equal("deny", Decide(Call("view_file", ("AbsolutePath", MemoryFile("client"))), rules ?? ""));
    }

    [Fact]
    public void Rules_of_a_version_this_build_does_not_know_deny_every_call()
    {
        var unknown = Base64Url("{\"v\":2,\"cli\":\"jason\",\"read\":[],\"approve\":[],\"deny\":[],\"write\":\"x\"}");

        Assert.Equal("deny", Decide(Command("jason workitem get wi_01"), unknown));
    }

    [Fact]
    public void Rules_with_a_member_of_the_wrong_kind_deny_every_call()
    {
        var wrong = Base64Url("{\"v\":1,\"cli\":\"jason\",\"read\":\"x\",\"approve\":[],\"deny\":[],\"write\":\"x\"}");

        Assert.Equal("deny", Decide(Command("jason workitem get wi_01"), wrong));
    }

    [Fact]
    public void The_rules_decode_back_to_what_was_encoded_and_the_text_is_url_safe_without_padding()
    {
        var rules = new AntigravityHookRules("jason", [MemoryFile("client")], ["search_web"], ["list_dir"], ResultFile);

        var text = AntigravityHookRules.Encode(rules);
        var back = AntigravityHookRules.Decode(text);

        Assert.NotNull(back);
        Assert.Equal(rules.Cli, back.Cli);
        Assert.Equal(rules.Read, back.Read);
        Assert.Equal(rules.Approve, back.Approve);
        Assert.Equal(rules.Deny, back.Deny);
        Assert.Equal(rules.Write, back.Write);
        Assert.Matches("^[A-Za-z0-9_-]+$", text);
    }

    /// <summary>Whatever the arguments hold, no byte of them is in the answer: the reason is one fixed sentence per decision.</summary>
    [Fact]
    [Critical(Family.S3, "the hook's answer carries nothing of the call it was asked about")]
    public void The_answer_is_one_line_of_json_whose_reason_is_fixed_and_carries_nothing_of_the_call()
    {
        const string Marker = "marker-7f3a9c-made-up";
        string[] calls =
        [
            Command("jason workitem get " + Marker),
            Command("rm -rf " + Marker),
            Call("view_file", ("AbsolutePath", "/" + Marker)),
            Call("write_to_file", ("TargetFile", "/" + Marker)),
            Call("read_url_content", ("Url", "https://example.test/" + Marker)),
            Call("browser_" + Marker, ("Url", Marker)),
            Call(Marker),
            "{" + Marker,
        ];

        foreach (var call in calls)
        {
            var answer = AntigravityHook.Answer(Rules(), call, windows: false);

            Assert.DoesNotContain(Marker, answer, StringComparison.Ordinal);
            Assert.DoesNotContain('\n', answer);
            using var document = JsonDocument.Parse(answer);
            Assert.Equal(["decision", "reason"], document.RootElement.EnumerateObject().Select(property => property.Name));
            Assert.False(string.IsNullOrWhiteSpace(document.RootElement.GetProperty("reason").GetString()));
        }

        Assert.Equal(AntigravityHook.Answer(Rules(), calls[0], false), AntigravityHook.Answer(Rules(), Command("jason workitem get other"), false));
    }

    [Fact]
    public async Task The_mode_reads_one_call_on_stdin_and_writes_one_answer_and_exits_zero()
    {
        var stdout = new StringWriter();
        var stderr = new StringWriter();

        var exit = await AntigravityHook.RunAsync(
            ["antigravity-cli", Rules()],
            Bytes(Command("jason workitem get wi_01")),
            stdout,
            stderr,
            TestContext.Current.CancellationToken);

        Assert.Equal(0, exit);
        using var document = JsonDocument.Parse(stdout.ToString());
        Assert.Equal("auto_approve", document.RootElement.GetProperty("decision").GetString());
        Assert.Equal(string.Empty, stderr.ToString());
    }

    [Theory]
    [InlineData("")]
    [InlineData("antigravity-cli")]
    [InlineData("other-host e30")]
    [InlineData("antigravity-cli e30 extra")]
    public async Task A_command_line_the_mode_does_not_know_is_denied_and_still_exits_zero(string words)
    {
        var args = words.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var stdout = new StringWriter();

        var exit = await AntigravityHook.RunAsync(args, Bytes(Command("jason workitem get wi_01")), stdout, new StringWriter(), TestContext.Current.CancellationToken);

        Assert.Equal(0, exit);
        using var document = JsonDocument.Parse(stdout.ToString());
        Assert.Equal("deny", document.RootElement.GetProperty("decision").GetString());
    }

    private static string Base64Url(string text) =>
        Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(text)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    // A lone surrogate escape is no text at all: reading the string throws, and the hook answers rather than dies.
    [Theory]
    [InlineData("{\"toolCall\":{\"name\":\"run_command\\uD800\",\"args\":{\"CommandLine\":\"jason\"}}}")]
    [InlineData("{\"toolCall\":{\"name\":\"run_command\",\"args\":{\"CommandLine\":\"jason \\uD800\"}}}")]
    [InlineData("{\"toolCall\":{\"name\":\"write_to_file\",\"args\":{\"TargetFile\":\"\\uD800\"}}}")]
    [InlineData("{\"toolCall\":{\"name\":\"view_file\",\"args\":{\"AbsolutePath\":\"\\uDC00x\"}}}")]
    [Critical(Family.S6, "a call the hook cannot read is refused, never a crash")]
    public async Task A_lone_surrogate_in_the_call_is_refused_and_the_mode_still_answers(string call)
    {
        Assert.Equal("deny", Decide(call));

        var stdout = new StringWriter();
        var exit = await AntigravityHook.RunAsync(["antigravity-cli", Rules()], Bytes(call), stdout, new StringWriter(), TestContext.Current.CancellationToken);

        Assert.Equal(0, exit);
        using var document = JsonDocument.Parse(stdout.ToString());
        Assert.Equal("deny", document.RootElement.GetProperty("decision").GetString());
    }

    [Fact]
    [Critical(Family.S6, "a call the hook cannot read is refused, never a crash")]
    public async Task A_lone_surrogate_in_the_rules_is_refused_and_the_mode_still_answers()
    {
        var rules = RulesOf("{\"v\":1,\"cli\":\"jason\\uD800\",\"read\":[],\"approve\":[],\"deny\":[],\"write\":\"x\"}");
        Assert.Equal("deny", Decide(Command("jason workitem get wi_01"), rules));

        var stdout = new StringWriter();
        var exit = await AntigravityHook.RunAsync(["antigravity-cli", rules], Bytes(Command("jason workitem get wi_01")), stdout, new StringWriter(), TestContext.Current.CancellationToken);

        Assert.Equal(0, exit);
        using var document = JsonDocument.Parse(stdout.ToString());
        Assert.Equal("deny", document.RootElement.GetProperty("decision").GetString());
    }

    // Two keys the hook reads, equal without regard to case: readers disagree on which wins, so neither is trusted.
    [Theory]
    [InlineData("{\"toolCall\":{\"name\":\"run_command\",\"args\":{\"commandLine\":\"jason workitem get wi_01\",\"CommandLine\":\"rm -rf x\"}}}")]
    [InlineData("{\"toolCall\":{\"name\":\"run_command\",\"args\":{\"CommandLine\":\"jason workitem get wi_01\",\"CommandLine\":\"rm -rf x\"}}}")]
    [InlineData("{\"toolCall\":{\"name\":\"run_command\",\"name\":\"list_dir\",\"args\":{\"CommandLine\":\"jason workitem get wi_01\"}}}")]
    [InlineData("{\"toolCall\":{\"name\":\"run_command\",\"args\":{\"CommandLine\":\"jason workitem get wi_01\"},\"Args\":{}}}")]
    [InlineData("{\"toolCall\":{\"name\":\"run_command\",\"args\":{\"CommandLine\":\"jason workitem get wi_01\"}},\"toolcall\":{}}")]
    [InlineData("{\"toolCall\":{\"name\":\"run_command\",\"args\":{\"CommandLine\":\"jason workitem get wi_01\",\"Other\":{\"x\":1,\"X\":2}}}}")]
    [InlineData("{\"toolCall\":{\"name\":\"run_command\",\"args\":{\"CommandLine\":\"jason workitem get wi_01\",\"Other\":[{\"x\":1,\"x\":2}]}}}")]
    [InlineData("{\"toolCall\":{\"name\":\"run_command\",\"args\":{\"CommandLine\":\"jason workitem get wi_01\"}},\"extra\":{\"a\":{\"b\":1,\"B\":1}}}")]
    [Critical(Family.S6, "a key read two ways is not read")]
    public void A_key_the_hook_reads_that_appears_twice_makes_the_call_unreadable(string call) =>
        Assert.Equal("deny", Decide(call));

    [Theory]
    [InlineData("{\"v\":1,\"v\":1,\"cli\":\"jason\",\"read\":[],\"approve\":[],\"deny\":[],\"write\":\"x\"}")]
    [InlineData("{\"v\":1,\"cli\":\"jason\",\"CLI\":\"rm\",\"read\":[],\"approve\":[],\"deny\":[],\"write\":\"x\"}")]
    [InlineData("{\"v\":1,\"cli\":\"jason\",\"read\":[],\"approve\":[],\"approve\":[\"read_url_content\"],\"deny\":[],\"write\":\"x\"}")]
    [Critical(Family.S6, "a key read two ways is not read")]
    public void A_key_the_rules_read_that_appears_twice_makes_them_unreadable(string json) =>
        Assert.Null(AntigravityHookRules.Decode(RulesOf(json)));

    [Fact]
    [Critical(Family.S6, "a byte-order mark before the call does not make it unreadable")]
    public async Task A_byte_order_mark_before_the_call_is_dropped_and_the_call_is_decided()
    {
        var body = System.Text.Encoding.UTF8.GetBytes(Command("jason workitem get wi_01"));
        var withMark = new MemoryStream([0xEF, 0xBB, 0xBF, .. body]);
        var stdout = new StringWriter();

        await AntigravityHook.RunAsync(["antigravity-cli", Rules()], withMark, stdout, new StringWriter(), TestContext.Current.CancellationToken);

        using var document = JsonDocument.Parse(stdout.ToString());
        Assert.Equal("auto_approve", document.RootElement.GetProperty("decision").GetString());
    }

    [Fact]
    [Critical(Family.S6, "the call is read as UTF-8 bytes, whatever the console code page is")]
    public async Task A_path_with_letters_outside_ascii_is_matched_as_the_host_wrote_it()
    {
        var path = Path.Combine(Root, "Прівіт-ü", "work", "result.json");
        var rules = AntigravityHookRules.Encode(new AntigravityHookRules(Cli, [], [], [], path));
        var stdout = new StringWriter();

        await AntigravityHook.RunAsync(["antigravity-cli", rules], Bytes(Call("write_to_file", ("TargetFile", path))), stdout, new StringWriter(), TestContext.Current.CancellationToken);

        using var document = JsonDocument.Parse(stdout.ToString());
        Assert.Equal("auto_approve", document.RootElement.GetProperty("decision").GetString());
    }

    [Theory]
    [InlineData(new byte[] { 0x7B, 0xFF, 0x7D })]
    [InlineData(new byte[] { 0xC3 })]
    [Critical(Family.S6, "the call is read as UTF-8 bytes, whatever the console code page is")]
    public async Task Bytes_that_are_not_utf8_are_refused(byte[] bytes)
    {
        var stdout = new StringWriter();

        var exit = await AntigravityHook.RunAsync(["antigravity-cli", Rules()], new MemoryStream(bytes), stdout, new StringWriter(), TestContext.Current.CancellationToken);

        Assert.Equal(0, exit);
        using var document = JsonDocument.Parse(stdout.ToString());
        Assert.Equal("deny", document.RootElement.GetProperty("decision").GetString());
    }

    [Fact]
    public async Task A_stream_longer_than_the_bound_is_refused()
    {
        var stdout = new StringWriter();
        var big = new MemoryStream(new byte[AntigravityHook.MaxInput + 1]);

        await AntigravityHook.RunAsync(["antigravity-cli", Rules()], big, stdout, new StringWriter(), TestContext.Current.CancellationToken);

        using var document = JsonDocument.Parse(stdout.ToString());
        Assert.Equal("deny", document.RootElement.GetProperty("decision").GetString());
    }
}
