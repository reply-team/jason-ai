using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Jason.Cli.Logs;
using Jason.Contracts.Configuration;

namespace Jason.Cli.Doctor;

/// <summary>
/// The one list of settings whose values may leave this machine in a diagnostic bundle, and the rule for each: a
/// value is carried only when it is of the kind its setting is meant to hold. A setting that is not on the list
/// carries no value, whatever it holds.
/// </summary>
/// <remarks>
/// <para>
/// The list is made of two kinds of setting. Some are a switch or a word from a closed set — whether the dispatcher
/// is on, how much the runtime logs, which chronicle kinds summon a manager review — and say how the runtime is set
/// up. The rest are a number with a size, a count, a time or a port, each named one by one. Everything else a
/// settings file can hold is a name, an address, a command, a program, a grant or a route's binding, and those are
/// where a secret typed onto the wrong line ends up, so none of them is ever listed. What is named as never carried
/// is named in the tests, so nobody adds it by accident.
/// </para>
/// <para>
/// A value is held to its setting's kind as well as to its path. A word the runtime would refuse is free text and
/// free text is what a secret looks like, so a level or a trigger kind travels only when it is one of the closed
/// set — the same sets the runtime's own validator reads — and a number only when it is an integer the setting's own
/// type holds, since a number the runtime would refuse is never said back by it either. A path is matched
/// whole, from its first key to its last and without regard to case, because configuration keys are matched that
/// way and because two settings share a last key: <c>Plugins:Exec:MaxCalls</c> is listed and a grant's own
/// <c>Exec</c> is not.
/// </para>
/// </remarks>
public sealed class SettingsAllowlist
{
    /// <summary>
    /// What stands for any one key in a listed path: the id of a plugin. Whether that key may be printed is for the key
    /// rule to say and not for this list, which only ever judges a value at a path whose keys have already been shown.
    /// </summary>
    private const string AnyPlugin = "<id>";

    /// <summary>What stands for one item of a list: <c>Manager:Triggers[]</c>, or an index as in <c>Manager:Triggers:0</c>.</summary>
    private const string Item = "[]";

    private enum Kind
    {
        /// <summary>true or false.</summary>
        Switch,

        /// <summary>One of the six names the runtime's log level reads.</summary>
        LogLevel,

        /// <summary>A chronicle kind the runtime writes itself.</summary>
        TriggerKind,

        /// <summary>A JSON integer the runtime binds to a 32-bit integer: all but one of the numbers on the list.</summary>
        Integer32,

        /// <summary>A JSON integer the runtime binds to a 64-bit integer: the size a log file may reach.</summary>
        Integer64,
    }

    private sealed record Entry(string Path, string[] Segments, Kind Kind)
    {
        public Entry(string path, Kind kind)
            : this(path, Split(path), kind)
        {
        }
    }

    private readonly Entry[] _entries;

    private SettingsAllowlist(IEnumerable<Entry> entries)
    {
        _entries = [.. entries];
        Published = [.. _entries.Select(entry => $"{entry.Path} ({Describe(entry.Kind)})")];
    }

    /// <summary>The list this client carries.</summary>
    public static SettingsAllowlist Default { get; } = new(
    [
        // How the runtime is set up: a switch each.
        new("Dispatcher:Enabled", Kind.Switch),
        new("Update:CheckEnabled", Kind.Switch),
        new("Notifications:Enabled", Kind.Switch),
        new("Notifications:Toast", Kind.Switch),
        new($"Notifications:Plugins:{AnyPlugin}:Enabled", Kind.Switch),
        new("Inbound:Enabled", Kind.Switch),

        // And a word from a closed set, or a list of them.
        new("Logging:MinimumLevel", Kind.LogLevel),
        new($"Manager:Triggers{Item}", Kind.TriggerKind),

        // A number with a size, a count, a time or a port, each named.
        new("Runtime:Port", Kind.Integer32),
        new("Logging:RetainedFileCountLimit", Kind.Integer32),
        new("Logging:FileSizeLimitBytes", Kind.Integer64),
        new("Dispatcher:TickSeconds", Kind.Integer32),
        new("Dispatcher:MaxParallel", Kind.Integer32),
        new("Dispatcher:DrainSeconds", Kind.Integer32),
        new("Dispatcher:RetryDelaySeconds", Kind.Integer32),
        new("Dispatcher:ExitGraceSeconds", Kind.Integer32),
        new("Dispatcher:AiRole:TimeoutSeconds", Kind.Integer32),
        new("Dispatcher:AiRole:HeartbeatSeconds", Kind.Integer32),
        new("Dispatcher:AiRole:MaxAttempts", Kind.Integer32),
        new("Dispatcher:ProviderOp:TimeoutSeconds", Kind.Integer32),
        new("Dispatcher:ProviderOp:HeartbeatSeconds", Kind.Integer32),
        new("Dispatcher:ProviderOp:MaxAttempts", Kind.Integer32),
        new("Manager:ReviewSeconds", Kind.Integer32),
        new("Manager:TimeoutSeconds", Kind.Integer32),
        new("Manager:MaxAttempts", Kind.Integer32),
        new("Manager:Priority", Kind.Integer32),
        new("Manager:MaxEntriesPerScan", Kind.Integer32),
        new("Roles:MaxStdoutBytes", Kind.Integer32),
        new("Roles:MaxSkillBytes", Kind.Integer32),
        new("Update:InitialDelayMinutes", Kind.Integer32),
        new("Update:IntervalHours", Kind.Integer32),
        new("Notifications:QuietSeconds", Kind.Integer32),
        new("Notifications:MaxWaitSeconds", Kind.Integer32),
        new("Notifications:MinIntervalMinutes", Kind.Integer32),
        new("Plugins:Limits:TimeoutMs", Kind.Integer32),
        new("Plugins:Limits:MaxTimeoutMs", Kind.Integer32),
        new("Plugins:Limits:MemoryMb", Kind.Integer32),
        new("Plugins:Limits:MaxMemoryMb", Kind.Integer32),
        new("Plugins:Limits:MaxStatements", Kind.Integer32),
        new("Plugins:Limits:MaxRecursion", Kind.Integer32),
        new("Plugins:Exec:OutputBytes", Kind.Integer32),
        new("Plugins:Exec:MaxCalls", Kind.Integer32),
        new("Plugins:Http:ResponseBytes", Kind.Integer32),
        new("Plugins:Http:RequestBytes", Kind.Integer32),
        new("Plugins:Http:MaxCalls", Kind.Integer32),
        new("Plugins:Http:TimeoutMs", Kind.Integer32),
        new("Plugins:Invoker:OutcomeBytes", Kind.Integer32),
        new("Plugins:Invoker:StderrBytes", Kind.Integer32),
        new("Plugins:Invoker:KillGraceMs", Kind.Integer32),
        new("Plugins:Invoker:VersionCheckTimeoutMs", Kind.Integer32),
        new("Plugins:Invoker:LogLineBytes", Kind.Integer32),
        new("Inbound:SyncSeconds", Kind.Integer32),
        new("Inbound:LookbackSeconds", Kind.Integer32),
    ]);

