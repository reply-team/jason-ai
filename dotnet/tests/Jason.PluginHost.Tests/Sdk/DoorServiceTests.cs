using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Jason.Contracts.Api;
using Jason.Contracts.Plugins;
using Jason.PluginHost.Sdk;
using Jason.PluginHost.Tests.Fixtures;
using Jason.Runtime.Tests;
using Jint;

namespace Jason.PluginHost.Tests.Sdk;

/// <summary>
/// <c>host.call</c>, the door a handler reaches the Runtime API through, against a real listener on loopback standing
/// in for the runtime: the host makes the call itself with the capability the envelope carried, speaks as the plugin
/// about the journal line, refuses everything outside the door before a byte leaves it, and hands the plugin either the
/// answer or a failure it can catch.
/// </summary>
public sealed class DoorServiceTests : IDisposable
{
    private const string Capability = "door-capability-0123456789abcdef";
    private const string LineId = "jrn_01JDOORLINE";

    private readonly TempPackage _package = new();
    private readonly ConcurrentQueue<Seen> _seen = new();
    private readonly List<LoopbackHttpServer> _servers = [];

    public void Dispose()
    {
        foreach (var server in _servers)
        {
            server.Dispose();
        }

        _package.Dispose();
    }

    private sealed record Seen(string Method, string Path, string? Authorization, string? ContentType, string Body);

    /// <summary>A runtime that answers every call with <paramref name="status"/> and <paramref name="body"/>.</summary>
    private LoopbackHttpServer Api(int status = 200, string body = """{"items":[],"next_cursor":null}""", int delayMs = 0)
    {
        var server = new LoopbackHttpServer(async context =>
        {
            using var reader = new StreamReader(context.Request.InputStream, Encoding.UTF8);
            var text = await reader.ReadToEndAsync().ConfigureAwait(false);
            _seen.Enqueue(new Seen(
                context.Request.HttpMethod,
                context.Request.Url!.AbsolutePath,
                context.Request.Headers["Authorization"],
                context.Request.ContentType,
                text));

            if (delayMs > 0)
            {
                await Task.Delay(delayMs).ConfigureAwait(false);
            }

            var bytes = Encoding.UTF8.GetBytes(body);
            context.Response.StatusCode = status;
            context.Response.ContentType = "application/json";
            context.Response.ContentLength64 = bytes.Length;
            await context.Response.OutputStream.WriteAsync(bytes).ConfigureAwait(false);
            context.Response.OutputStream.Close();
        });
        _servers.Add(server);
        return server;
    }

    private static InvocationDoor DoorTo(string baseUrl, DoorLimits? limits = null, params string[] operations) =>
        new(baseUrl, Capability, LineId, operations.Length == 0 ? [Operations.CampaignList, Operations.JournalAppend] : operations, limits ?? DoorLimits.Standard);

    private SdkHarness Harness(InvocationDoor door, TimeSpan? remaining = null) =>
        new(
            _package.Root,
            builder =>
            {
                builder.PluginId = "acme-forwarder";
                builder.Door = door;
                builder.Limits = InvocationFactory.DefaultLimits with { TimeoutMs = 60_000 };
            },
            remaining);

    private static string Base(LoopbackHttpServer server) => server.BaseUrl.TrimEnd('/');

    /// <summary>What the plugin caught, as the plain fields of the error it was handed.</summary>
    private static JsonObject Caught(SdkHarness harness, string call) =>
        (JsonObject)JsJson.ToJson(
            harness.Engine,
            harness.Evaluate(
                $$"""
                (() => {
                  try { {{call}}; return { caught: false }; }
                  catch (e) { return { caught: true, name: e.name, class: e.class, code: e.code, message: e.message, retryable: e.retryable, details: e.details }; }
                })()
                """))!;

    [Fact]
    public void A_call_posts_the_operation_with_the_capability_and_speaks_as_the_plugin_about_the_line()
    {
        var api = Api(body: """{"items":[{"id":"cmp_01J"}],"next_cursor":null}""");
        using var harness = Harness(DoorTo(Base(api)));

        var answer = harness.Evaluate("JSON.stringify(host.call(\"campaign.list\", { limit: 5 }))").AsString();

        Assert.Equal("""{"items":[{"id":"cmp_01J"}],"next_cursor":null}""", answer);
        var seen = Assert.Single(_seen);
        Assert.Equal("POST", seen.Method);
        Assert.Equal("/v1/campaign.list", seen.Path);
        Assert.Equal("Bearer " + Capability, seen.Authorization);
        Assert.StartsWith("application/json", seen.ContentType, StringComparison.Ordinal);
        var body = (JsonObject)JsonNode.Parse(seen.Body)!;
        Assert.Equal(5, body["limit"]!.GetValue<int>());
        Assert.Equal("""{"type":"plugin","id":"acme-forwarder"}""", body["actor"]!.ToJsonString());
        Assert.Equal("journal " + LineId, body["reason"]!.GetValue<string>());
    }

