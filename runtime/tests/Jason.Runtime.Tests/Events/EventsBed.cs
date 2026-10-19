using System.Text.Json.Nodes;
using Jason.Contracts.Api;
using Jason.Contracts.Ids;
using Jason.Contracts.Plugins;
using Jason.Runtime.Events;
using Jason.Runtime.Journal;
using Jason.Runtime.Persistence;
using Jason.Runtime.Plugins.Registry;
using Jason.Runtime.Routing;
using Jason.Runtime.Tests.Dispatch;
using Jason.Runtime.Tests.Plugins;
using Microsoft.EntityFrameworkCore;

namespace Jason.Runtime.Tests.Events;

/// <summary>
/// The dispatcher's harness with accounts whose events are read: plugins that declare the events contract, routes that
/// bind their accounts, sources as a database holds them, and the read the scan handed over, read when the test says.
/// </summary>
internal static class EventsBed
{
    public const string Reader = "reader";

    public const int Cadence = 600;

    public static readonly DateTime Noon = new(2026, 10, 18, 12, 0, 0, DateTimeKind.Utc);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>A provider that reads its accounts' events.</summary>
    public static LoadedPlugin EventsPlugin(string id = Reader) =>
        TestPlugins.Loaded(id, operations: ["campaign.get", "conversation.list"], events: [EventsContractVersion]);

    /// <summary>A harness at noon, with the events plugin loaded and its account <c>a</c> bound by the global default route.</summary>
    public static DispatchHarness Create(string account = "a")
    {
        var harness = new DispatchHarness(Noon);
        Load(harness, EventsPlugin());
        Route(harness, Bound(Reader, account));
        return harness;
    }

    public static void Load(DispatchHarness harness, params LoadedPlugin[] plugins) =>
        harness.Resolve<PluginRegistry>().Replace(
            new PluginSnapshot(PublicId.New(PluginProtocol.SnapshotIdPrefix), Noon, SnapshotSource.Startup, plugins),
            new ReloadReport(Noon, SnapshotSource.Startup, Activated: true, [], []));

    /// <summary>The routes in force: a global default, global overrides, and each campaign's own.</summary>
    public static void Route(
        DispatchHarness harness,
        Route? global,
        IReadOnlyDictionary<string, Route>? overrides = null,
        IReadOnlyDictionary<string, RouteSet>? campaigns = null)
    {
        var plugins = harness.Resolve<PluginRegistry>().Snapshot;
        harness.Resolve<RouteRegistry>().Replace(new RouteSnapshot(
            PublicId.New(RouteSnapshot.IdPrefix),
            Noon,
            plugins.Id,
            new RouteSet(global, overrides ?? new Dictionary<string, Route>(StringComparer.Ordinal)),
            campaigns ?? new Dictionary<string, RouteSet>(StringComparer.Ordinal)));
    }

    /// <summary>A route to one account of a plugin, its binding naming that account.</summary>
    public static Route Bound(string plugin, string account) =>
        new(plugin, new JsonObject { ["workspace"] = account }, "sha256:" + account);

    /// <summary>A source as the database holds it, for the account a route bound with <see cref="Bound"/>.</summary>
    public static async Task<EventSource> SourceAsync(
        DispatchHarness harness,
        string account = "a",
        string? cursor = null,
        DateTime? cursorAt = null,
        DateTime? started = null,
        DateTime? succeeded = null,
        string plugin = Reader)
    {
        await using var db = harness.Open();
        var source = new EventSource
        {
            PublicId = PublicId.New("evs"),
            PluginId = plugin,
            BindingIdentity = account == EventSource.NoBinding ? account : "sha256:" + account,
            Cursor = cursor,
            CursorAt = cursorAt,
            CreatedAt = Noon.AddDays(-60),
            ReadStartedAt = started,
            ReadEndedAt = started,
            SucceededAt = succeeded,
        };
        db.EventSources.Add(source);
        await db.SaveChangesAsync(Ct);
        return source;
    }

    public static async Task<EventSource> RowAsync(DispatchHarness harness, int sourceId)
    {
        await using var db = harness.Open();
        return await db.EventSources.AsNoTracking().SingleAsync(source => source.Id == sourceId, Ct);
    }

    public static async Task<List<EventSource>> RowsAsync(DispatchHarness harness)
    {
        await using var db = harness.Open();
        return await db.EventSources.AsNoTracking().OrderBy(source => source.Id).ToListAsync(Ct);
    }

    /// <summary>The events-read lines the journal holds, oldest first.</summary>
    public static async Task<List<JournalEntry>> ReadLinesAsync(DispatchHarness harness)
    {
        await using var db = harness.Open();
        return await db.Journal.AsNoTracking().Where(entry => entry.Kind == JournalKinds.EventsRead).OrderBy(entry => entry.Id).ToListAsync(Ct);
    }

    public static async Task<int> StoredAsync(DispatchHarness harness)
    {
        await using var db = harness.Open();
        return await db.Events.CountAsync(Ct);
    }

    /// <summary>One scan's events step, then the read it handed over, read to its end: null when nothing was handed over.</summary>
    public static async Task<EventReadReport?> ScanAndReadAsync(DispatchHarness harness)
    {
        await harness.EventScanAsync(Ct);
        return await harness.Reader.ReadNextAsync(Ct);
    }

    private const int EventsContractVersion = 1;
}
