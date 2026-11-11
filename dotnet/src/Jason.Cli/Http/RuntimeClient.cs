using System.Net;
using System.Net.Http.Headers;
using System.Net.WebSockets;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
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
    /// empty is the same as none. A value that is not all visible ASCII is not sent, and <paramref name="error"/> is
    /// told once. Required, so that no request is made without deciding it.
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
            // Not a check of the value's shape: a value that is not all visible ASCII is not sent, since a non-ASCII
            // character would fail the request before it leaves and a line break would start a header of its own. The
            // request goes as it would with the variable unset, and the line names the variable, never what it holds.
            error.WriteLine(
                $"{ExecutionEnvironment.AttemptIdVariable} holds a value that is not all visible ASCII, so it is not sent and the runtime is not told which attempt this runs inside.");
        }
    }

    /// <summary>The stderr writers already told, so a verb that makes more than one client says it once.</summary>
    private static readonly ConditionalWeakTable<TextWriter, object> Said = [];

    /// <summary>Visible ASCII only, 0x21 to 0x7E: no space, no line break and no character outside ASCII.</summary>
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

    /// <summary>
    /// Opens a stream of the runtime's: a WebSocket upgrade of <paramref name="pathAndQuery"/> under
    /// <paramref name="subprotocol"/>, made with this client's headers, the bearer among them, as every request is. The
    /// upgrade is made here rather than by <see cref="ClientWebSocket"/> so that a refusal comes back as the runtime wrote
    /// it, status and body, as an operation's does.
    /// </summary>
    public async Task<RuntimeStream> OpenStreamAsync(string pathAndQuery, string subprotocol, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(pathAndQuery);
        ArgumentNullException.ThrowIfNull(subprotocol);

        var key = Convert.ToBase64String(RandomNumberGenerator.GetBytes(16));
        using var request = new HttpRequestMessage(HttpMethod.Get, pathAndQuery)
        {
            Version = HttpVersion.Version11,
            VersionPolicy = HttpVersionPolicy.RequestVersionExact,
        };
        request.Headers.Connection.Add("Upgrade");
        request.Headers.Upgrade.Add(new ProductHeaderValue("websocket"));
        request.Headers.TryAddWithoutValidation("Sec-WebSocket-Version", "13");
        request.Headers.TryAddWithoutValidation("Sec-WebSocket-Key", key);
        request.Headers.TryAddWithoutValidation("Sec-WebSocket-Protocol", subprotocol);

        var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        try
        {
            if (response.StatusCode != HttpStatusCode.SwitchingProtocols)
            {
                var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                return new RuntimeStream(null, new RuntimeResponse((int)response.StatusCode, body), null);
            }

            // What makes the answer an upgrade of this request and not some other server's 101: the key, answered.
            if (!response.Headers.TryGetValues("Sec-WebSocket-Accept", out var accepts)
                || accepts.SingleOrDefault() != Accept(key)
                || !response.Headers.TryGetValues("Sec-WebSocket-Protocol", out var chosen)
                || chosen.SingleOrDefault() != subprotocol)
            {
                throw new HttpRequestException("The answer to the upgrade was not this stream's.");
            }

            var connection = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            var socket = WebSocket.CreateFromStream(connection, new WebSocketCreationOptions { IsServer = false, SubProtocol = subprotocol });
            var opened = new RuntimeStream(socket, null, response);
            response = null;
            return opened;
        }
        finally
        {
            response?.Dispose();
        }
    }

    /// <summary>RFC 6455's answer to a key: the SHA-1 of the key and the protocol's own GUID, in base64.</summary>
    private static string Accept(string key) =>
        Convert.ToBase64String(SHA1.HashData(Encoding.ASCII.GetBytes(key + "258EAFA5-E914-47DA-95CA-C5AB0DC85B11")));

    public void Dispose() => _http.Dispose();
}

/// <summary>
/// A stream the runtime opened, or the answer it refused it with: <see cref="Socket"/> or <see cref="Refusal"/>, never
/// both. Disposing it lets the connection go.
/// </summary>
public sealed class RuntimeStream(WebSocket? socket, RuntimeResponse? refusal, IDisposable? connection) : IDisposable
{
    public WebSocket? Socket { get; } = socket;

    public RuntimeResponse? Refusal { get; } = refusal;

    public void Dispose()
    {
        Socket?.Dispose();
        connection?.Dispose();
    }
}
