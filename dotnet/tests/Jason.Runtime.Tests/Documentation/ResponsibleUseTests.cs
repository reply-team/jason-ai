using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Jason.Cli.Skills;
using Jason.Contracts.Update;
using Jason.Runtime.Configuration;

namespace Jason.Runtime.Tests.Documentation;

/// <summary>
/// <c>docs/responsible-use.md</c> carries what the runtime's earlier licence said about data and about the person's
/// responsibilities, in that licence's words with only its framing removed: no section numbers, no capitalised defined
/// terms, and the company named as ReplyApp Inc. It is a page about the product, not a licence, so each claim the code
/// can hold is read here against the code: the update check's waits and its switch from the options, where the
/// requests go from the product's address, and where the skills come from.
/// </summary>
public sealed partial class ResponsibleUseTests
{
    /// <summary>"Five minutes" and "once a day" are the options' defaults, and both waits are settings.</summary>
    [Fact]
    public void The_page_s_waits_are_the_options_defaults()
    {
        var defaults = new UpdateOptions();

        Assert.Equal(5, defaults.InitialDelayMinutes);
        Assert.Equal(24, defaults.IntervalHours);
        Assert.Contains(
            "the running runtime asks whether a newer version exists five minutes after it starts and then once a day; both intervals can be configured, and a check you or your agent ask for (`jason update check`) is the same request.",
            Page(),
            StringComparison.Ordinal);
    }

    /// <summary>The switch is named as the settings file spells it, and the check is on unless it is set.</summary>
    [Fact]
    public void The_page_names_the_switch_that_turns_the_check_off()
    {
        Assert.True(new UpdateOptions().CheckEnabled);
        Assert.Contains(
            $"Unless you switch it off (by setting `{UpdateOptions.Section}:{nameof(UpdateOptions.CheckEnabled)}` to `false`)",
            Page(),
            StringComparison.Ordinal);
    }

    /// <summary>
    /// The request is the one the feed reader makes, which <c>UpdateFeedTests</c> pins to a <c>User-Agent</c> of
    /// <c>jason/&lt;version&gt;</c> and nothing else; the page says it identifies only the version. A download names the
    /// file, and the source a link names where the link a person followed carries one, which the product itself never
    /// adds.
    /// </summary>
    [Fact]
    public void The_page_says_what_the_check_carries_and_that_nothing_else_is_sent()
    {
        var page = Page();

        Assert.Contains("The update check identifies only the runtime's version, including its build, and a download only the file requested, and the `ref` of the link you followed where it carries one;", page, StringComparison.Ordinal);
        Assert.Contains("apart from the requests described below the runtime sends no telemetry, usage data or crash reports.", page, StringComparison.Ordinal);
    }

    /// <summary>
    /// The page says the update check and the downloads go to an address ReplyApp Inc. operates, which redirects them to
    /// GitHub. That is true only of a tree whose feed and install scripts name that address, so the page never ships in a
    /// tree that still goes to GitHub directly.
    /// </summary>
    [Fact]
    public void The_page_ships_only_in_a_tree_that_fetches_releases_through_the_address_it_describes()
    {
        var root = RepositoryTree.Root();
        Assert.Contains("go to an address operated by ReplyApp Inc., which redirects them to GitHub.", Page(), StringComparison.Ordinal);

        var sh = Assert.Single(ShellBase().Matches(File.ReadAllText(Path.Combine(root, "install", "install.sh")))).Groups["url"].Value;
        var ps1 = Assert.Single(PowerShellBase().Matches(File.ReadAllText(Path.Combine(root, "install", "install.ps1")))).Groups["url"].Value;

        var address = new Uri(ProductAddress.Root).Host;
        Assert.NotEqual("github.com", address);
        Assert.Equal(address, UpdateFeed.Default.Host);
        Assert.Equal(address, new Uri(sh).Host);
        Assert.Equal(address, new Uri(ps1).Host);
    }

    /// <summary>The skills are a clone of the public repository, which no redirect serves, and the page says so.</summary>
    [Fact]
    public void The_page_says_the_skills_come_from_github_directly()
    {
        Assert.Equal("github.com", new Uri(SkillsSource.DefaultSource).Host);
        Assert.Contains("when you install skills, which are fetched from GitHub directly, GitHub's terms and privacy statement apply.", Page(), StringComparison.Ordinal);
    }

