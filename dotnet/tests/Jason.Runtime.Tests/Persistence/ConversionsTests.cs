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
        var campaign = new Campaign { PublicId = "cmp_W", Name = "w", ProjectId = DefaultProject.Id, Status = CampaignStatus.Active, CreatedAt = Noon, UpdatedAt = Noon };
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

        // Refused, and not repeated: a column edited by hand can hold anything, and the refusal travels into logs.
        foreach (var unknown in new[] { "awaitingapproval", "Awaiting_Approval" })
        {
            var refused = Assert.Throws<UnreadableStoredValueException>(() => SnakeCaseEnumConverter<Sample>.Parse(unknown));
            Assert.Equal("not a stored Sample", refused.Failure);
            Assert.DoesNotContain(unknown, refused.ToString(), StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// Every column a converter reads refuses text it cannot read in words that name the column and quote none of the
    /// text: a parser's message quotes what it read, a key written twice is named by the error that finds it, and an
    /// exception kept inside another is written out with it by every log sink. Text no column can hold, and an object
    /// whose key is written twice, which the database's own checks accept.
    /// </summary>
    [Theory]
    [InlineData("{\"brief\": nSENTINEL_STORED}")]
    [InlineData("{\"SENTINEL_STORED\": 1, \"SENTINEL_STORED\": 2}")]
    public void Every_stored_column_refuses_text_it_cannot_read_by_its_own_name_and_quotes_none_of_it(string stored)
    {
        using var database = new TestDatabase();
        using var db = database.Open();
        var columns = db.Model.GetEntityTypes()
            .SelectMany(entity => entity.GetProperties()
                .Where(property => property.GetValueConverter()?.ProviderClrType == typeof(string))
                .Select(property => (Name: $"{entity.GetTableName()}.{property.GetColumnName()}", Converter: property.GetValueConverter()!)))
            .ToList();
        Assert.Contains(columns, column => column.Name == "campaigns.context_json");
        Assert.Contains(columns, column => column.Name == "campaigns.status");

        var offences = new List<string>();
        foreach (var (name, converter) in columns)
        {
            var refused = Record.Exception(() => converter.ConvertFromProvider(stored));
            if (refused is null)
            {
                offences.Add($"{name} read it");
            }
            else if (refused.ToString().Contains("SENTINEL_STORED", StringComparison.Ordinal) || refused.InnerException is not null || !refused.Message.Contains(name, StringComparison.Ordinal))
            {
                offences.Add($"{name}: {refused.GetType().Name}, {(refused.InnerException is null ? "no inner exception" : "an inner exception")}, {(refused.Message.Contains(name, StringComparison.Ordinal) ? "names its column" : "does not name its column")}, {(refused.ToString().Contains("SENTINEL_STORED", StringComparison.Ordinal) ? "quotes the text" : "quotes nothing")}");
            }
        }

        Assert.True(offences.Count == 0, $"{offences.Count} of {columns.Count} columns refused the text the wrong way:{Environment.NewLine}{string.Join(Environment.NewLine, offences)}");
    }

    /// <summary>
    /// Valid JSON of the wrong shape — what the database's own check accepts — fails in whatever reads it, and not only
    /// with the parser's exception: a list where an object belongs, an object where a list does, the literal null where a
    /// value does. Whatever the reader threw, every such column refuses it as an unreadable stored value, by its name, so
    /// the log sites and the start that recognise that exception say it, and nothing else escapes. The wrong shape follows
    /// the column's own: a column that holds a list is refused an object and a scalar, one that holds an object is refused a
    /// list, and <c>[]</c> is the right shape for the first, so it is given only to the second.
    /// </summary>
    [Theory]
    [InlineData(typeof(JsonObjectConverter), "[]")]
    [InlineData(typeof(JsonObjectConverter), "\"SENTINEL_STORED\"")]
    [InlineData(typeof(JsonObjectConverter), "null")]
    [InlineData(typeof(JsonArrayConverter), "{}")]
    [InlineData(typeof(JsonArrayConverter), "null")]
    [InlineData(typeof(JsonNodeConverter), "null")]
    [InlineData(typeof(JsonTextConverter<>), "[]", false)]
    [InlineData(typeof(JsonTextConverter<>), "{}", true)]
    [InlineData(typeof(JsonTextConverter<>), "\"SENTINEL_STORED\"", true)]
    [InlineData(typeof(JsonTextConverter<>), "\"SENTINEL_STORED\"", false)]
    [InlineData(typeof(JsonTextConverter<>), "null")]
    [InlineData(typeof(StringListConverter), "{}")]
    [InlineData(typeof(StringListConverter), "null")]
    public void Every_stored_column_refuses_json_of_the_wrong_shape_as_an_unreadable_stored_value(Type kind, string stored, bool? holdsList = null)
    {
        using var database = new TestDatabase();
        using var db = database.Open();
        var columns = db.Model.GetEntityTypes()
            .SelectMany(entity => entity.GetProperties()
                .Where(property => property.GetValueConverter() is { } converter
                    && (converter.GetType() == kind || (converter.GetType().IsGenericType && converter.GetType().GetGenericTypeDefinition() == kind))
                    && (holdsList is null || holdsList == IsList(converter.ModelClrType)))
                .Select(property => (Name: $"{entity.GetTableName()}.{property.GetColumnName()}", Converter: property.GetValueConverter()!)))
            .ToList();
        Assert.NotEmpty(columns);

        Assert.All(columns, column =>
        {
            var refused = Assert.IsType<UnreadableStoredValueException>(Record.Exception(() => column.Converter.ConvertFromProvider(stored)));
            Assert.Equal(column.Name, refused.Column);
            Assert.Null(refused.InnerException);
            Assert.DoesNotContain("SENTINEL_STORED", refused.ToString(), StringComparison.Ordinal);
        });
    }

    private static bool IsList(Type type) => type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>);

    [Fact]
    [Critical(Family.X1, "JSON column change tracking")]
    public void Json_context_round_trips_and_in_place_edits_are_detected()
    {
        using var database = new TestDatabase();
        var now = DateTime.UtcNow;
        using (var db = database.Open())
        {
            db.Campaigns.Add(new Campaign { PublicId = "cmp_J", Name = "j", ProjectId = DefaultProject.Id, CreatedAt = now, UpdatedAt = now, Context = new JsonObject { ["icp"] = "founders" } });
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
            db.Campaigns.Add(new Campaign { PublicId = "cmp_P", Name = "p", ProjectId = DefaultProject.Id, CreatedAt = now, UpdatedAt = now, Status = CampaignStatus.Paused });
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
    [Critical(Family.X1, "no guessed status")]
    public void Status_text_the_converter_does_not_know_stops_the_read_instead_of_being_guessed_at()
    {
        using var database = new TestDatabase();
        using (var connection = new SqliteConnection($"Data Source={database.File}"))
        {
            connection.Open();
            using var command = connection.CreateCommand();

            // Written the way a hand-edited database or a future version of the runtime would write it.
            command.CommandText =
                "INSERT INTO campaigns (public_id, name, status, created_at, updated_at, context_json, project_id) "
                + "VALUES ('cmp_U', 'u', 'pau_sed', '2026-01-01T00:00:00.000Z', '2026-01-01T00:00:00.000Z', '{}', (SELECT id FROM projects WHERE is_default = 1))";
            command.ExecuteNonQuery();
        }

        // The read stops, naming the column; the text it found is not repeated, because whoever edited it could have
        // written anything there and the refusal travels into logs.
        using var db = database.Open();
        var failure = Assert.Throws<UnreadableStoredValueException>(() => db.Campaigns.ToList());
        Assert.Equal("campaigns.status", failure.Column);
        Assert.DoesNotContain("pau_sed", failure.ToString(), StringComparison.Ordinal);
    }
}
