using System.Diagnostics;
using System.Text.Json.Nodes;
using Jason.Contracts.Plugins;
using Jason.PluginHost.Sdk;
using Jason.PluginHost.Tests.Fixtures;

namespace Jason.PluginHost.Tests.Sdk;

/// <summary>
/// A program a plugin runs without a timeout of its own may run for what is left of the invocation less a kill grace —
/// the smaller of five seconds and half of what is left — and that grace is the whole of what the host spends once the
/// program has ended: waiting for a killed program to go, and reading what it wrote. So a plugin that does not pass
/// <c>timeout_ms</c> is told its program timed out while it still has time to answer for itself, and a kill that does not
/// take is said, once, rather than waited out.
/// </summary>
/// <remarks>
/// Two of these tests time a call, so the class runs apart from the rest of this assembly: a timing taken beside a dozen
/// other tests measures the dozen.
/// </remarks>
[Collection(TimingCollection.Name)]
public sealed class KillGraceTests : IDisposable
{
    private const string Cli = "fake-cli";

    private static readonly TimeSpan FourSeconds = TimeSpan.FromSeconds(4);

    private readonly TempPackage _package = new();

    public void Dispose() => _package.Dispose();

    private static InvocationGrants Granted { get; } = new(new ExecGrants([new ExecutableGrant(Cli, FakeProviderCli.ExecutablePath)]), null, null);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private SdkHarness Harness(Func<Process, TimeSpan, bool>? endedWithin = null, Action<Process>? kill = null) =>
        new(_package.Root, builder => builder.Grants = Granted, remaining: FourSeconds, endedWithin: endedWithin, kill: kill);

    private static JsonObject Exec(SdkHarness harness, string request) =>
        (JsonObject)JsJson.ToJson(harness.Engine, harness.Evaluate("host.exec(" + request + ")"))!;

    private static IReadOnlyList<JsonObject> Unconfirmed(SdkHarness harness) =>
        [.. harness.Lines.Where(line => line["message"]?.GetValue<string>() == "exec_kill_unconfirmed")];

    /// <summary>
    /// The program started once before a timing, or before a run that has only its budget to spend: the first start of a
    /// freshly built program can be held while the machine inspects it, and that is not the host's time to measure.
    /// </summary>
    private void WarmUp()
    {
        using var harness = new SdkHarness(_package.Root, builder => builder.Grants = Granted);
        Exec(harness, $"{{ executable: \"{Cli}\", args: [\"exit\", \"0\"] }}");
    }

    /// <summary>The arithmetic the plugin page publishes, row by row: milliseconds, and -1 where no timeout was asked for.</summary>
    [Theory]
    [InlineData(20_000, -1, 15_000, 5_000)]
    [InlineData(10_000, -1, 5_000, 5_000)]
    [InlineData(4_000, -1, 2_000, 2_000)]
    [InlineData(1, -1, 1, 0.5)]
    [InlineData(20_000, 3_000, 3_000, 5_000)]
    [InlineData(20_000, 30_000, 20_000, 5_000)]
    [InlineData(4_000, 4_000, 4_000, 2_000)]
    public void A_call_without_a_timeout_runs_for_what_is_left_less_the_kill_grace(double left, double asked, double timeout, double grace)
    {
        var budget = ExecService.Budget(TimeSpan.FromMilliseconds(left), asked < 0 ? null : TimeSpan.FromMilliseconds(asked));

        Assert.Equal((TimeSpan.FromMilliseconds(timeout), TimeSpan.FromMilliseconds(grace)), budget);
    }

