using Jason.Contracts.Files;
using Jason.Runtime.Hosting;
using Jason.Runtime.Tests.Integration;

namespace Jason.Runtime.Tests.Hosting;

/// <summary>
/// A start whose endpoint descriptor cannot be put in place refuses with a sentence, where whoever started it hears it,
/// and exits 1 — as the other refusals at a start do — rather than ending unhandled with nothing said. Its log says the
/// same in one line, with the reason as a closed phrase and never the operating system's message.
/// </summary>
public sealed class DescriptorRefusalAtStartTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly HeldFileRule Short = new(TimeSpan.FromMilliseconds(300), TimeSpan.FromMilliseconds(50), AccessDeniedCounts: true);

    [Fact]
    public async Task A_descriptor_a_reader_holds_throughout_refuses_the_start_with_its_sentence()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Elsewhere a reader does not stop a rename over the file it reads.");

        using var data = new TempDataDir();
        Directory.CreateDirectory(data.Paths.RunDirectory);
        await File.WriteAllTextAsync(data.Paths.DescriptorFile, "{}", Ct);

        string said;
        using (new FileStream(data.Paths.DescriptorFile, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            said = await RefusedAsync(data);
        }

        Assert.Equal(
            $"The runtime could not publish its endpoint descriptor at '{data.Paths.DescriptorFile}': replacing it was refused for 0.3 seconds. "
            + "Another program may be holding the file open, or this account may not be allowed to replace it; free it or fix its "
            + "permissions, and start the runtime again.",
            said);

        var log = Log(data);
        Assert.Contains("The runtime did not start: its endpoint descriptor at {Descriptor} could not be published: {Why}", log, StringComparison.Ordinal);
        Assert.Contains("\"Why\":\"replacing it was refused for 0.3 seconds\"", log, StringComparison.Ordinal);
        SystemRefusalInLog.AssertNone(log, data.Paths);

        // Nothing of the refused start is held: once the reader has let go, the next start publishes.
        await using var runtime = await RuntimeHost.StartAsync(data.Paths, TestRuntimeOptions.Quiet, Ct);
        Assert.Equal(runtime.Descriptor, new Jason.Cli.Discovery.DescriptorReader(data.Paths).Read());
    }

    /// <summary>
    /// The same refusal on every platform, from a directory where the descriptor goes: on Windows the access-denied is
    /// waited out before it is said, and elsewhere it is said at once and sends the person to the run directory.
    /// </summary>
    [Fact]
    public async Task A_directory_in_the_descriptors_place_refuses_the_start_on_every_platform()
    {
        using var data = new TempDataDir();
        Directory.CreateDirectory(Path.Combine(data.Paths.DescriptorFile, "in-the-way"));

        var said = await RefusedAsync(data);

        Assert.StartsWith($"The runtime could not publish its endpoint descriptor at '{data.Paths.DescriptorFile}': replacing it was refused", said, StringComparison.Ordinal);
        Assert.Contains(
            OperatingSystem.IsWindows() ? "\"Why\":\"replacing it was refused for 0.3 seconds\"" : "\"Why\":\"replacing it was refused\"",
            Log(data),
            StringComparison.Ordinal);
    }

    /// <summary>
    /// A start stopped while it waits for a reader stops waiting: the start's own cancellation reaches the wait, which
    /// would otherwise run its full ten seconds.
    /// </summary>
    [Fact]
    public async Task A_start_cancelled_while_it_waits_for_the_descriptor_stops_waiting()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Elsewhere a reader does not stop a rename over the file it reads.");

        using var data = new TempDataDir();
        Directory.CreateDirectory(data.Paths.RunDirectory);
        await File.WriteAllTextAsync(data.Paths.DescriptorFile, "{}", Ct);
        using var reader = new FileStream(data.Paths.DescriptorFile, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        var started = TimeProvider.System.GetTimestamp();

        // Stopped once the start is well into the descriptor's ten-second wait; a start still short of it is stopped
        // earlier and answers the same, so this cannot be red for a slow machine, only for a wait that ignores the stop.
        stop.CancelAfter(TimeSpan.FromSeconds(3));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => RuntimeHost.StartAsync(data.Paths, TestRuntimeOptions.Quiet, stop.Token));

        Assert.True(TimeProvider.System.GetElapsedTime(started) < TimeSpan.FromSeconds(8), "the start waited out the descriptor's rule after it was stopped.");
    }

    /// <summary>What the start said where it was started from; it has to have refused with 1, and nothing may have escaped.</summary>
    private static async Task<string> RefusedAsync(TempDataDir data)
    {
        using var refusals = new StringWriter();
        var exit = 0;

        var escaped = await Record.ExceptionAsync(async () => exit = await RuntimeHost.RunAsync(
            data.Paths,
            TestRuntimeOptions.Quiet with { Refusals = refusals, DescriptorPublishing = Short },
            Ct));

        Assert.Null(escaped);
        Assert.Equal(1, exit);
        return refusals.ToString().Trim();
    }

    private static string Log(TempDataDir data) =>
        string.Concat(Directory.GetFiles(data.Paths.LogsDirectory).Select(FakeHostRuntime.ReadShared));
}
