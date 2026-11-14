using System.Globalization;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Jason.Contracts.Channels;
using Jason.Contracts.Configuration;
using Jason.Contracts.Discovery;
using Jason.Contracts.Operations;
using Jason.Contracts.Update;
using Jason.Runtime.Harness;
using Jason.Runtime.Journal;
using Jason.Runtime.Notifications;
using Jason.Runtime.Plugins.Manifest;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace Jason.Runtime.Configuration;

/// <summary>
/// One message shape for every setting — <c>Section:Key must …;</c> and then what was found there — so a
/// misconfigured runtime tells the person who edited the file which line to fix, whether the problem surfaces
/// before the host exists or on start through <c>ValidateOnStart</c>. What was found is a number the binder has
/// already read (<c>got 0.</c>), a word of the code's own (<c>got string.</c>), or, for text a person wrote, a
/// description of it (<see cref="Written"/>): <c>the value written is 37 characters long and is not one.</c>
/// </summary>
/// <remarks>
/// Text a person wrote is never said back: what is typed onto the wrong line is often a secret — a token pasted
/// where a name belonged — and these sentences go to the log, and from there into whatever bundle the log is
/// collected into. So a refused piece of text is described by its length, and a key of a path is written the way
/// <see cref="SettingKey.ShownPath"/> writes it. The text itself stays in the file, where the person who wrote it
/// can read it.
/// </remarks>
internal static class OptionRules
{
    public static void Range(List<string> failures, string setting, int value, int min, int max)
    {
        if (value < min || value > max)
        {
            failures.Add(string.Create(CultureInfo.InvariantCulture, $"{setting} must be between {min} and {max}; got {value}."));
        }
    }

    /// <summary>
    /// The end of a sentence that refuses a piece of text: how long it is and, where that is all it is, that it is
    /// white space — never a character of it. The length is what tells a stray space from a whole token, and it is
    /// as much of the text as a log may keep.
    /// </summary>
    /// <param name="what">What the text is to the setting: <c>value</c>, or <c>key</c> for the name of a map's entry.</param>
    /// <param name="text">The text that was refused.</param>
    /// <param name="refusal">How the sentence ends where there was text, after "and": <c>is not one</c>, say.</param>
    public static string Written(string what, string? text, string refusal) => text switch
    {
        null or "" => "nothing is written there.",
        _ when string.IsNullOrWhiteSpace(text) => $"the {what} written is {Characters(text.Length)} of white space.",
        _ => $"the {what} written is {Characters(text.Length)} long and {refusal}.",
    };

    /// <summary>What a binding that is not an object is instead, in JSON's own words: <c>string</c>, <c>array</c>, <c>true</c>.</summary>
    public static string Kind(JsonNode node) => node.GetValueKind().ToString().ToLowerInvariant();

    private static string Characters(int count) =>
        count == 1 ? "1 character" : string.Create(CultureInfo.InvariantCulture, $"{count} characters");

    public static ValidateOptionsResult Result(List<string> failures) =>
        failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
}

public sealed class RuntimeOptionsValidator : IValidateOptions<RuntimeOptions>
{
    public ValidateOptionsResult Validate(string? name, RuntimeOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var failures = new List<string>();
        OptionRules.Range(failures, "Runtime:Port", options.Port, 0, 65535);
        return OptionRules.Result(failures);
    }
}

public sealed class LoggingOptionsValidator : IValidateOptions<LoggingOptions>
{
    private static readonly string[] Levels = ["Verbose", "Debug", "Information", "Warning", "Error", "Fatal"];

    private const long MinimumFileSizeBytes = 1024 * 1024;

    public ValidateOptionsResult Validate(string? name, LoggingOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var failures = new List<string>();

        if (!Levels.Contains(options.MinimumLevel, StringComparer.OrdinalIgnoreCase))
        {
            failures.Add($"Logging:MinimumLevel must be one of {string.Join(", ", Levels)}; {OptionRules.Written("value", options.MinimumLevel, "is none of them")}");
        }

        if (options.RetainedFileCountLimit < 1)
        {
            failures.Add(string.Create(CultureInfo.InvariantCulture, $"Logging:RetainedFileCountLimit must be at least 1; got {options.RetainedFileCountLimit}."));
        }

        if (options.FileSizeLimitBytes < MinimumFileSizeBytes)
        {
            failures.Add(string.Create(CultureInfo.InvariantCulture, $"Logging:FileSizeLimitBytes must be at least {MinimumFileSizeBytes}; got {options.FileSizeLimitBytes}."));
        }

        return OptionRules.Result(failures);
    }
}

