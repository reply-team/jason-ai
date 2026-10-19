using System.Globalization;
using System.Text.Json.Nodes;
using Jason.Contracts.Api;
using Jason.Contracts.Ids;
using Jason.Runtime.Configuration;
using Jason.Runtime.Contacts;
using Jason.Runtime.Domain;
using Jason.Runtime.Journal;
using Jason.Runtime.Persistence;
using Jason.Runtime.Plugins.Registry;
using Jason.Runtime.Routing;
using Microsoft.EntityFrameworkCore;

namespace Jason.Runtime.Events;

/// <summary>
/// The record of the accounts whose events are read, and a person's request to read a span of one again. Nothing here
/// reads an account or invokes a plugin: the scan hands every read over, and what a request asks for is read when every
/// account is current.
/// </summary>
/// <remarks>
/// A source is answered as its row and the active snapshots say, never with its cursor: whether it holds one, and when
/// it was stored, is all a reader of the record needs and all the record says. Whether it is healthy is the inbound read's
/// own question, asked here of the same function with the same settings.
/// </remarks>
public sealed class EventsService(
    JasonDbContext db,
    JournalWriter journal,
    RouteRegistry routes,
    PluginRegistry plugins,
    EventReader reader,
    LiveSettings<DispatcherOptions> dispatcher,
    LiveSettings<InboundOptions> inbound,
    TimeProvider clock)
{
    /// <summary>How far back a span may begin: a year and a day.</summary>
    public const int BackfillDays = 366;

    /// <summary>How many spans of one account may wait at once.</summary>
    public const int MaxPending = 8;

    /// <summary>The longest reason a request keeps, as the journal keeps a reason.</summary>
    public const int MaxReasonLength = 2000;

    /// <summary>How many of an account's spans its record shows: every one still waiting is among them, since an account's spans are read oldest first.</summary>
    public const int RecentBackfills = 10;

    public async Task<Page<EventSourceDto>> SourcesAsync(EventsSourcesRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        var limit = Paging.ResolveLimit(request.Limit);
        var after = Paging.DecodeCursor(request.Cursor);

        var query = db.EventSources.AsNoTracking();
        if (after is not null)
        {
            query = query.Where(source => string.Compare(source.PublicId, after) > 0);
        }

        var fetched = await query.OrderBy(source => source.PublicId).Take(limit + 1).ToListAsync(ct).ConfigureAwait(false);
        var bound = EventAccounts.Bound(routes.Snapshot, plugins.Snapshot).ToDictionary(account => (account.PluginId, account.BindingIdentity));
        var now = clock.GetUtcNow().UtcDateTime;
        var records = new Dictionary<int, (EventGapDto? Gap, IReadOnlyList<EventBackfillDto> Backfills)>();
        foreach (var source in fetched.Take(limit))
        {
            records[source.Id] = (await LatestGapAsync(source, ct).ConfigureAwait(false), await RecentAsync(source, ct).ConfigureAwait(false));
        }

        return Paging.ToPage(fetched, limit, source => source.PublicId, source => ToDto(source, bound.GetValueOrDefault((source.PluginId, source.BindingIdentity)), records[source.Id], now));
    }

    /// <summary>
    /// Asks for a span of an account to be read again: every event at or after <c>from</c> and before <c>to</c>, to the
    /// millisecond. The same span asked for again while it waits is the one already waiting, said once.
    /// </summary>
    public async Task<EventBackfillDto> BackfillAsync(EventsBackfillRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        var actor = Actors.Resolve(request.Actor);

        // Now as the runtime keeps every moment, to the millisecond, as the span's ends are: an edge a person can name is
        // an edge the bounds admit, whatever the clock holds below the millisecond.
        var now = Millisecond(clock.GetUtcNow());
        var errors = new ValidationErrors();
        if (string.IsNullOrWhiteSpace(request.Source))
        {
            errors.Add("source", "required", "source is required: the id of an account, as jason events sources lists it.");
        }

        if (request.From is null)
        {
            errors.Add("from", "required", "from is required: the moment the span begins.");
        }

        if (request.To is null)
        {
            errors.Add("to", "required", "to is required: the moment the span ends.");
        }

        var from = request.From is { } begins ? Millisecond(begins) : default;
        var to = request.To is { } ends ? Millisecond(ends) : default;
        if (request.From is not null && request.To is not null)
        {
            if (to <= from)
            {
                errors.Add("to", "not_after_from", "to must be later than from.");
            }

            if (to > now)
            {
                errors.Add("to", "in_the_future", "to cannot be later than now: nothing has happened there to read.");
            }

            if (from < now.AddDays(-BackfillDays))
            {
                errors.Add("from", "too_old", string.Create(CultureInfo.InvariantCulture, $"from can be at most {BackfillDays} days back."));
            }
        }

        var reason = string.IsNullOrWhiteSpace(request.Reason) ? null : request.Reason;
        if (reason is not null && reason.Length > MaxReasonLength)
        {
            errors.Add("reason", "too_long", string.Create(CultureInfo.InvariantCulture, $"reason must be at most {MaxReasonLength} characters."));
        }

        errors.ThrowIfAny();

        var source = await db.EventSources.AsNoTracking().FirstOrDefaultAsync(row => row.PublicId == request.Source, ct).ConfigureAwait(false)
            ?? throw DomainErrors.EventSourceNotFound(request.Source!);
        if (!IsBound(source))
        {
            throw DomainErrors.EventSourceUnbound(source.PublicId);
        }

        // Counted and written under the writer's lock, so two requests cannot both find room for the eighth, and the
        // database's own index says a span waits once.
        await using var transaction = await db.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
        var waiting = await db.EventBackfills
            .AsNoTracking()
            .Where(backfill => backfill.SourceId == source.Id && backfill.Status == EventBackfillStatus.Pending)
            .ToListAsync(ct)
            .ConfigureAwait(false);
        if (waiting.FirstOrDefault(backfill => backfill.From == from && backfill.To == to) is { } same)
        {
            return ToDto(same, source.PublicId);
        }

        if (waiting.Count >= MaxPending)
        {
            throw DomainErrors.EventBackfillLimit(source.PublicId, MaxPending);
        }

        var asked = new EventBackfill
        {
            PublicId = PublicId.New("evb"),
            SourceId = source.Id,
            From = from,
            To = to,
            Status = EventBackfillStatus.Pending,
            RequestedAt = now,
            ActorType = actor.Type,
            ActorId = actor.Id,
            Reason = reason,
        };
        db.EventBackfills.Add(asked);
        journal.Append(
            db,
            actor,
            JournalKinds.EventsBackfillRequested,
            campaign: null,
            key: asked.PublicId,
            updated: new JsonObject
            {
                ["backfill"] = asked.PublicId,
                ["source"] = source.PublicId,
                ["plugin"] = source.PluginId,
                ["binding_identity"] = source.BindingIdentity,
                ["from"] = Instant(from),
                ["to"] = Instant(to),
            },
            reason: reason);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        await transaction.CommitAsync(ct).ConfigureAwait(false);
        return ToDto(asked, source.PublicId);
    }

    /// <summary>
    /// Asks for an account to be read sooner: its next read is due at the next scan, never within a minute of the last
    /// one's start. It keeps the moment on the account and nothing else — it writes no line, and a burst of signals is
    /// one read, whose own line says a signal brought it forward.
    /// </summary>
    public async Task<EventsSignalResponse> SignalAsync(EventsSignalRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.Source))
        {
            throw DomainErrors.Required("source");
        }

        var source = await db.EventSources.FirstOrDefaultAsync(row => row.PublicId == request.Source, ct).ConfigureAwait(false)
            ?? throw DomainErrors.EventSourceNotFound(request.Source);
        if (!IsBound(source))
        {
            throw DomainErrors.EventSourceUnbound(source.PublicId);
        }

        var now = clock.GetUtcNow().UtcDateTime;
        await db.EventSources
            .Where(row => row.Id == source.Id)
            .ExecuteUpdateAsync(set => set.SetProperty(row => row.SignalAt, now), ct)
            .ConfigureAwait(false);
        source.SignalAt = now;
        return new EventsSignalResponse(ContactMapper.ToOffset(now), Offset(NextReadAt(source, now)));
    }

    private bool IsBound(EventSource source) =>
        EventAccounts.Bound(routes.Snapshot, plugins.Snapshot).Any(account => account.PluginId == source.PluginId && account.BindingIdentity == source.BindingIdentity);

    /// <summary>When the account's own reading is next due, or now when that is already so.</summary>
    private DateTime NextReadAt(EventSource source, DateTime now) =>
        Later(EventSchedule.DueAt(source, reader.HasRead(source.Id), dispatcher.Current.EventsCadenceSeconds), now);

    private EventSourceDto ToDto(EventSource source, BoundAccount? account, (EventGapDto? Gap, IReadOnlyList<EventBackfillDto> Backfills) record, DateTime now)
    {
        var cadence = dispatcher.Current.EventsCadenceSeconds;
        var next = account is null ? (DateTime?)null : NextReadAt(source, now);
        return new EventSourceDto(
            source.PublicId,
            source.PluginId,
            source.BindingIdentity,
            account?.Binding?.DeepClone() as JsonObject,
            account is not null,
            source.Cursor is not null,
            Offset(source.CursorAt),
            Offset(source.ReadStartedAt),
            Offset(source.ReadEndedAt),
            Offset(source.SucceededAt),
            source.Failures,
            source.LastCode,
            source.LastClass,
            source.More,
            EventSchedule.IsHealthy(source, cadence, inbound.Current.LookbackSeconds, now),
            Offset(next),
            Offset(source.SignalAt),
            record.Gap,
            record.Backfills,
            ContactMapper.ToOffset(source.CreatedAt));
    }

    /// <summary>The newest line saying a span of this account nobody will read: its own key's, never another account's.</summary>
    private async Task<EventGapDto?> LatestGapAsync(EventSource source, CancellationToken ct)
    {
        var prefix = $"gap/{source.PublicId}/";
        var line = await db.Journal
            .AsNoTracking()
            .Where(entry => entry.Kind == JournalKinds.EventsGapObserved && entry.Key!.StartsWith(prefix))
            .OrderByDescending(entry => entry.Id)
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);
        return line?.New is JsonObject said
            && DateTimeOffset.TryParse((string?)said["from"], CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var from)
            && DateTimeOffset.TryParse((string?)said["to"], CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var to)
                ? new EventGapDto(from.ToUniversalTime(), to.ToUniversalTime(), ContactMapper.ToOffset(line.Ts))
                : null;
    }

    private async Task<IReadOnlyList<EventBackfillDto>> RecentAsync(EventSource source, CancellationToken ct)
    {
        var recent = await db.EventBackfills
            .AsNoTracking()
            .Where(backfill => backfill.SourceId == source.Id)
            .OrderByDescending(backfill => backfill.Id)
            .Take(RecentBackfills)
            .ToListAsync(ct)
            .ConfigureAwait(false);
        return [.. recent.Select(backfill => ToDto(backfill, source.PublicId))];
    }

    private static EventBackfillDto ToDto(EventBackfill backfill, string sourcePublicId) =>
        new(
            backfill.PublicId,
            sourcePublicId,
            ContactMapper.ToOffset(backfill.From),
            ContactMapper.ToOffset(backfill.To),
            backfill.Status,
            ContactMapper.ToOffset(backfill.RequestedAt),
            new ActorRef(backfill.ActorType, backfill.ActorId),
            backfill.Reason,
            backfill.Stored,
            backfill.Duplicates,
            backfill.Failures,
            Offset(backfill.AttemptedAt),
            Offset(backfill.EndedAt),
            backfill.LastCode,
            backfill.LastClass);

    private static DateTime Later(DateTime due, DateTime now) => due > now ? due : now;

    private static DateTimeOffset? Offset(DateTime? moment) => moment is { } utc ? ContactMapper.ToOffset(utc) : null;

    /// <summary>A moment to the millisecond, as the runtime keeps every timestamp it writes.</summary>
    private static DateTime Millisecond(DateTimeOffset moment)
    {
        var utc = moment.UtcDateTime;
        return new DateTime(utc.Ticks - (utc.Ticks % TimeSpan.TicksPerMillisecond), DateTimeKind.Utc);
    }

    private static string Instant(DateTime moment) => Dispatch.InboundSync.Instant(new DateTimeOffset(moment));
}
