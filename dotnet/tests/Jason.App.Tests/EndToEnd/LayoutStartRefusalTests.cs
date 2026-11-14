using System.Text.Json;
using Jason.Cli;
using Jason.Cli.Commands;
using Jason.Cli.Process;
using Jason.Contracts;
using Jason.Contracts.Discovery;

namespace Jason.App.Tests.EndToEnd;

/// <summary>
/// A data directory a newer Jason laid out, met by the shipped program: <c>jason runtime start</c> answers the code
/// itself and starts nothing, and a runtime started over it anyway refuses in the same envelope, which the start then
/// carries after "It said:".
/// </summary>
public class LayoutStartRefusalTests
{
    private const string Stamp = """{"layout":2,"written_by":"99.0.0","at":"2027-01-01T00:00:00Z"}""";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Runtime_start_answers_layout_newer_itself_and_starts_nothing()
    {
        using var it = GoldenPath.Create("start-layout-newer");
        await File.WriteAllTextAsync(Path.Combine(it.Root, "layout.json"), Stamp, Ct);

        var result = await GoldenPath.JasonAsync(it, "runtime", "start");
        if (result.ExitCode == 0)
        {
            await GoldenPath.StopAsync(it);
        }

        Assert.True(result.ExitCode == 1, $"The start answered {result.ExitCode}: {result.Output} {result.Error}");
        var error = GoldenPath.Json(result)["error"];
        Assert.Equal("layout_newer", (string?)error?["code"]);
        Assert.Equal(LayoutStamp.NewerSentence("99.0.0", JasonVersion.Current), (string?)error?["message"]);
        Assert.Null(GoldenPath.ReadDescriptor(it.Paths));
        Assert.False(Directory.Exists(it.Paths.StateDirectory), $"The start wrote {it.Paths.StateDirectory} before it refused.");
        Assert.False(Directory.Exists(it.Paths.LogsDirectory), $"The start wrote {it.Paths.LogsDirectory} before it refused.");
    }

    /// <summary>
    /// The start's own check passed — here because its paths were given a layout, as a stamp written between the two reads
    /// would — and the runtime it launched read the stamp and refused: the envelope arrives whole after "It said:".
    /// </summary>
    [Fact]
    public async Task A_runtime_that_meets_the_stamp_itself_is_heard_after_it_said()
    {
        using var it = GoldenPath.Create("start-layout-said");
        await File.WriteAllTextAsync(Path.Combine(it.Root, "layout.json"), Stamp, Ct);
        var output = new StringWriter();
        var env = new CliEnvironment(output, new StringWriter(), new JasonPaths(it.Root, 1), Processes: new ShippedRuntime());

        var exit = await RuntimeStartCommand.RunAsync(env, human: false, Ct, timeout: TimeSpan.FromMinutes(2));

        Assert.Equal(ExitCodes.ApiError, exit);
        using var answer = JsonDocument.Parse(output.ToString());
        var message = answer.RootElement.GetProperty("error").GetProperty("message").GetString();
        var envelope = CliErrors.Serialize("layout_newer", LayoutStamp.NewerSentence("99.0.0", JasonVersion.Current), retryable: false);
        Assert.Contains("It said: " + envelope, message, StringComparison.Ordinal);
        Assert.False(Directory.Exists(it.Paths.StateDirectory), $"The runtime wrote {it.Paths.StateDirectory} before it refused.");
    }

    /// <summary>The shipped executable in place of this process's own, which is the test host.</summary>
    private sealed class ShippedRuntime : IRuntimeProcessControl
    {
        public IProcessHandle Launch(JasonPaths paths, IReadOnlyList<string> executable, string? updateLaunch) =>
            RuntimeProcessControl.Instance.Launch(paths, ["dotnet", GoldenPath.Assembly], updateLaunch);

        public Task<FinishedCommand> RunAsync(JasonPaths paths, IReadOnlyList<string> command, TextWriter error, CancellationToken cancellationToken) =>
            throw new NotSupportedException("Only a launch is asked of it.");

        public bool IsRunning(int pid) => RuntimeProcessControl.Instance.IsRunning(pid);

        public DateTimeOffset? StartTime(int pid) => RuntimeProcessControl.Instance.StartTime(pid);
    }
}
