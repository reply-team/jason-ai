namespace Jason.Runtime.Configuration;

/// <summary>How much the runtime writes, and how much of it is kept.</summary>
public sealed class LoggingOptions
{
    public const string Section = "Logging";

    public string MinimumLevel { get; set; } = "Information";

    public int RetainedFileCountLimit { get; set; } = 14;

    public long FileSizeLimitBytes { get; set; } = 200L * 1024 * 1024;
}
