using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Serilog.Events;

namespace Jason.Runtime.Logging;

/// <summary>
/// The fields of the line the host writes for every request, in closed words. A request's path and method are
/// whatever a local client chose to send, and the line is written for every request — before the host it named is
/// checked and before its token is — so as sent they would carry anything at all into the log. The path is said as
/// the route of the operation the request matched, which only the code writes, or as <see cref="Unmatched"/>; the
/// method as one of the two the API uses, or as <see cref="Other"/>.
/// </summary>
internal static class RequestLine
{
    /// <summary>The path of a request that matched no operation: the catch-all answered it.</summary>
    internal const string Unmatched = "unmatched";

    /// <summary>The method of a request that used one the API does not.</summary>
    internal const string Other = "other";

    /// <summary>The properties the request line's template names, filled from what the request matched.</summary>
    internal static IEnumerable<LogEventProperty> Properties(HttpContext context, double elapsedMs, int statusCode)
    {
        ArgumentNullException.ThrowIfNull(context);
        return
        [
            new LogEventProperty("RequestMethod", new ScalarValue(Method(context.Request.Method))),
            new LogEventProperty("RequestPath", new ScalarValue(Route(context.GetEndpoint()))),
            new LogEventProperty("StatusCode", new ScalarValue(statusCode)),
            new LogEventProperty("Elapsed", new ScalarValue(elapsedMs)),
        ];
    }

    /// <summary>
    /// The route the request matched, where it is a literal one: every operation's route is, and a literal route is
    /// text the code wrote. The catch-all has a parameter, and a parameter is text the request supplies.
    /// </summary>
    internal static string Route(Endpoint? endpoint) =>
        endpoint is RouteEndpoint { RoutePattern: { Parameters.Count: 0, RawText: { } route } } ? route : Unmatched;

    /// <summary>
    /// Every operation is invoked with POST and the journal's stream is opened with GET, a WebSocket upgrade being a
    /// GET by its protocol; any other method is one word, whatever the client sent.
    /// </summary>
    internal static string Method(string method) =>
        HttpMethods.IsPost(method) ? HttpMethods.Post
        : HttpMethods.IsGet(method) ? HttpMethods.Get
        : Other;
}
