using System.Net.Http.Headers;
using System.Text;
using Jason.Contracts.Api;
using Jason.Contracts.Discovery;
using Jason.Contracts.Execution;

namespace Jason.Cli.Http;

public sealed record RuntimeResponse(int StatusCode, string Body)
{
    public bool IsSuccess => StatusCode is >= 200 and < 300;
}

/// <summary>
/// Thin HTTP client over the descriptor: base URL and bearer token come from it, and the attempt the CLI runs inside, when
/// it runs inside one, is named on every request beside the token.
/// </summary>
public sealed class RuntimeClient : IDisposable
{
    private readonly HttpClient _http;

    /// <param name="descriptor">The runtime to ask, and the token to ask it with.</param>
    /// <param name="attempt">
    /// The attempt the environment names, or null outside a run. It is sent trimmed and unchecked as
    /// <see cref="ExecutionEnvironment.AttemptHeader"/> on every request, reads included, whatever actor a body claims;
    /// empty is the same as none. Required, so that no request is made without deciding it.
    /// </param>
    /// <param name="handler">The handler to send through, or null for the machine's own.</param>
    public RuntimeClient(RuntimeDescriptor descriptor, string? attempt, HttpMessageHandler? handler = null)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        _http = handler is null ? new HttpClient() : new HttpClient(handler, disposeHandler: false);
        _http.BaseAddress = new Uri(descriptor.BaseUrl, UriKind.Absolute);
        _http.Timeout = TimeSpan.FromSeconds(30);
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", descriptor.Token);
        if (!string.IsNullOrWhiteSpace(attempt))
        {
            _http.DefaultRequestHeaders.TryAddWithoutValidation(ExecutionEnvironment.AttemptHeader, attempt.Trim());
        }
    }

    public Task<RuntimeResponse> PostAsync(string operation, CancellationToken cancellationToken) =>
        PostAsync(operation, "{}", cancellationToken);

    public async Task<RuntimeResponse> PostAsync(string operation, string jsonBody, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(jsonBody);

        using var content = new StringContent(jsonBody, Encoding.UTF8, "application/json");
        using var response = await _http.PostAsync(Operations.Route(operation), content, cancellationToken).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        return new RuntimeResponse((int)response.StatusCode, body);
    }

    public void Dispose() => _http.Dispose();
}
