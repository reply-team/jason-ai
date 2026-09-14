using System.Globalization;
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
