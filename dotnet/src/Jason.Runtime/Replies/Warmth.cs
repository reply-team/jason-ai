using System.Runtime.CompilerServices;
using Jason.Runtime.Dispatch;
using Jason.Runtime.Persistence;
using Jason.Runtime.WorkItems;
using Microsoft.EntityFrameworkCore;

namespace Jason.Runtime.Replies;

/// <summary>Where a contact stands in one campaign, as the campaign's own replies and verdicts say.</summary>
public enum WarmthState
{
    /// <summary>No reply of theirs in this campaign was judged a person's answer, and none waits.</summary>
    Cold,

    /// <summary>A verdict that stands in this campaign judged a reply of theirs a person's answer, and none waits.</summary>
    Warm,

    /// <summary>A reply of theirs the campaign's own read saw has not been judged since it moved.</summary>
    AwaitingJudgement,

    /// <summary>
    /// A reply of theirs was judged, but only by a verdict that does not say what the reply is: recorded before the
    /// runtime took a nature, or by a writer that left it out. No reply of theirs waits for a judgement.
    /// </summary>
    AwaitingNature,
}

/// <summary>
/// Whether a contact is cold or warm in one campaign, or waits on a verdict: the one fact the claim reads about a person
/// before it sends to them. It is read from the chronicle alone — the campaign's own <c>inbound_reply_observed</c> lines
/// and its <c>reply_classified</c> verdicts — and from the responders the runtime created for a thread; nothing is
/// stored for it, and nothing here reads a category, a reaction or the words of a reply.
/// </summary>
/// <remarks>
/// <para>
/// WARM: a verdict that stands in the campaign (no later verdict under its key), whose nature is <c>person</c>, which
/// names the contact itself in its body, on a thread the campaign's own read saw move: a line the inbound read wrote, or
/// one the events store said by the campaign's own pin. A verdict on a thread the campaign heard of only through the
/// contact's pin, or of no observed thread at all (a verdict recorded by hand included), warms nobody; nor does a verdict
/// whose body names nobody or another person, whatever the journal's contact column holds: the body must name this very
/// contact. Once warm, always warm: no moment is read. Channels
/// are one bucket, and another campaign's verdicts are never read.
/// </para>
/// <para>
/// AWAITING: a line of the campaign's own read that names the contact and that no standing verdict on its thread has
/// judged. A verdict judges a line when it stands, says a nature, and is about a message
/// at or after the line's move, or by the responder's arm (our own send into a replied thread moves it, and the
/// responder that wakes on it finds the verdict standing and records nothing). The responder's arm, in one sentence: a
/// verdict with a nature stands while a real <c>conversation.get</c> of the line's own thread was made after the line,
/// that read succeeded, and the responder that waited on it ran after it succeeded and succeeded itself.
/// Each is a fact of the chronicle. The read is the one row the responder's <c>inbound_reply.read</c> names: a
/// provider operation the system created in the campaign, its input naming the thread, with an attempt that succeeded
/// at the line's plugin; the responder is the system's too, about the contact, on the same plugin and thread, and
/// created after the line. "Made after" is the read's <c>workitem_created</c> line above the line's; "ran after" is a
/// <c>workitem_processing</c> line of the responder above the read's <c>workitem_succeeded</c> line. A responder with
/// no read (no route named the plugin that saw the thread, or the read refused its identifier), over a read that
/// failed, was cancelled or expired, or released at its wait's ceiling while the read was still open, saw no
/// transcript and judges no line. A responder or a read whose context anyone but the system patched judges nothing:
/// <c>workitem.update</c> may rewrite any key of an open item's context, the read named, the plugin and the thread
/// included. The dispatcher's own patch of a further move onto the responder is the system's and disqualifies nothing;
/// a move patched onto a responder that already existed is never judged by that one, and the next responder created
/// on the thread judges every earlier line of it at once. Every order is the journal's own, never a clock. When the
/// only verdicts that would judge a line say no
/// nature, the contact awaits the nature rather than a judgement. A waiting reply outranks warmth: a warm contact's new
/// reply may be a request to stop, so it is judged before anything else is sent. A provider event's <c>reply_detected</c>
/// line is not a reply that waits. A line that names nobody makes wait only a contact a standing verdict names on its thread,
/// and only a responder that names that contact judges it: the responder created for the unnamed line is about nobody and
/// clears nothing, so the contact waits for a verdict about a message at or after the move, or for a later responder that
/// names them.
/// </para>
/// <para>
/// Every read is written SQL that names its index, and a test pins each plan and the columns each search is bounded by,
/// as the standing approvals' reads are: the contact's own lines and verdicts through the contact index; the verdicts
/// on a thread through the verdicts' thread index by the campaign and the thread, and the one that replaced a verdict
/// through the same index by its key; the observed lines on a thread through their thread index; a responder through
/// the campaign's items of the same person, and its creation, its start and any patch of its context through the
/// item's own lines; the read it names as one row by its public id, its attempts through the item's attempts, and its
/// creation, its success and any patch of its context through its own lines.
/// None is bounded by the campaign's whole chronicle.
/// </para>
/// </remarks>
public static class Warmth
{
    /// <summary>
    /// Where the contact stands in the campaign, by internal keys as the claim holds them: awaiting a judgement first,
    /// then awaiting a nature, then warm, else cold.
    /// </summary>
    public static async Task<WarmthState> OfAsync(JasonDbContext db, int campaignId, int contactId, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(db);
        var waiting = await Awaiting(db, campaignId, contactId).SingleAsync(ct).ConfigureAwait(false);
        if (waiting == WaitsForJudgement)
        {
            return WarmthState.AwaitingJudgement;
        }

        if (waiting == WaitsForNature)
        {
            return WarmthState.AwaitingNature;
        }

        return await Warming(db, campaignId, contactId).AnyAsync(ct).ConfigureAwait(false)
            ? WarmthState.Warm
            : WarmthState.Cold;
    }

