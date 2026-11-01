using Jason.Cli;
using Jason.Contracts.Discovery;
using Jason.Contracts.Json;
using System.Text.Json;

namespace Jason.App.Tests.EndToEnd;

/// <summary>
/// The start a test makes when its subject is not the start: one answer, "it may still be starting", is followed the way a
/// person would follow it, and every other answer is handed back for the test's own assertion to fail on.
/// </summary>
public sealed class PatientStartTests : IDisposable
{
    private const string StillStarting = """{"error":{"code":"runtime_start_failed","message":"The runtime process 4242 did not publish an endpoint descriptor within 30 s and is still running: it may still be starting.","retryable":true}}""";

    private const string Answered = """{"instance_id":"rt_PATIENT"}""";

    private const string Unreachable = """{"error":{"code":"runtime_unreachable","message":"Nothing answered at http://127.0.0.1:5000.","retryable":true}}""";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly string _root = Path.Combine(Path.GetTempPath(), "jason-patient-start", Guid.NewGuid().ToString("N"));

    private readonly List<string> _said = [];

    public PatientStartTests() => Directory.CreateDirectory(_root);

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private JasonPaths Paths => new(_root);

    [Fact]
    public async Task A_runtime_still_starting_when_the_verb_gave_up_is_waited_for_and_its_status_is_the_answer()
    {
        var statusAsked = 0;
        var result = await PatientStart.RunAsync<(int ExitCode, string Output)>(
            Paths,
            within =>
            {
                // The verb's process has given up; the runtime it launched publishes a moment later.
                _ = Task.Delay(300, Ct).ContinueWith(_ => Publish(), CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default);
                return Task.FromResult((ExitCodes.ApiError, StillStarting));
            },
            () =>
            {
                statusAsked++;
                return Task.FromResult(File.Exists(Paths.DescriptorFile) ? (ExitCodes.Success, Answered) : (ExitCodes.RuntimeUnavailable, "{}"));
            },
            step => step,
            patience: TimeSpan.FromSeconds(30),
            say: _said.Add);

        Assert.Equal((ExitCodes.Success, Answered), result);
        Assert.True(statusAsked >= 1);
        var said = Assert.Single(_said);
        Assert.StartsWith("jason runtime start: the runtime answered after ", said, StringComparison.Ordinal);
        Assert.Contains("the verb itself gave up at its documented 30 s", said, StringComparison.Ordinal);
    }

    /// <summary>
    /// A runtime that has published its descriptor need not answer yet: it is still starting until it does. The helper
    /// keeps asking, and a status that fails on the way is not the answer; the one that succeeds is.
    /// </summary>
    [Fact]
    public async Task A_published_runtime_that_does_not_answer_yet_is_asked_again_until_it_does()
    {
        Publish();
        var statusAsked = 0;
        var result = await PatientStart.RunAsync<(int ExitCode, string Output)>(
            Paths,
            _ => Task.FromResult((ExitCodes.ApiError, StillStarting)),
            () =>
            {
                statusAsked++;
                return Task.FromResult(statusAsked <= 2 ? (ExitCodes.RuntimeUnavailable, Unreachable) : (ExitCodes.Success, Answered));
            },
            step => step,
            patience: TimeSpan.FromSeconds(30),
            say: _said.Add);

        Assert.Equal((ExitCodes.Success, Answered), result);
        Assert.Equal(3, statusAsked);
        Assert.StartsWith("jason runtime start: the runtime answered after ", Assert.Single(_said), StringComparison.Ordinal);
    }

