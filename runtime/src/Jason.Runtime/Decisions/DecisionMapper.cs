using System.Text.Json;
using System.Text.Json.Nodes;
using Jason.Contracts.Api;
using Jason.Contracts.Json;
using Jason.Runtime.Persistence;
using Jason.Runtime.WorkItems;

namespace Jason.Runtime.Decisions;

/// <summary>
/// Between the row and the wire. The options and the references are stored as JSON because they are small
/// lists of small things with no behaviour, and read back through the same serializer the API answers with,
/// so what a caller sent is what a caller reads.
/// </summary>
internal static class DecisionMapper
{
    public static JsonArray? ToJson<T>(IReadOnlyList<T>? values) =>
        values is null || values.Count == 0
            ? null
            : [.. values.Select(value => JsonSerializer.SerializeToNode(value, JasonJson.Options))];

    public static IReadOnlyList<T>? FromJson<T>(JsonArray? stored) =>
        stored is null ? null : [.. stored.Select(node => node.Deserialize<T>(JasonJson.Options)!)];

    public static DecisionDto ToDto(Decision decision, string campaignPublicId, string workItemPublicId, string attemptPublicId)
    {
        ArgumentNullException.ThrowIfNull(decision);
        return new DecisionDto(
            decision.PublicId,
            campaignPublicId,
            workItemPublicId,
            attemptPublicId,
            decision.Status,
            decision.Question,
            FromJson<DecisionOption>(decision.Options),
            FromJson<DecisionReference>(decision.References),
            WorkItemMapper.Utc(decision.RaisedAt),
            decision.Answer,
            decision.ChosenOption,
            WorkItemMapper.Utc(decision.AnsweredAt),
            Decider(decision));
    }

    public static DecisionSummaryDto ToSummary(Decision decision, string campaignPublicId, string workItemPublicId)
    {
        ArgumentNullException.ThrowIfNull(decision);
        return new DecisionSummaryDto(
            decision.PublicId,
            campaignPublicId,
            workItemPublicId,
            decision.Status,
            decision.Question,
            WorkItemMapper.Utc(decision.RaisedAt),
            WorkItemMapper.Utc(decision.AnsweredAt),
            Decider(decision));
    }

    private static ActorRef? Decider(Decision decision) =>
        decision.AnsweredByType is { } type ? new ActorRef(type, decision.AnsweredById) : null;
}
