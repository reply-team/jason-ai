using System.Globalization;
using System.Text.RegularExpressions;
using Jason.Cli.Doctor;

namespace Jason.App.Tests.Skills;

public partial class InteractiveSkillTests
{
    /// <summary>
    /// The troubleshooting skill was read against 0.29.0 and has moved since, so it is a draft again until a host reads it against
    /// a published version: the catalog records no reading of this text. It still says which work a paused campaign lets through:
    /// the field a contract declares, and the operations that declare it today, every one the catalogue runs there.
    /// </summary>
    [Fact]
    public void The_troubleshooting_skill_is_a_draft_and_names_what_runs_in_a_paused_campaign()
    {
        var file = SkillPack.Find("troubleshooting-jason").File;
        Assert.Equal("draft", SkillFrontMatter.Read(file).Metadata.GetValueOrDefault("status"));
        Assert.Contains("**Status: draft.**", Flattened("troubleshooting-jason"), StringComparison.Ordinal);
        Assert.DoesNotContain("| `troubleshooting-jason` | Claude Code", File.ReadAllText(SkillPack.Catalog()), StringComparison.Ordinal);
        Assert.Contains(
            "Only an active campaign's work is claimed, with two exceptions: a paused campaign still lets through a `provider_op` item whose operation declares `dispatch.in_paused_campaign: runs` (`optout.record`, `campaign.pause`, `message.cancel` and `audience.search_cancel`), and an archived one a stop of a search, whose operation declares `dispatch.in_archived_campaign: runs` (`audience.search_cancel`). So a still-draft campaign holds every one of its items at once, a paused one holds every item but theirs, and an archived one every item but a search's stop.",
            Flattened("troubleshooting-jason"),
            StringComparison.Ordinal);
        Assert.Equal(
            ["audience.search_cancel", "campaign.pause", "message.cancel", "optout.record"],
            Jason.Contracts.Operations.OperationCatalog.RunsInPausedCampaign.Order(StringComparer.Ordinal));
        Assert.Equal(["audience.search_cancel"], Jason.Contracts.Operations.OperationCatalog.RunsInArchivedCampaign);
    }

    /// <summary>
    /// A hold at a daily limit reads as state 11 and is no run's doing, so the state says what it is, where a person sees it, and
    /// each of the three ways out, a raised limit among them with what it does not reach.
    /// </summary>
    [Theory]
    [InlineData("**A budget hold is the claim's own wait, and no run stands behind it.**")]
    [InlineData("`Launches:MaxPerDay:<role>` limits how many runs of a role one campaign starts in a UTC day")]
    [InlineData("its `last_error` is `launches_spent`, whose message names the setting")]
    [InlineData("its `workitem_waiting` line in the chronicle says `held_by` with the code, the limit and the role")]
    [InlineData("A notification counts it among the items that wait on a budget")]
    [InlineData("at 00:00Z the runtime releases the item by itself and counts the new day, so a day already spent by then holds it again")]
    [InlineData("a person resumes that one item under their name, as above, and it goes past the limit with its run counted like any other")]
    [InlineData("a person raises the setting, which applies from the next scan to work not yet held, while an item already held keeps its moment")]
    public void The_troubleshooting_skill_says_what_a_budget_hold_is_and_its_three_ways_out(string sentence) =>
        Assert.Contains(sentence, Flattened("troubleshooting-jason"), StringComparison.Ordinal);

