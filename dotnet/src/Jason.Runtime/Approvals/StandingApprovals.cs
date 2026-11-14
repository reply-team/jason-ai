using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text.Json.Nodes;
using Jason.Contracts.Api;
using Jason.Contracts.Operations;
using Jason.Runtime.Audiences;
using Jason.Runtime.Dispatch;
using Jason.Runtime.Domain;
using Jason.Runtime.Journal;
using Jason.Runtime.Persistence;
using Jason.Runtime.Routing;
using Jason.Runtime.WorkItems;
using Microsoft.EntityFrameworkCore;

namespace Jason.Runtime.Approvals;

/// <summary>
/// A person's word, given in advance, that one campaign's drafted replies of one category may go without a decision each
/// time, or that its searches under one audience may, up to a ceiling in people (the allowance) — and the reads that say
/// whether it covers a piece of work the claim is about to park.
/// </summary>
/// <remarks>
/// <para>
/// The record is the chronicle: a <c>standing_approval_set</c> or <c>standing_approval_unset</c> line per change, keyed by
/// the category, or by <see cref="Searches"/> for the allowance, and the newest one for a campaign and a key is the state.
/// Each release under one is an <c>approval_standing_applied</c> line keyed by the line that set it, which is what its
/// ceiling counts: the lines themselves for a category, the people each search asked for for the allowance.
/// </para>
/// <para>
/// Every read here is written as SQL that names its index, and a test pins each plan. The kind index is on <c>kind</c>
/// alone. Its bound for these reads is the installation's lines of that kind: the standing-approval lines a person writes
/// by hand and the releases under them; a later verdict on a reply is read through the verdicts' thread index instead, by
/// the campaign, the thread and the key, which bounds it by that reply's own verdicts. The campaign index would bound the
/// others instead by the campaign's every line since the moment asked — and each reply a campaign answers brings about ten of its own (the responder's work
/// moving through its states, the verdict, the reply's work moving through its states, the release) besides what inbound
/// reading writes. So the kind index reads the smaller set unless an installation has more than about ten campaigns
/// releasing replies at once, and for a category never set it reads nothing where the campaign index reads the whole
/// chronicle.
/// </para>
/// </remarks>
public static class StandingApprovals
{
    /// <summary>The operations a standing approval may cover: a reply in the thread, and a message to the same person.</summary>
    public static IReadOnlyList<string> Coverable { get; } = ["conversation.reply", "message.send"];

    /// <summary>What a standing approval covers when it names nothing: a reply in the thread, never a message beside it.</summary>
    public static IReadOnlyList<string> DefaultOperations { get; } = ["conversation.reply"];

    /// <summary>The reserved work-item context key that names the verdict a drafted reply answers, by its line's id.</summary>
    public const string VerdictKey = "reply_verdict";

    /// <summary>The highest ceiling a standing approval may carry.</summary>
    public const int MaxReleases = 10_000;

    /// <summary>
    /// The operation an allowance covers: a search, under one of the campaign's audiences, until the people its searches
    /// asked for reach a ceiling. Its lines are keyed by the operation, so a campaign holds one allowance.
    /// </summary>
    public const string Searches = "audience.search";

    /// <summary>The highest ceiling in people an allowance may carry.</summary>
    public const int MaxPeople = 100_000;

    /// <summary>The highest cap per search: a search asks for at most this many people (<c>audience.search</c>'s <c>count</c>).</summary>
    public const int MaxPerSearch = 1_000;

    /// <summary>Why a standing approval did not release work it was asked about, in the order the claim asks.</summary>
    public static class Why
    {
        public const string NotCoverable = "not_coverable";
        public const string VerdictUnknown = "verdict_unknown";
        public const string VerdictSuperseded = "verdict_superseded";
        public const string ReactionNotDraft = "reaction_not_draft";
        public const string VerdictNotAuthentic = "verdict_not_authentic";
        public const string NotTheVerdictsWork = "not_the_verdicts_work";
        public const string Edited = "edited";
        public const string PluginDiffers = "plugin_differs";
        public const string ThreadDiffers = "thread_differs";
        public const string NoStandingApproval = "no_standing_approval";
        public const string OperationNotCovered = "operation_not_covered";
        public const string Expired = "expired";
        public const string CeilingReached = "ceiling_reached";
        public const string AnotherAudience = "another_audience";
        public const string OverPerSearch = "over_per_search";

