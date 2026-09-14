using System.Text.Json.Nodes;
using Jason.Contracts.Plugins;
using Jason.PluginHost.Sdk;
using Jason.PluginHost.Tests.Fixtures;
using Jint;

namespace Jason.PluginHost.Tests.Sdk;

public sealed class HttpServiceTests : IDisposable
{
    private readonly TempPackage _package = new();
    private readonly LoopbackHttpServer _server = new();

    public void Dispose()
    {
        _server.Dispose();
        _package.Dispose();
    }

    private SdkHarness Harness(Action<InvocationBuilder>? configure = null) =>
        new(_package.Root, builder =>
        {
            builder.Grants = new InvocationGrants(null, new HttpGrants([_server.HostKey]), null);
            configure?.Invoke(builder);
        });

    private static JsonObject Request(SdkHarness harness, string request) =>
        (JsonObject)JsJson.ToJson(harness.Engine, harness.Evaluate("host.http(" + request + ")"))!;

    private static string Quote(string value) => JsonValue.Create(value)!.ToJsonString();

    [Fact]
    public void A_request_to_a_granted_host_comes_back_whole()
    {
        using var harness = Harness();

        var response = Request(harness, $"{{ method: \"GET\", url: {Quote(_server.Url("/ok"))} }}");

        Assert.Equal(200, response["status"]!.GetValue<int>());
        Assert.Equal("{\"ok\":true}", response["body"]!.GetValue<string>());
        Assert.Equal("yes", response["headers"]!["x-test"]!.GetValue<string>());
        Assert.False(response["truncated"]!.GetValue<bool>());
    }

    [Fact]
    public void A_post_carries_its_body_and_its_content_type_and_nothing_else()
    {
        using var harness = Harness();

        var response = Request(
            harness,
            $"{{ method: \"POST\", url: {Quote(_server.Url("/echo"))}, headers: {{ \"Content-Type\": \"application/json\" }}, body: \"{{\\\"a\\\":1}}\" }}");

        var echo = (JsonObject)JsonNode.Parse(response["body"]!.GetValue<string>())!;
        Assert.Equal("POST", echo["method"]!.GetValue<string>());
        Assert.Equal("{\"a\":1}", echo["body"]!.GetValue<string>());
        Assert.Equal("application/json", echo["headers"]!["content-type"]!.GetValue<string>());
        Assert.Equal(HttpService.UserAgent, echo["headers"]!["user-agent"]!.GetValue<string>());
        Assert.Null(echo["headers"]!["authorization"]);
        Assert.Null(echo["headers"]!["cookie"]);
    }

    [Fact]
    public void A_method_beyond_the_two_is_refused()
    {
        using var harness = Harness();

        var refused = Assert.Throws<HostRuleException>(() =>
            harness.Evaluate($"host.http({{ method: \"PUT\", url: {Quote(_server.Url("/ok"))} }})"));

        Assert.Equal(OutcomeCodes.HttpMethodNotAllowed, refused.Code);
    }

    [Fact]
    public void Plain_http_is_only_for_loopback()
    {
        using var harness = Harness(builder => builder.Grants = new InvocationGrants(null, new HttpGrants(["example.test"]), null));

        var refused = Assert.Throws<HostRuleException>(() => harness.Evaluate("host.http({ method: \"GET\", url: \"http://example.test/\" })"));

        Assert.Equal(OutcomeCodes.HttpSchemeNotAllowed, refused.Code);
    }

    [Fact]
    public void A_host_that_was_not_granted_is_never_reached()
    {
        using var harness = Harness();

        var refused = Assert.Throws<HostRuleException>(() => harness.Evaluate("host.http({ method: \"GET\", url: \"https://not-granted.test/x\" })"));

        Assert.Equal(OutcomeCodes.HttpHostNotAllowed, refused.Code);
        Assert.Equal("not-granted.test", refused.Details!["host"]!.GetValue<string>());
    }

    [Fact]
    public void Without_the_capability_nothing_is_requested()
    {
        using var harness = new SdkHarness(_package.Root);

        var refused = Assert.Throws<HostRuleException>(() =>
            harness.Evaluate($"host.http({{ method: \"GET\", url: {Quote(_server.Url("/ok"))} }})"));

        Assert.Equal(OutcomeCodes.CapabilityNotGranted, refused.Code);
        Assert.Equal("http", refused.Details!["capability"]!.GetValue<string>());
    }

    [Fact]
    public void A_header_that_belongs_to_the_connection_is_refused()
    {
        using var harness = Harness();

        var refused = Assert.Throws<HostRuleException>(() =>
            harness.Evaluate($"host.http({{ method: \"GET\", url: {Quote(_server.Url("/ok"))}, headers: {{ Host: \"elsewhere.test\" }} }})"));

        Assert.Equal(OutcomeCodes.HttpHeaderNotAllowed, refused.Code);
        Assert.Equal("Host", refused.Details!["header"]!.GetValue<string>());
    }

