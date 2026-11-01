using System.Text.Json.Nodes;
using Jason.Runtime.Domain;
using Jason.Runtime.Journal;
using Jason.Runtime.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Jason.Runtime.Execution;

/// <summary>
/// What a read of a send says when it finds the send gone: one <see cref="JournalKinds.MessageSentObserved"/> line about
/// the work item that made the send, for any send a read finds gone, once, whether it went inside that item's call or
/// later, since the read adds the moment and the message's identifier. The item's result stays as it was frozen.
/// </summary>
/// <remarks>
/// The line is said once per send, under the key of the item that made it, and the database is what says so: the key
/// is looked up here first, and a second line under it is refused by
/// <c>ix_journal_campaign_kind_key_sends</c>. It holds identifiers and moments only. The text the read returned is in
/// the read's own result and nowhere else: a chronicle line is never forgotten, and a subject or a body is what a
/// person wrote to a person.
/// </remarks>
public sealed class SentFacts(JournalWriter journal)
{
    /// <summary>The read whose answer says whether a send went out: the only operation whose success says it.</summary>
    public const string Operation = "message.status";

    /// <summary>
    /// Adds the line to the context's change set (not saved here) where <paramref name="result"/> says the send went out
    /// and the campaign has not said it yet, and answers whether it did.
    /// </summary>
    /// <param name="read">The work item that read the send.</param>
    /// <param name="pluginId">Which plugin answered the read, as the claim pinned it.</param>
    /// <param name="input">The input the claim composed: its <c>send</c> names the send's work item and its reference.</param>
    /// <param name="result">What the read answered, already held to the operation's schema.</param>
    public async Task<bool> SayAsync(
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

        if (result?["state"]?.GetValue<string>() != "sent"
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

        // Read, then written, with nothing between to hold the two together: a campaign has at most one item scheduled or
        // processing (docs/work-execution.md, the claim), so two reads of one send never run at once, and the unique key
        // is the floor beneath that. Were the floor ever hit, the key refuses the read's save, the handler does not catch
        // that refusal, and the lease ends the attempt, so the read repeats and finds the line already said.
        var said = await db.Journal
            .AsNoTracking()
            .AnyAsync(
                entry => entry.CampaignId == send.CampaignId && entry.Kind == JournalKinds.MessageSentObserved && entry.Key == send.PublicId,
                cancellationToken)
            .ConfigureAwait(false);
        if (said)
        {
            return false;
        }

        journal.Append(
            db,
            Actors.Dispatcher,
            JournalKinds.MessageSentObserved,
            campaign: null,
            key: send.PublicId,
            updated: new JsonObject
            {
                ["plugin"] = pluginId,
                ["reference"] = reference,
                ["channel"] = result["channel"]?.DeepClone(),
                ["sent_at"] = result["sent_at"]?.DeepClone(),
                ["message_id"] = result["message_id"]?.DeepClone(),
                ["observed_by"] = "status",
                ["read_work_item"] = read.PublicId,
            },
            workItem: send);
        return true;
    }
}
