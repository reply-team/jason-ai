using Jason.Contracts.Update;

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
    /// An asset name arrives from a feed this runtime did not write and becomes part of a URL and part of a
    /// path. Anything that could leave the directory it is meant to land in, or that is not a file name at all,
    /// is refused before either is built — the test writes its own hostile names.
    /// </summary>
    [Theory]
    [InlineData("jason-win-x64.zip", true)]
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
