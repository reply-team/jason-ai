using System.Text.Json.Nodes;
using Jason.Cli;
using Jason.Cli.Session;
using Jason.Cli.Tests.Human;
using Jason.Contracts.Discovery;
using static Jason.Cli.Tests.Session.SessionOpenerTests;

namespace Jason.Cli.Tests.Session;

/// <summary>
/// <c>jason session open</c> at a folder: no item, so no brief. The folder is a campaign's, a project's or the harness
/// root, read from the runtime and never written there; the program is a host named by <c>--host</c>, else by the
/// preference, else Claude Code, or with <c>--shell</c> the system's plain shell; nothing follows the program on its
/// command line, so the session starts with no first input. The runtime is a fake that answers recorded exchanges, the
/// search path a temporary folder of stand-in files, and the terminal a recorder: nothing here starts a terminal or a host.
/// </summary>
public sealed class FolderSessionTests : IDisposable
{
    private readonly SessionRuntime _run = new();

    private readonly string _root;

    private readonly string _projectFolder;

    public FolderSessionTests()
    {
        _root = Path.Combine(_run.DataDirectory, "Jason").Replace('\\', '/');
        _projectFolder = Path.Combine(_run.DataDirectory, "Jason", "projects", "acme-corp").Replace('\\', '/');
        Directory.CreateDirectory(_root);
        Directory.CreateDirectory(_projectFolder);
        _run.Project = CopyPromptTests.Fixture("project.get", "by-id").Replace("~/Jason/projects/acme-corp", _projectFolder, StringComparison.Ordinal);
        _run.SystemInfo = SystemInfo("marked", _root);
    }

    public void Dispose() => _run.Dispose();

    // ---- The folder ----

    /// <summary>With neither --campaign nor --project the folder is the harness root, read from system.info alone.</summary>
    [Fact]
    public async Task Neither_flag_opens_the_preferred_host_at_the_harness_root_with_no_first_input()
    {
        var claude = _run.Programs.AddProgram("claude");

        var exit = await _run.Cli("session", "open");

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Equal(["system.info", "preference.list"], _run.Asked);
        var launch = Assert.Single(_run.Terminals.Launches);
        Assert.Equal([_run.ConsoleHost, "--", claude], launch.Argv);
        Assert.Equal(_root, launch.WorkingDirectory);
        var answer = JsonNode.Parse(_run.Output.ToString())!.AsObject();
        Assert.Equal(["kind", "folder", "program", "host", "terminal", "started"], answer.Select(field => field.Key).ToArray());
        Assert.Equal("folder", (string?)answer["kind"]);
        Assert.Equal("harness_root", (string?)answer["folder"]!["level"]);
        Assert.Equal(_root, (string?)answer["folder"]!["path"]);
        Assert.Equal("host", (string?)answer["program"]);
        Assert.Equal("claude_code", (string?)answer["host"]);
        Assert.Equal("conhost", (string?)answer["terminal"]);
        Assert.True((bool)answer["started"]!);
        Assert.Single(_run.Output.ToString().TrimEnd().Split('\n'));
        Assert.Empty(_run.Error.ToString());
    }

    [Fact]
    public async Task A_campaign_opens_at_the_campaigns_recorded_folder()
    {
        _run.Programs.AddProgram("claude");

        var exit = await _run.Cli("session", "open", "--campaign", "cmp_1");

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Equal(["campaign.get", "preference.list"], _run.Asked);
        Assert.Equal("{\"campaign_id\":\"cmp_1\"}", _run.Bodies[0]);
        Assert.Equal(_run.CampaignFolder, Assert.Single(_run.Terminals.Launches).WorkingDirectory);
        Assert.Equal("campaign", (string?)Answer()["folder"]!["level"]);
    }

    /// <summary>A campaign with no folder recorded opens at its project's, as the item form resolves it.</summary>
    [Fact]
    public async Task A_campaign_with_no_folder_opens_at_its_projects()
    {
        _run.Programs.AddProgram("claude");
        _run.Campaign = Without(_run.Campaign!, "folder_path");

        var exit = await _run.Cli("session", "open", "--campaign", "cmp_1");

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Equal(["campaign.get", "project.get", "preference.list"], _run.Asked);
        Assert.Equal("{\"project_id\":\"prj_1\"}", _run.Bodies[1]);
        Assert.Equal(_projectFolder, Assert.Single(_run.Terminals.Launches).WorkingDirectory);
        Assert.Equal("project", (string?)Answer()["folder"]!["level"]);
    }

