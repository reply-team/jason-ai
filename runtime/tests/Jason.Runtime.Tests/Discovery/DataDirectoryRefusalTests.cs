using Jason.Contracts.Discovery;
using Jason.Runtime.Discovery;
using Jason.Runtime.Hosting;

namespace Jason.Runtime.Tests.Discovery;

/// <summary>
/// A first run that cannot proceed owes a sentence, not a stack trace.
/// </summary>
/// <remarks>
/// Preparing the data directory is the first thing a runtime does, before its logging exists, so an exception
/// escaping there ended the process with nothing written anywhere — and the CLI that started it reported a
/// failure and sent the operator to a log directory it had just created empty. That is the worst possible
/// moment to be told to go and read something that is not there.
/// </remarks>
public class DataDirectoryRefusalTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>
    /// A data directory that cannot be made is a refusal naming it, not an unhandled exception. A file where
    /// the directory should be is the cheapest way to be certain of that on every platform; the condition the
    /// hand walk met was a permission one, which reaches the same catch.
    /// </summary>
    [Fact]
    public async Task A_data_directory_that_cannot_be_prepared_is_a_named_refusal()
    {
        using var tree = new TempTree();
        var blocker = Path.Combine(tree.Root, "blocked");
        await File.WriteAllTextAsync(blocker, "this is a file, not a directory", Ct);

        var refusal = await Assert.ThrowsAsync<DataDirectoryUnusableException>(
            () => RuntimeHost.StartAsync(new JasonPaths(Path.Combine(blocker, "data")), null, Ct));

        Assert.Contains("did not start", refusal.Message, StringComparison.Ordinal);
        Assert.Contains("data", refusal.Message, StringComparison.Ordinal);
        Assert.Contains("JASON_DATA_DIR", refusal.Message, StringComparison.Ordinal);

        // And it says not to go looking for a log file, because there is not going to be one.
        Assert.Contains("no log file exists", refusal.Message, StringComparison.Ordinal);
        Assert.NotNull(refusal.InnerException);
    }

    /// <summary>
    /// And an ordinary data directory still starts. A refusal that fired on the normal case would be a worse
    /// bug than the one it replaced.
    /// </summary>
    [Fact]
    public async Task An_ordinary_data_directory_still_starts()
    {
        using var data = new TempDataDir();

        await using var runtime = await RuntimeHost.StartAsync(data.Paths, null, Ct);

        Assert.True(Directory.Exists(data.Paths.LogsDirectory));
    }
}
