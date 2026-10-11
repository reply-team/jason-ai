using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Jason.Contracts.Update;

namespace Jason.Runtime.Tests.Documentation;

/// <summary>
/// The runtime's licence and the trademark note are the texts Reply.io accepted, byte for byte, apart from the edit
/// each took when it was placed here: the licence's version line carries the date its edition was approved, and the
/// note no longer says it is a draft that is not in force. Each test puts that one edit back and compares the result
/// with the accepted text's SHA-256, so any other change to either file fails here.
/// </summary>
public sealed partial class RuntimeLicenceTests
{
    /// <summary>The SHA-256 of the accepted trademark note, its draft line included.</summary>
    private const string AcceptedNote = "efe8fafad9aa5deac5725ab5d89e4cd204c67001519ef1e1c35860b3b42c821f";

    /// <summary>The SHA-256 of the accepted licence, dated the day its text was first drafted.</summary>
    private const string AcceptedLicence = "05168d3aa028dc92ad3030cea2e7eb995a4ea3efa35afecebb3b503e3ccd8dfa";

    [Fact]
    public void The_runtime_licence_is_the_accepted_text_dated_the_day_its_edition_was_approved()
    {
        var bytes = File.ReadAllBytes(Path.Combine(Publication.RepositoryRoot(), "LICENSE-RUNTIME.md"));
        var text = Encoding.UTF8.GetString(bytes);

        Assert.DoesNotContain('\r', text);
        Assert.Equal((byte)'\n', bytes[^1]);

        var lines = text.Split('\n');
        Assert.Equal("Version 1.0 — 10 October 2026", lines[2]);
        lines[2] = "Version 1.0 — 8 October 2026";
        Assert.Equal(AcceptedLicence, Sha256(string.Join('\n', lines)));
    }

    [Fact]
    public void The_trademark_note_is_the_accepted_text_without_its_draft_line()
    {
        var bytes = File.ReadAllBytes(Path.Combine(Publication.RepositoryRoot(), "TRADEMARKS.md"));
        var text = Encoding.UTF8.GetString(bytes);

        Assert.DoesNotContain("DRAFT", text, StringComparison.Ordinal);
        Assert.DoesNotContain('\r', text);
        Assert.Equal((byte)'\n', bytes[^1]);

        var lines = text.Split('\n').ToList();
        lines.InsertRange(2, ["DRAFT FOR LEGAL REVIEW (8 October 2026). Not in force.", string.Empty]);
        Assert.Equal(AcceptedNote, Sha256(string.Join('\n', lines)));
    }

    /// <summary>
    /// The open components and the runtime belong to one holder, so the MIT licence and the runtime's licence name the
    /// same one. Both names are read from the files, without the full stop that may end them.
    /// </summary>
    [Fact]
    public void The_open_components_and_the_runtime_name_one_holder()
    {
        var root = Publication.RepositoryRoot();
        var mit = File.ReadAllText(Path.Combine(root, "LICENSE"));
        mit = mit[Assert.Single(Regex.Matches(mit, "^MIT License$", RegexOptions.Multiline)).Index..];
        var runtime = File.ReadAllText(Path.Combine(root, "LICENSE-RUNTIME.md"));

        var open = Assert.Single(MitHolder().Matches(mit)).Groups["holder"].Value;
        var closed = Assert.Single(RuntimeHolder().Matches(runtime)).Groups["holder"].Value;

        Assert.Equal(closed, open);
    }

    [GeneratedRegex(@"^Copyright \(c\) [0-9]{4} (?<holder>.+?)\.?$", RegexOptions.Multiline)]
    private static partial Regex MitHolder();

    [GeneratedRegex(@"^Copyright \(c\) [0-9]{4} (?<holder>.+?)\.? All rights reserved\.$", RegexOptions.Multiline)]
    private static partial Regex RuntimeHolder();

    /// <summary>
    /// The licence says the update check and the downloads go to an address Reply.io operates, which redirects them to
    /// GitHub. That is true only of a tree whose feed and install scripts name that address, so the text never ships in
    /// a tree that still goes to GitHub directly.
    /// </summary>
    [Fact]
    public void The_licence_ships_only_in_a_tree_that_fetches_releases_through_the_address_it_describes()
    {
        var root = Publication.RepositoryRoot();
        Assert.Contains(
            "go to an address operated by Reply.io, which redirects them to GitHub",
            File.ReadAllText(Path.Combine(root, "LICENSE-RUNTIME.md")),
            StringComparison.Ordinal);

        var sh = Assert.Single(ShellBase().Matches(File.ReadAllText(Path.Combine(root, "install", "install.sh")))).Groups["url"].Value;
        var ps1 = Assert.Single(PowerShellBase().Matches(File.ReadAllText(Path.Combine(root, "install", "install.ps1")))).Groups["url"].Value;

        Assert.NotEqual("github.com", UpdateFeed.Default.Host);
        Assert.NotEqual("github.com", new Uri(sh).Host);
        Assert.NotEqual("github.com", new Uri(ps1).Host);
    }

    [GeneratedRegex(@"^RELEASES_URL=""(?<url>[^""]+)""\r?$", RegexOptions.Multiline)]
    private static partial Regex ShellBase();

    [GeneratedRegex(@"^\s*\$releasesUrl = '(?<url>[^']+)'\r?$", RegexOptions.Multiline)]
    private static partial Regex PowerShellBase();

    private static string Sha256(string text) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
}
