using System.IO.Compression;
using System.Text.Json;
using Jason.Contracts.Update;
using static Jason.Runtime.Tests.Documentation.Workflows;

namespace Jason.Runtime.Tests.Documentation;

/// <summary>
/// The skills' package as a release publishes it, <see cref="ReleaseAssets.Skills"/>: written once per release by
/// <c>.github/scripts/Write-SkillsArchive.ps1</c> from <c>skills/</c> at the tag, beside a <c>source.json</c> that says
/// which repository, tag and commit the files are. <c>jason skills install</c> with no source unpacks it and records
/// what that file says.
/// </summary>
/// <remarks>
/// The script is executed, not read. The release's pull request runs this suite on the tree the tag names, so the
/// archive the release writes holds the files this test compared.
/// </remarks>
public sealed class SkillsAssetTests
{
    private static readonly string[] ArchiveScript = [".github", "scripts", "Write-SkillsArchive.ps1"];

    private static readonly Dictionary<string, string> NoEnvironment = [];

    private const string Repository = "https://github.com/example/jason";

    private const string Ref = "v9.8.7";

    private const string Commit = "0123456789abcdef0123456789abcdef01234567";

    /// <summary>
    /// The archive of the real tree holds every file under <c>skills/</c> by its path from the tree, in ordinal order —
    /// the files whose names start with a dot included, because each pack's <c>.claude-plugin/plugin.json</c> is how a host
    /// reads it — with the bytes the tree holds, and <c>source.json</c> beside them and nothing else.
    /// </summary>
    [Fact]
    public void The_archive_holds_the_tree_s_skills_with_their_dot_files_and_says_where_they_came_from()
    {
        using var tree = new TempTree();
        var root = RepositoryRoot();
        var archive = WriteArchive(tree, root);

        var skills = Path.Combine(root, "skills");
        var files = Directory.EnumerateFiles(skills, "*", new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = 0 })
            .Select(path => Path.GetRelativePath(root, path).Replace('\\', '/'))
            .Order(StringComparer.Ordinal)
            .ToList();
        Assert.Contains("skills/runtime/.claude-plugin/plugin.json", files);
        Assert.Contains("skills/business/.claude-plugin/plugin.json", files);

        using var zip = ZipFile.OpenRead(archive);
        var entries = zip.Entries.Select(entry => entry.FullName).ToList();
        List<string> expected = [.. files.Append("source.json").Order(StringComparer.Ordinal)];
        Assert.True(entries.SequenceEqual(expected), $"The archive holds, in its order, [{string.Join(", ", entries)}]; expected [{string.Join(", ", expected)}].");

        foreach (var entry in zip.Entries.Where(entry => entry.FullName != "source.json"))
        {
            using var stream = entry.Open();
            using var copy = new MemoryStream();
            stream.CopyTo(copy);
            Assert.True(
                copy.ToArray().AsSpan().SequenceEqual(File.ReadAllBytes(Path.Combine(root, entry.FullName))),
                $"{entry.FullName} in the archive is not the tree's bytes.");
        }

        using var source = JsonDocument.Parse(ReadEntry(zip, "source.json"));
        Assert.Equal(["repository", "ref", "commit"], source.RootElement.EnumerateObject().Select(property => property.Name));
        Assert.Equal(Repository, source.RootElement.GetProperty("repository").GetString());
        Assert.Equal(Ref, source.RootElement.GetProperty("ref").GetString());
        Assert.Equal(Commit, source.RootElement.GetProperty("commit").GetString());
    }

    /// <summary>
    /// The script writes nothing for a ref that is not <c>v</c> and a version, a commit that is not forty hexadecimal
    /// digits, or a repository that is not an https address: <c>source.json</c> is what an installation records, and a
    /// record that names something else than the tag the release is would be wrong for as long as it stood.
    /// </summary>
    [Theory]
    [InlineData(Repository, "main", Commit)]
    [InlineData(Repository, "9.8.7", Commit)]
    [InlineData(Repository, Ref, "0123456")]
    [InlineData(Repository, Ref, "0123456789ABCDEF0123456789ABCDEF01234567")]
    [InlineData("http://github.com/example/jason", Ref, Commit)]
    public void The_archive_is_refused_for_what_is_not_a_tag_a_commit_and_an_https_repository(string repository, string reference, string commit)
    {
        using var tree = new TempTree();
        var archive = Path.Combine(tree.NewDirectory("release"), ReleaseAssets.Skills);
        var (exit, _, _) = Script(
            NoEnvironment,
            Path.Combine([RepositoryRoot(), .. ArchiveScript]),
            "-Tree", RepositoryRoot(),
            "-Archive", archive,
            "-Repository", repository,
            "-Ref", reference,
            "-Commit", commit);

        Assert.NotEqual(0, exit);
        Assert.False(File.Exists(archive), "The script refused and still wrote an archive.");
    }

    /// <summary>The script run as the release job runs it, over <paramref name="root"/>, and the archive it wrote.</summary>
    internal static string WriteArchive(TempTree tree, string root)
    {
        var archive = Path.Combine(tree.NewDirectory("release"), ReleaseAssets.Skills);
        var (exit, stdout, stderr) = Script(
            NoEnvironment,
            Path.Combine([RepositoryRoot(), .. ArchiveScript]),
            "-Tree", root,
            "-Archive", archive,
            "-Repository", Repository,
            "-Ref", Ref,
            "-Commit", Commit);
        Assert.True(exit == 0, $"Write-SkillsArchive.ps1 exited {exit}: {stderr}");
        Assert.True(File.Exists(archive), $"Write-SkillsArchive.ps1 exited 0 and {archive} is not there. Its stdout: '{stdout.Trim()}'.");
        return archive;
    }

    private static string ReadEntry(ZipArchive zip, string name)
    {
        using var reader = new StreamReader(zip.GetEntry(name)!.Open());
        return reader.ReadToEnd();
    }
}
