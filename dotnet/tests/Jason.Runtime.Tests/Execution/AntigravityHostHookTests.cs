using System.Text.Json;
using System.Text.Json.Nodes;
using Jason.Runtime.Execution.Hosts;

namespace Jason.Runtime.Tests.Execution;

/// <summary>
/// The decision the <c>host-hook</c> mode gives for one Antigravity CLI tool call: a pure function of the rules the work
/// directory's hook file carries and the one JSON document the host writes on standard input. Nothing here starts a
/// process or reads a file; the paths are only compared as text.
/// </summary>
public sealed class AntigravityHostHookTests
{
    private const string Cli = "jason";

    private static readonly string Root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "jason-tests", "hook-rows"));

    private static string WorkDir => Path.Combine(Root, "work");

    private static string ResultFile => Path.Combine(WorkDir, "result.json");

    private static string MemoryFile(string name) => Path.Combine(Root, "memory", name, "AGENTS.md");

    private static string Rules(string[]? approve = null, string[]? deny = null, string cli = Cli) =>
        AntigravityHookRules.Encode(new AntigravityHookRules(cli, [MemoryFile("client"), MemoryFile("campaign")], approve ?? [], deny ?? [], ResultFile));

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

    private static string Command(string line) => Call("run_command", ("CommandLine", line), ("Cwd", WorkDir));

    private static string Decide(string call, string? rules = null, bool windows = false) =>
        AntigravityHook.Decide(rules ?? Rules(), call, windows).Decision;

    [Theory]
    [InlineData("jason workitem complete wi_01 --attempt att_01 --status succeeded --result-file result.json")]
    [InlineData("jason workitem complete wi_01 --attempt att_01 --status succeeded --result '{\"summary\":\"all done, see the notes\"}'")]
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
    [InlineData("jason workitem complete wi_01 --result 'two\nlines'")]
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
        Assert.Equal("deny", Decide(Call("write_to_file", ("TargetFile", "result.json"))));
        Assert.Equal("deny", Decide(Call("write_to_file")));
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
            new StringReader(Command("jason workitem get wi_01")),
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

        var exit = await AntigravityHook.RunAsync(args, new StringReader(Command("jason workitem get wi_01")), stdout, new StringWriter(), TestContext.Current.CancellationToken);

        Assert.Equal(0, exit);
        using var document = JsonDocument.Parse(stdout.ToString());
        Assert.Equal("deny", document.RootElement.GetProperty("decision").GetString());
    }

    private static string Base64Url(string text) =>
        Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(text)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
