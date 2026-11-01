using System.Text.RegularExpressions;

namespace Jason.Runtime.Tests.Documentation;

/// <summary>
/// Where a person writes for help and where a vulnerability is reported is one address, said in three places — the
/// README's "Getting help", <c>SECURITY.md</c> and <c>CONTRIBUTING.md</c> — so changing it is one line in each, and none
/// of them can drift alone.
/// </summary>
public sealed partial class HelpAddressTests
{
    [Fact]
    public void The_help_address_is_one_address_in_every_place_that_gives_it()
    {
        var root = RepositoryTree.Root();
        var readme = File.ReadAllText(Path.Combine(root, "README.md")).ReplaceLineEndings("\n");
        var start = readme.IndexOf("\n## Getting help\n", StringComparison.Ordinal);
        Assert.True(start >= 0, "README.md has no \"Getting help\" section.");
        var end = readme.IndexOf("\n## ", start + 1, StringComparison.Ordinal);
        var places = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["README.md, Getting help"] = readme[start..(end < 0 ? readme.Length : end)],
            ["SECURITY.md"] = File.ReadAllText(Path.Combine(root, "SECURITY.md")),
            ["CONTRIBUTING.md"] = File.ReadAllText(Path.Combine(root, "CONTRIBUTING.md")),
        };

        var addresses = places.ToDictionary(
            place => place.Key,
            place => string.Join(", ", Address().Matches(place.Value).Select(match => match.Value)),
            StringComparer.Ordinal);

        Assert.True(
            addresses.Values.All(found => found.Length > 0 && !found.Contains(',', StringComparison.Ordinal))
                && addresses.Values.Distinct(StringComparer.Ordinal).Count() == 1,
            "Each place gives one address, the same one:\n" + string.Join('\n', addresses.Select(place => $"  {place.Key}: {place.Value}")));
    }

    [GeneratedRegex(@"[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,}")]
    private static partial Regex Address();
}
