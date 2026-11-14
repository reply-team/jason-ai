using System.Globalization;
using System.Text.Json.Nodes;
using Jason.Contracts.Api;
using Jason.Runtime.Domain;

namespace Jason.Runtime.Approvals;

/// <summary>
/// The review the runtime asks for a send before it is claimed, under an automatic send mode: a <c>critic</c> work item the
/// runtime creates as itself, in the send's own save, whose context is the reserved key <see cref="Key"/> naming the send,
/// <c>{"work_item_id": "wi_…", "operation": "…"}</c>. The key is the runtime's alone: a create or a patch that names it is
/// refused at that field, on every item and whoever sends it, so an item that carries it was made by the runtime and its
/// brief was never changed.
/// </summary>
public static class SendReview
{
    /// <summary>The reserved context key of the critic the runtime creates for a send.</summary>
    public const string Key = "send_review";

    /// <summary>The member of <see cref="Key"/> that names the send by its public id.</summary>
    public const string WorkItemIdMember = "work_item_id";

    /// <summary>The member of <see cref="Key"/> that names the send's operation.</summary>
    public const string OperationMember = "operation";

    /// <summary>The reason the critic's <c>workitem_created</c> line carries.</summary>
    public const string Reason = "a send to review before it is claimed";

    private const string RefusedAtCreate =
        "send_review is the runtime's: it names the send a critic the runtime created is to review, and nobody else writes it.";

    private const string RefusedInPatch =
        "send_review is written by the runtime when it creates a critic, and is never changed or taken back.";

    /// <summary>
    /// The send a critic's context names under <see cref="Key"/>, by its public id; null where the context carries no such
    /// key, or one of another shape.
    /// </summary>
    public static string? Named(JsonObject? context) =>
        context?[Key] is JsonObject review && review[WorkItemIdMember] is JsonValue id && id.TryGetValue(out string? text) ? text : null;

    /// <summary>The context of the critic the runtime creates for <paramref name="sendId"/>: the key, and nothing else.</summary>
    internal static JsonObject Context(string sendId, string operation) => new()
    {
        [Key] = new JsonObject
        {
            [WorkItemIdMember] = sendId,
            [OperationMember] = operation,
        },
    };

    /// <summary>A create never names <see cref="Key"/>, whatever the work and whoever sends it.</summary>
    internal static void RefuseAtCreate(JsonObject? context, ValidationErrors errors)
    {
        ArgumentNullException.ThrowIfNull(errors);
        if (context?.ContainsKey(Key) == true)
        {
            errors.Add("context." + Key, "not_allowed", RefusedAtCreate);
        }
    }

    /// <summary>A patch never names <see cref="Key"/>, to set it or to take it back, on any item and whoever sends it.</summary>
    internal static void RefuseInPatch(JsonObject? set, IReadOnlyList<string>? unset, ValidationErrors errors)
    {
        ArgumentNullException.ThrowIfNull(errors);
        if (set?.ContainsKey(Key) == true)
        {
            errors.Add("set." + Key, "not_allowed", RefusedInPatch);
        }

        for (var index = 0; index < (unset?.Count ?? 0); index++)
        {
            if (string.Equals(unset![index], Key, StringComparison.Ordinal))
            {
                errors.Add(string.Create(CultureInfo.InvariantCulture, $"unset[{index}]"), "not_allowed", RefusedInPatch);
            }
        }
    }
}
