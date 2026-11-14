using System.Globalization;
using System.Text;
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
/// A route may name a channel under its operation, and such a route is a row of a table of its own,
/// <c>campaign_channel_routes</c>: the migration creates that table and touches nothing else, so an older executable
/// started on the migrated database reads <c>campaign_routes</c> as it always did. Each case seeds a database at the
/// migration before, as an installation upgraded from it holds one — two campaigns, a default with a binding, an operation
/// route without one and the other campaign's operation route — and reads what the migration left.
/// </summary>
public class ChannelRoutesMigrationTests
{
    private const string PreviousMigration = "20261111143412_Audience";

    private const string ThisMigration = "ChannelRoutes";

    private const string Table = "campaign_channel_routes";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>
    /// The whole start path, the backup first and then the migration: every row of every table is there afterwards with
    /// every value it had; nothing refers to a row that is not there; no table was copied and renamed, so nothing temporary
    /// is left and the routes' id sequence is what it was; every index, trigger, column and reference of the database is
    /// there with the definition it had; and what the migration adds is the new table, exactly.
    /// </summary>
    [Fact]
    public void An_upgraded_installation_keeps_every_row_and_gains_the_channel_table_alone()
    {
        using var dir = Seeded();
        var before = Snapshot(dir.Paths.DatabaseFile);

        var report = new DatabaseMigrator(dir.Paths).Migrate();

        Assert.Equal(AppliedFrom(dir, ThisMigration), report.NewlyApplied);
        using var after = Open(dir.Paths.DatabaseFile);
        foreach (var (table, rows) in before.Rows)
        {
            Assert.Equal(rows, Dump(after, table, before.Columns[table]));
        }

        Assert.Equal(before.Sequence, Rows(after, "SELECT name || '=' || seq FROM sqlite_sequence ORDER BY name"));
        var orphans = Rows(after, "PRAGMA foreign_key_check");
        Assert.True(orphans.Count == 0, $"Rows refer to rows that are not there: {string.Join("; ", orphans)}");
        Assert.Empty(Rows(after, "SELECT name FROM sqlite_master WHERE name LIKE 'ef_temp_%'"));

        var shape = ShapeOf(after);
        var lost = before.Shape.Except(shape).ToList();
        Assert.True(lost.Count == 0, $"The migration lost or changed:{Environment.NewLine}{string.Join(Environment.NewLine, lost)}");
        Assert.Equal(
            [
                "column campaign_channel_routes.binding_json | TEXT notnull=0 default=none pk=0",
                "column campaign_channel_routes.campaign_id | INTEGER notnull=1 default=none pk=0",
                "column campaign_channel_routes.channel | TEXT notnull=1 default=none pk=0",
                "column campaign_channel_routes.id | INTEGER notnull=1 default=none pk=1",
                "column campaign_channel_routes.operation | TEXT notnull=1 default=none pk=0",
                "column campaign_channel_routes.plugin_id | TEXT notnull=1 default=none pk=0",
                "column campaign_channel_routes.updated_at | TEXT notnull=1 default=none pk=0",
                "index campaign_channel_routes.ix_campaign_channel_routes_campaign_id_operation_channel | CREATE UNIQUE INDEX \"ix_campaign_channel_routes_campaign_id_operation_channel\" ON \"campaign_channel_routes\" (\"campaign_id\", \"operation\", \"channel\")",
                "reference campaign_channel_routes.campaign_id | campaigns(id) on delete CASCADE on update NO ACTION",
            ],
            shape.Except(before.Shape).Where(line => TableOf(line) == Table));

        // Later migrations' additions, which the migrator applies in the same run, are theirs; this one adds one table.
        Assert.Equal([Table], Rows(after, "SELECT name FROM sqlite_master WHERE type = 'table'").Except(before.Tables));
        var definition = Assert.Single(Rows(after, $"SELECT sql FROM sqlite_master WHERE type = 'table' AND name = '{Table}'"));
        foreach (var held in new[] { "AUTOINCREMENT", "CONSTRAINT \"ck_campaign_channel_routes_binding_json\" CHECK (binding_json IS NULL OR json_valid(binding_json))" })
        {
            Assert.True(definition.Contains(held, StringComparison.Ordinal), $"The channel routes' table does not hold '{held}':{Environment.NewLine}{definition}");
        }
    }

