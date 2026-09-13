using System.Net.Http.Headers;
using System.Text;
using Jason.Contracts.Api;
using Jason.Contracts.Discovery;

namespace Jason.Cli.Http;

public sealed record RuntimeResponse(int StatusCode, string Body)
{
    public bool IsSuccess => StatusCode is >= 200 and < 300;
}

/// <summary>Thin HTTP client over the descriptor: base URL and bearer token come from it, nothing else.</summary>
public sealed class RuntimeClient : IDisposable
{
    private readonly HttpClient _http;

    public RuntimeClient(RuntimeDescriptor descriptor, HttpMessageHandler? handler = null)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        _http = handler is null ? new HttpClient() : new HttpClient(handler, disposeHandler: false);
        _http.BaseAddress = new Uri(descriptor.BaseUrl, UriKind.Absolute);
        _http.Timeout = TimeSpan.FromSeconds(30);
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", descriptor.Token);
    }

    public async Task<RuntimeResponse> PostAsync(string operation, CancellationToken cancellationToken)
    {
        using var content = new StringContent("{}", Encoding.UTF8, "application/json");
        using var response = await _http.PostAsync(Operations.Route(operation), content, cancellationToken).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        return new RuntimeResponse((int)response.StatusCode, body);
    }

    public void Dispose() => _http.Dispose();
}
