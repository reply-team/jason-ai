using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Jason.Contracts.Api;
using Jason.Contracts.Discovery;
using Jason.Contracts.Json;
using Jason.Runtime.Hosting;

namespace Jason.Runtime.Tests;

/// <summary>A real runtime on a temporary data directory plus an authenticated HTTP client that speaks the API's JSON.</summary>
public sealed class RuntimeApiFixture : IAsyncDisposable
{
    private static readonly RuntimeHostOptions Quiet = new(ShippedSettingsDirectory: null, ConsoleLogging: false);

    private readonly TempDataDir _dir = new();
    private RunningRuntime? _runtime;
    private HttpClient? _http;

    public JasonPaths Paths => _dir.Paths;

    public RunningRuntime Runtime => _runtime!;

    public static async Task<RuntimeApiFixture> StartAsync(CancellationToken cancellationToken)
    {
        var fixture = new RuntimeApiFixture();
        fixture._runtime = await RuntimeHost.StartAsync(fixture._dir.Paths, Quiet, cancellationToken);
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
