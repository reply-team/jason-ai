using System.Text.Json.Nodes;
using Jason.Cli;
using Jason.Cli.Skills;
using Jason.Contracts.Api;
using Jason.Contracts.Hosts;

namespace Jason.Runtime.Tests.Integration;

/// <summary>
/// The line <c>jason status</c> prints to repair a profile whose host is not on this machine, typed back into the real
/// CLI against the real runtime: a repair the runtime refuses is no repair, and the rule it would break lives in the
/// runtime, where the CLI cannot read it.
/// </summary>
public sealed class StatusRepairAgainstRuntimeTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>A machine with exactly one agent host on it, at a folder no test shares.</summary>
    private sealed class OneHost(AgentHostKind kind, string directory) : IHarnessLocator
    {
        public IReadOnlyList<HarnessRoot> Detect() => [new([kind], directory)];
    }

    /// <summary>
    /// A profile made as the README says, for the first host, then a machine that has another one: the line status prints is
    /// accepted, and the profile names the host it was moved to and that host's program.
    /// </summary>
    [Theory]
    [InlineData(AgentHostKind.Codex)]
    [InlineData(AgentHostKind.CopilotCli)]
    [InlineData(AgentHostKind.CursorCli)]
    [InlineData(AgentHostKind.GeminiCli)]
    [InlineData(AgentHostKind.AntigravityCli)]
    public async Task The_repair_status_prints_for_a_profile_whose_host_is_not_here_is_accepted_by_the_runtime(AgentHostKind detected)
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct);
        await api.PostOkAsync<ExecutionProfileDto>(
            Operations.ProfileCreate, new { name = "moving", host = "claude_code", program = "claude" }, Ct);

        var status = new StringWriter();
        await CliApp.RunAsync(
            ["status"],
            new CliEnvironment(status, new StringWriter(), api.Paths, Harnesses: new OneHost(detected, Path.Combine(api.Paths.Root, "skills"))),
            Ct);
        var repair = (string?)JsonNode.Parse(status.ToString())!["checks"]!.AsArray()
            .Single(check => (string?)check!["name"] == "agent_hosts")!["fix"];

        Assert.NotNull(repair);
        var words = repair.Split(' ');
        Assert.Equal(["jason", "profile", "update", "moving"], words[..4]);
        var typed = new StringWriter();
        var error = new StringWriter();
        var exit = await CliApp.RunAsync(words[1..], new CliEnvironment(typed, error, api.Paths), Ct);

        Assert.Equal((ExitCodes.Success, string.Empty), (exit, error.ToString()));
        var moved = JsonNode.Parse(typed.ToString())!;
        Assert.Equal(AgentHosts.Word(detected), (string?)moved["revision"]!["host"]);
        Assert.Equal(AgentHosts.Of(detected).CommandWord, (string?)moved["revision"]!["program"]);
    }
}
