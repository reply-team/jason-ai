using System.Collections.Frozen;
using System.Text.Json;
using Jason.Contracts.Api;
using Jason.Runtime.Events;
using Jason.Runtime.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Jason.Runtime.Campaigns;

/// <summary>
/// Which of a campaign's people each count means, written once: <c>campaign.summary</c> and <c>campaign.activity</c>
/// sum each definition per campaign and <c>campaign.list_contacts</c> filters by the same one, so a number and the list
/// it opens cannot disagree. <c>docs/work-execution.md</c> says each definition in words.
/// </summary>
/// <remarks>
/// <para>
/// One query of facts per membership — what their work, the questions and approvals waiting on it, and the journal say
/// about them — and each definition a condition over those facts. The facts are gathered a table at a time for the
/// campaigns asked, each row read once through an index and grouped by campaign and person, never searched for again per
/// person, so the reads hold at the volumes an installation reaches.
/// </para>
/// <para>
/// In SQL because those facts sit in JSON columns — an error's code, a question's references, what a line says — that
/// only SQLite's own functions reach. Work "about" a person is work whose campaign is the membership's and whose person
/// is the member; a line about a person is a line of the campaign whose person column is the member. <c>{0}</c> is the
/// campaigns asked as a JSON array of keys; an activity's period is <c>{1}</c> to <c>{2}</c>, since included and until
/// not.
/// </para>
/// </remarks>
internal static class CampaignPeople
{
    private const string Asked = "(SELECT value FROM json_each({0}))";

    /// <summary>
    /// The operations whose reach is act and that address a person: what Contacted counts. The counts, the series and the
    /// rows' whys read this one list.
    /// </summary>
    private const string ActOperationsSql = "'campaign.enroll', 'message.send', 'conversation.reply', 'social_invitation.send'";

    internal static readonly string[] ActOperations = [.. ActOperationsSql.Split(',').Select(name => name.Trim().Trim('\''))];

    private const string ContactedSql = $"w.kind = 'provider_op' AND w.status = 'succeeded' AND w.operation IN ({ActOperationsSql})";

    // Worked on: the work ended, done or failed; a rejection is a person's decision about the work, not the work failing.
    private const string WorkedOnSql = "(w.status = 'succeeded' OR (w.status = 'failed' AND json_extract(w.last_error_json, '$.code') IS NOT 'approval_rejected'))";

    // One pass over the campaigns' work, grouped by person: whether any is open or running, whether they were put into the
    // sequence or stopped there, and whether a failure is among their latest finished work — the latest failure finished
    // when their latest work did. A rejection ends the item as failed, but it is a person's decision about the work, not
    // the work failing. Work the runtime made about a person by itself, a responder's read of their thread say, is
    // nobody starting on them: only work a person, a role, an attempt or a plugin created makes them no longer new.
    private const string Work = $$"""
        SELECT w.campaign_id, w.contact_id,
               max(w.status IN ('created', 'awaiting_approval', 'scheduled', 'processing')) AS is_open,
               max(w.status IN ('created', 'scheduled', 'processing')) AS is_working,
               max(w.created_by_type <> 'system') AS has_own_work,
               max(w.kind = 'provider_op' AND w.operation = 'campaign.enroll' AND w.status = 'succeeded') AS is_enrolled,
               max(w.kind = 'provider_op' AND w.operation = 'enrollment.stop' AND w.status = 'succeeded') AS is_stopped,
               max(w.finished_at) AS last_finish,
               max(CASE WHEN w.status = 'expired'
                              OR (w.status = 'failed' AND json_extract(w.last_error_json, '$.code') IS NOT 'approval_rejected')
                         THEN w.finished_at END) AS last_failure
        FROM work_items AS w
        WHERE w.campaign_id IN {{Asked}} AND w.contact_id IS NOT NULL
        GROUP BY w.campaign_id, w.contact_id
        """;