public sealed class DispatcherOptionsValidator : IValidateOptions<DispatcherOptions>
{
    public ValidateOptionsResult Validate(string? name, DispatcherOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var failures = new List<string>();

        OptionRules.Range(failures, "Dispatcher:TickSeconds", options.TickSeconds, 1, 3600);
        OptionRules.Range(failures, "Dispatcher:MaxParallel", options.MaxParallel, 1, 64);
        OptionRules.Range(failures, "Dispatcher:DrainSeconds", options.DrainSeconds, 0, 20);
        OptionRules.Range(failures, "Dispatcher:RetryDelaySeconds", options.RetryDelaySeconds, 0, 86400);
        OptionRules.Range(failures, "Dispatcher:ExitGraceSeconds", options.ExitGraceSeconds, 0, 600);
        OptionRules.Range(failures, "Dispatcher:EventsCadenceSeconds", options.EventsCadenceSeconds, DispatcherOptions.MinimumEventsCadenceSeconds, DispatcherOptions.MaximumEventsCadenceSeconds);
        OptionRules.Range(failures, "Dispatcher:SearchCadenceSeconds", options.SearchCadenceSeconds, DispatcherOptions.MinimumSearchCadenceSeconds, DispatcherOptions.MaximumSearchCadenceSeconds);
        OptionRules.Range(failures, "Dispatcher:SearchStallSeconds", options.SearchStallSeconds, DispatcherOptions.MinimumSearchStallSeconds, DispatcherOptions.MaximumSearchStallSeconds);
        OptionRules.Range(failures, "Dispatcher:SearchHeldCadenceSeconds", options.SearchHeldCadenceSeconds, DispatcherOptions.MinimumSearchCadenceSeconds, DispatcherOptions.MaximumSearchCadenceSeconds);
        OptionRules.Range(failures, "Dispatcher:SearchHeldStallSeconds", options.SearchHeldStallSeconds, DispatcherOptions.MinimumSearchStallSeconds, DispatcherOptions.MaximumSearchStallSeconds);
        if (failures.Count == 0 && options.SearchStallSeconds < options.SearchCadenceSeconds)
        {
            failures.Add(string.Create(
                CultureInfo.InvariantCulture,
                $"Dispatcher:SearchStallSeconds ({options.SearchStallSeconds}) is below Dispatcher:SearchCadenceSeconds ({options.SearchCadenceSeconds}): every search whose first read lands nobody would be stalled at once."));
        }

        if (failures.Count == 0 && options.SearchHeldStallSeconds < options.SearchHeldCadenceSeconds)
        {
            failures.Add(string.Create(
                CultureInfo.InvariantCulture,
                $"Dispatcher:SearchHeldStallSeconds ({options.SearchHeldStallSeconds}) is below Dispatcher:SearchHeldCadenceSeconds ({options.SearchHeldCadenceSeconds}): every held search would be stalled at its first read."));
        }
        ValidateKind(failures, "Dispatcher:AiRole", options.AiRole);
        if (options.AiRole is { } role)
        {
            OptionRules.Range(failures, "Dispatcher:AiRole:MaxWaitSeconds", role.MaxWaitSeconds, AiRoleDefaults.MinimumWaitSeconds, AiRoleDefaults.MaximumWaitSeconds);
            OptionRules.Range(failures, "Dispatcher:AiRole:MaxWaits", role.MaxWaits, 1, AiRoleDefaults.MaximumWaits);
        }
        ValidateKind(failures, "Dispatcher:ProviderOp", options.ProviderOp);
        if (options.Blocks is { } blocks)
        {
            OptionRules.Range(failures, "Dispatcher:Blocks:TrialSeconds", blocks.TrialSeconds, BlockDefaults.MinimumTrialSeconds, BlockDefaults.MaximumTrialSeconds);
        }

        return OptionRules.Result(failures);
    }

    private static void ValidateKind(List<string> failures, string section, KindDefaults defaults)
    {
        if (defaults is null)
        {
            failures.Add($"{section} must be an object with TimeoutSeconds, HeartbeatSeconds and MaxAttempts.");
            return;
        }

        OptionRules.Range(failures, $"{section}:TimeoutSeconds", defaults.TimeoutSeconds, 30, 86400);
        if (defaults.HeartbeatSeconds != 0 && defaults.HeartbeatSeconds is < 10 or > 3600)
        {
            failures.Add(string.Create(CultureInfo.InvariantCulture, $"{section}:HeartbeatSeconds must be 0 or between 10 and 3600; got {defaults.HeartbeatSeconds}."));
        }

        OptionRules.Range(failures, $"{section}:MaxAttempts", defaults.MaxAttempts, 1, 10);
    }
}