    /// <summary>
    /// After the migration the database itself holds what the model says of a channel route: one per campaign, operation
    /// and channel; a binding that is JSON or nothing; a campaign that exists; and the rows go with their campaign, as its
    /// operation routes do, while another campaign's stay.
    /// </summary>
    [Fact]
    public void The_channel_table_refuses_what_the_model_refuses_and_goes_with_its_campaign()
    {
        using var dir = Seeded();
        new DatabaseMigrator(dir.Paths).Migrate();
        using var connection = Open(dir.Paths.DatabaseFile);
        Execute(connection, "PRAGMA foreign_keys = 1");
        void Route(int campaign, string channel, string binding) =>
            Execute(connection, $"INSERT INTO {Table} (campaign_id, operation, channel, plugin_id, binding_json, updated_at) VALUES ({campaign}, 'message.send', '{channel}', 'fake-provider', {binding}, '2026-11-12 09:00:00')");
        void Refused(int campaign, string channel, string binding, string because)
        {
            var refused = Assert.Throws<SqliteException>(() => Route(campaign, channel, binding));
            Assert.True(refused.Message.Contains(because, StringComparison.Ordinal), $"A route ({campaign}, {channel}, {binding}) was refused for another reason: {refused.Message}");
        }

        Route(1, "linkedin", "'{\"account\":\"social\"}'");
        Route(1, "email", "NULL");
        Route(2, "linkedin", "NULL");
        Refused(1, "linkedin", "NULL", "UNIQUE");
        Refused(1, "sms", "'not json'", "CHECK constraint failed: ck_campaign_channel_routes_binding_json");
        Refused(99, "linkedin", "NULL", "FOREIGN KEY");

        Execute(connection, "DELETE FROM campaigns WHERE id = 2");

        Assert.Equal(["1|email", "1|linkedin"], Rows(connection, $"SELECT campaign_id, channel FROM {Table} ORDER BY campaign_id, channel"));
        Assert.Equal(["1|default", "1|campaign.get"], Rows(connection, "SELECT campaign_id, coalesce(operation, 'default') FROM campaign_routes ORDER BY campaign_id, operation"));
        Assert.Empty(Rows(connection, "PRAGMA foreign_key_check"));
    }

    /// <summary>
    /// <c>Down</c> drops the channel table, and the channel routes in it — a stated loss — and leaves the installation as
    /// the migration before had it: every row of every other table with every value, and the database's shape, unchanged;
    /// so the operations' own routes and the defaults mean what they meant before any channel route was set.
    /// </summary>
    [Fact]
    public void Down_drops_the_channel_table_and_leaves_the_installation_as_it_stood()
    {
        using var dir = Seeded();
        var before = Snapshot(dir.Paths.DatabaseFile);
        new DatabaseMigrator(dir.Paths).Migrate();
        using (var connection = Open(dir.Paths.DatabaseFile))
        {
            Execute(connection, $"INSERT INTO {Table} (campaign_id, operation, channel, plugin_id, updated_at) VALUES (1, 'message.send', 'linkedin', 'other-provider', '2026-11-12 09:00:00')");
        }

        SqliteConnection.ClearAllPools();
        using (var db = new JasonDbContext(JasonDbContext.CreateOptions(dir.Paths.DatabaseFile)))
        {
            db.GetInfrastructure().GetRequiredService<IMigrator>().Migrate(PreviousMigration);
        }

        using var after = Open(dir.Paths.DatabaseFile);
        Assert.Empty(Rows(after, $"SELECT name FROM sqlite_master WHERE tbl_name = '{Table}'"));
        foreach (var (table, rows) in before.Rows)
        {
            Assert.Equal(rows, Dump(after, table, before.Columns[table]));
        }

        Assert.Equal(before.Shape, ShapeOf(after));
        Assert.Empty(Rows(after, "PRAGMA foreign_key_check"));
    }

