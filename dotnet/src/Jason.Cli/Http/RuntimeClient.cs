using System.Net;
using System.Net.Http.Headers;
using System.Net.WebSockets;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using Jason.Contracts.Api;
using Jason.Contracts.Discovery;
using Jason.Contracts.Execution;
using Jason.Contracts.Operations;

namespace Jason.Cli.Http;

public sealed record RuntimeResponse(int StatusCode, string Body)
{
    public bool IsSuccess => StatusCode is >= 200 and < 300;
}

/// <summary>
/// The runtime took the request and did not answer within its budget: it is reachable, and may still be working. It is a
/// <see cref="TaskCanceledException"/>, as the end of a request's wait always was, so a verb that reads a wait run out as
/// the runtime not answering still does; the operation runner tells it apart by its type.
/// </summary>
public sealed class RuntimeTimeoutException(string operation, TimeSpan budget)
    : TaskCanceledException($"'{operation}' was not answered within {budget.TotalSeconds:0} seconds.")
{
    public string Operation { get; } = operation;

    public TimeSpan Budget { get; } = budget;
}

/// <summary>
/// Thin HTTP client over the descriptor: base URL and bearer token come from it, and the attempt the CLI runs inside, when
/// it runs inside one, is named on every request beside the token.
/// </summary>
public sealed class RuntimeClient : IDisposable
{
    private readonly HttpClient _http;

    private readonly TimeProvider _clock;

    /// <summary>What a request waits beyond its operation's own budget: the runtime's work around the plugin's call.</summary>
    public static readonly TimeSpan Margin = TimeSpan.FromSeconds(30);

    /// <summary>What an operation the catalog does not publish is waited for, and the least any request is.</summary>
    public static readonly TimeSpan Ordinary = TimeSpan.FromSeconds(30);

    /// <summary>
    /// How long the CLI waits for the runtime's answer once the request is sent: an operation the catalog publishes, such
    /// as a campaign's read of an audience, is allowed its own <c>timeout_ms</c>, and the runtime's work around the
    /// plugin's call needs <see cref="Margin"/> beside it; anything else is <see cref="Ordinary"/>.
    /// </summary>
    public static TimeSpan Budget(string operation)
    {
        var own = OperationCatalog.Find(operation) is { } contract ? TimeSpan.FromMilliseconds(contract.TimeoutMs) + Margin : Ordinary;
        return own > Ordinary ? own : Ordinary;
    }

    /// <param name="descriptor">The runtime to ask, and the token to ask it with.</param>
    /// <param name="attempt">
    /// The attempt the environment names, or null outside a run. It is sent trimmed, and unchecked in its shape, as
    /// <see cref="ExecutionEnvironment.AttemptHeader"/> on every request, reads included, whatever actor a body claims;
    /// empty is the same as none. A value that is not all visible ASCII is not sent, and <paramref name="error"/> is
    /// told once. Required, so that no request is made without deciding it.
    /// </param>
    /// <param name="error">The CLI's stderr, where it says that an attempt could not be named.</param>
    /// <param name="handler">
    /// The handler to send through, or null for the machine's own, which gives up connecting after
    /// <see cref="ConnectTimeout"/>: a runtime that cannot be reached is told apart from one that took the request.
    /// </param>
    /// <param name="clock">What a request's budget is counted on; the system's clock where none is given.</param>
    public RuntimeClient(RuntimeDescriptor descriptor, string? attempt, TextWriter error, HttpMessageHandler? handler = null, TimeProvider? clock = null)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        ArgumentNullException.ThrowIfNull(error);
        _clock = clock ?? TimeProvider.System;
        _http = handler is null ? new HttpClient(new SocketsHttpHandler { ConnectTimeout = ConnectTimeout }) : new HttpClient(handler, disposeHandler: false);
        _http.BaseAddress = new Uri(descriptor.BaseUrl, UriKind.Absolute);

        // Each request is bounded by its own operation's budget, below; the client's one bound would cut them all alike.
        _http.Timeout = Timeout.InfiniteTimeSpan;
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

    /// <summary>How long connecting to the runtime may take before it counts as unreachable: the loopback answers at once.</summary>
    public static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(10);

    /// <summary>The stderr writers already told, so a verb that makes more than one client says it once.</summary>
    private static readonly ConditionalWeakTable<TextWriter, object> Said = [];

    /// <summary>Visible ASCII only, 0x21 to 0x7E: no space, no line break and no character outside ASCII.</summary>
    private static bool Travels(string value) => value.All(character => character is >= '!' and <= '~');

    public Task<RuntimeResponse> PostAsync(string operation, CancellationToken cancellationToken) =>
        PostAsync(operation, "{}", cancellationToken);

    /// <summary>
    /// Sends the operation and reads its answer within <see cref="Budget"/>; a wait that runs out throws
    /// <see cref="RuntimeTimeoutException"/>, a connection that cannot be made throws as the handler does.
    /// </summary>
    public async Task<RuntimeResponse> PostAsync(string operation, string jsonBody, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(jsonBody);

        var budget = Budget(operation);
        using var deadline = new CancellationTokenSource(budget, _clock);
        using var bounded = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, deadline.Token);
        try
        {
            using var content = new StringContent(jsonBody, Encoding.UTF8, "application/json");
            using var response = await _http.PostAsync(Operations.Route(operation), content, bounded.Token).ConfigureAwait(false);
            var body = await response.Content.ReadAsStringAsync(bounded.Token).ConfigureAwait(false);
            return new RuntimeResponse((int)response.StatusCode, body);
        }
        catch (OperationCanceledException) when (deadline.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            throw new RuntimeTimeoutException(operation, budget);
        }
    }

    /// <summary>
    /// Opens a stream of the runtime's: a WebSocket upgrade of <paramref name="pathAndQuery"/> under
    /// <paramref name="subprotocol"/>, made with this client's headers, the bearer among them, as every request is. The
    /// upgrade is made here rather than by <see cref="ClientWebSocket"/> so that a refusal comes back as the runtime wrote
    /// it, status and body, as an operation's does. The answer to the upgrade is waited for <see cref="Ordinary"/> and no
    /// longer, as a request with no budget of its own is; the stream it opens runs as long as it is read.
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

        using var deadline = new CancellationTokenSource(Ordinary, _clock);
        using var bounded = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, deadline.Token);
        var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, bounded.Token).ConfigureAwait(false);
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
