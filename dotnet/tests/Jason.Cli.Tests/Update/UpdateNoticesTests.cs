using Jason.Cli.Update;
using Jason.Contracts.Update;

namespace Jason.Cli.Tests.Update;

/// <summary>
/// The notices travel with the executable, at fixed names, in whichever directory it is in: staged, installed, or kept
/// for a rollback. One rule holds for each of them, as it does in the install scripts: <c>jason-</c> and its name is
/// there when the release it belongs to carried it, and is not when that release did not. The ledger records the
/// executable's paths only, and every notice's path is the executable's directory and the notice's name.
/// </summary>
public sealed class UpdateNoticesTests
{
    private const string Licence = "jason-LICENSE";

    private const string Notices = "jason-THIRD-PARTY-NOTICES";

    private static readonly Dictionary<string, string> Released = new(StringComparer.Ordinal)
    {
        ["LICENSE"] = "the licence of 9.9.9",
        ["THIRD-PARTY-NOTICES"] = "the notices of 9.9.9",
    };

    private static readonly Dictionary<string, string> Installed = new(StringComparer.Ordinal)
    {
        ["LICENSE"] = "the licence installed",
        ["THIRD-PARTY-NOTICES"] = "the notices installed",
    };

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task An_update_puts_the_release_s_notices_beside_the_executable_and_keeps_the_old_ones_with_the_old_executable()
    {
        using var installation = new FakeInstallation(releaseNotices: Released).WithRuntime();
        FakeInstallation.WriteNoticesBeside(installation.InstallPath, Installed);

        var ledger = await installation.Applier().ApplyAsync(Request(installation), Ct);

        Assert.Equal(UpdateStep.Complete, ledger.Step);
        Assert.Equal(Kept(Released), FakeInstallation.NoticesBeside(installation.InstallPath));
        Assert.Equal(Kept(Installed), FakeInstallation.NoticesBeside(installation.Update.PreviousExecutable));
        Assert.Empty(FakeInstallation.NoticesBeside(installation.Update.StagedExecutable(installation.To)));
    }

    /// <summary>
    /// An installation without notices — one an older jason updated — is updated to a release that has them, and what is
    /// kept for its rollback is that installation as it was: an earlier update's notices are not left beside its
    /// executable, so the directory never holds two versions' files.
    /// </summary>
    [Fact]
    public async Task An_update_of_an_installation_without_notices_keeps_none_of_an_earlier_update_s()
    {
        using var installation = new FakeInstallation(releaseNotices: Released).WithRuntime();
        FakeInstallation.WriteNoticesBeside(installation.Update.PreviousExecutable, new Dictionary<string, string> { ["LICENSE"] = "an earlier update's" });

        var ledger = await installation.Applier().ApplyAsync(Request(installation), Ct);

        Assert.Equal(UpdateStep.Complete, ledger.Step);
        Assert.Equal(Kept(Released), FakeInstallation.NoticesBeside(installation.InstallPath));
        Assert.Empty(FakeInstallation.NoticesBeside(installation.Update.PreviousExecutable));
    }

    /// <summary>A release without third-party notices leaves none beside the executable, the installed ones included.</summary>
    [Fact]
    public async Task An_update_to_a_release_without_a_notice_leaves_none_of_that_name()
    {
        var licence = new Dictionary<string, string> { ["LICENSE"] = "the licence of 9.9.9" };
        using var installation = new FakeInstallation(releaseNotices: licence).WithRuntime();
        FakeInstallation.WriteNoticesBeside(installation.InstallPath, Installed);

        await installation.Applier().ApplyAsync(Request(installation), Ct);

        Assert.Equal(Kept(licence), FakeInstallation.NoticesBeside(installation.InstallPath));
        Assert.Equal(Kept(Installed), FakeInstallation.NoticesBeside(installation.Update.PreviousExecutable));
    }

