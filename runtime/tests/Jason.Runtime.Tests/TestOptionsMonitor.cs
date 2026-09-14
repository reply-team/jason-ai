using Jason.Runtime.Configuration;
using Microsoft.Extensions.Options;

namespace Jason.Runtime.Tests;

/// <summary>Options a test owns outright: set <see cref="CurrentValue"/> and the next read sees it.</summary>
public sealed class TestOptionsMonitor<T>(T value) : IOptionsMonitor<T>
{
    public T CurrentValue { get; set; } = value;

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
}