    [Fact]
    public void A_call_without_arguments_sends_only_what_the_host_sets()
    {
        var api = Api();
        using var harness = Harness(DoorTo(Base(api)));

        harness.Evaluate("host.call(\"campaign.list\")");

        var body = (JsonObject)JsonNode.Parse(Assert.Single(_seen).Body)!;
        Assert.Equal(["actor", "reason"], body.Select(member => member.Key).Order(StringComparer.Ordinal));
    }

    /// <summary>
    /// The door's first check: an operation the capability does not open, or one the Runtime API does not publish, is
    /// refused before any connection is made, and the plugin cannot catch the refusal and quietly try another.
    /// </summary>
    [Theory]
    [InlineData("contact.list")]
    [InlineData("approval.approve")]
    [InlineData("system.info")]
    [InlineData("no.such")]
    [Critical(Family.R5, "a call outside the door never leaves the host")]
    public void An_operation_outside_the_door_never_leaves_the_host(string operation)
    {
        var api = Api();
        using var harness = Harness(DoorTo(Base(api)));

        var refused = Assert.Throws<HostRuleException>(() => harness.Caught($"host.call(\"{operation}\", {{}})"));

        Assert.Equal(HostOutcomeCodes.CapabilityNotGranted, refused.Code);
        Assert.Equal("api", refused.Details!["capability"]!.GetValue<string>());
        Assert.Equal(operation, refused.Details!["operation"]!.GetValue<string>());
        Assert.Equal(0, api.Requests);
        Assert.Equal(0, harness.Budget.ApiCalls);
    }

    [Theory]
    [InlineData("host.call(\"campaign.list\", { actor: { type: \"human\", id: \"x\" } })")]
    [InlineData("host.call(\"campaign.list\", { reason: \"mine\" })")]
    [InlineData("host.call(\"campaign.list\", 5)")]
    [InlineData("host.call(\"campaign.list\", [1])")]
    [InlineData("host.call(5)")]
    [InlineData("host.call()")]
    public void What_the_host_sets_or_cannot_send_is_a_type_error(string call)
    {
        var api = Api();
        using var harness = Harness(DoorTo(Base(api)));

        Assert.Equal("TypeError", harness.Caught(call));
        Assert.Equal(0, api.Requests);
    }

    [Theory]
    [InlineData(true, "transient")]
    [InlineData(false, "permanent")]
    public void An_error_the_API_answers_is_a_failure_the_plugin_can_catch(bool retryable, string failureClass)
    {
        var api = Api(409, """{"error":{"code":"conflict","message":"The campaign moved.","retryable":RETRYABLE}}""".Replace("RETRYABLE", retryable ? "true" : "false", StringComparison.Ordinal));
        using var harness = Harness(DoorTo(Base(api)));

        var caught = Caught(harness, "host.call(\"campaign.list\")");

        Assert.True(caught["caught"]!.GetValue<bool>());
        Assert.Equal(HostFailure.Name, caught["name"]!.GetValue<string>());
        Assert.Equal(failureClass, caught["class"]!.GetValue<string>());
        Assert.Equal("conflict", caught["code"]!.GetValue<string>());
        Assert.Equal("The campaign moved.", caught["message"]!.GetValue<string>());
        Assert.Equal(retryable, caught["retryable"]!.GetValue<bool>());
        Assert.Equal(409, caught["details"]!["status"]!.GetValue<int>());
        Assert.Equal("campaign.list", caught["details"]!["operation"]!.GetValue<string>());
    }

    /// <summary>Uncaught, an API error is a declared failure the host reads back as it reads one the plugin built.</summary>
    [Fact]
    public void An_API_error_reads_back_as_a_declared_failure()
    {
        var api = Api(400, """{"error":{"code":"validation_failed","message":"limit is too large","retryable":false}}""");
        using var harness = Harness(DoorTo(Base(api)));

        var thrown = harness.Evaluate("(() => { try { host.call(\"campaign.list\"); } catch (e) { return e; } })()");

        Assert.True(HostFailure.TryRead(harness.Engine, thrown, out var error));
        Assert.Equal(FailureClass.Permanent, error.Class);
        Assert.Equal("validation_failed", error.Code);
        Assert.Equal(400, error.Details!["status"]!.GetValue<int>());
    }

    [Fact]
    public void A_runtime_that_is_not_listening_is_transient_and_unreachable()
    {
        using var harness = Harness(DoorTo(string.Create(CultureInfo.InvariantCulture, $"http://127.0.0.1:{LoopbackHttpServer.FreePort()}")));

        var caught = Caught(harness, "host.call(\"campaign.list\")");

        Assert.Equal("transient", caught["class"]!.GetValue<string>());
        Assert.Equal(DoorService.RuntimeUnreachable, caught["code"]!.GetValue<string>());
        Assert.True(caught["retryable"]!.GetValue<bool>());
    }