/// <summary>
/// The plugin limits and the grants. Every ceiling is validated against the default it caps, so a settings file
/// that lowers <c>MaxTimeoutMs</c> below <c>TimeoutMs</c> is caught before a plugin runs under an impossible
/// budget; every grant entry must be something a manifest could have requested.
/// </summary>
/// <remarks>
/// One of those ceilings is measured against the published contracts rather than against another setting. The
/// default timeout is what a package that declares no limit of its own runs under, and a ceiling can only lower
/// an operation's budget: set below the slowest operation this build publishes, it kills a child partway through
/// work the runtime itself asked for and answers ambiguous, with nothing on the attempt to say why. The floor
/// therefore comes from the catalog, so publishing a slower operation moves it rather than leaving it stale.
/// </remarks>
/// <param name="installedPluginIds">
/// The plugins installed in this data directory, listed when there is a sentence to compose: a grant is keyed by
/// the id of the plugin it is for, and a key travels into a sentence only when it names one that is installed.
/// </param>
public sealed partial class PluginsOptionsValidator(Func<IReadOnlySet<string>> installedPluginIds) : IValidateOptions<PluginsOptions>
{
    /// <summary>A validator that knows of no installed plugin, so every grant's key is written as a placeholder.</summary>
    public PluginsOptionsValidator()
        : this(SafeBinding.NoInstalledPlugins)
    {
    }

    public ValidateOptionsResult Validate(string? name, PluginsOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var failures = new List<string>();
        var installed = new Lazy<IReadOnlySet<string>>(installedPluginIds);

        OptionRules.Range(failures, "Plugins:Limits:TimeoutMs", options.Limits.TimeoutMs, 1_000, 3_600_000);
        if (OperationCatalog.Slowest is { } slowest && options.Limits.TimeoutMs < slowest.TimeoutMs)
        {
            failures.Add(string.Create(
                CultureInfo.InvariantCulture,
                $"Plugins:Limits:TimeoutMs must be at least {slowest.TimeoutMs} to hold '{slowest.Id}', the slowest operation this build publishes, for a package that declares no limit of its own; got {options.Limits.TimeoutMs}."));
        }

        OptionRules.Range(failures, "Plugins:Limits:MaxTimeoutMs", options.Limits.MaxTimeoutMs, options.Limits.TimeoutMs, 86_400_000);
        OptionRules.Range(failures, "Plugins:Limits:MemoryMb", options.Limits.MemoryMb, 16, 1024);
        OptionRules.Range(failures, "Plugins:Limits:MaxMemoryMb", options.Limits.MaxMemoryMb, options.Limits.MemoryMb, 4096);
        OptionRules.Range(failures, "Plugins:Limits:MaxStatements", options.Limits.MaxStatements, 100_000, 1_000_000_000);
        OptionRules.Range(failures, "Plugins:Limits:MaxRecursion", options.Limits.MaxRecursion, 8, 1024);

        OptionRules.Range(failures, "Plugins:Exec:OutputBytes", options.Exec.OutputBytes, 65_536, 67_108_864);
        OptionRules.Range(failures, "Plugins:Exec:MaxCalls", options.Exec.MaxCalls, 1, 1000);

        OptionRules.Range(failures, "Plugins:Http:ResponseBytes", options.Http.ResponseBytes, 65_536, 67_108_864);
        OptionRules.Range(failures, "Plugins:Http:RequestBytes", options.Http.RequestBytes, 1024, 16_777_216);
        OptionRules.Range(failures, "Plugins:Http:MaxCalls", options.Http.MaxCalls, 1, 1000);
        OptionRules.Range(failures, "Plugins:Http:TimeoutMs", options.Http.TimeoutMs, 1_000, 300_000);

        OptionRules.Range(failures, "Plugins:Files:LineBytes", options.Files.LineBytes, 1024, 16_777_216);
        OptionRules.Range(failures, "Plugins:Files:MaxCalls", options.Files.MaxCalls, 1, 1000);

        OptionRules.Range(failures, "Plugins:Invoker:OutcomeBytes", options.Invoker.OutcomeBytes, 65_536, 16_777_216);
        OptionRules.Range(failures, "Plugins:Invoker:StderrBytes", options.Invoker.StderrBytes, 65_536, 67_108_864);
        OptionRules.Range(failures, "Plugins:Invoker:KillGraceMs", options.Invoker.KillGraceMs, 0, 60_000);
        OptionRules.Range(failures, "Plugins:Invoker:VersionCheckTimeoutMs", options.Invoker.VersionCheckTimeoutMs, 1_000, 60_000);
        OptionRules.Range(failures, "Plugins:Invoker:LogLineBytes", options.Invoker.LogLineBytes, 1024, 1_048_576);

        foreach (var (pluginId, grant) in options.Grants)
        {
            if (grant is null)
            {
                failures.Add($"{SettingKey.ShownPath($"Plugins:Grants:{pluginId}", installed.Value)} must be an object with Exec, Http, Env, Api, Subscriptions and Files lists.");
                continue;
            }

            ValidateGrantList(failures, $"Plugins:Grants:{pluginId}:Exec", grant.Exec, installed);
            ValidateGrantList(failures, $"Plugins:Grants:{pluginId}:Http", grant.Http, installed);
            ValidateGrantList(failures, $"Plugins:Grants:{pluginId}:Env", grant.Env, installed);
            ValidateGrantList(failures, $"Plugins:Grants:{pluginId}:Api", grant.Api, installed);
            ValidateSubscriptionGrant(failures, $"Plugins:Grants:{pluginId}:Subscriptions", grant.Subscriptions, installed);
        }

        // A key with children never reaches this map: the binder leaves it out and the section's refusal says so. A list
        // directly under a plugin or at the section does reach it, flattened into keys of digits, and those are all that
        // tell it from a map.
        foreach (var (pluginId, entries) in options.Config)
        {
            if (IsIndex(pluginId))
            {
                failures.Add($"{SettingKey.ShownPath($"Plugins:Config:{pluginId}", installed.Value)} must be a plugin's id: this section is a map from a plugin's id to its configuration, and a list is not one.");
            }
            else if (entries.Keys.FirstOrDefault(IsIndex) is { } index)
            {
                failures.Add($"{SettingKey.ShownPath($"Plugins:Config:{pluginId}:{index}", installed.Value)} must be a name: a plugin's configuration is a flat map from a name to text, and a list is not one.");
            }
        }

        return OptionRules.Result(failures);
    }