    /// <summary>Neither the campaign nor its project has a folder: the harness root.</summary>
    [Fact]
    public async Task A_campaign_whose_project_has_no_folder_opens_at_the_harness_root()
    {
        _run.Programs.AddProgram("claude");
        _run.Campaign = Without(_run.Campaign!, "folder_path");
        _run.Project = Without(_run.Project!, "folder_path");

        var exit = await _run.Cli("session", "open", "--campaign", "cmp_1");

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Equal(["campaign.get", "project.get", "system.info", "preference.list"], _run.Asked);
        Assert.Equal(_root, Assert.Single(_run.Terminals.Launches).WorkingDirectory);
        Assert.Equal("harness_root", (string?)Answer()["folder"]!["level"]);
    }

    /// <summary>A project named by its id is read by project_id, by anything else by slug; either opens at its folder.</summary>
    [Theory]
    [InlineData("prj_1", "{\"project_id\":\"prj_1\"}")]
    [InlineData("acme-corp", "{\"slug\":\"acme-corp\"}")]
    public async Task A_project_by_id_or_slug_opens_at_its_folder(string named, string body)
    {
        _run.Programs.AddProgram("claude");

        var exit = await _run.Cli("session", "open", "--project", named);

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Equal(["project.get", "preference.list"], _run.Asked);
        Assert.Equal(body, _run.Bodies[0]);
        Assert.Equal(_projectFolder, Assert.Single(_run.Terminals.Launches).WorkingDirectory);
        Assert.Equal("project", (string?)Answer()["folder"]!["level"]);
    }

    [Fact]
    public async Task A_project_with_no_folder_opens_at_the_harness_root()
    {
        _run.Programs.AddProgram("claude");
        _run.Project = Without(_run.Project!, "folder_path");

        var exit = await _run.Cli("session", "open", "--project", "prj_1");

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Equal(["project.get", "system.info", "preference.list"], _run.Asked);
        Assert.Equal(_root, Assert.Single(_run.Terminals.Launches).WorkingDirectory);
    }

    // ---- The program ----

    public static TheoryData<string, string> Hosts() => new()
    {
        { "claude_code", "claude" },
        { "codex", "codex" },
        { "copilot_cli", "copilot" },
        { "cursor_cli", "agent" },
        { "gemini_cli", "gemini" },
        { "antigravity_cli", "agy" },
    };

    /// <summary>
    /// --host names the host, and the preference is not read. Every host gets its program alone: no prompt, and no
    /// flag a prompt would follow, so the session starts with no first input.
    /// </summary>
    [Theory]
    [MemberData(nameof(Hosts))]
    public async Task Host_names_the_host_and_its_program_is_given_no_argument(string word, string command)
    {
        _run.Preference = "{\"host\":\"codex\"}";
        var program = _run.Programs.AddProgram(command);

        var exit = await _run.Cli("session", "open", "--host", word);

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Equal(["system.info"], _run.Asked);
        Assert.Equal([_run.ConsoleHost, "--", program], Assert.Single(_run.Terminals.Launches).Argv);
        Assert.Equal(word, (string?)Answer()["host"]);
    }

    /// <summary>Without --host the preference names the host, and its program still takes no argument.</summary>
    [Theory]
    [MemberData(nameof(Hosts))]
    public async Task The_preference_names_the_host_when_none_is_given(string word, string command)
    {
        _run.Preference = "{\"host\":\"" + word + "\"}";
        var program = _run.Programs.AddProgram(command);

        var exit = await _run.Cli("session", "open");

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Equal([_run.ConsoleHost, "--", program], Assert.Single(_run.Terminals.Launches).Argv);
        Assert.Equal(word, (string?)Answer()["host"]);
    }

    /// <summary>On Windows --shell is Windows PowerShell from the system directory, never the search path; no host is read.</summary>
    [Fact]
    public async Task Shell_on_windows_opens_powershell_from_the_system_directory()
    {
        var shell = SystemPowerShell();
        _run.Programs.AddProgram("powershell");

        var exit = await _run.Cli("session", "open", "--shell");

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Equal(["system.info"], _run.Asked);
        Assert.Equal([_run.ConsoleHost, "--", shell], Assert.Single(_run.Terminals.Launches).Argv);
        var answer = Answer();
        Assert.Equal("shell", (string?)answer["program"]);
        Assert.Null(answer["host"]);
        Assert.True(answer.ContainsKey("host"));
    }

