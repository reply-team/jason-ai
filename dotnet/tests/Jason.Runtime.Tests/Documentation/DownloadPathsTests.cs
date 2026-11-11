using System.Text.RegularExpressions;
using Jason.Contracts.Update;

namespace Jason.Runtime.Tests.Documentation;

/// <summary>
/// The download address's short desktop paths, <c>get/src/paths.mjs</c>, redirect to files of the latest release by
/// name. A link a person was given cannot be corrected after a release renames a file, so the names are held here to
/// the list the release publishes, both ways: a renamed file fails this build and not a link, and a desktop file a
/// person installs from that no path names fails it too.
/// </summary>
public sealed partial class DownloadPathsTests
{
    private const string PathsFile = "get/src/paths.mjs";

    /// <summary>
    /// The files of <see cref="ReleaseAssets.Desktop"/> a person installs from: not a block map, an update feed or the Mac
    /// update archive, which only an installed desktop's updater reads.
    /// </summary>
    private static IReadOnlyList<string> Installable { get; } = ReleaseAssets.Desktop
        .Where(name => !name.EndsWith(".blockmap", StringComparison.Ordinal)
            && !name.EndsWith(".yml", StringComparison.Ordinal)
            && !name.EndsWith(".zip", StringComparison.Ordinal))
        .ToList();

    [Fact]
    public void The_desktop_paths_name_exactly_the_desktop_files_a_person_installs_from()
    {
        var named = Named(File.ReadAllText(Path.Combine(RepositoryTree.Root(), PathsFile.Replace('/', Path.DirectorySeparatorChar))));

        Assert.Equal(Installable.Order(StringComparer.Ordinal), named.Order(StringComparer.Ordinal));
        Assert.Equal(named.Count, named.Distinct(StringComparer.Ordinal).Count());
    }

    /// <summary>The installable files are five today: the Windows installer, a disk image per Mac architecture, the deb and the AppImage.</summary>
    [Fact]
    public void Five_desktop_files_are_installed_from()
    {
        Assert.Equal(
            ["jason-desktop-windows-x64-setup.exe", "jason-desktop-macos-arm64.dmg", "jason-desktop-macos-x64.dmg", "jason-desktop-linux-x64.deb", "jason-desktop-linux-x64.AppImage"],
            Installable);
    }

    /// <summary>The reader, on texts, before it is trusted with the file.</summary>
    [Theory]
    [InlineData("'/desktop/mac': 'https://get.jasonharness.ai/releases/latest/download/jason-desktop-macos-arm64.dmg',", "jason-desktop-macos-arm64.dmg")]
    [InlineData("'/desktop/x': 'https://get.jasonharness.ai/releases/latest/download/jason-desktop-renamed.exe',", "jason-desktop-renamed.exe")]
    [InlineData("export const INSTALL_PAGE = 'https://raw.githubusercontent.com/reply-team/jason-ai/main/docs/INSTALL.md';", null)]
    public void The_reader_finds_each_file_a_path_redirects_to(string line, string? file)
    {
        string[] expected = file is null ? [] : [file];
        Assert.Equal(expected, Named(line));
    }

    private static List<string> Named(string text) =>
        LatestDownload().Matches(text).Select(match => match.Groups["file"].Value).ToList();

    [GeneratedRegex(@"https://get\.jasonharness\.ai/releases/latest/download/(?<file>[A-Za-z0-9._-]+)")]
    private static partial Regex LatestDownload();
}
