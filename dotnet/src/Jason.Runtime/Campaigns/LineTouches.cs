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
/// back, so it never went, as the hold reads it too; a cancel that failed, or is still to run, takes nothing back. A send a
/// succeeded <c>message.status</c> of the campaign (<see cref="HeldSends.Status"/>) says failed or was cancelled
/// (<see cref="HeldSends.UnsentStates"/>) is not counted, whoever asked for the read, unless a read of it says it went or
/// may have gone (<c>sent</c> or <c>unknown</c>, <see cref="HeldSends.MayHaveGoneStates"/>): reads that disagree leave it
/// counted, the safe side of a ceiling. A <c>queued</c> read, a read that failed and a letter the provider still holds as
/// <c>queued</c>, however long past its moment, leave it counted.
/// </para>
/// <para>
/// In SQL because the class sits in a JSON column only SQLite's own functions reach, and because the people list's reads
/// are SQL (<see cref="CampaignPeople"/>). <see cref="CountedSql"/> is a condition over one work item aliased <c>w</c>; its
/// attempts are read by the attempts' index on the work item, and what names it by the work items' index on the send an
/// item names, the campaign and the operation (<see cref="SendProbe"/>): each take-back and read of the send is found by a
/// seek, so the cost grows with what names the send rather than with the campaign's work.
/// </para>
/// </remarks>
internal static class LineTouches
{
    /// <summary>The operations that touch a person, as a SQL list of literals, in a stable order.</summary>
    private static readonly string TouchesSql =
        string.Join(", ", OperationCatalog.Touches.Order(StringComparer.Ordinal).Select(operation => $"'{operation}'"));

    /// <summary>
    /// Whether the work item aliased <paramref name="item"/> names the send aliased <paramref name="send"/> under
    /// <c>args.send</c>: the one wording of every probe of a send (<see cref="SendProbe"/>), so the index on what an item
    /// names is taken by each of them.
    /// </summary>
    private static string NamesSql(string item, string send) => SendProbe.Names(item, $"{send}.public_id");

    /// <summary>The states in <paramref name="states"/> as a SQL list of literals.</summary>
    private static string ListSql(IEnumerable<string> states) => string.Join(", ", states.Select(state => $"'{state}'"));

    /// <summary>
    /// Whether the send aliased <paramref name="send"/> never went: a succeeded <c>message.cancel</c> of the campaign names it,
    /// as <see cref="TakenBackSql"/> reads, or a succeeded <c>message.status</c> of the campaign names it and says it failed
    /// or was cancelled (<see cref="HeldSends.ReadSaidSql"/>), whoever asked for the read, while no succeeded read of it says
    /// it went or may have gone. Each looks for one operation's items naming the send, so each seeks the index on the
    /// campaign, the operation and the send (<see cref="SendProbe"/>); the reads that may outweigh a read are looked for only
    /// behind a read that says the send never went. The one definition, which a touch that counts and the hold's reading of
    /// Jason's own move both read.
    /// </summary>
    internal static string NeverWentSql(string send) => $$"""
        ({{TakenBack(send)}}
         OR EXISTS (SELECT 1 FROM work_items AS k
                    WHERE k.campaign_id = {{send}}.campaign_id AND k.operation = '{{HeldSends.Status}}' AND k.status = 'succeeded'
                      AND {{NamesSql("k", send)}}
                      AND {{HeldSends.ReadSaidSql("k", ListSql(HeldSends.UnsentStates))}}
                      AND NOT EXISTS (SELECT 1 FROM work_items AS g
                                      WHERE g.campaign_id = {{send}}.campaign_id AND g.operation = '{{HeldSends.Status}}'
                                        AND g.status = 'succeeded'
                                        AND {{NamesSql("g", send)}}
                                        AND {{HeldSends.ReadSaidSql("g", ListSql(HeldSends.MayHaveGoneStates))}})))
        """;

    /// <summary>
    /// Whether a succeeded <c>message.cancel</c> of the campaign names the send aliased <paramref name="send"/>: the provider
    /// held it and said it took it back, so it never went.
    /// </summary>
    private static string TakenBack(string send) => $$"""
        EXISTS (SELECT 1 FROM work_items AS k
                WHERE k.campaign_id = {{send}}.campaign_id AND k.operation = '{{HeldSends.Cancel}}' AND k.status = 'succeeded'
                  AND {{NamesSql("k", send)}})
        """;

    /// <summary>
    /// Whether a succeeded <c>message.cancel</c> of the campaign names the work item aliased <c>w</c>: the provider held the
    /// send and said it took it back, so it never went. A letter a message is named to follow (<see cref="LineRoots"/>) is
    /// asked this alone; a touch that counts asks it with what a read said, in <see cref="NeverWentSql"/>.
    /// </summary>
    internal static readonly string TakenBackSql = TakenBack("w");

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
