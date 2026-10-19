using System.Collections.Concurrent;
using System.Text.Json.Nodes;
using System.Threading.Channels;
using Jason.Contracts.Api;
using Jason.Contracts.Events;
using Jason.Contracts.Ids;
using Jason.Contracts.Operations;
using Jason.Contracts.Plugins;
using Jason.Runtime.Domain;
using Jason.Runtime.Execution;
using Jason.Runtime.Journal;
using Jason.Runtime.Logging;
using Jason.Runtime.Persistence;
using Jason.Runtime.Plugins.Invocation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Jason.Runtime.Events;

/// <summary>How one read of an account ended: what it read and stored, and the failure it ended with, if any.</summary>
public sealed record EventReadReport(string Outcome, int Pages, int Stored, int Duplicates, int Said, string? Code, FailureClass? Class, bool More)
{
    public const string Succeeded = "succeeded";

    public const string Failed = "failed";

    /// <summary>Another writer moved the source's cursor while the read was under way: nothing of this read's page was kept.</summary>
    public const string Moved = "moved";
}

/// <summary>
/// Reads one account's events at a time, off the scan: a slow provider delays no scan, and nothing a read does holds the
/// writer while another step thinks. The scan hands over at most one read, only when this reader is idle — a queue of
/// one — so at most one child of the plugin host runs for events, beside the handler pool's and the notifier's, and never
/// in a handler slot or as a work item.
/// </summary>
/// <remarks>
/// <para>
/// A read takes up to <see cref="EventSchedule.PagesPerRead"/> pages, each one child, each held to the events contract
/// and stored in its own transaction by the store, through a context of its own: the cursor moves only with the page it
/// ends. A page the contract refuses, or one it accepted that cannot be read, stores nothing and leaves the cursor where it
/// was; the read ends in failure there, and what earlier pages stored stays.
/// </para>
/// <para>
/// How a read ended is a table, not a judgement. Transient and ambiguous ends — a timeout, a crash, an answer outside the
/// contract — count a failure and the next read waits the doubled cadence; a permanent or a validation failure waits a
/// day; a cursor the plugin no longer knows is cleared, and the next read starts by date at once. A failure whose code the
/// contract does not declare, or whose class disagrees with the one it declares, is an answer outside the contract. A
/// runtime that stops while a read runs ends the child and writes nothing: its successor reads again from the cursor.
/// </para>
/// <para>
/// Nothing here reads meaning, and nothing an event holds, a cursor or a binding reaches the log or the journal: the
/// source's id, codes, classes and counts do.
/// </para>
/// </remarks>
public sealed class EventReader : BackgroundService
{
    /// <summary>The contract a read is.</summary>
    public const string Operation = "events.read";

    /// <summary>An answer outside the contract: a page the schema refused or the reader could not read, an identifier beside it, or a failure the contract does not declare.</summary>
    public const string ResultInvalid = "result_invalid";

    /// <summary>An input the runtime's own half of the contract refused before a child existed.</summary>
    public const string InputInvalid = "input_invalid";

    /// <summary>A page that was read and could not be written: nothing of it was kept.</summary>
    public const string NotStored = "events_not_stored";

    /// <summary>The plugin no longer knows the cursor it was handed.</summary>
    public const string CursorUnknown = "events_cursor_unknown";

    private static readonly Action<ILogger, string, int, int, int, Exception?> Read = LoggerMessage.Define<string, int, int, int>(
        LogLevel.Information,
        new EventId(1, nameof(Read)),
        "Events read of source {SourceId}: {Pages} pages, {Stored} events stored, {Duplicates} already held");

    private static readonly Action<ILogger, string, string, string, Exception?> ReadFailed = LoggerMessage.Define<string, string, string>(
        LogLevel.Warning,
        new EventId(2, nameof(ReadFailed)),
        "Events read of source {SourceId} failed ({Code}, {Class})");

    private static readonly Action<ILogger, string, Exception?> Moved = LoggerMessage.Define<string>(
        LogLevel.Information,
        new EventId(3, nameof(Moved)),
        "Events read of source {SourceId} stopped: another writer moved its cursor");

