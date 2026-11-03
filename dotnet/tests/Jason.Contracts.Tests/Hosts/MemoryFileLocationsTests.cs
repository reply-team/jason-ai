using Jason.Contracts.Execution;

namespace Jason.Contracts.Tests.Hosts;

public class MemoryFileLocationsTests
{
    [Fact]
    public void Named_levels_come_in_the_order_client_project_campaign()
    {
        var files = new MemoryFileLocations("/r/AGENTS.md", "/r/p/AGENTS.md", "/r/p/c/AGENTS.md");

        Assert.Equal(
            [("client", "/r/AGENTS.md"), ("project", "/r/p/AGENTS.md"), ("campaign", "/r/p/c/AGENTS.md")],
            files.NamedLevels());
    }

    [Fact]
    public void A_level_the_launch_does_not_know_is_left_out()
    {
        Assert.Equal([("client", "/r/AGENTS.md"), ("campaign", "/r/c/AGENTS.md")], new MemoryFileLocations("/r/AGENTS.md", null, "/r/c/AGENTS.md").NamedLevels());
        Assert.Empty(new MemoryFileLocations(null, null, null).NamedLevels());
    }
}
