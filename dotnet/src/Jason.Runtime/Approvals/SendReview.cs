using System.Text.Json.Nodes;

namespace Jason.Runtime.Approvals;

/// <summary>
/// The reserved context key of the critic's work the runtime makes for a send created under an automatic mode:
/// <c>send_review</c>, exactly <c>{"work_item_id": "&lt;the send&gt;", "operation": "&lt;its operation&gt;"}</c>. Only the
/// runtime writes it, on a work item it creates itself; the automatic mode counts a critic's pass only where the pass
/// comes from such an item naming the send it passed, whose context was changed since by nobody but the runtime's own
/// actors: a change by a person, a role or the critic's own attempt disqualifies it.
/// </summary>
public static class SendReview
{
    /// <summary>The reserved context key.</summary>
    public const string Key = "send_review";

    /// <summary>The member that names the send, by its public id.</summary>
    public const string WorkItemIdMember = "work_item_id";

    /// <summary>The member that names the send's operation.</summary>
    public const string OperationMember = "operation";

    /// <summary>The send <paramref name="context"/>'s key names, by its public id; null where it names none.</summary>
    public static string? Named(JsonObject? context) => Member(context, WorkItemIdMember);

    /// <summary>The operation <paramref name="context"/>'s key names; null where it names none.</summary>
    public static string? Operation(JsonObject? context) => Member(context, OperationMember);

    private static string? Member(JsonObject? context, string member) =>
        context?[Key] is JsonObject named && named[member] is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;
}
