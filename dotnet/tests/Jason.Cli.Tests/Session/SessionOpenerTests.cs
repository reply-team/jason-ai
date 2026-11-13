using System.Net;
using System.Text.Json.Nodes;
using Jason.Cli;
using Jason.Cli.Doctor;
using Jason.Cli.Human;
using Jason.Cli.Session;
using Jason.Cli.Tests.Human;
using Jason.Contracts.Api;
using Jason.Contracts.Discovery;
using Jason.Runtime.Tests.Plugins;

namespace Jason.Cli.Tests.Session;

/// <summary>
/// <c>jason session open</c>: the item read by its kind's own get, its folder resolved as <c>--prompt</c> resolves it, the
/// host the preference names found on the search path, and one terminal started with that host and the item's brief as
/// its first input. The runtime is a fake that answers recorded exchanges, the search path a temporary folder of stand-in
/// files, and the terminal a recorder: nothing here starts a terminal or a host.
/// </summary>
public sealed class SessionOpenerTests : IDisposable
{
    private readonly SessionRuntime _run = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose() => _run.Dispose();

    [Fact]
    public async Task An_approval_starts_claude_code_in_the_console_host_at_the_campaigns_folder_with_the_prompt_as_its_first_input()
    {
        var claude = _run.Programs.AddProgram("claude");

        var exit = await _run.Cli("session", "open", "--approval", "apr_1");

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Equal(["approval.get", "campaign.get", "preference.list"], _run.Asked);
        Assert.Equal("{\"approval_id\":\"apr_1\"}", _run.Bodies[0]);
        var launch = Assert.Single(_run.Terminals.Launches);
        var folder = _run.CampaignFolder;
        var prompt = CopyPrompt.Compose(PromptKind.Approval, "apr_1", "cmp_1", "wi_1", new PromptFolder(PromptFolderLevel.Campaign, folder));
        Assert.Equal([_run.ConsoleHost, "--", claude, prompt], launch.Argv);
        Assert.Equal(folder, launch.WorkingDirectory);
        var answer = JsonNode.Parse(_run.Output.ToString())!.AsObject();
        Assert.Equal(
            ["kind", "id", "campaign_id", "folder", "host", "program", "terminal", "started"],
            answer.Select(field => field.Key).ToArray());
        Assert.Equal("approval", (string?)answer["kind"]);
        Assert.Equal("apr_1", (string?)answer["id"]);
        Assert.Equal("cmp_1", (string?)answer["campaign_id"]);
        Assert.Equal("campaign", (string?)answer["folder"]!["level"]);
        Assert.Equal(folder, (string?)answer["folder"]!["path"]);
        Assert.Equal("claude_code", (string?)answer["host"]);
        Assert.Equal(claude, (string?)answer["program"]);
        Assert.Equal("conhost", (string?)answer["terminal"]);
        Assert.True((bool)answer["started"]!);
        Assert.Single(_run.Output.ToString().TrimEnd().Split('\n'));
        Assert.Empty(_run.Error.ToString());
    }

    /// <summary>The brief is the published text byte for byte: what <c>decision get --prompt</c> prints is what the host is given.</summary>
    [Fact]
    public async Task A_question_hands_the_host_the_published_prompt_byte_for_byte()
    {
        _run.Programs.AddProgram("claude");

        var exit = await _run.Cli("session", "open", "--question", "dec_1");

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Equal(["decision.get", "campaign.get", "preference.list"], _run.Asked);
        Assert.Equal("{\"decision_id\":\"dec_1\"}", _run.Bodies[0]);
        var published = File.ReadAllText(Path.Combine(CopyPromptTests.RepositoryRoot(), "docs", "prompts", "question.txt")).Replace("\r\n", "\n", StringComparison.Ordinal).TrimEnd('\n');

        // The published brief names the recorded folder; the answer here names this machine's in its place, and nothing else.
        Assert.Equal(published.Replace(SessionRuntime.RecordedFolder, _run.CampaignFolder, StringComparison.Ordinal), Assert.Single(_run.Terminals.Launches).Argv[^1]);
        Assert.Contains("\"kind\":\"question\",\"id\":\"dec_1\"", _run.Output.ToString(), StringComparison.Ordinal);
    }

    public static TheoryData<string, string, string?> Hosts() => new()
    {
        { "claude_code", "claude", null },
        { "codex", "codex", null },
        { "copilot_cli", "copilot", "-i" },
        { "cursor_cli", "agent", null },
        { "gemini_cli", "gemini", "-i" },
        { "antigravity_cli", "agy", "-i" },
    };

    /// <summary>The table above is every host this build knows, by the word the preference holds, and no other.</summary>
    [Fact]
    public void The_host_table_is_every_host_this_build_knows()
    {
        var words = Enum.GetValues<AgentHostKind>().Select(host => System.Text.Json.JsonSerializer.Serialize(host, Jason.Contracts.Json.JasonJson.Options).Trim('"'));

        Assert.Equal(words.Order(StringComparer.Ordinal), Hosts().Select(row => row.Data.Item1).Order(StringComparer.Ordinal));
    }

