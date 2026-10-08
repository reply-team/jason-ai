using Jason.Runtime.Hosting;
using Jason.Runtime.Tests.Integration;

namespace Jason.Runtime.Tests.Hosting;

/// <summary>
/// A stop that cannot remove the endpoint descriptor says so in the runtime's log, once, with the reason as a closed
/// phrase; it used to leave the descriptor behind in silence. The stop goes on either way, and a CLI that reads the
/// descriptor left behind finds nothing at its address and says so.
/// </summary>
public sealed class DescriptorRemovalAtStopTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private const string Template = "The endpoint descriptor at {Descriptor} could not be removed: {Why}. A CLI reading it will find no runtime at its address and say so.";

    /// <summary>A reader that holds it for the whole of the stop's wait, the remove rule's two seconds.</summary>
    [Fact]
    public async Task A_descriptor_a_reader_holds_through_the_stop_is_said_to_be_held()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Elsewhere a reader does not stop a delete of the file it reads.");

        using var data = new TempDataDir();
        var runtime = await RuntimeHost.StartAsync(data.Paths, TestRuntimeOptions.Quiet, Ct);

        using (new FileStream(data.Paths.DescriptorFile, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            await runtime.StopAsync();
        }

        var log = Log(data);
        Assert.Single(Lines(log, Template));
        Assert.Contains("\"Why\":\"another program held it for 2 seconds\"", log, StringComparison.Ordinal);
        Assert.True(File.Exists(data.Paths.DescriptorFile));
    }

    [Fact]
    public async Task A_read_only_descriptor_at_a_stop_is_said_to_be_refused()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Elsewhere a read-only file is removed, as its directory allows.");

        using var data = new TempDataDir();
        var runtime = await RuntimeHost.StartAsync(data.Paths, TestRuntimeOptions.Quiet, Ct);
        File.SetAttributes(data.Paths.DescriptorFile, FileAttributes.ReadOnly);

        try
        {
            await runtime.StopAsync();

            var log = Log(data);
            Assert.Single(Lines(log, Template));
            Assert.Contains("\"Why\":\"removing it was refused\"", log, StringComparison.Ordinal);
            Assert.DoesNotContain("Access to the path", log, StringComparison.Ordinal);
        }
        finally
        {
            File.SetAttributes(data.Paths.DescriptorFile, FileAttributes.Normal);
        }
    }

    /// <summary>
    /// A stop whose run directory somebody has removed has no descriptor left to remove, and says nothing about one.
    /// </summary>
    [Fact]
    public async Task A_stop_whose_run_directory_is_gone_says_nothing_about_the_descriptor()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "On Windows the runtime's own lock file holds its run directory open, so the directory cannot be removed while it runs.");

        using var data = new TempDataDir();
        var runtime = await RuntimeHost.StartAsync(data.Paths, TestRuntimeOptions.Quiet, Ct);
        Directory.Delete(data.Paths.RunDirectory, recursive: true);

        await runtime.StopAsync();

        Assert.Empty(Lines(Log(data), Template));
    }

    /// <summary>And a stop that removes it says nothing of the kind.</summary>
    [Fact]
    public async Task A_stop_that_removes_the_descriptor_says_nothing_about_it()
    {
        using var data = new TempDataDir();
        var runtime = await RuntimeHost.StartAsync(data.Paths, TestRuntimeOptions.Quiet, Ct);

        await runtime.StopAsync();

        Assert.False(File.Exists(data.Paths.DescriptorFile));
        Assert.Empty(Lines(Log(data), Template));
    }

    private static string Log(TempDataDir data) =>
        string.Concat(Directory.GetFiles(data.Paths.LogsDirectory).Select(FakeHostRuntime.ReadShared));

    private static IEnumerable<string> Lines(string log, string template) =>
        log.Split('\n').Where(line => line.Contains(template, StringComparison.Ordinal));
}
