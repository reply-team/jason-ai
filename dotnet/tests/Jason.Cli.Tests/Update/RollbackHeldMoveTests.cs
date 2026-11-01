using Jason.Cli.Doctor;
using Jason.Cli.Uninstall;
using Jason.Cli.Update;
using Jason.Contracts.Files;
using Jason.Contracts.Update;

namespace Jason.Cli.Tests.Update;

/// <summary>
/// A rollback's two moves of the executable — the installed one aside, the kept one back — wait for another program to let
/// go of the file they move by the same rule as an update's, and say a file still held at the end in the same sentence.
/// </summary>
public sealed class RollbackHeldMoveTests
{
    private const string Aside = "aside";

    private const string Back = "back";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static string Sep => Path.DirectorySeparatorChar.ToString();

    private static HeldFileRule Short { get; } =
        InstallationRemovers.HeldFiles with { Wait = TimeSpan.FromMilliseconds(300), Interval = TimeSpan.FromMilliseconds(50) };

    [Fact]
    public void The_moves_wait_by_the_uninstalls_own_rule()
    {
        using var installation = new FakeInstallation();
        Assert.Same(InstallationRemovers.HeldFiles, installation.Rollback().HeldFiles);
    }

    [Theory]
    [InlineData(Aside)]
    [InlineData(Back)]
    public async Task An_executable_held_for_a_moment_is_moved_once_it_is_let_go(string move)
    {
        using var installation = await UpdatedAsync();
        var holder = Hold(Moved(installation, move));
        var pauses = new List<TimeSpan>();
        var rollback = Rollback(installation, InstallationRemovers.HeldFiles, (delay, _) =>
        {
            pauses.Add(delay);
            if (pauses.Count == 2)
            {
                holder.Dispose();
            }
        });

        try
        {
            await rollback.RollBackAsync(Ct);
        }
        finally
        {
            holder.Dispose();
        }

        Assert.Equal(installation.From.ToString(), installation.Installed());
        Assert.Equal(OperatingSystem.IsWindows() ? [TimeSpan.FromMilliseconds(250), TimeSpan.FromMilliseconds(250)] : [], pauses);
    }

    [Theory]
    [InlineData(Aside)]
    [InlineData(Back)]
    public async Task An_executable_held_for_the_whole_wait_is_refused_in_a_sentence_of_its_own(string move)
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Only Windows refuses to rename a file another program holds open.");
        using var installation = await UpdatedAsync();
        var ledger = installation.Ledger()!;
        var source = Moved(installation, move);
        var pauses = new List<TimeSpan>();
        var rollback = Rollback(installation, Short, (delay, _) => pauses.Add(delay));

        UpdateException refused;
        using (Hold(source))
        {
            refused = await Assert.ThrowsAsync<UpdateException>(() => rollback.RollBackAsync(Ct));
        }

