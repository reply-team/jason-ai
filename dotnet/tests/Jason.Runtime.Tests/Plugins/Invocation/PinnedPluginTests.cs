using System.Text.Json.Nodes;
using Jason.Runtime.Plugins.Invocation;
using Jason.Runtime.Plugins.Registry;
using Microsoft.Extensions.DependencyInjection;

namespace Jason.Runtime.Tests.Plugins.Invocation;

/// <summary>
/// A pin says which package runs. The rest of the request still names a plugin — that name is what the
/// provenance records and what every log line about the invocation says — so the two can disagree, and a
/// disagreement means one of them is a lie about what ran. Neither is preferred over the other: the invocation
/// is refused before a process exists, at the only place that can see both.
/// </summary>
[Collection(ProcessEnvironmentCollection.Name)]
public class PinnedPluginTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_pinned_invocation_whose_plugin_id_disagrees_with_the_pin_is_refused()
    {
        await using var api = await PluginInvokerTests.StartAsync();
        var pinned = PinOf(api, TestPlugins.FakeProviderId);

        var result = await PluginInvokerTests.InvokeAsync(
            api,
            PluginInvokerTests.Request("echo.run", new JsonObject(), plugin: TestPlugins.OtherProviderId) with { Pinned = pinned },
            Ct);

        var failure = Assert.IsType<InvocationOutcome.ProtocolFailure>(result.Outcome);
        Assert.Equal(ProtocolCodes.PluginInvocationRejected, failure.Code);
        Assert.Null(result.Launch);

        // Both names, because which of the two is wrong is the caller's question to answer.
        Assert.Contains(TestPlugins.OtherProviderId, failure.Message, StringComparison.Ordinal);
        Assert.Contains(TestPlugins.FakeProviderId, failure.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// The other half of the same rule, without which "refuses a pinned invocation" would be just as true of a
    /// runtime that refused all of them: a pin the request agrees with runs the package it names.
    /// </summary>
    [Fact]
    public async Task A_pinned_invocation_the_request_agrees_with_runs_that_package()
    {
        await using var api = await PluginInvokerTests.StartAsync();
        var pinned = PinOf(api, TestPlugins.FakeProviderId);

        var result = await PluginInvokerTests.InvokeAsync(
            api,
            PluginInvokerTests.Request("echo.run", new JsonObject { ["hello"] = "world" }) with { Pinned = pinned },
            Ct);

        var succeeded = Assert.IsType<InvocationOutcome.Succeeded>(result.Outcome);
        Assert.Equal("world", succeeded.Result!["echo"]!["hello"]!.GetValue<string>());
        Assert.Equal(TestPlugins.FakeProviderId, result.Provenance.PluginId);
        Assert.Equal(pinned.SnapshotId, result.Provenance.SnapshotId);
    }

    private static PinnedPlugin PinOf(RuntimeApiFixture api, string pluginId)
    {
        using var scope = api.Runtime.Services.CreateScope();
        var snapshot = scope.ServiceProvider.GetRequiredService<PluginRegistry>().Snapshot;
        return new PinnedPlugin(snapshot.Find(pluginId)!, snapshot.Id);
    }
}