    /// <summary>
    /// On macOS --shell's script hands Terminal the account's login shell, read in Terminal's own environment; the script
    /// exports the data directory the CLI runs with, as the item form's does.
    /// </summary>
    [Fact]
    public async Task Shell_on_macos_execs_the_login_shell_and_carries_the_data_directory()
    {
        _run.Terminals.Platform = TerminalPlatform.MacOs;
        _run.Variables[JasonPaths.DataDirectoryVariable] = _run.DataDirectory;

        var exit = await _run.Cli("session", "open", "--shell", "--dry-run");

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Empty(_run.Terminals.Launches);
        var answer = Answer();
        Assert.Equal(["/usr/bin/open", "-a", "Terminal", "jason-harness_root.command"], answer["argv"]!.AsArray().Select(word => word!.GetValue<string>()));
        var script = answer["script"]!.GetValue<string>().Split('\n');
        Assert.Contains("cd -- " + TerminalPlan.ShellQuote(_root) + " || exit 1", script);
        Assert.Contains("export JASON_DATA_DIR=" + TerminalPlan.ShellQuote(_run.DataDirectory), script);
        Assert.Equal("exec '/bin/sh' '-c' 'exec \"${SHELL:-/bin/sh}\" -l'", script.Last(line => line.Length > 0));
    }

    /// <summary>A host on macOS: the script execs the host's program alone, and exports the data directory.</summary>
    [Fact]
    public async Task A_host_on_macos_execs_its_program_alone_and_carries_the_data_directory()
    {
        _run.Terminals.Platform = TerminalPlatform.MacOs;
        var claude = _run.Programs.AddProgram("claude");
        _run.Variables[JasonPaths.DataDirectoryVariable] = _run.DataDirectory;

        var exit = await _run.Cli("session", "open", "--campaign", "cmp_1", "--dry-run");

        Assert.Equal(ExitCodes.Success, exit);
        var answer = Answer();
        Assert.Equal("jason-campaign.command", answer["argv"]!.AsArray()[^1]!.GetValue<string>());
        var script = answer["script"]!.GetValue<string>().Split('\n');
        Assert.Contains("export JASON_DATA_DIR=" + TerminalPlan.ShellQuote(_run.DataDirectory), script);
        Assert.Equal("exec " + TerminalPlan.ShellQuote(claude), script.Last(line => line.Length > 0));
    }

    /// <summary>On Linux --shell is the SHELL variable when it names a program by its absolute path, given in the terminal's own form.</summary>
    [Fact]
    public async Task Shell_on_linux_is_the_shell_variable_in_the_terminals_form()
    {
        _run.Terminals.Platform = TerminalPlatform.Linux;
        var bash = _run.Programs.Add("bash", executable: true);
        var emulator = _run.Programs.Add("x-terminal-emulator", executable: true);
        _run.Variables["SHELL"] = bash;

        var exit = await _run.Cli("session", "open", "--shell");

        Assert.Equal(ExitCodes.Success, exit);
        var launch = Assert.Single(_run.Terminals.Launches);
        Assert.Equal([emulator, "-e", bash], launch.Argv);
        Assert.Equal(_root, launch.WorkingDirectory);
    }

    /// <summary>A SHELL that is unset, relative or names no program is not trusted: /bin/sh.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("bash")]
    [InlineData("missing")]
    public async Task Shell_on_linux_without_a_usable_shell_variable_is_bin_sh(string? shell)
    {
        _run.Terminals.Platform = TerminalPlatform.Linux;
        var emulator = _run.Programs.Add("x-terminal-emulator", executable: true);
        if (shell is not null)
        {
            _run.Variables["SHELL"] = shell == "missing" ? Path.Combine(_run.DataDirectory, "no-such-shell") : shell;
        }

        var exit = await _run.Cli("session", "open", "--shell");

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Equal([emulator, "-e", "/bin/sh"], Assert.Single(_run.Terminals.Launches).Argv);
    }

    [Fact]
    public async Task Human_says_what_was_started_and_where()
    {
        _run.Programs.AddProgram("claude");
        SystemPowerShell();

        Assert.Equal(ExitCodes.Success, await _run.Cli("session", "open", "--human"));
        Assert.Equal("Started claude_code in conhost at " + _root + ".", _run.Output.ToString().TrimEnd());
        _run.Output.GetStringBuilder().Clear();

        Assert.Equal(ExitCodes.Success, await _run.Cli("session", "open", "--shell", "--human", "--dry-run"));
        Assert.Equal("Would start a shell in conhost at " + _root + ".", _run.Output.ToString().TrimEnd());
    }

