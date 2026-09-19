using Jason.Contracts.Api;
using Jason.Runtime.Configuration;
using Jason.Runtime.Persistence;
using Jason.Runtime.WorkItems;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Jason.Runtime.Dispatch;

/// <summary>
/// The third step of a scan: decide which campaigns want a manager looking at them, and put a check-in on the
/// queue for each. It runs before the claim so that a check-in created this tick can be claimed this tick, and
/// after the lease enforcer so that a review of a lost attempt is summoned by the line that lost it.
/// <para>
/// One campaign, one short transaction, and one campaign's trouble is its own. The chronicle and the lookups
/// are read before the transaction opens, so the write lock is held for an insert and one guarded update; and
/// anything that goes wrong for one campaign — a write that will not land, a race lost, a constraint nobody
/// expected — is caught here, because the step after this one hands out all the work there is.
/// </para>
/// </summary>
/// <remarks>
/// Nothing here reads meaning. It reads a kind, some identifiers, a status and two timestamps — the promotion
/// from "something happened" to "somebody should think about this" is a table lookup, and the thinking is the
/// manager's.
/// </remarks>
public sealed class Summoner(
    WorkItemService items,
    TimeProvider clock,
    LiveSettings<ManagerOptions> settings,
    ILogger<Summoner> logger)
{
    /// <summary>The statuses in which a check-in still counts as open, and therefore stops a second one.</summary>
    public static readonly WorkItemStatus[] Open =
    [
        WorkItemStatus.Created,
        WorkItemStatus.Scheduled,
        WorkItemStatus.Processing,
        WorkItemStatus.AwaitingApproval,
    ];

    private static readonly Action<ILogger, string, string, Exception?> Summoned = LoggerMessage.Define<string, string>(
        LogLevel.Information,
        new EventId(1, nameof(Summoned)),
        "Dispatcher summoned a manager for campaign {CampaignId}: {Intent}");

    private static readonly Action<ILogger, string, Exception?> Lost = LoggerMessage.Define<string>(
        LogLevel.Information,
        new EventId(2, nameof(Lost)),
        "A check-in for campaign {CampaignId} was already being created by somebody else");

    private static readonly Action<ILogger, string, Exception?> Failed = LoggerMessage.Define<string>(
        LogLevel.Warning,
        new EventId(3, nameof(Failed)),
        "The summon for campaign {CampaignId} failed; the rest of the scan continues without it");

    private static readonly Action<ILogger, string, Exception?> Moved = LoggerMessage.Define<string>(
        LogLevel.Information,
        new EventId(4, nameof(Moved)),
        "Campaign {CampaignId} was accounted for by another writer while this summon was reading it");

    public async Task<int> SummonAsync(JasonDbContext db, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(db);
        var options = settings.Current;
        var triggers = new HashSet<string>(options.Triggers, StringComparer.Ordinal);
        var now = clock.GetUtcNow().UtcDateTime;
        var summoned = 0;

        foreach (var campaign in await WaitingAsync(db, ct).ConfigureAwait(false))
        {
            // Per campaign, because the step that hands out work comes after this one. A campaign whose write
            // will not land must cost that campaign and not every campaign behind it — and not the claim, which
            // would be a dispatcher quietly ceasing to dispatch while still logging a tick.
            try
            {
                if (await SummonOneAsync(db, campaign.Id, triggers, options, now, ct).ConfigureAwait(false))
                {
                    summoned++;
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // A summon that failed leaves nothing else behind — the row is untouched, by design — so this
                // line is the whole of what somebody gets. It names the campaign the way every other line here
                // does, because the row number is the one identifier that cannot be typed into any command.
                Failed(logger, campaign.PublicId, ex);
                db.ChangeTracker.Clear();
            }
        }

        return summoned;
    }

    /// <summary>
    /// Active campaigns with no check-in open. A campaign already waiting for a review is skipped entirely —
    /// it consumes no chronicle either, so an event that arrives while a review is open is still above the
    /// watermark when that review ends and summons the next one.
    /// </summary>
    private static async Task<IReadOnlyList<Waiting>> WaitingAsync(JasonDbContext db, CancellationToken ct) =>
        await db.Campaigns
            .AsNoTracking()
            .Where(campaign => campaign.Status == CampaignStatus.Active
                && !db.WorkItems.Any(item => item.CampaignId == campaign.Id
                    && item.Role == ManagerCheckIn.Role
                    && item.CreatedByType == ActorType.System
                    && Open.Contains(item.Status)))
            .OrderBy(campaign => campaign.Id)
            .Select(campaign => new Waiting(campaign.Id, campaign.PublicId))
            .ToListAsync(ct)
            .ConfigureAwait(false);

    /// <summary>A campaign this scan will consider: the key the queries take, and the name a person reads.</summary>
    private sealed record Waiting(int Id, string PublicId);

    private async Task<bool> SummonOneAsync(
        JasonDbContext db,
        int campaignId,
        IReadOnlySet<string> triggers,
        ManagerOptions options,
        DateTime now,
        CancellationToken ct)
    {
        // Everything that only reads happens out here, where nothing is holding the write lock: the campaign,
        // up to five hundred lines of its chronicle, and the two lookups that say which of them are a
        // check-in's own.
        var read = await db.Campaigns
            .AsNoTracking()
            .Where(campaign => campaign.Id == campaignId)
            .Select(campaign => new
            {
                campaign.PublicId,
                campaign.Status,
                campaign.ManagerEventWatermark,
                campaign.ManagerReviewAnchor,
                campaign.ManagerReviewSeconds,
            })
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);

        if (read is null || read.Status != CampaignStatus.Active)
        {
            return false;
        }

        var before = read.ManagerEventWatermark;
        var lines = await db.Journal
            .AsNoTracking()
            .Where(entry => entry.CampaignId == campaignId && entry.Id > before)
            .OrderBy(entry => entry.Id)
            .Take(options.MaxEntriesPerScan)
            .Select(entry => new ChronicleLine(
                entry.Id,
                entry.PublicId,
                entry.Kind,
                entry.WorkItemId,
                entry.AttemptId,
                entry.ActorType,
                entry.ActorId))
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var ours = await OursAsync(db, lines, ct).ConfigureAwait(false);
        var decision = ManagerTriggers.Read(lines, triggers, ours, before);
        var due = decision.Cause is null
            && ReviewSchedule.IsDue(read.ManagerReviewAnchor, read.ManagerReviewSeconds, options.ReviewSeconds, now);

        if (decision.Cause is null && !due && decision.Watermark == before)
        {
            // Nothing read and nothing due: the common answer, and it takes no transaction at all.
            return false;
        }

        await using var transaction = await db.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
        try
        {
            var accounted = await AccountForAsync(db, campaignId, before, decision.Watermark, ct).ConfigureAwait(false);
            if (accounted == 0)
            {
                Moved(logger, read.PublicId, null);
                await transaction.RollbackAsync(ct).ConfigureAwait(false);
                return false;
            }

            if (decision.Cause is null && !due)
            {
                await transaction.CommitAsync(ct).ConfigureAwait(false);
                return false;
            }

            var campaign = await db.Campaigns.FirstAsync(c => c.Id == campaignId, ct).ConfigureAwait(false);
            await ManagerCheckIn.CreateAsync(items, db, campaign, decision.Cause, options, ct).ConfigureAwait(false);
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
            await transaction.CommitAsync(ct).ConfigureAwait(false);
        }
        catch (DbUpdateException ex)
        {
            await transaction.RollbackAsync(ct).ConfigureAwait(false);
            db.ChangeTracker.Clear();

            // The database is what decides that one check-in is open, not the look above it: two writers both
            // looking would both see none and both insert. Losing that race is ordinary — the review somebody
            // else created is the review — and the campaign is left exactly as it was, watermark included,
            // because the check-in that won will read those lines. Anything else is a failure and says so
            // rather than being filed under a race that did not happen.
            if (!await OpenCheckInAsync(db, campaignId, ct).ConfigureAwait(false))
            {
                Failed(logger, read.PublicId, ex);
                return false;
            }

            Lost(logger, read.PublicId, null);
            return false;
        }

        Summoned(logger, read.PublicId, decision.Cause is { } cause ? cause.Kind : "scheduled", null);
        return true;
    }

    /// <summary>
    /// Moves a campaign's watermark, and only from the value this summon read. A watermark another writer has
    /// moved on is refused rather than overwritten: that writer's read may already have become a review of
    /// exactly these lines, and putting an older number back would have them reviewed twice.
    /// </summary>
    /// <returns>1 when the watermark was this summon's to move, 0 when somebody else had already moved it.</returns>
    public static Task<int> AccountForAsync(JasonDbContext db, int campaignId, int before, int after, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(db);
        return db.Campaigns
            .Where(campaign => campaign.Id == campaignId && campaign.ManagerEventWatermark == before)
            .ExecuteUpdateAsync(set => set.SetProperty(campaign => campaign.ManagerEventWatermark, after), ct);
    }

    /// <summary>
    /// Whether this campaign has a check-in open — which is how a refused insert is told from a failed one: the
    /// database is what decides that one check-in is open, so an insert refused while one exists is the race
    /// being lost, and an insert refused while none does is something else entirely.
    /// </summary>
    public static Task<bool> OpenCheckInAsync(JasonDbContext db, int campaignId, CancellationToken ct) =>
        db.WorkItems
            .AsNoTracking()
            .AnyAsync(
                item => item.CampaignId == campaignId
                    && item.Role == ManagerCheckIn.Role
                    && item.CreatedByType == ActorType.System
                    && Open.Contains(item.Status),
                ct);

    /// <summary>
    /// Which of these lines are a check-in's own: about a check-in, about one's attempt, or written <em>by</em>
    /// one's attempt. Asked once per campaign for the whole read rather than once per line, and asked at all
    /// because a manager that could summon a manager would summon one for ever.
    /// </summary>
    /// <remarks>
    /// Three columns, not two, and the third is the one that bites. A failed check-in names itself in
    /// <c>work_item_id</c> and its run in <c>attempt_id</c>, which is easy to see. But a line a check-in's
    /// attempt <em>wrote</em> may name neither: the chronicle fills <c>attempt_id</c> only when an attempt
    /// object is handed to it, and a report — one of the default trigger kinds — passes none. It carries its
    /// reporter as the actor instead. So a manager that reported an effect it produced would summon the next
    /// manager, which would read the same chronicle, and the loop would run once a tick for ever on a line it
    /// wrote about itself.
    /// </remarks>
    private static async Task<Func<ChronicleLine, bool>> OursAsync(
        JasonDbContext db,
        IReadOnlyList<ChronicleLine> lines,
        CancellationToken ct)
    {
        var workItems = lines.Select(line => line.WorkItemId).OfType<string>().Distinct().ToList();

        // The attempt a line is about and the attempt that wrote it are different columns and either can name a
        // check-in's run, so both are asked about together.
        var attempts = lines
            .SelectMany(line => new[]
            {
                line.AttemptId,
                line.ActorType == ActorType.Attempt ? line.ActorId : null,
            })
            .OfType<string>()
            .Distinct()
            .ToList();

        if (workItems.Count == 0 && attempts.Count == 0)
        {
            return _ => false;
        }

        var checkIns = await db.WorkItems
            .AsNoTracking()
            .Where(item => item.Role == ManagerCheckIn.Role && item.CreatedByType == ActorType.System
                && workItems.Contains(item.PublicId))
            .Select(item => item.PublicId)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var theirAttempts = await db.Attempts
            .AsNoTracking()
            .Where(attempt => attempts.Contains(attempt.PublicId)
                && attempt.WorkItem!.Role == ManagerCheckIn.Role
                && attempt.WorkItem.CreatedByType == ActorType.System)
            .Select(attempt => attempt.PublicId)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var byItem = new HashSet<string>(checkIns, StringComparer.Ordinal);
        var byAttempt = new HashSet<string>(theirAttempts, StringComparer.Ordinal);
        return line => (line.WorkItemId is { } item && byItem.Contains(item))
            || (line.AttemptId is { } about && byAttempt.Contains(about))
            || (line.ActorType == ActorType.Attempt && line.ActorId is { } wrote && byAttempt.Contains(wrote));
    }
}