    private const int WaitsForNature = 1;

    private const int WaitsForJudgement = 2;

    /// <summary>
    /// The worst wait among the contact's own lines that no verdict with a nature has judged: 2 when one has no verdict
    /// that judges it at all, 1 when every such line is judged only by verdicts that say no nature, 0 when none waits.
    /// </summary>
    /// <remarks>
    /// The lines that name nobody are reached from the contact's own standing verdicts, through the thread each names,
    /// never from the campaign's unnamed lines: inbound sync writes one for every thread of the account that moved, so a
    /// campaign may hold thousands, and only those on a thread the contact's verdicts name are read.
    /// </remarks>
    internal static IQueryable<int> Awaiting(JasonDbContext db, int campaignId, int contactId) =>
        db.Database.SqlQuery<int>(FormattableStringFactory.Create(
            $$"""
            SELECT coalesce(max(w), 0) AS "Value" FROM (
                SELECT CASE WHEN {{Judged("o", "IS NULL", "o.contact_id")}} THEN {{WaitsForNature}} ELSE {{WaitsForJudgement}} END AS w
                FROM journal AS o INDEXED BY ix_journal_contact_id_public_id
                WHERE o.contact_id = {0} AND o.campaign_id = {1} AND o.kind = 'inbound_reply_observed'
                  AND coalesce(json_extract(o.new_json, '$.attributed_by'), 'campaign_pin') = 'campaign_pin'
                  AND NOT {{Judged("o", "IS NOT NULL", "o.contact_id")}}
                UNION ALL
                SELECT CASE WHEN {{Judged("u", "IS NULL", "{0}")}} THEN {{WaitsForNature}} ELSE {{WaitsForJudgement}} END AS w
                FROM journal AS n INDEXED BY ix_journal_contact_id_public_id
                CROSS JOIN journal AS u INDEXED BY ix_journal_campaign_thread
                WHERE n.contact_id = {0} AND n.campaign_id = {1} AND n.kind = 'reply_classified'
                  AND json_extract(n.new_json, '$.contact_id') = (SELECT public_id FROM contacts WHERE id = {0})
                  AND {{Stands("n", "nl")}}
                  AND u.campaign_id = n.campaign_id AND u.kind = 'inbound_reply_observed'
                  AND json_extract(u.new_json, '$.thread') = json_extract(n.new_json, '$.thread')
                  AND json_extract(u.new_json, '$.plugin') = json_extract(n.new_json, '$.plugin')
                  AND u.contact_id IS NULL AND json_extract(u.new_json, '$.contact_id') IS NULL
                  AND coalesce(json_extract(u.new_json, '$.attributed_by'), 'campaign_pin') = 'campaign_pin'
                  AND NOT {{Judged("u", "IS NOT NULL", "{0}")}})
            """,
            contactId,
            campaignId));

