using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Jason.Cli.Tests.Documentation;
using Jason.Contracts.Operations;
using Jason.Runtime.Tests;

namespace Jason.App.Tests.Skills;

public partial class InteractiveSkillTests
{
    /// <summary>
    /// The reserve's threshold is twenty, the runtime's own number, which it sets at the first start of a campaign fed by a search, and a
    /// person has none by clearing it after the start. A session told the person sets it once the probe lands, a fifth of
    /// the probe, would set a second number over the runtime's, or tell a person a null before the start means none.
    /// </summary>
    [Fact]
    [Critical(Family.D4, "a skill a person's session reads teaches what was not decided")]
    public void The_reserve_s_threshold_is_twenty_the_first_start_sets_and_a_person_clears_after_it()
    {
        var managed = Flattened("managed-campaign-work");
        Assert.Contains("The reserve's threshold is the campaign's own field, twenty, the runtime's own number: the runtime sets it at the campaign's first start, because its people come from a search, and the start's answer shows it. A campaign whose people the person brought as a list is given none.", managed, StringComparison.Ordinal);
        Assert.Contains("or clear it after the start to have none, `--clear audience_threshold`, and it stays cleared.", managed, StringComparison.Ordinal);

        var approvals = Flattened("approvals-and-questions");
        Assert.Contains("own field, twenty, the runtime's own number, which it sets at the campaign's first start, because its people come from a search.", approvals, StringComparison.Ordinal);

        foreach (var skill in new[] { managed, approvals, Flattened("planner-in-session") })
        {
            Assert.DoesNotContain("a fifth of the probe", skill, StringComparison.Ordinal);
            Assert.DoesNotContain("a reserve threshold of 5", skill, StringComparison.Ordinal);
            Assert.DoesNotContain("one batch", skill, StringComparison.Ordinal);
        }
    }

    [Fact]
    [Critical(Family.D4, "a skill a person's session reads teaches what was not decided")]
    public void The_managed_path_says_what_the_runtime_decides_and_what_it_promises_nothing_about()
    {
        var skill = Flattened("managed-campaign-work");

        // No provider ships configured, and a route is somebody's deliberate act rather than a default. A
        // skill that implied otherwise would have the session promise work that fails with no_route, which
        // reads to a person as the product being broken. The sentence is pinned rather than the word: "route"
        // also matches "routed", so a text saying the opposite passed the assertion that was here.
        Assert.Contains("writing a route is an operator's explicit act", skill, StringComparison.Ordinal);
        Assert.Contains("no_route", skill, StringComparison.Ordinal);
        Assert.DoesNotContain("out of the box", skill, StringComparison.OrdinalIgnoreCase);

        // The wiring check proves a provider is reached, and it is not campaign work: it is a walk the person takes only
        // when they say so, mentioned by `quick-start`, and the real work begins with the person's brief and a plan, which
        // this skill turns into work. A session that took the check for the job would stop there.
        Assert.Contains(
            "The wiring check, `docs/golden-path.md`, connects a provider and proves it with the person's own address; it is not campaign work. It is a synthetic walk the person takes only when they say so, and `quick-start` is what mentions it. The real work begins with the person's brief — what they tell you about their first campaign, in their own words — and a campaign planned with the business pack's `campaign-planning` skill, and this skill is how the brief and that plan become work the runtime and Jason's roles perform.",
            skill,
            StringComparison.Ordinal);

        // And it says so where a session checks whether a provider is connected: at the end of "Before you start", after
        // the sentence that sends setting one up to the operating skill, and before the work itself.
        var text = File.ReadAllText(SkillPack.Find("managed-campaign-work").File).Replace("\r\n", "\n", StringComparison.Ordinal);
        var setUp = text.IndexOf("Setting one up is `operating-the-installation`", StringComparison.Ordinal);
        var wiring = text.IndexOf("The wiring check, `docs/golden-path.md`, connects a provider", StringComparison.Ordinal);
        var work = text.IndexOf("\n## The work itself\n", StringComparison.Ordinal);
        Assert.True(
            setUp >= 0 && wiring > setUp && work > wiring,
            $"managed-campaign-work says where the wiring check stands at offset {wiring}; it belongs after the operating skill's sentence (offset {setUp}) and before \"## The work itself\" (offset {work}).");

        // Arguments are judged when the item is created, against the operation's own published document — so
        // a refusal there is about what was asked for, and not about the provider being unreachable.
        Assert.Contains("validated when the item is created", skill, StringComparison.Ordinal);

        // Work created inside a launched run inherits that run's profile, and naming an actor breaks the
        // chain. A session that named one would scatter a campaign's work across hosts nobody chose.
        Assert.Contains("inherits the profile of the run that created it", skill, StringComparison.Ordinal);

        // Scheduling and resumption belong to the runtime. This is the sentence that replaces the model this
        // pack supersedes, and it has to be in the skill rather than only in a document nobody hands over.
        Assert.Contains("nothing else wakes the work", skill, StringComparison.Ordinal);

        // A create whose answer was lost is sent again with the same key, never without one: the key is what makes
        // the second send the first one's answer rather than a second campaign or a second enrolment. And nothing
        // makes a key up, so a session that does not give one has none to repeat.
        Assert.Contains("send the same command with the same key", skill, StringComparison.Ordinal);
        Assert.Contains("Nothing makes a key up for you", skill, StringComparison.Ordinal);

        // And the three moments that are skills of their own, named so that a session knows they exist. A
        // session that met a parked approval, a stopped item or an effect produced outside Jason and had
        // never heard of them would improvise — which is the one thing this pack exists to prevent.
        Assert.Contains("approvals-and-questions", skill, StringComparison.Ordinal);
        Assert.Contains("troubleshooting-jason", skill, StringComparison.Ordinal);
        Assert.Contains("reporting-outside-effects", skill, StringComparison.Ordinal);
    }

    /// <summary>
    /// The first campaign of a product goes under that product's project, named from what is sold and found before it is
    /// made, and the create names it. A campaign left to the default, or to whatever project the folder a command runs in
    /// would supply, files a product's first campaign under nobody's product; a project made twice is refused, and the
    /// refusal names the one the first create made. Held whole, and in the printed order: the project read, the project
    /// made, then the campaign under it.
    /// </summary>
    [Fact]
    [Critical(Family.D4, "a skill a person's session reads teaches what was not decided")]
    public void The_first_campaign_goes_under_the_project_of_what_is_sold()
    {
        const string Opening = "**What is sold is the project.**";
        var text = File.ReadAllText(SkillPack.Find("managed-campaign-work").File).Replace("\r\n", "\n", StringComparison.Ordinal);
        var start = text.IndexOf(Opening, StringComparison.Ordinal);
        Assert.True(start >= 0, $"managed-campaign-work no longer says '{Opening}'.");
        var end = text.IndexOf("\n\n", start, StringComparison.Ordinal);

        Assert.Equal(
            Opening + " A project gathers the campaigns of one product, so it is named from what the person sells, in their "
            + "words, and looked for before it is made: `jason project list --query` with a word of that name answers whether "
            + "this product has one already. Make it only when there is none. A create refused with `project_exists` names, "
            + "in its details, the project that already has the slug: read it with `jason project get`, and where its name is "
            + "this product's, it is the one to use, since the first create's answer was lost; where it is another product's, "
            + "the two names make the same slug, so give the new project one of its own with `--slug`. The campaign then goes "
            + "under it, named with `--project` and the project's id: `Default` holds the campaigns made without one and is "
            + "never the home of a named product's first campaign, unless the person renamed it to that product's name, which "
            + "makes it that product's project and the one `jason project list --query` finds. The project a command could "
            + "take from the folder it runs in is no answer for that first create.",
            Whitespace().Replace(text[start..(end > start ? end : text.Length)], " ").Trim());

        var from = text.IndexOf("\n## The work itself\n", StringComparison.Ordinal);
        var to = text.IndexOf("\n## ", from + 1, StringComparison.Ordinal);
        Assert.True(from >= 0 && to > from, "managed-campaign-work no longer has \"## The work itself\" followed by another section.");
        var printed = SkillPack.PrintedCommands(text[from..to].Split('\n')).ToList();
        var list = printed.FindIndex(line => line.StartsWith("jason project list --query ", StringComparison.Ordinal));
        var make = printed.FindIndex(line => line.StartsWith("jason project create --name ", StringComparison.Ordinal));
        var create = printed.FindIndex(line => line.StartsWith("jason campaign create ", StringComparison.Ordinal));
        Assert.True(
            list >= 0 && make > list && create > make,
            $"Under \"The work itself\" the project is read at {list}, made at {make} and the campaign created at {create} of: "
            + $"{string.Join(" | ", printed)}. The project is looked for, then made, then the campaign goes under it.");
        Assert.True(
            printed[create].Contains(" --project prj_", StringComparison.Ordinal),
            $"The first campaign create under \"The work itself\" names no project: {printed[create]}");

        // The section's lead-in says the same order in words, so a reader who skims to the commands meets it first; and
        // it says that the start is the set-up's, and a campaign's own messages are never the session's work.
        Assert.Contains(
            "A project for what is sold, a campaign under it, the people the campaign is about, and the start, as \"Setting a campaign up, and starting it\" leads them. The work a person asks for by name afterwards, an answer in a thread, a read or a pause, is one work item per thing to be done. A campaign's own messages are its roles' work, never yours.",
            Flattened("managed-campaign-work"),
            StringComparison.Ordinal);
    }

    /// <summary>
    /// The check that stops at no plugin stops only the work that reaches a provider. A person who skipped the wiring check
    /// has no plugin, and the planner tells them they plan as anybody does; a session that read the check as covering the
    /// project, the campaign and its people would stop the first run before anything was made.
    /// </summary>
    [Fact]
    [Critical(Family.D4, "a skill a person's session reads teaches what was not decided")]
    public void The_no_plugin_stop_is_for_work_that_reaches_a_provider()
    {
        Assert.Contains(
            "That stop is for work that reaches a provider: a project, a campaign and the people it is about need none, so they are made with no plugin loaded, and the stop comes at the first work item that would reach one.",
            Flattened("managed-campaign-work"),
            StringComparison.Ordinal);
    }

