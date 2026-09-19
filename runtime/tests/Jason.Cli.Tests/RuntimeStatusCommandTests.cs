using System.Net;
using System.Text.Json;
using Jason.Cli;
using Jason.Cli.Commands;
using Jason.Cli.Tests.Commands;
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
        InfoJson(instanceId, new DispatcherInfo(DispatcherState.Running, 10, 4, 0, null, 0, 0));

    private static string InfoJson(string instanceId, DispatcherInfo dispatcher) =>
        InfoJson(instanceId, dispatcher, new PluginsInfo(2, "snp_01J4", new DateTimeOffset(2026, 9, 14, 12, 0, 0, TimeSpan.Zero), true));

    private static string InfoJson(string instanceId, DispatcherInfo dispatcher, PluginsInfo plugins) =>
        InfoJson(instanceId, dispatcher, plugins, new RoutesInfo("rts_01J4", new DateTimeOffset(2026, 9, 14, 12, 0, 0, TimeSpan.Zero), "fake-provider", 1, 3));

    private static string InfoJson(string instanceId, DispatcherInfo dispatcher, PluginsInfo plugins, RoutesInfo routes) => JsonSerializer.Serialize(
        new SystemInfoResponse(
            "0.1.0-dev",
            "v1",
            instanceId,
            77,
            DateTimeOffset.UnixEpoch,
            "/home/u/.jason",
            new DatabaseInfo(["20260913225419_InitialCreate"]),
            dispatcher,
            plugins,
            routes),
        JasonJson.Options);

    /// <summary>
    /// The reviews this runtime has summoned, beside the counters they belong with. A loop that is running and
    /// has summoned nothing is exactly what somebody checking on it wants to see, and the number is no use in a
    /// field nothing reads.
    /// </summary>
    [Fact]
    public async Task Human_status_shows_how_many_reviews_have_been_summoned()
    {
        using var dir = new TempPaths();
        dir.WriteDescriptor(Descriptor);
        var dispatcher = new DispatcherInfo(DispatcherState.Running, 10, 4, 1, new DateTimeOffset(2026, 9, 14, 10, 0, 0, TimeSpan.Zero), 41, 7);
        var (env, output, _) = Environment(dir, new FakeHandler(_ => Response(HttpStatusCode.OK, InfoJson("rt_LIVE", dispatcher))));

        var exit = await RuntimeStatusCommand.RunAsync(env, human: true, CancellationToken.None);

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Contains("· 7 summoned", output.ToString(), StringComparison.Ordinal);
    }

    /// <summary>
    /// And zero is the number somebody actually checks for. "The loop is running and has summoned nothing"
    /// is a state worth seeing; a field that disappears at zero answers that question by saying nothing at
    /// all, which reads as a runtime too old to have the counter.
    /// </summary>
    [Fact]
    public async Task Human_status_shows_a_summons_count_of_none()
    {
        using var dir = new TempPaths();
        dir.WriteDescriptor(Descriptor);
        var dispatcher = new DispatcherInfo(DispatcherState.Running, 10, 4, 1, new DateTimeOffset(2026, 9, 14, 10, 0, 0, TimeSpan.Zero), 41, 0);
        var (env, output, _) = Environment(dir, new FakeHandler(_ => Response(HttpStatusCode.OK, InfoJson("rt_LIVE", dispatcher))));

        var exit = await RuntimeStatusCommand.RunAsync(env, human: true, CancellationToken.None);

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Contains("· 0 summoned", output.ToString(), StringComparison.Ordinal);
    }

    /// <summary>
    /// The descriptor publisher some of these tests launch runs on a thread of its own, and an exception
    /// escaping a bare thread does not fail a test — it terminates the test host, taking every unrelated test
    /// in the process with it and naming none of them. This is the way it really fails: the CLI it serves
    /// polls the same file, and a reader holds it in a way that denies a writer while it reads, so a publish
    /// landing mid-poll is refused.
    /// </summary>
    [Fact]
    public void A_descriptor_refused_for_the_whole_window_is_survived_rather_than_thrown()
    {
        using var dir = new TempPaths();
        dir.WriteDescriptor(Held);

        // Held exactly as a reader holds it, for longer than the publisher will keep trying: others may read,
        // nobody may write.
        using var reader = File.Open(dir.Paths.DescriptorFile, FileMode.Open, FileAccess.Read, FileShare.Read);

        RuntimeVerbs.Publish(dir, Descriptor);

        // It gave up rather than throwing, which is what keeps three hundred unrelated tests alive, and wrote
        // nothing; the test that was waiting for a descriptor is left to fail in its own words.
        Assert.Equal("rt_HELD", Instance(dir));
    }

    /// <summary>
    /// And a refusal that ends inside the window is waited out rather than given up on. The publisher exists
    /// to answer a CLI that is polling the same file, so being refused once is the ordinary case and not the
    /// failure — what would be a failure is a descriptor that never arrives because the first try lost a race.
    /// </summary>
    [Fact]
    public async Task A_descriptor_refused_and_then_released_lands_inside_the_retry_window()
    {
        using var dir = new TempPaths();
        dir.WriteDescriptor(Held);

        var ct = TestContext.Current.CancellationToken;
        var reader = File.Open(dir.Paths.DescriptorFile, FileMode.Open, FileAccess.Read, FileShare.Read);
        var released = Task.Run(
            async () =>
            {
                await Task.Delay(200, ct);
                reader.Dispose();
            },
            ct);

        RuntimeVerbs.Publish(dir, Descriptor);
        await released;

        // The publish that landed is the one made after the lock went, which only a retry can have made: a
        // single attempt would have been refused and given up while the file was still held.
        Assert.Equal("rt_LIVE", Instance(dir));
    }

    /// <summary>A descriptor whose only job is to be the value a publish has to replace.</summary>
    private static readonly RuntimeDescriptor Held =
        new("v1", "0.1.0-dev", "rt_HELD", 77, "http://127.0.0.1:5000", "the-token", DateTimeOffset.UnixEpoch);

    private static string? Instance(TempPaths dir) =>
        JsonSerializer.Deserialize<RuntimeDescriptor>(File.ReadAllText(dir.Paths.DescriptorFile), JasonJson.Options)?.InstanceId;

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
        var dispatcher = new DispatcherInfo(DispatcherState.Running, 10, 4, 2, new DateTimeOffset(2026, 9, 14, 10, 0, 0, TimeSpan.Zero), 41, 3);
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

    /// <summary>
    /// Where work is being sent, beside which packages are active: the two are one question, and a status that
    /// answered only half of it would send an operator to a second call to find the other half.
    /// </summary>
    [Fact]
    public async Task Human_mode_says_where_work_is_being_sent()
    {
        using var dir = new TempPaths();
        dir.WriteDescriptor(Descriptor);
        var (env, output, _) = Environment(dir, new FakeHandler(_ => Response(HttpStatusCode.OK, InfoJson("rt_LIVE"))));

        var exit = await RuntimeStatusCommand.RunAsync(env, human: true, CancellationToken.None);

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Contains(
            "Routes:     snapshot rts_01J4 · default fake-provider · 1 override · 3 campaign routes",
            output.ToString(),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Human_mode_says_when_nothing_is_routed_anywhere()
    {
        using var dir = new TempPaths();
        dir.WriteDescriptor(Descriptor);
        var dispatcher = new DispatcherInfo(DispatcherState.Running, 10, 4, 0, null, 0, 0);
        var plugins = new PluginsInfo(2, "snp_01J4", DateTimeOffset.UnixEpoch, true);
        var routes = new RoutesInfo("rts_EMPTY", DateTimeOffset.UnixEpoch, null, 0, 0);
        var (env, output, _) = Environment(dir, new FakeHandler(_ => Response(HttpStatusCode.OK, InfoJson("rt_LIVE", dispatcher, plugins, routes))));

        var exit = await RuntimeStatusCommand.RunAsync(env, human: true, CancellationToken.None);

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Contains("Routes:     nothing is routed anywhere · snapshot rts_EMPTY", output.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Human_mode_says_that_the_last_load_was_refused()
    {
        using var dir = new TempPaths();
        dir.WriteDescriptor(Descriptor);
        var plugins = new PluginsInfo(0, "snp_EMPTY", DateTimeOffset.UnixEpoch, false);
        var dispatcher = new DispatcherInfo(DispatcherState.Running, 10, 4, 0, null, 0, 0);
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
        Assert.Contains("Routes:     unknown", output.ToString(), StringComparison.Ordinal);
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
