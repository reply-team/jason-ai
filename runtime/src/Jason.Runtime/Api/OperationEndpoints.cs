using System.Text;
using System.Text.Json;
using Jason.Contracts.Api;
using Jason.Contracts.Json;
using Jason.Runtime.Domain;
using Jason.Runtime.Logging;
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
            catch (Exception ex)
            {
                // Whatever reading the body throws is answered here and logged nowhere. A converter throws what its
                // parser throws, and a parser's message quotes what it could not read: anything let past this point
                // is written whole by the host — on the request line and on the server's own line about a failure
                // nothing caught — and the caller is left with an empty 500. The serializer's own words go back to
                // the caller who wrote the body, as they always have; any other failure's message is not repeated.
                // A caller that went away mid-body is not answered: there is nobody left to read it.
                if (!context.RequestAborted.IsCancellationRequested)
                {
                    await ErrorResults.WriteAsync(context, StatusCodes.Status400BadRequest, "invalid_request", Unreadable(ex), retryable: false).ConfigureAwait(false);
                }

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
                var logger = context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("Jason.Runtime.Api");
                if (ParseFailure.Is(ex))
                {
                    // The door refused a body it could not read before this point, so a read failure here is one a
                    // handler met in something the body carried. The parser's message can quote that, and a log
                    // sink writes an exception whole: the line says what failed and where, and the exception stays out.
                    OperationFailedToParse(logger, operation, ParseFailure.Describe(ex), null);
                }
                else
                {
                    OperationFailed(logger, operation, ex);
                }

                await ErrorResults.WriteAsync(context, StatusCodes.Status500InternalServerError, "internal_error", "The runtime failed to perform the operation; see the runtime logs.", retryable: true).ConfigureAwait(false);
            }
        };

        return app.MapPost(Operations.Route(operation), operationHandler);
    }

    /// <summary>
    /// A property written twice is not a request this API can answer: the two values say different things and
    /// nothing in the contract picks between them. It is refused here, at the door, and at every depth — the
    /// serializer's own duplicate rule reads only the fields it binds, so a duplicate inside a work item's
    /// context, which arrives as a node it hands on without reading, would go through unseen and throw at
    /// whatever read it first.
    /// </summary>
    private static readonly JsonDocumentOptions Bodies = new() { AllowDuplicateProperties = false };

    private static async Task<TRequest> ReadRequestAsync<TRequest>(HttpContext context)
        where TRequest : class
    {
        using var reader = new StreamReader(context.Request.Body, Encoding.UTF8);
        var text = await reader.ReadToEndAsync(context.RequestAborted).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(text))
        {
            text = "{}";
        }

        using var document = JsonDocument.Parse(text, Bodies);
        return document.Deserialize<TRequest>(JasonJson.Options) ?? throw new JsonException("The request body must be a JSON object.");
    }

    private const string NotValid = "The request body is not valid JSON for this operation.";

    /// <summary>
    /// The refusal of a body the door could not read. A <see cref="JsonException"/> is the serializer's, and its
    /// message — where the reader stopped, and why, in the dialect's own terms — is the help the caller needs; every
    /// converter of the dialect refuses that way without quoting a value. Anything else came from a parser the door
    /// does not know, whose message may quote the value it could not read, so the caller gets the sentence alone.
    /// </summary>
    private static string Unreadable(Exception exception) =>
        exception is JsonException json ? NotValid + " " + json.Message : NotValid;

    private static readonly Action<ILogger, string, Exception> OperationFailed =
        LoggerMessage.Define<string>(LogLevel.Error, new EventId(1, nameof(OperationFailed)), "Operation {Operation} failed");

    // The same event as above, so one filter finds both: only what the line says about the failure differs.
    private static readonly Action<ILogger, string, string, Exception?> OperationFailedToParse =
        LoggerMessage.Define<string, string>(LogLevel.Error, new EventId(1, nameof(OperationFailed)), "Operation {Operation} failed: {Failure}");
}
