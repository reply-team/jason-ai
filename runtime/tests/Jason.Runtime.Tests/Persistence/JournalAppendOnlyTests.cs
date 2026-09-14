using Jason.Contracts.Api;
using Jason.Runtime.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Jason.Runtime.Tests.Persistence;

public class JournalAppendOnlyTests
{
    [Fact]
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
        var campaign = new Campaign { PublicId = "cmp_J", Name = "j", CreatedAt = now, UpdatedAt = now };
        db.Campaigns.Add(campaign);
        db.SaveChanges();

        db.Journal.Add(new JournalEntry { PublicId = "jrn_C", Ts = now, ActorType = ActorType.System, ActorId = "runtime", Kind = "suppression_added" });
        db.Journal.Add(new JournalEntry { PublicId = "jrn_D", Ts = now, ActorType = ActorType.Role, ActorId = "planner", Kind = "campaign_created", Campaign = campaign });
        db.SaveChanges();
        db.ChangeTracker.Clear();

        Assert.Null(db.Journal.Single(e => e.PublicId == "jrn_C").CampaignId);
        Assert.Equal(campaign.Id, db.Journal.Single(e => e.PublicId == "jrn_D").CampaignId);
    }
}
