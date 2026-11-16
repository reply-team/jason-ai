using System.Text.RegularExpressions;
using Jason.Runtime.Tests.Documentation;
using Jason.Runtime.Ui;

namespace Jason.Runtime.Tests.Ui;

/// <summary>
/// The runtime's sealed set and the desktop's own seal around a view of untrusted content are one allow-list written
/// twice, in C# and in TypeScript: held equal here, directive by directive and feature by feature, read from the desktop's
/// source as it stands. They differ in three places, each stated: a browser's page frames the file, so
/// <c>frame-ancestors</c> is the runtime's own origin rather than none; a browser has no origin of its own to give the
/// file, so its <c>sandbox</c> never keeps the same origin, where the desktop's keeps a scheme shared with nothing; and
/// the server sends no opener policy (below).
/// </summary>
public class SealedAllowListTests
{
    /// <summary>The two directives that differ, and what the runtime's says.</summary>
    private static readonly Dictionary<string, string> Differences = new(StringComparer.Ordinal)
    {
        ["frame-ancestors"] = "frame-ancestors 'self'",
        ["sandbox"] = "sandbox allow-scripts allow-forms",
    };

    private static string Source() => File.ReadAllText(Path.Combine(RepositoryTree.Root(), "desktop", "src", "demo-page.ts"));

    /// <summary>The string literals of one exported array of the desktop's source, in their order.</summary>
    private static List<string> Literals(string source, string name)
    {
        var array = Regex.Match(source, @"export const " + name + @"\s*=\s*\[(?<body>.*?)\]", RegexOptions.Singleline);
        Assert.True(array.Success, $"desktop/src/demo-page.ts exports no array {name}");
        return [.. Regex.Matches(array.Groups["body"].Value, "\"(?<double>[^\"]*)\"|'(?<single>[^']*)'")
            .Select(match => match.Groups["double"].Success ? match.Groups["double"].Value : match.Groups["single"].Value)];
    }

    private static string Directive(string text) => text.Split(' ', 2)[0];

    [Fact]
    public void The_policy_is_the_desktops_but_for_the_two_directives_that_differ()
    {
        var desktop = Literals(Source(), "demoPolicy");
        Assert.True(desktop.Count >= 10, $"read {desktop.Count} directives of the desktop's policy, fewer than it has");
        Assert.True(Differences.Keys.All(name => desktop.Any(directive => Directive(directive) == name)), "the desktop's policy lost a directive the two differ in");

        var expected = desktop.Select(directive => Differences.TryGetValue(Directive(directive), out var ours) ? ours : directive).ToList();
        var runtime = UiFiles.SealedContentSecurityPolicy.Split("; ").ToList();

        Assert.Equal(expected, runtime);
        Assert.DoesNotContain("allow-same-origin", UiFiles.SealedContentSecurityPolicy, StringComparison.Ordinal);
    }

    [Fact]
    public void The_features_denied_are_the_desktops()
    {
        var source = Source();
        var desktop = Literals(source, "deniedFeatures");
        Assert.True(desktop.Count >= 20, $"read {desktop.Count} features of the desktop's list, fewer than it has");

        Assert.Equal(desktop, UiFiles.SealedDeniedFeatures);
        Assert.Contains("'Permissions-Policy': deniedFeatures.map((feature) => `${feature}=()`).join(', ')", source, StringComparison.Ordinal);
        Assert.Equal(string.Join(", ", desktop.Select(feature => feature + "=()")), UiFiles.HeadersFor("sealed/page.html").Single(header => header.Key == "Permissions-Policy").Value);
    }

    /// <summary>
    /// Every other header is the desktop's, with the same value, and the desktop sends one more, the third difference:
    /// <c>Cross-Origin-Opener-Policy</c>, which acts on a window opened by itself and on nothing framed, and whose meeting
    /// with a sandbox set by the policy is not established, so the server's set leaves it out. The type of the content is
    /// the file's, not the set's.
    /// </summary>
    [Fact]
    public void Every_other_sealed_header_is_the_desktops_own_but_the_opener_policy()
    {
        var source = Source();
        var function = Regex.Match(source, @"export function demoHeaders\(\)[^{]*\{(?<body>.*?)\n\}", RegexOptions.Singleline);
        Assert.True(function.Success, "desktop/src/demo-page.ts has no demoHeaders");
        var desktop = Regex.Matches(function.Groups["body"].Value, @"'(?<name>[A-Za-z-]+)':\s*'(?<value>[^']*)'")
            .ToDictionary(match => match.Groups["name"].Value, match => match.Groups["value"].Value, StringComparer.Ordinal);
        Assert.True(desktop.Count >= 5, $"read {desktop.Count} headers of the desktop's seal, fewer than it has");
        Assert.Contains("'Content-Security-Policy': demoPolicy,", function.Groups["body"].Value, StringComparison.Ordinal);
        Assert.Equal("same-origin", desktop["Cross-Origin-Opener-Policy"]);

        var theirs = desktop.Where(header => header.Key is not ("Content-Type" or "Cross-Origin-Opener-Policy")).OrderBy(header => header.Key, StringComparer.Ordinal);
        var ours = UiFiles.HeadersFor("sealed/page.html").Where(header => header.Key is not ("Content-Security-Policy" or "Permissions-Policy")).OrderBy(header => header.Key, StringComparer.Ordinal);
        Assert.Equal(theirs, ours);
        Assert.DoesNotContain(UiFiles.HeadersFor("sealed/page.html"), header => header.Key == "Cross-Origin-Opener-Policy");
    }
}