    /// <summary>Each host word starts that host's program, found by its command word, with the prompt in that host's own form.</summary>
    [Theory]
    [MemberData(nameof(Hosts))]
    public async Task The_preference_names_the_host_and_each_host_takes_the_prompt_by_its_own_form(string word, string command, string? flag)
    {
        _run.Preference = "{\"host\":\"" + word + "\"}";
        var program = _run.Programs.AddProgram(command);

        var exit = await _run.Cli("session", "open", "--approval", "apr_1");

        Assert.Equal(ExitCodes.Success, exit);
        var argv = Assert.Single(_run.Terminals.Launches).Argv;
        var host = argv.Skip(2).ToArray();
        Assert.Equal(program, host[0]);
        Assert.Equal(flag is null ? 2 : 3, host.Length);
        if (flag is not null)
        {
            Assert.Equal(flag, host[1]);
        }

        Assert.StartsWith("Jason is waiting", host[^1], StringComparison.Ordinal);
        Assert.Contains("\"host\":\"" + word + "\"", _run.Output.ToString(), StringComparison.Ordinal);
    }

    /// <summary>npm's shim is read for the interpreter and entry script it names, as every program Jason starts by name is.</summary>
    [Fact]
    public async Task A_host_npm_installed_starts_through_the_interpreter_its_shim_names()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Only Windows has npm's cmd shim.");
        _run.Preference = "{\"host\":\"gemini_cli\"}";
        var node = _run.Programs.Add("node.exe", executable: true);
        var entry = _run.Programs.AddNested(@"node_modules\gemini-cli\dist\index.js", "// the entry point");
        _run.Programs.AddNested(@"node_modules\gemini-cli\package.json", "{\"name\":\"gemini-cli\",\"bin\":{\"gemini\":\"dist/index.js\"}}");
        _run.Programs.Add("gemini.cmd", TestPrograms.NpmShim("gemini-cli", @"dist\index.js"));

        var exit = await _run.Cli("session", "open", "--approval", "apr_1");

