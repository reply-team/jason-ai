namespace Jason.Runtime.Configuration;

/// <summary>
/// Strongly typed runtime settings. Three layers, later wins: defaults in code and the shipped
/// <c>appsettings.json</c> next to the binary → the user's <c>~/.jason/config/settings.json</c> (deltas only)
/// → environment variables <c>JASON_*</c> (<c>JASON_Runtime__Port</c>). Code never reads raw configuration keys.
/// </summary>
public sealed class RuntimeOptions
{
    public const string Section = "Runtime";

    /// <summary>Loopback port for the Runtime API. 0 lets the OS choose; the descriptor tells clients the result.</summary>
    public int Port { get; set; }
}
