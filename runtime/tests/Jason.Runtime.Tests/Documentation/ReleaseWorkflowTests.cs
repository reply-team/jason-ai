using Jason.Contracts.Update;
using YamlDotNet.RepresentationModel;

namespace Jason.Runtime.Tests.Documentation;

/// <summary>
/// The release pipeline's files, held to what the code they package already says. The action that packs an
/// executable, the workflow that runs it on a tag and the manifest the runtime later reads cannot see each
/// other, so the names they must agree on are read from <see cref="ReleaseAssets"/> here rather than typed a
/// second time — and what a workflow may do in only one place is asserted on the parsed document, not counted
/// as substrings.
/// <para>
/// What these tests do not prove is the workflows running: GitHub runs them, and a composite action has no
/// runner on a developer's machine. The packaging half is exercised by every pull request's CI; the release
/// half is not, until a tag exists.
/// </para>
/// </summary>
public class ReleaseWorkflowTests
{
    private static readonly string[] Action = [".github", "actions", "package", "action.yml"];
    private static readonly string[] Ci = [".github", "workflows", "ci.yml"];

    [Fact]
    public void The_packaging_action_names_the_assets_the_code_names()
    {
        var action = Read(Action);
        foreach (var rid in ReleaseAssets.Rids)
        {
            Assert.Contains(rid, action, StringComparison.Ordinal);
            Assert.Contains(ReleaseAssets.For(rid), action, StringComparison.Ordinal);
        }

        // Q5: MIT's own condition is that the notice accompanies every copy of the software.
        Assert.Contains("LICENSE", action, StringComparison.Ordinal);
    }

    [Fact]
    public void Ci_packages_through_the_action_rather_than_beside_it()
    {
        var ci = Read(Ci);
        var action = Read(Action);

        Assert.Contains("./.github/actions/package", ci, StringComparison.Ordinal);

        // PB3: the action owns the publish, so ci.yml must no longer carry one — counting publishes in ci.yml
        // would assert the opposite of what is wanted the moment this succeeds.
        Assert.Contains("dotnet publish", action, StringComparison.Ordinal);
        Assert.DoesNotContain("dotnet publish", ci, StringComparison.Ordinal);
    }

    /// <summary>
    /// PB2: the version is an input, so <c>ci.yml</c> and <c>release.yml</c> call the action identically and
    /// neither the action nor the CI workflow reads a tag that, on a pull request, does not exist.
    /// </summary>
    [Fact]
    public void The_action_is_told_the_version_rather_than_reading_the_tag()
    {
        var action = Read(Action);
        var inputs = Mapping(Parse(action), "inputs");

        Assert.Contains("version", inputs.Children.Keys.Select(key => key.ToString()));
        Assert.Contains("rid", inputs.Children.Keys.Select(key => key.ToString()));
        Assert.Contains("-p:Version=", action, StringComparison.Ordinal);
        Assert.DoesNotContain("github.ref_name", action, StringComparison.Ordinal);
        Assert.DoesNotContain("github.ref", action, StringComparison.Ordinal);
    }

    /// <summary>
    /// The archive holds the executable and <c>LICENSE</c> and nothing else, which is only true if the
    /// executable is one file. A self-contained single-file publish leaves native libraries beside it — SQLite's
    /// among them — unless the project says to bundle them, and nothing in CI would have noticed: the smoke
    /// test never opens a database. The project says so, and the action refuses a publish directory holding
    /// anything but the executable and its symbols, so the property cannot be lost quietly.
    /// </summary>
    [Fact]
    public void The_published_executable_is_one_file_with_its_native_libraries_inside()
    {
        var project = Read(["runtime", "src", "Jason.App", "Jason.App.csproj"]);
        Assert.Contains("<IncludeNativeLibrariesForSelfExtract>true</IncludeNativeLibrariesForSelfExtract>", project, StringComparison.Ordinal);

        Assert.Contains(".pdb", Read(Action), StringComparison.Ordinal);
    }

    private static string Read(string[] relative) => File.ReadAllText(Path.Combine([RepositoryRoot(), .. relative]));

    private static YamlMappingNode Parse(string yaml)
    {
        var stream = new YamlStream();
        stream.Load(new StringReader(yaml));
        return (YamlMappingNode)stream.Documents[0].RootNode;
    }

    private static YamlMappingNode Mapping(YamlMappingNode node, string key)
    {
        Assert.True(node.Children.TryGetValue(new YamlScalarNode(key), out var child), $"There is no '{key}' mapping.");
        return Assert.IsType<YamlMappingNode>(child);
    }

    /// <summary>The repository root, found by walking up from the test binaries to the directory holding <c>global.json</c>.</summary>
    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "global.json")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return directory.FullName;
    }
}
