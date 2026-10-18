using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Jason.Contracts.Json;
using Jason.Contracts.Plugins;
using Jason.PluginHost.Tests.Fixtures;

namespace Jason.PluginHost.Tests;

[Collection(ModeCollection.Name)]
public sealed class PluginHostModeTests : IDisposable
{
    private readonly IInvocationRunner _original = PluginHostMode.Runner;
    private readonly string _root = Path.Combine(Path.GetTempPath(), "jason-plugin-host-tests", Guid.NewGuid().ToString("N"));

    public PluginHostModeTests() =>
        InvocationFactory.WritePackage(_root, "export function invoke() { return { result: {} }; }");

    public void Dispose()
    {
        PluginHostMode.Runner = _original;
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static string[] Argv(string pluginId = "fake-provider", string operation = "echo.run", int protocol = 1) =>
        ["--protocol", protocol.ToString(System.Globalization.CultureInfo.InvariantCulture), "--plugin", pluginId, "--operation", operation, "--correlation", "test"];

    private static async Task<(int Exit, string Out, string Error)> RunAsync(string[] args, PluginInvocation? invocation = null)
    {
        using var stdin = new StringReader(invocation is null ? string.Empty : JsonSerializer.Serialize(invocation, JasonJson.Options));
        using var stdout = new StringWriter();
        using var stderr = new StringWriter();
        var exit = await PluginHostMode.RunAsync(args, stdin, stdout, stderr, Ct);
        return (exit, stdout.ToString(), stderr.ToString());
    }

    /// <param name="run">The outcome, from the envelope the runner was handed.</param>
    /// <param name="leave">What the run leaves behind for the host to wait for: a kill an exec stopped waiting for.</param>
    private sealed class StubRunner(Func<PluginInvocation, PluginOutcome> run, Action<UnfinishedKills>? leave = null) : IInvocationRunner
    {
        public int Calls { get; private set; }

        public PluginOutcome Run(PluginInvocation invocation, HostDiagnostics diagnostics, UnfinishedKills kills, CancellationToken deadline)
        {
            Calls++;
            leave?.Invoke(kills);
            return run(invocation);
        }
    }

    private static StubRunner Succeeding(Action<UnfinishedKills>? leave = null) => new(Succeeded, leave);

    private static PluginOutcome Succeeded(PluginInvocation invocation) => new(
        PluginProtocol.CurrentVersion,
        invocation.InvocationId,
        OutcomeStatus.Succeeded,
        JsonNode.Parse("""{"ok":true}"""),
        null,
        null,
        new OutcomeDiagnostics(1, 0, 0, 0));

    /// <summary>A process id the stand-in kill names; nothing is ever done to it.</summary>
    private const int KilledPid = 4242;

    /// <summary>
    /// The mode run as the process runs it, with its streams watched while it is still running: an exec's kill that
    /// outlived its call is left in the ledger by the runner, and ends when the test says.
    /// </summary>
    private static (Task<int> Run, WatchedWriter Stdout, WatchedWriter Stderr) StartWithAnUnfinishedKill(PluginInvocation invocation, Task kill) =>
        Start(invocation, Succeeding(kills => kills.Add(KilledPid, kill)));

    private static (Task<int> Run, WatchedWriter Stdout, WatchedWriter Stderr) Start(
        PluginInvocation invocation,
        IInvocationRunner runner,
        TimeProvider? clock = null,
        CancellationToken? stop = null)
    {
        PluginHostMode.Runner = runner;
        var stdout = new WatchedWriter();
        var stderr = new WatchedWriter();
        var stdin = new StringReader(JsonSerializer.Serialize(invocation, JasonJson.Options));
        return (PluginHostMode.RunAsync(Argv(), stdin, stdout, stderr, clock ?? TimeProvider.System, stop ?? Ct), stdout, stderr);
    }

    /// <summary>Waits for something the test cannot be told about, for no longer than the time given.</summary>
    private static async Task<bool> WithinAsync(Func<bool> condition, TimeSpan within)
    {
        var waited = Stopwatch.StartNew();
        while (!condition())
        {
            if (waited.Elapsed > within)
            {
                return false;
            }

            await Task.Delay(20, Ct);
        }

        return true;
    }

    private static IReadOnlyList<JsonObject> Unfinished(WatchedWriter stderr) =>
    [
        .. ModeRun.Records(ModeRun.Lines(stderr.Text))
            .Where(line => line["message"]?.GetValue<string>() == UnfinishedKills.Message),
    ];

    /// <summary>
    /// A kill an exec stopped waiting for is still ending its program when the outcome is written, and the host does not
    /// exit before it has: an exit would cut the kill short and leave the program running where the runtime's own kill no
    /// longer reaches it. The outcome is on stdout from the moment it was written all the same.
    /// </summary>
    [Fact]
    public async Task The_host_writes_its_outcome_and_does_not_exit_before_an_unfinished_kill_has_ended()
    {
        var kill = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var invocation = InvocationFactory.Create(_root, "echo.run", configure: builder => builder.Limits = builder.Limits with { TimeoutMs = 60_000 });

        var (run, stdout, stderr) = StartWithAnUnfinishedKill(invocation, kill.Task);
        try
        {
            await stdout.SeenAsync("\"status\":\"succeeded\"", run, Ct);

            // A moment for a host that would exit now to have done it.
            await Task.WhenAny(run, Task.Delay(500, Ct));
            Assert.False(run.IsCompleted, $"The host returned ({run.Status}) with a kill still unfinished. It said: {stderr.Text}");
        }
        finally
        {
            kill.TrySetResult();
        }

        Assert.Equal(HostExitCodes.Completed, await run.WaitAsync(TimeSpan.FromSeconds(30), Ct));
        Assert.Equal(OutcomeStatus.Succeeded, ModeRun.Outcome(stdout.Text).Status);
        Assert.True(Unfinished(stderr).Count == 0, $"A kill that ended before the deadline was said unfinished: {stderr.Text}");
    }

    /// <summary>
    /// At the invocation's deadline a kill still running is said, once, with its process id and the deadline, and the host
    /// goes on waiting rather than exit: what ends it then is the runtime's kill of its whole tree, with the program still
    /// in it, and the attempt is a timeout. The line comes no earlier than the deadline it names, read on the clock the host
    /// keeps it on; that the deadline is the host's start plus the invocation's timeout, exactly, is held on a clock the
    /// test moves itself.
    /// </summary>
    [Fact]
    public async Task At_the_deadline_an_unfinished_kill_is_said_once_and_the_host_goes_on_waiting()
    {
        const int TimeoutMs = 500;
        var kill = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var invocation = InvocationFactory.Create(_root, "echo.run", configure: builder => builder.Limits = builder.Limits with { TimeoutMs = TimeoutMs });

        var (run, stdout, stderr) = StartWithAnUnfinishedKill(invocation, kill.Task);
        try
        {
            await stderr.SeenAsync(UnfinishedKills.Message, run, Ct);
            var seen = DateTimeOffset.UtcNow;

            // A moment for a host that would exit at its deadline to have done it.
            await Task.WhenAny(run, Task.Delay(500, Ct));
            Assert.False(run.IsCompleted, $"The host returned ({run.Status}) at its deadline with a kill still unfinished. It said: {stderr.Text}");

            var line = Assert.Single(Unfinished(stderr));
            Assert.Equal("warn", line["level"]!.GetValue<string>());
            Assert.Equal("host", line["source"]!.GetValue<string>());
            var data = line["data"]!.AsObject();
            Assert.Equal(["deadline", "pid"], data.Select(pair => pair.Key).Order(StringComparer.Ordinal));
            Assert.Equal(KilledPid, data["pid"]!.GetValue<int>());
            var deadline = DateTimeOffset.Parse(data["deadline"]!.GetValue<string>(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);
            Assert.True(seen >= deadline, $"The kill was said unfinished by {seen:O}, before the deadline the line names, {deadline:O}.");
        }
        finally
        {
            kill.TrySetResult();
        }

        Assert.Equal(HostExitCodes.Completed, await run.WaitAsync(TimeSpan.FromSeconds(30), Ct));
        Assert.Equal(OutcomeStatus.Succeeded, ModeRun.Outcome(stdout.Text).Status);
        Assert.True(Unfinished(stderr).Count == 1, $"The unfinished kill was said more than once: {stderr.Text}");
    }

    /// <summary>
    /// The host breaking does not let it exit under a kill either. A program's kill can still be running when something
    /// goes wrong after its call answered; the host says what broke, and exits 4 only once the kill has ended.
    /// </summary>
    [Fact]
    public async Task A_host_that_breaks_with_a_kill_unfinished_exits_only_once_the_kill_has_ended()
    {
        var kill = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var invocation = InvocationFactory.Create(_root, "echo.run", configure: builder => builder.Limits = builder.Limits with { TimeoutMs = 60_000 });

        var (run, stdout, stderr) = Start(
            invocation,
            new StubRunner(_ => throw new InvalidOperationException("the host broke"), kills => kills.Add(KilledPid, kill.Task)));
        try
        {
            await stderr.SeenAsync("the host broke", run, Ct);

            // A moment for a host that would exit now to have done it.
            await Task.WhenAny(run, Task.Delay(500, Ct));
            Assert.False(run.IsCompleted, $"The host returned ({run.Status}) with a kill still unfinished. It said: {stderr.Text}");
        }
        finally
        {
            kill.TrySetResult();
        }

        Assert.Equal(HostExitCodes.HostFailure, await run.WaitAsync(TimeSpan.FromSeconds(30), Ct));
        Assert.Equal(string.Empty, stdout.Text);
    }

    /// <summary>
    /// The deadline is the invocation's own, counted from the host's start and not from the moment its outcome was written:
    /// on a clock that moves only when the test moves it, a runner that takes three times the invocation's timeout writes
    /// its outcome long after that deadline, and the line names the deadline the plugin was given. A host that counted from
    /// its outcome would wait for a deadline still ahead; the clock is moved to it, so that the line says which it counted.
    /// </summary>
    [Fact]
    public async Task The_deadline_is_counted_from_the_host_s_start_and_not_from_its_outcome()
    {
        const int TimeoutMs = 500;
        const int RunMs = 1500;
        var start = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
        var clock = new ManualClock(start);
        var kill = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var invocation = InvocationFactory.Create(_root, "echo.run", configure: builder => builder.Limits = builder.Limits with { TimeoutMs = TimeoutMs });
        var slow = new StubRunner(
            given =>
            {
                clock.Advance(TimeSpan.FromMilliseconds(RunMs));
                return Succeeded(given);
            },
            kills => kills.Add(KilledPid, kill.Task));

        var (run, stdout, stderr) = Start(invocation, slow, clock);
        try
        {
            await stdout.SeenAsync("\"status\":\"succeeded\"", run, Ct);
            Assert.True(
                await WithinAsync(() => Unfinished(stderr).Count > 0 || clock.Waiting > 0, TimeSpan.FromSeconds(30)),
                $"The host neither said the kill unfinished nor began to wait for a deadline. It said: {stderr.Text}");
            if (clock.NextDue is { } due)
            {
                clock.Advance(due - clock.GetUtcNow());
            }

            await WithinAsync(() => Unfinished(stderr).Count > 0, TimeSpan.FromSeconds(5));
            var line = Assert.Single(Unfinished(stderr));
            var deadline = DateTimeOffset.Parse(line["data"]!["deadline"]!.GetValue<string>(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);
            Assert.True(
                deadline == start.AddMilliseconds(TimeoutMs),
                $"The line names {deadline:O} as the deadline. Counted from the host's start, {start:O}, it is {start.AddMilliseconds(TimeoutMs):O}; counted from the outcome, written at {start.AddMilliseconds(RunMs):O}, it would be {start.AddMilliseconds(RunMs + TimeoutMs):O}.");
        }
        finally
        {
            kill.TrySetResult();
        }

        Assert.Equal(HostExitCodes.Completed, await run.WaitAsync(TimeSpan.FromSeconds(30), Ct));
    }

    /// <summary>
    /// And it is exactly that: the host's start plus the invocation's timeout, the deadline the plugin is told, which sits
    /// below the runtime's kill by the runtime's grace less the host's start. On a clock that moves only when the test moves
    /// it, the line is not said a millisecond before that moment, is said at it, and names it.
    /// </summary>
    [Fact]
    public async Task The_deadline_is_the_host_s_start_plus_the_invocation_s_timeout_exactly()
    {
        const int TimeoutMs = 500;
        var start = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
        var clock = new ManualClock(start);
        var kill = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var invocation = InvocationFactory.Create(_root, "echo.run", configure: builder => builder.Limits = builder.Limits with { TimeoutMs = TimeoutMs });

        var (run, stdout, stderr) = Start(invocation, Succeeding(kills => kills.Add(KilledPid, kill.Task)), clock);
        try
        {
            await stdout.SeenAsync("\"status\":\"succeeded\"", run, Ct);
            Assert.True(await WithinAsync(() => clock.Waiting > 0, TimeSpan.FromSeconds(30)), $"The host never began to wait for its deadline. It said: {stderr.Text}");

            clock.Advance(TimeSpan.FromMilliseconds(TimeoutMs - 1));

            // A moment for a line said early to have been written.
            await Task.WhenAny(run, Task.Delay(200, Ct));
            Assert.True(Unfinished(stderr).Count == 0, $"The kill was said unfinished a millisecond before the deadline, at {clock.GetUtcNow():O}: {stderr.Text}");

            clock.Advance(TimeSpan.FromMilliseconds(1));
            await WithinAsync(() => Unfinished(stderr).Count > 0, TimeSpan.FromSeconds(5));
            var said = Unfinished(stderr);
            Assert.True(
                said.Count == 1,
                $"Expected the kill said unfinished once at {start.AddMilliseconds(TimeoutMs):O}, the host's start plus {TimeoutMs} ms; at {clock.GetUtcNow():O} the host said: {stderr.Text}");
            var deadline = DateTimeOffset.Parse(said[0]["data"]!["deadline"]!.GetValue<string>(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);
            Assert.True(deadline == start.AddMilliseconds(TimeoutMs), $"The line names {deadline:O}; the host started at {start:O} with a {TimeoutMs} ms timeout.");
            Assert.False(run.IsCompleted, $"The host returned ({run.Status}) at its deadline with a kill still unfinished. It said: {stderr.Text}");
        }
        finally
        {
            kill.TrySetResult();
        }

        Assert.Equal(HostExitCodes.Completed, await run.WaitAsync(TimeSpan.FromSeconds(30), Ct));
    }

    /// <summary>
    /// The host waits for the deadline on its own clock and not on its timer, since a timer's tick can come before the moment
    /// it was set for: a timer that fires a millisecond short of the deadline brings no line, the host waits out what is
    /// left, and the line comes when the clock reaches the deadline.
    /// </summary>
    [Fact]
    public async Task A_timer_that_fires_before_the_deadline_brings_no_line_and_the_deadline_does()
    {
        const int TimeoutMs = 500;
        var start = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
        var deadline = start.AddMilliseconds(TimeoutMs);
        var clock = new ManualClock(start);
        var kill = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var invocation = InvocationFactory.Create(_root, "echo.run", configure: builder => builder.Limits = builder.Limits with { TimeoutMs = TimeoutMs });

        var (run, stdout, stderr) = Start(invocation, Succeeding(kills => kills.Add(KilledPid, kill.Task)), clock);
        try
        {
            await stdout.SeenAsync("\"status\":\"succeeded\"", run, Ct);
            Assert.True(await WithinAsync(() => clock.Waiting > 0, TimeSpan.FromSeconds(30)), $"The host never began to wait for its deadline. It said: {stderr.Text}");

            clock.Advance(deadline.AddMilliseconds(-1) - clock.GetUtcNow());
            clock.FireEarly();
            Assert.True(
                await WithinAsync(() => clock.Waiting > 0 || Unfinished(stderr).Count > 0, TimeSpan.FromSeconds(5)),
                $"After its timer fired at {clock.GetUtcNow():O} the host neither waited again nor said the kill unfinished. It said: {stderr.Text}");
            Assert.True(
                Unfinished(stderr).Count == 0,
                $"A timer that fired at {clock.GetUtcNow():O} brought the line before the deadline, {deadline:O}: {stderr.Text}");

            clock.Advance(deadline - clock.GetUtcNow());
            await WithinAsync(() => Unfinished(stderr).Count > 0, TimeSpan.FromSeconds(5));
            var said = Unfinished(stderr);
            Assert.True(said.Count == 1, $"Expected the kill said unfinished once at {deadline:O}; at {clock.GetUtcNow():O} the host said: {stderr.Text}");
            var named = DateTimeOffset.Parse(said[0]["data"]!["deadline"]!.GetValue<string>(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);
            Assert.True(named == deadline, $"The line names {named:O}; the host started at {start:O} with a {TimeoutMs} ms timeout.");
            Assert.False(run.IsCompleted, $"The host returned ({run.Status}) at its deadline with a kill still unfinished. It said: {stderr.Text}");
        }
        finally
        {
            kill.TrySetResult();
        }

        Assert.Equal(HostExitCodes.Completed, await run.WaitAsync(TimeSpan.FromSeconds(30), Ct));
    }

    /// <summary>
    /// A wait counts whole milliseconds, and what is left of the deadline seldom is: a host that begins to wait a fraction of
    /// a millisecond into its run waits that remainder rounded up, so its timer never falls due before the deadline. Rounded
    /// down, the timer would fall due short of it, with less than a millisecond left to wait, a wait that rounds down to
    /// nothing. A quarter of a millisecond tells rounding up from rounding down; three quarters tells it from rounding to
    /// the nearest millisecond, which a quarter rounds up as well.
    /// </summary>
    [Theory]
    [InlineData(250)]
    [InlineData(750)]
    public async Task A_wait_begun_a_fraction_of_a_millisecond_into_the_run_falls_due_no_earlier_than_the_deadline(int ranMicroseconds)
    {
        const int TimeoutMs = 500;
        var start = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
        var deadline = start.AddMilliseconds(TimeoutMs);
        var clock = new ManualClock(start);
        var kill = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var invocation = InvocationFactory.Create(_root, "echo.run", configure: builder => builder.Limits = builder.Limits with { TimeoutMs = TimeoutMs });

        // The run takes a fraction of a millisecond of the host's clock before the host begins to wait.
        var (run, stdout, stderr) = Start(
            invocation,
            Succeeding(kills =>
            {
                kills.Add(KilledPid, kill.Task);
                clock.Advance(TimeSpan.FromMicroseconds(ranMicroseconds));
            }),
            clock);
        try
        {
            await stdout.SeenAsync("\"status\":\"succeeded\"", run, Ct);
            Assert.True(await WithinAsync(() => clock.Waiting > 0, TimeSpan.FromSeconds(30)), $"The host never began to wait for its deadline. It said: {stderr.Text}");

            var due = clock.NextDue!.Value;
            Assert.True(due >= deadline, $"The host, waiting from {clock.GetUtcNow():O}, set its timer for {due:O}, before its deadline, {deadline:O}.");

            clock.Advance(due - clock.GetUtcNow());
            await WithinAsync(() => Unfinished(stderr).Count > 0, TimeSpan.FromSeconds(5));
            var said = Unfinished(stderr);
            Assert.True(said.Count == 1, $"Expected the kill said unfinished once at {due:O}; at {clock.GetUtcNow():O} the host said: {stderr.Text}");
            var named = DateTimeOffset.Parse(said[0]["data"]!["deadline"]!.GetValue<string>(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);
            Assert.True(named == deadline, $"The line names {named:O}; the host started at {start:O} with a {TimeoutMs} ms timeout.");
        }
        finally
        {
            kill.TrySetResult();
        }

        Assert.Equal(HostExitCodes.Completed, await run.WaitAsync(TimeSpan.FromSeconds(30), Ct));
    }

    /// <summary>
    /// A host told to stop by its own token while it waits for its deadline stops waiting and returns, its kill still
    /// running: a wait asked of a stopped token ends before it begins, and the host does not go round again for what is
    /// left of the deadline.
    /// </summary>
    [Fact]
    public async Task A_host_told_to_stop_while_it_waits_for_its_deadline_returns_without_going_round_again()
    {
        const int TimeoutMs = 500;
        var clock = new ManualClock(new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero));
        var kill = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var invocation = InvocationFactory.Create(_root, "echo.run", configure: builder => builder.Limits = builder.Limits with { TimeoutMs = TimeoutMs });
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(Ct);

        var (run, stdout, stderr) = Start(invocation, Succeeding(kills => kills.Add(KilledPid, kill.Task)), clock, stop.Token);
        var stopping = Task.CompletedTask;
        try
        {
            await stdout.SeenAsync("\"status\":\"succeeded\"", run, Ct);
            Assert.True(await WithinAsync(() => clock.Waiting > 0, TimeSpan.FromSeconds(30)), $"The host never began to wait for its deadline. It said: {stderr.Text}");

            // Told off this thread, so that a host going round again holds a thread of its own and not the test's.
            stopping = stop.CancelAsync();
            Assert.True(
                await WithinAsync(() => run.IsCompleted, TimeSpan.FromSeconds(5)),
                $"The host told to stop at {clock.GetUtcNow():O}, before its deadline, had not returned ({run.Status}). It said: {stderr.Text}");
        }
        finally
        {
            kill.TrySetResult();
        }

        await stopping;
        Assert.Equal(HostExitCodes.Completed, await run.WaitAsync(TimeSpan.FromSeconds(30), Ct));
    }

    /// <summary>
    /// An outcome can be written after the deadline has passed — a program's call keeps its grace even when its own timeout
    /// reached the end of the invocation — and a kill still running then is said at once, and waited for all the same.
    /// </summary>
    [Fact]
    public async Task An_outcome_written_past_the_deadline_says_an_unfinished_kill_at_once_and_waits_for_it()
    {
        const int TimeoutMs = 300;
        var kill = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var invocation = InvocationFactory.Create(_root, "echo.run", configure: builder => builder.Limits = builder.Limits with { TimeoutMs = TimeoutMs });
        var late = new StubRunner(
            given =>
            {
                Thread.Sleep(TimeoutMs + 500);
                return Succeeded(given);
            },
            kills => kills.Add(KilledPid, kill.Task));

        var (run, stdout, stderr) = Start(invocation, late);
        try
        {
            await stdout.SeenAsync("\"status\":\"succeeded\"", run, Ct);
            await stderr.SeenAsync(UnfinishedKills.Message, run, Ct);

            // A moment for a host that would exit now to have done it.
            await Task.WhenAny(run, Task.Delay(500, Ct));
            Assert.False(run.IsCompleted, $"The host returned ({run.Status}) with a kill still unfinished. It said: {stderr.Text}");
        }
        finally
        {
            kill.TrySetResult();
        }

        Assert.Equal(HostExitCodes.Completed, await run.WaitAsync(TimeSpan.FromSeconds(30), Ct));
        Assert.True(Unfinished(stderr).Count == 1, $"Expected the unfinished kill said once: {stderr.Text}");
    }

    /// <summary>Every kill is waited for: the first of two ending does not let the host exit under the second.</summary>
    [Fact]
    public async Task The_host_waits_for_every_unfinished_kill_and_not_only_the_first_to_end()
    {
        var first = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var second = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var invocation = InvocationFactory.Create(_root, "echo.run", configure: builder => builder.Limits = builder.Limits with { TimeoutMs = 60_000 });

        var (run, stdout, stderr) = Start(invocation, Succeeding(kills =>
        {
            kills.Add(KilledPid, first.Task);
            kills.Add(KilledPid + 1, second.Task);
        }));
        try
        {
            await stdout.SeenAsync("\"status\":\"succeeded\"", run, Ct);
            first.TrySetResult();

            // A moment for a host that would exit once one kill had ended to have done it.
            await Task.WhenAny(run, Task.Delay(500, Ct));
            Assert.False(run.IsCompleted, $"The host returned ({run.Status}) with its second kill still unfinished. It said: {stderr.Text}");
        }
        finally
        {
            first.TrySetResult();
            second.TrySetResult();
        }

        Assert.Equal(HostExitCodes.Completed, await run.WaitAsync(TimeSpan.FromSeconds(30), Ct));
    }

    [Fact]
    public async Task Without_arguments_it_is_a_usage_error_and_stdout_stays_empty()
    {
        var (exit, stdout, stderr) = await RunAsync([]);

        Assert.Equal(HostExitCodes.Usage, exit);
        Assert.Contains("usage", stderr, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(string.Empty, stdout);
    }

    [Fact]
    public async Task A_protocol_this_build_does_not_speak_is_a_usage_error()
    {
        var (exit, stdout, stderr) = await RunAsync(Argv(protocol: 2));

        Assert.Equal(HostExitCodes.Usage, exit);
        Assert.Contains("protocol", stderr, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(string.Empty, stdout);
    }

    [Fact]
    public async Task An_empty_stdin_is_a_rejected_invocation()
    {
        var (exit, stdout, stderr) = await RunAsync(Argv());

        Assert.Equal(HostExitCodes.Rejected, exit);
        Assert.Contains("\"code\":\"envelope_unreadable\"", stderr, StringComparison.Ordinal);
        Assert.Equal(string.Empty, stdout);
    }

    [Fact]
    public async Task Something_that_is_not_an_envelope_is_a_rejected_invocation()
    {
        using var stdin = new StringReader("not json at all");
        using var stdout = new StringWriter();
        using var stderr = new StringWriter();

        var exit = await PluginHostMode.RunAsync(Argv(), stdin, stdout, stderr, Ct);

        Assert.Equal(HostExitCodes.Rejected, exit);
        Assert.Contains("\"code\":\"envelope_invalid\"", stderr.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_envelope_that_disagrees_with_the_command_line_is_refused()
    {
        PluginHostMode.Runner = Succeeding();

        var (exit, _, stderr) = await RunAsync(Argv(pluginId: "other"), InvocationFactory.Create(_root, "echo.run"));

        Assert.Equal(HostExitCodes.Rejected, exit);
        Assert.Contains("\"code\":\"argv_mismatch\"", stderr, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_envelope_from_another_protocol_is_refused()
    {
        var invocation = InvocationFactory.Create(_root, "echo.run", configure: builder => builder.ProtocolVersion = 99);

        var (exit, _, stderr) = await RunAsync(Argv(), invocation);

        Assert.Equal(HostExitCodes.Rejected, exit);
        Assert.Contains("\"code\":\"protocol_unsupported\"", stderr, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_package_that_changed_since_the_runtime_validated_it_is_never_run()
    {
        var runner = Succeeding();
        PluginHostMode.Runner = runner;
        var invocation = InvocationFactory.Create(_root, "echo.run");
        File.WriteAllText(Path.Combine(_root, "main.js"), "export function invoke() { return { result: { tampered: true } }; }");

        var (exit, stdout, stderr) = await RunAsync(Argv(), invocation);

        Assert.Equal(HostExitCodes.Rejected, exit);
        Assert.Contains("\"code\":\"digest_mismatch\"", stderr, StringComparison.Ordinal);
        Assert.Equal(string.Empty, stdout);
        Assert.Equal(0, runner.Calls);
    }

    [Fact]
    public async Task A_hand_written_envelope_without_a_digest_runs_and_the_host_says_it_was_unverified()
    {
        var runner = Succeeding();
        PluginHostMode.Runner = runner;
        var invocation = InvocationFactory.Create(_root, "echo.run", configure: builder => builder.Digest = null);

        var (exit, stdout, stderr) = await RunAsync(Argv(), invocation);

        Assert.Equal(HostExitCodes.Completed, exit);
        Assert.Contains("digest_unverified", stderr, StringComparison.Ordinal);
        Assert.Equal(1, runner.Calls);
        Assert.NotEqual(string.Empty, stdout);
    }

    [Fact]
    public async Task A_missing_package_or_entry_module_is_refused()
    {
        var invocation = InvocationFactory.Create(_root, "echo.run", configure: builder => builder.Entry = new PluginEntry("absent.js", "invoke"));

        var (exit, _, stderr) = await RunAsync(Argv(), invocation);

        Assert.Equal(HostExitCodes.Rejected, exit);
        Assert.Contains("\"code\":\"entry_module_missing\"", stderr, StringComparison.Ordinal);

        var gone = Path.Combine(Path.GetTempPath(), "jason-plugin-host-tests", Guid.NewGuid().ToString("N"));
        var missing = InvocationFactory.Create(_root, "echo.run") with
        {
            Plugin = new InvocationPlugin("fake-provider", "1.0.0", PluginKind.Provider, gone, null, new PluginEntry("main.js", "invoke")),
        };

        var (exitMissing, _, stderrMissing) = await RunAsync(Argv(), missing);

        Assert.Equal(HostExitCodes.Rejected, exitMissing);
        Assert.Contains("\"code\":\"package_root_missing\"", stderrMissing, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_runner_that_throws_is_the_host_failing_rather_than_the_plugin()
    {
        PluginHostMode.Runner = new StubRunner(_ => throw new InvalidOperationException("the host broke"));

        var (exit, stdout, stderr) = await RunAsync(Argv(), InvocationFactory.Create(_root, "echo.run"));

        Assert.Equal(HostExitCodes.HostFailure, exit);
        Assert.Contains("the host broke", stderr, StringComparison.Ordinal);
        Assert.Equal(string.Empty, stdout);
    }

    [Fact]
    public async Task A_completed_invocation_writes_exactly_one_outcome_and_exits_zero()
    {
        var invocation = InvocationFactory.Create(_root, "echo.run");
        PluginHostMode.Runner = Succeeding();

        var (exit, stdout, _) = await RunAsync(Argv(), invocation);

        Assert.Equal(HostExitCodes.Completed, exit);
        var outcome = JsonSerializer.Deserialize<PluginOutcome>(stdout, JasonJson.Options)!;
        Assert.Equal(OutcomeStatus.Succeeded, outcome.Status);
        Assert.Equal(invocation.InvocationId, outcome.InvocationId);
        Assert.Equal(PluginProtocol.CurrentVersion, outcome.ProtocolVersion);
        Assert.False(stdout.TrimEnd().Contains('\n'), "stdout carries exactly one JSON document");
    }

    [Fact]
    public async Task The_runner_is_handed_the_envelope_it_was_given()
    {
        PluginInvocation? seen = null;
        PluginHostMode.Runner = new StubRunner(invocation =>
        {
            seen = invocation;
            return new PluginOutcome(PluginProtocol.CurrentVersion, invocation.InvocationId, OutcomeStatus.Succeeded, null, null, null, new OutcomeDiagnostics(0, 0, 0, 0));
        });
        var invocation = InvocationFactory.Create(_root, "echo.run", new JsonObject { ["value"] = "hello" });

        await RunAsync(Argv(), invocation);

        Assert.NotNull(seen);
        Assert.Equal("hello", seen.Input["value"]!.GetValue<string>());
        Assert.Equal(_root, seen.Plugin.Root);
    }
}
