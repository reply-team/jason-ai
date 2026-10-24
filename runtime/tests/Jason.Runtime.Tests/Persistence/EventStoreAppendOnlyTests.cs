using System.Text.Json.Nodes;
using Jason.Runtime.Persistence;
using Microsoft.Data.Sqlite;

namespace Jason.Runtime.Tests.Persistence;

/// <summary>
/// The events store is kept whole, as the journal is, three times over: no operation of the Runtime API changes or
/// removes an event (held where the operations are listed), the runtime itself refuses to before the database is reached,
/// and the database refuses a raw statement that tries. An event is a record of what a provider said, and which campaigns
/// it was found to concern is part of that record.
/// </summary>
public class EventStoreAppendOnlyTests
{
    private static readonly DateTime Noon = new(2026, 10, 18, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    [Critical(Family.P1, "the event store's triggers")]
    public void Raw_update_and_delete_of_an_event_are_refused_by_the_triggers()
    {
        using var database = new TestDatabase();
        Seed(database);

        using var connection = new SqliteConnection($"Data Source={database.File}");
        connection.Open();
        var update = Assert.Throws<SqliteException>(() => Run(connection, "UPDATE events SET kind = 'bounce' WHERE public_id = 'evt_A'"));
        Assert.Contains("events are append-only", update.Message, StringComparison.Ordinal);
        var delete = Assert.Throws<SqliteException>(() => Run(connection, "DELETE FROM events WHERE public_id = 'evt_A'"));
        Assert.Contains("events are append-only", delete.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Raw_update_and_delete_of_an_attribution_are_refused_by_the_triggers()
    {
        using var database = new TestDatabase();
        Seed(database);

        using var connection = new SqliteConnection($"Data Source={database.File}");
        connection.Open();
        var update = Assert.Throws<SqliteException>(() => Run(connection, "UPDATE event_attributions SET rule = 'contact_pin'"));
        Assert.Contains("event attributions are append-only", update.Message, StringComparison.Ordinal);
        var delete = Assert.Throws<SqliteException>(() => Run(connection, "DELETE FROM event_attributions"));
        Assert.Contains("event attributions are append-only", delete.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Ef_core_refuses_to_modify_or_remove_an_event_or_an_attribution_before_reaching_the_database()
    {
        using var database = new TestDatabase();
        Seed(database);
        using var db = database.Open();

        var stored = db.Events.Single(e => e.PublicId == "evt_A");
        db.Entry(stored).Property(e => e.ReadId).CurrentValue = "evr_changed";
        var changed = Assert.Throws<InvalidOperationException>(() => db.SaveChanges());
        Assert.Contains("append-only", changed.Message, StringComparison.Ordinal);

        db.ChangeTracker.Clear();
        db.Events.Remove(db.Events.Single(e => e.PublicId == "evt_A"));
        Assert.Throws<InvalidOperationException>(() => db.SaveChanges());

        db.ChangeTracker.Clear();
        var attribution = db.EventAttributions.Single();
        db.Entry(attribution).Property(a => a.Rule).CurrentValue = "contact_pin";
        Assert.Throws<InvalidOperationException>(() => db.SaveChanges());

        db.ChangeTracker.Clear();
        db.EventAttributions.Remove(db.EventAttributions.Single());
        Assert.Throws<InvalidOperationException>(() => db.SaveChanges());
    }

    [Fact]
    public void An_event_whose_data_is_not_json_is_refused_by_the_database()
    {
        using var database = new TestDatabase();
        Seed(database);

        using var connection = new SqliteConnection($"Data Source={database.File}");
        connection.Open();
        var refused = Assert.Throws<SqliteException>(() => Run(
            connection,
            "INSERT INTO events (public_id, source_id, plugin_id, kind, occurred_at, stored_at, key, data_json) "
            + "VALUES ('evt_B', 1, 'reader', 'bounce', '2026-10-18 12:00:00', '2026-10-18 12:00:00', 'sha256:b', 'not json')"));
        Assert.Contains("ck_events_data_json", refused.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// The first of the three: the Runtime API's operations, all of them named in one list, hold no events verb but the
    /// three that leave what the store keeps as it was. A read of the sources, a request that more be read, and a nudge
    /// to read sooner add to it or leave it; any other verb, whatever it is called, is taken to change what is there
    /// until this list says otherwise.
    /// </summary>
    [Fact]
    [Critical(Family.P1, "no API path rewrites an event")]
    public void No_operation_of_the_runtime_api_rewrites_or_removes_an_event()
    {
        var operations = typeof(Jason.Contracts.Api.Operations)
            .GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
            .Where(field => field.IsLiteral && field.FieldType == typeof(string))
            .Select(field => (string)field.GetRawConstantValue()!)
            .ToList();

        Assert.NotEmpty(operations);
        var offending = operations
            .Where(name => name.Split('.') is [var noun, var verb] && noun is "event" or "events" && verb is not ("sources" or "backfill" or "signal"))
            .ToList();
        Assert.True(offending.Count == 0, $"These operations would rewrite or remove the events store: {string.Join(", ", offending)}");
    }

    /// <summary>One source, one campaign, one event of it and the attribution that says the event concerns the campaign.</summary>
    private static void Seed(TestDatabase database)
    {
        using var db = database.Open();
        var campaign = new Campaign { PublicId = "cmp_E", Name = "e", ProjectId = DefaultProject.Id, CreatedAt = Noon, UpdatedAt = Noon };
        var source = new EventSource { PublicId = "evs_A", PluginId = "reader", BindingIdentity = "none", CreatedAt = Noon };
        db.Campaigns.Add(campaign);
        db.EventSources.Add(source);
        db.SaveChanges();

        var stored = new ProviderEvent
        {
            PublicId = "evt_A",
            SourceId = source.Id,
            PluginId = "reader",
            Kind = "bounce",
            OccurredAt = Noon,
            StoredAt = Noon,
            Key = "sha256:a",
            Data = new JsonObject { ["kind"] = "bounce" },
            ReadId = "evr_A",
        };
        db.Events.Add(stored);
        db.SaveChanges();

        db.EventAttributions.Add(new EventAttribution { EventId = stored.Id, CampaignId = campaign.Id, Rule = EventAttributionRules.CampaignPin });
        db.SaveChanges();
    }

    private static void Run(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }
}
