using System.Text.Json.Nodes;
using Jason.Contracts.Api;
using Jason.Runtime.Domain;
using Jason.Runtime.Journal;
using Jason.Runtime.Persistence;
using Jason.Runtime.Roles;
using Microsoft.EntityFrameworkCore;

namespace Jason.Runtime.Tests.Roles;

public class RoleServiceTests
{
    private static readonly DateTime Noon = new(2026, 9, 14, 12, 0, 0, DateTimeKind.Utc);

    private static readonly string[] Roster =
    [
        "manager", "planner", "researcher", "copywriter", "personalizer",
        "critic", "responder", "analyst", "deliverability-specialist",
    ];

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_registry_starts_as_the_builtin_roster_in_order()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();

        var page = await NewService(db).ListAsync(new RoleListRequest(null, null), Ct);

        Assert.Equal(Roster, page.Items.Select(r => r.Name));
        Assert.All(page.Items, role =>
        {
            Assert.True(role.Builtin);
            Assert.Empty(role.EntryCommand);
            Assert.Empty(role.ProfileDefaults);
            Assert.StartsWith("rol_", role.Id, StringComparison.Ordinal);
        });
        Assert.Null(page.NextCursor);
    }

    [Fact]
    public async Task A_role_somebody_added_comes_after_the_roster()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var service = NewService(db);
        await service.AddAsync(new RoleAddRequest("fake", null, null, null, null, null), Ct);

        var page = await service.ListAsync(new RoleListRequest(null, null), Ct);

        Assert.Equal(10, page.Items.Count);
        Assert.Equal("fake", page.Items[^1].Name);
    }

    [Fact]
    public async Task A_cursor_walks_the_whole_registry_without_gaps()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var service = NewService(db);
        await service.AddAsync(new RoleAddRequest("fake", null, null, null, null, null), Ct);

        var names = new List<string>();
        string? cursor = null;
        do
        {
            var page = await service.ListAsync(new RoleListRequest(4, cursor), Ct);
            names.AddRange(page.Items.Select(r => r.Name));
            cursor = page.NextCursor;
        }
        while (cursor is not null);

        Assert.Equal([.. Roster, "fake"], names);
    }

    [Fact]
    public async Task Adding_a_role_records_it_and_keeps_its_entry_command_in_order()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();

        var role = await NewService(db).AddAsync(
            new RoleAddRequest(
                " fake ",
                ["node", "host.js", "--role", "fake"],
                JsonNode.Parse("""{"model":"sonnet"}""")!.AsObject(),
                "A role a test made up.",
                null,
                "wiring the host up"),
            Ct);

        Assert.StartsWith("rol_", role.Id, StringComparison.Ordinal);
        Assert.Equal("fake", role.Name);
        Assert.False(role.Builtin);
        Assert.Equal(["node", "host.js", "--role", "fake"], role.EntryCommand);
        Assert.Equal("sonnet", (string?)role.ProfileDefaults["model"]);
        Assert.Equal(new DateTimeOffset(Noon), role.CreatedAt);

        var entry = Assert.Single(await db.Journal.AsNoTracking().Where(e => e.Kind == JournalKinds.RoleAdded).ToListAsync(Ct));
        Assert.Null(entry.CampaignId);
        Assert.Null(entry.WorkItemId);
        Assert.Equal("fake", entry.Key);
        Assert.True((bool)entry.New!["entry_command_present"]!);
        Assert.Equal("wiring the host up", entry.Reason);
        Assert.Equal(ActorType.Human, entry.ActorType);
    }

    [Fact]
    public async Task A_role_without_a_command_of_its_own_says_so_in_the_chronicle()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();

        await NewService(db).AddAsync(new RoleAddRequest("fake", [], null, null, null, null), Ct);

        var entry = Assert.Single(await db.Journal.AsNoTracking().Where(e => e.Kind == JournalKinds.RoleAdded).ToListAsync(Ct));
        Assert.False((bool)entry.New!["entry_command_present"]!);
    }

    [Fact]
    public async Task A_builtin_name_is_taken()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();

        var error = await Assert.ThrowsAsync<ConflictException>(
            () => NewService(db).AddAsync(new RoleAddRequest("manager", null, null, null, null, null), Ct));

        Assert.Equal("role_exists", error.Code);
        Assert.False(error.Retryable);
    }

    [Fact]
    public async Task The_same_role_cannot_be_added_twice()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var service = NewService(db);
        await service.AddAsync(new RoleAddRequest("fake", null, null, null, null, null), Ct);

        var error = await Assert.ThrowsAsync<ConflictException>(
            () => service.AddAsync(new RoleAddRequest("fake", null, null, null, null, null), Ct));

        Assert.Equal("role_exists", error.Code);
    }

    [Fact]
    public async Task A_role_name_is_lowercase_with_hyphens()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();

        var error = await Assert.ThrowsAsync<ValidationException>(
            () => NewService(db).AddAsync(new RoleAddRequest("Fake", null, null, null, null, null), Ct));

        var detail = Assert.Single(error.Details!);
        Assert.Equal("name", detail.Field);
        Assert.Equal("invalid", detail.Code);
    }

    [Fact]
    public async Task A_role_without_a_name_is_refused()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();

        var error = await Assert.ThrowsAsync<ValidationException>(
            () => NewService(db).AddAsync(new RoleAddRequest(null, null, null, null, null, null), Ct));

        var detail = Assert.Single(error.Details!);
        Assert.Equal("name", detail.Field);
        Assert.Equal("required", detail.Code);
    }

    [Fact]
    public async Task A_blank_argument_in_an_entry_command_names_its_position()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();

        var error = await Assert.ThrowsAsync<ValidationException>(
            () => NewService(db).AddAsync(new RoleAddRequest("fake", ["node", "   "], null, null, null, null), Ct));

        var detail = Assert.Single(error.Details!);
        Assert.Equal("entry_command[1]", detail.Field);
        Assert.Equal("invalid", detail.Code);
    }

    [Fact]
    public async Task An_entry_command_with_too_many_arguments_is_refused()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var arguments = Enumerable.Range(0, RoleService.MaxEntryCommandArgs + 1).Select(i => $"arg{i}").ToArray();

        var error = await Assert.ThrowsAsync<ValidationException>(
            () => NewService(db).AddAsync(new RoleAddRequest("fake", arguments, null, null, null, null), Ct));

        var detail = Assert.Single(error.Details!);
        Assert.Equal("entry_command", detail.Field);
        Assert.Equal("too_long", detail.Code);
    }

    [Fact]
    public async Task Profile_defaults_larger_than_sixteen_kibibytes_are_refused()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var defaults = new JsonObject { ["blob"] = JsonValue.Create(new string('x', RoleService.MaxProfileDefaultsBytes)) };

        var error = await Assert.ThrowsAsync<ValidationException>(
            () => NewService(db).AddAsync(new RoleAddRequest("fake", null, defaults, null, null, null), Ct));

        var detail = Assert.Single(error.Details!);
        Assert.Equal("profile_defaults", detail.Field);
        Assert.Equal("too_large", detail.Code);
    }

    [Fact]
    public async Task A_description_longer_than_the_limit_is_refused()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();

        var error = await Assert.ThrowsAsync<ValidationException>(
            () => NewService(db).AddAsync(new RoleAddRequest("fake", null, null, new string('x', RoleService.MaxDescriptionLength + 1), null, null), Ct));

        var detail = Assert.Single(error.Details!);
        Assert.Equal("description", detail.Field);
        Assert.Equal("too_long", detail.Code);
    }

    [Fact]
    public async Task An_actor_claiming_to_be_an_attempt_nobody_knows_is_refused()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();

        var error = await Assert.ThrowsAsync<ValidationException>(
            () => NewService(db).AddAsync(
                new RoleAddRequest("fake", null, null, null, new ActorRef(ActorType.Attempt, "att_01JASONNOTHERE"), null), Ct));

        var detail = Assert.Single(error.Details!);
        Assert.Equal("actor.id", detail.Field);
        Assert.Equal("unknown", detail.Code);
    }

    private static RoleService NewService(JasonDbContext db)
    {
        var clock = new FixedClock(Noon);
        return new RoleService(db, new JournalWriter(clock), clock);
    }
}
