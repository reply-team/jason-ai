using System.Globalization;
using Jason.Contracts.Api;
using Jason.Contracts.Ids;
using Jason.Contracts.Plugins;
using Jason.Runtime.Configuration;
using Jason.Runtime.Persistence;
using Jason.Runtime.Plugins.Registry;
using Jason.Runtime.Plugins.Subscriptions;
using Jason.Runtime.Tests.Dispatch;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Jason.Runtime.Tests.Plugins.Subscriptions;

/// <summary>
/// The dispatcher's harness with subscribers: plugins granted journal kinds, the chains a load froze for them, each kind's
/// position as a database holds it, and lines written straight into the journal, so a test says exactly which actor and
/// which work item a line names.
/// </summary>
internal static class SubscriptionsBed
{
    public const string Failed = "workitem_failed";

    public const string Requested = "approval_requested";

    public const string Created = "campaign_created";

    public static readonly DateTime Noon = new(2026, 11, 4, 12, 0, 0, DateTimeKind.Utc);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>A subscriber granted the kinds it is given.</summary>
    public static LoadedPlugin Subscriber(string id, params string[] kinds) => Subscriber(id, PluginStatus.Valid, kinds);

    public static LoadedPlugin Subscriber(string id, PluginStatus status, params string[] kinds) =>
        TestPlugins.Loaded(id, kind: PluginKind.Subscriber, status: status) with
        {
            Grants = ResolvedGrants.None with { Subscriptions = kinds },
        };

    /// <summary>
    /// The plugins in force and the chains a load froze for them: the settings' order first where one is given, then the
    /// rest of a kind's subscribers by id.
    /// </summary>
    public static PluginSnapshot Load(DispatchHarness harness, IReadOnlyDictionary<string, string[]>? order, params LoadedPlugin[] plugins)
    {
        var snapshot = new PluginSnapshot(PublicId.New(PluginProtocol.SnapshotIdPrefix), Noon, SnapshotSource.Startup, plugins);
        var settings = new SubscriptionsOptions();
        foreach (var (kind, ids) in order ?? new Dictionary<string, string[]>())
        {
            settings.Order[kind] = [.. ids];
        }

        var candidate = SubscriptionChains.Build(snapshot, settings);
        var frozen = snapshot with { Subscriptions = candidate.Chains ?? throw new InvalidOperationException("The order names a plugin the load would refuse.") };
        harness.Resolve<PluginRegistry>().Replace(frozen, new ReloadReport(Noon, SnapshotSource.Startup, Activated: true, [], []));
        return frozen;
    }

    public static PluginSnapshot Load(DispatchHarness harness, params LoadedPlugin[] plugins) => Load(harness, null, plugins);

    /// <summary>A kind's position, as the load that placed it would have left it.</summary>
    public static async Task CursorAsync(DispatchHarness harness, string kind, int after = 0, long revision = 0)
    {
        await using var db = harness.Open();
        db.SubscriptionCursors.Add(new SubscriptionCursor { Kind = kind, AfterLine = after, Revision = revision, CreatedAt = Noon, UpdatedAt = Noon });
        await db.SaveChangesAsync(Ct);
    }

    /// <summary>
    /// A line of the kind, written straight into the journal by the actor given, naming the work item given and holding
    /// the stored new value given as it is, read or not: its id, which is what a position counts in.
    /// </summary>
    public static int Line(DispatchHarness harness, string kind, string actorType = "system", string actorId = "runtime", string? workItem = null, string? newJson = null)
    {
        using var connection = new SqliteConnection($"Data Source={harness.DatabaseFile}");
        connection.Open();
        if (newJson is not null)
        {
            // A stored value is written as given, the way an edit made with the columns' own checks turned off would.
            using var pragma = connection.CreateCommand();
            pragma.CommandText = "PRAGMA ignore_check_constraints = ON;";
            pragma.ExecuteNonQuery();
        }

        using var command = connection.CreateCommand();
        command.CommandText =
            "INSERT INTO journal (public_id, ts, actor_type, actor_id, kind, key, work_item_id, new_json) VALUES ($id, $ts, $type, $actor, $kind, NULL, $item, $new); SELECT last_insert_rowid();";
        command.Parameters.AddWithValue("$id", PublicId.New("jrn"));
        command.Parameters.AddWithValue("$ts", Noon.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$type", actorType);
        command.Parameters.AddWithValue("$actor", actorId);
        command.Parameters.AddWithValue("$kind", kind);
        command.Parameters.AddWithValue("$item", (object?)workItem ?? DBNull.Value);
        command.Parameters.AddWithValue("$new", (object?)newJson ?? DBNull.Value);
        return Convert.ToInt32(command.ExecuteScalar(), CultureInfo.InvariantCulture);
    }

    /// <summary>Lines of the kinds given, in that order, each written by the runtime.</summary>
    public static int[] Lines(DispatchHarness harness, params string[] kinds) => [.. kinds.Select(kind => Line(harness, kind))];

    public static async Task<string> PublicIdOfAsync(DispatchHarness harness, int line)
    {
        await using var db = harness.Open();
        return await db.Journal.AsNoTracking().Where(entry => entry.Id == line).Select(entry => entry.PublicId).SingleAsync(Ct);
    }

    /// <summary>Each kind's position as <c>kind|after_line|revision</c>, by kind.</summary>
    public static async Task<List<string>> PositionsAsync(DispatchHarness harness)
    {
        await using var db = harness.Open();
        return [.. (await db.SubscriptionCursors.AsNoTracking().OrderBy(cursor => cursor.Kind).ToListAsync(Ct))
            .Select(cursor => string.Create(CultureInfo.InvariantCulture, $"{cursor.Kind}|{cursor.AfterLine}|{cursor.Revision}"))];
    }

    /// <summary>A runner of a runtime that has just started over the same database: nothing in flight, nothing in memory.</summary>
    public static SubscriptionRunner NewRunner(DispatchHarness harness) => new(
        harness.Resolve<LiveSettings<SubscriptionsOptions>>(),
        harness.Scopes,
        harness.Resolve<PluginRegistry>(),
        harness.Clock,
        harness.Resolve<ILogger<SubscriptionRunner>>());

    /// <summary>The journal's lines of the kind, oldest first.</summary>
    public static async Task<List<JournalEntry>> JournalAsync(DispatchHarness harness, string kind)
    {
        await using var db = harness.Open();
        return await db.Journal.AsNoTracking().Where(entry => entry.Kind == kind).OrderBy(entry => entry.Id).ToListAsync(Ct);
    }

    /// <summary>A work item of a new campaign, created by the actor type given: its public id.</summary>
    public static async Task<string> WorkItemAsync(DispatchHarness harness, ActorType createdBy)
    {
        await using var db = harness.Open();
        var campaign = WorkItemFactory.NewCampaign(now: Noon);
        var item = WorkItemFactory.NewAiRole(campaign, now: Noon, configure: item =>
        {
            item.CreatedByType = createdBy;
            item.CreatedById = createdBy == ActorType.Plugin ? "watcher" : "rol_01K0maker";
        });
        db.Campaigns.Add(campaign);
        db.WorkItems.Add(item);
        await db.SaveChangesAsync(Ct);
        return item.PublicId;
    }

    public static async Task<List<SubscriptionRun>> RunsAsync(DispatchHarness harness)
    {
        await using var db = harness.Open();
        return await db.SubscriptionRuns.AsNoTracking().OrderBy(run => run.Id).ToListAsync(Ct);
    }
}
