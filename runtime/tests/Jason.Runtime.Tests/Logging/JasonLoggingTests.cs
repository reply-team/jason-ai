using System.Text.Json.Nodes;
using Jason.Runtime.Configuration;
using Jason.Runtime.Logging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog;

namespace Jason.Runtime.Tests.Logging;

public class JasonLoggingTests
{
    [Fact]
    [Critical(Family.B3, "logs are JSON Lines")]
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

    /// <summary>
    /// The scope an attempt is logged under is only worth anything if the file a person reads has it. Whether a
    /// scope over key/value pairs becomes properties is the sink's behaviour and not this runtime's, so it is
    /// proved against the real one, wired the way the runtime wires it, and not against a logger of a test's own.
    /// </summary>
    [Fact]
    public void A_line_written_inside_an_attempts_scope_carries_its_three_ids_as_properties()
    {
        using var dir = new TempDataDir();
        using (var serilog = JasonLogging.Create(dir.Paths, new LoggingOptions { MinimumLevel = "Debug" }, console: false))
        {
            var services = new ServiceCollection();
            services.AddSerilog(serilog, dispose: false);
            using var provider = services.BuildServiceProvider();
            var logger = provider.GetRequiredService<ILoggerFactory>().CreateLogger("Jason.Runtime.Tests");

            logger.LogInformation("before");
            using (AttemptLogScope.Begin(logger, "cmp_01", "wi_02", "att_03"))
            {
                logger.LogInformation("inside {Detail}", "d");
            }

            logger.LogInformation("after");
        }

        var lines = File.ReadAllLines(Directory.GetFiles(dir.Paths.LogsDirectory, "runtime-*.jsonl").Single())
            .Select(line => JsonNode.Parse(line)!.AsObject())
            .ToList();
        Assert.Equal(3, lines.Count);

        var inside = lines[1];
        Assert.Equal("cmp_01", (string?)inside["CampaignId"]);
        Assert.Equal("wi_02", (string?)inside["WorkItemId"]);
        Assert.Equal("att_03", (string?)inside["AttemptId"]);
        Assert.Equal("d", (string?)inside["Detail"]);

        // Before the scope and after it the line is its own: nothing leaks past the end of an attempt.
        foreach (var outside in new[] { lines[0], lines[2] })
        {
            Assert.False(outside.ContainsKey("CampaignId"));
            Assert.False(outside.ContainsKey("WorkItemId"));
            Assert.False(outside.ContainsKey("AttemptId"));
        }
    }
}
