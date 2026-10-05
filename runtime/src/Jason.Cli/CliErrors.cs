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

    /// <summary>
    /// The runtime answered with an error and a body that is not JSON — the server's own answer to a failure, not one
    /// the runtime composed — so there was no envelope to print and this one says what came back instead.
    /// </summary>
    public const string RuntimeError = "runtime_error";

    /// <summary>No remover was named for this environment, so <c>jason uninstall</c> touched nothing.</summary>
    public const string RemoverUnsupported = "remover_unsupported";

    /// <summary>The runtime acknowledged the shutdown and did not go, so nothing further was removed.</summary>
    public const string RuntimeStillRunning = "runtime_still_running";

    /// <summary>An uninstall understood the command and declined to perform it.</summary>
    public const string UninstallRefused = "uninstall_refused";

    /// <summary>The log directory exists and <c>jason logs</c> could not read it: nothing was shown rather than less.</summary>
    public const string LogsUnreadable = "logs_unreadable";

    /// <summary>Something is already at the path <c>jason doctor --output</c> names, and a bundle is never written over anything.</summary>
    public const string DoctorOutputExists = "doctor_output_exists";

    /// <summary>The path <c>jason doctor</c> would write its bundle to cannot be written, so nothing was.</summary>
    public const string DoctorOutputUnwritable = "doctor_output_unwritable";

    public static string Serialize(string code, string message, bool retryable) =>
        JsonSerializer.Serialize(new ErrorResponse(new ErrorBody(code, message, retryable)), JasonJson.Options);
}
