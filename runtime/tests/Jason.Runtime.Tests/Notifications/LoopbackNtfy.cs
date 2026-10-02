using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Jason.Runtime.Tests.Notifications;

/// <summary>
/// An ntfy server on the loopback interface, as far as a plugin can tell: it records every request it was sent and
/// answers each with the status and body the test set. Shaped like the plugin host tests' loopback server, which lives
/// in another test project.
/// </summary>
internal sealed class LoopbackNtfy : IDisposable
{
    private readonly HttpListener _listener = new();
    private readonly CancellationTokenSource _stopping = new();
    private readonly ConcurrentQueue<Received> _requests = new();
    private readonly Task _serving;

    public LoopbackNtfy()
    {
        Port = FreePort();
        Host = "127.0.0.1";
        _listener.Prefixes.Add(BaseUrl);
        try
        {
            _listener.Start();
        }
        catch (HttpListenerException)
        {
            // Windows reserves 'localhost' for every user but not always a literal address; either is loopback.
            _listener.Prefixes.Clear();
            Host = "localhost";
            _listener.Prefixes.Add(BaseUrl);
            _listener.Start();
        }

        _serving = Task.Run(ServeAsync);
    }

    /// <summary>One request as the server saw it: the method, the path, every header and the body.</summary>
    public sealed record Received(string Method, string Path, IReadOnlyDictionary<string, string> Headers, string Body);

    public int Port { get; }

    public string Host { get; }

    /// <summary>The server's root with a trailing slash, as a listener prefix is written.</summary>
    public string BaseUrl => string.Create(CultureInfo.InvariantCulture, $"http://{Host}:{Port}/");

    /// <summary>The server as a binding names it: scheme, host and port, nothing after.</summary>
    public string Server => BaseUrl.TrimEnd('/');

    /// <summary>The host as a grant and a manifest name it.</summary>
    public string HostKey => string.Create(CultureInfo.InvariantCulture, $"{Host}:{Port}");

    public IReadOnlyList<Received> Requests => [.. _requests];

    public int Status { get; set; } = 200;

    public string Body { get; set; } = """{"id":"abc","event":"message","topic":"jason-test"}""";

    public void Dispose()
    {
        _stopping.Cancel();
        _listener.Close();
        try
        {
            _serving.Wait(TimeSpan.FromSeconds(5));
        }
        catch (AggregateException)
        {
            // The listener was closed under it; that is how this server stops.
        }

        _stopping.Dispose();
    }

    /// <summary>A port nothing listens on, for the server that is not there.</summary>
    public static int FreePort()
    {
        using var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        var port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        return port;
    }

    private async Task ServeAsync()
    {
        while (!_stopping.IsCancellationRequested)
        {
            HttpListenerContext context;
            try
            {
                context = await _listener.GetContextAsync().ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is HttpListenerException or ObjectDisposedException or InvalidOperationException)
            {
                return;
            }

            try
            {
                var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var name in context.Request.Headers.AllKeys)
                {
                    if (name is not null)
                    {
                        headers[name] = context.Request.Headers[name] ?? string.Empty;
                    }
                }

                using var reader = new StreamReader(context.Request.InputStream, Encoding.UTF8);
                var body = await reader.ReadToEndAsync(_stopping.Token).ConfigureAwait(false);
                _requests.Enqueue(new Received(context.Request.HttpMethod, context.Request.Url?.AbsolutePath ?? "/", headers, body));

                var bytes = Encoding.UTF8.GetBytes(Body);
                context.Response.StatusCode = Status;
                context.Response.ContentType = "application/json";
                context.Response.ContentLength64 = bytes.Length;
                await context.Response.OutputStream.WriteAsync(bytes, _stopping.Token).ConfigureAwait(false);
                context.Response.Close();
            }
            catch (Exception ex) when (ex is HttpListenerException or IOException or OperationCanceledException)
            {
                // The client went away mid-answer; the test it belongs to has what it needed.
            }
        }
    }
}
