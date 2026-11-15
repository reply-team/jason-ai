using System.Globalization;
using System.Runtime.CompilerServices;
using Jason.Runtime.Journal;
using Jason.Runtime.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Jason.Runtime.Events;

/// <summary>
/// The threads a campaign already holds as its own: those of its <c>inbound_reply_observed</c> lines that the campaign's
/// own read said, that the events store found by the campaign's own pin, or that the store marked <c>campaign_own</c>. A
/// reply found by the person's pin on such a thread is marked in that campaign, and the one campaign that alone had written
/// to the person is not asked for it (<see cref="SoleSenders"/>). Read once per page, for the campaigns the page's replies
/// were found in by the person's pin and the threads they name, inside the page's transaction, beside the members and the
/// writers; decided then, carried by the line, and never worked out again at a read.
/// </summary>
/// <remarks>
/// The read names its index and a test pins its plan: the campaign's observed lines by the thread each names
/// (<c>ix_journal_campaign_thread</c>), bounded by the page's campaigns and threads, never by a campaign's history. The
/// campaign ids are written into the text, values of ours; the plugin and the threads, the provider's, are parameters.
/// </remarks>
public static class CampaignOwnThreads
{
    /// <summary>One thread one campaign holds as its own.</summary>
    internal sealed record Held(int CampaignId, string Thread);

    /// <summary>
    /// Whether the observed line <paramref name="line"/> says a thread that is the campaign's own: a line the campaign's
    /// own read wrote (no rule), one the events store found by the campaign's pin, or one it found by the contact's pin and
    /// marked <c>campaign_own</c>. The mark was decided when the line was written and is read as the line carries it. Every
    /// read of whether a thread is a campaign's own reads this.
    /// </summary>
    internal static string Own(string line) => $"""
        (coalesce(json_extract({line}.new_json, '$.attributed_by'), 'campaign_pin') = 'campaign_pin'
          OR (json_extract({line}.new_json, '$.attributed_by') = 'contact_pin' AND json_extract({line}.new_json, '$.campaign_own') = 1))
        """;

    /// <summary>
    /// Which of these threads, read through the plugin, each of these campaigns already holds as its own. A campaign or a
    /// thread asked for that holds nothing is not in the set.
    /// </summary>
    public static async Task<HashSet<(int CampaignId, string Thread)>> OfAsync(
        JasonDbContext db,
        string pluginId,
        IReadOnlyCollection<int> campaigns,
        IReadOnlyCollection<string> threads,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(campaigns);
        ArgumentNullException.ThrowIfNull(threads);
        if (campaigns.Count == 0 || threads.Count == 0)
        {
            return [];
        }

        var held = await Lines(db, pluginId, campaigns, threads).ToListAsync(ct).ConfigureAwait(false);
        return [.. held.Select(line => (line.CampaignId, line.Thread))];
    }

    /// <summary>Each thread of these that each of these campaigns holds as its own through the plugin, each pair once.</summary>
    internal static IQueryable<Held> Lines(JasonDbContext db, string pluginId, IReadOnlyCollection<int> campaigns, IReadOnlyCollection<string> threads)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentException.ThrowIfNullOrWhiteSpace(pluginId);
        ArgumentNullException.ThrowIfNull(campaigns);
        ArgumentNullException.ThrowIfNull(threads);
        if (campaigns.Count == 0 || threads.Count == 0)
        {
            throw new ArgumentException("A read of the threads a campaign holds names at least one campaign and one thread.", nameof(campaigns));
        }

        var named = campaigns.Distinct().Order().Select(id => id.ToString(CultureInfo.InvariantCulture));
        var distinct = threads.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        var parameters = string.Join(", ", Enumerable.Range(1, distinct.Length).Select(index => $"{{{index}}}"));
        object[] arguments = [pluginId, .. distinct];
        return db.Database.SqlQuery<Held>(FormattableStringFactory.Create(
            $$"""
            SELECT DISTINCT o.campaign_id AS "CampaignId", json_extract(o.new_json, '$.thread') AS "Thread"
            FROM journal AS o INDEXED BY ix_journal_campaign_thread
            WHERE o.kind = '{{JournalKinds.InboundReplyObserved}}'
              AND o.campaign_id IN ({{string.Join(", ", named)}})
              AND json_extract(o.new_json, '$.thread') IN ({{parameters}})
              AND json_extract(o.new_json, '$.plugin') = {0}
              AND {{Own("o")}}
            """,
            arguments));
    }
}
