using Microsoft.AspNetCore.Http;

namespace Jason.Runtime.Api;

/// <summary>The API is loopback-only; a request that names any other host (DNS rebinding) is refused.</summary>
public static class LoopbackHostFilter
{
    private static readonly HashSet<string> AllowedHosts = new(StringComparer.OrdinalIgnoreCase) { "127.0.0.1", "localhost" };

    public static async Task Middleware(HttpContext context, Func<Task> next)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);

        if (!AllowedHosts.Contains(context.Request.Host.Host))
        {
            await ErrorResults.WriteAsync(context, StatusCodes.Status400BadRequest, "invalid_request", "Requests must address the runtime as 127.0.0.1 or localhost.", retryable: false).ConfigureAwait(false);
            return;
        }

        await next().ConfigureAwait(false);
    }
}
