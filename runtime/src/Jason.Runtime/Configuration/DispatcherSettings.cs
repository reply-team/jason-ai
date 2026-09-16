using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Jason.Runtime.Configuration;

/// <summary>
/// The dispatcher's settings as a running runtime works from them. Hand-editing <c>settings.json</c> while the
/// runtime runs is the operator's path to a tick, a budget or a route, and a value the validator refuses is a
/// fact about the file rather than about the process: the last value that did validate stays in force, the
/// validator's own sentence is said once for the edit that broke it — a loop complaining every tick is its own
/// defect — and the loop keeps its tick until the file is put right.
/// </summary>
/// <remarks>
/// A runtime that has never read a value it accepted is a different thing: nothing is in force to fall back to,
/// so the refusal stands and the start fails, which is what <c>ValidateOnStart</c> is there to do.
/// </remarks>
public sealed class DispatcherSettings(IOptionsMonitor<DispatcherOptions> monitor, ILogger<DispatcherSettings> logger)
{
    private static readonly Action<ILogger, string, Exception?> Refused = LoggerMessage.Define<string>(
        LogLevel.Error,
        new EventId(1, nameof(Refused)),
        "Edited settings were refused; the runtime keeps the last ones that validated: {Failure}");

    private readonly Lock _gate = new();
    private DispatcherOptions? _lastGood;
    private string? _complaint;
    private long _refusals;

    /// <summary>
    /// What the runtime is working from: the settings file where it validates, and the last value that did
    /// where it does not.
    /// </summary>
    public DispatcherOptions Current
    {
        get
        {
            DispatcherOptions current;
            try
            {
                current = monitor.CurrentValue;
            }
            catch (OptionsValidationException ex)
            {
                if (Fallback(string.Join(" ", ex.Failures)) is not { } good)
                {
                    throw;
                }

                return good;
            }

            lock (_gate)
            {
                _lastGood = current;

                // The next edit that breaks is a new thing to say, even if it breaks in exactly the same way.
                _complaint = null;
            }

            return current;
        }
    }

    /// <summary>How many reads the validator has refused since the runtime started; zero is the normal answer.</summary>
    public long Refusals => Interlocked.Read(ref _refusals);

    private DispatcherOptions? Fallback(string failure)
    {
        Interlocked.Increment(ref _refusals);
        lock (_gate)
        {
            if (!string.Equals(_complaint, failure, StringComparison.Ordinal))
            {
                _complaint = failure;
                Refused(logger, failure, null);
            }

            return _lastGood;
        }
    }
}