    // A question counts for the people whose work raised it or whose work it names; one about the campaign as a whole
    // names nobody's work and counts for nobody. The named work is found from the few questions waiting, by its public id:
    // CROSS JOIN keeps SQLite from reading the campaigns' work first. A unary + on a status keeps the search on the
    // campaign's index, rather than on every pending approval or question of the installation.
    private const string WaitingOn = $$"""
        SELECT w.campaign_id, w.contact_id
        FROM approvals AS a JOIN work_items AS w ON w.id = a.work_item_id
        WHERE a.campaign_id IN {{Asked}} AND +a.status = 'pending' AND w.contact_id IS NOT NULL
        UNION
        SELECT w.campaign_id, w.contact_id
        FROM decisions AS d JOIN work_items AS w ON w.id = d.work_item_id
        WHERE d.campaign_id IN {{Asked}} AND +d.status = 'pending' AND w.contact_id IS NOT NULL
        UNION
        SELECT w.campaign_id, w.contact_id
        FROM decisions AS d CROSS JOIN json_each(d.references_json) AS r CROSS JOIN work_items AS w
        WHERE d.campaign_id IN {{Asked}} AND +d.status = 'pending' AND json_extract(r.value, '$.kind') = 'work_item'
          AND w.public_id = json_extract(r.value, '$.id') AND w.campaign_id = d.campaign_id AND w.contact_id IS NOT NULL
        """;

    // One arm per kind, so each reads its campaigns' lines of that kind through the kind's own index.
    private const string OutLines = $$"""
        SELECT jl.campaign_id, jl.contact_id
        FROM journal AS jl
        WHERE jl.campaign_id IN {{Asked}} AND jl.kind = 'inbound_reply_observed' AND jl.contact_id IS NOT NULL
        UNION
        SELECT jl.campaign_id, jl.contact_id
        FROM journal AS jl
        WHERE jl.campaign_id IN {{Asked}} AND jl.kind = 'provider_event_observed' AND jl.contact_id IS NOT NULL
          AND (json_extract(jl.new_json, '$.kind') = 'reply_detected'
               OR (json_extract(jl.new_json, '$.kind') = 'bounce' AND json_extract(jl.new_json, '$.bounce') = 'hard'))
        """;

    private const string Suppressed = $$"""
        SELECT DISTINCT ch.contact_id
        FROM campaign_contacts AS m
        JOIN contact_channels AS ch ON ch.contact_id = m.contact_id
        JOIN suppressions AS s ON s.channel = ch.channel AND s.value = ch.value
        WHERE m.campaign_id IN {{Asked}}
        """;

    private const string ReAdded = $$"""
        SELECT DISTINCT jl.campaign_id, p.id AS contact_id
        FROM journal AS jl INDEXED BY ix_journal_contacts_added_campaign_ts, json_each(jl.new_json, '$.contact_ids') AS added
        JOIN contacts AS p ON p.public_id = added.value
        WHERE jl.campaign_id IN {{Asked}} AND jl.kind = 'contacts_added' AND jl.ts >= {1} AND jl.ts < {2}
        """;

    private const string Removals = $$"""
        SELECT DISTINCT jl.campaign_id, p.id AS contact_id
        FROM journal AS jl INDEXED BY ix_journal_contacts_removed_campaign_ts, json_each(jl.new_json, '$.contact_ids') AS removed
        JOIN contacts AS p ON p.public_id = removed.value
        WHERE jl.campaign_id IN {{Asked}} AND jl.kind = 'contacts_removed' AND jl.ts >= {1} AND jl.ts < {2}
        """;

    // Worked on and contacted in the period, per person and campaign; research and bookkeeping never contact anyone. Read
    // by person, so the grouping streams: the index by finish would have every row of a long period sorted again.
    private const string Finished = $$"""
        SELECT w.campaign_id, w.contact_id,
               max({{WorkedOnSql}}) AS worked_on,
               max({{ContactedSql}}) AS contacted
        FROM work_items AS w INDEXED BY ix_work_items_campaign_id_contact_id
        WHERE w.campaign_id IN {{Asked}} AND w.contact_id IS NOT NULL AND w.finished_at >= {1} AND w.finished_at < {2}
        GROUP BY w.campaign_id, w.contact_id
        """;

