using System.Xml.Linq;

namespace Jason.Cli.Tests;

/// <summary>
/// The release this tree is on its way to: <c>VersionPrefix</c> as <c>dotnet/Directory.Build.props</c> writes it. The
/// twin of the runtime tests' own, since this project does not reference them. A page held to the version this tree
/// builds is compared with this file, never with the build's own stamp, which a release build sets from outside and a
/// development build carries with <c>-dev</c> after it.
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
        var props = XDocument.Load(Path.Combine(directory.FullName, "dotnet", "Directory.Build.props"));
        var prefix = Assert.Single(props.Descendants("VersionPrefix")).Value.Trim();
        Assert.NotEmpty(prefix);
        return prefix;
    }
}
