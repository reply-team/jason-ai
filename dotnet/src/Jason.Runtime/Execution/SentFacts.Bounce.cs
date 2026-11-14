using System.Text.Json;
using System.Text.Json.Nodes;
using Jason.Runtime.Contacts;
using Jason.Runtime.Domain;
using Jason.Runtime.Journal;
using Jason.Runtime.Persistence;
using Jason.Runtime.Routing;
using Microsoft.EntityFrameworkCore;

namespace Jason.Runtime.Execution;

/// <summary>
/// What a read of a send says when it finds the message came back as a bounce: one
/// <see cref="JournalKinds.MessageBounceObserved"/> line about the work item that made the send, whoever made the read,
/// and, for a hard bounce or one the provider named no kind for, the stop: the email Jason would send that contact to,
/// put on the do-not-contact register by <see cref="Actors.Bounce"/>, in the same change set.
/// </summary>
/// <remarks>
/// Every read decides alone, at its own end. A read that says the send did not bounce, or cannot say, decides nothing and
/// stops nothing, and so does a read that fails: "no kind" means the provider said the message bounced and gave no kind,
/// never that nothing could find out. A soft bounce is recorded and stops nothing. The line is said once per send, under
/// the key of the item that made it.
/// </remarks>
public sealed partial class SentFacts
{
    /// <summary>The one channel a bounce stops: a bounce is an email's.</summary>
    private const string Email = "email";

    /// <summary>
    /// Adds the line, and the stop where its kind asks for one, to the context's change set (not saved here), where
    /// <paramref name="result"/> says the send bounced and the campaign has not said it of that send yet, and answers
    /// whether it did.
    /// </summary>
    /// <param name="read">The work item that read the send.</param>
    /// <param name="pluginId">Which plugin answered the read, as the claim pinned it.</param>
    /// <param name="input">The input the claim composed: its <c>send</c> names the send's work item and its reference.</param>
    /// <param name="result">What the read answered, already held to the operation's schema.</param>
    public async Task<bool> SayBounceAsync(
        JasonDbContext db,
        WorkItem read,
        string pluginId,
        JsonObject? input,
        JsonNode? result,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(read);
        ArgumentException.ThrowIfNullOrWhiteSpace(pluginId);

        if (result?["bounced"] is not JsonValue bounced
            || bounced.GetValueKind() != JsonValueKind.True
            || input?["send"] is not JsonObject named
            || named["work_item"]?.GetValue<string>() is not { } sendId
            || named["reference"]?.GetValue<string>() is not { } reference)
        {
            return false;
        }

        // The send is one of the read's own campaign: the claim refused any other, and a line names one campaign's work.
        var send = await db.WorkItems
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.PublicId == sendId && item.CampaignId == read.CampaignId, cancellationToken)
            .ConfigureAwait(false);
        if (send is null)
        {
            return false;
        }

        // Read, then written, as the sent line is: a campaign has at most one item scheduled or processing, so two reads of
        // one send never end at once, and the unique key on the campaign, the kind and the send is the floor beneath that.
        var said = await db.Journal
            .AsNoTracking()
            .AnyAsync(
                entry => entry.CampaignId == send.CampaignId && entry.Kind == JournalKinds.MessageBounceObserved && entry.Key == send.PublicId,
                cancellationToken)
            .ConfigureAwait(false);
        if (said)
        {
            return false;
        }

        var kind = result["bounce"]?.GetValue<string>();
        var channel = result["channel"]?.GetValue<string>();
        var line = journal.Append(
            db,
            Actors.Dispatcher,
            JournalKinds.MessageBounceObserved,
            campaign: null,
            key: send.PublicId,
            updated: new JsonObject
            {
                ["plugin"] = pluginId,
                ["reference"] = reference,
                ["channel"] = channel,
                ["message_id"] = result["message_id"]?.DeepClone(),
                ["bounce"] = kind,
                ["bounce_code"] = result["bounce_code"]?.DeepClone(),
                ["bounced_at"] = result["bounced_at"]?.DeepClone(),
                ["read_work_item"] = read.PublicId,
            },
            workItem: send);

        if (kind != "soft" && channel == Email && send.ContactId is { } contactId)
        {
            await StopAsync(db, line, send, contactId, kind, cancellationToken).ConfigureAwait(false);
        }

        return true;
    }

    /// <summary>
    /// The stop: the email Jason would send the contact to now — the primary, as the claim picks it — on the register, by
    /// <see cref="Actors.Bounce"/>, dated as the line. A send's record never holds the address it went to, so the address
    /// is the one the contact holds when the read ends, and the reason says so. An address already on the register is left
    /// as it is; a contact with no email has nothing to stop.
    /// </summary>
    private async Task StopAsync(JasonDbContext db, JournalEntry line, WorkItem send, int contactId, string? kind, CancellationToken cancellationToken)
    {
        var contact = await db.Contacts
            .AsNoTracking()
            .Include(candidate => candidate.Channels)
            .SingleOrDefaultAsync(candidate => candidate.Id == contactId, cancellationToken)
            .ConfigureAwait(false);
        if (contact is null || CanonicalInput.Reachable(contact, Email) is not { } address)
        {
            return;
        }

        var campaign = await db.Campaigns
            .AsNoTracking()
            .Where(candidate => candidate.Id == send.CampaignId)
            .Select(candidate => candidate.PublicId)
            .SingleAsync(cancellationToken)
            .ConfigureAwait(false);
        await SuppressionService.StageAsync(
                db,
                journal,
                line.Ts,
                Actors.Bounce,
                [(Email, address.Value)],
                BounceReason(kind, line.PublicId, send.PublicId, campaign),
                cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// The reason a register entry a bounce made carries: the kind, the line that says it, the send and its campaign, by
    /// their ids, and which address was taken. No address and no word of the provider's is repeated: the value is the
    /// entry itself, and a reader looking for the entries one bounce made finds them by the line's id.
    /// </summary>
    internal static string BounceReason(string? kind, string line, string send, string campaign) =>
        $"Bounced: {(kind == "hard" ? "a hard bounce" : "a bounce the provider named no kind for")}, line {line} of send {send} "
        + $"in campaign {campaign}. The address taken is the email Jason would send this contact to when the bounce was read.";
}
