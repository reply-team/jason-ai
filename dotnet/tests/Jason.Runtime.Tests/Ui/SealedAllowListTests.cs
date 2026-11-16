using System.Text.RegularExpressions;
using Jason.Runtime.Tests.Documentation;
using Jason.Runtime.Ui;

namespace Jason.Runtime.Tests.Ui;

/// <summary>
/// The runtime's sealed set and the desktop's own seal around a view of untrusted content are one allow-list written
/// twice, in C# and in TypeScript: held equal here, directive by directive and feature by feature, read from the desktop's
/// source as it stands. They differ in two places, each stated: a browser's page frames the file, so
/// <c>frame-ancestors</c> is the runtime's own origin rather than none; and the server sends no opener policy (below).
/// The <c>sandbox</c> is one form in both, the page's own frame: scripts alone, never the same origin or forms.
/// </summary>
public class SealedAllowListTests
{
    /// <summary>The one directive that differs, and what the runtime's says.</summary>
    private static readonly Dictionary<string, string> Differences = new(StringComparer.Ordinal)
    {
        ["frame-ancestors"] = "frame-ancestors 'self'",
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
    public void The_policy_is_the_desktops_but_for_the_directive_that_differs()
    {
        var desktop = Literals(Source(), "demoPolicy");
        Assert.True(desktop.Count >= 10, $"read {desktop.Count} directives of the desktop's policy, fewer than it has");
        Assert.True(Differences.Keys.All(name => desktop.Any(directive => Directive(directive) == name)), "the desktop's policy lost a directive the two differ in");

        var expected = desktop.Select(directive => Differences.TryGetValue(Directive(directive), out var ours) ? ours : directive).ToList();
        var runtime = UiFiles.SealedContentSecurityPolicy.Split("; ").ToList();

        Assert.Equal(expected, runtime);
        Assert.DoesNotContain("allow-same-origin", UiFiles.SealedContentSecurityPolicy, StringComparison.Ordinal);
        Assert.DoesNotContain("allow-forms", UiFiles.SealedContentSecurityPolicy, StringComparison.Ordinal);
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
    /// Every other header is the desktop's, with the same value, and the desktop sends one more, the second difference:
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
        var desktop = Pairs(function.Groups["body"].Value);
        Assert.True(desktop.Count >= 5, $"read {desktop.Count} headers of the desktop's seal, fewer than it has");
        Assert.Empty(Unread(function.Groups["body"].Value));
        Assert.Contains("'Content-Security-Policy': demoPolicy,", function.Groups["body"].Value, StringComparison.Ordinal);
        Assert.Equal("same-origin", desktop["Cross-Origin-Opener-Policy"]);

        var theirs = desktop.Where(header => header.Key is not ("Content-Type" or "Cross-Origin-Opener-Policy")).OrderBy(header => header.Key, StringComparer.Ordinal);
        var ours = UiFiles.HeadersFor("sealed/page.html").Where(header => header.Key is not ("Content-Security-Policy" or "Permissions-Policy")).OrderBy(header => header.Key, StringComparer.Ordinal);
        Assert.Equal(theirs, ours);
        Assert.DoesNotContain(UiFiles.HeadersFor("sealed/page.html"), header => header.Key == "Cross-Origin-Opener-Policy");
    }

    /// <summary>
    /// The guard reads every key of the desktop's headers, so a header written another way, in double quotes or computed,
    /// is a header it did not read, and red, never a header that stays out of the comparison.
    /// </summary>
    [Fact]
    public void A_desktop_header_written_another_way_is_one_the_guard_did_not_read()
    {
        const string standIn = """
                return {
                    'Content-Type': 'text/html; charset=utf-8',
                    'Content-Security-Policy': demoPolicy,
                    'Permissions-Policy': deniedFeatures.map((feature) => `${feature}=()`).join(', '),
                    'Referrer-Policy': 'no-referrer',
                    "X-Frame-Options": "DENY",
                    'Cross-Origin-Embedder-Policy': embedder(),
                    Origin_Agent_Cluster: '?1',
                };
            """;

        Assert.Equal(["Content-Type", "Referrer-Policy"], Pairs(standIn).Keys.Order(StringComparer.Ordinal));
        Assert.Equal(["Cross-Origin-Embedder-Policy", "Origin_Agent_Cluster", "X-Frame-Options"], Unread(standIn).Order(StringComparer.Ordinal));
    }

    /// <summary>The headers a body names as a single-quoted name and a single-quoted value.</summary>
    private static Dictionary<string, string> Pairs(string body) =>
        Regex.Matches(body, @"'(?<name>[A-Za-z-]+)':\s*'(?<value>[^']*)'")
            .ToDictionary(match => match.Groups["name"].Value, match => match.Groups["value"].Value, StringComparer.Ordinal);

    /// <summary>
    /// Every key the body names, however written, that is neither a pair <see cref="Pairs"/> read nor one of the two
    /// headers the guard compares by their own lists.
    /// </summary>
    private static List<string> Unread(string body)
    {
        var read = Pairs(body);
        return [.. Regex.Matches(body, @"(?m)^\s*(?:'(?<name>[^']+)'|""(?<name>[^""]+)""|(?<name>[A-Za-z_$][\w$-]*))\s*:")
            .Select(match => match.Groups["name"].Value)
            .Where(name => !read.ContainsKey(name) && name is not ("Content-Security-Policy" or "Permissions-Policy"))];
    }
}
