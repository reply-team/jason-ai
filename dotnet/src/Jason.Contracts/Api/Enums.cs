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
/// <c>Attempt</c> is a running executor, so work it creates stays traceable to the attempt that asked for it;
/// <c>Plugin</c> is a plugin's handler calling through <c>host.call</c>, accepted only on a request that carries the
/// capability the runtime minted for that plugin's invocation, and refused from every other caller as <c>System</c> is.
/// </summary>
public enum ActorType
{
    Human,
    Role,
    System,
    Attempt,
    Plugin,
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
    Codex,
    CopilotCli,
    CursorCli,
    GeminiCli,
    AntigravityCli,
}

/// <summary>
/// A tool a person grants by name to the agents a profile launches, beyond what the runtime composes for every attempt.
/// A closed vocabulary on purpose, like the hosts: the grant is the person's word, and each host turns it into its own
/// rules, so a word no host knows how to turn into a rule is refused when the profile is written.
/// </summary>
public enum AgentTool
{
    WebSearch,
    WebFetch,
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
/// How a work item listing is ordered, each in the one direction its question needs: <c>created</c> oldest first (the
/// order a listing always had), <c>due</c> soonest first with undated work last, <c>finished</c> newest first with
/// unfinished work left out. The window a listing takes measures the same time.
/// </summary>
public enum WorkItemListOrder
{
    Created,
    Due,
    Finished,
}

/// <summary>
/// How a contact listing is ordered: <c>created</c> oldest first (the order a listing always had), <c>activity</c> by the
/// person's newest journal line, newest first, people with no line of their own last.
/// </summary>
public enum ContactListOrder
{
    Created,
    Activity,
}

/// <summary>
/// Where a campaign's people stand now, one count each. A person may be in several at once, and the counts need not add
/// up to the campaign's people: <c>docs/work-execution.md</c> defines each one.
/// </summary>
public enum CampaignSummaryFilter
{
    Waiting,
    Failed,
    Working,
    New,
    Idle,
    Sequence,
    Out,
    Removed,
}

/// <summary>
/// What happened to a campaign's people in a period, one count each, removed people included: a person may be in
/// several. <c>docs/work-execution.md</c> defines each one.
/// </summary>
public enum CampaignActivityFilter
{
    People,
    Added,
    WorkedOn,
    Contacted,
    Replied,
    OptedOut,
    Removed,
}

/// <summary>The unit <c>campaign.activity_by_period</c> cuts a window into: the hours, days or Monday-to-Sunday weeks of a zone.</summary>
public enum ActivityPeriod
{
    Hour,
    Day,
    Week,
}

/// <summary>
/// Which of the six levels decided where an operation's work goes, most specific first: at each scope, the route
/// a channel has under the operation, the operation's route, then the default. A channel level answers only a call
/// that names a channel, under an operation whose document names the argument the channel is read from. Any answer at
/// campaign scope hides every global route for that campaign, the channel routes included: bringing one back takes an
/// explicit campaign route, because a campaign that named its provider must not still be sending one operation, or one
/// channel of it, somewhere else.
/// </summary>
public enum RouteScope
{
    CampaignChannel,
    CampaignOperation,
    CampaignDefault,
    GlobalChannel,
    GlobalOperation,
    GlobalDefault,
}

/// <summary>
/// Which half of routing a <c>route.set</c> or <c>route.unset</c> writes: one campaign's routes, kept in the database, or
/// the global routes every campaign without a route of its own falls back to, kept in the settings file. Left out, it is
/// the campaign's: a request that names no scope and no campaign is refused for the missing campaign, so a caller that
/// forgets the campaign never re-routes every campaign.
/// </summary>
public enum RouteWriteScope
{
    Campaign,
    Global,
}

/// <summary>
/// created → scheduled → processing → succeeded | failed, with cancelled and expired as the two ways an item
/// ends without running. A retriable failure returns the item to created; an expired item can be reopened.
/// <para>
/// Work whose operation needs a person's approval waits in <c>awaiting_approval</c> instead of being claimed:
/// approving it returns it to created, rejecting it ends it as failed, and it can still be cancelled or expire
/// there like any other unclaimed work.
/// </para>
/// <para>
/// Work whose attempt ended waiting for other work items, or for a moment, is <c>waiting</c>: it holds no slot and no
/// live attempt, and the dispatcher itself returns it to created when what it waits for has happened. It can be
/// cancelled or expire there like any other unclaimed work.
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
    Waiting,
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

/// <summary>
/// One run of one work item. <c>Interrupted</c> is a leftover of a runtime restart and is never counted. <c>Yielded</c>
/// is a run that ended waiting: it is over, it is not counted against the item's attempts, and the item's next run is
/// a new attempt. <c>Blocked</c> is a run the provider refused for a reason outside the work — money, a reached limit, a
/// revoked credential, a broken channel — and did nothing for: it is over, it keeps the error and the block it met, it
/// is not counted either, and the item waits on that block for its next run.
/// </summary>
public enum AttemptStatus
{
    Scheduled,
    Running,
    Succeeded,
    Failed,
    Interrupted,
    Cancelled,
    Yielded,
    Blocked,
}

/// <summary>
/// What a wait on work items asks for: <c>all</c> of them to have ended, or <c>any</c> one. A wait on a moment alone has
/// no mode.
/// </summary>
public enum WaitMode
{
    All,
    Any,
}

/// <summary>
/// Where one wait of a work item stands. <c>Open</c> while it holds the item; <c>released</c> once the dispatcher has
/// returned the item to the queue; <c>cancelled</c> and <c>expired</c> when the item itself was cancelled or reached its
/// due date while it waited.
/// </summary>
public enum WaitStatus
{
    Open,
    Released,
    Cancelled,
    Expired,
}

/// <summary>
/// Why a wait was released: the work items it named have ended, the moment it named has come, the ceiling on how long
/// any wait may last has come, the provider block it waited on has cleared, somebody resumed it by hand, or the item is
/// the trial of the provider block it waited on, let through to find out whether the block still holds.
/// </summary>
public enum WaitReleaseReason
{
    ChildrenEnded,
    MomentReached,
    WaitCeiling,
    BlockCleared,
    ResumedByActor,
    BlockTrial,
}

/// <summary>
/// What a provider block holds: <c>account</c>, every operation it holds through one binding; <c>sender</c>, those that go
/// from one sending account under it. The contract's own words (<c>docs/contracts/block/provider_block.json</c>).
/// </summary>
public enum ProviderBlockScope
{
    Account,
    Sender,
}

/// <summary>
/// Where a provider block stands: <c>open</c> while it holds work, <c>cleared</c> once a call through it got past the
/// provider. A cleared block is kept, with when and by which attempt it cleared.
/// </summary>
public enum ProviderBlockStatus
{
    Open,
    Cleared,
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

/// <summary>
/// Where a person's request to read a span of an account's events again stands. <c>Pending</c> until its span has been
/// read to the end; <c>failed</c> when a read of it ended in an answer that repeating cannot change.
/// </summary>
public enum EventBackfillStatus
{
    Pending,
    Done,
    Failed,
}

/// <summary>
/// What a role decided to do about one reply it read: act on it within what it may already do, draft an answer for a
/// person to approve, hand the conversation to a person, or nothing at all. It is the decision, recorded; doing it is
/// other work.
/// </summary>
public enum ReplyReaction
{
    Act,
    Draft,
    Escalate,
    None,
}

/// <summary>
/// Where the chain started for one journal line stands. <c>Running</c> while its handlers run or one waits to be tried
/// again; <c>succeeded</c> when every handler answered; <c>failed</c> when one ended it; <c>superseded</c> when its kind's
/// position moved under it — a rewind, or the kind left without a subscriber — so its end recorded nothing further.
/// </summary>
public enum SubscriptionRunStatus
{
    Running,
    Succeeded,
    Failed,
    Superseded,
}
