using Jason.Runtime.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Jason.Runtime.Tests.Persistence;

/// <summary>
/// A send the provider was seen to make is said once, under the key of the work item that accepted it, and the database
/// is what says so: an index of its own beside the inbound read's and the events store's, so the indexes earlier
/// migrations committed are left as they were.
/// </summary>
public class SentFactsSchemaTests
{
    private const string Name = "ix_journal_campaign_kind_key_sends";

    /// <summary>The index is written twice, in the model and in the migration that built it; nothing else ties them together.</summary>
    [Fact]
    public void The_index_has_its_columns_in_the_model_the_migration_and_the_database()
    {
        string[] columns = ["campaign_id", "kind", "key"];
        const string filter = "kind = 'message_sent_observed'";

        using var database = new TestDatabase();
        using var db = database.Open();
        var index = db.Model.FindEntityType(typeof(JournalEntry))!.GetIndexes().Single(index => index.GetDatabaseName() == Name);
        Assert.True(index.IsUnique);
        Assert.Equal(filter, index.GetFilter());
        Assert.Equal(columns, index.Properties.Select(property => property.GetColumnName()));

        var migration = File.ReadAllText(Directory.GetFiles(MigrationsDirectory(), "*_SentFacts.cs").Single());
        var start = migration.IndexOf($"name: \"{Name}\"", StringComparison.Ordinal);
        Assert.True(start >= 0, $"The migration creates no index named {Name}.");
        var call = migration[start..migration.IndexOf(");", start, StringComparison.Ordinal)];
        Assert.Contains("table: \"journal\"", call, StringComparison.Ordinal);
        Assert.Contains("new[] { \"campaign_id\", \"kind\", \"key\" }", call, StringComparison.Ordinal);
        Assert.Contains("unique: true", call, StringComparison.Ordinal);
        Assert.Contains($"filter: \"{filter}\"", call, StringComparison.Ordinal);

        Assert.Equal(columns, Rows(database.File, $"SELECT name FROM pragma_index_info('{Name}') ORDER BY seqno"));
    }

    /// <summary>
    /// A second line of the kind under a key the campaign already holds is refused. The other said-once kinds may hold the
    /// same key in the same campaign: each is held by its own index.
    /// </summary>
    [Fact]
    public void A_campaign_says_a_send_went_out_once()
    {
        using var database = new TestDatabase();
        Execute(
            database.File,
            """
            INSERT INTO campaigns (public_id, name, status, context_json, created_at, updated_at, project_id) VALUES
              ('cmp_A', 'a', 'active', '{}', '2026-10-29T00:00:00Z', '2026-10-29T00:00:00Z', (SELECT id FROM projects WHERE is_default = 1));
            INSERT INTO journal (public_id, ts, actor_type, actor_id, kind, campaign_id, key) VALUES
              ('jrn_1', '2026-10-29T00:00:00Z', 'system', 'dispatcher', 'message_sent_observed', 1, 'wi_01JB6K8TQ2W9V4MZ0C3Y7H5NRX'),
              ('jrn_2', '2026-10-29T00:00:00Z', 'system', 'dispatcher', 'inbound_reply_observed', 1, 'wi_01JB6K8TQ2W9V4MZ0C3Y7H5NRX'),
              ('jrn_3', '2026-10-29T00:00:00Z', 'system', 'dispatcher', 'provider_event_observed', 1, 'wi_01JB6K8TQ2W9V4MZ0C3Y7H5NRX');
            """);

        var refused = Assert.Throws<SqliteException>(() => Execute(
            database.File,
            "INSERT INTO journal (public_id, ts, actor_type, actor_id, kind, campaign_id, key) VALUES ('jrn_4', '2026-10-29T00:00:01Z', 'system', 'dispatcher', 'message_sent_observed', 1, 'wi_01JB6K8TQ2W9V4MZ0C3Y7H5NRX');"));
        Assert.Contains("UNIQUE", refused.Message, StringComparison.Ordinal);
        Assert.Equal(["3"], Rows(database.File, "SELECT COUNT(*) FROM journal"));
    }

    private static string MigrationsDirectory()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "global.json")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return Path.Combine(directory.FullName, "runtime", "src", "Jason.Runtime", "Persistence", "Migrations");
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
