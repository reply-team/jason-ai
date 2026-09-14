using System.Text.Json.Nodes;
using Jason.Contracts.Api;
using Jason.Runtime.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Jason.Runtime.Tests.Persistence;

public class ConversionsTests
{
    private static readonly DateTime Noon = new(2026, 9, 14, 12, 0, 0, DateTimeKind.Utc);

    private enum Sample
    {
        AwaitingApproval,
        Done,
    }

    [Fact]
    public void An_attempt_error_round_trips_through_its_json_column()
    {
        using var database = new TestDatabase();
        var error = new AttemptErrorDto("rate_limited", "the provider asked for a pause", true, "exit 3", [new ErrorDetail("f", "c", "m")]);
        var launch = new AttemptLaunchDto(["dotnet", "host.dll", "succeed"], "/work/wi_A/att_A", 4242, 0);

        using (var db = database.Open())
        {
            var item = SeedWorkItem(db);
            db.Attempts.Add(new Attempt
            {
                PublicId = "att_A",
                WorkItem = item,
                Number = 1,
                Command = WorkItemKind.AiRole,
                Status = AttemptStatus.Failed,
                ClaimedAt = Noon,
                LockUntil = Noon.AddHours(1),
                Error = error,
                Launch = launch,
            });
            db.SaveChanges();
        }

        using (var db = database.Open())
        {
            var attempt = db.Attempts.Single(a => a.PublicId == "att_A");
            Assert.Equal(error.Code, attempt.Error!.Code);
            Assert.Equal(error.Message, attempt.Error.Message);
            Assert.True(attempt.Error.Retriable);
            Assert.Equal("exit 3", attempt.Error.Trace);
            Assert.Equal("m", Assert.Single(attempt.Error.Details!).Message);
            Assert.Equal(launch.EntryCommand, attempt.Launch!.EntryCommand);
            Assert.Equal(4242, attempt.Launch.Pid);
        }
    }

    [Fact]
    public void An_entry_command_round_trips_and_its_order_is_part_of_its_identity()
    {
        var comparer = new StringListComparer();
        Assert.True(comparer.Equals(["a", "b"], ["a", "b"]));
        Assert.False(comparer.Equals(["a", "b"], ["b", "a"]));
        Assert.False(comparer.Equals(["a"], ["a", "b"]));

        using var database = new TestDatabase();
        using (var db = database.Open())
        {
            db.Roles.Add(new Role
            {
                PublicId = "rol_F",
                Name = "fake",
                EntryCommand = ["dotnet", "host.dll", "succeed"],
                ProfileDefaults = new JsonObject { ["model"] = "small" },
                CreatedAt = Noon,
                UpdatedAt = Noon,
            });
            db.SaveChanges();
        }

        using (var db = database.Open())
        {
            var role = db.Roles.Single(r => r.Name == "fake");
            Assert.Equal(["dotnet", "host.dll", "succeed"], role.EntryCommand);
            Assert.Equal("small", (string?)role.ProfileDefaults["model"]);
        }
    }

    [Fact]
    public void A_work_item_status_is_stored_as_snake_case_text()
    {
        using var database = new TestDatabase();
        using (var db = database.Open())
        {
            var item = SeedWorkItem(db);
            item.Kind = WorkItemKind.ProviderOp;
            item.Status = WorkItemStatus.Processing;
            db.SaveChanges();
        }

        using var connection = new SqliteConnection($"Data Source={database.File}");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT kind || '/' || status FROM work_items";
        Assert.Equal("provider_op/processing", (string?)command.ExecuteScalar());
    }

    private static WorkItem SeedWorkItem(JasonDbContext db)
    {
        var campaign = new Campaign { PublicId = "cmp_W", Name = "w", Status = CampaignStatus.Active, CreatedAt = Noon, UpdatedAt = Noon };
        db.Campaigns.Add(campaign);
        var item = new WorkItem
        {
            PublicId = "wi_W",
            Campaign = campaign,
            Kind = WorkItemKind.AiRole,
            Role = "researcher",
            CreatedByType = ActorType.Human,
            CreatedAt = Noon,
            UpdatedAt = Noon,
        };
        db.WorkItems.Add(item);
        db.SaveChanges();
        return item;
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

    [Fact]
    public void A_status_stored_as_snake_case_text_reads_back_as_its_enum()
    {
        using var database = new TestDatabase();
        var now = DateTime.UtcNow;
        using (var db = database.Open())
        {
            db.Campaigns.Add(new Campaign { PublicId = "cmp_P", Name = "p", CreatedAt = now, UpdatedAt = now, Status = CampaignStatus.Paused });
            db.SaveChanges();
        }

        using (var connection = new SqliteConnection($"Data Source={database.File}"))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT status FROM campaigns WHERE public_id = 'cmp_P'";
            Assert.Equal("paused", (string?)command.ExecuteScalar());
        }

        using (var db = database.Open())
        {
            Assert.Equal(CampaignStatus.Paused, db.Campaigns.Single(c => c.PublicId == "cmp_P").Status);
        }
    }

    [Fact]
    public void Status_text_the_converter_does_not_know_stops_the_read_instead_of_being_guessed_at()
    {
        using var database = new TestDatabase();
        using (var connection = new SqliteConnection($"Data Source={database.File}"))
        {
            connection.Open();
            using var command = connection.CreateCommand();

            // Written the way a hand-edited database or a future version of the runtime would write it.
            command.CommandText =
                "INSERT INTO campaigns (public_id, name, status, created_at, updated_at, context_json) "
                + "VALUES ('cmp_U', 'u', 'pau_sed', '2026-01-01T00:00:00.000Z', '2026-01-01T00:00:00.000Z', '{}')";
            command.ExecuteNonQuery();
        }

        using var db = database.Open();
        var failure = Assert.Throws<InvalidOperationException>(() => db.Campaigns.ToList());
        Assert.Contains("pau_sed", failure.Message, StringComparison.Ordinal);
    }
}