    private static bool IsIndex(string key) => key.Length > 0 && key.All(char.IsAsciiDigit);

    private static void ValidateGrantList(List<string> failures, string setting, List<string> entries, Lazy<IReadOnlySet<string>> installed)
    {
        for (var index = 0; index < entries.Count; index++)
        {
            var entry = entries[index];
            if (entry is "*" || (entry is not null && GrantEntry().IsMatch(entry)))
            {
                continue;
            }

            failures.Add(string.Create(
                CultureInfo.InvariantCulture,
                $"{SettingKey.ShownPath(setting, installed.Value)}[{index}] must be \"*\" or the name of an executable, a host, a variable or an api family the manifest requests; {OptionRules.Written("value", entry, "is not one")}"));
        }
    }

    private static void ValidateSubscriptionGrant(List<string> failures, string setting, List<string> entries, Lazy<IReadOnlySet<string>> installed)
    {
        for (var index = 0; index < entries.Count; index++)
        {
            var entry = entries[index];
            if (entry is "*" || (entry is not null && JournalKinds.IsWellFormed(entry)))
            {
                continue;
            }

            failures.Add(string.Create(
                CultureInfo.InvariantCulture,
                $"{SettingKey.ShownPath(setting, installed.Value)}[{index}] must be \"*\" or a journal kind the manifest declares; {OptionRules.Written("value", entry, "is not one")}"));
        }
    }

    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9._:()-]{0,127}\z")]
    private static partial Regex GrantEntry();
}

/// <summary>
/// The global routes, checked for shape alone. Whether the plugin exists, lists the operation, or accepts the
/// binding is a reload question — settings are read before any package is loaded — so what is refused here is
/// only what could never be right: a plugin nothing could be called, an operation this build does not publish, a
/// channel that is no channel, and a binding that is not an object.
/// </summary>
/// <param name="installedPluginIds">
/// The plugins installed in this data directory, listed when there is a sentence to compose. A route is keyed by
/// an operation, and a key that names neither a published operation nor anything else the code knows is written
/// as a placeholder; this is the one rule every key of a settings path is held to.
/// </param>
public sealed class RoutesOptionsValidator(Func<IReadOnlySet<string>> installedPluginIds) : IValidateOptions<RoutesOptions>
{
    /// <summary>A validator that knows of no installed plugin, so a key is shown only when the code itself knows it.</summary>
    public RoutesOptionsValidator()
        : this(SafeBinding.NoInstalledPlugins)
    {
    }

