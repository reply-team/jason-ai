using System.Text.Json.Nodes;
using Jason.Contracts.Api;

namespace Jason.Runtime.Persistence;

/// <summary>
/// A question a running role could not answer for itself, kept so that it outlives the attempt that asked.
/// That is the whole difference from an approval: an approval parks one operation on one work item and the
/// work is run again once a person decides, while the attempt that raised a decision is over before the
/// answer arrives, and what the answer releases is a fresh review.
/// </summary>
/// <remarks>
/// The causal references are <em>identifiers</em> and never copies, so a person — or the review that follows
/// — reads what is true now rather than what was true when somebody asked.
/// </remarks>
public sealed class Decision
{
    public int Id { get; set; }

    /// <summary>The <c>dec_</c> identifier, and the only one that leaves the runtime.</summary>
    public required string PublicId { get; set; }

    /// <summary>Denormalized from the item, so listing one campaign's open questions is one indexed read.</summary>
    public int CampaignId { get; set; }

    public Campaign? Campaign { get; set; }

    public int WorkItemId { get; set; }

    public WorkItem? WorkItem { get; set; }

    /// <summary>The attempt that asked. It is also the chain the review released by the answer belongs to.</summary>
    public int AttemptId { get; set; }

    public Attempt? Attempt { get; set; }

    /// <summary>The question in the role's own words.</summary>
    public required string Question { get; set; }

    /// <summary>The named answers a person may choose between, where the asker named any.</summary>
    public JsonArray? Options { get; set; }

    /// <summary>What to read before deciding: work, attempts, chronicle lines, reports and decisions, by id.</summary>
    public JsonArray? References { get; set; }

    public DecisionStatus Status { get; set; } = DecisionStatus.Pending;

    public DateTime RaisedAt { get; set; }

    public string? Answer { get; set; }

    /// <summary>Which of the named options was chosen, where any were named.</summary>
    public string? ChosenOption { get; set; }

    public DateTime? AnsweredAt { get; set; }

    /// <summary>Who answered, which is always a person: a role may not stand in for one.</summary>
    public ActorType? AnsweredByType { get; set; }

    public string? AnsweredById { get; set; }

    /// <summary>
    /// The chronicle line this decision's answer wrote. It is how the summon names the decision that woke it
    /// without reading a line's meaning: one lookup by an identifier it already holds. The alternative was a
    /// subject column on the one table nothing can edit afterwards, for one consumer.
    /// </summary>
    public string? AnswerJournalEntryId { get; set; }
}
