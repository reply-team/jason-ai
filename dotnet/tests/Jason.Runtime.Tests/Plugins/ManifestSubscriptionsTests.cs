using Jason.Contracts.Operations;
using Jason.Contracts.Plugins;
using Jason.Runtime.Plugins.Manifest;
using Jason.Runtime.Plugins.Subscriptions;

namespace Jason.Runtime.Tests.Plugins;

/// <summary>
/// The journal kinds a plugin subscribes to, as its manifest declares them, and the third kind of plugin, a subscriber,
/// which only handles them. Each declared kind is read against the catalog of the kinds the runtime writes; a test hands
/// the reader a lookup of its own where it needs a kind the catalog does not have, such as one no plugin may subscribe to.
/// </summary>
public sealed class ManifestSubscriptionsTests : IDisposable
{
    private static readonly ManifestBounds Bounds = new(3_600_000, 512);

    /// <summary>A lookup with one kind a plugin may subscribe to, one it may not, and nothing else.</summary>
    private static readonly SubscribableKinds Stub = new(kind => kind switch
    {
        "campaign_created" or "workitem_failed" => KindStanding.Subscribable,
        "machinery_ran" => KindStanding.NotSubscribable,
        _ => KindStanding.Unknown,
    });

    private const string Subscriber = """
        manifest_version: 1
        id: fake
        version: 1.0.0
        kind: subscriber
        contracts:
          protocol: [1]

        """;

    private readonly TempTree _tree = new();

    [Fact]
    public void A_subscriber_plugin_declares_the_kinds_it_handles_and_nothing_else()
    {
        var result = Read(Subscriber + "subscriptions: [workitem_failed, campaign_created]\n");

        Assert.True(result.IsValid, Problems(result));
        Assert.Equal(PluginKind.Subscriber, result.Manifest!.Kind);
        Assert.Equal(["workitem_failed", "campaign_created"], result.Manifest.Subscriptions);
        Assert.Empty(result.Manifest.Operations);
        Assert.Empty(result.Manifest.Contracts.Operations);
        Assert.False(result.Manifest.ReadsEvents);
    }

    [Fact]
    public void A_subscriber_plugin_must_declare_subscriptions()
    {
        var problem = Assert.Single(Read(Subscriber).Problems);

        Assert.Equal(("field_required", "subscriptions"), (problem.Code, problem.Path));
    }

    /// <summary>A subscriber implements no operation, is no channel and reads no account: what a provider declares for those is refused on it by the codes that refuse it on a notification plugin.</summary>
    [Theory]
    [InlineData("operations: [echo.run]\n", "operations_not_allowed", "operations")]
    [InlineData("settles_by_key: [message.send]\n", "settles_by_key_invalid", "settles_by_key")]
    [InlineData("arguments:\n  message.send:\n    send_after: [email]\n", "argument_invalid", "arguments")]
    [InlineData("templates:\n  message.send: [email]\n", "template_invalid", "templates")]
    public void A_subscriber_plugin_refuses_what_only_a_provider_declares(string extra, string code, string path)
    {
        var problem = Assert.Single(Read(Subscriber + "subscriptions: [workitem_failed]\n" + extra).Problems);

        Assert.Equal((code, path), (problem.Code, problem.Path));
    }

    [Fact]
    public void A_subscriber_plugin_speaks_no_operation_or_delivery_contract_and_reads_no_account()
    {
        var withOperations = Read("""
            manifest_version: 1
            id: fake
            version: 1.0.0
            kind: subscriber
            contracts:
              protocol: [1]
              operations: [1]
            subscriptions: [workitem_failed]
            """);
        var withEvents = Read("""
            manifest_version: 1
            id: fake
            version: 1.0.0
            kind: subscriber
            contracts:
              protocol: [1]
              events: [1]
            subscriptions: [workitem_failed]
            """);

        var operations = Assert.Single(withOperations.Problems);
        Assert.Equal(("field_invalid", "contracts.operations"), (operations.Code, operations.Path));
        var events = Assert.Single(withEvents.Problems);
        Assert.Equal(("events_not_allowed", "contracts.events"), (events.Code, events.Path));
    }

