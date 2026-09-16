using System.Globalization;
using Jason.Contracts.Api;
using Jason.Contracts.Json;
using Microsoft.AspNetCore.Http;

namespace Jason.Runtime.Api;

/// <summary>Writes the one error envelope every non-2xx response of the Runtime API carries.</summary>
public static class ErrorResults
{
    /// <summary>
    /// How many field problems one answer lists. A body wrong in twenty thousand places is wrong for one reason,
    /// and answering with twenty thousand pointers spends megabytes to say it: the first fifty show the shape of
    /// the mistake. The bound is here, where the answer is written, rather than in the validator — a caller fixing
    /// its arguments still wants every problem it can act on, and what has to be bounded is the size of one HTTP
    /// response.
    /// </summary>
    public const int MaxDetails = 50;

    public static Task WriteAsync(HttpContext context, int statusCode, string code, string message, bool retryable, IReadOnlyList<ErrorDetail>? details = null)
    {
        ArgumentNullException.ThrowIfNull(context);
        context.Response.StatusCode = statusCode;

        var listed = details;
        if (details is not null && details.Count > MaxDetails)
        {
            listed = [.. details.Take(MaxDetails)];

            // How many there were is already in the message the exception composed. Saying it again from a
            // different starting point — the details array rather than the sentence — put two counts of the same
            // thing side by side, disagreeing, and left a reader working out which was which.
            message = string.Create(CultureInfo.InvariantCulture, $"{message} Of those, only the first {MaxDetails} are listed here.");
        }

        return context.Response.WriteAsJsonAsync(new ErrorResponse(new ErrorBody(code, message, retryable, listed)), JasonJson.Options);
    }
}
