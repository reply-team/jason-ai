using System.Globalization;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Jason.Contracts.Api;
using Jason.Contracts.Plugins;
using Jason.Runtime.Contacts;
using Jason.Runtime.Domain;
using Jason.Runtime.Journal;
using Jason.Runtime.Persistence;
using Jason.Runtime.Plugins.Registry;
using Microsoft.EntityFrameworkCore;

namespace Jason.Runtime.Plugins.Subscriptions;

/// <summary>
/// Where each subscribed kind stands, the records of its chains, and a person's rewind of its cursor. Nothing here runs a
/// chain or invokes a plugin: the scan offers kinds and the runner runs them, and a rewind only moves a cursor back, which
/// the runner sees at its next line.
/// </summary>
/// <remarks>
/// A rewind bumps the cursor's revision, so a chain in flight for the kind ends superseded at its end's guarded update,
/// and the next scan offers the kind from where the rewind put it.
/// </remarks>
public sealed partial class SubscriptionService(JasonDbContext db, JournalWriter journal, PluginRegistry plugins, TimeProvider clock)
{
    /// <summary>The longest reason a rewind keeps, as the journal keeps a reason elsewhere.</summary>
    public const int MaxReasonLength = 2000;

    public async Task<Page<SubscriptionKindDto>> ListAsync(SubscriptionListRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        var limit = Paging.ResolveLimit(request.Limit);
        var after = Paging.DecodeCursor(request.Cursor);

        var query = db.SubscriptionCursors.AsNoTracking();
        if (after is not null)
        {
            query = query.Where(cursor => string.Compare(cursor.Kind, after) > 0);
        }

        var fetched = await query.OrderBy(cursor => cursor.Kind).Take(limit + 1).ToListAsync(ct).ConfigureAwait(false);
        var kinds = new Dictionary<string, SubscriptionKindDto>(StringComparer.Ordinal);
        foreach (var cursor in fetched.Take(limit))
        {
            kinds[cursor.Kind] = await KindAsync(cursor, ct).ConfigureAwait(false);
        }

        return Paging.ToPage(fetched, limit, cursor => cursor.Kind, cursor => kinds[cursor.Kind]);
    }

