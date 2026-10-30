using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Jason.Runtime.Tests.Documentation;

/// <summary>
/// A direct send ends when the provider has taken it, sent inside the call or accepted for later, and a lost answer is
/// repeated under the work item's key. Three
/// pages say so: the work-execution contract, where an item's end is defined; the plugin guide, where a plugin author
/// learns what a repeat under a key reads; and the contracts README, whose list of capabilities a provider may lack for
/// a while names what an agent does where a send for a later moment is not offered.
/// </summary>
public sealed partial class SendAcceptedDocTests
{
    [Fact]
    public void The_work_execution_contract_says_a_send_ends_sent_or_accepted()
    {
        var section = Section(Flat(Source("docs/work-execution.md")), "## Provider operations", "## Provider blocks");

        Assert.Contains(
            "**A send ends when the provider has taken it.** Its item succeeds `sent`, where the provider sent it inside the call, or `accepted`, where it took it for now or for the moment `send_after` names; the fact that an accepted send went is learned later, never from this item.",
            section,
            StringComparison.Ordinal);
    }

    [Fact]
    public void The_plugin_guide_says_a_repeat_under_the_key_is_the_recovery_read()
    {
        var section = Section(Flat(Source("docs/plugins.md")), "### Repeating after a lost answer", "### What to return");

        Assert.Contains(
            "Where the provider keeps the work item's idempotency key on the effect itself, the recovery read can be the same call made again under the same key, which the provider answers with what it took the first time: `message.send` and `social_invitation.send` read it that way.",
            section,
            StringComparison.Ordinal);
        Assert.Contains(
            "Where you cannot tell that the provider keeps the key, the repeat is not settled: fail `send_not_settled`, `permanent`, with nothing sent, rather than reach the person a second time.",
            section,
            StringComparison.Ordinal);
    }

    [Fact]
    public void The_contracts_readme_lists_a_send_for_a_later_moment_and_its_way_around()
    {
        var section = Section(Flat(Source("docs/contracts/README.md")), "## A capability the provider does not offer yet", "## ");

        Assert.Contains(
            "- `message.send` and `social_invitation.send` with `send_after`: where the provider does not offer it, leave it out and create the item with `not_before` at the moment instead; it is claimed once the moment has passed and, being `confirm_each`, waits for a person's approval then.",
            section,
            StringComparison.Ordinal);
        Assert.DoesNotContain("None is listed yet.", section, StringComparison.Ordinal);
    }

    [Fact]
    public void The_send_contract_says_a_moment_the_provider_only_estimates_is_named_as_none()
    {
        // A provider that holds a send for a time of its own choosing knows that time only roughly, so the moment it
        // took the message for is null there too, and `cancelable` is what tells that hold from a send on its way.
        var send = JsonNode.Parse(Source("docs/contracts/operations/message.send.json"))!;
        var acceptedFor = send["output_schema"]!["properties"]!["items"]!["items"]!["properties"]!["accepted_for"]!;

        Assert.Equal(
            "The moment the provider took the message to send at, in UTC; null where it named none: it sends as soon as it can, or holds the message for a time of its own choosing that it only estimates, which `cancelable` true tells from a send on its way. Absent reads as null.",
            (string?)acceptedFor["description"]);
        Assert.Contains(
            "`accepted`: the provider took it, to send as soon as it can, at the moment `send_after` names, or at a time of its own choosing;",
            (string?)send["output_schema"]!["properties"]!["items"]!["items"]!["properties"]!["status"]!["description"],
            StringComparison.Ordinal);
    }

    [Fact]
    public void The_send_page_follows_the_contract_on_a_moment_the_provider_only_estimates()
    {
        // The page a plugin author reads says what the document says: a null moment is a send as soon as the provider
        // can, or a hold it only estimates, and `cancelable` tells the two apart.
        var page = Flat(Source("docs/contracts/operations/message.send.md"));

        Assert.Contains(
            "`accepted_for` is the moment the provider took it to send at, in UTC, or null where it named none: it sends as soon as it can, or holds the message for a time of its own choosing that it only estimates, which `cancelable` true tells from a send on its way.",
            page,
            StringComparison.Ordinal);
        Assert.Contains("to send as soon as it can, at the moment `send_after` names, or at a time of its own choosing:", page, StringComparison.Ordinal);
        Assert.DoesNotContain("or null where it took it to send as soon as it can.", page, StringComparison.Ordinal);
    }

    private static string Section(string text, string heading, string next)
    {
        var start = text.IndexOf(heading, StringComparison.Ordinal);
        Assert.True(start >= 0, $"The page has no '{heading}' section.");
        var end = text.IndexOf(next, start + heading.Length, StringComparison.Ordinal);
        return end > start ? text[start..end] : text[start..];
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    private static string Flat(string text) => Whitespace().Replace(text, " ");

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