    /// <summary>
    /// A verdict that names the contact, stands, says <c>person</c>, on a thread the campaign's own read saw move.
    /// </summary>
    internal static IQueryable<int> Warming(JasonDbContext db, int campaignId, int contactId) =>
        db.Database.SqlQuery<int>(FormattableStringFactory.Create(
            $$"""
            SELECT v.id AS "Value" FROM journal AS v INDEXED BY ix_journal_contact_id_public_id
            WHERE v.contact_id = {0} AND v.campaign_id = {1} AND v.kind = 'reply_classified'
              AND json_extract(v.new_json, '$.contact_id') = (SELECT public_id FROM contacts WHERE id = {0})
              AND json_extract(v.new_json, '$.nature') = 'person'
              AND {{Stands("v", "later")}}
              AND EXISTS (SELECT 1 FROM journal AS o INDEXED BY ix_journal_campaign_thread
                  WHERE o.campaign_id = v.campaign_id AND o.kind = 'inbound_reply_observed'
                    AND json_extract(o.new_json, '$.thread') = json_extract(v.new_json, '$.thread')
                    AND json_extract(o.new_json, '$.plugin') = json_extract(v.new_json, '$.plugin')
                    AND coalesce(json_extract(o.new_json, '$.attributed_by'), 'campaign_pin') = 'campaign_pin')
            LIMIT 1
            """,
            contactId,
            campaignId));

    /// <summary>
    /// Whether the verdict <paramref name="verdict"/> stands: no later verdict of its campaign under its key, read by the
    /// key through the verdicts' thread index. The key is a digest of the plugin, the thread and the message, so a later
    /// verdict under it is on the same thread.
    /// </summary>
    private static string Stands(string verdict, string later) => $"""
        NOT EXISTS (SELECT 1 FROM journal AS {later} INDEXED BY ix_journal_campaign_verdict_thread
            WHERE {later}.kind = 'reply_classified' AND {later}.campaign_id = {verdict}.campaign_id
              AND json_extract({later}.new_json, '$.thread') = json_extract({verdict}.new_json, '$.thread')
              AND {later}.key = {verdict}.key AND {later}.id > {verdict}.id)
        """;

    /// <summary>
    /// Whether a standing verdict on the observed line <c>o</c>'s thread, whose nature passes <paramref name="nature"/>,
    /// judges it: about a message at or after its move, or by the responder's arm, whose rule in one sentence is: a real
    /// <c>conversation.get</c> of the line's own thread was made after the line, that read succeeded, and the responder
    /// that waited on it ran after it succeeded and succeeded itself. Neither the responder nor the read may carry a patch
    /// of its context by anyone but the system; the dispatcher's own patch of a further move is the system's and counts
    /// for nothing against it. Correlated with the line <paramref name="line"/> alone and the contact the responder is
    /// about, <paramref name="contact"/>, an expression. The verdicts are read by the line's thread, through the verdicts'
    /// thread index.
    /// </summary>
    private static string Judged(string line, string nature, string contact) => $"""
        EXISTS (SELECT 1 FROM journal AS v INDEXED BY ix_journal_campaign_verdict_thread
            WHERE v.kind = 'reply_classified' AND v.campaign_id = {line}.campaign_id
              AND json_extract(v.new_json, '$.thread') = json_extract({line}.new_json, '$.thread')
              AND json_extract(v.new_json, '$.plugin') = json_extract({line}.new_json, '$.plugin')
              AND json_extract(v.new_json, '$.nature') {nature}
              AND {Stands("v", "later")}
              AND (julianday(json_extract(v.new_json, '$.message_at')) >= julianday(json_extract({line}.new_json, '$.last_activity_at'))
                OR EXISTS (SELECT 1 FROM work_items AS r INDEXED BY ix_work_items_campaign_id_contact_id
                    WHERE r.campaign_id = {line}.campaign_id AND r.contact_id = {contact}
                      AND r.role = 'responder' AND r.created_by_type = 'system' AND r.status = 'succeeded'
                      AND json_extract(r.context_json, '$.inbound_reply.plugin') = json_extract({line}.new_json, '$.plugin')
                      AND json_extract(r.context_json, '$.inbound_reply.thread') = json_extract({line}.new_json, '$.thread')
                      AND EXISTS (SELECT 1 FROM journal AS c INDEXED BY ix_journal_work_item_id_public_id
                          WHERE c.work_item_id = r.public_id AND c.kind = 'workitem_created' AND c.id > {line}.id)
                      AND {PatchedOnlyBySystem("r", "rt")}
                      AND {Read("r", line)})))
        """;

