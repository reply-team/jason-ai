using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Jason.Contracts.Api;
using Jason.Contracts.Discovery;
using Jason.Contracts.Json;
using Jason.Runtime.Configuration;
using Jason.Runtime.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Jason.Runtime.Tests;

/// <summary>A real runtime on a temporary data directory plus an authenticated HTTP client that speaks the API's JSON.</summary>
public sealed class RuntimeApiFixture : IAsyncDisposable
{
    /// <summary>
    /// What a fixture writes when the test asked for no settings of its own. A shipped runtime dispatches by
    /// default: it scans the moment it starts and then every tick, so a test that leaves claimable work lying
    /// around for more than a tick would find it claimed out from under it. A test that wants the loop writes
    /// its own settings and drives the scans.
    /// </summary>
    public const string DispatcherOff = """{"Dispatcher":{"Enabled":false}}""";


    private readonly TempDataDir _dir = new();
    private RunningRuntime? _runtime;
    private HttpClient? _http;

    public JasonPaths Paths => _dir.Paths;

    public RunningRuntime Runtime => _runtime!;

    /// <summary>A service of the running runtime, so a test reads exactly what the runtime itself reads.</summary>
    public T Resolve<T>()
        where T : notnull => Runtime.Services.GetRequiredService<T>();

    /// <param name="prepare">
    /// Runs after the temporary data directory exists and before the runtime starts: write settings here. A
    /// test that passes none gets <see cref="DispatcherOff"/> written for it.
    /// </param>
    /// <param name="clock">The runtime's clock, so a test can move time.</param>
    /// <param name="configureServices">Registered last while composing, so a test's service wins over the runtime's own.</param>
    /// <param name="configurationBuilt">Handed the runtime's configuration as soon as it is built, before the runtime reads it.</param>
    public static async Task<RuntimeApiFixture> StartAsync(
        CancellationToken cancellationToken,
        Action<JasonPaths>? prepare = null,
        TimeProvider? clock = null,
        Action<IServiceCollection>? configureServices = null,
        Action<IConfigurationRoot>? configurationBuilt = null)
    {
        var fixture = new RuntimeApiFixture();
        Directory.CreateDirectory(fixture._dir.Paths.ConfigDirectory);
        if (prepare is null)
        {
            File.WriteAllText(fixture._dir.Paths.UserSettingsFile, DispatcherOff);
        }
        else
        {
            prepare(fixture._dir.Paths);
        }

        var options = TestRuntimeOptions.Quiet with
        {
            Clock = clock,
            ConfigurationBuilt = configurationBuilt,
            ConfigureServices = services =>
            {
                // Whatever settings the test wrote, a fixture's runtime never asks the release feed and never
                // notifies anybody, unless the test's own registration below turns one back on: those are the two
                // things a runtime does toward somebody outside the machine without being asked, and a test suite
                // is no place to do either from. An override after binding rather than a line in the settings
                // file, because the file is the test's to write — and the same call the shared options make, so
                // the rule is written once.
                TestRuntimeOptions.TurnTheOutsideOff(services);
                configureServices?.Invoke(services);
            },
        };
        fixture._runtime = await RuntimeHost.StartAsync(fixture._dir.Paths, options, cancellationToken);
        fixture._http = new HttpClient { BaseAddress = fixture._runtime.BaseUrl };
        fixture._http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", fixture._runtime.Token);
        return fixture;
    }

    public async Task<(HttpStatusCode Status, string Body)> PostAsync(string operation, object? body, CancellationToken cancellationToken)
    {
        using var content = new StringContent(body is null ? "{}" : JsonSerializer.Serialize(body, JasonJson.Options), Encoding.UTF8, "application/json");
        using var response = await _http!.PostAsync(Operations.Route(operation), content, cancellationToken);
        return (response.StatusCode, await response.Content.ReadAsStringAsync(cancellationToken));
    }

    /// <summary>The body exactly as written, for a request no serializer would produce.</summary>
    public async Task<(HttpStatusCode Status, string Body)> PostRawAsync(string operation, string body, CancellationToken cancellationToken)
    {
        using var content = new StringContent(body, Encoding.UTF8, "application/json");
        using var response = await _http!.PostAsync(Operations.Route(operation), content, cancellationToken);
        return (response.StatusCode, await response.Content.ReadAsStringAsync(cancellationToken));
    }

