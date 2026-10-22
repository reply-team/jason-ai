using System.Text.Json;
using System.Text.Json.Nodes;
using Jason.Contracts.Api;
using Jason.Runtime.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Jason.Runtime.Campaigns;

/// <summary>
/// What each person of one page of a filtered <c>campaign.list_contacts</c> is counted for, so a row needs no read of its
/// own: one query per kind of fact for the whole page, never one per person. Keyed by the contact's internal id.
/// </summary>
internal static class CampaignWhy
{
    private static readonly string[] ActOperations = ["campaign.enroll", "message.send", "conversation.reply", "social_invitation.send"];

    // The page's people's reply lines in the campaign, dated as the count dates them; one arm per kind, each through the
    // person's own index.
    internal const string FirstReplies = """
        SELECT j.contact_id AS ContactId, julianday(json_extract(j.new_json, '$.last_activity_at')) AS Day,
               json_extract(j.new_json, '$.channel') AS Channel
        FROM journal AS j
        WHERE j.contact_id IN (SELECT value FROM json_each({1})) AND j.campaign_id = {0} AND j.kind = 'inbound_reply_observed'
        UNION ALL
        SELECT j.contact_id, julianday(json_extract(j.new_json, '$.occurred_at')), json_extract(j.new_json, '$.channel')
        FROM journal AS j
        WHERE j.contact_id IN (SELECT value FROM json_each({1})) AND j.campaign_id = {0} AND j.kind = 'provider_event_observed'
          AND json_extract(j.new_json, '$.kind') = 'reply_detected'
        """;

    // The campaign's additions of the period ({0} the campaign, {1} to {2}), through their own index, as the count reads them.
    internal const string Additions = """
        SELECT j.ts AS Ts, j.new_json AS Said
        FROM journal AS j INDEXED BY ix_journal_contacts_added_campaign_ts
        WHERE j.campaign_id = {0} AND j.kind = 'contacts_added' AND j.ts >= {1} AND j.ts < {2}
        ORDER BY j.id DESC
        """;

    // The campaign's removals of the period ({0} the campaign, {1} to {2}), through their own index, as the count reads them.
    internal const string Removals = """
        SELECT j.ts AS Ts, j.new_json AS Said
        FROM journal AS j INDEXED BY ix_journal_contacts_removed_campaign_ts
        WHERE j.campaign_id = {0} AND j.kind = 'contacts_removed' AND j.ts >= {1} AND j.ts < {2}
        ORDER BY j.id DESC
        """;

    // The opt-outs of the period ({0} to {1}), through their own index, as the count reads them.
    internal const string OptOuts = """
        SELECT j.ts AS Ts, j.new_json AS Said
        FROM journal AS j INDEXED BY ix_journal_suppression_added_ts
        WHERE j.kind = 'suppression_added' AND j.ts >= {0} AND j.ts < {1}
        ORDER BY j.id DESC
        """;

    public static async Task<IReadOnlyDictionary<int, MembershipWhy>> ForSummaryAsync(
        JasonDbContext db,
        int campaignId,
        IReadOnlyList<Contact> page,
        CampaignSummaryFilter filter,
        CancellationToken ct) => filter switch
        {
            CampaignSummaryFilter.Waiting => await WaitingAsync(db, campaignId, Keys(page), ct).ConfigureAwait(false),
            CampaignSummaryFilter.Failed => await FailedAsync(db, campaignId, Keys(page), ct).ConfigureAwait(false),
            _ => new Dictionary<int, MembershipWhy>(),
        };

