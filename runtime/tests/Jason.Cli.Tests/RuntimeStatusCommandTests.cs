using System.Net;
using System.Text.Json;
using Jason.Cli;
using Jason.Cli.Commands;
using Jason.Contracts.Api;
using Jason.Contracts.Discovery;
using Jason.Contracts.Json;

namespace Jason.Cli.Tests;

public class RuntimeStatusCommandTests
{
    private static readonly RuntimeDescriptor Descriptor = new("v1", "0.1.0-dev", "rt_LIVE", 77, "http://127.0.0.1:5000", "the-token", DateTimeOffset.UnixEpoch);

    /// <summary>A runtime from before the dispatcher and the plugin registry existed: the fields are simply not in the body.</summary>
    private const string InfoWithoutADispatcher =
        "{\"runtime_version\":\"0.1.0-dev\",\"api_version\":\"v1\",\"instance_id\":\"rt_LIVE\",\"pid\":77,"
        + "\"started_at\":\"1970-01-01T00:00:00+00:00\",\"data_dir\":\"/home/u/.jason\","
        + "\"database\":{\"applied_migrations\":[\"20260913225419_InitialCreate\"]}}";

    private static string InfoJson(string instanceId) =>
        InfoJson(instanceId, new DispatcherInfo(DispatcherState.Running, 10, 4, 0, null, 0));

    private static string InfoJson(string instanceId, DispatcherInfo dispatcher) =>
        InfoJson(instanceId, dispatcher, new PluginsInfo(2, "snp_01J4", new DateTimeOffset(2026, 9, 14, 12, 0, 0, TimeSpan.Zero), true));

    private static string InfoJson(string instanceId, DispatcherInfo dispatcher, PluginsInfo plugins) => JsonSerializer.Serialize(
        new SystemInfoResponse(
            "0.1.0-dev",
            "v1",
            instanceId,
            77,
            DateTimeOffset.UnixEpoch,
            "/home/u/.jason",
            new DatabaseInfo(["20260913225419_InitialCreate"]),
            dispatcher,
            plugins),
        JasonJson.Options);

