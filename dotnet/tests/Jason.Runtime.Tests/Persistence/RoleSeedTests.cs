namespace Jason.Runtime.Tests.Persistence;

public class RoleSeedTests
{
    /// <summary>The roster of decision 12: the roles an SDR practice has, seeded so a fresh runtime already knows them.</summary>
    private static readonly string[] Roster =
    [
        "manager",
        "planner",
        "researcher",
        "copywriter",
        "personalizer",
        "critic",
        "responder",
        "analyst",
        "deliverability-specialist",
    ];

    [Fact]
    [Critical(Family.P2, "the seeded roster")]
    public void The_nine_builtin_roles_arrive_with_the_migration()
    {
        using var database = new TestDatabase();
        using var db = database.Open();

        var roles = db.Roles.OrderBy(r => r.PublicId).ToList();

        Assert.Equal(9, roles.Count);
        Assert.Equal(Roster, roles.Select(r => r.Name));
        Assert.All(roles, role =>
        {
            Assert.True(role.Builtin, role.Name);
            Assert.StartsWith("rol_", role.PublicId, StringComparison.Ordinal);
            Assert.Empty(role.EntryCommand);
            Assert.Empty(role.ProfileDefaults);
            Assert.False(string.IsNullOrWhiteSpace(role.Description), role.Name);
            Assert.Equal(role.CreatedAt, role.UpdatedAt);
            Assert.Equal(DateTimeKind.Utc, role.CreatedAt.Kind);
        });
    }

    /// <summary>
    /// The roles the contracts call shipped are exactly the seeded builtin roster: a diagnostic bundle carries those names
    /// and writes every other role's as its alias, so a role seeded here and missing there would be an alias for nothing,
    /// and one named there and not seeded would let a person's role of that name travel.
    /// </summary>
    [Fact]
    public void The_shipped_roles_the_contracts_name_are_the_seeded_roster()
    {
        using var database = new TestDatabase();
        using var db = database.Open();

        Assert.Equal(
            Jason.Contracts.Api.ShippedRoles.Names,
            db.Roles.Where(role => role.Builtin).OrderBy(role => role.PublicId).Select(role => role.Name).ToList());
    }

    [Fact]
    public void A_role_name_is_claimed_once()
    {
        using var database = new TestDatabase();
        using var db = database.Open();

        db.Roles.Add(new Jason.Runtime.Persistence.Role
        {
            PublicId = "rol_DUPLICATE",
            Name = "manager",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        });

        var duplicate = Assert.Throws<Microsoft.EntityFrameworkCore.DbUpdateException>(() => db.SaveChanges());
        Assert.Contains("UNIQUE", duplicate.InnerException!.Message, StringComparison.Ordinal);
    }
}