    // A person's first reply in the campaign, dated by the provider's moment: a follow-up moves a replied thread again, and
    // a backfill writes a reply from months ago today. The moment is ISO-8601 text, so it is compared by its julian day.
    private const string FirstReply = $$"""
        SELECT r.campaign_id, r.contact_id, min(r.day) AS first
        FROM (SELECT jl.campaign_id, jl.contact_id, julianday(json_extract(jl.new_json, '$.last_activity_at')) AS day
              FROM journal AS jl
              WHERE jl.campaign_id IN {{Asked}} AND jl.kind = 'inbound_reply_observed' AND jl.contact_id IS NOT NULL
              UNION ALL
              SELECT jl.campaign_id, jl.contact_id, julianday(json_extract(jl.new_json, '$.occurred_at')) AS day
              FROM journal AS jl
              WHERE jl.campaign_id IN {{Asked}} AND jl.kind = 'provider_event_observed' AND jl.contact_id IS NOT NULL
                AND json_extract(jl.new_json, '$.kind') = 'reply_detected') AS r
        GROUP BY r.campaign_id, r.contact_id
        """;

    // The register forgets an entry once it is lifted; the journal does not. Read from the period's opt-outs, each found
    // among the channels by its value; whose campaign they are in is the memberships' join to say.
    private const string OptedOut = """
        SELECT DISTINCT ch.contact_id
        FROM journal AS jl INDEXED BY ix_journal_suppression_added_ts
        JOIN contact_channels AS ch ON ch.channel = json_extract(jl.new_json, '$.channel') AND ch.value = json_extract(jl.new_json, '$.value')
        WHERE jl.kind = 'suppression_added' AND jl.ts >= {1} AND jl.ts < {2}
        """;

    // Every membership, the removed ones too: Removed counts them, and every other count only the members (Current).
    private const string SummaryMembers = $"cc.campaign_id IN {Asked}";

    private const string Current = "f.state <> 'excluded'";

    // Everyone it happened to, removed since or not: last week does not change because somebody was taken out today.
    private const string ActivityMembers = $"cc.campaign_id IN {Asked}";

    private static readonly Part WorkPart = new(
        "wk",
        Work,
        ByPerson: false,
        "coalesce(wk.is_open, 0) AS is_open, coalesce(wk.is_working, 0) AS is_working, wk.contact_id IS NOT NULL AS has_work, "
        + "coalesce(wk.has_own_work, 0) AS has_own_work, "
        + "coalesce(wk.is_enrolled, 0) AS is_enrolled, coalesce(wk.is_stopped, 0) AS is_stopped, coalesce(wk.last_failure = wk.last_finish, 0) AS last_failed");

    private static readonly Part WaitingPart = new("wo", WaitingOn, ByPerson: false, "wo.contact_id IS NOT NULL AS is_waiting");

    private static readonly Part OutPart = new("ol", OutLines, ByPerson: false, "ol.contact_id IS NOT NULL AS has_out_line");

    private static readonly Part SuppressedPart = new("sp", Suppressed, ByPerson: true, "sp.contact_id IS NOT NULL AS is_suppressed");

    private static readonly Part ReAddedPart = new(
        "ra", ReAdded, ByPerson: false, "((cc.added_at >= {1} AND cc.added_at < {2}) OR ra.contact_id IS NOT NULL) AS was_added");

    private static readonly Part FinishedPart = new(
        "fw", Finished, ByPerson: false, "coalesce(fw.worked_on, 0) AS was_worked_on, coalesce(fw.contacted, 0) AS was_contacted");

    private static readonly Part FirstReplyPart = new(
        "fr", FirstReply, ByPerson: false, "coalesce(fr.first >= julianday({1}) AND fr.first < julianday({2}), 0) AS has_replied");

    private static readonly Part OptedOutPart = new("oo", OptedOut, ByPerson: true, "oo.contact_id IS NOT NULL AS has_opted_out");

    private static readonly Part RemovedPart = new("rm", Removals, ByPerson: false, "rm.contact_id IS NOT NULL AS was_removed");

    private const string OutNow = "(f.has_out_line OR f.is_suppressed OR f.is_stopped)";

