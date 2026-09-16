using Jason.Contracts.Plugins;
using Jason.Runtime.Configuration;
using Jason.Runtime.Journal;
using Jason.Runtime.Plugins.Registry;
using Jason.Runtime.Routing;
using Microsoft.Extensions.Logging.Abstractions;

namespace Jason.Runtime.Tests.Plugins;

/// <summary>
/// The order of a reload's two effects. Swapping the snapshot is an in-memory act that cannot fail; writing the
/// chronicle entry can. So the record comes first: a reload that could not be written down leaves the previous
/// snapshot active and can simply be repeated, instead of a new snapshot running with no trace of when it began.
/// </summary>
public class ReloadOrderingTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_reload_whose_record_cannot_be_written_leaves_the_previous_snapshot_active()
    {
        using var dir = new TempDataDir();
        using var database = new TestDatabase();
        TestPlugins.Write(dir.Paths, "fake", TestPlugins.Manifest("fake"), "export function invoke() { return { result: {} }; }");
        var registry = new PluginRegistry(TimeProvider.System);
        var before = registry.Snapshot;
        var loader = new PluginLoader(
            dir.Paths,
            new ExecutableResolver(new TestSearchPath()),
            new TestOptionsMonitor<PluginsOptions>(new PluginsOptions()),
            TimeProvider.System,
            NullLogger<PluginLoader>.Instance);

        // A context that can no longer write anything stands in for a database that refuses the save.
        var db = database.Open();
        await db.DisposeAsync();
        var routes = new RouteRegistry(TimeProvider.System, registry);
        var service = new PluginService(
            db,
            new JournalWriter(TimeProvider.System),
            registry,
            loader,
            routes,
            new RouteActivator(db, new TestOptionsMonitor<RoutesOptions>(new RoutesOptions()), routes, TimeProvider.System),
            new ReloadGate(),
            NullLogger<PluginService>.Instance);

        await Assert.ThrowsAsync<ObjectDisposedException>(() => service.ReloadAsync(new PluginReloadRequest(null, null), Ct));

        Assert.Same(before, registry.Snapshot);
        Assert.Null(registry.LastReload);
    }
}
