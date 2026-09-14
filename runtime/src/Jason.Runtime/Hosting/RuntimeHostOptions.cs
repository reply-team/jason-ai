namespace Jason.Runtime.Hosting;

/// <param name="ShippedSettingsDirectory">Directory holding the shipped <c>appsettings.json</c> (the install directory); null = none.</param>
/// <param name="ConsoleLogging">Mirror logs to the console (foreground runs).</param>
public sealed record RuntimeHostOptions(string? ShippedSettingsDirectory = null, bool ConsoleLogging = true);
