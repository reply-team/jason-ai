using Jason.Cli.Uninstall;

namespace Jason.Cli.Tests.Uninstall;

/// <summary>
/// This machine's remover against a file that is really held open. On Windows another program's open handle stops a
/// delete until it lets go, so a file held for a moment is removed once it is let go, inside the wait; elsewhere an open
/// file does not stop its removal at all. Every path is in a temporary directory of the test's own.
/// </summary>
/// <remarks>
/// A hold of half a second against a wait of ten is a margin of twenty: the test is bounded by the wait, and does not
/// race it.
/// </remarks>
public sealed class MachineRemoverHeldFileTests
{
    private static readonly TimeSpan Moment = TimeSpan.FromMilliseconds(500);

    public static TheoryData<string> Removals => ["file", "executable", "tree", "extracted"];

    [Theory]
    [MemberData(nameof(Removals))]
    public async Task A_file_held_open_for_a_moment_is_removed_once_it_is_let_go(string removal)
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Elsewhere an open file does not stop its removal, so there is nothing to wait out.");

        using var dir = new TempPaths();
        var (target, held) = Plant(dir, removal);
        var holder = new FileStream(held, FileMode.Open, FileAccess.Read, FileShare.None);
        var letGo = Task.Run(
            async () =>
            {
                await Task.Delay(Moment);
                await holder.DisposeAsync();
            },
            TestContext.Current.CancellationToken);

        try
        {
            Remove(InstallationRemovers.ForThisMachine(), removal, target);
        }
        finally
        {
            await letGo;
        }