    public static async Task<IReadOnlyDictionary<int, MembershipWhy>> ForActivityAsync(
        JasonDbContext db,
        int campaignId,
        IReadOnlyList<Contact> page,
        CampaignActivityFilter filter,
        DateTime since,
        DateTime until,
        CancellationToken ct) => filter switch
        {
            CampaignActivityFilter.Added => await AddedAsync(db, campaignId, page, since, until, ct).ConfigureAwait(false),
            CampaignActivityFilter.WorkedOn => await LatestWorkAsync(db, campaignId, Keys(page), since, until, actOnly: false, ct).ConfigureAwait(false),
            CampaignActivityFilter.Contacted => await LatestWorkAsync(db, campaignId, Keys(page), since, until, actOnly: true, ct).ConfigureAwait(false),
            CampaignActivityFilter.Replied => await FirstReplyAsync(db, campaignId, Keys(page), ct).ConfigureAwait(false),
            CampaignActivityFilter.Removed => await RemovedAsync(db, campaignId, page, since, until, ct).ConfigureAwait(false),
            CampaignActivityFilter.OptedOut => await OptedOutAsync(db, page, since, until, ct).ConfigureAwait(false),
            // Several things may have happened to one person; their row says the latest, as a person's row does.
            _ => new Dictionary<int, MembershipWhy>(),
        };

    private static List<int> Keys(IReadOnlyList<Contact> page) => [.. page.Select(contact => contact.Id)];

    /// <summary>An approval first, the oldest of them; otherwise the oldest question raised by or naming their work.</summary>
    private static async Task<Dictionary<int, MembershipWhy>> WaitingAsync(JasonDbContext db, int campaignId, List<int> people, CancellationToken ct)
    {
        var approvals = await db.Approvals.AsNoTracking()
            .Where(approval => approval.CampaignId == campaignId && approval.Status == ApprovalStatus.Pending
                && approval.WorkItem!.ContactId != null && people.Contains(approval.WorkItem.ContactId.Value))
            .OrderBy(approval => approval.RequestedAt)
            .ThenBy(approval => approval.Id)
            .Select(approval => new { ContactId = approval.WorkItem!.ContactId!.Value, approval.PublicId, approval.Preview, approval.RequestedAt })
            .ToListAsync(ct)
            .ConfigureAwait(false);
        var questions = await db.Decisions.AsNoTracking()
            .Where(decision => decision.CampaignId == campaignId && decision.Status == DecisionStatus.Pending)
            .OrderBy(decision => decision.RaisedAt)
            .ThenBy(decision => decision.Id)
            .Select(decision => new { decision.PublicId, decision.Question, decision.RaisedAt, RaisedFor = decision.WorkItem!.ContactId, decision.References })
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var named = questions.SelectMany(question => ReferencedWork(question.References)).Distinct(StringComparer.Ordinal).ToList();
        var namedFor = await db.WorkItems.AsNoTracking()
            .Where(item => item.CampaignId == campaignId && named.Contains(item.PublicId) && item.ContactId != null && people.Contains(item.ContactId.Value))
            .Select(item => new { item.PublicId, ContactId = item.ContactId!.Value })
            .ToDictionaryAsync(item => item.PublicId, item => item.ContactId, StringComparer.Ordinal, ct)
            .ConfigureAwait(false);

        var why = new Dictionary<int, MembershipWhy>();
        foreach (var approval in approvals)
        {
            why.TryAdd(approval.ContactId, new MembershipWhy("approval", Text(approval.Preview["intent"]), null, Utc(approval.RequestedAt), approval.PublicId, null));
        }

        foreach (var question in questions)
        {
            var askedFor = ReferencedWork(question.References)
                .Where(namedFor.ContainsKey)
                .Select(id => namedFor[id])
                .Concat(question.RaisedFor is { } raisedFor && people.Contains(raisedFor) ? [raisedFor] : []);
            foreach (var person in askedFor)
            {
                why.TryAdd(person, new MembershipWhy("question", question.Question, null, Utc(question.RaisedAt), null, question.PublicId));
            }
        }

        return why;
    }

