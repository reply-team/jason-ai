using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Jason.Contracts.Api;
using Jason.Contracts.Ids;
using Jason.Contracts.Json;
using Jason.Contracts.Plugins;
using Jason.Runtime.Domain;
using Jason.Runtime.Journal;
using Jason.Runtime.Persistence;
using Jason.Runtime.WorkItems;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Jason.Runtime.Execution;

/// <summary>What one attempt of an operation a provider block holds says about the provider's blocks.</summary>
public enum BlockAnswer
{
    /// <summary>The call reached the provider and was judged on the work: it succeeded, or failed permanent or validation.</summary>
    GotPast,

    /// <summary>The provider refused the call for a reason outside the work, with a block its contract accepts.</summary>
    Blocked,

    /// <summary>Nothing is known: a transient or ambiguous failure, an answer outside the contract, or no answer at all.</summary>
    Undecided,
}

/// <summary>
/// What the runtime knows about the blocks a provider said it holds, and the rules both entries to a park read: the
/// claim, which parks work a block already holds, and <see cref="AttemptOutcomes"/>, which parks an attempt that met one.
/// A block is kept per plugin and binding identity — the account as a whole, or one sending account under it — and is
/// opened by the first answer that names it, seen again by every later one, and cleared by the first call that gets past
/// it. Nothing here saves: what it changes joins the caller's change set.
/// </summary>
public static class ProviderBlocks
{
    /// <summary>
    /// The latest a trial is put off by a provider's own word that its block clears by itself: a day. A moment further
    /// away is taken as a day from now, so a block that said it clears next month is still tried daily.
    /// </summary>
    public const int ClearsAtCapSeconds = 86_400;

    /// <summary>The key a block is kept under for a route: its binding's identity, or the word for a route that binds nothing.</summary>
    public static string Identity(string? bindingIdentity) => bindingIdentity ?? EventSource.NoBinding;

    /// <summary>
    /// The sending account a composed input names, at <see cref="ProviderBlockContract.SenderPointer"/>, or null where it
    /// names none — the one place a block of scope <c>sender</c> is matched against work.
    /// </summary>
    public static string? Sender(JsonObject? input)
    {
        JsonNode? node = input;
        foreach (var segment in ProviderBlockContract.SenderPointer.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            node = node is JsonObject parent && parent.TryGetPropertyValue(segment, out var child) ? child : null;
        }

        return node is JsonValue value && value.TryGetValue(out string? sender) && !string.IsNullOrEmpty(sender) ? sender : null;
    }

    /// <summary>
    /// The open block that holds work with this input, among the open blocks of its plugin and binding: the account's,
    /// which covers every sender under it, or else the block of the sender the input names. Null where neither is open.
    /// </summary>
    public static ProviderBlock? Covering(IEnumerable<ProviderBlock> open, JsonObject? input)
    {
        ArgumentNullException.ThrowIfNull(open);
        var blocks = open.Where(block => block.Status == ProviderBlockStatus.Open).ToList();
        if (blocks.FirstOrDefault(block => block.Scope == ProviderBlockScope.Account) is { } account)
        {
            return account;
        }

        return Sender(input) is { } sender
            ? blocks.FirstOrDefault(block => block.Scope == ProviderBlockScope.Sender && string.Equals(block.SenderExternalId, sender, StringComparison.Ordinal))
            : null;
    }

    /// <summary>
    /// Whether <paramref name="block"/> holds work parked on <paramref name="parkedOn"/>: it is that block, or it is the
    /// account's block and <paramref name="parkedOn"/> the open block of a sender under that account, which it covers.
    /// </summary>
    public static bool Holds(ProviderBlock block, ProviderBlock? parkedOn) =>
        block is not null && parkedOn is not null
        && (parkedOn.Id == block.Id
            || (block.Scope == ProviderBlockScope.Account
                && parkedOn is { Scope: ProviderBlockScope.Sender, Status: ProviderBlockStatus.Open }
                && string.Equals(parkedOn.PluginId, block.PluginId, StringComparison.Ordinal)
                && string.Equals(parkedOn.BindingIdentity, block.BindingIdentity, StringComparison.Ordinal)));

