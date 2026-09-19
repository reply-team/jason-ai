namespace Jason.Runtime.Tests.Hosting;

/// <summary>
/// The suite's promise that nothing it starts talks to the internet, enforced over the source rather than
/// trusted. A runtime composed in a test has a real transport for the release feed unless it is given another
/// one, and the five-minute initial delay is the only other thing between it and a request — which holds until
/// somebody writes a test that keeps a runtime alive, or shortens the delay to exercise something.
/// </summary>
/// <remarks>
/// This is written the way <c>ArchitectureTests</c> is, and for the same reason: the rule is about every call
/// site, including the ones not written yet, and a rule like that cannot be asserted from inside one test.
/// </remarks>
public class OfflineRuntimeTests
{
    /// <summary>
    /// There is one declaration of the options a test runtime is started with. Ten identical ones were ten
    /// places to forget, which is how nineteen runtimes came to be composed with a live transport pointed at a
    /// public web page and nothing but a five-minute delay in the way.
    /// </summary>
    [Fact]
    public void There_is_one_declaration_of_the_shared_options()
    {
        var declarations = Lines()
            .Where(line => line.Text.Contains("RuntimeHostOptions Quiet", StringComparison.Ordinal))
            .Select(Where)
            .ToList();

        Assert.True(declarations.Count == 1, $"the shared options are declared {declarations.Count} times: {string.Join(", ", declarations)}");
    }

    /// <summary>
    /// And nothing else builds host options at all — which is what makes the rule hold for call sites this test
    /// cannot read. Every options value under the test tree is <see cref="TestRuntimeOptions.Quiet"/> or a
    /// <c>with</c> of it, whether it is passed inline, held in a local or returned by a helper, so every test
    /// runtime carries the transport that refuses the release feed.
    /// </summary>
    [Fact]
    public void Nothing_else_builds_host_options()
    {
        var built = Lines()
            .Where(line => line.Text.Contains("new RuntimeHostOptions(", StringComparison.Ordinal))
            .Select(Where)
            .ToList();

        Assert.True(built.Count == 0, $"host options are built by hand at: {string.Join(", ", built)}");
    }

    /// <summary>
    /// And that transport is replaced in exactly one place: the tests whose subject the check is. Anywhere else
    /// it would be a runtime quietly given a way to the network, which is the thing all of this prevents.
    /// </summary>
    [Fact]
    public void The_feed_transport_is_replaced_only_where_the_check_itself_is_under_test()
    {
        var replaced = Lines()
            .Where(line => line.Text.Contains("FeedHandler =", StringComparison.Ordinal) || line.Text.Contains("FeedHandler:", StringComparison.Ordinal))
            .Where(line => !Path.GetFileName(line.File).Equals("UpdateCheckerTests.cs", StringComparison.Ordinal))
            .Where(line => !Path.GetFileName(line.File).Equals("TestRuntimeOptions.cs", StringComparison.Ordinal))
            .Select(Where)
            .ToList();

        Assert.True(replaced.Count == 0, $"the feed transport is replaced at: {string.Join(", ", replaced)}");
    }

    /// <summary>Every line of every test source but this one, with where it is.</summary>
    private static IEnumerable<(string File, int Line, string Text)> Lines() =>
        Sources().SelectMany(file => File.ReadLines(file).Select((text, index) => (File: file, Line: index + 1, Text: text)));

    private static string Where((string File, int Line, string Text) line) => $"{Path.GetFileName(line.File)}({line.Line})";

    /// <summary>
    /// Every test source but this one. A guard that reads source has its own source read back at it: the lines
    /// below name the very things they forbid, and counting them would be counting the rule as a breach of
    /// itself.
    /// </summary>
    private static IEnumerable<string> Sources() =>
        Directory
            .EnumerateFiles(Path.Combine(RepositoryRoot(), "runtime", "tests"), "*.cs", SearchOption.AllDirectories)
            .Where(file => !Path.GetFileName(file).Equals("OfflineRuntimeTests.cs", StringComparison.Ordinal));

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "global.json")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? ".";
    }
}
