using Jason.Runtime.Configuration;
using Jason.Runtime.Dispatch;
using Jason.Runtime.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Jason.Runtime.Tests.Dispatch;

public class EntryCommandResolverTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_role_own_command_wins()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var role = await db.Roles.SingleAsync(r => r.Name == "researcher", Ct);
        role.EntryCommand = ["agent-host", "--role", "researcher"];
        await db.SaveChangesAsync(Ct);

        var command = await NewResolver("fallback").ResolveAsync(db, "researcher", Ct);

        Assert.Equal(new[] { "agent-host", "--role", "researcher" }, command);
    }

    [Fact]
    public async Task One_configured_command_makes_the_whole_roster_launchable()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();

        var command = await NewResolver("agent-host").ResolveAsync(db, "planner", Ct);

        Assert.Equal(new[] { "agent-host" }, command);
    }

    [Fact]
    public async Task A_role_nothing_can_launch_resolves_to_nothing()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();

        Assert.Null(await NewResolver().ResolveAsync(db, "planner", Ct));
    }

    [Fact]
    public async Task A_role_that_does_not_exist_falls_back_like_any_other()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();

        Assert.Null(await NewResolver().ResolveAsync(db, "no-such-role", Ct));
        Assert.Equal(new[] { "agent-host" }, await NewResolver("agent-host").ResolveAsync(db, "no-such-role", Ct));
    }

    private static EntryCommandResolver NewResolver(params string[] defaultEntryCommand) =>
        new(new TestOptionsMonitor<RolesOptions>(new RolesOptions { DefaultEntryCommand = [.. defaultEntryCommand] }));
}
