using System.Threading.Channels;
using Jason.Contracts.Api;
using Jason.Contracts.Plugins;
using Jason.Runtime.Configuration;
using Jason.Runtime.Domain;
using Jason.Runtime.Journal;
using Jason.Runtime.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Jason.Runtime.Notifications;

/// <summary>
/// Delivers one notification at a time, off the scan: a slow channel delays no scan, and a parked item, an approval or
/// a decision has committed long before anything here runs. Each channel is bounded by its own budget and they run side
/// by side; a transient failure is tried once more after thirty seconds — best effort, because a runtime that ends
/// meanwhile has written nothing and its successor sends again. One journal line records what happened, and that line
/// is also where the notifier's watermark lives.
/// </summary>
/// <remarks>
/// Three cases write no line: every channel off (the notifier never hands anything over), nothing waiting by the time
/// it is sent, and a runtime that stops while it sends. One case writes a line though nothing reached anybody: channels
/// exist and none of them could, which moves the watermark so the same lines are not said again.
/// </remarks>
public sealed class NotificationSender(
    IServiceScopeFactory scopes,
    INotificationChannels channels,
    NotifierState state,
    NotificationStatus status,
    LiveSettings<NotificationsOptions> settings,
    TimeProvider clock,
    ILogger<NotificationSender> logger) : BackgroundService, INotificationQueue
{
    /// <summary>How long a channel that failed transiently waits before its one more try.</summary>
    public static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(30);

    /// <summary>What a channel that threw is recorded as: a defect in that channel, whose effect nobody can know.</summary>
    public const string ChannelFault = "notification_channel_fault";

    private static readonly Action<ILogger, Exception?> Off = LoggerMessage.Define(
        LogLevel.Information,
        new EventId(1, nameof(Off)),
        "Notifications are off by configuration; nothing will be sent");

    /// <summary>
    /// The message template of the line a failed channel writes. The log file is compact JSON, so this is what its
    /// <c>"@mt"</c> holds, with <c>Channel</c>, <c>Code</c> and <c>Class</c> beside it; the page shows it in that form.
    /// </summary>
    public const string ChannelFailedTemplate = "Notification channel {Channel} failed ({Code}, {Class})";

    /// <summary>The event that line carries as <c>"EventId"</c>, beside the logger's name as <c>"SourceContext"</c>.</summary>
    public static readonly EventId ChannelFailedEvent = new(2, nameof(ChannelFailed));

    private static readonly Action<ILogger, string, string, string, Exception?> ChannelFailed = LoggerMessage.Define<string, string, string>(
        LogLevel.Warning,
        ChannelFailedEvent,
        ChannelFailedTemplate);

    private static readonly Action<ILogger, string, Exception?> SendFailed = LoggerMessage.Define<string>(
        LogLevel.Warning,
        new EventId(3, nameof(SendFailed)),
        "Notification {NotificationId} could not be sent; the next one will say what waits");

    private readonly Channel<NotificationRequest> _queue = Channel.CreateBounded<NotificationRequest>(1);

    /// <exception cref="InvalidOperationException">A notification is already waiting to be sent.</exception>
    public void Enqueue(NotificationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!_queue.Writer.TryWrite(request))
        {
            throw new InvalidOperationException("A notification is already waiting to be sent.");
        }
    }

    public override Task StartAsync(CancellationToken cancellationToken)
    {
        if (!settings.Current.AnyChannel)
        {
            Off(logger, null);
        }

        return base.StartAsync(cancellationToken);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await foreach (var request in _queue.Reader.ReadAllAsync(stoppingToken).ConfigureAwait(false))
            {
                var progress = new SendProgress();
                try
                {
                    await SendAsync(request, stoppingToken, progress).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    // A stopping runtime writes no line: the next start reads these triggers again and sends once.
                    return;
                }
                catch (Exception ex)
                {
                    SendFailed(logger, request.NotificationId, ex);

                    // Nobody was told, and nothing recorded it: the burst goes back, and the next scan hands it over
                    // again. Where somebody was told, the floor was already kept, and the pointer is not repeated.
                    if (!progress.Reached)
                    {
                        state.Return(request);
                    }
                }
                finally
                {
                    state.Release(request.NotificationId);
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Stopped while waiting for the next one: nothing was being sent.
        }
    }

    /// <summary>Sends one notification and writes its line; null when nothing was waiting by then, and nothing was said.</summary>
    internal Task<NotificationLine?> SendAsync(NotificationRequest request, CancellationToken ct) => SendAsync(request, ct, new SendProgress());

    /// <summary>How far one send got: whether any channel reached anybody, which decides what a failure afterwards owes.</summary>
    internal sealed class SendProgress
    {
        public bool Reached { get; set; }
    }

    private async Task<NotificationLine?> SendAsync(NotificationRequest request, CancellationToken ct, SendProgress progress)
    {
        ArgumentNullException.ThrowIfNull(request);

        // Every channel turned off since the hand-over: nothing is said and no line is written, and the burst goes back
        // to be said once a channel is on again.
        if (!settings.Current.AnyChannel)
        {
            state.Return(request);
            return null;
        }

        var (approvals, decisions, item) = await PendingAsync(ct).ConfigureAwait(false);
        var failedWork = await FailedAsync(request, ct).ConfigureAwait(false);
        var gaps = await GapsAsync(request, ct).ConfigureAwait(false);
        var blocks = await BlocksAsync(request, ct).ConfigureAwait(false);
        if (approvals == 0 && decisions == 0 && failedWork.Count == 0 && gaps == 0 && blocks.Blocks == 0)
        {
            status.NothingWaited(clock.GetUtcNow());
            return null;
        }

        var message = NotificationComposer.Compose(approvals, decisions, failedWork.Count > 0 ? failedWork : null, gaps, blocks.Blocks > 0 ? blocks : null, item);
        var outcomes = await Task.WhenAll(channels.Resolve(settings.Current).Select(channel => DeliverAsync(channel, message, request.NotificationId, ct))).ConfigureAwait(false);
        var now = clock.GetUtcNow();

        // Kept before the line is written: somebody was told, and a line that then fails to write must not let the next
        // pointer follow at once.
        var reached = outcomes.Any(outcome => outcome.Result == ChannelResult.Delivered);
        if (reached)
        {
            progress.Reached = true;
            state.Delivered(now.UtcDateTime, request.CoveredThrough);
        }

        // Somebody was told what failed through the line this covers, so the next count starts after it; nobody was, so
        // the window stays where it was and the next pointer counts these failures again with any new ones.
        var line = new NotificationLine(
            request.NotificationId,
            approvals,
            decisions,
            request.CoveredThrough,
            outcomes,
            reached ? now.UtcDateTime : request.LastDeliveredAt,
            failedWork.Count,
            reached ? request.CoveredThrough : request.DeliveredThrough,
            gaps,
            blocks.Blocks);

        await using (var scope = scopes.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<JasonDbContext>();
            scope.ServiceProvider.GetRequiredService<JournalWriter>()
                .Append(db, Actors.Dispatcher, JournalKinds.NotificationSent, campaign: null, key: "notification", updated: line.ToJson());
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
        }

        // The line carries the last delivery forward, so the state holds it whichever process last delivered.
        if (line.LastDeliveredAt is { } delivered)
        {
            state.Delivered(delivered, line.DeliveredThrough);
        }

        status.Record(line, now);
        foreach (var failed in outcomes.Where(outcome => outcome.Result == ChannelResult.Failed))
        {
            ChannelFailed(logger, failed.Channel, failed.Code ?? ChannelFault, Word(failed.Class), null);
        }

        return line;
    }

    /// <summary>
    /// Protective work that failed in the window this notification closes: after the line the last one that reached
    /// anybody covered, through the line this one covers. Counted by item, campaigns in the order they first failed.
    /// </summary>
    private async Task<FailedProtectiveWork> FailedAsync(NotificationRequest request, CancellationToken ct)
    {
        var none = new FailedProtectiveWork(0, [], 0);
        if (request.CoveredThrough is null)
        {
            return none;
        }

        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<JasonDbContext>();
        var upTo = await IdOfAsync(db, request.CoveredThrough, ct).ConfigureAwait(false);
        int? after = request.DeliveredThrough is null ? 0 : await IdOfAsync(db, request.DeliveredThrough, ct).ConfigureAwait(false);
        if (upTo is null || after is null)
        {
            // A window whose ends are not in the journal counts nothing rather than everything.
            return none;
        }

        var failed = await ProtectiveFailureLines.Of(db)
            .Where(e => e.Id > after && e.Id <= upTo)
            .OrderBy(e => e.Id)
            .Select(e => new { e.WorkItemId, Campaign = e.Campaign!.PublicId })
            .ToListAsync(ct)
            .ConfigureAwait(false);
        var campaigns = failed.Select(f => f.Campaign).Distinct(StringComparer.Ordinal).ToList();
        return new FailedProtectiveWork(
            failed.Select(f => f.WorkItemId).Distinct(StringComparer.Ordinal).Count(),
            [.. campaigns.Take(NotificationComposer.MaxCampaignsNamed)],
            Math.Max(0, campaigns.Count - NotificationComposer.MaxCampaignsNamed));
    }

    /// <summary>
    /// The accounts with a span of events nobody will read, said in the same window as failed protective work: after the
    /// line the last notification that reached anybody covered, through the line this one covers. Counted by account.
    /// </summary>
    private async Task<int> GapsAsync(NotificationRequest request, CancellationToken ct)
    {
        if (request.CoveredThrough is null)
        {
            return 0;
        }

        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<JasonDbContext>();
        var upTo = await IdOfAsync(db, request.CoveredThrough, ct).ConfigureAwait(false);
        int? after = request.DeliveredThrough is null ? 0 : await IdOfAsync(db, request.DeliveredThrough, ct).ConfigureAwait(false);
        if (upTo is null || after is null)
        {
            return 0;
        }

        var gaps = await EventGapLines.Of(db).Where(e => e.Id > after && e.Id <= upTo).Select(e => e.New).ToListAsync(ct).ConfigureAwait(false);
        return gaps.Select(EventGapLines.Source).Distinct(StringComparer.Ordinal).Count();
    }

    /// <summary>
    /// The provider blocks opened in the same window — said first after the line the last notification that reached
    /// anybody covered, through the line this one covers — and still open as this one is sent, counted by block, with the
    /// work items their open waits hold now. A block cleared before the send is not said: nothing waits on it any more.
    /// </summary>
    private async Task<HeldByBlocks> BlocksAsync(NotificationRequest request, CancellationToken ct)
    {
        var none = new HeldByBlocks(0, 0);
        if (request.CoveredThrough is null)
        {
            return none;
        }

        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<JasonDbContext>();
        var upTo = await IdOfAsync(db, request.CoveredThrough, ct).ConfigureAwait(false);
        int? after = request.DeliveredThrough is null ? 0 : await IdOfAsync(db, request.DeliveredThrough, ct).ConfigureAwait(false);
        if (upTo is null || after is null)
        {
            return none;
        }

        var said = await ProviderBlockLines.Of(db)
            .Where(e => e.Id > after && e.Id <= upTo && e.Key != null)
            .Select(e => e.Key!)
            .Distinct()
            .ToListAsync(ct)
            .ConfigureAwait(false);
        if (said.Count == 0)
        {
            return none;
        }

        // A block a campaign was told about before the window opened was said then, whichever campaign it reaches now.
        var earlier = await ProviderBlockLines.Of(db)
            .Where(e => e.Id <= after && said.Contains(e.Key!))
            .Select(e => e.Key!)
            .Distinct()
            .ToListAsync(ct)
            .ConfigureAwait(false);
        var opened = said.Except(earlier, StringComparer.Ordinal).ToList();
        var open = await db.ProviderBlocks.AsNoTracking()
            .Where(block => opened.Contains(block.PublicId) && block.Status == ProviderBlockStatus.Open)
            .Select(block => block.Id)
            .ToListAsync(ct)
            .ConfigureAwait(false);
        if (open.Count == 0)
        {
            return none;
        }

        var held = await db.WorkItemWaits.AsNoTracking()
            .CountAsync(wait => wait.Status == WaitStatus.Open && wait.ProviderBlockId != null && open.Contains(wait.ProviderBlockId.Value), ct)
            .ConfigureAwait(false);
        return new HeldByBlocks(open.Count, held);
    }

    private static Task<int?> IdOfAsync(JasonDbContext db, string publicId, CancellationToken ct) =>
        db.Journal.AsNoTracking().Where(e => e.PublicId == publicId).Select(e => (int?)e.Id).FirstOrDefaultAsync(ct);

    /// <summary>
    /// How many approvals and questions wait, and, when exactly one does, its public id: the one thing a click can open.
    /// The id is read after the counts, so a race that answers it in between reads none and the click opens the list.
    /// </summary>
    private async Task<(int Approvals, int Decisions, string? Item)> PendingAsync(CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<JasonDbContext>();
        var approvals = await db.Approvals.AsNoTracking().CountAsync(a => a.Status == ApprovalStatus.Pending, ct).ConfigureAwait(false);
        var decisions = await db.Decisions.AsNoTracking().CountAsync(d => d.Status == DecisionStatus.Pending, ct).ConfigureAwait(false);
        var item = (approvals, decisions) switch
        {
            (1, 0) => await db.Approvals.AsNoTracking().Where(a => a.Status == ApprovalStatus.Pending).Select(a => a.PublicId).FirstOrDefaultAsync(ct).ConfigureAwait(false),
            (0, 1) => await db.Decisions.AsNoTracking().Where(d => d.Status == DecisionStatus.Pending).Select(d => d.PublicId).FirstOrDefaultAsync(ct).ConfigureAwait(false),
            _ => null,
        };
        return (approvals, decisions, item);
    }

    private async Task<ChannelOutcome> DeliverAsync(INotificationChannel channel, NotificationMessage message, string notificationId, CancellationToken ct)
    {
        var first = await AttemptAsync(channel, message, notificationId, ct).ConfigureAwait(false);
        if (first is not { Result: ChannelResult.Failed, Class: FailureClass.Transient })
        {
            return first;
        }

        await Task.Delay(RetryDelay, clock, ct).ConfigureAwait(false);
        return (await AttemptAsync(channel, message, notificationId, ct).ConfigureAwait(false)) with { Attempts = 2 };
    }

    /// <summary>One try. A channel that throws is a defect in that channel, and cannot take the others' outcomes down.</summary>
    private static async Task<ChannelOutcome> AttemptAsync(INotificationChannel channel, NotificationMessage message, string notificationId, CancellationToken ct)
    {
        try
        {
            return await channel.DeliverAsync(message, notificationId, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            return ChannelOutcome.Failed(channel.Name, channel.Kind, ChannelFault, FailureClass.Ambiguous, ex.GetType().Name);
        }
    }

    private static string Word(FailureClass? failure) => failure?.ToString().ToLowerInvariant() ?? "unknown";
}
