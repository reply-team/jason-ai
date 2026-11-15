using Jason.Runtime.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;

namespace Jason.Runtime.Tests.Persistence;

/// <summary>
/// How far the bounce step has read each campaign's chronicle, and the index that says a send's bounce once. An
/// installation upgrading into the step owes no read to a bounce said before the upgrade: every campaign starts level with
/// the chronicle as it stands, and a campaign made later starts at nothing. A second bounce line under one send's key in
/// one campaign is refused; the same key in another campaign, and every other kind, are not.
/// </summary>
public class BounceStepSchemaTests
{
    private const string PreviousMigration = "20261114190252_ProviderBlockChannels";

    [Fact]
    public void Migration_BounceStep_StartsAtJournalMax_AndSaysABounceOncePerCampaign()
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
              ('cmp_live', 'a', 'active', '{}', '2026-11-01T00:00:00Z', '2026-11-01T00:00:00Z', (SELECT id FROM projects WHERE is_default = 1)),
              ('cmp_idle', 'b', 'draft', '{}', '2026-11-01T00:00:00Z', '2026-11-01T00:00:00Z', (SELECT id FROM projects WHERE is_default = 1));
            INSERT INTO journal (public_id, ts, actor_type, actor_id, kind, campaign_id, key) VALUES
              ('jrn_1', '2026-11-01T00:00:00Z', 'system', 'dispatcher', 'provider_event_observed', 1, 'k1'),
              ('jrn_2', '2026-11-01T00:00:01Z', 'human', NULL, 'campaign_created', 2, NULL);
            """);

        using (var db = new JasonDbContext(options))
        {
            db.Database.Migrate();
        }

        // Level with the chronicle: a bounce said before the upgrade is history, not a read the step owes.
        Assert.Equal(["cmp_idle|2", "cmp_live|2"], Rows(dir.Paths.DatabaseFile, "SELECT public_id, bounce_watermark FROM campaigns ORDER BY public_id"));

        // A campaign made after the upgrade starts at nothing: the column's default, which the model states too.
        Execute(
            dir.Paths.DatabaseFile,
            "INSERT INTO campaigns (public_id, name, status, context_json, created_at, updated_at, project_id) VALUES ('cmp_new', 'c', 'draft', '{}', '2026-11-02T00:00:00Z', '2026-11-02T00:00:00Z', (SELECT id FROM projects WHERE is_default = 1));");
        Assert.Equal(["0"], Rows(dir.Paths.DatabaseFile, "SELECT bounce_watermark FROM campaigns WHERE public_id = 'cmp_new'"));
        using (var db = new JasonDbContext(options))
        {
            var column = db.Model.FindEntityType(typeof(Campaign))!.FindProperty(nameof(Campaign.BounceWatermark))!;
            Assert.Equal("bounce_watermark", column.GetColumnName());
            Assert.Equal(0, column.GetDefaultValue());
        }

        // One bounce line per send in a campaign; the same send's key in another campaign, and a line of another kind
        // under that key, are kept.
        Execute(
            dir.Paths.DatabaseFile,
            """
            INSERT INTO journal (public_id, ts, actor_type, actor_id, kind, campaign_id, key) VALUES
              ('jrn_3', '2026-11-02T00:00:00Z', 'system', 'dispatcher', 'message_bounce_observed', 1, 'wi_send'),
              ('jrn_4', '2026-11-02T00:00:01Z', 'system', 'dispatcher', 'message_bounce_observed', 2, 'wi_send'),
              ('jrn_5', '2026-11-02T00:00:02Z', 'system', 'dispatcher', 'message_sent_observed', 1, 'wi_send');
            """);
        var second = Assert.Throws<SqliteException>(() => Execute(
            dir.Paths.DatabaseFile,
            "INSERT INTO journal (public_id, ts, actor_type, actor_id, kind, campaign_id, key) VALUES ('jrn_6', '2026-11-02T00:00:03Z', 'system', 'dispatcher', 'message_bounce_observed', 1, 'wi_send');"));
        Assert.Contains("UNIQUE constraint failed", second.Message, StringComparison.Ordinal);
        Assert.Equal(
            ["ix_journal_campaign_kind_key_bounces|kind = 'message_bounce_observed'"],
            Rows(dir.Paths.DatabaseFile, "SELECT name, substr(sql, instr(sql, 'WHERE ') + 6) FROM sqlite_master WHERE type = 'index' AND name = 'ix_journal_campaign_kind_key_bounces'"));
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
