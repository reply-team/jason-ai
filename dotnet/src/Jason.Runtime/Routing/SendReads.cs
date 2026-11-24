using System.Globalization;
using System.Linq.Expressions;
using System.Text.Json;
using System.Text.Json.Nodes;
using Jason.Contracts.Api;
using Jason.Runtime.Persistence;
using Jason.Runtime.WorkItems;
using Microsoft.EntityFrameworkCore;

namespace Jason.Runtime.Routing;

/// <summary>
/// The reads of a campaign's sends the runtime made itself: the <c>message.status</c> items of the campaign the system
/// created, each naming its send by <c>context.input.send</c>. One reading of them, which the bounce step, the deferred
/// read step and the line a read of a deferred send writes all ask, so none of them counts a read another would not. A
/// read a person or a role made is theirs and is none of these: the runtime makes its own beside it.
/// </summary>
/// <remarks>
/// Nothing is stored for it: the work items are the record, read by the send each names. An open read is one not finished,
/// its statuses named rather than negated, so the query seeks the campaign's live items by the index on campaign and
/// status; a finished one is one with a moment it finished.
/// </remarks>
internal static class SendReads
{
    /// <summary>The read of one send.</summary>
    public const string Operation = "message.status";

    /// <summary>
    /// The finished reads of the sends named, one row a read: the send it names and when it ended, in UTC to the
    /// millisecond. A read is matched to its send by the identifier its input names; nothing else of its context is read.
    /// One read may be left out by its id, the read whose own end is being decided.
    /// </summary>
    private const string MadeSql = """
        SELECT json_extract(w.context_json, '$.input.send') AS Send,
               strftime('%Y-%m-%dT%H:%M:%fZ', w.finished_at) AS Ended
        FROM work_items AS w
        WHERE w.campaign_id = {0}
          AND w.operation = {1}
          AND w.created_by_type = 'system'
          AND w.finished_at IS NOT NULL
          AND ({3} IS NULL OR w.public_id <> {3})
          AND json_extract(w.context_json, '$.input.send') IN (SELECT value FROM json_each({2}))
        """;

    /// <summary>An open read the runtime made in the campaign: created by the system and not finished.</summary>
    private static Expression<Func<WorkItem, bool>> OpenIn(int campaignId) =>
        item => item.CampaignId == campaignId
            && (item.Status == WorkItemStatus.Created
                || item.Status == WorkItemStatus.AwaitingApproval
                || item.Status == WorkItemStatus.Scheduled
                || item.Status == WorkItemStatus.Processing
                || item.Status == WorkItemStatus.Waiting)
            && item.Operation == Operation
            && item.CreatedByType == ActorType.System;

    /// <summary>
    /// Which of <paramref name="sends"/> already have an open read the runtime made: a <c>message.status</c> of the
    /// campaign, created by the system and not finished, naming the send. <paramref name="except"/>, where given, is a read
    /// left out, the one whose own end is being decided.
    /// </summary>
    public static async Task<IReadOnlySet<string>> OpenAsync(
        JasonDbContext db, int campaignId, IReadOnlyCollection<string> sends, CancellationToken ct, string? except = null)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(sends);
        if (sends.Count == 0)
        {
            return new HashSet<string>(StringComparer.Ordinal);
        }

        var open = await db.WorkItems
            .AsNoTracking()
            .Where(OpenIn(campaignId))
            .Where(item => except == null || item.PublicId != except)
            .Select(item => item.Context)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var named = open
            .Select(context => context[WorkItemService.InputKey]?["send"] is JsonValue send
                && send.TryGetValue<string>(out var id) ? id : null)
            .OfType<string>();
        return named.Intersect(sends, StringComparer.Ordinal).ToHashSet(StringComparer.Ordinal);
    }

    /// <summary>How many reads the runtime made in the campaign are open now, whatever send each names.</summary>
    public static Task<int> OpenCountAsync(JasonDbContext db, int campaignId, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(db);
        return db.WorkItems.AsNoTracking().CountAsync(OpenIn(campaignId), ct);
    }

    /// <summary>
    /// The finished reads the runtime made of each send named that ended at or after the moment given for it: how many,
    /// and when the latest ended. A read that ended before the send's moment could only find it held, so it is not one of
    /// the reads of a send whose moment has passed. A send no such read has finished for is absent.
    /// <paramref name="except"/>, where given, is a read left out, the one whose own end is being decided.
    /// </summary>
    public static async Task<IReadOnlyDictionary<string, MadeReads>> MadeAsync(
        JasonDbContext db, int campaignId, IReadOnlyDictionary<string, DateTimeOffset> from, CancellationToken ct, string? except = null)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(from);
        if (from.Count == 0)
        {
            return new Dictionary<string, MadeReads>(StringComparer.Ordinal);
        }

        object left = except is null ? DBNull.Value : except;
        var rows = await db.Database
            .SqlQueryRaw<MadeReadRow>(MadeSql, campaignId, Operation, JsonSerializer.Serialize(from.Keys), left)
            .ToListAsync(ct)
            .ConfigureAwait(false);
        return rows
            .Select(row => (row.Send, Ended: DateTimeOffset.ParseExact(row.Ended, "yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal)))
            .Where(row => row.Ended >= from[row.Send])
            .GroupBy(row => row.Send, StringComparer.Ordinal)
            .ToDictionary(
                reads => reads.Key,
                reads => new MadeReads(reads.Count(), reads.Max(read => read.Ended)),
                StringComparer.Ordinal);
    }
}

/// <summary>The finished reads the runtime made of one send: how many, and when the latest of them ended.</summary>
internal sealed record MadeReads(int Count, DateTimeOffset LastEnded);

/// <summary>One finished read: the send it names, and its end as the database wrote it.</summary>
internal sealed record MadeReadRow(string Send, string Ended);