    /// <summary>
    /// The arithmetic above is what a call is given: with four seconds left by the host's own count, a call without
    /// <c>timeout_ms</c> hands its program two and keeps two back, and a call that asked for one and a half seconds hands
    /// it that — never all that is left. The program exits at once, so no clock is read.
    /// </summary>
    [Theory]
    [InlineData(-1, 2_000)]
    [InlineData(1_500, 1_500)]
    public void A_call_hands_its_program_the_budget_s_timeout_and_not_all_that_is_left(int asked, int timeout)
    {
        var given = new List<(TimeSpan Timeout, TimeSpan Grace)>();
        using var harness = new SdkHarness(
            _package.Root,
            builder => builder.Grants = Granted,
            remaining: FourSeconds,
            spending: (program, grace) => given.Add((program, grace)));
        var request = asked < 0
            ? $"{{ executable: \"{Cli}\", args: [\"exit\", \"0\"] }}"
            : $"{{ executable: \"{Cli}\", args: [\"exit\", \"0\"], timeout_ms: {asked} }}";

        Exec(harness, request);

        Assert.Equal([(TimeSpan.FromMilliseconds(timeout), TimeSpan.FromSeconds(2))], given);
    }

    /// <summary>
    /// The whole invocation, end to end: a plugin that runs a program without <c>timeout_ms</c> and answers a timeout
    /// itself. Given all of what was left, the program spends it, and the plugin is told at the very end of its budget —
    /// whether it still gets to answer then is a race between the kill and the engine's own clock, and a kill that is
    /// slow to take loses it to <c>plugin_timeout</c>. With the grace kept back, the plugin is told while it can still
    /// answer, so the invocation ends with the plugin's own <c>exec_timed_out</c> — ambiguous, since the program may have
    /// acted — and not with the host's timeout. How much grace is kept back is the budget's arithmetic, held by the rows
    /// above and by the plugin page, not by this one.
    /// </summary>
    [Fact]
    public void A_plugin_that_runs_a_program_without_a_timeout_is_told_with_time_to_answer_itself()
    {
        WarmUp();
        var budget = TimeSpan.FromSeconds(8);
        using var package = new TempPackage($$"""
            export function invoke() {
              const ran = host.exec({ executable: "{{Cli}}", args: ["sleep", "30000"] });
              if (ran.timed_out) {
                throw host.fail({ class: "ambiguous", code: "exec_timed_out", message: "the program outlived its time" });
              }

              return { result: { exit_code: ran.exit_code } };
            }
            """);
        var invocation = InvocationFactory.Create(package.Root, "echo.run", configure: builder =>
        {
            builder.Grants = Granted;
            builder.Limits = builder.Limits with { TimeoutMs = (int)budget.TotalMilliseconds };
        });
        using var stderr = new StringWriter();
        using var deadline = new CancellationTokenSource(budget + TimeSpan.FromSeconds(1));

        var outcome = new InvocationRunner().Run(invocation, SdkHarness.DiagnosticsFor(stderr, invocation), new UnfinishedKills(), deadline.Token);

        Assert.True(
            outcome.Status == OutcomeStatus.Failed && outcome.Error?.Code == "exec_timed_out",
            $"Expected the plugin's own exec_timed_out; the invocation ended {outcome.Status} with {outcome.Error?.Code} after {outcome.Diagnostics.DurationMs} ms.");
        Assert.Equal(FailureClass.Ambiguous, outcome.Error!.Class);
    }

