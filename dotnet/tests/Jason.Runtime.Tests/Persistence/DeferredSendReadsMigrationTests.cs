using Jason.Runtime.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;

namespace Jason.Runtime.Tests.Persistence;

/// <summary>
/// The index that says once that a send did not go. An installation upgrading keeps every line it holds; after the
/// upgrade a second <c>message_unsent_observed</c> line under one send's key in one campaign is refused, and the same key
/// in another campaign, and every other kind under that key, are not.
/// </summary>
public class DeferredSendReadsMigrationTests
{
    private const string PreviousMigration = "20261122075940_DeepReviewGrid";

    [Fact]
    [Critical(Family.P2, BrokenUntouched.Migration)]
    public void Migration_DeferredSendReads_KeepsTheChronicle_AndSaysAnUnsentSendOncePerCampaign()
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
              ('jrn_1', '2026-11-01T00:00:00Z', 'system', 'dispatcher', 'message_sent_observed', 1, 'wi_send');
            """);

        using (var db = new JasonDbContext(options))
        {
            db.Database.Migrate();
        }

        // What the chronicle held is kept as it was.
        Assert.Equal(["jrn_1|message_sent_observed|wi_send"], Rows(dir.Paths.DatabaseFile, "SELECT public_id, kind, key FROM journal ORDER BY id"));

        // One line per send in a campaign; the same send's key in another campaign, and a line of another kind under that
        // key, are kept.
        Execute(
            dir.Paths.DatabaseFile,
            """
            INSERT INTO journal (public_id, ts, actor_type, actor_id, kind, campaign_id, key) VALUES
              ('jrn_2', '2026-11-02T00:00:00Z', 'system', 'dispatcher', 'message_unsent_observed', 1, 'wi_send'),
              ('jrn_3', '2026-11-02T00:00:01Z', 'system', 'dispatcher', 'message_unsent_observed', 2, 'wi_send'),
              ('jrn_4', '2026-11-02T00:00:02Z', 'system', 'dispatcher', 'message_bounce_observed', 1, 'wi_send');
            """);
        var second = Assert.Throws<SqliteException>(() => Execute(
            dir.Paths.DatabaseFile,
            "INSERT INTO journal (public_id, ts, actor_type, actor_id, kind, campaign_id, key) VALUES ('jrn_5', '2026-11-02T00:00:03Z', 'system', 'dispatcher', 'message_unsent_observed', 1, 'wi_send');"));
        Assert.Contains("UNIQUE constraint failed", second.Message, StringComparison.Ordinal);
        Assert.Equal(
            ["ix_journal_campaign_kind_key_unsent|kind = 'message_unsent_observed'"],
            Rows(dir.Paths.DatabaseFile, "SELECT name, substr(sql, instr(sql, 'WHERE ') + 6) FROM sqlite_master WHERE type = 'index' AND name = 'ix_journal_campaign_kind_key_unsent'"));

        // The model states the index the migration made, so a database the model creates holds it too.
        using (var db = new JasonDbContext(options))
        {
            var index = db.Model.FindEntityType(typeof(JournalEntry))!.GetIndexes().Single(candidate => candidate.GetDatabaseName() == "ix_journal_campaign_kind_key_unsent");
            Assert.True(index.IsUnique);
            Assert.Equal("kind = 'message_unsent_observed'", index.GetFilter());
            Assert.Equal(["campaign_id", "kind", "key"], index.Properties.Select(property => property.GetColumnName()));
        }
    }

    /// <summary>Each row a query returns, its columns joined by a bar.</summary>
    private static List<string> Rows(string databaseFile, string sql)
    {
        using var connection = new SqliteConnection($"Data Source={databaseFile}");
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
