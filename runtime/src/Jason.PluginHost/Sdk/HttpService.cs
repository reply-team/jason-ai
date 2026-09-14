using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using Jason.Contracts;
using Jason.Contracts.Plugins;
using Jint;
using Jint.Native;

namespace Jason.PluginHost.Sdk;

/// <summary>
/// <c>host.http</c>: one request to a host the manifest declared and the user granted, over HTTPS or loopback,
/// with nothing the runtime adds of its own — no credentials, no cookies, no redirects followed. A 3xx comes
/// back to the plugin as an answer; a plugin that wants to follow it issues a new request, which passes the
/// same allowlist, so "no cross-host redirects" is true by construction rather than by policy.
/// </summary>
public sealed class HttpService(HostServices services) : IDisposable
{
    public const string Function = "host.http";

    public const int MaxHeaders = 32;
    public const int MaxHeaderValueBytes = 8192;
    public const int MaxUrlLength = 4096;
    public const int MaxMethodLength = 16;

    /// <summary>What the connection itself owns; a plugin that sets one of these is describing another request.</summary>
    public static IReadOnlySet<string> ForbiddenHeaders { get; } =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Host", "Content-Length", "Transfer-Encoding", "Connection" };

    public static IReadOnlySet<string> Allowed { get; } =
        new HashSet<string>(StringComparer.Ordinal) { "method", "url", "headers", "body", "timeout_ms" };

    public static IReadOnlySet<string> Methods { get; } = new HashSet<string>(StringComparer.Ordinal) { "GET", "POST" };

    private static readonly string[] LoopbackHosts = ["localhost", "127.0.0.1", "::1", "[::1]"];

    private HttpClient? _client;

    /// <summary>The one thing the host says about itself, and the only header it adds.</summary>
    public static string UserAgent => "jason-plugin-host/" + JasonVersion.Current;

