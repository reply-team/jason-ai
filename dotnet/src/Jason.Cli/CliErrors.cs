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

    /// <summary>
    /// The runtime took the request and did not answer within the operation's budget and a margin: it is reachable, and
    /// may still be working, so asking again starts the work over.
    /// </summary>
    public const string RequestTimedOut = "request_timed_out";
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

    /// <summary>
    /// This jason is the one the desktop application carries, so <c>jason uninstall</c> would delete it out of the
    /// application: nothing was read or removed, and the application is removed the way the system removes one.
    /// </summary>
    public const string UninstallCarriedByDesktop = "uninstall_carried_by_desktop";

    /// <summary>The log directory exists and <c>jason logs</c> could not read it: nothing was shown rather than less.</summary>
    public const string LogsUnreadable = "logs_unreadable";

    /// <summary>Something is already at the path <c>jason doctor --output</c> names, and a bundle is never written over anything.</summary>
    public const string DoctorOutputExists = "doctor_output_exists";

    /// <summary>The path <c>jason doctor</c> would write its bundle to cannot be written, so nothing was.</summary>
    public const string DoctorOutputUnwritable = "doctor_output_unwritable";

    /// <summary>The runtime runs, and a verb that works on its files cold was not told to stop it, so nothing was written.</summary>
    public const string RuntimeRunning = "runtime_running";

    /// <summary>Something is already at the path <c>jason export</c> would write, and an archive is never written over anything.</summary>
    public const string ExportOutputExists = "export_output_exists";

    /// <summary>The path <c>jason export</c> would write its archive to cannot be written, so nothing was.</summary>
    public const string ExportUnwritable = "export_unwritable";

    /// <summary>What <c>jason export</c> would carry holds something an archive cannot, so nothing was written.</summary>
    public const string ExportRefused = "export_refused";

    /// <summary>The file <c>jason import</c> was given cannot be read as an archive <c>jason export</c> wrote, so nothing was written.</summary>
    public const string ImportUnreadable = "import_unreadable";

    /// <summary>The archive, or the place <c>jason import</c> would put it, holds something an import never writes, so nothing was written.</summary>
    public const string ImportRefused = "import_refused";

    /// <summary>A file in the archive is not the one its listing names by size and digest, so nothing was written.</summary>
    public const string ImportDigestMismatch = "import_digest_mismatch";

    /// <summary>A newer Jason wrote the archive than the one asked to import it, so nothing was written.</summary>
    public const string ImportNewerVersion = "import_newer_version";

    /// <summary>An installation is already here and <c>--replace</c> was not given, so nothing was written.</summary>
    public const string ImportNotEmpty = "import_not_empty";

    /// <summary>What is here could not be moved aside, or the archive could not be placed in full.</summary>
    public const string ImportUnwritable = "import_unwritable";

    public static string Serialize(string code, string message, bool retryable) =>
        JsonSerializer.Serialize(new ErrorResponse(new ErrorBody(code, message, retryable)), JasonJson.Options);
}
