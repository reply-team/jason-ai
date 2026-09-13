using Jason.Runtime.Discovery;
using Microsoft.AspNetCore.Http;

namespace Jason.Runtime.Api;

/// <summary>Every request carries the capability token from the descriptor. Comparison is constant-time.</summary>
public static class BearerTokenAuthentication
{
    private const string Scheme = "Bearer ";

    public static Func<HttpContext, Func<Task>, Task> Middleware(string token) => async (context, next) =>
    {
        var header = context.Request.Headers.Authorization.ToString();
        var presented = header.StartsWith(Scheme, StringComparison.Ordinal) ? header[Scheme.Length..].Trim() : null;
        if (presented is null || !CapabilityToken.Matches(presented, token))
        {
            context.Response.Headers.WWWAuthenticate = "Bearer";
            await ErrorResults.WriteAsync(context, StatusCodes.Status401Unauthorized, "unauthorized", "A valid capability token is required: Authorization: Bearer <token> from the runtime descriptor.", retryable: false).ConfigureAwait(false);
            return;
        }

        await next().ConfigureAwait(false);
    };
}
