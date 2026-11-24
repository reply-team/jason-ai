using System.Globalization;
using System.Text.Json;
using Jason.Contracts.Operations;
using Jason.Runtime.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Jason.Runtime.Routing;

/// <summary>
/// The sends the provider still holds, which a route change must not strand and which a stopped line takes back
/// (<see cref="HeldSendCancels"/>): a send of a campaign that is not archived, which succeeded with the
/// provider's reference, and whose own result says the provider can cancel it: <c>cancelable: true</c>, read as the
/// operation's document defines the flag, where "absent reads as false", so a result that says nothing holds nothing, as
/// one that says false does. Its moment is the one the provider took it for, its result's <c>accepted_for</c>, where it
/// names one, and otherwise the <c>send_after</c> its arguments named; an estimate the provider gives is never read as a
/// moment. Only a moment known and still to come holds: a send whose moment has passed is held no longer, and a send with
/// no moment known holds nothing, since a provider answers a send it will make as soon as it can the same way as one it
/// keeps for a schedule of its own, and a hold for the first would outlast the send by as long as nobody reads it. A send
/// lets its route go as soon as the provider has said it holds the send no longer: a cancel of it succeeded; a cancel of
/// it ended with the answer that there was nothing to cancel (<see cref="NothingToCancel"/>); a status read of it ended
/// with the answer that the provider holds no such send (<see cref="NothingToRead"/>); a status read of it that succeeded
/// says it is final (<see cref="FinalStates"/>); or the latest status read of it that succeeded says it can no longer be
/// cancelled. A cancel or a read still to run, or one that failed for any other reason, keeps it held. Its cancel and its status read go only where it ran (<see cref="RouteResolver.ForSend"/>),
/// so while the provider holds it a route naming that plugin and account is what keeps it cancellable. All of it is the
/// runtime's own record: the send's work item, the latest succeeded attempt's provenance and the campaign's cancels and
/// reads; nothing is asked of the provider.
/// <para>
/// The same definition, with its moment test turned round (<see cref="HeldMoment.Passed"/>), is the deferred send whose
/// moment has passed, which the deferred read step reads to learn what became of it: no longer held, and not yet known to
/// have gone. For it a send is settled only by what says what became of it: a succeeded cancel, an answer that the
/// provider holds no such send, by a cancel or a read, or a final read. A cancel answered that there was nothing pending,
/// and a read that says the send can no longer be cancelled, say only that it can no longer be taken back, so they settle
/// nothing there. And the definition alone (<see cref="HeldMoment.Any"/>), at any moment, is the send whose read's answer
/// that it failed or was cancelled is said, unless the campaign's own cancel of it succeeded.
/// </para>
/// </summary>
internal static class HeldSends
{
    /// <summary>The operation that takes a send back by its work item.</summary>
    public const string Cancel = "message.cancel";

    /// <summary>The operation that reads a send by its work item.</summary>
    public const string Status = "message.status";

    /// <summary>
    /// The failures of <see cref="Cancel"/> its document declares as the provider's answer that it holds nothing to cancel:
    /// the send already went or failed, or the provider holds no send under the reference.
    /// </summary>
    public static IReadOnlyList<string> NothingToCancel { get; } = ["send_not_pending", "send_not_found"];

    /// <summary>
    /// The failures of <see cref="Status"/> its document declares as the provider's answer that it holds no such send.
    /// </summary>
    public static IReadOnlyList<string> NothingToRead { get; } = ["send_not_found"];

    /// <summary>The states of <see cref="Status"/>'s answer its document calls final: every one but <c>queued</c>.</summary>
    public static IReadOnlyList<string> FinalStates { get; } = ["sent", "cancelled", "failed", "unknown"];

