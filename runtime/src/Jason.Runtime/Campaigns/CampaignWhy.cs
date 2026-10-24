using System.Text.Json;
using System.Text.Json.Nodes;
using Jason.Contracts.Api;
using Jason.Runtime.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Jason.Runtime.Campaigns;

/// <summary>
/// What each person of one page of a filtered <c>campaign.list_contacts</c> is counted for, in each campaign that counts
/// them, so a row needs no read of its own: one query per kind of fact for the whole page, never one per person. Keyed by
/// the contact's and the campaign's internal ids.
/// </summary>
internal static class CampaignWhy
{
    // The page's people's reply lines in the campaigns ({0}), dated as the count dates them; one arm per kind, each through
    // the person's own index: a unary + on the kind keeps SQLite from reading every line of that kind instead.
    internal const string FirstReplies = """
        SELECT j.contact_id AS ContactId, j.campaign_id AS CampaignId, julianday(json_extract(j.new_json, '$.last_activity_at')) AS Day,
               json_extract(j.new_json, '$.channel') AS Channel
        FROM journal AS j
        WHERE j.contact_id IN (SELECT value FROM json_each({1})) AND j.campaign_id IN (SELECT value FROM json_each({0}))
          AND +j.kind = 'inbound_reply_observed'
        UNION ALL
        SELECT j.contact_id, j.campaign_id, julianday(json_extract(j.new_json, '$.occurred_at')), json_extract(j.new_json, '$.channel')
        FROM journal AS j
        WHERE j.contact_id IN (SELECT value FROM json_each({1})) AND j.campaign_id IN (SELECT value FROM json_each({0}))
          AND +j.kind = 'provider_event_observed' AND json_extract(j.new_json, '$.kind') = 'reply_detected'
        """;

    // The page's people's finished work in the campaigns ({0} the campaigns, {1} the people), through each person's own
    // index: a person's work is a handful of rows, where the campaigns' is every one of theirs.
    internal const string FinishedWork = """
        SELECT w.contact_id AS ContactId, w.campaign_id AS CampaignId, w.id AS Id, w.finished_at AS FinishedAt, w.status AS Status,
               w.kind AS Kind, w.operation AS Operation, w.role AS Role,
               json_extract(w.last_error_json, '$.code') AS Code, json_extract(w.last_error_json, '$.message') AS Message
        FROM work_items AS w INDEXED BY ix_work_items_contact_id
        WHERE w.contact_id IN (SELECT value FROM json_each({1})) AND w.campaign_id IN (SELECT value FROM json_each({0}))
          AND w.finished_at IS NOT NULL
        """;

    // The campaigns' additions of the period ({0} the campaigns, {1} to {2}), through their own index, as the count reads them.
    internal const string Additions = """
        SELECT j.campaign_id AS CampaignId, j.ts AS Ts, j.new_json AS Said
        FROM journal AS j INDEXED BY ix_journal_contacts_added_campaign_ts
        WHERE j.campaign_id IN (SELECT value FROM json_each({0})) AND j.kind = 'contacts_added' AND j.ts >= {1} AND j.ts < {2}
        ORDER BY j.id DESC
        """;

    // The campaigns' removals of the period ({0} the campaigns, {1} to {2}), through their own index, as the count reads them.
    internal const string Removals = """
        SELECT j.campaign_id AS CampaignId, j.ts AS Ts, j.new_json AS Said
        FROM journal AS j INDEXED BY ix_journal_contacts_removed_campaign_ts
        WHERE j.campaign_id IN (SELECT value FROM json_each({0})) AND j.kind = 'contacts_removed' AND j.ts >= {1} AND j.ts < {2}
        ORDER BY j.id DESC
        """;

