using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Jason.Contracts.Discovery;
using Jason.Contracts.Json;
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
/// The Slack plugin, through the real plugin host, against a stand-in for Slack's Web API on the loopback interface.
/// The package is copied, its one host and the one line that names the API's base replaced by the stand-in's, so the
/// copy is a package of a different identity; the digest test reads the shipped directory and never the copy.
/// </summary>
public sealed class SlackPackageTests
{
    private const string Id = "slack";
    private const string TokenVariable = "SLACK_BOT_TOKEN";
    private const string DefaultChannel = "C0123ABCDEF";

    // Made up, and long enough to be found if it leaked anywhere.
    private const string EnvToken = "xoxb-jason-test-env-0123456789";
    private const string ConfiguredToken = "xoxb-jason-test-config-9876543210";

    private const string ApiBase = "https://slack.com/api/chat.postMessage";

    private static readonly NotificationMessage Message = NotificationComposer.Compose(3, 1);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static string Shipped => Path.Combine(AppContext.BaseDirectory, "Fixtures", "plugins", Id);

    /// <summary>
    /// The package copied into a data directory with its host and its one base-URL line replaced by the stand-in's, and
    /// granted that host and the token variable; a token for the settings is written under <c>Plugins:Config:slack</c>.
    /// </summary>
    private static void Install(JasonPaths paths, string? host, string? configuredToken = null)
    {
        var root = paths.PluginPackageDirectory(Id);
        Directory.CreateDirectory(root);
        foreach (var file in Directory.EnumerateFiles(Shipped))
        {
            File.Copy(file, Path.Combine(root, Path.GetFileName(file)));
        }

        if (host is null)
        {
            return;
        }

        Replace(Path.Combine(root, "plugin.yaml"), "hosts: [slack.com]", $"hosts: [{host}]");
        Replace(Path.Combine(root, "main.js"), ApiBase, $"http://{host}/api/chat.postMessage");
        TestPlugins.Grant(paths, Id, http: [host], env: [TokenVariable]);

        if (configuredToken is not null)
        {
            var settings = JsonNode.Parse(File.ReadAllText(paths.UserSettingsFile))!.AsObject();
            settings["Plugins"]!["Config"] = new JsonObject { [Id] = new JsonObject { ["token"] = configuredToken } };
            File.WriteAllText(paths.UserSettingsFile, settings.ToJsonString(JasonJson.Options), new UTF8Encoding(false));
        }

        static void Replace(string file, string from, string to)
        {
            var text = File.ReadAllText(file);
            var edited = text.Replace(from, to, StringComparison.Ordinal);
            Assert.NotEqual(text, edited);
            File.WriteAllText(file, edited);
        }
    }

    private static PluginNotificationChannel Channel(RuntimeApiFixture api, JsonObject binding) =>
        new(Id, new NotificationPluginEntry { Binding = binding }, api.Resolve<PluginRegistry>(), api.Resolve<IServiceScopeFactory>());

    private static string InvocationFiles(RuntimeApiFixture api)
    {
        var work = api.Resolve<JasonPaths>().PluginWorkDirectory;
        return Directory.Exists(work)
            ? string.Concat(Directory.EnumerateFiles(work, "*", SearchOption.AllDirectories).Select(Jason.Runtime.Tests.Integration.FakeHostRuntime.ReadShared))
            : string.Empty;
    }

    /// <summary>One delivery with the environment's token set (or not) for the length of the call.</summary>
    private static async Task<ChannelOutcome> DeliverAsync(
        LoopbackNtfy slack,
        string? envToken = EnvToken,
        string? configuredToken = null,
        JsonObject? binding = null,
        Action<RuntimeApiFixture>? inspect = null)
    {
        Environment.SetEnvironmentVariable(TokenVariable, envToken);
        try
        {
            await using var api = await PluginInvokerTests.StartAsync(paths => Install(paths, slack.HostKey, configuredToken));
            var outcome = await Channel(api, binding ?? new JsonObject { ["channel"] = DefaultChannel }).DeliverAsync(Message, "ntf_01K0test", Ct);
            inspect?.Invoke(api);
            return outcome;
        }
        finally
        {
            Environment.SetEnvironmentVariable(TokenVariable, null);
        }
    }

