using System.Globalization;
using System.Runtime.CompilerServices;
using Jason.Contracts.Operations;
using Jason.Runtime.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Jason.Runtime.Events;

/// <summary>
/// Which one campaign, if exactly one, had written to a person through a plugin before the person replied: the fact the
/// events store marks a reply found by the contact's pin with, <c>campaign_own</c>, on a thread no campaign it found holds
/// as its own yet and that names no provider campaign, so that the thread becomes that campaign's own
/// (<see cref="CampaignOwnThreads"/> is asked first). Read once per page, for the people the page's replies name, inside
/// the page's transaction, beside the members the contact rule reads; each campaign that wrote is answered with the first
/// moment it wrote, and each reply is compared with its own moment. Decided then, carried by the line, and never worked
/// out again at a read.
/// </summary>
/// <remarks>
/// <para>
/// Written to a person: a provider operation of the campaign that writes to a person — its <c>reach</c> is <c>act</c>
/// without a condition, read from the catalog (<see cref="OperationCatalog.WritesToAPerson"/>) — about the contact, that
/// succeeded, with an attempt that succeeded at the plugin, in a campaign that is not archived when the line is written. An
/// enrolment's reach is conditional and does not count: a sequence's reply is found by the campaign's own pin.
/// </para>
/// <para>
/// Before the reply: the attempt that succeeded finished at or before the moment the provider gives the reply. A
/// campaign whose first send finished after it had not written when the person replied, and neither marks the reply nor
/// counts against another campaign, however soon after the reply it wrote and however late the page is read, a backfill's
/// included. The two moments come from two clocks, the provider's and this machine's, and are compared as they stand, with
/// no tolerance: where the two differ, a send that finished within that difference of the reply can fall on the wrong side
/// of it, which can cost a campaign that alone had written its mark, and can count a send against it, or not, wrongly.
/// </para>
/// <para>
/// A send the provider took for later succeeded when it was taken (<c>accepted</c>), and it counts from then: from the
/// moment its attempt finished. One that was later taken back, or that the provider later failed, still counts: its
/// item stays succeeded, and nothing here reads what became of it. The cost is accepted. Where a second campaign shares
/// the person, a send that never went takes a mark away; where the campaign is the only one, a reply the person wrote
/// before the send went, or though it never went, is marked its own. A send that ended <c>ambiguous</c> may have gone
/// and did not succeed: it does not count, and where it was the only one the line stays unmarked, the manager's as
/// before. A campaign archived after the line was written takes no mark back and gives none: the line was decided once.
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
    /// <summary>One campaign that has written to one person through the plugin, and the first moment it did.</summary>
    internal sealed record Writer(int ContactId, int CampaignId, DateTime FirstAt);

    /// <summary>The campaigns that had written to each person of a page, each with the first moment it did.</summary>
    public sealed class Written
    {
        private readonly IReadOnlyDictionary<int, IReadOnlyList<Writer>> byPerson;

        internal Written(IEnumerable<Writer> writers) =>
            byPerson = writers
                .GroupBy(writer => writer.ContactId)
                .ToDictionary(group => group.Key, group => (IReadOnlyList<Writer>)[.. group]);

        /// <summary>
        /// The one campaign that had written to the person at or before <paramref name="moment"/>, or null where none had,
        /// or two or more had. A campaign that first wrote after the moment is not counted, either way.
        /// </summary>
        public int? SoleBefore(int contactId, DateTime moment)
        {
            if (!byPerson.TryGetValue(contactId, out var writers))
            {
                return null;
            }

            var before = writers.Where(writer => writer.FirstAt <= moment).Select(writer => writer.CampaignId).Distinct().ToList();
            return before.Count == 1 ? before[0] : null;
        }
    }

    /// <summary>
    /// For each of these contacts, every campaign that has written to them through the plugin, with the first moment it
    /// did; asked of a reply by its own moment (<see cref="Written.SoleBefore"/>).
    /// </summary>
    public static async Task<Written> OfAsync(JasonDbContext db, string pluginId, IReadOnlyCollection<int> contacts, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(contacts);
        if (contacts.Count == 0)
        {
            return new Written([]);
        }

        var writers = await Writers(db, pluginId, contacts).ToListAsync(ct).ConfigureAwait(false);
        return new Written(writers.Select(writer => writer with { FirstAt = DateTime.SpecifyKind(writer.FirstAt, DateTimeKind.Utc) }));
    }

    /// <summary>
    /// The campaigns that have written to each of these contacts through the plugin, each pair once, with the moment the
    /// first of its attempts that succeeded there finished.
    /// </summary>
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
            SELECT w.contact_id AS "ContactId", w.campaign_id AS "CampaignId", MIN(a.finished_at) AS "FirstAt"
            FROM work_items AS w INDEXED BY ix_work_items_contact_id
            JOIN campaigns AS c ON c.id = w.campaign_id
            JOIN attempts AS a INDEXED BY ix_attempts_work_item_id_number ON a.work_item_id = w.id
            WHERE w.contact_id IN ({{people}}) AND w.kind = 'provider_op' AND w.status = 'succeeded'
              AND w.operation IN ({{operations}})
              AND c.status <> 'archived'
              AND a.status = 'succeeded' AND a.finished_at IS NOT NULL
              AND json_extract(a.provenance_json, '$.plugin_id') = {0}
            GROUP BY w.contact_id, w.campaign_id
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