    // The page's people's opt-outs of the period ({0} to {1}; {2} the people), through their own index, as the count
    // reads them: each line found among the people's channels by its value.
    internal const string OptOuts = """
        SELECT ch.contact_id AS ContactId, j.ts AS Ts, ch.channel AS Channel
        FROM journal AS j INDEXED BY ix_journal_suppression_added_ts
        JOIN contact_channels AS ch ON ch.channel = json_extract(j.new_json, '$.channel') AND ch.value = json_extract(j.new_json, '$.value')
        WHERE j.kind = 'suppression_added' AND j.ts >= {0} AND j.ts < {1} AND ch.contact_id IN (SELECT value FROM json_each({2}))
        ORDER BY j.id DESC
        """;

    /// <param name="db">The context to read through.</param>
    /// <param name="counted">The memberships the count counts on this page: the person and the campaign.</param>
    /// <param name="page">The page's people.</param>
    /// <param name="filter">The count.</param>
    /// <param name="ct">The request's token.</param>
    public static async Task<IReadOnlyDictionary<(int ContactId, int CampaignId), MembershipWhy>> ForSummaryAsync(
        JasonDbContext db,
        IReadOnlyCollection<(int ContactId, int CampaignId)> counted,
        IReadOnlyList<Contact> page,
        CampaignSummaryFilter filter,
        CancellationToken ct) => filter switch
        {
            CampaignSummaryFilter.Waiting => await WaitingAsync(db, Campaigns(counted), Keys(page), ct).ConfigureAwait(false),
            CampaignSummaryFilter.Failed => await FailedAsync(db, Campaigns(counted), Keys(page), ct).ConfigureAwait(false),
            _ => new Dictionary<(int, int), MembershipWhy>(),
        };

    /// <inheritdoc cref="ForSummaryAsync"/>
    public static async Task<IReadOnlyDictionary<(int ContactId, int CampaignId), MembershipWhy>> ForActivityAsync(
        JasonDbContext db,
        IReadOnlyCollection<(int ContactId, int CampaignId)> counted,
        IReadOnlyList<Contact> page,
        CampaignActivityFilter filter,
        DateTime since,
        DateTime until,
        CancellationToken ct)
    {
        var campaigns = Campaigns(counted);
        return filter switch
        {
            CampaignActivityFilter.Added => await AddedAsync(db, campaigns, page, since, until, ct).ConfigureAwait(false),
            CampaignActivityFilter.WorkedOn => await LatestWorkAsync(db, campaigns, Keys(page), since, until, actOnly: false, ct).ConfigureAwait(false),
            CampaignActivityFilter.Contacted => await LatestWorkAsync(db, campaigns, Keys(page), since, until, actOnly: true, ct).ConfigureAwait(false),
            CampaignActivityFilter.Replied => Within(await FirstReplyAsync(db, campaigns, Keys(page), ct).ConfigureAwait(false), since, until),
            CampaignActivityFilter.Removed => await RemovedAsync(db, campaigns, page, since, until, ct).ConfigureAwait(false),
            CampaignActivityFilter.OptedOut => await OptedOutAsync(db, counted, page, since, until, ct).ConfigureAwait(false),
            // Several things may have happened to one person; their row says the latest, as a person's row does, and its text
            // names the count, since a reply and an opt-out both say a channel. Contacted is work, so the latest work says it.
            _ => Latest(
                Named("added", await AddedAsync(db, campaigns, page, since, until, ct).ConfigureAwait(false)),
                Named("worked_on", await LatestWorkAsync(db, campaigns, Keys(page), since, until, actOnly: false, ct).ConfigureAwait(false)),
                Named("replied", Within(await FirstReplyAsync(db, campaigns, Keys(page), ct).ConfigureAwait(false), since, until)),
                Named("removed", await RemovedAsync(db, campaigns, page, since, until, ct).ConfigureAwait(false)),
                Named("opted_out", await OptedOutAsync(db, counted, page, since, until, ct).ConfigureAwait(false))),
        };
    }

    private static List<int> Keys(IReadOnlyList<Contact> page) => [.. page.Select(contact => contact.Id)];

    private static List<int> Campaigns(IReadOnlyCollection<(int ContactId, int CampaignId)> counted) => [.. counted.Select(pair => pair.CampaignId).Distinct()];

