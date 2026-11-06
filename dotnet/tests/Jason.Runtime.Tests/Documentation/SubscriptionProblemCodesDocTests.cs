using System.Reflection;
using System.Text.RegularExpressions;
using Jason.Runtime.Plugins.Manifest;
using Jason.Runtime.Plugins.Subscriptions;

namespace Jason.Runtime.Tests.Documentation;

/// <summary>
/// The plugin page's section on subscriptions is where an operator who wrote an order, and a plugin author who declared
/// a kind, read what a refusal means. It is held to the codes both ways: every code of <see cref="SubscriptionProblemCodes"/>
/// and the two manifest codes a declared kind is refused with are named there, and every subscription code the section
/// names is one the runtime spells, so a code added or renamed in a class cannot leave the page behind.
/// </summary>
public sealed partial class SubscriptionProblemCodesDocTests
{
    private const string Heading = "Subscriptions: declaration, grant, order";

    /// <summary>The manifest codes that refuse a declared kind; the manifest table carries them too.</summary>
    private static readonly string[] ManifestCodes = [ProblemCodes.SubscriptionKindUnknown, ProblemCodes.SubscriptionKindNotSubscribable];

    [Fact]
    public void The_section_names_every_code_a_subscription_is_refused_with()
    {
        var section = Section();

        var unnamed = Constants().Concat(ManifestCodes)
            .Where(code => !section.Contains($"`{code}`", StringComparison.Ordinal))
            .Order(StringComparer.Ordinal)
            .ToList();
        Assert.True(unnamed.Count == 0, $"docs/plugins.md's section '{Heading}' does not name: {string.Join(", ", unnamed)}.");
    }

    [Fact]
    public void Every_subscription_code_the_section_names_is_one_the_runtime_spells()
    {
        var spelled = Constants().Concat(ManifestCodes).ToHashSet(StringComparer.Ordinal);

        var unknown = Code().Matches(Section())
            .Select(match => match.Groups["code"].Value)
            .Where(code => !spelled.Contains(code))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToList();
        Assert.True(unknown.Count == 0, $"docs/plugins.md's section '{Heading}' names codes the runtime does not spell: {string.Join(", ", unknown)}.");
    }

    /// <summary>Every public constant of <see cref="SubscriptionProblemCodes"/>, by reflection.</summary>
    private static IEnumerable<string> Constants() => typeof(SubscriptionProblemCodes)
        .GetFields(BindingFlags.Public | BindingFlags.Static)
        .Where(field => field.IsLiteral && field.FieldType == typeof(string))
        .Select(field => (string)field.GetRawConstantValue()!);

    /// <summary>A rest is read on two pages, and each says a restart forgets it and that it is not a handler's retry.</summary>
    [Theory]
    [InlineData("docs/plugins.md", "The wait is kept in memory, so `rest_until` is null after a restart, which offers the kind at once, and it is not `in_flight.retry_at`, which is a handler's own retry within a chain.")]
    [InlineData("docs/runtime-api.md", "That is not `in_flight.retry_at`, a handler's own retry within a chain, and a restart forgets a rest, so `rest_until` is null after one.")]
    public void The_pages_keep_a_kind_s_rest_apart_from_a_handler_s_retry(string page, string sentence) =>
        Assert.Contains(sentence, Whitespace().Replace(Source(page), " "), StringComparison.Ordinal);

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    /// <summary>The section, from its heading to the next heading of the same level.</summary>
    private static string Section()
    {
        var page = Source("docs/plugins.md").ReplaceLineEndings("\n");
        var heading = Headings().Matches(page).FirstOrDefault(match => match.Groups["title"].Value == Heading);
        Assert.True(heading is not null, $"docs/plugins.md no longer has a section headed '{Heading}'.");
        var end = page.IndexOf("\n## ", heading.Index + heading.Length, StringComparison.Ordinal);
        return page[heading.Index..(end < 0 ? page.Length : end)];
    }

    [GeneratedRegex(@"^## (?:\d+\. )?(?<title>.+)$", RegexOptions.Multiline)]
    private static partial Regex Headings();

    [GeneratedRegex("`(?<code>subscriptions?_[a-z0-9]+(?:_[a-z0-9]+)*)`")]
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