    /// <summary>
    /// What the person told about their campaign becomes its context when Jason's campaign is created, before any contact
    /// exists and before anything is sent, so every role works from the brief rather than from whatever reached it later.
    /// The context is shared knowledge a role that reads the campaign reads whole, so its keys are the person's to name, but for
    /// `goals`, the one key the runtime reserves and checks; and not every role reads it, so a session is not told that a standing instruction there reaches one
    /// that works from its own work item's context alone.
    /// And the verb is Jason's own: the provider operation of the same name makes a campaign somewhere else. A session that
    /// added the people first, or kept the brief in the conversation, would start the work from less than it was given.
    /// The paragraph is held whole: a sentence added to it that named a second key every role is told to rely on would turn "one
    /// key is reserved" into its opposite while every sentence pinned here still stood.
    /// </summary>
    [Fact]
    [Critical(Family.D4, "a skill a person's session reads teaches what was not decided")]
    public void The_brief_becomes_Jason_s_campaign_before_any_contact_exists()
    {
        const string Opening = "**The brief becomes the campaign.**";
        var text = File.ReadAllText(SkillPack.Find("managed-campaign-work").File).Replace("\r\n", "\n", StringComparison.Ordinal);

        var start = text.IndexOf(Opening, StringComparison.Ordinal);
        Assert.True(start >= 0, "managed-campaign-work has no paragraph on the brief.");
        var end = text.IndexOf("\n\n", start, StringComparison.Ordinal);
        var paragraph = Whitespace().Replace(text[start..(end > start ? end : text.Length)], " ").Trim();

        Assert.Equal(
            Opening + " What the person told you about the campaign — the product, the audience, how it sells, in their own words — is the campaign's context, given at `jason campaign create` as the `context` of the request it reads on standard input (`--file -`), before any contact is added and before anything is sent. "
            + "It is the campaign's shared knowledge: a role that reads the campaign (`jason campaign get`) reads it whole, and a role that does not works from its own work item's context alone. So its keys are yours to name for what they hold, apart from `goals`, the one key Jason reserves and checks (below). "
            + "Keep the person's own words beside what you made of them, and put a link or a file in as what it says, briefly, with where it is, rather than pasted whole. "
            + "This `jason campaign create` makes Jason's own campaign; it is not the provider operation `campaign.create`, which makes a campaign at a provider.",
            paragraph);
        Assert.DoesNotContain("every one of Jason's roles", Flattened("managed-campaign-work"), StringComparison.Ordinal);

        // And the printed lines say it in the same order: under "The work itself" the first campaign create carries the
        // brief and comes before the first add-contacts, and the paragraph follows that block, before the one on keys.
        var from = text.IndexOf("\n## The work itself\n", StringComparison.Ordinal);
        var to = text.IndexOf("\n## ", from + 1, StringComparison.Ordinal);
        Assert.True(from >= 0 && to > from, "managed-campaign-work no longer has \"## The work itself\" followed by another section.");
        var work = text[from..to];

        var commands = SkillPack.Printed(work.Split('\n'));
        var printed = commands.Select(command => command.Line).ToList();
        var create = printed.FindIndex(line => line.StartsWith("jason campaign create ", StringComparison.Ordinal));
        var contacts = printed.FindIndex(line => line.StartsWith("jason campaign add-contacts ", StringComparison.Ordinal));
        Assert.True(
            create >= 0 && contacts > create,
            $"Under \"The work itself\" the campaign create is printed at line {create} and the first add-contacts at line {contacts} of: {string.Join(" | ", printed)}");
        Assert.True(
            commands[create].Body is { } body && JsonNode.Parse(body)?["context"] is JsonObject,
            $"The campaign create under \"The work itself\" gives the campaign no brief in the request it hands the program: {printed[create]}");

        var block = work.IndexOf(printed[create], StringComparison.Ordinal);
        var brief = work.IndexOf(Opening, StringComparison.Ordinal);
        var keys = work.IndexOf("**Give every create a key you could send again.**", StringComparison.Ordinal);
        Assert.True(
            block >= 0 && brief > block && keys > brief,
            $"The paragraph on the brief is at offset {brief} of \"The work itself\"; it belongs after the create (offset {block}) and before the paragraph on keys (offset {keys}).");
    }

    /// <summary>
    /// A brief changes after the campaign exists, and the change belongs where the brief went: the campaign's context,
    /// written with the verb that sets and unsets top-level keys and leaves the rest as it was. Without it a session that
    /// agreed a new direction keeps it in the conversation, where no role reads it, or creates a second campaign. The
    /// paragraph follows the one on the brief, and its line is printed beside it, where the sequence test runs it.
    /// </summary>
    [Fact]
    [Critical(Family.D4, "a skill a person's session reads teaches what was not decided")]
    public void A_change_of_direction_goes_into_the_campaign_s_context()
    {
        const string Opening = "**A change of direction goes into the context as well.**";
        var text = File.ReadAllText(SkillPack.Find("managed-campaign-work").File).Replace("\r\n", "\n", StringComparison.Ordinal);

        var start = text.IndexOf(Opening, StringComparison.Ordinal);
        Assert.True(start >= 0, "managed-campaign-work has no paragraph on a change of direction.");
        var end = text.IndexOf("\n\n", start, StringComparison.Ordinal);
        Assert.Equal(
            Opening + " On a campaign that already exists, what the person changes about it is written with `jason campaign update-context`: `set`, in the request it reads on standard input, writes the top-level keys it names, `--unset` removes a key, once for each, and every key it does not name stays as it was.",
            Whitespace().Replace(text[start..(end > start ? end : text.Length)], " ").Trim());

        var brief = text.IndexOf("**The brief becomes the campaign.**", StringComparison.Ordinal);
        var keys = text.IndexOf("**Give every create a key you could send again.**", StringComparison.Ordinal);
        // The set-up prints a change of context of its own, the first batch, before the work block; the line meant here
        // is the first one after the paragraph.
        var printed = text.IndexOf("\njason campaign update-context cmp_01JB6K8TQ2W9V4MZ0C3Y7H5NRD ", Math.Max(start, 0), StringComparison.Ordinal);
        Assert.True(
            brief >= 0 && start > brief && printed > start && keys > printed,
            $"The paragraph on a change of direction is at offset {start} and its line at {printed}; they belong after the paragraph on the brief (offset {brief}) and before the one on keys (offset {keys}).");
    }

    /// <summary>
    /// What the person has already written down is read before work is created, in the order every launched role reads
    /// it: the client's preferences, the memory files of the client, the project and the campaign, then what the person
    /// says now. A session's host composes some of those files and not others — Claude Code in print mode drops an import
    /// from outside the folder it started in, Codex, by its documentation, outside a repository reads its own folder's file alone — so the
    /// session reads by path the ones it was not given, from where the runtime records them. A session that trusted its
    /// host to have read all three would plan a campaign against the campaign's words alone.
    /// </summary>
    [Fact]
    [Critical(Family.D4, "a skill a person's session reads teaches what was not decided")]
    public void The_managed_path_reads_what_the_person_wrote_down_in_order_and_by_path()
    {
        var skill = Flattened("managed-campaign-work");

        var order = new[] { "the client's named preferences", "the person's memory file for the client", "then the project's", "then the campaign's", "what they tell you now" };
        var at = order.Select(step => skill.IndexOf(step, StringComparison.Ordinal)).ToList();
        Assert.True(
            at.All(index => index >= 0) && at.Zip(at.Skip(1)).All(pair => pair.First < pair.Second),
            $"managed-campaign-work names the reading order's steps at [{string.Join(", ", at)}] for [{string.Join(", ", order)}]: "
            + "each must be there, in that order.");
        Assert.Contains("where the two disagree, the preference wins", skill, StringComparison.Ordinal);
        Assert.Contains("the later winning where two say different things", skill, StringComparison.Ordinal);

        // Where each file is: from what the runtime records, and the root as the runtime reports it, never from a setting
        // the session has no tool to read.
        Assert.Contains("`folder_path`", skill, StringComparison.Ordinal);
        Assert.Contains("`jason runtime status` reports as the root in force", skill, StringComparison.Ordinal);
        Assert.DoesNotContain("`Harness:Root`", skill, StringComparison.Ordinal);
        Assert.Contains("only where it carries its marker", skill, StringComparison.Ordinal);
        var printed = SkillPack.PrintedCommands(SkillPack.Find("managed-campaign-work").File);
        foreach (var line in new[] { "jason preference list", "jason project get prj_01JB6K8TQ2W9V4MZ0C3Y7H5NRD" })
        {
            Assert.Contains(line, printed);
        }

        // And why a session reads them itself: what was measured of Claude Code, and what Codex's documentation says,
        // said as what a session can act on.
        Assert.Contains("Claude Code in print mode", skill, StringComparison.Ordinal);
        Assert.Contains("Codex, by its documentation and not by a measurement", skill, StringComparison.Ordinal);
        Assert.Contains("read by path every one of the three you were not given", skill, StringComparison.Ordinal);
    }

    /// <summary>
    /// A role's notes are kept at three levels, and a session that reads one reads the three, the client's first and the
    /// campaign's last. One that still said a role keeps one note per campaign would read a role's craft as missing.
    /// </summary>
    [Fact]
    [Critical(Family.D4, "a skill a person's session reads teaches what was not decided")]
    public void The_managed_path_reads_a_role_s_notes_at_three_levels()
    {
        var skill = Flattened("managed-campaign-work");
        Assert.DoesNotContain("one note per campaign", skill, StringComparison.Ordinal);

        var printed = SkillPack.PrintedCommands(SkillPack.Find("managed-campaign-work").File);
        foreach (var line in new[]
        {
            "jason rolenote get --role researcher",
            "jason rolenote get --role researcher --project prj_01JB6K8TQ2W9V4MZ0C3Y7H5NRD",
            "jason rolenote get --role researcher --campaign cmp_01JB6K8TQ2W9V4MZ0C3Y7H5NRD",
        })
        {
            Assert.Contains(line, printed);
        }
    }

