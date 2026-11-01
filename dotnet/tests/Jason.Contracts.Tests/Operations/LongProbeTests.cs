using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Jason.Contracts.Operations;
using Jason.Contracts.Plugins;

namespace Jason.Contracts.Tests.OperationContracts;

/// <summary>
/// A pattern slow only on a long value passed the short probe and timed out where a long binding or answer was matched
/// against it. The probes are two now: the short string, and the same string repeated to the longest binding a route can
/// carry, each under the dialect's own time. Measured on every platform the build runs on, a warm match of 64 KiB took
/// well under a millisecond for the shapes a schema is written in, so the long probe keeps the dialect's 100 ms.
/// </summary>
public class LongProbeTests
{
    private static JsonObject Patterned(string pattern) => new() { ["type"] = "string", ["pattern"] = pattern };

    [Fact]
    public void A_pattern_that_finishes_the_short_probe_but_never_a_long_value_is_refused_naming_the_long_probe()
    {
        var problem = Assert.Single(SchemaValidator.ProbePatterns(Patterned("^[a-z]+$"), new SlowOnLongValues()));

        Assert.Equal("schema_pattern_invalid", problem.Reason);
        Assert.Contains($"a {PluginProtocol.MaxBindingBytes}-character probe", problem.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void The_long_probe_is_matched_only_after_the_short_one_finishes_and_is_as_long_as_the_longest_binding()
    {
        var timing = new Recording();

        Assert.Empty(SchemaValidator.ProbePatterns(Patterned("^[a-z]+$"), timing));

        Assert.Equal([20, PluginProtocol.MaxBindingBytes], timing.Lengths);
    }

    [Fact]
    public void A_pattern_that_never_finishes_the_short_probe_is_refused_for_it_and_the_long_one_is_not_tried()
    {
        var timing = new Recording { Always = true };

        var problem = Assert.Single(SchemaValidator.ProbePatterns(Patterned("^[a-z]+$"), timing));

        Assert.Contains("a 20-character probe", problem.Message, StringComparison.Ordinal);
        Assert.All(timing.Lengths, length => Assert.Equal(20, length));
    }

    /// <summary>A timing under which a match never finishes on a value longer than a thousand characters.</summary>
    private sealed class SlowOnLongValues : PatternTiming
    {
        public override bool IsMatch(Regex pattern, string value) =>
            value.Length > 1024
                ? throw new RegexMatchTimeoutException(value[..16], pattern.ToString(), TimeSpan.FromMilliseconds(SchemaValidator.PatternTimeoutMilliseconds))
                : base.IsMatch(pattern, value);
    }

    /// <summary>A timing that records the length of every value it is asked to match, and can refuse them all.</summary>
    private sealed class Recording : PatternTiming
    {
        public List<int> Lengths { get; } = [];

        public bool Always { get; init; }

        public override bool IsMatch(Regex pattern, string value)
        {
            Lengths.Add(value.Length);
            return Always
                ? throw new RegexMatchTimeoutException(value[..Math.Min(16, value.Length)], pattern.ToString(), TimeSpan.FromMilliseconds(SchemaValidator.PatternTimeoutMilliseconds))
                : base.IsMatch(pattern, value);
        }
    }
}
