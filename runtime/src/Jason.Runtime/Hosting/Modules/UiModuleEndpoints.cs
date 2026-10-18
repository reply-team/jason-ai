using Jason.Contracts.Api;
using Jason.Runtime.Api;
using Jason.Runtime.Ui;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Jason.Runtime.Hosting.Modules;

/// <summary>The UI module's seam: three operations, and the files and the pairing door under <c>/ui</c>.</summary>
public static class UiModuleEndpoints
{
    public static IServiceCollection AddUiModule(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddSingleton<UiModule>();
        services.AddSingleton<UiTickets>();
        services.AddSingleton<UiTokenStore>();
        services.AddSingleton<UiService>();
        return services;
    }

    public static void MapUiOperations(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);
        app.MapOperation<UiService, UiOpenRequest, UiOpenResponse>(Operations.UiOpen, (service, request, ct) => service.OpenAsync(request, ct));
        app.MapOperation<UiService, UiTokensRequest, UiTokensResponse>(Operations.UiTokens, (service, request, ct) => service.TokensAsync(request, ct));
        app.MapOperation<UiService, UiRevokeRequest, UiRevokeResponse>(Operations.UiRevoke, (service, request, ct) => service.RevokeAsync(request, ct));
    }

    /// <summary>The files, under the headers that make the page safe to run. Mapped before the fallback so a mistyped file is a 404 of this module's.</summary>
    public static void MapUi(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.MapPost(UiPaths.Door, UiSessionDoor.HandleAsync);

        // One route for the prefix and everything under it: routing ignores a trailing slash, so /ui and /ui/ match the
        // same pattern, and the bare prefix is told apart by the path as sent and redirected to the page.
        app.MapGet(UiPaths.Prefix + "/{**path}", async (HttpContext context, string? path, UiModule module) =>
        {
            var relative = path ?? string.Empty;
            if (relative.Length == 0 && context.Request.Path.Value?.EndsWith('/') != true)
            {
                context.Response.Redirect(UiPaths.Prefix + "/", permanent: true);
                return;
            }

            UiFiles.ApplyHeaders(context.Response);
            var file = module.Resolve(relative);
            var contentType = file is null ? null : UiFiles.ContentType(file);
            if (file is null || contentType is null)
            {
                if (module.ActiveDirectory() is null)
                {
                    await ErrorResults.WriteAsync(context, StatusCodes.Status404NotFound, "ui_not_installed", module.NotInstalled(), retryable: false).ConfigureAwait(false);
                }
                else
                {
                    await ErrorResults.WriteAsync(context, StatusCodes.Status404NotFound, "not_found", "No such file under /ui/.", retryable: false).ConfigureAwait(false);
                }

                return;
            }

            context.Response.Headers.CacheControl = UiFiles.CacheControl(relative);
            context.Response.ContentType = contentType;
            await context.Response.SendFileAsync(file, context.RequestAborted).ConfigureAwait(false);
        });
    }
}
