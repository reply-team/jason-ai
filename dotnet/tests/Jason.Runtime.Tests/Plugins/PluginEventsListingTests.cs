using Jason.Runtime.Plugins.Registry;

namespace Jason.Runtime.Tests.Plugins;

/// <summary>What <c>plugin.list</c> says of the events contract: the versions a plugin declares, and none for one that declares none.</summary>
public class PluginEventsListingTests
{
    [Fact]
    public void A_plugin_that_reads_its_accounts_events_says_so_and_one_that_does_not_says_nothing()
    {
        var reads = PluginMapper.ToDto(TestPlugins.Loaded("reader", operations: ["campaign.get"], events: [1]));
        var silent = PluginMapper.ToDto(TestPlugins.Loaded("silent", operations: ["campaign.get"]));

        Assert.Equal([1], reads.Contracts.Events);
        Assert.Empty(silent.Contracts.Events);
    }
}