        Assert.Equal(UpdateCodes.FileRefused, refused.Code);
        Assert.True(refused.Retryable);
        Assert.NotEmpty(pauses);
        var held = $"'~{Sep}{Path.GetRelativePath(Home(installation), source)}' is held open by another program; it was tried for 0.3 seconds.";
        Assert.True(File.Exists(ledger.PreviousPath), "The executable to put back moved.");
        if (move == Aside)
        {
            Assert.Equal(
                $"moving the installed executable aside failed: {held} Nothing has been put back yet. {ledger.PreviousPath} is still the executable to restore by hand.",
                refused.Message);
            Assert.Equal(installation.To.ToString(), installation.Installed());
        }
        else
        {
            Assert.Equal(
                $"putting the previous executable back failed: {held} The install path is empty: copy {ledger.PreviousPath} there by hand.",
                refused.Message);
            Assert.False(File.Exists(installation.InstallPath));
        }
    }

    /// <summary>
    /// The notices go back with the executable and wait as it does: the one beside the install path, held as it is removed,
    /// and the kept one, held as it is moved back, are each moved once they are let go.
    /// </summary>
    [Theory]
    [InlineData(Aside)]
    [InlineData(Back)]
    public async Task A_notice_held_for_a_moment_goes_back_once_it_is_let_go(string move)
    {
        using var installation = await UpdatedAsync();
        var ledger = installation.Ledger()!;
        var notice = move == Aside
            ? ReleaseAssets.NoticeBeside(installation.InstallPath, "LICENSE")
            : ReleaseAssets.NoticeBeside(ledger.PreviousPath, "LICENSE");
        var holder = Hold(notice);
        var pauses = new List<TimeSpan>();
        var rollback = Rollback(installation, InstallationRemovers.HeldFiles, (delay, _) =>
        {
            pauses.Add(delay);
            if (pauses.Count == 2)
            {
                holder.Dispose();
            }
        });

        try
        {
            await rollback.RollBackAsync(Ct);
        }
        finally
        {
            holder.Dispose();
        }

        Assert.Equal(installation.From.ToString(), installation.Installed());
        Assert.Equal(KeptLicence, File.ReadAllText(ReleaseAssets.NoticeBeside(installation.InstallPath, "LICENSE")));
        Assert.Equal(OperatingSystem.IsWindows() ? [TimeSpan.FromMilliseconds(250), TimeSpan.FromMilliseconds(250)] : [], pauses);
    }

    /// <summary>A deletion denied is not a hold: it is said at once, in the system's words, as before.</summary>
    [Fact]
    public async Task A_move_refused_for_another_reason_is_said_at_once()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "A deny on the file itself is Windows' own way to refuse its rename.");
        using var installation = await UpdatedAsync();
        var pauses = new List<TimeSpan>();
        var rollback = Rollback(installation, InstallationRemovers.HeldFiles, (delay, _) => pauses.Add(delay));

        UpdateException refused;
        var undo = OperatingSystem.IsWindows() ? UpdateHeldMoveTests.DenyDeletion(installation.InstallPath) : throw new PlatformNotSupportedException();
        try
        {
            refused = await Assert.ThrowsAsync<UpdateException>(() => rollback.RollBackAsync(Ct));
        }
        finally
        {
            undo();
        }

        Assert.Equal(UpdateCodes.FileRefused, refused.Code);
        Assert.False(refused.Retryable);
        Assert.Empty(pauses);
        Assert.StartsWith("moving the installed executable aside failed at '", refused.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("held open", refused.Message, StringComparison.Ordinal);
    }

    /// <summary>The file a move moves: the installed executable aside, or the kept one back into its place.</summary>
    private static string Moved(FakeInstallation installation, string move) =>
        move == Aside ? installation.InstallPath : installation.Ledger()!.PreviousPath;

    private static FileStream Hold(string file) => new(file, FileMode.Open, FileAccess.Read, FileShare.Read);

    private static string Home(FakeInstallation installation) => Path.GetDirectoryName(installation.InstallDirectory)!;

    private static UpdateRollback Rollback(FakeInstallation installation, HeldFileRule rule, Action<TimeSpan, CancellationToken> pause) =>
        new(installation.Env with { Identity = new IdentityFacts(Home(installation), "someone", "a-machine", null, null) }, installation.Update, TimeProvider.System)
        {
            HeldFiles = rule,
            Pause = pause,
        };

    private const string KeptLicence = "the licence installed";

    /// <summary>An installation updated once, its licence kept with the executable it had.</summary>
    private static async Task<FakeInstallation> UpdatedAsync()
    {
        var installation = new FakeInstallation();
        installation.WithRuntime();
        FakeInstallation.WriteNoticesBeside(installation.InstallPath, new Dictionary<string, string> { ["LICENSE"] = KeptLicence });
        await installation.Applier().ApplyAsync(new UpdateRequest(installation.Feed, null, TimeSpan.FromSeconds(5)), Ct);
        return installation;
    }
}
