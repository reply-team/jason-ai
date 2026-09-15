using System.Diagnostics;
using System.Text.Json.Nodes;
using Jason.Contracts.Discovery;
using Jason.Contracts.Plugins;
using Jason.Runtime.Execution;
using Jason.Runtime.Plugins.Invocation;
using Microsoft.Extensions.DependencyInjection;

namespace Jason.Runtime.Tests.Plugins.Invocation;

/// <summary>
/// Everything that can go wrong around an invocation rather than inside it, each named distinctly. A correct
/// plugin host can never produce most of these, so the child here is a scripted stand-in started through the
/// same seam: the point is that the runtime survives a child that does not keep the protocol, and says which
/// promise was broken.
/// </summary>
public class ProtocolFailureTests
{
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
    /// A child may leave a helper running that inherited its standard handles, and the read end of a pipe only
    /// ends once every writer has let go of it. Waiting for that would hold the invocation open long after the
    /// tree it started was killed, and in time hold a handler slot with it. Whatever was captured by the end of
    /// the kill grace is the answer.
    /// </summary>
    [Fact]
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

    [Fact]
    public async Task A_host_that_cannot_be_started_at_all_is_a_launch_failure()
    {
        await using var api = await StartAsync(new CommandLocator("jason-plugin-host-that-does-not-exist"));

        var result = await InvokeAsync(api);

        var failure = Assert.IsType<InvocationOutcome.ProtocolFailure>(result.Outcome);
        Assert.Equal(ProtocolCodes.PluginLaunchFailed, failure.Code);
        Assert.Null(result.Launch);

        // The one protocol failure worth trying again: nothing ran, and the next attempt may find the program.
        Assert.Equal(FailureClass.Transient, OutcomeClassification.ClassOf(failure.Code));
        Assert.True(OutcomeClassification.IsRetriable(OutcomeClassification.ClassOf(failure.Code)));
    }

    /// <summary>The stand-in vendor CLI, which ignores the protocol arguments the invoker appends to its own.</summary>
    private static CommandLocator Child(params string[] behaviour) =>
        new(["dotnet", FakeProviderCli.Dll, .. behaviour]);

    private static Task<RuntimeApiFixture> StartAsync(IPluginHostLocator locator, string? settings = null) =>
        RuntimeApiFixture.StartAsync(
            Ct,
            prepare: paths =>
            {
                File.WriteAllText(paths.UserSettingsFile, settings ?? RuntimeApiFixture.DispatcherOff);
                TestPlugins.InstallFakeProvider(paths);
                TestPlugins.Grant(paths, TestPlugins.FakeProviderId, exec: ["*"]);
            },
            configureServices: services => services.AddSingleton(locator));

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
