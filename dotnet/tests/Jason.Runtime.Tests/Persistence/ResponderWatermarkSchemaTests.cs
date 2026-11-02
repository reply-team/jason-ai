using Jason.Runtime.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;

namespace Jason.Runtime.Tests.Persistence;

/// <summary>
/// How far the respond step has read each campaign's chronicle. An installation upgrading into it owes no answer to a reply
/// said before the upgrade: every campaign starts level with the chronicle as it stands, and a campaign made later starts
/// at nothing, since it has no older line.
/// </summary>
public class ResponderWatermarkSchemaTests
{
    private const string PreviousMigration = "20261029150321_SentFacts";

    [Fact]
    public void Migration_ResponderWatermark_StartsAtJournalMax()
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
              ('jrn_1', '2026-11-01T00:00:00Z', 'system', 'dispatcher', 'inbound_reply_observed', 1, 'k1'),
              ('jrn_2', '2026-11-01T00:00:01Z', 'system', 'dispatcher', 'inbound_reply_observed', 1, 'k2'),
              ('jrn_3', '2026-11-01T00:00:02Z', 'human', NULL, 'campaign_created', 2, NULL);
            """);

        using (var db = new JasonDbContext(options))
        {
            db.Database.Migrate();
        }

        // Level with the chronicle: the two replies said before the upgrade are the manager's history, not the responder's.
        Assert.Equal(["cmp_idle|3", "cmp_live|3"], Rows(dir.Paths.DatabaseFile, "SELECT public_id, responder_watermark FROM campaigns ORDER BY public_id"));

        // A campaign made after the upgrade starts at nothing: the column's default, which the model states too.
        Execute(
            dir.Paths.DatabaseFile,
            "INSERT INTO campaigns (public_id, name, status, context_json, created_at, updated_at, project_id) VALUES ('cmp_new', 'c', 'draft', '{}', '2026-11-02T00:00:00Z', '2026-11-02T00:00:00Z', (SELECT id FROM projects WHERE is_default = 1));");
        Assert.Equal(["0"], Rows(dir.Paths.DatabaseFile, "SELECT responder_watermark FROM campaigns WHERE public_id = 'cmp_new'"));
        using (var db = new JasonDbContext(options))
        {
            var column = db.Model.FindEntityType(typeof(Campaign))!.FindProperty(nameof(Campaign.ResponderWatermark))!;
            Assert.Equal("responder_watermark", column.GetColumnName());
            Assert.Equal(0, column.GetDefaultValue());
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
