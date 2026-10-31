using System.Reflection;
using System.Text.RegularExpressions;
using Jason.Runtime.Plugins.Manifest;

namespace Jason.Runtime.Tests.Documentation;

/// <summary>
/// The plugin page's manifest table says which problem code each rule answers with, and a plugin author reads that column
/// as the vocabulary. It is held to <see cref="ProblemCodes"/> both ways: a code in the table that the class does not
/// spell is a failure, and so is a constant the page never names, so a code added or renamed in the class cannot leave the
/// page behind. Some codes belong to no manifest field and the page names them in prose, so they are listed here; each
/// must be named on the page outside the table and outside the plugin host's exit-3 rejection list, which is another
/// vocabulary that happens to share package_unreadable, and none may also sit in the table, which keeps the list exact.
/// The environmental codes are held by <see cref="EnvironmentalCodesDocTests"/>.
/// </summary>
public sealed partial class ManifestProblemCodesDocTests
{
    /// <summary>The codes the page names in prose because no manifest field is the place they are raised at.</summary>
    private static readonly string[] NamedInProse =
    [
        ProblemCodes.ManifestMissing,
        ProblemCodes.YamlInvalid,
        ProblemCodes.UnknownField,
        ProblemCodes.PackageTooLarge,
        ProblemCodes.PackageUnreadable,
        ProblemCodes.PluginsSettingsInvalid,
        ProblemCodes.SettingsFileUnreadable,
        ProblemCodes.GrantUnrequested,
    ];

    [Fact]
    public void Every_code_in_the_manifest_table_is_a_problem_code_the_runtime_spells()
    {
        var spelled = Constants().ToHashSet(StringComparer.Ordinal);
        var unknown = TableCodes().Where(code => !spelled.Contains(code)).Order(StringComparer.Ordinal).ToList();

        Assert.True(unknown.Count == 0, $"The manifest table of docs/plugins.md names codes ProblemCodes does not spell: {string.Join(", ", unknown)}.");
    }

    [Fact]
    public void Every_problem_code_the_runtime_spells_is_in_the_table_or_named_in_the_page_s_prose()
    {
        var page = Source("docs/plugins.md").ReplaceLineEndings("\n");
        var table = TableCodes().ToHashSet(StringComparer.Ordinal);
        var outsideTable = Code().Matches(WithoutHostRejections(page.Replace(TableText(page), string.Empty, StringComparison.Ordinal)))
            .Select(match => match.Groups["code"].Value)
            .ToHashSet(StringComparer.Ordinal);

        var missing = Constants()
            .Where(code => !table.Contains(code) && !ProblemCodes.IsEnvironmental(code) && !NamedInProse.Contains(code))
            .Order(StringComparer.Ordinal)
            .ToList();
        Assert.True(missing.Count == 0, $"ProblemCodes spells codes the manifest table of docs/plugins.md has no row for: {string.Join(", ", missing)}.");

        var misfiled = NamedInProse.Where(table.Contains).Order(StringComparer.Ordinal).ToList();
        Assert.True(misfiled.Count == 0, $"The table has a row for codes this test lists as named in prose; take them off the list: {string.Join(", ", misfiled)}.");

        var unnamed = NamedInProse.Where(code => !outsideTable.Contains(code)).Order(StringComparer.Ordinal).ToList();
        Assert.True(unnamed.Count == 0, $"docs/plugins.md no longer names, in prose or in the table: {string.Join(", ", unnamed)}.");
    }

    [Fact]
    public void Every_problem_code_is_a_public_const_so_that_none_goes_unread()
    {
        var notConst = typeof(ProblemCodes)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field => field.FieldType == typeof(string) && !field.IsLiteral)
            .Select(field => field.Name)
            .Order(StringComparer.Ordinal)
            .ToList();

        Assert.True(notConst.Count == 0, $"ProblemCodes has public string fields that are not const, which the pin never reads: {string.Join(", ", notConst)}.");
    }

    /// <summary>
    /// The page without the paragraph that lists the plugin host's rejection codes, found by the sentence that opens it.
    /// That list is the host's own vocabulary (exit 3), and a manifest code it shares by name is not documented by it.
    /// </summary>
    private static string WithoutHostRejections(string page)
    {
        const string anchor = "Exit 3 carries one of ";
        var start = page.IndexOf(anchor, StringComparison.Ordinal);
        Assert.True(start >= 0, "docs/plugins.md no longer has its 'Exit 3 carries one of … rejection codes' paragraph.");
        var end = page.IndexOf("\n\n", start, StringComparison.Ordinal);
        return page.Remove(start, (end < 0 ? page.Length : end) - start);
    }

    /// <summary>Every public constant of <see cref="ProblemCodes"/>, by reflection, so a code added there is read without an edit here.</summary>
    private static IEnumerable<string> Constants() => typeof(ProblemCodes)
        .GetFields(BindingFlags.Public | BindingFlags.Static)
        .Where(field => field.IsLiteral && field.FieldType == typeof(string))
        .Select(field => (string)field.GetRawConstantValue()!);

    private static IEnumerable<string> TableCodes()
    {
        var rows = TableText(Source("docs/plugins.md").ReplaceLineEndings("\n")).Split('\n').Skip(2).ToList();
        Assert.NotEmpty(rows);

        return rows.SelectMany(row =>
        {
            var last = row.TrimEnd().TrimEnd('|');
            return Code().Matches(last[(last.LastIndexOf(" | ", StringComparison.Ordinal) + 3)..]).Select(match => match.Groups["code"].Value);
        });
    }

    /// <summary>The manifest table, from its header row to the line before the blank one that ends it.</summary>
    private static string TableText(string page)
    {
        const string header = "| Field | Rule | Problem code |";
        var start = page.IndexOf(header, StringComparison.Ordinal);
        Assert.True(start >= 0, "docs/plugins.md no longer has its '| Field | Rule | Problem code |' table.");
        var end = page.IndexOf("\n\n", start, StringComparison.Ordinal);
        return page[start..(end < 0 ? page.Length : end)];
    }

    [GeneratedRegex("`(?<code>[a-z][a-z0-9]*(?:_[a-z0-9]+)+)`")]
    private static partial Regex Code();

    private static string Source(string relative)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "global.json")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return File.ReadAllText(Path.Combine(directory.FullName, relative.Replace('/', Path.DirectorySeparatorChar)));
    }
}
