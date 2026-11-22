using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Jason.Contracts.Api;
using Jason.Contracts.Operations;
using Jason.Runtime.Domain;
using Jason.Runtime.Events;
using Jason.Runtime.Journal;
using Jason.Runtime.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Jason.Runtime.Contacts;

/// <summary>
/// The hold a reply sets on a person's line: a person who is an <c>enrolled</c> member of the campaign becomes
/// <c>paused</c>, so the claim takes none of their touches there (<see cref="Jason.Contracts.Operations.OperationCatalog.Touches"/>)
/// until the line is continued (<c>campaign.continue_contacts</c>) or ended (<c>campaign.finish_contacts</c>). A member
/// whose line is already paused, ended or removed is left as they are, and so is anyone who is not a member. One reserved
/// line says it, <c>contacts_paused</c>, as the dispatcher, naming the person and the reply line by their ids alone.
/// <para>
/// A thread moves when Jason writes into it too, and a line that does not know who moved the thread says every move: the
/// inbound read's line, and the events store's line from a <c>thread_moved</c> event that names no side. Such a line that
/// may be Jason's own move holds nobody (<see cref="OwnMoveAsync"/>): its moment is not later than the latest moment one
/// of Jason's own letters went into the thread. That letter is any succeeded letter of Jason's own: a touch of the person
/// in the campaign, at its result's <c>accepted_for</c> where it names one and otherwise at the end of its succeeded
/// attempt, or an answer the responder sent, a <c>conversation.reply</c> of the campaign into the line's own thread, at
/// the end of its succeeded attempt. A touch records no thread, so touches are compared for the person across the
/// campaign and an answer for its thread alone. Only a moment that has come counts, and a letter a cancel took back never
/// went. Moments of the runtime's own record are compared and nothing else. A line whose stored event names the other side
/// always holds, whatever the moments (<see cref="OtherSideNamedAsync"/>): where the provider said who wrote, the moments
/// do not overrule it.
/// </para>
/// <para>
/// Written once and called from one place, the respond step, inside the transaction that reads the reply line, whether
/// or not a responder is woken for it. Nothing here reads what the reply says: a reply holds the line, and what follows
/// is the manager's.
/// </para>
/// </summary>
internal static class LineHold
{
    /// <summary>The answer into a thread, compared for the thread it names.</summary>
    private const string Answer = "conversation.reply";

    /// <summary>The cancel that takes a letter back before it goes.</summary>
    private const string Cancel = "message.cancel";

    /// <summary>
    /// Whether a letter of Jason's went out to the person at or after the line's moment and by now: a succeeded touch of the
    /// person in the campaign, at its result's <c>accepted_for</c> or else its succeeded attempt's end, or a succeeded answer
    /// of the campaign into the line's thread, at its attempt's end; never one a succeeded cancel names. SQLite's date
    /// functions read the stored moment and the contract's forms alike, and one they cannot read is NULL and counts for
    /// nothing.
    /// </summary>
    private const string OwnMoveSql = """
        SELECT count(*) AS "Value"
        FROM work_items AS s
        JOIN attempts AS a ON a.work_item_id = s.id AND a.status = 'succeeded'
        WHERE s.campaign_id = {0}
          AND s.status = 'succeeded'
          AND ((s.contact_id = {1} AND s.operation IN (SELECT value FROM json_each({2})))
               OR ({3} IS NOT NULL AND s.operation = {4} AND json_extract(s.context_json, '$.input.thread.external_id') = {3}))
          AND NOT EXISTS (
              SELECT 1 FROM work_items AS k
              WHERE k.campaign_id = s.campaign_id AND k.operation = {5} AND k.status = 'succeeded'
                AND json_extract(k.context_json, '$.input.send') = s.public_id)
          AND julianday(coalesce(json_extract(s.result_json, '$.items[0].accepted_for'), a.finished_at)) >= julianday({6})
          AND julianday(coalesce(json_extract(s.result_json, '$.items[0].accepted_for'), a.finished_at)) <= julianday({7})
        """;

    /// <summary>
    /// Whether the stored event a line names is a thread the provider says the other side moved: the events contract's
    /// <c>moved_by</c>, read from the event as it was stored.
    /// </summary>
    private const string OtherSideSql = """
        SELECT count(*) AS "Value"
        FROM events AS e
        WHERE e.public_id = {0}
          AND e.kind = {1}
          AND json_extract(e.data_json, '$.moved_by') = {2}
        """;

    private static readonly string Touches = JsonSerializer.Serialize(OperationCatalog.Touches);

    /// <summary>
    /// Holds the person's line in the campaign when they are enrolled there and the line is not Jason's own move, and
    /// answers whether it did. The membership is read and written inside the caller's transaction, which holds the writer,
    /// so nothing changes it in between. A line with no moment the step can read is never taken for Jason's own move, and
    /// neither is one whose stored event, <paramref name="eventId"/>, names the other side.
    /// </summary>
    public static async Task<bool> ApplyAsync(
        JasonDbContext db,
        JournalWriter journal,
        DateTime now,
        int campaignId,
        int contactId,
        string linePublicId,
        string? thread,
        DateTime? moved,
        string? eventId,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(journal);

        if (moved is { } moment
            && !(eventId is not null && await OtherSideNamedAsync(db, eventId, ct).ConfigureAwait(false))
            && await OwnMoveAsync(db, campaignId, contactId, thread, moment, now, ct).ConfigureAwait(false))
        {
            return false;
        }

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

    /// <summary>
    /// Whether the line's moment may be Jason's own letter moving the thread: it is not later than a moment one of Jason's
    /// letters to the person went out, one that has come by <paramref name="now"/>.
    /// </summary>
    public static async Task<bool> OwnMoveAsync(
        JasonDbContext db, int campaignId, int contactId, string? thread, DateTime moved, DateTime now, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(db);
        object named = thread is null ? DBNull.Value : thread;
        var went = await db.Database
            .SqlQueryRaw<int>(OwnMoveSql, campaignId, contactId, Touches, named, Answer, Cancel, Moment(moved), Moment(now))
            .SingleAsync(ct)
            .ConfigureAwait(false);
        return went > 0;
    }

    /// <summary>
    /// Whether the line knows who moved the thread, and it was the other side: the stored event it names is a
    /// <c>thread_moved</c> whose <c>moved_by</c> is <c>other_side</c>. An event that names no side, or none found, does not.
    /// </summary>
    public static async Task<bool> OtherSideNamedAsync(JasonDbContext db, string eventId, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(db);
        var named = await db.Database
            .SqlQueryRaw<int>(OtherSideSql, eventId, ReadEvent.ThreadMoved, ReadEvent.MovedByOtherSide)
            .SingleAsync(ct)
            .ConfigureAwait(false);
        return named > 0;
    }

    private static string Moment(DateTime moment) =>
        DateTime.SpecifyKind(moment, DateTimeKind.Utc).ToString("yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'", CultureInfo.InvariantCulture);
}
