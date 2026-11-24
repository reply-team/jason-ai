using Jason.Contracts.Operations;
using Jason.Runtime.Persistence;
using Jason.Runtime.Routing;
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
/// ambiguous, and that did not succeed, reached nobody, and counts for nothing. Nor does a send a succeeded
/// <c>message.cancel</c> of the campaign names (<see cref="HeldSends.Cancel"/>): the provider held it, and said it took it
/// back, so it never went, as the hold reads it too; a cancel that failed, or is still to run, takes nothing back. Nor does
/// a send a succeeded <c>message.status</c> of the campaign names (<see cref="HeldSends.Status"/>) and answered the provider
/// failed or cancelled (<see cref="HeldSends.UnsentStates"/>), whoever asked for the read: the provider says it never went,
/// and it never will. A read that answered <c>queued</c> or <c>unknown</c>, or <c>sent</c>, or that failed, leaves the
/// send counted, since such a letter may still go, may have gone, or went.
/// </para>
/// <para>
/// In SQL because the class sits in a JSON column only SQLite's own functions reach, and because the people list's reads
/// are SQL (<see cref="CampaignPeople"/>). <see cref="CountedSql"/> is a condition over one work item aliased <c>w</c>; its
/// attempts are read by the attempts' index on the work item, and what names it by the work items' index on the campaign
/// and the status: the campaign's succeeded work is passed over by its operation, and each take-back's and read's
/// arguments are read for the send they name, the two in one subquery, so the cost grows with the campaign's succeeded
/// work and is paid once for both.
/// </para>
/// </remarks>
internal static class LineTouches
{
    /// <summary>The operations that touch a person, as a SQL list of literals, in a stable order.</summary>
    private static readonly string TouchesSql =
        string.Join(", ", OperationCatalog.Touches.Order(StringComparer.Ordinal).Select(operation => $"'{operation}'"));

    /// <summary>The states of a read that say a send never went (<see cref="HeldSends.UnsentStates"/>), as a SQL list of literals.</summary>
    private static string UnsentStatesSql => string.Join(", ", HeldSends.UnsentStates.Select(state => $"'{state}'"));

    /// <summary>
    /// Whether the send aliased <paramref name="send"/> never went: a succeeded <c>message.cancel</c> of the campaign names it,
    /// as <see cref="TakenBackSql"/> reads, or a succeeded <c>message.status</c> of the campaign names it and answered that
    /// the provider failed or cancelled it (<see cref="HeldSends.ReadSaidSql"/>), whoever asked for the read. One subquery
    /// for both, its columns read before its documents and the arguments before the answer, since a read's answer carries
    /// the letter. The one definition, which a touch that counts and the hold's reading of Jason's own move both read.
    /// </summary>
    internal static string NeverWentSql(string send) => $$"""
        EXISTS (SELECT 1 FROM work_items AS k
                WHERE k.campaign_id = {{send}}.campaign_id AND k.status = 'succeeded'
                  AND k.operation IN ('{{HeldSends.Cancel}}', '{{HeldSends.Status}}')
                  AND json_extract(k.context_json, '$.input.send') = {{send}}.public_id
                  AND (k.operation = '{{HeldSends.Cancel}}' OR {{HeldSends.ReadSaidSql(UnsentStatesSql)}}))
        """;

    /// <summary>
    /// Whether a succeeded <c>message.cancel</c> of the campaign names the work item aliased <c>w</c>: the provider held the
    /// send and said it took it back, so it never went. A letter a message is named to follow (<see cref="LineRoots"/>) is
    /// asked this alone; a touch that counts asks it with what a read said, in <see cref="NeverWentSql"/>.
    /// </summary>
    internal static readonly string TakenBackSql = $$"""
        EXISTS (SELECT 1 FROM work_items AS k
                WHERE k.campaign_id = w.campaign_id AND k.operation = '{{HeldSends.Cancel}}' AND k.status = 'succeeded'
                  AND json_extract(k.context_json, '$.input.send') = w.public_id)
        """;

    /// <summary>Whether the work item aliased <c>w</c> is a touch that counts: the one definition.</summary>
    internal static readonly string CountedSql = $$"""
        (w.kind = 'provider_op'
         AND w.operation IN ({{TouchesSql}})
         AND (w.status = 'succeeded'
              OR EXISTS (SELECT 1 FROM attempts AS a
                         WHERE a.work_item_id = w.id AND json_extract(a.error_json, '$.class') = 'ambiguous'))
         AND NOT {{NeverWentSql("w")}})
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