    /// <summary>
    /// What the address keeps, for how long and under which policy is ReplyApp Inc.'s statement, carried as the
    /// licence made it to the people who installed Jason under it, and then what its counts are made of: every dimension
    /// they are kept by, after the sentence whose "them" is the records.
    /// </summary>
    [Fact]
    public void The_page_keeps_the_promise_about_the_address_s_records()
    {
        Assert.Contains(
            "ReplyApp Inc. uses these records only to operate and secure the service and to count downloads in aggregate, keeps them for no longer than 31 days, and does not combine them with any other data. ReplyApp Inc.'s privacy policy (<https://reply.io/privacy-policy/>) applies to them. That address counts the downloads and install-page reads it answers by day, file, the `ref` the followed link names and the kind of program that asked (a browser, a command-line or agent fetcher, or a crawler), and keeps nothing of the person in those counts.",
            Page(),
            StringComparison.Ordinal);
    }

    /// <summary>
    /// The statements are the product's, in the licence's words without its framing: no section numbers, no defined
    /// terms, the company by its name, and nothing anyone accepts.
    /// </summary>
    [Fact]
    public void The_page_states_responsibilities_without_the_licence_s_framing()
    {
        var page = Page();

        Assert.Contains("these are statements about the product, not terms of that licence, and nothing here asks you to accept anything", page, StringComparison.Ordinal);
        Assert.Contains("For personal data that you process with the runtime, you are the controller.", page, StringComparison.Ordinal);
        Assert.Contains("are aids, not guarantees, and do not shift your responsibilities to ReplyApp Inc.", page, StringComparison.Ordinal);
        Assert.Contains("Nothing the runtime, its skills or its documentation say is legal advice.", page, StringComparison.Ordinal);
        Assert.DoesNotContain("Reply.io", page, StringComparison.Ordinal);
        Assert.DoesNotContain("section 7", page, StringComparison.Ordinal);
        Assert.DoesNotContain("this licence", page, StringComparison.Ordinal);
        Assert.DoesNotMatch(DefinedTerm(), page);
        Assert.DoesNotContain("you accept", page, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The SHA-256 of the page's <c>## Data</c> and <c>## Your responsibilities</c> sections, from the first heading to
    /// the end of the second section, exactly as they stand. They were read against sections 7 and 8 of the accepted
    /// Jason Runtime Licence and differ from them only in framing, so a change of one word in either is a change to text
    /// legal read. Have the new wording read again first, then put its digest here.
    /// </summary>
    private const string ReadSections = "c69a471ef4cebea676655e8d1e73de4ffed66430df98580d645d217a680dbbf5";

    /// <summary>The two sections that carry the earlier licence's words are held whole, not by the sentences quoted above.</summary>
    [Fact]
    public void The_data_and_responsibilities_sections_are_the_text_that_was_read()
    {
        var text = File.ReadAllText(Path.Combine(RepositoryTree.Root(), "docs", "responsible-use.md"));
        Assert.DoesNotContain('\r', text);

        var start = text.IndexOf("\n## Data\n", StringComparison.Ordinal) + 1;
        Assert.True(start > 0, "The page has no '## Data' section.");
        var second = text.IndexOf("\n## Your responsibilities\n", start, StringComparison.Ordinal);
        Assert.True(second > start, "The page has no '## Your responsibilities' section after '## Data'.");
        var next = text.IndexOf("\n## ", second + 1, StringComparison.Ordinal);
        var sections = text[start..(next < 0 ? text.Length : next + 1)];

        Assert.Equal(ReadSections, Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(sections))));
    }

    /// <summary>
    /// The page names the address only as one ReplyApp Inc. operates, never by its host, as the earlier licence did, so
    /// the text that was read does not change when the address does.
    /// </summary>
    [Fact]
    public void The_page_never_names_the_product_address()
    {
        var host = new Uri(ProductAddress.Root).Host;

        Assert.DoesNotContain(host, Page(), StringComparison.OrdinalIgnoreCase);
        // The registrable domain as well, the host's last two labels, so a page naming the address under another of its
        // hosts is caught; the first label alone may be an ordinary word.
        Assert.DoesNotContain(string.Join('.', host.Split('.')[^2..]), Page(), StringComparison.OrdinalIgnoreCase);
    }

    private static string Page() =>
        Whitespace().Replace(File.ReadAllText(Path.Combine(RepositoryTree.Root(), "docs", "responsible-use.md")), " ");

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    /// <summary>The licence's capitalised defined terms.</summary>
    [GeneratedRegex(@"\b(Runtime|Your Machines|Your Content|Open Components|Official Source)\b")]
    private static partial Regex DefinedTerm();

    [GeneratedRegex(@"^RELEASES_URL=""(?<url>[^""]+)""\r?$", RegexOptions.Multiline)]
    private static partial Regex ShellBase();

    [GeneratedRegex(@"^\s*\$releasesUrl = '(?<url>[^']+)'\r?$", RegexOptions.Multiline)]
    private static partial Regex PowerShellBase();
}