    private static async Task<Dictionary<int, MembershipWhy>> FailedAsync(JasonDbContext db, int campaignId, List<int> people, CancellationToken ct)
    {
        var finished = await db.WorkItems.AsNoTracking()
            .Where(item => item.CampaignId == campaignId && item.ContactId != null && people.Contains(item.ContactId.Value) && item.FinishedAt != null)
            .Select(item => new { ContactId = item.ContactId!.Value, item.Id, item.FinishedAt, item.Status, item.LastError })
            .ToListAsync(ct)
            .ConfigureAwait(false);
        return finished
            .GroupBy(item => item.ContactId)
            .Select(items => items.OrderByDescending(item => item.FinishedAt).ThenByDescending(item => item.Id).First())
            .ToDictionary(
                latest => latest.ContactId,
                latest => new MembershipWhy("failure", latest.LastError?.Message, latest.LastError?.Code ?? Name(latest.Status), Utc(latest.FinishedAt!.Value), null, null));
    }

    /// <summary>When they were taken out in the period: the latest line that removed them.</summary>
    private static async Task<Dictionary<int, MembershipWhy>> RemovedAsync(
        JasonDbContext db, int campaignId, IReadOnlyList<Contact> page, DateTime since, DateTime until, CancellationToken ct)
    {
        var lines = (await db.Database.SqlQueryRaw<Line>(Removals, campaignId, since, until).ToListAsync(ct).ConfigureAwait(false))
            .Select(line => new { line.Ts, New = line.Said is null ? null : JsonNode.Parse(line.Said) })
            .ToList();

        var why = new Dictionary<int, MembershipWhy>();
        foreach (var contact in page)
        {
            if (lines.FirstOrDefault(line => line.New?["contact_ids"] is JsonArray ids && ids.Any(id => Text(id) == contact.PublicId)) is { } line)
            {
                why[contact.Id] = new MembershipWhy("activity", null, "removed", Utc(line.Ts), null, null);
            }
        }

        return why;
    }

    /// <summary>When they joined in the period: the membership's own moment, or the line that re-added them.</summary>
    private static async Task<Dictionary<int, MembershipWhy>> AddedAsync(
        JasonDbContext db, int campaignId, IReadOnlyList<Contact> page, DateTime since, DateTime until, CancellationToken ct)
    {
        var people = Keys(page);
        var joined = await db.CampaignContacts.AsNoTracking()
            .Where(membership => membership.CampaignId == campaignId && people.Contains(membership.ContactId))
            .Select(membership => new { membership.ContactId, membership.AddedAt })
            .ToDictionaryAsync(membership => membership.ContactId, membership => membership.AddedAt, ct)
            .ConfigureAwait(false);
        var lines = (await db.Database.SqlQueryRaw<Line>(Additions, campaignId, since, until).ToListAsync(ct).ConfigureAwait(false))
            .Select(line => new { line.Ts, New = line.Said is null ? null : JsonNode.Parse(line.Said) })
            .ToList();

        var why = new Dictionary<int, MembershipWhy>();
        foreach (var contact in page)
        {
            DateTime? at = joined.TryGetValue(contact.Id, out var addedAt) && addedAt >= since && addedAt < until
                ? addedAt
                : lines.FirstOrDefault(line => line.New?["contact_ids"] is JsonArray ids && ids.Any(id => Text(id) == contact.PublicId))?.Ts;
            if (at is { } moment)
            {
                why[contact.Id] = new MembershipWhy("activity", null, "added", Utc(moment), null, null);
            }
        }

        return why;
    }