    /// <summary>
    /// One read, bounded by the moment: only a send whose moment stands where the mode asks, against a bound the caller
    /// passes, so the clock stays the caller's and never the database's, reaches the question of what released it. The
    /// operations that make a send are passed as a JSON array, the catalog's own answer, so no list of them is written here;
    /// so are the answers that release one, and a cancel's answers are passed for the mode, since a send whose moment has
    /// passed is not settled by a cancel answered that there was nothing pending. SQLite's date functions read the
    /// contract's <c>Z</c> and <c>±hh:mm</c> forms and its fraction, and a moment they cannot read is NULL and names no
    /// moment. A final state frees a send whichever read said it, since a send that went or failed does not come back; that
    /// it can no longer be cancelled is read from the latest read alone, since a provider may hold a send it was about to
    /// send again, and only while its moment is still to come. The latest is the one that finished last. The moment comes
    /// back in UTC to the millisecond. An archived campaign's cancel is cancelled by the dispatch, never run, so its sends
    /// keep nothing. Where a bound of the past is given, a send handed over before it is left out by its own column, before
    /// any of its JSON is read.
    /// </summary>
    private const string Sql = """
        SELECT Campaign, WorkItem, Operation, strftime('%Y-%m-%dT%H:%M:%fZ', Moment) AS Moment, PluginId, BindingIdentity
        FROM (
            SELECT c.public_id AS Campaign, s.public_id AS WorkItem, s.operation AS Operation, s.id AS Id, s.campaign_id AS CampaignRow,
                   coalesce(json_extract(s.result_json, '$.items[0].accepted_for'), json_extract(s.context_json, '$.input.send_after')) AS Moment,
                   json_extract(a.provenance_json, '$.plugin_id') AS PluginId,
                   json_extract(a.provenance_json, '$.binding_identity') AS BindingIdentity
            FROM work_items AS s
            JOIN campaigns AS c ON c.id = s.campaign_id AND c.status <> 'archived'
            JOIN attempts AS a ON a.work_item_id = s.id AND a.status = 'succeeded'
                 AND a.number = (SELECT max(b.number) FROM attempts AS b WHERE b.work_item_id = s.id AND b.status = 'succeeded')
            WHERE s.status = 'succeeded'
              AND ({12} IS NULL OR s.finished_at >= {12})
              AND ({7} IS NULL OR s.campaign_id = {7})
              AND ({8} IS NULL OR s.contact_id IN (SELECT value FROM json_each({8})))
              AND ({11} IS NULL OR s.public_id = {11})
              AND s.operation IN (SELECT value FROM json_each({0}))
              AND json_type(s.result_json, '$.items[0].cancelable') = 'true'
              AND json_extract(a.provenance_json, '$.external_ids_returned.send') IS NOT NULL
        ) AS held
        WHERE CASE {9}
                WHEN 'to_come' THEN julianday(Moment) > julianday({1})
                WHEN 'passed' THEN julianday(Moment) <= julianday({1}) AND ({10} IS NULL OR julianday(Moment) >= julianday({10}))
                ELSE julianday(Moment) IS NOT NULL
              END
          AND PluginId IS NOT NULL
          AND NOT EXISTS (
              SELECT 1 FROM work_items AS k
              WHERE k.campaign_id = held.CampaignRow
                AND k.operation IN ({2}, {3})
                AND json_extract(k.context_json, '$.input.send') = held.WorkItem
                AND (
                    (k.operation = {2} AND k.status = 'succeeded')
                    OR ({9} <> 'any' AND (
                        (k.operation = {2} AND k.status = 'failed'
                            AND json_extract(k.last_error_json, '$.code') IN (SELECT value FROM json_each({4})))
                        OR (k.operation = {3} AND k.status = 'failed'
                            AND json_extract(k.last_error_json, '$.code') IN (SELECT value FROM json_each({6})))
                        OR (k.operation = {3} AND k.status = 'succeeded'
                            AND json_extract(k.result_json, '$.state') IN (SELECT value FROM json_each({5})))
                        OR ({9} = 'to_come' AND k.operation = {3} AND k.status = 'succeeded'
                            AND json_type(k.result_json, '$.cancelable') = 'false'
                            AND k.id = (
                                SELECT r.id FROM work_items AS r
                                WHERE r.campaign_id = held.CampaignRow AND r.operation = {3} AND r.status = 'succeeded'
                                  AND json_extract(r.context_json, '$.input.send') = held.WorkItem
                                ORDER BY r.finished_at DESC, r.id DESC
                                LIMIT 1))))))
        ORDER BY CampaignRow, Id
        """;

    private static readonly string Makers = JsonSerializer.Serialize(
        OperationCatalog.All.Select(contract => contract.Id).Where(CanonicalInput.MakesASend).ToArray());

    private static readonly string Gone = JsonSerializer.Serialize(NothingToCancel);

    private static readonly string Final = JsonSerializer.Serialize(FinalStates);

    private static readonly string Unread = JsonSerializer.Serialize(NothingToRead);

    /// <summary>Every send the provider still holds for a moment after <paramref name="now"/>, in campaign order.</summary>
    public static Task<IReadOnlyList<HeldSend>> ReadAsync(JasonDbContext db, DateTimeOffset now, CancellationToken ct) =>
        ReadAsync(db, HeldMoment.ToCome, now, since: null, campaignId: null, contactIds: null, send: null, ct);

