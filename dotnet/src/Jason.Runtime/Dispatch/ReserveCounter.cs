using System.Text.Json.Nodes;
using Jason.Contracts.Api;
using Jason.Runtime.Campaigns;
using Jason.Runtime.Domain;
using Jason.Runtime.Journal;
using Jason.Runtime.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Jason.Runtime.Dispatch;

/// <summary>
/// The sixth step of a scan: for each live campaign that set an <c>audience_threshold</c>, count its untouched members
/// when something that can change the number was written since the last look, and say once per crossing that the reserve
/// fell under the threshold or is back at or above it. It runs after the respond step and before the summon, so a line
/// saying the reserve is low summons the manager's review in the same scan.
/// <para>
/// The journal alone decides: the latest of a campaign's <c>audience_reserve_low</c> and <c>audience_reserve_recovered</c>
/// lines is where its reserve stands, so a restart reads the same lines and says nothing twice, and nothing is kept here
/// between scans. One campaign, one short transaction, and one campaign's trouble is its own, as in the summon.
/// </para>
/// </summary>
/// <remarks>
/// Nothing here reads meaning: a line's kind and identifiers, the campaign's two numbers, and a count. Whether the
/// audience wants topping up, and how, is the manager's; this step creates no work item and no search.
/// </remarks>
public sealed class ReserveCounter(JournalWriter journal, ILogger<ReserveCounter> logger)
{
    public const int MinimumThreshold = 1;

    public const int MaximumThreshold = 100_000;

    /// <summary>
    /// The kinds that can move a campaign's untouched count: a member added or removed, a page of a search's people landed,
    /// work created about somebody, and the campaign's own fields changed, its threshold among them. Kinds only: a rename or work the runtime made costs one
    /// recount that finds nothing new, and the dispatcher keeps reading nothing but a kind.
    /// </summary>
    public static readonly string[] ChangeKinds =
    [
        JournalKinds.ContactsAdded,
        JournalKinds.ContactsRemoved,
        JournalKinds.AudiencePeopleLanded,
        JournalKinds.WorkItemCreated,
        JournalKinds.CampaignUpdated,
    ];

    private static readonly Action<ILogger, string, string, Exception?> Said = LoggerMessage.Define<string, string>(
        LogLevel.Information,
        new EventId(1, nameof(Said)),
        "Dispatcher said the reserve of campaign {CampaignId} is {Kind}");

    private static readonly Action<ILogger, string, Exception?> Moved = LoggerMessage.Define<string>(
        LogLevel.Information,
        new EventId(2, nameof(Moved)),
        "Campaign {CampaignId} was accounted for by another writer while this reserve count was reading it");

    private static readonly Action<ILogger, string, Exception?> Failed = LoggerMessage.Define<string>(
        LogLevel.Warning,
        new EventId(3, nameof(Failed)),
        "The reserve count for campaign {CampaignId} failed; the rest of the scan continues without it");

    /// <returns>How many lines the step wrote.</returns>
    public async Task<int> CountAsync(JasonDbContext db, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(db);
        var candidates = await db.Campaigns
            .AsNoTracking()
            .Where(campaign => campaign.Status == CampaignStatus.Active && campaign.AudienceThreshold != null)
            .OrderBy(campaign => campaign.Id)
            .Select(campaign => new Candidate(campaign.Id, campaign.PublicId, campaign.AudienceThreshold!.Value, campaign.AudienceWatermark))
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var said = 0;
        foreach (var candidate in candidates)
        {
            // Per campaign, because the summon and the claim come after this step: one campaign whose write will not land
            // must not cost the others, nor the scan.
            try
            {
                if (await CountOneAsync(db, candidate, ct).ConfigureAwait(false))
                {
                    said++;
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                Failed(logger, candidate.PublicId, ex);
                db.ChangeTracker.Clear();
            }
        }

        return said;
    }

    private async Task<bool> CountOneAsync(JasonDbContext db, Candidate candidate, CancellationToken ct)
    {
        // Everything that only reads happens out here, where nothing holds the write lock.
        var top = await db.Journal
            .AsNoTracking()
            .Where(entry => entry.CampaignId == candidate.Id)
            .MaxAsync(entry => (int?)entry.Id, ct)
            .ConfigureAwait(false) ?? 0;

        if (top <= candidate.Watermark)
        {
            // Nothing written since the last look: the common answer, and it takes no transaction at all.
            return false;
        }

        var changed = await db.Journal
            .AsNoTracking()
            .AnyAsync(
                entry => entry.CampaignId == candidate.Id
                    && entry.Id > candidate.Watermark
                    && entry.Id <= top
                    && ChangeKinds.Contains(entry.Kind),
                ct)
            .ConfigureAwait(false);

        string? kind = null;
        var untouched = 0;
        if (changed)
        {
            untouched = await CampaignPeople.UntouchedAsync(db, candidate.Id, ct).ConfigureAwait(false);
            var low = await LatestAsync(db, candidate.Id, JournalKinds.AudienceReserveLow, ct).ConfigureAwait(false);
            var recovered = await LatestAsync(db, candidate.Id, JournalKinds.AudienceReserveRecovered, ct).ConfigureAwait(false);
            var isLow = low > recovered;
            kind = untouched < candidate.Threshold && !isLow ? JournalKinds.AudienceReserveLow
                : untouched >= candidate.Threshold && isLow ? JournalKinds.AudienceReserveRecovered
                : null;
        }

        await using var transaction = await db.Database.BeginTransactionAsync(ct).ConfigureAwait(false);

        // The watermark moves at every look that found new lines, a change among them or not, so the next look reads only
        // what came after; and only from the value read, so a look another writer already accounted for is not repeated.
        var moved = await db.Campaigns
            .Where(campaign => campaign.Id == candidate.Id && campaign.AudienceWatermark == candidate.Watermark)
            .ExecuteUpdateAsync(set => set.SetProperty(campaign => campaign.AudienceWatermark, top), ct)
            .ConfigureAwait(false);
        if (moved == 0)
        {
            Moved(logger, candidate.PublicId, null);
            await transaction.RollbackAsync(ct).ConfigureAwait(false);
            return false;
        }

        if (kind is null)
        {
            await transaction.CommitAsync(ct).ConfigureAwait(false);
            return false;
        }

        var campaign = await db.Campaigns.FirstAsync(row => row.Id == candidate.Id, ct).ConfigureAwait(false);
        journal.Append(
            db,
            Actors.Dispatcher,
            kind,
            campaign,
            updated: new JsonObject { ["untouched"] = untouched, ["threshold"] = candidate.Threshold });
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        await transaction.CommitAsync(ct).ConfigureAwait(false);
        Said(logger, candidate.PublicId, kind, null);
        return true;
    }

    /// <summary>
    /// The id of the campaign's latest line of one reserve kind, or 0 when it has none: ids order the two kinds. The kind is
    /// written into the SQL, not bound: SQLite reads a filtered index only for a query whose own text says its filter.
    /// </summary>
    private static async Task<int> LatestAsync(JasonDbContext db, int campaignId, string kind, CancellationToken ct) =>
        await db.Journal
            .AsNoTracking()
            .Where(entry => entry.CampaignId == campaignId && entry.Kind == EF.Constant(kind))
            .MaxAsync(entry => (int?)entry.Id, ct)
            .ConfigureAwait(false) ?? 0;

    /// <summary>A campaign this scan will look at: its key, the name a person reads, and its two numbers as read.</summary>
    private sealed record Candidate(int Id, string PublicId, int Threshold, int Watermark);
}
