using System.Globalization;
using System.Text.RegularExpressions;
using Jason.Cli.Doctor;

namespace Jason.App.Tests.Skills;

public partial class InteractiveSkillTests
{
    /// <summary>
    /// The troubleshooting skill's text changed to teach jason logs and jason doctor, so it is a draft until a host
    /// reads it, and the catalog claims no reading of it; it still says which work a paused campaign lets through:
    /// the field a contract declares, and the two operations that declare it today.
    /// </summary>
    [Fact]
    public void The_troubleshooting_skill_is_a_draft_until_a_host_reads_it_and_names_what_runs_in_a_paused_campaign()
    {
        var file = SkillPack.Find("troubleshooting-jason").File;
        Assert.Equal("draft", SkillFrontMatter.Read(file).Metadata.GetValueOrDefault("status"));
        Assert.DoesNotContain("| `troubleshooting-jason` | Claude Code", File.ReadAllText(SkillPack.Catalog()), StringComparison.Ordinal);
        Assert.Contains(
            "Only an active campaign's work is claimed, with one exception: a paused campaign still lets through a `provider_op` item whose operation declares `dispatch.in_paused_campaign: runs` (`optout.record` and `campaign.pause`). So an archived or still-draft campaign holds every one of its items at once, and a paused one holds every item but theirs.",
            Flattened("troubleshooting-jason"),
            StringComparison.Ordinal);
    }

    [Fact]
    public void The_troubleshooting_skill_is_honest_about_what_this_version_does_not_have()
    {
        var skill = Flattened("troubleshooting-jason");

        // A failed item ends; there is no verb that reopens one. A session that offered to "retry it" would
        // be promising something the API cannot do, and the person would wait for work that never runs.
        Assert.Contains("a failed item is finished", skill, StringComparison.OrdinalIgnoreCase);

        // And the thing to do instead, said as a rule rather than as a refusal: the corrected item is new
        // work with its own id, which is also why the old one's history stays readable.
        Assert.Contains("work still wanted is new work", skill, StringComparison.Ordinal);

        // The runtime already retried whatever deserved retrying. Typing the same thing again by hand is
        // how a real message reaches a real person twice, and it is the single most tempting wrong move
        // when somebody is standing over the session asking why nothing has happened.
        Assert.Contains("do not retry blindly", skill, StringComparison.OrdinalIgnoreCase);

        // The one outcome class that is neither success nor failure has to be named by its code, because
        // that is the word the work item prints and the word a person will paste back.
        Assert.Contains("ambiguous", skill, StringComparison.Ordinal);

        // And what the code means, spelled out. Read as "it failed", an ambiguous attempt gets repeated and
        // the provider acts twice; read as "it worked", the effect is recorded as done when it may not be.
        Assert.Contains("nobody can say whether the provider acted", skill, StringComparison.Ordinal);
    }

    [Fact]
    public void The_troubleshooting_skill_collects_the_bundle_for_the_person_and_reads_the_logs_with_the_verb()
    {
        var skill = Flattened("troubleshooting-jason");

        // This version has the command. A skill that still said it had none would have a session gather by
        // hand what one verb collects, and tell the person the tool they read about does not exist.
        Assert.DoesNotContain("no command that collects a diagnostic bundle", skill, StringComparison.Ordinal);

        // The dry run is what the person reads before the file exists. Without it a session writes the bundle
        // first and asks afterwards, about a file that is theirs to share or not.
        Assert.Contains("jason doctor --dry-run", skill, StringComparison.Ordinal);

        // A host chooses a skill by its description and reads the body only after. A description that named the
        // states and not the bundle would leave a person asking for diagnostics with no skill that answers.
        Assert.Contains(
            "when none of them explains it, collect the installation's diagnostic file with jason doctor",
            SkillFrontMatter.Read(SkillPack.Find("troubleshooting-jason").File).Top["description"],
            StringComparison.Ordinal);

        // A session that has a file and a way to send it will offer to. The bundle is written to be safe to
        // share, and whether it is shared, and with whom, is still the person's decision.
        Assert.Contains("sending it anywhere is the person's act, never yours.", skill, StringComparison.Ordinal);

        // What the bundle leaves out, said where a session reads it. Without the first, a field the bundle
        // withheld reads as a runtime that never reported it; without the second, a session redacts by hand
        // what was already rewritten, or tells the person their paths are in the file.
        Assert.Contains("What the runtime says about itself travels with a few free-text fields withheld, and the bundle names each one.", skill, StringComparison.Ordinal);
        Assert.Contains("and the names of the account and the machine are rewritten before anything is written.", skill, StringComparison.Ordinal);

        // What a plain bundle never carries, said only as far as it is true for every bundle: with the journal
        // option a work item's context travels too, so a never-list that named it would be a promise the option
        // breaks - and a session that trusted it would hand over contexts it had told the person stay home.
        Assert.Contains("It never carries the capability token, a request body or a prompt,", skill, StringComparison.Ordinal);
        Assert.DoesNotContain("a prompt or a work item's context", skill, StringComparison.Ordinal);

        // The one option that carries what people typed, with what it carries named in the composer's own words: the
        // text the warning prints and the never-carried entry quotes, so the skill cannot promise a shorter list than
        // the file holds. A session that added the option by habit would ship what the list names in a file the
        // person thought they had read the shape of.
        Assert.Contains(
            $"`--include-journal` adds {DoctorComposer.JournalCarries} \u2014 so use it only when the person asks for it, and pass on the warning it prints.",
            skill,
            StringComparison.Ordinal);

        // The skill's own list, from before there was one text, is gone: it named contact fields, which the flag's
        // text does not, and it trailed off with "among them", which a list that names everything has no use for.
        Assert.DoesNotContain("contact fields", skill, StringComparison.Ordinal);
        Assert.DoesNotContain("among them", skill, StringComparison.Ordinal);

        // The logs have a reader now, and the sentence that said nobody reads them goes with it - a session that
        // believed it would open files by hand, or tell the person there is no way to search them.
        Assert.DoesNotContain("nothing reads them for you", skill, StringComparison.Ordinal);
        Assert.Contains("jason logs", skill, StringComparison.Ordinal);

        // Where the files are, still named. A person without the session needs the path, not the verb.
        Assert.Contains("~/.jason/logs/", skill, StringComparison.Ordinal);

        // And what the logs never hold. A session that promised to find the message a provider was sent would
        // search for something that was never written, and conclude the logging is broken.
        Assert.Contains("do not send somebody to the logs expecting to find what was actually sent to a provider", skill, StringComparison.Ordinal);
    }

