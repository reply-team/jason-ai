using System.Globalization;
using Jason.Contracts.Api;
using Jason.Runtime.Domain;
using Jason.Runtime.Execution;
using Jason.Runtime.Journal;
using Jason.Runtime.Persistence;
using Jason.Runtime.WorkItems;
using Microsoft.EntityFrameworkCore;

namespace Jason.Runtime.Blocks;

/// <summary>
/// The three verbs a person sees the provider's blocks through and ends one with. Nothing inside the runtime calls them:
/// the dispatcher opens a block when an answer says one holds and clears it when a call gets past it, and the only other
/// way one clears is a request that arrives here naming the person who says the cause is gone.
/// </summary>
public sealed class BlockService(JasonDbContext db, JournalWriter journal, TimeProvider clock)
{
    /// <summary>
    /// What is blocked, newest first: open blocks by default, because the question a person opens this with is "what is
    /// blocked now?" — the cleared ones are the history.
    /// </summary>
    public async Task<Page<ProviderBlockDto>> ListAsync(BlockListRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var limit = Paging.ResolveLimit(request.Limit);
        var after = After(Paging.DecodeCursor(request.Cursor));

        // Bounded by the blocks of the asked statuses: they are found through the index that leads with the status and
        // sorted there, so the open blocks a person asks for first never cost a walk of every block ever opened.
        IQueryable<ProviderBlock> query = db.ProviderBlocks.AsNoTracking();
        ProviderBlockStatus[] wanted = request.Statuses is { Count: > 0 } several ? [.. several.Distinct()] : [ProviderBlockStatus.Open];
        query = query.Where(b => wanted.Contains(b.Status));

        if (!string.IsNullOrWhiteSpace(request.PluginId))
        {
            var plugin = request.PluginId.Trim();
            query = query.Where(b => b.PluginId == plugin);
        }

        if (after is var (at, id))
        {
            query = query.Where(b => b.OpenedAt < at || (b.OpenedAt == at && string.Compare(b.PublicId, id) < 0));
        }

        var fetched = await Rows(query.OrderByDescending(b => b.OpenedAt).ThenByDescending(b => b.PublicId).Take(limit + 1))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        return Paging.ToPage(fetched, limit, row => CursorKey(row.Block), ToDto);
    }

