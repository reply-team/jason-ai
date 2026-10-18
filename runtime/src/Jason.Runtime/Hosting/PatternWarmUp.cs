using System.Diagnostics;
using System.Text.Json.Nodes;
using Jason.Contracts.Events;
using Jason.Contracts.Notifications;
using Jason.Contracts.Operations;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Jason.Runtime.Hosting;

/// <summary>
/// The runtime's own act at start: every pattern this build publishes — in each canonical operation's input and output
/// schema, in the delivery contract's and in the events contract's — compiled and matched once, before the plugin load and before the
/// dispatcher's first scan, outside any request. A cold engine pays for its automaton and its compiled code inside
/// its first match's timeout, and on a loaded machine that alone could run past the timeout; paid here, it is paid
/// where nobody is waiting, and a request meets a warm engine.
/// </summary>
/// <remarks>
/// <para>
/// It belongs to the runtime's host, and deliberately not to the catalogs that read these schemas or to a static
/// initialiser: a warm-up belongs to the process that matches values, and that is the runtime. The contracts assembly
/// is loaded by the command-line client too, which matches none, and a warm-up that ran wherever that assembly or a
/// catalog is first touched would make a cold <c>jason --help</c> pay for patterns nobody there matches, on the start
/// the release smoke test times.
/// </para>
/// <para>
/// Nothing here refuses or fails. The patterns are the build's own, so a timeout while warming is the cost being paid
/// and the warm-up tries again; a pattern it gives up on is left to a request's own second match, and the line it logs
/// says how many it finished and how many it left. The timing is the one every validating site is given, which is
/// how a test sees that this ran.
/// </para>
/// </remarks>
public sealed class PatternWarmUp(PatternTiming timing, ILogger<PatternWarmUp> logger) : IHostedService
{
    private static readonly Action<ILogger, int, long, Exception?> Warmed = LoggerMessage.Define<int, long>(
        LogLevel.Debug,
        new EventId(1, nameof(Warmed)),
        "Published schema patterns warmed: {Finished} in {DurationMs} ms");

    private static readonly Action<ILogger, int, int, long, Exception?> PartlyWarmed = LoggerMessage.Define<int, int, long>(
        LogLevel.Warning,
        new EventId(2, nameof(PartlyWarmed)),
        "Published schema patterns warmed: {Finished}; {Unfinished} did not finish warming in {DurationMs} ms and are left to a request's own second match");

    /// <summary>Every schema this build publishes and the runtime applies: the operations' first, then the delivery family's.</summary>
    public static IEnumerable<JsonObject> PublishedSchemas() =>
        OperationCatalog.All.SelectMany(contract => new[] { contract.InputSchema, contract.OutputSchema })
            .Concat(DeliveryCatalog.All.SelectMany(contract => new[] { contract.InputSchema, contract.OutputSchema }))
            .Concat(EventsCatalog.All.SelectMany(contract => new[] { contract.InputSchema, contract.OutputSchema }));

    public Task StartAsync(CancellationToken cancellationToken)
    {
        // Synchronous on purpose: hosted services start one after another, so returning only once every pattern has
        // been matched is what puts this before the plugin load and the first scan.
        cancellationToken.ThrowIfCancellationRequested();
        var elapsed = Stopwatch.StartNew();
        var warmed = SchemaValidator.Warm(PublishedSchemas(), timing);

        if (warmed.Unfinished == 0)
        {
            Warmed(logger, warmed.Finished, elapsed.ElapsedMilliseconds, null);
        }
        else
        {
            PartlyWarmed(logger, warmed.Finished, warmed.Unfinished, elapsed.ElapsedMilliseconds, null);
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
