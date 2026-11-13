using System.Globalization;
using System.Text.Json;
using Jason.Contracts.Operations;
using Jason.Runtime.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Jason.Runtime.Routing;

/// <summary>
/// The sends a route change must not strand: a send of a campaign that is not archived, which succeeded with the
/// provider's reference, whose arguments name a <c>send_after</c> still to come, and which no succeeded cancel names. Its
/// cancel and its status read go only where it ran (<see cref="RouteResolver.ForSend"/>), so while the provider holds it
/// for that moment a route naming that plugin and account is what keeps it cancellable. All of it is the runtime's own
/// record: the send's work item, the latest succeeded attempt's provenance and the campaign's cancels; nothing is asked of
/// the provider.
/// </summary>
internal static class HeldSends
{
    /// <summary>The operation that takes a send back by its work item.</summary>
    public const string Cancel = "message.cancel";

    /// <summary>The operation that reads a send by its work item.</summary>
    public const string Status = "message.status";

    /// <summary>
    /// One read: the operations that make a send are passed as a JSON array, the catalog's own answer, so no list of them
    /// is written here. An archived campaign's cancel is cancelled by the dispatch, never run, so its sends keep nothing.
    /// The moment itself is compared by the caller's clock, not by the database's.
    /// </summary>
    private const string Sql = """
        SELECT c.public_id AS Campaign, s.public_id AS WorkItem, s.operation AS Operation,
               json_extract(s.context_json, '$.input.send_after') AS SendAfter,
               json_extract(a.provenance_json, '$.plugin_id') AS PluginId,
               json_extract(a.provenance_json, '$.binding_identity') AS BindingIdentity
        FROM work_items AS s
        JOIN campaigns AS c ON c.id = s.campaign_id AND c.status <> 'archived'
        JOIN attempts AS a ON a.work_item_id = s.id AND a.status = 'succeeded'
             AND a.number = (SELECT max(b.number) FROM attempts AS b WHERE b.work_item_id = s.id AND b.status = 'succeeded')
        WHERE s.status = 'succeeded'
          AND s.operation IN (SELECT value FROM json_each({0}))
          AND json_type(s.context_json, '$.input.send_after') = 'text'
          AND json_extract(a.provenance_json, '$.external_ids_returned.send') IS NOT NULL
          AND NOT EXISTS (
              SELECT 1 FROM work_items AS k
              WHERE k.campaign_id = s.campaign_id AND k.operation = {1} AND k.status = 'succeeded'
                AND json_extract(k.context_json, '$.input.send') = s.public_id)
        ORDER BY s.campaign_id, s.id
        """;

    private static readonly string Makers = JsonSerializer.Serialize(
        OperationCatalog.All.Select(contract => contract.Id).Where(CanonicalInput.MakesASend).ToArray());

    /// <summary>
    /// Every send the provider still holds for a moment after <paramref name="now"/>, in campaign order. A moment no parser
    /// reads names no moment: creation held it to the contract's pattern, so only a row no build wrote could carry one.
    /// </summary>
    public static async Task<IReadOnlyList<HeldSend>> ReadAsync(JasonDbContext db, DateTimeOffset now, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(db);

        var rows = await db.Database.SqlQueryRaw<HeldSendRow>(Sql, Makers, Cancel).ToListAsync(ct).ConfigureAwait(false);
        return
        [
            .. rows
                .Where(row => row.PluginId is not null
                    && DateTimeOffset.TryParse(row.SendAfter, CultureInfo.InvariantCulture, DateTimeStyles.None, out var moment)
                    && moment > now)
                .Select(row => new HeldSend(row.Campaign, row.WorkItem, row.Operation, row.SendAfter, row.PluginId!, row.BindingIdentity)),
        ];
    }
}

/// <summary>A send a route change must not strand: its campaign, its work item and operation, its moment, and where it ran.</summary>
internal sealed record HeldSend(string Campaign, string WorkItem, string Operation, string SendAfter, string PluginId, string? BindingIdentity);

/// <summary>One row of the read, before its moment is compared.</summary>
internal sealed record HeldSendRow(string Campaign, string WorkItem, string Operation, string SendAfter, string? PluginId, string? BindingIdentity);
