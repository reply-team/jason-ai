using Jason.Cli.Status;
using Jason.Runtime.Tests;

namespace Jason.Cli.Tests.Status;

/// <summary>
/// The <c>roles_launchable</c> check: whether an enabled execution profile exists for the AI roles to run on. A fresh
/// installation has none, and every role's work then fails at its claim; the check says so and names the one command
/// and the one line of settings that repair it. It never decides readiness.
/// </summary>
public sealed class RolesLaunchableCheckTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private const string SettingsFile = "/data/config/settings.json";

    [Fact]
    public void No_enabled_profile_is_absent_and_names_the_command_and_the_settings_line()
    {
        var check = StatusChecks.RolesLaunchableCheck([], unread: null, SettingsFile);

        Assert.Equal("roles_launchable", check.Name);
        Assert.False(check.Required);
        Assert.Equal(CheckState.Absent, check.State);
        Assert.Equal(
            "No enabled execution profile exists, so no AI role can run on one: a role's work fails at its claim with "
            + "role_not_launchable, unless the role has an entry command of its own or Roles:DefaultEntryCommand names one. "
            + "`jason profile create claude-code --host claude_code --program claude`, then "
            + "`\"Roles\": {\"DefaultExecutionProfile\": \"claude-code\"}` in /data/config/settings.json, which the runtime "
            + "reads without a restart, run the roles on Claude Code.",
            check.Fact);
        Assert.Equal("jason profile create claude-code --host claude_code --program claude", check.Fix);
    }

    [Fact]
    public void An_enabled_profile_is_ok_and_says_which_names_decide_the_one_a_role_runs_on()
    {
        var check = StatusChecks.RolesLaunchableCheck([new("local-claude", "claude_code"), new("night-run", "codex")], unread: null, SettingsFile);

        Assert.Equal(CheckState.Ok, check.State);
        Assert.Equal(
            "2 enabled execution profiles. A role's work runs on the one its work item, its campaign, its role or "
            + "Roles:DefaultExecutionProfile names, and fails at its claim where none names one.",
            check.Fact);
        Assert.Null(check.Fix);
    }

    [Fact]
    public void Profiles_that_could_not_be_read_are_unknown_and_say_why()
    {
        var check = StatusChecks.RolesLaunchableCheck(null, "the runtime refused to list them", SettingsFile);

        Assert.Equal(CheckState.Unknown, check.State);
        Assert.Equal("Whether a role can run is not known: the profiles could not be read: the runtime refused to list them.", check.Fact);
        Assert.Null(check.Fix);

        var silent = StatusChecks.RolesLaunchableCheck(null, unread: null, SettingsFile);
        Assert.Equal("Whether a role can run is not known: the profiles could not be read: the runtime did not answer.", silent.Fact);
    }

    /// <summary>
    /// A fresh installation, as the install line leaves it: a runtime with no profile. The check comes after
    /// <c>agent_hosts</c>, names this installation's own settings file, and leaves <c>ready</c> to the required checks.
    /// </summary>
    [Fact]
    public async Task Status_on_a_runtime_with_no_profile_says_no_role_can_run_and_keeps_ready_to_the_required_checks()
    {
        using var dir = new TempPaths();
        StatusCommandTests.Running(dir);
        StatusCommandTests.Profiles(dir);
        var none = new StringWriter();
        await CliApp.RunAsync(["status"], StatusCommandTests.Machine(dir, none), Ct);

        var report = StatusCommandTests.Read(none);
        var checks = report.Checks.ToList();
        var at = checks.FindIndex(check => check.Name == "roles_launchable");
        Assert.Equal("agent_hosts", checks[at - 1].Name);
        Assert.Equal(CheckState.Absent, checks[at].State);
        Assert.Contains($"in {dir.Paths.UserSettingsFile}, which", checks[at].Fact, StringComparison.Ordinal);
        Assert.Equal(report.Checks.Where(check => check.Required).All(check => check.State == CheckState.Ok), report.Ready);

        using var made = new TempPaths();
        StatusCommandTests.Running(made);
        StatusCommandTests.Profiles(made, ("claude-code", "claude_code"));
        var one = new StringWriter();
        await CliApp.RunAsync(["status"], StatusCommandTests.Machine(made, one), Ct);

        var after = StatusCommandTests.Read(one);
        Assert.Equal(CheckState.Ok, Assert.Single(after.Checks, check => check.Name == "roles_launchable").State);
        Assert.Equal(report.Ready, after.Ready);
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
        Assert.True(help.IndexOf("an enabled execution profile exists for the AI roles to run on", StringComparison.Ordinal) > optional, help);
        Assert.Contains(
            "jason profile create claude-code --host claude_code --program claude, then \"Roles\": {\"DefaultExecutionProfile\": \"claude-code\"} in the settings file",
            help,
            StringComparison.Ordinal);
    }
}
