using System.Text.RegularExpressions;

namespace Jason.Runtime.Tests.Documentation;

/// <summary>
/// What the programs say names only the pages the tree holds. A sentence the CLI prints, or the runtime puts into an
/// answer, is read by somebody with this repository in front of them, and a page that is not there is a dead end.
/// </summary>
/// <remarks>
/// The strings are read from each production assembly's user-string heap, as the guards that read production IL read
/// it from the file alone: every literal piece of every string the code carries — plain, verbatim, raw, interpolated, or
/// constants joined at compile time — is there, and no comment is. So no reader of C# of this repository's own stands
/// between the rule and what the programs actually carry.
/// </remarks>
public sealed partial class CompiledPageNamesTests
{
    /// <summary>A tree of the rows' own: one page, and a directory with files under it.</summary>
    private static readonly string[] Tree = ["docs/INSTALL.md", "docs/contracts/README.md", "docs/contracts/operations/campaign.create.json"];

    [Fact]
    [Critical(Family.B4, "no compiled pointer outside the tree")]
    public void No_string_the_programs_carry_names_a_page_the_tree_does_not_hold()
    {
        var root = RepositoryTree.Root();
        var tree = RepositoryTree.Files(root, "docs/");
        var found = new List<string>();
        foreach (var assembly in CompiledCalls.ProductionAssemblies(root))
        {
            foreach (var text in UserStrings.Of(assembly))
            {
                found.AddRange(Missing(text, tree).Select(page => $"{Path.GetFileName(assembly)}: {page}, in \"{text}\""));
            }
        }

        Assert.True(found.Count == 0, $"The programs name {found.Count} page(s) the tree does not hold:\n" + string.Join('\n', found));
    }

    /// <summary>The rule itself: a page of <c>docs/</c>, or a directory of it, that the tree does not hold.</summary>
    [Theory]
    [InlineData("then `jason runtime start`. docs/release-and-update.md §8 says why.", "docs/release-and-update.md")]
    [InlineData("As docs/work-execution.md.", "docs/work-execution.md")]
    [InlineData("See docs/architecture.md, and docs/notes/ for the rest.", "docs/architecture.md", "docs/notes/")]
    public void A_page_or_a_directory_of_docs_the_tree_does_not_hold_is_named(string text, params string[] expected)
    {
        Assert.Equal(expected, Missing(text, Tree));
    }

    /// <summary>And a page the tree holds, a path under one of its directories, and the same words inside an address pass.</summary>
    [Theory]
    [InlineData("INSTALL §6 is docs/INSTALL.md, which says so.")]
    [InlineData("one of those under docs/contracts/operations, not a Jason verb.")]
    [InlineData("https://example.com/docs/private.md is somebody else's page.")]
    public void A_page_the_tree_holds_or_a_path_under_one_of_its_directories_passes(string text)
    {
        Assert.Empty(Missing(text, Tree));
    }

    /// <summary>
    /// Each page or directory of <c>docs/</c> a text names that the tree holds neither as a file nor as a directory with
    /// files under it: a page as its path, a directory as its path with a <c>/</c> after it.
    /// </summary>
    private static List<string> Missing(string text, IReadOnlyList<string> tree)
    {
        var named = new List<string>();
        foreach (Match match in DocsPath().Matches(text))
        {
            // A sentence's full stop after the path is not part of it.
            var value = match.Value.TrimEnd('.');
            var path = value.EndsWith(".md", StringComparison.Ordinal) ? value : $"docs/{value["docs/".Length..].Split('/')[0]}/";
            var held = tree.Any(file => file == path || (path.EndsWith('/') && file.StartsWith(path, StringComparison.Ordinal)));
            if (!held)
            {
                named.Add(path);
            }
        }

        return named;
    }

    /// <summary>A path into <c>docs/</c>, never one inside an address or another path: a page, or a directory and what is under it.</summary>
    [GeneratedRegex(@"(?<![\w/.-])docs/[\w.-]+(?:/[\w.-]+)*")]
    private static partial Regex DocsPath();
}
