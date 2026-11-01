using Jason.Contracts.Files;
using Jason.Runtime.Discovery;
using Jason.Runtime.Hosting;
using Xunit.Sdk;

namespace Jason.Runtime.Tests.Hosting;

/// <summary>
/// A start whose endpoint descriptor a reader holds hands its task back to whoever called it while the publish waits for
/// the reader to let go, rather than holding that caller's thread for as long as the publish rule allows; the start
/// completes once the reader has let go.
/// </summary>
public sealed class DescriptorWaitAtStartTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>
    /// The publish's pause is held until the row lets the reader go, which it does only once the call has returned, so the
    /// start has to have handed its task back while the publish still waits. A pause taken on the calling thread before the
    /// call returned is a start that holds its caller for the whole wait: it moves the runtime's clock past the rule, so the
    /// start is refused as held and the row fails by name, instead of waiting on itself for ever. A start that ends before
    /// the publish ever pauses, refused or not, fails the row at once as well, since nothing would signal the wait.
    /// The row sees an in-place publish only while the start reaches the publish without having yielded: a hosted service
    /// that later yields in its start would let a synchronous publish run on a pool thread on its own.
    /// </summary>
    [Fact]
    public async Task A_start_hands_its_task_back_while_a_reader_holds_the_descriptor_and_completes_once_it_lets_go()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Elsewhere a reader does not stop a rename over the file it reads.");

        using var data = new TempDataDir();
        Directory.CreateDirectory(data.Paths.RunDirectory);
        await File.WriteAllTextAsync(data.Paths.DescriptorFile, "{}", Ct);
        var reader = new FileStream(data.Paths.DescriptorFile, FileMode.Open, FileAccess.Read, FileShare.Read);
        var clock = new HeldClock();
        var caller = Environment.CurrentManagedThreadId;
        using var returned = new ManualResetEventSlim();
        using var letGo = new ManualResetEventSlim();
        var waiting = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var heldTheCaller = false;

        var start = RuntimeHost.StartAsync(
            data.Paths,
            TestRuntimeOptions.Quiet with
            {
                Clock = clock,
                DescriptorPause = (delay, cancellationToken) =>
                {
                    if (!returned.IsSet && Environment.CurrentManagedThreadId == caller)
                    {
                        Volatile.Write(ref heldTheCaller, true);
                        clock.Advance(DescriptorPublisher.PublishRule.Wait);
                        return;
                    }

                    waiting.TrySetResult();
                    letGo.Wait(cancellationToken);
                    HeldFileRetry.Sleep(delay, cancellationToken);
                },
            },
            Ct);
        returned.Set();

        var completedWhileWaiting = false;
        var endedBeforeWaiting = false;
        try
        {
            if (!Volatile.Read(ref heldTheCaller))
            {
                var first = await Task.WhenAny(waiting.Task, start).WaitAsync(Ct);
                endedBeforeWaiting = !ReferenceEquals(first, waiting.Task);
                completedWhileWaiting = !endedBeforeWaiting && start.IsCompleted;
            }
        }
        finally
        {
            reader.Dispose();
            letGo.Set();
        }

        var ended = await Record.ExceptionAsync(() => start);
        await using var runtime = ended is null ? await start : null;

        if (endedBeforeWaiting)
        {
            throw ended is null
                ? new XunitException("The start completed without its publish ever waiting for the reader.")
                : new XunitException("The start ended before its publish waited for the reader.", ended);
        }

        Assert.False(Volatile.Read(ref heldTheCaller), "The start held the thread that called it while the publish waited for the reader: its task came back only once the wait was over.");
        Assert.False(completedWhileWaiting, "The start completed while the publish still waited for the reader.");
        Assert.Null(ended);
        Assert.Equal(runtime!.Descriptor, new Jason.Cli.Discovery.DescriptorReader(data.Paths).Read());
    }
}
