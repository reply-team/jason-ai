using Jason.Contracts.Api;
using Jason.Contracts.Ids;
using Jason.Runtime.Persistence;

namespace Jason.Runtime.Tests;

/// <summary>
/// Entities a test can seed in one line. Nothing here is added to a context: the test decides what belongs in
/// which database, and the graph is wired up so the in-memory helpers (a live attempt, eligibility) work too.
/// </summary>
public static class WorkItemFactory
{
    private static readonly DateTime Default = new(2026, 9, 14, 12, 0, 0, DateTimeKind.Utc);

    public static Campaign NewCampaign(string name = "Flow", CampaignStatus status = CampaignStatus.Active, DateTime? now = null)
    {
        var moment = now ?? Default;
        return new Campaign
        {
            PublicId = PublicId.New("cmp"),
            Name = name,
            Status = status,
            CreatedAt = moment,
            UpdatedAt = moment,
        };
    }

    public static WorkItem NewAiRole(Campaign campaign, string role = "researcher", DateTime? now = null, Action<WorkItem>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(campaign);
        var moment = now ?? Default;
        var item = new WorkItem
        {
            PublicId = PublicId.New("wi"),
            Campaign = campaign,
            Kind = WorkItemKind.AiRole,
            Role = role,
            CreatedByType = ActorType.Human,
            CreatedAt = moment,
            UpdatedAt = moment,
        };
        configure?.Invoke(item);
        return item;
    }

    public static WorkItem NewProviderOp(Campaign campaign, string operation = "contacts.enroll", DateTime? now = null)
    {
        ArgumentNullException.ThrowIfNull(campaign);
        var moment = now ?? Default;
        return new WorkItem
        {
            PublicId = PublicId.New("wi"),
            Campaign = campaign,
            Kind = WorkItemKind.ProviderOp,
            Operation = operation,
            CreatedByType = ActorType.Human,
            CreatedAt = moment,
            UpdatedAt = moment,
        };
    }

    public static Attempt NewAttempt(WorkItem item, int number, AttemptStatus status, DateTime now, int timeoutSeconds = 3600)
    {
        ArgumentNullException.ThrowIfNull(item);
        var attempt = new Attempt
        {
            PublicId = PublicId.New("att"),
            WorkItem = item,
            Number = number,
            Command = item.Kind,
            Status = status,
            ExecutionProfile = item.ExecutionProfile,
            ContextSnapshot = item.Context.DeepClone().AsObject(),
            ClaimedAt = now,
            StartedAt = status == AttemptStatus.Running ? now : null,
            LockUntil = now.AddSeconds(timeoutSeconds),
        };
        item.Attempts.Add(attempt);
        return attempt;
    }
}