    /// <summary>
    /// The list as a person reads it, one line for each path and what kind of value travels there, for <c>--help</c>
    /// to print. It is made from the entries themselves, so the text a person is shown is the list the code applies.
    /// </summary>
    public IReadOnlyList<string> Published { get; }

    /// <summary>
    /// Whether the value at this path is carried: the path is on the list and the value is of the kind the list says.
    /// </summary>
    /// <param name="path">
    /// The setting as the configuration system spells it, <c>Section:Key:…</c>, in whatever case it was written. An item
    /// of a list is the list's path followed by <c>[]</c>, or by its index as in <c>Manager:Triggers:0</c>.
    /// </param>
    /// <param name="value">What is written there. Nothing, which is how a JSON null parses, is never carried.</param>
    public bool Carries(string path, JsonNode? value)
    {
        ArgumentNullException.ThrowIfNull(path);
        if (value is null)
        {
            return false;
        }

        var segments = Split(path);
        return _entries.Any(entry => Matches(entry.Segments, segments) && Accepts(entry.Kind, value));
    }

    private static string Describe(Kind kind) => kind switch
    {
        Kind.Switch => "true or false",
        Kind.LogLevel => "one of " + string.Join(", ", Enum.GetNames<LogLevelName>().Select(name => name.ToLowerInvariant())) + ", in any case",
        Kind.TriggerKind => "a kind the runtime writes into its own chronicle",
        Kind.Integer32 => "a 32-bit integer",
        Kind.Integer64 => "a 64-bit integer",
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    private static bool Accepts(Kind kind, JsonNode value) => kind switch
    {
        Kind.Switch => value is JsonValue && value.GetValueKind() is JsonValueKind.True or JsonValueKind.False,
        Kind.LogLevel => TextOf(value) is { } level && LogLevels.TryParse(level, out _),
        Kind.TriggerKind => TextOf(value) is { } trigger && ManagerTriggerKinds.All.Contains(trigger),
        Kind.Integer32 => IsInteger32(value),
        Kind.Integer64 => IsInteger64(value),
        _ => false,
    };

    private static string? TextOf(JsonNode value) =>
        value is JsonValue text && text.GetValueKind() == JsonValueKind.String && text.TryGetValue<string>(out var result) ? result : null;

    /// <summary>
    /// A JSON integer of the width the setting is bound to: digits with an optional sign that the type holds. Read from
    /// the number's own text, so that <c>2.0</c> and <c>1e3</c> are not integers, and a number the type cannot hold is
    /// not one — the runtime's binder refuses all three for an integer setting too, and says only which setting it was,
    /// never what was written there.
    /// </summary>
    private static bool IsInteger32(JsonNode value) =>
        NumberText(value) is { } text && int.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out _);

    private static bool IsInteger64(JsonNode value) =>
        NumberText(value) is { } text && long.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out _);

    private static string? NumberText(JsonNode value) =>
        value is JsonValue number && number.GetValueKind() == JsonValueKind.Number ? number.ToJsonString() : null;

    /// <summary>A path as its keys, with a trailing <c>[]</c> as one more.</summary>
    private static string[] Split(string path)
    {
        var isItem = path.EndsWith(Item, StringComparison.Ordinal);
        var keys = (isItem ? path[..^Item.Length] : path).Split(':');
        return isItem ? [.. keys, Item] : keys;
    }

    /// <summary>
    /// Whether every key matches, one for one. A listed key matches the same word in any case; the id of a plugin
    /// matches any one key; an item matches <c>[]</c> or an index.
    /// </summary>
    private static bool Matches(string[] listed, string[] path)
    {
        if (listed.Length != path.Length)
        {
            return false;
        }

        for (var at = 0; at < listed.Length; at++)
        {
            var matches = listed[at] switch
            {
                AnyPlugin => path[at].Length > 0 && path[at] != Item && !KeyPlaceholder.Is(path[at]),
                Item => path[at] == Item || IsIndex(path[at]),
                var key => string.Equals(key, path[at], StringComparison.OrdinalIgnoreCase),
            };

            if (!matches)
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsIndex(string key) => key.Length > 0 && key.All(char.IsAsciiDigit);
}
