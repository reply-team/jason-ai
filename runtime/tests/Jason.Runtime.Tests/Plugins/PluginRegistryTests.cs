using Jason.Contracts.Plugins;
using Jason.Runtime.Plugins.Registry;

namespace Jason.Runtime.Tests.Plugins;

/// <summary>
/// The one reference every reader of the registry goes through. A swap is whole; a rejected load changes the
/// diagnostics and nothing else.
/// </summary>
public class PluginRegistryTests
{
    private static readonly DateTimeOffset Noon = new(2026, 9, 14, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void A_registry_starts_empty_and_says_when()
    {
        var registry = new PluginRegistry(new FixedClock(Noon));

        Assert.StartsWith("snp_", registry.Snapshot.Id, StringComparison.Ordinal);
        Assert.Empty(registry.Snapshot.Plugins);
        Assert.Equal(SnapshotSource.Startup, registry.Snapshot.Source);
        Assert.Equal(Noon.UtcDateTime, registry.Snapshot.LoadedAt);
        Assert.Null(registry.LastReload);
    }

    [Fact]
    public void Activating_a_load_swaps_the_snapshot_and_the_diagnostics_together()
    {
        var registry = new PluginRegistry(new FixedClock(Noon));
        var snapshot = PluginSnapshot.Empty(Noon.UtcDateTime.AddMinutes(1), SnapshotSource.Reload);
        var report = new ReloadReport(Noon.UtcDateTime.AddMinutes(1), SnapshotSource.Reload, true, []);

        registry.Replace(snapshot, report);

        Assert.Same(snapshot, registry.Snapshot);
        Assert.Same(report, registry.LastReload);
    }

    [Fact]
    public void A_rejected_load_leaves_the_active_snapshot_exactly_where_it_was()
    {
        var registry = new PluginRegistry(new FixedClock(Noon));
        var active = PluginSnapshot.Empty(Noon.UtcDateTime, SnapshotSource.Reload);
        registry.Replace(active, new ReloadReport(Noon.UtcDateTime, SnapshotSource.Reload, true, []));
        var rejected = new ReloadReport(Noon.UtcDateTime.AddMinutes(5), SnapshotSource.Reload, false, []);

        registry.Record(rejected);

        Assert.Same(active, registry.Snapshot);
        Assert.Same(rejected, registry.LastReload);
        Assert.False(registry.LastReload!.Activated);
    }
}
