using System.Globalization;
using System.Text.Json.Nodes;
using Jason.Contracts.Api;
using Jason.Contracts.Operations;
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
/// time — and the reads that say whether it covers a piece of work the claim is about to park.
/// </summary>
/// <remarks>
/// <para>
/// The record is the chronicle: a <c>standing_approval_set</c> or <c>standing_approval_unset</c> line per change, keyed by
/// the category, and the newest one for a campaign and a category is the state. Each release under one is an
/// <c>approval_standing_applied</c> line keyed by the line that set it, which is what its ceiling counts.
/// </para>
/// <para>
/// Every read here is written as SQL that names its index, and a test pins each plan. The kind index is on <c>kind</c>
/// alone. Its bound for these reads is the installation's lines of that kind: the standing-approval lines a person writes
/// by hand, the releases under them, the verdicts. The campaign index would bound them instead by the campaign's every
/// line since the moment asked — and each reply a campaign answers brings about ten of its own (the responder's work
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

    /// <summary>How many releases name the line that set a standing approval: the releases since it was set.</summary>
    internal static IQueryable<int> Released(JasonDbContext db, JournalEntry set) =>
        db.Database.SqlQuery<int>($"""
            SELECT count(*) AS "Value" FROM journal INDEXED BY ix_journal_kind
            WHERE kind = 'approval_standing_applied' AND id > {set.Id} AND key = {set.PublicId}
            """);

    /// <summary>A later verdict on the same reply in the same campaign, which is the one that stands.</summary>
    internal static IQueryable<int> Later(JasonDbContext db, JournalEntry verdict) =>
        db.Database.SqlQuery<int>($"""
            SELECT id AS "Value" FROM journal INDEXED BY ix_journal_kind
            WHERE kind = 'reply_classified' AND id > {verdict.Id} AND campaign_id = {verdict.CampaignId} AND key = {verdict.Key}
            LIMIT 1
            """);

    /// <summary>A change to a work item's context after it was created, read from the item's own lines.</summary>
    internal static IQueryable<int> ContextChanges(JasonDbContext db, string workItemId) =>
        db.Database.SqlQuery<int>($"""
            SELECT id AS "Value" FROM journal INDEXED BY ix_journal_work_item_id_public_id
            WHERE work_item_id = {workItemId} AND kind = 'workitem_context_updated'
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
        int? ceiling = line["max_releases"] is JsonValue number && number.TryGetValue<int>(out var count) ? count : null;
        return new Record(set, set.Key ?? string.Empty, operations, expires, ceiling);
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
    /// left for a person.
    /// </remarks>
    internal static async Task<Answer?> CoverAsync(JasonDbContext db, WorkItem item, ProviderOpPlan plan, DateTime now, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(plan);

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
        if (newest is not { Kind: JournalKinds.StandingApprovalSet })
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

        return new Answer(new Cover(newest, verdict, category!, new ActorRef(newest.ActorType, newest.ActorId)), null);
    }

    /// <summary>
    /// Who wrote the verdict, where that is somebody whose verdict can cover: a run of the campaign's work whose context
    /// names the reply's plugin and thread as the runtime wrote them, or a person who gave their name. Null otherwise — a
    /// role by name outside a run, or nobody at all.
    /// </summary>
    private static async Task<Author?> AuthorAsync(JasonDbContext db, WorkItem item, JournalEntry verdict, JsonObject said, CancellationToken ct)
    {
        if (verdict.ActorType == ActorType.Human)
        {
            return string.IsNullOrWhiteSpace(verdict.ActorId) ? null : new Author(null);
        }

        if (verdict.ActorType != ActorType.Attempt || verdict.WorkItemId is null)
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

    private static Answer Miss(string why, string? verdict, string? category) => new(null, new Uncovered(why, verdict, category));

    private static string? Text(JsonNode? node) => node is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;

    /// <summary>A standing approval as its set line holds it.</summary>
    internal sealed record Record(JournalEntry Line, string Category, IReadOnlyList<string> Operations, DateTimeOffset? ExpiresAt, int? MaxReleases);

    /// <summary>The claim's answer: a cover, or why there is none.</summary>
    internal sealed record Answer(Cover? Cover, Uncovered? Uncovered);

    /// <summary>What released the work: the line that set the standing approval, the verdict, its category and who set it.</summary>
    internal sealed record Cover(JournalEntry Standing, JournalEntry Verdict, string Category, ActorRef SetBy);

    /// <summary>What a parked approval's preview says about a standing approval that did not release it.</summary>
    internal sealed record Uncovered(string Why, string? Verdict, string? Category)
    {
        public JsonObject ToJson() => new()
        {
            ["verdict"] = Verdict,
            ["category"] = Category,
            ["why"] = Why,
        };
    }

    /// <summary>A verdict's author: the work item a run of which wrote it, or none where a person did.</summary>
    private sealed record Author(int? WorkItemId);
}
