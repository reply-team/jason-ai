using System.Globalization;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Jason.Contracts.Operations;
using Jason.Contracts.Update;
using Jason.Runtime.Journal;
using Jason.Runtime.Plugins.Manifest;
using Microsoft.Extensions.Options;

namespace Jason.Runtime.Configuration;

/// <summary>
/// One message shape for every setting — <c>Section:Key must …; got …</c> — so a misconfigured runtime tells
/// the person who edited the file which line to fix, whether the problem surfaces before the host exists or
/// on start through <c>ValidateOnStart</c>.
/// </summary>
internal static class OptionRules
{
    public static void Range(List<string> failures, string setting, int value, int min, int max)
    {
        if (value < min || value > max)
        {
            failures.Add(string.Create(CultureInfo.InvariantCulture, $"{setting} must be between {min} and {max}; got {value}."));
        }
    }

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
            failures.Add($"Logging:MinimumLevel must be one of {string.Join(", ", Levels)}; got '{options.MinimumLevel}'.");
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
        ValidateKind(failures, "Dispatcher:AiRole", options.AiRole);
        ValidateKind(failures, "Dispatcher:ProviderOp", options.ProviderOp);

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
public sealed partial class PluginsOptionsValidator : IValidateOptions<PluginsOptions>
{
    public ValidateOptionsResult Validate(string? name, PluginsOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var failures = new List<string>();

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

        OptionRules.Range(failures, "Plugins:Invoker:OutcomeBytes", options.Invoker.OutcomeBytes, 65_536, 16_777_216);
        OptionRules.Range(failures, "Plugins:Invoker:StderrBytes", options.Invoker.StderrBytes, 65_536, 67_108_864);
        OptionRules.Range(failures, "Plugins:Invoker:KillGraceMs", options.Invoker.KillGraceMs, 0, 60_000);
        OptionRules.Range(failures, "Plugins:Invoker:VersionCheckTimeoutMs", options.Invoker.VersionCheckTimeoutMs, 1_000, 60_000);
        OptionRules.Range(failures, "Plugins:Invoker:LogLineBytes", options.Invoker.LogLineBytes, 1024, 1_048_576);

        foreach (var (pluginId, grant) in options.Grants)
        {
            if (grant is null)
            {
                failures.Add($"Plugins:Grants:{pluginId} must be an object with Exec, Http and Env lists.");
                continue;
            }

            ValidateGrantList(failures, $"Plugins:Grants:{pluginId}:Exec", grant.Exec);
            ValidateGrantList(failures, $"Plugins:Grants:{pluginId}:Http", grant.Http);
            ValidateGrantList(failures, $"Plugins:Grants:{pluginId}:Env", grant.Env);
        }

        return OptionRules.Result(failures);
    }

    private static void ValidateGrantList(List<string> failures, string setting, List<string> entries)
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
                $"{setting}[{index}] must be \"*\" or the name of an executable, a host or a variable the manifest requests; got '{entry}'."));
        }
    }

    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9._:()-]{0,127}\z")]
    private static partial Regex GrantEntry();
}

/// <summary>
/// The global routes, checked for shape alone. Whether the plugin exists, lists the operation, or accepts the
/// binding is a reload question — settings are read before any package is loaded — so what is refused here is
/// only what could never be right: a plugin nothing could be called, an operation this build does not publish,
/// and a binding that is not an object.
/// </summary>
public sealed class RoutesOptionsValidator : IValidateOptions<RoutesOptions>
{
    public ValidateOptionsResult Validate(string? name, RoutesOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var failures = new List<string>();

        if (options.Default is not null)
        {
            ValidateEntry(failures, $"{RoutesOptions.Section}:Default", options.Default);
        }

        foreach (var (operation, entry) in options.Operations)
        {
            var setting = $"{RoutesOptions.Section}:Operations:{operation}";
            if (!OperationCatalog.Knows(operation))
            {
                failures.Add($"{setting} must name a published operation; got '{operation}'.");
            }

            if (entry is null)
            {
                failures.Add($"{setting} must be an object with Plugin and an optional Binding.");
                continue;
            }

            ValidateEntry(failures, setting, entry);
        }

        return OptionRules.Result(failures);
    }

    private static void ValidateEntry(List<string> failures, string setting, RouteEntry entry)
    {
        if (!ManifestRules.IsPluginId(entry.Plugin))
        {
            failures.Add($"{setting}:Plugin must be a plugin id; got '{entry.Plugin}'.");
        }

        if (entry.Binding is not null and not JsonObject)
        {
            var text = entry.Binding is JsonValue value ? value.ToString() : entry.Binding.ToJsonString();
            failures.Add($"{setting}:Binding must be an object; got '{text}'.");
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
            failures.Add($"Roles:DefaultExecutionProfile must be lowercase letters, digits and hyphens, starting with a letter, at most 64 characters; got '{profile}'.");
        }

        OptionRules.Range(failures, "Roles:MaxStdoutBytes", options.MaxStdoutBytes, 65_536, 67_108_864);
        OptionRules.Range(failures, "Roles:MaxSkillBytes", options.MaxSkillBytes, 4_096, 16_777_216);

        return OptionRules.Result(failures);
    }

    /// <summary>The shape of a role's name, which is the shape a profile's name is written under.</summary>
    [GeneratedRegex("^[a-z][a-z0-9-]{0,63}$")]
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
    public ValidateOptionsResult Validate(string? name, ManagerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var failures = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        for (var index = 0; index < options.Triggers.Count; index++)
        {
            var kind = options.Triggers[index];
            if (!JournalKinds.Reserved.Contains(kind))
            {
                failures.Add(string.Create(
                    CultureInfo.InvariantCulture,
                    $"Manager:Triggers[{index}] must be a chronicle kind the runtime writes itself; got '{kind}'."));
            }
            else if (!seen.Add(kind))
            {
                failures.Add(string.Create(
                    CultureInfo.InvariantCulture,
                    $"Manager:Triggers[{index}] repeats '{kind}'; a kind summons one review however many times it is listed."));
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
            failures.Add($"Update:FeedUrl must be an absolute https address, or http on loopback; got '{options.FeedUrl}'.");
        }

        OptionRules.Range(failures, "Update:InitialDelayMinutes", options.InitialDelayMinutes, 1, 1440);
        OptionRules.Range(failures, "Update:IntervalHours", options.IntervalHours, 1, 168);

        return OptionRules.Result(failures);
    }
}
