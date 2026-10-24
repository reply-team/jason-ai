using Jason.Contracts.Files;
using Jason.Runtime.Discovery;

namespace Jason.Runtime.Tests.Discovery;

/// <summary>
/// The endpoint descriptor held open by a reader, as every CLI command holds it for one read. On Windows that handle
/// refuses the rename over it with an access-denied and its delete with a sharing violation, for as long as the read
/// lasts. So both are tried again for a while; a hold that outlasts the wait, and a refusal no wait ends, are said in
/// their own words.
/// </summary>
/// <remarks>A reader is held exactly as <c>File.ReadAllBytes</c> holds the file: read access, sharing reads only.</remarks>
public sealed class DescriptorHeldTests
{
    private static readonly TimeSpan Moment = TimeSpan.FromMilliseconds(300);

    private static readonly HeldFileRule Short = new(TimeSpan.FromMilliseconds(300), TimeSpan.FromMilliseconds(50), AccessDeniedCounts: true);

    /// <summary>Far more pauses than a reader that lets go after a moment can cost: ten seconds of them at the least.</summary>
    private const int AbsurdlyManyPauses = 200;

    private static FileStream Reader(string path) => new(path, FileMode.Open, FileAccess.Read, FileShare.Read);

    /// <summary>
    /// The real pause, until there have been absurdly many of them: then the held clock is moved past the rule, so a file
    /// nothing lets go of ends the row as held, which names the hold, instead of never ending.
    /// </summary>
    private static Action<TimeSpan, CancellationToken> GivingUpAfterAbsurdlyMany(HeldClock clock, HeldFileRule rule)
    {
        var pauses = 0;
        return (delay, cancellationToken) =>
        {
            if (Interlocked.Increment(ref pauses) == AbsurdlyManyPauses)
            {
                clock.Advance(rule.Wait);
            }

            HeldFileRetry.Sleep(delay, cancellationToken);
        };
    }

    [Fact]
    [Critical(Family.F5, "the held descriptor wait")]
    public async Task A_descriptor_a_reader_holds_for_a_moment_is_replaced_once_it_lets_go()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Elsewhere a reader does not stop a rename over the file it reads.");

        using var dir = new TempDataDir();
        // Its clock moves only after absurdly many pauses, so a loaded machine that keeps the reader back cannot run the
        // rule's wait out first.
        var clock = new HeldClock();
        var publisher = new DescriptorPublisher(dir.Paths) { Clock = clock, Pause = GivingUpAfterAbsurdlyMany(clock, DescriptorPublisher.PublishRule) };
        publisher.Publish(RuntimeInfo.Create(), new Uri("http://127.0.0.1:1"), "first", TestContext.Current.CancellationToken);
        var reader = Reader(dir.Paths.DescriptorFile);
        var letGo = LetGoAfter(Moment, reader.Dispose);

        try
        {
            publisher.Publish(RuntimeInfo.Create(), new Uri("http://127.0.0.1:2"), "second", TestContext.Current.CancellationToken);
        }
        finally
        {
            await letGo;
        }