    public ValidateOptionsResult Validate(string? name, RoutesOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var failures = new List<string>();
        var installed = new Lazy<IReadOnlySet<string>>(installedPluginIds);

        if (options.Default is not null)
        {
            ValidateEntry(failures, $"{RoutesOptions.Section}:Default", options.Default, installed);
        }

        foreach (var (operation, entry) in options.Operations)
        {
            var setting = $"{RoutesOptions.Section}:Operations:{operation}";
            if (!OperationCatalog.Knows(operation))
            {
                failures.Add($"{SettingKey.ShownPath(setting, installed.Value)} must name a published operation; {OptionRules.Written("key", operation, "is not one")}");
            }

            if (entry is null)
            {
                failures.Add($"{SettingKey.ShownPath(setting, installed.Value)} must be an object with Plugin and an optional Binding, or with Channels.");
                continue;
            }

            ValidateOperation(failures, setting, entry, installed);
        }

        return OptionRules.Result(failures);
    }

    /// <summary>
    /// An operation's entry: its own route, unless it names channel routes and leaves its own out, and then each channel
    /// route. A channel is held here to being a channel at all — a name the channel rules keep, spelled as they keep it —
    /// so a key that is not one is written as a placeholder, as an operation that is not one is; whether the operation
    /// takes channel routes, and that channel, is the reload's question, asked with the codes a campaign's route is
    /// answered with.
    /// </summary>
    private static void ValidateOperation(List<string> failures, string setting, OperationRouteEntry entry, Lazy<IReadOnlySet<string>> installed)
    {
        if (entry.ChannelsUnreadable)
        {
            failures.Add($"{SettingKey.ShownPath(setting, installed.Value)}:Channels must be a map of channels, each an object with Plugin and an optional Binding.");
        }

        if (entry.Plugin is not null || entry.Channels.Count == 0)
        {
            ValidateEntry(failures, setting, entry, installed);
        }
        else if (entry.Binding is not null)
        {
            failures.Add($"{SettingKey.ShownPath(setting, installed.Value)}:Binding must be left out where Plugin is left out: an operation routed by its channels alone names no account of its own.");
        }

        foreach (var (channel, route) in entry.Channels)
        {
            var path = $"{setting}:Channels:{channel}";
            if (!ChannelRules.IsKnown(channel))
            {
                failures.Add($"{SettingKey.ShownPath(path, installed.Value)} must name a channel; {OptionRules.Written("key", channel, "is not one")}");
            }

            if (route is null)
            {
                failures.Add($"{SettingKey.ShownPath(path, installed.Value)} must be an object with Plugin and an optional Binding.");
                continue;
            }

            ValidateEntry(failures, path, route, installed);
        }
    }

    private static void ValidateEntry(List<string> failures, string setting, RouteEntry entry, Lazy<IReadOnlySet<string>> installed)
    {
        if (!ManifestRules.IsPluginId(entry.Plugin))
        {
            failures.Add($"{SettingKey.ShownPath(setting, installed.Value)}:Plugin must be a plugin id; {OptionRules.Written("value", entry.Plugin, "is not one")}");
        }

        if (entry.Binding is not null and not JsonObject)
        {
            failures.Add($"{SettingKey.ShownPath(setting, installed.Value)}:Binding must be an object; got {OptionRules.Kind(entry.Binding)}.");
        }
    }
}

/// <summary>
/// How roles are launched when nothing more specific says. The global default profile is held to the shape of a
/// name and nothing more: settings are read before the database is open, so whether a profile of that name
/// exists is not a question this validator can ask.
/// </summary>
public sealed partial class RolesOptionsValidator : IValidateOptions<RolesOptions>
{
    public ValidateOptionsResult Validate(string? name, RolesOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var failures = new List<string>();

        for (var index = 0; index < options.DefaultEntryCommand.Count; index++)
        {
            if (string.IsNullOrWhiteSpace(options.DefaultEntryCommand[index]))
            {
                failures.Add(string.Create(CultureInfo.InvariantCulture, $"Roles:DefaultEntryCommand[{index}] must not be blank."));
            }
        }

        // Optional, and only a name. A well-formed name nothing answers is not a reason to refuse to start —
        // the profile it names may be created a minute later — so it becomes a visible refusal on the work item
        // when that item is claimed, where the person who has to fix it can see which work it stopped.
        if (options.DefaultExecutionProfile is { } profile && !ProfileName().IsMatch(profile))
        {
            failures.Add($"Roles:DefaultExecutionProfile must be lowercase letters, digits and hyphens, starting with a letter, at most 64 characters; {OptionRules.Written("value", profile, "does not have that shape")}");
        }

        OptionRules.Range(failures, "Roles:MaxStdoutBytes", options.MaxStdoutBytes, 65_536, 67_108_864);
        OptionRules.Range(failures, "Roles:MaxSkillBytes", options.MaxSkillBytes, 4_096, 16_777_216);

        return OptionRules.Result(failures);
    }

