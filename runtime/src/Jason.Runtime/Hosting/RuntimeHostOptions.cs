using Microsoft.Extensions.DependencyInjection;

namespace Jason.Runtime.Hosting;

/// <param name="ShippedSettingsDirectory">Directory holding the shipped <c>appsettings.json</c> (the install directory); null = none.</param>
/// <param name="ConsoleLogging">Mirror logs to the console (foreground runs).</param>
/// <param name="Clock">The runtime's clock; null means the system clock. A test moves time with its own.</param>
/// <param name="ConfigureServices">Runs last while composing, so a test can register a service after the real one.</param>
public sealed record RuntimeHostOptions(
    string? ShippedSettingsDirectory = null,
    bool ConsoleLogging = true,
    TimeProvider? Clock = null,
    Action<IServiceCollection>? ConfigureServices = null);
