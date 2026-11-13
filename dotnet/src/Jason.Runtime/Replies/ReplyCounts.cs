using System.Text.Json;
using Jason.Contracts.Api;
using Jason.Runtime.Campaigns;
using Jason.Runtime.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Jason.Runtime.Replies;

/// <summary>
/// <c>reply.count</c>: the replies roles have judged, by the category word each verdict that stands names, per campaign
/// asked and in total, over a window held to each reply's own moment. A reply is one message of one thread of one plugin
/// in one campaign, under the latest <c>reply_classified</c> line said of it there; a thread is one conversation, its
/// plugin and its identifier together. The words are the verdicts' own: none is known here, and none is read into.
/// </summary>
/// <remarks>
/// A reply nobody has judged is not counted, so these are not the people who replied that <c>campaign.activity</c>
/// counts. The verdict lines are read whoever the person is and however they stand in the campaign now: a reply of a
/// person removed since was a reply all the same.
/// </remarks>
public sealed class ReplyCounts(JasonDbContext db)
{
    /// <summary>
    /// The window's verdicts that stand, grouped twice in one read: by campaign and category, and by campaign alone, since a
    /// conversation that holds replies of two categories is one thread of the campaign. The verdicts are found through the
    /// kind's own index, the latest of each reply by its row id, and the reply's moment is compared by its julian day,
    /// which keeps the millisecond the line holds.
    /// </summary>
    private const string Sql = """
        WITH latest AS (
            SELECT max(id) AS id
            FROM journal INDEXED BY ix_journal_kind
            WHERE kind = 'reply_classified' AND campaign_id IN (SELECT value FROM json_each({0}))
            GROUP BY campaign_id, key),
        counted AS (
            SELECT j.campaign_id AS campaign_id,
                   json_extract(j.new_json, '$.category') AS category,
                   json_array(json_extract(j.new_json, '$.plugin'), json_extract(j.new_json, '$.thread')) AS thread
            FROM latest JOIN journal AS j ON j.id = latest.id
            WHERE julianday(json_extract(j.new_json, '$.message_at')) >= julianday({1})
              AND julianday(json_extract(j.new_json, '$.message_at')) < julianday({2}))
        SELECT campaign_id AS "CampaignId", 1 AS "ByCategory", category AS "Category", count(*) AS "Replies", count(DISTINCT thread) AS "Threads"
        FROM counted GROUP BY campaign_id, category
        UNION ALL
        SELECT campaign_id, 0, NULL, count(*), count(DISTINCT thread)
        FROM counted GROUP BY campaign_id
        """;

    public async Task<ReplyCountResponse> CountAsync(ReplyCountRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var (since, until) = CampaignNumbers.Period(request.Since, request.Until);
        var campaigns = await CampaignNumbers.ResolveAsync(db, request.CampaignIds, cancellationToken).ConfigureAwait(false);
        var rows = await Read(db, [.. campaigns.Select(campaign => campaign.Id)], since, until).ToListAsync(cancellationToken).ConfigureAwait(false);

        var byCampaign = rows.ToLookup(row => row.CampaignId);
        var counted = campaigns.Select(campaign =>
        {
            var lines = byCampaign[campaign.Id].ToList();
            var whole = lines.SingleOrDefault(row => row.ByCategory == 0);
            return new ReplyCountRow(
                campaign.PublicId,
                whole?.Replies ?? 0,
                whole?.Threads ?? 0,
                Ordered(lines.Where(row => row.ByCategory == 1).Select(row => new ReplyCategoryCount(row.Category, row.Replies, row.Threads))));
        }).ToList();

        // A thread is a thread of its campaign, so the total adds the campaigns' counts up, a category's with its own.
        var total = new ReplyCountTotal(
            counted.Sum(row => row.Replies),
            counted.Sum(row => row.Threads),
            Ordered(counted.SelectMany(row => row.Categories)
                .GroupBy(category => category.Category, StringComparer.Ordinal)
                .Select(same => new ReplyCategoryCount(same.Key, same.Sum(category => category.Replies), same.Sum(category => category.Threads)))));
        return new ReplyCountResponse(counted, total);
    }

    /// <summary>The grouped read itself, to compose on and to be asked for its plan.</summary>
    internal static IQueryable<ReplyCountLine> Read(JasonDbContext db, IReadOnlyCollection<int> campaignIds, DateTime since, DateTime until)
    {
        ArgumentNullException.ThrowIfNull(db);
        return db.Database.SqlQueryRaw<ReplyCountLine>(Sql, JsonSerializer.Serialize(campaignIds), Utc(since), Utc(until));
    }

    /// <summary>Most replies first, then the word in ordinal order, a reply with no category after every word it ties with.</summary>
    private static List<ReplyCategoryCount> Ordered(IEnumerable<ReplyCategoryCount> categories) =>
        [.. categories
            .OrderByDescending(category => category.Replies)
            .ThenBy(category => category.Category is null)
            .ThenBy(category => category.Category, StringComparer.Ordinal)];

    private static DateTime Utc(DateTime moment) => moment.Kind == DateTimeKind.Utc ? moment : moment.ToUniversalTime();
}

/// <summary>One group of the read: a campaign's category, or with <c>ByCategory</c> 0 the campaign whole.</summary>
internal sealed record ReplyCountLine(int CampaignId, int ByCategory, string? Category, int Replies, int Threads);
