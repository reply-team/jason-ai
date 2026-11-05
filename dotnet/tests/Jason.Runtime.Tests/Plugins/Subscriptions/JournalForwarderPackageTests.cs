using System.Text;
using System.Text.Json.Nodes;
using Jason.Contracts.Api;
using Jason.Contracts.Discovery;
using Jason.Contracts.Ids;
using Jason.Contracts.Journal;
using Jason.Contracts.Json;
using Jason.Contracts.Plugins;
using Jason.Runtime.Persistence;
using Jason.Runtime.Plugins.Invocation;
using Jason.Runtime.Plugins.Registry;
using Jason.Runtime.Plugins.Subscriptions;
using Jason.Runtime.Tests.Integration;
using Jason.Runtime.Tests.Notifications;
using Jason.Runtime.Tests.Plugins.Invocation;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System.Globalization;

namespace Jason.Runtime.Tests.Plugins.Subscriptions;

/// <summary>
/// The journal-forwarder reference subscriber, through the real plugin host and the real subscription runner, against a
/// server on the loopback interface. The package is copied and its one host, <c>localhost:8787</c>, replaced by the
/// loopback server's: the owner's edit its README names, which makes a package of a different identity.
/// </summary>
[Collection(ProcessEnvironmentCollection.Name)]
public sealed class JournalForwarderPackageTests
{
    private const string Id = "journal-forwarder";
    private const string UrlVariable = "JOURNAL_FORWARDER_URL";
    private const string TokenVariable = "JOURNAL_FORWARDER_TOKEN";
    private const string Kind = "workitem_failed";
    private const string Token = "tk_jason_forwarder_test_0123456789";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static string Shipped => Path.Combine(AppContext.BaseDirectory, "Fixtures", "plugins", Id);