    [Fact]
    public void A_body_beyond_the_cap_is_refused_before_it_is_sent()
    {
        using var harness = Harness(builder => builder.Limits = builder.Limits with { Http = builder.Limits.Http with { RequestBytes = 1024 } });

        var refused = Assert.Throws<HostRuleException>(() =>
            harness.Evaluate($"host.http({{ method: \"POST\", url: {Quote(_server.Url("/echo"))}, body: \"x\".repeat(1025) }})"));

        Assert.Equal(OutcomeCodes.HttpLimit, refused.Code);
        Assert.Equal("request_bytes", refused.Details!["limit"]!.GetValue<string>());
    }

    [Fact]
    public void A_response_beyond_the_cap_is_cut_and_the_cut_is_flagged()
    {
        using var harness = Harness(builder => builder.Limits = builder.Limits with { Http = builder.Limits.Http with { ResponseBytes = 65_536 } });

        var response = Request(harness, $"{{ method: \"GET\", url: {Quote(_server.Url("/big?bytes=200000"))} }}");

        Assert.True(response["truncated"]!.GetValue<bool>());
        Assert.Equal(65_536, response["body"]!.GetValue<string>().Length);
    }

    [Fact]
    public void A_request_that_outstays_its_timeout_comes_back_as_no_answer()
    {
        using var harness = Harness();

        var response = Request(harness, $"{{ method: \"GET\", url: {Quote(_server.Url("/slow?ms=30000"))}, timeout_ms: 300 }}");

        Assert.Equal(0, response["status"]!.GetValue<int>());
        Assert.False(string.IsNullOrWhiteSpace(response["error"]!.GetValue<string>()));
        Assert.True(response["duration_ms"]!.GetValue<long>() < 10_000);
    }

    [Fact]
    public void A_redirect_is_answered_to_the_plugin_rather_than_followed()
    {
        using var harness = Harness();

        var response = Request(harness, $"{{ method: \"GET\", url: {Quote(_server.Url("/redirect"))} }}");

        Assert.Equal(302, response["status"]!.GetValue<int>());
        Assert.Equal("/ok", response["headers"]!["location"]!.GetValue<string>());
        Assert.Equal(string.Empty, response["body"]!.GetValue<string>());
    }

    [Fact]
    public void An_invocation_may_make_only_so_many_requests()
    {
        using var harness = Harness(builder => builder.Limits = builder.Limits with { Http = builder.Limits.Http with { MaxCalls = 2 } });

        Request(harness, $"{{ method: \"GET\", url: {Quote(_server.Url("/ok"))} }}");
        Request(harness, $"{{ method: \"GET\", url: {Quote(_server.Url("/ok"))} }}");
        var refused = Assert.Throws<HostRuleException>(() =>
            harness.Evaluate($"host.http({{ method: \"GET\", url: {Quote(_server.Url("/ok"))} }})"));

        Assert.Equal(OutcomeCodes.HttpLimit, refused.Code);
        Assert.Equal(2, harness.Budget.HttpCalls);
    }

    [Fact]
    public void Every_request_is_on_the_record_without_its_query_headers_or_body()
    {
        using var harness = Harness();

        Request(harness, $"{{ method: \"GET\", url: {Quote(_server.Url("/big?bytes=8"))} }}");

        var line = Assert.Single(harness.Lines);
        Assert.Equal("http", line["message"]!.GetValue<string>());
        Assert.Equal("GET", line["data"]!["method"]!.GetValue<string>());
        Assert.Equal("/big", line["data"]!["path"]!.GetValue<string>());
        Assert.Equal(200, line["data"]!["status"]!.GetValue<int>());
        Assert.DoesNotContain("bytes=8", harness.Stderr, StringComparison.Ordinal);
    }

    [Fact]
    public void An_option_the_call_does_not_have_is_a_type_error()
    {
        using var harness = Harness();

        Assert.Equal("TypeError", harness.Caught($"host.http({{ method: \"GET\", url: {Quote(_server.Url("/ok"))}, follow: true }})"));
        Assert.Equal("TypeError", harness.Caught("host.http({ method: \"GET\", url: \"not a url\" })"));
        Assert.Equal("TypeError", harness.Caught("host.http({ method: \"GET\" })"));
    }

    [Theory]
    [InlineData("https://a.test/", "a.test", true)]
    [InlineData("https://a.test:8443/", "a.test", false)]
    [InlineData("https://a.test:8443/", "a.test:8443", true)]
    [InlineData("https://A.TEST/", "a.test", true)]
    [InlineData("https://sub.a.test/", "a.test", false)]
    [InlineData("https://a.test:443/", "a.test", true)]
    public void An_allowlist_entry_matches_a_host_and_its_port_exactly(string url, string granted, bool allowed)
    {
        Assert.Equal(allowed, HttpService.IsAllowed(new Uri(url), [granted]));
    }

    [Theory]
    [InlineData("http://localhost:5555/", true)]
    [InlineData("http://127.0.0.1/", true)]
    [InlineData("http://[::1]:8080/", true)]
    [InlineData("http://10.0.0.1/", false)]
    public void Loopback_is_the_three_names_a_machine_calls_itself(string url, bool loopback)
    {
        Assert.Equal(loopback, HttpService.IsLoopback(new Uri(url)));
    }
}
