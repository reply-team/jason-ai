using Jason.Contracts.Api;
using Jason.Runtime.Discovery;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Jason.Runtime.Hosting;

/// <summary>
/// Ending the process on request. The acknowledgement is written first and the host is asked to stop a moment
/// later, so the caller learns which instance is going down instead of losing the answer to a closed socket.
/// From there it is the same path as Ctrl+C: stop listening, remove the descriptor, release the lock, exit 0.
/// </summary>
public sealed class ShutdownCoordinator(IHostApplicationLifetime lifetime, RuntimeInfo info, ILogger<ShutdownCoordinator> logger)
{
    /// <summary>
    /// Long enough for the response to leave the socket on a loopback connection, short enough that a client
    /// polling for the process to disappear never notices the pause.
    /// </summary>
    private static readonly TimeSpan Grace = TimeSpan.FromMilliseconds(200);

    private static readonly Action<ILogger, string, Exception?> ShutdownRequested =
        LoggerMessage.Define<string>(LogLevel.Information, new EventId(1, nameof(ShutdownRequested)), "Shutdown requested through the API; instance {InstanceId} is stopping");

    /// <summary>Acknowledges the request and stops the host just after the answer has gone out.</summary>
    public ShutdownResponse RequestShutdown()
    {
        ShutdownRequested(logger, info.InstanceId, null);

        _ = Task.Run(async () =>
        {
            await Task.Delay(Grace).ConfigureAwait(false);
            lifetime.StopApplication();
        });

        return new ShutdownResponse(info.InstanceId, info.Pid, Stopping: true);
    }
}