        Assert.False(File.Exists(held), "the held file is still there.");
        Assert.False(removal is "tree" or "extracted" && Directory.Exists(target), "the directory is still there.");
    }

    /// <summary>
    /// A file held for the whole of the wait ends the removal in a <see cref="HeldOpen"/> naming what was asked for, and
    /// whether it was a directory. The wait is a short one of the test's own, through the remover's seam.
    /// </summary>
    [Theory]
    [MemberData(nameof(Removals))]
    public void A_file_held_for_the_whole_wait_ends_in_a_refusal_that_says_so(string removal)
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Elsewhere an open file does not stop its removal, so there is nothing to wait out.");

        using var dir = new TempPaths();
        var (target, held) = Plant(dir, removal);
        using var holder = new FileStream(held, FileMode.Open, FileAccess.Read, FileShare.None);
        var rule = InstallationRemovers.HeldFiles with { Wait = TimeSpan.FromMilliseconds(300), Interval = TimeSpan.FromMilliseconds(50) };
        var started = TimeProvider.System.GetTimestamp();

        var refusal = Assert.Throws<HeldOpen>(() => Remove(InstallationRemovers.ForThisMachine(UserPathValue.EnvironmentKey, rule, TimeProvider.System, Contracts.Files.HeldFileRetry.Sleep), removal, target));

        Assert.True(TimeProvider.System.GetElapsedTime(started) >= rule.Wait, "the removal gave up before the wait was over.");
        Assert.Equal(target, refusal.Target);
        Assert.Equal(removal is "tree" or "extracted", refusal.IsDirectory);
        Assert.Equal(unchecked((int)0x80070020), refusal.InnerException?.HResult);
        Assert.True(File.Exists(held));
    }

    /// <summary>
    /// The running image is moved aside rather than deleted on Windows, and the move waits for a holder the same way. The
    /// seam says the planted file is the running image, moves it into a temporary directory of the test's own, and answers
    /// a cleanup's id without starting one.
    /// </summary>
    [Fact]
    public async Task The_running_image_held_for_a_moment_is_moved_aside_once_it_is_let_go()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Elsewhere the running image is deleted, and an open file does not stop that.");

        using var dir = new TempPaths();
        var (executable, _) = Plant(dir, "running");
        var temporary = Directory.CreateDirectory(Path.Combine(dir.Paths.Root, "tmp")).FullName;
        var holder = new FileStream(executable, FileMode.Open, FileAccess.Read, FileShare.None);
        var letGo = Task.Run(
            async () =>
            {
                await Task.Delay(Moment);
                await holder.DisposeAsync();
            },
            TestContext.Current.CancellationToken);

        ExecutableOutcome outcome;
        try
        {
            outcome = RunningImageRemover(InstallationRemovers.HeldFiles, temporary).RemoveExecutable(executable);
        }
        finally
        {
            await letGo;
        }

        Assert.False(File.Exists(executable), "the running image is still in its directory.");
        Assert.NotNull(outcome.MovedTo);
        Assert.StartsWith(temporary, outcome.MovedTo, StringComparison.OrdinalIgnoreCase);
        Assert.True(File.Exists(outcome.MovedTo));
    }

    [Fact]
    public void The_running_image_held_for_the_whole_wait_ends_in_a_refusal_that_says_so()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Elsewhere the running image is deleted, and an open file does not stop that.");

        using var dir = new TempPaths();
        var (executable, _) = Plant(dir, "running");
        var temporary = Directory.CreateDirectory(Path.Combine(dir.Paths.Root, "tmp")).FullName;
        using var holder = new FileStream(executable, FileMode.Open, FileAccess.Read, FileShare.None);
        var rule = InstallationRemovers.HeldFiles with { Wait = TimeSpan.FromMilliseconds(300), Interval = TimeSpan.FromMilliseconds(50) };

        var refusal = Assert.Throws<HeldOpen>(() => RunningImageRemover(rule, temporary).RemoveExecutable(executable));

        Assert.Equal(executable, refusal.Target);
        Assert.False(refusal.IsDirectory);
        Assert.True(File.Exists(executable));
    }

    /// <summary>
    /// An empty directory another program holds — a shell whose working directory it is, a window open on it — refuses its
    /// delete the same way, and is waited for the same way. .NET cannot open a directory, so the hold is the operating
    /// system's own handle, opened with the backup flag a directory needs.
    /// </summary>
    [Fact]
    public async Task An_empty_directory_held_for_a_moment_is_removed_once_it_is_let_go()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Elsewhere an open directory does not stop its removal, so there is nothing to wait out.");

        using var dir = new TempPaths();
        var empty = Directory.CreateDirectory(Path.Combine(dir.Paths.Root, "planted", "empty")).FullName;
        var holder = HoldDirectory(empty);
        var letGo = Task.Run(
            async () =>
            {
                await Task.Delay(Moment);
                holder.Dispose();
            },
            TestContext.Current.CancellationToken);

        bool removed;
        try
        {
            removed = InstallationRemovers.ForThisMachine().RemoveDirectoryIfEmpty(empty);
        }
        finally
        {
            await letGo;
        }

        Assert.True(removed);
        Assert.False(Directory.Exists(empty));
    }

    [Fact]
    public void An_empty_directory_held_for_the_whole_wait_ends_in_a_refusal_that_says_so()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Elsewhere an open directory does not stop its removal, so there is nothing to wait out.");

        using var dir = new TempPaths();
        var empty = Directory.CreateDirectory(Path.Combine(dir.Paths.Root, "planted", "empty")).FullName;
        using var holder = HoldDirectory(empty);
        var rule = InstallationRemovers.HeldFiles with { Wait = TimeSpan.FromMilliseconds(300), Interval = TimeSpan.FromMilliseconds(50) };

        var refusal = Assert.Throws<HeldOpen>(() => InstallationRemovers
            .ForThisMachine(UserPathValue.EnvironmentKey, rule, TimeProvider.System, Contracts.Files.HeldFileRetry.Sleep)
            .RemoveDirectoryIfEmpty(empty));

        Assert.Equal(empty, refusal.Target);
        Assert.True(refusal.IsDirectory);
        Assert.Equal(unchecked((int)0x80070020), refusal.InnerException?.HResult);
        Assert.True(Directory.Exists(empty));
    }

    /// <summary>The uninstall's rule as it was decided: ten seconds, every quarter of a second, an access-denied not counted.</summary>
    [Fact]
    public void The_uninstalls_rule_is_ten_seconds_every_quarter_second_with_no_access_denied_counted() =>
        Assert.Equal(
            new Contracts.Files.HeldFileRule(TimeSpan.FromSeconds(10), TimeSpan.FromMilliseconds(250), AccessDeniedCounts: false),
            InstallationRemovers.HeldFiles);

    /// <summary>A directory held open by this process, as a shell whose working directory it is holds one.</summary>
    private static Microsoft.Win32.SafeHandles.SafeFileHandle HoldDirectory(string path)
    {
        const uint GenericRead = 0x80000000;
        const uint ShareRead = 0x00000001;
        const uint OpenExisting = 3;
        const uint BackupSemantics = 0x02000000;

        var handle = CreateFileW(path, GenericRead, ShareRead, IntPtr.Zero, OpenExisting, BackupSemantics, IntPtr.Zero);
        if (handle.IsInvalid)
        {
            throw new System.ComponentModel.Win32Exception(System.Runtime.InteropServices.Marshal.GetLastPInvokeError());
        }

        return handle;
    }

    [System.Runtime.InteropServices.DllImport("kernel32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode, SetLastError = true)]
    private static extern Microsoft.Win32.SafeHandles.SafeFileHandle CreateFileW(string name, uint access, uint share, IntPtr security, uint disposition, uint flags, IntPtr template);

    private static IInstallationRemover RunningImageRemover(Contracts.Files.HeldFileRule rule, string temporary) =>
        InstallationRemovers.ForThisMachine(
            UserPathValue.EnvironmentKey,
            rule,
            TimeProvider.System,
            Contracts.Files.HeldFileRetry.Sleep,
            runningImage: _ => true,
            temporaryDirectory: temporary,
            startCleanup: _ => 4242);

    /// <summary>
    /// An access-denied is not a hold, and is not waited for: a read-only file is refused at the first try, with no pause
    /// taken, and with the operating system's own refusal.
    /// </summary>
    [Fact]
    public void An_access_denied_is_refused_at_once_without_a_pause()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Elsewhere a read-only file is removed, as its directory allows.");

        using var dir = new TempPaths();
        var (target, _) = Plant(dir, "file");
        File.SetAttributes(target, FileAttributes.ReadOnly);
        var pauses = new List<TimeSpan>();

        try
        {
            Assert.Throws<UnauthorizedAccessException>(() => InstallationRemovers
                .ForThisMachine(UserPathValue.EnvironmentKey, InstallationRemovers.HeldFiles, TimeProvider.System, (delay, _) => pauses.Add(delay))
                .RemoveFile(target));

            Assert.Empty(pauses);
        }
        finally
        {
            File.SetAttributes(target, FileAttributes.Normal);
        }
    }

    [Theory]
    [MemberData(nameof(Removals))]
    public void Elsewhere_than_Windows_an_open_file_does_not_stop_its_removal(string removal)
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "On Windows an open file stops its removal until it is let go.");

        using var dir = new TempPaths();
        var (target, held) = Plant(dir, removal);
        using var holder = new FileStream(held, FileMode.Open, FileAccess.Read, FileShare.None);

        Remove(InstallationRemovers.ForThisMachine(), removal, target);

        Assert.False(File.Exists(held), "the held file is still there.");
    }

    /// <summary>What a removal is asked for, and the file in it that is held: the same file, or one inside the directory.</summary>
    private static (string Target, string Held) Plant(TempPaths dir, string removal)
    {
        var directory = Directory.CreateDirectory(Path.Combine(dir.Paths.Root, "planted", removal)).FullName;
        var file = Path.Combine(directory, "held.bin");
        File.WriteAllText(file, "held");
        return (removal is "tree" or "extracted" ? directory : file, file);
    }

    private static void Remove(IInstallationRemover remover, string removal, string target)
    {
        switch (removal)
        {
            case "file":
                remover.RemoveFile(target);
                break;
            case "executable":
                Assert.True(remover.RemoveExecutable(target).Removed, "a file that is not the running image was not removed.");
                break;
            case "tree":
                remover.RemoveTree(target);
                break;
            case "extracted":
                remover.RemoveExtractedLibraries(target);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(removal), removal, "not a removal this test knows");
        }
    }
}
