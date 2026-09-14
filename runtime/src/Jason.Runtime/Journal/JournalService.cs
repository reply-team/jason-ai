using System.Globalization;
using System.Text.Json;
using Jason.Contracts.Api;
using Jason.Contracts.Json;
using Jason.Runtime.Campaigns;
using Jason.Runtime.Domain;
using Jason.Runtime.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Jason.Runtime.Journal;

/// <summary>
/// The chronicle as callers see it: roles append their own vocabulary and everyone reads the campaign's history
/// newest first. Nothing here can rewrite a line — the only write this service performs is an append.
/// </summary>
public sealed class JournalService(JasonDbContext db, JournalWriter journal)
{
    public const int MaxKeyLength = 200;

    /// <summary>A journal value is a note, not a payload: 64 KiB is generous for one and refuses the other.</summary>
    public const int MaxValueBytes = 64 * 1024;

    public async Task<JournalEntryDto> AppendAsync(JournalAppendRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var actor = Actors.Resolve(request.Actor);

        var errors = new ValidationErrors();
        if (string.IsNullOrWhiteSpace(request.Kind))
        {
            errors.Add("kind", "required", "kind is required.");
        }
        else if (!JournalKinds.IsWellFormed(request.Kind.Trim()))
        {
            errors.Add("kind", "invalid", "kind starts with a lowercase letter and continues with lowercase letters, digits or underscores, up to 64 characters.");
        }

        if (request.Key is not null && request.Key.Trim().Length > MaxKeyLength)
        {
            errors.Add("key", "too_long", string.Create(CultureInfo.InvariantCulture, $"key must be at most {MaxKeyLength} characters."));
        }

        if (request.New is not null && JsonSerializer.SerializeToUtf8Bytes(request.New, JasonJson.Options).Length > MaxValueBytes)
        {
            errors.Add("new", "too_large", string.Create(CultureInfo.InvariantCulture, $"new must serialize to at most {MaxValueBytes} bytes."));
        }

        if (request.Reason is not null && request.Reason.Trim().Length > CampaignService.MaxReasonLength)
        {
            errors.Add("reason", "too_long", string.Create(CultureInfo.InvariantCulture, $"reason must be at most {CampaignService.MaxReasonLength} characters."));
        }

        errors.ThrowIfAny();

        var kind = request.Kind!.Trim();
        if (JournalKinds.Reserved.Contains(kind))
        {
            throw DomainErrors.ReservedKind(kind);
        }

        // An archived campaign still accepts entries: the chronicle is how anyone explains later what the
        // campaign did, and that conversation outlives the campaign.
        var campaign = await CampaignService.LoadAsync(db, request.CampaignId, cancellationToken).ConfigureAwait(false);

        var entry = journal.Append(
            db,
            actor,
            kind,
            campaign,
            key: Blank(request.Key) ? null : request.Key!.Trim(),
            updated: request.New?.DeepClone(),
            reason: Blank(request.Reason) ? null : request.Reason!.Trim());
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return JournalWriter.ToDto(entry, campaign.PublicId);
    }

    public async Task<Page<JournalEntryDto>> ListAsync(JournalListRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var limit = Paging.ResolveLimit(request.Limit);
        var before = Paging.DecodeCursor(request.Cursor);

        IQueryable<JournalEntry> query = db.Journal.AsNoTracking().Include(e => e.Campaign);

        if (!Blank(request.CampaignId))
        {
            var campaign = await CampaignService.LoadAsync(db, request.CampaignId, cancellationToken).ConfigureAwait(false);
            query = query.Where(e => e.CampaignId == campaign.Id);
        }

        if (!Blank(request.Kind))
        {
            var kind = request.Kind!.Trim();
            query = query.Where(e => e.Kind == kind);
        }

        if (request.Since is { } since)
        {
            var from = since.UtcDateTime;
            query = query.Where(e => e.Ts >= from);
        }

        // Newest first, so the cursor walks backwards through the public ids.
        if (before is not null)
        {
            query = query.Where(e => string.Compare(e.PublicId, before) < 0);
        }

        var fetched = await query.OrderByDescending(e => e.PublicId).Take(limit + 1).ToListAsync(cancellationToken).ConfigureAwait(false);
        return Paging.ToPage(fetched, limit, e => e.PublicId, e => JournalWriter.ToDto(e, e.Campaign?.PublicId));
    }

    private static bool Blank(string? value) => string.IsNullOrWhiteSpace(value);
}