    private static readonly Action<ILogger, string, Exception?> Gave = LoggerMessage.Define<string>(
        LogLevel.Warning,
        new EventId(4, nameof(Gave)),
        "Events read of source {SourceId} could not finish; the next scan hands out the next");

    // The same event, for a read that gave out reading a stored value: what kind and where, and no exception, because what
    // a parser quotes is what the value held.
    private static readonly Action<ILogger, string, string, Exception?> GaveToParse = LoggerMessage.Define<string, string>(
        LogLevel.Warning,
        new EventId(4, nameof(Gave)),
        "Events read of source {SourceId} could not finish; the next scan hands out the next: {Failure}");

    private readonly Channel<EventRead> _queue = Channel.CreateBounded<EventRead>(1);
    private readonly ConcurrentDictionary<int, bool> _read = new();
    private readonly IServiceScopeFactory _scopes;
    private readonly TimeProvider _clock;
    private readonly ILogger<EventReader> _logger;
    private readonly EventsContract _contract;
    private int _busy;

    /// <param name="contract">The events contract; only a test hands in another, to reach a page the published one would refuse first.</param>
    public EventReader(IServiceScopeFactory scopes, TimeProvider clock, ILogger<EventReader> logger, EventsContract? contract = null)
    {
        _scopes = scopes;
        _clock = clock;
        _logger = logger;
        _contract = contract ?? EventsCatalog.Find(Operation) ?? throw new InvalidOperationException("This build publishes no events.read contract.");
    }

    /// <summary>Whether no read is handed over or running: the one time the scan may hand over the next.</summary>
    public bool Idle => Volatile.Read(ref _busy) == 0;

    /// <summary>Whether this process has started a read of the source: a start reads every account it has not read yet at once.</summary>
    public bool HasRead(int sourceId) => _read.ContainsKey(sourceId);

