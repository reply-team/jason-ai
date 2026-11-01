using System.Text.RegularExpressions;
using Jason.Contracts.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Jason.Runtime.Configuration;

/// <summary>
/// One section of the settings as a running runtime works from it. Hand-editing <c>settings.json</c> while the
/// runtime runs is the operator's path to a tick, a budget, a plugin's memory or a role's entry command, and a
/// value the validator refuses is a fact about the file rather than about the process: the last value that did
/// validate stays in force, the validator's own sentence is said once for the edit that broke it — a loop
/// complaining every tick is its own defect — and everything that reads this section keeps working meanwhile.
/// A value of the wrong type — text where a number belongs — arrives here as the same refusal, because binding
/// records it as a validation failure (<see cref="SafeBinding"/>); nothing here has to know about the binder.
/// </summary>
/// <remarks>
/// A runtime that has never read a value it accepted is a different thing: nothing is in force to fall back to,
/// so the refusal stands and the start fails, which is what <c>ValidateOnStart</c> is there to do. So that a running
/// runtime is never in that state, the start reads every seam once as soon as every section has validated
/// (<see cref="ISettingsSeam"/>).
/// </remarks>
/// <param name="section">
/// The section this seam guards, said in the complaint. Several are guarded, and an operator told only that
/// "settings were refused" would not know which part of the file to open.
/// </param>
public sealed class LiveSettings<TOptions>(
    IOptionsMonitor<TOptions> monitor,
    ILogger<LiveSettings<TOptions>> logger,
    string section) : ISettingsSeam
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

    /// <summary>
    /// The last settings that validated, and the complaint for the edit that broke them said once. Said only where there
    /// is something to fall back to: the complaint says the runtime keeps the last ones that validated, which is not
    /// true of a seam that never read any — at start, where a validator reads another section through its seam before
    /// either has validated, the start's own refusal is what is said.
    /// </summary>
    /// <inheritdoc />
    void ISettingsSeam.Seed() => TryCurrent(out _);

    private TOptions? Fallback(string failure)
    {
        Interlocked.Increment(ref _refusals);
        lock (_gate)
        {
            if (_lastGood is not null && !string.Equals(_complaint, failure, StringComparison.Ordinal))
            {
                _complaint = failure;
                Refused(logger, section, failure, null);
            }

            return _lastGood;
        }
    }
}

/// <summary>
/// A seam whatever its section, so the start can read every one once. A seam keeps the last settings that validated only
/// once it has read some, and one that nothing read at start — the roles' and the manager's, in a runtime with no dispatch
/// loop — met a refused edit with nothing to keep: the runtime's answer about itself was a server error, which a caller
/// reads as a runtime that is not there.
/// </summary>
internal interface ISettingsSeam
{
    /// <summary>Reads the section once, so that what the start validated is what the seam keeps.</summary>
    void Seed();
}

/// <summary>
/// Which setting the validator was talking about. Its sentences begin with the setting's own path where there is
/// one and name only the section where the shape itself is wrong — one implementation, because the seam, the
/// plugin loader and the route activator all have to name it the same way.
/// </summary>
public static partial class SettingsFailure
{
    public static string Name(string section, string failure)
    {
        ArgumentNullException.ThrowIfNull(section);
        ArgumentNullException.ThrowIfNull(failure);
        var first = failure.Split(' ', 2)[0];
        return first.StartsWith(section + ":", StringComparison.Ordinal) ? first : section;
    }

    /// <summary>
    /// The sentence for a value the binder could not convert to its setting's type, in the validator's own shape:
    /// <c>Plugins:Limits:TimeoutMs could not be read as System.Int32.</c> The binder's message repeats the value,
    /// and a value typed onto the wrong line is often a secret, so the message is read for the setting's path and
    /// the type and for nothing else, and each key of the path is written as <see cref="SettingKey.ShownPath"/>
    /// writes it. A message in words the pattern does not know names only the section.
    /// </summary>
    public static string Unbindable(string section, InvalidOperationException binder, IReadOnlySet<string> installedPluginIds)
    {
        ArgumentNullException.ThrowIfNull(section);
        ArgumentNullException.ThrowIfNull(binder);
        ArgumentNullException.ThrowIfNull(installedPluginIds);

        return Read(binder, installedPluginIds) ?? $"{section} holds a value that could not be read.";
    }

    /// <summary>
    /// The sentence <see cref="Unbindable"/> composes when the binder's message ends in words the pattern knows, and null
    /// when it does not, for a caller that has a better sentence of its own than the section's name.
    /// </summary>
    internal static string? Read(InvalidOperationException binder, IReadOnlySet<string> installedPluginIds)
    {
        ArgumentNullException.ThrowIfNull(binder);
        ArgumentNullException.ThrowIfNull(installedPluginIds);

        var said = BinderWords().Match(binder.Message);
        return said.Success
            ? $"{SettingKey.ShownPath(said.Groups["path"].Value, installedPluginIds)} could not be read as {said.Groups["type"].Value}."
            : null;
    }

    /// <summary>
    /// How the binder's message ends: <c>… at 'Section:Key' to type 'System.Int32'.</c> Anchored at the end, because
    /// the binder writes the value before the path, so a value can imitate these very words; only its last words
    /// are its own.
    /// </summary>
    [GeneratedRegex(@"at '(?<path>[^']+)' to type '(?<type>[^']+)'\.?\z")]
    private static partial Regex BinderWords();
}
