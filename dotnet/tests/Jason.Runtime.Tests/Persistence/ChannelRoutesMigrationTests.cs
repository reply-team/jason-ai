using System.Globalization;
using Jason.Contracts.Api;
using Jason.Contracts.Discovery;
using Jason.Runtime.Persistence;
using Jason.Runtime.Routing;
using Jason.Runtime.Tests.Plugins;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;

namespace Jason.Runtime.Tests.Persistence;

/// <summary>
/// A route may name a channel under its operation. The routes an installation stored before name none, and they keep
/// their meaning: each row is where it was, with its id, its plugin and its binding, and no channel; the unique index over
/// (campaign, operation) is the filtered one over the rows without a channel; and a runtime started on such a database
/// resolves every campaign's work where it resolved it before. Each case seeds a database at the migration before, as an
/// installation upgraded from it holds one.
/// </summary>
public class ChannelRoutesMigrationTests
{
    private const string PreviousMigration = "20261111143412_Audience";

    private const string ThisMigration = "20261112233613_ChannelRoutes";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public void Stored_routes_keep_their_rows_and_name_no_channel_and_the_index_keeps_one_route_per_operation()
    {
        using var dir = new TempDataDir();
        Seed(dir.Paths);
        MigrateToThis(dir.Paths);

        using var connection = Open(dir.Paths);
        Assert.Equal(
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["1"] = "1|default|fake-provider|{\"workspace\":\"west\"}|none",
                ["2"] = "1|campaign.get|other-provider|null|none",
                ["3"] = "2|campaign.get|fake-provider|{\"workspace\":\"east\"}|none",
            },
            Rows(
                connection,
                "SELECT id, campaign_id || '|' || coalesce(operation, 'default') || '|' || plugin_id || '|' || coalesce(binding_json, 'null') || '|' || coalesce(channel, 'none') FROM campaign_routes"));

