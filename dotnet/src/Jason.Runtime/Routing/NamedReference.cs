using System.Text.Json.Nodes;
using Jason.Contracts.Api;

namespace Jason.Runtime.Routing;

/// <summary>
/// A provider's work that a later operation acts on by the reference the provider gave it — a send a read or a take-back
/// names under <c>args.send</c>, a search a status read or a stop names under <c>args.search</c> — as the claim found it
/// in the work's own campaign: the work item that made it, and what its succeeded attempt kept of where it went.
/// </summary>
/// <param name="WorkItem">The public id of the work item that made it.</param>
/// <param name="Operation">The operation that work item performs.</param>
/// <param name="Status">Where that work item stands.</param>
/// <param name="Reference">The provider's reference its succeeded attempt returned, or null where none did.</param>
/// <param name="PluginId">The plugin that attempt ran under, as its provenance kept it.</param>
/// <param name="BindingIdentity">The binding that attempt ran under, as its provenance kept it.</param>
public abstract record NamedReference(
    string WorkItem,
    string? Operation,
    WorkItemStatus Status,
    string? Reference,
    string? PluginId,
    string? BindingIdentity);

/// <summary>The send a read of one, or a take-back of one, names under <c>args.send</c>.</summary>
public sealed record NamedSend(
    string WorkItem,
    string? Operation,
    WorkItemStatus Status,
    string? Reference,
    string? PluginId,
    string? BindingIdentity)
    : NamedReference(WorkItem, Operation, Status, Reference, PluginId, BindingIdentity);

/// <summary>
/// The search a status read, or a stop, names under <c>args.search</c>. <paramref name="Cursor"/> is where the next status
/// read starts: the plugin's own, stored and never parsed, and null in a build that keeps no search's cursor, which reads
/// from the start.
/// </summary>
public sealed record NamedSearch(
    string WorkItem,
    string? Operation,
    WorkItemStatus Status,
    string? Reference,
    string? PluginId,
    string? BindingIdentity,
    string? Cursor = null)
    : NamedReference(WorkItem, Operation, Status, Reference, PluginId, BindingIdentity);

/// <summary>
/// An audience the campaign holds, as the work names it by its number under <c>args.audience</c>: the definition it was
/// searched under, which the runtime composes for a top-up.
/// </summary>
public sealed record NamedAudience(int Number, JsonObject Definition);
