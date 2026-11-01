using System.Text.Json.Nodes;
using Jason.Runtime.Hosting;
using Jason.Runtime.Tests.Dispatch;
using Microsoft.Extensions.Logging;

namespace Jason.Runtime.Tests.Hosting;

/// <summary>
/// The warm-up's line says what happened: a warm-up that gave up on a pattern is a warm-up that left that pattern's
/// cold cost to a request's own second match, and a line saying everything was warmed would hide exactly that.
/// </summary>
public sealed class PatternWarmUpLogTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_warm_up_that_finished_every_pattern_says_so_and_warns_of_nothing()
    {
        var logs = new RecordingLogs();

        await new PatternWarmUp(TimingOut.Never(), Logger(logs)).StartAsync(Ct);

        Assert.Empty(logs.Warnings);
        Assert.Contains(logs.All, line => line.StartsWith($"Published schema patterns warmed: {PublishedPatterns()} in ", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_warm_up_that_gave_up_on_patterns_says_how_many_and_where_their_cost_went()
    {
        var logs = new RecordingLogs();

        await new PatternWarmUp(TimingOut.Always(), Logger(logs)).StartAsync(Ct);

        var line = Assert.Single(logs.Warnings);
        Assert.StartsWith($"Published schema patterns warmed: 0; {PublishedPatterns()} did not finish warming in ", line, StringComparison.Ordinal);
        Assert.EndsWith("and are left to a request's own second match", line, StringComparison.Ordinal);
    }

    private static ILogger<PatternWarmUp> Logger(RecordingLogs logs) => new Logger<PatternWarmUp>(new LoggerFactory([logs]));

    /// <summary>How many distinct patterns the published schemas hold, read as plain JSON rather than through the validator.</summary>
    private static int PublishedPatterns() =>
        PatternWarmUp.PublishedSchemas().SelectMany(Patterns).Distinct(StringComparer.Ordinal).Count();

    private static IEnumerable<string> Patterns(JsonNode? node) => node switch
    {
        JsonObject map => map.SelectMany(pair =>
            pair.Key == "pattern" && pair.Value is JsonValue value && value.TryGetValue(out string? pattern)
                ? [pattern]
                : Patterns(pair.Value)),
        JsonArray list => list.SelectMany(Patterns),
        _ => [],
    };
}
