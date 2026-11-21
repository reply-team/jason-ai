namespace Jason.Cli.Tests;

/// <summary>
/// One verb run twice on the same machine, without <c>--human</c> and then with it: each run's exit code and stdout are
/// held to the literal the test states, the envelope byte for byte and the sentence word for word.
/// </summary>
internal static class HumanErrorPins
{
    public static async Task BothAsync(Func<bool, Task<(int Exit, string Out)>> run, int exitCode, string plain, string human)
    {
        var without = await run(false);
        var with = await run(true);

        Assert.Equal(exitCode, without.Exit);
        Assert.Equal(exitCode, with.Exit);
        Pin(plain, without.Out);
        Pin(human, with.Out);
    }

    /// <summary>What was printed is <paramref name="expected"/> and one line ending, nothing before and nothing after.</summary>
    public static void Pin(string expected, string actual) => Assert.Equal(expected + Environment.NewLine, actual);
}
