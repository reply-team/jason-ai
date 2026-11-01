using Jason.Cli.Logs;
using Jason.Runtime.Configuration;
using Serilog.Events;

namespace Jason.Runtime.Tests.Configuration;

/// <summary>
/// The log levels the command-line client reads, held to the levels the runtime accepts for <c>Logging:MinimumLevel</c>.
/// The client keeps its own six names, for <c>jason logs --level</c> and for telling a level from other text in a
/// settings file, because it cannot reference the runtime; so the two agree only as long as a test makes them. The
/// runtime's list is private to its validator, so it is read the only way anything reads it, by asking.
/// </summary>
public sealed class LogLevelAgreementTests
{
    /// <summary>
    /// Every word either side has a reason to know, in three cases, and the near misses a person might type: the client
    /// reads a word as a level exactly when the runtime accepts it as one.
    /// </summary>
    [Fact]
    public void The_client_reads_a_word_as_a_level_exactly_when_the_runtime_accepts_it()
    {
        var names = Enum.GetNames<LogLevelName>().Concat(Enum.GetNames<LogEventLevel>());
        var candidates = names
            .SelectMany(name => new[] { name, name.ToLowerInvariant(), name.ToUpperInvariant() })
            .Concat(["Trace", "Critical", "None", "Off", "Warn", "Info", "Err", "Warning ", " Warning", "", "0", "3", "Information,Warning"])
            .Distinct(StringComparer.Ordinal)
            .ToList();

        var validator = new LoggingOptionsValidator();
        foreach (var word in candidates)
        {
            var accepted = validator.Validate(null, new LoggingOptions { MinimumLevel = word }).Succeeded;

            Assert.True(accepted == LogLevels.TryParse(word, out _), $"'{word}': the runtime accepts it as a level: {accepted}.");
        }
    }

    /// <summary>The six, by name, so that the test above cannot pass by agreeing on nothing.</summary>
    [Fact]
    public void The_six_levels_are_the_ones_serilog_names_and_the_runtime_accepts()
    {
        var validator = new LoggingOptionsValidator();

        Assert.Equal(
            ["Verbose", "Debug", "Information", "Warning", "Error", "Fatal"],
            Enum.GetNames<LogLevelName>());
        Assert.Equal(Enum.GetNames<LogEventLevel>(), Enum.GetNames<LogLevelName>());
        Assert.All(
            Enum.GetNames<LogLevelName>(),
            name => Assert.True(validator.Validate(null, new LoggingOptions { MinimumLevel = name }).Succeeded, name));
    }
}
