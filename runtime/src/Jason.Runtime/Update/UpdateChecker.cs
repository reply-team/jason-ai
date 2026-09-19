using Jason.Contracts.Update;
using Jason.Runtime.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Jason.Runtime.Update;

/// <summary>
/// The unattended check: after the initial delay, and then once per interval, read the feed and record what it
/// says. It sleeps through the runtime's clock, so a test moves time instead of waiting; it reads its settings
/// after every wait, so an operator's edit is in force at the next one; and it survives every failed check,
/// saying once per failure what went wrong, because a checker that stopped at the first outage would be one
/// that never learned anything again.
/// </summary>
/// <remarks>
/// Off at start means off for the life of the process — the service returns and holds no timer. A runtime that
/// started with the check on reads the switch again before every check, so turning it off applies at the next
/// wait and turning it back on needs no restart either.
/// </remarks>
public sealed class UpdateChecker(
    TimeProvider clock,
    LiveSettings<UpdateOptions> settings,
    UpdateFeed feed,
    UpdateAdvertisement advertisement,
    ILogger<UpdateChecker> logger) : BackgroundService
{
    private static readonly Action<ILogger, Exception?> Disabled = LoggerMessage.Define(
        LogLevel.Information,
        new EventId(1, nameof(Disabled)),
        "Update check is disabled by configuration; this runtime will not ask the release feed");

    private static readonly Action<ILogger, string, Exception?> NewerAvailable = LoggerMessage.Define<string>(
        LogLevel.Information,
        new EventId(2, nameof(NewerAvailable)),
        "A newer version of Jason is available: {Version}");

    private static readonly Action<ILogger, string, string, Exception?> CheckFailed = LoggerMessage.Define<string, string>(
        LogLevel.Warning,
        new EventId(3, nameof(CheckFailed)),
        "Update check failed ({Code}): {Message}");

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var options = settings.Current;
        if (!options.CheckEnabled)
        {
            Disabled(logger, null);
            return;
        }

        var wait = TimeSpan.FromMinutes(options.InitialDelayMinutes);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(wait, clock, stoppingToken).ConfigureAwait(false);

                // Read after the wait rather than before it, so an edit made during a day's sleep is the one in
                // force when the day is up — and through the guarded seam, so an edit the validator refused
                // costs the edit and not the loop.
                options = settings.Current;
                if (options.CheckEnabled)
                {
                    await CheckOnceAsync(options.FeedUrl, stoppingToken).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }

            wait = TimeSpan.FromHours(options.IntervalHours);
        }
    }

    private async Task CheckOnceAsync(string feedUrl, CancellationToken cancellationToken)
    {
        try
        {
            var manifest = await feed.ReadAsync(new Uri(feedUrl, UriKind.Absolute), cancellationToken).ConfigureAwait(false);
            advertisement.Record(manifest, SemanticVersion.Current, clock.GetUtcNow());
            if (manifest.IsNewerThan(SemanticVersion.Current))
            {
                NewerAvailable(logger, manifest.Version.ToString(), null);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (UpdateFeedException error)
        {
            // Once per failed check, with the code: a feed that is down for a week is visible daily and no
            // louder, and the line says which of the three things went wrong.
            CheckFailed(logger, error.Code, error.Message, null);
        }
        catch (Exception error)
        {
            // Everything else too. A check that could take the runtime down is a check nobody would dare leave
            // on, and this one has to be on to be any use.
            //
            // The code is not one of the three the feed publishes, deliberately: reaching here means the reader
            // let something through that it should have named itself, so the line says "this was not one of the
            // failures we know about" rather than dressing an unknown up as a known one. It was reachable once —
            // a body that died after the headers threw a raw IOException from outside the reader's guard — and
            // that is now an update_feed_unreachable like any other network failure.
            CheckFailed(logger, "update_check_failed", error.Message, error);
        }
    }
}