    /// <summary>The package copied into a data directory, its host replaced by the one given, and granted what a person grants it.</summary>
    private static void Install(JasonPaths paths, string? host, params string[] granted)
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
            var edited = File.ReadAllText(manifest).Replace("hosts: [localhost:8787]", $"hosts: [{host}]", StringComparison.Ordinal);
            Assert.NotEqual(File.ReadAllText(manifest), edited);
            File.WriteAllText(manifest, edited);
        }

        TestPlugins.Grant(paths, Id, http: [host ?? "localhost:8787"], env: [UrlVariable, TokenVariable]);
        Subscribe(paths, Id, granted.Length == 0 ? ["*"] : granted);
    }

    private static void Subscribe(JasonPaths paths, string id, params string[] kinds)
    {
        var settings = JsonNode.Parse(File.ReadAllText(paths.UserSettingsFile))!.AsObject();
        settings["Plugins"]!["Grants"]![id]!["Subscriptions"] = new JsonArray([.. kinds.Select(kind => (JsonNode)JsonValue.Create(kind)!)]);
        File.WriteAllText(paths.UserSettingsFile, settings.ToJsonString(JasonJson.Options), new UTF8Encoding(false));
    }

    /// <summary>A line of the kind written straight into the journal after the load placed the kind's position: its public id.</summary>
    private static string Line(JasonPaths paths, string kind = Kind)
    {
        var id = PublicId.New("jrn");
        using var connection = new SqliteConnection($"Data Source={paths.DatabaseFile}");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO journal (public_id, ts, actor_type, actor_id, kind, key) VALUES ($id, $ts, 'system', 'runtime', $kind, 'att_1')";
        command.Parameters.AddWithValue("$id", id);
        command.Parameters.AddWithValue("$ts", DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$kind", kind);
        command.ExecuteNonQuery();
        return id;
    }

    /// <summary>Runs the body with the process environment the runtime's children inherit, and puts it back.</summary>
    private static async Task WithEnvironment(string? url, string? token, Func<Task> body)
    {
        Environment.SetEnvironmentVariable(UrlVariable, url);
        Environment.SetEnvironmentVariable(TokenVariable, token);
        try
        {
            await body();
        }
        finally
        {
            Environment.SetEnvironmentVariable(UrlVariable, null);
            Environment.SetEnvironmentVariable(TokenVariable, null);
        }
    }

    private static async Task<SubscriptionRun> TakeAsync(RuntimeApiFixture api, string kind = Kind)
    {
        await api.Resolve<SubscriptionRunner>().RunNextAsync(kind, Ct);
        using var scope = api.Runtime.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<JasonDbContext>();
        return await db.SubscriptionRuns.AsNoTracking().OrderBy(run => run.Id).LastAsync(Ct);
    }

    private static string Outcome(SubscriptionRun run) => run.Handlers[^1]!["outcome"]!.GetValue<string>();

    private static string Code(SubscriptionRun run) => run.Handlers[^1]!["code"]?.GetValue<string>() ?? string.Empty;

    /// <summary>Everything the invocations wrote in their own directories.</summary>
    private static string InvocationFiles(RuntimeApiFixture api)
    {
        var work = api.Resolve<JasonPaths>().PluginWorkDirectory;
        return Directory.Exists(work)
            ? string.Concat(Directory.EnumerateFiles(work, "*", SearchOption.AllDirectories).Select(FakeHostRuntime.ReadShared))
            : string.Empty;
    }

    [Fact]
    public async Task A_granted_kinds_line_is_posted_once_with_its_body_and_headers_and_the_envelope_passes_through()
    {
        using var receiver = new LoopbackNtfy();
        await using var api = await PluginInvokerTests.StartAsync(
            paths =>
            {
                TestPlugins.Write(
                    paths,
                    "aaa-before",
                    $"manifest_version: 1\nid: aaa-before\nversion: 1.0.0\nkind: subscriber\ncontracts:\n  protocol: [1]\nsubscriptions: [{Kind}]\n",
                    "export function invoke(operation, input) { return { result: { envelope: { before: input.line.id } } }; }");
                TestPlugins.Grant(paths, "aaa-before");
                Subscribe(paths, "aaa-before", Kind);
                Install(paths, receiver.HostKey, Kind);
            },
            settings: "{\"Dispatcher\":{\"Enabled\":false},\"Subscriptions\":{\"Order\":{\"" + Kind + "\":[\"aaa-before\",\"" + Id + "\"]}}}");

        await WithEnvironment(receiver.Server + "/hook", null, async () =>
        {
            var line = Line(api.Paths);

            var run = await TakeAsync(api);

            Assert.Equal(SubscriptionRunStatus.Succeeded, run.Status);
            var request = Assert.Single(receiver.Requests);
            Assert.Equal(("POST", "/hook"), (request.Method, request.Path));
            Assert.Equal("application/json", request.Headers["Content-Type"]);
            Assert.Equal(line, request.Headers["Jason-Line-Id"]);
            Assert.False(request.Headers.ContainsKey("Authorization"));
            var body = JsonNode.Parse(request.Body)!.AsObject();
            Assert.Equal(["line", "envelope", "attempt"], body.Select(pair => pair.Key));
            Assert.Equal((line, Kind), (body["line"]!["id"]!.GetValue<string>(), body["line"]!["kind"]!.GetValue<string>()));
            Assert.Equal(1, body["attempt"]!.GetValue<int>());
            Assert.Equal($$"""{"before":"{{line}}"}""", body["envelope"]!.ToJsonString());
            Assert.Equal(body["envelope"]!.ToJsonString(), run.Envelope!.ToJsonString());
        });
    }

    /// <summary>
    /// The token, the whole URL and its query are found in no outcome, file, run or setting. The URL's path is not asserted
    /// absent: the plugin host's own diagnostic names the host and the path of each request, and a secret belongs in the
    /// query or the token. The row stands on the host's redaction of granted values as much as on the plugin.
    /// </summary>
    [Theory]
    [InlineData(200)]
    [InlineData(500)]
    [InlineData(401)]
    public async Task A_granted_token_travels_as_a_bearer_header_and_the_token_the_whole_url_and_the_query_are_found_nowhere_else(int status)
    {
        using var receiver = new LoopbackNtfy { Status = status, Body = "{}" };
        await using var api = await PluginInvokerTests.StartAsync(paths => Install(paths, receiver.HostKey));

        var url = receiver.Server + "/hook/ab12?key=SECRETQUERY9876";
        await WithEnvironment(url, Token, async () =>
        {
            var request = new PluginInvocationRequest(
                Id,
                Kind,
                new JsonObject { ["line"] = new JsonObject { ["id"] = "jrn_1", ["kind"] = Kind }, ["envelope"] = new JsonObject(), ["attempt"] = 1 },
                null,
                "jrn_1",
                Purpose: InvocationPurpose.Subscription);

            var result = await PluginInvokerTests.InvokeAsync(api, request, Ct);

            Assert.Equal($"Bearer {Token}", Assert.Single(receiver.Requests).Headers["Authorization"]);
            var everywhere = InvocationFiles(api) + result.Outcome + result.Provenance;
            Assert.DoesNotContain(Token, everywhere, StringComparison.Ordinal);
            Assert.DoesNotContain(url, everywhere, StringComparison.Ordinal);
            Assert.DoesNotContain("SECRETQUERY9876", everywhere, StringComparison.Ordinal);
            Assert.DoesNotContain(Token, File.ReadAllText(api.Paths.UserSettingsFile), StringComparison.Ordinal);
        });
    }

    [Theory]
    [InlineData(200, "succeeded", "")]
    [InlineData(204, "succeeded", "")]
    [InlineData(408, "transient", "forwarder_unreachable")]
    [InlineData(429, "transient", "forwarder_unreachable")]
    [InlineData(500, "transient", "forwarder_unreachable")]
    [InlineData(503, "transient", "forwarder_unreachable")]
    [InlineData(401, "permanent", "forwarder_unauthorized")]
    [InlineData(403, "permanent", "forwarder_unauthorized")]
    [InlineData(400, "permanent", "forwarder_refused")]
    [InlineData(404, "permanent", "forwarder_refused")]
    [InlineData(300, "permanent", "forwarder_refused")]
    [InlineData(302, "permanent", "forwarder_refused")]
    public async Task Each_answer_of_the_receiver_ends_in_its_class_and_code(int status, string outcome, string code)
    {
        const string Words = "receiver words that stay with the receiver";
        using var receiver = new LoopbackNtfy { Status = status, Body = Words };
        await using var api = await PluginInvokerTests.StartAsync(paths => Install(paths, receiver.HostKey));

        await WithEnvironment(receiver.Server + "/", null, async () =>
        {
            Line(api.Paths);

            var run = await TakeAsync(api);

            Assert.Equal((outcome, code), (Outcome(run), Code(run)));
            Assert.Single(receiver.Requests);
            Assert.DoesNotContain(Words, InvocationFiles(api) + run.Handlers.ToJsonString() + run.Envelope?.ToJsonString() + run.FailureCode, StringComparison.Ordinal);
            if (outcome == "permanent")
            {
                Assert.Equal((SubscriptionRunStatus.Failed, "permanent", code), (run.Status, run.FailureClass, run.FailureCode));
            }
        });
    }

    /// <summary>
    /// A refused connection is transient: the receiver may be back by the retry. The host classes a timeout the same way, and
    /// this row drives only the refused connection.
    /// </summary>
    [Fact]
    public async Task A_receiver_that_does_not_answer_is_transient()
    {
        var host = $"127.0.0.1:{LoopbackNtfy.FreePort()}";
        await using var api = await PluginInvokerTests.StartAsync(paths => Install(paths, host));

        await WithEnvironment("http://" + host + "/", null, async () =>
        {
            Line(api.Paths);

            var run = await TakeAsync(api);

            Assert.Equal(("transient", "forwarder_unreachable"), (Outcome(run), Code(run)));
            Assert.NotNull(run.RetryAt);
        });
    }

    [Theory]
    [InlineData(null, "forwarder_url_missing")]
    [InlineData("", "forwarder_url_missing")]
    [InlineData("not a url", "forwarder_url_invalid")]
    [InlineData("ftp://localhost:8787/hook", "forwarder_url_invalid")]
    [InlineData("localhost:8787/hook", "forwarder_url_invalid")]
    public async Task A_missing_or_malformed_url_is_permanent_and_nothing_is_sent(string? url, string code)
    {
        using var receiver = new LoopbackNtfy();
        await using var api = await PluginInvokerTests.StartAsync(paths => Install(paths, receiver.HostKey));

        await WithEnvironment(url, null, async () =>
        {
            Line(api.Paths);

            var run = await TakeAsync(api);

            Assert.Equal((SubscriptionRunStatus.Failed, "permanent", code), (run.Status, run.FailureClass, run.FailureCode));
            Assert.Empty(receiver.Requests);
        });
    }

    /// <summary>A URL whose host the grant does not name is the host's refusal, recorded by its own code, never a send.</summary>
    [Fact]
    public async Task A_url_whose_host_the_grant_does_not_name_is_the_hosts_refusal_and_nothing_is_sent()
    {
        using var receiver = new LoopbackNtfy();
        using var elsewhere = new LoopbackNtfy();
        await using var api = await PluginInvokerTests.StartAsync(paths => Install(paths, receiver.HostKey));

        await WithEnvironment(elsewhere.Server + "/", null, async () =>
        {
            Line(api.Paths);

            var run = await TakeAsync(api);

            Assert.Equal((SubscriptionRunStatus.Failed, "permanent", "http_host_not_allowed"), (run.Status, run.FailureClass, run.FailureCode));
            Assert.Empty(receiver.Requests);
            Assert.Empty(elsewhere.Requests);
        });
    }

    /// <summary>
    /// The runtime invokes a subscriber under a kind it was granted, which is the line's own; a line of another kind under that
    /// word is the handler's validation failure. (A word the plugin was not granted never reaches the child: the runtime refuses it.)
    /// </summary>
    [Fact]
    public async Task An_operation_that_is_not_the_lines_kind_is_a_validation_failure()
    {
        using var receiver = new LoopbackNtfy();
        await using var api = await PluginInvokerTests.StartAsync(paths => Install(paths, receiver.HostKey));

        await WithEnvironment(receiver.Server + "/", null, async () =>
        {
            var line = new JsonObject { ["id"] = "jrn_1", ["kind"] = "campaign_created" };
            var request = new PluginInvocationRequest(
                Id,
                Kind,
                new JsonObject { ["line"] = line, ["envelope"] = new JsonObject(), ["attempt"] = 1 },
                null,
                "jrn_1",
                Purpose: InvocationPurpose.Subscription);

            var result = await PluginInvokerTests.InvokeAsync(api, request, Ct);

            var failed = Assert.IsType<InvocationOutcome.Failed>(result.Outcome);
            Assert.Equal((FailureClass.Validation, "unknown_operation"), (failed.Error.Class, failed.Error.Code));
            Assert.Empty(receiver.Requests);
        });
    }

    [Fact]
    public async Task The_shipped_package_loads_as_a_subscriber_that_asks_for_http_and_two_variables_and_no_api()
    {
        await using var api = await PluginInvokerTests.StartAsync(paths => Install(paths, host: null));

        var plugin = api.Resolve<PluginRegistry>().Snapshot.Find(Id);

        Assert.NotNull(plugin);
        Assert.Equal(PluginStatus.Valid, plugin.Status);
        Assert.Equal(PluginKind.Subscriber, plugin.Manifest.Kind);
        Assert.Equal("1.0.0", plugin.Manifest.Version);
        Assert.Equal(["localhost:8787"], plugin.Manifest.Capabilities.Http!.Hosts);
        Assert.Equal([UrlVariable, TokenVariable], plugin.Manifest.Capabilities.Env!.Variables);
        Assert.Null(plugin.Manifest.Capabilities.Api);
        Assert.Null(plugin.Manifest.Capabilities.Exec);
    }

    /// <summary>A scheme in capitals is the URL the host accepts, and the forwarder takes it.</summary>
    [Fact]
    public async Task A_url_with_its_scheme_in_capitals_is_posted_to()
    {
        using var receiver = new LoopbackNtfy();
        await using var api = await PluginInvokerTests.StartAsync(paths => Install(paths, receiver.HostKey));

        await WithEnvironment("HTTP://" + receiver.HostKey + "/hook", null, async () =>
        {
            Line(api.Paths);

            var run = await TakeAsync(api);

            Assert.Equal(SubscriptionRunStatus.Succeeded, run.Status);
            Assert.Single(receiver.Requests);
        });
    }

    /// <summary>A line handed out again carries its attempt, and the receiver is told.</summary>
    [Fact]
    public async Task A_line_handed_out_again_is_posted_with_its_attempt()
    {
        using var receiver = new LoopbackNtfy();
        await using var api = await PluginInvokerTests.StartAsync(paths => Install(paths, receiver.HostKey));

        await WithEnvironment(receiver.Server + "/", null, async () =>
        {
            var request = new PluginInvocationRequest(
                Id,
                Kind,
                new JsonObject { ["line"] = new JsonObject { ["id"] = "jrn_1", ["kind"] = Kind }, ["envelope"] = new JsonObject(), ["attempt"] = 2 },
                null,
                "jrn_1",
                Purpose: InvocationPurpose.Subscription);

            var result = await PluginInvokerTests.InvokeAsync(api, request, Ct);

            Assert.IsType<InvocationOutcome.Succeeded>(result.Outcome);
            var request1 = Assert.Single(receiver.Requests);
            Assert.Equal(2, JsonNode.Parse(request1.Body)!["attempt"]!.GetValue<int>());
            Assert.Equal("jrn_1", request1.Headers["Jason-Line-Id"]);
        });
    }

    /// <summary>
    /// The manifest names every kind a plugin may subscribe to, because a declaration has no wildcard and the person narrows
    /// by the grant. A kind added to the catalog turns this red: add it to the package's <c>subscriptions</c> and move the
    /// package's version and its changelog.
    /// </summary>
    [Fact]
    public async Task The_manifest_declares_every_subscribable_kind_of_the_catalog_and_nothing_else()
    {
        await using var api = await PluginInvokerTests.StartAsync(paths => Install(paths, host: null));

        var declared = api.Resolve<PluginRegistry>().Snapshot.Find(Id)!.Manifest.Subscriptions;

        var subscribable = JournalKindCatalog.All.Where(contract => contract.Subscribable).Select(contract => contract.Kind).Order(StringComparer.Ordinal);
        Assert.Equal(subscribable, declared.Order(StringComparer.Ordinal));
        Assert.Equal(declared.Count, declared.Distinct().Count());
    }
}