        Assert.Equal(ExitCodes.Success, exit);
        var argv = Assert.Single(_run.Terminals.Launches).Argv;
        Assert.Equal(node, Path.GetFullPath(argv[2]));
        Assert.Equal(entry, Path.GetFullPath(argv[3]));
        Assert.Equal("-i", argv[4]);
        Assert.Contains("\"program\":" + Quoted(node), _run.Output.ToString(), StringComparison.Ordinal);
    }

    /// <summary>A host npm installed under a scope is read as an unscoped one is, through the same lookup.</summary>
    [Fact]
    public async Task A_host_npm_installed_under_a_scope_starts_through_the_interpreter_its_shim_names()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Only Windows has npm's cmd shim.");
        _run.Preference = "{\"host\":\"codex\"}";
        var node = _run.Programs.Add("node.exe", executable: true);
        var entry = _run.Programs.AddNested(@"node_modules\@openai\codex\bin\codex.js", "// the entry point");
        _run.Programs.AddNested(@"node_modules\@openai\codex\package.json", "{\"name\":\"@openai/codex\",\"bin\":{\"codex\":\"bin/codex.js\"}}");
        _run.Programs.Add("codex.cmd", TestPrograms.NpmShim(@"@openai\codex", @"bin\codex.js"));

        var exit = await _run.Cli("session", "open", "--approval", "apr_1");

        Assert.Equal(ExitCodes.Success, exit);
        var argv = Assert.Single(_run.Terminals.Launches).Argv;
        Assert.Equal(node, Path.GetFullPath(argv[2]));
        Assert.Equal(entry, Path.GetFullPath(argv[3]));
        Assert.StartsWith("Jason is waiting", argv[^1], StringComparison.Ordinal);
    }

    /// <summary>
    /// Claude Code's package ships an executable, and npm's shim for it starts that executable with no interpreter: the
    /// executable is what starts, never the shim.
    /// </summary>
    [Fact]
    public async Task Claude_code_installed_by_npm_starts_the_executable_its_package_ships()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Only Windows has npm's cmd shim.");
        var executable = _run.Programs.AddNested(@"node_modules\@anthropic-ai\claude-code\bin\claude.exe", "not a real program");
        _run.Programs.AddNested(
            @"node_modules\@anthropic-ai\claude-code\package.json", "{\"name\":\"@anthropic-ai/claude-code\",\"bin\":{\"claude\":\"bin/claude.exe\"}}");
        _run.Programs.Add("claude.cmd", TestPrograms.NpmNativeShim(@"@anthropic-ai\claude-code", @"bin\claude.exe"));

        var exit = await _run.Cli("session", "open", "--approval", "apr_1");

        Assert.Equal(ExitCodes.Success, exit);
        var argv = Assert.Single(_run.Terminals.Launches).Argv;
        Assert.Equal(executable, Path.GetFullPath(argv[2]));
        Assert.Equal(4, argv.Count);
        Assert.StartsWith("Jason is waiting", argv[^1], StringComparison.Ordinal);
        Assert.Contains("\"program\":" + Quoted(executable), _run.Output.ToString(), StringComparison.Ordinal);
    }

    /// <summary>Cursor CLI's launcher script is read for the program it starts, by the reading the runtime uses.</summary>
    [Fact]
    public async Task Cursor_clis_launcher_is_read_for_the_program_it_starts()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Only Windows has Cursor CLI's launcher script.");
        _run.Preference = "{\"host\":\"cursor_cli\"}";
        _run.Programs.Add("agent.cmd", "@echo off");
        _run.Programs.Add("cursor-agent.ps1", "# the launcher");
        var node = _run.Programs.AddNested(@"versions\2026.10.22-27dcf5f\node.exe", "not a real program");
        var index = _run.Programs.AddNested(@"versions\2026.10.22-27dcf5f\index.js", "// the entry point");

        var exit = await _run.Cli("session", "open", "--approval", "apr_1");

        Assert.Equal(ExitCodes.Success, exit);
        var argv = Assert.Single(_run.Terminals.Launches).Argv;
        Assert.Equal([node, index], argv.Skip(2).Take(2));
        Assert.StartsWith("Jason is waiting", argv[^1], StringComparison.Ordinal);
    }

    /// <summary>--dry-run starts nothing and writes nothing, and prints the same object with the whole command, prompt included.</summary>
    [Fact]
    public async Task A_dry_run_starts_nothing_and_prints_the_whole_command()
    {
        var claude = _run.Programs.AddProgram("claude");

        var exit = await _run.Cli("session", "open", "--approval", "apr_1", "--dry-run");

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Empty(_run.Terminals.Launches);
        var printed = JsonNode.Parse(_run.Output.ToString())!.AsObject();
        Assert.False(printed["started"]!.GetValue<bool>());
        var argv = printed["argv"]!.AsArray().Select(word => word!.GetValue<string>()).ToArray();
        Assert.Equal([_run.ConsoleHost, "--", claude], argv.Take(3));
        Assert.StartsWith("Jason is waiting for a person's decision on an approval: apr_1", argv[^1], StringComparison.Ordinal);
        Assert.False(printed.ContainsKey("script"));
    }

    /// <summary>On macOS a dry run prints the script Terminal would run, and the command names it by its file name.</summary>
    [Fact]
    public async Task A_dry_run_on_macos_prints_the_script()
    {
        _run.Terminals.Platform = TerminalPlatform.MacOs;
        _run.Programs.AddProgram("claude");

        var exit = await _run.Cli("session", "open", "--approval", "apr_1", "--dry-run");

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Empty(_run.Terminals.Launches);
        var printed = JsonNode.Parse(_run.Output.ToString())!.AsObject();
        Assert.Equal("Terminal", printed["terminal"]!.GetValue<string>());
        Assert.Equal(["/usr/bin/open", "-a", "Terminal", "jason-apr_1.command"], printed["argv"]!.AsArray().Select(word => word!.GetValue<string>()));
        var script = printed["script"]!.GetValue<string>();
        Assert.StartsWith("#!/bin/sh\nrm -f -- \"$0\"\n", script, StringComparison.Ordinal);
        Assert.Contains("cd -- '" + _run.CampaignFolder + "' || exit 1", script, StringComparison.Ordinal);
        Assert.DoesNotContain("JASON_DATA_DIR", script, StringComparison.Ordinal);
    }

    /// <summary>
    /// A CLI that runs with <c>JASON_DATA_DIR</c> has the macOS script export the data directory it uses, so the host's own
    /// <c>jason</c> commands reach this installation and not the default one; a blank value is no data directory, as it
    /// is to the CLI itself.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task On_macos_the_script_carries_the_data_directory_only_when_the_cli_runs_with_one(bool set)
    {
        _run.Terminals.Platform = TerminalPlatform.MacOs;
        _run.Programs.AddProgram("claude");
        _run.Variables[JasonPaths.DataDirectoryVariable] = set ? _run.DataDirectory : "  ";

        var exit = await _run.Cli("session", "open", "--approval", "apr_1", "--dry-run");

        Assert.Equal(ExitCodes.Success, exit);
        var script = JsonNode.Parse(_run.Output.ToString())!["script"]!.GetValue<string>();
        var exports = script.Split('\n').Where(line => line.StartsWith("export ", StringComparison.Ordinal)).ToArray();
        string[] expected = set ? ["export JASON_DATA_DIR=" + TerminalPlan.ShellQuote(_run.DataDirectory)] : [];
        Assert.Equal(expected, exports);
    }

    /// <summary>
    /// On Linux the terminal is the one the <c>TERMINAL</c> variable names, found on the search path, given the host in
    /// that program's own form: kitty takes it as its positional arguments.
    /// </summary>
    [Fact]
    public async Task On_linux_the_terminal_the_terminal_variable_names_starts_the_host_at_the_campaigns_folder()
    {
        _run.Terminals.Platform = TerminalPlatform.Linux;
        var claude = _run.Programs.AddProgram("claude");
        var kitty = _run.Programs.Add("kitty", executable: true);
        _run.Variables["TERMINAL"] = "kitty";

        var exit = await _run.Cli("session", "open", "--approval", "apr_1");

        Assert.Equal(ExitCodes.Success, exit);
        var launch = Assert.Single(_run.Terminals.Launches);
        var prompt = CopyPrompt.Compose(PromptKind.Approval, "apr_1", "cmp_1", "wi_1", new PromptFolder(PromptFolderLevel.Campaign, _run.CampaignFolder));
        Assert.Equal("kitty", launch.Terminal);
        Assert.Equal([kitty, claude, prompt], launch.Argv);
        Assert.Equal(_run.CampaignFolder, launch.WorkingDirectory);
        Assert.Null(launch.Script);
    }

    [Fact]
    public async Task Human_says_what_was_started_and_where()
    {
        _run.Programs.AddProgram("claude");

        var exit = await _run.Cli("session", "open", "--approval", "apr_1", "--human");

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Equal("Started claude_code in conhost at " + _run.CampaignFolder + ".", _run.Output.ToString().TrimEnd());
    }

    /// <summary>
    /// No folder recorded anywhere: the host starts in the person's home folder, never in the folder the CLI happens to run
    /// in, which for a click is whatever the shell gave.
    /// </summary>
    [Fact]
    public async Task With_no_folder_the_host_starts_in_the_home_folder()
    {
        _run.Programs.AddProgram("claude");
        _run.Campaign = CopyPromptTests.Fixture("campaign.get", "one");

        var exit = await _run.Cli("session", "open", "--approval", "apr_1");

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Equal(_run.Home, Assert.Single(_run.Terminals.Launches).WorkingDirectory);
        Assert.Contains("\"folder\":null", _run.Output.ToString(), StringComparison.Ordinal);
        _run.Output.GetStringBuilder().Clear();

        Assert.Equal(ExitCodes.Success, await _run.Cli("session", "open", "--approval", "apr_1", "--human"));
        Assert.Equal("Started claude_code in conhost at your home folder.", _run.Output.ToString().TrimEnd());
    }

    /// <summary>No folder recorded and no home folder named: there is nowhere to start, said in its own line and the way by hand.</summary>
    [Fact]
    public async Task With_no_folder_and_no_home_folder_nothing_is_started()
    {
        _run.Programs.AddProgram("claude");
        _run.Campaign = CopyPromptTests.Fixture("campaign.get", "one");
        _run.GivenHome = "";

        var exit = await _run.Cli("session", "open", "--approval", "apr_1");

        Assert.Equal(ExitCodes.ApiError, exit);
        Assert.Empty(_run.Terminals.Launches);
        Assert.Empty(_run.Output.ToString());
        var said = _run.Error.ToString().TrimEnd().Split('\n');
        Assert.Equal(2, said.Length);
        Assert.Equal(
            "No folder is recorded for the approval's campaign and this account names no home folder, so there is nowhere to start claude_code and nothing was started.",
            said[0].TrimEnd('\r'));
        Assert.Contains("jason approval get apr_1 --prompt", said[1], StringComparison.Ordinal);
    }

    /// <summary>
    /// The forms the UI's Open an AI session link and the command shown beside it take (written by
    /// <c>ui/src/screens/attention/session-link.tsx</c>) are this build's: the command parses through the program's own
    /// parser to <c>session open</c>, and the link is the one the table the click is read with writes and reads, so a
    /// renamed option or a moved row turns this red rather than a dead link on the page. That the UI writes these forms is
    /// its own screen test's to hold (<c>session-link.screen.test.tsx</c>).
    /// </summary>
    [Theory]
    [InlineData("approval", "approvals", "apr_01JBZ3X7Q9K2M4N6P8R0S2T4V6")]
    [InlineData("question", "questions", "dec_01JBZ3X7Q9K2M4N6P8R0S2T4V6")]
    public void The_uis_session_link_and_command_are_ones_this_build_takes(string kind, string collection, string id)
    {
        var command = $"jason session open --{kind} {id}";
        using var paths = new TempPaths();
        var root = CliApp.BuildRootCommand(new CliEnvironment(TextWriter.Null, TextWriter.Null, paths.Paths));
        var parsed = root.Parse(command.Split(' ').Skip(1).ToArray());
        Assert.Empty(parsed.Errors);
        Assert.Equal("open", parsed.CommandResult.Command.Name);
        Assert.Equal("session", parsed.CommandResult.Command.Parents.OfType<System.CommandLine.Command>().First().Name);

        Assert.Equal($"jason://attention/{collection}/{id}/session", Jason.Contracts.Notifications.AttentionLinks.Session(id));
        Assert.Equal(Jason.Contracts.Notifications.AttentionAct.Session, Jason.Contracts.Notifications.AttentionLinks.Parse($"jason://attention/{collection}/{id}/session")?.Act);
    }

    [Theory]
    [InlineData]
    [InlineData("--approval", "apr_1", "--question", "dec_1")]
    public async Task Neither_or_both_of_the_items_is_a_usage_error_that_asks_nothing(params string[] items)
    {
        var exit = await _run.Cli(["session", "open", .. items]);

        Assert.Equal(ExitCodes.Usage, exit);
        Assert.Empty(_run.Asked);
        Assert.Empty(_run.Terminals.Launches);
    }

    /// <summary>An empty id is a usage error like a missing one, said in the CLI's own words, and nothing is asked.</summary>
    [Theory]
    [InlineData("--approval")]
    [InlineData("--question")]
    public async Task An_empty_id_is_a_usage_error_that_asks_nothing(string option)
    {
        var exit = await _run.Cli("session", "open", option, "");

        Assert.Equal(ExitCodes.Usage, exit);
        Assert.Empty(_run.Asked);
        Assert.Empty(_run.Terminals.Launches);
        Assert.Contains(option + " was given an empty id", _run.Error.ToString(), StringComparison.Ordinal);
    }

    /// <summary>A decided item has nothing for a session to propose: refused in one line, and its brief is still printed by --prompt.</summary>
    [Theory]
    [InlineData("approved")]
    [InlineData("rejected")]
    public async Task An_item_that_no_longer_waits_is_refused_in_one_line(string status)
    {
        _run.Programs.AddProgram("claude");
        var decided = JsonNode.Parse(_run.Item!)!;
        decided["status"] = status;
        _run.Item = decided.ToJsonString();

        var exit = await _run.Cli("session", "open", "--approval", "apr_1");

        Assert.Equal(ExitCodes.ApiError, exit);
        Assert.Equal(["approval.get"], _run.Asked);
        Assert.Empty(_run.Terminals.Launches);
        Assert.Empty(_run.Output.ToString());
        var said = Assert.Single(_run.Error.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries));
        Assert.Contains("Nothing waits on this approval", said, StringComparison.Ordinal);
        Assert.Contains(status, said, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_item_the_runtime_does_not_know_is_its_refusal_and_starts_nothing()
    {
        _run.Item = null;

        var exit = await _run.Cli("session", "open", "--question", "dec_9");

        Assert.Equal(ExitCodes.ApiError, exit);
        Assert.Equal(["decision.get"], _run.Asked);
        Assert.Empty(_run.Terminals.Launches);
        Assert.Contains("\"code\":\"not_found\"", _run.Output.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task No_runtime_exits_3_and_starts_nothing()
    {
        _run.Descriptor = false;

        var exit = await _run.Cli("session", "open", "--approval", "apr_1");

        Assert.Equal(ExitCodes.RuntimeUnavailable, exit);
        Assert.Empty(_run.Terminals.Launches);
    }

    /// <summary>A word a newer runtime's catalog accepted and this build does not know is named, with the words it knows.</summary>
    [Fact]
    public async Task A_host_word_this_build_does_not_know_is_refused_naming_the_ones_it_knows()
    {
        _run.Programs.AddProgram("claude");
        _run.Preference = "{\"host\":\"tomorrows_host\"}";

        var exit = await _run.Cli("session", "open", "--approval", "apr_1");

        Assert.Equal(ExitCodes.ApiError, exit);
        Assert.Empty(_run.Terminals.Launches);
        var said = _run.Error.ToString();
        Assert.Contains("'tomorrows_host'", said, StringComparison.Ordinal);
        Assert.Contains(Jason.Contracts.Hosts.AgentHosts.Vocabulary, said, StringComparison.Ordinal);
        Assert.Contains("jason approval get apr_1 --prompt", said, StringComparison.Ordinal);
    }

    /// <summary>The person named a host, so a host that is not installed is refused and never replaced by another.</summary>
    [Fact]
    public async Task A_host_that_is_not_on_the_search_path_is_refused_and_never_replaced()
    {
        _run.Programs.AddProgram("claude");
        _run.Preference = "{\"host\":\"codex\"}";

        var exit = await _run.Cli("session", "open", "--approval", "apr_1");

        Assert.Equal(ExitCodes.ApiError, exit);
        Assert.Empty(_run.Terminals.Launches);
        var said = _run.Error.ToString();
        Assert.Contains("codex's program 'codex' is not on this machine's search path", said, StringComparison.Ordinal);
        Assert.Contains("jason approval get apr_1 --prompt", said, StringComparison.Ordinal);

        // A preference is a person's own word, so the line names the person, as the verb requires. The value goes in a
        // file, the one form every shell passes whole: Windows PowerShell 5.1 and cmd.exe break the inline JSON's quotes.
        Assert.Contains(
            "put {\"host\":\"<host>\"} in a file, host.json say, and run `jason preference set agent.interactive_host --value-file host.json --actor human:<your-name>`; "
            + "or go back to claude_code with `jason preference unset agent.interactive_host --actor human:<your-name>`.",
            said,
            StringComparison.Ordinal);
        Assert.DoesNotContain("--value '", said, StringComparison.Ordinal);
    }

    /// <summary>A host whose shim names an executable the disk does not hold is named by its shim, not as a name nothing answers for.</summary>
    [Fact]
    public async Task A_host_whose_npm_shim_names_an_executable_that_is_not_there_is_named_by_its_shim()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Only Windows has npm's cmd shim.");
        _run.Programs.AddNested(
            @"node_modules\@anthropic-ai\claude-code\package.json", "{\"name\":\"@anthropic-ai/claude-code\",\"bin\":{\"claude\":\"bin/claude.exe\"}}");
        var shim = _run.Programs.Add("claude.cmd", TestPrograms.NpmNativeShim(@"@anthropic-ai\claude-code", @"bin\claude.exe"));

        var exit = await _run.Cli("session", "open", "--approval", "apr_1");

        Assert.Equal(ExitCodes.ApiError, exit);
        Assert.Empty(_run.Terminals.Launches);
        Assert.Contains(
            $"claude_code's program 'claude' resolves to {shim}, npm's shim, whose executable is not there, so nothing was started.",
            _run.Error.ToString(),
            StringComparison.Ordinal);
    }

    /// <summary>
    /// A host that is installed but found only as a shim Jason refuses is not "not on the search path": the line names the
    /// file and the rule it broke, as <c>jason status</c> does, so the person knows what to put right.
    /// </summary>
    [Fact]
    public async Task A_host_found_only_as_a_refused_shim_is_refused_by_the_file_and_its_rule()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Only Windows has npm's cmd shim.");
        _run.Preference = "{\"host\":\"gemini_cli\"}";
        _run.Programs.Add("node.exe", executable: true);
        var shim = _run.Programs.Add("gemini.cmd", TestPrograms.NpmShim("..", @"dist\index.js"));

        var exit = await _run.Cli("session", "open", "--approval", "apr_1");

        Assert.Equal(ExitCodes.ApiError, exit);
        Assert.Empty(_run.Terminals.Launches);
        var said = _run.Error.ToString();
        Assert.DoesNotContain("is not on this machine's search path", said, StringComparison.Ordinal);
        Assert.Contains("gemini_cli's program 'gemini' resolves only to ", said, StringComparison.Ordinal);
        Assert.Contains(Path.GetFileName(shim), said, StringComparison.Ordinal);

        // A shim whose package climbs out of node_modules is refused by a rule that stays, whatever else becomes readable.
        Assert.Contains("which names a package whose name is not one npm could have written, so nothing was started", said, StringComparison.Ordinal);
        Assert.Contains("jason approval get apr_1 --prompt", said, StringComparison.Ordinal);
    }

    [Fact]
    public async Task No_terminal_is_refused_with_the_way_to_do_it_by_hand()
    {
        _run.Programs.AddProgram("claude");
        File.Delete(_run.ConsoleHost);

        var exit = await _run.Cli("session", "open", "--approval", "apr_1");

        Assert.Equal(ExitCodes.ApiError, exit);
        Assert.Empty(_run.Terminals.Launches);
        Assert.Contains(
            "No terminal was found to start claude_code in. In a terminal of your own, at " + _run.CampaignFolder + ", run `jason approval get apr_1 --prompt` and give the brief to your host.",
            _run.Error.ToString(),
            StringComparison.Ordinal);
    }

    /// <summary>
    /// A folder the runtime recorded that is not on this machine any more is refused before anything starts: a terminal
    /// started there would open somewhere else, or nowhere. The line names the path and the repair.
    /// </summary>
    [Fact]
    public async Task A_recorded_folder_that_is_not_there_is_refused_before_the_start()
    {
        _run.Programs.AddProgram("claude");
        _run.Campaign = CopyPromptTests.Fixture("campaign.get", "in-project-with-folder");

        var exit = await _run.Cli("session", "open", "--approval", "apr_1");

        Assert.Equal(ExitCodes.ApiError, exit);
        Assert.Empty(_run.Terminals.Launches);
        Assert.Empty(_run.Output.ToString());
        var said = _run.Error.ToString().TrimEnd().Split('\n');
        Assert.Equal(2, said.Length);
        Assert.Equal(
            "The campaign's folder recorded at " + SessionRuntime.RecordedFolder + " is not there, so nothing was started; if it was moved, `jason project link --path <the project's folder, where it is now>` records it and its campaigns again.",
            said[0].TrimEnd('\r'));
        Assert.Contains("jason approval get apr_1 --prompt", said[1], StringComparison.Ordinal);
    }

    /// <summary>
    /// An id in the runtime's answer that no script may be named from ends the verb with one line and exit 1, never a
    /// stack trace, and nothing is written or started.
    /// </summary>
    [Fact]
    public async Task A_malformed_id_in_the_answer_is_refused_in_one_line()
    {
        _run.Terminals.Platform = TerminalPlatform.MacOs;
        _run.Programs.AddProgram("claude");
        _run.Item = CopyPromptTests.Fixture("approval.get", "pending-with-preview").Replace("\"id\":\"apr_1\"", "\"id\":\"apr_1;x\"", StringComparison.Ordinal);

        var exit = await _run.Cli("session", "open", "--approval", "apr_1");

        Assert.Equal(ExitCodes.ApiError, exit);
        Assert.Empty(_run.Terminals.Launches);
        Assert.Empty(_run.Output.ToString());
        var said = _run.Error.ToString().TrimEnd();
        Assert.Equal("The runtime's answer names the approval by an id no script may be named from, so nothing was started.", said);
    }

    /// <summary>Where nothing is set up to start a terminal, which is every environment but the real one, nothing is started.</summary>
    [Fact]
    public async Task No_terminal_starter_starts_nothing_and_exits_1()
    {
        _run.Programs.AddProgram("claude");

        var exit = await _run.Cli(terminals: false, "session", "open", "--approval", "apr_1");

        Assert.Equal(ExitCodes.ApiError, exit);
        Assert.Contains("No terminal was found", _run.Error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_terminal_that_does_not_start_is_refused_saying_why()
    {
        _run.Programs.AddProgram("claude");
        _run.Terminals.Refusal = "the file is not a program";

        var exit = await _run.Cli("session", "open", "--approval", "apr_1");

        Assert.Equal(ExitCodes.ApiError, exit);
        Assert.Contains("conhost did not start (the file is not a program)", _run.Error.ToString(), StringComparison.Ordinal);
        Assert.Contains("jason approval get apr_1 --prompt", _run.Error.ToString(), StringComparison.Ordinal);
        Assert.Empty(_run.Output.ToString());
    }

    /// <summary>The preference read failing is a refusal: the host it would have named is not guessed.</summary>
    [Fact]
    public async Task A_preference_read_the_runtime_refuses_starts_nothing()
    {
        _run.Programs.AddProgram("claude");
        _run.PreferencesAnswer = false;

        var exit = await _run.Cli("session", "open", "--approval", "apr_1");

        Assert.Equal(ExitCodes.ApiError, exit);
        Assert.Empty(_run.Terminals.Launches);
    }

    /// <summary>A preference list that is not JSON is no answer either: nothing is started.</summary>
    [Fact]
    public async Task A_preference_list_that_is_not_json_starts_nothing()
    {
        _run.Programs.AddProgram("claude");
        _run.PreferencesBody = "<html>not the runtime</html>";

        var exit = await _run.Cli("session", "open", "--approval", "apr_1");

        Assert.Equal(ExitCodes.ApiError, exit);
        Assert.Equal(["approval.get", "campaign.get", "preference.list"], _run.Asked);
        Assert.Empty(_run.Terminals.Launches);
    }

    private static string Quoted(string text) => System.Text.Json.JsonSerializer.Serialize(text);

    /// <summary>A runtime that answers the recorded exchanges a session takes, and a machine of temporary folders.</summary>
    internal sealed class SessionRuntime : IDisposable
    {
        private static readonly RuntimeDescriptor Live = new("v1", "0.1.0-dev", "rt_LIVE", 77, "http://127.0.0.1:5000", "the-token", DateTimeOffset.UnixEpoch);

        private readonly TempPaths _dir = new();

        private readonly string _system;

        public SessionRuntime()
        {
            _system = Path.Combine(_dir.Paths.Root, "System32");
            Directory.CreateDirectory(_system);
            ConsoleHost = Path.Combine(_system, "conhost.exe");
            File.WriteAllBytes(ConsoleHost, []);
            Home = Path.Combine(_dir.Paths.Root, "home");
            Terminals = new RecordingTerminals { SystemDirectory = _system };

            // The recorded exchange names a folder under a home of its own; a start needs one that is there, so the answer
            // names a real one in its place, written with forward slashes so the JSON needs no escape.
            CampaignFolder = Path.Combine(_dir.Paths.Root, "Jason", "projects", "acme-corp", "campaigns", "q3-leads").Replace('\\', '/');
            Directory.CreateDirectory(CampaignFolder);
            Campaign = CopyPromptTests.Fixture("campaign.get", "in-project-with-folder").Replace(RecordedFolder, CampaignFolder, StringComparison.Ordinal);
        }

        /// <summary>The data directory the CLI under test runs on.</summary>
        public string DataDirectory => _dir.Paths.Root;

        /// <summary>The campaign's folder as the recorded exchange names it.</summary>
        public const string RecordedFolder = "~/Jason/projects/acme-corp/campaigns/q3-leads";

        /// <summary>The campaign's folder this machine has, which the default answer names.</summary>
        public string CampaignFolder { get; }

        public TestPrograms Programs { get; } = new();

        public RecordingTerminals Terminals { get; }

        public string ConsoleHost { get; }

        public string Home { get; }

        /// <summary>The home folder the account's identity names, when a row gives one other than <see cref="Home"/>.</summary>
        public string? GivenHome { get; set; }

        public string? Item { get; set; } = CopyPromptTests.Fixture("approval.get", "pending-with-preview");

        public string? Campaign { get; set; }

        /// <summary>The value of <c>agent.interactive_host</c>, or null for a key that is unset.</summary>
        public string? Preference { get; set; }

        public bool PreferencesAnswer { get; set; } = true;

        /// <summary>The preference list's body as it is sent, in place of the one composed from <see cref="Preference"/>.</summary>
        public string? PreferencesBody { get; set; }

        /// <summary>The environment variables the verb reads, none unless a row names one.</summary>
        public Dictionary<string, string> Variables { get; } = [];

        public bool Descriptor { get; set; } = true;

        public List<string> Asked { get; } = [];

        public List<string> Bodies { get; } = [];

        public StringWriter Output { get; } = new();

        public StringWriter Error { get; } = new();

        public Task<int> Cli(params string[] args) => Cli(terminals: true, args);

        public Task<int> Cli(bool terminals, params string[] args) => CliApp.RunAsync(args, Environment(terminals), Ct);

        /// <summary>A click on a link, as the shell starts the executable with it.</summary>
        public Task<int> Link(string link) => Jason.Cli.Link.LinkHandler.RunAsync([link], Environment(terminals: true), Ct);

        private CliEnvironment Environment(bool terminals)
        {
            if (Descriptor)
            {
                _dir.WriteDescriptor(Live);
            }

            var handler = new FakeHandler(request =>
            {
                var operation = request.RequestUri!.AbsolutePath.Split('/')[^1];
                Asked.Add(operation);
                Bodies.Add(request.Content?.ReadAsStringAsync(CancellationToken.None).GetAwaiter().GetResult() ?? string.Empty);
                var answer = operation switch
                {
                    "approval.get" => Item,
                    "decision.get" => Item is null ? null : CopyPromptTests.Fixture("decision.get", "pending-with-options"),
                    "campaign.get" => Campaign,
                    "preference.list" => PreferencesAnswer ? PreferencesBody ?? Preferences() : null,
                    _ => null,
                };
                return answer is null
                    ? new HttpResponseMessage(HttpStatusCode.NotFound) { Content = new StringContent("{\"error\":{\"code\":\"not_found\",\"message\":\"Not found.\",\"retryable\":false}}") }
                    : new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(answer, System.Text.Encoding.UTF8, "application/json") };
            });
            return new CliEnvironment(
                Output,
                Error,
                _dir.Paths,
                handler,
                SearchPath: Programs.Root,
                Identity: new IdentityFacts(GivenHome ?? Home, "marta", "DESK-1", null, null),
                Variables: Variables,
                Terminals: terminals ? Terminals : null);
        }

        private string Preferences()
        {
            var value = Preference is null ? "null" : Preference;
            return "{\"items\":[{\"key\":\"agent.interactive_host\",\"value\":" + value + ",\"value_hash\":null,\"value_bytes\":null,\"created_at\":null,\"updated_at\":null,\"updated_by\":null},"
                + "{\"key\":\"tone_of_voice\",\"value\":null,\"value_hash\":null,\"value_bytes\":null,\"created_at\":null,\"updated_at\":null,\"updated_by\":null}],\"next_cursor\":null}";
        }

        public void Dispose()
        {
            Programs.Dispose();
            _dir.Dispose();
        }
    }

    /// <summary>A terminal that records what it was asked to start and starts nothing.</summary>
    internal sealed class RecordingTerminals : ITerminalStarter
    {
        public TerminalPlatform Platform { get; set; } = TerminalPlatform.Windows;

        public string? SystemDirectory { get; set; }

        public string? Refusal { get; set; }

        public List<TerminalLaunch> Launches { get; } = [];

        public bool Start(TerminalLaunch launch, out string? problem)
        {
            problem = Refusal;
            if (Refusal is null)
            {
                Launches.Add(launch);
            }

            return Refusal is null;
        }
    }
}
