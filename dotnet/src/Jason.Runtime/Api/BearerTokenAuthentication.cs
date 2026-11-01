using Jason.Runtime.Discovery;
using Jason.Runtime.Ui;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Jason.Runtime.Api;

/// <summary>
/// Every request carries a bearer: the capability token from the descriptor, or a UI token a browser paired for.
/// Comparison with the run token is constant-time; a UI token is looked up by its hash. The UI's own paths carry
/// none and are exempt — that is where a browser gets the page and trades its ticket in.
/// </summary>
public static class BearerTokenAuthentication
{
    private const string Scheme = "Bearer ";

    public static Func<HttpContext, Func<Task>, Task> Middleware(string token) => async (context, next) =>
    {
        if (UiPaths.IsUi(context.Request.Path))
        {
            await next().ConfigureAwait(false);
            return;
        }

        var header = context.Request.Headers.Authorization.ToString();
        var presented = header.StartsWith(Scheme, StringComparison.Ordinal) ? header[Scheme.Length..].Trim() : null;
        if (presented is not null && (CapabilityToken.Matches(presented, token) || await IsUiTokenAsync(context, presented).ConfigureAwait(false)))
        {
            await next().ConfigureAwait(false);
            return;
        }

        context.Response.Headers.WWWAuthenticate = "Bearer";
        await ErrorResults.WriteAsync(context, StatusCodes.Status401Unauthorized, "unauthorized", "A valid capability token is required: Authorization: Bearer <token> from the runtime descriptor.", retryable: false).ConfigureAwait(false);
    };

    private static Task<bool> IsUiTokenAsync(HttpContext context, string presented)
    {
        var store = context.RequestServices.GetService<UiTokenStore>();
        return store is null ? Task.FromResult(false) : store.MatchesAsync(presented, context.RequestAborted);
    }
}
