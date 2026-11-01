using Jason.Contracts.Api;
using Jason.Contracts.Ids;
using Jason.Runtime.Persistence;

namespace Jason.Runtime.Tests.Dispatch;

/// <summary>
/// The rows a provider block leaves: the block, opened by an attempt of an item that met it, and the waits of the items it
/// holds. Made-up plugins and bindings; the binding identity is only ever compared, never read.
/// </summary>
internal static class BlockRows
{
    public const string Plugin = "fake-provider";

    public const string Binding = "sha256:0a0a0a0a0a0a0a0a0a0a0a0a0a0a0a0a0a0a0a0a0a0a0a0a0a0a0a0a0a0a0a0a";

    /// <summary>A block, opened by a new attempt of <paramref name="opener"/>, its trial due at <paramref name="nextTrialAt"/>.</summary>
    public static ProviderBlock Block(
        WorkItem opener,
        DateTime now,
        DateTime nextTrialAt,
        ProviderBlockScope scope = ProviderBlockScope.Account,
        string? sender = null,
        string binding = Binding,
        string reason = "billing")
    {
        ArgumentNullException.ThrowIfNull(opener);
        var attempt = WorkItemFactory.NewAttempt(opener, opener.Attempts.Count + 1, AttemptStatus.Failed, now);
        attempt.FinishedAt = now;
        return new ProviderBlock
        {
            PublicId = PublicId.New("blk"),
            PluginId = Plugin,
            BindingIdentity = binding,
            Scope = scope,
            SenderExternalId = sender,
            Reason = reason,
            OpenedAt = now,
            OpenedByAttempt = attempt,
            LastSeenAt = now,
            NextTrialAt = nextTrialAt,
        };
    }

    /// <summary>
    /// A provider item of <paramref name="operation"/> parked on <paramref name="block"/> since <paramref name="now"/>, added
    /// to <paramref name="db"/> with its wait.
    /// </summary>
    public static WorkItem Parked(JasonDbContext db, Campaign campaign, ProviderBlock block, DateTime now, string operation = "message.send", int priority = 0)
    {
        ArgumentNullException.ThrowIfNull(db);
        var item = WorkItemFactory.NewProviderOp(campaign, operation, now);
        item.Status = WorkItemStatus.Waiting;
        item.Priority = priority;
        db.WorkItems.Add(item);
        db.WorkItemWaits.Add(new WorkItemWait { WorkItem = item, ProviderBlock = block, OpenedAt = now });
        return item;
    }
}
