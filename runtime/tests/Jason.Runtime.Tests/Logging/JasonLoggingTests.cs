using Jason.Runtime.Configuration;
using Jason.Runtime.Logging;

namespace Jason.Runtime.Tests.Logging;

public class JasonLoggingTests
{
    [Fact]
    public void Writes_json_lines_into_the_logs_directory()
    {
        using var dir = new TempDataDir();
        var options = new LoggingOptions { MinimumLevel = "Debug" };

        using (var logger = JasonLogging.Create(dir.Paths, options, console: false))
        {
            logger.Information("hello {Name}", "world");
            logger.Debug("debug line");
        }

        var files = Directory.GetFiles(dir.Paths.LogsDirectory, "runtime-*.jsonl");
        var file = Assert.Single(files);
        var lines = File.ReadAllLines(file);
        Assert.Equal(2, lines.Length);
        Assert.Contains("\"@mt\":\"hello {Name}\"", lines[0], StringComparison.Ordinal);
        Assert.Contains("\"Name\":\"world\"", lines[0], StringComparison.Ordinal);
        Assert.StartsWith("{", lines[1], StringComparison.Ordinal);
    }

    [Fact]
    public void Respects_the_minimum_level()
    {
        using var dir = new TempDataDir();
        using (var logger = JasonLogging.Create(dir.Paths, new LoggingOptions { MinimumLevel = "Warning" }, console: false))
        {
            logger.Information("dropped");
            logger.Warning("kept");
        }

        var text = File.ReadAllText(Directory.GetFiles(dir.Paths.LogsDirectory).Single());
        Assert.DoesNotContain("dropped", text, StringComparison.Ordinal);
        Assert.Contains("kept", text, StringComparison.Ordinal);
    }
}
