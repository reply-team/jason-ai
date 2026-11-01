namespace Jason.Runtime.Configuration;

/// <summary>
/// Strongly typed runtime settings. Three layers, later wins: defaults in code and the shipped
/// <c>appsettings.json</c> next to the binary → the user's <c>~/.jason/config/settings.json</c> (deltas only)
/// → environment variables <c>JASON_*</c> (<c>JASON_Runtime__Port</c>). Code never reads raw configuration keys.
/// </summary>
public sealed class RuntimeOptions
{
    public const string Section = "Runtime";

    /// <summary>"JASON" on a phone keypad: in the dynamic range, where nothing is registered.</summary>
    public const int DefaultPort = 52766;

    /// <summary>
    /// Loopback port for the Runtime API. When it is taken the runtime listens on the next free port above it and
    /// <c>system.info</c> says so; 0 lets the OS choose. The descriptor tells clients the result either way.
    /// </summary>
    public int Port { get; set; } = DefaultPort;
}
