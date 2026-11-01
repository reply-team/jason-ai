using Jason.Contracts.Update;

namespace Jason.Runtime.Configuration;

/// <summary>
/// The unattended update check: whether a running runtime asks the release feed at all, which feed, and when.
/// Read through <c>IOptionsMonitor</c> before every check, so a hand edit of the settings file applies at the
/// next one without a restart.
/// </summary>
public sealed class UpdateOptions
{
    public const string Section = "Update";

    /// <summary>
    /// The narrowest and widest the two waits may be, named here rather than in the validator for the reason
    /// the manager's cadence is: the page prints these numbers, and a bound nobody can print is a bound that
    /// drifts from the rule that enforces it.
    /// </summary>
    public const int MinimumInitialDelayMinutes = 1;

    public const int MaximumInitialDelayMinutes = 1440;

    public const int MinimumIntervalHours = 1;

    public const int MaximumIntervalHours = 168;

    /// <summary>
    /// Off means the runtime never opens a connection for this. The check is the one thing a runtime does that
    /// reaches past the machine without being asked, so it has a switch of its own — and every test runtime
    /// turns it off.
    /// </summary>
    public bool CheckEnabled { get; set; } = true;

    /// <summary>
    /// Where the manifest is read from: https, or http on loopback, and nothing else. The reader refuses the
    /// rest, and so does the validator, so a feed that could never be read is refused before the first check.
    /// </summary>
    public string FeedUrl { get; set; } = UpdateFeed.Default.ToString();

    /// <summary>How long after start the first check runs; a runtime that lives less than this never asks.</summary>
    public int InitialDelayMinutes { get; set; } = 5;

    /// <summary>How long between one check and the next.</summary>
    public int IntervalHours { get; set; } = 24;
}
