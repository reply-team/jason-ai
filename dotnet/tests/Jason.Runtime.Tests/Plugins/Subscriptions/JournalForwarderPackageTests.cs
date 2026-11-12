using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Jason.Contracts.Api;
using Jason.Contracts.Discovery;
using Jason.Contracts.Ids;
using Jason.Contracts.Journal;
using Jason.Contracts.Json;
using Jason.Contracts.Plugins;
using Jason.Contracts.Update;
using Jason.Runtime.Configuration;
using Jason.Runtime.Persistence;
using Jason.Runtime.Plugins.Invocation;
using Jason.Runtime.Plugins.Manifest;
using Jason.Runtime.Plugins.Registry;
using Jason.Runtime.Plugins.Subscriptions;
using Jason.Runtime.Tests.Dispatch;
using Jason.Runtime.Tests.Documentation;
using Jason.Runtime.Tests.Integration;
using Jason.Runtime.Tests.Notifications;
using Jason.Runtime.Tests.Plugins.Invocation;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using System.Globalization;

namespace Jason.Runtime.Tests.Plugins.Subscriptions;

/// <summary>
/// The journal-forwarder reference subscriber, through the real plugin host and the real subscription runner, against a
/// server on the loopback interface. The package is copied as it stands and granted the loopback server's host, which its
/// manifest never names: that is what <c>any_granted_host</c> lets a person do, so no row edits the package.
/// </summary>
[Collection(ProcessEnvironmentCollection.Name)]
public sealed class JournalForwarderPackageTests
{
    private const string Id = "journal-forwarder";
    private const string UrlVariable = "JOURNAL_FORWARDER_URL";
    private const string TokenVariable = "JOURNAL_FORWARDER_TOKEN";
    private const string FileVariable = "JOURNAL_FORWARDER_FILE";
    private const string Kind = "workitem_failed";
    private const string Token = "tk_jason_forwarder_test_0123456789";
    private const string ConfiguredToken = "tk_jason_forwarder_configured_9876543210";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static string Shipped => Path.Combine(AppContext.BaseDirectory, "Fixtures", "plugins", Id);

    /// <summary>The package copied into a data directory as it stands, and granted the host given and what a person grants it.</summary>
    private static void Install(JasonPaths paths, string? host, params string[] granted)
    {
        var root = paths.PluginPackageDirectory(Id);
        Directory.CreateDirectory(root);
        foreach (var file in Directory.EnumerateFiles(Shipped))
        {
            File.Copy(file, Path.Combine(root, Path.GetFileName(file)));
        }

        TestPlugins.Grant(paths, Id, http: [host ?? "localhost:8787"], env: [UrlVariable, TokenVariable, FileVariable]);
        Subscribe(paths, Id, granted.Length == 0 ? ["*"] : granted);
    }

    private static void Subscribe(JasonPaths paths, string id, params string[] kinds)
    {
        var settings = JsonNode.Parse(File.ReadAllText(paths.UserSettingsFile))!.AsObject();
        settings["Plugins"]!["Grants"]![id]!["Subscriptions"] = new JsonArray([.. kinds.Select(kind => (JsonNode)JsonValue.Create(kind)!)]);
        File.WriteAllText(paths.UserSettingsFile, settings.ToJsonString(JasonJson.Options), new UTF8Encoding(false));
    }

    /// <summary>What a person writes under <c>Plugins:Config:journal-forwarder</c>: the two keys the package reads.</summary>
    private static void Configure(JasonPaths paths, string? url, string? token = null)
    {
        var configuration = new JsonObject();
        if (url is not null)
        {
            configuration["url"] = url;
        }

        if (token is not null)
        {
            configuration["token"] = token;
        }

        var settings = JsonNode.Parse(File.ReadAllText(paths.UserSettingsFile))!.AsObject();
        settings["Plugins"]!["Config"] = new JsonObject { [Id] = configuration };
        File.WriteAllText(paths.UserSettingsFile, settings.ToJsonString(JasonJson.Options), new UTF8Encoding(false));
    }