    /// <summary>
    /// A host loads a skill by its description. A description that named only turning an objective into work left a
    /// person's first question about the work — what do we do first — loading no skill of Jason's, and the session
    /// answered from whatever it already had. The description names that question too, beside the one it always named.
    /// </summary>
    [Fact]
    [Critical(Family.D4, "a skill a person's session reads teaches what was not decided")]
    public void The_managed_path_s_description_names_the_first_question()
    {
        var description = SkillFrontMatter.Read(SkillPack.Find("managed-campaign-work").File).Top.GetValueOrDefault("description") ?? string.Empty;

        Assert.StartsWith(
            "Use when a person wants Jason to start outbound work for them, such as getting meetings or running a campaign, and asks what to do first, and when an interactive agent session should turn an outbound objective into durable campaign work that Jason performs",
            description,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// The inbox writes and the provider's pause are taught by naming their documents, which say what each reaches, whether
    /// it can be undone, what it needs approved and what its input holds. A name that points at no document sends a session
    /// looking for a rule nobody wrote, and a document that is not named leaves the session to guess the input.
    /// </summary>
    [Fact]
    [Critical(Family.D4, "a skill a person's session reads teaches what was not decided")]
    public void The_operation_documents_the_managed_path_names_exist()
    {
        var skill = Flattened("managed-campaign-work");
        var named = OperationDocument().Matches(skill).Select(match => match.Groups[1].Value).Distinct(StringComparer.Ordinal).ToList();

        foreach (var operation in new[] { "conversation.get", "conversation.reply", "conversation.classify", "conversation.close", "conversation.reopen", "campaign.pause" })
        {
            Assert.True(
                named.Contains(operation, StringComparer.Ordinal),
                $"managed-campaign-work names the documents [{string.Join(", ", named)}], and not `docs/contracts/operations/{operation}.md`.");
        }

        foreach (var operation in named)
        {
            var page = Path.Combine(SkillPack.RepositoryRoot(), "docs", "contracts", "operations", operation + ".md");
            Assert.True(
                File.Exists(page) && OperationCatalog.Knows(operation),
                $"managed-campaign-work names `docs/contracts/operations/{operation}.md`, and no operation of that name is published.");
        }
    }

    /// <summary>
    /// The guard over printed commands asks only whether a line parses; the arguments of a provider operation are judged
    /// later, at <c>workitem.create</c>, against the operation's own document. So each <c>--input</c> the skill prints is
    /// judged here against that document, and an operation that needs the work item's person names one: a session copies
    /// these lines, and a line refused at the create teaches the session that the managed path does not work. The arguments
    /// are the line's <c>--input</c>, or the <c>context.input</c> of the request its here-document hands the program.
    /// </summary>
    [Fact]
    [Critical(Family.D4, "a skill a person's session reads teaches what was not decided")]
    public void Every_provider_operation_the_managed_path_prints_carries_arguments_its_document_accepts()
    {
        var printed = SkillPack.Printed(SkillPack.Find("managed-campaign-work").File)
            .Where(command => command.Line.Contains(" --kind provider_op ", StringComparison.Ordinal))
            .ToList();

        var operations = new List<string>();
        foreach (var command in printed)
        {
            var line = command.Line;
            var words = ShellWords.Split(line);
            var operation = ValueOf(words, "--operation") ?? string.Empty;
            var contract = OperationCatalog.Find(operation);
            Assert.True(contract is not null, $"managed-campaign-work prints a work item for '{operation}', which is no published operation: {line}");

            var input = ValueOf(words, "--input") is { } inline
                ? JsonNode.Parse(inline)
                : command.Body is { } body ? JsonNode.Parse(body)?["context"]?["input"]?.DeepClone() : null;
            Assert.True(input is not null, $"managed-campaign-work prints a work item for {operation} with no arguments, on the line or in its body: {line}");
            var problems = SchemaValidator.Verdicts(SchemaValidator.Validate(input, contract.ArgumentSchema));
            Assert.True(
                problems.Count == 0,
                $"The --input managed-campaign-work prints for {operation} is refused by its document: "
                + $"{string.Join("; ", problems.Select(problem => $"{problem.Pointer} {problem.Message}"))}. The line: {line}");

            Assert.True(
                contract.Preflight.Contact != ContactRequirement.Required || ValueOf(words, "--contact") is not null,
                $"{operation} needs the person a work item is about, and the line managed-campaign-work prints for it names none: {line}");
            operations.Add(operation);
        }

        foreach (var operation in new[] { "conversation.get", "conversation.reply", "conversation.classify", "conversation.close", "conversation.reopen", "campaign.pause" })
        {
            Assert.True(
                operations.Contains(operation, StringComparer.Ordinal),
                $"managed-campaign-work prints work items for [{string.Join(", ", operations)}], and none for {operation}.");
        }
    }

    /// <summary>
    /// A reply reaches a person, so it is decided one send at a time and goes where the person wrote: a session that put two
    /// texts behind one approval, or answered with a message outside the thread, would send something nobody approved as
    /// such, to an inbox the person never wrote from. The decision itself is the approvals skill's, named rather than copied.
    /// </summary>
    [Fact]
    [Critical(Family.D4, "a skill a person's session reads teaches what was not decided")]
    public void A_reply_is_one_approval_per_send_in_the_person_s_own_thread()
    {
        var section = Whitespace().Replace(Section("Answering a conversation"), " ");

        Assert.Contains("**A reply goes into the thread the person wrote in, and each send is approved on its own.**", section, StringComparison.Ordinal);
        Assert.Contains("never a new thread", section, StringComparison.Ordinal);
        Assert.Contains("one approval is one send: one work item is one reply to one person", section, StringComparison.Ordinal);
        Assert.Contains("Two replies are two work items and two approvals, never one item for both.", section, StringComparison.Ordinal);
        Assert.Contains("`approvals-and-questions`", section, StringComparison.Ordinal);
    }

    /// <summary>
    /// A reply the runtime noticed is a line that says a thread moved and nothing of what was written in it. A session that
    /// drafted an answer from the line alone would answer a message it never read — or a follow-up of its own. So the line,
    /// then the thread, then the reply, in that order, and what was read is said to the person before anything is drafted.
    /// </summary>
    [Fact]
    [Critical(Family.D4, "a skill a person's session reads teaches what was not decided")]
    public void A_reply_the_runtime_saw_is_read_and_said_before_it_is_answered()
    {
        var raw = Section("Answering a conversation");
        Assert.Contains(
            "So when the person asks you to answer one, read the thread first and tell them what you read — who wrote last, when, and what they said — before anything is drafted.",
            Whitespace().Replace(raw, " "),
            StringComparison.Ordinal);

        var printed = SkillPack.PrintedCommands(raw.Split('\n')).ToList();
        var seen = printed.FindIndex(line => line.StartsWith("jason journal list --campaign cmp_", StringComparison.Ordinal) && line.Contains(" --kind inbound_reply_observed", StringComparison.Ordinal));
        var read = printed.FindIndex(line => line.StartsWith("jason contact thread cnt_", StringComparison.Ordinal));
        var reply = printed.FindIndex(line => line.Contains(" --operation conversation.reply ", StringComparison.Ordinal));
        Assert.True(
            seen >= 0 && read > seen && reply > read,
            $"Under \"Answering a conversation\" the noticed reply is listed at {seen}, its thread read at {read} and the reply created at {reply} of: "
            + $"{string.Join(" | ", printed)}. The line, then the thread, then the reply.");
    }

    /// <summary>
    /// An operation's approval class is its document's, and a second copy in a skill drifts from it the day the document
    /// changes. The skill says which writes reach a person and therefore park, and spells one class only: the provider's
    /// pause, whose not waiting for anyone is what a person stopping a send has to know — and that one is read here from
    /// the document, so the skill cannot keep a class the document no longer gives.
    /// </summary>
    [Fact]
    [Critical(Family.D4, "a skill a person's session reads teaches what was not decided")]
    public void The_managed_path_restates_no_approval_class_but_the_provider_pause_s_from_its_document()
    {
        var skill = Flattened("managed-campaign-work");
        var pause = OperationCatalog.Find("campaign.pause")!.Approval.Value;

        Assert.Contains($"its document, `docs/contracts/operations/campaign.pause.md`, gives it `{pause}` approval", skill, StringComparison.Ordinal);

        var spelled = OperationContract.ApprovalWords
            .SelectMany(word => Regex.Matches(skill, Regex.Escape($"`{word}`")).Select(_ => word))
            .ToList();
        Assert.True(
            spelled.Count == 1,
            $"managed-campaign-work spells an approval class {spelled.Count} times ([{string.Join(", ", spelled)}]); it names the "
            + "provider's pause's alone, from its document, and every other is the operation document's to state.");
    }

    /// <summary>
    /// "Stop it" and "not yet" are four different acts, and a session that reached for the wrong one would believe a thing
    /// stopped that did not: a cancelled item takes nothing back at the provider, a held one still runs later, Jason's pause
    /// leaves the provider's sequence sending, and the provider's pause is an act at the provider. The two pauses share a
    /// name, so one sentence tells them apart in terms a person can act on; and when the wrong copy already went out, the
    /// provider is stopped first.
    /// </summary>
    [Fact]
    [Critical(Family.D4, "a skill a person's session reads teaches what was not decided")]
    public void Stopping_or_holding_work_names_four_acts_and_what_each_does_not_stop()
    {
        var raw = Section("Stopping or holding work");
        var section = Whitespace().Replace(raw, " ");

        Assert.Contains(
            "`jason campaign pause` stops Jason's own work and leaves the provider sending; only a work item naming the operation `campaign.pause` stops the provider's campaign, so when the provider has to stop, that work item is the one to create.",
            section,
            StringComparison.Ordinal);

        foreach (var fact in new[]
        {
            "**`jason workitem cancel` ends one work item for good.**",
            "It touches no provider: what an attempt already did there stays done",
            "**`jason workitem update` holds or reorders one work item without ending it.**",
            "Neither stops an attempt already running",
            "**`jason campaign pause` pauses Jason's campaign.**",
            "It does not stop the provider's sequence",
            "**The provider operation `campaign.pause` pauses the provider's campaign**",
            "It is an act at the provider",
            "**When the wrong copy already went out**, stop the provider first",
            "It runs even while Jason's campaign is paused.",
            "tell the person the provider has stopped only once that work item has succeeded",
            "so it works for a campaign linked there, whose pin names it, and fails at the claim for one that is not",
        })
        {
            Assert.True(section.Contains(fact, StringComparison.Ordinal), $"\"Stopping or holding work\" no longer says: {fact}");
        }

        var pause = OperationCatalog.Find("campaign.pause")!.Dispatch.InPausedCampaign;
        Assert.True(
            pause == InPausedCampaign.Runs,
            $"\"Stopping or holding work\" says the provider's pause runs while Jason's campaign is paused, and its document says {pause}.");

        var printed = SkillPack.PrintedCommands(raw.Split('\n')).ToList();
        var provider = printed.FindIndex(line => line.Contains(" --operation campaign.pause ", StringComparison.Ordinal));
        var jason = printed.FindIndex(line => line.StartsWith("jason campaign pause cmp_", StringComparison.Ordinal));
        var acts = new[]
        {
            printed.FindIndex(line => line.StartsWith("jason workitem cancel wi_", StringComparison.Ordinal)),
            printed.FindIndex(line => line.StartsWith("jason workitem update wi_", StringComparison.Ordinal) && line.Contains(" --not-before ", StringComparison.Ordinal)),
            printed.FindIndex(line => line.StartsWith("jason workitem update wi_", StringComparison.Ordinal) && line.Contains(" --priority ", StringComparison.Ordinal)),
            printed.FindIndex(line => line.StartsWith("jason campaign start cmp_", StringComparison.Ordinal)),
        };
        Assert.True(
            provider >= 0 && jason > provider && acts.All(index => index >= 0),
            $"\"Stopping or holding work\" prints the provider's pause at {provider}, Jason's at {jason}, and the cancel, the two holds "
            + $"and the resume at [{string.Join(", ", acts)}] of: {string.Join(" | ", printed)}. Each is printed, the provider's pause first.");
    }

    /// <summary>
    /// Closing and reopening a thread move the thread and nothing else, and filing one need not: a provider may give a
    /// category it reserves a meaning of its own, and filing there asks for that meaning too. A session told that filing
    /// reaches nobody would file under such a category with nobody asked. So the skill says so, sends the reader to the
    /// section of the classify document that says what that meaning can be, by the heading the document has, and says the
    /// example's category promises nothing.
    /// </summary>
    [Fact]
    [Critical(Family.D4, "a skill a person's session reads teaches what was not decided")]
    public void Filing_a_thread_may_carry_the_provider_s_own_meaning_and_closing_or_reopening_reaches_no_person()
    {
        var section = Whitespace().Replace(Section("Answering a conversation"), " ");
        const string Reserved = "The provider's own meaning on its reserved names";

        var classify = File.ReadAllText(Path.Combine(SkillPack.RepositoryRoot(), "docs", "contracts", "operations", "conversation.classify.md"))
            .Replace("\r\n", "\n", StringComparison.Ordinal);
        Assert.True(
            classify.Contains($"\n## {Reserved}\n", StringComparison.Ordinal),
            $"managed-campaign-work sends the reader to the section \"{Reserved}\" of conversation.classify.md, which has no such heading.");

        Assert.False(
            section.Contains("Filing, closing and reopening a thread reach no person", StringComparison.Ordinal),
            "\"Answering a conversation\" still says filing a thread reaches no person.");
        foreach (var fact in new[]
        {
            "**Closing and reopening a thread reach no person.**",
            "**Filing it may reach one.**",
            $"its document's section \"{Reserved}\" says what that can be",
            "a campaign that must own every send files under categories of its own",
            "The `Interested` below is only an example",
        })
        {
            Assert.True(section.Contains(fact, StringComparison.Ordinal), $"\"Answering a conversation\" does not say: {fact}");
        }
    }

    /// <summary>
    /// A noticed reply is not always a line that names its person: <c>contact_id</c> is null where no contact, or more
    /// than one, is pinned with the thread's identifier, and a reply the provider's events reported is a line of another
    /// kind. A session that knew one kind and one read would miss the other and stop where there is no person to name. So
    /// both kinds are listed, a line with no person is read by its thread in its own campaign, the reply's person is found
    /// before the reply, and the reads on request say what they refuse and what they take.
    /// </summary>
    [Fact]
    [Critical(Family.D4, "a skill a person's session reads teaches what was not decided")]
    public void A_line_that_names_no_person_is_read_by_its_thread_and_answered_in_its_campaign()
    {
        var raw = Section("Answering a conversation");
        var section = Whitespace().Replace(raw, " ");

        foreach (var fact in new[]
        {
            "or a `provider_event_observed` line of kind `reply_detected`",
            "**A line that names no person is read by its thread.**",
            "Its `contact_id` is null when no contact, or more than one, is pinned at that plugin",
            "Read it with a `conversation.get` work item in the line's campaign, which needs only the thread.",
            "find them with `jason contact list --query`, or have the person name them, and create the reply in that same campaign",
            "refuse with `no_route` when no current route reaches the plugin that learned the person's identifier",
            "take `--plugin` for a person several providers know",
        })
        {
            Assert.True(section.Contains(fact, StringComparison.Ordinal), $"\"Answering a conversation\" does not say: {fact}");
        }

        var printed = SkillPack.PrintedCommands(raw.Split('\n')).ToList();
        var seen = printed.Find(line => line.StartsWith("jason journal list --campaign cmp_", StringComparison.Ordinal));
        Assert.True(
            seen is not null
            && seen.Contains(" --kind inbound_reply_observed", StringComparison.Ordinal)
            && seen.Contains(" --kind provider_event_observed", StringComparison.Ordinal),
            $"Under \"Answering a conversation\" the noticed replies are listed by '{seen}', which does not ask for both kinds.");

        var read = printed.Find(line => line.Contains(" --operation conversation.get ", StringComparison.Ordinal));
        Assert.True(
            read is not null && read.StartsWith("jason workitem create cmp_", StringComparison.Ordinal) && !read.Contains(" --contact ", StringComparison.Ordinal),
            $"Under \"Answering a conversation\" the thread of a line with no person is read by '{read}', not by a work item in the campaign that names only the thread.");
    }

    /// <summary>
    /// A campaign runs one work item at a time, so while the wrong copy is being sent, the provider's pause waits behind
    /// it whatever its priority. A session that paused first and cancelled last would watch the copy finish before the
    /// pause was claimed. So the running item is cancelled first, and the skill says what that cancel cannot tell — whether
    /// the provider already had it — and that a reply cannot be taken back.
    /// </summary>
    [Fact]
    [Critical(Family.D4, "a skill a person's session reads teaches what was not decided")]
    public void The_wrong_copy_is_stopped_by_freeing_the_campaign_before_the_provider_s_pause()
    {
        var raw = Section("Stopping or holding work");
        var section = Whitespace().Replace(raw, " ");

        foreach (var fact in new[]
        {
            "while one of its items is scheduled or running, nothing more of it is claimed, the provider's pause included, whatever its `--priority`.",
            "So cancel the item that is running or scheduled to send the copy, then create the provider's `campaign.pause` with a high `--priority`, then pause Jason's campaign",
            "whether it reached the provider before it was stopped",
            "a reply that went out cannot be taken back",
        })
        {
            Assert.True(section.Contains(fact, StringComparison.Ordinal), $"\"Stopping or holding work\" does not say: {fact}");
        }

        var printed = SkillPack.PrintedCommands(raw.Split('\n')).ToList();
        var cancel = printed.FindIndex(line => line.StartsWith("jason workitem cancel wi_", StringComparison.Ordinal));
        var provider = printed.FindIndex(line => line.Contains(" --operation campaign.pause ", StringComparison.Ordinal));
        var jason = printed.FindIndex(line => line.StartsWith("jason campaign pause cmp_", StringComparison.Ordinal));
        Assert.True(
            cancel >= 0 && provider > cancel && jason > provider,
            $"\"Stopping or holding work\" prints the cancel at {cancel}, the provider's pause at {provider} and Jason's at {jason} of: "
            + $"{string.Join(" | ", printed)}. The cancel, then the provider's pause, then Jason's.");
    }

    /// <summary>
    /// The four conversation writes wait while their campaign is paused, and a paused campaign's threads are not read. A
    /// session that paused Jason's campaign and then approved an answer would wait for a send that never runs. The claim is
    /// held against the four documents, so a document that let one of them run would turn this red.
    /// </summary>
    [Fact]
    [Critical(Family.D4, "a skill a person's session reads teaches what was not decided")]
    public void Answering_a_conversation_needs_an_active_campaign()
    {
        var section = Whitespace().Replace(Section("Answering a conversation"), " ");

        foreach (var fact in new[]
        {
            "**The campaign has to be active** for an answer to run and for Jason's read of the campaign's threads to notice a reply",
            "a paused campaign's threads are not read",
            "After a pause, `jason campaign start` comes first.",
        })
        {
            Assert.True(section.Contains(fact, StringComparison.Ordinal), $"\"Answering a conversation\" does not say: {fact}");
        }

        foreach (var operation in new[] { "conversation.reply", "conversation.classify", "conversation.close", "conversation.reopen" })
        {
            var dispatch = OperationCatalog.Find(operation)!.Dispatch.InPausedCampaign;
            Assert.True(
                dispatch == InPausedCampaign.Waits,
                $"\"Answering a conversation\" says an answer needs an active campaign, and {operation}'s document says it {dispatch} in a paused one.");
        }
    }

    /// <summary>
    /// A host loads a skill by its description, and a person who asks to answer a reply, or to stop what is going out,
    /// says neither of the words the description was first written with. It names both.
    /// </summary>
    [Fact]
    [Critical(Family.D4, "a skill a person's session reads teaches what was not decided")]
    public void The_managed_path_s_description_names_answering_and_stopping_work()
    {
        var description = SkillFrontMatter.Read(SkillPack.Find("managed-campaign-work").File).Top.GetValueOrDefault("description") ?? string.Empty;

        foreach (var words in new[] { "answering a conversation", "stopping or holding work" })
        {
            Assert.True(
                description.Contains(words, StringComparison.Ordinal),
                $"managed-campaign-work's description does not say '{words}': {description}");
        }
    }

    /// <summary>One section of managed-campaign-work, from its heading to the next.</summary>
    private static string Section(string heading)
    {
        var text = File.ReadAllText(SkillPack.Find("managed-campaign-work").File).Replace("\r\n", "\n", StringComparison.Ordinal);
        var from = text.IndexOf($"\n## {heading}\n", StringComparison.Ordinal);
        var to = from < 0 ? -1 : text.IndexOf("\n## ", from + 1, StringComparison.Ordinal);
        Assert.True(from >= 0 && to > from, $"managed-campaign-work has no section \"## {heading}\" followed by another.");
        return text[from..to];
    }

    /// <summary>
    /// The wiring check's section on a direct send prints a work item for <c>message.send</c>. The guard over printed commands
    /// asks only whether a line parses; the arguments are judged at <c>workitem.create</c>, against the operation's own
    /// document. So the page's <c>--input</c> is judged here against that document, and the page cannot drift from it.
    /// </summary>
    [Fact]
    [Critical(Family.D4, "a skill a person's session reads teaches what was not decided")]
    public void The_input_the_wiring_check_prints_for_a_direct_send_is_accepted_by_the_operations_document()
    {
        var page = Path.Combine(SkillPack.RepositoryRoot(), "docs", "golden-path.md");
        var line = Assert.Single(
            SkillPack.PrintedCommands(page),
            printed => printed.StartsWith("jason workitem create ", StringComparison.Ordinal)
                && printed.Contains("--operation message.send ", StringComparison.Ordinal));

        var words = ShellWords.Split(line);
        var input = ValueOf(words, "--input");
        Assert.True(input is not null, $"docs/golden-path.md prints a message.send work item with no --input: {line}");
        Assert.True(ValueOf(words, "--idempotency-key") is not null, $"docs/golden-path.md prints a message.send work item with no key: {line}");
        Assert.True(ValueOf(words, "--contact") is not null, $"message.send needs the person a work item is about, and the page's line names none: {line}");

        var contract = OperationCatalog.Find("message.send");
        Assert.True(contract is not null, "message.send is no published operation.");
        var problems = SchemaValidator.Verdicts(SchemaValidator.Validate(JsonNode.Parse(input), contract.ArgumentSchema));
        Assert.True(
            problems.Count == 0,
            "The --input docs/golden-path.md prints for message.send is refused by its document: "
            + $"{string.Join("; ", problems.Select(problem => $"{problem.Pointer} {problem.Message}"))}. The line: {line}");
        Assert.Equal("email", JsonNode.Parse(input)!["channel"]!.GetValue<string>());
    }

    /// <summary>The word after an option on a split command line, or null where the line does not give the option.</summary>
    private static string? ValueOf(IReadOnlyList<string> words, string option)
    {
        for (var index = 0; index < words.Count - 1; index++)
        {
            if (string.Equals(words[index], option, StringComparison.Ordinal))
            {
                return words[index + 1];
            }
        }

        return null;
    }

    /// <summary>
    /// A person who asks what a waiting item is doing is answered from the item's own <c>wait</c> block, field by field, and
    /// a person who wants it to go on sooner ends the wait on their own word, which the line records as theirs.
    /// </summary>
    [Fact]
    [Critical(Family.D4, "a skill a person's session reads teaches what was not decided")]
    public void The_managed_path_says_what_waiting_means_and_reads_the_wait_block_to_the_person()
    {
        var skill = Flattened("managed-campaign-work");

        Assert.Contains("**An item that is `waiting` is neither stuck nor finished.**", skill, StringComparison.Ordinal);
        Assert.Contains("While it waits it holds no slot, and the work it waits for runs.", skill, StringComparison.Ordinal);
        foreach (var field in new[] { "`on`", "`mode`", "`until`", "`deadline`", "`opened_at`" })
        {
            Assert.True(
                skill.Contains($"{field} is", StringComparison.Ordinal),
                $"The managed path does not say what the wait block's {field} is.");
        }

        Assert.Contains("read them its `wait` block from `workitem get`", skill, StringComparison.Ordinal);
        Assert.Contains("and null for a wait on a moment alone", skill, StringComparison.Ordinal);
        Assert.Contains("A `workitem list` carries the block too, with `on_count` and `ended_count`", skill, StringComparison.Ordinal);
        Assert.Contains("Only a waiting item can be resumed", skill, StringComparison.Ordinal);

        var resumes = SkillPack.PrintedCommands(File.ReadAllLines(SkillPack.Find("managed-campaign-work").File))
            .Where(line => line.Contains(" workitem resume ", StringComparison.Ordinal))
            .ToList();
        Assert.True(
            resumes.Count == 1
                && resumes[0].StartsWith("jason workitem resume wi_", StringComparison.Ordinal)
                && Regex.IsMatch(resumes[0], @" --actor human:\S+ --reason '[^']*'$"),
            $"The managed path prints [{string.Join(" | ", resumes)}] to end a wait; it prints one, naming the person, its "
            + "options at the end of the command, where a host's rule for the verb still matches.");
    }

    [GeneratedRegex(@"`docs/contracts/operations/([a-z_]+\.[a-z_]+)\.md`")]
    private static partial Regex OperationDocument();

    /// <summary>
    /// "What is blocked" is answered where a waiting item is explained: a parked item is a waiting one, and the block, not
    /// the session, releases it.
    /// </summary>
    [Fact]
    [Critical(Family.D4, "a skill a person's session reads teaches what was not decided")]
    public void The_managed_campaign_work_skill_says_what_is_blocked_where_waiting_is_explained()
    {
        var skill = Flattened("managed-campaign-work");

        var waiting = skill.IndexOf("**An item that is `waiting` is neither stuck nor finished.**", StringComparison.Ordinal);
        var blocked = skill.IndexOf("**What is blocked.**", StringComparison.Ordinal);
        var resumable = skill.IndexOf("Only a waiting item can be resumed", StringComparison.Ordinal);
        Assert.True(
            waiting >= 0 && blocked > waiting && resumable > blocked,
            $"The waiting item opens at {waiting}, what is blocked at {blocked} and the resume rule at {resumable}: what is blocked belongs between them.");
        Assert.Contains(
            "When the person asks what is blocked, `jason block list` shows each open block, the `held_count` of items it holds and the `link` where they clear it at the provider",
            skill,
            StringComparison.Ordinal);
        Assert.Contains("The block releases its items by itself, so do not resume, cancel or create them again.", skill, StringComparison.Ordinal);
    }

    /// <summary>
    /// The managed path names what a failed item's code can say, so a provider that does not offer something yet is among
    /// them: it is neither the caller's arguments nor an unreachable provider.
    /// </summary>
    [Fact]
    [Critical(Family.D4, "a skill a person's session reads teaches what was not decided")]
    public void The_managed_campaign_work_skill_names_a_provider_that_does_not_offer_what_was_asked_among_the_codes()
    {
        var skill = Flattened("managed-campaign-work");

        Assert.Contains(
            "your arguments, a provider that was unreachable, a provider that does not offer what you asked for yet, or a person who has not answered yet",
            skill,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// What the campaign is for is its goals, the one reserved key. The paragraph sits with the context's other
    /// paragraphs, between the change of direction and the keys, names the key the runtime reserves and says what a
    /// campaign with none does, so a session that wrote the person's aim under a key of its own, or told them a campaign
    /// without goals is refused, is corrected here.
    /// </summary>
    [Fact]
    [Critical(Family.D4, "a skill a person's session reads teaches what was not decided")]
    public void What_the_campaign_is_for_is_its_goals_the_one_reserved_key()
    {
        var text = File.ReadAllText(SkillPack.Find("managed-campaign-work").File).Replace("\r\n", "\n", StringComparison.Ordinal);
        var goals = text.IndexOf("**What the campaign is for is its `goals`.**", StringComparison.Ordinal);
        var change = text.IndexOf("**A change of direction goes into the context as well.**", StringComparison.Ordinal);
        var keys = text.IndexOf("**Give every create a key you could send again.**", StringComparison.Ordinal);
        Assert.True(
            change >= 0 && goals > change && keys > goals,
            $"The paragraph on goals is at offset {goals}; it belongs after the change of direction ({change}) and before the keys ({keys}).");

        var end = text.IndexOf("\n\n", goals, StringComparison.Ordinal);
        var paragraph = Whitespace().Replace(text[goals..end], " ");
        Assert.Contains("are the one reserved key of the context", paragraph, StringComparison.Ordinal);
        Assert.Contains("a patch replaces the list whole, so send every goal the campaign keeps", paragraph, StringComparison.Ordinal);
        Assert.Contains("A campaign with no goals is valid, and its roles take the default goal, a demo.", paragraph, StringComparison.Ordinal);
        Assert.Contains("`docs/contracts/context/goals.md`", paragraph, StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Combine(SkillPack.RepositoryRoot(), "docs", "contracts", "context", "goals.md")));
    }

    /// <summary>
    /// A reply the runtime noticed wakes the responder before anybody reads it, so the session that is asked to answer
    /// one looks at what the responder left first and drafts no second answer. The line it looks for is the kind the
    /// runtime writes, and the filter the printed list takes is the role's own name.
    /// </summary>
    [Fact]
    [Critical(Family.D4, "a skill a person's session reads teaches what was not decided")]
    public void A_session_asked_to_answer_a_reply_looks_at_what_the_responder_left_first()
    {
        var text = File.ReadAllText(SkillPack.Find("managed-campaign-work").File).Replace("\r\n", "\n", StringComparison.Ordinal);
        var skill = Flattened("managed-campaign-work");

        var responder = text.IndexOf("**A reply usually wakes the responder first.**", StringComparison.Ordinal);
        var read = text.IndexOf("**Read the thread before you act on it.**", StringComparison.Ordinal);
        Assert.True(responder >= 0 && read > responder, $"The responder paragraph is at {responder} and the one on reading the thread at {read}.");

        Assert.Contains("records one verdict as a `reply_classified` line, and acts, drafts an answer or asks a person", skill, StringComparison.Ordinal);
        Assert.Contains("never write a second draft for a reply it has already answered", skill, StringComparison.Ordinal);
        Assert.Contains("jason workitem list --campaign cmp_01JB6K8TQ2W9V4MZ0C3Y7H5NRD --kind ai_role --role responder", SkillPack.PrintedCommands(SkillPack.Find("managed-campaign-work").File));
        Assert.Equal("reply_classified", Jason.Runtime.Journal.JournalKinds.ReplyClassified);
    }

    /// <summary>
    /// "Each send is approved on its own" keeps its words, and the exception is stated beside it: a person's standing
    /// approval, never a session's, and the two lines that show what it let go. The kinds and the verbs are the runtime's.
    /// </summary>
    [Fact]
    [Critical(Family.D4, "a skill a person's session reads teaches what was not decided")]
    public void A_standing_approval_is_a_person_s_and_the_chronicle_shows_what_it_let_go()
    {
        var skill = Flattened("managed-campaign-work");

        Assert.Contains("**A person may have decided it in advance, for one kind of reply.**", skill, StringComparison.Ordinal);
        Assert.Contains("You do not set one: it is theirs, in `approvals-and-questions`, and an agent sets one only on a person's explicit word, naming them.", skill, StringComparison.Ordinal);

        var printed = SkillPack.PrintedCommands(SkillPack.Find("managed-campaign-work").File);
        Assert.Contains("jason approval list-standing cmp_01JB6K8TQ2W9V4MZ0C3Y7H5NRD", printed);
        Assert.Contains("jason journal list --campaign cmp_01JB6K8TQ2W9V4MZ0C3Y7H5NRD --kind approval_standing_applied", printed);
        Assert.Equal("approval_standing_applied", Jason.Runtime.Journal.JournalKinds.ApprovalStandingApplied);
    }

    /// <summary>
    /// Asked whether somebody answered, an agent reads the provider now and says what it shows, instead of answering
    /// "nothing yet" and waiting for the runtime's own read. The two cadences it names are the options' real defaults.
    /// </summary>
    [Fact]
    [Critical(Family.D4, "a skill a person's session reads teaches what was not decided")]
    public void Asked_whether_somebody_answered_the_agent_reads_now_and_names_the_real_cadences()
    {
        foreach (var pack in new[] { "managed-campaign-work", "inbox-triage" })
        {
            var skill = pack == "inbox-triage"
                ? string.Join(' ', File.ReadAllText(Path.Combine(SkillPack.BusinessRoot(), pack, "SKILL.md")).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
                : Flattened(pack);

            Assert.Contains("such a read records nothing", skill, StringComparison.Ordinal);
            Assert.DoesNotContain("Jason has not noticed that reply", skill, StringComparison.Ordinal);
            Assert.Contains("Whether Jason has noticed the reply is in the lines below", skill, StringComparison.Ordinal);
            Assert.Contains("only then wakes its responder", skill, StringComparison.Ordinal);
            Assert.Contains("`Dispatcher:EventsCadenceSeconds`", skill, StringComparison.Ordinal);
            Assert.Contains("`Inbound:SyncSeconds`", skill, StringComparison.Ordinal);
            Assert.Contains("600", skill, StringComparison.Ordinal);
            Assert.Contains("900", skill, StringComparison.Ordinal);
            Assert.DoesNotContain("events signal", skill, StringComparison.Ordinal);

            var printed = pack == "inbox-triage"
                ? SkillPack.PrintedCommands(Path.Combine(SkillPack.BusinessRoot(), pack, "SKILL.md"))
                : SkillPack.PrintedCommands(SkillPack.Find(pack).File);
            Assert.Contains("jason contact threads cnt_01JB6K8TQ2W9V4MZ0C3Y7H5NRD", printed);
            Assert.Contains("jason contact thread cnt_01JB6K8TQ2W9V4MZ0C3Y7H5NRD t_99812", printed);
            Assert.Contains("jason journal list --campaign cmp_01JB6K8TQ2W9V4MZ0C3Y7H5NRD --kind inbound_reply_observed --kind provider_event_observed", printed);
            Assert.Contains("jason workitem list --campaign cmp_01JB6K8TQ2W9V4MZ0C3Y7H5NRD --kind ai_role --role responder", printed);
        }

        Assert.Equal(600, new Jason.Runtime.Configuration.DispatcherOptions().EventsCadenceSeconds);
        Assert.Equal(900, new Jason.Runtime.Configuration.InboundOptions().SyncSeconds);
    }

    /// <summary>The section that leads a campaign's set-up and its start, by its heading.</summary>
    private const string SetUp = "Setting a campaign up, and starting it";

    /// <summary>
    /// A host loads a skill by its description, and a person who asks to have a campaign set up, or to fill in the draft
    /// a click already made, says neither of the words the description was first written with. It names the set-up and
    /// the start, the draft filled in, and, last, the business pack's two skills as the set-up's method, so a host that
    /// matched the business pack's launch skill on "start a campaign" meets this one too.
    /// </summary>
    [Fact]
    [Critical(Family.D4, "a skill a person's session reads teaches what was not decided")]
    public void The_managed_path_s_description_names_the_start_and_its_method()
    {
        var description = SkillFrontMatter.Read(SkillPack.Find("managed-campaign-work").File).Top.GetValueOrDefault("description") ?? string.Empty;

        Assert.Contains("when a person wants a campaign set up and started, a new one or a draft campaign filled in", description, StringComparison.Ordinal);
        Assert.Contains("started on their yes, after which Jason's roles write and send", description, StringComparison.Ordinal);
        Assert.EndsWith("Its set-up and start use `campaign-launch` and `audience-building` as their method.", description, StringComparison.Ordinal);
    }

    /// <summary>The catalog says the managed path sets a campaign up and starts it, and names the set-up's method.</summary>
    [Fact]
    [Critical(Family.D4, "a skill a person's session reads teaches what was not decided")]
    public void The_catalog_says_the_managed_path_sets_a_campaign_up_with_its_method()
    {
        var catalog = Whitespace().Replace(File.ReadAllText(SkillPack.Catalog()), " ");
        var start = catalog.IndexOf("- [`managed-campaign-work`]", StringComparison.Ordinal);
        Assert.True(start >= 0, "The catalog has no line for 'managed-campaign-work'.");
        var end = catalog.IndexOf(" - [`", start + 1, StringComparison.Ordinal);
        var line = end > start ? catalog[start..end] : catalog[start..];

        Assert.Contains("a campaign set up with the person and started on their yes, with `campaign-launch` and `audience-building` as its method", line, StringComparison.Ordinal);
        Assert.Contains("its first batch of twenty", line, StringComparison.Ordinal);
    }

    /// <summary>
    /// The set-up's method is the business pack's, named once: what must hold before a campaign reaches anybody, and who
    /// belongs on it. A rule copied from either drifts from it the day that skill changes, so the section's own prose
    /// shares no run of eight words with them.
    /// </summary>
    [Fact]
    [Critical(Family.D4, "a skill a person's session reads teaches what was not decided")]
    public void The_start_names_its_method_and_copies_no_rule()
    {
        var section = Section(SetUp);
        Assert.Contains(
            "The set-up's method is the business pack's: `campaign-launch` for what must hold before a campaign reaches anybody, and `audience-building` for who belongs on it and where they come from. Read them for that judgement; what Jason performs, in which order, and what it leaves to its roles after the start is said here, and where the two differ, this section holds.",
            Whitespace().Replace(section, " "),
            StringComparison.Ordinal);

        var own = Runs(ProseOf(section.Split('\n')));
        Assert.True(own.Count > 50, $"The set-up's own prose gave {own.Count} runs of eight words, so this guard holds little.");
        foreach (var method in new[] { "campaign-launch", "audience-building" })
        {
            var file = Path.Combine(SkillPack.BusinessRoot(), method, SkillPack.SkillFile);
            var shared = own.Intersect(Runs(Prose(file)), StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();
            Assert.True(
                shared.Count == 0,
                $"\"{SetUp}\" shares {shared.Count} runs of eight words with '{method}', the first '{shared.FirstOrDefault()}'. "
                + "Name that skill as the method rather than copying its rule.");
        }
    }

    /// <summary>
    /// The quick start and Jason Desktop's New agent both make the campaign before the set-up begins. A set-up that made
    /// one of its own would leave the person with two campaigns of one brief, the second holding none of what the first
    /// was told.
    /// </summary>
    [Fact]
    [Critical(Family.D4, "a skill a person's session reads teaches what was not decided")]
    public void The_start_fills_a_draft_that_already_exists()
    {
        var section = Whitespace().Replace(Section(SetUp), " ");
        Assert.Contains(
            "Where a draft campaign already exists, made by `quick-start` or by New agent in Jason Desktop, the set-up fills that one: read what it holds, ask only for what is missing, and make no second campaign.",
            section,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// The set-up leads and does no role's work. A session that wrote the first letter, planned the steps or built a
    /// sequence at the provider would send what no role follows up and no critic read; and one that refused a person's
    /// plain request would stand between them and their own act. Said once, at the head of the section, and the section
    /// prints no send.
    /// </summary>
    [Fact]
    [Critical(Family.D4, "a skill a person's session reads teaches what was not decided")]
    public void The_set_up_writes_no_letter_unasked()
    {
        var raw = Section(SetUp);
        var section = Whitespace().Replace(raw, " ");
        Assert.Contains(
            "So you write no message, no template and no plan of steps or follow-ups, you say nothing of a sequence at a provider, and you create no send.",
            section,
            StringComparison.Ordinal);
        Assert.Contains(
            "When the person asks in plain words for the thing itself, \"write this letter yourself\" or \"send it to her now\", say once what is lost: no role follows it up, and no critic has read it. Then do what they asked, as their act and in their name.",
            section,
            StringComparison.Ordinal);

        var printed = SkillPack.PrintedCommands(raw.Split('\n')).ToList();
        Assert.DoesNotContain(printed, line => line.Contains(" --operation message.send", StringComparison.Ordinal)
            || line.Contains(" --operation social_invitation.send", StringComparison.Ordinal)
            || line.Contains(" --operation campaign.", StringComparison.Ordinal)
            || line.Contains(" --role copywriter", StringComparison.Ordinal));
    }

    /// <summary>
    /// Who the messages are written as is asked once, and first, where it has no value: the roles that write end their
    /// work failed without it, so a campaign started before it is a campaign whose first review writes nothing. Once it
    /// has a value it is said back and never asked again.
    /// </summary>
    [Fact]
    [Critical(Family.D4, "a skill a person's session reads teaches what was not decided")]
    public void The_set_up_asks_the_sender_s_name_once_and_first()
    {
        var section = Whitespace().Replace(Section(SetUp), " ");
        foreach (var fact in new[]
        {
            "**Who the messages are written as, first.** Read `jason preference list`. Where `sender.identity` has no value, ask once, before anything else,",
            "Once it has a value it is said back in one line of the question below, and never asked again.",
            "Until it has one, the roles that write end their work failed with `sender_identity_unset`, so the campaign is not started.",
        })
        {
            Assert.True(section.Contains(fact, StringComparison.Ordinal), $"\"{SetUp}\" does not say: {fact}");
        }

        var first = section.IndexOf("**Who the messages are written as, first.**", StringComparison.Ordinal);
        var sold = section.IndexOf("**What is sold, and to whom.**", StringComparison.Ordinal);
        var people = section.IndexOf("**The people.**", StringComparison.Ordinal);
        Assert.True(first >= 0 && sold > first && people > sold, $"The set-up collects the sender at {first}, what is sold at {sold} and the people at {people}: the sender first.");
    }

    /// <summary>
    /// How messages go is said in the question as a fact read from the campaign, never assumed and never asked: a
    /// session that said "every message waits" of a campaign whose cold sends go on their own would tell the person a
    /// thing that is not so, and one that asked them to choose there would set a mode on a word given to another question.
    /// </summary>
    [Fact]
    [Critical(Family.D4, "a skill a person's session reads teaches what was not decided")]
    public void The_set_up_says_how_messages_go_from_what_the_campaign_answers()
    {
        var section = Whitespace().Replace(Section(SetUp), " ");
        foreach (var fact in new[]
        {
            "How messages go is said as a fact, read from `send_modes_in_force` in what `jason campaign get` answers.",
            "Where a kind goes automatically, say which kind, and that the critic reads each such message before it goes.",
            "Never ask the person to choose here, and never set a mode: `approvals-and-questions` asks it at an approval.",
        })
        {
            Assert.True(section.Contains(fact, StringComparison.Ordinal), $"\"{SetUp}\" does not say: {fact}");
        }

        Assert.True(
            typeof(Jason.Contracts.Api.CampaignDto).GetProperty("SendModesInForce") is not null,
            "The set-up reads `send_modes_in_force` from `campaign get`, and the campaign's answer carries no such field.");
    }

    /// <summary>
    /// A start summons the manager at once, and the manager is a role: on an installation where no role can run, the
    /// start ends in a review that fails before anything is planned. So the roles check stops the start and names the
    /// check's own repair, which is an operator's act the session proposes; and like every readiness read it says
    /// nothing where all is well.
    /// </summary>
    [Fact]
    [Critical(Family.D4, "a skill a person's session reads teaches what was not decided")]
    public void The_roles_check_speaks_only_on_a_problem_and_stops_the_start()
    {
        var raw = Section(SetUp);
        var section = Whitespace().Replace(raw, " ");
        foreach (var fact in new[]
        {
            "These reads speak only where something is wrong: where every one passes, say nothing of them and go on to the question.",
            "**Can a role run here?** Read the `roles_launchable` check in what `jason status` answers.",
            "Do not ask the question and do not start the campaign: say plainly that no role can run here yet, and give the repair the check names, in its own words.",
            "It is an operator's act on this machine, `operating-the-installation`'s: propose it, and run it only when the person asks.",
        })
        {
            Assert.True(section.Contains(fact, StringComparison.Ordinal), $"\"{SetUp}\" does not say: {fact}");
        }

        var printed = SkillPack.PrintedCommands(raw.Split('\n')).ToList();
        var status = printed.IndexOf("jason status");
        var start = printed.FindIndex(line => line.StartsWith("jason campaign start ", StringComparison.Ordinal));
        Assert.True(status >= 0 && start > status, $"\"{SetUp}\" prints the readiness read at {status} and the start at {start} of: {string.Join(" | ", printed)}. The read comes first.");
    }

    /// <summary>
    /// The early check of notifications reads what `jason status` already says, and only speaks on a problem: a person
    /// whose notifications are off hears once that nothing will tell them when Jason waits for them, and may still start;
    /// a person whose notifications work hears nothing of them.
    /// </summary>
    [Fact]
    [Critical(Family.D4, "a skill a person's session reads teaches what was not decided")]
    public void The_early_check_says_once_that_nothing_will_tell_them_and_never_stops_the_start()
    {
        Assert.Contains(
            "**Will they hear back?** Read the `notifications` check in the same answer. Where it is `absent` or `failed`, say in one sentence that nothing will tell them when Jason starts waiting for them, and give the check's own words; it never stops the start. Where it is `ok`, say nothing.",
            Whitespace().Replace(Section(SetUp), " "),
            StringComparison.Ordinal);

        // The check has a fourth state, which says only that nobody knows: said once, and no stop either.
        Assert.Contains(
            "Where it is `unknown`, say once that whether they will hear is not known; that never stops the start either.",
            Whitespace().Replace(Section(SetUp), " "),
            StringComparison.Ordinal);
    }

    /// <summary>
    /// The first batch is twenty, said as a fact in the question and asked about nowhere; a number the person names is
    /// recorded where the roles read it, the campaign's context, under one key.
    /// </summary>
    [Fact]
    [Critical(Family.D4, "a skill a person's session reads teaches what was not decided")]
    public void The_set_up_says_the_first_batch_is_twenty_and_records_another_number_the_person_names()
    {
        var raw = Section(SetUp);
        var section = Whitespace().Replace(raw, " ");
        Assert.Contains("After the start, the first work is for a first batch of twenty people", section, StringComparison.Ordinal);
        Assert.Contains("Where the person names another number, there or earlier, record it as the context's `first_batch`.", section, StringComparison.Ordinal);
        Assert.Contains("that the first work is for the first twenty people, or the number they named;", section, StringComparison.Ordinal);

        var batch = Assert.Single(SkillPack.Printed(raw.Split('\n')), command => command.Line.StartsWith("jason campaign update-context ", StringComparison.Ordinal));
        Assert.True(
            batch.Body is { } body && JsonNode.Parse(body)?["set"]?["first_batch"]?.GetValue<int>() is > 0,
            $"\"{SetUp}\" records another first batch with '{batch.Line}', whose request sets no `first_batch`.");
    }

    /// <summary>
    /// The set-up ends in one question, and a yes to it starts the campaign and nothing else, under the person's own name,
    /// which the session says: the journal cannot tell their act from the session's, so the name is said aloud each time.
    /// A start at a moment the person names does not exist, and is never promised.
    /// </summary>
    [Fact]
    [Critical(Family.D4, "a skill a person's session reads teaches what was not decided")]
    public void The_start_comes_on_the_person_s_yes_and_says_whose_name_it_carries()
    {
        var raw = Section(SetUp);
        var section = Whitespace().Replace(raw, " ");
        foreach (var fact in new[]
        {
            "The message ends with exactly: \"Everything is ready. Start it?\"",
            "**Yes**, in any words that answer that question: start it, with the person named as the one who starts it, then read it back.",
            "Say \"Started, as you said. It is recorded under your name, {name}.\" only once `jason campaign get` answers the status `active`.",
            "A yes to that question starts the campaign and nothing else: it lets no message go, answers no question and sets no send mode.",
            "Jason has no start at a moment the person names, \"Monday at nine\", and a session cannot wake itself: say so plainly where they ask for one, and never promise it.",
            "No launched role can start it for them: a start from inside a run is refused, because it is a person's word.",
        })
        {
            Assert.True(section.Contains(fact, StringComparison.Ordinal), $"\"{SetUp}\" does not say: {fact}");
        }

        var start = Assert.Single(SkillPack.PrintedCommands(raw.Split('\n')), line => line.StartsWith("jason campaign start ", StringComparison.Ordinal));
        Assert.True(
            Regex.IsMatch(start, @"^jason campaign start cmp_\S+ --actor human:\S+ --reason '[^']*'$"),
            $"\"{SetUp}\" starts the campaign with '{start}': the person is named as the actor, with a reason, at the end of the command.");
    }

    /// <summary>
    /// Where the set-up sits: after what the person has already written down, which it reads first, and before the work
    /// block, whose commands it points to. And what a session's acts carry is said once, before either.
    /// </summary>
    [Fact]
    [Critical(Family.D4, "a skill a person's session reads teaches what was not decided")]
    public void The_set_up_stands_before_the_work_and_says_whose_name_the_acts_carry()
    {
        var text = File.ReadAllText(SkillPack.Find("managed-campaign-work").File).Replace("\r\n", "\n", StringComparison.Ordinal);
        var names = text.IndexOf("\n## Whose name your acts carry\n", StringComparison.Ordinal);
        var written = text.IndexOf("\n## What the person has already written down\n", StringComparison.Ordinal);
        var setUp = text.IndexOf($"\n## {SetUp}\n", StringComparison.Ordinal);
        var work = text.IndexOf("\n## The work itself\n", StringComparison.Ordinal);
        Assert.True(
            names >= 0 && written > names && setUp > written && work > setUp,
            $"managed-campaign-work has whose name the acts carry at {names}, what was written down at {written}, the set-up at {setUp} and the work at {work}: in that order.");

        var section = Whitespace().Replace(Section("Whose name your acts carry"), " ");
        foreach (var fact in new[]
        {
            "In a person's own session, a command you run without `--actor` is recorded as theirs: `human:` with the name of the account you run under.",
            "The journal cannot tell their act from yours. So you act as the person only on what they said.",
            "Their word covers what it names and nothing beside it:",
            "Where you are not sure that what they said covers an act, ask before you act.",
        })
        {
            Assert.True(section.Contains(fact, StringComparison.Ordinal), $"\"Whose name your acts carry\" does not say: {fact}");
        }
    }

    /// <summary>
    /// The runtime wakes a campaign that has no item: its manager is summoned at the start, on the events it is summoned
    /// by, and on its own cadence. A session that believed it had to keep the campaign moving would stay and create work
    /// in the manager's place.
    /// </summary>
    [Fact]
    [Critical(Family.D4, "a skill a person's session reads teaches what was not decided")]
    public void A_campaign_with_no_item_is_woken_by_its_manager_and_the_session_ends_at_the_start()
    {
        var skill = Flattened("managed-campaign-work");
        Assert.Contains(
            "A campaign with no item is woken the same way: the runtime summons its manager when it starts, on the events the manager is summoned by, and on its own cadence. So a session's job ends when what the person asked for is recorded in Jason, for a new campaign when it has started, and the person has been told what to expect.",
            skill,
            StringComparison.Ordinal);
        Assert.Contains("The person may ask you to stay and watch; then you read and tell them what you see, and what you see is no reason to create work.", skill, StringComparison.Ordinal);
    }

    /// <summary>
    /// The fence on a provider's sequence, said where a direct request lands: beside the managed path's example of an
    /// enrolment at the provider, and in the launch skill, whose trigger words include "sequence". A sequence linked to a
    /// campaign sends its own steps, and Jason's roles, who know nothing of them, would send their own letters beside
    /// them; so a campaign linked to one gets no letters of Jason's own, and the person hears that, that the support is
    /// partial, and that it may be tried on their yes, before anything is done. Never offered, also where the account
    /// holds some.
    /// </summary>
    [Fact]
    [Critical(Family.D4, "a skill a person's session reads teaches what was not decided")]
    public void The_fence_on_a_providers_sequence_is_said_where_a_direct_request_lands()
    {
        const string Never = "A provider's sequence is never offered, also where the account holds some.";
        const string Fence =
            "Where a person asks directly to link a campaign to a provider's sequence, or to create one there, say three things before anything else: "
            + "1. Jason's support of a provider's sequences is partial. "
            + "2. In a campaign linked to a provider's sequence Jason sends no letters at all: whether the sequence sends is the sequence's matter, and Jason adds no steps to it. "
            + "3. It may be tried, on the person's yes.";

        // The managed path: in "The work itself", before the enrolment it prints as its example of a provider operation.
        var work = Section("The work itself");
        var flat = Whitespace().Replace(work, " ");
        Assert.Contains(Never, flat, StringComparison.Ordinal);
        Assert.Contains(Fence, flat, StringComparison.Ordinal);
        var enrol = flat.IndexOf("--operation campaign.enroll", StringComparison.Ordinal);
        Assert.True(
            enrol > flat.IndexOf(Fence, StringComparison.Ordinal),
            "managed-campaign-work says the fence after its enrolment example, or prints no enrolment; it belongs before the example.");

        // The rule is the campaign's, and the manager's watch starts from the line the session writes into the
        // campaign's context: a fence that dropped either would pass the three sentences and never reach a role.
        Assert.Contains("The rule is the campaign's, not one person's.", flat, StringComparison.Ordinal);
        Assert.Contains("write in the campaign's context, in their words, that the campaign is linked to that sequence", flat, StringComparison.Ordinal);

        // Jason makes the provider's campaign as an empty draft (`campaign.create`), so the fence never says it has no
        // operation that makes one: it says that what it makes sends nothing.
        Assert.Contains("Jason makes no sequence that sends: `campaign.create` makes only an empty draft at the provider, with no steps of its own.", flat, StringComparison.Ordinal);
        Assert.DoesNotContain("Jason has no operation that makes a sequence", flat, StringComparison.Ordinal);

        // The launch skill: in its section on Jason, before its prerequisites.
        var launch = File.ReadAllText(Path.Combine(SkillPack.BusinessRoot(), "campaign-launch", SkillPack.SkillFile)).Replace("\r\n", "\n", StringComparison.Ordinal);
        var from = launch.IndexOf("\n## Jason's set-up, and this skill as its method\n", StringComparison.Ordinal);
        var to = launch.IndexOf("\n## Prerequisites\n", StringComparison.Ordinal);
        Assert.True(from >= 0 && to > from, "campaign-launch has no section \"## Jason's set-up, and this skill as its method\" before \"## Prerequisites\".");
        var section = Whitespace().Replace(launch[from..to], " ");
        Assert.Contains(Never, section, StringComparison.Ordinal);
        Assert.Contains(Fence, section, StringComparison.Ordinal);
        Assert.Contains("The rule is the campaign's, not one person's: a campaign linked to a sequence gets no letter of Jason's own.", section, StringComparison.Ordinal);

        // The philosophy page, which the README links, says the same rule, and no longer steers towards a sequence
        // "where that is the better tool".
        var philosophy = Whitespace().Replace(File.ReadAllText(Path.Combine(SkillPack.RepositoryRoot(), "docs", "philosophy.md")), " ");
        Assert.Contains("a campaign is linked to one only where the person asks for it; such a campaign gets no letters of Jason's own.", philosophy, StringComparison.Ordinal);
        Assert.DoesNotContain("the better tool", philosophy, StringComparison.Ordinal);
    }

    /// <summary>
    /// The fence stands in two places, and no other skill steers towards a provider's sequence: a sentence that speaks of
    /// linking a campaign to one, creating one, putting people into one or offering one points at one of the two places,
    /// or it is a second rule that drifts from the fence, or a steer the fence was written to stop. Read over every
    /// Markdown file of both packs but the manager's, which carries the manager's own part of the rule; the two files the
    /// fence stands in are read too, all but the fence's own text, since a steer comes back there most readily. A
    /// sentence is passed over only where it sends the reader to the fence; naming either skill is no excuse.
    /// Words in backticks are names, a retired <c>sequence.create</c> among them, and are not read as prose. The phrases:
    /// linking and a sequence in one sentence, either way round; creating, building, setting up or making one; enrolling,
    /// putting or adding somebody into one; and offering, proposing, suggesting or recommending one.
    /// </summary>
    [Fact]
    [Critical(Family.D4, "a skill a person's session reads teaches what was not decided")]
    public void No_skill_but_the_fences_two_places_speaks_of_linking_or_creating_a_providers_sequence()
    {
        var phrases = new (string Name, Regex Pattern, string Steer)[]
        {
            ("linking to a sequence", new Regex(@"\blink\w*\b[^.;:]{0,80}\bsequences?\b", RegexOptions.IgnoreCase), "Link the campaign to the sequence the account already holds."),
            ("a sequence linked", new Regex(@"\bsequences?\b[^.;:]{0,80}\blink\w*", RegexOptions.IgnoreCase), "Where the account holds sequences, ask which one to link."),
            ("creating a sequence", new Regex(@"\b(creat|build|built|set up|setting up|make|making|made)\w*\b[^.;:]{0,60}\bsequences?\b", RegexOptions.IgnoreCase), "Create a sequence at the provider with three steps."),
            ("into a sequence", new Regex(@"\b(enrol|put|add)\w*\b[^.;:]{0,40}\b(into|to|in) (a|the|their|your|its|that|this)( provider's)? sequences?\b", RegexOptions.IgnoreCase), "Enrol the first twenty people into the provider's sequence."),
            ("offering a sequence", new Regex(@"\b(offer|propos|suggest|recommend)\w*\b[^.;:]{0,60}\bsequences?\b|\bsequences?\b[^.;:]{0,40}\b(offer|propos|suggest|recommend)\w*", RegexOptions.IgnoreCase), "Offer the person one of their sequences."),
        };

        // Each phrase finds the steer it is named for, so a pattern that matched nothing would not pass for a clean pack.
        foreach (var phrase in phrases)
        {
            Assert.True(phrase.Pattern.IsMatch(phrase.Steer), $"The phrase '{phrase.Name}' does not find its own example: {phrase.Steer}");
        }

        var skills = Path.Combine(SkillPack.RepositoryRoot(), "skills");
        var managed = SkillPack.Find("managed-campaign-work").File;
        var launch = Path.Combine(SkillPack.BusinessRoot(), "campaign-launch", SkillPack.SkillFile);
        var manager = Path.Combine(SkillPack.Root(), "roles", "manager") + Path.DirectorySeparatorChar;
        var files = Directory.EnumerateFiles(skills, "*.md", SearchOption.AllDirectories)
            .Where(file => !file.StartsWith(manager, StringComparison.OrdinalIgnoreCase))
            .ToList();
        Assert.True(files.Count > 20, $"Only {files.Count} Markdown files were read under '{skills}', so this guard holds little.");
        Assert.Contains(managed, files, StringComparer.OrdinalIgnoreCase);
        Assert.Contains(launch, files, StringComparer.OrdinalIgnoreCase);

        // A sentence that sends the reader to the fence, or says its own rule in its own words, is not a steer; naming
        // either skill for any other reason is no excuse.
        var pointer = new Regex(@"\bas (`managed-campaign-work`|\*A provider's sequence\*|""The work itself"")[^.;:]{0,40}\bsays\b|\bprovider's sequence is never offered\b", RegexOptions.IgnoreCase);

        var found = new List<string>();
        foreach (var file in files)
        {
            var raw = File.ReadAllText(file).Replace("\r\n", "\n", StringComparison.Ordinal);
            if (string.Equals(file, managed, StringComparison.OrdinalIgnoreCase))
            {
                // The fence's own text: its paragraph through the enrolment it prints.
                var enrol = raw.IndexOf("--operation campaign.enroll", StringComparison.Ordinal);
                raw = WithoutPart(raw, "**A provider's sequence.**", raw.IndexOf("\n```\n", Math.Max(enrol, 0), StringComparison.Ordinal), "managed-campaign-work");
            }
            else if (string.Equals(file, launch, StringComparison.OrdinalIgnoreCase))
            {
                // The fence's own text: its subsection, and the bullet of "When NOT to use" that sends the request to it.
                raw = WithoutPart(raw, "### A provider's sequence\n", raw.IndexOf("\n## Prerequisites\n", StringComparison.Ordinal), "campaign-launch");
                var bullet = raw.IndexOf("\n- A request to link a campaign to a provider's sequence", StringComparison.Ordinal);
                Assert.True(bullet >= 0, "campaign-launch has no bullet sending a request to link a campaign to a provider's sequence to its fence.");
                raw = WithoutPart(raw, "\n- A request to link a campaign to a provider's sequence", raw.IndexOf("\n- ", bullet + 1, StringComparison.Ordinal), "campaign-launch");
            }

            var text = Whitespace().Replace(raw, " ");
            var neutral = Backticked().Replace(text, match => match.Value.Replace('.', '·'));
            foreach (var sentence in SentenceEnd().Split(neutral))
            {
                if (pointer.IsMatch(sentence))
                {
                    continue;
                }

                var bare = Backticked().Replace(sentence, " ");
                found.AddRange(phrases.Where(phrase => phrase.Pattern.IsMatch(bare)).Select(phrase => $"'{Path.GetRelativePath(skills, file)}' ({phrase.Name}): {sentence}"));
            }
        }

        Assert.True(
            found.Count == 0,
            "A sentence outside the fence's own text speaks of a provider's sequence; point it at the fence instead, as `managed-campaign-work` says:\n"
            + string.Join('\n', found));
    }

    /// <summary>The text with the part from <paramref name="start"/> up to <paramref name="end"/> cut out, which must be there.</summary>
    private static string WithoutPart(string text, string start, int end, string skill)
    {
        var from = text.IndexOf(start, StringComparison.Ordinal);
        Assert.True(from >= 0 && end > from, $"{skill} no longer holds the fence's own text from '{start.Trim()}', so this guard cannot tell it from the rest.");
        return text[..from] + text[end..];
    }

    [GeneratedRegex(@"`[^`]*`")]
    private static partial Regex Backticked();

    [GeneratedRegex(@"(?<=[.!?])\s+")]
    private static partial Regex SentenceEnd();
}
