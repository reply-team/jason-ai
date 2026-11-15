using Jason.Cli.Status;
using Jason.Runtime.Tests;

namespace Jason.Cli.Tests.Status;

/// <summary>
/// The <c>roles_launchable</c> check: whether a role's work would find a profile by <c>Roles:DefaultExecutionProfile</c>,
/// read from the settings file and the environment as the runtime reads it. A fresh installation has no profile and names
/// no default, and every role's work then fails at its claim; the check says so and names whichever of the one command and
/// the one line of settings is still missing. It is ok only where the default names an enabled profile, and it never
/// decides readiness.
/// </summary>
public sealed class RolesLaunchableCheckTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private const string SettingsFile = "/data/config/settings.json";

    private const string Create = "jason profile create claude-code --host claude_code --program claude";

    private const string NotRead =
        "This check reads that default alone: not a profile a work item, a campaign's policy or a role's policy names, nor an entry command.";

    private static readonly StatusChecks.DefaultProfileSetting None = new(null, null);

    private static StatusChecks.DefaultProfileSetting Named(string name) => new(name, null);

    [Fact]
    public void No_profile_and_no_default_is_absent_and_names_the_command_and_the_settings_line()
    {
        var check = StatusChecks.RolesLaunchableCheck([], unread: null, SettingsFile, None);

        Assert.Equal("roles_launchable", check.Name);
        Assert.False(check.Required);
        Assert.Equal(CheckState.Absent, check.State);
        Assert.Equal(
            "No enabled execution profile exists and Roles:DefaultExecutionProfile names none, so a role's work that nothing "
            + "else names a profile for fails at its claim with role_not_launchable, unless the role has an entry command of its "
            + "own or Roles:DefaultEntryCommand names one. "
            + "`jason profile create claude-code --host claude_code --program claude`, then "
            + "`\"Roles\": {\"DefaultExecutionProfile\": \"claude-code\"}` in /data/config/settings.json, which the runtime "
            + "reads without a restart, run the roles on Claude Code. " + NotRead,
            check.Fact);
        Assert.Equal(Create, check.Fix);
    }

    /// <summary>
    /// The install line's half-done repair: the profile made, the line not yet written. Every role's work still fails, so the
    /// check is not ok, and the one step left is the line, which is no command: there is no fix to run.
    /// </summary>
    [Fact]
    public void The_profile_made_and_no_default_named_is_absent_and_names_the_line_alone()
    {
        var check = StatusChecks.RolesLaunchableCheck([new("claude-code", "claude_code"), new("night-run", "codex")], unread: null, SettingsFile, None);

        Assert.Equal(CheckState.Absent, check.State);
        Assert.Equal(
            "2 enabled execution profiles exist and Roles:DefaultExecutionProfile names none, so a role's work that nothing else "
            + "names a profile for fails at its claim with role_not_launchable, unless the role has an entry command of its own "
            + "or Roles:DefaultEntryCommand names one. "
            + "`\"Roles\": {\"DefaultExecutionProfile\": \"claude-code\"}` in /data/config/settings.json, which the runtime "
            + "reads without a restart, runs the roles on claude-code. " + NotRead,
            check.Fact);
        Assert.Null(check.Fix);
    }

    [Fact]
    public void A_person_s_own_profile_and_no_default_names_the_line_with_its_name_or_with_one_of_theirs()
    {
        var one = StatusChecks.RolesLaunchableCheck([new("local-cursor", "cursor_cli")], unread: null, SettingsFile, None);
        Assert.Equal(CheckState.Absent, one.State);
        Assert.StartsWith("1 enabled execution profile exists and Roles:DefaultExecutionProfile names none, so ", one.Fact, StringComparison.Ordinal);
        Assert.Contains(
            "`\"Roles\": {\"DefaultExecutionProfile\": \"local-cursor\"}` in /data/config/settings.json, which the runtime reads without a restart, runs the roles on local-cursor.",
            one.Fact,
            StringComparison.Ordinal);
        Assert.Null(one.Fix);

        var two = StatusChecks.RolesLaunchableCheck([new("local-cursor", "cursor_cli"), new("night-run", "codex")], unread: null, SettingsFile, None);
        Assert.Contains(
            "`\"Roles\": {\"DefaultExecutionProfile\": \"<name>\"}` in /data/config/settings.json, with the name of one of them "
            + "(local-cursor, night-run), which the runtime reads without a restart, runs the roles on it.",
            two.Fact,
            StringComparison.Ordinal);
        Assert.Null(two.Fix);
    }

    [Fact]
    public void A_default_that_names_an_enabled_profile_is_ok_and_says_what_the_check_does_not_read()
    {
        var check = StatusChecks.RolesLaunchableCheck([new("local-claude", "claude_code"), new("night-run", "codex")], unread: null, SettingsFile, Named("night-run"));

        Assert.Equal(CheckState.Ok, check.State);
        Assert.Equal(
            "Roles:DefaultExecutionProfile names night-run, an enabled execution profile, so a role's work that nothing else "
            + "names a profile for runs on it. " + NotRead,
            check.Fact);
        Assert.Null(check.Fix);
    }

    /// <summary>The line written and the profile not made, or out of service: the command is the step left.</summary>
    [Fact]
    public void A_default_named_claude_code_with_no_such_enabled_profile_names_the_command_alone()
    {
        var check = StatusChecks.RolesLaunchableCheck([], unread: null, SettingsFile, Named("claude-code"));

        Assert.Equal(CheckState.Absent, check.State);
        Assert.Equal(
            "Roles:DefaultExecutionProfile names claude-code, which is not an enabled execution profile here, so a role's work "
            + "that nothing else names a profile for fails at its claim with profile_not_found or profile_disabled. "
            + "`jason profile create claude-code --host claude_code --program claude` makes it, or `jason profile enable "
            + "claude-code` where it is there and disabled, and the roles run on Claude Code. " + NotRead,
            check.Fact);
        Assert.Equal(Create, check.Fix);
    }

    [Fact]
    public void A_default_that_names_no_enabled_profile_names_what_is_missing_of_the_repair()
    {
        var both = StatusChecks.RolesLaunchableCheck([new("mine", "codex")], unread: null, SettingsFile, Named("house"));
        Assert.Equal(CheckState.Absent, both.State);
        Assert.Equal(
            "Roles:DefaultExecutionProfile names house, which is not an enabled execution profile here, so a role's work that "
            + "nothing else names a profile for fails at its claim with profile_not_found or profile_disabled. "
            + "`jason profile create claude-code --host claude_code --program claude`, then "
            + "`\"Roles\": {\"DefaultExecutionProfile\": \"claude-code\"}` in /data/config/settings.json in place of the name it "
            + "gives, which the runtime reads without a restart, run the roles on Claude Code. " + NotRead,
            both.Fact);
        Assert.Equal(Create, both.Fix);

        var line = StatusChecks.RolesLaunchableCheck([new("claude-code", "claude_code")], unread: null, SettingsFile, Named("house"));
        Assert.Equal(CheckState.Absent, line.State);
        Assert.Contains(
            "`\"Roles\": {\"DefaultExecutionProfile\": \"claude-code\"}` in /data/config/settings.json in place of the name it "
            + "gives, which the runtime reads without a restart, runs the roles on claude-code.",
            line.Fact,
            StringComparison.Ordinal);
        Assert.Null(line.Fix);
    }

    [Fact]
    public void Profiles_or_settings_that_could_not_be_read_are_unknown_and_say_why()
    {
        var check = StatusChecks.RolesLaunchableCheck(null, "the runtime refused to list them", SettingsFile, None);

        Assert.Equal(CheckState.Unknown, check.State);
        Assert.Equal("Whether a role can run is not known: the profiles could not be read: the runtime refused to list them.", check.Fact);
        Assert.Null(check.Fix);

        var silent = StatusChecks.RolesLaunchableCheck(null, unread: null, SettingsFile, None);
        Assert.Equal("Whether a role can run is not known: the profiles could not be read: the runtime did not answer.", silent.Fact);

        var settings = StatusChecks.RolesLaunchableCheck([new("claude-code", "claude_code")], unread: null, SettingsFile, new(null, "it is not JSON"));
        Assert.Equal(CheckState.Unknown, settings.State);
        Assert.Equal(
            "Whether a role can run is not known: the settings file at /data/config/settings.json cannot be read (it is not JSON), "
            + "so the default profile it may name is not known.",
            settings.Fact);
        Assert.Null(settings.Fix);
    }

    /// <summary>
    /// The default as the runtime's configuration reads it: <c>JASON_Roles__DefaultExecutionProfile</c> over the settings
    /// file, keys compared without case, a key spelt with a colon the same as the section it names, comments allowed, and a
    /// blank value no value — a blank variable included, which hides the file's.
    /// </summary>
    [Fact]
    public void The_default_is_read_from_the_variable_over_the_settings_file_as_the_runtime_reads_it()
    {
        using var dir = new TempPaths();
        var env = new CliEnvironment(new StringWriter(), new StringWriter(), dir.Paths, Variables: new Dictionary<string, string>());

        Assert.Equal(None, StatusChecks.DefaultProfileOf(env));

        Directory.CreateDirectory(Path.GetDirectoryName(dir.Paths.UserSettingsFile)!);
        File.WriteAllText(dir.Paths.UserSettingsFile, """{ "roles": { "defaultexecutionprofile": "claude-code" }, /* a person's note */ }""");
        Assert.Equal(Named("claude-code"), StatusChecks.DefaultProfileOf(env));

        File.WriteAllText(dir.Paths.UserSettingsFile, """{ "Roles:DefaultExecutionProfile": " night-run " }""");
        Assert.Equal(Named("night-run"), StatusChecks.DefaultProfileOf(env));

        File.WriteAllText(dir.Paths.UserSettingsFile, """{ "Roles": { "DefaultExecutionProfile": "  " } }""");
        Assert.Equal(None, StatusChecks.DefaultProfileOf(env));

        File.WriteAllText(dir.Paths.UserSettingsFile, """{ "Roles": { "DefaultExecutionProfile": "claude-code" } }""");
        var variable = env with { Variables = new Dictionary<string, string> { ["jason_roles__defaultexecutionprofile"] = "codex" } };
        Assert.Equal(Named("codex"), StatusChecks.DefaultProfileOf(variable));
        var blank = env with { Variables = new Dictionary<string, string> { ["JASON_Roles__DefaultExecutionProfile"] = "" } };
        Assert.Equal(None, StatusChecks.DefaultProfileOf(blank));

        File.WriteAllText(dir.Paths.UserSettingsFile, "{ not json");
        var unread = StatusChecks.DefaultProfileOf(env);
        Assert.Null(unread.Name);
        Assert.StartsWith("it is not JSON the configuration reads: ", unread.Unread, StringComparison.Ordinal);
        Assert.Equal(Named("codex"), StatusChecks.DefaultProfileOf(variable));
    }

    /// <summary>
    /// A fresh installation, as the install line leaves it: a runtime with no profile. The check comes after
    /// <c>agent_hosts</c>, names this installation's own settings file, and leaves <c>ready</c> to the required checks. The
    /// profile made and not named is still not ok; the line written beside it is.
    /// </summary>
    [Fact]
    public async Task Status_says_no_role_can_run_until_the_profile_is_made_and_named_and_keeps_ready_to_the_required_checks()
    {
        using var dir = new TempPaths();
        StatusCommandTests.Running(dir);
        StatusCommandTests.Profiles(dir);
        var report = await StatusAsync(dir);
        var checks = report.Checks.ToList();
        var at = checks.FindIndex(check => check.Name == "roles_launchable");
        Assert.Equal("agent_hosts", checks[at - 1].Name);
        Assert.Equal(CheckState.Absent, checks[at].State);
        Assert.Contains($"in {dir.Paths.UserSettingsFile}, which", checks[at].Fact, StringComparison.Ordinal);
        Assert.Equal(Create, checks[at].Fix);
        Assert.Equal(report.Checks.Where(check => check.Required).All(check => check.State == CheckState.Ok), report.Ready);

        using var made = new TempPaths();
        StatusCommandTests.Running(made);
        StatusCommandTests.Profiles(made, ("claude-code", "claude_code"));
        var half = await StatusAsync(made);
        var unnamed = Assert.Single(half.Checks, check => check.Name == "roles_launchable");
        Assert.Equal(CheckState.Absent, unnamed.State);
        Assert.Null(unnamed.Fix);
        Assert.Contains("`\"Roles\": {\"DefaultExecutionProfile\": \"claude-code\"}`", unnamed.Fact, StringComparison.Ordinal);
        Assert.Equal(report.Ready, half.Ready);

        Directory.CreateDirectory(Path.GetDirectoryName(made.Paths.UserSettingsFile)!);
        File.WriteAllText(made.Paths.UserSettingsFile, """{"Roles": {"DefaultExecutionProfile": "claude-code"}}""");
        var after = await StatusAsync(made);
        Assert.Equal(CheckState.Ok, Assert.Single(after.Checks, check => check.Name == "roles_launchable").State);
        Assert.Equal(report.Ready, after.Ready);
    }

    private static async Task<StatusReport> StatusAsync(TempPaths dir)
    {
        var output = new StringWriter();
        await CliApp.RunAsync(["status"], StatusCommandTests.Machine(dir, output) with { Variables = new Dictionary<string, string>() }, Ct);
        return StatusCommandTests.Read(output);
    }

    [Fact]
    public async Task The_help_lists_the_check_among_the_optional_ones_and_names_the_command_and_the_line()
    {
        var output = new StringWriter();
        using var dir = new TempPaths();
        await CliApp.RunAsync(["status", "--help"], new CliEnvironment(output, new StringWriter(), dir.Paths), Ct);

        var help = string.Join(' ', output.ToString().Split((char[])['\r', '\n', ' '], StringSplitOptions.RemoveEmptyEntries));
        var optional = help.IndexOf("Optional — absent never fails:", StringComparison.Ordinal);
        Assert.True(optional > 0, help);
        Assert.True(help.IndexOf("a default execution profile is named for the AI roles to run on, and is enabled", StringComparison.Ordinal) > optional, help);
        Assert.Contains(
            "jason profile create claude-code --host claude_code --program claude, then \"Roles\": {\"DefaultExecutionProfile\": \"claude-code\"} in the settings file",
            help,
            StringComparison.Ordinal);
        Assert.Contains("JASON_Roles__DefaultExecutionProfile", help, StringComparison.Ordinal);
        Assert.Contains("It is ok only where that default names an enabled profile", help, StringComparison.Ordinal);
    }
}
