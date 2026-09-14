using System.Text.Json.Nodes;
using Jason.Runtime.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Jason.Runtime.Tests.Persistence;

public class ConversionsTests
{
    private enum Sample
    {
        AwaitingApproval,
        Done,
    }

    [Fact]
    public void Enum_text_is_snake_case_and_parsing_is_exact()
    {
        Assert.Equal("awaiting_approval", SnakeCaseEnumConverter<Sample>.Format(Sample.AwaitingApproval));
        Assert.Equal("done", SnakeCaseEnumConverter<Sample>.Format(Sample.Done));
        Assert.Equal(Sample.AwaitingApproval, SnakeCaseEnumConverter<Sample>.Parse("awaiting_approval"));
        Assert.Throws<InvalidOperationException>(() => SnakeCaseEnumConverter<Sample>.Parse("awaitingapproval"));
        Assert.Throws<InvalidOperationException>(() => SnakeCaseEnumConverter<Sample>.Parse("Awaiting_Approval"));
    }

    [Fact]
    public void Json_context_round_trips_and_in_place_edits_are_detected()
    {
        using var database = new TestDatabase();
        var now = DateTime.UtcNow;
        using (var db = database.Open())
        {
            db.Campaigns.Add(new Campaign { PublicId = "cmp_J", Name = "j", CreatedAt = now, UpdatedAt = now, Context = new JsonObject { ["icp"] = "founders" } });
            db.SaveChanges();
        }

        using (var db = database.Open())
        {
            var campaign = db.Campaigns.Single(c => c.PublicId == "cmp_J");
            Assert.Equal("founders", (string?)campaign.Context["icp"]);
            campaign.Context["icp"] = "cto";   // in place, no new object
            db.SaveChanges();
        }

        using (var db = database.Open())
        {
            Assert.Equal("cto", (string?)db.Campaigns.Single(c => c.PublicId == "cmp_J").Context["icp"]);
        }
    }

    [Fact]
    public void Free_json_nodes_round_trip_including_scalars_and_nulls()
    {
        using var database = new TestDatabase();
        var now = DateTime.UtcNow;
        using (var db = database.Open())
        {
            db.Journal.Add(new JournalEntry { PublicId = "jrn_S", Ts = now, ActorType = Contracts.Api.ActorType.Human, Kind = "note", Old = null, New = JsonValue.Create(3) });
            db.Journal.Add(new JournalEntry { PublicId = "jrn_O", Ts = now, ActorType = Contracts.Api.ActorType.Human, Kind = "note", New = new JsonObject { ["a"] = new JsonArray(1, 2) } });
            db.SaveChanges();
        }

        using (var db = database.Open())
        {
            var scalar = db.Journal.Single(e => e.PublicId == "jrn_S");
            Assert.Null(scalar.Old);
            Assert.Equal(3, (int)scalar.New!);
            var nested = db.Journal.Single(e => e.PublicId == "jrn_O");
            Assert.Equal("{\"a\":[1,2]}", nested.New!.ToJsonString());
        }
    }
}
