using System.Text.Json.Nodes;
using Jason.Contracts.Api;
using Jason.Contracts.Ids;
using Jason.Runtime.Persistence;

namespace Jason.Runtime.Journal;

/// <summary>
/// Writes chronicle entries. An entry is added to the same change set as the change it describes, so the
/// record and the fact commit together or not at all.
/// </summary>
public sealed class JournalWriter(TimeProvider clock)
{
    /// <summary>
    /// Adds an entry to the context's change set (not saved here) so it commits with the change it describes.
    /// An entry about a work item inherits the item's campaign when no campaign is passed, so the campaign
    /// chronicle stays one query.
    /// </summary>
    public JournalEntry Append(
        JasonDbContext db,
        ActorRef actor,
        string kind,
        Campaign? campaign,
        string? key = null,
        JsonNode? old = null,
        JsonNode? updated = null,
        string? reason = null,
        WorkItem? workItem = null,
        Attempt? attempt = null)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(actor);
        ArgumentException.ThrowIfNullOrWhiteSpace(kind);

        var entry = new JournalEntry
        {
            PublicId = PublicId.New("jrn"),
            Ts = clock.GetUtcNow().UtcDateTime,
            ActorType = actor.Type,
            ActorId = actor.Id,
            Kind = kind,

            // With a campaign in hand the navigation carries the key, including for a campaign that is itself
            // still being inserted; without one the item's foreign key is the denormalized answer.
            CampaignId = campaign is null ? workItem?.CampaignId : null,
            Campaign = campaign,
            WorkItemId = workItem?.PublicId,
            AttemptId = attempt?.PublicId,
            Key = key,
            Old = old,
            New = updated,
            Reason = reason,
        };

        db.Journal.Add(entry);
        return entry;
    }

    /// <summary>The campaign's public id has to be supplied (or loaded into the navigation): the internal key never leaves the runtime.</summary>
    public static JournalEntryDto ToDto(JournalEntry entry, string? campaignPublicId)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return new JournalEntryDto(
            entry.PublicId,
            new DateTimeOffset(DateTime.SpecifyKind(entry.Ts, DateTimeKind.Utc)),
            new ActorRef(entry.ActorType, entry.ActorId),
            entry.Kind,
            campaignPublicId ?? entry.Campaign?.PublicId,
            entry.WorkItemId,
            entry.AttemptId,
            entry.Key,
            entry.Old,
            entry.New,
            entry.Reason);
    }
}
