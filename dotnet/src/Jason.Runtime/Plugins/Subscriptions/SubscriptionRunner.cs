using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Jason.Contracts.Api;
using Jason.Contracts.Ids;
using Jason.Contracts.Json;
using Jason.Contracts.Plugins;
using Jason.Contracts.Subscriptions;
using Jason.Runtime.Configuration;
using Jason.Runtime.Domain;
using Jason.Runtime.Execution;
using Jason.Runtime.Journal;
using Jason.Runtime.Logging;
using Jason.Runtime.Persistence;
using Jason.Runtime.Plugins.Invocation;
using Jason.Runtime.Plugins.Registry;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Jason.Runtime.Plugins.Subscriptions;

/// <summary>
/// What runs the subscribed kinds' chains, off the scan: never a work item and never one of the dispatcher's handler
/// slots. It holds one chain in flight per kind and at most <c>Subscriptions:MaxParallel</c> children at once.
/// </summary>
/// <remarks>
/// <para>
/// A turn of a kind takes its lines one at a time, at most <see cref="LinesPerTurn"/>, and hands each line to every member
/// of the kind's chain in the order the load froze, each handler given the envelope the one before returned. Each line's
/// start reads the kind's position afresh, and its end moves the position only where it still stands as that read found
/// it — the same row at the same revision; otherwise the line's record is superseded and the turn ends. A move under a
/// turn is therefore seen at the next line, never later.
/// </para>
/// <para>
/// Delivery is at least once: a handler entry is written before its child starts, so a stop, a crash or a backoff that
/// outlives the runtime leaves a record the next start takes up and runs from the first handler, and each handler is
/// told by <c>attempt</c> how many times it has been handed the line. A transient failure is tried again after a backoff
/// that holds no child; every other failure, and a transient one whose retries are spent, ends the chain for that line
/// with one <c>subscription_failed</c> line, and the kind moves on. A line a plugin wrote, or one about work a plugin
/// created, is passed over: a handler's own acts never start another chain.
/// </para>
/// </remarks>
public sealed class SubscriptionRunner(
    LiveSettings<SubscriptionsOptions> settings,
    IServiceScopeFactory scopes,
    PluginRegistry plugins,
    TimeProvider clock,
    ILogger<SubscriptionRunner> logger) : BackgroundService
{
    /// <summary>How many lines of a kind, skipped ones included, one turn takes before the kind goes back to the scan.</summary>
    public const int LinesPerTurn = 50;

    /// <summary>The longest wait before a transient handler is run again, whatever the doubling reaches.</summary>
    public const int MaximumBackoffSeconds = 3_600;

    private const string Contract = "subscription.handle";

    private const string Stamp = "yyyy-MM-dd'T'HH:mm:ss.fff'Z'";

    private static readonly Action<ILogger, string, string, Exception?> Started = LoggerMessage.Define<string, string>(
        LogLevel.Information,
        new EventId(1, nameof(Started)),
        "Subscription chain for {Kind} at line {LineId} started");

    private static readonly Action<ILogger, string, string, string, string, long, Exception?> Ended = LoggerMessage.Define<string, string, string, string, long>(
        LogLevel.Information,
        new EventId(2, nameof(Ended)),
        "Subscription handler {PluginId} for {Kind} ended {Outcome} ({Code}) in {DurationMs} ms");

    private static readonly Action<ILogger, string, string, string, string, string, Exception?> ChainFailed = LoggerMessage.Define<string, string, string, string, string>(
        LogLevel.Warning,
        new EventId(3, nameof(ChainFailed)),
        "Subscription chain for {Kind} at line {LineId} failed at {PluginId} ({Class}, {Code})");

    private static readonly Action<ILogger, string, string, string, Exception?> Waits = LoggerMessage.Define<string, string, string>(
        LogLevel.Warning,
        new EventId(4, nameof(Waits)),
        "Subscription kind {Kind} waits at line {LineId} on unavailable {PluginId}");

    private static readonly Action<ILogger, int, Exception?> Stopped = LoggerMessage.Define<int>(
        LogLevel.Information,
        new EventId(5, nameof(Stopped)),
        "Subscription runner stopped with {Count} chains in flight");

    // What a parser quotes is what the value held, so a parse failure is said by its sentence and never handed over whole.
    private static readonly Action<ILogger, string, string, Exception?> GaveOut = LoggerMessage.Define<string, string>(
        LogLevel.Warning,
        new EventId(6, nameof(GaveOut)),
        "Subscription chain for {Kind} could not finish ({Failure}); the next scan offers it again");

    private readonly SubscriptionContract _contract =
        SubscriptionCatalog.Find(Contract) ?? throw new InvalidOperationException("This build publishes no subscription.handle contract.");

    private readonly Lock _gate = new();
    private readonly List<Flight> _flight = [];
    private readonly HashSet<(string Snapshot, string Kind)> _said = [];
    private readonly SemaphoreSlim _wake = new(0, int.MaxValue);
    private readonly SemaphoreSlim _settle = new(1, 1);
    private bool _settled;
    private int _children;

    private enum FlightState
    {
        Offered,
        Running,
        Waiting,
    }

    /// <summary>
    /// Whether a chain offered now would have a child to run in: the children running and the chains whose backoff is
    /// over, which have the first claim, leave one free. The scan offers nothing more once this is false.
    /// </summary>
    public bool HasFreeChild
    {
        get
        {
            lock (_gate)
            {
                return _children + DueWaiting() < settings.Current.MaxParallel;
            }
        }
    }

    /// <summary>The kinds with a chain in flight, waiting out a backoff included, in the order they were taken.</summary>
    public IReadOnlyList<string> InFlight
    {
        get
        {
            lock (_gate)
            {
                return [.. _flight.Select(flight => flight.Kind)];
            }
        }
    }

    /// <summary>Takes a kind's next turn; false, and nothing taken, while the kind has a chain in flight or every child is taken.</summary>
    public bool TryStart(string kind)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(kind);
        lock (_gate)
        {
            if (Find(kind) is not null || _children + DueWaiting() >= settings.Current.MaxParallel)
            {
                return false;
            }

            _flight.Add(new Flight(kind) { State = FlightState.Offered });
            _children++;
        }

        _wake.Release();
        return true;
    }

    /// <summary>
    /// The kind's next turn, run now on the caller's flow rather than by the service's own loop: the turn offered, the
    /// backoff that is over, or a kind not in flight taken here while a child is free; null when none of these holds. A
    /// test's way to run a turn at a moment it chooses; the runtime runs them in <see cref="ExecuteAsync"/>.
    /// </summary>
    internal async Task<SubscriptionTurn?> RunNextAsync(string kind, CancellationToken ct)
    {
        Flight? flight;
        lock (_gate)
        {
            flight = Find(kind);
            if (flight is null)
            {
                if (_children + DueWaiting() >= settings.Current.MaxParallel)
                {
                    return null;
                }

                flight = new Flight(kind) { State = FlightState.Running };
                _flight.Add(flight);
                _children++;
            }
            else if (flight.State == FlightState.Offered)
            {
                flight.State = FlightState.Running;
            }
            else if (flight.State == FlightState.Waiting && IsDue(flight) && _children < settings.Current.MaxParallel)
            {
                flight.State = FlightState.Running;
                _children++;
            }
            else
            {
                return null;
            }
        }

        return await TurnAsync(flight, ct).ConfigureAwait(false);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var turns = new List<Task>();
        try
        {
            while (true)
            {
                foreach (var flight in TakeStartable())
                {
                    turns.Add(Task.Run(() => LoopTurnAsync(flight, stoppingToken), CancellationToken.None));
                }

                turns.RemoveAll(turn => turn.IsCompleted);
                using var waiting = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                var woken = _wake.WaitAsync(waiting.Token);
                if (NextDue() is { } due)
                {
                    var delay = due - clock.GetUtcNow();
                    await Task.WhenAny(woken, Task.Delay(delay > TimeSpan.Zero ? delay : TimeSpan.Zero, clock, waiting.Token)).ConfigureAwait(false);
                    stoppingToken.ThrowIfCancellationRequested();
                    await waiting.CancelAsync().ConfigureAwait(false);
                }
                else
                {
                    await woken.ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // A stop writes nothing: what was in flight is taken up again by the next start, from its record.
        }

        int count;
        lock (_gate)
        {
            count = _flight.Count;
        }

        await Task.WhenAll(turns).ConfigureAwait(false);
        if (count > 0)
        {
            Stopped(logger, count, null);
        }
    }

    public override void Dispose()
    {
        _wake.Dispose();
        _settle.Dispose();
        base.Dispose();
    }

    private async Task LoopTurnAsync(Flight flight, CancellationToken stoppingToken)
    {
        try
        {
            await TurnAsync(flight, stoppingToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Stopped mid-turn: the record keeps what was under way, and the next start takes it up.
        }
        catch (Exception ex)
        {
            var parse = ParseFailure.Is(ex);
            GaveOut(logger, flight.Kind, parse ? ParseFailure.Describe(ex) : ex.GetType().Name, parse ? null : ex);
        }
    }

    private List<Flight> TakeStartable()
    {
        lock (_gate)
        {
            var max = settings.Current.MaxParallel;
            var startable = new List<Flight>();
            foreach (var flight in _flight)
            {
                if (flight.State == FlightState.Offered)
                {
                    flight.State = FlightState.Running;
                    startable.Add(flight);
                }
                else if (flight.State == FlightState.Waiting && IsDue(flight) && _children < max)
                {
                    flight.State = FlightState.Running;
                    _children++;
                    startable.Add(flight);
                }
            }

            return startable;
        }
    }

    /// <summary>When the earliest backoff not yet over ends: the one moment the loop wakes for without being told.</summary>
    private DateTimeOffset? NextDue()
    {
        lock (_gate)
        {
            var now = clock.GetUtcNow();
            return _flight
                .Where(flight => flight.State == FlightState.Waiting && flight.Paused!.Due > now)
                .Select(flight => (DateTimeOffset?)flight.Paused!.Due)
                .Min();
        }
    }

    private async Task<SubscriptionTurn> TurnAsync(Flight flight, CancellationToken ct)
    {
        Paused? paused = null;
        try
        {
            var (turn, pause) = await RunTurnAsync(flight, ct).ConfigureAwait(false);
            paused = pause;
            return turn;
        }
        finally
        {
            Release(flight, paused);
        }
    }

    private void Release(Flight flight, Paused? paused)
    {
        lock (_gate)
        {
            _children--;
            if (paused is null)
            {
                _flight.Remove(flight);
            }
            else
            {
                flight.State = FlightState.Waiting;
                flight.Paused = paused;
            }
        }

        _wake.Release();
    }

    private Flight? Find(string kind) => _flight.Find(flight => string.Equals(flight.Kind, kind, StringComparison.Ordinal));

    private bool IsDue(Flight flight) => flight.Paused is { } paused && paused.Due <= clock.GetUtcNow();

    private int DueWaiting() => _flight.Count(flight => flight.State == FlightState.Waiting && IsDue(flight));

    private async Task<(SubscriptionTurn Turn, Paused? Paused)> RunTurnAsync(Flight flight, CancellationToken ct)
    {
        await SettleAsync(ct).ConfigureAwait(false);
        var kind = flight.Kind;
        var lines = 0;
        var skipped = 0;

        if (flight.Paused is { } resumed)
        {
            flight.Paused = null;
            lines++;
            var end = await ChainAsync(resumed.Chain, resumed.Handler, resumed.Envelope, ct).ConfigureAwait(false);
            if (end.Paused is not null)
            {
                return (new SubscriptionTurn(kind, lines, skipped, SubscriptionTurn.Backoff), end.Paused);
            }

            if (end.Superseded)
            {
                return (new SubscriptionTurn(kind, lines, skipped, SubscriptionTurn.Superseded), null);
            }
        }

        while (lines + skipped < LinesPerTurn)
        {
            await using var scope = scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<JasonDbContext>();
            var cursor = await db.SubscriptionCursors.AsNoTracking().SingleOrDefaultAsync(row => row.Kind == kind, ct).ConfigureAwait(false);
            if (cursor is null)
            {
                return (new SubscriptionTurn(kind, lines, skipped, SubscriptionTurn.Gone), null);
            }

            var batch = await db.Journal
                .AsNoTracking()
                .Where(line => line.Kind == kind && line.Id > cursor.AfterLine)
                .OrderBy(line => line.Id)
                .Take(LinesPerTurn - lines - skipped)
                .Select(line => new
                {
                    line.Id,
                    line.PublicId,
                    Skip = line.ActorType == ActorType.Plugin
                        || db.WorkItems.Any(item => item.PublicId == line.WorkItemId && item.CreatedByType == ActorType.Plugin),
                })
                .ToListAsync(ct)
                .ConfigureAwait(false);
            if (batch.Count == 0)
            {
                return (new SubscriptionTurn(kind, lines, skipped, SubscriptionTurn.Head), null);
            }

            // A member that cannot run on this machine is no outcome: the kind waits at its line and nothing is spent,
            // not even the lines that would be passed over.
            var snapshot = plugins.Snapshot;
            var members = snapshot.Subscriptions.For(kind);
            if (members.Count == 0)
            {
                return (new SubscriptionTurn(kind, lines, skipped, SubscriptionTurn.Waiting), null);
            }

            if (members.FirstOrDefault(id => snapshot.Find(id) is not { Status: PluginStatus.Valid }) is { } unavailable)
            {
                bool first;
                lock (_gate)
                {
                    first = _said.Add((snapshot.Id, kind));
                }

                if (first)
                {
                    Waits(logger, kind, batch[0].PublicId, unavailable, null);
                }

                return (new SubscriptionTurn(kind, lines, skipped, SubscriptionTurn.Waiting), null);
            }

            var leading = batch.TakeWhile(line => line.Skip).Count();
            if (leading > 0)
            {
                var past = batch[leading - 1].Id;
                if (!await MoveAsync(db, cursor, past, ct).ConfigureAwait(false))
                {
                    return (new SubscriptionTurn(kind, lines, skipped, SubscriptionTurn.Superseded), null);
                }

                skipped += leading;
                continue;
            }

            lines++;
            var chain = await StartAsync(db, snapshot, members, cursor, batch[0].Id, ct).ConfigureAwait(false);
            var end = await ChainAsync(chain, 0, [], ct).ConfigureAwait(false);
            if (end.Paused is not null)
            {
                return (new SubscriptionTurn(kind, lines, skipped, SubscriptionTurn.Backoff), end.Paused);
            }

            if (end.Superseded)
            {
                return (new SubscriptionTurn(kind, lines, skipped, SubscriptionTurn.Superseded), null);
            }
        }

        return (new SubscriptionTurn(kind, lines, skipped, SubscriptionTurn.Limit), null);
    }

    /// <summary>Moves the position past lines passed over, where it still stands as the read found it.</summary>
    private async Task<bool> MoveAsync(JasonDbContext db, SubscriptionCursor cursor, int past, CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var moved = await db.SubscriptionCursors
            .Where(row => row.Id == cursor.Id && row.Kind == cursor.Kind && row.Revision == cursor.Revision)
            .ExecuteUpdateAsync(set => set
                .SetProperty(row => row.AfterLine, past)
                .SetProperty(row => row.Revision, row => row.Revision + 1)
                .SetProperty(row => row.UpdatedAt, now), ct)
            .ConfigureAwait(false);
        return moved == 1;
    }

    /// <summary>
    /// A line's chain begins: the bound in force now, the line as the journal reads it, and its record — a record left
    /// running at another line superseded, one left running at this line taken up, otherwise a new one — with the kind's
    /// oldest ended records beyond the bound deleted.
    /// </summary>
    private async Task<Chain> StartAsync(
        JasonDbContext db,
        PluginSnapshot snapshot,
        IReadOnlyList<string> members,
        SubscriptionCursor cursor,
        int lineId,
        CancellationToken ct)
    {
        var bound = settings.Current.EnvelopeBytes;
        var keep = settings.Current.RunRecordsPerKind;
        var kind = cursor.Kind;
        var entry = await db.Journal.AsNoTracking().SingleAsync(line => line.Id == lineId, ct).ConfigureAwait(false);
        var dto = (await JournalEntries.ToDtosAsync(db, [entry], ct).ConfigureAwait(false))[0];
        var line = JsonSerializer.SerializeToNode(dto, JasonJson.Options)!.AsObject();
        var now = clock.GetUtcNow().UtcDateTime;

        int runId;
        await using (var tx = await db.Database.BeginTransactionAsync(ct).ConfigureAwait(false))
        {
            var left = await db.SubscriptionRuns
                .SingleOrDefaultAsync(run => run.Kind == kind && run.Status == SubscriptionRunStatus.Running, ct)
                .ConfigureAwait(false);
            if (left is not null && left.LineId != lineId)
            {
                left.Status = SubscriptionRunStatus.Superseded;
                left.EndedAt = now;
                await db.SaveChangesAsync(ct).ConfigureAwait(false);
                left = null;
            }

            if (left is not null)
            {
                left.RetryAt = null;
                await db.SaveChangesAsync(ct).ConfigureAwait(false);
            }
            else
            {
                left = new SubscriptionRun
                {
                    PublicId = PublicId.New("sbr"),
                    Kind = kind,
                    LineId = lineId,
                    LinePublicId = entry.PublicId,
                    Status = SubscriptionRunStatus.Running,
                    StartedAt = now,
                };
                db.SubscriptionRuns.Add(left);
                await db.SaveChangesAsync(ct).ConfigureAwait(false);
            }

            runId = left.Id;
            var cutoff = await db.SubscriptionRuns
                .Where(run => run.Kind == kind && run.Status != SubscriptionRunStatus.Running)
                .OrderByDescending(run => run.Id)
                .Skip(keep)
                .Select(run => (int?)run.Id)
                .FirstOrDefaultAsync(ct)
                .ConfigureAwait(false);
            if (cutoff is { } oldest)
            {
                await db.SubscriptionRuns
                    .Where(run => run.Kind == kind && run.Status != SubscriptionRunStatus.Running && run.Id <= oldest)
                    .ExecuteDeleteAsync(ct)
                    .ConfigureAwait(false);
            }

            await tx.CommitAsync(ct).ConfigureAwait(false);
        }

        Started(logger, kind, entry.PublicId, null);
        return new Chain(snapshot, members, runId, lineId, entry.PublicId, line, cursor.Id, kind, cursor.Revision, bound);
    }

    /// <summary>Runs the chain from the handler given, handed the envelope as it stood before that handler.</summary>
    private async Task<ChainEnd> ChainAsync(Chain chain, int from, JsonObject envelope, CancellationToken ct)
    {
        for (var index = from; index < chain.Members.Count; index++)
        {
            var pluginId = chain.Members[index];
            await using var scope = scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<JasonDbContext>();

            // The entry is written before the child exists: an invocation that may have acted is never unrecorded.
            var handlers = await HandlersAsync(db, chain.RunId, ct).ConfigureAwait(false);
            if (handlers is null)
            {
                return ChainEnd.Ended(superseded: true);
            }

            var attempt = Entries(handlers, pluginId).Count() + 1;
            handlers.Add(new JsonObject
            {
                ["plugin_id"] = pluginId,
                ["started_at"] = clock.GetUtcNow().UtcDateTime.ToString(Stamp, CultureInfo.InvariantCulture),
            });
            if (!await WriteHandlersAsync(db, chain.RunId, handlers, retryAt: null, ct).ConfigureAwait(false))
            {
                return ChainEnd.Ended(superseded: true);
            }

            var request = new PluginInvocationRequest(
                pluginId,
                chain.Kind,
                new JsonObject
                {
                    ["line"] = chain.Line.DeepClone(),
                    ["envelope"] = envelope.DeepClone(),
                    ["attempt"] = attempt,
                },
                null,
                chain.LinePublicId,
                Timeout: TimeSpan.FromMilliseconds(_contract.TimeoutMs),
                Pinned: new PinnedPlugin(chain.Snapshot.Find(pluginId)!, chain.Snapshot.Id),
                Purpose: InvocationPurpose.Subscription,
                Door: new DoorRequest(chain.LinePublicId));
            var started = clock.GetTimestamp();
            var result = await scope.ServiceProvider.GetRequiredService<ISubscriptionInvoker>().InvokeAsync(request, ct).ConfigureAwait(false);

            // A stop writes nothing more: the entry stays without an outcome and the next start takes the line up again.
            ct.ThrowIfCancellationRequested();
            var duration = (long)clock.GetElapsedTime(started).TotalMilliseconds;
            var (failure, code, answered) = Read(result.Outcome, chain.Bound);
            var outcome = failure is { } failed ? Word(failed) : "succeeded";

            var done = (JsonObject)handlers[^1]!;
            done["outcome"] = outcome;
            if (code is not null)
            {
                done["code"] = code;
            }

            done["duration_ms"] = duration;
            Ended(logger, pluginId, chain.Kind, outcome, code ?? "none", duration, null);

            if (failure is null)
            {
                if (!await WriteHandlersAsync(db, chain.RunId, handlers, retryAt: null, ct).ConfigureAwait(false))
                {
                    return ChainEnd.Ended(superseded: true);
                }

                envelope = answered!;
                continue;
            }

            if (failure == FailureClass.Transient)
            {
                var spent = Entries(handlers, pluginId).Count(entry => entry["outcome"]?.GetValue<string>() == "transient");
                var current = settings.Current;
                if (spent <= current.TransientRetries)
                {
                    var seconds = Math.Min(MaximumBackoffSeconds, current.RetryBackoffSeconds * Math.Pow(2, spent - 1));
                    var due = clock.GetUtcNow().AddSeconds(seconds);
                    if (!await WriteHandlersAsync(db, chain.RunId, handlers, due.UtcDateTime, ct).ConfigureAwait(false))
                    {
                        return ChainEnd.Ended(superseded: true);
                    }

                    return ChainEnd.Waiting(new Paused(chain, index, envelope, due));
                }
            }

            if (!await WriteHandlersAsync(db, chain.RunId, handlers, retryAt: null, ct).ConfigureAwait(false))
            {
                return ChainEnd.Ended(superseded: true);
            }

            var final = new Failure(pluginId, Word(failure.Value), code!, attempt);
            return ChainEnd.Ended(!await EndAsync(db, scope.ServiceProvider.GetRequiredService<JournalWriter>(), chain, null, final, ct).ConfigureAwait(false));
        }

        await using (var scope = scopes.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<JasonDbContext>();
            return ChainEnd.Ended(!await EndAsync(db, scope.ServiceProvider.GetRequiredService<JournalWriter>(), chain, envelope, null, ct).ConfigureAwait(false));
        }
    }

    /// <summary>
    /// What a handler's answer comes to: no failure and the envelope it returned, or the failure's class and code. An
    /// answer outside the contract and an envelope over the bound are the runtime's own validation codes, charged to the
    /// handler that returned them.
    /// </summary>
    private (FailureClass? Failure, string? Code, JsonObject? Envelope) Read(InvocationOutcome outcome, int bound)
    {
        switch (outcome)
        {
            case InvocationOutcome.Succeeded succeeded:
                if (_contract.CheckResult(succeeded.Result).Count > 0 || SubscriptionContract.CheckExternalIds(succeeded.ExternalIds).Count > 0
                    || succeeded.Result?["envelope"] is not JsonObject returned)
                {
                    return (FailureClass.Validation, SubscriptionContract.AnswerInvalid, null);
                }

                if (Encoding.UTF8.GetByteCount(returned.ToJsonString(JasonJson.Options)) > bound)
                {
                    return (FailureClass.Validation, SubscriptionContract.EnvelopeTooLarge, null);
                }

                return (null, null, (JsonObject)returned.DeepClone());
            case InvocationOutcome.Failed failed:
                return (failed.Error.Class, failed.Error.Code, null);
            case InvocationOutcome.ProtocolFailure protocol:
                return (OutcomeClassification.ClassOf(protocol.Code), protocol.Code, null);
            default:
                throw new InvalidOperationException($"An invocation outcome of an unknown shape: {outcome.GetType().Name}.");
        }
    }

    /// <summary>
    /// A line's chain ends in one transaction: the position moves past the line where it still stands as the line's start
    /// read it, and the record ends succeeded or failed — or superseded where the position moved, saying no failure. A
    /// record another writer already ended ends nothing, and the turn ends.
    /// </summary>
    /// <returns>Whether the position moved.</returns>
    private async Task<bool> EndAsync(JasonDbContext db, JournalWriter journal, Chain chain, JsonObject? envelope, Failure? failure, CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        await using var tx = await db.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
        var moved = await db.SubscriptionCursors
            .Where(row => row.Id == chain.CursorId && row.Kind == chain.Kind && row.Revision == chain.Revision)
            .ExecuteUpdateAsync(set => set
                .SetProperty(row => row.AfterLine, chain.LineId)
                .SetProperty(row => row.Revision, row => row.Revision + 1)
                .SetProperty(row => row.UpdatedAt, now), ct)
            .ConfigureAwait(false) == 1;

        var status = !moved ? SubscriptionRunStatus.Superseded : failure is null ? SubscriptionRunStatus.Succeeded : SubscriptionRunStatus.Failed;
        var kept = moved ? envelope : null;
        var recorded = moved ? failure : null;
        var (failedPlugin, failedClass, failedCode) = (recorded?.PluginId, recorded?.Class, recorded?.Code);
        var ended = await db.SubscriptionRuns
            .Where(run => run.Id == chain.RunId && run.Status == SubscriptionRunStatus.Running)
            .ExecuteUpdateAsync(set => set
                .SetProperty(run => run.Status, status)
                .SetProperty(run => run.EndedAt, now)
                .SetProperty(run => run.RetryAt, (DateTime?)null)
                .SetProperty(run => run.Envelope, kept)
                .SetProperty(run => run.FailedPluginId, failedPlugin)
                .SetProperty(run => run.FailureClass, failedClass)
                .SetProperty(run => run.FailureCode, failedCode), ct)
            .ConfigureAwait(false);
        if (ended == 0)
        {
            await tx.RollbackAsync(ct).ConfigureAwait(false);
            return false;
        }

        if (recorded is not null)
        {
            journal.Append(
                db,
                Actors.Dispatcher,
                JournalKinds.SubscriptionFailed,
                campaign: null,
                key: chain.Kind,
                updated: new JsonObject
                {
                    ["kind"] = chain.Kind,
                    ["line"] = chain.LinePublicId,
                    ["plugin_id"] = recorded.PluginId,
                    ["class"] = recorded.Class,
                    ["code"] = recorded.Code,
                    ["attempts"] = recorded.Attempts,
                });
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
        }

        await tx.CommitAsync(ct).ConfigureAwait(false);
        if (recorded is not null)
        {
            ChainFailed(logger, chain.Kind, chain.LinePublicId, recorded.PluginId, recorded.Class, recorded.Code, null);
        }

        return moved;
    }

    /// <summary>The record's handler entries while it runs; null once another writer has ended it.</summary>
    private static async Task<JsonArray?> HandlersAsync(JasonDbContext db, int runId, CancellationToken ct)
    {
        var run = await db.SubscriptionRuns
            .AsNoTracking()
            .Where(row => row.Id == runId && row.Status == SubscriptionRunStatus.Running)
            .Select(row => row.Handlers)
            .SingleOrDefaultAsync(ct)
            .ConfigureAwait(false);
        return run is null ? null : (JsonArray)run.DeepClone();
    }

    /// <summary>One guarded write of the entries, which also sets or clears the retry time: false once the record has ended.</summary>
    private static async Task<bool> WriteHandlersAsync(JasonDbContext db, int runId, JsonArray handlers, DateTime? retryAt, CancellationToken ct) =>
        await db.SubscriptionRuns
            .Where(row => row.Id == runId && row.Status == SubscriptionRunStatus.Running)
            .ExecuteUpdateAsync(set => set
                .SetProperty(row => row.Handlers, handlers)
                .SetProperty(row => row.RetryAt, retryAt), ct)
            .ConfigureAwait(false) == 1;

    private static IEnumerable<JsonObject> Entries(JsonArray handlers, string pluginId) =>
        handlers.OfType<JsonObject>().Where(entry => entry["plugin_id"]?.GetValue<string>() == pluginId);

    private static string Word(FailureClass failure) => failure.ToString().ToLowerInvariant();

    /// <summary>
    /// What a start finds left running by the runtime before, once per runner: a record at its kind's next line is taken
    /// up and its retry time cleared, for the kind's next turn to run from the first handler; any other is superseded. A
    /// settle that fails is said as the turn's failure and tried again at the next turn.
    /// </summary>
    private async Task SettleAsync(CancellationToken ct)
    {
        await _settle.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_settled)
            {
                return;
            }

            await using var scope = scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<JasonDbContext>();
            var now = clock.GetUtcNow().UtcDateTime;
            var left = await db.SubscriptionRuns.Where(run => run.Status == SubscriptionRunStatus.Running).ToListAsync(ct).ConfigureAwait(false);
            foreach (var run in left)
            {
                var next = await db.SubscriptionCursors
                    .Where(cursor => cursor.Kind == run.Kind)
                    .Select(cursor => db.Journal.Where(line => line.Kind == cursor.Kind && line.Id > cursor.AfterLine).Min(line => (int?)line.Id))
                    .ToListAsync(ct)
                    .ConfigureAwait(false);
                if (next is [{ } line] && line == run.LineId)
                {
                    run.RetryAt = null;
                }
                else
                {
                    run.Status = SubscriptionRunStatus.Superseded;
                    run.EndedAt = now;
                }
            }

            await db.SaveChangesAsync(ct).ConfigureAwait(false);
            _settled = true;
        }
        finally
        {
            _settle.Release();
        }
    }

    private sealed class Flight(string kind)
    {
        public string Kind { get; } = kind;

        public FlightState State { get; set; }

        public Paused? Paused { get; set; }
    }

    /// <summary>
    /// One line's chain as its start fixed it: the load it runs under, its members, its record, the line, and the
    /// position and bound read at its start.
    /// </summary>
    private sealed record Chain(
        PluginSnapshot Snapshot,
        IReadOnlyList<string> Members,
        int RunId,
        int LineId,
        string LinePublicId,
        JsonObject Line,
        int CursorId,
        string Kind,
        long Revision,
        int Bound);

    /// <summary>A chain waiting out a backoff: the handler to run again, the envelope as it stood before it, and when.</summary>
    private sealed record Paused(Chain Chain, int Handler, JsonObject Envelope, DateTimeOffset Due);

    private sealed record Failure(string PluginId, string Class, string Code, int Attempts);

    private sealed record ChainEnd(Paused? Paused, bool Superseded)
    {
        public static ChainEnd Waiting(Paused paused) => new(paused, false);

        public static ChainEnd Ended(bool superseded) => new(null, superseded);
    }
}

/// <summary>How one turn of a kind ended: the lines whose chains it ran, the lines it passed over, and why it stopped.</summary>
public sealed record SubscriptionTurn(string Kind, int Lines, int Skipped, string Ended)
{
    /// <summary>No line of the kind is left after its position.</summary>
    public const string Head = "head";

    /// <summary>The turn took <see cref="SubscriptionRunner.LinesPerTurn"/> lines; the next scan offers the kind again.</summary>
    public const string Limit = "limit";

    /// <summary>A handler failed in a way that may pass and waits out its backoff, holding no child.</summary>
    public const string Backoff = "backoff";

    /// <summary>The kind's position moved under the chain, or its record was ended by another writer.</summary>
    public const string Superseded = "superseded";

    /// <summary>A member of the kind's chain cannot run on this machine: the kind waits at its line.</summary>
    public const string Waiting = "waiting";

    /// <summary>The kind has no position: no plugin in force subscribes to it.</summary>
    public const string Gone = "gone";
}
