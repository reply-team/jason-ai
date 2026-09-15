using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Jason.Contracts.Api;
using Jason.Contracts.Discovery;
using Jason.Contracts.Json;
using Jason.Runtime.Hosting;
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

    private static readonly RuntimeHostOptions Quiet = new(ShippedSettingsDirectory: null, ConsoleLogging: false);

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
    public static async Task<RuntimeApiFixture> StartAsync(
        CancellationToken cancellationToken,
        Action<JasonPaths>? prepare = null,
        TimeProvider? clock = null,
        Action<IServiceCollection>? configureServices = null)
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

        var options = Quiet with { Clock = clock, ConfigureServices = configureServices };
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

    public async Task<T> PostOkAsync<T>(string operation, object? body, CancellationToken cancellationToken)
    {
        var (status, text) = await PostAsync(operation, body, cancellationToken);
        Assert.Equal(HttpStatusCode.OK, status);
        return JsonSerializer.Deserialize<T>(text, JasonJson.Options)!;
    }

    public async Task<ErrorBody> PostErrorAsync(string operation, object? body, HttpStatusCode expected, CancellationToken cancellationToken)
    {
        var (status, text) = await PostAsync(operation, body, cancellationToken);
        Assert.Equal(expected, status);
        return JsonSerializer.Deserialize<ErrorResponse>(text, JasonJson.Options)!.Error;
    }

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
