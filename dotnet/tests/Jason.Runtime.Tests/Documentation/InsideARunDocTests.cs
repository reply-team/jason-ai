using System.Globalization;
using System.Text.RegularExpressions;
using Jason.Contracts.Execution;
using Jason.Runtime.Api;
using Jason.Runtime.Domain;

namespace Jason.Runtime.Tests.Documentation;

/// <summary>
/// A request from inside a run, as the pages a reader meets it on say it, held to the runtime: the header the CLI sends,
/// the operations that refuse it, the status and code they refuse it with, and that the rule is a higher bar rather
/// than a boundary. A program of one's own reads the first page, a role's author the second, a person answering a
/// question the third.
/// </summary>
public sealed partial class InsideARunDocTests
{
    private static readonly ForbiddenException Refusal = DomainErrors.DecidedInsideARun("att_x");

    private static string Status => Refusal.StatusCode.ToString(CultureInfo.InvariantCulture);

    /// <summary>The token's section names the header, says what it is not, and gives the refusal by status and code.</summary>
    [Fact]
    public void The_runtime_api_page_names_the_header_beside_the_token_and_the_refusal()
    {
        var page = Page("runtime-api.md");
        var section = Section(page, "## How a program authorises");

        Assert.Contains($"{ExecutionEnvironment.AttemptHeader}: att_...", section, StringComparison.Ordinal);
        var flat = Flat(section);
        Assert.Contains($"in `{ExecutionEnvironment.AttemptIdVariable}` — says so on every request it sends, reads included", flat, StringComparison.Ordinal);
        Assert.Contains("it is not a credential, it opens nothing, and it fences nothing", flat, StringComparison.Ordinal);
        Assert.Contains($"answer it `{Status}` with the code `{Refusal.Code}` before the body is read, and record nothing", flat, StringComparison.Ordinal);
        Assert.Contains(
            "A value that is not all visible ASCII is not sent: `jason` says so in one line on stderr, naming the variable and never its value.",
            flat,
            StringComparison.Ordinal);
    }

    /// <summary>The bullet that lists a person's decisions lists exactly the ones that refuse, the rule, and what it is worth.</summary>
    [Fact]
    public void The_runtime_api_page_says_a_persons_decision_is_refused_from_inside_a_run_as_a_higher_bar()
    {
        var bullet = Flat(Regex.Match(Page("runtime-api.md"), @"(?ms)^- \*\*A decision in a person's name\*\*.*?(?=^- )").Value);

        var named = Regex.Matches(bullet, @"`(?<operation>[a-z]+\.[a-z_]+)`").Select(match => match.Groups["operation"].Value);
        Assert.Equal(InsideARun.PersonsWord.Order(StringComparer.Ordinal), named.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal));
        Assert.Contains(
            $"A request to any of them that carries `{ExecutionEnvironment.AttemptHeader}`, as `jason` sends from inside a run, is answered `{Status}` `{Refusal.Code}` whatever person it names, and changes nothing: a person's word is given outside any run.",
            bullet,
            StringComparison.Ordinal);
        Assert.Contains("That is a higher bar, not a boundary: a run that dropped the header and named a person would not be told apart.", bullet, StringComparison.Ordinal);
    }

    /// <summary>The launched child's variables have a second purpose, and the approvals section says who the rule keeps out.</summary>
    [Fact]
    public void The_work_execution_page_gives_the_attempt_variable_its_second_purpose_and_the_approvals_their_rule()
    {
        var page = Flat(Page("work-execution.md"));

        Assert.Contains(
            $"and the CLI names the attempt to the runtime on every request in the `{ExecutionEnvironment.AttemptHeader}` header, whatever actor it claims, so the verbs that are a person's own word refuse it (`{Refusal.Code}`);",
            page,
            StringComparison.Ordinal);
        Assert.Contains(
            $"An approval, an answer, a preference or a campaign's send mode sent from inside a run is refused with `{Refusal.Code}` whoever it names, since the CLI names its attempt on every request it sends from there: a higher bar against a role deciding its own work, not a boundary.",
            page,
            StringComparison.Ordinal);
    }

    /// <summary>The codes a question's answer can meet carry this one beside the refusal of a role or an attempt.</summary>
    [Fact]
    public void The_campaign_manager_page_lists_the_code_beside_the_answer_of_a_role()
    {
        var page = Page("campaign-manager.md");

        Assert.Matches($@"(?m)^\| `decision_not_human` \|.*\n\| `{Refusal.Code}` \| .+ \|$", page);
    }

    private static string Page(string name) =>
        File.ReadAllText(Path.Combine(RepositoryTree.Root(), "docs", name)).Replace("\r\n", "\n", StringComparison.Ordinal);

    private static string Flat(string text) => Whitespace().Replace(text, " ");

    /// <summary>From a heading to the next heading of the same level.</summary>
    private static string Section(string page, string heading)
    {
        var start = page.IndexOf(heading + "\n", StringComparison.Ordinal);
        Assert.True(start >= 0, $"The page has no heading '{heading}'.");
        var next = page.IndexOf("\n## ", start + heading.Length, StringComparison.Ordinal);
        return next < 0 ? page[start..] : page[start..next];
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}
