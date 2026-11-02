using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json.Nodes;
using Jason.Contracts.Api;
using Jason.Runtime.Configuration;
using Jason.Runtime.Domain;
using Jason.Runtime.Execution;
using Jason.Runtime.Journal;
using Jason.Runtime.Logging;
using Jason.Runtime.Persistence;
using Jason.Runtime.Routing;
using Jason.Runtime.WorkItems;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Jason.Runtime.Dispatch;

/// <summary>
/// The step of a scan between the inbound read and the summon: a reply the runtime says wakes the campaign's
/// responder at once. For each <c>inbound_reply_observed</c> line above the campaign's own watermark — whichever writer
/// said it, the inbound read or the events store, since both say a move under one key — it queues a read of the thread
/// (<c>conversation.get</c>) and the responder waiting on that read, or, when the thread's responder is still open,
/// records the further move on it. After the inbound read, so a line it says wakes the responder in the same scan;
/// before the summon, so the responder is queued ahead of the check-in the same line wakes.
/// <para>
/// One line, one short transaction: the look for the thread's open responder, the write and the watermark's guarded
/// move commit together, so nothing another writer does falls between them. One campaign's trouble is its own, as in
/// the summon.
/// </para>
/// </summary>
/// <remarks>
/// Nothing here reads meaning. It reads a kind, identifiers, a moment and which rule found the campaign; what the reply
/// says, and what to do about it, is the responder's. A thread the campaign holds only through a person it shares with
/// other campaigns is left to the manager, which reads across them before anyone answers.
/// </remarks>
public sealed class Responder(
    WorkItemService items,
    AttemptOutcomes outcomes,
    EntryCommandResolver entryCommands,
    TimeProvider clock,
    LiveSettings<ResponderOptions> settings,
    LiveSettings<RolesOptions> roles,
    RouteRegistry routes,
    ResponderSkips skips,
    ILogger<Responder> logger)
{
    /// <summary>The role a reply wakes.</summary>
    public const string Role = "responder";

    /// <summary>The operation that reads the thread for it.</summary>
    public const string ReadOperation = "conversation.get";

    /// <summary>
    /// The reserved context key the runtime writes the inbound reply that woke the responder into, and reads an open
    /// responder's thread from; named as the line kind that says it is.
    /// </summary>
    public const string InboundReplyKey = "inbound_reply";

    /// <summary>How many lines one scan reads of one campaign at most; the rest are read by the scans after it.</summary>
    public const int LinesPerScan = 100;

    /// <summary>How many of a thread's moves a responder's reply keeps: the newest.</summary>
    public const int MovesKept = 20;

    /// <summary>How many messages of the thread the read asks for: the newest page.</summary>
    public const int ReadLimit = 20;

    /// <summary>
    /// What a responder's answer has to look like: what it did, in one of four words, and a summary a person can read
    /// at the manager's check-in; the work it queued and the questions it raised, by id.
    /// </summary>
    public static JsonNode ResultFormat { get; } = JsonNode.Parse(
        """
        {
          "type": "object",
          "additionalProperties": false,
          "required": ["outcome", "summary"],
          "properties": {
            "outcome": { "type": "string", "enum": ["acted", "drafted", "escalated", "nothing"] },
            "summary": { "type": "string", "maxLength": 500 },
            "verdict": { "type": "string" },
            "created_work_items": { "type": "array", "items": { "type": "string" } },
            "decisions_raised": { "type": "array", "items": { "type": "string" } }
          }
        }
        """)!;

    private static readonly Action<ILogger, string, string, Exception?> Woke = LoggerMessage.Define<string, string>(
        LogLevel.Information,
        new EventId(1, nameof(Woke)),
        "Dispatcher woke a responder for campaign {CampaignId}: {WorkItemId}");

    private static readonly Action<ILogger, string, string, Exception?> Skipped = LoggerMessage.Define<string, string>(
        LogLevel.Information,
        new EventId(2, nameof(Skipped)),
        "The respond step woke no responder for campaign {CampaignId}: {Code}");

    private static readonly Action<ILogger, string, string, Exception?> PassedOver = LoggerMessage.Define<string, string>(
        LogLevel.Warning,
        new EventId(3, nameof(PassedOver)),
        "The respond step passed over a line of campaign {CampaignId} it could not act on: {Code}");

    private static readonly Action<ILogger, string, Exception?> Failed = LoggerMessage.Define<string>(
        LogLevel.Warning,
        new EventId(4, nameof(Failed)),
        "The respond step for campaign {CampaignId} failed; the rest of the scan continues without it");

    // The same event, for a failure while reading JSON: what kind and where, and no exception, because what the parser
    // quoted is somebody's text.
    private static readonly Action<ILogger, string, string, Exception?> FailedToParse = LoggerMessage.Define<string, string>(
        LogLevel.Warning,
        new EventId(4, nameof(Failed)),
        "The respond step for campaign {CampaignId} failed; the rest of the scan continues without it: {Failure}");

    private static readonly Action<ILogger, string, Exception?> Moved = LoggerMessage.Define<string>(
        LogLevel.Information,
        new EventId(5, nameof(Moved)),
        "Campaign {CampaignId} was accounted for by another writer while the respond step was reading it");

    /// <summary>Reads every active campaign's new replies, and answers how many responders it woke.</summary>
    public async Task<int> RespondAsync(JasonDbContext db, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(db);
        var options = settings.Current;
        var woke = 0;

        foreach (var campaign in await ActiveAsync(db, ct).ConfigureAwait(false))
        {
            // Per campaign, because the steps after this one summon and hand out the work there is.
            try
            {
                woke += await RespondOneAsync(db, campaign, options, ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                if (ParseFailure.Is(ex))
                {
                    FailedToParse(logger, campaign.PublicId, ParseFailure.Describe(ex), null);
                }
                else
                {
                    Failed(logger, campaign.PublicId, ex);
                }

                db.ChangeTracker.Clear();
            }
        }

        return woke;
    }

    private async Task<int> RespondOneAsync(JasonDbContext db, Active campaign, ResponderOptions options, CancellationToken ct)
    {
        var before = campaign.Watermark;
        var lines = await db.Journal
            .AsNoTracking()
            .Where(entry => entry.CampaignId == campaign.Id && entry.Id > before && entry.Kind == JournalKinds.InboundReplyObserved)
            .OrderBy(entry => entry.Id)
            .Take(LinesPerScan)
            .Select(entry => new Line(entry.Id, entry.PublicId, entry.New))
            .ToListAsync(ct)
            .ConfigureAwait(false);

        if (lines.Count == 0)
        {
            await LevelAsync(db, campaign, before, ct).ConfigureAwait(false);
            return 0;
        }

        bool? configured = null;
        var oldest = clock.GetUtcNow().UtcDateTime.AddSeconds(-options.MaxAgeSeconds);
        var woke = 0;
        foreach (var line in lines)
        {
            // Off, the step still reads and moves past every line, so turning it on later wakes nothing for a reply said
            // while it was off. A line that lacks a field the rule needs is passed over as off passes it.
            var reply = options.OnReply ? Reply.Read(line) : null;
            string? skip = null;
            if (reply is not null)
            {
                if (reply.AttributedBy is { } rule && !string.Equals(rule, EventAttributionRules.CampaignPin, StringComparison.Ordinal))
                {
                    skip = ResponderSkipCodes.ThreadNotCampaignOwn;
                }
                else if (reply.LastActivityAt < oldest)
                {
                    skip = ResponderSkipCodes.ReplyTooOld;
                }
                else if (!(configured ??= await ConfiguredAsync(db, campaign, ct).ConfigureAwait(false)))
                {
                    skip = ResponderSkipCodes.NotConfigured;
                }
            }

            var acted = await OneLineAsync(db, campaign, before, line, skip is null ? reply : null, options, ct).ConfigureAwait(false);
            if (acted.Outcome == LineOutcome.Moved)
            {
                Moved(logger, campaign.PublicId, null);
                return woke;
            }

            before = line.Id;
            if (skip is not null && skips.FirstTime(campaign.Id, skip))
            {
                Skipped(logger, campaign.PublicId, skip, null);
            }

            if (acted is { Outcome: LineOutcome.Woke, WorkItemId: { } responder })
            {
                Woke(logger, campaign.PublicId, responder, null);
                woke++;
            }
        }

        return woke;
    }

    /// <summary>
    /// One line in one immediate transaction: the watermark moves past it from the value read, and, when it is to be
    /// acted on, the thread's open responder is looked for and patched or a pair is created. A line whose write the
    /// database or the domain refuses is rolled back and passed over alone; anything else is the campaign's failure.
    /// </summary>
    private async Task<Acted> OneLineAsync(
        JasonDbContext db,
        Active campaign,
        int before,
        Line line,
        Reply? reply,
        ResponderOptions options,
        CancellationToken ct)
    {
        await using (var transaction = await db.Database.BeginTransactionAsync(ct).ConfigureAwait(false))
        {
            try
            {
                if (await AccountForAsync(db, campaign.Id, before, line.Id, ct).ConfigureAwait(false) == 0)
                {
                    await transaction.RollbackAsync(ct).ConfigureAwait(false);
                    return new Acted(LineOutcome.Moved);
                }

                var outcome = new Acted(LineOutcome.Read);
                if (reply is not null)
                {
                    outcome = await ActAsync(db, campaign, line, reply, options, ct).ConfigureAwait(false);
                }

                await transaction.CommitAsync(ct).ConfigureAwait(false);
                db.ChangeTracker.Clear();
                return outcome;
            }
            catch (Exception ex) when (ex is DomainException || Refused(ex))
            {
                await transaction.RollbackAsync(ct).ConfigureAwait(false);
                db.ChangeTracker.Clear();
                var code = ex is DomainException domain ? domain.Code : "write_refused";
                if (skips.FirstTime(campaign.Id, code))
                {
                    PassedOver(logger, campaign.PublicId, code, null);
                }
            }
        }

        // Past the poisoned line alone, so it is not tried again on every scan and the lines after it are still read.
        // Only a refusal is the line's: a write that failed for any other reason, a disk or a lock, is rethrown above and
        // fails the campaign for this scan, so the next scan reads the line again.
        await using var passing = await db.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
        if (await AccountForAsync(db, campaign.Id, before, line.Id, ct).ConfigureAwait(false) == 0)
        {
            await passing.RollbackAsync(ct).ConfigureAwait(false);
            return new Acted(LineOutcome.Moved);
        }

        await passing.CommitAsync(ct).ConfigureAwait(false);
        return new Acted(LineOutcome.Read);
    }

    private async Task<Acted> ActAsync(JasonDbContext db, Active campaign, Line line, Reply reply, ResponderOptions options, CancellationToken ct)
    {
        var move = new JsonObject { ["line"] = line.PublicId, ["at"] = reply.LastActivityAtText };
        var open = await OpenResponderAsync(db, campaign.Id, reply, ct).ConfigureAwait(false);
        if (open is not null)
        {
            var recorded = open.Context[InboundReplyKey]!.DeepClone().AsObject();
            var moves = recorded["moves"] as JsonArray ?? [];
            var kept = moves.Select(node => node?.DeepClone()).Append(move).TakeLast(MovesKept).ToArray();
            recorded["moves"] = new JsonArray(kept);
            recorded["move_count"] = (recorded["move_count"] is JsonValue counted && counted.TryGetValue<int>(out var count) ? count : moves.Count) + 1;
            recorded["last_activity_at"] = reply.LastActivityAtText;

            await items.UpdateCoreAsync(
                new WorkItemUpdateRequest(
                    open.PublicId,
                    new JsonObject { [InboundReplyKey] = recorded },
                    Unset: null,
                    default,
                    default,
                    default,
                    default,
                    default,
                    default,
                    default,
                    default,
                    Actor: null,
                    Reason: "a further reply"),
                Actors.Dispatcher,
                ct).ConfigureAwait(false);
            return new Acted(LineOutcome.Recorded);
        }

        var contact = await MemberAsync(db, campaign.Id, reply.ContactId, ct).ConfigureAwait(false);
        var read = await ReadAsync(campaign, reply, contact, options, ct).ConfigureAwait(false);

        var responder = await items.CreateCoreAsync(
            new WorkItemCreateRequest(
                campaign.PublicId,
                WorkItemKind.AiRole,
                Role,
                Operation: null,
                contact,
                ExecutionProfile: null,
                options.Priority,
                NotBefore: null,
                DueAt: null,
                options.TimeoutSeconds,
                HeartbeatSeconds: null,
                options.MaxAttempts,
                new JsonObject
                {
                    ["trigger"] = JournalKinds.InboundReplyObserved,
                    [InboundReplyKey] = new JsonObject
                    {
                        ["plugin"] = reply.Plugin,
                        ["thread"] = reply.Thread,
                        ["channel"] = reply.Channel,
                        ["contact_id"] = reply.ContactId,
                        ["read"] = read?.PublicId,
                        ["last_activity_at"] = reply.LastActivityAtText,
                        ["moves"] = new JsonArray(move),
                        ["move_count"] = 1,
                    },
                },
                ResultFormat.DeepClone(),
                Actor: null,
                Reason: "a reply to answer"),
            Actors.Dispatcher,
            LineageRecord.Root,
            ct).ConfigureAwait(false);

        // Waiting on the read from the start: the responder is not claimed until the read has ended or the wait's deadline
        // has come, under a provider block or not, and spends no launch waiting for it. No attempt waited, so its first
        // run is told of no resumption and reads the read named in its context.
        if (read is not null)
        {
            outcomes.WaitFromCreation(db, responder, [read], Actors.Dispatcher);
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
        }

        return new Acted(LineOutcome.Woke, responder.PublicId);
    }

    /// <summary>
    /// The read of the thread, when the campaign's route for it names the plugin the reply was read at; none when it names
    /// another or nothing, or when the thread's identifier is one the operation's input refuses — validation refuses before
    /// anything is added, so nothing is left behind.
    /// </summary>
    private async Task<WorkItem?> ReadAsync(Active campaign, Reply reply, string? contact, ResponderOptions options, CancellationToken ct)
    {
        if (RouteResolver.Resolve(routes.Snapshot, campaign.PublicId, ReadOperation) is not { } resolution
            || !string.Equals(resolution.Route.PluginId, reply.Plugin, StringComparison.Ordinal))
        {
            return null;
        }

        try
        {
            return await items.CreateCoreAsync(
                new WorkItemCreateRequest(
                    campaign.PublicId,
                    WorkItemKind.ProviderOp,
                    Role: null,
                    ReadOperation,
                    contact,
                    ExecutionProfile: null,
                    options.Priority,
                    NotBefore: null,
                    DueAt: null,
                    TimeoutSeconds: null,
                    HeartbeatSeconds: null,
                    MaxAttempts: null,
                    new JsonObject
                    {
                        [WorkItemService.InputKey] = new JsonObject
                        {
                            ["thread"] = new JsonObject { ["external_id"] = reply.Thread },
                            ["limit"] = ReadLimit,
                            ["offset"] = 0,
                        },
                    },
                    ResultFormat: null,
                    Actor: null,
                    Reason: "a reply to read"),
                Actors.Dispatcher,
                LineageRecord.Root,
                ct).ConfigureAwait(false);
        }
        catch (ValidationException)
        {
            return null;
        }
    }

    /// <summary>
    /// The thread's open responder: one the runtime itself created, in this campaign, not finished, whose reply names the
    /// same plugin and thread. A responder a role or a person created is theirs, and not looked at.
    /// </summary>
    private static async Task<OpenResponder?> OpenResponderAsync(JasonDbContext db, int campaignId, Reply reply, CancellationToken ct)
    {
        // The statuses that are not final, named rather than negated, so the query seeks the campaign's live items by the
        // index on campaign and status instead of walking every item the campaign ever had, under the writer's lock.
        var open = await db.WorkItems
            .AsNoTracking()
            .Where(item => item.CampaignId == campaignId
                && (item.Status == WorkItemStatus.Created
                    || item.Status == WorkItemStatus.AwaitingApproval
                    || item.Status == WorkItemStatus.Scheduled
                    || item.Status == WorkItemStatus.Processing
                    || item.Status == WorkItemStatus.Waiting)
                && item.Role == Role
                && item.CreatedByType == ActorType.System)
            .OrderBy(item => item.Id)
            .Select(item => new OpenResponder(item.Id, item.PublicId, item.Context))
            .ToListAsync(ct)
            .ConfigureAwait(false);

        return open.FirstOrDefault(item => item.Context[InboundReplyKey] is JsonObject recorded
            && recorded["plugin"] is JsonValue plugin && plugin.TryGetValue<string>(out var p) && p == reply.Plugin
            && recorded["thread"] is JsonValue thread && thread.TryGetValue<string>(out var t) && t == reply.Thread);
    }

    /// <summary>
    /// The person the items are about, when the line names one the campaign may touch: not archived, and a member not
    /// excluded. Otherwise no one, and the reply still names them for the responder to read.
    /// </summary>
    private static async Task<string?> MemberAsync(JasonDbContext db, int campaignId, string? contactId, CancellationToken ct)
    {
        if (contactId is null)
        {
            return null;
        }

        var member = await db.CampaignContacts
            .AsNoTracking()
            .AnyAsync(
                m => m.CampaignId == campaignId
                    && m.State != MembershipState.Excluded
                    && m.Contact!.PublicId == contactId
                    && m.Contact.ArchivedAt == null,
                ct)
            .ConfigureAwait(false);
        return member ? contactId : null;
    }

    /// <summary>
    /// Whether the claim could launch a responder of this campaign: a profile is resolved for it, enabled or not, or an
    /// entry command is. Otherwise the claim would end it <c>role_not_launchable</c>, and nothing is created.
    /// </summary>
    private async Task<bool> ConfiguredAsync(JasonDbContext db, Active campaign, CancellationToken ct)
    {
        var rolePolicy = await db.Roles
            .AsNoTracking()
            .Where(role => role.Name == Role)
            .Select(role => role.ExecutionProfile)
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);

        var decision = ProfileResolution.Decide(new ProfileInputs(
            Override: null,
            campaign.ExecutionProfile,
            rolePolicy,
            LineageState.Root,
            LineageName: null,
            LineageRevision: null,
            roles.Current.DefaultExecutionProfile));

        return decision is not ProfileDecision.None
            || await entryCommands.ResolveAsync(db, Role, ct).ConfigureAwait(false) is not null;
    }

    /// <summary>
    /// No new line: the watermark goes to the campaign's own last line, read again inside the transaction that writes
    /// it, so a line said meanwhile is either seen here and left for the next scan or written after the move. A line said
    /// anywhere else leaves a level campaign alone, with no transaction: a quiet campaign costs one seek a scan.
    /// </summary>
    private static async Task LevelAsync(JasonDbContext db, Active campaign, int before, CancellationToken ct)
    {
        var last = await OwnLastLineAsync(db, campaign.Id, ct).ConfigureAwait(false);
        if (last <= before)
        {
            return;
        }

        await using var transaction = await db.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
        var said = await db.Journal
            .AnyAsync(entry => entry.CampaignId == campaign.Id && entry.Id > before && entry.Kind == JournalKinds.InboundReplyObserved, ct)
            .ConfigureAwait(false);
        last = await OwnLastLineAsync(db, campaign.Id, ct).ConfigureAwait(false);
        if (said || await AccountForAsync(db, campaign.Id, before, last, ct).ConfigureAwait(false) == 0)
        {
            await transaction.RollbackAsync(ct).ConfigureAwait(false);
            return;
        }

        await transaction.CommitAsync(ct).ConfigureAwait(false);
    }

    /// <summary>A write the database refused by a constraint, which the line caused; every other failure of a write is not its own.</summary>
    private static bool Refused(Exception exception) =>
        exception is DbUpdateException { InnerException: SqliteException { SqliteErrorCode: 19 } };

    /// <summary>The campaign's own last line, by the index on campaign and id; 0 for a campaign with none.</summary>
    private static async Task<int> OwnLastLineAsync(JasonDbContext db, int campaignId, CancellationToken ct) =>
        await db.Journal
            .Where(entry => entry.CampaignId == campaignId)
            .MaxAsync(entry => (int?)entry.Id, ct)
            .ConfigureAwait(false) ?? 0;

    /// <summary>Moves a campaign's watermark, and only from the value this step read.</summary>
    private static Task<int> AccountForAsync(JasonDbContext db, int campaignId, int before, int after, CancellationToken ct) =>
        db.Campaigns
            .Where(campaign => campaign.Id == campaignId && campaign.ResponderWatermark == before)
            .ExecuteUpdateAsync(set => set.SetProperty(campaign => campaign.ResponderWatermark, after), ct);

    /// <summary>Active campaigns, in the order they were made: a paused one holds its watermark until it resumes.</summary>
    private static async Task<IReadOnlyList<Active>> ActiveAsync(JasonDbContext db, CancellationToken ct) =>
        await db.Campaigns
            .AsNoTracking()
            .Where(campaign => campaign.Status == CampaignStatus.Active)
            .OrderBy(campaign => campaign.Id)
            .Select(campaign => new Active(campaign.Id, campaign.PublicId, campaign.ResponderWatermark, campaign.ExecutionProfile))
            .ToListAsync(ct)
            .ConfigureAwait(false);

    private sealed record Active(int Id, string PublicId, int Watermark, string? ExecutionProfile);

    private sealed record OpenResponder(int Id, string PublicId, JsonObject Context);

    private sealed record Line(int Id, string PublicId, JsonNode? New);

    private sealed record Acted(LineOutcome Outcome, string? WorkItemId = null);

    private enum LineOutcome
    {
        Read,
        Woke,
        Recorded,
        Moved,
    }

    /// <summary>What a line says about a reply, in the runtime's own fields: identifiers, a moment, which rule found the campaign.</summary>
    private sealed record Reply(
        string Plugin,
        string Thread,
        string? Channel,
        string? ContactId,
        DateTime LastActivityAt,
        string LastActivityAtText,
        string? AttributedBy)
    {
        /// <summary>The reply a line says, or none when the line lacks a field the rule needs.</summary>
        public static Reply? Read(Line line)
        {
            if (line.New is not JsonObject said
                || Text(said, "plugin") is not { } plugin
                || Text(said, "thread") is not { } thread
                || Text(said, "last_activity_at") is not { } moment
                || !DateTime.TryParse(moment, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var at))
            {
                return null;
            }

            return new Reply(plugin, thread, Text(said, "channel"), Text(said, "contact_id"), at, moment, Text(said, "attributed_by"));
        }

        private static string? Text(JsonObject said, string key) =>
            said[key] is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;
    }
}

/// <summary>Why a reply woke no responder, one code per reason.</summary>
public static class ResponderSkipCodes
{
    /// <summary>The claim could launch no responder of the campaign: no profile resolves for it and no entry command does.</summary>
    public const string NotConfigured = "responder_not_configured";

    /// <summary>The campaign holds the thread only through a person it may share with other campaigns: the manager's.</summary>
    public const string ThreadNotCampaignOwn = "thread_not_campaign_own";

    /// <summary>The thread last moved longer ago than <c>Responder:MaxAgeSeconds</c>: the manager's.</summary>
    public const string ReplyTooOld = "reply_too_old";
}

/// <summary>
/// Which campaigns the respond step has said it woke no responder for, and why, in the life of this process: once for each
/// campaign and reason rather than once a line. A restart says each once more.
/// </summary>
public sealed class ResponderSkips
{
    private readonly ConcurrentDictionary<(int Campaign, string Code), bool> _said = new();

    /// <summary>True the first time this campaign is passed over for this reason, and never again.</summary>
    public bool FirstTime(int campaign, string code) => _said.TryAdd((campaign, code), true);
}
