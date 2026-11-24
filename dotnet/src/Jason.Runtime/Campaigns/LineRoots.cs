using System.Text.Json;
using System.Text.Json.Nodes;
using Jason.Runtime.Persistence;
using Jason.Runtime.Routing;
using Jason.Runtime.WorkItems;
using Microsoft.EntityFrameworkCore;

namespace Jason.Runtime.Campaigns;

/// <summary>
/// Which earlier letter of a person's line in a campaign a message may follow, as a reply in that letter's thread, written
/// once: the line's root, which the runtime's default follows, and what the claim reads of a letter, which the five checks of
/// a letter named hold to the message (<see cref="FollowsDecision"/>). Read inside the claim's transaction, like every other
/// fact it decides on.
/// </summary>
/// <remarks>
/// <para>
/// The line's root is the latest <c>message.send</c> of the same campaign about the same person on the same channel — its
/// <c>args.channel</c> the message's own — that is a touch that counts (<see cref="LineTouches.CountedSql"/>, so a send a
/// succeeded take-back names is none), whose latest succeeded attempt returned the provider's reference under
/// <c>external_ids_returned.send</c>, and that itself follows nothing: its <c>context.follows</c> is absent, as on every
/// letter claimed before the runtime decided this at all, or names no work item. Latest is by that attempt's
/// <c>claimed_at</c>, then by the work item's row, both descending. The work being claimed is never its own root. A letter
/// that opened a thread of its own is a root, so the root moves to the latest such letter; a letter that followed one is a
/// reply in that thread and is never one.
/// </para>
/// <para>
/// In SQL for the reason a touch is: the channel, the record of what a letter followed and the provider's reference sit in
/// JSON columns only SQLite's own functions reach. The root is read through the work items' index on the campaign and the
/// person.
/// </para>
/// </remarks>
internal static class LineRoots
{
    /// <summary>
    /// The line's root by its work item's id: <c>{0}</c> is the campaign, <c>{1}</c> the person, <c>{2}</c> the work item being
    /// claimed, left out, <c>{3}</c> the operation and <c>{4}</c> the channel.
    /// </summary>
    internal static readonly string RootSql = $$"""
        SELECT w.public_id AS Value
        FROM work_items AS w
        JOIN attempts AS s ON s.work_item_id = w.id AND s.status = 'succeeded'
             AND s.number = (SELECT max(b.number) FROM attempts AS b WHERE b.work_item_id = w.id AND b.status = 'succeeded')
        WHERE w.campaign_id = {0} AND w.contact_id = {1} AND w.id <> {2}
          AND w.operation = {3}
          AND json_extract(w.context_json, '$.input.channel') = {4}
          AND {{LineTouches.CountedSql}}
          AND json_extract(s.provenance_json, '$.external_ids_returned.send') IS NOT NULL
          AND coalesce(json_type(w.context_json, '$.follows.work_item'), 'null') <> 'text'
        ORDER BY s.claimed_at DESC, w.id DESC
        LIMIT 1
        """;

    /// <summary>Whether a succeeded take-back of the campaign names the work item: <c>{0}</c> is the campaign, <c>{1}</c> the work item's id.</summary>
    internal static readonly string TakenBackCountSql =
        $"SELECT count(*) AS Value FROM work_items AS w WHERE w.campaign_id = {{0}} AND w.public_id = {{1}} AND {LineTouches.TakenBackSql}";

    /// <summary>The work item of the line's root of a message to <paramref name="contactId"/> on <paramref name="channel"/>, or null where the line holds none.</summary>
    public static async Task<string?> RootAsync(
        JasonDbContext db,
        int campaignId,
        int contactId,
        int workItemId,
        string operation,
        string channel,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(db);
        var roots = await db.Database
            .SqlQueryRaw<string>(RootSql, campaignId, contactId, workItemId, operation, channel)
            .ToListAsync(ct)
            .ConfigureAwait(false);
        return roots.FirstOrDefault();
    }

    /// <summary>Whether a succeeded <c>message.cancel</c> of the campaign names <paramref name="workItem"/>: it never went.</summary>
    public static async Task<bool> TakenBackAsync(JasonDbContext db, int campaignId, string workItem, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(db);
        return await db.Database.SqlQueryRaw<int>(TakenBackCountSql, campaignId, workItem).SingleAsync(ct).ConfigureAwait(false) > 0;
    }

    /// <summary>The channel a letter went on, as its arguments name it under <c>args.channel</c>; null where they name none.</summary>
    public static string? Channel(WorkItem letter)
    {
        ArgumentNullException.ThrowIfNull(letter);
        return letter.Context[WorkItemService.InputKey] is JsonObject arguments ? Text(arguments["channel"]) : null;
    }

    /// <summary>
    /// The provider's account a letter went from, as its result says it under <c>items[0].sender_external_id</c>; null where it
    /// says none, as a letter that has not succeeded does not.
    /// </summary>
    public static string? Sender(WorkItem letter)
    {
        ArgumentNullException.ThrowIfNull(letter);
        return letter.Result is JsonObject result && result["items"] is JsonArray { Count: > 0 } items && items[0] is JsonObject first
            ? Text(first["sender_external_id"])
            : null;
    }

    private static string? Text(JsonNode? node) =>
        node is JsonValue value && value.GetValueKind() == JsonValueKind.String ? value.GetValue<string>() : null;
}
