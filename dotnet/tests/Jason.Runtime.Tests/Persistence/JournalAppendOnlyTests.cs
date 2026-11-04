using System.Text.Json.Nodes;
using Jason.Contracts.Api;
using Jason.Runtime.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Jason.Runtime.Tests.Persistence;

public class JournalAppendOnlyTests
{
    [Fact]
    [Critical(Family.P1, "the journal's triggers")]
    public void Raw_update_and_delete_are_refused_by_the_triggers()
    {
        using var database = new TestDatabase();
        using (var db = database.Open())
        {
            db.Journal.Add(new JournalEntry { PublicId = "jrn_A", Ts = DateTime.UtcNow, ActorType = ActorType.Human, Kind = "note", Reason = "first" });
            db.SaveChanges();
        }

        using var connection = new SqliteConnection($"Data Source={database.File}");
        connection.Open();
        using var update = connection.CreateCommand();
        update.CommandText = "UPDATE journal SET reason = 'rewritten' WHERE public_id = 'jrn_A'";
        var ex = Assert.Throws<SqliteException>(() => update.ExecuteNonQuery());
        Assert.Contains("append-only", ex.Message, StringComparison.Ordinal);

        using var delete = connection.CreateCommand();
        delete.CommandText = "DELETE FROM journal WHERE public_id = 'jrn_A'";
        Assert.Throws<SqliteException>(() => delete.ExecuteNonQuery());
    }

    [Fact]
    [Critical(Family.P1, "the EF interceptor")]
    public void Ef_core_refuses_to_modify_or_remove_journal_rows_before_reaching_the_database()
    {
        using var database = new TestDatabase();
        using var db = database.Open();
        var entry = new JournalEntry { PublicId = "jrn_B", Ts = DateTime.UtcNow, ActorType = ActorType.Human, Kind = "note" };
        db.Journal.Add(entry);
        db.SaveChanges();

        db.Entry(entry).Property(e => e.Reason).CurrentValue = "changed";
        Assert.Throws<InvalidOperationException>(() => db.SaveChanges());

        db.ChangeTracker.Clear();
        var again = db.Journal.Single(e => e.PublicId == "jrn_B");
        db.Journal.Remove(again);
        Assert.Throws<InvalidOperationException>(() => db.SaveChanges());
    }

    [Fact]
    public void Entries_may_be_global_or_belong_to_a_campaign()
    {
        using var database = new TestDatabase();
        using var db = database.Open();
        var now = DateTime.UtcNow;
        var campaign = new Campaign { PublicId = "cmp_J", Name = "j", ProjectId = DefaultProject.Id, CreatedAt = now, UpdatedAt = now };
        db.Campaigns.Add(campaign);
        db.SaveChanges();

        db.Journal.Add(new JournalEntry { PublicId = "jrn_C", Ts = now, ActorType = ActorType.System, ActorId = "runtime", Kind = "suppression_added", Key = "email", New = new JsonObject { ["channel"] = "email", ["value"] = "ada@example.com" } });
        db.Journal.Add(new JournalEntry { PublicId = "jrn_D", Ts = now, ActorType = ActorType.Role, ActorId = "planner", Kind = "campaign_created", Campaign = campaign, Key = "name", New = JsonValue.Create("j") });
        db.SaveChanges();
        db.ChangeTracker.Clear();

        Assert.Null(db.Journal.Single(e => e.PublicId == "jrn_C").CampaignId);
        Assert.Equal(campaign.Id, db.Journal.Single(e => e.PublicId == "jrn_D").CampaignId);
    }

    /// <summary>
    /// The first of the three: the Runtime API's operations, all of them named in one list, hold no journal verb but the
    /// two that leave what is there as it was. An entry is appended and read; any other verb, whatever it is called, is
    /// taken to change what is there until this list says otherwise.
    /// </summary>
    [Fact]
    [Critical(Family.P1, "no API path rewrites a journal entry")]
    public void No_operation_of_the_runtime_api_rewrites_or_removes_a_journal_entry()
    {
        var operations = OperationsOf(typeof(Operations));

        Assert.NotEmpty(operations);
        var offending = RewritingTheJournal(operations);
        Assert.True(offending.Count == 0, $"These operations would rewrite or remove a journal entry: {string.Join(", ", offending)}");
    }

    /// <summary>The same reading of a catalogue that does hold such operations names them, and only them, a verb no list foresaw included.</summary>
    [Fact]
    public void A_catalogue_with_an_operation_that_rewrites_the_journal_is_caught()
    {
        var offending = RewritingTheJournal(OperationsOf(typeof(RewritingCatalogue)));

        Assert.True(
            offending.Order(StringComparer.Ordinal).SequenceEqual(["journal.delete", "journal.purge", "journal.update"], StringComparer.Ordinal),
            $"The reading named: {string.Join(", ", offending)}");
    }

    private static List<string> OperationsOf(Type catalogue) =>
    [
        .. catalogue
            .GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
            .Where(field => field.IsLiteral && field.FieldType == typeof(string))
            .Select(field => (string)field.GetRawConstantValue()!),
    ];

    private static List<string> RewritingTheJournal(IEnumerable<string> operations) =>
        [.. operations.Where(name => name.Split('.') is ["journal", var verb] && verb is not ("append" or "list"))];

    /// <summary>A stand-in for the operations list, with three paths that would rewrite an entry beside the two that do not.</summary>
    private static class RewritingCatalogue
    {
        public const string Append = "journal.append";
        public const string Update = "journal.update";
        public const string Delete = "journal.delete";
        public const string Purge = "journal.purge";
        public const string List = "journal.list";
        public const string CampaignUpdate = "campaign.update";
    }
}
