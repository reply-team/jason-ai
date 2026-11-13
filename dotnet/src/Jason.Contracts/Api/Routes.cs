using System.Text.Json.Nodes;
using Jason.Contracts.Plugins;

namespace Jason.Contracts.Api;

/// <summary>
/// Which plugin would perform one operation for one campaign, asked of the snapshot that is active now. <c>Channel</c> is
/// the channel a call of the operation names for its route — the one the claim reads from the work item — so the answer
/// is the one for a call by that channel; null asks the operation as a call that names none.
/// </summary>
public sealed record RouteResolveRequest(string? CampaignId, string? Operation, string? Channel = null);

/// <summary>
/// The answer a claim would reach, and — when the routed plugin could not actually run the work — why not.
/// <para>
/// Nothing resolving at all is not this: that is the business error <c>no_route</c>, because a campaign with no
/// route is a configuration answer. A route that resolves but cannot run is a diagnosis, and an operator needs
/// it in full: the plugin it names, the state that plugin is in, and every problem between the two.
/// </para>
/// </summary>
public sealed record RouteResolutionDto(
    string Operation,
    string CampaignId,
    string? PluginId,
    string? PluginVersion,
    string? Digest,
    PluginStatus? PluginStatus,
    RouteScope? Scope,
    JsonObject? Binding,
    string? BindingIdentity,
    string RoutingSnapshotId,
    string PluginSnapshotId,
    int OperationVersion,
    bool Usable,
    IReadOnlyList<ErrorDetail> Problems,
    string? Channel = null);

/// <summary>Every route, or one campaign's. The global set is always answered: it is what a campaign falls back to.</summary>
public sealed record RouteListRequest(string? CampaignId);

public sealed record RouteDto(string PluginId, JsonObject? Binding, string? BindingIdentity);

/// <summary>
/// One scope's routes: the default, the operations' own routes, and the channel routes under an operation — operation,
/// then channel, then the route. The runtime always answers <c>channels</c>, empty where no route names a channel.
/// </summary>
public sealed record RouteSetDto(
    RouteDto? Default,
    IReadOnlyDictionary<string, RouteDto> Operations,
    IReadOnlyDictionary<string, IReadOnlyDictionary<string, RouteDto>>? Channels = null);

/// <summary>One campaign's routes, in the shape of <see cref="RouteSetDto"/>.</summary>
public sealed record CampaignRoutesDto(
    string CampaignId,
    RouteDto? Default,
    IReadOnlyDictionary<string, RouteDto> Operations,
    IReadOnlyDictionary<string, IReadOnlyDictionary<string, RouteDto>>? Channels = null);

/// <summary>
/// The whole routing picture under the two snapshot ids it was frozen with. Both are answered because which
/// plugin performs an operation is only answerable from both.
/// </summary>
public sealed record RoutesDto(
    string RoutingSnapshotId,
    string PluginSnapshotId,
    RouteSetDto Global,
    IReadOnlyList<CampaignRoutesDto> Campaigns);

/// <summary>
/// Sends one campaign's work somewhere. <c>Operation</c> null is the campaign's default route, which hides
/// every global operation override for that campaign. There is no global <c>route.set</c>: the global set lives
/// in the settings file and is activated by a plugin reload, so that what a reload freezes is what the file says.
/// <para>
/// <c>Channel</c> names a channel under <c>Operation</c>, one the operation's document lets a route name: the route is
/// then that channel's alone, set beside the operation's own route and never in its place. Null is the operation's own
/// route; a channel needs an operation, since the default names none.
/// </para>
/// </summary>
public sealed record RouteSetRequest(
    string? CampaignId,
    string? Operation,
    string? Plugin,
    JsonObject? Binding,
    ActorRef? Actor,
    string? Reason,
    string? Channel = null);

/// <summary>
/// Takes one campaign route away: the default, an operation's own route, or with <c>Channel</c> one channel's route under
/// <c>Operation</c> — each alone, so taking the operation's route away leaves its channel routes, and the other way round.
/// </summary>
public sealed record RouteUnsetRequest(string? CampaignId, string? Operation, ActorRef? Actor, string? Reason, string? Channel = null);