        /// <summary>
        /// Covered in every other respect, but a doubt stands about the send — its creator's <c>ask_person</c>, or a doubting
        /// review of its text — and a doubt is the person's to answer, so no standing approval releases it.
        /// </summary>
        public const string Doubted = "doubted";

        /// <summary>
        /// Covered in every other respect, but the send was written in the desktop's chat, or its input edited there, and
        /// such a send waits for the person whatever stands for it, so no standing approval releases it.
        /// </summary>
        public const string SentFromChat = "sent_from_chat";

        /// <summary>
        /// Covered in every other respect, but the automatic mode would have released the send and the campaign has reached
        /// <c>Sending:AutomaticMaxPerDay</c> for the UTC day, which never slips, so no standing approval releases it either.
        /// </summary>
        public const string DailyCeiling = "daily_ceiling";
    }

    /// <summary>The newest standing-approval line of one campaign for one category, set or unset, through the kind index.</summary>
    internal static IQueryable<JournalEntry> Newest(JasonDbContext db, int campaignId, string category) =>
        db.Journal.FromSqlInterpolated($"""
            SELECT * FROM journal INDEXED BY ix_journal_kind
            WHERE kind IN ('standing_approval_set', 'standing_approval_unset') AND campaign_id = {campaignId} AND key = {category}
            ORDER BY id DESC LIMIT 1
            """).AsNoTracking();

    /// <summary>Every standing-approval line of one campaign, oldest first, through the kind index.</summary>
    internal static IQueryable<JournalEntry> Lines(JasonDbContext db, int campaignId) =>
        db.Journal.FromSqlInterpolated($"""
            SELECT * FROM journal INDEXED BY ix_journal_kind
            WHERE kind IN ('standing_approval_set', 'standing_approval_unset') AND campaign_id = {campaignId}
            ORDER BY id
            """).AsNoTracking();

    /// <summary>
    /// How many people the searches released under an allowance asked for since it was set: each release's count, added up
    /// over the same lines <see cref="Released"/> counts.
    /// </summary>
    internal static IQueryable<int> People(JasonDbContext db, JournalEntry set) =>
        db.Database.SqlQuery<int>($"""
            SELECT coalesce(sum(json_extract(new_json, '$.count')), 0) AS "Value" FROM journal INDEXED BY ix_journal_kind
            WHERE kind = 'approval_standing_applied' AND id > {set.Id} AND key = {set.PublicId}
            """);

    /// <summary>How many releases name the line that set a standing approval: the releases since it was set.</summary>
    internal static IQueryable<int> Released(JasonDbContext db, JournalEntry set) =>
        db.Database.SqlQuery<int>($"""
            SELECT count(*) AS "Value" FROM journal INDEXED BY ix_journal_kind
            WHERE kind = 'approval_standing_applied' AND id > {set.Id} AND key = {set.PublicId}
            """);

    /// <summary>
    /// A later verdict on the same reply in the same campaign, which is the one that stands: read through the verdicts'
    /// thread index by the campaign, the thread the verdict names and its key, which is a digest of the plugin, the thread
    /// and the message, so it is bounded by that reply's own verdicts and never by every verdict the installation holds.
    /// </summary>
    internal static IQueryable<int> Later(JasonDbContext db, JournalEntry verdict)
    {
        ArgumentNullException.ThrowIfNull(verdict);
        var thread = Text((verdict.New as JsonObject)?["thread"]);
        return db.Database.SqlQuery<int>($"""
            SELECT id AS "Value" FROM journal INDEXED BY ix_journal_campaign_verdict_thread
            WHERE kind = 'reply_classified' AND campaign_id = {verdict.CampaignId}
              AND json_extract(new_json, '$.thread') = {thread} AND key = {verdict.Key} AND id > {verdict.Id}
            LIMIT 1
            """);
    }

