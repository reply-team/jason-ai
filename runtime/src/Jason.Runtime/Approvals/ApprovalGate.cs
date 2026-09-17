using System.Text.Json;
using System.Text.Json.Nodes;
using Jason.Contracts.Api;
using Jason.Contracts.Ids;
using Jason.Contracts.Json;
using Jason.Runtime.Domain;
using Jason.Runtime.Journal;
using Jason.Runtime.Persistence;
using Jason.Runtime.Routing;
using Jason.Runtime.WorkItems;
using Microsoft.EntityFrameworkCore;

namespace Jason.Runtime.Approvals;

/// <summary>
/// Where work waits for a person, and how it leaves. The claim parks work whose operation needs an approval and
/// releases it when the decision it was given still matches what would run; the canceller and the expirer resolve
/// a decision nobody will ever need. Nothing here decides anything itself — a runtime may not approve.
/// </summary>
public static class ApprovalGate
{
    /// <summary>The prefix of an approval's public id.</summary>
    public const string IdPrefix = "apr";

    /// <summary>Why the work was parked the first time: its operation is not one a dispatcher may approve.</summary>
    public const string ApprovalRequired = "approval_required";

    /// <summary>
    /// Why it was parked again: a decision had been made, and the work it was about is no longer the work in
    /// front of us. Nothing runs under a decision about something else.
    /// </summary>
    public const string InputChanged = "input_changed";

    /// <summary>
    /// The decision that is still about this item: the one pending, or the one approved and not yet outgrown.
    /// At most one exists — a park supersedes whatever it replaces, and the database holds the pending half of
    /// that to one row per item.
    /// </summary>
    public static Task<Approval?> LiveAsync(JasonDbContext db, int workItemId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(db);
        return db.Approvals
            .Where(a => a.WorkItemId == workItemId && (a.Status == ApprovalStatus.Pending || a.Status == ApprovalStatus.Approved))
            .OrderByDescending(a => a.Id)
            .FirstOrDefaultAsync(cancellationToken);
    }

    /// <summary>
    /// Parks the item and writes down what is being approved. A park makes no attempt: an attempt is a run, and
    /// nothing has run — an item parked three times that had spent three of its attempts would be given up on
    /// for being patient.
    /// </summary>
    public static Approval Park(
        JasonDbContext db,
        JournalWriter journal,
        WorkItem item,
        ProviderOpPlan plan,
        ApprovalSubject subject,
        JsonObject preview,
        Approval? outgrown,
        string reason,
        DateTime now)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(journal);
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(subject);
        ArgumentNullException.ThrowIfNull(preview);

        // A decision about a subject this item no longer has is not a decision anybody can act on, and it is the
        // reason the new parking says what changed rather than only that one happened.
        if (outgrown is not null)
        {
            Resolve(db, journal, item, outgrown, ApprovalStatus.Superseded, Actors.Dispatcher, reason: null, now);
        }

        var approval = new Approval
        {
            PublicId = PublicId.New(IdPrefix),
            WorkItem = item,
            CampaignId = item.CampaignId,
            Operation = plan.Contract.Id,
            OperationVersion = plan.Contract.Version,
            Subject = subject.Document.DeepClone().AsObject(),
            SubjectHash = subject.Hash,
            Preview = preview,
            PluginId = plan.Plugin.Manifest.Id,
            BindingIdentity = plan.BindingIdentity,
            RouteScope = plan.Scope,
            PluginSnapshotId = plan.PluginSnapshotId,
            RoutingSnapshotId = plan.RoutingSnapshotId,
            Status = ApprovalStatus.Pending,
            Reason = reason,
            RequestedAt = now,
        };

        db.Approvals.Add(approval);
        Wait(db, journal, item, now);
        journal.Append(
            db,
            Actors.Dispatcher,
            JournalKinds.ApprovalRequested,
            campaign: null,
            key: "approval",
            updated: Names(approval),
            reason: approval.Reason,
            workItem: item);

        return approval;
    }

    /// <summary>
    /// Moves the item to where it waits. Kept apart from <see cref="Park"/> for the one case that needs it
    /// alone: a decision that is still pending and still about this work is not asked for a second time.
    /// </summary>
    public static void Wait(JasonDbContext db, JournalWriter journal, WorkItem item, DateTime now)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(journal);
        ArgumentNullException.ThrowIfNull(item);

        var previous = item.Status;
        WorkItemTransitions.Apply(item, WorkItemStatus.AwaitingApproval, now);
        journal.Append(
            db,
            Actors.Dispatcher,
            WorkItemTransitions.JournalKind(previous, WorkItemStatus.AwaitingApproval),
            campaign: null,
            key: "status",
            old: WorkItemJson.Status(previous),
            updated: WorkItemJson.Status(WorkItemStatus.AwaitingApproval),
            workItem: item);
    }

    /// <summary>
    /// Ends a decision, with the line that says so. The work it was about has moved on — it was cancelled, it
    /// expired, or its subject changed — and a pending row about work that can no longer run is the one thing
    /// that would make <c>approval.list</c> untrue.
    /// </summary>
    public static void Resolve(
        JasonDbContext db,
        JournalWriter journal,
        WorkItem item,
        Approval approval,
        ApprovalStatus status,
        ActorRef actor,
        string? reason,
        DateTime now)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(journal);
        ArgumentNullException.ThrowIfNull(approval);
        ArgumentNullException.ThrowIfNull(actor);

        approval.Status = status;
        approval.DecidedAt = now;
        approval.DecidedByType = actor.Type;
        approval.DecidedById = actor.Id;
        approval.DecisionReason = reason;

        journal.Append(
            db,
            actor,
            Kind(status),
            campaign: null,
            key: "approval",
            updated: Names(approval),
            reason: reason,
            workItem: item);
    }

    /// <summary>
    /// What a journal line about a decision carries: identifiers, and never the subject or the preview. Both of
    /// those live on the approval row and in what the API answers, which is where somebody entitled to read them
    /// reads them — a chronicle is not a copy of a person's contact details.
    /// </summary>
    private static JsonObject Names(Approval approval) => new()
    {
        ["id"] = approval.PublicId,
        ["operation"] = approval.Operation,
        ["subject_hash"] = approval.SubjectHash,
    };

    private static string Kind(ApprovalStatus status) => status switch
    {
        ApprovalStatus.Approved => JournalKinds.ApprovalApproved,
        ApprovalStatus.Rejected => JournalKinds.ApprovalRejected,
        ApprovalStatus.Superseded => JournalKinds.ApprovalSuperseded,
        ApprovalStatus.Cancelled => JournalKinds.ApprovalCancelled,
        _ => throw new InvalidOperationException($"An approval is never journaled as {status}."),
    };
}
