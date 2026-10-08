using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
using Jason.Cli.Doctor;
using Jason.Cli.Uninstall;
using Jason.Cli.Update;
using Jason.Contracts.Files;
using Jason.Contracts.Update;

namespace Jason.Cli.Tests.Update;

/// <summary>
/// An update's two moves of the executable wait for another program to let go of the file they move — a scanner reading
/// the executable that was just run, or the one that was just downloaded — by the uninstall's own rule, and say a file
/// still held at the end of the wait in a sentence of their own. Anything else that refuses a move is said at once, as
/// the operating system put it.
/// </summary>
/// <remarks>
/// The hold is real: the test opens the file the way a reader does, sharing reading and nothing else, which stops a
/// rename on Windows and stops nothing anywhere else.
/// </remarks>
public sealed class UpdateHeldMoveTests
{
    private const string Aside = "aside";

    private const string InPlace = "in place";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static string Sep => Path.DirectorySeparatorChar.ToString();

    private static HeldFileRule Short { get; } =
        InstallationRemovers.HeldFiles with { Wait = TimeSpan.FromMilliseconds(300), Interval = TimeSpan.FromMilliseconds(50) };

    [Fact]
    public void The_moves_wait_by_the_uninstalls_own_rule()
    {
        using var installation = new FakeInstallation();
        var applier = installation.Applier();

        Assert.Same(InstallationRemovers.HeldFiles, applier.HeldFiles);
        Assert.Equal(TimeSpan.FromSeconds(10), applier.HeldFiles.Wait);
        Assert.Equal(TimeSpan.FromMilliseconds(250), applier.HeldFiles.Interval);
        Assert.False(applier.HeldFiles.AccessDeniedCounts);
    }

    /// <summary>
    /// Held for a moment, the file is moved once it is let go and the update finishes. Elsewhere than Windows the hold
    /// does not stop the rename at all, so the move goes through at once and no pause is taken.
    /// </summary>
    [Theory]
    [InlineData(Aside)]
    [InlineData(InPlace)]
    public async Task An_executable_held_for_a_moment_is_moved_once_it_is_let_go(string move)
    {
        using var installation = new FakeInstallation().WithRuntime();
        FileStream? holder = null;
        installation.OnStop = () => holder = Hold(Moved(installation, move));
        var pauses = new List<TimeSpan>();
        var applier = Applier(installation, InstallationRemovers.HeldFiles, (delay, _) =>
        {
            pauses.Add(delay);
            if (pauses.Count == 2)
            {
                holder?.Dispose();
            }
        });

        try
        {
            await applier.ApplyAsync(Request(installation), Ct);
        }
        finally
        {
            holder?.Dispose();
        }

        Assert.Equal(installation.To.ToString(), installation.Installed());
        Assert.Equal(OperatingSystem.IsWindows() ? [TimeSpan.FromMilliseconds(250), TimeSpan.FromMilliseconds(250)] : [], pauses);
    }

    /// <summary>
    /// Held for the whole wait, the move is refused with the file from the home directory and how long it was tried, and no
    /// word of the operating system's; then the step's own way out. Asking again once the file is free can work, so it is
    /// retryable. Nothing moved.
    /// </summary>
    [Theory]
    [InlineData(Aside)]
    [InlineData(InPlace)]
    public async Task An_executable_held_for_the_whole_wait_is_refused_in_a_sentence_of_its_own(string move)
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Only Windows refuses to rename a file another program holds open.");
        using var installation = new FakeInstallation().WithRuntime();
        string? source = null;
        FileStream? holder = null;
        installation.OnStop = () =>
        {
            source = Moved(installation, move);
            holder = Hold(source);
        };
        var pauses = new List<TimeSpan>();
        var applier = Applier(installation, Short, (delay, _) => pauses.Add(delay));

        UpdateException refused;
        try
        {
            refused = await Assert.ThrowsAsync<UpdateException>(() => applier.ApplyAsync(Request(installation), Ct));
        }
        finally
        {
            holder?.Dispose();
        }

