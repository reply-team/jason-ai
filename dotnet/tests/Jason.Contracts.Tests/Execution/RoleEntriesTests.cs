using Jason.Contracts.Execution;

namespace Jason.Contracts.Tests.Execution;

/// <summary>
/// The vocabulary of entries: which named ways in a role has. It is role-neutral in its shape, and today only the
/// manager has rows.
/// </summary>
public class RoleEntriesTests
{
    [Fact]
    public void Only_the_manager_has_entries_and_they_are_exactly_the_five()
    {
        Assert.Equal(["manager"], RoleEntries.Roles);
        Assert.Equal(
            ["deep_review", "quick_look", "reply", "resume", "start"],
            RoleEntries.Of("manager").Order(StringComparer.Ordinal));
    }

    [Theory]
    [InlineData("manager", "start", true)]
    [InlineData("manager", "resume", true)]
    [InlineData("manager", "quick_look", true)]
    [InlineData("manager", "deep_review", true)]
    [InlineData("manager", "reply", true)]
    [InlineData("manager", "Start", false)]
    [InlineData("manager", "start ", false)]
    [InlineData("manager", "", false)]
    [InlineData("manager", "general", false)]
    [InlineData("researcher", "start", false)]
    [InlineData("responder", "reply", false)]
    [InlineData("Manager", "start", false)]
    public void A_role_carries_an_entry_only_where_the_table_gives_it_that_name(string role, string entry, bool carries)
    {
        Assert.Equal(carries, RoleEntries.Carries(role, entry));
    }

    [Fact]
    public void A_role_the_table_does_not_name_has_no_entries()
    {
        Assert.Empty(RoleEntries.Of("researcher"));
        Assert.Empty(RoleEntries.Of("critic"));
    }
}