    /// <summary>A first reply counts in the period it fell in; one before it is no reply of this period.</summary>
    private static Dictionary<(int, int), MembershipWhy> Within(Dictionary<(int, int), MembershipWhy> read, DateTime since, DateTime until) =>
        read.Where(pair => pair.Value.At >= Utc(since) && pair.Value.At < Utc(until)).ToDictionary(pair => pair.Key, pair => pair.Value);

    private static Dictionary<(int, int), MembershipWhy> Named(string count, Dictionary<(int, int), MembershipWhy> read) =>
        read.ToDictionary(pair => pair.Key, pair => pair.Value with { Text = count });

    /// <summary>For each person and campaign, the latest of what several reads say happened.</summary>
    private static Dictionary<(int, int), MembershipWhy> Latest(params Dictionary<(int, int), MembershipWhy>[] reads)
    {
        var latest = new Dictionary<(int, int), MembershipWhy>();
        foreach (var (key, why) in reads.SelectMany(read => read))
        {
            if (!latest.TryGetValue(key, out var seen) || why.At > seen.At)
            {
                latest[key] = why;
            }
        }

        return latest;
    }

    /// <summary>An approval first, the oldest of them; otherwise the oldest question raised by or naming their work.</summary>
    private static async Task<Dictionary<(int, int), MembershipWhy>> WaitingAsync(JasonDbContext db, List<int> campaigns, List<int> people, CancellationToken ct)
    {
        var approvals = await db.Approvals.AsNoTracking()
            .Where(approval => campaigns.Contains(approval.CampaignId) && approval.Status == ApprovalStatus.Pending
                && approval.WorkItem!.ContactId != null && people.Contains(approval.WorkItem.ContactId.Value))
            .OrderBy(approval => approval.RequestedAt)
            .ThenBy(approval => approval.Id)
            .Select(approval => new { ContactId = approval.WorkItem!.ContactId!.Value, approval.CampaignId, approval.PublicId, approval.Preview, approval.RequestedAt })
            .ToListAsync(ct)
            .ConfigureAwait(false);
        var questions = await db.Decisions.AsNoTracking()
            .Where(decision => campaigns.Contains(decision.CampaignId) && decision.Status == DecisionStatus.Pending)
            .OrderBy(decision => decision.RaisedAt)
            .ThenBy(decision => decision.Id)
            .Select(decision => new { decision.PublicId, decision.CampaignId, decision.Question, decision.RaisedAt, RaisedFor = decision.WorkItem!.ContactId, decision.References })
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var named = questions.SelectMany(question => ReferencedWork(question.References)).Distinct(StringComparer.Ordinal).ToList();
        var namedFor = await db.WorkItems.AsNoTracking()
            .Where(item => campaigns.Contains(item.CampaignId) && named.Contains(item.PublicId) && item.ContactId != null && people.Contains(item.ContactId.Value))
            .Select(item => new { item.PublicId, item.CampaignId, ContactId = item.ContactId!.Value })
            .ToDictionaryAsync(item => item.PublicId, item => (item.CampaignId, item.ContactId), StringComparer.Ordinal, ct)
            .ConfigureAwait(false);

        var why = new Dictionary<(int, int), MembershipWhy>();
        foreach (var approval in approvals)
        {
            why.TryAdd(
                (approval.ContactId, approval.CampaignId),
                new MembershipWhy("approval", Text(approval.Preview["intent"]), null, Utc(approval.RequestedAt), approval.PublicId, null));
        }

        foreach (var question in questions)
        {
            // A question names work of its own campaign; the work's person is who it waits on, there.
            var askedFor = ReferencedWork(question.References)
                .Where(id => namedFor.TryGetValue(id, out var work) && work.CampaignId == question.CampaignId)
                .Select(id => namedFor[id].ContactId)
                .Concat(question.RaisedFor is { } raisedFor && people.Contains(raisedFor) ? [raisedFor] : []);
            foreach (var person in askedFor)
            {
                why.TryAdd((person, question.CampaignId), new MembershipWhy("question", question.Question, null, Utc(question.RaisedAt), null, question.PublicId));
            }
        }

        return why;
    }

