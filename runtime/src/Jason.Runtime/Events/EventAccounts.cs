using System.Text.Json.Nodes;
using Jason.Contracts.Plugins;
using Jason.Runtime.Persistence;
using Jason.Runtime.Plugins.Invocation;
using Jason.Runtime.Plugins.Registry;
using Jason.Runtime.Routing;

namespace Jason.Runtime.Events;

/// <summary>
/// One account a route binds at a plugin that reads its accounts' events: the plugin, the identity of the binding — or
/// <see cref="EventSource.NoBinding"/> for a route that names none — and the binding as the active snapshot holds it.
/// </summary>
public sealed record BoundAccount(string PluginId, string BindingIdentity, JsonObject? Binding);

/// <summary>
/// One read of one account, as the scan hands it to the reader: which source, the account's binding as the snapshot held
/// it, the cursor the source held — the fence the page's cursor replaces — what the read starts from, and the package and
/// snapshots it runs under.
/// </summary>
public sealed record EventRead(
    int SourceId,
    string SourcePublicId,
    string PluginId,
    string BindingIdentity,
    JsonObject? Binding,
    string? HeldCursor,
    EventReadPlan Plan,
    PinnedPlugin Plugin,
    string RoutingSnapshotId);

/// <summary>
/// Which accounts are read: every distinct plugin and binding identity a route names — the global routes and every
/// campaign's alike — whose plugin is loaded, valid here and declares the events contract. A pure function of the two
/// active snapshots, so an account's events are held before any campaign links there, and an account no route names any
/// more is not read.
/// </summary>
public static class EventAccounts
{
    public static IReadOnlyList<BoundAccount> Bound(RouteSnapshot routes, PluginSnapshot plugins)
    {
        ArgumentNullException.ThrowIfNull(routes);
        ArgumentNullException.ThrowIfNull(plugins);
        var bound = new List<BoundAccount>();
        var seen = new HashSet<(string, string)>();
        foreach (var route in Named(routes))
        {
            if (plugins.Find(route.PluginId) is not { Status: PluginStatus.Valid } plugin || !plugin.Manifest.ReadsEvents)
            {
                continue;
            }

            var identity = route.BindingIdentity ?? EventSource.NoBinding;
            if (seen.Add((route.PluginId, identity)))
            {
                bound.Add(new BoundAccount(route.PluginId, identity, route.Binding));
            }
        }

        return bound;
    }

    /// <summary>Every route of the snapshot: the global default and overrides, then each campaign's, in a fixed order.</summary>
    private static IEnumerable<Route> Named(RouteSnapshot routes)
    {
        foreach (var set in new[] { routes.Global }.Concat(routes.Campaigns.OrderBy(entry => entry.Key, StringComparer.Ordinal).Select(entry => entry.Value)))
        {
            if (set.Default is { } fallback)
            {
                yield return fallback;
            }

            foreach (var (_, route) in set.Operations.OrderBy(entry => entry.Key, StringComparer.Ordinal))
            {
                yield return route;
            }
        }
    }
}

/// <summary>
/// How the reader invokes a plugin for a page: the plugin host through <see cref="PluginInvoker"/> in the runtime, and a
/// test's own pages in a test, which is what lets the reader's rules be held without a child.
/// </summary>
public interface IEventsInvoker
{
    Task<PluginInvocationResult> InvokeAsync(PluginInvocationRequest request, CancellationToken cancellationToken);
}

/// <summary>The runtime's invoker: one child of the plugin host per page, made at the one door every child is made at.</summary>
public sealed class PluginEventsInvoker(PluginInvoker invoker) : IEventsInvoker
{
    public Task<PluginInvocationResult> InvokeAsync(PluginInvocationRequest request, CancellationToken cancellationToken) =>
        invoker.InvokeAsync(request, cancellationToken);
}
