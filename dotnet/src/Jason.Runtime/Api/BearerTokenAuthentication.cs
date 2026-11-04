using Jason.Contracts.Api;
using Jason.Runtime.Discovery;
using Jason.Runtime.Plugins.Door;
using Jason.Runtime.Ui;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Jason.Runtime.Api;

/// <summary>
/// Every request carries a bearer: the capability token from the descriptor, a UI token a browser paired for, or a
/// handler capability the runtime minted for one plugin invocation. Comparison with the run token is constant-time; a
/// UI token and a handler capability are looked up by their hash. The UI's own paths carry none and are exempt — that
/// is where a browser gets the page and trades its ticket in.
/// </summary>
/// <remarks>
/// A handler capability is narrower than the other two: it opens only the operations it was minted for, so any other
/// path it is presented on, an operation outside its set or no operation at all, is refused here with
/// <c>operation_not_granted</c> before a handler runs.
/// </remarks>
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

        if (presented is not null && IsHandlerCapability(context, presented, out var grant))
        {
            if (!Opens(grant, context.Request.Path))
            {
                await ErrorResults.WriteAsync(context, StatusCodes.Status403Forbidden, "operation_not_granted", "This handler's capability does not open this operation; the plugin's api grant decides which it opens.", retryable: false).ConfigureAwait(false);
                return;
            }

            context.Features.Set(new HandlerCaller(grant));
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

    private static bool IsHandlerCapability(HttpContext context, string presented, out DoorGrant grant)
    {
        grant = null!;
        var capabilities = context.RequestServices.GetService<HandlerCapabilities>();
        var clock = context.RequestServices.GetService<TimeProvider>() ?? TimeProvider.System;
        return capabilities is not null && capabilities.TryRecognise(presented, clock.GetUtcNow(), out grant);
    }

    /// <summary>Whether the path is exactly one operation's route and that operation is in the grant.</summary>
    private static bool Opens(DoorGrant grant, PathString path)
    {
        var prefix = Operations.Route(string.Empty);
        var value = path.Value;
        return value is not null
            && value.StartsWith(prefix, StringComparison.Ordinal)
            && grant.Operations.Contains(value[prefix.Length..]);
    }
}