    /// <summary>
    /// A kill the operating system did not carry out within the grace is said in one line that names the process and
    /// the grace, and nothing a plugin wrote — and the call is still answered as a timeout once the grace kept back for
    /// it has run out, rather than after waits that each took their own five seconds. The program leaves a helper behind
    /// that holds its pipes, so after the wait for the program the reading of its output waits too — inside the same
    /// grace, not in one of its own. The host's part of the budget is what is measured: the grace it waits with, and
    /// everything it does after the kill. Starting the program and being scheduled on a busy machine come before both,
    /// and the call's whole time on a runner starting every test project at once has included seconds of them.
    /// </summary>
    [Fact]
    public void A_killed_program_not_seen_to_end_within_the_grace_is_said_once_and_the_call_returns_when_the_grace_runs_out()
    {
        WarmUp();
        var watch = Stopwatch.StartNew();
        var killedAt = TimeSpan.Zero;
        var given = TimeSpan.Zero;
        using var harness = Harness(endedWithin: (_, grace) =>
        {
            killedAt = watch.Elapsed;
            given = grace;
            Thread.Sleep(grace);
            return false;
        });

        watch.Restart();
        var result = Exec(harness, $"{{ executable: \"{Cli}\", args: [\"spawn-orphan\", \"30000\"] }}");
        watch.Stop();

        Assert.True(result["timed_out"]!.GetValue<bool>());

        // The grace kept back from four seconds is two, and everything after the kill fits in it: the kill itself, the wait
        // for the program and the reads its helper holds open alike. The wait for the program is given what the kill left.
        Assert.True(given > TimeSpan.Zero && given <= TimeSpan.FromSeconds(2), $"The wait for the killed program was given {given.TotalMilliseconds:0} ms of a 2000 ms grace.");
        var afterTheKill = watch.Elapsed - killedAt;
        Assert.True(afterTheKill < TimeSpan.FromSeconds(2.5), $"The call spent {afterTheKill.TotalMilliseconds:0} ms after the kill, of a 2000 ms grace.");

        var lines = Unconfirmed(harness);
        Assert.True(lines.Count == 1, $"Expected one exec_kill_unconfirmed line; the host wrote: {harness.Stderr}");
        Assert.Equal("warn", lines[0]["level"]!.GetValue<string>());
        var data = lines[0]["data"]!.AsObject();
        Assert.Equal(["grace_ms", "pid"], data.Select(pair => pair.Key).Order(StringComparer.Ordinal));
        Assert.Equal("2000", data["grace_ms"]!.ToJsonString());
        Assert.True(data["pid"]!.GetValue<int>() > 0, lines[0].ToJsonString());
    }

    /// <summary>
    /// A kill that does not come back is waited for within the grace and no longer. .NET walks every process on the machine
    /// before it kills anything, so the kill is the one step after a timeout whose length the host cannot bound; the call
    /// still answers as a timeout when the grace runs out, says the kill unconfirmed, and leaves the kill to finish on its
    /// own. The kill here stands still for thirty seconds, and what is measured is the call's time from the moment it began.
    /// </summary>
    [Fact]
    public void A_kill_that_does_not_come_back_within_the_grace_does_not_hold_the_call()
    {
        WarmUp();
        var watch = Stopwatch.StartNew();
        var killedAt = TimeSpan.Zero;
        var pid = 0;
        using var release = new ManualResetEventSlim();
        using var harness = Harness(kill: process =>
        {
            killedAt = watch.Elapsed;
            pid = process.Id;
            release.Wait(TimeSpan.FromSeconds(30));
        });

        try
        {
            watch.Restart();
            var result = Exec(harness, $"{{ executable: \"{Cli}\", args: [\"sleep\", \"120000\"], timeout_ms: 300 }}");
            var afterTheKill = watch.Elapsed - killedAt;

            Assert.True(result["timed_out"]!.GetValue<bool>());

            // The grace kept back from four seconds is two; three seconds more is the host's own time to answer, on a busy machine.
            Assert.True(afterTheKill < TimeSpan.FromSeconds(5), $"The call answered {afterTheKill.TotalMilliseconds:0} ms after its kill began, of a 2000 ms grace.");
            var lines = Unconfirmed(harness);
            Assert.True(lines.Count == 1, $"Expected one exec_kill_unconfirmed line; the host wrote: {harness.Stderr}");
            Assert.Equal("2000", lines[0]["data"]!["grace_ms"]!.ToJsonString());
        }
        finally
        {
            release.Set();
            EndByNumber(pid);
        }
    }

