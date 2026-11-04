using System.Collections.Concurrent;
using System.Net;
using System.Text.Json.Nodes;
using Jason.Contracts.Api;
using Jason.Contracts.Discovery;
using Jason.Contracts.Plugins;
using Jason.Runtime.Configuration;
using Jason.Runtime.Plugins.Door;
using Jason.Runtime.Plugins.Invocation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace Jason.Runtime.Tests.Plugins.Invocation;

/// <summary>
/// The door the invoker opens for a handler: minted for one invocation of a plugin granted a family of the Runtime API,
/// handed to the child on stdin and nowhere else, and closed when the invocation ends, however it ends. The requests here
/// are built by the test, as no production path asks for a door yet; the runtime, the host and the calls are the real
/// ones.
/// </summary>
[Collection(ProcessEnvironmentCollection.Name)]
public class PluginInvokerDoorTests
{
    private const string Caller = "caller";

    private const string LineId = "jrn_01K0D00RL1NE";

    private const string PatientEngine = """{"Dispatcher":{"Enabled":false},"Plugins":{"Limits":{"MaxStatements":1000000000}}}""";

    private const string CallerJs = """
        export function invoke(operation) {
          if (operation === 'echo.run') {
            return { result: { answer: host.call('campaign.list') } };
          }

          if (operation === 'fail.run') {
            throw new Error('the handler gave up');
          }

          while (true) { }
        }
        """;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_handler_granted_a_family_reaches_the_runtime_through_its_door_and_the_door_dies_with_the_invocation()
    {
        var seen = new Bearers();
        await using var api = await StartAsync(seen);

        var result = await InvokeAsync(api, Request("echo.run"), Ct);

        var succeeded = Assert.IsType<InvocationOutcome.Succeeded>(result.Outcome);
        Assert.IsType<JsonArray>(succeeded.Result!["answer"]!["items"]);
        var capability = Assert.Single(seen.Other(api.Runtime.Token));

        // The host said what it called and how it went, and the capability is not among what it said.
        var stderr = await File.ReadAllTextAsync(Path.Combine(result.Launch!.WorkDir, "stderr.log"), Ct);
        Assert.Contains("\"api\"", stderr, StringComparison.Ordinal);
        Assert.Contains("\"campaign.list\"", stderr, StringComparison.Ordinal);
        Assert.DoesNotContain(capability, stderr, StringComparison.Ordinal);

        // Once the invocation has returned, the capability opens nothing.
        Assert.Equal(0, api.Resolve<HandlerCapabilities>().Count);
        using var client = new HttpClient { BaseAddress = api.Runtime.BaseUrl };
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", capability);
        using var content = new StringContent("{}", System.Text.Encoding.UTF8, "application/json");
        using var after = await client.PostAsync(Operations.Route(Operations.CampaignList), content, Ct);
        Assert.Equal(HttpStatusCode.Unauthorized, after.StatusCode);
    }

    /// <summary>
    /// A capability is minted before the child is started, so it outlives the invocation's deadline and kill grace by one
    /// call's bound: the margin covers the start of the child and a call still in flight when the deadline comes.
    /// </summary>
    [Fact]
    public async Task A_door_lives_past_the_invocation_s_deadline_and_kill_grace_by_one_call_s_bound()
    {
        var seen = new Bearers();
        await using var api = await StartAsync(seen);
        var grace = api.Resolve<LiveSettings<PluginsOptions>>().Current.Invoker.KillGraceMs;
        var timeout = TimeSpan.FromSeconds(20);

        var before = DateTimeOffset.UtcNow;
        var result = await InvokeAsync(api, Request("echo.run") with { Timeout = timeout }, Ct);
        var after = DateTimeOffset.UtcNow;

        Assert.IsType<InvocationOutcome.Succeeded>(result.Outcome);
        var expires = Assert.Single(seen.Expiries);
        var margin = timeout + TimeSpan.FromMilliseconds(grace + PluginProtocol.DoorCallTimeoutMs);
        Assert.InRange(expires - margin, before, after);
    }

