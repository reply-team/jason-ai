using System.Text.Json.Nodes;
using Jason.Contracts.Api;
using Jason.Runtime.Domain;
using Jason.Runtime.Journal;
using Jason.Runtime.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Jason.Runtime.Contacts;

/// <summary>
/// The hold a reply sets on a person's line: a person who is an <c>enrolled</c> member of the campaign becomes
/// <c>paused</c>, so the claim takes none of their touches there (<see cref="Jason.Contracts.Operations.OperationCatalog.Touches"/>)
/// until the line is continued (<c>campaign.continue_contacts</c>) or ended (<c>campaign.finish_contacts</c>). A member
/// whose line is already held, ended or removed is left as they are, and so is anyone who is not a member. One reserved
/// line says it, <c>contacts_paused</c>, as the dispatcher, naming the person and the reply line by their ids alone.
/// <para>
/// Written once and called from one place, the respond step, inside the transaction that reads the reply line, whether
/// or not a responder is woken for it. Nothing here reads what the reply says: a reply holds the line, and what follows
/// is the manager's.
/// </para>
/// </summary>
internal static class LineHold
{
    /// <summary>
    /// Holds the person's line in the campaign when they are enrolled there, and answers whether it did. The membership is
    /// read and written inside the caller's transaction, which holds the writer, so nothing changes it in between.
    /// </summary>
    public static async Task<bool> ApplyAsync(
        JasonDbContext db,
        JournalWriter journal,
        DateTime now,
        int campaignId,
        int contactId,
        string linePublicId,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(journal);

        var membership = await db.CampaignContacts
            .Include(m => m.Contact)
            .SingleOrDefaultAsync(m => m.CampaignId == campaignId && m.ContactId == contactId, ct)
            .ConfigureAwait(false);
        if (membership is not { State: MembershipState.Enrolled })
        {
            return false;
        }

        membership.State = MembershipState.Paused;
        membership.UpdatedAt = now;
        journal.Append(
            db,
            Actors.Dispatcher,
            JournalKinds.ContactsPaused,
            campaign: null,
            updated: new JsonObject
            {
                ["contact_ids"] = new JsonArray(JsonValue.Create(membership.Contact!.PublicId)),
                ["line"] = linePublicId,
            },
            campaignId: campaignId);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return true;
    }
}
