using System.Text.Json.Nodes;
using Jason.Contracts.Api;

namespace Jason.Runtime.Persistence;

/// <summary>
/// One effect somebody performed outside Jason and told Jason about afterwards. It is an assertion, not a
/// record of work: nothing here was approved, routed, supervised or verified by this runtime, and admitting it
/// claims only that the report is well-formed and names who made it.
/// </summary>
/// <remarks>
/// The row is in two halves. <see cref="Assertion"/> and the columns denormalized out of it are the reporter's
/// own words, stored exactly as they arrived and never changed afterwards. <see cref="PublicId"/>,
/// <see cref="ReceivedAt"/>, <see cref="AssertionHash"/>, <see cref="OperationKnown"/> and
/// <see cref="ContactInCampaign"/> are the receipt: the little the runtime knows by itself. A submission that
/// carries one of the receipt's names is refused rather than quietly overwritten, because a document that comes
/// back different from the one that was sent is not the assertion anybody made.
/// </remarks>
public sealed class Report
{
    public int Id { get; set; }

    /// <summary>The <c>rpt_</c> identifier, and the only one that leaves the runtime.</summary>
    public required string PublicId { get; set; }

    /// <summary>
    /// Who reported it, as claimed and — for an attempt — as checked. A person, a role acting in a session, or a
    /// running executor; never the runtime itself, which performs effects rather than reporting them.
    /// </summary>
    public ActorType ReporterType { get; set; }

    public required string ReporterId { get; set; }

    /// <summary>What the reporter says was done, in the reporter's own vocabulary. Jason does not interpret it.</summary>
    public required string Effect { get; set; }

    /// <summary>What it was done with: a CLI, an MCP server, a script, a person at a keyboard.</summary>
    public required string Tool { get; set; }

    public string? Provider { get; set; }

    /// <summary>The account the effect was performed through, named by identity. Never a credential.</summary>
    public string? Account { get; set; }

    /// <summary>When the reporter says it happened, and when they say they saw it. Both are claims.</summary>
    public DateTime? OccurredAt { get; set; }

    public DateTime? ObservedAt { get; set; }

    public int? CampaignId { get; set; }

    public Campaign? Campaign { get; set; }

    public int? ContactId { get; set; }

    public Contact? Contact { get; set; }

    public int? WorkItemId { get; set; }

    public WorkItem? WorkItem { get; set; }

    /// <summary>The canonical operation the reporter believes this corresponds to, if any. Free text: an
    /// operation this runtime has never heard of is still admissible, and stays the reporter's word.</summary>
    public string? Operation { get; set; }

    /// <summary>Whether <see cref="Operation"/> names something the catalog publishes. Decided at admission and
    /// never a reason to refuse one.</summary>
    public bool OperationKnown { get; set; }

    /// <summary>
    /// Whether the reported contact was a member of the reported campaign when the report landed. Null when the
    /// report names no contact or no campaign. False is not an error: an out-of-band act reaching somebody who is
    /// not in the local campaign yet is exactly the kind of fact a report exists to carry.
    /// </summary>
    public bool? ContactInCampaign { get; set; }

    public required string Summary { get; set; }

    /// <summary>The submitted document, exactly as it arrived, minus the request envelope.</summary>
    public required JsonObject Assertion { get; set; }

    /// <summary>The assertion's canonical <c>sha256:</c>, which decides a duplicate when no key was given.</summary>
    public required string AssertionHash { get; set; }

    /// <summary>The reporter's own key for this submission, if they gave one. It is what a retry is matched by.</summary>
    public string? IdempotencyKey { get; set; }

    public string? Reason { get; set; }

    /// <summary>When the runtime admitted it, which is the one time in the row the runtime can vouch for.</summary>
    public DateTime ReceivedAt { get; set; }
}