    [Fact]
    [Critical(Family.S3, "a door's capability reaches the child on stdin alone")]
    public async Task A_door_s_capability_reaches_the_child_on_stdin_and_in_no_argument_variable_or_stderr_line()
    {
        var record = Path.Combine(Path.GetTempPath(), "jason-door-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            await using var api = await StartAsync(host: new RecordingHost(record));

            var result = await InvokeAsync(api, Request("echo.run"), Ct);

            // The stand-in answered nothing, so the invocation failed; what matters is what it was handed.
            Assert.IsType<InvocationOutcome.ProtocolFailure>(result.Outcome);
            var launch = JsonNode.Parse(await File.ReadAllTextAsync(record, Ct))!;
            var door = JsonNode.Parse(launch["stdin"]!.GetValue<string>())!["door"]!;
            var capability = door["capability"]!.GetValue<string>();
            Assert.False(string.IsNullOrWhiteSpace(capability));
            Assert.Equal($"http://127.0.0.1:{api.Runtime.BaseUrl.Port}", door["base_url"]!.GetValue<string>());
            Assert.Equal(LineId, door["line_id"]!.GetValue<string>());
            Assert.Equal(
                OperationFamilies.OperationsOf([OperationFamilies.Read]).Order(StringComparer.Ordinal),
                door["operations"]!.AsArray().Select(operation => operation!.GetValue<string>()));
            Assert.Equal(PluginProtocol.MaxDoorCalls, door["limits"]!["max_calls"]!.GetValue<int>());

            Assert.DoesNotContain(launch["argv"]!.AsArray(), item => item!.GetValue<string>().Contains(capability, StringComparison.Ordinal));
            Assert.DoesNotContain(launch["env"]!.AsObject(), variable => variable.Value!.GetValue<string>().Contains(capability, StringComparison.Ordinal));

            // The stand-in wrote its whole standard input to stderr too; what was kept of it is masked.
            var stderr = await File.ReadAllTextAsync(Path.Combine(result.Launch!.WorkDir, "stderr.log"), Ct);
            Assert.Contains(Redactor.Mask, stderr, StringComparison.Ordinal);
            Assert.DoesNotContain(capability, stderr, StringComparison.Ordinal);

            Assert.Equal(0, api.Resolve<HandlerCapabilities>().Count);
        }
        finally
        {
            File.Delete(record);
        }
    }