    /// <summary>
    /// A kill after the executable is moved aside and before its notices are: the next run moves them and finishes, with
    /// every file where an update that was never killed would have put it.
    /// </summary>
    [Fact]
    public async Task A_kill_between_the_executable_s_move_aside_and_its_notices_finishes_the_same_way()
    {
        using var installation = new FakeInstallation(releaseNotices: Released).WithRuntime();
        FakeInstallation.WriteNoticesBeside(installation.InstallPath, Installed);
        var stopped = installation.Killed(UpdateStep.Stopped);
        Directory.CreateDirectory(installation.Update.Applier);
        Directory.CreateDirectory(installation.Update.Previous);
        File.Copy(installation.InstallPath, Path.Combine(installation.Update.Applier, ReleaseAssets.ExecutableName));
        File.Move(installation.InstallPath, installation.Update.PreviousExecutable);
        (stopped with { Step = UpdateStep.Kept }).Write(installation.Update.Ledger);

        var ledger = await installation.Applier().ApplyAsync(Request(installation), Ct);

        Assert.Equal(UpdateStep.Complete, ledger.Step);
        Assert.Equal(installation.To.ToString(), installation.Installed());
        Assert.Equal(Kept(Released), FakeInstallation.NoticesBeside(installation.InstallPath));
        Assert.Equal(Kept(Installed), FakeInstallation.NoticesBeside(installation.Update.PreviousExecutable));
    }

    /// <summary>
    /// A kill between the two notices' moves aside: the executable and one notice are kept, the other is still beside the
    /// install path. The next run keeps the second and finishes, and the kept directory holds both of the installed
    /// version's notices. What an earlier update kept is cleared only with the executable's own move: cleared again on
    /// the resume, it would be the notice this update had just kept.
    /// </summary>
    [Fact]
    public async Task A_kill_between_the_two_notices_moves_aside_keeps_both_with_the_executable()
    {
        using var installation = new FakeInstallation(releaseNotices: Released).WithRuntime();
        FakeInstallation.WriteNoticesBeside(installation.InstallPath, Installed);
        var stopped = installation.Killed(UpdateStep.Stopped);
        Directory.CreateDirectory(installation.Update.Applier);
        Directory.CreateDirectory(installation.Update.Previous);
        File.Copy(installation.InstallPath, Path.Combine(installation.Update.Applier, ReleaseAssets.ExecutableName));
        File.Move(installation.InstallPath, installation.Update.PreviousExecutable);
        File.Move(
            ReleaseAssets.NoticeBeside(installation.InstallPath, "LICENSE"),
            ReleaseAssets.NoticeBeside(installation.Update.PreviousExecutable, "LICENSE"));
        (stopped with { Step = UpdateStep.Kept }).Write(installation.Update.Ledger);

        var ledger = await installation.Applier().ApplyAsync(Request(installation), Ct);

        Assert.Equal(UpdateStep.Complete, ledger.Step);
        Assert.Equal(Kept(Released), FakeInstallation.NoticesBeside(installation.InstallPath));
        Assert.Equal(Kept(Installed), FakeInstallation.NoticesBeside(installation.Update.PreviousExecutable));
    }

    /// <summary>
    /// The swap's twin: a kill between the two new notices' moves in. The next run moves the second in, and the kept
    /// directory is left as the keeping left it.
    /// </summary>
    [Fact]
    public async Task A_kill_between_the_two_new_notices_moves_in_puts_both_beside_the_executable()
    {
        using var installation = new FakeInstallation(releaseNotices: Released).WithRuntime();
        FakeInstallation.WriteNoticesBeside(installation.InstallPath, Installed);
        var kept = installation.Killed(UpdateStep.Kept);
        var staged = installation.Update.StagedExecutable(installation.To);
        File.Move(staged, installation.InstallPath);
        File.Move(ReleaseAssets.NoticeBeside(staged, "LICENSE"), ReleaseAssets.NoticeBeside(installation.InstallPath, "LICENSE"));
        (kept with { Step = UpdateStep.Swapped }).Write(installation.Update.Ledger);

        var ledger = await installation.Applier().ApplyAsync(Request(installation), Ct);

        Assert.Equal(UpdateStep.Complete, ledger.Step);
        Assert.Equal(Kept(Released), FakeInstallation.NoticesBeside(installation.InstallPath));
        Assert.Equal(Kept(Installed), FakeInstallation.NoticesBeside(installation.Update.PreviousExecutable));
        Assert.Empty(FakeInstallation.NoticesBeside(staged));
    }

