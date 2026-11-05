using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
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
    /// The attempt the environment names, or null outside a run. It is sent trimmed, and unchecked in its shape, as
    /// <see cref="ExecutionEnvironment.AttemptHeader"/> on every request, reads included, whatever actor a body claims;
    /// empty is the same as none. A value a header cannot carry, anything but visible ASCII, is not sent, and
    /// <paramref name="error"/> is told once. Required, so that no request is made without deciding it.
    /// </param>
    /// <param name="error">The CLI's stderr, where it says that an attempt could not be named.</param>
    /// <param name="handler">The handler to send through, or null for the machine's own.</param>
    public RuntimeClient(RuntimeDescriptor descriptor, string? attempt, TextWriter error, HttpMessageHandler? handler = null)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        ArgumentNullException.ThrowIfNull(error);
        _http = handler is null ? new HttpClient() : new HttpClient(handler, disposeHandler: false);
        _http.BaseAddress = new Uri(descriptor.BaseUrl, UriKind.Absolute);
        _http.Timeout = TimeSpan.FromSeconds(30);
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", descriptor.Token);
        if (string.IsNullOrWhiteSpace(attempt))
        {
            return;
        }

        var value = attempt.Trim();
        if (Travels(value))
        {
            _http.DefaultRequestHeaders.TryAddWithoutValidation(ExecutionEnvironment.AttemptHeader, value);
        }
        else if (Said.TryAdd(error, Said))
        {
            // Not a check of the value's shape but of the wire: a character outside visible ASCII fails the request
            // before it leaves, and a line break would start a header of its own. The request goes as it would with the
            // variable unset, and the line names the variable, never what it holds.
            error.WriteLine(
                $"{ExecutionEnvironment.AttemptIdVariable} holds a value that cannot travel as a header (only visible ASCII can), so the runtime is not told which attempt this runs inside.");
        }
    }

    /// <summary>The stderr writers already told, so a verb that makes more than one client says it once.</summary>
    private static readonly ConditionalWeakTable<TextWriter, object> Said = [];

    /// <summary>Visible ASCII only, 0x21 to 0x7E: what a header value carries whole, with no space and no line break.</summary>
    private static bool Travels(string value) => value.All(character => character is >= '!' and <= '~');

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
