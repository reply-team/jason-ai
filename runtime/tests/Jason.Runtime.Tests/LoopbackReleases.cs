using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Net.Sockets;

namespace Jason.Runtime.Tests;

/// <summary>
/// The product's own address and the store behind it, on the loopback interface. Every request under
/// <c>/releases/</c> is answered as the address answers it: 302, with the same path under <c>/store</c>. Under
/// <c>/store/</c> the files of one directory are served by their names. Offline by construction: nothing here leaves
/// the machine.
/// </summary>
/// <remarks>
/// It records every request it was sent, in order, as its method and path, so a test can say that a client asked the
/// address first and then followed it, file by file.
/// </remarks>
public sealed class LoopbackReleases : IDisposable
{
    /// <summary>How many ports it tries before it gives up: each one was free a moment before it was tried.</summary>
    private const int Attempts = 8;

    private readonly string _directory;
    private readonly HttpListener _listener;
    private readonly CancellationTokenSource _stopping = new();
    private readonly ConcurrentQueue<string> _asked = new();
    private readonly Task _serving;

    /// <param name="directory">What the store holds: a release's files, by their names.</param>
    public LoopbackReleases(string directory)
    {
        _directory = directory;

        // A free port is found by binding one and letting it go, so another test can take it before this listens;
        // a start that fails closes its listener, so every try is a new one. Windows reserves 'localhost' for every
        // user but not always a literal address; either is loopback.
        for (var attempt = 1; ; attempt++)
        {
            var port = FreePort();
            foreach (var host in (string[])["127.0.0.1", "localhost"])
            {
                var listener = new HttpListener();
                listener.Prefixes.Add(string.Create(CultureInfo.InvariantCulture, $"http://{host}:{port}/"));
                try
                {
                    listener.Start();
                    (_listener, Server) = (listener, string.Create(CultureInfo.InvariantCulture, $"http://{host}:{port}"));
                    _serving = Task.Run(ServeAsync);
                    return;
                }
                catch (HttpListenerException) when (attempt < Attempts || host != "localhost")
                {
                    listener.Close();
                }
            }
        }
    }

    /// <summary>The scheme, host and port, with nothing after.</summary>
    public string Server { get; }

    /// <summary>The address's releases base, as <c>ProductAddress.Releases</c> is the real one's.</summary>
    public string Releases => Server + "/releases";

    /// <summary>Every request so far, in order, as <c>GET /path</c>.</summary>
    public IReadOnlyList<string> Asked => [.. _asked];

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
            catch (Exception error) when (error is HttpListenerException or ObjectDisposedException or InvalidOperationException)
            {
                return;
            }

            try
            {
                await AnswerAsync(context).ConfigureAwait(false);
            }
            catch (Exception error) when (error is HttpListenerException or IOException)
            {
                // The client went away mid-answer; the test it belongs to has what it needed.
            }
        }
    }

    private async Task AnswerAsync(HttpListenerContext context)
    {
        var path = context.Request.Url?.AbsolutePath ?? "/";
        _asked.Enqueue($"{context.Request.HttpMethod} {path}");
        var response = context.Response;

        if (path.StartsWith("/releases/", StringComparison.Ordinal))
        {
            response.StatusCode = (int)HttpStatusCode.Found;
            response.RedirectLocation = Server + "/store" + path;
            response.ContentLength64 = 0;
            response.OutputStream.Close();
            return;
        }

        var file = path.StartsWith("/store/", StringComparison.Ordinal) ? Path.Combine(_directory, Path.GetFileName(path)) : null;
        if (file is null || !File.Exists(file))
        {
            response.StatusCode = (int)HttpStatusCode.NotFound;
            response.ContentLength64 = 0;
            response.OutputStream.Close();
            return;
        }

        var bytes = await File.ReadAllBytesAsync(file, _stopping.Token).ConfigureAwait(false);
        response.StatusCode = (int)HttpStatusCode.OK;
        response.ContentType = "application/octet-stream";
        response.ContentLength64 = bytes.Length;
        await response.OutputStream.WriteAsync(bytes, _stopping.Token).ConfigureAwait(false);
        response.OutputStream.Close();
    }
}