    [Fact]
    public void An_answer_larger_than_the_door_allows_is_too_large()
    {
        var api = Api(body: "{\"text\":\"" + new string('x', 200) + "\"}");
        using var harness = Harness(DoorTo(Base(api), DoorLimits.Standard with { ResponseBytes = 64 }));

        var caught = Caught(harness, "host.call(\"campaign.list\")");

        Assert.Equal("permanent", caught["class"]!.GetValue<string>());
        Assert.Equal(DoorService.ResponseTooLarge, caught["code"]!.GetValue<string>());
        Assert.Equal(1, api.Requests);
    }

    [Fact]
    public void An_answer_that_is_not_the_API_s_is_said_as_such()
    {
        var api = Api(502, "<html>bad gateway</html>");
        using var harness = Harness(DoorTo(Base(api)));

        var caught = Caught(harness, "host.call(\"campaign.list\")");

        Assert.Equal("permanent", caught["class"]!.GetValue<string>());
        Assert.Equal(DoorService.RuntimeAnswerInvalid, caught["code"]!.GetValue<string>());
        Assert.Equal(502, caught["details"]!["status"]!.GetValue<int>());
    }

    /// <summary>A call that outlasts the door's own bound may have acted, so it is ambiguous, and it ends at the bound.</summary>
    [Fact]
    public void A_call_ends_at_the_door_s_own_bound()
    {
        var api = Api(delayMs: 4_000);
        using var harness = Harness(DoorTo(Base(api), DoorLimits.Standard with { TimeoutMs = 300 }));

        var watch = Stopwatch.StartNew();
        var caught = Caught(harness, "host.call(\"campaign.list\")");

        Assert.Equal("ambiguous", caught["class"]!.GetValue<string>());
        Assert.Equal(DoorService.RuntimeTimeout, caught["code"]!.GetValue<string>());
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(3), $"the call took {watch.Elapsed}");
    }

    /// <summary>Where the invocation has less time left than the door's bound, the call ends when the invocation's time does.</summary>
    [Fact]
    public void A_call_ends_at_what_is_left_of_the_invocation()
    {
        var api = Api(delayMs: 4_000);
        using var harness = Harness(DoorTo(Base(api)), remaining: TimeSpan.FromMilliseconds(300));

        var watch = Stopwatch.StartNew();
        var caught = Caught(harness, "host.call(\"campaign.list\")");

        Assert.Equal(DoorService.RuntimeTimeout, caught["code"]!.GetValue<string>());
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(3), $"the call took {watch.Elapsed}");
    }

    [Fact]
    public void A_door_makes_at_most_its_calls()
    {
        var api = Api();
        using var harness = Harness(DoorTo(Base(api), DoorLimits.Standard with { MaxCalls = 2 }));

        harness.Evaluate("host.call(\"campaign.list\"); host.call(\"campaign.list\")");
        var refused = Assert.Throws<HostRuleException>(() => harness.Caught("host.call(\"campaign.list\")"));

        Assert.Equal(HostOutcomeCodes.ApiLimit, refused.Code);
        Assert.Equal(2, refused.Details!["max"]!.GetValue<int>());
        Assert.Equal(2, api.Requests);
        Assert.Equal(2, harness.Budget.ApiCalls);
        Assert.Equal(2, harness.Budget.ExternalCallsStarted);
    }

    [Fact]
    public void A_request_larger_than_the_protocol_allows_never_leaves_the_host()
    {
        var api = Api();
        using var harness = Harness(DoorTo(Base(api)));

        var refused = Assert.Throws<HostRuleException>(
            () => harness.Caught(string.Create(CultureInfo.InvariantCulture, $"host.call(\"journal.append\", {{ text: \"x\".repeat({PluginProtocol.MaxResultBytes}) }})")));

        Assert.Equal(HostOutcomeCodes.ApiLimit, refused.Code);
        Assert.Equal(0, api.Requests);
    }

    /// <summary>The host says each call on stderr by its operation and status, and never writes the capability.</summary>
    [Fact]
    public void Each_call_is_said_on_stderr_and_the_capability_never_is()
    {
        var api = Api();
        using var harness = Harness(DoorTo(Base(api)));

        harness.Evaluate($"host.call(\"campaign.list\"); host.log(\"info\", \"{Capability}\")");

        var line = Assert.Single(harness.Lines, line => line["message"]?.GetValue<string>() == "api");
        Assert.Equal("campaign.list", line["data"]!["operation"]!.GetValue<string>());
        Assert.Equal((int)HttpStatusCode.OK, line["data"]!["status"]!.GetValue<int>());
        Assert.DoesNotContain(Capability, harness.Stderr, StringComparison.Ordinal);
        Assert.Contains(Redactor.Mask, harness.Stderr, StringComparison.Ordinal);
    }

    [Fact]
    public void Without_a_door_there_is_no_call()
    {
        using var harness = new SdkHarness(_package.Root);

        Assert.Equal("undefined", harness.Evaluate("typeof host.call").AsString());
        Assert.Null(harness.Door);
    }
}
