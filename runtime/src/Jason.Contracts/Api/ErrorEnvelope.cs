namespace Jason.Contracts.Api;

/// <summary>Every non-2xx response and every CLI-side failure carries exactly this shape.</summary>
public sealed record ErrorResponse(ErrorBody Error);

public sealed record ErrorBody(string Code, string Message, bool Retryable);
