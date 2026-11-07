using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Jason.Contracts.Discovery;
using Jason.Contracts.Notifications;
using Jason.Contracts.Plugins;
using Jason.Runtime.Notifications;
using Jason.Runtime.Notifications.Plugins;
using Jason.Runtime.Plugins.Manifest;
using Jason.Runtime.Plugins.Registry;
using Jason.Runtime.Tests.Documentation;
using Jason.Runtime.Tests.Plugins;
using Jason.Runtime.Tests.Plugins.Invocation;
using Microsoft.Extensions.DependencyInjection;

namespace Jason.Runtime.Tests.Notifications;

/// <summary>
/// The ntfy reference plugin, through the real plugin host, against an ntfy server on the loopback interface. The
/// shipped package is copied whole and the loopback server's host granted — the self-hosted procedure its own README
/// gives, which edits no file of the package, so what runs here is the package a release publishes.
/// </summary>
public sealed class NtfyPackageTests
{
    private const string Id = "ntfy";
    private const string TokenVariable = "NTFY_TOKEN";

    private static readonly NotificationMessage Message = NotificationComposer.Compose(3, 1);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static string Shipped => Path.Combine(AppContext.BaseDirectory, "Fixtures", "plugins", Id);

    /// <summary>The shipped package copied into a data directory, and granted the host given and the token.</summary>
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
    [Critical(Family.S3, "ntfy's token")]
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

        // What the README says of it: the failure's message is the operating system's wording, and that names the binding's
        // server by its address, on a line every subscriber of the kind reads.
        Assert.Contains(host, outcome.Detail, StringComparison.Ordinal);
    }

    /// <summary>
    /// A binding that names a server the package was not granted is the host's refusal, recorded, never a send, though the
    /// manifest leaves the host to the grant: a grant of another host does not reach it, neither does the star, which
    /// means the declared host alone, and no grant at all is no capability to call with.
    /// </summary>
    [Theory]
    [InlineData("another host", "http_host_not_allowed")]
    [InlineData("the star", "http_host_not_allowed")]
    [InlineData("no grant", "capability_not_granted")]
    public async Task A_server_whose_host_is_not_granted_is_a_recorded_failure(string grant, string code)
    {
        using var ntfy = new LoopbackNtfy();
        await using var api = await PluginInvokerTests.StartAsync(paths =>
            Install(paths, grant switch { "another host" => $"127.0.0.1:{LoopbackNtfy.FreePort()}", "the star" => "*", _ => null }));

        var outcome = await Channel(api, ntfy.Server).DeliverAsync(Message, "ntf_01K0test", Ct);

        Assert.Equal((ChannelResult.Failed, code, (FailureClass?)FailureClass.Permanent), (outcome.Result, outcome.Code, outcome.Class));
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

    /// <summary>
    /// A server of the person's own takes a grant and a binding, and the package that reaches it is the one a release
    /// publishes: no file of it changed, so its digest is the last line of its list, and an update moves it on.
    /// </summary>
    [Fact]
    public async Task A_self_hosted_server_takes_a_grant_and_the_package_stays_the_published_one()
    {
        using var ntfy = new LoopbackNtfy();
        await using var api = await PluginInvokerTests.StartAsync(paths => Install(paths, ntfy.HostKey));

        var outcome = await Channel(api, ntfy.Server).DeliverAsync(Message, "ntf_01K0test", Ct);

        Assert.Equal(ChannelResult.Delivered, outcome.Result);
        var listed = File.ReadAllLines(Path.Combine(RepositoryTree.Root(), DigestList)).Last(entry => entry.Length > 0 && !entry.StartsWith('#'));
        Assert.Equal(listed, $"{PackageVersion} {api.Resolve<PluginRegistry>().Snapshot.Find(Id)!.Digest}");
    }

    [Fact]
    public async Task The_shipped_package_loads_as_a_notification_plugin_declaring_delivery_1()
    {
        await using var api = await PluginInvokerTests.StartAsync(paths => Install(paths, host: null));

        var plugin = api.Resolve<PluginRegistry>().Snapshot.Find(Id);

        Assert.NotNull(plugin);
        Assert.Equal(PluginStatus.Valid, plugin.Status);
        Assert.Equal(PluginKind.Notification, plugin.Manifest.Kind);

        // The content digest covers every file in the package, its README included, so the version moves with it.
        Assert.Equal(PackageVersion, plugin.Manifest.Version);
        Assert.Contains(DeliveryContract.CurrentVersion, plugin.Manifest.Contracts.Operations);
        Assert.Equal(["ntfy.sh"], plugin.Manifest.Capabilities.Http!.Hosts);
        Assert.True(plugin.Manifest.Capabilities.Http!.AnyGrantedHost);
    }

    /// <summary>The version of the package as this tree holds it.</summary>
    private const string PackageVersion = "1.0.3";

    /// <summary>The list beside the package that records the digest of each of its versions, as Reply's list does for Reply's.</summary>
    private const string DigestList = "plugins/ntfy.digests";

    /// <summary>
    /// The digest of each version the package had before a release published it, computed from the tree of the tag that
    /// first shipped it: v0.2.0, v0.5.0 and v0.7.0. A tag's tree never changes, so these lines never do: they are what lets
    /// <c>jason update</c> read a copy a person made by hand from any of those releases as its version, and replace it.
    /// </summary>
    private static readonly string[] ShippedInTheTree =
    [
        "1.0.0 sha256:16394c9b2ff439c19a42a3bc069502bfd6b12c44b21e2a7be4448ac0eebefc12",
        "1.0.1 sha256:b95b95345e0dd6cf2933f38a58f1c558c4db5f5d1517d095ec1ed7ae203bec32",
        "1.0.2 sha256:bcd90d5dcdd558ac42a5d27b699bb1b9a3b129c8efb7fb887021192a9a0d8a34",
    ];

    /// <summary>
    /// A package's version is its content: the digest of the shipped package has one line on the list beside it, for the
    /// version its manifest names, and that line is the last, after the three versions the tags shipped in their trees.
    /// </summary>
    [Fact]
    [Critical(Family.R1, "a package's version is its content")]
    public void The_package_is_the_digest_its_version_has_on_the_list()
    {
        var root = RepositoryTree.Root();
        var package = Path.Combine(root, "plugins", Id);
        var version = YamlToJson.Convert(File.ReadAllText(Path.Combine(package, "plugin.yaml")))!["version"]!.GetValue<string>();
        var line = $"{version} {PackageDigest.Compute(package).Digest}";

        var path = Path.Combine(root, DigestList);
        Assert.True(File.Exists(path), $"{DigestList} is missing. Create it with a header comment and this line:{Environment.NewLine}{line}");

        var listed = File.ReadAllLines(path).Where(entry => entry.Length > 0 && !entry.StartsWith('#')).ToList();
        foreach (var entry in listed)
        {
            Assert.Matches(new Regex("^(0|[1-9][0-9]*)\\.(0|[1-9][0-9]*)\\.(0|[1-9][0-9]*) sha256:[0-9a-f]{64}$"), entry);
        }

        Assert.Equal(ShippedInTheTree, listed.Take(ShippedInTheTree.Length));
        Assert.Equal(listed.Count, listed.Select(entry => entry.Split(' ')[0]).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(listed.Count, listed.Select(entry => entry.Split(' ')[1]).Distinct(StringComparer.Ordinal).Count());
        Assert.True(
            listed[^1] == line,
            $"The last line of {DigestList} is the shipped package's, version {version}. Append this line, after the last edit to the package:{Environment.NewLine}{line}");
    }
}