    /// <summary>
    /// The routes of an upgraded installation resolve as they did: the runtime starts on the database at the migration
    /// before, migrates it, freezes the stored rows, and answers each campaign's operation route and default route at the
    /// scope and the plugin it answered before, with no channel route anywhere.
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
                campaigns = SeedInto(paths);
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

    /// <summary>The populated installation, at the migration before this one.</summary>
    private static TempDataDir Seeded()
    {
        var dir = new TempDataDir();
        SeedInto(dir.Paths);
        return dir;
    }

    /// <summary>
    /// A database at the migration before, holding two campaigns and the routes an installation stored then: the first
    /// campaign's default and an operation route, the second's operation route. Answers the campaigns' public ids.
    /// </summary>
    private static string[] SeedInto(JasonPaths paths)
    {
        Directory.CreateDirectory(paths.StateDirectory);
        using (var db = new JasonDbContext(JasonDbContext.CreateOptions(paths.DatabaseFile)))
        {
            db.GetInfrastructure().GetRequiredService<IMigrator>().Migrate(PreviousMigration);
        }

        // Campaigns and routes as raw rows, as the installation held them then: the entity has since gained columns a
        // database at the migration before does not have.
        string[] campaigns = ["cmp_01K7QN0000000000000000000A", "cmp_01K7QN0000000000000000000B"];
        using (var connection = Open(paths.DatabaseFile))
        {
            Execute(
                connection,
                """
                INSERT INTO campaigns (id, public_id, name, project_id, status, context_json, created_at, updated_at)
                SELECT 1, 'cmp_01K7QN0000000000000000000A', 'Upgraded', id, 'active', '{}', '2026-11-01 09:00:00', '2026-11-01 09:00:00'
                FROM projects ORDER BY id LIMIT 1;
                INSERT INTO campaigns (id, public_id, name, project_id, status, context_json, created_at, updated_at)
                SELECT 2, 'cmp_01K7QN0000000000000000000B', 'Upgraded too', id, 'active', '{}', '2026-11-01 09:00:00', '2026-11-01 09:00:00'
                FROM projects ORDER BY id LIMIT 1;
                INSERT INTO campaign_routes (id, campaign_id, operation, plugin_id, binding_json, updated_at) VALUES
                  (1, 1, NULL, 'fake-provider', '{"workspace":"west"}', '2026-11-01 09:00:00'),
                  (2, 1, 'campaign.get', 'other-provider', NULL, '2026-11-01 09:00:00'),
                  (3, 2, 'campaign.get', 'fake-provider', '{"workspace":"east"}', '2026-11-01 09:00:00'),
                  (4, 2, 'campaign.enroll', 'fake-provider', NULL, '2026-11-01 09:00:00');
                DELETE FROM campaign_routes WHERE id = 4;
                """);
        }

        // The contexts above pooled theirs; the runtime that opens this file next must find nothing holding it.
        SqliteConnection.ClearAllPools();
        return campaigns;
    }

    /// <summary>What a database holds, read to be compared: every row of every table, their columns, its tables, the id sequences and its shape.</summary>
    private sealed record State(
        Dictionary<string, List<string>> Rows,
        Dictionary<string, List<string>> Columns,
        List<string> Tables,
        List<string> Sequence,
        List<string> Shape);

    private static State Snapshot(string file)
    {
        using var connection = Open(file);
        var tables = Rows(connection, "SELECT name FROM sqlite_master WHERE type = 'table' AND substr(name, 1, 7) <> 'sqlite_' AND substr(name, 1, 4) <> '__EF' ORDER BY name");
        var columns = tables.ToDictionary(table => table, table => Rows(connection, $"SELECT name FROM pragma_table_info('{table}') ORDER BY cid"), StringComparer.Ordinal);
        var rows = tables.ToDictionary(table => table, table => Dump(connection, table, columns[table]), StringComparer.Ordinal);
        foreach (var table in new[] { "campaigns", "campaign_routes" })
        {
            Assert.True(rows[table].Count > 0, $"The seed leaves '{table}' empty, so the migration could not be seen to keep its rows.");
        }

        return new State(
            rows,
            columns,
            Rows(connection, "SELECT name FROM sqlite_master WHERE type = 'table'"),
            Rows(connection, "SELECT name || '=' || seq FROM sqlite_sequence ORDER BY name"),
            ShapeOf(connection));
    }

