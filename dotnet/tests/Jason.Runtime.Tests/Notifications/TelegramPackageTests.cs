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
/// The Telegram plugin, through the real plugin host, against a stand-in for the Bot API on the loopback interface. The
/// package is copied and the manifest's one host and the one base-URL constant of <c>main.js</c> are replaced by the
/// stand-in's, which is an edit of the package and so a package of a different identity; the digest test reads the
/// shipped directory. The tokens here are made up.
/// </summary>
public sealed class TelegramPackageTests
{
    private const string Id = "telegram";
    private const string TokenVariable = "TELEGRAM_BOT_TOKEN";
    private const string ConfigToken = "7000000001:AAF-made-up-config-token-0123";
    private const string EnvToken = "7000000002:AAF-made-up-env-token-456789";
    private const string Chat = "-1001234567890";
    private const string Answered = """{"ok":true,"result":{"message_id":42}}""";

    private static readonly NotificationMessage Message = NotificationComposer.Compose(3, 1);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static string Shipped => Path.Combine(AppContext.BaseDirectory, "Fixtures", "plugins", Id);

    /// <summary>
    /// The package copied into a data directory with its host and base URL replaced, granted that host and the token variable,
    /// and given the token under <c>Plugins:Config</c> when one is passed.
    /// </summary>
    private static void Install(JasonPaths paths, string? host, string? configToken = null)
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

        Edit(Path.Combine(root, "plugin.yaml"), "hosts: [api.telegram.org]", $"hosts: [{host}]");
        Edit(Path.Combine(root, "main.js"), "const BASE = \"https://api.telegram.org\";", $"const BASE = \"http://{host}\";");
        TestPlugins.Grant(paths, Id, http: [host], env: [TokenVariable]);