    // ---- Refusals ----

    [Theory]
    [InlineData("--host", "claude_code", "--shell")]
    [InlineData("--campaign", "cmp_1", "--project", "prj_1")]
    [InlineData("--approval", "apr_1", "--campaign", "cmp_1")]
    [InlineData("--approval", "apr_1", "--project", "prj_1")]
    [InlineData("--question", "dec_1", "--host", "codex")]
    [InlineData("--question", "dec_1", "--shell")]
    [InlineData("--campaign", "")]
    [InlineData("--project", "")]
    [InlineData("--host", "")]
    [InlineData("--host", "Claude-Code")]
    public async Task Flags_that_do_not_go_together_or_say_nothing_are_a_usage_error_that_asks_nothing(params string[] flags)
    {
        _run.Programs.AddProgram("claude");

        var exit = await _run.Cli(["session", "open", .. flags]);

        Assert.Equal(ExitCodes.Usage, exit);
        Assert.Empty(_run.Asked);
        Assert.Empty(_run.Terminals.Launches);
    }

    /// <summary>A host word this build does not know is named with the ones it knows.</summary>
    [Fact]
    public async Task An_unknown_host_word_names_the_ones_this_build_knows()
    {
        var exit = await _run.Cli("session", "open", "--host", "tomorrows_host");

        Assert.Equal(ExitCodes.Usage, exit);
        Assert.Contains("tomorrows_host", _run.Error.ToString(), StringComparison.Ordinal);
        Assert.Contains(Jason.Contracts.Hosts.AgentHosts.Vocabulary, _run.Error.ToString(), StringComparison.Ordinal);
    }

    /// <summary>A campaign or project the runtime does not know is the runtime's own refusal, printed as it came.</summary>
    [Theory]
    [InlineData("campaign.get", "--campaign", "cmp_3", "campaign_not_found")]
    [InlineData("project.get", "--project", "prj_4", "project_not_found")]
    public async Task An_unknown_campaign_or_project_is_the_runtimes_refusal(string operation, string flag, string id, string code)
    {
        _run.Programs.AddProgram("claude");
        _run.Refusals[operation] = CopyPromptTests.Fixture(operation, "unknown");

        var exit = await _run.Cli("session", "open", flag, id);

        Assert.Equal(ExitCodes.ApiError, exit);
        Assert.Equal([operation], _run.Asked);
        Assert.Empty(_run.Terminals.Launches);
        Assert.Equal(code, Code());
    }

    /// <summary>The harness root is a place only when marked; anything else, and nothing recorded above it, is refused.</summary>
    [Theory]
    [InlineData("absent")]
    [InlineData("unmarked")]
    [InlineData("taken")]
    public async Task No_folder_recorded_is_refused(string rootState)
    {
        _run.Programs.AddProgram("claude");
        _run.SystemInfo = SystemInfo(rootState, _root);

        var exit = await _run.Cli("session", "open");

        Assert.Equal(ExitCodes.ApiError, exit);
        Assert.Equal(["system.info"], _run.Asked);
        Assert.Empty(_run.Terminals.Launches);
        Assert.Equal("folder_not_recorded", Code());
        Assert.NotEmpty(_run.Error.ToString());
    }

