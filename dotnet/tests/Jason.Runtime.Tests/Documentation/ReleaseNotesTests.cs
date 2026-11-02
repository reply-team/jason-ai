using System.Text.RegularExpressions;

namespace Jason.Runtime.Tests.Documentation;

/// <summary>
/// What a release's own notes, <c>RELEASE-NOTES.md</c>, are held to beyond their shape, which
/// <see cref="ReleaseWorkflowTests"/> reads. A change to the notes runs the documentation classes, so every test that
/// reads them is one of those: a test elsewhere that reads them would be left unrun by a change to them, and found red
/// only later, by whoever runs the whole suite.
/// </summary>
public partial class ReleaseNotesTests
{
    /// <summary>
    /// Every test source that reads the notes, by their name or by <see cref="Links.ReleaseNotes"/>, is in a
    /// documentation namespace, in every test project.
    /// </summary>
    [Fact]
    public void Every_test_that_reads_the_notes_is_in_a_documentation_namespace()
    {
        var root = RepositoryTree.Root();
        var sources = RepositoryTree.Files(root, "dotnet/tests").Where(file => file.EndsWith(".cs", StringComparison.Ordinal)).ToList();
        Assert.Contains("dotnet/tests/Jason.Runtime.Tests/Documentation/ReleaseNotesTests.cs", sources);

        var elsewhere = sources
            .Where(file =>
            {
                var text = File.ReadAllText(Path.Combine(root, file.Replace('/', Path.DirectorySeparatorChar)));
                var reads = text.Contains("\"" + Links.ReleaseNotes + "\"", StringComparison.Ordinal)
                    || text.Contains("Links." + nameof(Links.ReleaseNotes), StringComparison.Ordinal);
                return reads && !Namespace(text).EndsWith(".Documentation", StringComparison.Ordinal);
            })
            .ToList();

        Assert.True(elsewhere.Count == 0, "These read the release's notes outside a documentation namespace:\n" + string.Join('\n', elsewhere));
    }

    /// <summary>
    /// A LinkedIn message may name a Reply sending schedule from the Reply package's 0.14.3, so no release's notes keep
    /// the known issue that a schedule times an email only. The package's item in the notes of its own release, 0.23.0,
    /// stays at that release's tag; a later release's notes are not held to it.
    /// </summary>
    [Fact]
    public void No_notes_keep_the_known_issue_that_a_sending_schedule_times_an_email_only()
    {
        var path = Path.Combine(RepositoryTree.Root(), Links.ReleaseNotes);
        var notes = File.Exists(path) ? Regex.Replace(File.ReadAllText(path), @"\s+", " ") : "";

        Assert.DoesNotContain("a sending schedule times an email only", notes, StringComparison.Ordinal);
    }

    /// <summary>The namespace a source file declares, file-scoped or in a block, or nothing where it declares none.</summary>
    private static string Namespace(string text) => NamespaceLine().Match(text) is { Success: true } match ? match.Groups[1].Value : "";

    [GeneratedRegex(@"^namespace\s+([A-Za-z0-9_.]+)", RegexOptions.Multiline | RegexOptions.CultureInvariant)]
    private static partial Regex NamespaceLine();
}
