using System.Collections.Concurrent;
using System.Text.Json.Nodes;
using Jason.Contracts.Api;
using Jason.Contracts.Operations;
using Jason.Runtime.Configuration;
using Jason.Runtime.Domain;
using Jason.Runtime.Journal;
using Jason.Runtime.Logging;
using Jason.Runtime.Persistence;
using Jason.Runtime.Plugins.Registry;
using Jason.Runtime.Routing;
using Jason.Runtime.WorkItems;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Jason.Runtime.Dispatch;

/// <summary>
/// The step of a scan right after the bounce step: a deferred send whose moment has passed is read, to learn what became
/// of it. Its candidates are the one definition of a send the provider holds (<see cref="HeldSends"/>) with the moment
/// test turned round: a send the definition covers whose moment passed at least <c>Dispatcher:DeferredReadGraceSeconds</c>
/// ago, handed over and due no more than <see cref="LookBack"/> ago, of which nothing yet says what became of it — no
/// succeeded cancel, no answer that the provider holds no such send, no final read, whoever made it. For each it queues a
/// read (<c>message.status</c>) where the send ran, as the dispatcher, unless one the runtime made is open already; the
/// first once the grace has passed, the second <c>Dispatcher:DeferredReadFirstWaitSeconds</c> after the first ended, and
/// each later one <c>Dispatcher:DeferredReadWaitSeconds</c> after the one before it ended, whatever it answered, until the
/// send is said not to have gone (<see cref="JournalKinds.MessageUnsentObserved"/>) or <see cref="MostReads"/> have
/// succeeded: the reads it counts are the reads the runtime made of the send that succeeded and ended at or after its
/// moment. A read that ends <c>Dispatcher:DeferredReadOverdueSeconds</c> or more after the moment and still finds the
/// send waiting says it is overdue, so a read judged under a span since changed is followed by one more; a read that
/// failed counts for none of the nine.
/// <para>
/// It writes no line: each read decides alone, at its own end (<see cref="Execution.SentFacts"/>), whoever made it. A
/// read that finds the send went says so as it always has; one that finds it failed or was cancelled, and the last read
/// where none found what became of it, say the send did not go.
/// </para>
/// <para>
/// An active campaign's sends only: a paused campaign's reads wait, so none is made there. The step makes no read in a
/// campaign while <see cref="OpenPerCampaign"/> reads the runtime made are open there, whichever step made them, the
/// oldest sends first. A read no route takes where the send ran is not made, said once per campaign, and asked again at
/// the next scan. The candidates are read at most once every <see cref="Cadence"/>: no wait of the reads is shorter. A
/// read of them that fails, a stored value it cannot parse among them, skips the step's run and never the scan.
/// </para>
/// </summary>
public sealed class DeferredReads(
    WorkItemService items,
    LiveSettings<DispatcherOptions> settings,
    RouteRegistry routes,
    PluginRegistry plugins,
    DeferredReadSkips skips,
    TimeProvider clock,
    ILogger<DeferredReads> logger)
{
    /// <summary>The most reads the runtime makes of one send after its moment.</summary>
    public const int MostReads = 9;

    /// <summary>The most reads the runtime made that may be open in one campaign at once.</summary>
    public const int OpenPerCampaign = 5;

    /// <summary>The reason each read carries, as its creation line records it.</summary>
    public const string Reason = "a deferred send to read";

    /// <summary>How far back a moment is read: a send whose moment passed longer ago than this is never read.</summary>
    public static TimeSpan LookBack { get; } = TimeSpan.FromDays(30);

    /// <summary>How often the candidates are read at most: a minute, the shortest grace or wait a setting may name.</summary>
    public static TimeSpan Cadence { get; } = TimeSpan.FromSeconds(DispatcherOptions.MinimumDeferredReadSeconds);

    private static readonly OperationContract StatusContract = OperationCatalog.Find(SendReads.Operation)!;

    private static readonly Action<ILogger, string, int, Exception?> Queued = LoggerMessage.Define<string, int>(
        LogLevel.Information,
        new EventId(1, nameof(Queued)),
        "The deferred read step queued reads of deferred sends for campaign {CampaignId}: {Count}");

    private static readonly Action<ILogger, string, string, Exception?> Skipped = LoggerMessage.Define<string, string>(
        LogLevel.Information,
        new EventId(2, nameof(Skipped)),
        "The deferred read step queued no read of a deferred send for campaign {CampaignId}: {Code}");

    private static readonly Action<ILogger, string, Exception?> Failed = LoggerMessage.Define<string>(
        LogLevel.Warning,
        new EventId(3, nameof(Failed)),
        "The deferred read step for campaign {CampaignId} failed; the rest of the scan continues without it");

    private static readonly Action<ILogger, string, string, Exception?> FailedToParse = LoggerMessage.Define<string, string>(
        LogLevel.Warning,
        new EventId(3, nameof(Failed)),
        "The deferred read step for campaign {CampaignId} failed; the rest of the scan continues without it: {Failure}");

    private static readonly Action<ILogger, Exception?> Unread = LoggerMessage.Define(
        LogLevel.Warning,
        new EventId(4, nameof(Unread)),
        "The deferred read step could not read the deferred sends; it reads them again at its next run, and the rest of the scan continues without it");

    private static readonly Action<ILogger, string, Exception?> UnreadToParse = LoggerMessage.Define<string>(
        LogLevel.Warning,
        new EventId(4, nameof(Unread)),
        "The deferred read step could not read the deferred sends; it reads them again at its next run, and the rest of the scan continues without it: {Failure}");

    /// <summary>
    /// How long after its moment the last of <see cref="MostReads"/> reads of a send ends at the soonest: the grace, the
    /// first wait, and a wait after each read after the second. A send may be said overdue no later than this, or its
    /// last read would end before it could be.
    /// </summary>
    public static long Reached(DispatcherOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return (long)options.DeferredReadGraceSeconds + options.DeferredReadFirstWaitSeconds + ((long)(MostReads - 2) * options.DeferredReadWaitSeconds);
    }

    /// <summary>Queues the reads of deferred sends that are due, and answers how many it queued.</summary>
    public async Task<int> ReadAsync(JasonDbContext db, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(db);
        var now = clock.GetUtcNow();
        if (!skips.Due(now))
        {
            return 0;
        }

        var options = settings.Current;
        IReadOnlyList<HeldSend> passed;
        Dictionary<string, int> active;
        try
        {
            passed = await HeldSends.ReadPassedAsync(db, now.AddSeconds(-options.DeferredReadGraceSeconds), now - LookBack, ct).ConfigureAwait(false);
            if (passed.Count == 0)
            {
                return 0;
            }

            active = await db.Campaigns
                .AsNoTracking()
                .Where(campaign => campaign.Status == CampaignStatus.Active)
                .Select(campaign => new { campaign.Id, campaign.PublicId })
                .ToDictionaryAsync(campaign => campaign.PublicId, campaign => campaign.Id, StringComparer.Ordinal, ct)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // The candidates are every campaign's, read in one query: a value it cannot parse in any of them, or a failure of
            // the database, would otherwise end the scan. The run is skipped, said by what failed and never by what was read.
            if (ParseFailure.Is(ex))
            {
                UnreadToParse(logger, ParseFailure.Describe(ex), null);
            }
            else
            {
                Unread(logger, ex);
            }

            db.ChangeTracker.Clear();
            return 0;
        }

        var queued = 0;
        foreach (var sends in passed.Where(send => active.ContainsKey(send.Campaign)).GroupBy(send => send.Campaign, StringComparer.Ordinal))
        {
            try
            {
                queued += await ReadOneAsync(db, active[sends.Key], sends.Key, [.. sends], options, now, ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                if (ParseFailure.Is(ex))
                {
                    FailedToParse(logger, sends.Key, ParseFailure.Describe(ex), null);
                }
                else
                {
                    Failed(logger, sends.Key, ex);
                }

                db.ChangeTracker.Clear();
            }
        }

        return queued;
    }

    /// <summary>
    /// Whether a read of <paramref name="send"/> is due at <paramref name="now"/>, by the reads the runtime made of it
    /// since its moment (<paramref name="made"/>, null where none has ended) and whether it was said not to have gone
    /// (<paramref name="said"/>): the first once the grace has passed, then a wait after the last one ended, whatever it
    /// answered, the first wait after the first read and the later wait after each other; never once the send was said not
    /// to have gone, which a read past the overdue span that still finds it waiting says, or once
    /// <see cref="MostReads"/> have succeeded. A read that failed spaces the next and counts for none of the nine.
    /// </summary>
    internal static bool Due(HeldSend send, MadeReads? made, bool said, DispatcherOptions options, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(send);
        ArgumentNullException.ThrowIfNull(options);
        if (said)
        {
            return false;
        }

        if (made is null)
        {
            return now >= send.Moment.AddSeconds(options.DeferredReadGraceSeconds);
        }

        if (made.Succeeded >= MostReads)
        {
            return false;
        }

        var wait = made.Ended == 1 ? options.DeferredReadFirstWaitSeconds : options.DeferredReadWaitSeconds;
        return now >= made.LastEnded.AddSeconds(wait);
    }

    private async Task<int> ReadOneAsync(
        JasonDbContext db, int campaignRow, string campaign, IReadOnlyList<HeldSend> sends, DispatcherOptions options, DateTimeOffset now, CancellationToken ct)
    {
        var room = OpenPerCampaign - await SendReads.OpenCountAsync(db, campaignRow, ct).ConfigureAwait(false);
        if (room <= 0)
        {
            return 0;
        }

        var ids = sends.Select(send => send.WorkItem).ToList();
        var open = await SendReads.OpenAsync(db, campaignRow, ids, ct).ConfigureAwait(false);
        var made = await SendReads.MadeAsync(db, campaignRow, sends.ToDictionary(send => send.WorkItem, send => send.Moment, StringComparer.Ordinal), ct)
            .ConfigureAwait(false);
        var said = await SaidAsync(db, campaignRow, ids, ct).ConfigureAwait(false);

        var queued = 0;
        var skipped = false;
        foreach (var send in sends)
        {
            if (queued >= room)
            {
                break;
            }

            if (open.Contains(send.WorkItem) || !Due(send, made.GetValueOrDefault(send.WorkItem), said.Contains(send.WorkItem), options, now))
            {
                continue;
            }

            if (!Routed(send))
            {
                skipped = true;
                continue;
            }

            await items.CreateCoreAsync(
                new WorkItemCreateRequest(
                    campaign,
                    WorkItemKind.ProviderOp,
                    Role: null,
                    SendReads.Operation,
                    ContactId: null,
                    ExecutionProfile: null,
                    Priority: 0,
                    NotBefore: null,
                    DueAt: null,
                    TimeoutSeconds: null,
                    HeartbeatSeconds: null,
                    MaxAttempts: null,
                    new JsonObject { [WorkItemService.InputKey] = new JsonObject { ["send"] = send.WorkItem } },
                    ResultFormat: null,
                    Actor: null,
                    Reason: Reason),
                Actors.Dispatcher,
                LineageRecord.Root,
                ct).ConfigureAwait(false);
            queued++;
        }

        if (skipped && skips.FirstTime(campaignRow, DeferredReadSkipCodes.NotRouted))
        {
            Skipped(logger, campaign, DeferredReadSkipCodes.NotRouted, null);
        }

        if (queued > 0)
        {
            Queued(logger, campaign, queued, null);
        }

        return queued;
    }

    /// <summary>
    /// Which of the sends named a line already says did not go: its key is the send's work item, one line per send, read by
    /// the campaign, the kind and the key the line's own index holds.
    /// </summary>
    private static async Task<IReadOnlySet<string>> SaidAsync(JasonDbContext db, int campaignRow, IReadOnlyCollection<string> sends, CancellationToken ct)
    {
        var said = await db.Journal
            .AsNoTracking()
            .Where(entry => entry.CampaignId == campaignRow && entry.Kind == JournalKinds.MessageUnsentObserved && sends.Contains(entry.Key!))
            .Select(entry => entry.Key!)
            .ToListAsync(ct)
            .ConfigureAwait(false);
        return said.ToHashSet(StringComparer.Ordinal);
    }

    /// <summary>
    /// Whether the claim would take a read of the send where it ran: a route active now names the plugin and the account
    /// the send ran under (<see cref="RouteResolver.ForSend"/>), and that plugin performs the read. The send's channel only
    /// orders the walk, as at a take-back, so it is not asked for. Otherwise the claim would refuse it, so none is made.
    /// </summary>
    private bool Routed(HeldSend send) =>
        RouteResolver.ForSend(routes.Snapshot, send.Campaign, SendReads.Operation, send.Operation, sendChannel: null, send.PluginId, send.BindingIdentity) is { } found
        && string.Equals(found.Route.PluginId, send.PluginId, StringComparison.Ordinal)
        && string.Equals(found.Route.BindingIdentity, send.BindingIdentity, StringComparison.Ordinal)
        && ProviderOpPreflight.Routable(plugins.Snapshot, StatusContract, found.Route) is null;
}

/// <summary>Why the deferred read step queued no read of a deferred send, one code per reason.</summary>
public static class DeferredReadSkipCodes
{
    /// <summary>No route active now takes a read of the send where it ran, so the claim would refuse one.</summary>
    public const string NotRouted = "deferred_read_not_routed";
}

/// <summary>
/// What the deferred read step keeps in the life of this process: which campaigns it has said it queued no read for, and
/// why, and when it last read its candidates.
/// </summary>
public sealed class DeferredReadSkips
{
    private readonly ConcurrentDictionary<(int Campaign, string Code), bool> _said = new();

    private readonly Lock _gate = new();

    private DateTimeOffset? _read;

    /// <summary>True the first time this campaign is passed over for this reason, and never again.</summary>
    public bool FirstTime(int campaign, string code) => _said.TryAdd((campaign, code), true);

    /// <summary>
    /// True where the candidates were last read at least <see cref="DeferredReads.Cadence"/> before <paramref name="now"/>,
    /// or never, and then <paramref name="now"/> is when they were last read.
    /// </summary>
    public bool Due(DateTimeOffset now)
    {
        lock (_gate)
        {
            if (_read is { } last && now - last < DeferredReads.Cadence)
            {
                return false;
            }

            _read = now;
            return true;
        }
    }
}