    /// <summary>
    /// And one that published and never answers is handed back as the verb said it once patience runs out — never as the
    /// last status that failed, which would say the runtime is not there when it is.
    /// </summary>
    [Fact]
    public async Task A_published_runtime_that_never_answers_is_handed_back_as_the_verb_said_once_patience_runs_out()
    {
        Publish();
        var statusAsked = 0;
        var result = await PatientStart.RunAsync<(int ExitCode, string Output)>(
            Paths,
            _ => Task.FromResult((ExitCodes.ApiError, StillStarting)),
            () =>
            {
                statusAsked++;
                return Task.FromResult((ExitCodes.RuntimeUnavailable, Unreachable));
            },
            step => step,
            patience: TimeSpan.FromMilliseconds(600),
            say: _said.Add);

        Assert.Equal((ExitCodes.ApiError, StillStarting), result);
        Assert.True(statusAsked >= 1, "the published runtime was never asked");
        Assert.Empty(_said);
    }

    [Theory]
    [InlineData(ExitCodes.ApiError, """{"error":{"code":"runtime_start_failed","message":"The runtime process exited with code 1.","retryable":false}}""")]
    [InlineData(ExitCodes.ApiError, """{"error":{"code":"settings_invalid","message":"nope","retryable":true}}""")]
    [InlineData(ExitCodes.Usage, "")]
    [InlineData(ExitCodes.ApiError, "not json")]
    public async Task Any_other_failure_is_handed_back_at_once_and_nothing_else_is_asked(int exit, string output)
    {
        // A descriptor is there, so a helper that followed this answer would find it and ask; it must not ask at all.
        Publish();
        var clock = System.Diagnostics.Stopwatch.StartNew();
        var result = await PatientStart.RunAsync<(int ExitCode, string Output)>(
            Paths,
            _ => Task.FromResult((exit, output)),
            () => throw new InvalidOperationException("only a start still starting is followed"),
            step => step,
            patience: TimeSpan.FromSeconds(30),
            say: _said.Add);

        Assert.Equal((exit, output), result);
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(10), $"handed back after {clock.Elapsed.TotalSeconds:F1} s of a 30 s patience");
        Assert.Empty(_said);
    }

    [Fact]
    public async Task A_runtime_that_never_publishes_is_handed_back_as_the_verb_said_once_patience_runs_out()
    {
        var clock = System.Diagnostics.Stopwatch.StartNew();
        var result = await PatientStart.RunAsync<(int ExitCode, string Output)>(
            Paths,
            _ => Task.FromResult((ExitCodes.ApiError, StillStarting)),
            () => Task.FromResult((ExitCodes.RuntimeUnavailable, "{}")),
            step => step,
            patience: TimeSpan.FromMilliseconds(400),
            say: _said.Add);

        Assert.Equal((ExitCodes.ApiError, StillStarting), result);
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(10), $"patience of 0.4 s took {clock.Elapsed.TotalSeconds:F1} s");
        Assert.Empty(_said);
    }

    [Fact]
    public async Task A_start_that_answered_is_handed_back_and_the_verb_is_given_the_whole_patience()
    {
        TimeSpan? given = null;
        var result = await PatientStart.RunAsync<(int ExitCode, string Output)>(
            Paths,
            within =>
            {
                given = within;
                return Task.FromResult((ExitCodes.Success, Answered));
            },
            () => throw new InvalidOperationException("an answered start needs no status"),
            step => step,
            patience: TimeSpan.FromSeconds(45),
            say: _said.Add);

        Assert.Equal((ExitCodes.Success, Answered), result);
        Assert.Equal(TimeSpan.FromSeconds(45), given);
        Assert.Empty(_said);
    }

    /// <summary>The patience a start is given by default is the suite's own bound for a slow step.</summary>
    [Fact]
    public void The_default_patience_is_the_suites_bound_for_a_slow_step() =>
        Assert.Equal(GoldenPath.Patience, PatientStart.DefaultPatience);

    private void Publish()
    {
        Directory.CreateDirectory(Paths.RunDirectory);
        File.WriteAllText(
            Paths.DescriptorFile,
            JsonSerializer.Serialize(new RuntimeDescriptor("1", "0.0.0", "rt_PATIENT", 4242, "http://127.0.0.1:5000", "token", DateTimeOffset.UtcNow), JasonJson.Options));
    }
}