    /// <summary>A change to a work item's context after it was created, read from the item's own lines.</summary>
    internal static IQueryable<int> ContextChanges(JasonDbContext db, string workItemId) =>
        db.Database.SqlQuery<int>($"""
            SELECT id AS "Value" FROM journal INDEXED BY ix_journal_work_item_id_public_id
            WHERE work_item_id = {workItemId} AND kind = 'workitem_context_updated'
            LIMIT 1
            """);

    /// <summary>
    /// Whether anybody was ever asked about a work item: any approval row of its own, whatever became of it. Read through
    /// the campaign's approvals, so it is bounded by the campaign rather than by every approval the installation holds.
    /// </summary>
    internal static IQueryable<int> Asked(JasonDbContext db, int campaignId, int workItemId) =>
        db.Database.SqlQuery<int>($"""
            SELECT id AS "Value" FROM approvals INDEXED BY ix_approvals_campaign_id
            WHERE campaign_id = {campaignId} AND work_item_id = {workItemId}
            LIMIT 1
            """);

    /// <summary>The standing approval a line sets, as the record it holds.</summary>
    internal static Record Read(JournalEntry set)
    {
        ArgumentNullException.ThrowIfNull(set);
        var line = set.New as JsonObject ?? [];
        var operations = line["operations"] is JsonArray named
            ? [.. named.Select(node => node?.GetValue<string>()).OfType<string>()]
            : DefaultOperations;
        DateTimeOffset? expires = line["expires_at"] is JsonValue moment && moment.TryGetValue<string>(out var text)
            ? DateTimeOffset.Parse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal)
            : null;
        var audience = line["audience"] is JsonObject which
            && Text(which["id"]) is { } id
            && which["number"] is JsonValue held && held.TryGetValue<int>(out var number)
            ? new AudienceRef(id, number)
            : null;
        return new Record(set, Text(line["category"]), operations, expires, Whole(line["max_releases"]), audience, Whole(line["max_people"]), Whole(line["max_per_search"]));
    }

    /// <summary>
    /// The campaign's allowance as it stands, with how much of it is used: the newest line for <see cref="Searches"/> where
    /// it is a set that names an audience, the releases made under it and the people they asked for; null where there is
    /// none. One read, for every reader that says how far the allowance has gone.
    /// </summary>
    public static async Task<Allowance?> AllowanceAsync(JasonDbContext db, int campaignId, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(db);
        var newest = await Newest(db, campaignId, Searches).FirstOrDefaultAsync(ct).ConfigureAwait(false);
        return newest is { Kind: JournalKinds.StandingApprovalSet } ? await UsedAsync(db, newest, ct).ConfigureAwait(false) : null;
    }

    /// <summary>The allowance a set line holds, with the releases and people since; null where the line holds none.</summary>
    internal static async Task<Allowance?> UsedAsync(JasonDbContext db, JournalEntry set, CancellationToken ct)
    {
        var record = Read(set);
        if (record.Audience is null || record.MaxPeople is null)
        {
            return null;
        }

        var released = await Released(db, set).SingleAsync(ct).ConfigureAwait(false);
        var people = await People(db, set).SingleAsync(ct).ConfigureAwait(false);
        return new Allowance(record, released, people);
    }

    /// <summary>
    /// The record an allowance's set line writes: no category, the one operation, the expiry and no count of releases as a
    /// reply's has, then the audience by id and number, the ceiling in people and the cap per search.
    /// </summary>
    internal static JsonObject AllowanceLine(AudienceRef audience, int maxPeople, int? maxPerSearch, DateTimeOffset? expiresAt) => new()
    {
        ["category"] = null,
        ["operations"] = new JsonArray(Searches),
        ["expires_at"] = expiresAt is { } moment ? InboundSync.Instant(moment) : null,
        ["max_releases"] = null,
        ["audience"] = new JsonObject { ["id"] = audience.Id, ["number"] = audience.Number },
        ["max_people"] = maxPeople,
        ["max_per_search"] = maxPerSearch,
    };

    /// <summary>
    /// Writes an allowance, retiring the one it replaces: an unset of the newest set where there is one, then the set, both
    /// by <paramref name="actor"/> with the same reason. Nothing is saved here; the caller's act saves them with whatever
    /// else it writes.
    /// </summary>
    internal static JournalEntry Allow(JasonDbContext db, JournalWriter journal, ActorRef actor, Campaign campaign, JournalEntry? standing, JsonObject record, string? reason)
    {
        ArgumentNullException.ThrowIfNull(journal);
        if (standing is not null)
        {
            Retire(db, journal, actor, campaign, standing, reason);
        }

        return journal.Append(db, actor, JournalKinds.StandingApprovalSet, campaign, key: Searches, old: null, updated: record, reason: reason);
    }

    /// <summary>Takes back the standing approval <paramref name="standing"/> set, by <paramref name="actor"/>: an unset line, unsaved.</summary>
    internal static void Retire(JasonDbContext db, JournalWriter journal, ActorRef actor, Campaign campaign, JournalEntry standing, string? reason)
    {
        ArgumentNullException.ThrowIfNull(journal);
        ArgumentNullException.ThrowIfNull(standing);
        journal.Append(db, actor, JournalKinds.StandingApprovalUnset, campaign, key: standing.Key, old: standing.New?.DeepClone(), reason: reason);
    }

    /// <summary>
    /// The limits an allowance is held to, wherever a person gives one, each refusal at its field under
    /// <paramref name="prefix"/>: a ceiling in people, required; a cap per search no larger than a search may ask for; an
    /// expiry later than now.
    /// </summary>
    internal static void ValidateLimits(int? maxPeople, int? maxPerSearch, DateTimeOffset? expiresAt, DateTimeOffset now, ValidationErrors errors, string prefix = "")
    {
        ArgumentNullException.ThrowIfNull(errors);
        if (maxPeople is null)
        {
            errors.Add(prefix + "max_people", "required", "max_people is required: the most people the searches under the allowance may ask for.");
        }
        else if (maxPeople is < 1 or > MaxPeople)
        {
            errors.Add(prefix + "max_people", "invalid", string.Create(CultureInfo.InvariantCulture, $"max_people must be from 1 to {MaxPeople}."));
        }

        if (maxPerSearch is < 1 or > MaxPerSearch)
        {
            errors.Add(prefix + "max_per_search", "invalid", string.Create(CultureInfo.InvariantCulture, $"max_per_search must be from 1 to {MaxPerSearch}."));
        }

        if (expiresAt is { } expires && expires <= now)
        {
            errors.Add(prefix + "expires_at", "in_the_past", "expires_at must be a moment later than now.");
        }
    }

    /// <summary>The record a set line writes: the category, the operations, the expiry and the ceiling, nothing else.</summary>
    internal static JsonObject Line(string category, IReadOnlyList<string> operations, DateTimeOffset? expiresAt, int? maxReleases) => new()
    {
        ["category"] = category,
        ["operations"] = new JsonArray([.. operations.Select(operation => (JsonNode?)JsonValue.Create(operation))]),
        ["expires_at"] = expiresAt is { } moment ? InboundSync.Instant(moment) : null,
        ["max_releases"] = maxReleases,
    };

    /// <summary>
    /// Whether a standing approval covers this work, asked by the claim where no decision about the work is live yet: the
    /// cover, an uncovered answer that says why, or null for work that names no verdict, which parks as it always did.
    /// </summary>
    /// <remarks>
    /// The conditions are asked in this order and the first that fails names the answer. What runs under a standing
    /// approval is the draft its verdict's author made for that very reply, exactly as it was made: a verdict a role wrote
    /// outside the run that read the reply, or one a later verdict replaced, or a draft somebody else wrote or edited, is
    /// left for a person. A verdict that carries a person's answer to a question about the reply is that person's, whoever
    /// recorded it.
    /// </remarks>
    internal static async Task<Answer?> CoverAsync(JasonDbContext db, WorkItem item, ProviderOpPlan plan, DateTime now, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(plan);

        if (string.Equals(plan.Contract.Id, Searches, StringComparison.Ordinal))
        {
            return await AllowanceCoverAsync(db, item, plan, now, ct).ConfigureAwait(false);
        }

        if (item.Context[VerdictKey] is not { } named)
        {
            return null;
        }

        var cited = named is JsonValue value && value.TryGetValue<string>(out var id) ? id : null;
        if (!Coverable.Contains(plan.Contract.Id, StringComparer.Ordinal))
        {
            return Miss(Why.NotCoverable, cited, null);
        }

        var verdict = cited is null
            ? null
            : await db.Journal.AsNoTracking().FirstOrDefaultAsync(line => line.PublicId == cited, ct).ConfigureAwait(false);
        if (verdict is null || verdict.Kind != JournalKinds.ReplyClassified || verdict.CampaignId != item.CampaignId)
        {
            return Miss(Why.VerdictUnknown, cited, null);
        }

        var said = verdict.New as JsonObject ?? [];
        var category = Text(said["category"]);
        if (await Later(db, verdict).AnyAsync(ct).ConfigureAwait(false))
        {
            return Miss(Why.VerdictSuperseded, cited, category);
        }

        if (!string.Equals(Text(said["reaction"]), "draft", StringComparison.Ordinal))
        {
            return Miss(Why.ReactionNotDraft, cited, category);
        }

        var author = await AuthorAsync(db, item, verdict, said, ct).ConfigureAwait(false);
        if (author is null)
        {
            return Miss(Why.VerdictNotAuthentic, cited, category);
        }

        if (!await DraftedByAsync(db, item, author, ct).ConfigureAwait(false))
        {
            return Miss(Why.NotTheVerdictsWork, cited, category);
        }

        if (await ContextChanges(db, item.PublicId).AnyAsync(ct).ConfigureAwait(false))
        {
            return Miss(Why.Edited, cited, category);
        }

        if (!string.Equals(Text(said["plugin"]), plan.Plugin.Manifest.Id, StringComparison.Ordinal))
        {
            return Miss(Why.PluginDiffers, cited, category);
        }

        if (!SameConversation(plan, item, said))
        {
            return Miss(Why.ThreadDiffers, cited, category);
        }

        var newest = category is null
            ? null
            : await Newest(db, item.CampaignId, category).FirstOrDefaultAsync(ct).ConfigureAwait(false);
        if (!Sets(newest))
        {
            return Miss(Why.NoStandingApproval, cited, category);
        }

        var standing = Read(newest);
        if (!standing.Operations.Contains(plan.Contract.Id, StringComparer.Ordinal))
        {
            return Miss(Why.OperationNotCovered, cited, category);
        }

        if (standing.ExpiresAt is { } expires && expires.UtcDateTime <= now)
        {
            return Miss(Why.Expired, cited, category);
        }

        if (standing.MaxReleases is { } ceiling
            && await Released(db, newest).SingleAsync(ct).ConfigureAwait(false) >= ceiling)
        {
            return Miss(Why.CeilingReached, cited, category);
        }

        var names = new JsonObject { ["verdict"] = verdict.PublicId, ["category"] = category };
        return new Answer(new Cover(newest, new ActorRef(newest.ActorType, newest.ActorId), names), null);
    }

    /// <summary>
    /// Whether the campaign's allowance covers this search, asked in this order, the first that fails naming the answer:
    /// there is one; it names the audience the search runs under, which the claim has just recorded on the search; it has
    /// not expired; the search asks for no more people than one search may; and the people the searches let go under it
    /// asked for, with this one's, stay within its ceiling. The gate counts people by the count a search asks for; what the
    /// provider charges is the provider's to count.
    /// </summary>
    private static async Task<Answer> AllowanceCoverAsync(JasonDbContext db, WorkItem item, ProviderOpPlan plan, DateTime now, CancellationToken ct)
    {
        var count = Count(plan.Input["args"]!["count"]);
        var searched = item.Context[CampaignAudiences.ContextKey] is JsonObject carried ? Text(carried["id"]) : null;
        var allowance = await AllowanceAsync(db, item.CampaignId, ct).ConfigureAwait(false);
        var record = allowance?.Record;
        var facts = new JsonObject
        {
            ["standing"] = record?.Line.PublicId,
            ["audience"] = record?.Audience is { } named ? new JsonObject { ["id"] = named.Id, ["number"] = named.Number } : null,
            ["expires_at"] = record?.ExpiresAt is { } moment ? InboundSync.Instant(moment) : null,
            ["max_people"] = record?.MaxPeople,
            ["max_per_search"] = record?.MaxPerSearch,
            ["people"] = allowance?.People,
            ["count"] = count,
        };

        var why = allowance switch
        {
            null => Why.NoStandingApproval,
            _ when !Sets(record!.Line) => Why.NoStandingApproval,
            _ when !string.Equals(record!.Audience!.Id, searched, StringComparison.Ordinal) => Why.AnotherAudience,
            _ when record!.ExpiresAt is { } expires && expires.UtcDateTime <= now => Why.Expired,
            _ when record!.MaxPerSearch is { } cap && count > cap => Why.OverPerSearch,
            _ when allowance.People + count > record!.MaxPeople => Why.CeilingReached,
            _ => null,
        };
        if (why is not null)
        {
            return new Answer(null, new Uncovered(why, facts));
        }

        var released = new JsonObject
        {
            ["verdict"] = null,
            ["category"] = null,
            ["audience"] = facts["audience"]!.DeepClone(),
            ["count"] = count,
        };
        return new Answer(new Cover(record!.Line, new ActorRef(record.Line.ActorType, record.Line.ActorId), released), null);
    }

    /// <summary>
    /// Who wrote the verdict, where that is somebody whose verdict can cover: a run of the campaign's work whose context
    /// names the reply's plugin and thread as the runtime wrote them, or a person who gave their name — either by recording
    /// it, or by answering the question whose answer the line carries. Null otherwise — a role by name outside a run, or
    /// nobody at all.
    /// </summary>
    /// <remarks>
    /// The line names who answered only when <c>reply.record</c> checked the decision as it wrote the line: answered, by a
    /// named person, with the category recorded. The claim trusts the line for that and reads no decision. A line that
    /// carries an attempt is the run's, whatever actor it names: a person named from inside a run vouches as that run.
    /// </remarks>
    private static async Task<Author?> AuthorAsync(JasonDbContext db, WorkItem item, JournalEntry verdict, JsonObject said, CancellationToken ct)
    {
        if (said["answered_by"] is JsonObject)
        {
            return new Author(null);
        }

        if (verdict.ActorType == ActorType.Human && verdict.AttemptId is null)
        {
            return string.IsNullOrWhiteSpace(verdict.ActorId) ? null : new Author(null);
        }

        if (verdict.ActorType is not (ActorType.Attempt or ActorType.Human) || verdict.WorkItemId is null)
        {
            return null;
        }

        var work = await db.WorkItems.AsNoTracking()
            .FirstOrDefaultAsync(row => row.PublicId == verdict.WorkItemId, ct)
            .ConfigureAwait(false);
        if (work is null || work.CampaignId != item.CampaignId || work.Context[Responder.InboundReplyKey] is not JsonObject inbound)
        {
            return null;
        }

        return string.Equals(Text(inbound["plugin"]), Text(said["plugin"]), StringComparison.Ordinal)
            && string.Equals(Text(inbound["thread"]), Text(said["thread"]), StringComparison.Ordinal)
            ? new Author(work.Id)
            : null;
    }

    /// <summary>
    /// Whether the draft is the verdict author's own: a run's verdict releases only work a run of the same work item
    /// created; a person's verdict vouches for the category, and releases whoever drafted it.
    /// </summary>
    private static async Task<bool> DraftedByAsync(JasonDbContext db, WorkItem item, Author author, CancellationToken ct)
    {
        if (author.WorkItemId is not { } work)
        {
            return true;
        }

        if (item.CreatedByType != ActorType.Attempt || item.CreatedById is not { } creator)
        {
            return false;
        }

        return await db.Attempts.AsNoTracking()
            .AnyAsync(attempt => attempt.PublicId == creator && attempt.WorkItemId == work, ct)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// The verdict is about the conversation the work continues: the thread a reply answers in, or — for a message, which
    /// has no thread — the person the verdict named.
    /// </summary>
    private static bool SameConversation(ProviderOpPlan plan, WorkItem item, JsonObject said)
    {
        if (string.Equals(plan.Contract.Id, "message.send", StringComparison.Ordinal))
        {
            return Text(said["contact_id"]) is { } contact && string.Equals(contact, item.Contact?.PublicId, StringComparison.Ordinal);
        }

        return string.Equals(Text(said["thread"]), Text(plan.Input["args"]?["thread"]?["external_id"]), StringComparison.Ordinal);
    }

    /// <summary>
    /// The checks <c>context.reply_verdict</c> is held to when work is created or its context patched: the work is a reply
    /// or a message, and the value names a verdict of the same campaign about the same thread, or the same person.
    /// Whether that verdict can release the work is the claim's question; this one is whether the reference is true.
    /// </summary>
    internal static async Task ValidateReferenceAsync(
        JasonDbContext db,
        WorkItemKind kind,
        string? operation,
        int campaignId,
        string? contactId,
        JsonObject context,
        ValidationErrors errors,
        CancellationToken ct)
    {
        const string Field = "context." + VerdictKey;
        if (context[VerdictKey] is not { } named)
        {
            return;
        }

        if (kind != WorkItemKind.ProviderOp || operation is null || !Coverable.Contains(operation, StringComparer.Ordinal))
        {
            errors.Add(Field, "invalid", "reply_verdict belongs to a conversation.reply or a message.send: the verdict the drafted reply answers.");
            return;
        }

        if (named is not JsonValue value || !value.TryGetValue<string>(out var cited) || string.IsNullOrWhiteSpace(cited))
        {
            errors.Add(Field, "invalid", "reply_verdict is the id of a reply_classified journal line.");
            return;
        }

        var verdict = await db.Journal.AsNoTracking().FirstOrDefaultAsync(line => line.PublicId == cited, ct).ConfigureAwait(false);
        if (verdict is null)
        {
            errors.Add(Field, "unknown", "reply_verdict must name a journal line that exists.");
            return;
        }

        if (verdict.Kind != JournalKinds.ReplyClassified || verdict.CampaignId != campaignId)
        {
            errors.Add(Field, "invalid", "reply_verdict must name a reply_classified line of the same campaign.");
            return;
        }

        var said = verdict.New as JsonObject ?? [];
        if (string.Equals(operation, "message.send", StringComparison.Ordinal))
        {
            if (Text(said["contact_id"]) is not { } contact || !string.Equals(contact, contactId, StringComparison.Ordinal))
            {
                errors.Add(Field, "invalid", "reply_verdict must name a verdict about the same contact the message goes to.");
            }

            return;
        }

        // Read without assuming a shape: at an update the arguments' own errors are collected and not yet thrown, so what
        // stands here may be anything a patch sent, and a reference is never measured against a thread that is not there.
        var thread = ((context[WorkItemService.InputKey] as JsonObject)?["thread"] as JsonObject)?["external_id"];
        if (!string.Equals(Text(said["thread"]), Text(thread), StringComparison.Ordinal))
        {
            errors.Add(Field, "invalid", "reply_verdict must name a verdict about the thread the reply answers in.");
        }
    }

    /// <summary>
    /// The answer for work <paramref name="cover"/> would release but something holds for the person: not covered, saying
    /// <paramref name="why"/> — <see cref="Why.Doubted"/>, <see cref="Why.SentFromChat"/> or <see cref="Why.DailyCeiling"/> —
    /// beside the facts the cover
    /// names, the verdict and its category for a reply.
    /// </summary>
    internal static Answer Held(Cover cover, string why)
    {
        ArgumentNullException.ThrowIfNull(cover);
        return new(null, new Uncovered(why, cover.Names.DeepClone().AsObject()));
    }

    /// <summary>
    /// Whether <paramref name="line"/> sets a standing approval the claim may release under: a set, written outside any run.
    /// A set line that carries an attempt came from inside a run, which the door refuses; whatever actor it names, it sets
    /// nothing.
    /// </summary>
    private static bool Sets([NotNullWhen(true)] JournalEntry? line) =>
        line is { Kind: JournalKinds.StandingApprovalSet, AttemptId: null };

    private static Answer Miss(string why, string? verdict, string? category) =>
        new(null, new Uncovered(why, new JsonObject { ["verdict"] = verdict, ["category"] = category }));

    private static string? Text(JsonNode? node) => node is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;

    private static int? Whole(JsonNode? node) => node is JsonValue value && value.TryGetValue<int>(out var number) ? number : null;

    /// <summary>
    /// The number of people a search asks for. The claim held the input to the operation's schema before it asked, and the
    /// schema takes any whole number from 1 to 1000, <c>50.0</c> among them, which is not an <see cref="int"/> as written.
    /// </summary>
    private static int Count(JsonNode? node) => node switch
    {
        JsonValue value when value.TryGetValue<int>(out var whole) => whole,
        JsonValue value when value.TryGetValue<decimal>(out var exact) => (int)exact,
        _ => (int)node!.GetValue<double>(),
    };

    /// <summary>
    /// A standing approval as its set line holds it: a category of reply's, or an allowance, which names an audience and a
    /// ceiling in people and no category.
    /// </summary>
    public sealed record Record(
        JournalEntry Line,
        string? Category,
        IReadOnlyList<string> Operations,
        DateTimeOffset? ExpiresAt,
        int? MaxReleases,
        AudienceRef? Audience,
        int? MaxPeople,
        int? MaxPerSearch)
    {
        /// <summary>
        /// Whether it still stands, having let go <paramref name="released"/> pieces of work that asked for
        /// <paramref name="people"/>: not past its expiry, under its ceilings, and in a campaign that is not archived — an
        /// archived campaign's work is over, so nothing it set stands, though the record of it is listed.
        /// </summary>
        public bool InForce(int released, int? people, DateTime now, CampaignStatus campaign) =>
            !(ExpiresAt is { } expires && expires.UtcDateTime <= now)
            && !(MaxReleases is { } ceiling && released >= ceiling)
            && !(MaxPeople is { } most && people >= most)
            && campaign != CampaignStatus.Archived;
    }

    /// <summary>The campaign's allowance: its record, the searches released under it, and the people those asked for.</summary>
    public sealed record Allowance(Record Record, int Released, int People)
    {
        /// <summary>Whether it still stands at <paramref name="now"/> in a campaign of status <paramref name="campaign"/>.</summary>
        public bool InForce(DateTime now, CampaignStatus campaign) => Record.InForce(Released, People, now, campaign);
    }

    /// <summary>The claim's answer: a cover, or why there is none.</summary>
    internal sealed record Answer(Cover? Cover, Uncovered? Uncovered);

    /// <summary>
    /// What released the work: the line that set the standing approval, who set it, and what the line of the release names
    /// besides — the verdict and its category for a reply, the audience and the people asked for for a search.
    /// </summary>
    internal sealed record Cover(JournalEntry Standing, ActorRef SetBy, JsonObject Names);

    /// <summary>
    /// What a parked approval's preview says about a standing approval that did not release it: the facts it was decided
    /// on — the verdict and its category for a reply; the allowance, its use and the search's count for a search — and why.
    /// </summary>
    internal sealed record Uncovered(string Why, JsonObject Facts)
    {
        public JsonObject ToJson()
        {
            var said = Facts.DeepClone().AsObject();
            said["why"] = Why;
            return said;
        }
    }

    /// <summary>A verdict's author: the work item a run of which wrote it, or none where a person did.</summary>
    private sealed record Author(int? WorkItemId);
}