        Assert.Equal(UpdateCodes.FileRefused, refused.Code);
        Assert.True(refused.Retryable);
        Assert.NotEmpty(pauses);
        var ledger = installation.Ledger()!;
        var held = $"'~{Sep}{Path.GetRelativePath(Home(installation), source!)}' is held open by another program; it was tried for 0.3 seconds.";
        if (move == Aside)
        {
            Assert.Equal(
                $"moving the installed executable aside failed: {held} Nothing has been replaced yet. Run `jason update apply` again once the file is free.",
                refused.Message);
            Assert.Equal(installation.From.ToString(), installation.Installed());
            Assert.False(File.Exists(ledger.PreviousPath));
        }
        else
        {
            Assert.Equal(
                $"putting the new executable in place failed: {held} There is nothing at the install path until this succeeds: run `jason update apply` again, from {Path.Combine(installation.Update.Applier, ReleaseAssets.ExecutableName)} if `jason` is no longer on your PATH, or put {ledger.PreviousPath} back by hand.",
                refused.Message);
            Assert.True(File.Exists(ledger.StagedPath));
            Assert.False(File.Exists(installation.InstallPath));
        }
    }

    /// <summary>
    /// Anything else that refuses a move is said at once, in the operating system's words, as it always was: waiting
    /// changes no permission, no directory where the file should go, and no reader on a file the move would replace — a
    /// replace a reader holds is refused as an access-denied, which is not a hold.
    /// </summary>
    [Theory]
    [InlineData("a reader on the file it replaces")]
    [InlineData("its deletion denied")]
    [InlineData("a directory where it goes")]
    [InlineData("a directory it may not write")]
    public async Task A_move_refused_for_any_other_reason_is_said_at_once(string reason)
    {
        Assert.SkipUnless(
            OperatingSystem.IsWindows() || reason is "a directory where it goes" or "a directory it may not write",
            "Elsewhere a rename replaces a file a reader holds, and a deny on the file itself is Windows' own.");
        Assert.SkipWhen(
            reason == "a directory it may not write" && (OperatingSystem.IsWindows() || Environment.UserName == "root"),
            "A read-only directory stops no rename on Windows, and none for the superuser.");
        using var installation = new FakeInstallation().WithRuntime();
        var undo = new List<Action>();
        installation.OnStop = () => undo.Add(Refuse(installation, reason));
        var pauses = new List<TimeSpan>();
        var applier = Applier(installation, InstallationRemovers.HeldFiles, (delay, _) => pauses.Add(delay));

        UpdateException refused;
        try
        {
            refused = await Assert.ThrowsAsync<UpdateException>(() => applier.ApplyAsync(Request(installation), Ct));
        }
        finally
        {
            undo.ForEach(action => action());
        }

        Assert.Equal(UpdateCodes.FileRefused, refused.Code);
        Assert.False(refused.Retryable);
        Assert.Empty(pauses);
        Assert.Contains(" failed at '", refused.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("held open", refused.Message, StringComparison.Ordinal);
    }

    /// <summary>Stages one way a move is refused, and returns what puts it back.</summary>
    private static Action Refuse(FakeInstallation installation, string reason)
    {
        switch (reason)
        {
            case "a reader on the file it replaces":
                {
                    var previous = installation.Update.PreviousExecutable;
                    Directory.CreateDirectory(Path.GetDirectoryName(previous)!);
                    File.WriteAllText(previous, "jason an earlier version");
                    var reader = Hold(previous);
                    return reader.Dispose;
                }

            case "its deletion denied":
                return OperatingSystem.IsWindows()
                    ? DenyDeletion(installation.InstallPath)
                    : throw new PlatformNotSupportedException("a deny on the file itself is Windows' own");

            case "a directory where it goes":
                {
                    // The install path stops being somewhere a file can be moved to, so the move that meets it is the swap's.
                    File.Delete(installation.InstallPath);
                    Directory.CreateDirectory(installation.InstallPath);
                    return () => { };
                }

            case "a directory it may not write":
                return OperatingSystem.IsWindows()
                    ? throw new PlatformNotSupportedException("a read-only directory stops no rename on Windows")
                    : ReadOnly(installation.InstallDirectory);

            default:
                throw new ArgumentOutOfRangeException(nameof(reason), reason, null);
        }
    }

    /// <summary>
    /// Denies this account the deletion of the file, which is what a rename of it needs: on the file itself, and as a child
    /// of its directory, which grants it otherwise. Returns the undoing.
    /// </summary>
    [SupportedOSPlatform("windows")]
    internal static Action DenyDeletion(string path)
    {
        var account = WindowsIdentity.GetCurrent().User!;
        var file = new FileInfo(path);
        var directory = file.Directory!;
        var onFile = new FileSystemAccessRule(account, FileSystemRights.Delete, AccessControlType.Deny);
        var onChildren = new FileSystemAccessRule(account, FileSystemRights.DeleteSubdirectoriesAndFiles, AccessControlType.Deny);

        var fileSecurity = file.GetAccessControl();
        fileSecurity.AddAccessRule(onFile);
        file.SetAccessControl(fileSecurity);
        var directorySecurity = directory.GetAccessControl();
        directorySecurity.AddAccessRule(onChildren);
        directory.SetAccessControl(directorySecurity);
        return () =>
        {
            var directoryNow = directory.GetAccessControl();
            directoryNow.RemoveAccessRule(onChildren);
            directory.SetAccessControl(directoryNow);
            var fileNow = file.GetAccessControl();
            fileNow.RemoveAccessRule(onFile);
            file.SetAccessControl(fileNow);
        };
    }

    /// <summary>Makes the directory one this account may read and not write, and returns the undoing.</summary>
    [UnsupportedOSPlatform("windows")]
    private static Action ReadOnly(string directory)
    {
        var mode = File.GetUnixFileMode(directory);
        File.SetUnixFileMode(directory, UnixFileMode.UserRead | UnixFileMode.UserExecute);
        return () => File.SetUnixFileMode(directory, mode);
    }

    /// <summary>The file a move moves: the installed executable aside, or the staged one into its place.</summary>
    private static string Moved(FakeInstallation installation, string move) =>
        move == Aside ? installation.InstallPath : installation.Ledger()!.StagedPath;

    /// <summary>Opened as a reader opens a file: reading it, and letting others read it, and nothing else.</summary>
    private static FileStream Hold(string file) => new(file, FileMode.Open, FileAccess.Read, FileShare.Read);

    /// <summary>The directory these tests write paths from: the one holding the installation's whole tree.</summary>
    private static string Home(FakeInstallation installation) => Path.GetDirectoryName(installation.InstallDirectory)!;

    private static UpdateApplier Applier(FakeInstallation installation, HeldFileRule rule, Action<TimeSpan, CancellationToken> pause) =>
        new(installation.Env with { Identity = new IdentityFacts(Home(installation), "someone", "a-machine", null, null) }, installation.Update, TimeProvider.System, installation.InstallPath)
        {
            HeldFiles = rule,
            Pause = pause,
        };

    private static UpdateRequest Request(FakeInstallation installation) => new(installation.Feed, null, TimeSpan.Zero);
}
