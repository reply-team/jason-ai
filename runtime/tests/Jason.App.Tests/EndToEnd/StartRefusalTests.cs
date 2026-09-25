namespace Jason.App.Tests.EndToEnd;

/// <summary>
/// A runtime that will not start, heard by the command that started it — the shipped program at both ends.
/// </summary>
/// <remarks>
/// <c>jason runtime start</c> launches the runtime detached, and a detached runtime points its standard streams at
/// the null device before it does anything else. So when it refused to start — a data directory it could not
/// prepare — it wrote the reason to nowhere, the CLI read an exit code, and it sent the operator to an empty log
/// directory with advice to run the runtime in the foreground. Only the two real processes together show whether
/// the sentence arrives.
/// </remarks>
public class StartRefusalTests
{
    [Fact]
    public async Task A_detached_runtime_that_will_not_start_is_heard_in_its_own_words()
    {
        using var it = GoldenPath.Create("start-refused");

        // A file where the runtime's first directory goes, so the data directory cannot be prepared.
        await File.WriteAllTextAsync(it.Paths.StateDirectory, "a file where the state directory goes", TestContext.Current.CancellationToken);

        var result = await GoldenPath.JasonAsync(it, "runtime", "start");

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("It said: The data directory", result.Output, StringComparison.Ordinal);
        Assert.Contains("could not be prepared", result.Output, StringComparison.Ordinal);
        Assert.DoesNotContain("jason runtime run", result.Output, StringComparison.Ordinal);
    }
}