    /// <summary>The grant moved to one host, the runtime having read it, and the reload that freezes it.</summary>
    private static async Task GrantHostAndReloadAsync(RuntimeApiFixture api, string host)
    {
        var settings = JsonNode.Parse(await File.ReadAllTextAsync(api.Paths.UserSettingsFile, Ct))!.AsObject();
        settings["Plugins"]!["Grants"]![Id]!["Http"] = new JsonArray(host);
        await File.WriteAllTextAsync(api.Paths.UserSettingsFile, settings.ToJsonString(JasonJson.Options), Ct);

        var monitor = api.Resolve<IOptionsMonitor<PluginsOptions>>();
        Assert.True(await DispatchHarness.EventuallyAsync(() => monitor.CurrentValue.Grants[Id].Http.Contains(host), Ct));
        await api.PostOkAsync<PluginRegistryDto>(Operations.PluginReload, null, Ct);
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

    /// <summary>The settings come before the environment, the URL and the token each on its own.</summary>
    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task What_the_settings_configure_comes_before_the_environment(bool url, bool token)
    {
        using var receiver = new LoopbackNtfy();
        await using var api = await PluginInvokerTests.StartAsync(paths =>
        {
            Install(paths, receiver.HostKey);
            Configure(paths, url ? receiver.Server + "/configured" : null, token ? ConfiguredToken : null);
        });

        await WithEnvironment(receiver.Server + "/environment", Token, async () =>
        {
            Line(api.Paths);

            var run = await TakeAsync(api);

            Assert.Equal(SubscriptionRunStatus.Succeeded, run.Status);
            var request = Assert.Single(receiver.Requests);
            Assert.Equal(url ? "/configured" : "/environment", request.Path);
            Assert.Equal($"Bearer {(token ? ConfiguredToken : Token)}", request.Headers["Authorization"]);
        });
    }

    /// <summary>A URL left empty in the settings says nothing, so the environment still names the receiver.</summary>
    [Fact]
    public async Task A_configured_url_left_empty_leaves_the_environment_to_name_the_receiver()
    {
        using var receiver = new LoopbackNtfy();
        await using var api = await PluginInvokerTests.StartAsync(paths =>
        {
            Install(paths, receiver.HostKey);
            Configure(paths, string.Empty);
        });

        await WithEnvironment(receiver.Server + "/environment", null, async () =>
        {
            Line(api.Paths);

            await TakeAsync(api);

            Assert.Equal("/environment", Assert.Single(receiver.Requests).Path);
        });
    }

    /// <summary>The codes of a URL the plugin cannot use are the codes the environment's URL has, and nothing is sent.</summary>
    [Theory]
    [InlineData("", "forwarder_receiver_missing")]
    [InlineData("not a url", "forwarder_url_invalid")]
    [InlineData("ftp://localhost:8787/hook", "forwarder_url_invalid")]
    public async Task A_configured_url_the_plugin_cannot_use_ends_under_the_same_codes(string url, string code)
    {
        using var receiver = new LoopbackNtfy();
        await using var api = await PluginInvokerTests.StartAsync(paths =>
        {
            Install(paths, receiver.HostKey);
            Configure(paths, url);
        });

        await WithEnvironment(null, null, async () =>
        {
            Line(api.Paths);

            var run = await TakeAsync(api);

            Assert.Equal((SubscriptionRunStatus.Failed, "permanent", code), (run.Status, run.FailureClass, run.FailureCode));
            Assert.Empty(receiver.Requests);
        });
    }

    /// <summary>What the settings hold travels where it is meant to, and is found in no outcome, file or provenance, a failure's included.</summary>
    [Theory]
    [InlineData(200)]
    [InlineData(500)]
    [InlineData(401)]
    public async Task A_configured_token_and_url_reach_the_receiver_and_no_record(int status)
    {
        using var receiver = new LoopbackNtfy { Status = status, Body = "{}" };
        var url = receiver.Server + "/hook/ab12?key=CONFIGUREDQUERY1234";
        await using var api = await PluginInvokerTests.StartAsync(paths =>
        {
            Install(paths, receiver.HostKey);
            Configure(paths, url, ConfiguredToken);
        });
        var request = new PluginInvocationRequest(
            Id,
            Kind,
            new JsonObject { ["line"] = new JsonObject { ["id"] = "jrn_1", ["kind"] = Kind }, ["envelope"] = new JsonObject(), ["attempt"] = 1 },
            null,
            "jrn_1",
            Purpose: InvocationPurpose.Subscription);

        var result = await PluginInvokerTests.InvokeAsync(api, request, Ct);

        Assert.Equal($"Bearer {ConfiguredToken}", Assert.Single(receiver.Requests).Headers["Authorization"]);
        var everywhere = InvocationFiles(api) + result.Outcome + result.Provenance;
        Assert.DoesNotContain(ConfiguredToken, everywhere, StringComparison.Ordinal);
        Assert.DoesNotContain(url, everywhere, StringComparison.Ordinal);
        Assert.DoesNotContain("CONFIGUREDQUERY1234", everywhere, StringComparison.Ordinal);
    }

    /// <summary>
    /// A receiver on a port the manifest never declared, with no file of the package touched: the host refuses it until a
    /// person grants it, the same settings file then delivers to it after one reload, and the package is still the one the
    /// release published, which is what <c>jason update</c> reads by the digest on the list.
    /// </summary>
    [Fact]
    public async Task A_receiver_the_manifest_never_named_is_refused_then_reached_by_a_grant_and_a_configuration_with_the_package_unedited()
    {
        using var receiver = new LoopbackNtfy();
        await using var api = await PluginInvokerTests.StartAsync(paths =>
        {
            Install(paths, host: null);
            Configure(paths, receiver.Server + "/hook", ConfiguredToken);
        });
        var declared = api.Resolve<PluginRegistry>().Snapshot.Find(Id)!.Manifest.Capabilities.Http!.Hosts;
        Assert.DoesNotContain(receiver.HostKey, declared);

        await WithEnvironment(null, null, async () =>
        {
            Line(api.Paths);
            var refused = await TakeAsync(api);
            Assert.Equal((SubscriptionRunStatus.Failed, "permanent", "http_host_not_allowed"), (refused.Status, refused.FailureClass, refused.FailureCode));
            Assert.Empty(receiver.Requests);

            await GrantHostAndReloadAsync(api, receiver.HostKey);

            var line = Line(api.Paths);
            var delivered = await TakeAsync(api);
            Assert.Equal(SubscriptionRunStatus.Succeeded, delivered.Status);
            var request = Assert.Single(receiver.Requests);
            Assert.Equal(("/hook", line, $"Bearer {ConfiguredToken}"), (request.Path, request.Headers["Jason-Line-Id"], request.Headers["Authorization"]));
        });

        var installed = api.Resolve<PluginRegistry>().Snapshot;
        var digest = PackageDigest.Compute(api.Paths.PluginPackageDirectory(Id)).Digest;
        var plugin = installed.Find(Id)!;
        Assert.Equal(digest, plugin.Digest);
        Assert.Equal(File.ReadLines(Path.Combine(RepositoryTree.Root(), DigestList)).Last(), $"{plugin.Manifest.Version} {digest}");
        Assert.Equal(ReleasedPluginState.Current, ReleasedPlugins.Embedded().Compare(installed).Single(entry => entry.Id == Id).State);
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
    [InlineData(null, "forwarder_receiver_missing")]
    [InlineData("", "forwarder_receiver_missing")]
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
    public async Task The_shipped_package_loads_as_a_subscriber_that_asks_for_http_files_and_three_variables_and_no_api()
    {
        await using var api = await PluginInvokerTests.StartAsync(paths => Install(paths, host: null));

        var plugin = api.Resolve<PluginRegistry>().Snapshot.Find(Id);

        Assert.NotNull(plugin);
        Assert.Equal(PluginStatus.Valid, plugin.Status);
        Assert.Equal(PluginKind.Subscriber, plugin.Manifest.Kind);
        Assert.Equal("1.3.0", plugin.Manifest.Version);
        Assert.Equal(["localhost:8787"], plugin.Manifest.Capabilities.Http!.Hosts);
        Assert.True(plugin.Manifest.Capabilities.Http.AnyGrantedHost);
        Assert.Equal([UrlVariable, TokenVariable, FileVariable], plugin.Manifest.Capabilities.Env!.Variables);
        Assert.True(plugin.Manifest.Capabilities.Files!.Append);
        Assert.Null(plugin.Manifest.Capabilities.Api);
        Assert.Null(plugin.Manifest.Capabilities.Exec);
    }

    /// <summary>A directory outside everything a test owns: a grant may not stand in a runtime's data directory or be its home.</summary>
    private sealed class Folder : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "jason-forwarder-file-" + Guid.NewGuid().ToString("N"));

        public string Directory => Path.Combine(_root, "granted-directory");

        public string File => Path.Combine(Directory, "lines.jsonl");

        public string[] Lines() => System.IO.File.ReadAllLines(File, new UTF8Encoding(false));

        /// <summary>A file the disk will not add to, whoever asks: a failure of the disk, on every platform, without a full one.</summary>
        public void RefuseWrites()
        {
            Assert.SkipWhen(Environment.UserName == "root", "root writes a read-only file all the same.");
            System.IO.Directory.CreateDirectory(Directory);
            System.IO.File.WriteAllText(File, string.Empty);
            System.IO.File.SetAttributes(File, FileAttributes.ReadOnly);
        }

        public void Dispose()
        {
            if (System.IO.Directory.Exists(_root))
            {
                if (System.IO.File.Exists(File))
                {
                    System.IO.File.SetAttributes(File, FileAttributes.Normal);
                }

                System.IO.Directory.Delete(_root, recursive: true);
            }
        }
    }

