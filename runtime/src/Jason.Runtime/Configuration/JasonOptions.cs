namespace Jason.Runtime.Configuration;

/// <summary>
/// Strongly typed runtime settings. Three layers, later wins: defaults in code and the shipped
/// <c>appsettings.json</c> next to the binary → the user's <c>~/.jason/config/settings.json</c> (deltas only)
/// → environment variables <c>JASON_*</c> (<c>JASON_Runtime__Port</c>). Code never reads raw configuration keys.
/// </summary>
public sealed class JasonOptions
{
    private static readonly string[] Levels = ["Verbose", "Debug", "Information", "Warning", "Error", "Fatal"];

    public RuntimeOptions Runtime { get; set; } = new();
    public LoggingOptions Logging { get; set; } = new();

    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();
        if (Runtime.Port is < 0 or > 65535)
        {
            errors.Add($"Runtime:Port must be between 0 and 65535 (0 = pick a free port); got {Runtime.Port}.");
        }

        if (!Levels.Contains(Logging.MinimumLevel, StringComparer.OrdinalIgnoreCase))
        {
            errors.Add($"Logging:MinimumLevel must be one of {string.Join(", ", Levels)}; got '{Logging.MinimumLevel}'.");
        }

        if (Logging.RetainedFileCountLimit < 1)
        {
            errors.Add($"Logging:RetainedFileCountLimit must be at least 1; got {Logging.RetainedFileCountLimit}.");
        }

        if (Logging.FileSizeLimitBytes < 1024 * 1024)
        {
            errors.Add($"Logging:FileSizeLimitBytes must be at least 1048576; got {Logging.FileSizeLimitBytes}.");
        }

        return errors;
    }
}

public sealed class RuntimeOptions
{
    /// <summary>Loopback port for the Runtime API. 0 lets the OS choose; the descriptor tells clients the result.</summary>
    public int Port { get; set; }
}

public sealed class LoggingOptions
{
    public string MinimumLevel { get; set; } = "Information";
    public int RetainedFileCountLimit { get; set; } = 14;
    public long FileSizeLimitBytes { get; set; } = 200L * 1024 * 1024;
}
