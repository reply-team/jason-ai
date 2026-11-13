using System.Text.Json.Nodes;
using Jason.Cli;
using Jason.Cli.Http;
using Jason.Cli.Session;
using Jason.Cli.Tests.Human;
using Jason.Contracts.Api;
using Jason.Contracts.Discovery;
using Jason.Contracts.Harness;
using Jason.Runtime.Tests;
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
        Assert.Equal(["kind", "folder", "host", "shell", "program", "terminal", "started"], answer.Select(field => field.Key).ToArray());
        Assert.Equal("folder", (string?)answer["kind"]);
        Assert.Equal("harness_root", (string?)answer["folder"]!["level"]);
        Assert.Equal(_root, (string?)answer["folder"]!["path"]);
        Assert.Equal("claude_code", (string?)answer["host"]);
        Assert.False((bool)answer["shell"]!);
        Assert.Equal(claude, (string?)answer["program"]);
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

    /// <summary>
    /// Inside a project's folder, with neither flag, the folder form takes that project, as every verb that takes a project
    /// and is given none does, and says so on stderr; from a campaign's folder it takes the campaign's project, never the
    /// campaign.
    /// </summary>
    [Theory]
    [InlineData("project")]
    [InlineData("campaign")]
    public async Task Inside_a_projects_folder_neither_flag_opens_at_that_project(string level)
    {
        _run.Programs.AddProgram("claude");
        Mark(_projectFolder, new HarnessMarker(HarnessMarker.Project, "prj_1"));
        Mark(_run.CampaignFolder, new HarnessMarker(HarnessMarker.Campaign, "cmp_1"));
        _run.WorkingDirectory = Path.Combine(level == "project" ? _projectFolder : _run.CampaignFolder, "notes");
        Directory.CreateDirectory(_run.WorkingDirectory);

        var exit = await _run.Cli("session", "open");

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Equal(level == "project" ? ["project.get", "project.get", "preference.list"] : ["campaign.get", "project.get", "preference.list"], _run.Asked);
        Assert.Equal(_projectFolder, Assert.Single(_run.Terminals.Launches).WorkingDirectory);
        Assert.Equal("project", (string?)Answer()["folder"]!["level"]);
        Assert.Contains("Project acme-corp (prj_1), from the working directory", _run.Error.ToString(), StringComparison.Ordinal);
        Assert.Contains("give --project or --campaign to name another folder", _run.Error.ToString(), StringComparison.Ordinal);
    }

    /// <summary>Outside every harness folder there is no project to take: the harness root, and nothing said.</summary>
    [Fact]
    public async Task Outside_every_harness_folder_neither_flag_opens_at_the_harness_root()
    {
        _run.Programs.AddProgram("claude");
        _run.WorkingDirectory = Path.Combine(_run.DataDirectory, "elsewhere");
        Directory.CreateDirectory(_run.WorkingDirectory);

        var exit = await _run.Cli("session", "open");

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Equal(["system.info", "preference.list"], _run.Asked);
        Assert.Equal(_root, Assert.Single(_run.Terminals.Launches).WorkingDirectory);
        Assert.Empty(_run.Error.ToString());
    }

    /// <summary>
    /// A marker in a folder that is not the one the runtime records for its project, a copy say, is said on stderr and
    /// not used: the session opens at the harness root, never in the copy and never at the project's real folder.
    /// </summary>
    [Fact]
    public async Task A_marker_that_is_not_the_recorded_folders_is_said_and_not_used()
    {
        _run.Programs.AddProgram("claude");
        var copy = Path.Combine(_run.DataDirectory, "copy-of-acme").Replace('\\', '/');
        Mark(copy, new HarnessMarker(HarnessMarker.Project, "prj_1"));
        _run.WorkingDirectory = copy;

        var exit = await _run.Cli("session", "open");

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Equal(["project.get", "system.info", "preference.list"], _run.Asked);
        Assert.Equal(_root, Assert.Single(_run.Terminals.Launches).WorkingDirectory);
        Assert.Contains("so it is not used here", _run.Error.ToString(), StringComparison.Ordinal);
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

    /// <summary>A host npm installed starts through the interpreter and entry script its shim names, and nothing follows them.</summary>
    [Fact]
    public async Task A_host_npm_installed_starts_through_its_shim_with_nothing_after_the_program()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Only Windows has npm's cmd shim.");
        var node = _run.Programs.Add("node.exe", executable: true);
        var entry = _run.Programs.AddNested(@"node_modules\gemini-cli\dist\index.js", "// the entry point");
        _run.Programs.AddNested(@"node_modules\gemini-cli\package.json", "{\"name\":\"gemini-cli\",\"bin\":{\"gemini\":\"dist/index.js\"}}");
        _run.Programs.Add("gemini.cmd", Jason.Runtime.Tests.Plugins.TestPrograms.NpmShim("gemini-cli", @"dist\index.js"));

        var exit = await _run.Cli("session", "open", "--host", "gemini_cli");

        Assert.Equal(ExitCodes.Success, exit);
        var argv = Assert.Single(_run.Terminals.Launches).Argv;
        Assert.Equal(4, argv.Count);
        Assert.Equal(node, Path.GetFullPath(argv[2]));
        Assert.Equal(entry, Path.GetFullPath(argv[3]));
        Assert.Equal(node, Path.GetFullPath((string)Answer()["program"]!));
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
        Assert.True((bool)answer["shell"]!);
        Assert.Equal(shell, (string?)answer["program"]);
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
    [InlineData("whitespace")]
    [InlineData("not executable")]
    public async Task Shell_on_linux_without_a_usable_shell_variable_is_bin_sh(string? shell)
    {
        Assert.SkipWhen(shell == "not executable" && OperatingSystem.IsWindows(), "Windows keeps no executable bit; the Linux legs run this row.");
        _run.Terminals.Platform = TerminalPlatform.Linux;
        var emulator = _run.Programs.Add("x-terminal-emulator", executable: true);
        if (shell is not null)
        {
            _run.Variables["SHELL"] = shell switch
            {
                "missing" => Path.Combine(_run.DataDirectory, "no-such-shell"),
                "whitespace" => _run.Programs.Add("bash", executable: true) + " -l",
                "not executable" => _run.Programs.Add("zsh", executable: false),
                _ => shell,
            };
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
    [InlineData("--here", "--campaign", "cmp_1")]
    [InlineData("--here", "--project", "prj_1")]
    [InlineData("--approval", "apr_1", "--here")]
    [InlineData("--question", "dec_1", "--here")]
    [InlineData("--resume", "abc", "--shell")]
    [InlineData("--here", "--resume", "abc", "--shell")]
    [InlineData("--approval", "apr_1", "--resume", "abc")]
    [InlineData("--question", "dec_1", "--resume", "abc")]
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
    [InlineData("not_a_folder")]
    [InlineData("unreadable")]
    public async Task No_folder_recorded_is_refused(string rootState)
    {
        _run.Programs.AddProgram("claude");
        _run.SystemInfo = SystemInfo(rootState, _root);

        var exit = await _run.Cli("session", "open");

        Assert.Equal(ExitCodes.ApiError, exit);
        Assert.Equal(["system.info"], _run.Asked);
        Assert.Empty(_run.Terminals.Launches);
        Assert.Equal("folder_not_recorded", Code());
        var said = _run.Error.ToString().TrimEnd();
        Assert.Equal(Message(), said);

        // Only where nothing, or an unmarked folder, stands at the root does a first project or campaign make it a place.
        Assert.Equal(rootState is "absent" or "unmarked", said.Contains("the first project or campaign made", StringComparison.Ordinal));
        if (rootState == "taken")
        {
            Assert.Equal(
                "The folder at the harness root " + _root + " holds a marker that names something other than a root, so there is nowhere to start and nothing was started.",
                said);
        }
    }

    /// <summary>A campaign whose recorded folders are all gone names what it read: the campaign and its project have none.</summary>
    [Fact]
    public async Task No_folder_recorded_for_a_campaign_says_the_campaign_and_its_project_have_none()
    {
        _run.Programs.AddProgram("claude");
        _run.Campaign = Without(_run.Campaign!, "folder_path");
        _run.Project = Without(_run.Project!, "folder_path");
        _run.SystemInfo = SystemInfo("absent", _root);

        var exit = await _run.Cli("session", "open", "--campaign", "cmp_1");

        Assert.Equal(ExitCodes.ApiError, exit);
        Assert.Equal(
            "The campaign and its project have no folder recorded, and nothing is at the harness root " + _root + " yet (the first project or campaign made makes it), so there is nowhere to start and nothing was started.",
            _run.Error.ToString().TrimEnd());
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

    [Theory]
    [InlineData("project")]
    [InlineData("harness_root")]
    public async Task A_recorded_project_folder_or_root_that_is_not_there_is_refused(string level)
    {
        _run.Programs.AddProgram("claude");
        var gone = (level == "project" ? _projectFolder : _root) + "-gone";
        if (level == "project")
        {
            _run.Project = _run.Project!.Replace(_projectFolder, gone, StringComparison.Ordinal);
        }
        else
        {
            _run.SystemInfo = SystemInfo("marked", gone);
        }

        var exit = await _run.Cli(level == "project" ? ["session", "open", "--project", "prj_1"] : ["session", "open"]);

        Assert.Equal(ExitCodes.ApiError, exit);
        Assert.Empty(_run.Terminals.Launches);
        Assert.Equal("folder_missing", Code());
        Assert.Equal(
            level == "project"
                ? "The project's folder recorded at " + gone + " is not there, so nothing was started; if it was moved, `jason project link --path <the project's folder, where it is now>` records it and its campaigns again."
                : "The harness root at " + gone + " is not there, so nothing was started.",
            _run.Error.ToString().TrimEnd());
    }

    /// <summary>An answer the runtime gave that cannot be read is refused in the envelope and said on stderr.</summary>
    [Fact]
    public async Task An_answer_that_cannot_be_read_is_runtime_error_said_on_both_streams()
    {
        _run.Programs.AddProgram("claude");
        _run.SystemInfo = "<html>not the runtime</html>";

        var exit = await _run.Cli("session", "open");

        Assert.Equal(ExitCodes.ApiError, exit);
        Assert.Equal("runtime_error", Code());
        Assert.Equal(Message(), _run.Error.ToString().TrimEnd());
    }

    /// <summary>
    /// A preference list the runtime took and did not answer in time is the read's own envelope, request_timed_out, and
    /// no second one: stdout holds exactly one object.
    /// </summary>
    [Fact]
    public async Task A_preference_read_that_times_out_prints_one_envelope()
    {
        using var paths = new TempPaths();
        paths.WriteDescriptor(new RuntimeDescriptor("v1", "0.1.0-dev", "rt_LIVE", 77, "http://127.0.0.1:5000", "the-token", DateTimeOffset.UnixEpoch));
        var clock = new FixedClock(new DateTimeOffset(2026, 11, 11, 12, 0, 0, TimeSpan.Zero));
        var asked = new TaskCompletionSource<CancellationToken>(TaskCreationOptions.RunContinuationsAsynchronously);
        var output = new StringWriter();
        var env = new CliEnvironment(output, new StringWriter(), paths.Paths, new RootThenWaiting(SystemInfo("marked", _root), asked), Clock: clock, Terminals: _run.Terminals);
        var budget = RuntimeClient.Budget(Operations.PreferenceList);

        var run = CliApp.RunAsync(["session", "open"], env, TestContext.Current.CancellationToken);
        await asked.Task.WaitAsync(TestContext.Current.CancellationToken);
        clock.Advance(budget);
        var exit = await run.WaitAsync(TestContext.Current.CancellationToken);

        Assert.Equal(ExitCodes.ApiError, exit);
        var printed = Assert.Single(output.ToString().TrimEnd().Split('\n'));
        Assert.Equal("request_timed_out", (string?)JsonNode.Parse(printed)!["error"]!["code"]);
        Assert.Empty(_run.Terminals.Launches);
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

    // ---- Here ----

    /// <summary>
    /// --here opens at the folder the command runs in, as it is: no marker is read there, so a project's marker in it names
    /// nothing and nothing is said on stderr; of the runtime only the preference is asked, since no program was named.
    /// </summary>
    [Fact]
    public async Task Here_opens_at_the_folder_the_command_runs_in_and_reads_no_marker()
    {
        var claude = _run.Programs.AddProgram("claude");
        Mark(_projectFolder, new HarnessMarker(HarnessMarker.Project, "prj_1"));
        _run.WorkingDirectory = _projectFolder;

        var exit = await _run.Cli("session", "open", "--here");

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Equal(["preference.list"], _run.Asked);
        var launch = Assert.Single(_run.Terminals.Launches);
        Assert.Equal([_run.ConsoleHost, "--", claude], launch.Argv);
        Assert.Equal(_projectFolder, launch.WorkingDirectory);
        var answer = Answer();
        Assert.Equal(["kind", "folder", "host", "shell", "program", "terminal", "started"], answer.Select(field => field.Key).ToArray());
        Assert.Equal("here", (string?)answer["folder"]!["level"]);
        Assert.Equal(_projectFolder, (string?)answer["folder"]!["path"]);
        Assert.Empty(_run.Error.ToString());
    }

    /// <summary>A folder that is no harness folder at all is a place for --here; with --host or --shell nothing is asked of the runtime.</summary>
    [Theory]
    [InlineData("--host", "codex")]
    [InlineData("--shell")]
    public async Task Here_with_a_program_named_asks_the_runtime_nothing(params string[] program)
    {
        _run.Programs.AddProgram("codex");
        SystemPowerShell();
        var elsewhere = Path.Combine(_run.DataDirectory, "elsewhere").Replace('\\', '/');
        Directory.CreateDirectory(elsewhere);
        _run.WorkingDirectory = elsewhere;

        var exit = await _run.Cli(["session", "open", "--here", .. program]);

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Empty(_run.Asked);
        Assert.Equal(elsewhere, Assert.Single(_run.Terminals.Launches).WorkingDirectory);
    }

    /// <summary>The folder the command runs in must be there: one deleted under the shell, or one the system cannot name, starts nothing.</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Here_where_the_folder_is_not_there_is_refused(bool named)
    {
        _run.Programs.AddProgram("claude");
        var gone = Path.Combine(_run.DataDirectory, "gone").Replace('\\', '/');
        _run.WorkingDirectory = named ? gone : null;

        var exit = await _run.Cli("session", "open", "--here", "--host", "claude_code");

        Assert.Equal(ExitCodes.ApiError, exit);
        Assert.Empty(_run.Terminals.Launches);
        Assert.Equal("folder_missing", Code());
        Assert.Equal(
            named
                ? "The folder this command runs in, " + gone + ", is not there, so nothing was started."
                : "The folder this command runs in cannot be named, so nothing was started.",
            _run.Error.ToString().TrimEnd());
    }

    // ---- Resume ----

    public static TheoryData<string, string, string> ResumingHosts() => new()
    {
        { "claude_code", "claude", "--resume" },
        { "codex", "codex", "resume" },
    };

    public static TheoryData<string, string> HostsThatCannotResume() => new()
    {
        { "copilot_cli", "copilot" },
        { "cursor_cli", "agent" },
        { "gemini_cli", "gemini" },
        { "antigravity_cli", "agy" },
    };

    private const string Conversation = "0f8e2c1a-5b7d-4e3f-9a6b-1c2d3e4f5a6b";

    /// <summary>--resume puts the host's own words and the id after its program, and nothing else; the answer carries the id.</summary>
    [Theory]
    [MemberData(nameof(ResumingHosts))]
    public async Task Resume_gives_the_host_its_own_words_and_the_id(string word, string command, string resume)
    {
        var program = _run.Programs.AddProgram(command);
        _run.WorkingDirectory = _projectFolder;

        var exit = await _run.Cli("session", "open", "--here", "--host", word, "--resume", Conversation);

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Empty(_run.Asked);
        Assert.Equal([_run.ConsoleHost, "--", program, resume, Conversation], Assert.Single(_run.Terminals.Launches).Argv);
        var answer = Answer();
        Assert.Equal(["kind", "folder", "host", "shell", "program", "resume", "terminal", "started"], answer.Select(field => field.Key).ToArray());
        Assert.Equal(Conversation, (string?)answer["resume"]);
    }

    /// <summary>Without --host the preference names the host that resumes, at any folder the verb opens at.</summary>
    [Theory]
    [MemberData(nameof(ResumingHosts))]
    public async Task Resume_takes_the_host_the_preference_names(string word, string command, string resume)
    {
        _run.Preference = "{\"host\":\"" + word + "\"}";
        var program = _run.Programs.AddProgram(command);

        var exit = await _run.Cli("session", "open", "--campaign", "cmp_1", "--resume", Conversation);

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Equal(["campaign.get", "preference.list"], _run.Asked);
        var launch = Assert.Single(_run.Terminals.Launches);
        Assert.Equal([_run.ConsoleHost, "--", program, resume, Conversation], launch.Argv);
        Assert.Equal(_run.CampaignFolder, launch.WorkingDirectory);
    }

    /// <summary>
    /// A host with no words to resume by is refused in the envelope, before its program or a terminal is looked for: with no
    /// program and no terminal on this machine the refusal is still this one. Named by --host, nothing is asked of the runtime.
    /// </summary>
    [Theory]
    [MemberData(nameof(HostsThatCannotResume))]
    public async Task A_host_that_cannot_resume_is_refused_before_anything_is_looked_for(string word, string command)
    {
        _ = command;
        File.Delete(_run.ConsoleHost);
        _run.WorkingDirectory = _projectFolder;

        var exit = await _run.Cli("session", "open", "--here", "--host", word, "--resume", Conversation);

        Assert.Equal(ExitCodes.ApiError, exit);
        Assert.Empty(_run.Asked);
        Assert.Empty(_run.Terminals.Launches);
        Assert.Equal("host_cannot_resume", Code());
        Assert.Equal(
            word + " has no way this jason knows to continue one of its own conversations, so nothing was started; --resume is taken by claude_code, codex.",
            _run.Error.ToString().TrimEnd());
        Assert.Equal(Message(), _run.Error.ToString().TrimEnd());
    }

    /// <summary>A host the preference names that cannot resume is refused after the preference is read, and before any lookup.</summary>
    [Theory]
    [MemberData(nameof(HostsThatCannotResume))]
    public async Task A_preferred_host_that_cannot_resume_is_refused_after_the_preference(string word, string command)
    {
        _run.Programs.AddProgram(command);
        _run.Preference = "{\"host\":\"" + word + "\"}";

        var exit = await _run.Cli("session", "open", "--resume", Conversation);

        Assert.Equal(ExitCodes.ApiError, exit);
        Assert.Equal(["system.info", "preference.list"], _run.Asked);
        Assert.Empty(_run.Terminals.Launches);
        Assert.Equal("host_cannot_resume", Code());
    }

    /// <summary>The id is held to its shape at both edges before anything is asked: one character and two hundred are taken.</summary>
    [Theory]
    [InlineData(1)]
    [InlineData(200)]
    public async Task Resume_takes_an_id_at_both_edges_of_its_length(int length)
    {
        var claude = _run.Programs.AddProgram("claude");
        _run.WorkingDirectory = _projectFolder;
        var id = "a" + new string('7', length - 1);

        var exit = await _run.Cli("session", "open", "--here", "--host", "claude_code", "--resume", id);

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Equal([_run.ConsoleHost, "--", claude, "--resume", id], Assert.Single(_run.Terminals.Launches).Argv);
    }

    public static TheoryData<string> BadIds() =>
    [
        string.Empty,
        "a" + new string('7', 200),
        "-rf",
        ".hidden",
        "a b",
        "a;b",
        "a'b",
        "a\n",
        "a/b",
        "café",
    ];

    /// <summary>An id outside the shape is a usage error that asks nothing and starts nothing: past two hundred, a bad first or later character.</summary>
    [Theory]
    [MemberData(nameof(BadIds))]
    public async Task Resume_with_an_id_outside_its_shape_is_a_usage_error(string id)
    {
        _run.Programs.AddProgram("claude");
        _run.WorkingDirectory = _projectFolder;

        var exit = await _run.Cli("session", "open", "--here", "--host", "claude_code", "--resume=" + id);

        Assert.Equal(ExitCodes.Usage, exit);
        Assert.Empty(_run.Asked);
        Assert.Empty(_run.Terminals.Launches);
    }

    /// <summary>A dry run starts nothing and prints the folder and the whole command, the host's words and the id among it.</summary>
    [Fact]
    public async Task A_dry_run_prints_the_folder_and_the_words()
    {
        var codex = _run.Programs.AddProgram("codex");
        _run.WorkingDirectory = _projectFolder;

        var exit = await _run.Cli("session", "open", "--here", "--host", "codex", "--resume", Conversation, "--dry-run");

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Empty(_run.Terminals.Launches);
        var answer = Answer();
        Assert.False((bool)answer["started"]!);
        Assert.Equal(_projectFolder, (string?)answer["folder"]!["path"]);
        Assert.Equal([_run.ConsoleHost, "--", codex, "resume", Conversation], answer["argv"]!.AsArray().Select(word => word!.GetValue<string>()));
    }

    /// <summary>On macOS the script quotes the host's words and the id as it quotes every word, and exports the data directory.</summary>
    [Fact]
    public async Task On_macos_the_script_quotes_the_words_and_the_id()
    {
        _run.Terminals.Platform = TerminalPlatform.MacOs;
        var claude = _run.Programs.AddProgram("claude");
        _run.Variables[JasonPaths.DataDirectoryVariable] = _run.DataDirectory;
        _run.WorkingDirectory = _projectFolder;
        const string Id = "thread_01.part:2-b";

        var exit = await _run.Cli("session", "open", "--here", "--host", "claude_code", "--resume", Id, "--dry-run");

        Assert.Equal(ExitCodes.Success, exit);
        var answer = Answer();
        Assert.Equal(["/usr/bin/open", "-a", "Terminal", "jason-here.command"], answer["argv"]!.AsArray().Select(word => word!.GetValue<string>()));
        var script = answer["script"]!.GetValue<string>().Split('\n');
        Assert.Contains("cd -- " + TerminalPlan.ShellQuote(_projectFolder) + " || exit 1", script);
        Assert.Contains("export JASON_DATA_DIR=" + TerminalPlan.ShellQuote(_run.DataDirectory), script);
        Assert.Equal("exec " + TerminalPlan.ShellQuote(claude) + " '--resume' 'thread_01.part:2-b'", script.Last(line => line.Length > 0));
    }

    [Fact]
    public async Task Human_says_which_conversation_continues()
    {
        _run.Programs.AddProgram("claude");
        _run.WorkingDirectory = _projectFolder;

        Assert.Equal(ExitCodes.Success, await _run.Cli("session", "open", "--here", "--host", "claude_code", "--resume", Conversation, "--human", "--dry-run"));
        Assert.Equal("Would start claude_code in conhost at " + _projectFolder + ", continuing its conversation " + Conversation + ".", _run.Output.ToString().TrimEnd());
    }

    // ---- Helpers ----

    private JsonObject Answer() => JsonNode.Parse(_run.Output.ToString())!.AsObject();

    private string? Code() => (string?)JsonNode.Parse(_run.Output.ToString())!["error"]!["code"];

    private string? Message() => (string?)JsonNode.Parse(_run.Output.ToString())!["error"]!["message"];

    private static void Mark(string folder, HarnessMarker marker)
    {
        Directory.CreateDirectory(Path.Combine(folder, HarnessMarker.Directory));
        File.WriteAllBytes(HarnessMarker.PathIn(folder), marker.Bytes());
    }

    /// <summary>A runtime that answers system.info and takes preference.list without ever answering it.</summary>
    private sealed class RootThenWaiting(string systemInfo, TaskCompletionSource<CancellationToken> asked) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("system.info", StringComparison.Ordinal))
            {
                return new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new StringContent(systemInfo, System.Text.Encoding.UTF8, "application/json") };
            }

            asked.TrySetResult(cancellationToken);
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            throw new InvalidOperationException("unreachable");
        }
    }

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
