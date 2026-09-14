using Jason.Contracts.Api;
using Jason.Contracts.Json;
using Microsoft.AspNetCore.Http;

namespace Jason.Runtime.Api;

/// <summary>Writes the one error envelope every non-2xx response of the Runtime API carries.</summary>
public static class ErrorResults
{
    public static Task WriteAsync(HttpContext context, int statusCode, string code, string message, bool retryable, IReadOnlyList<ErrorDetail>? details = null)
    {
        ArgumentNullException.ThrowIfNull(context);
        context.Response.StatusCode = statusCode;
        return context.Response.WriteAsJsonAsync(new ErrorResponse(new ErrorBody(code, message, retryable, details)), JasonJson.Options);
    }
}
