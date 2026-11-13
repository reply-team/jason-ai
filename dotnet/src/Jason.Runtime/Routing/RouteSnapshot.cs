using System.Text.Json.Nodes;
using Jason.Contracts.Api;
using Jason.Contracts.Ids;

namespace Jason.Runtime.Routing;

/// <summary>
/// Where one operation's work goes: a plugin, and the binding that says which account, workspace or mailbox of
/// that plugin to work through. The binding is opaque to the runtime — it selects an identity the plugin's own
/// credential store already holds, and never carries the credential. <see cref="BindingIdentity"/> is the hash
/// of that binding, which is what an attempt records: the value itself is already in the journal and in
/// <c>route.list</c>, and the same identity across two attempts is what says whether a retry ran against the
/// same account.
/// </summary>
public sealed record Route(string PluginId, JsonObject? Binding, string? BindingIdentity);

/// <summary>
/// One scope's routes: a default, the operations that override it, and the channels that override an operation —
/// operation, then channel, then the route. Global and campaign have the same shape, so the precedence is six lookups
/// over two of these rather than six kinds of thing. A set made without channel routes holds none
/// (<see cref="Channels"/> is null), so every set made before they existed means what it meant.
/// </summary>
public sealed record RouteSet(
    Route? Default,
    IReadOnlyDictionary<string, Route> Operations,
    IReadOnlyDictionary<string, IReadOnlyDictionary<string, Route>>? Channels = null)
{
    public static RouteSet Empty { get; } = new(null, new Dictionary<string, Route>(StringComparer.Ordinal));

    /// <summary>
    /// Every channel route of the set in one fixed order: operations ordinally, then each operation's channels ordinally.
    /// What walks every route walks them in this order, so its answer never depends on how a dictionary was filled.
    /// </summary>
    public IEnumerable<(string Operation, string Channel, Route Route)> ChannelRoutes =>
        Channels is null
            ? []
            : Channels
                .OrderBy(entry => entry.Key, StringComparer.Ordinal)
                .SelectMany(entry => entry.Value
                    .OrderBy(channel => channel.Key, StringComparer.Ordinal)
                    .Select(channel => (entry.Key, channel.Key, channel.Value)));

    /// <summary>The route <paramref name="channel"/> has under <paramref name="operation"/>; null where it has none, and always for no channel.</summary>
    public Route? ChannelRoute(string operation, string? channel) =>
        channel is not null
        && Channels is not null
        && Channels.TryGetValue(operation, out var channels)
        && channels.TryGetValue(channel, out var route)
            ? route
            : null;
}

/// <summary>
/// Every route the last activation produced, under one id and pinned to the plugin snapshot it was validated
/// against. It lives in memory only, derived from the settings plus the campaign rows, and it is immutable: a
/// resolution taken from it stays the resolution for as long as it is held, whatever a concurrent reload
/// decides. The route snapshot id is independent of the plugin snapshot id — <c>route.set</c> makes a new route
/// snapshot over the same plugin snapshot — and both are pinned per attempt.
/// </summary>
public sealed record RouteSnapshot(
    string Id,
    DateTimeOffset ActivatedAt,
    string PluginSnapshotId,
    RouteSet Global,
    IReadOnlyDictionary<string, RouteSet> Campaigns)
{
    public const string IdPrefix = "rts";

    /// <summary>Nothing routed anywhere: what a runtime holds before its first load, and after a load that found no routes.</summary>
    public static RouteSnapshot Empty(DateTimeOffset now, string pluginSnapshotId)
    {
        ArgumentNullException.ThrowIfNull(pluginSnapshotId);
        return new RouteSnapshot(
            PublicId.New(IdPrefix),
            now,
            pluginSnapshotId,
            RouteSet.Empty,
            new Dictionary<string, RouteSet>(StringComparer.Ordinal));
    }
}

/// <summary>The route that won, and the level it won at.</summary>
public sealed record Resolution(Route Route, RouteScope Scope);