    [Fact]
    public async Task A_recorded_folder_that_is_not_there_is_refused()
    {
        _run.Programs.AddProgram("claude");
        _run.Campaign = _run.Campaign!.Replace(_run.CampaignFolder, _run.CampaignFolder + "-moved", StringComparison.Ordinal);

        var exit = await _run.Cli("session", "open", "--campaign", "cmp_1");

        Assert.Equal(ExitCodes.ApiError, exit);
        Assert.Empty(_run.Terminals.Launches);
        Assert.Equal("folder_missing", Code());
        Assert.Contains(_run.CampaignFolder + "-moved", _run.Error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_preference_the_runtime_will_not_read_is_refused()
    {
        _run.Programs.AddProgram("claude");
        _run.PreferencesAnswer = false;

        var exit = await _run.Cli("session", "open");

        Assert.Equal(ExitCodes.ApiError, exit);
        Assert.Empty(_run.Terminals.Launches);
        Assert.Equal("preference_unreadable", Code());
    }

    [Fact]
    public async Task A_preference_naming_a_host_this_build_does_not_know_is_refused()
    {
        _run.Programs.AddProgram("claude");
        _run.Preference = "{\"host\":\"tomorrows_host\"}";

        var exit = await _run.Cli("session", "open");

        Assert.Equal(ExitCodes.ApiError, exit);
        Assert.Empty(_run.Terminals.Launches);
        Assert.Equal("host_unknown", Code());
        Assert.Contains("tomorrows_host", _run.Error.ToString(), StringComparison.Ordinal);
    }

    /// <summary>A host that is not on the search path is named, never replaced by another or by a shell.</summary>
    [Fact]
    public async Task A_host_not_on_the_search_path_is_refused_and_never_replaced()
    {
        _run.Programs.AddProgram("claude");
        SystemPowerShell();

        var exit = await _run.Cli("session", "open", "--host", "codex");

        Assert.Equal(ExitCodes.ApiError, exit);
        Assert.Empty(_run.Terminals.Launches);
        Assert.Equal("host_not_found", Code());
        Assert.Contains("codex's program 'codex' is not on this machine's search path", _run.Error.ToString(), StringComparison.Ordinal);
    }

    /// <summary>Windows PowerShell missing from the system directory is refused; a powershell on the search path is not taken.</summary>
    [Fact]
    public async Task A_shell_not_in_its_fixed_place_is_refused()
    {
        _run.Programs.AddProgram("powershell");

        var exit = await _run.Cli("session", "open", "--shell");

        Assert.Equal(ExitCodes.ApiError, exit);
        Assert.Empty(_run.Terminals.Launches);
        Assert.Equal("shell_not_found", Code());
    }

    [Fact]
    public async Task No_terminal_is_refused()
    {
        _run.Programs.AddProgram("claude");
        File.Delete(_run.ConsoleHost);

        var exit = await _run.Cli("session", "open");

        Assert.Equal(ExitCodes.ApiError, exit);
        Assert.Empty(_run.Terminals.Launches);
        Assert.Equal("terminal_not_found", Code());
    }

    /// <summary>Where nothing is set up to start a terminal, which is every environment but the real one, nothing is started.</summary>
    [Fact]
    public async Task No_terminal_starter_is_refused()
    {
        _run.Programs.AddProgram("claude");

        var exit = await _run.Cli(terminals: false, "session", "open", "--shell");

        Assert.Equal(ExitCodes.ApiError, exit);
        Assert.Equal("terminal_not_found", Code());
    }

    [Fact]
    public async Task A_terminal_that_does_not_start_is_refused_saying_why()
    {
        _run.Programs.AddProgram("claude");
        _run.Terminals.Refusal = "the file is not a program";

        var exit = await _run.Cli("session", "open");

        Assert.Equal(ExitCodes.ApiError, exit);
        Assert.Equal("terminal_start_failed", Code());
        Assert.Contains("conhost did not start (the file is not a program)", _run.Error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task No_runtime_exits_3_and_starts_nothing()
    {
        _run.Programs.AddProgram("claude");
        _run.Descriptor = false;

        var exit = await _run.Cli("session", "open", "--shell");

        Assert.Equal(ExitCodes.RuntimeUnavailable, exit);
        Assert.Empty(_run.Terminals.Launches);
    }

    // ---- Helpers ----

    private JsonObject Answer() => JsonNode.Parse(_run.Output.ToString())!.AsObject();

    private string? Code() => (string?)JsonNode.Parse(_run.Output.ToString())!["error"]!["code"];

    /// <summary>Windows PowerShell where the system keeps it, under the system directory the recorder names.</summary>
    private string SystemPowerShell()
    {
        var folder = Path.Combine(_run.Terminals.SystemDirectory!, "WindowsPowerShell", "v1.0");
        Directory.CreateDirectory(folder);
        var shell = Path.Combine(folder, "powershell.exe");
        File.WriteAllBytes(shell, []);
        return shell;
    }

    private static string Without(string json, string field)
    {
        var node = JsonNode.Parse(json)!.AsObject();
        node[field] = null;
        return node.ToJsonString();
    }

    private static string SystemInfo(string rootState, string root)
    {
        var info = JsonNode.Parse(CopyPromptTests.Fixture("system.info", "with-ui"))!.AsObject();
        info["harness"] = new JsonObject { ["root"] = root, ["root_state"] = rootState };
        return info.ToJsonString();
    }
}
