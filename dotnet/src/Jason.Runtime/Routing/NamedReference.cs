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

/// <summary>
/// The send a read of one, or a take-back of one, names under <c>args.send</c>. <paramref name="Channel"/> is the channel
/// the send's own route was resolved by, read from its arguments as its operation's document names
/// (<see cref="Jason.Contracts.Operations.OperationContract.RouteChannel"/>), and null where it named none: a read or a
/// take-back of the send asks that route first among the campaign's, since it is the one the send went by.
/// </summary>
public sealed record NamedSend(
    string WorkItem,
    string? Operation,
    WorkItemStatus Status,
    string? Reference,
    string? PluginId,
    string? BindingIdentity,
    string? Channel = null)
    : NamedReference(WorkItem, Operation, Status, Reference, PluginId, BindingIdentity);

/// <summary>
/// The search a status read, or a stop, names under <c>args.search</c>. <paramref name="Cursor"/> is where the next status
/// read starts: the plugin's own, as the search's row keeps it from the last read that answered one, never parsed, and null
/// before any did, which reads from the first person found.
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

/// <summary>
/// An earlier letter of the campaign a message may follow, as the claim read it in the work's own campaign: the one the
/// arguments name under <c>args.follows</c>, the line's root where they name none (<see cref="Jason.Runtime.Campaigns.LineRoots"/>),
/// or, on a repeat, the one the first claim recorded. What it kept of where it went is its latest succeeded attempt's.
/// </summary>
/// <param name="ContactId">The person it was about, by the contact's row, or null where it named nobody.</param>
/// <param name="Channel">The channel its arguments named under <c>args.channel</c>.</param>
/// <param name="Sender">
/// The provider's account it went from, as its result says under <c>items[0].sender_external_id</c>, or null where it holds
/// no result that says one.
/// </param>
/// <param name="TakenBack">Whether a succeeded <c>message.cancel</c> of the campaign names it: it never went.</param>
public sealed record EarlierLetter(
    string WorkItem,
    string? Operation,
    WorkItemStatus Status,
    string? Reference,
    string? PluginId,
    string? BindingIdentity,
    int? ContactId,
    string? Channel,
    string? Sender,
    bool TakenBack)
    : NamedReference(WorkItem, Operation, Status, Reference, PluginId, BindingIdentity);
