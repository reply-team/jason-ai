namespace Jason.App.Tests.EndToEnd;

/// <summary>
/// <c>jason runtime run</c> read by the shipped program: <c>--help</c> says what the verb takes and starts nothing, and a flag
/// it does not know is refused rather than ignored.
/// </summary>
public class RuntimeRunHelpTests
{
    [Theory]
    [InlineData("--help")]
    [InlineData("-h")]
    public async Task Help_prints_the_usage_on_stdout_and_starts_nothing(string help)
    {
        using var it = GoldenPath.Create("run-help");
        var before = Entries(it);

        var result = await GoldenPath.JasonAsync(it, "runtime", "run", help);

        Assert.Equal(0, result.ExitCode);
        Assert.StartsWith("usage: jason runtime run [--detached] [--data-dir <path>]", result.Output, StringComparison.Ordinal);
        Assert.Contains("jason runtime start", result.Output, StringComparison.Ordinal);
        Assert.Equal(string.Empty, result.Error);
        Assert.Equal(before, Entries(it));
        Assert.Null(GoldenPath.ReadDescriptor(it.Paths));
    }

    [Fact]
    public async Task A_flag_it_does_not_know_is_refused_and_creates_nothing()
    {
        using var it = GoldenPath.Create("run-unknown-flag");
        var before = Entries(it);

        var result = await GoldenPath.JasonAsync(it, "runtime", "run", "--bogus");

        Assert.Equal(2, result.ExitCode);
        Assert.Equal(string.Empty, result.Output);
        Assert.Contains("usage: jason runtime run [--detached] [--data-dir <path>]", result.Error, StringComparison.Ordinal);
        Assert.Contains("--bogus", result.Error, StringComparison.Ordinal);
        Assert.Equal(before, Entries(it));
        Assert.Null(GoldenPath.ReadDescriptor(it.Paths));
    }

    /// <summary>Every file and directory under the installation's root, so "no database, log or descriptor" is a comparison and not a list of names.</summary>
    private static string[] Entries(Installation it) =>
        [.. Directory.EnumerateFileSystemEntries(it.Root, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal)];
}
