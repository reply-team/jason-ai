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

/// <summary>
/// Where a question a person has to answer has got to. A cancelled one was about a campaign that can never
/// run again; nothing else retires a question, because it is meant to outlive the attempt that asked.
/// </summary>
public enum DecisionStatus
{
    Pending,
    Answered,
    Cancelled,
}

/// <summary>
/// What a decision's causal reference points at. A closed vocabulary of the entities that have a public
/// identifier and a campaign: a role note is deliberately not one of them, because a note is addressed by
/// campaign and role rather than by an id, and both are already in hand wherever a decision is read.
/// </summary>
public enum DecisionReferenceKind
{
    WorkItem,
    Attempt,
    JournalEntry,
    Report,
    Approval,
}

/// <summary>
/// Which launchable agent host an execution profile describes. A closed vocabulary on purpose: an unknown host
/// is refused when a profile is written, rather than discovered when an attempt is already running.
/// </summary>
public enum AgentHostKind
{
    ClaudeCode,
}

/// <summary>
/// Which level of the published order decided an attempt's execution profile — or, for
/// <see cref="RoleEntryCommand"/>, that no profile did and the role's own command ran it.
/// </summary>
public enum ProfileResolutionSource
{
    WorkItemOverride,
    CampaignPolicy,
    RolePolicy,
    Lineage,
    GlobalDefault,
    RoleEntryCommand,
}

/// <summary>
/// What a work item's materialized causal ancestry amounts to. <c>Root</c> is work nobody's run caused;
/// <c>Inherited</c> carries a profile forward from the run that caused it, across deterministic work as well as
/// agent work; <c>Unresolved</c> says a run caused this and nothing about its profile can be read — which
/// blocks rather than falling through to the global default.
/// </summary>
public enum LineageState
{
    Root,
    Inherited,
    Unresolved,
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

/// <summary>Whether a submitted report was admitted as a new one, or matched one already held.</summary>
public enum ReportDedupOutcome
{
    Admitted,
    Duplicate,
}

/// <summary>Which rule matched a duplicate: the reporter's own key, or the canonical form of what they said.</summary>
public enum ReportDedupMatch
{
    Key,
    Content,
}
