using Microsoft.Extensions.DependencyInjection;

namespace Jason.Runtime.Hosting;

/// <param name="ShippedSettingsDirectory">Directory holding the shipped <c>appsettings.json</c> (the install directory); null = none.</param>
/// <param name="ConsoleLogging">Mirror logs to the console (foreground runs).</param>
/// <param name="Clock">The runtime's clock; null means the system clock. A test moves time with its own.</param>
/// <param name="FeedHandler">
/// Where the update feed's requests go; null means the network. A test installs its own here, exactly where a
/// real runtime would send, so what it proves about the check is proved against the composed runtime.
/// </param>
/// <param name="ConfigureServices">Runs last while composing, so a test can register a service after the real one.</param>
/// <param name="Refusals">
/// Where a runtime that will not start says why; null means standard error. A detached runtime has pointed its
/// standard error at the null device before it gets that far, so it hands in the one it was started with — the
/// only way <c>jason runtime start</c> hears the reason. <see cref="RuntimeHost.RunAsync"/> closes it once the
/// runtime has started or refused, so nothing of the process that started it is held for longer than that.
/// </param>
public sealed record RuntimeHostOptions(
    string? ShippedSettingsDirectory = null,
    bool ConsoleLogging = true,
    TimeProvider? Clock = null,
    HttpMessageHandler? FeedHandler = null,
    Action<IServiceCollection>? ConfigureServices = null,
    TextWriter? Refusals = null);