    // Each definition names the facts it reads: a list joins only those, the counts join every one once.
    private static readonly Definition<CampaignSummaryFilter>[] SummaryDefinitions =
    [
        new(CampaignSummaryFilter.Waiting, $"({Current} AND f.is_waiting)", [WaitingPart]),
        new(CampaignSummaryFilter.Failed, $"({Current} AND NOT f.is_open AND f.last_failed)", [WorkPart]),
        new(CampaignSummaryFilter.Working, $"({Current} AND f.is_working)", [WorkPart]),
        new(CampaignSummaryFilter.New, $"({Current} AND NOT f.has_own_work)", [WorkPart]),
        new(CampaignSummaryFilter.Idle, $"({Current} AND f.has_work AND NOT f.is_open AND NOT f.last_failed AND NOT f.is_enrolled)", [WorkPart]),
        new(CampaignSummaryFilter.Sequence, $"({Current} AND f.is_enrolled AND NOT {OutNow})", [WorkPart, OutPart, SuppressedPart]),
        new(CampaignSummaryFilter.Out, $"({Current} AND {OutNow})", [WorkPart, OutPart, SuppressedPart]),
        new(CampaignSummaryFilter.Removed, "f.state = 'excluded'", []),
    ];

    private static readonly Definition<CampaignActivityFilter>[] Happenings =
    [
        new(CampaignActivityFilter.Added, "f.was_added", [ReAddedPart]),
        new(CampaignActivityFilter.WorkedOn, "f.was_worked_on", [FinishedPart]),
        new(CampaignActivityFilter.Contacted, "f.was_contacted", [FinishedPart]),
        new(CampaignActivityFilter.Replied, "f.has_replied", [FirstReplyPart]),
        new(CampaignActivityFilter.OptedOut, "f.has_opted_out", [OptedOutPart]),
        new(CampaignActivityFilter.Removed, "f.was_removed", [RemovedPart]),
    ];

    // People: anything above happened to them in the period.
    private static readonly Definition<CampaignActivityFilter>[] ActivityDefinitions =
    [
        new(
            CampaignActivityFilter.People,
            $"({string.Join(" OR ", Happenings.Select(happening => happening.Condition))})",
            [.. Happenings.SelectMany(happening => happening.Parts).Distinct()]),
        .. Happenings,
    ];

    /// <summary>Each Summary count's people, as rows of <c>campaign_contacts</c>.</summary>
    internal static readonly FrozenDictionary<CampaignSummaryFilter, string> Summary = SummaryDefinitions.ToFrozenDictionary(
        definition => definition.Filter,
        definition => $"SELECT * FROM ({Facts(definition.Parts, SummaryMembers)}) AS f WHERE {definition.Condition}");

    /// <summary>
    /// The untouched members of one campaign: the summary's <c>new</c>, by the same SQL, which is what the reserve step
    /// counts, so the number it says and the number the summary shows cannot drift apart.
    /// </summary>
    internal static readonly string UntouchedCountSql = $"SELECT count(*) AS Value FROM ({Summary[CampaignSummaryFilter.New]}) AS n";

    /// <summary>Every Summary count per campaign, from the same conditions the lists filter by.</summary>
    internal static readonly string SummaryCountsSql =
        $"SELECT f.campaign_id AS CampaignId, sum(CASE WHEN {Current} THEN 1 ELSE 0 END) AS People, {Sums(SummaryDefinitions)} "
        + $"FROM ({Facts(SummaryDefinitions.SelectMany(definition => definition.Parts), SummaryMembers)}) AS f GROUP BY f.campaign_id";

    internal static readonly string ActivityCountsSql =
        $"SELECT f.campaign_id AS CampaignId, {Sums(ActivityDefinitions)} "
        + $"FROM ({Facts(ActivityDefinitions.SelectMany(definition => definition.Parts), ActivityMembers)}) AS f GROUP BY f.campaign_id";

    // When each Activity count's happenings happened, one row each, dated where the list's why dates it: an addition by
    // the membership or the line that added the person back, work by its finish, a reply by the provider's moment of the
    // first, an opt-out and a removal by their line. Only members are counted, as the counts count memberships. Work is
    // read by its finish, so a short window reads only the work that finished in it.
    private const string AddedMoments = $$"""
        SELECT cc.campaign_id, cc.contact_id, julianday(cc.added_at) AS day
        FROM campaign_contacts AS cc
        WHERE cc.campaign_id IN {{Asked}} AND cc.added_at >= {1} AND cc.added_at < {2}
        UNION ALL
        SELECT jl.campaign_id, p.id AS contact_id, julianday(jl.ts) AS day
        FROM journal AS jl INDEXED BY ix_journal_contacts_added_campaign_ts, json_each(jl.new_json, '$.contact_ids') AS added
        JOIN contacts AS p ON p.public_id = added.value
        WHERE jl.campaign_id IN {{Asked}} AND jl.kind = 'contacts_added' AND jl.ts >= {1} AND jl.ts < {2}
        """;

