using Jason.Contracts.Api;
using Microsoft.AspNetCore.Http;

namespace Jason.Runtime.Domain;

/// <summary>
/// A failure the caller is meant to read: it carries the status, the snake_case code and the retryable flag of the
/// error envelope. Anything else that escapes a service is a bug and becomes a 500.
/// </summary>
public class DomainException(int statusCode, string code, string message, bool retryable = false, IReadOnlyList<ErrorDetail>? details = null)
    : Exception(message)
{
    public int StatusCode { get; } = statusCode;

    public string Code { get; } = code;

    public bool Retryable { get; } = retryable;

    public IReadOnlyList<ErrorDetail>? Details { get; } = details;
}

public sealed class NotFoundException(string code, string message) : DomainException(StatusCodes.Status404NotFound, code, message);

public sealed class ConflictException(string code, string message) : DomainException(StatusCodes.Status409Conflict, code, message);

public sealed class InvalidRequestException(string code, string message) : DomainException(StatusCodes.Status400BadRequest, code, message);

/// <summary>Field-level problems. The message repeats the field names and rule codes, never the submitted values.</summary>
public sealed class ValidationException(IReadOnlyList<ErrorDetail> details)
    : DomainException(
        StatusCodes.Status400BadRequest,
        "validation_failed",
        "Validation failed: " + string.Join("; ", details.Select(d => $"{d.Field}: {d.Code}")) + ".",
        false,
        details);

/// <summary>Collects field problems so one response reports all of them. Messages name the rule, never the submitted value.</summary>
public sealed class ValidationErrors
{
    private readonly List<ErrorDetail> _details = [];

    public bool Any => _details.Count > 0;

    public void Add(string field, string code, string message) => _details.Add(new ErrorDetail(field, code, message));

    public void ThrowIfAny()
    {
        if (Any)
        {
            throw new ValidationException(_details);
        }
    }
}
