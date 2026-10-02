using System.Text.Json.Nodes;
using Jason.Contracts.Discovery;
using Jason.Contracts.Notifications;
using Jason.Contracts.Plugins;
using Jason.Runtime.Notifications;
using Jason.Runtime.Notifications.Plugins;
using Jason.Runtime.Plugins.Registry;
using Jason.Runtime.Tests.Plugins;
using Jason.Runtime.Tests.Plugins.Invocation;
using Microsoft.Extensions.DependencyInjection;

namespace Jason.Runtime.Tests.Notifications;

/// <summary>
/// The ntfy reference plugin, through the real plugin host, against an ntfy server on the loopback interface. The
/// package is copied and its one host, <c>ntfy.sh</c>, replaced by the loopback server's — the self-hosted procedure
/// its own README gives, which is an owner's edit of the manifest and so a package of a different identity.
/// </summary>
public sealed class NtfyPackageTests
{
    private const string Id = "ntfy";
    private const string TokenVariable = "NTFY_TOKEN";

    private static readonly NotificationMessage Message = NotificationComposer.Compose(3, 1);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static string Shipped => Path.Combine(AppContext.BaseDirectory, "Fixtures", "plugins", Id);

    /// <summary>The package copied into a data directory, its host replaced by the one given, and granted that host and the token.</summary>
    private static void Install(JasonPaths paths, string? host)
    {
        var root = paths.PluginPackageDirectory(Id);
        Directory.CreateDirectory(root);
        foreach (var file in Directory.EnumerateFiles(Shipped))
        {
            File.Copy(file, Path.Combine(root, Path.GetFileName(file)));
        }

        if (host is not null)
        {
            var manifest = Path.Combine(root, "plugin.yaml");
            var edited = File.ReadAllText(manifest).Replace("hosts: [ntfy.sh]", $"hosts: [{host}]", StringComparison.Ordinal);
            Assert.NotEqual(File.ReadAllText(manifest), edited);
            File.WriteAllText(manifest, edited);
            TestPlugins.Grant(paths, Id, http: [host], env: [TokenVariable]);
        }
    }

    private static PluginNotificationChannel Channel(RuntimeApiFixture api, string server, string topic = "jason-test") =>
        new(Id, new NotificationPluginEntry { Binding = new JsonObject { ["server"] = server, ["topic"] = topic } }, api.Resolve<PluginRegistry>(), api.Resolve<IServiceScopeFactory>());

    /// <summary>Everything the invocations wrote in their own directories.</summary>
    private static string InvocationFiles(RuntimeApiFixture api)
    {
        var work = api.Resolve<JasonPaths>().PluginWorkDirectory;
        return Directory.Exists(work)
            ? string.Concat(Directory.EnumerateFiles(work, "*", SearchOption.AllDirectories).Select(FakeHostRuntimeRead))
            : string.Empty;
    }

    private static string FakeHostRuntimeRead(string file) => Jason.Runtime.Tests.Integration.FakeHostRuntime.ReadShared(file);

    [Fact]
    public async Task It_publishes_the_pointer_and_nothing_else()
    {
        Assert.Null(Environment.GetEnvironmentVariable(TokenVariable));
        using var ntfy = new LoopbackNtfy();
        await using var api = await PluginInvokerTests.StartAsync(paths => Install(paths, ntfy.HostKey));

        var outcome = await Channel(api, ntfy.Server).DeliverAsync(Message, "ntf_01K0test", Ct);

        Assert.Equal(ChannelResult.Delivered, outcome.Result);
        var request = Assert.Single(ntfy.Requests);
        Assert.Equal(("POST", "/"), (request.Method, request.Path));
        Assert.Equal(
            new JsonObject { ["topic"] = "jason-test", ["title"] = "Jason", ["message"] = Message.Text }.ToJsonString(),
            JsonNode.Parse(request.Body)!.ToJsonString());
        Assert.False(request.Headers.ContainsKey("Authorization"));
    }

    /// <summary>The token the operator granted is sent as a bearer header, and is found nowhere else.</summary>
    [Fact]
    public async Task A_granted_token_travels_as_a_bearer_header_and_nowhere_else()
    {
        const string Token = "tk_jason_ntfy_test_0123456789";
        using var ntfy = new LoopbackNtfy();
        Environment.SetEnvironmentVariable(TokenVariable, Token);
        try
        {
            await using var api = await PluginInvokerTests.StartAsync(paths => Install(paths, ntfy.HostKey));
            var channel = Channel(api, ntfy.Server);

            var outcome = await channel.DeliverAsync(Message, "ntf_01K0test", Ct);

            Assert.Equal(ChannelResult.Delivered, outcome.Result);
            Assert.Equal($"Bearer {Token}", Assert.Single(ntfy.Requests).Headers["Authorization"]);
            Assert.DoesNotContain(Token, InvocationFiles(api), StringComparison.Ordinal);
            Assert.DoesNotContain(Token, outcome.ToString(), StringComparison.Ordinal);
            Assert.DoesNotContain(Token, File.ReadAllText(api.Resolve<JasonPaths>().UserSettingsFile), StringComparison.Ordinal);
        }
        finally
        {
            Environment.SetEnvironmentVariable(TokenVariable, null);
        }
    }

