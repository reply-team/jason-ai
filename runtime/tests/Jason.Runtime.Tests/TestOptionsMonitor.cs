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
    public static DispatcherSettings Settings(Action<DispatcherOptions>? configure = null) =>
        new(Dispatcher(configure), NullLogger<DispatcherSettings>.Instance);

    /// <summary>The same seam over options the test already holds, so it can still change them afterwards.</summary>
    public static DispatcherSettings Settings(IOptionsMonitor<DispatcherOptions> monitor) =>
        new(monitor, NullLogger<DispatcherSettings>.Instance);

    /// <summary>The seam of a runtime that has never read settings the validator accepted.</summary>
    public static DispatcherSettings NothingValidated() =>
        Settings(new ThrowingOptionsMonitor<DispatcherOptions>(new OptionsValidationException(
            DispatcherOptions.Section,
            typeof(DispatcherOptions),
            ["Dispatcher:TickSeconds must be between 1 and 3600; got 0."])));

    /// <summary>Plugin options with the shipped defaults, adjusted by the test.</summary>
    public static TestOptionsMonitor<PluginsOptions> Plugins(Action<PluginsOptions>? configure = null)
    {
        var options = new PluginsOptions();
        configure?.Invoke(options);
        return new TestOptionsMonitor<PluginsOptions>(options);
    }
}
