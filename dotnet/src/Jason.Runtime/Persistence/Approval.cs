using System.Text.Json.Nodes;
using Jason.Contracts.Api;

namespace Jason.Runtime.Persistence;

/// <summary>
/// One decision about one work item: what is being approved, what it was called, what a person was shown, and
/// what they answered. Written by the claim when it parks work whose operation needs a person's approval, and
/// read by the claim again before anything runs.
/// </summary>
/// <remarks>
/// The row carries the route material the claim resolved — which plugin, at which scope, through which account,
/// from which snapshots — because that is what makes the preview honest about the effect. It never carries a
/// credential: the binding is named by its hash, as an attempt names it.
/// </remarks>
public sealed class Approval
{
    public int Id { get; set; }

    /// <summary>The <c>apr_</c> identifier, and the only one that leaves the runtime.</summary>
    public required string PublicId { get; set; }

    public int WorkItemId { get; set; }

    public WorkItem? WorkItem { get; set; }

    /// <summary>Denormalized from the item, so listing a campaign's pending decisions is one indexed read.</summary>
    public int CampaignId { get; set; }

    public required string Operation { get; set; }

    public int OperationVersion { get; set; }

    /// <summary>What is being approved: the composed input, the operation, the item, the plugin and the account.</summary>
    public required JsonObject Subject { get; set; }

    /// <summary>The subject's canonical <c>sha256:</c>, which the claim compares before it runs anything.</summary>
    public required string SubjectHash { get; set; }

    /// <summary>What a person is shown, assembled at the claim and never again.</summary>
    public required JsonObject Preview { get; set; }

    public required string PluginId { get; set; }

    public string? BindingIdentity { get; set; }

    public RouteScope RouteScope { get; set; }

    public required string PluginSnapshotId { get; set; }

    public required string RoutingSnapshotId { get; set; }

    public ApprovalStatus Status { get; set; } = ApprovalStatus.Pending;

    /// <summary>
    /// Why the work was parked: <c>approval_required</c> the first time, and <c>input_changed</c> when a decision
    /// had already been made about a subject this one no longer matches.
    /// </summary>
    public required string Reason { get; set; }

    public DateTime RequestedAt { get; set; }

    public DateTime? DecidedAt { get; set; }

    /// <summary>Who decided, which is always a person: a dispatcher may not stand in for one.</summary>
    public ActorType? DecidedByType { get; set; }

    public string? DecidedById { get; set; }

    /// <summary>What the person said about their decision, which a rejection carries into the item's failure.</summary>
    public string? DecisionReason { get; set; }
}
