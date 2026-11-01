using System.Text.Json;
using System.Text.Json.Nodes;
using Jason.Contracts.Api;
using Jason.Contracts.Json;

namespace Jason.Runtime.WorkItems;

/// <summary>
/// A work item's status as the chronicle spells it. One place, because three writers record a transition — the
/// claim, the canceller and a person's decision — and a status written two ways is a chronicle nobody can query.
/// </summary>
public static class WorkItemJson
{
    public static JsonNode? Status(WorkItemStatus status) => JsonSerializer.SerializeToNode(status, JasonJson.Options);
}
