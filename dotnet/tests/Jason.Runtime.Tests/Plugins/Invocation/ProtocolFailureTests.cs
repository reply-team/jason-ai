using System.Diagnostics;
using System.Text.Json.Nodes;
using Jason.Contracts.Discovery;
using Jason.Contracts.Plugins;
using Jason.Runtime.Execution;
using Jason.Runtime.Plugins.Invocation;
using Jason.Runtime.Tests.Dispatch;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Jason.Runtime.Tests.Plugins.Invocation;

/// <summary>
/// Everything that can go wrong around an invocation rather than inside it, each named distinctly. A correct
/// plugin host can never produce most of these, so the child here is a scripted stand-in started through the
/// same seam: the point is that the runtime survives a child that does not keep the protocol, and says which
/// promise was broken.
/// </summary>
public class ProtocolFailureTests
{
    private const string Sentinel = "SENTINEL_PLUGIN_STDOUT";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_child_that_writes_something_other_than_an_outcome_is_not_believed()
    {
        await using var api = await StartAsync(Child("stdout", "not json at all"));

        var result = await InvokeAsync(api);

        var failure = Assert.IsType<InvocationOutcome.ProtocolFailure>(result.Outcome);
        Assert.Equal(ProtocolCodes.PluginMalformedOutcome, failure.Code);
        Assert.Equal(FailureClass.Ambiguous, OutcomeClassification.ClassOf(failure.Code));
    }

    /// <summary>
    /// What a child wrote when it did not write an outcome is the plugin's own text, and a plugin carries whatever
    /// a person put into the work. It travels into the attempt's error, which is a value in the database; no log
    /// line quotes it, whether the parser would or not. The invocation's own lines are in the record, so an
    /// empty search means the text is absent and not that the lines never arrived.
    /// </summary>
    [Fact]
    [Critical(Family.B3, "a bad outcome is not logged")]
    public async Task A_child_that_writes_an_unreadable_outcome_is_not_quoted_in_any_log_line()
    {
        var logs = new RecordingLogs();
        await using var api = await StartAsync(Child("stdout", "{\"note\":\"" + Sentinel + "\",\"status\":}"), logs: logs);

        var result = await InvokeAsync(api);

        var failure = Assert.IsType<InvocationOutcome.ProtocolFailure>(result.Outcome);
        Assert.Equal(ProtocolCodes.PluginMalformedOutcome, failure.Code);
        Assert.Contains(logs.All, line => line.Contains(" ended: ", StringComparison.Ordinal));
        Assert.False(logs.Mentions(Sentinel), "the child's output reached the log");
    }

    /// <summary>
    /// The same holds for a document that is valid JSON and not an outcome: it is read by the typed reader, whose
    /// message names the path it stopped at, and that text goes into the attempt's error and no further. The reader
    /// matches a name whatever its case and its path keeps the name as the child spelled it, so a name spelled oddly
    /// is the child's own text inside the reader's message; the test shows that it is there, so a log line that
    /// carried the message would carry it too. The failure says it was this reader that refused, so the case is the
    /// one it is named for.
    /// </summary>
    [Fact]
    public async Task A_child_that_writes_json_that_is_not_an_outcome_is_not_quoted_in_any_log_line()
    {
        const string spelled = "PrOtOcOl_VeRsIoN";
        var logs = new RecordingLogs();
        await using var api = await StartAsync(Child("stdout", "{\"" + spelled + "\":\"" + Sentinel + "\"}"), logs: logs);

        var result = await InvokeAsync(api);

        var failure = Assert.IsType<InvocationOutcome.ProtocolFailure>(result.Outcome);
        Assert.Equal(ProtocolCodes.PluginMalformedOutcome, failure.Code);
        Assert.Contains("not an outcome document", failure.Message, StringComparison.Ordinal);
        Assert.Contains(spelled, failure.Message, StringComparison.Ordinal);
        Assert.Contains(logs.All, line => line.Contains(" ended: ", StringComparison.Ordinal));
        Assert.False(logs.Mentions(spelled), "the reader's message, which quotes the child's output, reached the log");
        Assert.False(logs.Mentions(Sentinel), "the child's output reached the log");
    }

