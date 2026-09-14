using System.Text.Json.Serialization;

namespace Jason.Contracts.Api;

/// <summary>Every non-2xx response and every CLI-side failure carries exactly this shape.</summary>
public sealed record ErrorResponse(ErrorBody Error);

/// <summary>
/// <see cref="Details"/> is additive and omitted unless the error is about specific fields: a client that does not
/// know it still reads code and message, while an agent can fix its whole payload in one round trip.
/// </summary>
public sealed record ErrorBody(
    string Code,
    string Message,
    bool Retryable,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<ErrorDetail>? Details = null);

/// <summary>One field problem. The message names the rule that failed, never the value that failed it.</summary>
public sealed record ErrorDetail(string Field, string Code, string Message);