    /// <summary>The shape of a role's name, which is the shape a profile's name is written under.</summary>
    [GeneratedRegex(@"^[a-z][a-z0-9-]{0,63}\z")]
    private static partial Regex ProfileName();
}

/// <summary>
/// The manager loop. The list of triggers is the part worth refusing a start over: a kind that is not one the
/// runtime writes itself is a rule that can never fire, or — worse — one anybody could fire by appending a line
/// of that name to the chronicle. An installation whose review rules quietly do nothing is one that believes it
/// is being managed and is not.
/// </summary>
public sealed class ManagerOptionsValidator : IValidateOptions<ManagerOptions>
{
    /// <summary>The refusal of a list written with no kind in it, as the start, a live edit and the manager page say it.</summary>
    public const string EmptyTriggers = "Manager:Triggers is written but lists no trigger kind: leave it out for the defaults, or list the kinds.";

    public ValidateOptionsResult Validate(string? name, ManagerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var failures = new List<string>();
        var first = new Dictionary<string, int>(StringComparer.Ordinal);

        if (options.Triggers.Count == 0)
        {
            // Only a list the file wrote with nothing in it is empty here: one it left out is the defaults.
            failures.Add(EmptyTriggers);
        }

        for (var index = 0; index < options.Triggers.Count; index++)
        {
            var kind = options.Triggers[index];
            if (!JournalKinds.Reserved.Contains(kind))
            {
                failures.Add(string.Create(
                    CultureInfo.InvariantCulture,
                    $"Manager:Triggers[{index}] must be a chronicle kind the runtime writes itself; {OptionRules.Written("value", kind, "is not one")}"));
            }
            else if (!first.TryAdd(kind, index))
            {
                // Named by the line it repeats rather than by what it says, like every other refusal here.
                failures.Add(string.Create(
                    CultureInfo.InvariantCulture,
                    $"Manager:Triggers[{index}] repeats Manager:Triggers[{first[kind]}]; a kind summons one review however many times it is listed."));
            }
        }

        OptionRules.Range(
            failures,
            "Manager:ReviewSeconds",
            options.ReviewSeconds,
            ManagerOptions.MinimumReviewSeconds,
            ManagerOptions.MaximumReviewSeconds);
        OptionRules.Range(failures, "Manager:TimeoutSeconds", options.TimeoutSeconds, 30, 86_400);
        OptionRules.Range(failures, "Manager:MaxAttempts", options.MaxAttempts, 1, 10);

        // A work item takes any priority, so this bound is the loop's own: a mistyped number that put every
        // review permanently ahead of the work it is reviewing would be a hard thing to notice from the outside.
        OptionRules.Range(failures, "Manager:Priority", options.Priority, -1_000, 1_000);
        OptionRules.Range(failures, "Manager:MaxEntriesPerScan", options.MaxEntriesPerScan, 50, 10_000);

        return OptionRules.Result(failures);
    }
}

/// <summary>
/// The inbound read's cadence and look-back, each within the bounds the page prints.
/// </summary>
public sealed class InboundOptionsValidator : IValidateOptions<InboundOptions>
{
    public ValidateOptionsResult Validate(string? name, InboundOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var failures = new List<string>();

        OptionRules.Range(
            failures,
            "Inbound:SyncSeconds",
            options.SyncSeconds,
            InboundOptions.MinimumSyncSeconds,
            InboundOptions.MaximumSyncSeconds);
        OptionRules.Range(failures, "Inbound:LookbackSeconds", options.LookbackSeconds, 0, InboundOptions.MaximumLookbackSeconds);

        return OptionRules.Result(failures);
    }
}