    /// <summary>
    /// Whether the read the responder <paramref name="responder"/> names in its <c>inbound_reply.read</c> is a real
    /// <c>conversation.get</c> of the line <paramref name="line"/>'s thread, made after the line, that succeeded before the
    /// responder ran: one row by its public id, the system's, of the line's campaign, its input naming the thread, an
    /// attempt of it that succeeded at the line's plugin; its own <c>workitem_created</c> line above the line's; and a
    /// <c>workitem_processing</c> line of the responder above the read's <c>workitem_succeeded</c> line. The wait from
    /// creation is released at its ceiling too, with the read still open, so a responder may run before its read ended;
    /// one that did saw no transcript. A read whose context anyone but the system patched counts for nothing. The item's
    /// own lines are read through the journal's work item index, its attempts through theirs.
    /// </summary>
    private static string Read(string responder, string line) => $"""
        EXISTS (SELECT 1 FROM work_items AS g INDEXED BY ix_work_items_public_id
            WHERE g.public_id = json_extract({responder}.context_json, '$.inbound_reply.read')
              AND g.campaign_id = {responder}.campaign_id AND g.kind = 'provider_op' AND g.operation = '{Responder.ReadOperation}'
              AND g.created_by_type = 'system' AND g.status = 'succeeded'
              AND json_extract(g.context_json, '$.{WorkItemService.InputKey}.thread.external_id') = json_extract({line}.new_json, '$.thread')
              AND EXISTS (SELECT 1 FROM attempts AS ga INDEXED BY ix_attempts_work_item_id_number
                  WHERE ga.work_item_id = g.id AND ga.status = 'succeeded'
                    AND json_extract(ga.provenance_json, '$.plugin_id') = json_extract({line}.new_json, '$.plugin'))
              AND EXISTS (SELECT 1 FROM journal AS gc INDEXED BY ix_journal_work_item_id_public_id
                  WHERE gc.work_item_id = g.public_id AND gc.kind = 'workitem_created' AND gc.id > {line}.id)
              AND {PatchedOnlyBySystem("g", "gt")}
              AND EXISTS (SELECT 1 FROM journal AS gs INDEXED BY ix_journal_work_item_id_public_id
                  WHERE gs.work_item_id = g.public_id AND gs.kind = 'workitem_succeeded'
                    AND EXISTS (SELECT 1 FROM journal AS rp INDEXED BY ix_journal_work_item_id_public_id
                        WHERE rp.work_item_id = {responder}.public_id AND rp.kind = 'workitem_processing' AND rp.id > gs.id)))
        """;

    /// <summary>
    /// Whether no line of the item <paramref name="item"/> says its context was patched by anyone but the system:
    /// <c>workitem.update</c> may rewrite any key of an open item's context, and a person or a role is never the system,
    /// which the runtime alone may act as. Read through the item's own lines.
    /// </summary>
    private static string PatchedOnlyBySystem(string item, string patch) => $"""
        NOT EXISTS (SELECT 1 FROM journal AS {patch} INDEXED BY ix_journal_work_item_id_public_id
            WHERE {patch}.work_item_id = {item}.public_id AND {patch}.kind = 'workitem_context_updated' AND {patch}.actor_type <> 'system')
        """;
}