    /// <summary>The directory granted, and, with <paramref name="file"/>, the key that names the file; the host's grant goes when no receiver is wanted.</summary>
    private static void GrantFile(JasonPaths paths, string directory, string? file = null, string? url = null, bool noHost = false)
    {
        var settings = JsonNode.Parse(File.ReadAllText(paths.UserSettingsFile))!.AsObject();
        var grant = settings["Plugins"]!["Grants"]![Id]!.AsObject();
        grant["Files"] = new JsonArray(directory);
        if (noHost)
        {
            grant.Remove("Http");
        }

        var configuration = new JsonObject();
        if (file is not null)
        {
            configuration["file"] = file;
        }

        if (url is not null)
        {
            configuration["url"] = url;
        }

        settings["Plugins"]!["Config"] = new JsonObject { [Id] = configuration };
        File.WriteAllText(paths.UserSettingsFile, settings.ToJsonString(JasonJson.Options), new UTF8Encoding(false));
    }

    /// <summary>The line handed to the plugin directly, as a chain would hand it, so a row can hand it again.</summary>
    private static async Task<InvocationOutcome> HandAsync(RuntimeApiFixture api, int attempt)
    {
        var request = new PluginInvocationRequest(
            Id,
            Kind,
            new JsonObject { ["line"] = new JsonObject { ["id"] = "jrn_1", ["kind"] = Kind }, ["envelope"] = new JsonObject(), ["attempt"] = attempt },
            null,
            "jrn_1",
            Purpose: InvocationPurpose.Subscription);
        return (await PluginInvokerTests.InvokeAsync(api, request, Ct)).Outcome;
    }