    /// <summary>
    /// A kill that outlives the grace still ends the program. The call has answered and let go of everything else by then,
    /// but not of the process the kill is ending: disposed under a kill still running, it leaves the kill no process to
    /// end, and the program the plugin was told had timed out runs on. The real kill is run here only once the call has
    /// answered, and three seconds later still — longer than the whole two-second grace — so the process is held for as
    /// long as the kill takes, not for a grace more.
    /// </summary>
    [Fact]
    public async Task A_kill_that_finishes_after_the_call_has_answered_still_ends_the_program()
    {
        var pid = 0;
        using var answered = new ManualResetEventSlim();
        var killed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var harness = Harness(kill: process =>
        {
            pid = process.Id;
            answered.Wait(TimeSpan.FromSeconds(30));
            Thread.Sleep(TimeSpan.FromSeconds(3));
            ExecService.TryKill(process);
            killed.SetResult();
        });

        try
        {
            var result = Exec(harness, $"{{ executable: \"{Cli}\", args: [\"sleep\", \"120000\"], timeout_ms: 300 }}");
            answered.Set();
            await killed.Task.WaitAsync(TimeSpan.FromSeconds(30), Ct);

            Assert.True(result["timed_out"]!.GetValue<bool>());
            Assert.True(Gone(pid, TimeSpan.FromSeconds(10)), $"Process {pid} still runs after the kill that finished once the call had answered.");

            // Seen gone: the number may now be another program's.
            pid = 0;
        }
        finally
        {
            answered.Set();
            EndByNumber(pid);
        }
    }

    /// <summary>
    /// A helper the killed program left behind can hold its pipes open past the grace; the program itself was seen to
    /// end, so the kill took, and running out of time to read is not a kill that failed.
    /// </summary>
    [Fact]
    public async Task A_read_that_runs_out_after_a_killed_program_ended_is_not_a_kill_that_failed()
    {
        using var harness = Harness();

        var result = await Task.Run(() => Exec(harness, $"{{ executable: \"{Cli}\", args: [\"spawn-orphan\", \"30000\"] }}"), Ct)
            .WaitAsync(TimeSpan.FromSeconds(20), Ct);

        Assert.True(result["timed_out"]!.GetValue<bool>());
        Assert.True(Unconfirmed(harness).Count == 0, $"A kill that took was said to have failed: {harness.Stderr}");
    }

    /// <summary>The same helper behind a program that exited of its own accord: nothing was killed, so nothing failed.</summary>
    [Fact]
    public async Task A_read_that_runs_out_after_a_clean_exit_is_not_a_kill_that_failed()
    {
        using var harness = Harness();

        var result = await Task.Run(() => Exec(harness, $"{{ executable: \"{Cli}\", args: [\"spawn-orphan-inner\", \"30000\"] }}"), Ct)
            .WaitAsync(TimeSpan.FromSeconds(20), Ct);

        Assert.False(result["timed_out"]!.GetValue<bool>());
        Assert.True(Unconfirmed(harness).Count == 0, $"A clean exit was said to be a kill that failed: {harness.Stderr}");
    }

    /// <summary>Whether the process with this id has gone, or goes within the time given.</summary>
    internal static bool Gone(int pid, TimeSpan within)
    {
        var deadline = Stopwatch.StartNew();
        while (true)
        {
            try
            {
                using var process = Process.GetProcessById(pid);
                if (process.HasExited)
                {
                    return true;
                }
            }
            catch (ArgumentException)
            {
                return true;
            }

            if (deadline.Elapsed >= within)
            {
                return false;
            }

            Thread.Sleep(50);
        }
    }

    /// <summary>A program a test's kill stood in for is ended by its number, whatever the test found.</summary>
    internal static void EndByNumber(int pid)
    {
        if (pid <= 0)
        {
            return;
        }

        try
        {
            using var process = Process.GetProcessById(pid);
            process.Kill(entireProcessTree: true);
        }
        catch (ArgumentException)
        {
            // Already gone.
        }
        catch (InvalidOperationException)
        {
            // Gone between the lookup and the kill.
        }
    }
}
