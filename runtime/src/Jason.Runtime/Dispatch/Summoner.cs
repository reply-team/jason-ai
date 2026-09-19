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
/// One campaign, one short transaction. A campaign whose chronicle has two thousand lines waiting must not hold
/// the scan while they are counted, and a campaign whose insert loses a race must not cost the others their
/// turn.
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
    private static readonly WorkItemStatus[] Open =
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

    public async Task<int> SummonAsync(JasonDbContext db, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(db);
        var options = settings.Current;
        var triggers = new HashSet<string>(options.Triggers, StringComparer.Ordinal);
        var now = clock.GetUtcNow().UtcDateTime;
        var summoned = 0;

        foreach (var id in await WaitingAsync(db, ct).ConfigureAwait(false))
        {
            if (await SummonOneAsync(db, id, triggers, options, now, ct).ConfigureAwait(false))
            {
                summoned++;
            }
        }

        return summoned;
    }

    /// <summary>
    /// Active campaigns with no check-in open. A campaign already waiting for a review is skipped entirely —
    /// it consumes no chronicle either, so an event that arrives while a review is open is still above the
    /// watermark when that review ends and summons the next one.
    /// </summary>
    private static async Task<IReadOnlyList<int>> WaitingAsync(JasonDbContext db, CancellationToken ct) =>
        await db.Campaigns
            .AsNoTracking()
            .Where(campaign => campaign.Status == CampaignStatus.Active
                && !db.WorkItems.Any(item => item.CampaignId == campaign.Id
                    && item.Role == ManagerCheckIn.Role
                    && item.CreatedByType == ActorType.System
                    && Open.Contains(item.Status)))
            .OrderBy(campaign => campaign.Id)
            .Select(campaign => campaign.Id)
            .ToListAsync(ct)
            .ConfigureAwait(false);

    private async Task<bool> SummonOneAsync(
        JasonDbContext db,
        int campaignId,
        IReadOnlySet<string> triggers,
        ManagerOptions options,
        DateTime now,
        CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
        var campaign = await db.Campaigns.FirstOrDefaultAsync(c => c.Id == campaignId, ct).ConfigureAwait(false);
        if (campaign is null || campaign.Status != CampaignStatus.Active)
        {
            return false;
        }

        var entries = await db.Journal
            .AsNoTracking()
            .Where(entry => entry.CampaignId == campaignId && entry.Id > campaign.ManagerEventWatermark)
            .OrderBy(entry => entry.Id)
            .Take(options.MaxEntriesPerScan)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var ours = await OursAsync(db, entries, ct).ConfigureAwait(false);
        var read = ManagerTriggers.Read(entries, triggers, entry => ours(entry), campaign.ManagerEventWatermark);
        campaign.ManagerEventWatermark = read.Watermark;

        var summon = read.Cause is not null || ReviewSchedule.IsDue(campaign, options.ReviewSeconds, now);
        if (!summon)
        {
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
            await transaction.CommitAsync(ct).ConfigureAwait(false);
            return false;
        }

        try
        {
            await ManagerCheckIn.CreateAsync(items, db, campaign, read.Cause, options, ct).ConfigureAwait(false);
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
            await transaction.CommitAsync(ct).ConfigureAwait(false);
        }
        catch (DbUpdateException)
        {
            // The database is what decides that one check-in is open, not the look above it: two writers both
            // looking would both see none and both insert. Losing the race is ordinary, and the campaign is
            // left exactly as it was — watermark included, because the check-in that won will read those lines.
            Lost(logger, campaign.PublicId, null);
            await transaction.RollbackAsync(ct).ConfigureAwait(false);
            db.ChangeTracker.Clear();
            return false;
        }

        Summoned(logger, campaign.PublicId, read.Cause is { } cause ? cause.Kind : "scheduled", null);
        return true;
    }

    /// <summary>
    /// Which of these lines are a check-in's own — about a check-in, or written by one's attempt. Asked once
    /// per campaign for the whole read rather than once per line, and asked at all because a manager that could
    /// summon a manager would summon one for ever: a review whose host is missing fails at pre-flight like any
    /// other item, and that failure is a line of exactly the kind that summons a review.
    /// </summary>
    private static async Task<Func<JournalEntry, bool>> OursAsync(
        JasonDbContext db,
        IReadOnlyList<JournalEntry> entries,
        CancellationToken ct)
    {
        var workItems = entries.Select(entry => entry.WorkItemId).OfType<string>().Distinct().ToList();
        var attempts = entries.Select(entry => entry.AttemptId).OfType<string>().Distinct().ToList();
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
        return entry => (entry.WorkItemId is { } item && byItem.Contains(item))
            || (entry.AttemptId is { } attempt && byAttempt.Contains(attempt));
    }
}