    /// <summary>A kill after the new executable moved in and before its notices did: the next run moves them in.</summary>
    [Fact]
    public async Task A_kill_between_the_swap_and_the_new_notices_moving_in_finishes_the_same_way()
    {
        using var installation = new FakeInstallation(releaseNotices: Released).WithRuntime();
        FakeInstallation.WriteNoticesBeside(installation.InstallPath, Installed);
        var kept = installation.Killed(UpdateStep.Kept);
        File.Move(installation.Update.StagedExecutable(installation.To), installation.InstallPath);
        (kept with { Step = UpdateStep.Swapped }).Write(installation.Update.Ledger);

        var ledger = await installation.Applier().ApplyAsync(Request(installation), Ct);

        Assert.Equal(UpdateStep.Complete, ledger.Step);
        Assert.Equal(Kept(Released), FakeInstallation.NoticesBeside(installation.InstallPath));
        Assert.Equal(Kept(Installed), FakeInstallation.NoticesBeside(installation.Update.PreviousExecutable));
        Assert.Empty(FakeInstallation.NoticesBeside(installation.Update.StagedExecutable(installation.To)));
    }

    [Fact]
    public async Task A_rollback_puts_the_notices_of_the_version_it_returns_to_back()
    {
        using var installation = new FakeInstallation(releaseNotices: Released).WithRuntime();
        FakeInstallation.WriteNoticesBeside(installation.InstallPath, Installed);
        await installation.Applier().ApplyAsync(Request(installation), Ct);
        Assert.Equal(Kept(Released), FakeInstallation.NoticesBeside(installation.InstallPath));

        await installation.Rollback().RollBackAsync(Ct);

        Assert.Equal(installation.From.ToString(), installation.Installed());
        Assert.Equal(Kept(Installed), FakeInstallation.NoticesBeside(installation.InstallPath));
        Assert.Empty(FakeInstallation.NoticesBeside(installation.Update.PreviousExecutable));
    }

    /// <summary>
    /// The version a rollback returns to was kept with its licence alone, as a release before 0.6.0 is: the third-party
    /// notices of the version it leaves are not left beside it.
    /// </summary>
    [Fact]
    public async Task A_rollback_to_a_version_kept_with_its_licence_alone_leaves_no_third_party_notices()
    {
        var licence = new Dictionary<string, string> { ["LICENSE"] = "the licence installed" };
        using var installation = new FakeInstallation(releaseNotices: Released).WithRuntime();
        FakeInstallation.WriteNoticesBeside(installation.InstallPath, licence);
        await installation.Applier().ApplyAsync(Request(installation), Ct);
        Assert.Equal(Kept(licence), FakeInstallation.NoticesBeside(installation.Update.PreviousExecutable));

        await installation.Rollback().RollBackAsync(Ct);

        Assert.Equal(Kept(licence), FakeInstallation.NoticesBeside(installation.InstallPath));
    }

    /// <summary>
    /// The version a rollback returns to had no notices at all — it was updated by a jason before them — and none are left
    /// beside it.
    /// </summary>
    [Fact]
    public async Task A_rollback_to_a_version_kept_without_notices_leaves_none()
    {
        using var installation = new FakeInstallation(releaseNotices: Released).WithRuntime();
        await installation.Applier().ApplyAsync(Request(installation), Ct);
        Assert.Equal(Kept(Released), FakeInstallation.NoticesBeside(installation.InstallPath));

        await installation.Rollback().RollBackAsync(Ct);

        Assert.Empty(FakeInstallation.NoticesBeside(installation.InstallPath));
    }

    /// <summary>Notices as they are kept beside an executable: by their names there, with their text.</summary>
    private static Dictionary<string, string> Kept(IReadOnlyDictionary<string, string> notices) =>
        notices.ToDictionary(notice => ReleaseAssets.NoticeFileName(notice.Key), notice => notice.Value, StringComparer.Ordinal);

    private static UpdateRequest Request(FakeInstallation installation) => new(installation.Feed, null, TimeSpan.Zero);
}
