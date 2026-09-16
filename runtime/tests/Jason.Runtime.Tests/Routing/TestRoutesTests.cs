using System.Text.Json.Nodes;
using Jason.Runtime.Configuration;
using Jason.Runtime.Tests.Plugins;
using Microsoft.Extensions.Configuration;

namespace Jason.Runtime.Tests.Routing;

/// <summary>
/// The helper six later tasks write their fixtures against, held to the one thing that would quietly ruin them:
/// a route must not overwrite a grant, and a grant must not overwrite a route.
/// </summary>
public class TestRoutesTests
{
    [Fact]
    public void A_global_route_is_merged_into_the_settings_the_runtime_reads()
    {
        using var dir = new TempDataDir();

        TestRoutes.WriteGlobal(dir.Paths, TestRoutes.GlobalDefault(TestPlugins.FakeProviderId, new JsonObject { ["workspace"] = "west" }));

        var configuration = JasonConfiguration.Build(dir.Paths, null);
        var options = RoutesOptions.Read(configuration.GetSection(RoutesOptions.Section));

        Assert.Equal(TestPlugins.FakeProviderId, options.Default!.Plugin);
        Assert.Equal("west", (string?)Assert.IsType<JsonObject>(options.Default.Binding)["workspace"]);

        // The dispatcher stays off: a route must never quietly switch the loop on under a test.
        Assert.False(configuration.GetValue<bool>("Dispatcher:Enabled"));
    }

    [Fact]
    public void A_route_and_a_grant_written_in_either_order_both_survive()
    {
        using var first = new TempDataDir();
        TestPlugins.Grant(first.Paths, TestPlugins.FakeProviderId, env: ["A"]);
        TestRoutes.WriteGlobal(first.Paths, TestRoutes.GlobalDefault(TestPlugins.FakeProviderId));

        using var second = new TempDataDir();
        TestRoutes.WriteGlobal(second.Paths, TestRoutes.GlobalDefault(TestPlugins.FakeProviderId));
        TestPlugins.Grant(second.Paths, TestPlugins.FakeProviderId, env: ["A"]);

        foreach (var paths in new[] { first.Paths, second.Paths })
        {
            var configuration = JasonConfiguration.Build(paths, null);
            Assert.Equal(["A"], configuration.GetSection($"Plugins:Grants:{TestPlugins.FakeProviderId}:Env").Get<string[]>()!);
            Assert.Equal(TestPlugins.FakeProviderId, configuration["Routes:Default:Plugin"]);
        }
    }

    [Fact]
    public void A_second_write_replaces_the_routes_and_leaves_everything_else_alone()
    {
        using var dir = new TempDataDir();
        TestPlugins.Grant(dir.Paths, TestPlugins.OtherProviderId, exec: ["dotnet"]);

        TestRoutes.WriteGlobal(dir.Paths, TestRoutes.GlobalDefault(TestPlugins.FakeProviderId, new JsonObject { ["workspace"] = "west" }));
        TestRoutes.WriteGlobal(dir.Paths, TestRoutes.GlobalDefault(TestPlugins.OtherProviderId));

        var configuration = JasonConfiguration.Build(dir.Paths, null);
        var options = RoutesOptions.Read(configuration.GetSection(RoutesOptions.Section));

        Assert.Equal(TestPlugins.OtherProviderId, options.Default!.Plugin);
        Assert.Null(options.Default.Binding);
        Assert.Equal(["dotnet"], configuration.GetSection($"Plugins:Grants:{TestPlugins.OtherProviderId}:Exec").Get<string[]>()!);
    }

    [Fact]
    public void A_default_written_with_no_binding_carries_none()
    {
        var section = TestRoutes.GlobalDefault(TestPlugins.FakeProviderId);

        Assert.Equal("""{"Default":{"Plugin":"fake-provider"}}""", section);
        Assert.Equal(TestPlugins.FakeProviderId, "fake-provider");
    }
}
