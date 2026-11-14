using System.Runtime.CompilerServices;
using Jason.Runtime.Persistence;
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
/// at or after the line's move, or stands while a responder the runtime created for that thread succeeded with an
/// attempt that started after the line was written (our own send into a replied thread moves it, and the responder that
/// wakes on it finds the verdict standing and records nothing). The attempt's start is its <c>workitem_processing</c>
/// line, so the order is the chronicle's own, never a clock. When the only verdicts that would judge a line say no
/// nature, the contact awaits the nature rather than a judgement. A waiting reply outranks warmth: a warm contact's new
/// reply may be a request to stop, so it is judged before anything else is sent. A provider event's <c>reply_detected</c>
/// line is not a reply that waits. A line that names nobody makes wait only a contact a standing verdict names on its thread.
/// </para>
/// <para>
/// Every read is written SQL that names its index, and a test pins each plan and the columns each search is bounded by,
/// as the standing approvals' reads are: the contact's own lines and verdicts through the contact index; the verdicts
/// on a thread through the verdicts' thread index by the campaign and the thread, and the one that replaced a verdict
/// through the same index by its key; the observed lines on a thread through their thread index; a responder through
/// the campaign's items of the same person, its attempts by item, and an attempt's start through the item's own lines.
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
    /// judges it: about a message at or after its move, or standing while a responder the runtime
    /// created for the thread succeeded with an attempt that started after it. Correlated with the line <paramref name="line"/> alone and the
    /// contact the responder is about, <paramref name="contact"/>, an expression. The verdicts are read by the line's
    /// thread, through the verdicts' thread index.
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
                      AND EXISTS (SELECT 1 FROM attempts AS a INDEXED BY ix_attempts_work_item_id_number
                          WHERE a.work_item_id = r.id AND a.status = 'succeeded'
                            AND EXISTS (SELECT 1 FROM journal AS s INDEXED BY ix_journal_work_item_id_public_id
                                WHERE s.work_item_id = r.public_id AND s.kind = 'workitem_processing'
                                  AND s.attempt_id = a.public_id AND s.id > {line}.id)))))
        """;
}