    private const string WorkedOnMoments = $$"""
        SELECT w.campaign_id, w.contact_id, julianday(w.finished_at) AS day
        FROM work_items AS w INDEXED BY ix_work_items_campaign_id_finished_at
        WHERE w.campaign_id IN {{Asked}} AND w.contact_id IS NOT NULL AND w.finished_at >= {1} AND w.finished_at < {2}
          AND {{WorkedOnSql}}
        """;

    private const string ContactedMoments = $$"""
        SELECT w.campaign_id, w.contact_id, julianday(w.finished_at) AS day
        FROM work_items AS w INDEXED BY ix_work_items_campaign_id_finished_at
        WHERE w.campaign_id IN {{Asked}} AND w.contact_id IS NOT NULL AND w.finished_at >= {1} AND w.finished_at < {2}
          AND {{ContactedSql}}
        """;

    private const string RepliedMoments = $$"""
        SELECT fr.campaign_id, fr.contact_id, fr.first AS day
        FROM ({{FirstReply}}) AS fr
        WHERE fr.first >= julianday({1}) AND fr.first < julianday({2})
        """;

    private const string OptedOutMoments = $$"""
        SELECT m.campaign_id, ch.contact_id, julianday(jl.ts) AS day
        FROM journal AS jl INDEXED BY ix_journal_suppression_added_ts
        JOIN contact_channels AS ch ON ch.channel = json_extract(jl.new_json, '$.channel') AND ch.value = json_extract(jl.new_json, '$.value')
        JOIN campaign_contacts AS m ON m.contact_id = ch.contact_id
        WHERE m.campaign_id IN {{Asked}} AND jl.kind = 'suppression_added' AND jl.ts >= {1} AND jl.ts < {2}
        """;

    private const string RemovedMoments = $$"""
        SELECT jl.campaign_id, p.id AS contact_id, julianday(jl.ts) AS day
        FROM journal AS jl INDEXED BY ix_journal_contacts_removed_campaign_ts, json_each(jl.new_json, '$.contact_ids') AS removed
        JOIN contacts AS p ON p.public_id = removed.value
        WHERE jl.campaign_id IN {{Asked}} AND jl.kind = 'contacts_removed' AND jl.ts >= {1} AND jl.ts < {2}
        """;

    private static readonly FrozenDictionary<CampaignActivityFilter, string> HappeningMoments = new Dictionary<CampaignActivityFilter, string>
    {
        [CampaignActivityFilter.Added] = AddedMoments,
        [CampaignActivityFilter.WorkedOn] = WorkedOnMoments,
        [CampaignActivityFilter.Contacted] = ContactedMoments,
        [CampaignActivityFilter.Replied] = RepliedMoments,
        [CampaignActivityFilter.OptedOut] = OptedOutMoments,
        [CampaignActivityFilter.Removed] = RemovedMoments,
    }.ToFrozenDictionary();

    // People is every count's happenings; Contacted's are work that finished, so Worked on already holds them.
    private static readonly string People = string.Join(
        " UNION ALL ", HappeningMoments.Where(pair => pair.Key != CampaignActivityFilter.Contacted).Select(pair => pair.Value));

    /// <summary>
    /// Each Activity count's happenings in the period, a member's id and the moment as Unix milliseconds, which SQLite's
    /// julian day rounds to exactly; People is every count's.
    /// </summary>
    internal static readonly FrozenDictionary<CampaignActivityFilter, string> Moments = Enum.GetValues<CampaignActivityFilter>().ToFrozenDictionary(
        count => count,
        count => "SELECT h.contact_id AS ContactId, CAST(round((h.day - 2440587.5) * 86400000.0) AS INTEGER) AS Ms "
            + $"FROM ({(count == CampaignActivityFilter.People ? People : HappeningMoments[count])}) AS h "
            + "JOIN campaign_contacts AS cc ON cc.campaign_id = h.campaign_id AND cc.contact_id = h.contact_id");