    /// <summary>A file and no receiver at all: the line is the body a POST would carry, one JSON object on one line, and no host is granted.</summary>
    [Fact]
    public async Task A_file_alone_takes_each_line_as_the_body_a_post_would_carry()
    {
        using var folder = new Folder();
        await using var api = await PluginInvokerTests.StartAsync(paths =>
        {
            Install(paths, host: null);
            GrantFile(paths, folder.Directory, file: folder.File, noHost: true);
        });

        await WithEnvironment(null, null, async () =>
        {
            var line = Line(api.Paths);

            var run = await TakeAsync(api);

            Assert.Equal(SubscriptionRunStatus.Succeeded, run.Status);
            var body = JsonNode.Parse(Assert.Single(folder.Lines()))!.AsObject();
            Assert.Equal(["line", "envelope", "attempt"], body.Select(pair => pair.Key));
            Assert.Equal((line, Kind), (body["line"]!["id"]!.GetValue<string>(), body["line"]!["kind"]!.GetValue<string>()));
            Assert.Equal(1, body["attempt"]!.GetValue<int>());
        });
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task The_settings_name_the_file_first_and_the_environment_after_them(bool configured)
    {
        using var folder = new Folder();
        var environment = Path.Combine(folder.Directory, "environment.jsonl");
        await using var api = await PluginInvokerTests.StartAsync(paths =>
        {
            Install(paths, host: null);
            GrantFile(paths, folder.Directory, file: configured ? folder.File : null, noHost: true);
        });
        Environment.SetEnvironmentVariable(FileVariable, environment);
        try
        {
            Line(api.Paths);

            var run = await TakeAsync(api);

            Assert.Equal(SubscriptionRunStatus.Succeeded, run.Status);
            Assert.Equal(configured, File.Exists(folder.File));
            Assert.Equal(!configured, File.Exists(environment));
        }
        finally
        {
            Environment.SetEnvironmentVariable(FileVariable, null);
        }
    }

    [Fact]
    public async Task Both_receivers_take_the_same_line_byte_for_byte()
    {
        using var folder = new Folder();
        using var receiver = new LoopbackNtfy();
        await using var api = await PluginInvokerTests.StartAsync(paths =>
        {
            Install(paths, receiver.HostKey);
            GrantFile(paths, folder.Directory, file: folder.File, url: receiver.Server + "/hook");
        });

        await WithEnvironment(null, null, async () =>
        {
            Line(api.Paths);

            var run = await TakeAsync(api);

            Assert.Equal(SubscriptionRunStatus.Succeeded, run.Status);
            Assert.Equal(Assert.Single(receiver.Requests).Body, Assert.Single(folder.Lines()));
        });
    }

    /// <summary>The disk's refusal does not cost the receiver the line, and the line still fails, with the first failure.</summary>
    [Theory]
    [InlineData(200)]
    [InlineData(500)]
    public async Task A_file_the_disk_refuses_still_leaves_the_post_and_the_first_failure_is_what_ends_the_handler(int status)
    {
        using var folder = new Folder();
        using var receiver = new LoopbackNtfy { Status = status };
        folder.RefuseWrites();
        await using var api = await PluginInvokerTests.StartAsync(paths =>
        {
            Install(paths, receiver.HostKey);
            GrantFile(paths, folder.Directory, file: folder.File, url: receiver.Server + "/hook");
        });

        await WithEnvironment(null, null, async () =>
        {
            Line(api.Paths);

            var run = await TakeAsync(api);

            Assert.Equal(("transient", "file_write_failed"), (Outcome(run), Code(run)));
            Assert.Single(receiver.Requests);
        });
    }

    [Fact]
    public async Task A_file_the_disk_refuses_with_no_receiver_is_the_same_failure()
    {
        using var folder = new Folder();
        folder.RefuseWrites();
        await using var api = await PluginInvokerTests.StartAsync(paths =>
        {
            Install(paths, host: null);
            GrantFile(paths, folder.Directory, file: folder.File, noHost: true);
        });

        await WithEnvironment(null, null, async () =>
        {
            Line(api.Paths);

            var run = await TakeAsync(api);

            Assert.Equal(("transient", "file_write_failed"), (Outcome(run), Code(run)));
        });
    }

    /// <summary>One invocation has one outcome, so a repeat for the receiver adds the line again, and the line's id tells a reader so.</summary>
    [Fact]
    public async Task A_failed_post_leaves_the_line_in_the_file_and_the_repeat_adds_it_again()
    {
        using var folder = new Folder();
        using var receiver = new LoopbackNtfy { Status = 503 };
        await using var api = await PluginInvokerTests.StartAsync(paths =>
        {
            Install(paths, receiver.HostKey);
            GrantFile(paths, folder.Directory, file: folder.File, url: receiver.Server + "/hook");
        });

        await WithEnvironment(null, null, async () =>
        {
            var first = Assert.IsType<InvocationOutcome.Failed>(await HandAsync(api, attempt: 1));
            Assert.Equal("forwarder_unreachable", first.Error.Code);
            Assert.Single(folder.Lines());

            receiver.Status = 200;
            Assert.IsType<InvocationOutcome.Succeeded>(await HandAsync(api, attempt: 2));

            var ids = folder.Lines().Select(line => JsonNode.Parse(line)!["line"]!["id"]!.GetValue<string>()).ToList();
            Assert.Equal(["jrn_1", "jrn_1"], ids);
        });
    }

    /// <summary>A rule refused is a misconfiguration: nothing a plugin may catch, so it ends where it stands and nothing is sent.</summary>
    [Theory]
    [InlineData("elsewhere", "file_path_not_allowed")]
    [InlineData("bad name", "file_path_not_allowed")]
    [InlineData("ungranted", "capability_not_granted")]
    public async Task A_file_the_rules_refuse_ends_the_line_where_it_stands_and_posts_nothing(string shape, string code)
    {
        using var folder = new Folder();
        using var other = new Folder();
        using var receiver = new LoopbackNtfy();
        await using var api = await PluginInvokerTests.StartAsync(paths =>
        {
            Install(paths, receiver.HostKey);
            GrantFile(
                paths,
                folder.Directory,
                file: shape switch
                {
                    "elsewhere" => other.File,
                    "bad name" => Path.Combine(folder.Directory, "a line.jsonl"),
                    _ => folder.File,
                },
                url: receiver.Server + "/hook");
            if (shape == "ungranted")
            {
                var settings = JsonNode.Parse(File.ReadAllText(paths.UserSettingsFile))!.AsObject();
                settings["Plugins"]!["Grants"]![Id]!.AsObject().Remove("Files");
                File.WriteAllText(paths.UserSettingsFile, settings.ToJsonString(JasonJson.Options), new UTF8Encoding(false));
            }
        });

        await WithEnvironment(null, null, async () =>
        {
            Line(api.Paths);

            var run = await TakeAsync(api);

            Assert.Equal((SubscriptionRunStatus.Failed, "permanent", code), (run.Status, run.FailureClass, run.FailureCode));
            Assert.Empty(receiver.Requests);
            Assert.False(File.Exists(other.File));
        });
    }

    /// <summary>A URL the plugin cannot use says so before anything is written, so a repair is not a repeat of the line.</summary>
    [Fact]
    public async Task A_url_the_plugin_cannot_use_ends_the_line_before_the_file_has_it()
    {
        using var folder = new Folder();
        await using var api = await PluginInvokerTests.StartAsync(paths =>
        {
            Install(paths, host: null);
            GrantFile(paths, folder.Directory, file: folder.File, url: "not a url");
        });

        await WithEnvironment(null, null, async () =>
        {
            Line(api.Paths);

            var run = await TakeAsync(api);

            Assert.Equal((SubscriptionRunStatus.Failed, "permanent", "forwarder_url_invalid"), (run.Status, run.FailureClass, run.FailureCode));
            Assert.False(File.Exists(folder.File));
        });
    }

    /// <summary>The directory is a person's: what the handler's records keep of the file is its name.</summary>
    [Fact]
    public async Task The_directory_reaches_no_record_of_the_invocation()
    {
        using var folder = new Folder();
        await using var api = await PluginInvokerTests.StartAsync(paths =>
        {
            Install(paths, host: null);
            GrantFile(paths, folder.Directory, file: folder.File, noHost: true);
        });

        await WithEnvironment(null, null, async () =>
        {
            Line(api.Paths);

            var run = await TakeAsync(api);

            Assert.Equal(SubscriptionRunStatus.Succeeded, run.Status);
            var records = InvocationFiles(api);
            Assert.Contains("lines.jsonl", records, StringComparison.Ordinal);
            Assert.DoesNotContain(Path.GetFileName(folder.Directory), records + run.Handlers.ToJsonString(), StringComparison.Ordinal);
        });
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

    /// <summary>
    /// The package's README installs it from the release a person runs, by <c>jason plugin install journal-forwarder</c>,
    /// never by a copy out of a checkout, which a person who installed Jason does not have. A receiver on another host is a
    /// grant and a configuration in the settings, each with its JSON, and no edit of the package.
    /// </summary>
    [Fact]
    public void The_readme_installs_the_package_from_the_release_and_a_receiver_elsewhere_edits_no_file_of_it()
    {
        var readme = File.ReadAllText(Path.Combine(RepositoryTree.Root(), "plugins", Id, "README.md")).ReplaceLineEndings("\n");

        var installing = Section(readme, "Installing it");
        Assert.Contains($"Every release of Jason from 0.30.0 on publishes it as `{ReleaseAssets.JournalForwarderPlugin}`", installing, StringComparison.Ordinal);
        Assert.Contains("jason plugin install journal-forwarder", installing, StringComparison.Ordinal);
        Assert.DoesNotContain("cp -r plugins/journal-forwarder", readme, StringComparison.Ordinal);
        Assert.DoesNotContain("Copy the package", readme, StringComparison.Ordinal);

        var elsewhere = Section(readme, "A receiver elsewhere than the loopback");
        Assert.Contains("### The grant", elsewhere, StringComparison.Ordinal);
        Assert.Contains("### The configuration", elsewhere, StringComparison.Ordinal);
        Assert.Contains("\"Http\": [\"receiver.example.com\"]", elsewhere, StringComparison.Ordinal);
        Assert.Contains("\"url\": \"https://receiver.example.com/jason\", \"token\": \"a-token-of-yours\"", elsewhere, StringComparison.Ordinal);
        Assert.Contains("`jason plugin reload`", elsewhere, StringComparison.Ordinal);
        Assert.DoesNotContain("From then on the copy is yours.", elsewhere, StringComparison.Ordinal);
        Assert.Contains("**Jason 0.36.0 or newer**", Section(readme, "What you need"), StringComparison.Ordinal);
        Assert.DoesNotContain("no way to write a file", readme, StringComparison.Ordinal);
        Assert.Contains("`forwarder_receiver_missing`", readme, StringComparison.Ordinal);
        Assert.Contains("\"Files\": [\"", Section(readme, "A file instead of a receiver, or beside one"), StringComparison.Ordinal);
    }

    /// <summary>A section of a README, its heading to the next, with its whitespace made single spaces.</summary>
    private static string Section(string readme, string heading)
    {
        var start = readme.IndexOf($"\n## {heading}\n", StringComparison.Ordinal);
        Assert.True(start >= 0, $"plugins/{Id}/README.md has no section '{heading}'.");
        var end = readme.IndexOf("\n## ", start + 1, StringComparison.Ordinal);
        return Regex.Replace(readme[start..(end < 0 ? readme.Length : end)], "\\s+", " ");
    }

    /// <summary>The list beside the package that records the digest of each of its versions, as Reply's list does for Reply's.</summary>
    private const string DigestList = "plugins/journal-forwarder.digests";

    /// <summary>
    /// The digest of the package as v0.29.0 shipped it in its tree, version 1.0.0, computed from that tag's tree. A tag's
    /// tree never changes, so this line never does: it is what lets <c>jason update</c> read a copy a person made by hand
    /// from that release as version 1.0.0, a version the release has a later one of, and replace it.
    /// </summary>
    private const string FirstShipped = "1.0.0 sha256:9239df983b3709868f9c9dd0cce591c2fc15e3d702678fa4f24a7ed84f2f545b";

    /// <summary>
    /// A package's version is its content, as for Reply's package: the digest of the whole package has one line on the list
    /// beside it, <c>plugins/journal-forwarder.digests</c>, for the version its manifest names, and that line is the last. The
    /// release's manifest names the package from this list, and the runtime built from the same tree carries the package it
    /// names. The list's first line is the package 0.29.0 shipped in its tree. Each failure says what to do.
    /// </summary>
    /// <remarks>
    /// A line that changes or disappears in a diff is what this test cannot see, since the list it reads is the changed list:
    /// a line already on it is refused in review.
    /// </remarks>
    [Fact]
    [Critical(Family.R1, "a package's version is its content")]
    public void The_package_is_the_digest_its_version_has_on_the_list()
    {
        var root = RepositoryTree.Root();
        var package = Path.Combine(root, "plugins", Id);
        var version = YamlToJson.Convert(File.ReadAllText(Path.Combine(package, "plugin.yaml")))!["version"]!.GetValue<string>();
        var digest = PackageDigest.Compute(package).Digest;
        var line = $"{version} {digest}";

        var path = Path.Combine(root, DigestList);
        Assert.True(
            File.Exists(path),
            $"{DigestList} is missing. It records the digest of each version of the package, one line per version, beside the "
            + "package and outside it: create it with a header comment, the line of the package 0.29.0 shipped in its tree and this line:"
            + Environment.NewLine
            + FirstShipped
            + Environment.NewLine
            + line);

        var listed = new List<(string Version, string Digest)>();
        foreach (var entry in File.ReadAllLines(path))
        {
            if (entry.Length == 0 || entry.StartsWith('#'))
            {
                continue;
            }

            Assert.True(
                IsListLine(entry),
                $"Not a line of {DigestList}: '{entry}'. A line is a bare version, X.Y.Z with no pre-release or build metadata, a "
                + "space and the package's sha256:digest.");
            var parts = entry.Split(' ');
            listed.Add((parts[0], parts[1]));
        }

        var problems = new List<string>();
        if (listed.Count == 0 || $"{listed[0].Version} {listed[0].Digest}" != FirstShipped)
        {
            problems.Add($"The first line of {DigestList} is the package 0.29.0 shipped in its tree, and it is: {FirstShipped}");
        }

        foreach (var twice in listed.GroupBy(entry => entry.Version, StringComparer.Ordinal).Where(group => group.Count() > 1))
        {
            problems.Add(
                $"{DigestList} names version {twice.Key} {twice.Count()} times. A version has one line and a line is never changed: "
                + "delete the line that was added or edited, put the package's content under a new version in plugin.yaml, and append "
                + "the line for that version.");
        }

        var own = listed.Where(entry => entry.Version == version).ToList();
        if (!IsListLine(line))
        {
            problems.Add(
                $"plugin.yaml says version {version}, and a version on {DigestList} is a bare X.Y.Z, as the release's manifest writer "
                + "reads one: give the package a version with no pre-release or build metadata.");
        }
        else if (own.Count == 0)
        {
            problems.Add(
                $"plugin.yaml says version {version} and {DigestList} has no line for it. A version is its content, recorded once: "
                + $"append this line to the end of {DigestList}, after the edits to the package are done, since every edit changes it:"
                + Environment.NewLine
                + line);
        }
        else if (own.Count == 1)
        {
            if (own[0].Digest != digest)
            {
                problems.Add(
                    $"The package is not the content version {version} was recorded with: {DigestList} says {own[0].Digest}, the package "
                    + $"is {digest}. A version is its content, so the version changes whenever any file of plugins/{Id} does: "
                    + "bump the version in plugin.yaml and write the changelog entry, then run this test again, which prints the line "
                    + $"to append to the end of {DigestList}. The line already there is left as it is.");
            }

            if (listed[^1].Version != version)
            {
                problems.Add(
                    $"The last line of {DigestList} is for version {listed[^1].Version} and plugin.yaml says version {version}. A line is "
                    + "appended at the end of the list, after the lines already there: put the line for version "
                    + $"{version} at the end of {DigestList}.");
            }
        }

        Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
    }

    /// <summary>A line of the list by the rule <c>.github/scripts/Write-Manifest.ps1</c> reads it with, character for character.</summary>
    private static bool IsListLine(string entry) =>
        Regex.IsMatch(entry, "^((0|[1-9][0-9]*)\\.(0|[1-9][0-9]*)\\.(0|[1-9][0-9]*)) (sha256:[0-9a-f]{64})$", RegexOptions.CultureInvariant);
}
