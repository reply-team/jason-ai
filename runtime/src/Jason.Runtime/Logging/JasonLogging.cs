using Jason.Contracts.Discovery;
using Jason.Runtime.Configuration;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using Serilog.Formatting.Compact;

namespace Jason.Runtime.Logging;

/// <summary>
/// Technical diagnostics as JSON Lines under <c>logs/</c> (the primary log reader is an agent), plus the
/// console when running in the foreground. Never logs request bodies, prompts, contexts or the token.
/// </summary>
public static class JasonLogging
{
    public static Logger Create(JasonPaths paths, LoggingOptions options, bool console)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(options);
        Directory.CreateDirectory(paths.LogsDirectory);

        var level = Enum.Parse<LogEventLevel>(options.MinimumLevel, ignoreCase: true);
        var configuration = new LoggerConfiguration()
            .MinimumLevel.Is(level)
            .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
            .MinimumLevel.Override("Microsoft.EntityFrameworkCore", LogEventLevel.Warning)
            .Enrich.FromLogContext()
            .WriteTo.File(
                new CompactJsonFormatter(),
                Path.Combine(paths.LogsDirectory, "runtime-.jsonl"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: options.RetainedFileCountLimit,
                fileSizeLimitBytes: options.FileSizeLimitBytes,
                rollOnFileSizeLimit: true);

        if (console)
        {
            configuration = configuration.WriteTo.Console();
        }

        return configuration.CreateLogger();
    }
}
