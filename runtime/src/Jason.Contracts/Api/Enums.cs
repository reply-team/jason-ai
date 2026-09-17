namespace Jason.Contracts.Api;

/// <summary>draft → active → paused → archived. Archived is terminal.</summary>
public enum CampaignStatus
{
    Draft,
    Active,
    Paused,
    Archived,
}

/// <summary>Where a contact stands inside one campaign. Removal is the soft state <c>excluded</c>, never a deletion.</summary>
public enum MembershipState
{
    Enrolled,
    Paused,
    Finished,
    Excluded,
}

/// <summary>
/// Who performs an operation. <c>System</c> belongs to the runtime's own writes and is refused from callers;
/// <c>Attempt</c> is a running executor, so work it creates stays traceable to the attempt that asked for it.
/// </summary>
public enum ActorType
{
    Human,
    Role,
    System,
    Attempt,
}

/// <summary>What a work item is: a role doing its job, or one provider operation performed through a plugin.</summary>
public enum WorkItemKind
{
    AiRole,
    ProviderOp,
}

/// <summary>
/// Which of the four levels decided where an operation's work goes, most specific first. A campaign default
/// hides every global operation override for that campaign: bringing one back takes an explicit campaign
/// operation override, because a campaign that named its provider must not still be sending one operation
/// somewhere else.
/// </summary>
public enum RouteScope
{
    CampaignOperation,
    CampaignDefault,
    GlobalOperation,
    GlobalDefault,
}

/// <summary>
/// created → scheduled → processing → succeeded | failed, with cancelled and expired as the two ways an item
/// ends without running. A retriable failure returns the item to created; an expired item can be reopened.
/// <para>
/// Work whose operation needs a person's approval waits in <c>awaiting_approval</c> instead of being claimed:
/// approving it returns it to created, rejecting it ends it as failed, and it can still be cancelled or expire
/// there like any other unclaimed work.
/// </para>
/// </summary>
public enum WorkItemStatus
{
    Created,
    AwaitingApproval,
    Scheduled,
    Processing,
    Succeeded,
    Failed,
    Cancelled,
    Expired,
}

/// <summary>
/// One decision about one work item. <c>superseded</c> is a decision the work outgrew — its input changed, or a
/// newer parking replaced it; <c>cancelled</c> is a decision nobody will ever need, because the work it was
/// about is gone.
/// </summary>
public enum ApprovalStatus
{
    Pending,
    Approved,
    Rejected,
    Superseded,
    Cancelled,
}

/// <summary>One run of one work item. <c>Interrupted</c> is a leftover of a runtime restart and is never counted.</summary>
public enum AttemptStatus
{
    Scheduled,
    Running,
    Succeeded,
    Failed,
    Interrupted,
    Cancelled,
}

/// <summary>How an executor says its attempt ended.</summary>
public enum CompletionStatus
{
    Succeeded,
    Failed,
}

/// <summary>What the dispatcher is doing: turned off, not started, scanning, or finishing what it has.</summary>
public enum DispatcherState
{
    Disabled,
    Stopped,
    Running,
    Draining,
}

/// <summary>Outcome of one item of <c>campaign.add_contacts</c>.</summary>
public enum AddContactsItemStatus
{
    Added,
    AlreadyMember,
    Rejected,
}

/// <summary>Outcome of one item of <c>campaign.remove_contacts</c>.</summary>
public enum RemoveContactsItemStatus
{
    Removed,
    NotMember,
    AlreadyExcluded,
    Rejected,
}