    /// <summary>
    /// The people one Activity count counts across the campaigns asked, once each, with the latest moment it happened to
    /// them: what an activity listing orders by, newest first.
    /// </summary>
    internal static readonly FrozenDictionary<CampaignActivityFilter, string> PeopleByMoment = Moments.ToFrozenDictionary(
        pair => pair.Key,
        pair => "SELECT bym.ContactId AS ContactId, p.public_id AS PublicId, max(bym.Ms) AS Ms "
            + $"FROM ({pair.Value}) AS bym JOIN contacts AS p ON p.id = bym.ContactId GROUP BY bym.ContactId, p.public_id");

    /// <summary>Every Summary count across the campaigns asked, a person once, from the same conditions.</summary>
    internal static readonly string SummaryTotalSql =
        $"SELECT 0 AS CampaignId, count(DISTINCT CASE WHEN {Current} THEN f.contact_id END) AS People, {Distincts(SummaryDefinitions)} "
        + $"FROM ({Facts(SummaryDefinitions.SelectMany(definition => definition.Parts), SummaryMembers)}) AS f";

    internal static readonly string ActivityTotalSql =
        $"SELECT 0 AS CampaignId, {Distincts(ActivityDefinitions)} "
        + $"FROM ({Facts(ActivityDefinitions.SelectMany(definition => definition.Parts), ActivityMembers)}) AS f";

    /// <summary>The people of these campaigns one Summary count counts, now.</summary>
    public static IQueryable<CampaignContact> Matching(JasonDbContext db, IReadOnlyCollection<int> campaignIds, CampaignSummaryFilter filter)
    {
        ArgumentNullException.ThrowIfNull(db);
        return db.CampaignContacts.FromSqlRaw(Summary[filter], Keys(campaignIds));
    }

    /// <summary>Every Summary count of each campaign asked that has people, in one read.</summary>
    public static Task<Dictionary<int, SummaryCounts>> SummaryCountsAsync(JasonDbContext db, IReadOnlyCollection<int> campaignIds, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(db);
        return db.Database.SqlQueryRaw<SummaryCounts>(SummaryCountsSql, Keys(campaignIds)).ToDictionaryAsync(counts => counts.CampaignId, ct);
    }

    /// <summary>How many untouched members one campaign has now: its <c>new</c>, read on its own.</summary>
    public static Task<int> UntouchedAsync(JasonDbContext db, int campaignId, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(db);
        return db.Database.SqlQueryRaw<int>(UntouchedCountSql, Keys([campaignId])).SingleAsync(ct);
    }

    /// <summary>Every Activity count of each campaign asked that has had people, in one read.</summary>
    public static Task<Dictionary<int, ActivityCounts>> ActivityCountsAsync(
        JasonDbContext db, IReadOnlyCollection<int> campaignIds, DateTime since, DateTime until, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(db);
        return db.Database.SqlQueryRaw<ActivityCounts>(ActivityCountsSql, Keys(campaignIds), Utc(since), Utc(until))
            .ToDictionaryAsync(counts => counts.CampaignId, ct);
    }

    /// <summary>Every Summary count across the campaigns asked, each person once.</summary>
    public static Task<SummaryCounts> SummaryTotalAsync(JasonDbContext db, IReadOnlyCollection<int> campaignIds, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(db);
        return db.Database.SqlQueryRaw<SummaryCounts>(SummaryTotalSql, Keys(campaignIds)).SingleAsync(ct);
    }

    /// <summary>Every Activity count across the campaigns asked, each person once.</summary>
    public static Task<ActivityCounts> ActivityTotalAsync(
        JasonDbContext db, IReadOnlyCollection<int> campaignIds, DateTime since, DateTime until, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(db);
        return db.Database.SqlQueryRaw<ActivityCounts>(ActivityTotalSql, Keys(campaignIds), Utc(since), Utc(until)).SingleAsync(ct);
    }

    /// <summary>The people of one Activity count across the campaigns asked, each with the latest moment it happened, to compose on.</summary>
    public static IQueryable<PersonMoment> ByMoment(
        JasonDbContext db, IReadOnlyCollection<int> campaignIds, CampaignActivityFilter count, DateTime since, DateTime until)
    {
        ArgumentNullException.ThrowIfNull(db);
        return db.Database.SqlQueryRaw<PersonMoment>(PeopleByMoment[count], Keys(campaignIds), Utc(since), Utc(until));
    }