    /// <summary>
    /// Whether a save was refused because a block it opens is already open — opened by another attempt that read the
    /// same open blocks and saved first. Only the two indexes of one open block per account and per sender answer yes,
    /// and both lead with the plugin; any other refusal of this table, or of another, is not this one.
    /// </summary>
    public static bool OpenedFirstElsewhere(DbUpdateException failure) =>
        failure?.InnerException is SqliteException { SqliteErrorCode: 19 } refused
        && refused.Message.Contains(OneOpenRefused, StringComparison.Ordinal);

    /// <summary>
    /// Whether a save was refused because a block it changes is no longer as it read it — cleared by another attempt that
    /// read it open, got past it and saved first. A block's status guards every change to it; only that guard answers yes,
    /// and a guard of any other row is not this one.
    /// </summary>
    public static bool ClearedFirstElsewhere(DbUpdateException failure) =>
        failure is DbUpdateConcurrencyException { Entries.Count: > 0 } refused
        && refused.Entries.All(entry => entry.Entity is ProviderBlock);

    /// <summary>The open blocks of one plugin and binding identity, tracked, read by the index of the blocks by status.</summary>
    public static Task<List<ProviderBlock>> OpenAsync(JasonDbContext db, string pluginId, string identity, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(db);
        return db.ProviderBlocks
            .Where(block => block.Status == ProviderBlockStatus.Open && block.PluginId == pluginId && block.BindingIdentity == identity)
            .OrderBy(block => block.Id)
            .ToListAsync(ct);
    }

    /// <summary>
    /// The block an answer names, among the open ones of its binding: seen again where it is open — counted, with the
    /// latest reason, link and moment it said, and its next trial put off from now — or opened by
    /// <paramref name="attempt"/> where it is not. <paramref name="said"/> is a block its contract accepts.
    /// </summary>
    public static ProviderBlock Meet(
        JasonDbContext db,
        IReadOnlyList<ProviderBlock> open,
        string pluginId,
        string identity,
        JsonObject said,
        Attempt attempt,
        DateTime now,
        int trialSeconds)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(open);
        ArgumentNullException.ThrowIfNull(said);
        ArgumentNullException.ThrowIfNull(attempt);

        var scope = SnakeCaseEnumConverter<ProviderBlockScope>.Parse(Text(said, "scope")!);
        var sender = scope == ProviderBlockScope.Sender ? Text(said, "sender_external_id") : null;
        var reason = Text(said, "reason")!;
        var link = Text(said, "link");
        DateTime? clearsAt = Text(said, "clears_at") is { } moment
            ? DateTimeOffset.Parse(moment, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind).UtcDateTime
            : null;

        var block = open.FirstOrDefault(candidate => candidate.Status == ProviderBlockStatus.Open
            && candidate.Scope == scope
            && string.Equals(candidate.SenderExternalId, sender, StringComparison.Ordinal));
        if (block is null)
        {
            block = new ProviderBlock
            {
                PublicId = PublicId.New("blk"),
                PluginId = pluginId,
                BindingIdentity = identity,
                Scope = scope,
                SenderExternalId = sender,
                Reason = reason,
                OpenedAt = now,
                OpenedByAttempt = attempt,
                LastSeenAt = now,
                SeenCount = 1,
            };
            db.ProviderBlocks.Add(block);
        }
        else
        {
            block.SeenCount++;
            block.LastSeenAt = now;
            block.Reason = reason;
        }

