using Jason.Cli.Status;
using Jason.Contracts.Api;
using Jason.Runtime.Tests;

namespace Jason.Cli.Tests.Status;

/// <summary>
/// The required check that the runtime claims work. A runtime left draining — by <c>jason runtime drain</c>, or by an update
/// or an install that ended part-way — answers every question and claims nothing, and <c>ready</c> said yes to it; only
/// <c>jason runtime status</c> showed it.
/// </summary>
public sealed class DispatcherCheckTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>
    /// A draining runtime is a required failure with the resume as its repair, and <c>ready</c> is false while everything
    /// else passes; the exit is 1, never 3, and a person reading it is given the same line to type.
    /// </summary>
    [Fact]
    [Critical(Family.I2, "a draining runtime is not ready")]
    public async Task A_draining_runtime_is_a_required_failure_with_the_resume()
    {
        using var dir = new TempPaths();
        var output = new StringWriter();
        StatusCommandTests.Running(dir);
        StatusCommandTests.Dispatcher(dir, new DispatcherInfo(DispatcherState.Draining, 10, 4, 2, null, 0, 0));

        var exit = await CliApp.RunAsync(["status"], StatusCommandTests.Machine(dir, output), Ct);

        var report = StatusCommandTests.Read(output);
        Assert.False(report.Ready, "ready said yes to a runtime that claims nothing");
        Assert.Equal(ExitCodes.ApiError, exit);
        var check = Assert.Single(report.Checks, check => check.Name == "dispatcher");
        Assert.True(check.Required);
        Assert.Equal(CheckState.Failed, check.State);
        Assert.Equal(
            "The runtime is draining: it claims no new work until `jason runtime resume` or a restart of the runtime; 2 attempts are still running.",
            check.Fact);
        Assert.Equal("jason runtime resume", check.Fix);
        Assert.All(report.Checks.Where(other => other.Name != "dispatcher" && other.Required), other => Assert.Equal(CheckState.Ok, other.State));

        var human = new StringWriter();
        Assert.Equal(ExitCodes.ApiError, await CliApp.RunAsync(["status", "--human"], StatusCommandTests.Machine(dir, human), Ct));
        var text = human.ToString();
        Assert.Contains("jason runtime resume", text[text.IndexOf("To repair:", StringComparison.Ordinal)..].Split(Environment.NewLine).Select(line => line.Trim()));
    }

    /// <summary>A runtime that claims work passes, and so does one whose settings turned its dispatcher off, which is said.</summary>
    [Theory]
    [InlineData(DispatcherState.Running, "The runtime claims work.")]
    [InlineData(DispatcherState.Disabled, "The dispatcher is turned off by the settings (Dispatcher:Enabled), so this runtime claims no work.")]
    public async Task A_runtime_that_claims_or_was_set_not_to_passes(DispatcherState state, string fact)
    {
        using var dir = new TempPaths();
        var output = new StringWriter();
        StatusCommandTests.Running(dir);
        StatusCommandTests.Dispatcher(dir, new DispatcherInfo(state, 10, 4, 0, null, 0, 0));

        var exit = await CliApp.RunAsync(["status"], StatusCommandTests.Machine(dir, output), Ct);

        var report = StatusCommandTests.Read(output);
        var check = Assert.Single(report.Checks, check => check.Name == "dispatcher");
        Assert.True(check.Required);
        Assert.Equal(CheckState.Ok, check.State);
        Assert.Equal(fact, check.Fact);
        Assert.Null(check.Fix);
        Assert.True(report.Ready);
        Assert.Equal(ExitCodes.Success, exit);
    }

    /// <summary>A runtime that did not answer is not asked: unknown, with the start the runtime check asks for, and exit 1, never 3.</summary>
    [Fact]
    public async Task Without_a_runtime_it_is_unknown_and_status_still_exits_1()
    {
        using var dir = new TempPaths();
        var output = new StringWriter();

        var exit = await CliApp.RunAsync(["status"], StatusCommandTests.Machine(dir, output), Ct);

        Assert.Equal(ExitCodes.ApiError, exit);
        var check = Assert.Single(StatusCommandTests.Read(output).Checks, check => check.Name == "dispatcher");
        Assert.True(check.Required);
        Assert.Equal(CheckState.Unknown, check.State);
        Assert.Equal("jason runtime start", check.Fix);
    }

    /// <summary>The verb's help names the check and its repair among the required ones.</summary>
    [Fact]
    public async Task The_help_names_the_draining_check_among_the_required()
    {
        using var dir = new TempPaths();
        var output = new StringWriter();

        Assert.Equal(ExitCodes.Success, await CliApp.RunAsync(["status", "--help"], StatusCommandTests.Machine(dir, output), Ct));

        var help = string.Join(' ', output.ToString().Split((char[])['\r', '\n', ' '], StringSplitOptions.RemoveEmptyEntries));
        var required = help[help.IndexOf("Required", StringComparison.Ordinal)..help.IndexOf("Optional", StringComparison.Ordinal)];
        Assert.Contains("the runtime is not draining (its repair is jason runtime resume)", required, StringComparison.Ordinal);
        Assert.Contains("The dispatcher check fails while the runtime is draining", help, StringComparison.Ordinal);
    }
}
