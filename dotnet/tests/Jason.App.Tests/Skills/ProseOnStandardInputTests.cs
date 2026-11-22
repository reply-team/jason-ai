using System.Text.Json.Nodes;
using Jason.Cli;
using Jason.Cli.Tests.Documentation;
using Jason.Runtime.Tests;

namespace Jason.App.Tests.Skills;

/// <summary>
/// No page of the runtime pack a person's session reads prints prose on the command line. A campaign's context, a role's brief, a role's note, a
/// reply's body, a text preference and a person's answer are words somebody reads, and on argv they are quoted badly,
/// seen in process lists and shell history, and broken by an apostrophe. An agent copies the shape a page prints, so the
/// pages print the careful one: the request as JSON on standard input, <c>--file -</c> and a quoted here-document.
/// </summary>
/// <remarks>
/// <para>
/// The flags that carry prose: <c>--context</c>, <c>--note</c> and <c>--answer</c> whatever they hold; <c>--set</c> and
/// a preference's <c>--value</c> when the JSON holds text, a string with a space in it; and an <c>--input</c> whose JSON
/// holds a <c>body</c>, a <c>note</c> or a <c>statement</c>, the operation's own words. A <c>--reason</c> stays on the
/// line, as does an <c>--input</c> that holds identifiers and settings only.
/// </para>
/// <para>
/// A verb that takes no file option cannot be given its prose any other way, so it is allowed, by name and with its
/// reason, and the allowance is held to the verb's own help: the day the verb takes a file, its entry fails here.
/// </para>
/// <para>
/// A launched role's page, under <c>roles/</c>, is not held to this: what a launch grants a role is the plain callback,
/// one command with no redirect joined to it, and a role has no file-writing tool, so its note and its result travel
/// inside the command (<c>ShippedRoleSkillTests</c> holds the role pages to that). A here-document there would send the
/// role to a refusal in the middle of a paid attempt. The folder decides, as it does for every guard of the pack.
/// </para>
/// </remarks>
public class ProseOnStandardInputTests
{
    /// <summary>The verbs whose prose has no other way in, each with why.</summary>
    private static readonly Dictionary<(string Verb, string Flag), string> Allowed = new()
    {
        [("decision answer", "--answer")] =
            "decision answer takes no file option: the person's answer is given as --answer alone",
        [("workitem review", "--note")] =
            "workitem review takes no file option: the doubt's one line, at most 280 characters, is given as --note alone",
    };

    private static readonly string[] OperationWords = ["body", "note", "statement"];

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public void No_page_of_the_runtime_pack_prints_prose_on_the_command_line()
    {
        var offences = new List<string>();
        var read = 0;
        foreach (var skill in SkillPack.All().Where(skill => !skill.IsRole))
        {
            var file = Path.GetRelativePath(SkillPack.RepositoryRoot(), skill.File).Replace('\\', '/');
            foreach (var command in SkillPack.Printed(skill.File))
            {
                read++;
                foreach (var flag in Prose(command.Line))
                {
                    offences.Add($"{file}:{command.Number}: {flag}");
                }
            }
        }

        Assert.True(read > 0, "The pack printed no commands at all, so this guard asserted nothing.");
        Assert.True(
            offences.Count == 0,
            "These printed lines carry prose on the command line; give it in the request on standard input instead, "
            + "--file - (or the verb's own file option) and a quoted here-document:"
            + Environment.NewLine + string.Join(Environment.NewLine, offences));
    }

    /// <summary>The guard reads what it says it reads: each flag it names is found, and each line that keeps to the shape passes.</summary>
    [Theory]
    [InlineData("jason campaign create --name \"Q3\" --context '{\"offer\":\"payroll\"}'", "--context")]
    [InlineData("jason rolenote set --role analyst --note '{\"checked\":\"2026-09-21\"}'", "--note")]
    [InlineData("jason campaign update-context cmp_1 --set '{\"directive\":\"paused until the domain is warm\"}'", "--set")]
    [InlineData("jason preference set tone_of_voice --value '{\"summary\":\"Plain and warm.\"}' --actor human:ada", "--value")]
    [InlineData("jason workitem create cmp_1 --kind provider_op --operation conversation.reply --input '{\"thread\":{\"external_id\":\"1\"},\"body\":\"Hi\"}'", "--input")]
    [InlineData("jason workitem create cmp_1 --kind provider_op --operation campaign.pause --input '{\"reason\":{\"code\":\"content\",\"note\":\"x\"}}'", "--input")]
    public void A_line_with_prose_in_a_named_flag_is_found(string line, string flag) =>
        Assert.Equal([flag], Prose(line).Select(found => found.Split(' ')[0]));

