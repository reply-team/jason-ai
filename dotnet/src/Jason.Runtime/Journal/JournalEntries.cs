using Jason.Contracts.Api;
using Jason.Runtime.Contacts;
using Jason.Runtime.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Jason.Runtime.Journal;

/// <summary>
/// Lines as a reader gets them: the ids a line stores, and beside them what those ids are called and, for a line about a
/// work item, what the item is, all looked up when the lines are read. A page of lines costs one query per kind of thing
/// it names, never one per line.
/// </summary>
internal static class JournalEntries
{
    public static async Task<IReadOnlyList<JournalEntryDto>> ToDtosAsync(JasonDbContext db, IReadOnlyList<JournalEntry> entries, CancellationToken ct)
    {
        var campaignIds = entries.Select(e => e.CampaignId).OfType<int>().Distinct().ToList();
        var contactIds = entries.Select(e => e.ContactId).OfType<int>().Distinct().ToList();
        var itemIds = entries.Select(e => e.WorkItemId).OfType<string>().Distinct(StringComparer.Ordinal).ToList();
        var attemptIds = entries
            .Where(e => e.ActorType == ActorType.Attempt && e.ActorId is not null)
            .Select(e => e.ActorId!)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        var campaigns = (await db.Campaigns.AsNoTracking()
                .Where(c => campaignIds.Contains(c.Id))
                .Select(c => new { c.Id, c.PublicId, c.Name })
                .ToListAsync(ct).ConfigureAwait(false))
            .ToDictionary(c => c.Id, c => (c.PublicId, c.Name));
        var contacts = (await db.Contacts.AsNoTracking()
                .Where(c => contactIds.Contains(c.Id))
                .ToListAsync(ct).ConfigureAwait(false))
            .ToDictionary(c => c.Id, c => (c.PublicId, Name: ContactNames.Display(c)));
        var items = (await db.WorkItems.AsNoTracking()
                .Where(w => itemIds.Contains(w.PublicId))
                .Select(w => new { w.PublicId, w.Kind, w.Role, w.Operation })
                .ToListAsync(ct).ConfigureAwait(false))
            .ToDictionary(w => w.PublicId, w => (w.Kind, w.Role, w.Operation), StringComparer.Ordinal);
        var actingRoles = (await db.Attempts.AsNoTracking()
                .Where(a => attemptIds.Contains(a.PublicId))
                .Select(a => new { a.PublicId, a.WorkItem!.Role })
                .ToListAsync(ct).ConfigureAwait(false))
            .ToDictionary(a => a.PublicId, a => a.Role, StringComparer.Ordinal);

        return entries.Select(entry =>
        {
            var campaign = entry.CampaignId is { } campaignId && campaigns.TryGetValue(campaignId, out var c) ? c : default((string PublicId, string Name)?);
            var contact = entry.ContactId is { } contactId && contacts.TryGetValue(contactId, out var p) ? p : default((string PublicId, string? Name)?);
            var item = entry.WorkItemId is { } itemId && items.TryGetValue(itemId, out var w) ? w : default((WorkItemKind Kind, string? Role, string? Operation)?);
            var actingRole = entry.ActorType == ActorType.Attempt && entry.ActorId is { } attemptId && actingRoles.TryGetValue(attemptId, out var role) ? role : null;
            return JournalWriter.ToDto(entry, campaign?.PublicId) with
            {
                ContactId = contact?.PublicId,
                CampaignName = campaign?.Name,
                ContactName = contact?.Name,
                WorkItemKind = item?.Kind,
                WorkItemRole = item?.Role,
                WorkItemOperation = item?.Operation,
                ActorRole = actingRole,
            };
        }).ToList();
    }
}
