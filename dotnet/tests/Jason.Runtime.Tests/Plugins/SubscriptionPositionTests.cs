using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Jason.Contracts.Api;
using Jason.Contracts.Discovery;
using Jason.Contracts.Json;
using Jason.Contracts.Operations;
using Jason.Contracts.Plugins;
using Jason.Runtime.Tests.Routing;
using Microsoft.Data.Sqlite;

namespace Jason.Runtime.Tests.Plugins;

/// <summary>
/// A subscribed kind's position is kept by the act that freezes the chains, in the same save as its activation line: a
/// kind's first subscriber places it at the journal's head, a load that keeps a subscriber keeps it where it stands, a
/// load that leaves none removes it, and a load that is rejected touches none.
/// </summary>
public class SubscriptionPositionTests
{
    private const string Failed = "workitem_failed";

    private const string Created = "campaign_created";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>
    /// The head is the journal as it stood before the activation's own line, so a subscriber to the activation's kind
    /// sees that line; a kind the load did not touch keeps its position.
    /// </summary>
    [Fact]
    public async Task A_kind_s_first_subscriber_places_its_position_at_the_journal_s_head()
    {
        await using var api = await StartAsync(paths =>
        {
            WriteSubscriber(paths, "watcher", Failed);
            Settings(paths, settings => Grant(settings, "watcher", Failed));
        });
        var startup = Lines(api, "SELECT id FROM journal WHERE kind = 'plugins_reloaded' ORDER BY id");
        Assert.Equal(["1"], startup);
        Assert.Equal([$"{Failed}|0|0"], Positions(api));

        WriteSubscriber(api.Paths, "alpha", Created);
        Settings(api.Paths, settings => Grant(settings, "alpha", Created));
        await api.PostOkAsync<PluginRegistryDto>(Operations.PluginReload, new { }, Ct);

        Assert.Equal(["1", "2"], Lines(api, "SELECT id FROM journal WHERE kind = 'plugins_reloaded' ORDER BY id"));
        Assert.Equal([$"{Created}|1|0", $"{Failed}|0|0"], Positions(api));
    }

    [Fact]
    public async Task A_load_that_keeps_a_subscriber_keeps_the_kind_s_position_where_it_stands()
    {
        await using var api = await StartAsync(paths =>
        {
            WriteSubscriber(paths, "watcher", Failed);
            Settings(paths, settings => Grant(settings, "watcher", Failed));
        });
        Execute(api, $"UPDATE subscription_cursors SET after_line = 7, revision = 3 WHERE kind = '{Failed}';");

        WriteSubscriber(api.Paths, "alpha", Failed);
        Settings(api.Paths, settings => Grant(settings, "alpha", Failed));
        await api.PostOkAsync<PluginRegistryDto>(Operations.PluginReload, new { }, Ct);

        Assert.Equal([$"{Failed}|7|3"], Positions(api));
    }

    /// <summary>A kind left with no subscriber loses its position, so a subscriber that returns starts at the head again.</summary>
    [Fact]
    public async Task A_load_that_leaves_a_kind_no_subscriber_removes_its_position()
    {
        await using var api = await StartAsync(paths =>
        {
            WriteSubscriber(paths, "watcher", Failed);
            Settings(paths, settings => Grant(settings, "watcher", Failed));
        });
        Execute(api, $"UPDATE subscription_cursors SET after_line = 7, revision = 3 WHERE kind = '{Failed}';");

        Settings(api.Paths, settings => Grant(settings, "watcher"));
        await api.PostOkAsync<PluginRegistryDto>(Operations.PluginReload, new { }, Ct);
        Assert.Empty(Positions(api));

        Settings(api.Paths, settings => Grant(settings, "watcher", Failed));
        await api.PostOkAsync<PluginRegistryDto>(Operations.PluginReload, new { }, Ct);
        Assert.Equal([$"{Failed}|2|0"], Positions(api));
    }

