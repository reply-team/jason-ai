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
/// that names nobody, whatever the journal's contact column holds. Once warm, always warm: no moment is read. Channels
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
/// line is not a reply that waits, and a line that names nobody parks nobody.
/// </para>
/// <para>
/// Every read is written SQL that names its index, and a test pins each plan, as the standing approvals' reads are:
/// the person's own lines through the contact index, the verdicts, and the one that replaced each, through the campaign's own lines, the campaign's own observed
/// lines through their filtered index, a responder through the campaign's items of the same person, its attempts by
/// item, and an attempt's start through the item's own lines.
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
    internal static IQueryable<int> Awaiting(JasonDbContext db, int campaignId, int contactId) =>
        db.Database.SqlQuery<int>(FormattableStringFactory.Create(
            $$"""
            SELECT coalesce(max(CASE WHEN {{Judged("IS NULL")}} THEN {{WaitsForNature}} ELSE {{WaitsForJudgement}} END), 0) AS "Value"
            FROM journal AS o INDEXED BY ix_journal_contact_id_public_id
            WHERE o.contact_id = {0} AND o.campaign_id = {1} AND o.kind = 'inbound_reply_observed'
              AND coalesce(json_extract(o.new_json, '$.attributed_by'), 'campaign_pin') = 'campaign_pin'
              AND NOT {{Judged("IS NOT NULL")}}
            """,
            contactId,
            campaignId));

    /// <summary>
    /// A verdict that names the contact, stands, says <c>person</c>, on a thread the campaign's own read saw move.
    /// </summary>
    internal static IQueryable<int> Warming(JasonDbContext db, int campaignId, int contactId) =>
        db.Database.SqlQuery<int>($"""
            SELECT v.id AS "Value" FROM journal AS v INDEXED BY ix_journal_contact_id_public_id
            WHERE v.contact_id = {contactId} AND v.campaign_id = {campaignId} AND v.kind = 'reply_classified'
              AND json_extract(v.new_json, '$.contact_id') IS NOT NULL
              AND json_extract(v.new_json, '$.nature') = 'person'
              AND NOT EXISTS (SELECT 1 FROM journal AS later INDEXED BY ix_journal_campaign_id_id
                  WHERE later.kind = 'reply_classified' AND later.id > v.id
                    AND later.campaign_id = v.campaign_id AND later.key = v.key)
              AND EXISTS (SELECT 1 FROM journal AS o INDEXED BY ix_journal_campaign_kind_key
                  WHERE o.campaign_id = v.campaign_id AND o.kind = 'inbound_reply_observed'
                    AND json_extract(o.new_json, '$.plugin') = json_extract(v.new_json, '$.plugin')
                    AND json_extract(o.new_json, '$.thread') = json_extract(v.new_json, '$.thread')
                    AND coalesce(json_extract(o.new_json, '$.attributed_by'), 'campaign_pin') = 'campaign_pin')
            LIMIT 1
            """);

    /// <summary>
    /// Whether a standing verdict on the observed line <c>o</c>'s thread, whose nature passes <paramref name="nature"/>,
    /// judges it: about a message at or after its move, or standing while a responder the runtime
    /// created for the thread succeeded with an attempt that started after it. Correlated with <c>o</c> alone, so it
    /// carries no parameter of its own.
    /// </summary>
    private static string Judged(string nature) => $"""
        EXISTS (SELECT 1 FROM journal AS v INDEXED BY ix_journal_campaign_id_id
            WHERE v.kind = 'reply_classified' AND v.campaign_id = o.campaign_id
              AND json_extract(v.new_json, '$.plugin') = json_extract(o.new_json, '$.plugin')
              AND json_extract(v.new_json, '$.thread') = json_extract(o.new_json, '$.thread')
              AND json_extract(v.new_json, '$.nature') {nature}
              AND NOT EXISTS (SELECT 1 FROM journal AS later INDEXED BY ix_journal_campaign_id_id
                  WHERE later.kind = 'reply_classified' AND later.id > v.id
                    AND later.campaign_id = v.campaign_id AND later.key = v.key)
              AND (julianday(json_extract(v.new_json, '$.message_at')) >= julianday(json_extract(o.new_json, '$.last_activity_at'))
                OR EXISTS (SELECT 1 FROM work_items AS r INDEXED BY ix_work_items_campaign_id_contact_id
                    WHERE r.campaign_id = o.campaign_id AND r.contact_id = o.contact_id
                      AND r.role = 'responder' AND r.created_by_type = 'system' AND r.status = 'succeeded'
                      AND json_extract(r.context_json, '$.inbound_reply.plugin') = json_extract(o.new_json, '$.plugin')
                      AND json_extract(r.context_json, '$.inbound_reply.thread') = json_extract(o.new_json, '$.thread')
                      AND EXISTS (SELECT 1 FROM attempts AS a INDEXED BY ix_attempts_work_item_id_number
                          WHERE a.work_item_id = r.id AND a.status = 'succeeded'
                            AND EXISTS (SELECT 1 FROM journal AS s INDEXED BY ix_journal_work_item_id_public_id
                                WHERE s.work_item_id = r.public_id AND s.kind = 'workitem_processing'
                                  AND s.attempt_id = a.public_id AND s.id > o.id)))))
        """;
}