    /// <summary>
    /// The same read, narrowed to one campaign's sends to the people named, by their rows: what the provider still holds
    /// for them there, by this one definition, so a take-back and a route change never disagree about what is held.
    /// </summary>
    public static Task<IReadOnlyList<HeldSend>> ReadForAsync(JasonDbContext db, int campaignId, IReadOnlyCollection<int> contactIds, DateTimeOffset now, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(contactIds);
        return ReadAsync(db, HeldMoment.ToCome, now, since: null, campaignId, contactIds, send: null, ct);
    }

    /// <summary>
    /// Every send the definition holds whose moment is no later than <paramref name="passedBy"/> and, where given, no
    /// earlier than <paramref name="since"/>, handed over, where given, no earlier than <paramref name="handedSince"/>, and
    /// of which nothing yet says what became of it, in campaign order: the deferred sends whose moment has passed.
    /// <paramref name="campaignId"/> and <paramref name="send"/>, where given, narrow it to one campaign's row and one send.
    /// </summary>
    public static Task<IReadOnlyList<HeldSend>> ReadPassedAsync(
        JasonDbContext db,
        DateTimeOffset passedBy,
        DateTimeOffset? since,
        CancellationToken ct,
        int? campaignId = null,
        string? send = null,
        DateTimeOffset? handedSince = null) =>
        ReadAsync(db, HeldMoment.Passed, passedBy, since, campaignId, contactIds: null, send, ct, handedSince);

    /// <summary>
    /// The send of the campaign's row named, where the definition covers it, at any moment, and no cancel of the campaign's
    /// that succeeded names it; otherwise null.
    /// </summary>
    public static async Task<HeldSend?> CoveredAsync(JasonDbContext db, int campaignId, string send, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(send);
        var found = await ReadAsync(db, HeldMoment.Any, DateTimeOffset.MinValue, since: null, campaignId, contactIds: null, send, ct).ConfigureAwait(false);
        return found.Count == 0 ? null : found[0];
    }

    private static async Task<IReadOnlyList<HeldSend>> ReadAsync(
        JasonDbContext db,
        HeldMoment mode,
        DateTimeOffset bound,
        DateTimeOffset? since,
        int? campaignId,
        IReadOnlyCollection<int>? contactIds,
        string? send,
        CancellationToken ct,
        DateTimeOffset? handedSince = null)
    {
        ArgumentNullException.ThrowIfNull(db);

        object campaign = campaignId is { } row ? row : DBNull.Value;
        object people = contactIds is null ? DBNull.Value : JsonSerializer.Serialize(contactIds);
        object from = since is { } earliest ? Moment(earliest) : DBNull.Value;
        object handed = handedSince is { } earliestHanded ? earliestHanded.UtcDateTime : DBNull.Value;
        object one = send is null ? DBNull.Value : send;
        var rows = await db.Database
            .SqlQueryRaw<HeldSendRow>(
                Sql,
                Makers,
                Moment(bound),
                Cancel,
                Status,
                mode == HeldMoment.ToCome ? Gone : Unread,
                Final,
                Unread,
                campaign,
                people,
                Word(mode),
                from,
                one,
                handed)
            .ToListAsync(ct)
            .ConfigureAwait(false);
        return rows
            .Select(row => new HeldSend(
                row.Campaign,
                row.WorkItem,
                row.Operation,
                DateTimeOffset.ParseExact(row.Moment, "yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal),
                row.PluginId,
                row.BindingIdentity))
            .ToList();
    }

    private static string Moment(DateTimeOffset moment) =>
        moment.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'", CultureInfo.InvariantCulture);

    private static string Word(HeldMoment mode) => mode switch
    {
        HeldMoment.ToCome => "to_come",
        HeldMoment.Passed => "passed",
        _ => "any",
    };
}

/// <summary>Which side of a send's moment the one definition of a held send is asked about.</summary>
internal enum HeldMoment
{
    /// <summary>A moment still to come: what the provider holds now, which a route change and a take-back read.</summary>
    ToCome,

    /// <summary>A moment passed by the caller's bound: a deferred send the provider should have made by now.</summary>
    Passed,

    /// <summary>Any moment: the definition alone, released by nothing but the campaign's own cancel that succeeded.</summary>
    Any,
}

/// <summary>
/// A send a route change must not strand: its campaign, its work item and operation, the moment it is held for, and where
/// it ran. Nothing the send says to a person is kept here: not its recipient, its subject or its text.
/// </summary>
internal sealed record HeldSend(string Campaign, string WorkItem, string Operation, DateTimeOffset Moment, string PluginId, string? BindingIdentity);

/// <summary>One row of the read: the moment in UTC to the millisecond, as the database wrote it.</summary>
internal sealed record HeldSendRow(string Campaign, string WorkItem, string Operation, string Moment, string PluginId, string? BindingIdentity);
