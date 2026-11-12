using Jason.Cli;
using Jason.Contracts.Discovery;
using Jason.Contracts.Json;
using System.Diagnostics;
using System.Text.Json;

namespace Jason.App.Tests.EndToEnd;

/// <summary>
/// The start a test makes when its subject is not the start: one answer, "it may still be starting", is followed the way a
/// person would follow it, and every other answer is handed back for the test's own assertion to fail on.
/// </summary>
public sealed class PatientStartTests : IDisposable
{
    // The number is no process: rows that give up build the answer from a stand-in with StillStartingAs.
    private const string StillStarting = """{"error":{"code":"runtime_start_failed","message":"The runtime process 4242 did not publish an endpoint descriptor within 30 s and is still running: it may still be starting.","retryable":true}}""";

    private const string Answered = """{"instance_id":"rt_PATIENT"}""";

    private const string Unreachable = """{"error":{"code":"runtime_unreachable","message":"Nothing answered at http://127.0.0.1:5000.","retryable":true}}""";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly string _root = Path.Combine(Path.GetTempPath(), "jason-patient-start", Guid.NewGuid().ToString("N"));

    private readonly List<string> _said = [];

    public PatientStartTests() => Directory.CreateDirectory(_root);

    private readonly List<Process> _standIns = [];

    public void Dispose()
    {
        foreach (var standIn in _standIns)
        {
            if (!standIn.HasExited)
            {
                standIn.Kill(entireProcessTree: true);
                standIn.WaitForExit();
            }

            standIn.Dispose();
        }

        Directory.Delete(_root, recursive: true);
    }

    private static string StillStartingAs(int pid) => StillStarting.Replace("process 4242", $"process {pid}", StringComparison.Ordinal);

    /// <summary>A program that starts and never publishes anything: a sleeping shell, which is all the runtime is to this helper.</summary>
    private Process StartStandIn()
    {
        var start = new ProcessStartInfo("pwsh") { UseShellExecute = false, CreateNoWindow = true };
        foreach (var argument in new[] { "-NoProfile", "-NonInteractive", "-Command", "Start-Sleep -Seconds 600" })
        {
            start.ArgumentList.Add(argument);
        }

        var standIn = Process.Start(start) ?? throw new InvalidOperationException("The stand-in could not be started.");
        _standIns.Add(standIn);
        return standIn;
    }

    private void WriteLog(params string[] lines)
    {
        Directory.CreateDirectory(Paths.LogsDirectory);
        File.WriteAllLines(Path.Combine(Paths.LogsDirectory, "jason-20260101.log"), lines);
    }

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
        var verbSaid = StillStartingAs(StartStandIn().Id);
        var statusAsked = 0;
        var result = await PatientStart.RunAsync<(int ExitCode, string Output)>(
            Paths,
            _ => Task.FromResult((ExitCodes.ApiError, verbSaid)),
            () =>
            {
                statusAsked++;
                return Task.FromResult((ExitCodes.RuntimeUnavailable, Unreachable));
            },
            step => step,
            patience: TimeSpan.FromMilliseconds(600),
            say: _said.Add);

        Assert.Equal((ExitCodes.ApiError, verbSaid), result);
        Assert.True(statusAsked >= 1, "the published runtime was never asked");
        Assert.Contains(_said, line => line.Contains("ended the runtime process", StringComparison.Ordinal));
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
        var verbSaid = StillStartingAs(StartStandIn().Id);
        var result = await PatientStart.RunAsync<(int ExitCode, string Output)>(
            Paths,
            _ => Task.FromResult((ExitCodes.ApiError, verbSaid)),
            () => Task.FromResult((ExitCodes.RuntimeUnavailable, "{}")),
            step => step,
            patience: TimeSpan.FromMilliseconds(400),
            say: _said.Add);

        Assert.Equal((ExitCodes.ApiError, verbSaid), result);
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(10), $"patience of 0.4 s took {clock.Elapsed.TotalSeconds:F1} s");
    }

    /// <summary>
    /// A start that never publishes says what the runtime log held when it gave up, each line by its time and its
    /// template and never by the values that filled it, so a slow step can be told from a stuck one afterwards.
    /// </summary>
    [Fact]
    public async Task A_start_that_never_publishes_says_the_log_times_and_templates_and_nothing_a_person_wrote()
    {
        WriteLog(
            """{"@t":"2026-01-01T10:00:01.0000000Z","@mt":"Database ready","@l":"Information"}""",
            """{"@t":"2026-01-01T10:00:04.5000000Z","@mt":"Imported {Count} things for {Campaign}","Count":3,"Campaign":"Ada secret campaign","@l":"Information"}""",
            """{"@t":"2026-01-01T10:00:05.0000000Z","@mt":"A step failed","@x":"System.Exception: private words","@l":"Error"}""",
            "not json at all");

        await PatientStart.RunAsync<(int ExitCode, string Output)>(
            Paths,
            _ => Task.FromResult((ExitCodes.ApiError, StillStartingAs(StartStandIn().Id))),
            () => Task.FromResult((ExitCodes.RuntimeUnavailable, "{}")),
            step => step,
            patience: TimeSpan.FromMilliseconds(300),
            say: _said.Add);

        Assert.Contains("2026-01-01T10:00:01.0000000Z Database ready", _said);
        Assert.Contains("2026-01-01T10:00:04.5000000Z Imported {Count} things for {Campaign}", _said);
        Assert.Contains("2026-01-01T10:00:05.0000000Z A step failed", _said);
        var everything = string.Join('\n', _said);
        Assert.DoesNotContain("secret campaign", everything, StringComparison.Ordinal);
        Assert.DoesNotContain("private words", everything, StringComparison.Ordinal);
        Assert.DoesNotContain("not json", everything, StringComparison.Ordinal);
    }

    /// <summary>
    /// And leaves no process behind: the one the verb named is ended by its number and is gone when the helper returns,
    /// which is when the caller removes the folder it was using.
    /// </summary>
    [Fact]
    public async Task A_start_that_never_publishes_ends_the_process_the_verb_named_and_it_is_gone_on_return()
    {
        var runtime = StartStandIn();
        var bystander = StartStandIn();

        await PatientStart.RunAsync<(int ExitCode, string Output)>(
            Paths,
            _ => Task.FromResult((ExitCodes.ApiError, StillStartingAs(runtime.Id))),
            () => Task.FromResult((ExitCodes.RuntimeUnavailable, "{}")),
            step => step,
            patience: TimeSpan.FromMilliseconds(300),
            say: _said.Add);

        Assert.True(runtime.HasExited, "the runtime the verb launched outlived the start that gave up");
        Assert.False(bystander.HasExited, "a process the verb did not name was ended");
        Assert.Contains($"ended the runtime process {runtime.Id}", string.Join('\n', _said), StringComparison.Ordinal);
    }

    /// <summary>An answer that names no process ends none: the helper never looks for a runtime by its name.</summary>
    [Fact]
    public async Task A_start_that_gave_up_without_naming_a_process_ends_nothing()
    {
        var bystander = StartStandIn();

        await PatientStart.RunAsync<(int ExitCode, string Output)>(
            Paths,
            _ => Task.FromResult((ExitCodes.ApiError, """{"error":{"code":"runtime_start_failed","message":"It may still be starting.","retryable":true}}""")),
            () => Task.FromResult((ExitCodes.RuntimeUnavailable, "{}")),
            step => step,
            patience: TimeSpan.FromMilliseconds(300),
            say: _said.Add);

        Assert.False(bystander.HasExited);
        Assert.Contains(_said, line => line.Contains("named no process", StringComparison.Ordinal));
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