    public async Task<Page<SubscriptionRunDto>> RunsAsync(SubscriptionRunsRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.Kind))
        {
            throw DomainErrors.Required("kind");
        }

        var limit = Paging.ResolveLimit(request.Limit);
        var before = Paging.DecodeCursor(request.Cursor) is { } decoded
            ? int.TryParse(decoded, NumberStyles.None, CultureInfo.InvariantCulture, out var id) ? id : throw InvalidCursor()
            : (int?)null;

        var query = db.SubscriptionRuns.AsNoTracking().Where(run => run.Kind == request.Kind);
        if (before is { } below)
        {
            query = query.Where(run => run.Id < below);
        }

        var fetched = await query.OrderByDescending(run => run.Id).Take(limit + 1).ToListAsync(ct).ConfigureAwait(false);
        return Paging.ToPage(fetched, limit, run => run.Id.ToString(CultureInfo.InvariantCulture), ToDto);
    }

    /// <summary>
    /// Moves a subscribed kind's cursor back, to just before a line of the kind or to just before the first line written at
    /// or after a moment, so every line of the kind after it is handed to the chain again. A target after the cursor is
    /// refused; the cursor where it stands is a target, which ends a chain in flight and hands its line again.
    /// </summary>
    public async Task<SubscriptionKindDto> RewindAsync(SubscriptionRewindRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        var actor = Person(request.Actor);

        var errors = new ValidationErrors();
        if (string.IsNullOrWhiteSpace(request.Kind))
        {
            errors.Add("kind", "required", "kind is required: a subscribed journal kind, as jason subscription list lists it.");
        }

        var byLine = !string.IsNullOrWhiteSpace(request.Line);
        var byMoment = !string.IsNullOrWhiteSpace(request.Since);
        if (byLine == byMoment)
        {
            errors.Add(
                byLine ? "since" : "line",
                "exactly_one",
                "exactly one of line and since is required: the line of the kind to hand again first, or the moment from which every line of the kind is handed again.");
        }

        DateTime since = default;
        if (byMoment && !TryMoment(request.Since!, out since))
        {
            errors.Add("since", "invalid", "since must be an ISO-8601 moment with its offset, such as 2026-01-15T09:00:00Z.");
        }

        var reason = string.IsNullOrWhiteSpace(request.Reason) ? null : request.Reason;
        if (reason is not null && reason.Length > MaxReasonLength)
        {
            errors.Add("reason", "too_long", string.Create(CultureInfo.InvariantCulture, $"reason must be at most {MaxReasonLength} characters."));
        }

        errors.ThrowIfAny();
        var kind = request.Kind!;

        // Read and written under the writer's lock, so the cursor a target is measured against is the one the update moves.
        await using var transaction = await db.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
        var cursor = await db.SubscriptionCursors.FirstOrDefaultAsync(row => row.Kind == kind, ct).ConfigureAwait(false)
            ?? throw DomainErrors.SubscriptionKindUnsubscribed(kind);

        // The first line handed again: the one named, or the first written at or after the moment. A line's moment is
        // stamped when it is appended and its id when it is saved, so ids and moments need not run together: the cursor
        // goes to just before that first line, which may hand again a line stamped before the moment but never skips one
        // stamped at or after it. With no line at or after the moment, the cursor goes to the head.
        int? first;
        if (byLine)
        {
            first = await db.Journal
                .AsNoTracking()
                .Where(entry => entry.PublicId == request.Line && entry.Kind == kind)
                .Select(entry => (int?)entry.Id)
                .FirstOrDefaultAsync(ct)
                .ConfigureAwait(false)
                ?? throw DomainErrors.SubscriptionLineNotFound(kind, request.Line!);
        }
        else
        {
            first = await db.Journal.Where(entry => entry.Ts >= since).MinAsync(entry => (int?)entry.Id, ct).ConfigureAwait(false);
        }

        var target = first is { } from
            ? await db.Journal.Where(entry => entry.Id < from).MaxAsync(entry => (int?)entry.Id, ct).ConfigureAwait(false) ?? 0
            : await db.Journal.MaxAsync(entry => (int?)entry.Id, ct).ConfigureAwait(false) ?? 0;

        if (target > cursor.AfterLine)
        {
            throw DomainErrors.SubscriptionRewindForward(kind);
        }

        var was = await LineAtAsync(cursor.AfterLine, ct).ConfigureAwait(false);
        var now = clock.GetUtcNow().UtcDateTime;
        cursor.AfterLine = target;
        cursor.Revision++;
        cursor.UpdatedAt = now;
        journal.Append(
            db,
            actor,
            JournalKinds.SubscriptionCursorRewound,
            campaign: null,
            key: kind,
            old: new JsonObject { ["line"] = was },
            updated: new JsonObject { ["line"] = await LineAtAsync(target, ct).ConfigureAwait(false) },
            reason: reason);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        await transaction.CommitAsync(ct).ConfigureAwait(false);
        return await KindAsync(cursor, ct).ConfigureAwait(false);
    }

    private async Task<SubscriptionKindDto> KindAsync(SubscriptionCursor cursor, CancellationToken ct)
    {
        var snapshot = plugins.Snapshot;
        var chain = snapshot.Subscriptions.For(cursor.Kind);
        var lines = db.Journal.AsNoTracking().Where(entry => entry.Kind == cursor.Kind);
        var head = await lines.OrderByDescending(entry => entry.Id).Select(entry => entry.PublicId).FirstOrDefaultAsync(ct).ConfigureAwait(false);
        var lag = await lines.CountAsync(entry => entry.Id > cursor.AfterLine, ct).ConfigureAwait(false);
        var running = await db.SubscriptionRuns
            .AsNoTracking()
            .Where(run => run.Kind == cursor.Kind && run.Status == SubscriptionRunStatus.Running)
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);
        return new SubscriptionKindDto(
            cursor.Kind,
            chain,
            await LineAtAsync(cursor.AfterLine, ct).ConfigureAwait(false),
            head,
            lag,
            [.. chain.Where(id => snapshot.Find(id) is not { Status: PluginStatus.Valid })],
            running is null
                ? null
                : new SubscriptionInFlightDto(
                    running.PublicId,
                    running.LinePublicId,
                    ContactMapper.ToOffset(running.StartedAt),
                    running.RetryAt is { } retry ? ContactMapper.ToOffset(retry) : null));
    }

    /// <summary>The public id of the journal line a cursor stands after; null at the journal's start.</summary>
    private async Task<string?> LineAtAsync(int after, CancellationToken ct) =>
        after <= 0
            ? null
            : await db.Journal.AsNoTracking().Where(entry => entry.Id <= after).OrderByDescending(entry => entry.Id).Select(entry => entry.PublicId).FirstOrDefaultAsync(ct).ConfigureAwait(false);

    private static SubscriptionRunDto ToDto(SubscriptionRun run) => new(
        run.PublicId,
        run.Kind,
        run.LinePublicId,
        run.Status,
        ContactMapper.ToOffset(run.StartedAt),
        run.EndedAt is { } ended ? ContactMapper.ToOffset(ended) : null,
        run.Handlers,
        run.FailedPluginId is { } plugin ? new SubscriptionFailureDto(plugin, run.FailureClass ?? string.Empty, run.FailureCode ?? string.Empty) : null,
        run.Envelope);

    /// <summary>
    /// Who rewinds. An absent actor is an anonymous human, which is right for creating work and wrong for handing lines to
    /// plugins again: what is recorded names a person. A plugin, a role and an attempt are refused alike.
    /// </summary>
    private static ActorRef Person(ActorRef? claimed)
    {
        var actor = Actors.Resolve(claimed);
        if (actor.Type != ActorType.Human)
        {
            throw DomainErrors.SubscriptionRewindNotHuman(actor.Type);
        }

        return string.IsNullOrWhiteSpace(actor.Id) ? throw DomainErrors.SubscriptionRewindActorRequired() : actor;
    }

    /// <summary>An ISO-8601 moment that carries its offset, <c>Z</c> or <c>±hh:mm</c>: one with none names no moment.</summary>
    private static bool TryMoment(string text, out DateTime utc)
    {
        utc = default;
        if (!Moment().IsMatch(text)
            || !DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var moment))
        {
            return false;
        }

        utc = moment.UtcDateTime;
        return true;
    }

    private static ValidationException InvalidCursor() =>
        new([new ErrorDetail("cursor", "invalid", "cursor must be the next_cursor returned by an earlier page.")]);

    [GeneratedRegex(@"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(\.\d{1,7})?(Z|[+-]\d{2}:\d{2})\z", RegexOptions.CultureInvariant)]
    private static partial Regex Moment();
}
