using System.Text.Json.Nodes;
using Jason.Contracts.Api;
using Jason.Contracts.Operations;
using Jason.Runtime.Json;
using Jason.Runtime.Persistence;
using Jason.Runtime.WorkItems;

namespace Jason.Runtime.Approvals;

/// <summary>
/// What a review of a send is bound to: the operation, the item and its context's <c>input</c> as stored, hashed
/// canonically. One function, called by the review verb, by <c>workitem.get</c> and by the claim, so the three compare the
/// same string.
/// </summary>
/// <remarks>
/// It is the text, not the approval's subject. The subject is composed at the claim from the routes, the pins and the
/// campaign as they stand then, so a pin learned by another send, a campaign renamed or started, or a route set would void
/// a review whose text never moved. The text moves only by an edit of the input, which is exactly when a review no longer
/// stands. The full subject is still what the approval row records.
/// </remarks>
public static class ReviewSubject
{
    /// <summary>The review hash of <paramref name="item"/>, whatever its kind; only a reviewable send is ever reviewed.</summary>
    public static string Hash(WorkItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        var document = new JsonObject
        {
            ["input"] = item.Context[WorkItemService.InputKey]?.DeepClone(),
            ["operation"] = item.Operation,
            ["work_item"] = item.PublicId,
        };

        return CanonicalJson.Hash(document);
    }

    /// <summary>
    /// Whether <paramref name="item"/> is a send a review is for: provider work whose operation's contract has a person decide
    /// each one (<c>confirm_each</c>), read from the catalog so no operation is named here.
    /// </summary>
    public static bool IsReviewable(WorkItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        return item.Kind == WorkItemKind.ProviderOp && IsReviewable(item.Operation);
    }

    /// <summary>Whether <paramref name="operation"/> is a send to a person.</summary>
    public static bool IsReviewable(string? operation) =>
        operation is not null
        && OperationCatalog.Find(operation) is { } contract
        && string.Equals(contract.Approval.Value, ConfirmEach, StringComparison.Ordinal);

    private const string ConfirmEach = "confirm_each";
}
