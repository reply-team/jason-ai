using System.Globalization;
using System.Runtime.CompilerServices;
using Jason.Contracts.Operations;
using Jason.Runtime.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Jason.Runtime.Events;

/// <summary>
/// Which one campaign, if exactly one, has written to a person through a plugin: the fact the events store marks a reply
/// found by the contact's pin with, <c>sole_sender</c>, so that the thread is that campaign's own. Read once per page, for
/// the people the page's replies name, inside the page's transaction, beside the members the contact rule reads; decided
/// then, carried by the line, and never worked out again at a read.
/// </summary>
/// <remarks>
/// <para>
/// Written to a person: a provider operation of the campaign that writes to a person — its <c>reach</c> is <c>act</c>
/// without a condition, read from the catalog (<see cref="OperationCatalog.WritesToAPerson"/>) — about the contact, that
/// succeeded, with an attempt that succeeded at the plugin, in a campaign that is not archived. An enrolment's reach is
/// conditional and does not count: a sequence's reply is found by the campaign's own pin.
/// </para>
/// <para>
/// A send the provider took for later succeeded when it was taken (<c>accepted</c>), and it counts from then. One that
/// was later taken back, or that the provider later failed, still counts: its item stays succeeded, and nothing here
/// reads what became of it. The cost is accepted: it can only take a mark away, and only where a second campaign shares
/// the person, its send never went, and the person replies all at once. A send that ended <c>ambiguous</c> may have gone
/// and did not succeed: it does not count, and where it was the only one the line stays unmarked, the manager's as before.
/// A campaign archived after the line was written takes no mark back and gives none: the line was decided once.
/// </para>
/// <para>
/// The read names its index and a test pins its plan: the person's own items through the contact index, each one's
/// attempts through the item's attempts, its campaign by its key. It is bounded by the people, never by a campaign's
/// history. The contact ids and the operation ids are written into the text, both values of ours; the plugin is a
/// parameter.
/// </para>
/// </remarks>
public static class SoleSenders
{
    /// <summary>One campaign that has written to one person through the plugin.</summary>
    internal sealed record Writer(int ContactId, int CampaignId);

    /// <summary>
    /// For each of these contacts that exactly one campaign has written to through the plugin, that campaign. A contact
    /// no campaign wrote to, or two or more did, is not in the map.
    /// </summary>
    public static async Task<IReadOnlyDictionary<int, int>> OfAsync(JasonDbContext db, string pluginId, IReadOnlyCollection<int> contacts, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(contacts);
        if (contacts.Count == 0)
        {
            return new Dictionary<int, int>();
        }

        var writers = await Writers(db, pluginId, contacts).ToListAsync(ct).ConfigureAwait(false);
        return writers
            .GroupBy(writer => writer.ContactId)
            .Where(group => group.Select(writer => writer.CampaignId).Distinct().Count() == 1)
            .ToDictionary(group => group.Key, group => group.First().CampaignId);
    }

    /// <summary>The campaigns that have written to each of these contacts through the plugin, each pair once.</summary>
    internal static IQueryable<Writer> Writers(JasonDbContext db, string pluginId, IReadOnlyCollection<int> contacts)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentException.ThrowIfNullOrWhiteSpace(pluginId);
        ArgumentNullException.ThrowIfNull(contacts);
        if (contacts.Count == 0)
        {
            throw new ArgumentException("A read of who wrote to a person names at least one person.", nameof(contacts));
        }

        var people = string.Join(", ", contacts.Distinct().Order().Select(id => id.ToString(CultureInfo.InvariantCulture)));
        var operations = Words(OperationCatalog.WritesToAPerson.Order(StringComparer.Ordinal));
        return db.Database.SqlQuery<Writer>(FormattableStringFactory.Create(
            $$"""
            SELECT DISTINCT w.contact_id AS "ContactId", w.campaign_id AS "CampaignId"
            FROM work_items AS w INDEXED BY ix_work_items_contact_id
            JOIN campaigns AS c ON c.id = w.campaign_id
            WHERE w.contact_id IN ({{people}}) AND w.kind = 'provider_op' AND w.status = 'succeeded'
              AND w.operation IN ({{operations}})
              AND c.status <> 'archived'
              AND EXISTS (SELECT 1 FROM attempts AS a INDEXED BY ix_attempts_work_item_id_number
                  WHERE a.work_item_id = w.id AND a.status = 'succeeded'
                    AND json_extract(a.provenance_json, '$.plugin_id') = {0})
            """,
            pluginId));
    }

    /// <summary>
    /// Operation names written into the text: the catalog's own identifiers, lowercase words, dots and underscores, and
    /// refused here if one ever is not.
    /// </summary>
    private static string Words(IEnumerable<string> operations) =>
        string.Join(", ", operations.Select(operation =>
            operation.Length > 0 && operation.All(static c => c is (>= 'a' and <= 'z') or '.' or '_')
                ? $"'{operation}'"
                : throw new InvalidOperationException($"The operation identifier '{operation}' cannot be written into a query.")));
}
