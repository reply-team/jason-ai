using Jason.Contracts.Api;

namespace Jason.Runtime.Persistence;

/// <summary>
/// What one waiting work item waits for: the items it named and whether all or any of them must end, the moment it
/// named, and the deadline that bounds it. Written when an attempt ends waiting, read by the dispatcher's resume step,
/// and closed once — released into the queue, or cancelled or expired with the item. Structural facts only: nothing
/// here is anybody's text, so deciding that a wait is over never reads what the work means.
/// </summary>
public sealed class WorkItemWait
{
    public int Id { get; set; }

    public int WorkItemId { get; set; }

    public WorkItem? WorkItem { get; set; }

    /// <summary>
    /// The attempt that ended waiting: <c>yielded</c> for a role's wait, <c>blocked</c> for an attempt a provider block
    /// stopped. Null only for a wait no attempt asked for: an item the claim parked on a block, or a responder the respond
    /// step created already waiting on the read of its thread, whose first run is therefore told of no resumption.
    /// </summary>
    public int? AttemptId { get; set; }

    public Attempt? Attempt { get; set; }

    /// <summary>Whether all the named items must end, or any one of them; null when the wait names a moment only.</summary>
    public WaitMode? Mode { get; set; }

    /// <summary>The moment the wait named, if it named one.</summary>
    public DateTime? Until { get; set; }

    /// <summary>
    /// When the wait is over whatever else happens: the moment it named, or else the ceiling counted from when it opened.
    /// A role's wait always has one.
    /// </summary>
    public DateTime? Deadline { get; set; }

    public WaitStatus Status { get; set; } = WaitStatus.Open;

    public DateTime OpenedAt { get; set; }

    /// <summary>When the wait was closed, for whichever reason it closed.</summary>
    public DateTime? ReleasedAt { get; set; }

    /// <summary>Why it was released into the queue; null while it is open, and when it closed with its item instead.</summary>
    public WaitReleaseReason? ReleaseReason { get; set; }

    /// <summary>The attempt the released item ran next, which is the one told what it waited for.</summary>
    public int? ResumedByAttemptId { get; set; }

    public Attempt? ResumedByAttempt { get; set; }

    /// <summary>
    /// The provider block the item is parked on. Such a wait names no items, no mode, no moment and no deadline: it is
    /// bounded by the item's own due date alone, and released when the block clears or when the item is the block's
    /// trial. Null for every other wait.
    /// </summary>
    public int? ProviderBlockId { get; set; }

    public ProviderBlock? ProviderBlock { get; set; }

    /// <summary>The items the wait named, each with its place in the order they were named in.</summary>
    public List<WorkItemWaitItem> Items { get; set; } = [];
}

/// <summary>One item a wait named. The pair is the key, so a wait names an item once.</summary>
public sealed class WorkItemWaitItem
{
    public int WaitId { get; set; }

    public WorkItemWait? Wait { get; set; }

    public int ChildWorkItemId { get; set; }

    public WorkItem? ChildWorkItem { get; set; }

    /// <summary>
    /// Where the item stood in the order the wait named them, from 0. Kept in the table rather than only in the
    /// <c>workitem_waiting</c> line, because what the next run is told about the items is told in that order.
    /// </summary>
    public int Position { get; set; }
}
