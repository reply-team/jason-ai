using System.Xml.Linq;

namespace Jason.Runtime.Tests;

/// <summary>
/// The release this tree is on its way to: <c>VersionPrefix</c> as <c>runtime/Directory.Build.props</c> writes it.
/// The tests that check what a build is stamped with compare the stamp against this file, so a new version is one
/// line there rather than a number in every such test — and never against the build's own version, which is the
/// value they check.
/// </summary>
public static class TreeVersion
{
    public static string Prefix()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "global.json")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        var props = XDocument.Load(Path.Combine(directory.FullName, "runtime", "Directory.Build.props"));
        var prefix = Assert.Single(props.Descendants("VersionPrefix")).Value.Trim();
        Assert.NotEmpty(prefix);
        return prefix;
    }
}
