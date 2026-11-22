using Jason.Contracts.Operations;
using Jason.Runtime.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Jason.Runtime.Campaigns;

/// <summary>
/// Which of a person's work in a campaign is a touch that counts, written once: the claim's ceiling of touches,
/// <c>Lines:MaxTouches</c>, counts by it, and the number of touches the people list shows is to count by the same
/// condition, so the number a person reads and the number the ceiling stops at cannot disagree.
/// </summary>
/// <remarks>
/// <para>
/// A touch counts when its work item is a provider operation whose document says <c>touch: true</c>
/// (<see cref="OperationCatalog.Touches"/>) and it either succeeded or any attempt of it ended classed <c>ambiguous</c>,
/// whatever came after that attempt and whatever the item's status now, since that attempt may have reached the person: an
/// item put back for a repeat, parked again, failed, cancelled or expired after such an attempt still counts. One item is
/// one touch at most. Where a later attempt found that nothing had been sent this counts one too many, which is the safe
/// side of a ceiling; nothing the store keeps proves an earlier attempt did not act. Work none of whose attempts ended
/// ambiguous, and that did not succeed, reached nobody, and counts for nothing.
/// </para>
/// <para>
/// In SQL because the class sits in a JSON column only SQLite's own functions reach, and because the people list's reads
/// are SQL (<see cref="CampaignPeople"/>). <see cref="CountedSql"/> is a condition over one work item aliased <c>w</c>; its
/// attempts are read by the attempts' index on the work item.
/// </para>
/// </remarks>
internal static class LineTouches
{
    /// <summary>The operations that touch a person, as a SQL list of literals, in a stable order.</summary>
    private static readonly string TouchesSql =
        string.Join(", ", OperationCatalog.Touches.Order(StringComparer.Ordinal).Select(operation => $"'{operation}'"));

    /// <summary>Whether the work item aliased <c>w</c> is a touch that counts: the one definition.</summary>
    internal static readonly string CountedSql = $$"""
        (w.kind = 'provider_op'
         AND w.operation IN ({{TouchesSql}})
         AND (w.status = 'succeeded'
              OR EXISTS (SELECT 1 FROM attempts AS a
                         WHERE a.work_item_id = w.id AND json_extract(a.error_json, '$.class') = 'ambiguous')))
        """;

    /// <summary>
    /// The touches that count of one person in one campaign other than one work item: <c>{0}</c> is the campaign, <c>{1}</c>
    /// the person and <c>{2}</c> the work item left out, read through the work items' index on the campaign and the person.
    /// </summary>
    internal static readonly string OthersCountSql =
        $"SELECT count(*) AS Value FROM work_items AS w WHERE w.campaign_id = {{0}} AND w.contact_id = {{1}} AND w.id <> {{2}} AND {CountedSql}";

    /// <summary>How many touches that count the person has had in the campaign, the work item being claimed left out.</summary>
    public static Task<int> OthersAsync(JasonDbContext db, int campaignId, int contactId, int workItemId, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(db);
        return db.Database.SqlQueryRaw<int>(OthersCountSql, campaignId, contactId, workItemId).SingleAsync(ct);
    }
}
