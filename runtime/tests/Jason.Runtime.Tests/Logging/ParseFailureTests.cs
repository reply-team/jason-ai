using System.Text.Json;
using Jason.Runtime.Logging;
using Jason.Runtime.Persistence;

namespace Jason.Runtime.Tests.Logging;

/// <summary>
/// A read failure is said by its kind and where the reader stopped, because the parser's message can quote what it
/// read and its path can carry a key somebody typed. What is said here is all a log line gets, so each part is held:
/// the type, and the position counted the way a person with the payload open counts.
/// </summary>
public class ParseFailureTests
{
    private const string Sentinel = "SENTINEL_PARSE";

    private const string SentinelKey = "SENTINEL_KEY";

    [Fact]
    public void A_syntax_error_is_said_by_its_position_and_not_by_the_parsers_words()
    {
        var failure = Assert.ThrowsAny<JsonException>(() => JsonDocument.Parse("{\n  \"a\": " + Sentinel + "\n}"));

        // The reader stops on the value's first character, the eighth byte of the second line as an editor counts: from 1.
        Assert.Equal("JsonException at line 2, byte 8", ParseFailure.Describe(failure));
        Assert.DoesNotContain(Sentinel, ParseFailure.Describe(failure), StringComparison.Ordinal);
    }

    [Fact]
    public void A_value_that_cannot_be_converted_is_said_by_its_position_and_not_by_its_path()
    {
        var failure = Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<Price>("{\"Amount\":\"" + Sentinel + "\"}"));

        Assert.Matches(@"^JsonException at line 1, byte \d+$", ParseFailure.Describe(failure));
        Assert.DoesNotContain(Sentinel, ParseFailure.Describe(failure), StringComparison.Ordinal);
    }

    /// <summary>
    /// The path the parser stopped at names a key a person typed when the object is free-form, and a key is as much
    /// their text as a value is: only names the code itself knows may travel, and a path is not one of them.
    /// </summary>
    [Fact]
    public void A_key_somebody_typed_is_not_said_even_though_the_path_names_it()
    {
        var failure = Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<Dictionary<string, int>>("{\"" + SentinelKey + "\":\"many\"}"));
        Assert.Contains(SentinelKey, failure.Path, StringComparison.Ordinal);

        Assert.DoesNotContain(SentinelKey, ParseFailure.Describe(failure), StringComparison.Ordinal);
        Assert.Matches(@"^JsonException at line 1, byte \d+$", ParseFailure.Describe(failure));
    }

    [Fact]
    public void A_failure_found_inside_wrappers_is_said_as_the_failure_it_wraps()
    {
        var failure = Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<Price>("{\"Amount\":\"" + Sentinel + "\"}"));
        var wrapped = new InvalidOperationException("outer", new AggregateException("middle", failure));

        Assert.True(ParseFailure.Is(wrapped));
        Assert.Equal(ParseFailure.Describe(failure), ParseFailure.Describe(wrapped));
    }

    /// <summary>
    /// An aggregate keeps all of what failed, and only the first of them is its inner exception: a read failure that
    /// came after another one is as much a read failure as the first.
    /// </summary>
    [Fact]
    public void A_failure_found_beyond_the_first_of_an_aggregates_failures_is_found()
    {
        var failure = Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<Price>("{\"Amount\":\"" + Sentinel + "\"}"));
        var aggregate = new AggregateException(new InvalidOperationException("first"), new InvalidOperationException("second", failure));

        Assert.True(ParseFailure.Is(aggregate));
        Assert.Equal(ParseFailure.Describe(failure), ParseFailure.Describe(aggregate));
    }

    [Fact]
    public void A_read_failure_that_knows_no_position_is_said_by_its_type_alone()
    {
        var failure = new JsonException("a message quoting " + Sentinel);

        Assert.True(ParseFailure.Is(failure));
        Assert.Equal("JsonException", ParseFailure.Describe(failure));
    }

    [Fact]
    public void A_failure_that_is_not_a_read_failure_is_not_one_and_is_still_said_by_its_type()
    {
        var failure = new InvalidOperationException("not a parse " + Sentinel);

        Assert.False(ParseFailure.Is(failure));
        Assert.Equal("InvalidOperationException", ParseFailure.Describe(failure));
    }

    /// <summary>
    /// A stored value that cannot be read is a read failure, found inside whatever wraps it and said by its column as
    /// well: the column is a name the model gives it, and the failure it stands for is not kept at all.
    /// </summary>
    [Fact]
    public void A_stored_value_that_cannot_be_read_is_said_by_its_column_and_its_failure_inside_whatever_wraps_it()
    {
        var failure = Assert.ThrowsAny<JsonException>(() => JsonDocument.Parse("{\"a\": n" + Sentinel + "}"));
        var stored = UnreadableStoredValueException.From("campaigns.context_json", failure);
        var wrapped = new InvalidOperationException("outer", stored);

        Assert.True(ParseFailure.Is(wrapped));
        Assert.Equal("the value stored in campaigns.context_json cannot be read (JsonException at line 1, byte 8)", ParseFailure.Describe(wrapped));
        Assert.Null(stored.InnerException);
        Assert.DoesNotContain(Sentinel, stored.ToString(), StringComparison.Ordinal);
    }

    private sealed record Price(int Amount);
}
