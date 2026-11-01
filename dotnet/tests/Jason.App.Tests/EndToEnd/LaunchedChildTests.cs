using System.Text.Json.Nodes;

namespace Jason.App.Tests.EndToEnd;

/// <summary>
/// A child an attempt launched is ended by its number only while the runtime's own record does not say it
/// exited, and it is proven by the moment its number was read, never by the moment the attempt was claimed.
/// </summary>
public class LaunchedChildTests
{
    private static readonly DateTimeOffset ReadAt = new(2026, 9, 14, 12, 0, 30, TimeSpan.Zero);

    private static JsonNode Attempt(string launch) =>
        JsonNode.Parse($$"""{"number":1,"claimed_at":"2026-09-14T12:00:00Z","launch":{{launch}} }""")!;

    [Fact]
    public void A_child_the_launch_says_exited_is_nobody_to_end()
    {
        Assert.Null(LaunchedChild.Of(Attempt("""{"pid":4242,"exit_code":0}"""), ReadAt));
        Assert.Null(LaunchedChild.Of(Attempt("""{"pid":4242,"exit_code":137}"""), ReadAt));
    }

    [Fact]
    public void A_child_without_an_exit_code_is_proven_by_the_moment_its_number_was_read()
    {
        var child = LaunchedChild.Of(Attempt("""{"pid":4242,"exit_code":null}"""), ReadAt);

        // Not the claim, which is half a minute before: a child that began after it, late, would be refused.
        Assert.Equal((4242, ReadAt), child);
        Assert.Equal((4242, ReadAt), LaunchedChild.Of(Attempt("""{"pid":4242}"""), ReadAt));
    }

    [Fact]
    public void An_attempt_that_launched_nothing_names_no_child()
    {
        Assert.Null(LaunchedChild.Of(Attempt("""{"pid":null,"exit_code":null}"""), ReadAt));
        Assert.Null(LaunchedChild.Of(JsonNode.Parse("""{"number":1}"""), ReadAt));
        Assert.Null(LaunchedChild.Of(null, ReadAt));
    }
}
