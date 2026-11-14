using System.Text.Json.Nodes;
using Jason.Cli;
using Jason.Cli.Session;
using Jason.Cli.Skills;
using Jason.Contracts.Api;
using Jason.Contracts.Discovery;
using Jason.Contracts.Hosts;
using Jason.Contracts.Skills;
using static Jason.Cli.Tests.Session.SessionOpenerTests;

namespace Jason.Cli.Tests.Session;

/// <summary>
/// <c>jason session open --setup</c>: the person's own agent host on Jason itself, at the role skills folder with the
/// plugins folder beside it where the host takes a second folder, and the setup brief as its first input. The folders are
/// the ones <see cref="SetupInfo.From"/> names; the runtime is asked at most the preference; the skills receipt of the
/// host's skills root says whether the skill the brief names is deployed there. The runtime is a fake, the programs
/// stand-ins and the terminal a recorder: nothing here starts a terminal or a host.
/// </summary>
public sealed class SetupSessionTests : IDisposable
{
    private readonly SessionRuntime _run = new();

    private readonly SetupInfo _setup;

    public SetupSessionTests()
    {
        _setup = SetupInfo.From(new JasonPaths(_run.DataDirectory));
        Directory.CreateDirectory(_setup.RoleSkillsDirectory);
        Directory.CreateDirectory(_setup.PluginsDirectory);
        _run.Harnesses = HarnessLocators.ForHome(_run.Home);
    }

    public void Dispose() => _run.Dispose();

    private JsonObject Answer() => JsonNode.Parse(_run.Output.ToString())!.AsObject();

    private string SkillsRoot(AgentHostKind host) => AgentHosts.Under(_run.Home, AgentHosts.Of(host).HomeSkills);