/// <summary>
/// Each role's daily limit: a key shaped like a role's name, a number between one and a thousand. A key the code does not
/// know is shown as a placeholder, and the sentence that refuses one says its length and never its text.
/// </summary>
/// <remarks>
/// The keys this number used to live under are found here, by their path in the configuration, so a file and a variable
/// are the same refusal; a key a person kept is a limit they chose, and passing over it would raise it to the default
/// without a word. The sentence names the path, never what is written there, and a refused live edit keeps the last good
/// limits like any other.
/// </remarks>
/// <param name="configuration">The whole configuration, which the retired keys are looked up in.</param>
public sealed partial class LaunchesOptionsValidator(IConfiguration configuration) : IValidateOptions<LaunchesOptions>
{
    private const string MapPath = LaunchesOptions.Section + ":" + nameof(LaunchesOptions.MaxPerDay);

    private static readonly IReadOnlySet<string> NoPluginIds = new HashSet<string>();

    /// <summary>Every key retired from this section's place, with the one that replaced it.</summary>
    private static readonly (string Section, string Key, string Replacement)[] RetiredKeys =
    [
        (ResponderOptions.Section, "MaxLaunchesPerDay", MapPath + ":responder"),
    ];

    public ValidateOptionsResult Validate(string? name, LaunchesOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var failures = new List<string>();

        foreach (var (section, key, replacement) in RetiredKeys)
        {
            // By the children, since a key written as null or as an empty object still counts as written.
            if (configuration.GetSection(section).GetChildren().Any(child => string.Equals(child.Key, key, StringComparison.OrdinalIgnoreCase)))
            {
                failures.Add($"{section}:{key} was renamed to {replacement}; carry the value over by hand and take the old key out.");
            }
        }

        foreach (var (role, limit) in options.MaxPerDay)
        {
            var setting = SettingKey.ShownPath($"{MapPath}:{role}", NoPluginIds);
            if (!RoleName().IsMatch(role))
            {
                failures.Add(
                    $"{setting} must be a role's name, lowercase letters, digits and hyphens starting with a letter, at most 64 characters; "
                    + OptionRules.Written("key", role, "does not have that shape"));
                continue;
            }

            OptionRules.Range(failures, setting, limit, LaunchesOptions.MinimumPerDay, LaunchesOptions.MaximumPerDay);
        }

        return OptionRules.Result(failures);
    }

    /// <summary>The shape of a role's name, as <see cref="RolesOptionsValidator"/> holds a profile's name to it.</summary>
    [GeneratedRegex(@"^[a-z][a-z0-9-]{0,63}\z")]
    private static partial Regex RoleName();
}

/// <summary>
/// The daily ceiling of automatic sends, within its bounds: at least one, since a person who wants none sets the mode to
/// every message, and at most a thousand, since the ceiling never slips.
/// </summary>
public sealed class SendingOptionsValidator : IValidateOptions<SendingOptions>
{
    public ValidateOptionsResult Validate(string? name, SendingOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var failures = new List<string>();

        OptionRules.Range(
            failures,
            SendingOptions.Section + ":" + nameof(SendingOptions.AutomaticMaxPerDay),
            options.AutomaticMaxPerDay,
            SendingOptions.MinimumPerDay,
            SendingOptions.MaximumPerDay);

        return OptionRules.Result(failures);
    }
}

/// <summary>
/// The respond step's numbers, each within its bounds: the queue's range, the time a check-in is held to and an age
/// between an hour and thirty days.
/// </summary>
public sealed class ResponderOptionsValidator : IValidateOptions<ResponderOptions>
{
    public ValidateOptionsResult Validate(string? name, ResponderOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var failures = new List<string>();

        OptionRules.Range(failures, "Responder:Priority", options.Priority, -1_000, 1_000);
        OptionRules.Range(failures, "Responder:TimeoutSeconds", options.TimeoutSeconds, 30, 86_400);
        OptionRules.Range(failures, "Responder:MaxAttempts", options.MaxAttempts, 1, 10);
        OptionRules.Range(
            failures,
            "Responder:MaxAgeSeconds",
            options.MaxAgeSeconds,
            ResponderOptions.MinimumMaxAgeSeconds,
            ResponderOptions.MaximumMaxAgeSeconds);

        return OptionRules.Result(failures);
    }
}

/// <summary>
/// The harness root, refused where it may never be: a relative path; the home directory, a folder holding it or the top
/// of a drive, where every session of the account would read its memory files; the data directory, a folder inside it
/// or one holding it (<see cref="HarnessRoot.Refusal"/>), each compared where it leads. Nothing here makes or opens a
/// file: it asks only whether the parts of a path are links, and whether the folder exists or can be made is a question
/// for the first create that needs it, not for the start.
/// </summary>
/// <param name="paths">The data directory this runtime keeps its state in.</param>
/// <param name="home">The home directory the default root is made in, and the one folder the root may never be.</param>
public sealed class HarnessOptionsValidator(JasonPaths paths, string home) : IValidateOptions<HarnessOptions>
{
    public ValidateOptionsResult Validate(string? name, HarnessOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return HarnessRoot.Refusal(options, paths, home) is { } refusal ? ValidateOptionsResult.Fail(refusal) : ValidateOptionsResult.Success;
    }
}

