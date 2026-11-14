using System.Globalization;
using System.Text.Json.Nodes;
using Jason.Contracts.Api;
using Jason.Runtime.Journal;
using Jason.Runtime.Persistence;
using Jason.Runtime.WorkItems;
using Microsoft.EntityFrameworkCore;

namespace Jason.Runtime.Dispatch;

/// <summary>
/// The sends of a campaign a bounce of one of its people could be of: the campaign's own emails to that contact through the
/// plugin the bounce was read at, which succeeded with the provider's reference, finished in the window before the bounce,
/// and of which no bounce has been said yet; newest first, at most <see cref="Most"/>. A send of a provider's own sequence,
/// one written by hand or one older than the window is none of them, so a bounce of it finds nothing here.
/// </summary>
/// <remarks>
/// A send's record never holds its address, its reference or where it ran in a column of its own: the reference and the
/// plugin are in the provenance of the attempt that succeeded, the latest one, as the claim reads them for a read of the
/// send; the channel is the one its arguments named. Nothing the send said to a person is read.
/// </remarks>
internal static class BounceCandidates
{
    /// <summary>How many sends one bounce asks to be read at most: the newest.</summary>
    public const int Most = 3;

    /// <summary>How far back from the bounce a send can be the one that bounced.</summary>
    public static TimeSpan Window { get; } = TimeSpan.FromDays(45);

    /// <summary>The operation whose sends a bounce can be of: an email is a message.</summary>
    public const string SendOperation = "message.send";

    /// <summary>The read of one send.</summary>
    public const string StatusOperation = "message.status";

    /// <summary>
    /// One read. The seek is the index on the campaign and the contact; the bounce line's absence is asked of the unique
    /// index on the campaign, the kind and the key. SQLite's date functions read both the stored moments and the bound one,
    /// so the comparison is of moments rather than of text.
    /// </summary>
    private const string Sql = """
        SELECT s.public_id AS WorkItem, s.operation AS Operation,
               json_extract(a.provenance_json, '$.plugin_id') AS PluginId,
               json_extract(a.provenance_json, '$.binding_identity') AS BindingIdentity
        FROM work_items AS s
        JOIN attempts AS a ON a.work_item_id = s.id AND a.status = 'succeeded'
             AND a.number = (SELECT max(b.number) FROM attempts AS b WHERE b.work_item_id = s.id AND b.status = 'succeeded')
        WHERE s.campaign_id = {0} AND s.contact_id = {1}
          AND s.operation = {2} AND s.status = 'succeeded'
          AND json_extract(s.context_json, '$.input.channel') = 'email'
          AND julianday(s.finished_at) >= julianday({3})
          AND json_extract(a.provenance_json, '$.plugin_id') = {4}
          AND json_extract(a.provenance_json, '$.external_ids_returned.send') IS NOT NULL
          AND NOT EXISTS (SELECT 1 FROM journal AS j WHERE j.campaign_id = s.campaign_id
                          AND j.kind = {5} AND j.key = s.public_id)
        ORDER BY julianday(s.finished_at) DESC, s.id DESC
        LIMIT {6}
        """;

    /// <summary>
    /// The newest sends of <paramref name="campaignId"/> to <paramref name="contactId"/> through <paramref name="pluginId"/>
    /// that a bounce at <paramref name="bouncedAt"/> could be of.
    /// </summary>
    public static async Task<IReadOnlyList<BounceCandidate>> ReadAsync(
        JasonDbContext db, int campaignId, int contactId, string pluginId, DateTime bouncedAt, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentException.ThrowIfNullOrWhiteSpace(pluginId);

        var since = bouncedAt.ToUniversalTime().Subtract(Window).ToString("yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'", CultureInfo.InvariantCulture);
        return await db.Database
            .SqlQueryRaw<BounceCandidate>(Sql, campaignId, contactId, SendOperation, since, pluginId, JournalKinds.MessageBounceObserved, Most)
            .ToListAsync(ct)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Which of <paramref name="sends"/> already have an open read the runtime made: a <c>message.status</c> of the campaign,
    /// created by the system and not finished, naming the send. A read a person or a role made is theirs, and does not
    /// count: the runtime makes its own beside it.
    /// </summary>
    public static async Task<IReadOnlySet<string>> ReadOpenAsync(JasonDbContext db, int campaignId, IReadOnlyCollection<string> sends, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(sends);
        if (sends.Count == 0)
        {
            return new HashSet<string>(StringComparer.Ordinal);
        }

        // The statuses that are not final, named rather than negated, so the query seeks the campaign's live items by the
        // index on campaign and status.
        var open = await db.WorkItems
            .AsNoTracking()
            .Where(item => item.CampaignId == campaignId
                && (item.Status == WorkItemStatus.Created
                    || item.Status == WorkItemStatus.AwaitingApproval
                    || item.Status == WorkItemStatus.Scheduled
                    || item.Status == WorkItemStatus.Processing
                    || item.Status == WorkItemStatus.Waiting)
                && item.Operation == StatusOperation
                && item.CreatedByType == ActorType.System)
            .Select(item => item.Context)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var named = open
            .Select(context => context[WorkItemService.InputKey]?["send"] is JsonValue send
                && send.TryGetValue<string>(out var id) ? id : null)
            .OfType<string>();
        return named.Intersect(sends, StringComparer.Ordinal).ToHashSet(StringComparer.Ordinal);
    }
}

/// <summary>A send a bounce could be of: its work item and operation, and the plugin and account it ran under.</summary>
internal sealed record BounceCandidate(string WorkItem, string Operation, string PluginId, string? BindingIdentity);