    private static async Task<Dictionary<(int, int), MembershipWhy>> FailedAsync(JasonDbContext db, List<int> campaigns, List<int> people, CancellationToken ct)
    {
        var finished = await FinishedAsync(db, campaigns, people, ct).ConfigureAwait(false);
        return finished
            .GroupBy(item => (item.ContactId, item.CampaignId))
            .Select(items => items.OrderByDescending(item => item.FinishedAt).ThenByDescending(item => item.Id).First())
            .ToDictionary(
                latest => (latest.ContactId, latest.CampaignId),
                latest => new MembershipWhy("failure", latest.Message, latest.Code ?? latest.Status, Utc(latest.FinishedAt), null, null));
    }

    private static Task<List<Finished>> FinishedAsync(JasonDbContext db, List<int> campaigns, List<int> people, CancellationToken ct) =>
        db.Database.SqlQueryRaw<Finished>(FinishedWork, JsonSerializer.Serialize(campaigns), JsonSerializer.Serialize(people)).ToListAsync(ct);

    /// <summary>When they were taken out in the period: the latest line that removed them.</summary>
    private static async Task<Dictionary<(int, int), MembershipWhy>> RemovedAsync(
        JasonDbContext db, List<int> campaigns, IReadOnlyList<Contact> page, DateTime since, DateTime until, CancellationToken ct)
    {
        var lines = await LinesAsync(db, Removals, ct, JsonSerializer.Serialize(campaigns), since, until).ConfigureAwait(false);
        var why = new Dictionary<(int, int), MembershipWhy>();
        foreach (var contact in page)
        {
            foreach (var line in lines.Where(line => Names(line, contact)))
            {
                why.TryAdd((contact.Id, line.CampaignId), new MembershipWhy("activity", null, "removed", Utc(line.Ts), null, null));
            }
        }

        return why;
    }

    /// <summary>When they joined in the period: the later of the membership's own moment and the line that re-added them.</summary>
    private static async Task<Dictionary<(int, int), MembershipWhy>> AddedAsync(
        JasonDbContext db, List<int> campaigns, IReadOnlyList<Contact> page, DateTime since, DateTime until, CancellationToken ct)
    {
        var people = Keys(page);
        var joined = await db.CampaignContacts.AsNoTracking()
            .Where(membership => campaigns.Contains(membership.CampaignId) && people.Contains(membership.ContactId)
                && membership.AddedAt >= since && membership.AddedAt < until)
            .Select(membership => new { membership.ContactId, membership.CampaignId, membership.AddedAt })
            .ToListAsync(ct)
            .ConfigureAwait(false);
        var lines = await LinesAsync(db, Additions, ct, JsonSerializer.Serialize(campaigns), since, until).ConfigureAwait(false);

        var at = joined.ToDictionary(membership => (membership.ContactId, membership.CampaignId), membership => membership.AddedAt);
        foreach (var contact in page)
        {
            // Lines come newest first: the first that names the person in a campaign is their latest re-adding there.
            foreach (var line in lines.Where(line => Names(line, contact)))
            {
                var key = (contact.Id, line.CampaignId);
                if (!at.TryGetValue(key, out var seen) || line.Ts > seen)
                {
                    at[key] = line.Ts;
                }
            }
        }

        return at.ToDictionary(pair => pair.Key, pair => new MembershipWhy("activity", null, "added", Utc(pair.Value), null, null));
    }

    private static async Task<Dictionary<(int, int), MembershipWhy>> LatestWorkAsync(
        JasonDbContext db, List<int> campaigns, List<int> people, DateTime since, DateTime until, bool actOnly, CancellationToken ct)
    {
        var finished = (await FinishedAsync(db, campaigns, people, ct).ConfigureAwait(false))
            .Where(item => item.FinishedAt >= since && item.FinishedAt < until)
            .Where(item => actOnly
                ? item.Kind == "provider_op" && item.Status == "succeeded" && CampaignPeople.ActOperations.Contains(item.Operation)
                : item.Status == "succeeded" || item.Status == "failed");
        return finished
            .Where(item => item.Code != "approval_rejected")
            .GroupBy(item => (item.ContactId, item.CampaignId))
            .Select(items => items.OrderByDescending(item => item.FinishedAt).ThenByDescending(item => item.Id).First())
            .ToDictionary(
                latest => (latest.ContactId, latest.CampaignId),
                latest => new MembershipWhy("activity", null, latest.Operation ?? latest.Role, Utc(latest.FinishedAt), null, null));
    }