        Assert.Contains("\"token\":\"second\"", File.ReadAllText(dir.Paths.DescriptorFile), StringComparison.Ordinal);
        Assert.Single(Directory.EnumerateFileSystemEntries(dir.Paths.RunDirectory));
    }

    [Fact]
    public void A_descriptor_a_reader_never_lets_go_of_is_refused_with_both_causes_named()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Elsewhere a reader does not stop a rename over the file it reads.");

        using var dir = new TempDataDir();
        new DescriptorPublisher(dir.Paths).Publish(RuntimeInfo.Create(), new Uri("http://127.0.0.1:1"), "first", TestContext.Current.CancellationToken);
        using var reader = Reader(dir.Paths.DescriptorFile);
        var publisher = new DescriptorPublisher(dir.Paths) { Publishing = Short };

        var refusal = Assert.Throws<DescriptorUnpublishableException>(() => publisher.Publish(RuntimeInfo.Create(), new Uri("http://127.0.0.1:2"), "second", TestContext.Current.CancellationToken));

        Assert.Equal(
            $"The runtime could not publish its endpoint descriptor at '{dir.Paths.DescriptorFile}': replacing it was refused for 0.3 seconds. "
            + "Another program may be holding the file open, or this account may not be allowed to replace it; free it or fix its "
            + "permissions, and start the runtime again.",
            refusal.Message);
        Assert.Equal("replacing it was refused for 0.3 seconds", refusal.Why);
        Assert.Single(Directory.EnumerateFileSystemEntries(dir.Paths.RunDirectory));
    }

    /// <summary>
    /// A cancellation ends the wait: the publish stops with it rather than waiting out the rule. It is cancelled as its
    /// first pause begins, long before the rule's ten seconds are out, so a publish that ignored it would be refused as held.
    /// </summary>
    [Fact]
    public void A_cancelled_publish_stops_waiting_for_the_reader()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Elsewhere a reader does not stop a rename over the file it reads.");

        using var dir = new TempDataDir();
        new DescriptorPublisher(dir.Paths).Publish(RuntimeInfo.Create(), new Uri("http://127.0.0.1:1"), "first", TestContext.Current.CancellationToken);
        using var reader = Reader(dir.Paths.DescriptorFile);
        using var cancelled = new CancellationTokenSource();
        var publisher = new DescriptorPublisher(dir.Paths)
        {
            Pause = (delay, cancellationToken) =>
            {
                cancelled.Cancel();
                HeldFileRetry.Sleep(delay, cancellationToken);
            },
        };

        Assert.ThrowsAny<OperationCanceledException>(() => publisher.Publish(RuntimeInfo.Create(), new Uri("http://127.0.0.1:2"), "second", cancelled.Token));
    }

    /// <summary>The sentence's number is the rule's: ten seconds at a real start.</summary>
    [Fact]
    public void The_wait_a_start_names_is_the_publish_rules()
    {
        Assert.Equal(new HeldFileRule(TimeSpan.FromSeconds(10), TimeSpan.FromMilliseconds(50), AccessDeniedCounts: true), DescriptorPublisher.PublishRule);
        Assert.Contains(
            "replacing it was refused for 10 seconds.",
            DescriptorUnpublishableException.Held("p", DescriptorPublisher.PublishRule, null).Message,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// A read-only descriptor is refused at once: Windows answers it with the same access-denied as a reader's handle, and
    /// no wait would end it.
    /// </summary>
    [Fact]
    public void A_read_only_descriptor_is_refused_at_once_and_says_so()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Elsewhere a rename replaces a read-only file, as its directory allows.");

        using var dir = new TempDataDir();
        new DescriptorPublisher(dir.Paths).Publish(RuntimeInfo.Create(), new Uri("http://127.0.0.1:1"), "first", TestContext.Current.CancellationToken);
        File.SetAttributes(dir.Paths.DescriptorFile, FileAttributes.ReadOnly);
        var pauses = new List<TimeSpan>();
        var publisher = new DescriptorPublisher(dir.Paths) { Pause = (delay, _) => pauses.Add(delay) };

        try
        {
            var refusal = Assert.Throws<DescriptorUnpublishableException>(() => publisher.Publish(RuntimeInfo.Create(), new Uri("http://127.0.0.1:2"), "second", TestContext.Current.CancellationToken));

            Assert.Equal(
                $"The runtime could not publish its endpoint descriptor at '{dir.Paths.DescriptorFile}': the file is read-only. "
                + "Clear its read-only attribute and start the runtime again.",
                refusal.Message);
            Assert.Equal("the file is read-only", refusal.Why);
            Assert.Empty(pauses);
        }
        finally
        {
            File.SetAttributes(dir.Paths.DescriptorFile, FileAttributes.Normal);
        }
    }

    /// <summary>
    /// A refusal no wait ends is said at once, and sends the person to the run directory: here a full disk, which no file
    /// system can be made to answer on demand, so the rename is the seam's.
    /// </summary>
    [Fact]
    public void A_refusal_no_wait_ends_is_said_at_once()
    {
        using var dir = new TempDataDir();
        var pauses = new List<TimeSpan>();
        var publisher = new DescriptorPublisher(dir.Paths)
        {
            Pause = (delay, _) => pauses.Add(delay),
            Rename = (_, _) => throw new IOException("There is not enough space on the disk.", unchecked((int)0x80070070)),
        };

        var refusal = Assert.Throws<DescriptorUnpublishableException>(() => publisher.Publish(RuntimeInfo.Create(), new Uri("http://127.0.0.1:2"), "second", TestContext.Current.CancellationToken));

        Assert.Equal(Refused(dir), refusal.Message);
        Assert.Equal("replacing it was refused", refusal.Why);
        Assert.Empty(pauses);
        Assert.Empty(Directory.EnumerateFileSystemEntries(dir.Paths.RunDirectory));
    }

    /// <summary>
    /// A directory where the descriptor should be, on a real file system. Windows refuses the rename over it with the same
    /// access-denied a reader's handle gets, which a publish waits out before it says so; elsewhere nothing is waited for,
    /// and it is said at once.
    /// </summary>
    [Fact]
    public void A_directory_in_the_descriptors_place_is_refused_as_its_platform_refuses_it()
    {
        using var dir = new TempDataDir();
        Directory.CreateDirectory(Path.Combine(dir.Paths.DescriptorFile, "in-the-way"));
        var pauses = new List<TimeSpan>();
        var publisher = new DescriptorPublisher(dir.Paths) { Publishing = Short, Pause = (delay, cancellationToken) => { pauses.Add(delay); HeldFileRetry.Sleep(delay, cancellationToken); } };

        var refusal = Assert.Throws<DescriptorUnpublishableException>(() => publisher.Publish(RuntimeInfo.Create(), new Uri("http://127.0.0.1:2"), "second", TestContext.Current.CancellationToken));

        if (OperatingSystem.IsWindows())
        {
            Assert.Equal("replacing it was refused for 0.3 seconds", refusal.Why);
            Assert.NotEmpty(pauses);
        }
        else
        {
            Assert.Equal(Refused(dir), refusal.Message);
            Assert.Empty(pauses);
        }
    }

    private static string Refused(TempDataDir dir) =>
        $"The runtime could not publish its endpoint descriptor at '{dir.Paths.DescriptorFile}': replacing it was refused. "
        + $"Check that the account the runtime runs as can write to '{dir.Paths.RunDirectory}', and that its volume has room; "
        + "then start the runtime again.";

    [Fact]
    public async Task A_descriptor_a_reader_holds_for_a_moment_at_a_stop_is_removed_once_it_lets_go()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Elsewhere a reader does not stop a delete of the file it reads.");

        using var dir = new TempDataDir();
        // Its clock moves only after absurdly many pauses, so a loaded machine that keeps the reader back cannot run the
        // rule's wait out first.
        var clock = new HeldClock();
        var publisher = new DescriptorPublisher(dir.Paths) { Clock = clock, Pause = GivingUpAfterAbsurdlyMany(clock, DescriptorPublisher.RemoveRule) };
        publisher.Publish(RuntimeInfo.Create(), new Uri("http://127.0.0.1:1"), "first", TestContext.Current.CancellationToken);
        var reader = Reader(dir.Paths.DescriptorFile);
        var letGo = LetGoAfter(Moment, reader.Dispose);

        DescriptorRemoval removal;
        try
        {
            removal = publisher.Remove();
        }
        finally
        {
            await letGo;
        }

        Assert.Equal(DescriptorRemoval.Removed, removal);
        Assert.False(File.Exists(dir.Paths.DescriptorFile));
    }

    [Fact]
    public void A_descriptor_a_reader_never_lets_go_of_at_a_stop_is_said_to_be_held()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Elsewhere a reader does not stop a delete of the file it reads.");

        using var dir = new TempDataDir();
        new DescriptorPublisher(dir.Paths).Publish(RuntimeInfo.Create(), new Uri("http://127.0.0.1:1"), "first", TestContext.Current.CancellationToken);
        using var reader = Reader(dir.Paths.DescriptorFile);
        var publisher = new DescriptorPublisher(dir.Paths) { Removing = Short with { AccessDeniedCounts = false } };

        Assert.Equal(DescriptorRemoval.Held, publisher.Remove());
        Assert.Equal("another program held it for 0.3 seconds", publisher.Why(DescriptorRemoval.Held));
        Assert.True(File.Exists(dir.Paths.DescriptorFile));
    }

    /// <summary>
    /// An access-denied at a stop is not waited for: a read-only descriptor is said to be refused at once, with no pause.
    /// </summary>
    [Fact]
    public void A_read_only_descriptor_at_a_stop_is_refused_at_once()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Elsewhere a read-only file is removed, as its directory allows.");

        using var dir = new TempDataDir();
        new DescriptorPublisher(dir.Paths).Publish(RuntimeInfo.Create(), new Uri("http://127.0.0.1:1"), "first", TestContext.Current.CancellationToken);
        File.SetAttributes(dir.Paths.DescriptorFile, FileAttributes.ReadOnly);
        var pauses = new List<TimeSpan>();
        var publisher = new DescriptorPublisher(dir.Paths) { Pause = (delay, _) => pauses.Add(delay) };

        try
        {
            Assert.Equal(DescriptorRemoval.Refused, publisher.Remove());
            Assert.Equal("removing it was refused", publisher.Why(DescriptorRemoval.Refused));
            Assert.Empty(pauses);
        }
        finally
        {
            File.SetAttributes(dir.Paths.DescriptorFile, FileAttributes.Normal);
        }
    }

    /// <summary>A run directory that is gone holds no descriptor, so a stop finds the descriptor removed, not refused.</summary>
    [Fact]
    public void A_stop_whose_run_directory_is_gone_finds_the_descriptor_removed()
    {
        using var dir = new TempDataDir();
        var publisher = new DescriptorPublisher(dir.Paths);
        publisher.Publish(RuntimeInfo.Create(), new Uri("http://127.0.0.1:1"), "first", TestContext.Current.CancellationToken);
        Directory.Delete(dir.Paths.RunDirectory, recursive: true);

        Assert.Equal(DescriptorRemoval.Removed, publisher.Remove());
    }

    /// <summary>The number a stop's warning names is the remove rule's: two seconds.</summary>
    [Fact]
    public void The_wait_a_stop_names_is_the_remove_rules()
    {
        using var dir = new TempDataDir();
        Assert.Equal(new HeldFileRule(TimeSpan.FromSeconds(2), TimeSpan.FromMilliseconds(50), AccessDeniedCounts: false), DescriptorPublisher.RemoveRule);
        Assert.Equal("another program held it for 2 seconds", new DescriptorPublisher(dir.Paths).Why(DescriptorRemoval.Held));
    }

    /// <summary>
    /// Lets go after a moment, on a thread of its own. The wait under test blocks the test's own thread, and a release
    /// queued to the thread pool can be held back past that wait's whole budget while a loaded suite keeps the pool busy,
    /// and the test would then fail with the product doing exactly what its rule says.
    /// </summary>
    private static Task LetGoAfter(TimeSpan moment, Action letGo)
    {
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                Thread.Sleep(moment);
                letGo();
                done.SetResult();
            }
            catch (Exception exception)
            {
                done.SetException(exception);
            }
        })
        {
            IsBackground = true,
            Name = "let go of a held file",
        };
        thread.Start();
        return done.Task;
    }
}
