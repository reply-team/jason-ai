using System.Text.Json;
using Jason.Contracts.Api;
using Jason.Contracts.Json;

namespace Jason.Cli;

/// <summary>
/// Failures detected on the client side (no descriptor, dead port, rejected token, stale descriptor) are
/// reported on stdout in the same envelope the API uses, so agents parse one shape.
/// </summary>
public static class CliErrors
{
    public const string NoDescriptor = "no_descriptor";
    public const string RuntimeUnreachable = "runtime_unreachable";
    public const string Unauthorized = "unauthorized";
    public const string StaleDescriptor = "stale_descriptor";
    public const string ShutdownTimeout = "shutdown_timeout";
    public const string RuntimeStartFailed = "runtime_start_failed";

    public static string Serialize(string code, string message, bool retryable) =>
        JsonSerializer.Serialize(new ErrorResponse(new ErrorBody(code, message, retryable)), JasonJson.Options);
}
