using Jason.Contracts.Update;
using Jason.Runtime.Tests;

namespace Jason.Contracts.Tests.Update;

/// <summary>
/// The names a release publishes, which have to be the same in three places that cannot see each other: the
/// workflow that writes them, the manifest that lists them, and the applier that downloads one. They are here
/// so that there is one place, and the workflow guard reads this type rather than a string in a YAML file.
/// </summary>
public class ReleaseAssetsTests
{
    /// <summary>
    /// The names carry no version on purpose: `…/releases/latest/download/<asset>` then always resolves, and a
    /// README that names one never has to be edited again.
    /// </summary>
    [Theory]
    [InlineData("win-x64", "jason-win-x64.zip")]
    [InlineData("linux-x64", "jason-linux-x64.tar.gz")]
    [InlineData("osx-arm64", "jason-osx-arm64.tar.gz")]
    public void Each_platform_has_one_stable_asset_name(string rid, string asset) =>
        Assert.Equal(asset, ReleaseAssets.For(rid));

    [Fact]
    public void The_three_supported_platforms_are_the_three_the_build_publishes() =>
        Assert.Equal(["win-x64", "linux-x64", "osx-arm64"], ReleaseAssets.Rids);

    [Fact]
    public void A_platform_nothing_is_built_for_has_no_asset_to_offer() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => ReleaseAssets.For("linux-arm64"));

    [Fact]
    public void The_two_assets_that_are_not_archives_are_named_here_too()
    {
        Assert.Equal("manifest.json", ReleaseAssets.Manifest);
        Assert.Equal("checksums.txt", ReleaseAssets.Checksums);
    }

    /// <summary>
    /// The Reply plugin's package is a release's own file too, under a name with no version in it: the version is inside
    /// its <c>plugin.yaml</c>, and a stable name lets a pinned address and the latest one both resolve.
    /// </summary>
    [Fact]
    public void The_reply_plugin_s_package_has_one_stable_asset_name() =>
        Assert.Equal("jason-plugin-reply.zip", ReleaseAssets.ReplyPlugin);

    /// <summary>
    /// The journal forwarder's package is a release's own file as well, named as every plugin package is named, by its id
    /// after <c>jason-plugin-</c>: that is the name the manifest's writer requires of a package it lists under
    /// <c>plugins</c>, and the name <c>jason plugin install journal-forwarder</c> downloads.
    /// </summary>
    [Fact]
    public void The_journal_forwarder_s_package_has_one_stable_asset_name() =>
        Assert.Equal("jason-plugin-journal-forwarder.zip", ReleaseAssets.JournalForwarderPlugin);

    /// <summary>
    /// The three packages that deliver Jason's notifications are named the same way, each by its id, which is the name
    /// <c>jason plugin install</c> downloads.
    /// </summary>
    [Fact]
    public void The_notification_packages_each_have_one_stable_asset_name() =>
        Assert.Equal(
            ["jason-plugin-ntfy.zip", "jason-plugin-slack.zip", "jason-plugin-telegram.zip"],
            [ReleaseAssets.NtfyPlugin, ReleaseAssets.SlackPlugin, ReleaseAssets.TelegramPlugin]);

    /// <summary>
    /// The UI's package, which <c>jason ui install</c> fetches beside the checksums: one name for every release, its
    /// version inside its own <c>manifest.json</c>.
    /// </summary>
    [Fact]
    public void The_ui_s_package_has_one_stable_asset_name() =>
        Assert.Equal("jason-ui.zip", ReleaseAssets.Ui);

    /// <summary>
    /// The skills' package, which <c>jason skills install</c> fetches beside the checksums when it is given no source: the
    /// two skill packs at the release's tag, under one name for every release.
    /// </summary>
    [Fact]
    public void The_skills_package_has_one_stable_asset_name() =>
        Assert.Equal("jason-skills.zip", ReleaseAssets.Skills);

    /// <summary>
    /// Every file a release carries, in the order the workflow attaches them: the three archives, the feed and its
    /// checksums, the two install scripts, the five plugins' packages, the UI's and the skills', and the desktop's
    /// thirteen: the Windows installer with its blockmap and feed, each Mac architecture's dmg, zip and the zip's blockmap,
    /// the Mac feed, and Linux's deb and AppImage with the feed that names both. Twenty-seven, and a twenty-eighth is a
    /// decision rather than a drift.
    /// </summary>
    [Fact]
    [Critical(Family.I1, "the release's asset set")]
    public void A_release_publishes_exactly_these_twenty_seven_files() =>
        Assert.Equal(
            ["jason-win-x64.zip", "jason-linux-x64.tar.gz", "jason-osx-arm64.tar.gz", "manifest.json", "checksums.txt", "install.sh", "install.ps1", "jason-plugin-reply.zip", "jason-plugin-journal-forwarder.zip", "jason-plugin-ntfy.zip", "jason-plugin-slack.zip", "jason-plugin-telegram.zip", "jason-ui.zip", "jason-skills.zip",
                "jason-desktop-windows-x64-setup.exe", "jason-desktop-windows-x64-setup.exe.blockmap", "latest.yml",
                "jason-desktop-macos-arm64.dmg", "jason-desktop-macos-arm64.zip", "jason-desktop-macos-arm64.zip.blockmap",
                "jason-desktop-macos-x64.dmg", "jason-desktop-macos-x64.zip", "jason-desktop-macos-x64.zip.blockmap", "latest-mac.yml",
                "jason-desktop-linux-x64.deb", "jason-desktop-linux-x64.AppImage", "latest-linux.yml"],
            ReleaseAssets.Published);

    /// <summary>
    /// An asset name arrives from a feed this runtime did not write and becomes part of a URL and part of a
    /// path. Anything that could leave the directory it is meant to land in, or that is not a file name at all,
    /// is refused before either is built — the test writes its own hostile names.
    /// </summary>
    [Theory]
    // Critical: an asset name cannot climb.
    [InlineData("jason-win-x64.zip", true, Traits = ["Category", "Critical", "CriticalFamily", nameof(Family.I1)])]
    [InlineData("checksums.txt", true)]
    [InlineData("a", true)]
    [InlineData("", false)]
    [InlineData(" ", false)]
    [InlineData("..", false)]
    [InlineData("../evil", false)]
    [InlineData("..\\evil", false)]
    [InlineData("dir/jason.zip", false)]
    [InlineData("/etc/passwd", false)]
    [InlineData("C:\\windows\\system32", false)]
    [InlineData("jason .zip", false)]
    [InlineData("jason%2e%2e.zip", false)]
    [InlineData("jason\u0000.zip", false)]
    [InlineData("jason\n.zip", false)]
    [InlineData("-jason.zip", false)]
    public void An_asset_name_is_a_file_name_and_nothing_else(string name, bool wellFormed) =>
        Assert.Equal(wellFormed, ReleaseAssets.IsWellFormedAssetName(name));

    [Fact]
    public void An_asset_name_longer_than_any_real_one_is_refused() =>
        Assert.False(ReleaseAssets.IsWellFormedAssetName(new string('a', 101)));

    /// <summary>The name of the file inside the archive, which is what the applier swaps and the scripts install.</summary>
    [Fact]
    public void The_executable_is_named_for_the_platform_it_runs_on() =>
        Assert.Equal(OperatingSystem.IsWindows() ? "jason.exe" : "jason", ReleaseAssets.ExecutableName);

    /// <summary>
    /// This machine's own platform, which is null where Jason publishes nothing — a Linux on ARM, say. Null is
    /// the honest answer there: the caller says "no release is built for this machine" rather than downloading
    /// something that cannot run.
    /// </summary>
    [Fact]
    public void This_machine_is_one_of_the_three_or_none_of_them() =>
        Assert.True(ReleaseAssets.CurrentRid is null || ReleaseAssets.Rids.Contains(ReleaseAssets.CurrentRid));
}
