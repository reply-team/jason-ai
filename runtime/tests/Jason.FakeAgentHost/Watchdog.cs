using System.Diagnostics;
using Jason.Contracts.Api;
using Jason.Contracts.Execution;

namespace Jason.FakeAgentHost;

/// <summary>
/// The one thing this host does on its own initiative: stop, once the runtime that launched it is gone.
/// <para>
/// A shutdown abandons the children it is holding deliberately — their leases are still good and a survivor
/// finishes its work against the next runtime — and two of the behaviours here never end by design. Put those
/// together and a test that launched a host can leave it running after the test process itself has exited; on
/// Windows the handles it inherited then keep the test runner from seeing end of file, and a suite that has
/// finished sits open for as long as the child lives.
/// </para>
/// </summary>
/// <remarks>
/// <para>
/// The lease is deliberately not the signal. While the runtime is alive the lease enforcer ends an overdue
/// attempt and kills its child, and a host told to hang keeps its own lease healthy by heartbeating — so a
/// child giving up on its lease would be answering a question the runtime answers better, and would quietly
/// change what the lease and timeout tests assert. What is being watched for is the runtime itself: the
/// descriptor it published is gone, or it has stopped answering.
/// </para>
/// <para>
/// The contract says a survivor may finish its work against the <em>next</em> runtime, by re-reading the
/// descriptor. This stand-in deliberately does not wait for one: in a test the next runtime belongs to another
/// test, and a child that waited for it is the orphan this exists to prevent. A real host is not affected —
/// nothing here ships.
/// </para>
/// </remarks>
internal static class Watchdog
{
    /// <summary>Nobody was there any more. Distinct from every behaviour's own ending.</summary>
    public const int Abandoned = 5;

    private static readonly TimeSpan Beat = TimeSpan.FromSeconds(1);

    /// <summary>
    /// How long a host that has never reached a runtime may live. Nothing can ever tell it to stop, so it is
    /// the one case that needs a clock rather than an observation.
    /// </summary>
    private static readonly TimeSpan NoRuntimeCeiling = TimeSpan.FromSeconds(120);

    /// <summary>Two in a row, so a runtime restarting between the write of a descriptor and its read is not a death.</summary>
    private const int Strikes = 2;

    /// <param name="sawRuntime">
    /// Whether a descriptor could be read when this host started. It is the difference between the two cases:
    /// a host that never had a runtime cannot be abandoned by one, and is bounded by the ceiling instead.
    /// </param>
    public static void Start(LaunchEnvelope envelope, RuntimeApi api, bool sawRuntime) =>
        _ = Task.Run(() => WatchAsync(envelope, api, sawRuntime));

    private static async Task WatchAsync(LaunchEnvelope envelope, RuntimeApi api, bool sawRuntime)
    {
        var started = Stopwatch.StartNew();
        var seen = sawRuntime;
        var answered = false;
        var missed = 0;

        while (true)
        {
            await Task.Delay(Beat).ConfigureAwait(false);

            var readable = api.ReadDescriptor() is not null;
            seen |= readable;

            // Gone, in the two ways a runtime can go: the descriptor it published has been taken away — a
            // runtime takes its descriptor with it, and nothing else removes one — or it published one, was
            // answering, and has stopped.
            var gone = seen && !readable;
            if (!gone && readable)
            {
                var answers = await AnswersAsync(envelope, api).ConfigureAwait(false);
                answered |= answers;
                gone = answered && !answers;
            }

            if (!gone)
            {
                missed = 0;

                // Never had a runtime at all: nothing can ever tell this host to stop, so the clock does. A
                // host pointed at a descriptor that was never there is not an orphan — it is the case where an
                // unreachable API is reported and survived, which is a behaviour with a test of its own.
                if (!seen && started.Elapsed >= NoRuntimeCeiling)
                {
                    await Diagnostics.WriteAsync("no runtime was ever there and the ceiling passed; stopping").ConfigureAwait(false);
                    Environment.Exit(Abandoned);
                }

                continue;
            }

            if (++missed >= Strikes)
            {
                await Diagnostics.WriteAsync("the runtime that launched this host is gone; stopping").ConfigureAwait(false);
                Environment.Exit(Abandoned);
            }
        }
    }

    /// <summary>One harmless read, to see whether anything is still behind the descriptor.</summary>
    private static async Task<bool> AnswersAsync(LaunchEnvelope envelope, RuntimeApi api)
    {
        var (status, _) = await api.CallAsync(Operations.WorkItemGet, new WorkItemGetRequest(envelope.WorkItemId, false))
            .ConfigureAwait(false);
        return status != RuntimeApi.NoAnswer;
    }
}