    /// <summary>Every happening of one Activity count in the period, across the campaigns asked, in no order.</summary>
    public static Task<List<Moment>> MomentsAsync(
        JasonDbContext db, IReadOnlyCollection<int> campaignIds, CampaignActivityFilter count, DateTime since, DateTime until, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(db);
        return db.Database.SqlQueryRaw<Moment>(Moments[count], Keys(campaignIds), Utc(since), Utc(until)).ToListAsync(ct);
    }

    /// <summary>The campaigns among these whose provider campaign is pinned: only they can say who is in the sequence.</summary>
    public static async Task<IReadOnlySet<int>> PinnedAsync(JasonDbContext db, IReadOnlyCollection<int> campaignIds, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(campaignIds);
        var pinned = await db.ExternalIds.AsNoTracking()
            .Where(pin => pin.CampaignId != null && campaignIds.Contains(pin.CampaignId.Value) && pin.Kind == CampaignPinRule.Kind)
            .Select(pin => pin.CampaignId!.Value)
            .Distinct()
            .ToListAsync(ct)
            .ConfigureAwait(false);
        return pinned.ToHashSet();
    }

    private static string Distincts<TFilter>(Definition<TFilter>[] definitions)
        where TFilter : struct, Enum =>
        string.Join(", ", definitions.Select(definition => $"count(DISTINCT CASE WHEN {definition.Condition} THEN f.contact_id END) AS {definition.Filter}"));

    private static string Sums<TFilter>(Definition<TFilter>[] definitions)
        where TFilter : struct, Enum =>
        string.Join(", ", definitions.Select(definition => $"sum(CASE WHEN {definition.Condition} THEN 1 ELSE 0 END) AS {definition.Filter}"));

    /// <summary>The memberships asked about, each with the facts these parts say about them, every part joined once.</summary>
    private static string Facts(IEnumerable<Part> parts, string members)
    {
        var used = parts.Distinct().ToList();
        var columns = string.Concat(used.Select(part => ", " + part.Columns));
        var joins = string.Concat(used.Select(part => $" LEFT JOIN ({part.Sql}) AS {part.Alias} ON "
            + (part.ByPerson
                ? $"{part.Alias}.contact_id = cc.contact_id"
                : $"{part.Alias}.campaign_id = cc.campaign_id AND {part.Alias}.contact_id = cc.contact_id")));
        return $"SELECT cc.*{columns} FROM campaign_contacts AS cc{joins} WHERE {members}";
    }

    /// <summary>One table of facts about people, joined by campaign and person, or by person alone for what holds across campaigns.</summary>
    private sealed record Part(string Alias, string Sql, bool ByPerson, string Columns);

    private sealed record Definition<TFilter>(TFilter Filter, string Condition, Part[] Parts)
        where TFilter : struct, Enum;

    private static string Keys(IReadOnlyCollection<int> campaignIds)
    {
        ArgumentNullException.ThrowIfNull(campaignIds);
        return JsonSerializer.Serialize(campaignIds);
    }

    private static DateTime Utc(DateTime moment) => moment.Kind == DateTimeKind.Utc ? moment : moment.ToUniversalTime();
}

/// <summary>One happening of a count: whose, and when, in Unix milliseconds.</summary>
internal sealed record Moment(int ContactId, long Ms);

/// <summary>A person one count counts, and the latest moment it happened to them, in Unix milliseconds.</summary>
internal sealed record PersonMoment(int ContactId, string PublicId, long Ms);

/// <summary>One campaign's Summary counts, as <see cref="CampaignPeople.SummaryCountsAsync"/> reads them.</summary>
internal sealed record SummaryCounts(int CampaignId, int People, int Waiting, int Failed, int Working, int New, int Idle, int Sequence, int Out, int Removed);

/// <summary>One campaign's Activity counts, as <see cref="CampaignPeople.ActivityCountsAsync"/> reads them.</summary>
internal sealed record ActivityCounts(int CampaignId, int People, int Added, int WorkedOn, int Contacted, int Replied, int OptedOut, int Removed);
