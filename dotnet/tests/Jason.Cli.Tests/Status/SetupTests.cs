using System.Text.Json.Nodes;
using Jason.Cli.Status;
using Jason.Contracts.Api;
using Jason.Runtime.Tests;

namespace Jason.Cli.Tests.Status;

/// <summary>
/// Where this installation's parts are, as <c>jason status</c> says it: under <c>setup</c> in the body and as five lines
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
            ["role_skills_directory", "plugins_directory", "custom_ui_directory", "settings_file", "settings_file_holds_secrets", "layout"],
            setup.Select(member => member.Key));
        Assert.Equal(dir.Paths.RoleSkillsDirectory, setup["role_skills_directory"]!.GetValue<string>());
        Assert.Equal(dir.Paths.PluginsDirectory, setup["plugins_directory"]!.GetValue<string>());
        Assert.Equal(dir.Paths.UiCustomDirectory, setup["custom_ui_directory"]!.GetValue<string>());
        Assert.Equal(dir.Paths.UserSettingsFile, setup["settings_file"]!.GetValue<string>());
        Assert.True(setup["settings_file_holds_secrets"]!.GetValue<bool>());
        Assert.Equal(1, setup["layout"]!.GetValue<int>());
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
    /// The five lines, in this order, their labels in one column, after the checks and before the repairs — so the repairs
    /// stay the last thing said.
    /// </summary>
    [Fact]
    public async Task Status_human_prints_the_four_places_and_the_layout_after_the_checks_and_before_the_repairs()
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
                $"Settings:    {dir.Paths.UserSettingsFile} (where plugin secrets are kept)",
                "Layout:      1",
            ],
            lines[first..(first + 5)]);
        Assert.Equal(string.Empty, lines[first - 1]);
        Assert.Contains("CHECK", string.Join('\n', lines[..first]), StringComparison.Ordinal);
        var repairs = Array.IndexOf(lines, "To repair:");
        Assert.True(repairs > first + 5, "The repairs come after the parts.");
    }

    /// <summary>
    /// Where the settings file holds no secrets — layout 2 — the line says it is the person's to change, and nothing about
    /// secrets; the parts are where layout 2 keeps them, and the layout is said under them.
    /// </summary>
    [Fact]
    public void Status_human_says_settings_you_may_change_when_the_file_holds_no_secrets()
    {
        var root = OperatingSystem.IsWindows() ? @"C:\data" : "/data";
        var paths = new Contracts.Discovery.JasonPaths(root, 2);
        var setup = SetupInfo.From(paths);
        var output = new StringWriter();

        StatusCommand.Render(output, new StatusReport(true, [], setup));

        var text = output.ToString();
        var lines = text.Split(Environment.NewLine);
        Assert.Equal(
            [
                $"Role skills:             {Path.Combine(root, "setup", "roles")}",
                $"Plugins:                 {Path.Combine(root, "setup", "plugins")}",
                $"Custom UI:               {Path.Combine(root, "setup", "ui")}",
                $"Settings you may change: {Path.Combine(root, "setup", "settings.json")}",
                "Layout:                  2",
            ],
            lines[Array.FindIndex(lines, line => line.StartsWith("Role skills:", StringComparison.Ordinal))..][..5]);
        Assert.DoesNotContain("plugin secrets", text, StringComparison.Ordinal);
        Assert.DoesNotContain("To repair:", text, StringComparison.Ordinal);
    }

    /// <summary>
    /// A data directory a newer Jason laid out is reported, never refused: the body carries the stamp's layout, the
    /// required layout check fails with the refusal's own sentence and the stamp's version and moment, <c>ready</c> is
    /// false, and the verb exits 1, never 3.
    /// </summary>
    [Fact]
    public async Task Status_reports_a_newer_layout_and_never_refuses()
    {
        using var dir = new TempPaths();
        File.WriteAllText(Path.Combine(dir.Paths.Root, "layout.json"), """{"layout":2,"written_by":"0.43.0","at":"2026-11-20T10:00:00Z"}""");
        var output = new StringWriter();

        var exit = await CliApp.RunAsync(["status"], StatusCommandTests.Machine(dir, output) with { Paths = new Contracts.Discovery.JasonPaths(dir.Paths.Root) }, Ct);

        Assert.Equal(ExitCodes.ApiError, exit);
        var report = StatusCommandTests.Read(output);
        Assert.False(report.Ready);
        Assert.Equal(2, report.Setup.Layout);
        var layout = Assert.Single(report.Checks, check => check.Name == "layout");
        Assert.True(layout.Required);
        Assert.Equal(CheckState.Failed, layout.State);
        Assert.StartsWith("This data directory was set up by Jason 0.43.0, newer than this jason (", layout.Fact, StringComparison.Ordinal);
        Assert.Null(layout.Fix);
        Assert.Equal(new Dictionary<string, string> { ["written_by"] = "0.43.0", ["at"] = "2026-11-20T10:00:00Z" }, layout.Details);
    }

    /// <summary>
    /// A stamp that cannot be read: the layout in the body is null, never a layout guessed for it, the check fails with the
    /// reason, and <c>--human</c> says the layout is unreadable.
    /// </summary>
    [Fact]
    public async Task Status_reports_an_unreadable_stamp_with_no_layout_and_its_reason()
    {
        using var dir = new TempPaths();
        File.WriteAllText(Path.Combine(dir.Paths.Root, "layout.json"), "{\"layout\":");
        var env = StatusCommandTests.Machine(dir, new StringWriter()) with { Paths = new Contracts.Discovery.JasonPaths(dir.Paths.Root) };
        var output = new StringWriter();

        var exit = await CliApp.RunAsync(["status"], env with { Out = output }, Ct);
        var human = new StringWriter();
        await CliApp.RunAsync(["status", "--human"], env with { Out = human }, Ct);

        Assert.Equal(ExitCodes.ApiError, exit);
        var body = JsonNode.Parse(output.ToString())!.AsObject();
        Assert.Null(body["setup"]!["layout"]);
        Assert.True(body["setup"]!.AsObject().ContainsKey("layout"));
        var report = StatusCommandTests.Read(output);
        var layout = Assert.Single(report.Checks, check => check.Name == "layout");
        Assert.Equal(CheckState.Failed, layout.State);
        Assert.Contains("cannot be read (it is not JSON at line 1, byte 11)", layout.Fact, StringComparison.Ordinal);
        Assert.Null(layout.Details);
        Assert.Contains(human.ToString().Split(Environment.NewLine), line => line.StartsWith("Layout:", StringComparison.Ordinal) && line.EndsWith(" unreadable", StringComparison.Ordinal));
    }

    /// <summary>With no stamp the layout check passes and says so, and never decides readiness against an installation that is otherwise ready.</summary>
    [Fact]
    public async Task With_no_stamp_the_layout_check_passes_saying_there_is_no_layout_json()
    {
        using var dir = new TempPaths();
        var output = new StringWriter();

        await CliApp.RunAsync(["status"], StatusCommandTests.Machine(dir, output) with { Paths = new Contracts.Discovery.JasonPaths(dir.Paths.Root) }, Ct);

        var layout = Assert.Single(StatusCommandTests.Read(output).Checks, check => check.Name == "layout");
        Assert.Equal(CheckState.Ok, layout.State);
        Assert.Equal("This data directory is laid out as this jason runs it (layout 1, no layout.json).", layout.Fact);
    }
}