    public async Task<ProviderBlockDto> GetAsync(BlockGetRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return await ReadAsync(RequireId(request.BlockId), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// A person's clear: the open block is cleared now, under their name and by no attempt, and every campaign that was
    /// told it held their work is told it cleared, with the reason they gave. Nothing is released here: the next scan's
    /// resume step releases what the block held, as it does for a block an attempt got past. A block cleared already —
    /// by a person, or by an attempt that got past it between the read and this write — is refused, and that clear stands.
    /// </summary>
    public async Task<ProviderBlockDto> ClearAsync(BlockClearRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var errors = new ValidationErrors();
        if (string.IsNullOrWhiteSpace(request.BlockId))
        {
            errors.Add("block_id", "required", "block_id is required.");
        }

        WorkItemValidation.ValidateReason(request.Reason, errors);
        errors.ThrowIfAny();
        var actor = Person(request.Actor);
        await ActorVerification.VerifyAsync(db, actor, cancellationToken).ConfigureAwait(false);

        // BEGIN IMMEDIATE, as the claim begins: from the block's read to the commit no other writer runs, so no campaign is
        // told the block holds its work after the told set was read, and no trial is set on a block this clears.
        var id = request.BlockId!.Trim();
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        var block = await db.ProviderBlocks.FirstOrDefaultAsync(b => b.PublicId == id, cancellationToken).ConfigureAwait(false)
            ?? throw DomainErrors.BlockNotFound(id);
        if (block.Status != ProviderBlockStatus.Open)
        {
            throw DomainErrors.BlockNotOpen(id);
        }

        var reason = string.IsNullOrWhiteSpace(request.Reason) ? null : request.Reason.Trim();
        ProviderBlocks.ClearedBy(block, actor, clock.GetUtcNow().UtcDateTime);
        await ProviderBlocks.SayClearedAsync(db, journal, block, actor, reason, cancellationToken).ConfigureAwait(false);

        try
        {
            // The block's UPDATE still carries the status it read in its WHERE. Inside this transaction nothing can have
            // changed it since the read; should that ever stop being true, the token makes this write nothing, no clear
            // and no line, rather than a second clear.
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException)
        {
            db.ChangeTracker.Clear();
            throw DomainErrors.BlockNotOpen(id);
        }

        return await ReadAsync(id, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Who clears. An absent actor is an anonymous human, which is right for creating work and wrong for ending a block: a
    /// clear names the person whose word it is, as a decision does.
    /// </summary>
    private static ActorRef Person(ActorRef? claimed)
    {
        var actor = Actors.Resolve(claimed);
        if (actor.Type != ActorType.Human)
        {
            throw DomainErrors.BlockNotHuman(actor.Type);
        }

        return string.IsNullOrWhiteSpace(actor.Id) ? throw DomainErrors.BlockActorRequired() : actor;
    }

    private async Task<ProviderBlockDto> ReadAsync(string id, CancellationToken cancellationToken)
    {
        var row = await Rows(db.ProviderBlocks.AsNoTracking().Where(b => b.PublicId == id))
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false)
            ?? throw DomainErrors.BlockNotFound(id);
        return ToDto(row);
    }

    /// <summary>Each block with the public ids it refers to and the open waits parked on it, counted in the same query.</summary>
    private IQueryable<Row> Rows(IQueryable<ProviderBlock> blocks) =>
        blocks.Select(b => new Row(
            b,
            b.OpenedByAttempt!.PublicId,
            b.TrialWorkItem != null ? b.TrialWorkItem.PublicId : null,
            b.ClearedByAttempt != null ? b.ClearedByAttempt.PublicId : null,
            db.WorkItemWaits.Count(w => w.ProviderBlockId == b.Id && w.Status == WaitStatus.Open)));

    private static ProviderBlockDto ToDto(Row row)
    {
        var block = row.Block;
        return new ProviderBlockDto(
            block.PublicId,
            block.PluginId,
            block.BindingIdentity,
            block.Scope,
            block.SenderExternalId,
            block.Reason,
            block.Link,
            WorkItemMapper.Utc(block.ClearsAt),
            block.Status,
            WorkItemMapper.Utc(block.OpenedAt),
            WorkItemMapper.Utc(block.LastSeenAt),
            block.SeenCount,
            WorkItemMapper.Utc(block.NextTrialAt),
            row.TrialWorkItemId,
            row.OpenedByAttemptId,
            row.HeldCount,
            WorkItemMapper.Utc(block.ClearedAt),
            row.ClearedByAttemptId,
            block.ClearedByType is { } type ? new ActorRef(type, block.ClearedById) : null);
    }

    /// <summary>A page's cursor names its last block by the moment it opened and its id: the moment alone is not unique.</summary>
    private static string CursorKey(ProviderBlock block) =>
        DateTime.SpecifyKind(block.OpenedAt, DateTimeKind.Utc).ToString("O", CultureInfo.InvariantCulture) + "|" + block.PublicId;

    private static (DateTime At, string Id)? After(string? cursor)
    {
        if (cursor is null)
        {
            return null;
        }

        var bar = cursor.IndexOf('|', StringComparison.Ordinal);
        return bar > 0 && bar < cursor.Length - 1
            && DateTime.TryParse(cursor[..bar], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var at)
            ? (at, cursor[(bar + 1)..])
            : throw new ValidationException([new ErrorDetail("cursor", "invalid", "cursor must be the next_cursor returned by an earlier page.")]);
    }

    private static string RequireId(string? id) =>
        string.IsNullOrWhiteSpace(id) ? throw DomainErrors.Required("block_id") : id.Trim();

    private sealed record Row(ProviderBlock Block, string OpenedByAttemptId, string? TrialWorkItemId, string? ClearedByAttemptId, int HeldCount);
}
