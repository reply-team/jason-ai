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
/// The lease is deliberately not the signal. While the runtime is alive the lease enforcer ends an overdue
/// attempt and kills its child, and a host told to hang keeps its own lease healthy by heartbeating — so a
/// child giving up on its lease would be answering a question the runtime answers better, and would quietly
/// change what the lease and timeout tests assert. What is being watched for is the runtime itself: the
/// descriptor it publishes is gone, and it no longer answers.
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

    public static void Start(LaunchEnvelope envelope, RuntimeApi api) => _ = Task.Run(() => WatchAsync(envelope, api));

    private static async Task WatchAsync(LaunchEnvelope envelope, RuntimeApi api)
    {
        var started = Stopwatch.StartNew();
        var answered = false;
        var missed = 0;

        while (true)
        {
            await Task.Delay(Beat).ConfigureAwait(false);

            // A descriptor that has stopped being readable is the whole answer: a runtime takes its descriptor
            // with it, and nothing else removes one.
            var gone = api.ReadDescriptor() is null;

            if (!gone)
            {
                var answers = await AnswersAsync(envelope, api).ConfigureAwait(false);
                answered |= answers;

                // A descriptor that outlived its runtime — a directory that could not be removed — is caught
                // here instead. Only after an answer, though: a host pointed at a descriptor nothing is
                // listening behind is not an orphan, it is the case where an unreachable API is reported and
                // survived, which is a behaviour of its own.
                gone = answered && !answers;
            }

            if (!gone)
            {
                missed = 0;

                // The one case nothing can ever tell to stop.
                if (!answered && started.Elapsed >= NoRuntimeCeiling)
                {
                    await Diagnostics.WriteAsync("no runtime ever answered and the ceiling passed; stopping").ConfigureAwait(false);
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