    [Fact]
    public async Task An_outcome_for_another_invocation_is_not_this_invocation_s_answer()
    {
        var foreign = new JsonObject
        {
            ["protocol_version"] = 1,
            ["invocation_id"] = "pin_other",
            ["status"] = "succeeded",
            ["result"] = new JsonObject { ["stolen"] = true },
            ["external_ids"] = null,
            ["error"] = null,
            ["diagnostics"] = new JsonObject
            {
                ["duration_ms"] = 1,
                ["exec_calls"] = 0,
                ["http_calls"] = 0,
                ["log_lines"] = 0,
            },
        }.ToJsonString();
        await using var api = await StartAsync(Child("stdout", foreign));

        var result = await InvokeAsync(api);

        var failure = Assert.IsType<InvocationOutcome.ProtocolFailure>(result.Outcome);
        Assert.Equal(ProtocolCodes.PluginMalformedOutcome, failure.Code);
        Assert.Contains("pin_other", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    [Critical(Family.F4, "stdout flood")]
    public async Task A_child_that_floods_stdout_is_ended_rather_than_read()
    {
        await using var api = await StartAsync(
            Child("spew", "5000000"),
            """{"Dispatcher":{"Enabled":false},"Plugins":{"Invoker":{"OutcomeBytes":65536}}}""");

        var result = await InvokeAsync(api);

        var failure = Assert.IsType<InvocationOutcome.ProtocolFailure>(result.Outcome);
        Assert.Equal(ProtocolCodes.PluginOutputTooLarge, failure.Code);
        AssertGone(result.Launch!.Pid!.Value);
    }

    /// <summary>
    /// The flood is ended by a kill like any other, so reading what is left of it is bounded like any other:
    /// this child leaves a helper holding its pipes behind before it floods, and the answer still arrives.
    /// </summary>
    [Fact]
    public async Task A_flood_that_left_a_helper_holding_the_pipes_is_answered_all_the_same()
    {
        await using var api = await StartAsync(
            Child("spew-orphan", "5000000", "30000"),
            """{"Dispatcher":{"Enabled":false},"Plugins":{"Invoker":{"OutcomeBytes":65536,"KillGraceMs":500}}}""");

        var call = InvokeAsync(api);
        var answered = await Task.WhenAny(call, Task.Delay(TimeSpan.FromSeconds(20), Ct)) == call;

        Assert.True(answered, "the invocation was still reading a stream the process it killed no longer writes to");
        var failure = Assert.IsType<InvocationOutcome.ProtocolFailure>((await call).Outcome);
        Assert.Equal(ProtocolCodes.PluginOutputTooLarge, failure.Code);
    }

    [Fact]
    public async Task A_child_that_floods_stderr_without_ever_ending_a_line_is_capped_and_still_answered()
    {
        await using var api = await StartAsync(Child("spew", "8388608", "stderr"));

        var result = await InvokeAsync(api);

        // Megabytes with no line ending in them are still only a line: the invocation ends with an answer, and
        // nothing of the flood is kept — not in the file the user is left with, not in the trace the failure
        // carries, and not in the memory it would take to assemble it.
        var failure = Assert.IsType<InvocationOutcome.ProtocolFailure>(result.Outcome);
        Assert.Equal(ProtocolCodes.PluginNoOutcome, failure.Code);
        Assert.Contains(StderrSink.DroppedLineNotice, failure.StderrTail!, StringComparison.Ordinal);
        Assert.DoesNotContain('x', failure.StderrTail!);

        var kept = await File.ReadAllTextAsync(Path.Combine(result.Launch!.WorkDir, "stderr.log"), Ct);
        Assert.Contains(StderrSink.TruncationNotice, kept, StringComparison.Ordinal);
        Assert.DoesNotContain('x', kept);
    }

    [Fact]
    public async Task A_child_that_refuses_the_invocation_is_reported_as_a_refusal()
    {
        await using var api = await StartAsync(Child("exit", "3"));

        var result = await InvokeAsync(api);

        var failure = Assert.IsType<InvocationOutcome.ProtocolFailure>(result.Outcome);
        Assert.Equal(ProtocolCodes.PluginInvocationRejected, failure.Code);
        Assert.Equal(3, failure.ExitCode);
        Assert.Equal(FailureClass.Permanent, OutcomeClassification.ClassOf(failure.Code));
    }

    /// <summary>
    /// The reason a refusal carries is read from the last line of the child's stderr, and that line is the
    /// child's own text: a host that broke in an unforeseen way, or something that is not the host at all,
    /// can write anything there. None of it may throw out of an invocation that still has an answer to give.
    /// </summary>
    [Theory]
    [InlineData("""{"message":5}""")]
    [InlineData("""{"message":"refused","data":5}""")]
    [InlineData("""{"message":"refused","data":{"code":7}}""")]
    public async Task A_diagnostic_shaped_unlike_a_diagnostic_is_read_as_one_no_further_than_it_goes(string line)
    {
        await using var api = await StartAsync(Child("stderr-exit", "3", line));

        var result = await InvokeAsync(api);

        var failure = Assert.IsType<InvocationOutcome.ProtocolFailure>(result.Outcome);
        Assert.Equal(ProtocolCodes.PluginInvocationRejected, failure.Code);
        Assert.Equal(3, failure.ExitCode);
        Assert.Contains("refused the invocation", failure.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// The child masks what it writes, and the runtime masks what it reads: a host that broke before it could still leaves a
    /// configuration value on the stream, and the copy on disk and the tail a failure quotes carry the mask.
    /// </summary>
    [Fact]
    [Critical(Family.S3, "the runtime masks a configuration value on a child's stderr")]
    public async Task A_configuration_value_a_child_left_on_stderr_is_masked_by_the_runtime()
    {
        const string Value = "configured-token-0123456789abcdef";
        await using var api = await StartAsync(
            Child("stderr-exit", "4", "the token is " + Value),
            PluginInvokerTests.Configured(new JsonObject { ["token"] = Value }));

        var result = await InvokeAsync(api);

        var failure = Assert.IsType<InvocationOutcome.ProtocolFailure>(result.Outcome);
        Assert.Equal(ProtocolCodes.PluginNoOutcome, failure.Code);
        Assert.DoesNotContain(Value, failure.StderrTail!, StringComparison.Ordinal);
        Assert.Contains(Redactor.Mask, failure.StderrTail!, StringComparison.Ordinal);
        var kept = await File.ReadAllTextAsync(Path.Combine(result.Launch!.WorkDir, "stderr.log"), Ct);
        Assert.DoesNotContain(Value, kept, StringComparison.Ordinal);
        Assert.Contains(Redactor.Mask, kept, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_child_that_broke_on_its_own_left_no_outcome()
    {
        await using var api = await StartAsync(Child("exit", "4"));

        var result = await InvokeAsync(api);

        var failure = Assert.IsType<InvocationOutcome.ProtocolFailure>(result.Outcome);
        Assert.Equal(ProtocolCodes.PluginNoOutcome, failure.Code);
        Assert.Equal(4, failure.ExitCode);
        Assert.Contains("exiting", failure.StderrTail!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_child_that_exits_cleanly_without_writing_anything_left_no_outcome_either()
    {
        await using var api = await StartAsync(Child("exit", "0"));

        var result = await InvokeAsync(api);

        var failure = Assert.IsType<InvocationOutcome.ProtocolFailure>(result.Outcome);
        Assert.Equal(ProtocolCodes.PluginNoOutcome, failure.Code);
        Assert.Equal(0, failure.ExitCode);
    }

    [Fact]
    public async Task A_child_that_never_exits_is_ended_when_the_budget_and_its_grace_are_spent()
    {
        await using var api = await StartAsync(
            Child("sleep", "30000"),
            """{"Dispatcher":{"Enabled":false},"Plugins":{"Invoker":{"KillGraceMs":500}}}""");
        var watch = Stopwatch.StartNew();

        var result = await InvokeAsync(api, TimeSpan.FromMilliseconds(500));

        var failure = Assert.IsType<InvocationOutcome.ProtocolFailure>(result.Outcome);
        Assert.Equal(ProtocolCodes.PluginTimeout, failure.Code);
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(20), $"the kill waited {watch.Elapsed} for a 500 ms budget");
        AssertGone(result.Launch!.Pid!.Value);
    }

    [Fact]
    [Critical(Family.F4, "stdin never read")]
    public async Task A_child_that_never_reads_its_envelope_is_ended_rather_than_holding_the_write_open()
    {
        await using var api = await StartAsync(
            Child("sleep", "30000"),
            """{"Dispatcher":{"Enabled":false},"Plugins":{"Invoker":{"KillGraceMs":500}}}""");

        // An envelope this size is far larger than any pipe buffer, so the write finishes only if the child
        // reads it — and this one never reads a byte. Nothing may wait on that write that the budget cannot end.
        var input = new JsonObject { ["big"] = new string('x', PluginProtocol.MaxInputBytes - 1024) };
        var watch = Stopwatch.StartNew();

        var result = await InvokeAsync(api, TimeSpan.FromMilliseconds(500), input).WaitAsync(TimeSpan.FromSeconds(60), Ct);

        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(30), $"the envelope write outlived the budget: {watch.Elapsed}");
        var failure = Assert.IsType<InvocationOutcome.ProtocolFailure>(result.Outcome);
        Assert.Equal(ProtocolCodes.PluginTimeout, failure.Code);
        AssertGone(result.Launch!.Pid!.Value);
    }

    /// <summary>
    /// A child that exits without reading its envelope, leaving behind a helper that holds stdin and reads
    /// nothing: the write into that pipe can neither finish nor break, and on this path no deadline or kill has
    /// told it to stop. The invocation stops waiting for it at the grace, as it does for every pipe, and answers
    /// then. Letting go of the child's stdin may not throw out of the invocation — a writer with a write in flight
    /// throws rather than close — and stdin is let go of when its write ends:
    /// <list type="bullet">
    /// <item>the invocation stops the write once the child has ended, so the helper, reading only once the answer
    /// is in, finds the end of what the pipe held rather than the rest of the envelope;</item>
    /// <item>where that cancellation misses — continuous integration once found a write still in flight after
    /// it — the write is left to finish, and the helper reads all of it and then its end.</item>
    /// </list>
    /// </summary>
    /// <remarks>
    /// Staged, not raced. The helper exists before the child exits, because starting it is what hands it the
    /// handles, and it reads only when this test creates the file it waits for. A missed cancellation is held by
    /// the invoker's own hook, because the instant it happens in cannot be chosen from outside.
    /// </remarks>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task An_envelope_left_unread_by_a_child_that_exited_is_let_go_of_rather_than_thrown_over(bool cancellationReachesTheWrite)
    {
        var directory = Path.Combine(Path.GetTempPath(), "jason-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var go = Path.Combine(directory, "go");
        var read = Path.Combine(directory, "read");
        await using var api = await StartAsync(
            Child("leave-reader", go, read),
            """{"Dispatcher":{"Enabled":false},"Plugins":{"Invoker":{"KillGraceMs":500}}}""");

        // Far larger than any pipe buffer, so the write is still in flight when the child has gone.
        var input = new JsonObject { ["big"] = new string('x', PluginProtocol.MaxInputBytes - 1024) };

        PluginInvocationResult result;
        PluginInvoker.EnvelopeWriteMissesTheCancellation.Value = !cancellationReachesTheWrite;
        try
        {
            result = await InvokeAsync(api, TimeSpan.FromSeconds(30), input).WaitAsync(TimeSpan.FromSeconds(20), Ct);
        }
        finally
        {
            PluginInvoker.EnvelopeWriteMissesTheCancellation.Value = false;

            // Whatever happened, the helper is told to read and so to end, rather than outliving the test.
            await File.WriteAllTextAsync(go, string.Empty, Ct);
        }

        var failure = Assert.IsType<InvocationOutcome.ProtocolFailure>(result.Outcome);
        Assert.Equal(ProtocolCodes.PluginNoOutcome, failure.Code);
        Assert.Equal(0, failure.ExitCode);

        var waited = Stopwatch.StartNew();
        while (!File.Exists(read))
        {
            Assert.True(waited.Elapsed < TimeSpan.FromSeconds(30), "the helper never found the end of its stdin");
            await Task.Delay(50, Ct);
        }

        var arrived = long.Parse(await File.ReadAllTextAsync(read, Ct), System.Globalization.CultureInfo.InvariantCulture);
        if (cancellationReachesTheWrite)
        {
            Assert.True(
                arrived < PluginProtocol.MaxInputBytes / 2,
                $"the helper read {arrived} bytes: the envelope was still being written after the invocation had answered");
        }
        else
        {
            Assert.True(
                arrived > PluginProtocol.MaxInputBytes - 1024,
                $"the helper read {arrived} bytes and then the end: the write the cancellation missed was cut short rather than left to finish");
        }
    }
    /// <summary>
    /// A child may leave a helper running that inherited its standard handles, and the read end of a pipe only
    /// ends once every writer has let go of it. Waiting for that would hold the invocation open long after the
    /// tree it started was killed, and in time hold a handler slot with it. Whatever was captured by the end of
    /// the kill grace is the answer.
    /// </summary>
    [Fact]
    [Critical(Family.F4, "a grandchild holding the pipes")]
    public async Task Something_the_killed_child_left_behind_does_not_hold_the_invocation_open()
    {
        await using var api = await StartAsync(
            Child("spawn-orphan", "30000"),
            """{"Dispatcher":{"Enabled":false},"Plugins":{"Invoker":{"KillGraceMs":500}}}""");

        // The budget is generous so that the lingering process certainly exists by the time the tree is killed:
        // the child starts a middle process which starts it and exits at once, and a kill never reaches it.
        var call = InvokeAsync(api, TimeSpan.FromSeconds(5));
        var answered = await Task.WhenAny(call, Task.Delay(TimeSpan.FromSeconds(20), Ct)) == call;

        Assert.True(answered, "the invocation was still waiting for pipes the process it killed no longer holds");
        var result = await call;
        var failure = Assert.IsType<InvocationOutcome.ProtocolFailure>(result.Outcome);
        Assert.Equal(ProtocolCodes.PluginTimeout, failure.Code);
        AssertGone(result.Launch!.Pid!.Value);
    }

    /// <summary>
    /// The same helper, after a child that ended of its own accord. Nothing was killed, and the answer is
    /// already in hand — so the read ends that are still open are held by something the child left, and waiting
    /// for them would hold the invocation, and in time a handler slot, for a process nobody is coming back for.
    /// A vendor CLI that detaches an update check does exactly this after writing a perfectly good outcome.
    /// </summary>
    [Fact]
    public async Task Something_a_child_that_exited_left_behind_does_not_hold_the_invocation_open()
    {
        await using var api = await StartAsync(
            Child("outcome-orphan", "30000"),
            """{"Dispatcher":{"Enabled":false},"Plugins":{"Invoker":{"KillGraceMs":2000}}}""");

        // The budget is far longer than the child needs: it writes its outcome and exits in milliseconds, so
        // nothing here is waiting on a deadline. Only the pipes are left, and only the grace bounds them — and
        // because the helper holds them for the whole of it, this case always spends its grace in full. Two
        // seconds is room for a loaded agent to get the outcome read before the settle gives up on the pipes;
        // it is a fraction of the shipped grace, and well inside the ten seconds the settle is held to below.
        var watch = Stopwatch.StartNew();
        var call = InvokeAsync(api, TimeSpan.FromSeconds(30));
        var answered = await Task.WhenAny(call, Task.Delay(TimeSpan.FromSeconds(20), Ct)) == call;

        Assert.True(answered, "the invocation waited for pipes that only something the child left behind still holds");
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(10), $"the settle outlived the kill grace: {watch.Elapsed}");

        // A failure here says which one it was. The type mismatch alone leaves whoever reads a run that went
        // wrong knowing only that the outcome was not a success, which is the one thing they already knew.
        var outcome = (await call).Outcome;
        if (outcome is InvocationOutcome.ProtocolFailure broken)
        {
            Assert.Fail($"the outcome the child wrote was not read back: {broken.Code} — {broken.Message}");
        }

        var succeeded = Assert.IsType<InvocationOutcome.Succeeded>(outcome);
        Assert.True(succeeded.Result!["ok"]!.GetValue<bool>());
    }

    [Fact]
    public async Task A_host_that_cannot_be_started_at_all_is_a_launch_failure()
    {
        await using var api = await StartAsync(new CommandLocator("jason-plugin-host-that-does-not-exist"));

        var result = await InvokeAsync(api);

        var failure = Assert.IsType<InvocationOutcome.ProtocolFailure>(result.Outcome);
        Assert.Equal(ProtocolCodes.PluginLaunchFailed, failure.Code);
        Assert.Null(result.Launch);

        // The one protocol failure that is not even ambiguous: nothing ran, and the next attempt may find the program.
        Assert.Equal(FailureClass.Transient, OutcomeClassification.ClassOf(failure.Code));
    }

    /// <summary>
    /// The invocation is ASCII on the wire whatever its input holds: every character outside ASCII crosses as an
    /// escape, a second defence beside the encoding the door chooses. The child in the host's place reads the bytes
    /// itself and writes their code points to a file, since what it says on stdout is read as an outcome.
    /// </summary>
    [Fact]
    [Critical(Family.F4, "ASCII on the wire")]
    public async Task The_invocation_is_ascii_on_the_wire_whatever_its_input_holds()
    {
        using var directory = new TempTree();
        var said = Path.Combine(directory.Root, "codepoints.txt");
        await using var api = await StartAsync(Child("stdin-codepoints", said));

        await InvokeAsync(api, input: new JsonObject { ["note"] = "José 😀" });

        var wire = FakeProviderCli.FromCodepoints(await File.ReadAllTextAsync(said, Ct));
        Assert.True(wire.Length > 0 && wire.All(char.IsAscii), $"The invocation crossed as '{wire}'.");
        Assert.Equal("José 😀", (string?)JsonNode.Parse(wire)!["input"]!["note"]);
    }

    /// <summary>The stand-in vendor CLI, which ignores the protocol arguments the invoker appends to its own.</summary>
    private static CommandLocator Child(params string[] behaviour) =>
        new(["dotnet", FakeProviderCli.Dll, .. behaviour]);

    /// <param name="logs">Where the invoker writes its lines, when a test reads them: a running host writes through its file sink and nothing else would see them.</param>
    private static Task<RuntimeApiFixture> StartAsync(IPluginHostLocator locator, string? settings = null, RecordingLogs? logs = null) =>
        RuntimeApiFixture.StartAsync(
            Ct,
            prepare: paths =>
            {
                File.WriteAllText(paths.UserSettingsFile, settings ?? RuntimeApiFixture.DispatcherOff);
                TestPlugins.InstallFakeProvider(paths);
                TestPlugins.Grant(paths, TestPlugins.FakeProviderId, exec: ["*"]);
            },
            configureServices: services =>
            {
                services.AddSingleton(locator);
                services.AddSingleton(TestPlugins.SearchPath);
                if (logs is not null)
                {
                    services.AddSingleton<ILogger<PluginInvoker>>(new Logger<PluginInvoker>(LoggerFactory.Create(builder => builder.AddProvider(logs))));
                }
            });

    private static async Task<PluginInvocationResult> InvokeAsync(
        RuntimeApiFixture api,
        TimeSpan? timeout = null,
        JsonObject? input = null)
    {
        using var scope = api.Runtime.Services.CreateScope();
        var invoker = scope.ServiceProvider.GetRequiredService<PluginInvoker>();
        return await invoker.InvokeAsync(
            new PluginInvocationRequest(TestPlugins.FakeProviderId, "echo.run", input ?? [], null, "att_01K0PROTOCOL", Timeout: timeout),
            Ct);
    }

    private static void AssertGone(int pid)
    {
        try
        {
            using var child = Process.GetProcessById(pid);
            Assert.True(child.HasExited, "the child outlived the kill");
        }
        catch (ArgumentException)
        {
            // Gone entirely, which is the same answer.
        }
    }
}