    [Fact]
    public async Task No_descriptor_exits_3_with_no_descriptor_error()
    {
        using var dir = new TempPaths();
        var (env, output, _) = Environment(dir, new FakeHandler(_ => throw new InvalidOperationException("must not be called")));

        var exit = await RuntimeStatusCommand.RunAsync(env, human: false, CancellationToken.None);

        Assert.Equal(ExitCodes.RuntimeUnavailable, exit);
        Assert.Contains("\"code\":\"no_descriptor\"", output.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Unreachable_runtime_exits_3_with_runtime_unreachable()
    {
        using var dir = new TempPaths();
        dir.WriteDescriptor(Descriptor);
        var (env, output, _) = Environment(dir, new FakeHandler(_ => throw new HttpRequestException("connection refused")));

        var exit = await RuntimeStatusCommand.RunAsync(env, human: false, CancellationToken.None);

        Assert.Equal(ExitCodes.RuntimeUnavailable, exit);
        Assert.Contains("\"code\":\"runtime_unreachable\"", output.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Rejected_token_exits_3_with_unauthorized()
    {
        using var dir = new TempPaths();
        dir.WriteDescriptor(Descriptor);
        var (env, output, _) = Environment(dir, new FakeHandler(_ => Response(HttpStatusCode.Unauthorized, "{\"error\":{\"code\":\"unauthorized\",\"message\":\"x\",\"retryable\":false}}")));

        var exit = await RuntimeStatusCommand.RunAsync(env, human: false, CancellationToken.None);

        Assert.Equal(ExitCodes.RuntimeUnavailable, exit);
        Assert.Contains("\"code\":\"unauthorized\"", output.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Healthy_runtime_prints_the_exact_response_and_exits_0()
    {
        using var dir = new TempPaths();
        dir.WriteDescriptor(Descriptor);
        HttpRequestMessage? seen = null;
        var body = InfoJson("rt_LIVE");
        var (env, output, error) = Environment(dir, new FakeHandler(request => { seen = request; return Response(HttpStatusCode.OK, body); }));

        var exit = await RuntimeStatusCommand.RunAsync(env, human: false, CancellationToken.None);

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Equal(body + System.Environment.NewLine, output.ToString());
        Assert.Equal(string.Empty, error.ToString());
        Assert.NotNull(seen);
        Assert.Equal(HttpMethod.Post, seen!.Method);
        Assert.Equal("http://127.0.0.1:5000/v1/system.info", seen.RequestUri!.ToString());
        Assert.Equal("Bearer", seen.Headers.Authorization!.Scheme);
        Assert.Equal("the-token", seen.Headers.Authorization.Parameter);
    }

    [Fact]
    public async Task Instance_mismatch_exits_3_with_stale_descriptor()
    {
        using var dir = new TempPaths();
        dir.WriteDescriptor(Descriptor);
        var (env, output, _) = Environment(dir, new FakeHandler(_ => Response(HttpStatusCode.OK, InfoJson("rt_OTHER"))));

        var exit = await RuntimeStatusCommand.RunAsync(env, human: false, CancellationToken.None);

        Assert.Equal(ExitCodes.RuntimeUnavailable, exit);
        Assert.Contains("\"code\":\"stale_descriptor\"", output.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Api_business_error_is_printed_verbatim_and_exits_1()
    {
        using var dir = new TempPaths();
        dir.WriteDescriptor(Descriptor);
        const string envelope = "{\"error\":{\"code\":\"something_odd\",\"message\":\"nope\",\"retryable\":false}}";
        var (env, output, _) = Environment(dir, new FakeHandler(_ => Response(HttpStatusCode.InternalServerError, envelope)));

        var exit = await RuntimeStatusCommand.RunAsync(env, human: false, CancellationToken.None);

        Assert.Equal(ExitCodes.ApiError, exit);
        Assert.Equal(envelope + System.Environment.NewLine, output.ToString());
    }

    [Fact]
    public async Task Human_mode_renders_lines_for_people()
    {
        using var dir = new TempPaths();
        dir.WriteDescriptor(Descriptor);
        var (env, output, _) = Environment(dir, new FakeHandler(_ => Response(HttpStatusCode.OK, InfoJson("rt_LIVE"))));

        var exit = await RuntimeStatusCommand.RunAsync(env, human: true, CancellationToken.None);

        Assert.Equal(ExitCodes.Success, exit);
        var text = output.ToString();
        Assert.Contains("Runtime:", text, StringComparison.Ordinal);
        Assert.Contains("rt_LIVE", text, StringComparison.Ordinal);
        Assert.Contains("http://127.0.0.1:5000", text, StringComparison.Ordinal);
        Assert.DoesNotContain("{", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Human_mode_prints_what_the_dispatcher_is_doing()
    {
        using var dir = new TempPaths();
        dir.WriteDescriptor(Descriptor);
        var dispatcher = new DispatcherInfo(DispatcherState.Running, 10, 4, 2, new DateTimeOffset(2026, 9, 14, 10, 0, 0, TimeSpan.Zero), 41);
        var (env, output, _) = Environment(dir, new FakeHandler(_ => Response(HttpStatusCode.OK, InfoJson("rt_LIVE", dispatcher))));

        var exit = await RuntimeStatusCommand.RunAsync(env, human: true, CancellationToken.None);

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Contains(
            "Dispatcher: running · tick 10 s · 2/4 attempts · last scan 2026-09-14 10:00:00 UTC",
            output.ToString(),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Human_mode_says_the_dispatcher_has_not_scanned_yet()
    {
        using var dir = new TempPaths();
        dir.WriteDescriptor(Descriptor);
        var (env, output, _) = Environment(dir, new FakeHandler(_ => Response(HttpStatusCode.OK, InfoJson("rt_LIVE"))));

        var exit = await RuntimeStatusCommand.RunAsync(env, human: true, CancellationToken.None);

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Contains("Dispatcher: running · tick 10 s · 0/4 attempts · last scan never", output.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Human_mode_tolerates_a_runtime_that_reports_no_dispatcher()
    {
        using var dir = new TempPaths();
        dir.WriteDescriptor(Descriptor);
        var (env, output, _) = Environment(dir, new FakeHandler(_ => Response(HttpStatusCode.OK, InfoWithoutADispatcher)));

        var exit = await RuntimeStatusCommand.RunAsync(env, human: true, CancellationToken.None);

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Contains("Dispatcher: unknown", output.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Human_mode_prints_what_the_plugin_registry_holds()
    {
        using var dir = new TempPaths();
        dir.WriteDescriptor(Descriptor);
        var (env, output, _) = Environment(dir, new FakeHandler(_ => Response(HttpStatusCode.OK, InfoJson("rt_LIVE"))));

        var exit = await RuntimeStatusCommand.RunAsync(env, human: true, CancellationToken.None);

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Contains(
            "Plugins:    2 active · snapshot snp_01J4 · loaded 2026-09-14 12:00:00 UTC",
            output.ToString(),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Human_mode_says_that_the_last_load_was_refused()
    {
        using var dir = new TempPaths();
        dir.WriteDescriptor(Descriptor);
        var plugins = new PluginsInfo(0, "snp_EMPTY", DateTimeOffset.UnixEpoch, false);
        var dispatcher = new DispatcherInfo(DispatcherState.Running, 10, 4, 0, null, 0);
        var (env, output, _) = Environment(dir, new FakeHandler(_ => Response(HttpStatusCode.OK, InfoJson("rt_LIVE", dispatcher, plugins))));

        var exit = await RuntimeStatusCommand.RunAsync(env, human: true, CancellationToken.None);

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Contains("Plugins:    none active · last reload rejected", output.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Human_mode_tolerates_a_runtime_that_reports_no_plugins()
    {
        using var dir = new TempPaths();
        dir.WriteDescriptor(Descriptor);
        var (env, output, _) = Environment(dir, new FakeHandler(_ => Response(HttpStatusCode.OK, InfoWithoutADispatcher)));

        var exit = await RuntimeStatusCommand.RunAsync(env, human: true, CancellationToken.None);

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Contains("Plugins:    unknown", output.ToString(), StringComparison.Ordinal);
    }

    private static (CliEnvironment Env, StringWriter Out, StringWriter Error) Environment(TempPaths dir, HttpMessageHandler handler)
    {
        var output = new StringWriter();
        var error = new StringWriter();
        return (new CliEnvironment(output, error, dir.Paths, handler), output, error);
    }

    private static HttpResponseMessage Response(HttpStatusCode status, string json) =>
        new(status) { Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json") };
}

public sealed class FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
        Task.FromResult(respond(request));
}
