using Jason.Contracts.Plugins;
using Jason.Runtime.Configuration;
using Jason.Runtime.Journal;
using Jason.Runtime.Persistence;
using Jason.Runtime.Plugins.Registry;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Jason.Runtime.Tests.Plugins;

/// <summary>
/// The order of a load's two effects, whichever load it is. Swapping the snapshot is an in-memory act that cannot
/// fail; writing the chronicle entry can. So the record comes first: a load that could not be written down leaves
/// the previous snapshot active and can simply be repeated, instead of a new snapshot running with no trace of
/// when it began. The startup load follows exactly the same path as a reload, so it is held to the same order.
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
        var service = new PluginService(db, new JournalWriter(TimeProvider.System), registry, loader, new ReloadGate(), NullLogger<PluginService>.Instance);

        await Assert.ThrowsAsync<ObjectDisposedException>(() => service.ReloadAsync(new PluginReloadRequest(null, null), Ct));

        Assert.Same(before, registry.Snapshot);
        Assert.Null(registry.LastReload);
    }

    /// <summary>
    /// The same order at startup, where nobody is waiting for an answer: the loader swallows the failure so that
    /// a broken plugin never stands between a person and their campaigns, which is exactly what would let a
    /// snapshot go active with no record of it if the swap came first.
    /// </summary>
    [Fact]
    public async Task A_startup_load_whose_record_cannot_be_written_activates_nothing()
    {
        using var dir = new TempDataDir();
        using var database = new TestDatabase();
        TestPlugins.Write(dir.Paths, "fake", TestPlugins.Manifest("fake"), "export function invoke() { return { result: {} }; }");
        var registry = new PluginRegistry(TimeProvider.System);
        var before = registry.Snapshot;

        var db = database.Open();
        await db.DisposeAsync();

        var services = new ServiceCollection();
        services.AddSingleton(new PluginLoader(
            dir.Paths,
            new ExecutableResolver(new TestSearchPath()),
            new TestOptionsMonitor<PluginsOptions>(new PluginsOptions()),
            TimeProvider.System,
            NullLogger<PluginLoader>.Instance));
        services.AddSingleton(new JournalWriter(TimeProvider.System));
        services.AddSingleton(db);
        await using var provider = services.BuildServiceProvider();

        var startup = new PluginStartupLoader(
            provider.GetRequiredService<IServiceScopeFactory>(),
            registry,
            NullLogger<PluginStartupLoader>.Instance);

        await startup.StartAsync(Ct);

        Assert.Same(before, registry.Snapshot);
        Assert.Null(registry.LastReload);
    }
}