        if (configToken is not null)
        {
            var settings = JsonNode.Parse(File.ReadAllText(paths.UserSettingsFile))!.AsObject();
            settings["Plugins"]!["Config"] = new JsonObject { [Id] = new JsonObject { ["token"] = configToken } };
            File.WriteAllText(paths.UserSettingsFile, settings.ToJsonString());
        }
    }

    private static void Edit(string file, string from, string to)
    {
        var text = File.ReadAllText(file);
        var edited = text.Replace(from, to, StringComparison.Ordinal);
        Assert.NotEqual(text, edited);
        File.WriteAllText(file, edited);
    }

    private static PluginNotificationChannel Channel(RuntimeApiFixture api, JsonObject binding) =>
        new(Id, new NotificationPluginEntry { Binding = binding }, api.Resolve<PluginRegistry>(), api.Resolve<IServiceScopeFactory>());

    private static JsonObject ChatBinding(string chat = Chat) => new() { ["chat"] = chat };

    /// <summary>Everything the invocations wrote in their own directories.</summary>
    private static string InvocationFiles(RuntimeApiFixture api)
    {
        var work = api.Resolve<JasonPaths>().PluginWorkDirectory;
        return Directory.Exists(work)
            ? string.Concat(Directory.EnumerateFiles(work, "*", SearchOption.AllDirectories).Select(Jason.Runtime.Tests.Integration.FakeHostRuntime.ReadShared))
            : string.Empty;
    }

    /// <summary>Delivers once to a stand-in answering as given, with the token in the places the test says; the stand-in and the runtime are returned with the outcome.</summary>
    private static async Task<(ChannelOutcome Outcome, LoopbackNtfy Server, RuntimeApiFixture Api)> DeliverAsync(
        int status,
        string body,
        string? configToken,
        string? envToken,
        string chat = Chat)
    {
        var server = new LoopbackNtfy { Status = status, Body = body };
        var previous = Environment.GetEnvironmentVariable(TokenVariable);
        Environment.SetEnvironmentVariable(TokenVariable, envToken);
        try
        {
            var api = await PluginInvokerTests.StartAsync(paths => Install(paths, server.HostKey, configToken));
            var outcome = await Channel(api, ChatBinding(chat)).DeliverAsync(Message, "ntf_01K0test", Ct);
            return (outcome, server, api);
        }
        catch
        {
            server.Dispose();
            throw;
        }
        finally
        {
            Environment.SetEnvironmentVariable(TokenVariable, previous);
        }
    }

    [Fact]
    public async Task It_sends_the_pointer_as_plain_text_with_the_token_in_the_path_only()
    {
        var (outcome, server, api) = await DeliverAsync(200, Answered, ConfigToken, null);
        using (server)
        await using (api)
        {
            Assert.Equal((ChannelResult.Delivered, "42"), (outcome.Result, outcome.Receipt));
            var request = Assert.Single(server.Requests);
            Assert.Equal(("POST", $"/bot{ConfigToken}/sendMessage"), (request.Method, request.Path));
            Assert.StartsWith("application/json", request.Headers["Content-Type"], StringComparison.Ordinal);
            var body = JsonNode.Parse(request.Body)!.AsObject();
            Assert.Equal(["chat_id", "text"], body.Select(pair => pair.Key));
            Assert.Equal(Message.Text, body["text"]!.GetValue<string>());
            Assert.DoesNotContain(ConfigToken, request.Body, StringComparison.Ordinal);
            Assert.DoesNotContain(ConfigToken, outcome.ToString(), StringComparison.Ordinal);
            // The positive control: the host's own line about the call is in the files, with the token masked in the path.
            var written = InvocationFiles(api);
            Assert.Contains("\"path\":\"/bot[redacted]/sendMessage\"", written, StringComparison.Ordinal);
            Assert.DoesNotContain("AAF-made-up", written, StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData(200, Answered, ChannelResult.Delivered, null, null)]
    [InlineData(401, """{"ok":false,"error_code":401,"description":"Unauthorized"}""", ChannelResult.Failed, "notification_unauthorized", FailureClass.Permanent)]
    [InlineData(404, """{"ok":false,"error_code":404,"description":"Not Found"}""", ChannelResult.Failed, "notification_unauthorized", FailureClass.Permanent)]
    [InlineData(400, """{"ok":false,"error_code":400,"description":"Bad Request: chat not found"}""", ChannelResult.Failed, "notification_refused", FailureClass.Permanent)]
    [InlineData(400, """{"ok":false,"error_code":400,"description":"Bad Request: group chat was upgraded to a supergroup chat","parameters":{"migrate_to_chat_id":-1009876543210}}""", ChannelResult.Failed, "notification_refused", FailureClass.Permanent)]
    [InlineData(403, """{"ok":false,"error_code":403,"description":"Forbidden: bot was blocked by the user"}""", ChannelResult.Failed, "notification_refused", FailureClass.Permanent)]
    [InlineData(409, "", ChannelResult.Failed, "notification_refused", FailureClass.Permanent)]
    [InlineData(302, "", ChannelResult.Failed, "notification_refused", FailureClass.Permanent)]
    [InlineData(429, """{"ok":false,"error_code":429,"description":"Too Many Requests: retry after 5","parameters":{"retry_after":5}}""", ChannelResult.Failed, "notification_unreachable", FailureClass.Transient)]
    [InlineData(500, "", ChannelResult.Failed, "notification_unreachable", FailureClass.Transient)]
    [InlineData(503, "", ChannelResult.Failed, "notification_unreachable", FailureClass.Transient)]
    [InlineData(200, "<html>not json</html>", ChannelResult.Failed, "notification_unreachable", FailureClass.Transient)]
    [InlineData(200, "{}", ChannelResult.Failed, "notification_unreachable", FailureClass.Transient)]
    [InlineData(200, """{"ok":"true"}""", ChannelResult.Failed, "notification_unreachable", FailureClass.Transient)]
    [InlineData(200, "null", ChannelResult.Failed, "notification_unreachable", FailureClass.Transient)]
    [InlineData(200, """{"ok":false,"description":"Bad Request: chat not found"}""", ChannelResult.Failed, "notification_refused", FailureClass.Permanent)]
    [InlineData(200, """{"ok":false,"error_code":500}""", ChannelResult.Failed, "notification_refused", FailureClass.Permanent)]
    public async Task Its_answers_map_to_the_contract_by_the_status_and_never_by_the_error_code(
        int status,
        string body,
        ChannelResult result,
        string? code,
        FailureClass? @class)
    {
        var (outcome, server, api) = await DeliverAsync(status, body, ConfigToken, null);
        using (server)
        await using (api)
        {
            Assert.Equal((result, code, @class), (outcome.Result, outcome.Code, outcome.Class));
            Assert.Single(server.Requests);
            Assert.DoesNotContain(ConfigToken, outcome.ToString(), StringComparison.Ordinal);
        }
    }

    /// <summary>Telegram's own words for a failure reach the line through the channel's detail, after the package's, at each answer that can carry them.</summary>
    [Theory]
    [InlineData(401, """{"ok":false,"error_code":401,"description":"Unauthorized"}""", "the server did not accept the bot token (401): Unauthorized")]
    [InlineData(404, """{"ok":false,"error_code":404,"description":"Not Found"}""", "the server did not accept the bot token (404): Not Found")]
    [InlineData(429, """{"ok":false,"error_code":429,"description":"Too Many Requests: retry after 5"}""", "the server cannot take messages for now (429): Too Many Requests: retry after 5")]
    [InlineData(503, """{"ok":false,"error_code":503,"description":"Service Unavailable"}""", "the server cannot take messages for now (503): Service Unavailable")]
    [InlineData(400, """{"ok":false,"error_code":400,"description":"Bad Request: chat not found"}""", "the server refused the message (400): Bad Request: chat not found")]
    [InlineData(200, """{"ok":false,"description":"Bad Request: chat not found"}""", "the server did not send the message: Bad Request: chat not found")]
    public async Task Telegram_s_description_is_the_detail_of_a_failure(int status, string body, string detail)
    {
        var (outcome, server, api) = await DeliverAsync(status, body, ConfigToken, null);
        using (server)
        await using (api)
        {
            Assert.Equal(detail, outcome.Detail);
        }
    }

    /// <summary>A description is one line of the detail: at most its first two hundred units, whatever Telegram sent.</summary>
    [Theory]
    [InlineData(200)]
    [InlineData(201)]
    [InlineData(4000)]
    public async Task A_description_past_two_hundred_is_cut_to_its_first_two_hundred(int length)
    {
        var description = string.Concat(Enumerable.Range(0, length).Select(index => (char)('a' + (index % 26))));
        var body = new JsonObject { ["ok"] = false, ["error_code"] = 400, ["description"] = description }.ToJsonString();

        var (outcome, server, api) = await DeliverAsync(400, body, ConfigToken, null);
        using (server)
        await using (api)
        {
            Assert.Equal("the server refused the message (400): " + description[..200], outcome.Detail);
        }
    }

    [Fact]
    public async Task A_server_that_does_not_answer_is_unreachable_and_the_token_is_not_in_the_outcome()
    {
        var host = $"127.0.0.1:{LoopbackNtfy.FreePort()}";
        await using var api = await PluginInvokerTests.StartAsync(paths => Install(paths, host, ConfigToken));

        var outcome = await Channel(api, ChatBinding()).DeliverAsync(Message, "ntf_01K0test", Ct);

        Assert.Equal((ChannelResult.Failed, "notification_unreachable", (FailureClass?)FailureClass.Transient), (outcome.Result, outcome.Code, outcome.Class));
        Assert.DoesNotContain(ConfigToken, outcome.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("AAF-made-up", InvocationFiles(api), StringComparison.Ordinal);
    }

    /// <summary>No token from either source is a refusal before any URL is built: nothing is sent, not even to a path with no token in it.</summary>
    [Fact]
    public async Task Without_a_token_nothing_is_sent_and_the_channel_is_unauthorized()
    {
        var (outcome, server, api) = await DeliverAsync(200, Answered, configToken: null, envToken: null);
        using (server)
        await using (api)
        {
            Assert.Equal((ChannelResult.Failed, "notification_unauthorized", (FailureClass?)FailureClass.Permanent), (outcome.Result, outcome.Code, outcome.Class));
            Assert.Empty(server.Requests);
        }
    }

    [Fact]
    public async Task A_token_in_the_environment_alone_is_used()
    {
        var (outcome, server, api) = await DeliverAsync(200, Answered, configToken: null, envToken: EnvToken);
        using (server)
        await using (api)
        {
            Assert.Equal(ChannelResult.Delivered, outcome.Result);
            Assert.Equal($"/bot{EnvToken}/sendMessage", Assert.Single(server.Requests).Path);
            Assert.DoesNotContain(EnvToken, outcome.ToString(), StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task The_token_written_in_the_settings_is_read_before_the_environment()
    {
        var (outcome, server, api) = await DeliverAsync(200, Answered, ConfigToken, EnvToken);
        using (server)
        await using (api)
        {
            Assert.Equal(ChannelResult.Delivered, outcome.Result);
            Assert.Equal($"/bot{ConfigToken}/sendMessage", Assert.Single(server.Requests).Path);
        }
    }

    /// <summary>The documented form is an Integer, so a chat that a number holds exactly travels as one; an @name and a number nothing could hold travel as text.</summary>
    [Theory]
    [InlineData("-1001234567890", "\"chat_id\":-1001234567890")]
    [InlineData("123456789", "\"chat_id\":123456789")]
    [InlineData("@jason_alerts", "\"chat_id\":\"@jason_alerts\"")]
    [InlineData("12345678901234567890", "\"chat_id\":\"12345678901234567890\"")]
    [InlineData("-9007199254740993", "\"chat_id\":\"-9007199254740993\"")]
    [InlineData("9007199254740991", "\"chat_id\":9007199254740991")]
    [InlineData("007", "\"chat_id\":\"007\"")]
    [InlineData("-0", "\"chat_id\":\"-0\"")]
    public async Task The_chat_travels_as_a_number_when_a_number_holds_it_exactly(string chat, string expected)
    {
        var (outcome, server, api) = await DeliverAsync(200, Answered, ConfigToken, null, chat);
        using (server)
        await using (api)
        {
            Assert.Equal(ChannelResult.Delivered, outcome.Result);
            Assert.Contains(expected, Assert.Single(server.Requests).Body, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task A_confirmation_without_a_message_id_answers_no_receipt()
    {
        var (outcome, server, api) = await DeliverAsync(200, """{"ok":true,"result":{}}""", ConfigToken, null);
        using (server)
        await using (api)
        {
            Assert.Equal((ChannelResult.Delivered, (string?)null), (outcome.Result, outcome.Receipt));
        }
    }

    [Theory]
    [InlineData("-1001234567890", true)]
    [InlineData("123", true)]
    [InlineData("@jason_alerts", true)]
    [InlineData("@abcde", true)]
    [InlineData("", false)]
    [InlineData("@abcd", false)]
    [InlineData("@Abcde", true)]
    [InlineData("@1abcde", false)]
    [InlineData("@_abcde", false)]
    [InlineData("jason_alerts", false)]
    [InlineData("@jason alerts", false)]
    [InlineData("12a", false)]
    [InlineData("12345678901234567890", true)]
    [InlineData("123456789012345678901", false)]
    [InlineData("1234567890123456789012", false)]
    [InlineData("-", false)]
    [InlineData("-1001234567890\n", false)]
    public async Task The_binding_takes_a_number_or_an_at_name_and_nothing_else(string chat, bool accepted)
    {
        var (outcome, server, api) = await DeliverAsync(200, Answered, ConfigToken, null, chat);
        using (server)
        await using (api)
        {
            Assert.Equal(accepted ? ChannelResult.Delivered : ChannelResult.NotAttempted, outcome.Result);
            if (!accepted)
            {
                Assert.Equal("binding_invalid", outcome.Reason);
                Assert.Empty(server.Requests);
            }
        }
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("""{"chat":"123","parse_mode":"Markdown"}""")]
    public async Task A_binding_needs_a_chat_and_takes_no_other_field(string binding)
    {
        using var server = new LoopbackNtfy { Body = Answered };
        await using var api = await PluginInvokerTests.StartAsync(paths => Install(paths, server.HostKey, ConfigToken));

        var outcome = await Channel(api, JsonNode.Parse(binding)!.AsObject()).DeliverAsync(Message, "ntf_01K0test", Ct);

        Assert.Equal((ChannelResult.NotAttempted, "binding_invalid"), (outcome.Result, outcome.Reason));
        Assert.Empty(server.Requests);
    }

    [Fact]
    public async Task The_shipped_package_loads_as_a_notification_plugin_asking_for_the_one_host_and_the_one_variable()
    {
        await using var api = await PluginInvokerTests.StartAsync(paths => Install(paths, host: null));

        var plugin = api.Resolve<PluginRegistry>().Snapshot.Find(Id);

        Assert.NotNull(plugin);
        Assert.Equal(PluginStatus.Valid, plugin.Status);
        Assert.Equal(PluginKind.Notification, plugin.Manifest.Kind);
        Assert.Equal("1.0.0", plugin.Manifest.Version);
        Assert.Contains(DeliveryContract.CurrentVersion, plugin.Manifest.Contracts.Operations);
        Assert.Equal(["api.telegram.org"], plugin.Manifest.Capabilities.Http!.Hosts);
        Assert.Equal([TokenVariable], plugin.Manifest.Capabilities.Env!.Variables);
    }

    [Fact]
    public void The_base_url_is_one_constant_on_a_line_of_its_own()
    {
        var lines = File.ReadAllLines(Path.Combine(Shipped, "main.js"));

        Assert.Single(lines, line => line == "const BASE = \"https://api.telegram.org\";");
        Assert.Single(lines, line => line.Contains("api.telegram.org", StringComparison.Ordinal) && !line.TrimStart().StartsWith("//", StringComparison.Ordinal));
    }

    private const string DigestList = "plugins/telegram.digests";

    /// <summary>
    /// The package as v0.32.0 shipped it, version 1.0.0, computed from that tag's tree. A tag's tree never changes, so this
    /// line never does: it is what lets <c>jason update</c> read a copy of that release as version 1.0.0 and replace it.
    /// </summary>
    private const string FirstShipped = "1.0.0 sha256:808fcbbd7cc58860ed128a6ed98d348ad2dbf1b6c11d5e89fcba6a12376cdaa9";

    /// <summary>
    /// A package's version is its content: the digest of the shipped package has one line on the list beside it, for the
    /// version its manifest names, and that line is the last. The list starts at 1.0.0, the package's first version.
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

        Assert.True(listed.Count > 0, $"{DigestList} has no line. Append this line, after the last edit to the package:{Environment.NewLine}{line}");
        Assert.True(listed[0] == FirstShipped, $"The first line of {DigestList} is the package 0.32.0 shipped, and it is: {FirstShipped}");
        Assert.Equal(listed.Count, listed.Select(entry => entry.Split(' ')[0]).Distinct(StringComparer.Ordinal).Count());
        Assert.True(
            listed[^1] == line,
            $"The last line of {DigestList} is the shipped package's, version {version}. Append this line, after the last edit to the package:{Environment.NewLine}{line}");
    }
}
