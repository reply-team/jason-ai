using System.Globalization;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Jason.Contracts.Operations;
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
public sealed partial class PluginsOptionsValidator : IValidateOptions<PluginsOptions>
{
    public ValidateOptionsResult Validate(string? name, PluginsOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var failures = new List<string>();

        OptionRules.Range(failures, "Plugins:Limits:TimeoutMs", options.Limits.TimeoutMs, 1_000, 3_600_000);
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

    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9._:()-]{0,127}$")]
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

public sealed class RolesOptionsValidator : IValidateOptions<RolesOptions>
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

        return OptionRules.Result(failures);
    }
}
