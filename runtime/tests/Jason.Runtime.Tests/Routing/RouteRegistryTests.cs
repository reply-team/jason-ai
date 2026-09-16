using System.Globalization;
using Jason.Runtime.Plugins.Registry;
using Jason.Runtime.Routing;

namespace Jason.Runtime.Tests.Routing;

/// <summary>
/// One active snapshot behind one reference, swapped whole. A claim that has already taken its snapshot keeps
/// resolving against it, so what an activation decides never reaches work that is already on its way.
/// </summary>
public class RouteRegistryTests
{
    [Fact]
    public void A_runtime_starts_routing_nothing_anywhere()
    {
        var plugins = new PluginRegistry(TimeProvider.System);
        var registry = new RouteRegistry(TimeProvider.System, plugins);

        Assert.StartsWith("rts_", registry.Snapshot.Id, StringComparison.Ordinal);
        Assert.Null(registry.Snapshot.Global.Default);
        Assert.Empty(registry.Snapshot.Global.Operations);
        Assert.Empty(registry.Snapshot.Campaigns);
    }

    /// <summary>A route snapshot is always pinned to the plugin snapshot it belongs to, from the first moment.</summary>
    [Fact]
    public void The_snapshot_a_runtime_starts_with_names_the_plugin_snapshot_it_started_with()
    {
        var plugins = new PluginRegistry(TimeProvider.System);

        Assert.Equal(plugins.Snapshot.Id, new RouteRegistry(TimeProvider.System, plugins).Snapshot.PluginSnapshotId);
    }

    [Fact]
    public void A_snapshot_taken_before_an_activation_is_not_changed_by_it()
    {
        var started = DateTimeOffset.Parse("2026-09-15T10:00:00Z", CultureInfo.InvariantCulture);
        var clock = new FixedClock(started);
        var registry = new RouteRegistry(clock, new PluginRegistry(clock));
        var before = registry.Snapshot;

        clock.Advance(TimeSpan.FromMinutes(1));
        var next = RouteSnapshot.Empty(clock.GetUtcNow(), "snp_01K5DDDDDDDDDDDDDDDDDDDDDD");
        registry.Replace(next);

        Assert.Same(next, registry.Snapshot);
        Assert.NotEqual(before.Id, registry.Snapshot.Id);
        Assert.Equal(started, before.ActivatedAt);
    }
}
