using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Text.RegularExpressions;

namespace Jason.Runtime.Tests.Documentation;

/// <summary>
/// What the programs say names only the pages a release publishes. A sentence the CLI prints, or the runtime puts into an
/// answer, is read by somebody with the published tree in front of them, and a page outside it is a dead end there.
/// </summary>
/// <remarks>
/// The strings are read from each production assembly's user-string heap, as the guards that read production IL read
/// it from the file alone: every literal piece of every string the code carries — plain, verbatim, raw, interpolated, or
/// constants joined at compile time — is there, and no comment is. So no reader of C# of this repository's own stands
/// between the rule and what the programs actually carry.
/// </remarks>
public sealed partial class CompiledPageNamesTests
{
    [Fact]
    [Critical(Family.B4, "no compiled pointer outside the open set")]
    public void No_string_the_programs_carry_names_a_page_a_release_does_not_publish()
    {
        var root = Publication.RepositoryRoot();
        var openSet = OpenSet.Lines(root);
        var found = new List<string>();
        foreach (var assembly in CompiledCalls.ProductionAssemblies(root))
        {
            foreach (var text in UserStrings(assembly))
            {
                found.AddRange(Unpublished(text, openSet).Select(page => $"{Path.GetFileName(assembly)}: {page}, in \"{text}\""));
            }
        }

        Assert.True(found.Count == 0, $"The programs name {found.Count} page(s) a release does not publish:\n" + string.Join('\n', found));
    }

    /// <summary>The rule itself: a page of <c>docs/</c>, or a directory of it, that the open set does not publish.</summary>
    [Theory]
    [InlineData("then `jason runtime start`. docs/release-and-update.md §8 says why.", "docs/release-and-update.md")]
    [InlineData("As docs/work-execution.md.", "docs/work-execution.md")]
    [InlineData("See docs/architecture.md, and docs/notes/ for the rest.", "docs/architecture.md", "docs/notes/")]
    public void A_page_or_a_directory_of_docs_outside_the_open_set_is_named(string text, params string[] expected)
    {
        Assert.Equal(expected, Unpublished(text, ["docs/INSTALL.md", "docs/contracts/"]));
    }

    /// <summary>And a published page, a path under a published directory, and the same words inside an address pass.</summary>
    [Theory]
    [InlineData("INSTALL §6 is docs/INSTALL.md, which says so.")]
    [InlineData("one of those under docs/contracts/operations, not a Jason verb.")]
    [InlineData("https://example.com/docs/private.md is somebody else's page.")]
    public void A_published_page_or_a_path_under_a_published_directory_passes(string text)
    {
        Assert.Empty(Unpublished(text, ["docs/INSTALL.md", "docs/contracts/"]));
    }

    /// <summary>
    /// Each page or directory of <c>docs/</c> a text names that the open set publishes neither by its own line nor under a
    /// directory line: a page as its path, a directory as its path with a <c>/</c> after it.
    /// </summary>
    private static List<string> Unpublished(string text, IReadOnlyList<string> openSet)
    {
        var named = new List<string>();
        foreach (Match match in DocsPath().Matches(text))
        {
            // A sentence's full stop after the path is not part of it.
            var value = match.Value.TrimEnd('.');
            var path = value.EndsWith(".md", StringComparison.Ordinal) ? value : $"docs/{value["docs/".Length..].Split('/')[0]}/";
            var published = openSet.Any(entry => entry == path || (entry.EndsWith('/') && path.StartsWith(entry, StringComparison.Ordinal)));
            if (!published)
            {
                named.Add(path);
            }
        }

        return named;
    }

    /// <summary>The strings an assembly carries, read from its user-string heap without loading it.</summary>
    private static List<string> UserStrings(string assemblyFile)
    {
        using var image = new PEReader(File.OpenRead(assemblyFile));
        var metadata = image.GetMetadataReader();
        var strings = new List<string>();
        if (metadata.GetHeapSize(HeapIndex.UserString) <= 1)
        {
            return strings;
        }

        for (var handle = MetadataTokens.UserStringHandle(1); !handle.IsNil; handle = metadata.GetNextHandle(handle))
        {
            strings.Add(metadata.GetUserString(handle));
        }

        return strings;
    }

    /// <summary>A path into <c>docs/</c>, never one inside an address or another path: a page, or a directory and what is under it.</summary>
    [GeneratedRegex(@"(?<![\w/.-])docs/[\w.-]+(?:/[\w.-]+)*")]
    private static partial Regex DocsPath();
}
