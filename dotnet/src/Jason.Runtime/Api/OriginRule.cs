using Microsoft.AspNetCore.Http;

namespace Jason.Runtime.Api;

/// <summary>
/// A request that carries an <c>Origin</c> must name this runtime's own, or it is refused. CLI and agent calls
/// carry none and pass untouched; a page from any other origin is refused here, before the bearer is read, so a
/// stolen token from a foreign page buys nothing — and the browser's own CORS preflight, which gets no answer, is
/// the first lock this rule stands behind.
/// </summary>
public static class OriginRule
{
    private static readonly HashSet<string> OwnHosts = new(StringComparer.OrdinalIgnoreCase) { "127.0.0.1", "localhost" };

    public static async Task Middleware(HttpContext context, Func<Task> next)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);

        var origin = context.Request.Headers.Origin.ToString();
        if (origin.Length > 0 && !IsOwn(origin, context.Connection.LocalPort))
        {
            await Refuse(context).ConfigureAwait(false);
            return;
        }

        await next().ConfigureAwait(false);
    }

    /// <summary><c>http://127.0.0.1:port</c> or <c>http://localhost:port</c>, and nothing else: not https, not another port, not a path, not "null".</summary>
    public static bool IsOwn(string origin, int port) =>
        Uri.TryCreate(origin, UriKind.Absolute, out var uri)
        && uri.Scheme == Uri.UriSchemeHttp
        && OwnHosts.Contains(uri.Host)
        && uri.Port == port
        && uri.AbsolutePath == "/"
        && uri.Query.Length == 0
        && uri.Fragment.Length == 0
        && !origin.TrimEnd().EndsWith('/');

    public static Task Refuse(HttpContext context) =>
        ErrorResults.WriteAsync(context, StatusCodes.Status403Forbidden, "origin_refused", "A request that names its origin must come from the runtime's own: http://127.0.0.1:<port> or http://localhost:<port>.", retryable: false);
}
