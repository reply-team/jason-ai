using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Jason.Runtime.Configuration;

/// <summary>
/// One section of the settings as a running runtime works from it. Hand-editing <c>settings.json</c> while the
/// runtime runs is the operator's path to a tick, a budget, a plugin's memory or a role's entry command, and a
/// value the validator refuses is a fact about the file rather than about the process: the last value that did
/// validate stays in force, the validator's own sentence is said once for the edit that broke it — a loop
/// complaining every tick is its own defect — and everything that reads this section keeps working meanwhile.
/// </summary>
/// <remarks>
/// A runtime that has never read a value it accepted is a different thing: nothing is in force to fall back to,
/// so the refusal stands and the start fails, which is what <c>ValidateOnStart</c> is there to do.
/// </remarks>
/// <param name="section">
/// The section this seam guards, said in the complaint. Three of them are guarded, and an operator told only
/// that "settings were refused" would not know which part of the file to open.
/// </param>
public sealed class LiveSettings<TOptions>(
    IOptionsMonitor<TOptions> monitor,
    ILogger<LiveSettings<TOptions>> logger,
    string section)
    where TOptions : class
{
    private static readonly Action<ILogger, string, string, Exception?> Refused = LoggerMessage.Define<string, string>(
        LogLevel.Error,
        new EventId(1, nameof(Refused)),
        "Edited {Section} settings were refused; the runtime keeps the last ones that validated: {Failure}");

    private readonly Lock _gate = new();
    private TOptions? _lastGood;
    private string? _complaint;
    private long _refusals;

    /// <summary>
    /// What the runtime is working from: the settings file where it validates, and the last value that did
    /// where it does not.
    /// </summary>
    public TOptions Current
    {
        get
        {
            TOptions current;
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

    /// <summary>
    /// The settings for a path that has to answer. A request is not a tick: an executor reporting what it did
    /// cannot act on an exception, so where the loop may fall back and carry on, an endpoint needs to know that
    /// there is nothing to fall back to and say so in its own vocabulary.
    /// </summary>
    public bool TryCurrent(out TOptions options)
    {
        try
        {
            options = Current;
            return true;
        }
        catch (OptionsValidationException)
        {
            options = null!;
            return false;
        }
    }

    private TOptions? Fallback(string failure)
    {
        Interlocked.Increment(ref _refusals);
        lock (_gate)
        {
            if (!string.Equals(_complaint, failure, StringComparison.Ordinal))
            {
                _complaint = failure;
                Refused(logger, section, failure, null);
            }

            return _lastGood;
        }
    }
}

/// <summary>
/// Which setting the validator was talking about. Its sentences begin with the setting's own path where there is
/// one and name only the section where the shape itself is wrong — one implementation, because the seam, the
/// plugin loader and the route activator all have to name it the same way.
/// </summary>
public static class SettingsFailure
{
    public static string Name(string section, string failure)
    {
        ArgumentNullException.ThrowIfNull(section);
        ArgumentNullException.ThrowIfNull(failure);
        var first = failure.Split(' ', 2)[0];
        return first.StartsWith(section + ":", StringComparison.Ordinal) ? first : section;
    }
}