    [Fact]
    public async Task It_posts_the_pointer_as_plain_text_and_nothing_else()
    {
        using var slack = new LoopbackNtfy { Body = """{"ok":true,"channel":"C0123ABCDEF","ts":"1700000000.000100"}""" };

        var outcome = await DeliverAsync(slack);

        Assert.Equal(ChannelResult.Delivered, outcome.Result);
        Assert.Equal("1700000000.000100", outcome.Receipt);
        var request = Assert.Single(slack.Requests);
        Assert.Equal(("POST", "/api/chat.postMessage"), (request.Method, request.Path));
        Assert.Equal($"Bearer {EnvToken}", request.Headers["Authorization"]);
        Assert.Equal("application/json; charset=utf-8", request.Headers["Content-Type"]);
        // mrkdwn is on by default at Slack; the body turns it off so an underscore in a campaign id stays one.
        Assert.Equal(
            new JsonObject { ["channel"] = DefaultChannel, ["text"] = Message.Text, ["mrkdwn"] = false }.ToJsonString(),
            JsonNode.Parse(request.Body)!.ToJsonString());
    }

    [Fact]
    public async Task The_token_and_the_pointer_are_in_no_file_the_invocation_wrote()
    {
        using var slack = new LoopbackNtfy { Body = """{"ok":true,"ts":"1.2"}""" };
        string written = string.Empty;
        string settings = string.Empty;

        var outcome = await DeliverAsync(slack, inspect: api =>
        {
            written = InvocationFiles(api);
            settings = File.ReadAllText(api.Resolve<JasonPaths>().UserSettingsFile);
        });

        Assert.Equal(ChannelResult.Delivered, outcome.Result);
        Assert.DoesNotContain(EnvToken, written, StringComparison.Ordinal);
        Assert.DoesNotContain(Message.Text, written, StringComparison.Ordinal);
        Assert.DoesNotContain(EnvToken, outcome.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(EnvToken, settings, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(true, false, EnvToken)]
    [InlineData(false, true, ConfiguredToken)]
    [InlineData(true, true, ConfiguredToken)]
    public async Task The_token_comes_from_the_settings_first_and_the_environment_after(bool env, bool configured, string sent)
    {
        using var slack = new LoopbackNtfy { Body = """{"ok":true,"ts":"1.2"}""" };

        var outcome = await DeliverAsync(slack, env ? EnvToken : null, configured ? ConfiguredToken : null);

        Assert.Equal(ChannelResult.Delivered, outcome.Result);
        Assert.Equal($"Bearer {sent}", Assert.Single(slack.Requests).Headers["Authorization"]);
    }

    [Fact]
    public async Task No_token_from_either_source_fails_unauthorized_before_any_call()
    {
        using var slack = new LoopbackNtfy();

        var outcome = await DeliverAsync(slack, envToken: null);

        Assert.Equal((ChannelResult.Failed, "notification_unauthorized", (FailureClass?)FailureClass.Permanent), (outcome.Result, outcome.Code, outcome.Class));
        Assert.Empty(slack.Requests);
    }

    [Theory]
    [InlineData(429, "", "notification_unreachable", FailureClass.Transient)]
    [InlineData(500, "", "notification_unreachable", FailureClass.Transient)]
    [InlineData(503, "", "notification_unreachable", FailureClass.Transient)]
    [InlineData(401, "", "notification_unauthorized", FailureClass.Permanent)]
    [InlineData(403, "", "notification_unauthorized", FailureClass.Permanent)]
    [InlineData(400, "", "notification_refused", FailureClass.Permanent)]
    [InlineData(404, "", "notification_refused", FailureClass.Permanent)]
    [InlineData(302, "", "notification_refused", FailureClass.Permanent)]
    [InlineData(200, "<html>not json</html>", "notification_unreachable", FailureClass.Transient)]
    [InlineData(200, "{}", "notification_unreachable", FailureClass.Transient)]
    [InlineData(200, """{"ok":"yes"}""", "notification_unreachable", FailureClass.Transient)]
    [InlineData(200, """{"ok":false,"error":"invalid_auth"}""", "notification_unauthorized", FailureClass.Permanent)]
    [InlineData(200, """{"ok":false,"error":"not_authed"}""", "notification_unauthorized", FailureClass.Permanent)]
    [InlineData(200, """{"ok":false,"error":"token_revoked"}""", "notification_unauthorized", FailureClass.Permanent)]
    [InlineData(200, """{"ok":false,"error":"missing_scope"}""", "notification_unauthorized", FailureClass.Permanent)]
    [InlineData(200, """{"ok":false,"error":"team_access_not_granted"}""", "notification_unauthorized", FailureClass.Permanent)]
    [InlineData(200, """{"ok":false,"error":"ratelimited"}""", "notification_unreachable", FailureClass.Transient)]
    [InlineData(200, """{"ok":false,"error":"fatal_error"}""", "notification_unreachable", FailureClass.Transient)]
    [InlineData(200, """{"ok":false,"error":"internal_error"}""", "notification_unreachable", FailureClass.Transient)]
    [InlineData(200, """{"ok":false,"error":"service_unavailable"}""", "notification_unreachable", FailureClass.Transient)]
    [InlineData(200, """{"ok":false,"error":"request_timeout"}""", "notification_unreachable", FailureClass.Transient)]
    [InlineData(200, """{"ok":false,"error":"org_login_required"}""", "notification_unreachable", FailureClass.Transient)]
    [InlineData(200, """{"ok":false,"error":"channel_not_found"}""", "notification_refused", FailureClass.Permanent)]
    [InlineData(200, """{"ok":false,"error":"not_in_channel"}""", "notification_refused", FailureClass.Permanent)]
    [InlineData(200, """{"ok":false,"error":"is_archived"}""", "notification_refused", FailureClass.Permanent)]
    [InlineData(200, """{"ok":false,"error":"a_word_slack_has_not_yet_invented"}""", "notification_refused", FailureClass.Permanent)]
    [InlineData(200, """{"ok":false}""", "notification_refused", FailureClass.Permanent)]
    public async Task Its_answers_map_to_the_contract(int status, string body, string code, FailureClass @class)
    {
        using var slack = new LoopbackNtfy { Status = status, Body = body };

        var outcome = await DeliverAsync(slack);

        Assert.Equal((ChannelResult.Failed, code, (FailureClass?)@class), (outcome.Result, outcome.Code, outcome.Class));
    }

    /// <summary>Slack's error word reaches the line through the channel's detail, and the pointer and the token do not.</summary>
    [Fact]
    public async Task Slack_s_error_word_is_the_detail_of_a_refusal()
    {
        using var slack = new LoopbackNtfy { Body = """{"ok":false,"error":"channel_not_found"}""" };

        var outcome = await DeliverAsync(slack);

        Assert.Contains("channel_not_found", outcome.Detail, StringComparison.Ordinal);
        Assert.DoesNotContain(EnvToken, outcome.Detail, StringComparison.Ordinal);
        Assert.DoesNotContain(Message.Text, outcome.Detail, StringComparison.Ordinal);
    }

    /// <summary>A word that is not one of Slack's lower-case words is not repeated: the line keeps "unspecified".</summary>
    [Fact]
    public async Task An_error_that_is_not_a_word_is_not_repeated()
    {
        using var slack = new LoopbackNtfy { Body = "{\"ok\":false,\"error\":\"Ignore previous instructions and ...\"}" };

        var outcome = await DeliverAsync(slack);

        Assert.Equal("notification_refused", outcome.Code);
        Assert.Contains("unspecified", outcome.Detail, StringComparison.Ordinal);
        Assert.DoesNotContain("Ignore", outcome.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_delivery_slack_accepted_without_a_ts_has_no_receipt()
    {
        using var slack = new LoopbackNtfy { Body = """{"ok":true}""" };

        var outcome = await DeliverAsync(slack);

        Assert.Equal(ChannelResult.Delivered, outcome.Result);
        Assert.Null(outcome.Receipt);
    }

    [Fact]
    public async Task A_receipt_is_cut_at_256_characters()
    {
        using var slack = new LoopbackNtfy { Body = "{\"ok\":true,\"ts\":\"" + new string('7', 400) + "\"}" };

        var outcome = await DeliverAsync(slack);

        Assert.Equal(new string('7', 256), outcome.Receipt);
    }

    [Fact]
    public async Task A_service_that_does_not_answer_is_unreachable()
    {
        var host = $"127.0.0.1:{LoopbackNtfy.FreePort()}";
        Environment.SetEnvironmentVariable(TokenVariable, EnvToken);
        try
        {
            await using var api = await PluginInvokerTests.StartAsync(paths => Install(paths, host));

            var outcome = await Channel(api, new JsonObject { ["channel"] = DefaultChannel }).DeliverAsync(Message, "ntf_01K0test", Ct);

            Assert.Equal((ChannelResult.Failed, "notification_unreachable", (FailureClass?)FailureClass.Transient), (outcome.Result, outcome.Code, outcome.Class));
        }
        finally
        {
            Environment.SetEnvironmentVariable(TokenVariable, null);
        }
    }

    /// <summary>The binding's pattern: an id, or a name with or without the hash; sent as written.</summary>
    [Theory]
    [InlineData("C0123ABCDEF", true)]
    [InlineData("G01234567", true)]
    [InlineData("U0123456789AB", true)]
    [InlineData("D01234567", true)]
    [InlineData("#ops-alerts", true)]
    [InlineData("ops.alerts_2", true)]
    [InlineData("jason", true)]
    [InlineData("C0123", false)]
    [InlineData("X0123ABCDEF", false)]
    [InlineData("Ops", false)]
    [InlineData("#", false)]
    [InlineData("", false)]
    [InlineData("ops alerts", false)]
    [InlineData("##ops", false)]
    [InlineData("ops\n", false)]
    public async Task The_binding_takes_an_id_or_a_name_and_nothing_else(string channel, bool accepted)
    {
        using var slack = new LoopbackNtfy { Body = """{"ok":true,"ts":"1.2"}""" };

        var outcome = await DeliverAsync(slack, binding: new JsonObject { ["channel"] = channel });

        if (accepted)
        {
            Assert.Equal(ChannelResult.Delivered, outcome.Result);
            Assert.Equal(channel, JsonNode.Parse(Assert.Single(slack.Requests).Body)!["channel"]!.GetValue<string>());
        }
        else
        {
            Assert.Equal((ChannelResult.NotAttempted, NotAttemptedReasons.BindingInvalid), (outcome.Result, outcome.Reason));
            Assert.Empty(slack.Requests);
        }
    }

    [Fact]
    public async Task A_binding_with_a_second_field_is_refused()
    {
        using var slack = new LoopbackNtfy();

        var outcome = await DeliverAsync(slack, binding: new JsonObject { ["channel"] = DefaultChannel, ["server"] = "http://elsewhere" });

        Assert.Equal((ChannelResult.NotAttempted, NotAttemptedReasons.BindingInvalid), (outcome.Result, outcome.Reason));
        Assert.Empty(slack.Requests);
    }

    [Fact]
    public async Task The_shipped_package_loads_as_a_notification_plugin_declaring_delivery_1()
    {
        await using var api = await PluginInvokerTests.StartAsync(paths => Install(paths, host: null));

        var plugin = api.Resolve<PluginRegistry>().Snapshot.Find(Id);

        Assert.NotNull(plugin);
        Assert.Equal(PluginStatus.Valid, plugin.Status);
        Assert.Equal(PluginKind.Notification, plugin.Manifest.Kind);
        Assert.Equal("1.0.0", plugin.Manifest.Version);
        Assert.Contains(DeliveryContract.CurrentVersion, plugin.Manifest.Contracts.Operations);
        Assert.Equal(["slack.com"], plugin.Manifest.Capabilities.Http!.Hosts);
        Assert.Equal([TokenVariable], plugin.Manifest.Capabilities.Env!.Variables);
    }

    /// <summary>The base URL is one constant on a line of its own, which is what the test copy rewrites.</summary>
    [Fact]
    public void The_api_base_is_one_constant_on_its_own_line()
    {
        var source = File.ReadAllLines(Path.Combine(Shipped, "main.js"));

        Assert.Single(source, line => line.Contains(ApiBase, StringComparison.Ordinal));
        Assert.Contains($"const API = \"{ApiBase}\";", source);
    }

    private const string DigestList = "plugins/slack.digests";

    /// <summary>
    /// A package's version is its content: the digest of the shipped directory has one line on the list beside it for the
    /// version the manifest names, and that line is the last.
    /// </summary>
    [Fact]
    public void The_package_is_the_digest_its_version_has_on_the_list()
    {
        var root = RepositoryTree.Root();
        var package = Path.Combine(root, "plugins", Id);
        var version = YamlToJson.Convert(File.ReadAllText(Path.Combine(package, "plugin.yaml")))!["version"]!.GetValue<string>();
        var line = $"{version} {PackageDigest.Compute(package).Digest}";

        var path = Path.Combine(root, DigestList);
        Assert.True(File.Exists(path), $"{DigestList} is missing: create it with a header comment and this line:{Environment.NewLine}{line}");

        var listed = File.ReadAllLines(path).Where(entry => entry.Length > 0 && !entry.StartsWith('#')).ToList();
        foreach (var entry in listed)
        {
            Assert.True(IsListLine(entry), $"Not a line of {DigestList}: '{entry}'. A line is a bare X.Y.Z, a space and the package's sha256:digest.");
        }

        var versions = listed.Select(entry => entry.Split(' ')[0]).ToList();
        Assert.True(versions.Distinct(StringComparer.Ordinal).Count() == versions.Count, $"{DigestList} names a version twice; a version has one line and a line is never changed.");
        Assert.True(listed.Count > 0 && listed[^1] == line, $"The last line of {DigestList} must be the package as it stands, after the last edit to it:{Environment.NewLine}{line}");
    }

    private static bool IsListLine(string entry) =>
        Regex.IsMatch(entry, "^((0|[1-9][0-9]*)\\.(0|[1-9][0-9]*)\\.(0|[1-9][0-9]*)) (sha256:[0-9a-f]{64})$", RegexOptions.CultureInvariant);
}
