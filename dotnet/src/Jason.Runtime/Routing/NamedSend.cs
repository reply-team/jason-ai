using Jason.Contracts.Api;

namespace Jason.Runtime.Routing;

/// <summary>
/// The send a read of one, or a take-back of one, names under <c>args.send</c>, as the claim found it in the work's own
/// campaign: the work item that made it, and what its succeeded attempt kept of where it went.
/// </summary>
/// <param name="WorkItem">The public id of the work item that made the send.</param>
/// <param name="Operation">The operation that work item performs.</param>
/// <param name="Status">Where that work item stands.</param>
/// <param name="Reference">The provider's reference its succeeded attempt returned, or null where none did.</param>
/// <param name="PluginId">The plugin that attempt ran under, as its provenance kept it.</param>
/// <param name="BindingIdentity">The binding that attempt ran under, as its provenance kept it.</param>
public sealed record NamedSend(
    string WorkItem,
    string? Operation,
    WorkItemStatus Status,
    string? Reference,
    string? PluginId,
    string? BindingIdentity);
