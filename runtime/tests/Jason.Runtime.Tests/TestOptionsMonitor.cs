using Jason.Runtime.Configuration;
using Jason.Runtime.Tests.Dispatch;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Jason.Runtime.Tests;

/// <summary>Options a test owns outright: set <see cref="CurrentValue"/> and the next read sees it.</summary>
public sealed class TestOptionsMonitor<T>(T value) : IOptionsMonitor<T>
{
    public T CurrentValue { get; set; } = value;

    public T Get(string? name) => CurrentValue;

    public IDisposable? OnChange(Action<T, string?> listener) => null;
}

/// <summary>
/// A monitor that has never had a value to give: every read is the validator's refusal. It is what the settings
/// look like from the inside when the file was already broken and nothing has ever read one that was not.
/// </summary>
public sealed class ThrowingOptionsMonitor<T>(OptionsValidationException failure) : IOptionsMonitor<T>
{
    public T CurrentValue => throw failure;

    public T Get(string? name) => CurrentValue;

    public IDisposable? OnChange(Action<T, string?> listener) => null;
}

public static class TestOptions
{
    /// <summary>Dispatcher options with the shipped defaults, adjusted by the test.</summary>
    public static TestOptionsMonitor<DispatcherOptions> Dispatcher(Action<DispatcherOptions>? configure = null)
    {
        var options = new DispatcherOptions();
        configure?.Invoke(options);
        return new TestOptionsMonitor<DispatcherOptions>(options);
    }

    /// <summary>The guarded seam over test options, for the many tests that build a dispatcher part by hand.</summary>
    public static LiveSettings<DispatcherOptions> Settings(Action<DispatcherOptions>? configure = null) =>
        Seam(Dispatcher(configure), DispatcherOptions.Section);

    /// <summary>The same seam over options the test already holds, so it can still change them afterwards.</summary>
    public static LiveSettings<DispatcherOptions> Settings(IOptionsMonitor<DispatcherOptions> monitor) =>
        Seam(monitor, DispatcherOptions.Section);

    /// <summary>The seam over the plugins section, for the parts that read one number out of it.</summary>
    public static LiveSettings<PluginsOptions> PluginSettings(Action<PluginsOptions>? configure = null) =>
        Seam(Plugins(configure), PluginsOptions.Section);

    /// <summary>The same seam over a value the test already built.</summary>
    public static LiveSettings<PluginsOptions> PluginSettings(PluginsOptions options) =>
        Seam(new TestOptionsMonitor<PluginsOptions>(options), PluginsOptions.Section);

    /// <summary>The same seam over a monitor the test owns, so it can change the value afterwards.</summary>
    public static LiveSettings<PluginsOptions> PluginSettings(IOptionsMonitor<PluginsOptions> monitor) =>
        Seam(monitor, PluginsOptions.Section);

    /// <summary>The seam over the roles section, which is read for one list: how a role is launched.</summary>
    public static LiveSettings<RolesOptions> RoleSettings(RolesOptions? options = null) =>
        Seam(new TestOptionsMonitor<RolesOptions>(options ?? new RolesOptions()), RolesOptions.Section);

    /// <summary>The seam of a runtime that has never read settings the validator accepted.</summary>
    public static LiveSettings<DispatcherOptions> NothingValidated() =>
        Settings(new ThrowingOptionsMonitor<DispatcherOptions>(new OptionsValidationException(
            DispatcherOptions.Section,
            typeof(DispatcherOptions),
            ["Dispatcher:TickSeconds must be between 1 and 3600; got 0."])));

    /// <summary>The plugins seam of a runtime that has never read a section the validator accepted.</summary>
    public static LiveSettings<PluginsOptions> NoPluginsValidated() =>
        Seam(
            new ThrowingOptionsMonitor<PluginsOptions>(new OptionsValidationException(
                PluginsOptions.Section,
                typeof(PluginsOptions),
                ["Plugins:Limits:MemoryMb must be between 16 and 1024; got 0."])),
            PluginsOptions.Section);

    /// <summary>
    /// Waits until the validator has refused the settings this test wrote, and for nothing else. The options
    /// system notices a file on its own schedule, so a request that arrives first reads the settings that were
    /// still good — and a test that inferred "the edit is in force" from "the request succeeded" would pass with
    /// the seam taken out again. The seam's own counter is the one event that says the broken file has been read.
    /// </summary>
    /// <remarks>
    /// The condition is "has refused", not "has refused exactly once". <c>Refusals</c> counts every refused read
    /// in the process, and this settings section has more than one reader — <c>ProviderOpBudgetValidator</c>
    /// consults it whenever the dispatcher's own options are validated — so a caller who happens to read while
    /// the watcher is landing the edit can carry the count past one before this poll ever sees it. Asking for
    /// exactly one made the wait an assertion about what the rest of the process did, which is neither the
    /// caller's claim nor anything it can control, and it failed intermittently under a full run.
    /// </remarks>
    public static Task<bool> RefusedAsync<TOptions>(LiveSettings<TOptions> settings, CancellationToken ct)
        where TOptions : class =>
        DispatchHarness.EventuallyAsync(
            () =>
            {
                settings.TryCurrent(out _);
                return settings.Refusals >= 1;
            },
            ct);

    private static LiveSettings<TOptions> Seam<TOptions>(IOptionsMonitor<TOptions> monitor, string section)
        where TOptions : class =>
        new(monitor, NullLogger<LiveSettings<TOptions>>.Instance, section);

    /// <summary>Plugin options with the shipped defaults, adjusted by the test.</summary>
    public static TestOptionsMonitor<PluginsOptions> Plugins(Action<PluginsOptions>? configure = null)
    {
        var options = new PluginsOptions();
        configure?.Invoke(options);
        return new TestOptionsMonitor<PluginsOptions>(options);
    }
}