    [Fact]
    public async Task A_rejected_reload_touches_no_position()
    {
        await using var api = await StartAsync(paths =>
        {
            WriteSubscriber(paths, "watcher", Failed);
            Settings(paths, settings => Grant(settings, "watcher", Failed));
        });
        Execute(api, $"UPDATE subscription_cursors SET after_line = 7, revision = 3 WHERE kind = '{Failed}';");

        WriteSubscriber(api.Paths, "alpha", Created);
        Settings(api.Paths, settings =>
        {
            Grant(settings, "alpha", Created);
            Grant(settings, "watcher");
            Order(settings, Created, "gone");
        });
        var error = await api.PostErrorAsync(Operations.PluginReload, new { }, HttpStatusCode.Conflict, Ct);

        Assert.Equal("plugin_reload_rejected", error.Code);
        Assert.Equal([$"{Failed}|7|3"], Positions(api));
    }

    [Fact]
    public async Task A_rejected_start_places_no_position()
    {
        await using var api = await StartAsync(paths =>
        {
            WriteSubscriber(paths, "watcher", Failed);
            Settings(paths, settings =>
            {
                Grant(settings, "watcher", Failed);
                Order(settings, Failed, "gone");
            });
        });

        Assert.Empty(Positions(api));
        Assert.Empty(Lines(api, "SELECT id FROM journal"));
    }

    private static Task<RuntimeApiFixture> StartAsync(Action<JasonPaths> prepare) =>
        RouteActivationTests.StartAsync(prepare: prepare);

    private static List<string> Positions(RuntimeApiFixture api) =>
        Lines(api, "SELECT kind, after_line, revision FROM subscription_cursors ORDER BY kind");

    private static List<string> Lines(RuntimeApiFixture api, string sql)
    {
        using var connection = new SqliteConnection($"Data Source={api.Paths.DatabaseFile}");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        using var reader = command.ExecuteReader();
        var rows = new List<string>();
        while (reader.Read())
        {
            rows.Add(string.Join("|", Enumerable.Range(0, reader.FieldCount).Select(index => Convert.ToString(reader.GetValue(index), CultureInfo.InvariantCulture))));
        }

        return rows;
    }

    private static void Execute(RuntimeApiFixture api, string sql)
    {
        using var connection = new SqliteConnection($"Data Source={api.Paths.DatabaseFile}");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private static void WriteSubscriber(JasonPaths paths, string id, string kind) =>
        TestPlugins.Write(
            paths,
            id,
            $"""
            manifest_version: 1
            id: {id}
            version: 1.0.0
            kind: subscriber
            contracts:
              protocol: [1]
            subscriptions: [{kind}]

            """,
            "export function invoke() { return { result: { envelope: {} } }; }");

    /// <summary>The settings file merged rather than written over, so the dispatcher stays off and earlier lines stay.</summary>
    private static void Settings(JasonPaths paths, Action<JsonObject> edit)
    {
        Directory.CreateDirectory(paths.ConfigDirectory);
        var settings = File.Exists(paths.UserSettingsFile)
            ? JsonNode.Parse(File.ReadAllText(paths.UserSettingsFile))!.AsObject()
            : JsonNode.Parse(RuntimeApiFixture.DispatcherOff)!.AsObject();
        edit(settings);
        File.WriteAllText(paths.UserSettingsFile, settings.ToJsonString(JasonJson.Options), new UTF8Encoding(false));
    }

    private static void Grant(JsonObject settings, string id, params string[] kinds) =>
        Child(Child(Child(settings, "Plugins"), "Grants"), id)["Subscriptions"] = Array(kinds);

    private static void Order(JsonObject settings, string kind, params string[] ids) =>
        Child(Child(settings, "Subscriptions"), "Order")[kind] = Array(ids);

    private static JsonArray Array(string[] values) => [.. values.Select(value => (JsonNode)JsonValue.Create(value))];

    private static JsonObject Child(JsonObject parent, string name)
    {
        if (parent[name] is JsonObject existing)
        {
            return existing;
        }

        var created = new JsonObject();
        parent[name] = created;
        return created;
    }
}