        // The index over the operation is replaced by the two filtered ones; the default's own index is left as it was.
        Assert.Equal(
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["ix_campaign_routes_one_default_per_campaign"] = "WHERE operation IS NULL",
                ["ix_campaign_routes_one_per_channel"] = "WHERE channel IS NOT NULL",
                ["ix_campaign_routes_one_per_operation"] = "WHERE channel IS NULL",
            },
            Rows(
                connection,
                "SELECT name, CASE WHEN instr(sql, 'WHERE') > 0 THEN substr(sql, instr(sql, 'WHERE')) ELSE 'none' END FROM sqlite_master WHERE type = 'index' AND tbl_name = 'campaign_routes' AND sql IS NOT NULL"));

        // A second route of the same operation, without a channel, is refused as it was before; one with a channel is not.
        var duplicate = Assert.Throws<SqliteException>(() => Execute(
            connection,
            "INSERT INTO campaign_routes (campaign_id, operation, plugin_id, updated_at) VALUES (1, 'campaign.get', 'fake-provider', '2026-11-01')"));
        Assert.Contains("UNIQUE", duplicate.Message, StringComparison.Ordinal);
        Execute(connection, "INSERT INTO campaign_routes (campaign_id, operation, channel, plugin_id, updated_at) VALUES (1, 'message.send', 'email', 'fake-provider', '2026-11-01')");
    }

    /// <summary>
    /// The routes of an upgraded installation resolve as they did: the runtime starts on the database at the migration
    /// before, migrates it, freezes the stored rows, and answers each campaign's operation route and default route at the
    /// scope and the plugin it answered before.
    /// </summary>
    [Fact]
    public async Task An_upgraded_installations_routes_resolve_as_they_did_before()
    {
        string[] campaigns = [];
        await using var api = await RuntimeApiFixture.StartAsync(
            Ct,
            prepare: paths =>
            {
                File.WriteAllText(paths.UserSettingsFile, RuntimeApiFixture.DispatcherOff);
                TestPlugins.InstallFakeProvider(paths);
                TestPlugins.InstallOtherProvider(paths);
                campaigns = Seed(paths);
            },
            configureServices: services => services.AddSingleton(TestPlugins.SearchPath));

        var routed = await ResolveAsync(api, campaigns[0], "campaign.get");
        var defaulted = await ResolveAsync(api, campaigns[0], "campaign.enroll");
        var other = await ResolveAsync(api, campaigns[1], "campaign.get");

        Assert.Equal((TestPlugins.OtherProviderId, RouteScope.CampaignOperation, true), (routed.PluginId, routed.Scope, routed.Usable));
        Assert.Equal((TestPlugins.FakeProviderId, RouteScope.CampaignDefault, true), (defaulted.PluginId, defaulted.Scope, defaulted.Usable));
        Assert.Equal("west", (string?)defaulted.Binding!["workspace"]);
        Assert.Equal((TestPlugins.FakeProviderId, RouteScope.CampaignOperation, true), (other.PluginId, other.Scope, other.Usable));
        Assert.Equal("east", (string?)other.Binding!["workspace"]);
        Assert.All(api.Resolve<RouteRegistry>().Snapshot.Campaigns.Values, set => Assert.Null(set.Channels));
    }

    private static Task<RouteResolutionDto> ResolveAsync(RuntimeApiFixture api, string campaign, string operation) =>
        api.PostOkAsync<RouteResolutionDto>(Operations.RouteResolve, new { campaign_id = campaign, operation }, Ct);

    /// <summary>
    /// A database at the migration before, holding two campaigns and the routes an installation stored then: the first
    /// campaign's default and an operation route, the second's operation route. Answers the campaigns' public ids.
    /// </summary>
    private static string[] Seed(JasonPaths paths)
    {
        Directory.CreateDirectory(paths.StateDirectory);
        using (var db = new JasonDbContext(JasonDbContext.CreateOptions(paths.DatabaseFile)))
        {
            db.GetInfrastructure().GetRequiredService<IMigrator>().Migrate(PreviousMigration);
        }

        // Campaigns through the entity, which the migrations between that one and this leave as they were, and the routes
        // as raw rows, since the entity now has a column the table at that migration does not.
        string[] campaigns;
        using (var db = new JasonDbContext(JasonDbContext.CreateOptions(paths.DatabaseFile)))
        {
            var first = WorkItemFactory.NewCampaign("Upgraded");
            var second = WorkItemFactory.NewCampaign("Upgraded too");
            db.Campaigns.AddRange(first, second);
            db.SaveChanges();
            campaigns = [first.PublicId, second.PublicId];
        }

        using var connection = Open(paths);
        Execute(
            connection,
            """
            INSERT INTO campaign_routes (id, campaign_id, operation, plugin_id, binding_json, updated_at) VALUES
              (1, 1, NULL, 'fake-provider', '{"workspace":"west"}', '2026-11-01 09:00:00'),
              (2, 1, 'campaign.get', 'other-provider', NULL, '2026-11-01 09:00:00'),
              (3, 2, 'campaign.get', 'fake-provider', '{"workspace":"east"}', '2026-11-01 09:00:00');
            """);

        // The contexts above pooled theirs; the runtime that opens this file next must find nothing holding it.
        SqliteConnection.ClearAllPools();
        return campaigns;
    }

    private static void MigrateToThis(JasonPaths paths)
    {
        using var db = new JasonDbContext(JasonDbContext.CreateOptions(paths.DatabaseFile));
        db.GetInfrastructure().GetRequiredService<IMigrator>().Migrate(ThisMigration);
    }

    private static SqliteConnection Open(JasonPaths paths)
    {
        // No pooling: the runtime opens this file next, and a pooled connection would hold it on a machine that locks it.
        var connection = new SqliteConnection(JasonDbContext.ConnectionString(paths.DatabaseFile) + ";Pooling=False");
        connection.Open();
        return connection;
    }

    private static void Execute(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    /// <summary>The first column of every row as the key, the second as the value.</summary>
    private static Dictionary<string, string> Rows(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        using var reader = command.ExecuteReader();
        var rows = new Dictionary<string, string>(StringComparer.Ordinal);
        while (reader.Read())
        {
            rows[Convert.ToString(reader.GetValue(0), CultureInfo.InvariantCulture)!] = reader.GetString(1);
        }

        return rows;
    }
}
