using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Jason.Cli.Tests.Documentation;

namespace Jason.App.Tests.Skills;

public partial class RoleSkillTests
{
    /// <summary>The words each role that writes for a person is told, once, where its skill speaks of what it writes.</summary>
    private const string InWords = "names people, messages and campaigns in words";

    /// <summary>
    /// What a role writes for a person — a question, its options, the sentence of a doubt or of a review on an approval,
    /// a proposal read beside one, the reason on a reply the person reads — names people, messages and campaigns in
    /// words. A person reads it in Needs you and on the cards, where a raw <c>wi_…</c> or "thread 501" says nothing;
    /// the identifier belongs in the member made for it — a question's <c>--reference</c>, a send's
    /// <c>--contact</c> and input — and a role copies what its skill prints.
    /// </summary>
    [Fact]
    public void What_a_role_writes_for_a_person_carries_no_identifier()
    {
        foreach (var role in new[] { "manager", "responder", "planner", "critic", "copywriter", "personalizer" })
        {
            Assert.Contains(InWords, Flattened(role), StringComparison.Ordinal);
        }

        var read = 0;
        foreach (var skill in SkillPack.All().Where(skill => skill.IsRole))
        {
            foreach (var line in SkillPack.PrintedCommands(skill.File))
            {
                foreach (var text in WrittenForAPerson(line))
                {
                    read++;
                    var found = Identifier().Match(text);
                    Assert.False(
                        found.Success,
                        $"'{skill.Name}' prints '{found.Value}' inside text a person reads: \"{text}\". Name the person, "
                        + "the message or the campaign in words, and put the identifier in the member made for it.");
                }
            }
        }

        // The walk read what it was written for: a reader that found nothing would pass every skill.
        Assert.True(read >= 10, $"Only {read} person-facing texts were read from the role skills' printed commands.");
    }

    private static IEnumerable<string> WrittenForAPerson(string line)
    {
        var words = ShellWords.Split(line).ToList();
        string? After(int at) => at + 1 < words.Count ? words[at + 1] : null;

        var isReview = line.StartsWith("jason workitem review ", StringComparison.Ordinal);
        var isSend = line.Contains("--operation conversation.reply", StringComparison.Ordinal)
            || line.Contains("--operation message.send", StringComparison.Ordinal);
        for (var at = 0; at < words.Count; at++)
        {
            var value = After(at);
            if (value is null)
            {
                continue;
            }

            switch (words[at])
            {
                case "--question" or "--option" or "--ask-person-note":
                case "--note" when isReview:
                case "--reason" when isSend:
                    yield return value;
                    break;
                case "--context" when JsonNode.Parse(value) is JsonObject context && context["proposal"] is JsonValue proposal:
                    yield return (string)proposal!;
                    break;
            }
        }
    }

    /// <summary>A public id (a prefix and a ULID), or a provider's thread by its number.</summary>
    [GeneratedRegex(@"\b[a-z]{2,4}_[0-9A-Z]{26}\b|\bthread \d+\b")]
    private static partial Regex Identifier();
}