/// <summary>
/// The update check. The feed is held to the reader's own rule — https, or http on loopback — at start rather
/// than at the first check: a feed the reader would refuse is a fact about the settings file, and the person
/// who wrote it is at the keyboard now, not five minutes later when the first check logs a warning nobody reads.
/// </summary>
public sealed class UpdateOptionsValidator : IValidateOptions<UpdateOptions>
{
    public ValidateOptionsResult Validate(string? name, UpdateOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var failures = new List<string>();

        if (!Uri.TryCreate(options.FeedUrl, UriKind.Absolute, out var feed) || !UpdateFeed.IsAllowed(feed))
        {
            failures.Add($"Update:FeedUrl must be an absolute https address, or http on loopback; {OptionRules.Written("value", options.FeedUrl, "is not one")}");
        }

        OptionRules.Range(
            failures,
            "Update:InitialDelayMinutes",
            options.InitialDelayMinutes,
            UpdateOptions.MinimumInitialDelayMinutes,
            UpdateOptions.MaximumInitialDelayMinutes);

        OptionRules.Range(
            failures,
            "Update:IntervalHours",
            options.IntervalHours,
            UpdateOptions.MinimumIntervalHours,
            UpdateOptions.MaximumIntervalHours);

        return OptionRules.Result(failures);
    }
}

/// <summary>
/// The notification settings, refused on start where a number is outside the range the coalescing rule can honour,
/// a plugin is named by something no package could be called, or a binding is not an object a plugin could read.
/// </summary>
/// <param name="installedPluginIds">
/// The plugins installed in this data directory, listed when there is a sentence to compose: a channel is keyed by
/// the id of the plugin that delivers it, and a key travels into a sentence only when it names one that is installed.
/// </param>
public sealed class NotificationsOptionsValidator(Func<IReadOnlySet<string>> installedPluginIds) : IValidateOptions<NotificationsOptions>
{
    /// <summary>A validator that knows of no installed plugin, so every channel's key is written as a placeholder.</summary>
    public NotificationsOptionsValidator()
        : this(SafeBinding.NoInstalledPlugins)
    {
    }

    public ValidateOptionsResult Validate(string? name, NotificationsOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var failures = new List<string>();
        var installed = new Lazy<IReadOnlySet<string>>(installedPluginIds);

        OptionRules.Range(failures, "Notifications:QuietSeconds", options.QuietSeconds, NotificationsOptions.QuietSecondsMin, NotificationsOptions.QuietSecondsMax);
        OptionRules.Range(failures, "Notifications:MaxWaitSeconds", options.MaxWaitSeconds, Math.Max(NotificationsOptions.QuietSecondsMin, options.QuietSeconds), NotificationsOptions.MaxWaitSecondsMax);
        OptionRules.Range(failures, "Notifications:MinIntervalMinutes", options.MinIntervalMinutes, NotificationsOptions.MinIntervalMinutesMin, NotificationsOptions.MinIntervalMinutesMax);

        foreach (var (id, entry) in options.Plugins)
        {
            if (!ManifestRules.IsPluginId(id))
            {
                failures.Add($"Notifications:Plugins must name each plugin by its id: a lowercase letter, then lowercase letters, digits or dashes; {OptionRules.Written("key", id, "is not one")}");
                continue;
            }

            if (string.Equals(id, "toast", StringComparison.Ordinal))
            {
                failures.Add("Notifications:Plugins cannot name a plugin 'toast': that is the operating system's notification, the channel Notifications:Toast turns on and off.");
                continue;
            }

            // Refused rather than read through: an exception out of a validator is not a refusal anybody catches, and it
            // stops the runtime instead of keeping the settings it had.
            if (entry is null)
            {
                failures.Add($"{SettingKey.ShownPath($"Notifications:Plugins:{id}", installed.Value)} must be an object with an optional Enabled and an optional Binding.");
                continue;
            }

            if (entry.Binding is not null and not JsonObject)
            {
                failures.Add($"{SettingKey.ShownPath($"Notifications:Plugins:{id}:Binding", installed.Value)} must be an object; got {OptionRules.Kind(entry.Binding)}.");
            }
        }

        return OptionRules.Result(failures);
    }
}
