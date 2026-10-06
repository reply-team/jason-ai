using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Jason.Contracts.Api;
using Jason.Contracts.Json;
using Jason.Runtime.Configuration;
using Jason.Runtime.Domain;
using Jason.Runtime.Journal;
using Jason.Runtime.Json;
using Jason.Runtime.Logging;
using Jason.Runtime.Persistence;
using Jason.Runtime.Plugins.Registry;
using Jason.Runtime.Routing;
using Jason.Runtime.WorkItems;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Jason.Runtime.Dispatch;

/// <summary>What one inbound step did: the reads it started, and the lines it wrote about threads that moved.</summary>
public sealed record InboundReport(int Synced, int Observed);

/// <summary>
/// The step of a scan that reads each live campaign's conversations, between the lease enforcer and the summon: a line
/// it writes is read by the same scan's summon, and a read it starts is claimed by the same scan's claim.
/// <para>
/// For each active campaign, one read at a time. A read is a <c>conversation.list</c> work item the dispatcher creates
/// through the routine every work item is made by, so it is routed, claimed, retried and recorded like any other. A
/// campaign that cannot be read — nothing routes the operation, the plugin it is routed to does not perform it, or the
/// campaign was never linked there — is skipped, and the skip moves it on as an empty read would.
/// </para>
/// <para>
/// One campaign, short transactions, and one campaign's trouble is its own, as in the summon. Every write to a
/// campaign's row is guarded on what was read, so a row somebody else moved meanwhile is left as they left it.
/// </para>
/// </summary>
/// <remarks>
/// Nothing here reads meaning. A completed read is turned into lines that carry identifiers, moments and the
/// provider's own flags; what a reply says, and what to do about it, is the manager's.
/// </remarks>
public sealed class InboundSync(
    WorkItemService items,
    TimeProvider clock,
    LiveSettings<InboundOptions> settings,
    PluginRegistry plugins,
    RouteRegistry routes,
    InboundSkips skips,
    JournalWriter journal,
    ILogger<InboundSync> logger)
{
    /// <summary>The statuses in which a read is still under way, and is left to the routine that runs it.</summary>
    private static readonly WorkItemStatus[] Open =
    [
        WorkItemStatus.Created,
        WorkItemStatus.Scheduled,
        WorkItemStatus.Processing,
        WorkItemStatus.AwaitingApproval,
    ];

    /// <summary>The operation a read is.</summary>
    public const string Operation = "conversation.list";

    /// <summary>How many threads one page of a read asks for: the most the operation allows.</summary>
    public const int PageSize = 100;

    /// <summary>The longest a campaign waits for its next read, however many in a row have failed: a day.</summary>
    public const int MaximumWaitSeconds = 86_400;

    /// <summary>The furthest offset <c>conversation.list</c> takes: a read that would go past it ends where it is.</summary>
    public const int MaximumOffset = 1_000_000;

    /// <summary>The kind of pin that links a campaign at a plugin.</summary>
    private const string CampaignPin = "campaign";

    /// <summary>The kind of pin that names a person at a plugin.</summary>
    private const string ContactPin = "contact";

    private static readonly Action<ILogger, string, string, Exception?> Skipped = LoggerMessage.Define<string, string>(
        LogLevel.Information,
        new EventId(1, nameof(Skipped)),
        "Inbound sync skipped campaign {CampaignId}: {Code}");

    private static readonly Action<ILogger, string, Exception?> Failed = LoggerMessage.Define<string>(
        LogLevel.Warning,
        new EventId(2, nameof(Failed)),
        "The inbound sync of campaign {CampaignId} failed; the rest of the scan continues without it");

    // The same event, for a failure while reading JSON: what kind and where, and no exception, because what the parser
    // quoted is a provider's text.
    private static readonly Action<ILogger, string, string, Exception?> FailedToParse = LoggerMessage.Define<string, string>(
        LogLevel.Warning,
        new EventId(2, nameof(Failed)),
        "The inbound sync of campaign {CampaignId} failed; the rest of the scan continues without it: {Failure}");

    // Said by its kind and where a reader stopped, never by what the result held: a page is a provider's words.
    private static readonly Action<ILogger, string, string, Exception?> NotAPage = LoggerMessage.Define<string, string>(
        LogLevel.Warning,
        new EventId(4, nameof(NotAPage)),
        "The inbound read of campaign {CampaignId} ended with a result that is not a page: {Failure}");

    private static readonly Action<ILogger, string, Exception?> Moved = LoggerMessage.Define<string>(
        LogLevel.Information,
        new EventId(3, nameof(Moved)),
        "Campaign {CampaignId} was moved by another writer while its inbound sync was reading it");

    public async Task<InboundReport> SyncAsync(JasonDbContext db, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(db);
        var options = settings.Current;
        if (!options.Enabled)
        {
            return new InboundReport(0, 0);
        }

        var now = clock.GetUtcNow().UtcDateTime;
        var synced = 0;
        var observed = 0;

        foreach (var campaign in await LiveAsync(db, ct).ConfigureAwait(false))
        {
            // Per campaign, because the steps after this one summon and hand out the work there is: one campaign's
            // write that will not land costs that campaign, not every campaign behind it.
            try
            {
                if (campaign.ItemId is not null)
                {
                    observed += await AccountAsync(db, campaign, options, now, ct).ConfigureAwait(false);
                }
                else if (IsDue(campaign.SyncedAt, campaign.Failures, options.SyncSeconds, now)
                    && await StartAsync(db, campaign, options, now, ct).ConfigureAwait(false))
                {
                    synced++;
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                SyncFailed(campaign.PublicId, ex);
                db.ChangeTracker.Clear();

                // A read that cannot be accounted for would otherwise be tried again every scan, and fail and be said
                // again every scan, for ever. It counts as a failed read instead: the campaign waits longer, and its
                // next read starts again from the first page.
                if (campaign.ItemId is not null)
                {
                    await GiveUpAsync(db, campaign, ct).ConfigureAwait(false);
                }
            }
        }

        return new InboundReport(synced, observed);
    }

    /// <summary>
    /// The failed-read accounting of a read whose accounting threw, once the read is known to have ended: a read still
    /// running is left to finish, or the campaign would start a second read beside it. A second failure here is already
    /// said.
    /// </summary>
    private async Task GiveUpAsync(JasonDbContext db, Live campaign, CancellationToken ct)
    {
        try
        {
            var status = await db.WorkItems
                .AsNoTracking()
                .Where(row => row.Id == campaign.ItemId)
                .Select(row => (WorkItemStatus?)row.Status)
                .FirstOrDefaultAsync(ct)
                .ConfigureAwait(false);
            if (status is { } running && Open.Contains(running))
            {
                return;
            }

            await FailAsync(db, campaign, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            db.ChangeTracker.Clear();
        }
    }

    /// <summary>
    /// Whether a campaign's next read is due: at once when it has never been read, otherwise a cadence after the last
    /// one started, doubled for each read in a row that failed, and never more than a day.
    /// </summary>
    public static bool IsDue(DateTime? syncedAt, int failures, int syncSeconds, DateTime now)
    {
        if (syncedAt is not { } last)
        {
            return true;
        }

        // Doubling past the cap only overflows: thirty-two doublings of a minute are far beyond a day.
        var wait = Math.Min(syncSeconds * Math.Pow(2, Math.Clamp(failures, 0, 32)), MaximumWaitSeconds);
        return now >= last.AddSeconds(wait);
    }

    /// <summary>The later of two moments, where the first may be missing: how a watermark moves, never back.</summary>
    public static DateTime Later(DateTime? current, DateTime candidate) =>
        current is { } value && value >= candidate ? value : candidate;

    /// <summary>
    /// The read still outstanding, accounted for once it has ended. Until it has, nothing: the item is claimed, retried
    /// and finished by the ordinary routine, and this step only reads what it ended with.
    /// </summary>
    private async Task<int> AccountAsync(JasonDbContext db, Live campaign, InboundOptions options, DateTime now, CancellationToken ct)
    {
        var item = await db.WorkItems
            .Include(row => row.Attempts.Where(attempt => attempt.Status == AttemptStatus.Succeeded))
            .FirstOrDefaultAsync(row => row.Id == campaign.ItemId, ct)
            .ConfigureAwait(false);
        if (item is not null && Open.Contains(item.Status))
        {
            return 0;
        }

        if (item is null || item.Status != WorkItemStatus.Succeeded)
        {
            await FailAsync(db, campaign, ct).ConfigureAwait(false);
            return 0;
        }

        // The plugin the read ran at, as its attempt recorded it at the claim - not the one the campaign is routed to
        // now. A route changed since then changes where the next read goes, not what this one found.
        var attempt = item.Attempts.SingleOrDefault();
        var unreadable = Unreadable(attempt, item, out var page, out var since, out var offset);
        if (unreadable is not null || attempt?.Provenance?.PluginId is not { } pluginId)
        {
            NotAPage(logger, campaign.PublicId, unreadable ?? "no_plugin", null);
            await FailAsync(db, campaign, ct).ConfigureAwait(false);
            return 0;
        }

        var threads = Observed(page!, since, pluginId);
        var said = await SaidAsync(db, campaign.Id, threads.Select(thread => thread.Key).ToList(), ct).ConfigureAwait(false);
        var fresh = threads.Where(thread => !said.Contains(thread.Key)).ToList();
        var people = await PeopleAsync(db, pluginId, fresh, ct).ConfigureAwait(false);

        await using var transaction = await db.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
        foreach (var thread in fresh)
        {
            journal.Append(
                db,
                Actors.Dispatcher,
                JournalKinds.InboundReplyObserved,
                campaign: null,
                key: thread.Key,
                updated: new JsonObject
                {
                    ["plugin"] = pluginId,
                    ["thread"] = thread.Id,
                    ["channel"] = thread.Channel,
                    ["last_activity_at"] = thread.Moment,
                    ["unread"] = thread.Unread,
                    ["has_meeting_intent"] = thread.HasMeetingIntent,
                    ["contact_id"] = thread.Person is { } person && people.TryGetValue(person, out var contact) ? contact : null,
                },
                workItem: item,
                attempt: attempt);
        }

        // The provider holds more: the next page is asked for now, from where this one ended and with the same start, so
        // every page of one read lists the threads that moved since the same moment. A page that says more follow and
        // returned nothing would be asked for again for ever, and one past the furthest offset the operation takes
        // cannot be asked for at all; either ends the read.
        var returned = page!.Items!.Count;
        var next = offset + returned;
        int accounted;
        if (page.HasMore == true && returned > 0 && next <= MaximumOffset)
        {
            var following = await items.CreateCoreAsync(Request(campaign.PublicId, since, next), Actors.Dispatcher, LineageRecord.Root, ct)
                .ConfigureAwait(false);
            accounted = await Reading(db, campaign)
                .ExecuteUpdateAsync(
                    set => set
                        .SetProperty(row => row.InboundSyncItemId, following.Id)
                        .SetProperty(row => row.InboundOffset, next),
                    ct)
                .ConfigureAwait(false);
        }
        else
        {
            // A complete read: the next starts from where this one started, less the look-back, unless the watermark
            // is already later. It follows the clock, not the moments read, so a campaign nobody writes to still moves
            // on, and a reply the provider lists only after a later one was read is still listed next time.
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
            var watermark = Later(campaign.Watermark, (campaign.SyncedAt ?? now).AddSeconds(-options.LookbackSeconds));
            accounted = await Reading(db, campaign)
                .ExecuteUpdateAsync(
                    set => set
                        .SetProperty(row => row.InboundSyncItemId, (int?)null)
                        .SetProperty(row => row.InboundOffset, 0)
                        .SetProperty(row => row.InboundFailures, 0)
                        .SetProperty(row => row.InboundWatermark, watermark),
                    ct)
                .ConfigureAwait(false);
        }

        if (accounted == 0)
        {
            await transaction.RollbackAsync(ct).ConfigureAwait(false);
            db.ChangeTracker.Clear();
            Moved(logger, campaign.PublicId, null);
            return 0;
        }

        await transaction.CommitAsync(ct).ConfigureAwait(false);
        return fresh.Count;
    }

    /// <summary>
    /// A read that ended without a page — failed, cancelled, expired, or succeeded with something that is not one —
    /// counts a failure, so the next read waits longer, and starts again from the first page with the same start. The
    /// watermark and the moment the read started stay where they were. Starting again rather than resuming is what keeps
    /// a reply: pages are ordered by last activity, so a thread that moved while the read waited is listed first, and a
    /// read that resumed past the first page would never list it. The threads listed again are not said twice: their
    /// keys are already said.
    /// </summary>
    private async Task FailAsync(JasonDbContext db, Live campaign, CancellationToken ct)
    {
        var accounted = await Reading(db, campaign)
            .ExecuteUpdateAsync(
                set => set
                    .SetProperty(row => row.InboundSyncItemId, (int?)null)
                    .SetProperty(row => row.InboundOffset, 0)
                    .SetProperty(row => row.InboundFailures, row => row.InboundFailures + 1),
                ct)
            .ConfigureAwait(false);
        if (accounted == 0)
        {
            Moved(logger, campaign.PublicId, null);
        }
    }

    /// <summary>
    /// Why a succeeded read's result cannot be read as a page, by kind and never by what it holds: null when it can,
    /// with the page and the moment the read asked from.
    /// </summary>
    /// <remarks>
    /// Where the read started and which page it asked for are read from the input its attempt was claimed with, not
    /// from the item's context as it is now: a context can still be edited while a read runs, and what the read listed
    /// is what it was asked for.
    /// </remarks>
    private static string? Unreadable(Attempt? attempt, WorkItem item, out InboundPage? page, out DateTime since, out int offset)
    {
        page = null;
        since = default;
        offset = 0;
        if (attempt is null)
        {
            return "no_attempt";
        }

        if (Since(attempt.ContextSnapshot) is not { } start)
        {
            return "no_start";
        }

        if (Offset(attempt.ContextSnapshot) is not { } asked)
        {
            return "no_offset";
        }

        since = start;
        offset = asked;
        try
        {
            page = InboundPage.Read(item.Result);
        }
        catch (JsonException ex)
        {
            return ParseFailure.Describe(ex);
        }

        return page is null ? "not_a_page" : null;
    }

    /// <summary>
    /// The threads of a page that moved at or after the read's start, each once, in the page's order and with its key:
    /// the plugin, the thread and the moment, hashed. A thread listed twice on one page is one move; a thread whose last
    /// move is before the start is history, even if the provider listed it.
    /// </summary>
    private static List<Observation> Observed(InboundPage page, DateTime since, string pluginId)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var threads = new List<Observation>();
        foreach (var thread in page.Items!)
        {
            if (thread?.ExternalId is not { Length: > 0 } id || thread.LastActivityAt is not { } moved || moved.UtcDateTime < since)
            {
                continue;
            }

            var moment = Instant(moved);
            var key = CanonicalJson.Hash(new JsonObject { ["plugin"] = pluginId, ["thread"] = id, ["moment"] = moment });
            if (seen.Add(key))
            {
                threads.Add(new Observation(key, id, thread.Channel, moment, thread.Unread, thread.HasMeetingIntent, thread.Contact?.ExternalId));
            }
        }

        return threads;
    }

    /// <summary>Which of these keys this campaign has already said: one query for the page, on the journal's own index.</summary>
    private static async Task<HashSet<string>> SaidAsync(JasonDbContext db, int campaignId, List<string> keys, CancellationToken ct)
    {
        if (keys.Count == 0)
        {
            return [];
        }

        var said = await db.Journal
            .AsNoTracking()
            .Where(entry => entry.CampaignId == campaignId && entry.Kind == JournalKinds.InboundReplyObserved && keys.Contains(entry.Key!))
            .Select(entry => entry.Key!)
            .ToListAsync(ct)
            .ConfigureAwait(false);
        return new HashSet<string>(said, StringComparer.Ordinal);
    }

    /// <summary>
    /// The contact each person is, by the identifier the provider gave: the one contact pinned at this plugin with it, and
    /// nobody when none is or more than one is. A value recorded only beside a pin, as a divergence, is not a pin.
    /// </summary>
    private static async Task<Dictionary<string, string>> PeopleAsync(
        JasonDbContext db,
        string pluginId,
        List<Observation> threads,
        CancellationToken ct)
    {
        var values = threads.Select(thread => thread.Person).OfType<string>().Distinct(StringComparer.Ordinal).ToList();
        if (values.Count == 0)
        {
            return new Dictionary<string, string>(StringComparer.Ordinal);
        }

        var pins = await db.ExternalIds
            .AsNoTracking()
            .Where(pin => pin.PluginId == pluginId && pin.Kind == ContactPin && pin.ContactId != null && values.Contains(pin.Value))
            .Select(pin => new { pin.Value, Contact = pin.Contact!.PublicId })
            .ToListAsync(ct)
            .ConfigureAwait(false);
        return pins
            .GroupBy(pin => pin.Value, StringComparer.Ordinal)
            .Where(group => group.Count() == 1)
            .ToDictionary(group => group.Key, group => group.Single().Contact, StringComparer.Ordinal);
    }

    /// <summary>Which page the read asked for, as its own context says: the offset of its first thread.</summary>
    private static int? Offset(JsonObject context) =>
        context["input"] is JsonObject input && input["offset"] is JsonValue value && value.TryGetValue(out int offset)
            ? offset
            : null;

    /// <summary>Where the read started, as its own context says: the moment it asked the provider for threads since.</summary>
    private static DateTime? Since(JsonObject context) =>
        context["input"] is JsonObject input
            && input["since"] is JsonValue value
            && value.TryGetValue(out string? text)
            && DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var since)
                ? since.UtcDateTime
                : null;

    /// <summary>A moment as every timestamp the runtime writes: ISO-8601 UTC to the millisecond.</summary>
    private static string Instant(DateTimeOffset moment) =>
        JsonSerializer.SerializeToNode(moment, JasonJson.Options)!.GetValue<string>();

    /// <summary>The campaign's row, as long as the read this step is accounting for is still the one outstanding.</summary>
    private static IQueryable<Campaign> Reading(JasonDbContext db, Live campaign) =>
        db.Campaigns.Where(row => row.Id == campaign.Id
            && row.InboundSyncItemId == campaign.ItemId
            && row.InboundSyncedAt == campaign.SyncedAt);

    /// <summary>One thread that moved, as a line will say it.</summary>
    private sealed record Observation(
        string Key,
        string Id,
        string? Channel,
        string Moment,
        bool? Unread,
        bool? HasMeetingIntent,
        string? Person);

    /// <summary>
    /// Starts a read, or skips the campaign by the first reason it cannot be read, in a fixed order: nothing routes the
    /// operation; the plugin it is routed to is loaded and does not perform it; the campaign was never linked there. A
    /// plugin routed but not loaded, unavailable, speaking another contract version or refusing its binding is not a
    /// reason to skip: the read is made and fails at its claim, where the reason is recorded on the item.
    /// </summary>
    private async Task<bool> StartAsync(JasonDbContext db, Live campaign, InboundOptions options, DateTime now, CancellationToken ct)
    {
        if (campaign.Watermark is not { } watermark)
        {
            // A live campaign always has a watermark: going live sets one and an upgrade gave every live campaign one.
            // Should one have none, it is levelled as a skip levels it and read from then on.
            await LevelAsync(db, campaign, options, now, ct).ConfigureAwait(false);
            return false;
        }

        if (RouteResolver.Resolve(routes.Snapshot, campaign.PublicId, Operation) is not { } resolution)
        {
            await SkipAsync(db, campaign, InboundSkipCodes.NoRoute, options, now, ct).ConfigureAwait(false);
            return false;
        }

        var pluginId = resolution.Route.PluginId;
        if (RouteChecks.Loaded(plugins.Snapshot, pluginId) is { } plugin
            && (RouteChecks.KindNotInvocable(plugin) || RouteChecks.OperationUnsupported(plugin, Operation)))
        {
            await SkipAsync(db, campaign, InboundSkipCodes.PluginOperationUnsupported, options, now, ct).ConfigureAwait(false);
            return false;
        }

        var linked = await db.ExternalIds
            .AsNoTracking()
            .AnyAsync(pin => pin.CampaignId == campaign.Id && pin.PluginId == pluginId && pin.Kind == CampaignPin, ct)
            .ConfigureAwait(false);
        if (!linked)
        {
            await SkipAsync(db, campaign, InboundSkipCodes.CampaignNotLinked, options, now, ct).ConfigureAwait(false);
            return false;
        }

        await using var transaction = await db.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
        // A read always starts from the first page: a page offset means something only while its read is outstanding.
        var item = await items.CreateCoreAsync(
            Request(campaign.PublicId, watermark, offset: 0),
            Actors.Dispatcher,
            LineageRecord.Root,
            ct).ConfigureAwait(false);

        var started = await Unread(db, campaign)
            .ExecuteUpdateAsync(
                set => set
                    .SetProperty(row => row.InboundSyncItemId, item.Id)
                    .SetProperty(row => row.InboundSyncedAt, now)
                    .SetProperty(row => row.InboundOffset, 0),
                ct)
            .ConfigureAwait(false);
        if (started == 0)
        {
            await transaction.RollbackAsync(ct).ConfigureAwait(false);
            db.ChangeTracker.Clear();
            Moved(logger, campaign.PublicId, null);
            return false;
        }

        await transaction.CommitAsync(ct).ConfigureAwait(false);
        return true;
    }

    /// <summary>
    /// A campaign that cannot be read is moved on as a complete read that found nothing would move it: its read is
    /// stamped now, and its watermark becomes now less the look-back unless it is already later. So a campaign that
    /// gains its route or its link later starts from then, less the look-back, and never before it went live. Said once
    /// for the campaign and the reason in the life of this process, not once a cadence.
    /// </summary>
    private async Task SkipAsync(JasonDbContext db, Live campaign, string code, InboundOptions options, DateTime now, CancellationToken ct)
    {
        if (await LevelAsync(db, campaign, options, now, ct).ConfigureAwait(false) && skips.FirstTime(campaign.Id, code))
        {
            Skipped(logger, campaign.PublicId, code, null);
        }
    }

    private async Task<bool> LevelAsync(JasonDbContext db, Live campaign, InboundOptions options, DateTime now, CancellationToken ct)
    {
        var watermark = Later(campaign.Watermark, now.AddSeconds(-options.LookbackSeconds));
        var levelled = await Unread(db, campaign)
            .ExecuteUpdateAsync(
                set => set
                    .SetProperty(row => row.InboundSyncedAt, now)
                    .SetProperty(row => row.InboundWatermark, watermark)
                    .SetProperty(row => row.InboundFailures, 0),
                ct)
            .ConfigureAwait(false);
        if (levelled == 0)
        {
            Moved(logger, campaign.PublicId, null);
        }

        return levelled > 0;
    }

    /// <summary>The campaign's row, as long as it still has no read outstanding and its last read is the one this step saw.</summary>
    private static IQueryable<Campaign> Unread(JasonDbContext db, Live campaign) =>
        db.Campaigns.Where(row => row.Id == campaign.Id
            && row.InboundSyncItemId == null
            && row.InboundSyncedAt == campaign.SyncedAt);

    /// <summary>
    /// One page of a read: the threads the other side wrote into that moved at or after the watermark, a full page at a
    /// time, from the offset the read is at. The watermark is written to the second, rounded down, so the read never
    /// starts later than the watermark says.
    /// </summary>
    private static WorkItemCreateRequest Request(string campaignId, DateTime since, int offset) =>
        new(
            campaignId,
            WorkItemKind.ProviderOp,
            Role: null,
            Operation,
            ContactId: null,
            ExecutionProfile: null,
            Priority: null,
            NotBefore: null,
            DueAt: null,
            TimeoutSeconds: null,
            HeartbeatSeconds: null,
            MaxAttempts: null,
            new JsonObject
            {
                ["input"] = new JsonObject
                {
                    ["source"] = "inbound",
                    ["since"] = Stamp(since),
                    ["limit"] = PageSize,
                    ["offset"] = offset,
                },
            },
            ResultFormat: null,
            Actor: null,
            Reason: "inbound sync");

    private static string Stamp(DateTime moment) =>
        new DateTime(moment.Ticks - (moment.Ticks % TimeSpan.TicksPerSecond), DateTimeKind.Utc)
            .ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

    private void SyncFailed(string campaignId, Exception exception)
    {
        if (ParseFailure.Is(exception))
        {
            FailedToParse(logger, campaignId, ParseFailure.Describe(exception), null);
        }
        else
        {
            Failed(logger, campaignId, exception);
        }
    }

    /// <summary>Active campaigns, in the order they were made, with what their read stands at.</summary>
    private static async Task<IReadOnlyList<Live>> LiveAsync(JasonDbContext db, CancellationToken ct) =>
        await db.Campaigns
            .AsNoTracking()
            .Where(campaign => campaign.Status == CampaignStatus.Active)
            .OrderBy(campaign => campaign.Id)
            .Select(campaign => new Live(
                campaign.Id,
                campaign.PublicId,
                campaign.InboundWatermark,
                campaign.InboundSyncedAt,
                campaign.InboundSyncItemId,
                campaign.InboundOffset,
                campaign.InboundFailures))
            .ToListAsync(ct)
            .ConfigureAwait(false);

    /// <summary>A campaign this step will consider, and where its read stands as this step read it.</summary>
    private sealed record Live(int Id, string PublicId, DateTime? Watermark, DateTime? SyncedAt, int? ItemId, int Offset, int Failures);
}

/// <summary>Why a campaign was not read, one code per reason, in the order they are asked.</summary>
public static class InboundSkipCodes
{
    public const string NoRoute = "no_route";

    public const string PluginOperationUnsupported = "plugin_operation_unsupported";

    public const string CampaignNotLinked = "campaign_not_linked";
}

/// <summary>
/// Which campaigns have been said to be skipped, and why, in the life of this process: a skip is logged once for its
/// campaign and reason rather than once a cadence. A restart says each once more.
/// </summary>
public sealed class InboundSkips
{
    private readonly ConcurrentDictionary<(int Campaign, string Code), bool> _said = new();

    /// <summary>True the first time this campaign is skipped for this reason, and never again.</summary>
    public bool FirstTime(int campaign, string code) => _said.TryAdd((campaign, code), true);
}