    [Theory]
    [InlineData(200, """{"id":"abc"}""", ChannelResult.Delivered, null, null)]
    [InlineData(401, "", ChannelResult.Failed, "notification_unauthorized", FailureClass.Permanent)]
    [InlineData(403, "", ChannelResult.Failed, "notification_unauthorized", FailureClass.Permanent)]
    [InlineData(400, "", ChannelResult.Failed, "notification_refused", FailureClass.Permanent)]
    [InlineData(503, "", ChannelResult.Failed, "notification_unreachable", FailureClass.Transient)]
    [InlineData(429, "", ChannelResult.Failed, "notification_unreachable", FailureClass.Transient)]
    public async Task Its_answers_map_to_the_contract(int status, string body, ChannelResult result, string? code, FailureClass? @class)
    {
        using var ntfy = new LoopbackNtfy { Status = status, Body = body };
        await using var api = await PluginInvokerTests.StartAsync(paths => Install(paths, ntfy.HostKey));

        var outcome = await Channel(api, ntfy.Server).DeliverAsync(Message, "ntf_01K0test", Ct);

        Assert.Equal((result, code, @class), (outcome.Result, outcome.Code, outcome.Class));
        if (result == ChannelResult.Delivered)
        {
            Assert.Equal("abc", outcome.Receipt);
        }
    }

    [Fact]
    public async Task A_server_that_does_not_answer_is_unreachable()
    {
        var host = $"127.0.0.1:{LoopbackNtfy.FreePort()}";
        await using var api = await PluginInvokerTests.StartAsync(paths => Install(paths, host));

        var outcome = await Channel(api, "http://" + host).DeliverAsync(Message, "ntf_01K0test", Ct);

        Assert.Equal((ChannelResult.Failed, "notification_unreachable", (FailureClass?)FailureClass.Transient), (outcome.Result, outcome.Code, outcome.Class));
    }

    /// <summary>A binding that names a server the package was not granted is the host's refusal, recorded, never a send.</summary>
    [Fact]
    public async Task A_server_whose_host_is_not_granted_is_a_recorded_failure()
    {
        using var ntfy = new LoopbackNtfy();
        await using var api = await PluginInvokerTests.StartAsync(paths => Install(paths, ntfy.HostKey));

        var outcome = await Channel(api, $"http://127.0.0.1:{LoopbackNtfy.FreePort()}").DeliverAsync(Message, "ntf_01K0test", Ct);

        Assert.Equal((ChannelResult.Failed, "http_host_not_allowed", (FailureClass?)FailureClass.Permanent), (outcome.Result, outcome.Code, outcome.Class));
        Assert.Empty(ntfy.Requests);
    }

    /// <summary>
    /// The binding's schema accepts <c>http://</c>, the plugin host does not beyond the loopback interface: a granted host
    /// named over plain http is the host's refusal, decided before any request is made.
    /// </summary>
    [Fact]
    public async Task A_granted_server_named_over_http_beyond_the_loopback_is_a_recorded_failure()
    {
        await using var api = await PluginInvokerTests.StartAsync(paths => Install(paths, "ntfy.example"));

        var outcome = await Channel(api, "http://ntfy.example").DeliverAsync(Message, "ntf_01K0test", Ct);

        Assert.Equal((ChannelResult.Failed, "http_scheme_not_allowed", (FailureClass?)FailureClass.Permanent), (outcome.Result, outcome.Code, outcome.Class));
    }

    /// <summary>The topic travels in the body, so the host's log line about the call names the root and not the topic.</summary>
    [Fact]
    public async Task The_topic_is_not_in_the_log()
    {
        using var ntfy = new LoopbackNtfy();
        await using var api = await PluginInvokerTests.StartAsync(paths => Install(paths, ntfy.HostKey));

        await Channel(api, ntfy.Server, topic: "jason-topic-7f3c").DeliverAsync(Message, "ntf_01K0test", Ct);

        var written = InvocationFiles(api);
        Assert.Contains("\"path\":\"/\"", written, StringComparison.Ordinal);
        Assert.DoesNotContain("jason-topic-7f3c", written, StringComparison.Ordinal);
    }

    /// <summary>The self-hosted edit makes a package of another identity: the digest is the copy's, not the shipped one's.</summary>
    [Fact]
    public async Task Editing_the_manifest_changes_the_package_s_identity()
    {
        string shipped;
        await using (var api = await PluginInvokerTests.StartAsync(paths => Install(paths, host: null)))
        {
            shipped = api.Resolve<PluginRegistry>().Snapshot.Find(Id)!.Digest;
        }

        using var ntfy = new LoopbackNtfy();
        await using var edited = await PluginInvokerTests.StartAsync(paths => Install(paths, ntfy.HostKey));

        Assert.NotEqual(shipped, edited.Resolve<PluginRegistry>().Snapshot.Find(Id)!.Digest);
    }

    [Fact]
    public async Task The_shipped_package_loads_as_a_notification_plugin_declaring_delivery_1()
    {
        await using var api = await PluginInvokerTests.StartAsync(paths => Install(paths, host: null));

        var plugin = api.Resolve<PluginRegistry>().Snapshot.Find(Id);

        Assert.NotNull(plugin);
        Assert.Equal(PluginStatus.Valid, plugin.Status);
        Assert.Equal(PluginKind.Notification, plugin.Manifest.Kind);
        Assert.Contains(DeliveryContract.CurrentVersion, plugin.Manifest.Contracts.Operations);
        Assert.Equal(["ntfy.sh"], plugin.Manifest.Capabilities.Http!.Hosts);
    }
}
