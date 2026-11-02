using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Jason.Cli.Tests.Documentation;
using Jason.Contracts.Operations;

namespace Jason.App.Tests.Skills;

public partial class InteractiveSkillTests
{
    [Fact]
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

        // The wiring check proves a provider is reached, and it is not campaign work: it is a walk the person takes or
        // skips, asked once by the operating skill, and the real work begins with the person's brief and a plan, which
        // this skill turns into work. A session that took the check for the job would stop there.
        Assert.Contains(
            "The wiring check, `docs/golden-path.md`, connects a provider and proves it with the person's own address; it is not campaign work. It is a synthetic walk the person takes or skips when the installation becomes ready, and asking them once is `operating-the-installation`'s. The real work begins with the person's brief — what they tell you about their first campaign, in their own words — and a campaign planned with the business pack's `campaign-planning` skill, and this skill is how the brief and that plan become work the runtime and Jason's roles perform.",
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

        // The section's lead-in says the same order in words, so a reader who skims to the commands meets it first.
        Assert.Contains(
            "A project for what is sold, a campaign under it, the people the campaign is about, and then one work item per thing to be done.",
            Flattened("managed-campaign-work"),
            StringComparison.Ordinal);
    }

    /// <summary>
    /// The check that stops at no plugin stops only the work that reaches a provider. A person who skipped the wiring check
    /// has no plugin, and the planner tells them they plan as anybody does; a session that read the check as covering the
    /// project, the campaign and its people would stop the first run before anything was made.
    /// </summary>
    [Fact]
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
    public void The_brief_becomes_Jason_s_campaign_before_any_contact_exists()
    {
        const string Opening = "**The brief becomes the campaign.**";
        var text = File.ReadAllText(SkillPack.Find("managed-campaign-work").File).Replace("\r\n", "\n", StringComparison.Ordinal);

        var start = text.IndexOf(Opening, StringComparison.Ordinal);
        Assert.True(start >= 0, "managed-campaign-work has no paragraph on the brief.");
        var end = text.IndexOf("\n\n", start, StringComparison.Ordinal);
        var paragraph = Whitespace().Replace(text[start..(end > start ? end : text.Length)], " ").Trim();

        Assert.Equal(
            Opening + " What the person told you about the campaign — the product, the audience, how it sells, in their own words — is the campaign's context, given at `jason campaign create` with `--context`, before any contact is added and before anything is sent. "
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

        var printed = SkillPack.PrintedCommands(work.Split('\n')).ToList();
        var create = printed.FindIndex(line => line.StartsWith("jason campaign create ", StringComparison.Ordinal));
        var contacts = printed.FindIndex(line => line.StartsWith("jason campaign add-contacts ", StringComparison.Ordinal));
        Assert.True(
            create >= 0 && contacts > create,
            $"Under \"The work itself\" the campaign create is printed at line {create} and the first add-contacts at line {contacts} of: {string.Join(" | ", printed)}");
        Assert.True(
            printed[create].Contains(" --context '{", StringComparison.Ordinal),
            $"The campaign create under \"The work itself\" gives the campaign no brief: {printed[create]}");

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
    public void A_change_of_direction_goes_into_the_campaign_s_context()
    {
        const string Opening = "**A change of direction goes into the context as well.**";
        var text = File.ReadAllText(SkillPack.Find("managed-campaign-work").File).Replace("\r\n", "\n", StringComparison.Ordinal);

        var start = text.IndexOf(Opening, StringComparison.Ordinal);
        Assert.True(start >= 0, "managed-campaign-work has no paragraph on a change of direction.");
        var end = text.IndexOf("\n\n", start, StringComparison.Ordinal);
        Assert.Equal(
            Opening + " On a campaign that already exists, what the person changes about it is written with `jason campaign update-context`: `--set` writes the top-level keys it names, `--unset` removes a key, once for each, and every key it does not name stays as it was.",
            Whitespace().Replace(text[start..(end > start ? end : text.Length)], " ").Trim());

        var brief = text.IndexOf("**The brief becomes the campaign.**", StringComparison.Ordinal);
        var keys = text.IndexOf("**Give every create a key you could send again.**", StringComparison.Ordinal);
        var printed = text.IndexOf("\njason campaign update-context cmp_01JB6K8TQ2W9V4MZ0C3Y7H5NRD --set ", StringComparison.Ordinal);
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
    /// these lines, and a line refused at the create teaches the session that the managed path does not work.
    /// </summary>
    [Fact]
    public void Every_provider_operation_the_managed_path_prints_carries_arguments_its_document_accepts()
    {
        var printed = SkillPack.PrintedCommands(SkillPack.Find("managed-campaign-work").File)
            .Where(line => line.Contains(" --kind provider_op ", StringComparison.Ordinal))
            .ToList();

        var operations = new List<string>();
        foreach (var line in printed)
        {
            var words = ShellWords.Split(line);
            var operation = ValueOf(words, "--operation") ?? string.Empty;
            var contract = OperationCatalog.Find(operation);
            Assert.True(contract is not null, $"managed-campaign-work prints a work item for '{operation}', which is no published operation: {line}");

            var input = ValueOf(words, "--input");
            Assert.True(input is not null, $"managed-campaign-work prints a work item for {operation} with no --input: {line}");
            var problems = SchemaValidator.Verdicts(SchemaValidator.Validate(JsonNode.Parse(input), contract.ArgumentSchema));
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
            resumes.Count == 1 && resumes[0].StartsWith("jason --actor human:", StringComparison.Ordinal),
            $"The managed path prints [{string.Join(" | ", resumes)}] to end a wait; it prints one, naming the person.");
    }

    [GeneratedRegex(@"`docs/contracts/operations/([a-z_]+\.[a-z_]+)\.md`")]
    private static partial Regex OperationDocument();

    /// <summary>
    /// "What is blocked" is answered where a waiting item is explained: a parked item is a waiting one, and the block, not
    /// the session, releases it.
    /// </summary>
    [Fact]
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
}