    /// <summary>The first reply's moment and channel in each campaign, the same reply <see cref="CampaignPeople"/> dates the count by.</summary>
    private static async Task<Dictionary<(int, int), MembershipWhy>> FirstReplyAsync(JasonDbContext db, List<int> campaigns, List<int> people, CancellationToken ct)
    {
        var replies = await db.Database.SqlQueryRaw<ReplySeen>(FirstReplies, JsonSerializer.Serialize(campaigns), JsonSerializer.Serialize(people))
            .ToListAsync(ct)
            .ConfigureAwait(false);
        return replies
            .GroupBy(reply => (reply.ContactId, reply.CampaignId))
            .Select(seen => seen.MinBy(reply => reply.Day)!)
            .ToDictionary(first => (first.ContactId, first.CampaignId), first => new MembershipWhy("activity", null, first.Channel, FromJulianDay(first.Day), null, null));
    }

    /// <summary>
    /// The newest opt-out in the period of a channel value the person has. The register is the installation's, not a
    /// campaign's, so it says the same in every campaign that counts them.
    /// </summary>
    private static async Task<Dictionary<(int, int), MembershipWhy>> OptedOutAsync(
        JasonDbContext db, IReadOnlyCollection<(int ContactId, int CampaignId)> counted, IReadOnlyList<Contact> page, DateTime since, DateTime until, CancellationToken ct)
    {
        var lines = await db.Database.SqlQueryRaw<OptOut>(OptOuts, since, until, JsonSerializer.Serialize(Keys(page))).ToListAsync(ct).ConfigureAwait(false);
        var newest = lines.GroupBy(line => line.ContactId).ToDictionary(group => group.Key, group => group.First());
        var why = new Dictionary<(int, int), MembershipWhy>();
        foreach (var (contact, campaign) in counted)
        {
            if (newest.TryGetValue(contact, out var line))
            {
                why[(contact, campaign)] = new MembershipWhy("activity", null, line.Channel, Utc(line.Ts), null, null);
            }
        }

        return why;
    }

    private static async Task<List<ParsedLine>> LinesAsync(JasonDbContext db, string sql, CancellationToken ct, params object[] parameters) =>
        [.. (await db.Database.SqlQueryRaw<Line>(sql, parameters).ToListAsync(ct).ConfigureAwait(false))
            .Select(line => new ParsedLine(line.CampaignId, line.Ts, line.Said is null ? null : JsonNode.Parse(line.Said)))];

    private static bool Names(ParsedLine line, Contact contact) =>
        line.New?["contact_ids"] is JsonArray ids && ids.Any(id => Text(id) == contact.PublicId);

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

    private static DateTimeOffset Utc(DateTime moment) => new(DateTime.SpecifyKind(moment, DateTimeKind.Utc));

    /// <summary>SQLite's julian day back to an instant, to the millisecond it was written with.</summary>
    private static DateTimeOffset FromJulianDay(double day) =>
        DateTimeOffset.FromUnixTimeMilliseconds((long)Math.Round((day - 2440587.5) * 86_400_000d, MidpointRounding.AwayFromZero));

    private sealed record ReplySeen(int ContactId, int CampaignId, double Day, string? Channel);

    private sealed record Line(int CampaignId, DateTime Ts, string? Said);

    private sealed record ParsedLine(int CampaignId, DateTime Ts, JsonNode? New);

    private sealed record OptOut(int ContactId, DateTime Ts, string Channel);

    private sealed record Finished(int ContactId, int CampaignId, int Id, DateTime FinishedAt, string Status, string Kind, string? Operation, string? Role, string? Code, string? Message);
}
