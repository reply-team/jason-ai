using System.IO.Pipes;
using System.Runtime.Versioning;
using Jason.Runtime.Tests;

namespace Jason.App.Tests.EndToEnd;

/// <summary>
/// A handle the CLI was handed beside its three streams, by a program between whoever reads and the CLI, reaches no
/// runtime the CLI starts. Windows PowerShell running <c>jason</c> in a pipeline hands it the pipes it was itself handed,
/// inheritable, beside the three it made for it; the runtime <c>jason runtime start</c> launched took them on, and the
/// reader at the far end waited for an end of file that came only when that runtime ended. This test process stands where
/// that program stands: it hands the real executable an inheritable pipe it never names, and reads the pipe to its end.
/// The rows of this assembly run one at a time, so no other row's child is handed the pipe meanwhile.
/// </summary>
public sealed class HandedHandleTests
{
    /// <summary>How long the read may take to end once the CLI has exited: the moment it takes, and far more.</summary>
    private static readonly TimeSpan ReadEnds = TimeSpan.FromSeconds(10);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    [SupportedOSPlatform("windows")]
    [Critical(Family.F4, "a handle the CLI was handed beside its streams reaches no runtime it starts, so its reader's read ends")]
    public async Task A_pipe_the_cli_was_handed_beside_its_streams_is_not_held_by_the_runtime_it_starts()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "A child is handed its parent's inheritable handles on Windows.");

        // The pipe first, so the installation goes first at the end: a runtime that holds the pipe is ended before the
        // pipe is closed under a read still waiting on it.
        using var handed = new AnonymousPipeServerStream(PipeDirection.In, HandleInheritability.Inheritable);
        using var it = GoldenPath.Create("handed-handle");

        CliResult started;
        try
        {
            started = await GoldenPath.LauncherAsync(it, "runtime", "start");
        }
        finally
        {
            // Only the processes this one started hold the writing end now.
            handed.DisposeLocalCopyOfClientHandle();
        }

        GoldenPath.AssertStarted(it.Paths, started);
        var runtime = GoldenPath.ReadDescriptor(it.Paths)
            ?? throw new InvalidOperationException("runtime start returned success without leaving a descriptor behind.");
        it.Pid = runtime.Pid;
        it.PidStartedAt = runtime.StartedAt;

        var read = handed.ReadAsync(new byte[1], Ct).AsTask();
        Assert.True(
            await Task.WhenAny(read, Task.Delay(ReadEnds, Ct)) == read,
            $"The pipe handed to 'jason runtime start' did not reach its end within {ReadEnds.TotalSeconds:F0} s of the verb's exit: "
            + $"the runtime it started, process {runtime.Pid}, holds it.");
        Assert.Equal(0, await read);

        Assert.True(GoldenPath.IsRunning(runtime.Pid), $"The runtime, process {runtime.Pid}, did not live on after its start.");
        GoldenPath.AssertSuccess(await GoldenPath.LauncherAsync(it, "runtime", "status"));
        await GoldenPath.StopAsync(it);
    }
}