    /// <summary>
    /// A database's shape, a line a thing, in order: every index and trigger with its definition, every column of every
    /// table with its type, whether it may be null and its default, and every reference a table makes.
    /// </summary>
    private static List<string> ShapeOf(SqliteConnection connection)
    {
        var shape = Rows(connection, "SELECT type || ' ' || tbl_name || '.' || name || ' | ' || sql FROM sqlite_master WHERE type IN ('index', 'trigger') AND sql IS NOT NULL");
        foreach (var table in Rows(connection, "SELECT name FROM sqlite_master WHERE type = 'table' AND substr(name, 1, 7) <> 'sqlite_' AND substr(name, 1, 4) <> '__EF'"))
        {
            shape.AddRange(Rows(connection, $"SELECT 'column {table}.' || name || ' | ' || type || ' notnull=' || \"notnull\" || ' default=' || coalesce(dflt_value, 'none') || ' pk=' || pk FROM pragma_table_info('{table}')"));
            shape.AddRange(Rows(connection, $"SELECT 'reference {table}.' || \"from\" || ' | ' || \"table\" || '(' || \"to\" || ') on delete ' || on_delete || ' on update ' || on_update FROM pragma_foreign_key_list('{table}')"));
        }

        return [.. shape.Order(StringComparer.Ordinal)];
    }

    /// <summary>The table a line of <see cref="ShapeOf"/> names: between its kind and its dot.</summary>
    private static string TableOf(string line) => line[(line.IndexOf(' ', StringComparison.Ordinal) + 1)..line.IndexOf('.', StringComparison.Ordinal)];

    /// <summary>Every row of a table, the columns named, in the order of its key; a value's type is written beside it so a number that became text shows.</summary>
    private static List<string> Dump(SqliteConnection connection, string table, IReadOnlyList<string> columns)
    {
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT {string.Join(", ", columns.Select(column => $"\"{column}\""))} FROM \"{table}\" ORDER BY rowid";
        using var reader = command.ExecuteReader();
        var rows = new List<string>();
        while (reader.Read())
        {
            var row = new StringBuilder(table);
            for (var index = 0; index < reader.FieldCount; index++)
            {
                var value = reader.GetValue(index);
                row.Append('|').Append(columns[index]).Append('=')
                    .Append(value is DBNull ? "null" : $"{value.GetType().Name}:{Convert.ToString(value, CultureInfo.InvariantCulture)}");
            }

            rows.Add(row.ToString());
        }

        return rows;
    }

    /// <summary>
    /// What a start from the migration before this one applies: this migration and every one the tree carries after it,
    /// so the assertion holds on a branch where later migrations follow.
    /// </summary>
    private static List<string> AppliedFrom(TempDataDir dir, string suffix)
    {
        using var db = new JasonDbContext(JasonDbContext.CreateOptions(dir.Paths.DatabaseFile));
        var from = Assert.Single(db.Database.GetMigrations(), name => name.EndsWith("_" + suffix, StringComparison.Ordinal));
        return db.Database.GetMigrations().SkipWhile(name => name != from).ToList();
    }

    private static SqliteConnection Open(string file)
    {
        // No pooling: the runtime opens this file next, and a pooled connection would hold it on a machine that locks it.
        var connection = new SqliteConnection(JasonDbContext.ConnectionString(file) + ";Pooling=False");
        connection.Open();
        return connection;
    }

    private static void Execute(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    /// <summary>Every row, its columns joined by a bar.</summary>
    private static List<string> Rows(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        using var reader = command.ExecuteReader();
        var rows = new List<string>();
        while (reader.Read())
        {
            rows.Add(string.Join('|', Enumerable.Range(0, reader.FieldCount).Select(index => Convert.ToString(reader.GetValue(index), CultureInfo.InvariantCulture))));
        }

        return rows;
    }
}
