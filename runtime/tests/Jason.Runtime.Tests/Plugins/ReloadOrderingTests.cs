using Jason.Contracts.Plugins;
using Jason.Runtime.Configuration;
using Jason.Runtime.Journal;
using Jason.Runtime.Persistence;
using Jason.Runtime.Plugins.Registry;
using Jason.Runtime.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Jason.Runtime.Tests.Plugins;

/// <summary>
/// The order of a load's two effects, whichever load it is. Swapping the snapshot is an in-memory act that cannot
/// fail; writing the chronicle entry can. So the record comes first: a load that could not be written down leaves
/// the previous snapshot active and can simply be repeated, instead of a new snapshot running with no trace of
/// when it began. The startup load follows exactly the same path as a reload, so it is held to the same order.
/// </summary>
/// <remarks>
/// The database here answers every read and refuses only the save. That is the whole difficulty of testing an
/// order: a database that refuses everything fails at the route build, which this load does first, and then the
/// test would pass whichever way round the last two steps are. The failure has to land on the save and nowhere
/// else, or these are tests of nothing.
/// </remarks>
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
        var routes = new RouteRegistry(TimeProvider.System, registry);
        var before = registry.Snapshot;
        var routedBefore = routes.Snapshot;
        var loader = new PluginLoader(
            dir.Paths,
            new ExecutableResolver(new TestSearchPath()),
            new TestOptionsMonitor<PluginsOptions>(new PluginsOptions()),
            TimeProvider.System,
            NullLogger<PluginLoader>.Instance);

        await using var db = RefusingSave(database);
        var service = new PluginService(
            db,
            new JournalWriter(TimeProvider.System),
            registry,
            loader,
            routes,
            new RouteActivator(db, new TestOptionsMonitor<RoutesOptions>(new RoutesOptions()), routes, TimeProvider.System),
            new ReloadGate(),
            NullLogger<PluginService>.Instance);

        await Assert.ThrowsAsync<RefusedSaveException>(() => service.ReloadAsync(new PluginReloadRequest(null, null), Ct));

        Assert.Same(before, registry.Snapshot);
        Assert.Same(routedBefore, routes.Snapshot);
        Assert.Null(registry.LastReload);
    }

    /// <summary>
    /// The same order at startup, where nobody is waiting for an answer: the loader swallows the failure so that
    /// a broken plugin never stands between a person and their campaigns, which is exactly what would let a
    /// snapshot go active with no record of it if the swap came first. Everything the startup load resolves is
    /// registered here, so the only thing it can fall over is the save.
    /// </summary>
    [Fact]
    public async Task A_startup_load_whose_record_cannot_be_written_activates_nothing()
    {
        using var dir = new TempDataDir();
        using var database = new TestDatabase();
        TestPlugins.Write(dir.Paths, "fake", TestPlugins.Manifest("fake"), "export function invoke() { return { result: {} }; }");
        var registry = new PluginRegistry(TimeProvider.System);
        var routes = new RouteRegistry(TimeProvider.System, registry);
        var before = registry.Snapshot;
        var routedBefore = routes.Snapshot;

        await using var db = RefusingSave(database);

        var services = new ServiceCollection();
        services.AddSingleton(new PluginLoader(
            dir.Paths,
            new ExecutableResolver(new TestSearchPath()),
            new TestOptionsMonitor<PluginsOptions>(new PluginsOptions()),
            TimeProvider.System,
            NullLogger<PluginLoader>.Instance));
        services.AddSingleton(new JournalWriter(TimeProvider.System));
        services.AddSingleton(db);
        services.AddSingleton(routes);
        services.AddSingleton(new RouteActivator(
            db,
            new TestOptionsMonitor<RoutesOptions>(new RoutesOptions()),
            routes,
            TimeProvider.System));
        await using var provider = services.BuildServiceProvider();

        var startup = new PluginStartupLoader(
            provider.GetRequiredService<IServiceScopeFactory>(),
            registry,
            NullLogger<PluginStartupLoader>.Instance);

        await startup.StartAsync(Ct);

        Assert.Same(before, registry.Snapshot);
        Assert.Same(routedBefore, routes.Snapshot);
        Assert.Null(registry.LastReload);
    }

    /// <summary>The migrated database of this test, opened so that only <c>SaveChangesAsync</c> fails.</summary>
    private static JasonDbContext RefusingSave(TestDatabase database) =>
        new(new DbContextOptionsBuilder<JasonDbContext>(database.Options)
            .AddInterceptors(new RefusingSaveInterceptor())
            .Options);

    /// <summary>A save that refuses, and nothing else: reads go to the real database and answer.</summary>
    private sealed class RefusingSaveInterceptor : SaveChangesInterceptor
    {
        public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result) =>
            throw new RefusedSaveException();

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default) =>
            throw new RefusedSaveException();
    }

    /// <summary>Distinct from anything the loader, the route build or EF itself could raise, so the assertion names the save.</summary>
    private sealed class RefusedSaveException() : Exception("The database refused the save.");
}