    /// <summary>The skill the brief names, deployed into the host's skills root with the receipt a deployment writes.</summary>
    private void DeploySkill(AgentHostKind host)
    {
        var root = SkillsRoot(host);
        var file = AgentHosts.Under(root, SetupBrief.SkillName + "/SKILL.md");
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file, "---\nname: customising-jason\n---\n");
        SkillsRecord.Write(root, new SkillsRecord(
            SkillsRecord.CurrentVersion,
            [new SkillsDeployment("jason-runtime-skills", "https://example.com/skills", "v0.42.0", false, null, DateTimeOffset.UnixEpoch, [new DeployedFile(SetupBrief.SkillName + "/SKILL.md", SkillsRecord.Digest(file))])]));
    }

    // ---- What it opens ----

    /// <summary>
    /// Asked nothing but the preference, it opens the host the preference names at the role skills folder, the brief its one
    /// first word and the plugins folder after it, by the host's flag.
    /// </summary>
    [Fact]
    public async Task Setup_opens_the_preferred_host_at_the_role_skills_folder_with_the_plugins_folder_added_and_the_brief_first()
    {
        var claude = _run.Programs.AddProgram("claude");
        DeploySkill(AgentHostKind.ClaudeCode);

        var exit = await _run.Cli("session", "open", "--setup");

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Equal(["preference.list"], _run.Asked);
        var launch = Assert.Single(_run.Terminals.Launches);
        Assert.Equal([_run.ConsoleHost, "--", claude, SetupBrief.Text, "--add-dir", _setup.PluginsDirectory], launch.Argv);
        Assert.Equal(_setup.RoleSkillsDirectory, launch.WorkingDirectory);
        var answer = Answer();
        Assert.Equal(["kind", "folder", "plugins_folder", "host", "skill", "program", "terminal", "started"], answer.Select(field => field.Key).ToArray());
        Assert.Equal("setup", (string?)answer["kind"]);
        Assert.Equal("role_skills", (string?)answer["folder"]!["level"]);
        Assert.Equal(_setup.RoleSkillsDirectory, (string?)answer["folder"]!["path"]);
        Assert.Equal(["path", "added"], answer["plugins_folder"]!.AsObject().Select(field => field.Key).ToArray());
        Assert.Equal(_setup.PluginsDirectory, (string?)answer["plugins_folder"]!["path"]);
        Assert.True((bool)answer["plugins_folder"]!["added"]!);
        Assert.Equal("claude_code", (string?)answer["host"]);
        Assert.Equal(SetupBrief.SkillName, (string?)answer["skill"]!["name"]);
        Assert.True((bool)answer["skill"]!["deployed"]!);
        Assert.Equal(SkillsRoot(AgentHostKind.ClaudeCode), (string?)answer["skill"]!["root"]);
        Assert.Equal(claude, (string?)answer["program"]);
        Assert.Equal("conhost", (string?)answer["terminal"]);
        Assert.True((bool)answer["started"]!);
        Assert.Empty(_run.Error.ToString());
    }

    /// <summary>A host named by <c>--host</c> needs no runtime: nothing is asked, so none has to be there.</summary>
    [Fact]
    public async Task Setup_with_a_host_named_asks_the_runtime_nothing_and_needs_none()
    {
        _run.Programs.AddProgram("claude");
        _run.Descriptor = false;

        var exit = await _run.Cli("session", "open", "--setup", "--host", "claude_code");

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Empty(_run.Asked);
        Assert.Single(_run.Terminals.Launches);
    }

    [Fact]
    public async Task Setup_on_codex_adds_the_plugins_folder_by_its_own_flag()
    {
        var codex = _run.Programs.AddProgram("codex");
        _run.Preference = "{\"host\":\"codex\"}";

        var exit = await _run.Cli("session", "open", "--setup");

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Equal([_run.ConsoleHost, "--", codex, SetupBrief.Text, "--add-dir", _setup.PluginsDirectory], Assert.Single(_run.Terminals.Launches).Argv);
        Assert.Equal(SkillsRoot(AgentHostKind.Codex), (string?)Answer()["skill"]!["root"]);
    }

    /// <summary>A host Jason knows no folder flag for is handed none, and the answer says why the folder is not beside it.</summary>
    [Fact]
    public async Task Setup_on_a_host_with_no_folder_flag_opens_with_none_and_says_so()
    {
        var copilot = _run.Programs.AddProgram("copilot");

        var exit = await _run.Cli("session", "open", "--setup", "--host", "copilot_cli");

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Equal([_run.ConsoleHost, "--", copilot, "-i", SetupBrief.Text], Assert.Single(_run.Terminals.Launches).Argv);
        var folder = Answer()["plugins_folder"]!.AsObject();
        Assert.Equal(["path", "added", "reason"], folder.Select(field => field.Key).ToArray());
        Assert.False((bool)folder["added"]!);
        Assert.Equal("host_takes_no_folder", (string?)folder["reason"]);
    }

    /// <summary>A plugins folder that is not there is not handed to the host, and none is made.</summary>
    [Fact]
    public async Task Setup_adds_no_plugins_folder_that_is_not_there_and_makes_none()
    {
        var claude = _run.Programs.AddProgram("claude");
        Directory.Delete(_setup.PluginsDirectory);

        var exit = await _run.Cli("session", "open", "--setup", "--host", "claude_code");

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Equal([_run.ConsoleHost, "--", claude, SetupBrief.Text], Assert.Single(_run.Terminals.Launches).Argv);
        Assert.False((bool)Answer()["plugins_folder"]!["added"]!);
        Assert.Equal("folder_missing", (string?)Answer()["plugins_folder"]!["reason"]);
        Assert.False(Directory.Exists(_setup.PluginsDirectory));
    }

    [Fact]
    public async Task Setup_refuses_when_the_role_skills_folder_is_not_there()
    {
        _run.Programs.AddProgram("claude");
        Directory.Delete(_setup.RoleSkillsDirectory);

        var exit = await _run.Cli("session", "open", "--setup");

        Assert.Equal(ExitCodes.ApiError, exit);
        Assert.Empty(_run.Asked);
        Assert.Empty(_run.Terminals.Launches);
        var said = $"The role skills folder {_setup.RoleSkillsDirectory} is not there, so nothing was started; `jason skills install --roles-only` places the role skills there.";
        Assert.Equal("folder_missing", (string?)Answer()["error"]!["code"]);
        Assert.Equal(said, (string?)Answer()["error"]!["message"]);
        Assert.Equal(said, _run.Error.ToString().TrimEnd());
        Assert.False(Directory.Exists(_setup.RoleSkillsDirectory));
    }

    /// <summary>Opening it changes nothing: the data directory holds the same entries before and after.</summary>
    [Fact]
    public async Task Setup_writes_no_file_of_jasons()
    {
        _run.Programs.AddProgram("claude");
        string[] Tree() => [.. Directory.EnumerateFileSystemEntries(_run.DataDirectory, "*", SearchOption.AllDirectories)
            .Where(entry => !entry.StartsWith(_run.Programs.Root, StringComparison.Ordinal))
            .Order(StringComparer.Ordinal)];
        _ = await _run.Cli("session", "open", "--setup", "--host", "claude_code", "--dry-run");
        var before = Tree();

        var exit = await _run.Cli("session", "open", "--setup");

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Equal(before, Tree());
    }

    /// <summary>
    /// The folders are the ones <see cref="SetupInfo.From"/> names for the data directory the CLI runs on, so a layout that
    /// moves them is followed here with no edit.
    /// </summary>
    [Fact]
    public async Task Setup_follows_the_folders_SetupInfo_names()
    {
        _run.Programs.AddProgram("claude");

        _ = await _run.Cli("session", "open", "--setup", "--host", "claude_code", "--dry-run");

        var answer = Answer();
        var expected = SetupInfo.From(new JasonPaths(_run.DataDirectory));
        Assert.Equal(expected.RoleSkillsDirectory, (string?)answer["folder"]!["path"]);
        Assert.Equal(expected.PluginsDirectory, (string?)answer["plugins_folder"]!["path"]);
    }

    /// <summary>
    /// The setup form composes no path of Jason's layout: its folders come from <see cref="SetupInfo"/> alone, so a later
    /// layout is followed by changing the one place the layout is written.
    /// </summary>
    [Fact]
    public void The_setup_opener_spells_no_part_of_the_layout()
    {
        foreach (var file in new[] { "SetupSessionOpener.cs", "SetupBrief.cs" })
        {
            var source = File.ReadAllText(Path.Combine(RepositoryRoot(), "dotnet", "src", "Jason.Cli", "Session", file));
            Assert.DoesNotContain("Path.Combine", source, StringComparison.Ordinal);
            Assert.DoesNotContain("JasonPaths.", source.Replace("JasonPaths.DataDirectoryVariable", string.Empty, StringComparison.Ordinal), StringComparison.Ordinal);
            foreach (var part in new[] { "\"roles\"", "\"plugins\"", "\"skills\"", "\"config\"", "\"ui\"", "settings.json" })
            {
                Assert.DoesNotContain(part, source, StringComparison.Ordinal);
            }
        }
    }

    [Fact]
    public async Task Setup_dry_run_prints_the_whole_command_and_the_brief_and_starts_nothing()
    {
        var claude = _run.Programs.AddProgram("claude");

        var exit = await _run.Cli("session", "open", "--setup", "--host", "claude_code", "--dry-run");

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Empty(_run.Terminals.Launches);
        var answer = Answer();
        Assert.False((bool)answer["started"]!);
        Assert.Equal(
            [_run.ConsoleHost, "--", claude, SetupBrief.Text, "--add-dir", _setup.PluginsDirectory],
            answer["argv"]!.AsArray().Select(word => word!.GetValue<string>()));
    }

    /// <summary>On macOS the one script Terminal runs is named for the form and carries the brief as one quoted word.</summary>
    [Fact]
    public async Task Setup_on_macos_writes_one_script_named_for_setup_with_the_brief_quoted()
    {
        _run.Terminals.Platform = TerminalPlatform.MacOs;
        var claude = _run.Programs.AddProgram("claude");

        var exit = await _run.Cli("session", "open", "--setup", "--host", "claude_code", "--dry-run");

        Assert.Equal(ExitCodes.Success, exit);
        var answer = Answer();
        Assert.Equal(["/usr/bin/open", "-a", "Terminal", "jason-setup.command"], answer["argv"]!.AsArray().Select(word => word!.GetValue<string>()));
        var script = answer["script"]!.GetValue<string>().Split('\n');
        Assert.Contains("cd -- " + TerminalPlan.ShellQuote(_setup.RoleSkillsDirectory) + " || exit 1", script);
        Assert.Contains(
            "exec " + string.Join(' ', new[] { claude, SetupBrief.Text, "--add-dir", _setup.PluginsDirectory }.Select(TerminalPlan.ShellQuote)),
            answer["script"]!.GetValue<string>(),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Setup_refuses_a_host_that_is_not_installed()
    {
        var exit = await _run.Cli("session", "open", "--setup", "--host", "codex");

        Assert.Equal(ExitCodes.ApiError, exit);
        Assert.Equal("host_not_found", (string?)Answer()["error"]!["code"]);
        Assert.Empty(_run.Terminals.Launches);
    }

    [Fact]
    public async Task Setup_with_no_runtime_and_no_host_exits_3()
    {
        _run.Programs.AddProgram("claude");
        _run.Descriptor = false;

        var exit = await _run.Cli("session", "open", "--setup");

        Assert.Equal(ExitCodes.RuntimeUnavailable, exit);
        Assert.Empty(_run.Terminals.Launches);
    }

    [Fact]
    public async Task Setup_says_when_no_terminal_is_found()
    {
        _run.Programs.AddProgram("claude");

        var exit = await _run.Cli(terminals: false, "session", "open", "--setup", "--host", "claude_code");

        Assert.Equal(ExitCodes.ApiError, exit);
        Assert.Equal("terminal_not_found", (string?)Answer()["error"]!["code"]);
        Assert.Equal(
            "No terminal was found to start claude_code in, so nothing was started; `jason session open --setup --dry-run` prints the command and its brief, to run in a terminal of your own.",
            _run.Error.ToString().TrimEnd());
    }

    // ---- The skill the brief names ----

    /// <summary>
    /// Where the host's skills root holds no receipt naming the skill, the answer says it is not deployed, and a person is
    /// told the one line that deploys it.
    /// </summary>
    [Fact]
    public async Task Setup_says_when_the_skill_is_not_deployed_for_the_host_it_opens()
    {
        _run.Programs.AddProgram("claude");

        var exit = await _run.Cli("session", "open", "--setup", "--host", "claude_code");

        Assert.Equal(ExitCodes.Success, exit);
        Assert.False((bool)Answer()["skill"]!["deployed"]!);

        _run.Output.GetStringBuilder().Clear();
        _ = await _run.Cli("session", "open", "--setup", "--host", "claude_code", "--human");
        Assert.Equal(
            [
                $"Started claude_code in conhost at the role skills folder {_setup.RoleSkillsDirectory}, with the plugins folder {_setup.PluginsDirectory} beside it.",
                "The skill customising-jason, which teaches this session the job, is not deployed for claude_code: `jason skills install --host claude_code` deploys it.",
            ],
            _run.Output.ToString().TrimEnd().Split('\n').Select(line => line.TrimEnd('\r')));
    }

    /// <summary>A skill deployed for another host's root is not deployed for this one: the receipt is read per host.</summary>
    [Fact]
    public async Task Setup_reads_the_receipt_of_the_host_it_opens_and_no_other()
    {
        _run.Programs.AddProgram("codex");
        DeploySkill(AgentHostKind.ClaudeCode);

        _ = await _run.Cli("session", "open", "--setup", "--host", "codex");

        Assert.False((bool)Answer()["skill"]!["deployed"]!);
    }

    /// <summary>A receipt this build cannot read is not a skill that is absent: the answer says it does not know.</summary>
    [Fact]
    public async Task Setup_does_not_guess_from_a_receipt_it_cannot_read()
    {
        _run.Programs.AddProgram("claude");
        var root = SkillsRoot(AgentHostKind.ClaudeCode);
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, SkillsRecord.FileName), "not a record");

        var exit = await _run.Cli("session", "open", "--setup", "--host", "claude_code", "--human");

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Contains(
            "Whether the skill customising-jason is deployed for claude_code could not be read; `jason skills install --host claude_code` deploys it.",
            _run.Output.ToString(),
            StringComparison.Ordinal);
        _run.Output.GetStringBuilder().Clear();
        _ = await _run.Cli("session", "open", "--setup", "--host", "claude_code");
        Assert.Null(Answer()["skill"]!["deployed"]);
        Assert.Equal(root, (string?)Answer()["skill"]!["root"]);
    }

    [Fact]
    public async Task Setup_says_under_human_when_the_host_takes_no_second_folder_or_it_is_not_there()
    {
        _run.Programs.AddProgram("copilot");
        _run.Programs.AddProgram("claude");
        DeploySkill(AgentHostKind.CopilotCli);
        DeploySkill(AgentHostKind.ClaudeCode);

        _ = await _run.Cli("session", "open", "--setup", "--host", "copilot_cli", "--human");
        Assert.Equal(
            $"Started copilot_cli in conhost at the role skills folder {_setup.RoleSkillsDirectory}; copilot_cli takes no second folder, so the plugins folder {_setup.PluginsDirectory} is not beside it.",
            _run.Output.ToString().TrimEnd());

        _run.Output.GetStringBuilder().Clear();
        Directory.Delete(_setup.PluginsDirectory);
        _ = await _run.Cli("session", "open", "--setup", "--host", "claude_code", "--human", "--dry-run");
        Assert.Equal(
            $"Would start claude_code in conhost at the role skills folder {_setup.RoleSkillsDirectory}; the plugins folder {_setup.PluginsDirectory} is not there yet, so it is not beside it.",
            _run.Output.ToString().TrimEnd());
    }

    // ---- What it is not taken with ----

    public static TheoryData<string[], string> NotTakenWith() => new()
    {
        { ["--approval", "apr_1"], "--setup opens a session on Jason itself, with no item; it is not taken with --approval or --question." },
        { ["--question", "dec_1"], "--setup opens a session on Jason itself, with no item; it is not taken with --approval or --question." },
        { ["--campaign", "cmp_1"], "--setup opens at this installation's role skills folder; it is not taken with --campaign, --project or --here." },
        { ["--project", "acme-corp"], "--setup opens at this installation's role skills folder; it is not taken with --campaign, --project or --here." },
        { ["--here"], "--setup opens at this installation's role skills folder; it is not taken with --campaign, --project or --here." },
        { ["--shell"], "--setup starts an agent host with the setup brief as its first message; it is not taken with --shell." },
        { ["--resume", "abc"], "--setup starts a new conversation with the setup brief as its first message; it is not taken with --resume." },
    };

    [Theory]
    [MemberData(nameof(NotTakenWith))]
    public async Task Setup_is_not_taken_with_an_item_or_a_folder(string[] with, string said)
    {
        _run.Programs.AddProgram("claude");

        var exit = await _run.Cli(["session", "open", "--setup", .. with]);

        Assert.Equal(ExitCodes.Usage, exit);
        Assert.Contains(said, _run.Error.ToString(), StringComparison.Ordinal);
        Assert.Empty(_run.Asked);
        Assert.Empty(_run.Terminals.Launches);
    }

    private static string RepositoryRoot()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "global.json")))
        {
            root = root.Parent;
        }

        Assert.NotNull(root);
        return root.FullName;
    }
}
