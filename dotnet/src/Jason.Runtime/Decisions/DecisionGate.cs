using System.Text.Json.Nodes;
using Jason.Contracts.Api;
using Jason.Runtime.Journal;
using Jason.Runtime.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Jason.Runtime.Decisions;

/// <summary>
/// Retiring questions that can no longer be answered usefully, from wherever the thing they were about ends.
/// A static beside the writer rather than a service of its own, the way <c>ApprovalGate</c> is: the caller
/// already holds the transaction, and what is added here has to commit with it or not at all.
/// </summary>
public static class DecisionGate
{
    /// <summary>
    /// Every open question about a campaign that can never run again. A live question about work that can
    /// never proceed is the one row that would make <c>decision list</c> untrue — the same reason an archived
    /// campaign's queued work is cancelled with it.
    /// </summary>
    /// <remarks>
    /// No clock is taken. A cancelled question records no time of its own — the row keeps <c>raised_at</c> and
    /// <c>answered_at</c> and nothing else — and the chronicle line the cancellation writes is stamped by the
    /// writer that appends it. A parameter nothing can use is a parameter every caller has to guess about.
    /// <para>
    /// Nothing is saved here. The rows and their chronicle lines join the caller's change set, so archiving a
    /// campaign and retiring its questions is one commit.
    /// </para>
    /// </remarks>
    public static async Task<int> CancelPendingAsync(
        JasonDbContext db,
        JournalWriter journal,
        Campaign campaign,
        ActorRef actor,
        string? reason,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(journal);
        ArgumentNullException.ThrowIfNull(campaign);

        var pending = await db.Decisions
            .Include(decision => decision.WorkItem)
            .Include(decision => decision.Attempt)
            .Where(decision => decision.CampaignId == campaign.Id && decision.Status == DecisionStatus.Pending)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        foreach (var decision in pending)
        {
            decision.Status = DecisionStatus.Cancelled;
            journal.Append(
                db,
                actor,
                JournalKinds.DecisionCancelled,
                campaign,
                key: "decision",
                updated: new JsonObject { ["decision_id"] = decision.PublicId },
                reason: reason,
                workItem: decision.WorkItem,
                attempt: decision.Attempt);
        }

        return pending.Count;
    }
}
