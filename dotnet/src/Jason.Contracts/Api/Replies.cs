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

/// <summary>
/// The campaigns as <see cref="CampaignActivityRequest"/> names them — named, archived ones included, or every campaign
/// not archived — and the window, held to each reply's own moment: <c>since</c> included, <c>until</c> not, both required.
/// </summary>
public sealed record ReplyCountRequest(IReadOnlyList<string>? CampaignIds, DateTimeOffset? Since, DateTimeOffset? Until);

/// <summary>
/// The replies of one category word, as the verdicts that stand say it: how many, and in how many conversations, a
/// conversation being one thread of one plugin. A null category is the replies whose verdict names none.
/// </summary>
public sealed record ReplyCategoryCount(string? Category, int Replies, int Threads);

/// <summary>
/// One campaign's judged replies in the window: how many, in how many conversations, and the same by category. The
/// categories' replies add up to <c>replies</c>; their threads may add up to more than <c>threads</c>, since one
/// conversation may hold replies of two categories.
/// </summary>
public sealed record ReplyCountRow(string CampaignId, int Replies, int Threads, IReadOnlyList<ReplyCategoryCount> Categories);

/// <summary>The campaigns' counts added up: a conversation of two campaigns is counted in each.</summary>
public sealed record ReplyCountTotal(int Replies, int Threads, IReadOnlyList<ReplyCategoryCount> Categories);

/// <summary>A row per campaign asked, and <c>total</c> across them.</summary>
public sealed record ReplyCountResponse(IReadOnlyList<ReplyCountRow> Campaigns, ReplyCountTotal Total);
