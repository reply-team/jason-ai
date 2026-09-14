using System.Text.Json.Nodes;
using Jason.Contracts.Api;
using Jason.Runtime.Domain;
using Jason.Runtime.Journal;
using Jason.Runtime.Persistence;

namespace Jason.Runtime.Tests.Journal;

public class JournalWriterTests
{
    private static readonly DateTimeOffset Noon = new(2026, 9, 14, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void An_entry_takes_its_time_from_the_clock_and_its_actor_from_the_caller()
    {
        using var database = new TestDatabase();
        using var db = database.Open();
        var writer = new JournalWriter(new FixedClock(Noon));
        var campaign = new Campaign { PublicId = "cmp_A", Name = "a", CreatedAt = Noon.UtcDateTime, UpdatedAt = Noon.UtcDateTime };
        db.Campaigns.Add(campaign);

        var entry = writer.Append(db, new ActorRef(ActorType.Role, "planner"), JournalKinds.CampaignCreated, campaign, key: "name", old: null, updated: JsonValue.Create("a"), reason: "because");
        db.SaveChanges();

        Assert.StartsWith("jrn_", entry.PublicId, StringComparison.Ordinal);
        Assert.Equal(Noon.UtcDateTime, entry.Ts);
        Assert.Equal(ActorType.Role, entry.ActorType);
        Assert.Equal("planner", entry.ActorId);
        Assert.Equal(campaign.Id, entry.CampaignId);

        var dto = JournalWriter.ToDto(entry, campaign.PublicId);
        Assert.StartsWith("jrn_", dto.Id, StringComparison.Ordinal);
        Assert.Equal(Noon, dto.Ts);
        Assert.Equal(new ActorRef(ActorType.Role, "planner"), dto.Actor);
        Assert.Equal("cmp_A", dto.CampaignId);
        Assert.Equal("name", dto.Key);
        Assert.Null(dto.Old);
        Assert.Equal("a", (string?)dto.New);
        Assert.Equal("because", dto.Reason);
    }

    [Fact]
    public void A_global_entry_needs_no_campaign()
    {
        using var database = new TestDatabase();
        using var db = database.Open();
        var writer = new JournalWriter(new FixedClock(Noon));

        var entry = writer.Append(db, Actors.Runtime, JournalKinds.SuppressionAdded, campaign: null, key: "email");
        db.SaveChanges();

        Assert.Null(entry.CampaignId);
        Assert.Equal(ActorType.System, entry.ActorType);
        Assert.Null(JournalWriter.ToDto(entry, null).CampaignId);
    }

    [Fact]
    public void Kinds_the_runtime_writes_itself_are_reserved_and_well_formed()
    {
        Assert.Contains(JournalKinds.ContextUpdated, JournalKinds.Reserved);
        Assert.Contains(JournalKinds.ContactsRemoved, JournalKinds.Reserved);
        Assert.All(JournalKinds.Reserved, kind => Assert.True(JournalKinds.IsWellFormed(kind), kind));

        Assert.True(JournalKinds.IsWellFormed("plan_revision"));
        Assert.False(JournalKinds.IsWellFormed("Plan-Revision"));
        Assert.False(JournalKinds.IsWellFormed("_leading"));
        Assert.False(JournalKinds.IsWellFormed(string.Empty));
        Assert.False(JournalKinds.IsWellFormed(new string('a', 65)));
    }
}