    /// <summary>
    /// The two installation skills teach the journal's kinds and the subscription verbs, and what the person alone does:
    /// where a chain stands, why it waits, what a failed chain left, and a rewind that needs a named person and an offset.
    /// </summary>
    [Fact]
    public void The_installation_skills_teach_the_journal_kinds_and_the_subscription_verbs()
    {
        var operating = Flattened("operating-the-installation");
        foreach (var text in new[] { "`jason journal kinds", "jason subscription list", "`Plugins:Grants:<id>:Subscriptions`", "`*` admits every declared kind and nothing more", "`Subscriptions:Order:<kind>`", "`waiting_on`" })
        {
            Assert.Contains(text, operating, StringComparison.Ordinal);
        }

        var troubleshooting = Flattened("troubleshooting-jason");
        foreach (var text in new[]
        {
            "`jason journal kinds", "jason subscription runs --kind", "jason subscription rewind --kind", "`subscription_failed`",
            "`waiting_on`", "--actor human:<Name>", "`subscription_cursor_rewound`", "never goes forward", "A moment needs its offset", "(a line of that kind)", "a host rule's",
        })
        {
            Assert.Contains(text, troubleshooting, StringComparison.Ordinal);
        }
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

    /// <summary>
    /// A problem is reported as a public issue with the default file attached, and the command prints the form's address
    /// once it has written the file. A session offers that link and stops there: opening the issue is the person's act. A
    /// file made with the journal's entries carries what people typed, and an issue is public the moment it is
    /// submitted, so it goes to the support address only — the skill says so in the words the command prints.
    /// </summary>
    [Fact]
    public void The_troubleshooting_skill_offers_the_issue_link_as_the_person_s_act_and_a_journal_file_to_support_only()
    {
        var skill = Flattened("troubleshooting-jason");

        Assert.Contains(
            "After the file is written, `jason doctor` prints the address of the repository's problem form in its result and on stderr, as `problem_report_url` in the result, with the version and the platform filled in. **Offer the person that link: opening the issue, with the file attached, is their act, never yours.**",
            skill,
            StringComparison.Ordinal);
        Assert.Contains(
            "A file made with `--include-journal` goes only to the support address the README gives under Getting help, never to an issue: an issue is public the moment it is submitted.",
            skill,
            StringComparison.Ordinal);
        Assert.Contains("goes only to the support address under Getting help", ProblemReport.MadeToBeShared, StringComparison.Ordinal);

        // And what the bundle does with a name Jason did not ship, in the words of its own header.
        Assert.Contains("A plugin, a role or a skill pack Jason does not ship travels as an alias — `<plugin-1>`, `<role-1>`, `<skill-pack-1>`, one per name — never by its own name.", skill, StringComparison.Ordinal);
        Assert.Equal(["<plugin-N>", "<role-N>", "<skill-pack-N>"], NameAliases.Marks.Values);
        Assert.EndsWith("never to an issue.", ProblemReport.JournalToSupport, StringComparison.Ordinal);

        // A skill source of the person's own, and the folders a person chose, by the marks the header lists.
        Assert.Contains("A skill source given with `--source` other than the public repository, and the ref it was taken at unless that is a release tag, travel as `<skill-source>`.", skill, StringComparison.Ordinal);
        Assert.Equal("<skill-source>", IdentityRewriter.MarkWords["skill_source_written_as"]);
        Assert.Contains("The home directory, a data directory other than `~/.jason` — this one, or another the runtime reports — the harness root, the folder the executable is in outside home, and the names of the account and the machine are rewritten before anything is written.", skill, StringComparison.Ordinal);

        // A file with the journal's entries: the result names no form, and its warnings say where it goes.
        Assert.Contains("With `--include-journal`, `problem_report_url` is null and the result's warnings say the file goes to the support address only; never offer an issue for it.", skill, StringComparison.Ordinal);
    }

    /// <summary>
    /// The four readings come before any diagnostic file, the dry run included. Said as advice, a host read
    /// "Look here first" and ran <c>jason doctor --dry-run</c> as its very first command; the file is for what
    /// the readings cannot explain, and nobody knows what that is before reading them.
    /// </summary>
    [Fact]
    public void The_troubleshooting_skill_makes_its_four_readings_binding_before_any_doctor()
    {
        var skill = Flattened("troubleshooting-jason");

        Assert.Contains(
            "**Before any `jason doctor` — the dry run included — run these four readings, in this order. A diagnostic file is for what they cannot explain, and you cannot know that before you have read them.** Each one can end the investigation on its own, and each one makes the next easier to read.",
            skill,
            StringComparison.Ordinal);
        Assert.DoesNotContain("Four readings before you form an opinion.", skill, StringComparison.Ordinal);
    }

    /// <summary>
    /// A person a notification sent because protective work failed arrives with campaign ids and no work item. The
    /// failed items are named by the third reading, so it comes first there; and the paragraph sits after the four
    /// readings it refers to and before the three that stay advice.
    /// </summary>
    [Fact]
    public void The_troubleshooting_skill_says_what_to_read_when_a_notification_brought_the_person()
    {
        var skill = Flattened("troubleshooting-jason");
        const string Paragraph =
            "**When a notification brought them here.** A person may arrive because a notification said protective actions failed in campaigns it names by id. The failed and expired work items of those campaigns are the subject: the third reading above, once for each id it names, and without `--campaign` when it says there are more. Each item it lists is then read as this skill reads a failed item. The four readings are binding here too, before anything else; here the third comes before the second, because it is what names the items.";

        var at = skill.IndexOf(Paragraph, StringComparison.Ordinal);
        Assert.True(at >= 0, "the paragraph for a person a notification brought is not in the skill, word for word.");
        Assert.True(at > skill.IndexOf("jason journal list --campaign", StringComparison.Ordinal), "the paragraph comes before the four readings it refers to.");
        Assert.True(at < skill.IndexOf("Three further readings", StringComparison.Ordinal), "the paragraph comes after the readings that stay advice.");
    }

    /// <summary>
    /// After the dry run, one question and nothing else. A host that offered a report instead, or beside the file, left
    /// the person choosing between things they had not asked about, and wrote nothing they had.
    /// </summary>
    [Fact]
    public void The_troubleshooting_skill_asks_one_question_after_the_dry_run_and_offers_nothing_else()
    {
        var skill = Flattened("troubleshooting-jason");

        Assert.Contains(
            "It writes nothing; it lists what a plain run would write, and where. **Then ask the person one question — may I write the file? — and stop. Offer no alternatives; a report written instead, or beside it, is not what they were asked about.** The bundle holds",
            skill,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// A conversation that already showed the dry run continues with "go ahead"; a host chooses by the description, so
    /// the words a continuing conversation uses are there.
    /// </summary>
    [Fact]
    public void The_troubleshooting_skill_is_chosen_by_the_words_a_continuing_conversation_uses()
    {
        Assert.Contains(
            "collect the installation's diagnostic file with jason doctor — including when the person says go ahead with the diagnostic file, write it, or collect the diagnostics.",
            SkillFrontMatter.Read(SkillPack.Find("troubleshooting-jason").File).Top["description"],
            StringComparison.Ordinal);
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
    /// a host told there are nine stops looking after the ninth, at the plugin that is unavailable. The count is
    /// "the ten states"; a phrase such as "either of two states" counts nothing and is not held to it.
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

    /// <summary>
    /// Restarting the runtime is a repair a session would run itself, and a restart is a stop: before the line is printed,
    /// the repair points at the one rule for a stop a session runs — the operating skill's — rather than leaving the
    /// session to run it on the person's complaint alone. And its account of what the stop does to work in flight says
    /// what that rule's sentence says, in the same terms: claiming stops, handlers get up to ten seconds, nothing is
    /// killed, so a session that read both skills is never told two different things.
    /// </summary>
    [Fact]
    public void The_restart_repair_points_at_the_one_rule_for_a_stop()
    {
        var skill = Flattened("troubleshooting-jason");
        const string Pointer = "A restart is a stop you run yourself: ask first, as \"Starting and stopping it\" in `operating-the-installation` says.";

        var repair = skill.IndexOf("**Restarting the runtime**", StringComparison.Ordinal);
        var pointer = skill.IndexOf(Pointer, StringComparison.Ordinal);
        var line = skill.IndexOf("``` jason runtime restart ```", StringComparison.Ordinal);
        Assert.True(
            repair >= 0 && pointer > repair && line > pointer,
            $"The restart repair opens at offset {repair}, points at the rule at {pointer} and prints its line at {line}; the pointer belongs between the two.");

        Assert.Contains(
            "a shutdown stops claiming, gives handlers up to ten seconds, and **does not kill children that are still running**",
            skill,
            StringComparison.Ordinal);
        Assert.DoesNotContain("waits briefly for handlers", skill, StringComparison.Ordinal);
    }

    /// <summary>
    /// A waiting item is the runtime doing what it was told, and it has no repair while the wait holds: a session that
    /// cancelled it, or raised its work again, to unstick it would undo a plan or run it twice. Ending it early is a person's
    /// word, and the line printed for it names them.
    /// </summary>
    [Fact]
    public void The_troubleshooting_skill_tells_a_waiting_item_apart_and_leaves_its_early_end_to_a_person()
    {
        var file = SkillPack.Find("troubleshooting-jason").File;
        var skill = Flattened("troubleshooting-jason");

        // The heading names the state, not one thing it waits on: a park on a provider block is this state as well.
        var headings = File.ReadAllLines(file).Where(line => line.StartsWith("### 11. ", StringComparison.Ordinal)).ToList();
        Assert.True(
            headings is ["### 11. Waiting"],
            $"The troubleshooting skill heads state 11 as [{string.Join(" | ", headings)}]; it is headed '### 11. Waiting'.");
        Assert.Contains(
            "A park on a provider block reads as this same `waiting` state with its block named, not as a state of its own.",
            skill,
            StringComparison.Ordinal);
        Assert.Contains("**How you recognise it.** The status is `waiting`", skill, StringComparison.Ordinal);
        Assert.Contains("There is no repair while the wait holds", skill, StringComparison.Ordinal);
        Assert.Contains("do not cancel it to unstick it, and do not create its work again", skill, StringComparison.Ordinal);
        Assert.Contains("five of them are not failures at all", skill, StringComparison.Ordinal);

        var resumes = SkillPack.PrintedCommands(File.ReadAllLines(file))
            .Where(line => line.Contains(" workitem resume ", StringComparison.Ordinal))
            .ToList();
        Assert.True(
            resumes.Count == 1 && resumes[0].StartsWith("jason --actor human:", StringComparison.Ordinal),
            $"The troubleshooting skill prints [{string.Join(" | ", resumes)}] to end a wait; it prints one, naming the person.");
    }

    /// <summary>
    /// A capability the provider does not offer yet is a state of its own: final like a permanent problem, but no
    /// correction of the arguments gets past it and nothing falls back on its own, so state 7, the one a reader reaches
    /// first, points at it, and the summary counts it among the ones nobody can fix where the work was asked.
    /// </summary>
    [Fact]
    public void The_troubleshooting_skill_tells_a_capability_the_provider_does_not_offer_apart()
    {
        var file = SkillPack.Find("troubleshooting-jason").File;
        var skill = Flattened("troubleshooting-jason");

        var headings = File.ReadAllLines(file).Where(line => line.StartsWith("### 12. ", StringComparison.Ordinal)).ToList();
        Assert.True(
            headings is ["### 12. A capability the provider does not offer yet"],
            $"The troubleshooting skill heads state 12 as [{string.Join(" | ", headings)}].");
        Assert.Contains("**How you recognise it.** `capability_not_offered`, classed `permanent`.", skill, StringComparison.Ordinal);
        Assert.Contains("no correction of the arguments gets past it", skill, StringComparison.Ordinal);
        Assert.Contains("nothing in the runtime falls back to something else", skill, StringComparison.Ordinal);
        Assert.Contains("its API has no route for the call, or does not take the argument. Nothing was done there.", skill, StringComparison.Ordinal);
        Assert.Contains("How a plugin tells is stated once in the contracts README (`docs/contracts/README.md`)", skill, StringComparison.Ordinal);
        Assert.DoesNotContain("does not support the request", skill, StringComparison.Ordinal);
        Assert.Contains("`capability_not_offered` is classed `permanent` as well, and is state 12.", skill, StringComparison.Ordinal);
        Assert.Contains("five of them nobody can fix from a session", skill, StringComparison.Ordinal);
    }

    /// <summary>
    /// A plugin that cannot read the provider's own object an item carries refuses it before any call, and that refusal
    /// is state 7's, with a repair of its own kind: the message in the item's last error names the shape the plugin
    /// reads, and the role that wrote the object corrects it in a new item. A skill that left the code out would send a
    /// reader to the operation's document, which says nothing of that object.
    /// </summary>
    [Fact]
    public void State_seven_names_the_refusal_of_the_provider_object_and_its_repair()
    {
        var text = File.ReadAllText(SkillPack.Find("troubleshooting-jason").File).ReplaceLineEndings("\n");
        var start = text.IndexOf("### 7. A permanent or validation problem", StringComparison.Ordinal);
        var end = text.IndexOf("### 8. ", StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start, "The troubleshooting skill has no state 7 before state 8.");
        var state = Regex.Replace(text[start..end], @"\s+", " ");

        Assert.Contains(
            "One validation code is about the provider's own object rather than the operation's arguments: `provider_input_invalid` says the plugin could not read the `provider` object the item carries beside its arguments in `context.input`, and refused it before any call.",
            state,
            StringComparison.Ordinal);
        Assert.Contains(
            "Its message, in the item's `last_error.message`, names the shape the plugin reads, or what in the object it cannot take, and never quotes what was given, and the provider's own skill teaches what goes there.",
            state,
            StringComparison.Ordinal);
        Assert.Contains(
            "The role that wrote the object corrects it on its next attempt: a corrected new work item, with the object in that shape.",
            state,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// While an earlier attempt of a send may have reached the person, a refusal at the claim keeps the code of the
    /// state it would name alone — a missing route, a suppressed value, arguments to correct — and is recorded
    /// ambiguous. Each of those states says nothing was started or sent and points at a new item, so an agent reading
    /// the code alone would create the send again before anyone knew whether the first one went. The rule is stated
    /// once, where the states are told apart, and state 8 says how such a refusal looks.
    /// </summary>
    [Fact]
    public void The_troubleshooting_skill_reads_a_refusal_recorded_ambiguous_as_an_interrupted_attempt_first()
    {
        var skill = Flattened("troubleshooting-jason");

        const string rule = "**A refusal recorded `ambiguous` is state 8 first, whatever its code.**";
        var states = skill.IndexOf("## The twelve states", StringComparison.Ordinal);
        var at = skill.IndexOf(rule, StringComparison.Ordinal);
        var first = skill.IndexOf("### 1. Waiting for its time", StringComparison.Ordinal);
        Assert.True(
            states >= 0 && at > states && first > at,
            $"The states open at {states}, the rule at {at} and state 1 at {first}: the rule belongs where the states are told apart.");
        Assert.Single(Regex.Matches(skill, Regex.Escape(rule)));
        Assert.Contains(
            "Nobody creates the send again, corrected or not, until the provider has said the earlier attempt did not act.",
            skill,
            StringComparison.Ordinal);

        var interrupted = skill.IndexOf("### 8. An attempt that was interrupted", StringComparison.Ordinal);
        var shape = skill.IndexOf(
            "So is a refusal whose code names another state, classed `ambiguous`, whose message says that an earlier attempt of this work item may have reached the person",
            StringComparison.Ordinal);
        var meaning = skill.IndexOf("**What it means.** It means exactly one thing", StringComparison.Ordinal);
        Assert.True(
            interrupted >= 0 && shape > interrupted && meaning > shape,
            $"State 8 opens at {interrupted}, the refusal's shape at {shape} and its meaning at {meaning}: the shape belongs under how you recognise it.");
    }

    private static readonly string[] Counted =
        ["zero", "one", "two", "three", "four", "five", "six", "seven", "eight", "nine", "ten", "eleven", "twelve"];

    [GeneratedRegex(@"^### (\d+)\. ", RegexOptions.Multiline)]
    private static partial Regex StateHeading();

    [GeneratedRegex(@"\bthe (one|two|three|four|five|six|seven|eight|nine|ten|eleven|twelve) states\b", RegexOptions.IgnoreCase)]
    private static partial Regex StateCount();

    /// <summary>
    /// A park on a provider block is state 11 with a block named, and its repair is the person's at the provider: a session
    /// that cancelled, recreated or resumed the parked items would only park the same work again, and one that cleared the
    /// block on its own judgement would be saying the cause is gone when nobody had looked.
    /// </summary>
    [Fact]
    public void The_troubleshooting_skill_reads_a_park_on_a_block_and_leaves_its_repair_to_the_person()
    {
        var skill = Flattened("troubleshooting-jason");

        var waiting = skill.IndexOf("### 11. Waiting", StringComparison.Ordinal);
        var park = skill.IndexOf("**A park on a provider block has a repair, and it is the person's.**", StringComparison.Ordinal);
        var repairs = skill.IndexOf("## The repairs that are safe", StringComparison.Ordinal);
        Assert.True(
            waiting >= 0 && park > waiting && repairs > park,
            $"State 11 opens at {waiting}, the park on a block at {park} and the safe repairs at {repairs}: the park belongs inside state 11.");
        Assert.Contains("Its `wait` names the block under `blocked_by`", skill, StringComparison.Ordinal);
        Assert.Contains("its `last_error` is `provider_blocked`", skill, StringComparison.Ordinal);
        Assert.Contains("`jason block list` shows every open block, how many items each holds and its link", skill, StringComparison.Ordinal);
        Assert.Contains(
            "they fix the cause at the provider, through the link, and the block's trial then lets one item through: acts first; where none can run, one held control, whose answer decides nothing but `blocked`. Once the provider takes an act, the block clears and releases everything it held.",
            skill,
            StringComparison.Ordinal);
        Assert.Contains("jason workitem list --block blk_01JB6K8TQ2W9V4MZ0C3Y7H5NRD --human", skill, StringComparison.Ordinal);
        Assert.Contains("jason --actor human:ada block clear blk_01JB6K8TQ2W9V4MZ0C3Y7H5NRD --reason", skill, StringComparison.Ordinal);
        Assert.Contains("A clear is their word that the cause is gone, not a mute, and where the provider still refuses, the next call opens a new block.", skill, StringComparison.Ordinal);
        Assert.Contains("Do not cancel, recreate or resume a parked item", skill, StringComparison.Ordinal);
        Assert.Contains("Work a block holds does not end here: it waits on the block, which is state 11.", skill, StringComparison.Ordinal);
    }
}
