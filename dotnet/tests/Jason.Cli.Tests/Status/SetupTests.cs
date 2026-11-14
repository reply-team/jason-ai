using System.Text.Json.Nodes;
using Jason.Cli.Status;
using Jason.Contracts.Api;
using Jason.Runtime.Tests;

namespace Jason.Cli.Tests.Status;

/// <summary>
/// Where this installation's parts are, as <c>jason status</c> says it: under <c>setup</c> in the body and as four lines
/// under the checks in <c>--human</c>, composed from this CLI's own data directory, so it is said whether or not a runtime
/// answers and never decides <c>ready</c>.
/// </summary>
public class SetupTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>
    /// With no runtime at all: the body still carries <c>ready</c> and every part, each the folder or file this CLI's
    /// own paths name, and the verb exits 1, never 3.
    /// </summary>
    [Fact]
    public async Task Status_carries_where_the_parts_are_with_no_runtime_answering()
    {
        using var dir = new TempPaths();
        var output = new StringWriter();

        var exit = await CliApp.RunAsync(["status"], StatusCommandTests.Machine(dir, output), Ct);

        Assert.Equal(ExitCodes.ApiError, exit);
        var body = JsonNode.Parse(output.ToString())!.AsObject();
        Assert.Equal(["ready", "checks", "setup"], body.Select(member => member.Key));
        Assert.False(body["ready"]!.GetValue<bool>());

        var setup = body["setup"]!.AsObject();
        Assert.Equal(
            ["role_skills_directory", "plugins_directory", "custom_ui_directory", "settings_file", "settings_file_holds_secrets"],
            setup.Select(member => member.Key));
        Assert.Equal(dir.Paths.RoleSkillsDirectory, setup["role_skills_directory"]!.GetValue<string>());
        Assert.Equal(dir.Paths.PluginsDirectory, setup["plugins_directory"]!.GetValue<string>());
        Assert.Equal(dir.Paths.UiCustomDirectory, setup["custom_ui_directory"]!.GetValue<string>());
        Assert.Equal(dir.Paths.UserSettingsFile, setup["settings_file"]!.GetValue<string>());
        Assert.True(setup["settings_file_holds_secrets"]!.GetValue<bool>());
    }

    /// <summary>
    /// The parts never decide readiness: a ready installation is ready with them in its body, exit 0, and the parts are
    /// this CLI's own even where the runtime that answered said nothing of them.
    /// </summary>
    [Fact]
    public async Task Setup_never_decides_ready()
    {
        using var dir = new TempPaths();
        var output = new StringWriter();
        StatusCommandTests.Running(dir);

        var exit = await CliApp.RunAsync(["status"], StatusCommandTests.Machine(dir, output), Ct);

        var report = StatusCommandTests.Read(output);
        Assert.True(report.Ready, string.Join(Environment.NewLine, report.Checks.Where(check => check.Required && check.State != CheckState.Ok).Select(check => check.Fact)));
        Assert.Equal(ExitCodes.Success, exit);
        Assert.Equal(SetupInfo.From(dir.Paths), report.Setup);
    }

    /// <summary>
    /// The four lines, in this order, their labels in one column, after the checks and before the repairs — so the repairs
    /// stay the last thing said.
    /// </summary>
    [Fact]
    public async Task Status_human_prints_the_four_places_after_the_checks_and_before_the_repairs()
    {
        using var dir = new TempPaths();
        var output = new StringWriter();

        await CliApp.RunAsync(["status", "--human"], StatusCommandTests.Machine(dir, output), Ct);

        var lines = output.ToString().Split(Environment.NewLine);
        var first = Array.FindIndex(lines, line => line.StartsWith("Role skills:", StringComparison.Ordinal));
        Assert.True(first > 0, output.ToString());
        Assert.Equal(
            [
                $"Role skills: {dir.Paths.RoleSkillsDirectory}",
                $"Plugins:     {dir.Paths.PluginsDirectory}",
                $"Custom UI:   {dir.Paths.UiCustomDirectory}",
                $"Settings:    {dir.Paths.UserSettingsFile} (where plugin secrets are kept: change routes and grants with `jason route set --global` and `jason plugin grant`)",
            ],
            lines[first..(first + 4)]);
        Assert.Equal(string.Empty, lines[first - 1]);
        Assert.Contains("CHECK", string.Join('\n', lines[..first]), StringComparison.Ordinal);
        var repairs = Array.IndexOf(lines, "To repair:");
        Assert.True(repairs > first + 4, "The repairs come after the parts.");
    }

    /// <summary>
    /// Where the settings file holds no secrets — the layout to come — the line says it is the person's to change, and
    /// nothing about secrets. Composed by hand, because no layout of this build answers so yet.
    /// </summary>
    [Fact]
    public void Status_human_says_settings_you_may_change_when_the_file_holds_no_secrets()
    {
        var setup = new SetupInfo("/data/roles", "/data/plugins", "/data/ui", "/data/settings.json", SettingsFileHoldsSecrets: false);
        var output = new StringWriter();

        StatusCommand.Render(output, new StatusReport(true, [], setup));

        var text = output.ToString();
        var lines = text.Split(Environment.NewLine);
        Assert.Equal(
            [
                "Role skills:             /data/roles",
                "Plugins:                 /data/plugins",
                "Custom UI:               /data/ui",
                "Settings you may change: /data/settings.json",
            ],
            lines[Array.FindIndex(lines, line => line.StartsWith("Role skills:", StringComparison.Ordinal))..][..4]);
        Assert.DoesNotContain("plugin secrets", text, StringComparison.Ordinal);
        Assert.DoesNotContain("To repair:", text, StringComparison.Ordinal);
    }
}