    public static bool IsLoopback(Uri uri)
    {
        ArgumentNullException.ThrowIfNull(uri);
        return LoopbackHosts.Contains(uri.Host, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>A URL's host as an allowlist entry spells it: the port only when it is not the scheme's default.</summary>
    public static string HostKey(Uri uri)
    {
        ArgumentNullException.ThrowIfNull(uri);
        return uri.IsDefaultPort
            ? uri.Host.ToLowerInvariant()
            : string.Create(CultureInfo.InvariantCulture, $"{uri.Host.ToLowerInvariant()}:{uri.Port}");
    }

    public static bool IsAllowed(Uri uri, IReadOnlyList<string> grantedHosts)
    {
        ArgumentNullException.ThrowIfNull(grantedHosts);
        var key = HostKey(uri);
        return grantedHosts.Contains(key, StringComparer.OrdinalIgnoreCase);
    }

    public JsValue Invoke(Engine engine, JsValue[] args)
    {
        ArgumentNullException.ThrowIfNull(engine);

        var options = ArgumentReader.Options(engine, args, Function, Allowed);
        var method = ArgumentReader.RequiredString(engine, options, "method", Function, MaxMethodLength);
        var address = ArgumentReader.RequiredString(engine, options, "url", Function, MaxUrlLength);
        if (!Uri.TryCreate(address, UriKind.Absolute, out var url))
        {
            throw ArgumentReader.TypeError(engine, $"{Function}: 'url' must be an absolute URL.");
        }

        var granted = services.Grants.Http;
        if (granted is null)
        {
            throw new HostRuleException(
                OutcomeCodes.CapabilityNotGranted,
                "This plugin was not granted the http capability.",
                new JsonObject { ["capability"] = "http", ["requested"] = HostKey(url) });
        }

        if (!Methods.Contains(method))
        {
            throw new HostRuleException(
                OutcomeCodes.HttpMethodNotAllowed,
                $"'{method}' is not a method a plugin may use; only GET and POST are.",
                new JsonObject { ["method"] = method });
        }

        if (!string.Equals(url.Scheme, Uri.UriSchemeHttps, StringComparison.Ordinal)
            && !(string.Equals(url.Scheme, Uri.UriSchemeHttp, StringComparison.Ordinal) && IsLoopback(url)))
        {
            throw new HostRuleException(
                OutcomeCodes.HttpSchemeNotAllowed,
                "A request must use https, or http to a loopback address.",
                new JsonObject { ["scheme"] = url.Scheme, ["host"] = HostKey(url) });
        }

        if (!IsAllowed(url, granted.Hosts))
        {
            throw new HostRuleException(
                OutcomeCodes.HttpHostNotAllowed,
                $"'{HostKey(url)}' is not one of the hosts this plugin was granted.",
                new JsonObject { ["host"] = HostKey(url) });
        }

        var headers = ReadHeaders(engine, options);
        var body = ReadBody(engine, options);
        var timeout = Budget(engine, options);

        services.Budget.Http();
        return JsJson.FromJson(engine, Send(method, url, headers, body, timeout));
    }

    public void Dispose() => _client?.Dispose();

    private static IReadOnlyList<KeyValuePair<string, string>> ReadHeaders(Engine engine, JsonObject options)
    {
        var headers = ArgumentReader.OptionalStringMap(engine, options, "headers", Function, MaxHeaders, MaxHeaderValueBytes);
        foreach (var (name, _) in headers)
        {
            if (ForbiddenHeaders.Contains(name))
            {
                throw new HostRuleException(
                    OutcomeCodes.HttpHeaderNotAllowed,
                    $"'{name}' belongs to the connection and cannot be set by a plugin.",
                    new JsonObject { ["header"] = name });
            }
        }

        return headers;
    }

    private string? ReadBody(Engine engine, JsonObject options)
    {
        var body = ArgumentReader.OptionalString(engine, options, "body", Function, int.MaxValue);
        if (body is not null && Encoding.UTF8.GetByteCount(body) > services.Limits.Http.RequestBytes)
        {
            throw new HostRuleException(
                OutcomeCodes.HttpLimit,
                string.Create(CultureInfo.InvariantCulture, $"A request body is at most {services.Limits.Http.RequestBytes} bytes."),
                new JsonObject { ["limit"] = "request_bytes", ["max"] = services.Limits.Http.RequestBytes });
        }

        return body;
    }

    private TimeSpan Budget(Engine engine, JsonObject options)
    {
        var remaining = services.Remaining();
        var asked = ArgumentReader.OptionalInt(engine, options, "timeout_ms", Function, 1, services.Limits.TimeoutMs);
        var wanted = TimeSpan.FromMilliseconds(asked ?? services.Limits.Http.TimeoutMs);
        return wanted < remaining ? wanted : remaining;
    }

    private JsonObject Send(
        string method,
        Uri url,
        IReadOnlyList<KeyValuePair<string, string>> headers,
        string? body,
        TimeSpan timeout)
    {
        var watch = System.Diagnostics.Stopwatch.StartNew();
        var client = _client ??= CreateClient();

        using var request = new HttpRequestMessage(new HttpMethod(method), url);
        if (body is not null)
        {
            var content = new StringContent(body, Encoding.UTF8);

            // StringContent labels itself text/plain; the plugin's own Content-Type, if it set one, decides.
            content.Headers.ContentType = null;
            request.Content = content;
        }

        foreach (var (name, value) in headers)
        {
            if (!request.Headers.TryAddWithoutValidation(name, value))
            {
                request.Content?.Headers.TryAddWithoutValidation(name, value);
            }
        }

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(services.Deadline);
        deadline.CancelAfter(timeout);

        try
        {
            using var response = client
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, deadline.Token)
                .GetAwaiter()
                .GetResult();

            var capture = new BoundedCapture(services.Limits.Http.ResponseBytes);
            using (var stream = response.Content.ReadAsStream(deadline.Token))
            {
                capture.DrainAsync(stream, deadline.Token).GetAwaiter().GetResult();
            }

            services.Diagnostics.Host(
                "info",
                "http",
                new JsonObject
                {
                    ["method"] = method,
                    ["scheme"] = url.Scheme,
                    ["host"] = HostKey(url),
                    ["path"] = url.AbsolutePath,
                    ["status"] = (int)response.StatusCode,
                    ["duration_ms"] = watch.ElapsedMilliseconds,
                });

            return new JsonObject
            {
                ["status"] = (int)response.StatusCode,
                ["headers"] = Headers(response),
                ["body"] = capture.Text,
                ["truncated"] = capture.Truncated,
                ["duration_ms"] = watch.ElapsedMilliseconds,
            };
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or IOException)
        {
            // A request that never completed is not a rule the plugin broke: it is told what happened and
            // decides for itself whether that is transient, ambiguous or final.
            services.Diagnostics.Host(
                "warn",
                "http_failed",
                new JsonObject
                {
                    ["method"] = method,
                    ["scheme"] = url.Scheme,
                    ["host"] = HostKey(url),
                    ["path"] = url.AbsolutePath,
                    ["duration_ms"] = watch.ElapsedMilliseconds,
                });

            return new JsonObject
            {
                ["status"] = 0,
                ["headers"] = new JsonObject(),
                ["body"] = string.Empty,
                ["truncated"] = false,
                ["duration_ms"] = watch.ElapsedMilliseconds,
                ["error"] = ex.Message,
            };
        }
    }

    private static HttpClient CreateClient()
    {
        var handler = new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            UseCookies = false,
        };

        var client = new HttpClient(handler, disposeHandler: true)
        {
            // Every request carries its own deadline; the client's own timeout would only fight with it.
            Timeout = Timeout.InfiniteTimeSpan,
        };

        client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", UserAgent);
        return client;
    }

    private static JsonObject Headers(HttpResponseMessage response)
    {
        var headers = new JsonObject();
        foreach (var (name, values) in response.Headers.Concat<KeyValuePair<string, IEnumerable<string>>>(response.Content.Headers))
        {
            headers[name.ToLowerInvariant()] = string.Join(", ", values);
        }

        return headers;
    }
}
