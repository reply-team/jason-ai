using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json.Nodes;

namespace Jason.PluginHost.Tests.Fixtures;

/// <summary>
/// A real HTTP server on the loopback interface, so <c>host.http</c> is tested against sockets rather than a
/// stub: every rule it enforces — hosts, schemes, caps, redirects, headers — only means something against a
/// server that actually answers. Offline by construction: nothing here leaves the machine.
/// </summary>
public sealed class LoopbackHttpServer : IDisposable
{
    private readonly HttpListener _listener = new();
    private readonly CancellationTokenSource _stopping = new();
    private readonly Task _serving;

    public LoopbackHttpServer()
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

    public int Port { get; }

    public string Host { get; }

    public string BaseUrl => string.Create(CultureInfo.InvariantCulture, $"http://{Host}:{Port}/");

    /// <summary>The server as an allowlist entry spells it: a host and its non-default port.</summary>
    public string HostKey => string.Create(CultureInfo.InvariantCulture, $"{Host}:{Port}");

    public string Url(string path) => BaseUrl.TrimEnd('/') + path;

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

    private static int FreePort()
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
            catch (HttpListenerException)
            {
                return;
            }
            catch (ObjectDisposedException)
            {
                return;
            }
            catch (InvalidOperationException)
            {
                return;
            }

            try
            {
                await AnswerAsync(context).ConfigureAwait(false);
            }
            catch (HttpListenerException)
            {
                // The client went away mid-answer; the test it belongs to has what it needed.
            }
            catch (IOException)
            {
            }
        }
    }

    private async Task AnswerAsync(HttpListenerContext context)
    {
        var request = context.Request;
        var response = context.Response;
        var path = request.Url?.AbsolutePath ?? "/";
        var query = request.Url?.Query;

        switch (path)
        {
            case "/ok":
                response.StatusCode = 200;
                response.ContentType = "application/json";
                response.Headers.Add("X-Test", "yes");
                await WriteAsync(response, "{\"ok\":true}").ConfigureAwait(false);
                return;

            case "/echo":
                {
                    var headers = new JsonObject();
                    foreach (var name in request.Headers.AllKeys)
                    {
                        if (name is not null)
                        {
                            headers[name.ToLowerInvariant()] = request.Headers[name];
                        }
                    }

                    using var reader = new StreamReader(request.InputStream, Encoding.UTF8);
                    var body = await reader.ReadToEndAsync(_stopping.Token).ConfigureAwait(false);
                    response.StatusCode = 200;
                    response.ContentType = "application/json";
                    await WriteAsync(
                        response,
                        new JsonObject
                        {
                            ["method"] = request.HttpMethod,
                            ["headers"] = headers,
                            ["body"] = body,
                        }.ToJsonString()).ConfigureAwait(false);
                    return;
                }

            case "/big":
                response.StatusCode = 200;
                response.ContentType = "text/plain";
                await WriteAsync(response, new string('x', int.Parse(QueryValue(query, "bytes") ?? "1024", CultureInfo.InvariantCulture))).ConfigureAwait(false);
                return;

            case "/slow":
                await Task.Delay(int.Parse(QueryValue(query, "ms") ?? "0", CultureInfo.InvariantCulture), _stopping.Token).ConfigureAwait(false);
                response.StatusCode = 200;
                await WriteAsync(response, "late").ConfigureAwait(false);
                return;

            case "/redirect":
                response.StatusCode = 302;
                response.Headers.Add("Location", "/ok");
                await WriteAsync(response, string.Empty).ConfigureAwait(false);
                return;

            default:
                if (path.StartsWith("/status/", StringComparison.Ordinal)
                    && int.TryParse(path["/status/".Length..], CultureInfo.InvariantCulture, out var status))
                {
                    response.StatusCode = status;
                    await WriteAsync(response, string.Empty).ConfigureAwait(false);
                    return;
                }

                response.StatusCode = 404;
                await WriteAsync(response, string.Empty).ConfigureAwait(false);
                return;
        }
    }

    private static string? QueryValue(string? query, string name)
    {
        foreach (var pair in (query ?? string.Empty).TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = pair.Split('=', 2);
            if (parts.Length == 2 && string.Equals(parts[0], name, StringComparison.Ordinal))
            {
                return parts[1];
            }
        }

        return null;
    }

    private static async Task WriteAsync(HttpListenerResponse response, string body)
    {
        var bytes = Encoding.UTF8.GetBytes(body);
        response.ContentLength64 = bytes.Length;
        await response.OutputStream.WriteAsync(bytes).ConfigureAwait(false);
        response.OutputStream.Close();
    }
}