    private static async Task<Dictionary<int, MembershipWhy>> LatestWorkAsync(
        JasonDbContext db, int campaignId, List<int> people, DateTime since, DateTime until, bool actOnly, CancellationToken ct)
    {
        var query = db.WorkItems.AsNoTracking()
            .Where(item => item.CampaignId == campaignId && item.ContactId != null && people.Contains(item.ContactId.Value)
                && item.FinishedAt >= since && item.FinishedAt < until);
        query = actOnly
            ? query.Where(item => item.Kind == WorkItemKind.ProviderOp && item.Status == WorkItemStatus.Succeeded && ActOperations.Contains(item.Operation))
            : query.Where(item => item.Status == WorkItemStatus.Succeeded || item.Status == WorkItemStatus.Failed);
        var finished = await query
            .Select(item => new { ContactId = item.ContactId!.Value, item.Id, item.FinishedAt, item.Operation, item.Role, item.LastError })
            .ToListAsync(ct)
            .ConfigureAwait(false);
        return finished
            .Where(item => item.LastError?.Code != "approval_rejected")
            .GroupBy(item => item.ContactId)
            .Select(items => items.OrderByDescending(item => item.FinishedAt).ThenByDescending(item => item.Id).First())
            .ToDictionary(
                latest => latest.ContactId,
                latest => new MembershipWhy("activity", null, latest.Operation ?? latest.Role, Utc(latest.FinishedAt!.Value), null, null));
    }

    /// <summary>The first reply's moment and channel, the same reply <see cref="CampaignPeople"/> dates the count by.</summary>
    private static async Task<Dictionary<int, MembershipWhy>> FirstReplyAsync(JasonDbContext db, int campaignId, List<int> people, CancellationToken ct)
    {
        var replies = await db.Database.SqlQueryRaw<ReplySeen>(FirstReplies, campaignId, JsonSerializer.Serialize(people))
            .ToListAsync(ct)
            .ConfigureAwait(false);
        return replies
            .GroupBy(reply => reply.ContactId)
            .Select(seen => seen.MinBy(reply => reply.Day)!)
            .ToDictionary(first => first.ContactId, first => new MembershipWhy("activity", null, first.Channel, FromJulianDay(first.Day), null, null));
    }

    /// <summary>The newest opt-out in the period of a channel value the person has.</summary>
    private static async Task<Dictionary<int, MembershipWhy>> OptedOutAsync(
        JasonDbContext db, IReadOnlyList<Contact> page, DateTime since, DateTime until, CancellationToken ct)
    {
        var lines = (await db.Database.SqlQueryRaw<Line>(OptOuts, since, until).ToListAsync(ct).ConfigureAwait(false))
            .Select(line => new { line.Ts, New = line.Said is null ? null : JsonNode.Parse(line.Said) })
            .ToList();
        var why = new Dictionary<int, MembershipWhy>();
        foreach (var contact in page)
        {
            var line = lines.FirstOrDefault(line => contact.Channels.Any(channel =>
                Text(line.New?["channel"]) == channel.Channel && Text(line.New?["value"]) == channel.Value));
            if (line is not null)
            {
                why[contact.Id] = new MembershipWhy("activity", null, Text(line.New?["channel"]), Utc(line.Ts), null, null);
            }
        }

        return why;
    }

    private static IEnumerable<string> ReferencedWork(JsonArray? references) =>
        references is null
            ? []
            : references
                .OfType<JsonObject>()
                .Where(reference => Text(reference["kind"]) == "work_item")
                .Select(reference => Text(reference["id"]))
                .OfType<string>();

    private static string? Text(JsonNode? node) =>
        node is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;

    private static string Name(WorkItemStatus status) => JsonNamingPolicy.SnakeCaseLower.ConvertName(status.ToString());

    private static DateTimeOffset Utc(DateTime moment) => new(DateTime.SpecifyKind(moment, DateTimeKind.Utc));

    /// <summary>SQLite's julian day back to an instant, to the millisecond it was written with.</summary>
    private static DateTimeOffset FromJulianDay(double day) =>
        DateTimeOffset.FromUnixTimeMilliseconds((long)Math.Round((day - 2440587.5) * 86_400_000d, MidpointRounding.AwayFromZero));

    private sealed record ReplySeen(int ContactId, double Day, string? Channel);

    private sealed record Line(DateTime Ts, string? Said);
}
