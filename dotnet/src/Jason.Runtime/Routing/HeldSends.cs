using System.Globalization;
using System.Text.Json;
using Jason.Contracts.Operations;
using Jason.Runtime.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Jason.Runtime.Routing;

/// <summary>
/// The sends a route change must not strand: a send of a campaign that is not archived, which succeeded with the
/// provider's reference, and whose own result says the provider can cancel it: <c>cancelable: true</c>, read as the
/// operation's document defines the flag, where "absent reads as false", so a result that says nothing holds nothing, as
/// one that says false does. Its moment is the one the provider took it for, its result's <c>accepted_for</c>, where it
/// names one, and otherwise the <c>send_after</c> its arguments named; an estimate the provider gives is never read as a
/// moment. A send whose moment has passed is held no longer. A send with no moment known still holds: the provider holds
/// it for a sending schedule of its own, the ordinary case of a message queued outside sending hours, so it may go at any
/// time and only the provider's own word frees it. A send lets its route go as soon as the provider has said
/// it holds the send no longer: a cancel of it succeeded; a cancel of it ended with the answer that there was nothing to
/// cancel (<see cref="NothingToCancel"/>); or a status read of it that succeeded says it is final
/// (<see cref="FinalStates"/>) or can no longer be cancelled. A cancel or a read still to run, or one that failed for any
/// other reason, keeps it held. Its cancel and its status read go only where it ran (<see cref="RouteResolver.ForSend"/>),
/// so while the provider holds it a route naming that plugin and account is what keeps it cancellable. All of it is the
/// runtime's own record: the send's work item, the latest succeeded attempt's provenance and the campaign's cancels and
/// reads; nothing is asked of the provider.
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

    /// <summary>The states of <see cref="Status"/>'s answer its document calls final: every one but <c>queued</c>.</summary>
    public static IReadOnlyList<string> FinalStates { get; } = ["sent", "cancelled", "failed", "unknown"];

    /// <summary>
    /// One read, bounded by the moment: only a send held for a moment after the caller's clock, which is passed in so the
    /// clock stays the caller's and never the database's, or for none known, reaches the question of what released it. The operations that
    /// make a send are passed as a JSON array, the catalog's own answer, so no list of them is written here; so are the
    /// answers that release one. SQLite's date functions read the contract's <c>Z</c> and <c>±hh:mm</c> forms and its
    /// fraction, and a moment they cannot read is NULL and is no moment known. The moment comes back in UTC to the
    /// millisecond. An archived campaign's cancel is cancelled by the dispatch, never run, so its sends keep nothing.
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
              AND s.operation IN (SELECT value FROM json_each({0}))
              AND json_type(s.result_json, '$.items[0].cancelable') = 'true'
              AND json_extract(a.provenance_json, '$.external_ids_returned.send') IS NOT NULL
        ) AS held
        WHERE (julianday(Moment) IS NULL OR julianday(Moment) > julianday({1}))
          AND PluginId IS NOT NULL
          AND NOT EXISTS (
              SELECT 1 FROM work_items AS k
              WHERE k.campaign_id = held.CampaignRow
                AND k.operation IN ({2}, {3})
                AND json_extract(k.context_json, '$.input.send') = held.WorkItem
                AND (
                    (k.operation = {2} AND k.status = 'succeeded')
                    OR (k.operation = {2} AND k.status = 'failed'
                        AND json_extract(k.last_error_json, '$.code') IN (SELECT value FROM json_each({4})))
                    OR (k.operation = {3} AND k.status = 'succeeded'
                        AND (json_extract(k.result_json, '$.state') IN (SELECT value FROM json_each({5}))
                             OR json_type(k.result_json, '$.cancelable') = 'false'))))
        ORDER BY CampaignRow, Id
        """;

    private static readonly string Makers = JsonSerializer.Serialize(
        OperationCatalog.All.Select(contract => contract.Id).Where(CanonicalInput.MakesASend).ToArray());

    private static readonly string Gone = JsonSerializer.Serialize(NothingToCancel);

    private static readonly string Final = JsonSerializer.Serialize(FinalStates);

    /// <summary>
    /// Every send the provider still holds for a moment after <paramref name="now"/>, or for one of its own, in campaign
    /// order.
    /// </summary>
    public static async Task<IReadOnlyList<HeldSend>> ReadAsync(JasonDbContext db, DateTimeOffset now, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(db);

        var at = now.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'", CultureInfo.InvariantCulture);
        var rows = await db.Database
            .SqlQueryRaw<HeldSendRow>(Sql, Makers, at, Cancel, Status, Gone, Final)
            .ToListAsync(ct)
            .ConfigureAwait(false);
        return rows
            .Select(row => new HeldSend(
                row.Campaign,
                row.WorkItem,
                row.Operation,
                row.Moment is { } moment
                    ? DateTimeOffset.ParseExact(moment, "yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal)
                    : null,
                row.PluginId,
                row.BindingIdentity))
            .ToList();
    }
}

/// <summary>
/// A send a route change must not strand: its campaign, its work item and operation, the moment it is held for (none where
/// the provider holds it for a schedule of its own), and where it ran. Nothing the send says to a person is kept here: not its recipient, its subject or its text.
/// </summary>
internal sealed record HeldSend(string Campaign, string WorkItem, string Operation, DateTimeOffset? Moment, string PluginId, string? BindingIdentity);

/// <summary>One row of the read: the moment in UTC to the millisecond, as the database wrote it, or none known.</summary>
internal sealed record HeldSendRow(string Campaign, string WorkItem, string Operation, string? Moment, string PluginId, string? BindingIdentity);
