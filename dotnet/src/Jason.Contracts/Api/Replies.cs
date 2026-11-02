namespace Jason.Contracts.Api;

/// <summary>
/// A role's verdict on one reply in one campaign: which reply, by the provider's own identifiers and its moment; what
/// kind of reply the role took it for, as a category word the skills own; how sure it is; what it decided to do; and
/// which of the campaign's goals that steers towards, by the goal's id. It carries no word of the reply, which stays
/// with the provider, and the runtime reads nothing into the category but that it is one. Where the category is a
/// person's answer to a question about the reply, <c>DecisionId</c> names that question, by its <c>dec_</c> id.
/// </summary>
/// <remarks>
/// Every field is nullable so that leaving one out is a refusal naming it rather than a default the caller never chose.
/// </remarks>
public sealed record ReplyRecordRequest(
    string? CampaignId,
    string? PluginId,
    string? ThreadId,
    string? MessageId,
    DateTimeOffset? MessageAt,
    string? Category,
    double? Confidence,
    ReplyReaction? Reaction,
    string? ContactId = null,
    string? GoalId = null,
    ActorRef? Actor = null,
    string? Reason = null,
    string? DecisionId = null);