    [Theory]
    [InlineData("jason campaign update-context cmp_1 --set '{\"daily_cap\":20}' --reason \"the warm-up plan\"")]
    [InlineData("jason preference set send_mode.cold --value '{\"mode\":\"automatic\"}' --actor human:ada")]
    [InlineData("jason workitem create cmp_1 --kind provider_op --operation audience.search --input '{\"audience\":1,\"count\":25}' --reason 'the person asked'")]
    [InlineData("jason campaign create --name \"Q3\" --file - <<'EOF'")]
    [InlineData("jason decision answer dec_1 --answer \"pause it\" --actor human:ada")]
    public void A_line_that_keeps_to_the_shape_or_is_allowed_passes(string line) =>
        Assert.Empty(Prose(line));

    /// <summary>
    /// Each allowance is a verb with no file option, read from the verb's own help: an allowance outlives its reason
    /// otherwise, and the day the verb takes a file the page is still allowed to print its prose on the line.
    /// </summary>
    [Fact]
    public async Task Every_allowed_verb_still_takes_no_file_option()
    {
        foreach (var ((verb, flag), reason) in Allowed)
        {
            using var tree = new TempTree();
            var output = new StringWriter();
            var error = new StringWriter();
            var machine = DocumentedSkillCommandsTests.Machine(tree, error) with { Out = output };

            var exit = await CliApp.RunAsync([.. verb.Split(' '), "--help"], machine, Ct);

            Assert.True(exit == ExitCodes.Success, $"'{verb} --help' exited {exit}: {error}");
            var help = output.ToString();
            Assert.Contains(flag, help, StringComparison.Ordinal);
            Assert.False(
                help.Contains("-file", StringComparison.Ordinal),
                $"'{verb}' now takes a file option, so its allowance ('{reason}') no longer holds: move the pages' prose to it.");
        }
    }

    /// <summary>Every flag of the line that carries prose and is not allowed, with what it carries.</summary>
    private static IEnumerable<string> Prose(string line)
    {
        var words = ShellWords.Split(line);
        var verb = words.Count >= 2 ? words[0] + " " + words[1] : string.Empty;
        for (var index = 0; index < words.Count - 1; index++)
        {
            var flag = words[index];
            var value = words[index + 1];
            var prose = flag switch
            {
                "--context" or "--note" or "--answer" => true,
                "--set" => HoldsText(Parse(value)),
                "--value" => verb == "preference set" && HoldsText(Parse(value)),
                "--input" => HoldsWords(Parse(value)),
                _ => false,
            };

            if (prose && !Allowed.ContainsKey((verb, flag)))
            {
                yield return $"{flag} on '{verb}'";
            }
        }
    }

    private static JsonNode? Parse(string value)
    {
        try
        {
            return JsonNode.Parse(value);
        }
        catch (System.Text.Json.JsonException)
        {
            return JsonValue.Create(value);
        }
    }

    /// <summary>Whether the JSON holds text: a string with a space in it, anywhere.</summary>
    private static bool HoldsText(JsonNode? node) => node switch
    {
        JsonObject members => members.Any(member => HoldsText(member.Value)),
        JsonArray items => items.Any(HoldsText),
        JsonValue scalar => scalar.TryGetValue<string>(out var text) && text.Any(char.IsWhiteSpace),
        _ => false,
    };

    /// <summary>Whether an operation's arguments hold its own words: a body, a note or a statement, anywhere.</summary>
    private static bool HoldsWords(JsonNode? node) => node switch
    {
        JsonObject members => members.Any(member => OperationWords.Contains(member.Key) || HoldsWords(member.Value)),
        JsonArray items => items.Any(HoldsWords),
        _ => false,
    };
}