    /// <summary>Hands over one read; false, and nothing handed over, while another is handed over or running.</summary>
    public bool TryHandOver(EventRead read)
    {
        ArgumentNullException.ThrowIfNull(read);
        if (Interlocked.CompareExchange(ref _busy, 1, 0) != 0)
        {
            return false;
        }

        if (!_queue.Writer.TryWrite(read))
        {
            Volatile.Write(ref _busy, 0);
            return false;
        }

        return true;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await foreach (var read in _queue.Reader.ReadAllAsync(stoppingToken).ConfigureAwait(false))
            {
                try
                {
                    await ReadAsync(read, stoppingToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    // A stopping runtime writes nothing: its successor reads again from the cursor.
                    return;
                }
                catch (Exception ex)
                {
                    if (ParseFailure.Is(ex))
                    {
                        GaveToParse(_logger, read.SourcePublicId, ParseFailure.Describe(ex), null);
                    }
                    else
                    {
                        Gave(_logger, read.SourcePublicId, ex);
                    }
                }
                finally
                {
                    Volatile.Write(ref _busy, 0);
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Stopped while waiting for the next one: nothing was being read.
        }
    }

    /// <summary>
    /// The read handed over, read now on the caller's flow rather than by the service's own loop: null when nothing was
    /// handed over. A test's way to read one at a moment it chooses; the runtime reads them in <see cref="ExecuteAsync"/>.
    /// </summary>
    internal async Task<EventReadReport?> ReadNextAsync(CancellationToken ct)
    {
        if (!_queue.Reader.TryRead(out var read))
        {
            return null;
        }

        try
        {
            return await ReadAsync(read, ct).ConfigureAwait(false);
        }
        finally
        {
            Volatile.Write(ref _busy, 0);
        }
    }

    /// <summary>One read, page by page, to its end: what the scan handed over, and what the source's row and the journal keep of it.</summary>
    internal async Task<EventReadReport> ReadAsync(EventRead read, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(read);
        var readId = PublicId.New("evr");
        var started = _clock.GetUtcNow().UtcDateTime;
        if (read.Backfill is { } backfill)
        {
            // A span asked for again is not the account's own reading: what says when the account is read next, and
            // whether it is healthy, stays as that reading left it.
            await OnBackfillAsync(backfill, row => row.SetProperty(span => span.AttemptedAt, started), ct).ConfigureAwait(false);
        }
        else
        {
            _read.TryAdd(read.SourceId, true);
            await OnRowAsync(read, row => row.SetProperty(source => source.ReadStartedAt, started).SetProperty(source => source.More, false), ct).ConfigureAwait(false);
        }

        var held = read.HeldCursor;
        var input = read.Plan.Input;
        int pages = 0, stored = 0, duplicates = 0, said = 0;
        EventReadReport? end = null;
        while (end is null)
        {
            if (pages == EventSchedule.PagesPerRead)
            {
                end = new EventReadReport(EventReadReport.Succeeded, pages, stored, duplicates, said, null, null, More: true);
                break;
            }

            if (Refused(_contract.CheckInput(input)) is { } refused)
            {
                end = Failure(pages, stored, duplicates, said, refused.Code, refused.Class);
                break;
            }

            var result = await InvokeAsync(read, input, readId, ct).ConfigureAwait(false);
            pages++;
            switch (result.Outcome)
            {
                case InvocationOutcome.Succeeded succeeded:
                    var page = Page(succeeded, out var invalid);
                    if (page is null)
                    {
                        end = Failure(pages, stored, duplicates, said, invalid!.Value.Code, invalid.Value.Class);
                        break;
                    }

                    EventStoreResult landed;
                    try
                    {
                        landed = await StoreAsync(read, held, page, readId, pages == 1 ? read.Plan.Gap : null, ct).ConfigureAwait(false);
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        end = Failure(pages, stored, duplicates, said, NotStored, FailureClass.Ambiguous);
                        break;
                    }

                    if (landed.Moved)
                    {
                        Moved(_logger, read.SourcePublicId, null);
                        end = new EventReadReport(EventReadReport.Moved, pages, stored, duplicates, said, null, null, More: true);
                        break;
                    }

                    stored += landed.Stored;
                    duplicates += landed.Duplicates;
                    said += landed.Said;

                    // More follow now: the next page is asked for from the cursor this one returned. A page that says more
                    // follow and hands back the cursor it was given would be asked for again for ever, so it ends the read.
                    var next = page.HasMore && !string.Equals(page.Cursor, held, StringComparison.Ordinal);
                    held = page.Cursor;
                    if (next)
                    {
                        input = EventSchedule.Next(page.Cursor);
                    }
                    else
                    {
                        end = new EventReadReport(EventReadReport.Succeeded, pages, stored, duplicates, said, null, null, More: false);
                    }

                    break;

                case InvocationOutcome.Failed failed:
                    var (code, failureClass) = Declared(failed.Error);
                    if (code == CursorUnknown && input["cursor"] is not null)
                    {
                        // The plugin no longer knows the cursor: it is cleared where it still stands, and the next read starts
                        // by date at once, from a day before the cursor was stored.
                        await ForgetAsync(read, held, ct).ConfigureAwait(false);
                        end = Failure(pages, stored, duplicates, said, code, failureClass) with { More = true };
                    }
                    else
                    {
                        end = Failure(pages, stored, duplicates, said, code, failureClass);
                    }

                    break;

                case InvocationOutcome.ProtocolFailure protocol:
                    ct.ThrowIfCancellationRequested();
                    end = Failure(pages, stored, duplicates, said, protocol.Code, OutcomeClassification.ClassOf(protocol.Code));
                    break;
            }
        }

        await EndAsync(read, readId, started, end, ct).ConfigureAwait(false);
        if (end.Outcome == EventReadReport.Failed)
        {
            ReadFailed(_logger, read.SourcePublicId, end.Code!, Word(end.Class), null);
        }
        else if (end.Stored > 0 || end.Duplicates > 0)
        {
            Read(_logger, read.SourcePublicId, end.Pages, end.Stored, end.Duplicates, null);
        }

        return end;
    }

    private async Task<PluginInvocationResult> InvokeAsync(EventRead read, JsonObject input, string readId, CancellationToken ct)
    {
        var request = new PluginInvocationRequest(
            read.PluginId,
            _contract.Id,
            (JsonObject)input.DeepClone(),
            read.Binding?.DeepClone() as JsonObject,
            readId,
            Timeout: TimeSpan.FromMilliseconds(_contract.TimeoutMs),
            Pinned: read.Plugin,
            Purpose: InvocationPurpose.Events);

        await using var scope = _scopes.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IEventsInvoker>().InvokeAsync(request, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// The page an answer is, held to the contract: or null with why not — identifiers beside it (a read pins nothing),
    /// a result the schema refuses, or one it accepted that the reader cannot read. All three are answers outside the
    /// contract, ambiguous; a schema pattern that could not finish measuring says nothing about the answer, under its own code.
    /// </summary>
    private EventPage? Page(InvocationOutcome.Succeeded succeeded, out (string Code, FailureClass Class)? invalid)
    {
        invalid = null;
        if (EventsContract.CheckExternalIds(succeeded.ExternalIds).Count > 0)
        {
            invalid = (ResultInvalid, FailureClass.Ambiguous);
            return null;
        }

        var page = EventPage.Read(_contract, succeeded.Result, out var problems);
        if (page is null)
        {
            invalid = (SchemaValidator.Unmeasured(problems) ? AttemptErrors.ValidationTimedOut : ResultInvalid, FailureClass.Ambiguous);
        }

        return page;
    }

    /// <summary>
    /// A failure as the contract declares it: its code and its class, when the contract names that code with that class
    /// and nothing beside it names an identifier; otherwise an answer outside the contract.
    /// </summary>
    private (string Code, FailureClass Class) Declared(OutcomeError error)
    {
        var declared = _contract.FailureCodes.FirstOrDefault(code => string.Equals(code.Code, error.Code, StringComparison.Ordinal));
        return declared is not null && declared.Class == error.Class && EventsContract.CheckExternalIds(error.ExternalIds).Count == 0
            ? (error.Code, error.Class)
            : (ResultInvalid, FailureClass.Ambiguous);
    }

    /// <summary>An input the runtime's own half of the contract refuses is final; one a pattern could not finish measuring is not refused, and is tried again.</summary>
    private static (string Code, FailureClass Class)? Refused(IReadOnlyList<SchemaProblem> problems) =>
        SchemaValidator.Verdicts(problems).Count > 0 ? (InputInvalid, FailureClass.Validation)
        : problems.Count > 0 ? (AttemptErrors.ValidationTimedOut, FailureClass.Transient)
        : null;

    private async Task<EventStoreResult> StoreAsync(EventRead read, string? held, EventPage page, string readId, EventGap? gap, CancellationToken ct)
    {
        // A context of its own for each page: a page that throws leaves nothing tracked behind it for the next.
        await using var scope = _scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<JasonDbContext>();
        var source = new EventSource { Id = read.SourceId, PublicId = read.SourcePublicId, PluginId = read.PluginId, BindingIdentity = read.BindingIdentity };
        var fence = read.Backfill is { } backfill ? EventFence.OfBackfill(backfill.Id, held) : EventFence.OfSource(held, gap);
        return await scope.ServiceProvider.GetRequiredService<EventStore>().StoreAsync(db, source, fence, page, readId, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Clears a cursor the plugin no longer knows, where it still stands: the account's, whose stored moment is kept as
    /// where the next read starts from, or a span's, which is then read again from its start.
    /// </summary>
    private async Task ForgetAsync(EventRead read, string? held, CancellationToken ct)
    {
        await using var scope = _scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<JasonDbContext>();
        if (read.Backfill is { } backfill)
        {
            await db.EventBackfills
                .Where(span => span.Id == backfill.Id && span.Cursor == held)
                .ExecuteUpdateAsync(set => set.SetProperty(span => span.Cursor, (string?)null), ct)
                .ConfigureAwait(false);
            return;
        }

        await db.EventSources
            .Where(source => source.Id == read.SourceId && source.Cursor == held)
            .ExecuteUpdateAsync(set => set.SetProperty(source => source.Cursor, (string?)null), ct)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// What the source's row keeps of the read, and its <c>events_read</c> line, in one transaction: a read that stored
    /// something, failed, or read by date is journaled; a read from a cursor that found nothing is not, or a quiet account
    /// would write a line every cadence.
    /// </summary>
    private async Task EndAsync(EventRead read, string readId, DateTime started, EventReadReport end, CancellationToken ct)
    {
        var ended = _clock.GetUtcNow().UtcDateTime;
        await using var scope = _scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<JasonDbContext>();
        await using var transaction = await db.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
        var row = db.EventSources.Where(source => source.Id == read.SourceId);
        EventBackfillStatus? span = null;
        if (read.Backfill is { } backfill)
        {
            span = await EndBackfillAsync(db, backfill, ended, end, ct).ConfigureAwait(false);
        }
        else if (end.Outcome == EventReadReport.Failed)
        {
            var word = Word(end.Class);
            await row.ExecuteUpdateAsync(
                set => set
                    .SetProperty(source => source.ReadEndedAt, ended)
                    .SetProperty(source => source.Failures, source => source.Failures + 1)
                    .SetProperty(source => source.LastCode, end.Code)
                    .SetProperty(source => source.LastClass, word)
                    .SetProperty(source => source.More, end.More),
                ct).ConfigureAwait(false);
        }
        else if (end.Outcome == EventReadReport.Succeeded)
        {
            await row.ExecuteUpdateAsync(
                set => set
                    .SetProperty(source => source.ReadEndedAt, ended)
                    .SetProperty(source => source.SucceededAt, started)
                    .SetProperty(source => source.Failures, 0)
                    .SetProperty(source => source.LastCode, (string?)null)
                    .SetProperty(source => source.LastClass, (string?)null)
                    .SetProperty(source => source.More, end.More),
                ct).ConfigureAwait(false);
        }
        else
        {
            await row.ExecuteUpdateAsync(
                set => set.SetProperty(source => source.ReadEndedAt, ended).SetProperty(source => source.More, end.More),
                ct).ConfigureAwait(false);
        }

        if (end.Stored > 0 || end.Outcome != EventReadReport.Succeeded || read.Plan.Mode != EventReadMode.Cursor)
        {
            var line = new JsonObject
            {
                ["read_id"] = readId,
                ["source"] = read.SourcePublicId,
                ["plugin"] = read.PluginId,
                ["binding_identity"] = read.BindingIdentity,
                ["mode"] = read.Plan.ModeWord,
                ["pages"] = end.Pages,
                ["stored"] = end.Stored,
                ["duplicates"] = end.Duplicates,
                ["said"] = end.Said,
                ["outcome"] = end.Outcome,
                ["code"] = end.Code,
                ["class"] = end.Class is { } failure ? Word(failure) : null,
                ["plugin_version"] = read.Plugin.Plugin.Manifest.Version,
                ["plugin_digest"] = read.Plugin.Plugin.Digest,
                ["plugin_snapshot_id"] = read.Plugin.SnapshotId,
                ["routing_snapshot_id"] = read.RoutingSnapshotId,
                ["signaled"] = read.Signalled,
            };
            if (read.Backfill is { } asked && span is { } status)
            {
                line["backfill"] = asked.PublicId;
                line["backfill_status"] = SnakeCaseEnumConverter<EventBackfillStatus>.Format(status);
            }

            scope.ServiceProvider.GetRequiredService<JournalWriter>().Append(db, Actors.Dispatcher, JournalKinds.EventsRead, campaign: null, updated: line);
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
        }

        await transaction.CommitAsync(ct).ConfigureAwait(false);
    }

    /// <summary>
    /// What a span's row keeps of a read of it, and where the span stands after it. Read to its end, it is done. An answer
    /// repeating cannot change ends it failed, with its code and class, so that a span that could never succeed does not
    /// hold its place for ever. A cursor of its own the plugin forgot sends it back to its start, at once. Anything else
    /// that failed counts on the span and doubles its wait. A read that stopped with more, or that another writer
    /// overtook, leaves it waiting where its last page landed.
    /// </summary>
    private static async Task<EventBackfillStatus> EndBackfillAsync(JasonDbContext db, EventBackfillRef backfill, DateTime ended, EventReadReport end, CancellationToken ct)
    {
        var row = db.EventBackfills.Where(span => span.Id == backfill.Id && span.Status == EventBackfillStatus.Pending);

        // Sent back to its start by a cursor the plugin forgot, and nothing new stored since: this read found nothing it
        // had not stored before. Starting again is free only while it does, or a plugin that forgets every cursor would
        // have the span read from its start at every idle scan, for ever.
        var startedAgain = await row.Select(span => span.LastCode).FirstOrDefaultAsync(ct).ConfigureAwait(false) == CursorUnknown;
        var stale = startedAgain && end.Stored == 0;
        if (end.Outcome == EventReadReport.Failed)
        {
            var code = end.Code!;
            var word = Word(end.Class);
            if (end.More)
            {
                // A second forgetting with nothing new since the last start again is a failure that may pass: it doubles
                // the wait and keeps the span's place. The first, or one after something new, starts it again at once.
                await row.ExecuteUpdateAsync(
                    set => set
                        .SetProperty(span => span.Failures, span => stale ? span.Failures + 1 : 0)
                        .SetProperty(span => span.LastCode, code)
                        .SetProperty(span => span.LastClass, word),
                    ct).ConfigureAwait(false);
                return EventBackfillStatus.Pending;
            }

            if (EventSchedule.EndsABackfill(code, end.Class!.Value))
            {
                await row.ExecuteUpdateAsync(
                    set => set
                        .SetProperty(span => span.Status, EventBackfillStatus.Failed)
                        .SetProperty(span => span.EndedAt, ended)
                        .SetProperty(span => span.LastCode, code)
                        .SetProperty(span => span.LastClass, word),
                    ct).ConfigureAwait(false);
                return EventBackfillStatus.Failed;
            }

            await row.ExecuteUpdateAsync(
                set => set
                    .SetProperty(span => span.Failures, span => span.Failures + 1)
                    .SetProperty(span => span.LastCode, code)
                    .SetProperty(span => span.LastClass, word),
                ct).ConfigureAwait(false);
            return EventBackfillStatus.Pending;
        }

        if (end.Outcome == EventReadReport.Succeeded)
        {
            var done = !end.More;
            if (!done && stale)
            {
                // Twenty pages again of what it had stored before it was sent back: it still owes the first new event, so
                // the failures it is counting stay counted.
                return EventBackfillStatus.Pending;
            }

            await row.ExecuteUpdateAsync(
                set => set
                    .SetProperty(span => span.Status, done ? EventBackfillStatus.Done : EventBackfillStatus.Pending)
                    .SetProperty(span => span.EndedAt, done ? ended : (DateTime?)null)
                    .SetProperty(span => span.Failures, 0)
                    .SetProperty(span => span.LastCode, (string?)null)
                    .SetProperty(span => span.LastClass, (string?)null),
                ct).ConfigureAwait(false);
            return done ? EventBackfillStatus.Done : EventBackfillStatus.Pending;
        }

        return EventBackfillStatus.Pending;
    }

    private async Task OnBackfillAsync(
        EventBackfillRef backfill,
        Action<Microsoft.EntityFrameworkCore.Query.UpdateSettersBuilder<EventBackfill>> set,
        CancellationToken ct)
    {
        await using var scope = _scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<JasonDbContext>();
        await db.EventBackfills.Where(span => span.Id == backfill.Id).ExecuteUpdateAsync(set, ct).ConfigureAwait(false);
    }

    private async Task OnRowAsync(
        EventRead read,
        Action<Microsoft.EntityFrameworkCore.Query.UpdateSettersBuilder<EventSource>> set,
        CancellationToken ct)
    {
        await using var scope = _scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<JasonDbContext>();
        await db.EventSources.Where(source => source.Id == read.SourceId).ExecuteUpdateAsync(set, ct).ConfigureAwait(false);
    }

    private static EventReadReport Failure(int pages, int stored, int duplicates, int said, string code, FailureClass failureClass) =>
        new(EventReadReport.Failed, pages, stored, duplicates, said, code, failureClass, More: false);

    private static string Word(FailureClass? failure) => failure?.ToString().ToLowerInvariant() ?? "unknown";
}