    [Fact]
    public void The_troubleshooting_skill_tells_a_transient_end_from_a_refusal_and_from_a_lost_answer()
    {
        var skill = Flattened("troubleshooting-jason");

        // A schema that could not be measured in time at the claim ends transient, and nothing was asked of a
        // provider. Its code reads like a refusal of the arguments; a session that took it for one would edit an
        // item that may be right, or tell the person the provider was down when it was never reached.
        Assert.Contains("`validation_timed_out`", skill, StringComparison.Ordinal);
        Assert.Contains("despite its name, found nothing wrong with the item, so it is not state 7.", skill, StringComparison.Ordinal);
        Assert.DoesNotContain("The provider was unreachable or busy, the runtime already knows", skill, StringComparison.Ordinal);

        // The same code over a plugin's answer is the other class: the provider has acted, and its effect may have
        // landed. Read as state 6, it would be left to a retry that sends the same thing to the same person again;
        // and state 8 has to say that an answer that could not be measured is one of the ways it is reached.
        Assert.Contains("The same code classed `ambiguous` is state 8: there the answer, not the item, went unmeasured, after the provider had acted.", skill, StringComparison.Ordinal);
        Assert.Contains("cut off somewhere between sending and hearing back, or its answer could not be measured,", skill, StringComparison.Ordinal);

        // And the repair follows the state. Told to fix the arguments for a transient end, a session edits
        // arguments nothing found wrong - and a new item asked for with them changed is different work. And the
        // same work asked again the moment the attempts run out, with the provider still away, is the blind retry
        // the runtime has just stopped making.
        Assert.Contains("The ordinary repair for state 7: read the contract, fix the arguments, ask again.", skill, StringComparison.Ordinal);
        Assert.Contains("When state 6 runs out of attempts the item fails final the same way, and the repair is the same work asked again as a new item once the provider is back; nothing in the arguments changes.", skill, StringComparison.Ordinal);
        Assert.DoesNotContain("The ordinary repair for states 6 and 7: read the contract, fix the arguments", skill, StringComparison.Ordinal);
        Assert.DoesNotContain("a fresh item with corrected arguments", skill, StringComparison.Ordinal);
    }

    /// <summary>
    /// The skill says how many states there are in more than one place, and the headings are what a session
    /// actually walks. A count that disagreed with them is how this skill said nine for as long as it had ten:
    /// a host told there are nine stops looking after the ninth, at the plugin that is unavailable.
    /// </summary>
    [Fact]
    public void The_troubleshooting_skill_counts_its_states_the_way_its_headings_number_them()
    {
        var file = SkillPack.Find("troubleshooting-jason").File;
        var numbers = StateHeading().Matches(File.ReadAllText(file))
            .Select(match => int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture))
            .ToList();
        Assert.Equal(Enumerable.Range(1, numbers.Count), numbers);

        var word = Counted[numbers.Count];
        var counts = StateCount().Matches(Flattened("troubleshooting-jason"))
            .Select(match => match.Groups[1].Value)
            .ToList();
        Assert.True(counts.Count >= 3, $"The skill names its count of states {counts.Count} time(s); its description, its heading and the section after the states each name it.");
        Assert.All(counts, count => Assert.Equal(word, count, ignoreCase: true));

        // The description is what a host chooses the skill by, so it is held on its own as well.
        Assert.Contains($"tell the {word} states apart", SkillFrontMatter.Read(file).Top["description"], StringComparison.Ordinal);
    }

    private static readonly string[] Counted =
        ["zero", "one", "two", "three", "four", "five", "six", "seven", "eight", "nine", "ten", "eleven", "twelve"];

    [GeneratedRegex(@"^### (\d+)\. ", RegexOptions.Multiline)]
    private static partial Regex StateHeading();

    [GeneratedRegex(@"\b(one|two|three|four|five|six|seven|eight|nine|ten|eleven|twelve) states\b", RegexOptions.IgnoreCase)]
    private static partial Regex StateCount();
}
