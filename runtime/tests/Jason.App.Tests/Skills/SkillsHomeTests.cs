using Jason.Cli;
using Jason.Runtime.Tests;

namespace Jason.App.Tests.Skills;

/// <summary>
/// What this repository publishes about where its skills live, and about a verb it does not have yet. Both are
/// the same failure in two shapes: a document that was true when somebody wrote it and is not true of the tree
/// it ships in.
/// </summary>
public class SkillsHomeTests
{
    /// <summary>
    /// Every markdown document this repository publishes, walked rather than listed. A fixed list of the five
    /// files that were on somebody's mind is exactly how two documents went on publishing the old home — the
    /// canonical-operations README and the divergence note, neither of which anybody thinks of as a document
    /// about skills.
    /// </summary>
    private static IEnumerable<string> PublicDocuments() =>
        Directory
            .EnumerateFiles(SkillPack.RepositoryRoot(), "*.md", SearchOption.AllDirectories)
            .Where(file =>
                !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                && !file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal));

    [Fact]
    public void No_public_document_still_says_the_business_half_has_not_moved()
    {
        foreach (var file in PublicDocuments())
        {
            var text = File.ReadAllText(file);
            foreach (var stale in (string[])["has not moved", "is a placeholder"])
            {
                Assert.False(
                    text.Contains(stale, StringComparison.OrdinalIgnoreCase),
                    $"'{file}' still says '{stale}'. The business pack is in this repository now, and a "
                    + "document that says otherwise sends a reader somewhere else to look for it.");
            }
        }
    }

    /// <summary>
    /// The old home may still be named — it is where this knowledge was published, and erasing that would be
    /// its own kind of false. What it may not be is named in the present tense, as the place the pack lives or
    /// is published now.
    /// </summary>
    [Fact]
    public void A_document_that_names_the_old_home_names_it_as_the_former_one()
    {
        foreach (var file in PublicDocuments())
        {
            var text = File.ReadAllText(file);
            if (!text.Contains("reply-team/reply-skills", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            Assert.True(
                text.Contains("formerly", StringComparison.OrdinalIgnoreCase),
                $"'{file}' names reply-team/reply-skills and nowhere calls it the former home. The pack is in "
                + "this repository now; a document that publishes the old address in the present tense sends a "
                + "reader to look for a file that is not there.");
        }
    }

    /// <summary>
    /// <c>jason skills install</c> does not exist in this build. A document that prints it has to say so — and
    /// has to stop saying so the moment it does exist, or the sentence that was true becomes the false claim in
    /// reverse and nothing goes red.
    /// </summary>
    /// <remarks>
    /// The condition is read from the tree rather than from the calendar or a constant somebody remembers to
    /// flip: the bare noun is handed to this program's own argument router, through the four seams the pack
    /// guard uses, and what is read is the parse error it comes back with. A parse error is answered before any
    /// action runs, so nothing is installed anywhere by asking.
    /// </remarks>
    /// <remarks>
    /// The exit code alone cannot answer this and <c>--help</c> cannot either. <c>--help</c> is a global option:
    /// this program prints the root help and exits 0 whatever unknown token precedes it. And a bare noun exits 2
    /// both while it is missing — "unrecognized" — and once it exists, because a noun with no verb after it is
    /// itself a usage error. Only the text tells the two apart, so the text is what is read.
    /// </remarks>
    [Fact]
    public async Task A_document_that_names_a_verb_this_build_lacks_says_when_it_arrives()
    {
        const string Marker = "does not ship in this build yet";

        using var tree = new TempTree();
        var error = new StringWriter();
        var exit = await CliApp.RunAsync(
            ["skills"],
            DocumentedSkillCommandsTests.Machine(tree, error),
            TestContext.Current.CancellationToken);

        var absent = exit == ExitCodes.Usage
            && error.ToString().Contains("Unrecognized command or argument 'skills'", StringComparison.Ordinal);

        foreach (var file in PublicDocuments())
        {
            var text = File.ReadAllText(file);
            if (!text.Contains("jason skills install", StringComparison.Ordinal))
            {
                continue;
            }

            if (absent)
            {
                Assert.True(
                    text.Contains(Marker, StringComparison.Ordinal),
                    $"'{file}' names 'jason skills install' and never says it does not exist yet. A reader who "
                    + "types it against this build gets a usage error.");
            }
            else
            {
                Assert.False(
                    text.Contains(Marker, StringComparison.Ordinal),
                    $"'{file}' says 'jason skills install' {Marker}, and this build answers it. The sentence "
                    + "was true of the increment that wrote it and is false of this one.");
            }
        }
    }

    /// <summary>
    /// A CODEOWNERS entry naming a team that does not exist is a promise of review nobody receives, and in a
    /// public repository it is read as a statement about how this project is maintained.
    /// </summary>
    [Fact]
    public void Codeowners_names_no_team_that_does_not_exist()
    {
        var file = Path.Combine(SkillPack.RepositoryRoot(), "CODEOWNERS");
        Assert.True(File.Exists(file), "The repository has no CODEOWNERS.");

        foreach (var line in File.ReadAllLines(file))
        {
            var text = line.Trim();
            if (text.Length == 0 || text.StartsWith('#'))
            {
                continue;
            }

            var owner = text.IndexOf('@', StringComparison.Ordinal);
            Assert.True(owner > 0, $"'{text}' names no owner at all.");
            Assert.DoesNotContain(
                "/",
                text[owner..],
                StringComparison.Ordinal);
        }
    }
}