    /// <summary>
    /// A call that must succeed. When it does not, the failure carries what the runtime answered — the error's code
    /// says why, and a status alone does not.
    /// </summary>
    public async Task<T> PostOkAsync<T>(string operation, object? body, CancellationToken cancellationToken)
    {
        var (status, text) = await PostAsync(operation, body, cancellationToken);
        Assert.True(status == HttpStatusCode.OK, Unexpected(operation, status, HttpStatusCode.OK, text));
        return JsonSerializer.Deserialize<T>(text, JasonJson.Options)!;
    }

    /// <summary>A call that must be refused with one status; any other answer is reported whole.</summary>
    public async Task<ErrorBody> PostErrorAsync(string operation, object? body, HttpStatusCode expected, CancellationToken cancellationToken)
    {
        var (status, text) = await PostAsync(operation, body, cancellationToken);
        Assert.True(status == expected, Unexpected(operation, status, expected, text));
        return JsonSerializer.Deserialize<ErrorResponse>(text, JasonJson.Options)!.Error;
    }

    private FixtureScrubber? _scrubber;

    private FixtureScrubber Scrubber => _scrubber ??= new FixtureScrubber(Paths.Root, _dir.Home);

    /// <summary>A recorded call that must succeed: the exchange is held to its file under docs/api/fixtures, and the answer comes back for the test's own assertions.</summary>
    public async Task<T> RecordAsync<T>(string operation, string @case, object? body, CancellationToken cancellationToken)
    {
        var requestJson = body is null ? "{}" : JsonSerializer.Serialize(body, JasonJson.Options);
        var (status, text) = await PostRawAsync(operation, requestJson, cancellationToken);
        Assert.True(status == HttpStatusCode.OK, Unexpected(operation, status, HttpStatusCode.OK, text));
        Hold(operation, @case, requestJson, (int)status, text);
        return JsonSerializer.Deserialize<T>(text, JasonJson.Options)!;
    }

    public async Task<ErrorBody> RecordErrorAsync(string operation, string @case, object? body, HttpStatusCode expected, CancellationToken cancellationToken)
    {
        var requestJson = body is null ? "{}" : JsonSerializer.Serialize(body, JasonJson.Options);
        var (status, text) = await PostRawAsync(operation, requestJson, cancellationToken);
        Assert.True(status == expected, Unexpected(operation, status, expected, text));
        Hold(operation, @case, requestJson, (int)status, text);
        return JsonSerializer.Deserialize<ErrorResponse>(text, JasonJson.Options)!.Error;
    }

    /// <summary>The 401 a page meets when its token is gone: recorded with a bearer nobody minted.</summary>
    public async Task RecordUnauthorizedAsync(string operation, string @case, CancellationToken cancellationToken)
    {
        using var http = new HttpClient { BaseAddress = Runtime.BaseUrl };
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "revoked-" + new string('x', 36));
        using var content = new StringContent("{}", Encoding.UTF8, "application/json");
        using var response = await http.PostAsync(Operations.Route(operation), content, cancellationToken);
        var text = await response.Content.ReadAsStringAsync(cancellationToken);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Hold(operation, @case, "{}", (int)response.StatusCode, text);
    }

    private void Hold(string operation, string @case, string requestJson, int status, string responseJson)
    {
        var exchange = new JsonObject
        {
            ["operation"] = operation,
            ["case"] = @case,
            ["request"] = Scrubber.Scrub(JsonNode.Parse(requestJson)),
            ["status"] = status,
            ["response"] = Scrubber.Scrub(JsonNode.Parse(responseJson)),
        };
        ApiFixtures.Hold(operation + "/" + @case, exchange);
    }

    private static string Unexpected(string operation, HttpStatusCode status, HttpStatusCode expected, string body) =>
        string.Create(
            System.Globalization.CultureInfo.InvariantCulture,
            $"{operation} answered {(int)status} {status} where {(int)expected} {expected} was expected: {body}");

    public async ValueTask DisposeAsync()
    {
        _http?.Dispose();
        if (_runtime is not null)
        {
            await _runtime.StopAsync();
        }

        _dir.Dispose();
    }
}