        block.Link = link;
        block.ClearsAt = clearsAt;
        block.NextTrialAt = NextTrial(clearsAt, now, trialSeconds);
        return block;
    }

    /// <summary>
    /// When a block's next trial is due: the moment the provider said it clears by itself, where that is still ahead and
    /// no further than <see cref="ClearsAtCapSeconds"/> away, and otherwise the trial cadence from now.
    /// </summary>
    public static DateTime NextTrial(DateTime? clearsAt, DateTime now, int trialSeconds)
    {
        var cap = now.AddSeconds(ClearsAtCapSeconds);
        return clearsAt is { } moment && moment > now
            ? moment < cap ? moment : cap
            : now.AddSeconds(trialSeconds);
    }

    /// <summary>A trial that says nothing about the block: it stays open, and the next trial is due a cadence from now.</summary>
    public static void Undecided(ProviderBlock block, DateTime now, int trialSeconds)
    {
        ArgumentNullException.ThrowIfNull(block);
        block.TrialWorkItemId = null;
        block.NextTrialAt = now.AddSeconds(trialSeconds);
    }

    /// <summary>The block is cleared by the attempt that got past it; the work it holds is released by the next scan.</summary>
    public static void Clear(ProviderBlock block, Attempt attempt, DateTime now)
    {
        ArgumentNullException.ThrowIfNull(block);
        ArgumentNullException.ThrowIfNull(attempt);
        block.Status = ProviderBlockStatus.Cleared;
        block.ClearedAt = now;
        block.ClearedByAttempt = attempt;
        block.TrialWorkItemId = null;
    }

    /// <summary>
    /// The block is cleared on a person's word that the cause is gone; the work it holds is released by the next scan, as
    /// it is for a block an attempt got past. Not a mute: where the provider still enforces it, the next call that meets it
    /// opens a new block.
    /// </summary>
    public static void ClearedBy(ProviderBlock block, ActorRef actor, DateTime now)
    {
        ArgumentNullException.ThrowIfNull(block);
        ArgumentNullException.ThrowIfNull(actor);
        block.Status = ProviderBlockStatus.Cleared;
        block.ClearedAt = now;
        block.ClearedByType = actor.Type;
        block.ClearedById = actor.Id;
        block.TrialWorkItemId = null;
    }

    /// <summary>
    /// Says, once per campaign, that a block holds its work: a reserved <c>provider_blocked</c> line under the block's
    /// public id, a default manager trigger. The block's link is never in it; the sender's identifier is, where the block
    /// is a sender's. A campaign that has already been told about this block is not told again, whichever of its items
    /// parks next.
    /// </summary>
    public static async Task SayBlockedAsync(
        JasonDbContext db,
        JournalWriter journal,
        WorkItem item,
        ProviderBlock block,
        Attempt? attempt,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(journal);
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(block);

        var said = db.Journal.Local.Any(entry => Blocked(entry, block, item.CampaignId))
            || await db.Journal.AnyAsync(
                entry => entry.Kind == JournalKinds.ProviderBlocked && entry.Key == block.PublicId && entry.CampaignId == item.CampaignId,
                ct).ConfigureAwait(false);
        if (said)
        {
            return;
        }

        var campaign = item.Campaign ?? await db.Campaigns.FirstAsync(c => c.Id == item.CampaignId, ct).ConfigureAwait(false);
        journal.Append(
            db,
            Actors.Dispatcher,
            JournalKinds.ProviderBlocked,
            campaign,
            key: block.PublicId,
            updated: new JsonObject
            {
                ["provider_block_id"] = block.PublicId,
                ["plugin_id"] = block.PluginId,
                ["scope"] = SnakeCaseEnumConverter<ProviderBlockScope>.Format(block.Scope),
                ["sender_external_id"] = block.SenderExternalId,
                ["reason"] = block.Reason,
                ["clears_at"] = block.ClearsAt is { } clearsAt ? JsonSerializer.SerializeToNode(WorkItemMapper.Utc(clearsAt), JasonJson.Options) : null,
                ["work_item_id"] = item.PublicId,
            },
            attempt: attempt);
    }

    /// <summary>
    /// Says, in every campaign that was told the block held its work, that it has cleared: a reserved
    /// <c>provider_block_cleared</c> line under the block's public id. Not a trigger: what the block held runs again by
    /// itself, and the manager was told when it began.
    /// </summary>
    public static Task SayClearedAsync(JasonDbContext db, JournalWriter journal, ProviderBlock block, Attempt attempt, CancellationToken ct) =>
        SayClearedAsync(db, journal, block, Actors.Dispatcher, attempt, reason: null, ct);

    /// <summary>
    /// The same line for a block a person cleared: under their name, with the reason they gave, and no attempt.
    /// </summary>
    public static Task SayClearedAsync(JasonDbContext db, JournalWriter journal, ProviderBlock block, ActorRef actor, string? reason, CancellationToken ct) =>
        SayClearedAsync(db, journal, block, actor, attempt: null, reason, ct);

    private static async Task SayClearedAsync(JasonDbContext db, JournalWriter journal, ProviderBlock block, ActorRef actor, Attempt? attempt, string? reason, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(journal);
        ArgumentNullException.ThrowIfNull(block);

        var told = await db.Journal.AsNoTracking()
            .Where(entry => entry.Kind == JournalKinds.ProviderBlocked && entry.Key == block.PublicId && entry.CampaignId != null)
            .Select(entry => entry.CampaignId!.Value)
            .ToListAsync(ct)
            .ConfigureAwait(false);
        told.AddRange(db.Journal.Local.Where(entry => entry.Kind == JournalKinds.ProviderBlocked && entry.Key == block.PublicId)
            .Select(entry => entry.CampaignId ?? entry.Campaign?.Id ?? 0)
            .Where(id => id != 0));

        foreach (var campaignId in told.Distinct().Order())
        {
            var campaign = await db.Campaigns.FirstAsync(c => c.Id == campaignId, ct).ConfigureAwait(false);
            journal.Append(
                db,
                actor,
                JournalKinds.ProviderBlockCleared,
                campaign,
                key: block.PublicId,
                updated: new JsonObject { ["provider_block_id"] = block.PublicId },
                reason: reason,
                attempt: attempt);
        }
    }

    /// <summary>What the twenty-sixth check says of work an open block holds: the operation, the plugin and the block's reason.</summary>
    public static string HeldMessage(string operation, ProviderBlock block)
    {
        ArgumentNullException.ThrowIfNull(block);
        return $"Operation '{operation}' is held by an open provider block of '{block.PluginId}' ({block.Reason}); the item waits until the block clears or a trial gets past it.";
    }

    /// <summary>
    /// The last error of an item the claim parked, which no attempt met the block for: the block as it is kept, its link
    /// included, so a person reads it where they read the block of an item an attempt parked. Not retried: the block
    /// clearing, or a trial getting past it, releases the item.
    /// </summary>
    public static AttemptErrorDto HeldError(string operation, ProviderBlock block) =>
        new(AttemptErrors.ProviderBlocked, HeldMessage(operation, block), Retriable: false, Class: FailureClass.Blocked, Block: Said(block));

    /// <summary>A kept block in the shape a provider says one: scope, the sender for a sender's, reason, and the link and moment where known.</summary>
    public static JsonObject Said(ProviderBlock block)
    {
        ArgumentNullException.ThrowIfNull(block);
        var said = new JsonObject { ["scope"] = SnakeCaseEnumConverter<ProviderBlockScope>.Format(block.Scope) };
        if (block.SenderExternalId is { } sender)
        {
            said["sender_external_id"] = sender;
        }

        said["reason"] = block.Reason;
        if (block.Link is { } link)
        {
            said["link"] = link;
        }

        if (block.ClearsAt is { } clearsAt)
        {
            said["clears_at"] = JsonSerializer.SerializeToNode(WorkItemMapper.Utc(clearsAt), JasonJson.Options);
        }

        return said;
    }

    /// <summary>What the <c>wait</c> block of an item parked on <paramref name="block"/> says it is blocked by.</summary>
    public static JsonObject BlockedBy(ProviderBlock block)
    {
        ArgumentNullException.ThrowIfNull(block);
        return new JsonObject
        {
            ["provider_block_id"] = block.PublicId,
            ["scope"] = SnakeCaseEnumConverter<ProviderBlockScope>.Format(block.Scope),
            ["reason"] = block.Reason,
        };
    }

    /// <summary>The start of what SQLite says when either one-open index refuses a block: both lead with the plugin.</summary>
    private const string OneOpenRefused = "UNIQUE constraint failed: provider_blocks.plugin_id";

    private static bool Blocked(JournalEntry entry, ProviderBlock block, int campaignId) =>
        entry.Kind == JournalKinds.ProviderBlocked
        && string.Equals(entry.Key, block.PublicId, StringComparison.Ordinal)
        && (entry.CampaignId ?? entry.Campaign?.Id) == campaignId;

    private static string? Text(JsonObject said, string name) =>
        said.TryGetPropertyValue(name, out var node) && node is JsonValue value && value.TryGetValue(out string? text) ? text : null;
}
