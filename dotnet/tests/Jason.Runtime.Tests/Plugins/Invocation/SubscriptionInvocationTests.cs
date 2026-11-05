using System.Text;
using System.Text.Json.Nodes;
using Jason.Contracts.Discovery;
using Jason.Contracts.Json;
using Jason.Contracts.Plugins;
using Jason.Contracts.Subscriptions;
using Jason.Runtime.Plugins.Invocation;
using Jason.Runtime.Tests.Integration;

namespace Jason.Runtime.Tests.Plugins.Invocation;

/// <summary>
/// A plugin of any kind is invoked for a journal kind it subscribes to, and only for one the person granted it: the kind
/// gate of a subscription reads the frozen grant, never the plugin's kind, so a provider, a notification plugin and a
/// subscriber are each admitted with the grant and refused without it, before a process exists.
/// </summary>
[Collection(ProcessEnvironmentCollection.Name)]
public class SubscriptionInvocationTests
{
    private const string Failed = "workitem_failed";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("provider")]
    [InlineData("notification")]
    [InlineData("subscriber")]
    public async Task A_plugin_of_any_kind_granted_the_kind_is_invoked_for_its_line(string kind)
    {
        await using var api = await PluginInvokerTests.StartAsync(paths => Install(paths, kind, granted: [Failed]));

        var result = await PluginInvokerTests.InvokeAsync(api, Handle("watcher", Failed), Ct);

        var succeeded = Assert.IsType<InvocationOutcome.Succeeded>(result.Outcome);
        Assert.Equal("workitem_failed:1:jrn_01K0line", succeeded.Result!["envelope"]!["seen"]!.GetValue<string>());
        Assert.Equal(SubscriptionContract.CurrentVersion, result.Provenance.OperationContractVersion);
        Assert.Equal("jrn_01K0line", result.Provenance.CorrelationId);
        Assert.NotNull(result.Launch);
    }

    [Theory]
    [InlineData("provider")]
    [InlineData("notification")]
    [InlineData("subscriber")]
    public async Task A_plugin_that_declares_the_kind_but_was_not_granted_it_is_refused_before_anything_starts(string kind)
    {
        await using var api = await PluginInvokerTests.StartAsync(paths => Install(paths, kind, granted: []));

        var result = await PluginInvokerTests.InvokeAsync(api, Handle("watcher", Failed), Ct);

        var refused = Assert.IsType<InvocationOutcome.ProtocolFailure>(result.Outcome);
        Assert.Equal(ProtocolCodes.PluginOperationUnsupported, refused.Code);
        Assert.Equal("The plugin 'watcher' is not granted a subscription to 'workitem_failed'.", refused.Message);
        Assert.Null(result.Launch);
    }

    /// <summary>A grant of one kind opens that kind alone: the operation word of a subscription is the line's kind.</summary>
    [Fact]
    public async Task A_grant_of_one_kind_does_not_open_another()
    {
        await using var api = await PluginInvokerTests.StartAsync(paths => Install(paths, "subscriber", granted: [Failed]));

        var result = await PluginInvokerTests.InvokeAsync(api, Handle("watcher", "approval_requested"), Ct);

        var refused = Assert.IsType<InvocationOutcome.ProtocolFailure>(result.Outcome);
        Assert.Equal(ProtocolCodes.PluginOperationUnsupported, refused.Code);
        Assert.Equal("The plugin 'watcher' is not granted a subscription to 'approval_requested'.", refused.Message);
        Assert.Null(result.Launch);
    }

    /// <summary>What the runtime's subscription runner hands a handler: the line, the envelope so far and the attempt.</summary>
    private static PluginInvocationRequest Handle(string plugin, string kind) => new(
        plugin,
        kind,
        new JsonObject
        {
            ["line"] = new JsonObject
            {
                ["id"] = "jrn_01K0line",
                ["ts"] = "2026-10-16T09:00:00.000Z",
                ["actor"] = new JsonObject { ["type"] = "system", ["id"] = "runtime" },
                ["kind"] = kind,
            },
            ["envelope"] = new JsonObject(),
            ["attempt"] = 1,
        },
        null,
        "jrn_01K0line",
        Purpose: InvocationPurpose.Subscription);

    private static void Install(JasonPaths paths, string kind, string[] granted)
    {
        var contracts = kind switch
        {
            "provider" => "  protocol: [1]\n  operations: [1]\noperations: [echo.run]\n",
            "notification" => "  protocol: [1]\n  operations: [1]\n",
            _ => "  protocol: [1]\n",
        };
        TestPlugins.Write(
            paths,
            "watcher",
            $"manifest_version: 1\nid: watcher\nversion: 1.0.0\nkind: {kind}\ncontracts:\n{contracts}subscriptions: [{Failed}]\n",
            "export function invoke(operation, input) { return { result: { envelope: { seen: operation + ':' + input.attempt + ':' + input.line.id } } }; }");
        TestPlugins.Grant(paths, "watcher");

        var settings = JsonNode.Parse(File.ReadAllText(paths.UserSettingsFile))!.AsObject();
        settings["Plugins"]!["Grants"]!["watcher"]!["Subscriptions"] = new JsonArray([.. granted.Select(name => (JsonNode)JsonValue.Create(name))]);
        File.WriteAllText(paths.UserSettingsFile, settings.ToJsonString(JasonJson.Options), new UTF8Encoding(false));
    }
}
