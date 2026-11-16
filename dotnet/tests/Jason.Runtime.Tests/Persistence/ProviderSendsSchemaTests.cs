using Jason.Runtime.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;

namespace Jason.Runtime.Tests.Persistence;

/// <summary>
/// The index that says a send the provider reported once per campaign. The migration writes no row: it makes one filtered
/// unique index on the journal, and a database that already holds lines of the store's other kinds keeps every one of
/// them. A second send line under one key in one campaign is refused; the same key in another campaign, and a line of
/// another kind under it, are not.
/// </summary>
public class ProviderSendsSchemaTests
{
    private const string PreviousMigration = "20261114215710_BounceStep";

    private const string ThisMigration = "20261116022841_ProviderSends";

    [Fact]
    [Critical(Family.P2, BrokenUntouched.Migration)]
    public void Migration_ProviderSends_KeepsEveryLine_AndSaysASendOncePerCampaign()
    {
        using var dir = new TempDataDir();
        Directory.CreateDirectory(dir.Paths.StateDirectory);
        var options = JasonDbContext.CreateOptions(dir.Paths.DatabaseFile);

        using (var db = new JasonDbContext(options))
        {
            db.GetInfrastructure().GetRequiredService<IMigrator>().Migrate(PreviousMigration);
        }

        Execute(
            dir.Paths.DatabaseFile,
            """
            INSERT INTO campaigns (public_id, name, status, context_json, created_at, updated_at, project_id) VALUES
              ('cmp_one', 'a', 'active', '{}', '2026-11-01T00:00:00Z', '2026-11-01T00:00:00Z', (SELECT id FROM projects WHERE is_default = 1)),
              ('cmp_two', 'b', 'active', '{}', '2026-11-01T00:00:00Z', '2026-11-01T00:00:00Z', (SELECT id FROM projects WHERE is_default = 1));
            INSERT INTO journal (public_id, ts, actor_type, actor_id, kind, campaign_id, key) VALUES
              ('jrn_1', '2026-11-01T00:00:00Z', 'system', 'dispatcher', 'inbound_reply_observed', 1, 'sha256:k'),
              ('jrn_2', '2026-11-01T00:00:01Z', 'system', 'dispatcher', 'provider_event_observed', 1, 'sha256:k'),
              ('jrn_3', '2026-11-01T00:00:02Z', 'system', 'dispatcher', 'message_sent_observed', 1, 'wi_send');
            """);

        // To this migration by name and no further, so a later migration's own change is not read as this one's: what was
        // applied is every migration up to this one, this one last.
        SqliteConnection.ClearAllPools();
        using (var db = new JasonDbContext(options))
        {
            db.GetInfrastructure().GetRequiredService<IMigrator>().Migrate(ThisMigration);
            Assert.Equal(
                [.. db.Database.GetMigrations().TakeWhile(name => name != ThisMigration), ThisMigration],
                db.Database.GetAppliedMigrations());
        }

        // Every line the database held is there, as it was.
        Assert.Equal(
            ["jrn_1|inbound_reply_observed", "jrn_2|provider_event_observed", "jrn_3|message_sent_observed"],
            Rows(dir.Paths.DatabaseFile, "SELECT public_id, kind FROM journal ORDER BY id"));

        // One send line per key in a campaign; the same key in another campaign, and the other kinds under it, are kept.
        Execute(
            dir.Paths.DatabaseFile,
            """
            INSERT INTO journal (public_id, ts, actor_type, actor_id, kind, campaign_id, key) VALUES
              ('jrn_4', '2026-11-02T00:00:00Z', 'system', 'dispatcher', 'provider_send_observed', 1, 'sha256:k'),
              ('jrn_5', '2026-11-02T00:00:01Z', 'system', 'dispatcher', 'provider_send_observed', 2, 'sha256:k');
            """);
        var second = Assert.Throws<SqliteException>(() => Execute(
            dir.Paths.DatabaseFile,
            "INSERT INTO journal (public_id, ts, actor_type, actor_id, kind, campaign_id, key) VALUES ('jrn_6', '2026-11-02T00:00:02Z', 'system', 'dispatcher', 'provider_send_observed', 1, 'sha256:k');"));
        Assert.Contains("UNIQUE constraint failed", second.Message, StringComparison.Ordinal);
        Assert.Equal(
            ["ix_journal_campaign_kind_key_provider_sends|kind = 'provider_send_observed'"],
            Rows(dir.Paths.DatabaseFile, "SELECT name, substr(sql, instr(sql, 'WHERE ') + 6) FROM sqlite_master WHERE type = 'index' AND name = 'ix_journal_campaign_kind_key_provider_sends'"));

        // And the model says the same index, so the next migration EF writes does not undo it.
        using (var db = new JasonDbContext(options))
        {
            var index = db.Model.FindEntityType(typeof(JournalEntry))!.GetIndexes().Single(i => i.GetDatabaseName() == "ix_journal_campaign_kind_key_provider_sends");
            Assert.True(index.IsUnique);
            Assert.Equal("kind = 'provider_send_observed'", index.GetFilter());
        }
    }

    /// <summary>Each row a query returns, its columns joined by a bar.</summary>
    private static List<string> Rows(string databaseFile, string sql)
    {
        using var connection = new SqliteConnection($"Data Source={databaseFile};Pooling=False");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        using var reader = command.ExecuteReader();
        var rows = new List<string>();
        while (reader.Read())
        {
            rows.Add(string.Join("|", Enumerable.Range(0, reader.FieldCount).Select(index => Convert.ToString(reader.GetValue(index), System.Globalization.CultureInfo.InvariantCulture))));
        }

        return rows;
    }

    private static void Execute(string databaseFile, string sql)
    {
        using var connection = new SqliteConnection($"Data Source={databaseFile};Foreign Keys=True");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }
}
