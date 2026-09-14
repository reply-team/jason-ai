using Jason.Contracts.Api;
using Jason.Runtime.Configuration;
using Jason.Runtime.Execution;
using Jason.Runtime.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Jason.Runtime.Dispatch;

/// <summary>
/// The loop. It starts by settling what the last run left behind, then scans on its own tick until the process
/// is asked to stop; stopping means claiming nothing more and giving the handlers a bounded while to see their
/// children out. Children that outlive the wait are left alive with their leases — the restart decides.
/// </summary>
public sealed class DispatcherService(
    ScanRunner runner,
    IServiceScopeFactory scopes,
    IOptionsMonitor<DispatcherOptions> options,
    TimeProvider clock,
    DispatcherStatus status,
    HandlerPool pool,
    ILogger<DispatcherService> logger) : BackgroundService
{
    private static readonly Action<ILogger, int, Exception?> Recovered = LoggerMessage.Define<int>(
        LogLevel.Information,
        new EventId(1, nameof(Recovered)),
        "Dispatcher start: {Count} interrupted attempts released");

    private static readonly Action<ILogger, Exception?> Disabled = LoggerMessage.Define(
        LogLevel.Information,
        new EventId(2, nameof(Disabled)),
        "Dispatcher is disabled by configuration; no work will be claimed");

    private static readonly Action<ILogger, Exception?> ScanFailed = LoggerMessage.Define(
        LogLevel.Error,
        new EventId(3, nameof(ScanFailed)),
        "Dispatcher scan failed; the loop continues");

    private static readonly Action<ILogger, int, string, Exception?> DrainGaveUp = LoggerMessage.Define<int, string>(
        LogLevel.Information,
        new EventId(4, nameof(DrainGaveUp)),
        "Dispatcher stopped; {Count} attempts still running: {AttemptIds}");

    public override async Task StartAsync(CancellationToken cancellationToken)
    {
        // The anchor of the heartbeat grace: an executor cannot be held to beats this process was not there to hear.
        status.RuntimeStartedAt = clock.GetUtcNow().UtcDateTime;
        status.MaxParallel = pool.MaxParallel;

        if (!options.CurrentValue.Enabled)
        {
            status.State = DispatcherState.Disabled;
            Disabled(logger, null);
            return;
        }

        await using (var scope = scopes.CreateAsyncScope())
        {
            var released = await scope.ServiceProvider.GetRequiredService<StartupRecovery>()
                .RunAsync(scope.ServiceProvider.GetRequiredService<JasonDbContext>(), cancellationToken)
                .ConfigureAwait(false);
            Recovered(logger, released, null);
        }

        status.State = DispatcherState.Running;
        await base.StartAsync(cancellationToken).ConfigureAwait(false);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await runner.ScanOnceAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // One bad scan is not the end of the dispatcher: the next tick tries again.
                ScanFailed(logger, ex);
            }

            try
            {
                // Read every iteration, so an edit of the settings file changes the tick without a restart.
                await Task.Delay(TimeSpan.FromSeconds(options.CurrentValue.TickSeconds), clock, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        if (status.State == DispatcherState.Disabled)
        {
            return;
        }

        status.State = DispatcherState.Draining;
        await base.StopAsync(cancellationToken).ConfigureAwait(false);

        var drained = await pool.DrainAsync(TimeSpan.FromSeconds(options.CurrentValue.DrainSeconds)).ConfigureAwait(false);
        status.State = DispatcherState.Stopped;
        if (!drained)
        {
            var running = pool.InFlightAttemptIds;
            DrainGaveUp(logger, running.Count, string.Join(", ", running), null);
        }
    }
}
