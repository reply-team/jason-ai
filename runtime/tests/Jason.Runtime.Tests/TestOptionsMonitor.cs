using Jason.Runtime.Configuration;
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