    [Theory]
    [InlineData("subscriptions: []\n", "field_invalid", "subscriptions")]
    [InlineData("subscriptions: workitem_failed\n", "field_invalid", "subscriptions")]
    [InlineData("subscriptions: [[workitem_failed]]\n", "field_invalid", "subscriptions[0]")]
    [InlineData("subscriptions: [workitem_failed, workitem_failed]\n", "field_invalid", "subscriptions[1]")]
    [InlineData("subscriptions: [workitem_failed, no_such_kind]\n", "subscription_kind_unknown", "subscriptions[1]")]
    [InlineData("subscriptions: [machinery_ran]\n", "subscription_kind_not_subscribable", "subscriptions[0]")]
    public void Each_declared_kind_is_a_kind_the_catalog_lets_a_plugin_subscribe_to_named_once(string declared, string code, string path)
    {
        var result = Read(TestPlugins.Manifest("fake", extra: declared));

        Assert.Null(result.Manifest);
        var problem = Assert.Single(result.Problems);
        Assert.Equal((code, path), (problem.Code, problem.Path));
    }

    /// <summary>Nothing ties a subscription to a kind of plugin: a provider and a notification plugin may each declare one.</summary>
    [Fact]
    public void A_provider_and_a_notification_plugin_may_subscribe()
    {
        var provider = Read(TestPlugins.Manifest("fake", extra: "subscriptions: [workitem_failed]\n"));
        var notification = Read("""
            manifest_version: 1
            id: fake
            version: 1.0.0
            kind: notification
            contracts:
              protocol: [1]
              operations: [1]
            subscriptions: [campaign_created]
            """);

        Assert.True(provider.IsValid, Problems(provider));
        Assert.Equal(["workitem_failed"], provider.Manifest!.Subscriptions);
        Assert.Equal(["echo.run"], provider.Manifest.Operations);
        Assert.True(notification.IsValid, Problems(notification));
        Assert.Equal(["campaign_created"], notification.Manifest!.Subscriptions);
    }

    [Fact]
    public void A_manifest_that_declares_no_subscriptions_subscribes_to_nothing()
    {
        var result = Read(TestPlugins.Manifest("fake"));

        Assert.True(result.IsValid, Problems(result));
        Assert.Empty(result.Manifest!.Subscriptions);
    }

    /// <summary>Without a lookup of its own the reader reads the catalog: a kind the runtime writes is one a plugin may declare.</summary>
    [Fact]
    public void The_reader_reads_declared_kinds_against_the_catalog_by_default()
    {
        var manifest = TestPlugins.Manifest("fake", extra: "subscriptions: [workitem_failed, not_a_kind_at_all]\n");

        var result = ManifestReader.Read(manifest, "fake", NewPackage(manifest), Bounds);

        var problem = Assert.Single(result.Problems);
        Assert.Equal(("subscription_kind_unknown", "subscriptions[1]"), (problem.Code, problem.Path));
    }

    private ManifestReadResult Read(string manifest) =>
        ManifestReader.Read(manifest, "fake", NewPackage(manifest), Bounds, PatternTiming.Default, probeTimeoutsHoldOnlyThePlugin: false, Stub);

    private static string Problems(ManifestReadResult result) =>
        string.Join("; ", result.Problems.Select(p => $"{p.Path}: {p.Code}: {p.Message}"));

    private string NewPackage(string manifest)
    {
        var root = _tree.NewDirectory("fake");
        File.WriteAllText(Path.Combine(root, "plugin.yaml"), manifest);
        File.WriteAllText(Path.Combine(root, "main.js"), "export function invoke() { return { result: {} }; }");
        return root;
    }

    public void Dispose() => _tree.Dispose();
}
