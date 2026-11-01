using Jason.Contracts.Api;
using Jason.Contracts.Operations;
using Jason.Runtime.Journal;
using Jason.Runtime.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Jason.Runtime.Notifications;

/// <summary>
/// The lines that say a protective action failed: a <c>provider_op</c> item whose operation the catalogue runs in a
/// paused campaign — the ones that stop contact — ended failed or expired, in a campaign of any status. A paused
/// campaign's manager is never summoned, so a pointer is the one way such a failure reaches a person. One predicate,
/// read where the notifier wakes on its lines, where it folds them, and where the sender counts them, so the three
/// cannot come to disagree about what counts.
/// </summary>
public static class ProtectiveFailureLines
{
    /// <summary>The endings that count. A cancellation is somebody's decision, not a failure.</summary>
    public static IReadOnlyList<string> Kinds { get; } = [JournalKinds.WorkItemFailed, JournalKinds.WorkItemExpired];

    /// <summary>The protective operations: those the catalogue runs in a paused campaign.</summary>
    public static IReadOnlyList<string> Operations => OperationCatalog.RunsInPausedCampaign;

    /// <summary>Every such line in the journal, for a caller to bound by position.</summary>
    public static IQueryable<JournalEntry> Of(JasonDbContext db)
    {
        ArgumentNullException.ThrowIfNull(db);
        string[] kinds = [.. Kinds];
        string[] operations = [.. Operations];
        return db.Journal.AsNoTracking().Where(entry =>
            kinds.Contains(entry.Kind)
            && db.WorkItems.Any(item =>
                item.PublicId == entry.WorkItemId
                && item.Kind == WorkItemKind.ProviderOp
                && item.Operation != null
                && operations.Contains(item.Operation)));
    }
}
