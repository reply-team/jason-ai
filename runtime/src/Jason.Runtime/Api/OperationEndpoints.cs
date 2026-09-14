using System.Text;
using System.Text.Json;
using Jason.Contracts.Api;
using Jason.Contracts.Json;
using Jason.Runtime.Domain;
using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Jason.Runtime.Api;

/// <summary>
/// Every operation is mapped the same way, so the wire behaviour of the whole API is decided in one place:
/// body binding, the error envelope, and the rule that only a <see cref="DomainException"/> reaches the caller.
/// </summary>
public static class OperationEndpoints
{
    /// <summary>Binds the JSON body (empty = {}), resolves TService from the request scope, converts DomainException into the envelope, unexpected exceptions into 500 internal_error.</summary>
    public static RouteHandlerBuilder MapOperation<TService, TRequest, TResponse>(
        this IEndpointRouteBuilder app,
        string operation,
        Func<TService, TRequest, CancellationToken, Task<TResponse>> handler)
        where TService : notnull
        where TRequest : class
    {
        ArgumentNullException.ThrowIfNull(app);
        ArgumentNullException.ThrowIfNull(handler);

        // Typed as Delegate on purpose: the RequestDelegate overload of MapPost would hand back a builder that
        // carries no route-handler conventions, and every operation should stay configurable like any other route.
        Delegate operationHandler = async (HttpContext context) =>
        {
            TRequest request;
            try
            {
                request = await ReadRequestAsync<TRequest>(context).ConfigureAwait(false);
            }
            catch (JsonException ex)
            {
                await ErrorResults.WriteAsync(context, StatusCodes.Status400BadRequest, "invalid_request", "The request body is not valid JSON for this operation. " + ex.Message, retryable: false).ConfigureAwait(false);
                return;
            }

            try
            {
                var service = context.RequestServices.GetRequiredService<TService>();
                var response = await handler(service, request, context.RequestAborted).ConfigureAwait(false);
                await context.Response.WriteAsJsonAsync(response, JasonJson.Options, context.RequestAborted).ConfigureAwait(false);
            }
            catch (DomainException ex)
            {
                await ErrorResults.WriteAsync(context, ex.StatusCode, ex.Code, ex.Message, ex.Retryable, ex.Details).ConfigureAwait(false);
            }
            catch (DbUpdateConcurrencyException)
            {
                // Losing a race is not a failure of the runtime: the caller read a row, someone else changed it
                // first, and the honest answer is to say so and let them decide with the new state in hand.
                var conflict = DomainErrors.ConcurrentUpdate();
                await ErrorResults.WriteAsync(context, conflict.StatusCode, conflict.Code, conflict.Message, conflict.Retryable).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // The caller learns nothing about the failure beyond "it was ours": the detail is in the runtime log,
                // which is also the only place the request ever gets near.
                OperationFailed(context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("Jason.Runtime.Api"), operation, ex);
                await ErrorResults.WriteAsync(context, StatusCodes.Status500InternalServerError, "internal_error", "The runtime failed to perform the operation; see the runtime logs.", retryable: true).ConfigureAwait(false);
            }
        };

        return app.MapPost(Operations.Route(operation), operationHandler);
    }

    private static async Task<TRequest> ReadRequestAsync<TRequest>(HttpContext context)
        where TRequest : class
    {
        using var reader = new StreamReader(context.Request.Body, Encoding.UTF8);
        var text = await reader.ReadToEndAsync(context.RequestAborted).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(text))
        {
            text = "{}";
        }

        return JsonSerializer.Deserialize<TRequest>(text, JasonJson.Options) ?? throw new JsonException("The request body must be a JSON object.");
    }

    private static readonly Action<ILogger, string, Exception> OperationFailed =
        LoggerMessage.Define<string>(LogLevel.Error, new EventId(1, nameof(OperationFailed)), "Operation {Operation} failed");
}