    /// <summary>
    /// A door is the plugin's grant and the caller's request together: a plugin that never asked for the API, one that
    /// asked and was granted nothing, and a granted one whose caller asked for no door all run without one.
    /// </summary>
    [Fact]
    public async Task Without_a_grant_or_without_a_request_there_is_no_door()
    {
        var record = Path.Combine(Path.GetTempPath(), "jason-door-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            await using var api = await StartAsync(
                host: new RecordingHost(record),
                extra: paths => TestPlugins.Write(
                    paths,
                    "ungranted",
                    TestPlugins.Manifest("ungranted", extra: "capabilities:\n  api: [read]\n"),
                    CallerJs));

            foreach (var request in new[]
            {
                Request("echo.run", TestPlugins.FakeProviderId),
                Request("echo.run", "ungranted"),
                Request("echo.run") with { Door = null },
            })
            {
                var ran = await InvokeAsync(api, request, Ct);
                Assert.True(File.Exists(record), $"{request.PluginId}: {ran.Outcome}");

                var launch = JsonNode.Parse(await File.ReadAllTextAsync(record, Ct))!;
                var envelope = JsonNode.Parse(launch["stdin"]!.GetValue<string>())!.AsObject();
                Assert.False(envelope.ContainsKey("door"), $"{request.PluginId} was given a door: {envelope["door"]?.ToJsonString()}");
                File.Delete(record);
            }

            Assert.Equal(0, api.Resolve<HandlerCapabilities>().Count);
        }
        finally
        {
            File.Delete(record);
        }
    }

    [Fact]
    public async Task A_door_is_closed_when_the_invocation_ends_however_it_ends()
    {
        await using var api = await StartAsync(settings: PatientEngine);
        var capabilities = api.Resolve<HandlerCapabilities>();

        Assert.IsType<InvocationOutcome.Succeeded>((await InvokeAsync(api, Request("echo.run"), Ct)).Outcome);
        Assert.Equal(0, capabilities.Count);

        Assert.IsType<InvocationOutcome.Failed>((await InvokeAsync(api, Request("fail.run"), Ct)).Outcome);
        Assert.Equal(0, capabilities.Count);

        var timedOut = await InvokeAsync(api, Request("hang.run") with { Timeout = TimeSpan.FromSeconds(1) }, Ct);
        Assert.Equal("plugin_timeout", Assert.IsType<InvocationOutcome.Failed>(timedOut.Outcome).Error.Code);
        Assert.Equal(0, capabilities.Count);

        using var kill = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));
        var killed = await InvokeAsync(api, Request("hang.run") with { Timeout = TimeSpan.FromSeconds(30) }, kill.Token);
        Assert.Equal(ProtocolCodes.PluginKilled, Assert.IsType<InvocationOutcome.ProtocolFailure>(killed.Outcome).Code);
        Assert.Equal(0, capabilities.Count);
    }

    /// <summary>
    /// The runtime's log is written about every invocation and every request, and a door's capability reaches neither:
    /// read back from the files this runtime wrote while a handler called through its door.
    /// </summary>
    [Fact]
    [Critical(Family.S3, "a door's capability never reaches the runtime's log")]
    public async Task The_runtime_s_log_holds_no_door_capability()
    {
        var seen = new Bearers();
        await using var api = await StartAsync(seen);

        var result = await InvokeAsync(api, Request("echo.run"), Ct);

        Assert.IsType<InvocationOutcome.Succeeded>(result.Outcome);
        var capability = Assert.Single(seen.Other(api.Runtime.Token));

        // Read once the runtime has stopped, so the log is whole.
        await api.Runtime.StopAsync();
        var logs = string.Concat(Directory.GetFiles(api.Paths.LogsDirectory, "*", SearchOption.AllDirectories).Select(File.ReadAllText));
        Assert.Contains(result.Provenance.InvocationId, logs, StringComparison.Ordinal);
        Assert.Contains("/v1/campaign.list", logs, StringComparison.Ordinal);
        Assert.DoesNotContain(capability, logs, StringComparison.Ordinal);
    }

    private static PluginInvocationRequest Request(string operation, string plugin = Caller) =>
        new(plugin, operation, [], null, "att_01K0" + operation.Replace('.', '_'), Door: new DoorRequest(LineId));

    private static Task<RuntimeApiFixture> StartAsync(
        Bearers? seen = null,
        IPluginHostLocator? host = null,
        string? settings = null,
        Action<JasonPaths>? extra = null) =>
        RuntimeApiFixture.StartAsync(
            Ct,
            prepare: paths =>
            {
                File.WriteAllText(paths.UserSettingsFile, settings ?? RuntimeApiFixture.DispatcherOff);
                TestPlugins.InstallFakeProvider(paths);
                TestPlugins.Write(
                    paths,
                    Caller,
                    TestPlugins.Manifest(Caller, operations: "[echo.run, fail.run, hang.run]", extra: "capabilities:\n  api: [read]\n"),
                    CallerJs);
                TestPlugins.Grant(paths, Caller, api: [OperationFamilies.Read]);
                extra?.Invoke(paths);
            },
            configureServices: services =>
            {
                services.AddSingleton(host ?? new JasonDllLocator());

                // The fixture package declares the stand-in vendor CLI, which it finds on this search path.
                services.AddSingleton(TestPlugins.SearchPath);
                if (seen is not null)
                {
                    services.AddSingleton<IStartupFilter>(seen);
                }
            });

    private static async Task<PluginInvocationResult> InvokeAsync(RuntimeApiFixture api, PluginInvocationRequest request, CancellationToken kill)
    {
        using var scope = api.Runtime.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<PluginInvoker>().InvokeAsync(request, kill);
    }

    /// <summary>Every bearer the runtime was shown, read in front of everything the runtime does with a request.</summary>
    private sealed class Bearers : IStartupFilter
    {
        private readonly ConcurrentQueue<string> _seen = new();

        private readonly ConcurrentQueue<DateTimeOffset> _expiries = new();

        /// <summary>When each handler capability the runtime was shown was minted to die.</summary>
        public IReadOnlyList<DateTimeOffset> Expiries => [.. _expiries];

        public IReadOnlyList<string> Other(string runToken) =>
            [.. _seen.Where(bearer => !string.Equals(bearer, runToken, StringComparison.Ordinal)).Distinct(StringComparer.Ordinal)];

        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use(async (context, following) =>
            {
                const string Scheme = "Bearer ";
                var header = context.Request.Headers.Authorization.ToString();
                if (header.StartsWith(Scheme, StringComparison.Ordinal))
                {
                    var bearer = header[Scheme.Length..];
                    _seen.Enqueue(bearer);

                    // Asked as of the earliest moment, so the look never ends a capability it reads.
                    if (context.RequestServices.GetRequiredService<HandlerCapabilities>().TryRecognise(bearer, DateTimeOffset.MinValue, out var grant))
                    {
                        _expiries.Enqueue(grant.ExpiresAt);
                    }
                }

                await following(context);
            });
            next(app);
        };
    }

    /// <summary>The stand-in vendor CLI where the plugin host would be, writing down what it was handed.</summary>
    private sealed class RecordingHost(string record) : IPluginHostLocator
    {
        public IReadOnlyList<string> Command => [FakeProviderCli.ExecutablePath, "record-launch", record];
    }
}
