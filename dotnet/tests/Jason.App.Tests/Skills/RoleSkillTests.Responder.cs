using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Jason.Cli.Tests.Documentation;
using Jason.Contracts.Operations;
using Jason.Contracts.Skills;

namespace Jason.App.Tests.Skills;

public partial class RoleSkillTests
{
    [Fact]
    public void The_responder_is_told_that_a_reply_is_read_only_through_the_provider()
    {
        var skill = Flattened("responder");

        // The roster calls this role "handles replies", and the one way this build reads a reply is a provider
        // operation's result. A responder that assumed a verb of Jason's read a mailbox would hunt for it and then
        // invent it, which is the failure this whole section of the pack exists to prevent.
        Assert.Contains("This build reads a reply only through the provider.", skill, StringComparison.Ordinal);
        Assert.Contains("no verb of Jason's reads a mailbox", skill, StringComparison.Ordinal);
        Assert.Contains("is in such an item's result, or nowhere, and you never imagine one", skill, StringComparison.Ordinal);

        CarriesTheLaunchContract("responder");
    }

    [Fact]
    public void The_responder_records_a_stop_in_order_and_proposes_taking_the_person_out()
    {
        var skill = Flattened("responder");

        // Somebody who said stop is the one case where waiting is the harm. Jason's own register first, because it
        // stops Jason's reach at once; then the provider's half, which runs without anybody approving it; and taking
        // them out of the campaign proposed for a person, never done on the responder's own say.
        var register = skill.IndexOf("jason suppression add --channel email", StringComparison.Ordinal);
        var optout = skill.IndexOf("--operation optout.record", StringComparison.Ordinal);
        var stop = skill.IndexOf("--operation enrollment.stop", StringComparison.Ordinal);
        Assert.True(register >= 0 && optout > register && stop > optout, "the register, then the opt-out, then the proposed stop");
        Assert.Contains("runs even while the campaign is paused", skill, StringComparison.Ordinal);
        Assert.Contains("waits for a person", skill, StringComparison.Ordinal);

        // A campaign that is archived takes no new work, and the provider's opt-out is account-wide.
        Assert.Contains("record the opt-out through any active or paused campaign they are a member of that is routed to the same provider account", skill, StringComparison.Ordinal);
        Assert.Contains("Do not remove them, or archive them or the campaign, until the opt-out has succeeded", skill, StringComparison.Ordinal);
        Assert.Contains("the provider's half waits, and say so", skill, StringComparison.Ordinal);
        Assert.DoesNotContain("rather than acting on it yourself", skill, StringComparison.Ordinal);
    }

    /// <summary>
    /// A brief that names a read is a reply in that read's result, and what the responder does is decided by the read's
    /// state when it wakes: one read of its own where the first ended without a result, a question at once where the
    /// first has not ended, since whatever holds it holds a second, and a question where there is no read. It never waits
    /// on the read it was created waiting on.
    /// </summary>
    [Fact]
    public void The_responder_reads_the_reply_from_the_read_its_brief_names()
    {
        var skill = Flattened("responder");

        Assert.Contains("When the brief names a `read`", skill, StringComparison.Ordinal);
        Assert.Contains("check its `status` before you trust its result", skill, StringComparison.Ordinal);
        Assert.Contains("you never wait on it again", skill, StringComparison.Ordinal);
        Assert.Contains("- `succeeded`: the reply is that item's result.", skill, StringComparison.Ordinal);
        Assert.Contains("- `failed`, `cancelled` or `expired`, so it ended without a result: create one read of your own", skill, StringComparison.Ordinal);
        Assert.Contains("Still no thread after that: ask the person.", skill, StringComparison.Ordinal);
        Assert.Contains("it has not ended: read nothing again, since whatever holds the first read holds a second, and ask the person at once.", skill, StringComparison.Ordinal);
        Assert.Contains("- `read` is null: no route reads the thread for you, so ask the person.", skill, StringComparison.Ordinal);
        Assert.Contains(
            "It names the campaign, the thread's identifier, the read's id and its status, and how long ago the thread moved, and it quotes nothing.",
            skill,
            StringComparison.Ordinal);
        Assert.DoesNotContain("fail with a code rather than answer a reply you have not seen", skill, StringComparison.Ordinal);
        Assert.Contains(
            "A run whose `resumption` names your own read reads that read's result there, and never applies these branches to the brief's read again.",
            skill,
            StringComparison.Ordinal);

        // The read of its own is the runtime's read, the same thread at the same page, under a key; the wait on it names it.
        var printed = SkillPack.PrintedCommands(SkillPack.Find("responder").File);
        var read = Assert.Single(printed, line => line.Contains("--operation conversation.get", StringComparison.Ordinal));
        Assert.Contains("--input '{\"thread\":{\"external_id\":\"501\"},\"limit\":20,\"offset\":0}'", read, StringComparison.Ordinal);
        Assert.Contains("--idempotency-key wi_01JB6K8TQ2W9V4MZ0C3Y7H5NRD-read-501", read, StringComparison.Ordinal);
        Assert.Equal(20, Jason.Runtime.Dispatch.Responder.ReadLimit);
        Assert.Single(printed, line => line.StartsWith("jason workitem wait ", StringComparison.Ordinal) && line.Contains("a read of thread 501 of my own", StringComparison.Ordinal));
    }

    /// <summary>
    /// Below the confidence the data file names, the best guess is recorded and the person is asked which category it is:
    /// no draft and no act, and at most as many options as a question holds.
    /// </summary>
    [Fact]
    public void The_responder_asks_rather_than_drafts_below_the_confidence_the_data_file_names()
    {
        var skill = Flattened("responder");

        Assert.Equal(0.7, (double)Categories()["question_below"]!);
        Assert.Contains("Where your confidence is below the file's `question_below`", skill, StringComparison.Ordinal);
        Assert.Contains("record your best guess with its confidence first, with `--reaction escalate`, since asking is what is done about it, then ask the person which category it is", skill, StringComparison.Ordinal);

        // The answer backs a verdict only through a question the run that recorded a verdict on the reply raised.
        Assert.Contains("The order is load-bearing: a question raised before the guess is recorded cannot back the person's answer", skill, StringComparison.Ordinal);
        Assert.Contains("The guess is recorded from your own attempt, with no actor typed, never as `role:responder`: a line recorded by the role names no attempt, and then no answer to your question can back a verdict.", skill, StringComparison.Ordinal);
        Assert.Contains("write no draft and act on nothing, with one exception", skill, StringComparison.Ordinal);

        // A probable stop is the one act that does not wait: the register entry is Jason's own and a person lifts it.
        Assert.Contains(
            "where your best guess is `opt_out`, put them on the register at once, as below, and leave the provider's `optout.record` and the proposed stop for the answer",
            skill,
            StringComparison.Ordinal);
        Assert.Contains(
            "the question says the entry was made and that an answer naming another category takes it off, and your summary says the entry was made",
            skill,
            StringComparison.Ordinal);
        Assert.Contains("below `question_below` it is `escalate`", skill, StringComparison.Ordinal);
        Assert.Contains("at most ten, as many as a question holds, the most likely first, and `other` always among them", skill, StringComparison.Ordinal);
        Assert.Contains("A guess is never mirrored: the mirror follows a verdict that stands.", skill, StringComparison.Ordinal);
        Assert.Equal(10, Jason.Runtime.Decisions.DecisionLimits.MaxOptions);
    }

    /// <summary>
    /// An absence, a later moment and a bounce are acted on by a note in the campaign's own role note: nothing is sent,
    /// nothing is created, and a bounce never reaches the register of people who refused.
    /// </summary>
    [Fact]
    public void The_responder_notes_a_return_a_later_moment_or_a_bounce_and_sends_nothing()
    {
        var skill = Flattened("responder");

        Assert.Contains("**Note what is due later, and send nothing.**", skill, StringComparison.Ordinal);
        Assert.Contains("the contact's id, the channel's kind and the moment, never the bounce's text", skill, StringComparison.Ordinal);
        Assert.Contains("A bounce never goes on the do-not-contact register", skill, StringComparison.Ordinal);
        Assert.Contains("no draft, no question and no new item", skill, StringComparison.Ordinal);
        Assert.Contains("A reply that fits two ids takes the one whose reaction is due now, and the other fact goes into the note.", skill, StringComparison.Ordinal);

        var note = Assert.Single(
            SkillPack.PrintedCommands(SkillPack.Find("responder").File),
            line => line.StartsWith("jason rolenote set --role responder --campaign ", StringComparison.Ordinal) && line.Contains("\"bounces\"", StringComparison.Ordinal));
        Assert.Contains("under `returns`, each with `contact`, `category` and `back_at`", skill, StringComparison.Ordinal);
        Assert.Contains("under `bounces`, each with `contact`, `channel` and `at`", skill, StringComparison.Ordinal);

        // One shape, the one the manager reads: exactly these keys an entry, and a later moment under returns with its category.
        var words = ShellWords.Split(note).ToList();
        var body = JsonNode.Parse(words[words.IndexOf("--note") + 1])!.AsObject();
        Assert.Equal(["returns", "bounces"], body.Select(member => member.Key));
        foreach (var entry in body["returns"]!.AsArray())
        {
            Assert.Equal(["contact", "category", "back_at"], entry!.AsObject().Select(member => member.Key));
        }

        foreach (var entry in body["bounces"]!.AsArray())
        {
            Assert.Equal(["contact", "channel", "at"], entry!.AsObject().Select(member => member.Key));
        }

        Assert.Contains(body["returns"]!.AsArray(), entry => (string)entry!["category"]! == "not_now");
    }

    /// <summary>
    /// The answer is a reply in the thread the person wrote in, as work: never a new thread, on the thread's own channel,
    /// the whole text in the item under a key of its own, and drafted only after the two checks that can make a reply
    /// wrong. The catalog says the same of the role, and nothing says the answer is only words.
    /// </summary>
    [Fact]
    public void The_responder_answers_in_the_person_s_own_thread_and_never_in_a_new_one()
    {
        var skill = Flattened("responder");

        Assert.Contains("you answer with the reading and with the reply item you create.", skill, StringComparison.Ordinal);
        Assert.Contains(
            "What you would write back is a `provider_op` item naming `conversation.reply`, in this campaign, for the person the thread is with, naming the read's thread: it answers inside that thread and reaches nobody else, never a new thread.",
            skill,
            StringComparison.Ordinal);
        Assert.Contains(
            "Its `channel` is the thread's, as the read reports it, and its `body` is the whole text, exactly as a person will read it before deciding.",
            skill,
            StringComparison.Ordinal);
        Assert.Contains(
            "Its key is built from your own work item's id, the thread and the number of this reply in it — `-reply-501-1`, then `-reply-501-2` — so that two replies in one thread are two keys.",
            skill,
            StringComparison.Ordinal);
        Assert.Contains("--idempotency-key wi_01JB6K8TQ2W9V4MZ0C3Y7H5NRD-reply-501-1", skill, StringComparison.Ordinal);
        Assert.Contains(
            "is its document's, `docs/contracts/operations/conversation.reply.md`, and a mismatch comes back as the item's error `thread_contact_mismatch`, with nothing sent.",
            skill,
            StringComparison.Ordinal);

        // Drafted after the two checks that can make a reply wrong: whether there is one to answer, and whose it is.
        var nothing = skill.IndexOf("**Nothing to answer when the latest message is your side's own.**", StringComparison.Ordinal);
        var nobody = skill.IndexOf("**Where your item names nobody, find the person through the thread.**", StringComparison.Ordinal);
        var reply = skill.IndexOf("--operation conversation.reply", StringComparison.Ordinal);
        Assert.True(
            nothing >= 0 && nobody > nothing && reply > nobody,
            $"the reply is printed at {reply}, after both checks (nothing to answer at {nothing}, nobody named at {nobody})");

        // What was false once a reply could be sent: that nothing writes a message, and that the answer is only words.
        Assert.DoesNotContain("No operation this build publishes writes a message", skill, StringComparison.Ordinal);
        Assert.DoesNotContain("A reply you draft is this work item's result", skill, StringComparison.Ordinal);
        Assert.DoesNotContain("the words you would send back", skill, StringComparison.Ordinal);
        var catalog = Whitespace().Replace(File.ReadAllText(SkillPack.Catalog()), " ");
        Assert.DoesNotContain("the answer is a reading and a draft rather than anything sent", catalog, StringComparison.Ordinal);
        Assert.DoesNotContain("the answer goes into the person's own thread as a reply a person approves, one approval for each send", catalog, StringComparison.Ordinal);
        Assert.Contains("an answer in the person's own thread as a draft, which a person approves or a standing approval they set for its category releases", catalog, StringComparison.Ordinal);
        Assert.Contains("classified by the categories in the data file beside the skill and by the person's own", catalog, StringComparison.Ordinal);
    }

    /// <summary>
    /// A reply reaches a person, so without a standing approval it parks, and one item is one send: the responder neither
    /// approves nor bundles.
    /// </summary>
    [Fact]
    public void The_responder_asks_one_approval_for_each_send()
    {
        var skill = Flattened("responder");

        Assert.Contains("without one, it parks for a person's approval, and you do not approve it.", skill, StringComparison.Ordinal);
        Assert.DoesNotContain("It reaches a person, so it parks for a person's approval", skill, StringComparison.Ordinal);
        Assert.Contains("One work item is one reply to one person and one approval: two replies are two work items, never one item for both.", skill, StringComparison.Ordinal);
    }

    /// <summary>
    /// A thread whose latest message is outbound moved for something sent from this side: there is nothing to answer. A
    /// read that stops short of the end, or a last message nobody can place, does not show the latest message: a question.
    /// </summary>
    [Fact]
    public void The_responder_has_nothing_to_answer_when_the_latest_message_is_Jason_s_own()
    {
        var skill = Flattened("responder");

        Assert.Contains(
            "**Nothing to answer when the latest message is your side's own.** The read's messages come oldest first; when the last one is `outbound`,",
            skill,
            StringComparison.Ordinal);
        Assert.Contains("there is no reply to answer: say so in your answer and create nothing.", skill, StringComparison.Ordinal);
        Assert.Contains(
            "When the read's `has_more` is true, or the last message's `direction` is `unknown`, the read does not show you the latest message: create no reply, and say so in your answer, so that a person can be asked.",
            skill,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// Somebody who asked to stop gets no answer: the stop is recorded and nothing is drafted, so the opt-out section
    /// comes before the reply and says so. A stop from somebody Jason cannot name still reaches the register, by address.
    /// </summary>
    [Fact]
    public void The_responder_sends_no_answer_to_a_person_who_asked_to_stop()
    {
        var skill = Flattened("responder");

        Assert.Contains("Somebody who asked to stop is sent no answer: recording the stop is the whole of what you do about that reply.", skill, StringComparison.Ordinal);
        Assert.Contains(
            "Where no single person was found, the register's entry is still made, by the address they wrote from; no `optout.record` item is created, because it needs the person, and your answer says that the provider's half waits for a person.",
            skill,
            StringComparison.Ordinal);
        Assert.True(
            skill.IndexOf("--operation optout.record", StringComparison.Ordinal) < skill.IndexOf("--operation conversation.reply", StringComparison.Ordinal),
            "the stop is taught before the answer");
    }

    /// <summary>
    /// A line that named no person brings an item that names nobody. The responder looks the person up by the address
    /// they wrote from, and a match counts only on that exact address; exactly one is an answer, none or several is a
    /// question for a person, never a guess, and the reply's own check is what stands behind a wrong one.
    /// </summary>
    [Fact]
    public void The_responder_finds_the_person_through_the_thread_when_its_item_names_nobody()
    {
        var skill = Flattened("responder");

        Assert.Contains("**Where your item names nobody, find the person through the thread.**", skill, StringComparison.Ordinal);
        Assert.Contains(
            "Look them up in Jason by the address the latest inbound message came `from`, never by the read's `contact`, which carries the provider's own identifier and a name: the name only says who wrote.",
            skill,
            StringComparison.Ordinal);
        Assert.Contains("jason contact list --query marta@example.com", skill, StringComparison.Ordinal);
        Assert.Contains(
            "A person found counts only when their value on the thread's channel equals that address exactly: `--query` matches any part of a name, a company, a title or a channel value, so a result can be somebody else, and a `from` that is a display name rather than an address matches nobody.",
            skill,
            StringComparison.Ordinal);
        Assert.Contains("Exactly one such person: the reply names them with `--contact`.", skill, StringComparison.Ordinal);
        Assert.Contains(
            "None, or more than one: create no reply, and say in your answer who wrote, from which address, and why nothing was drafted as an item, so that a person can be asked.",
            skill,
            StringComparison.Ordinal);
        Assert.Contains("A reply that names the wrong person anyway is refused by the reply's own check, below, with nothing sent.", skill, StringComparison.Ordinal);
    }

    /// <summary>
    /// Filing a thread can carry the provider's own meaning, so the responder files only under a category the campaign
    /// lists as its own under one key, written as the provider names it, and only where that name is the recorded
    /// category's own, ignoring case and reading an underscore as a space; anything else is said in the summary and not
    /// filed. A read of one thread reports only the category it sits in now, so nothing is checked against it.
    /// </summary>
    [Fact]
    public void The_responder_files_the_thread_only_under_a_category_the_campaign_names()
    {
        var skill = Flattened("responder");

        Assert.Contains("**Mirror the verdict only where the campaign names a provider category the same way.**", skill, StringComparison.Ordinal);
        Assert.Contains("under `filing_categories`, a list of category names written as the provider names them.", skill, StringComparison.Ordinal);
        Assert.Contains("equal to the category you recorded, ignoring case and reading `_` as a space", skill, StringComparison.Ordinal);
        Assert.Contains("file it under that name, with a `provider_op` item naming `conversation.classify`, and under nothing else", skill, StringComparison.Ordinal);
        Assert.Contains("The mirror is never a second verdict", skill, StringComparison.Ordinal);
        Assert.Contains("--operation conversation.classify", skill, StringComparison.Ordinal);
        Assert.Contains("`docs/contracts/operations/conversation.classify.md`", skill, StringComparison.Ordinal);
        Assert.Contains("A name the provider does not hold fails `category_not_found`, with nothing filed.", skill, StringComparison.Ordinal);
        Assert.Contains(
            "Where no name meets, or the key is absent or empty, file nothing, and say so in your summary.",
            skill,
            StringComparison.Ordinal);
        Assert.DoesNotContain("Where the reply calls for filing", skill, StringComparison.Ordinal);
        Assert.DoesNotContain("and the read reports", skill, StringComparison.Ordinal);
        Assert.DoesNotContain("as a read of a thread reports them", skill, StringComparison.Ordinal);

        // The key is said in one more place, written the same way: where a session is told how a thread is filed.
        var session = Flattened("managed-campaign-work");
        Assert.Contains("lists them in the campaign's context under `filing_categories`, written as the provider names them,", session, StringComparison.Ordinal);
        Assert.Contains("files it under one of the provider's own categories, as the provider names them,", session, StringComparison.Ordinal);
        Assert.DoesNotContain("the ones a read of the thread reports", session, StringComparison.Ordinal);
        Assert.DoesNotContain("as a read of a thread reports them", session, StringComparison.Ordinal);
    }

    /// <summary>
    /// A launched responder copies these lines, and a line refused at the create is an answer that never parks: each
    /// <c>--input</c> it prints is judged against the operation's own document, and an operation that needs the person
    /// names one.
    /// </summary>
    [Fact]
    public void Every_provider_operation_the_responder_prints_carries_arguments_its_document_accepts()
    {
        var operations = new List<string>();
        foreach (var line in SkillPack.PrintedCommands(SkillPack.Find("responder").File).Where(line => line.Contains(" --kind provider_op ", StringComparison.Ordinal)))
        {
            var words = ShellWords.Split(line).ToList();
            string? Value(string option) => words.IndexOf(option) is var at and >= 0 && at < words.Count - 1 ? words[at + 1] : null;

            var operation = Value("--operation") ?? string.Empty;
            var contract = OperationCatalog.Find(operation);
            Assert.True(contract is not null, $"The responder prints a work item for '{operation}', which is no published operation: {line}");
            var input = Value("--input");
            Assert.True(input is not null, $"The responder prints a work item for {operation} with no --input: {line}");
            var problems = SchemaValidator.Verdicts(SchemaValidator.Validate(JsonNode.Parse(input), contract.ArgumentSchema));
            Assert.True(
                problems.Count == 0,
                $"The --input the responder prints for {operation} is refused by its document: "
                + $"{string.Join("; ", problems.Select(problem => $"{problem.Pointer} {problem.Message}"))}. The line: {line}");
            Assert.True(
                contract.Preflight.Contact != ContactRequirement.Required || Value("--contact") is not null,
                $"{operation} needs the person a work item is about, and the line the responder prints for it names none: {line}");
            operations.Add(operation);
        }

        foreach (var operation in new[] { "conversation.get", "optout.record", "enrollment.stop", "conversation.reply", "conversation.classify" })
        {
            Assert.True(
                operations.Contains(operation, StringComparer.Ordinal),
                $"The responder prints work items for [{string.Join(", ", operations)}], and none for {operation}.");
        }
    }

    /// <summary>The base categories, in the order the data file lists them.</summary>
    internal static readonly string[] BaseCategories =
    [
        "interested", "meeting_requested", "meeting_booked", "question", "objection", "not_now", "wrong_person",
        "referral", "out_of_office", "auto_reply", "bounce", "opt_out", "other",
    ];

    /// <summary>The responder's category data, read from the file a person edits.</summary>
    internal static JsonObject Categories() =>
        JsonNode.Parse(File.ReadAllText(Path.Combine(SkillPack.Find("responder").Directory, "categories.json")))!.AsObject();

    /// <summary>
    /// The categories are data beside the skill, not prose inside it: a person edits a list, and a launch carries the file
    /// because a role's whole folder travels. The principle opens the file, since it decides every later addition: a
    /// category is what a standing approval is set on, so two replies whose safe reaction differs never share one.
    /// </summary>
    [Fact]
    public void The_responder_s_categories_are_a_data_file_beside_its_skill()
    {
        var skill = SkillPack.Find("responder");
        var file = Path.Combine(skill.Directory, "categories.json");
        Assert.True(File.Exists(file), $"No categories.json beside '{skill.File}'.");

        var data = Categories();
        Assert.Equal(["principle", "question_below", "categories"], data.Select(member => member.Key));
        var below = (double)data["question_below"]!;
        Assert.True(below is > 0 and < 1, $"question_below is {below}, where it is a confidence between 0 and 1.");
        Assert.StartsWith(
            "A category is the unit a standing approval is set on, so two replies whose safe reaction differs in what it reaches or in its approval class never share an id, and two replies with the same reaction share one.",
            (string)data["principle"]!,
            StringComparison.Ordinal);

        var entries = data["categories"]!.AsArray().Select(entry => entry!.AsObject()).ToList();
        Assert.Equal(BaseCategories, entries.Select(entry => (string)entry["id"]!));

        // Three members an entry, each one line: the id, what such a reply says, and the default reaction, which opens
        // with the word the verdict records (the four reply.record takes) and a colon.
        foreach (var entry in entries)
        {
            var id = (string)entry["id"]!;
            Assert.True(
                entry.Select(member => member.Key).SequenceEqual(["id", "says", "reaction"]),
                $"'{id}' carries [{string.Join(", ", entry.Select(member => member.Key))}], where an entry is id, says and reaction.");
            Assert.Matches("^[a-z][a-z0-9_]*$", id);
            foreach (var member in new[] { "says", "reaction" })
            {
                var text = (string)entry[member]!;
                Assert.False(string.IsNullOrWhiteSpace(text), $"'{id}' says nothing under {member}.");
                Assert.DoesNotContain('\n', text);
            }

            Assert.Matches("^(act|draft|escalate|none): ", (string)entry["reaction"]!);
        }

        // Where the reaction reaches someone new, protects a person or repairs the channel, it is never a draft: a
        // standing approval on a draft would release what only a person may decide.
        string Word(string id) => ((string)entries.Single(entry => (string)entry["id"]! == id)["reaction"]!).Split(':')[0];
        Assert.Equal("escalate", Word("referral"));
        Assert.Equal("act", Word("opt_out"));
        Assert.Equal("act", Word("bounce"));
        Assert.Equal("act", Word("out_of_office"));
        Assert.Equal("act", Word("not_now"));
        Assert.Equal("none", Word("auto_reply"));
        Assert.Equal("draft", Word("interested"));

        // A reaction is what the role does, and a question it raises quotes nothing: no reaction asks for what the reply
        // said, and the one that always asks names the nearest category instead.
        foreach (var entry in entries)
        {
            var reaction = (string)entry["reaction"]!;
            foreach (var phrase in new[] { "what you read", "what they wrote", "what they said", "the reply's text", "quote the" })
            {
                Assert.False(
                    reaction.Contains(phrase, StringComparison.OrdinalIgnoreCase),
                    $"'{(string)entry["id"]!}' reacts with \"{reaction}\", which asks for the reply's text ('{phrase}').");
            }
        }

        var other = (string)entries.Single(entry => (string)entry["id"]! == "other")["reaction"]!;
        Assert.Contains("the nearest category, why it does not fit, and what you would do", other, StringComparison.Ordinal);
        Assert.Contains("quotes nothing", other, StringComparison.Ordinal);

        // A launch copies what the role's folder holds; the data file is among it.
        var reading = RoleSkillRules.Read(skill.Directory, "responder", int.MaxValue);
        Assert.Null(reading.Problem);
        Assert.Contains(reading.Files, path => Path.GetFileName(path) == "categories.json");
    }

    /// <summary>
    /// One run, one pass through the loop, in the order that keeps a reply to one verdict and one draft: the read, the
    /// thread's open work, the classification, the verdict recorded before anything is done about it, the reaction, the
    /// mirror, and the answer in the item's shape.
    /// </summary>
    [Fact]
    public void The_responder_goes_through_the_loop_in_order()
    {
        var skill = Flattened("responder");

        string[] steps =
        [
            "**The loop, in order.**",
            "**What your brief gives you.**",
            "**One verdict per reply, one draft per verdict.**",
            "**Classify by the categories, then by the person's own rules.**",
            "**Record the verdict first, from your own attempt.**",
            "**React as the category says.**",
            "**Answer in the shape your item asks for.**",
        ];
        var at = steps.Select(step => skill.IndexOf(step, StringComparison.Ordinal)).ToArray();
        for (var index = 0; index < steps.Length; index++)
        {
            Assert.True(at[index] >= 0, $"The responder's skill has no '{steps[index]}'.");
            if (index > 0)
            {
                Assert.True(at[index] > at[index - 1], $"'{steps[index]}' comes before '{steps[index - 1]}'.");
            }
        }

        Assert.True(
            skill.IndexOf("--operation conversation.reply", StringComparison.Ordinal) > at[5],
            "The reply is drafted as a reaction, after the verdict is recorded.");
    }

    /// <summary>
    /// On email, a second reply that arrives before the first answer can be lost at the provider, so a classified thread
    /// never waits on the responder: it answers or parks the draft in the same run. Said without the provider's name.
    /// </summary>
    [Fact]
    public void The_responder_answers_or_parks_at_once_because_a_follow_up_can_be_lost()
    {
        var skill = Flattened("responder");

        // The principle only: how a given provider holds a follow-up is that provider's to document, not this skill's.
        Assert.Contains(
            "**Act or park at once.** A provider may hold a contact's later replies only once this side has answered, so you either answer or park the draft at once, never leave a classified thread without an action, and the person's approval lag is the window in which a follow-up can be lost.",
            skill,
            StringComparison.Ordinal);
        Assert.DoesNotContain("ingest", skill, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("On email", skill, StringComparison.Ordinal);
    }

    /// <summary>
    /// The brief is the runtime's <c>inbound_reply</c>, identifiers and moments only, and the reply is the result of the
    /// read it names, which the responder was created waiting on and never waits on again.
    /// </summary>
    [Fact]
    public void The_responder_reads_its_brief_as_the_inbound_reply_the_runtime_wrote()
    {
        var skill = Flattened("responder");

        Assert.Contains("Your brief's `inbound_reply` is what the runtime wrote when the thread moved", skill, StringComparison.Ordinal);
        foreach (var member in new[] { "plugin", "thread", "channel", "contact_id", "read", "moves" })
        {
            Assert.Contains($"`{member}`", skill, StringComparison.Ordinal);
        }

        Assert.Contains("you were created already waiting on it, and you never wait on it again", skill, StringComparison.Ordinal);
        Assert.Contains("check its `status` before you trust its result", skill, StringComparison.Ordinal);
        Assert.DoesNotContain("`inbound_reply.moves` say", skill, StringComparison.Ordinal);
    }

    /// <summary>
    /// A reply gets one verdict and one draft: the thread's open work and the campaign's verdicts are read before
    /// classifying, and an open draft that already answers a verdict on this message ends the run with nothing done.
    /// </summary>
    [Fact]
    public void The_responder_records_one_verdict_per_reply_and_drafts_once_per_verdict()
    {
        var skill = Flattened("responder");

        // A listing carries no context, so the verdict a draft cites is read item by item.
        Assert.Contains(
            "A listing carries no context, so read each `conversation.reply` it shows with `jason workitem get` for its `context.reply_verdict`, and match that `jrn_` id to a `reply_classified` line by the line's `plugin`, `thread` and `message_id`.",
            skill,
            StringComparison.Ordinal);

        // A verdict already standing is the verdict, whoever recorded it, and the run reacts by its word.
        Assert.Contains(
            "A verdict already standing on this message — the latest `reply_classified` line for the same `plugin`, `thread` and `message_id` — is the verdict, whoever recorded it: record nothing, and react as that line's reaction word says.",
            skill,
            StringComparison.Ordinal);
        Assert.Contains(
            "- `draft`: where a `conversation.reply` citing a verdict on this message is open or has succeeded, the reply has its draft: draft nothing, and say so in your summary, with the outcome `nothing`. Where none is, write the draft under that line, naming it in `reply_verdict`.",
            skill,
            StringComparison.Ordinal);
        Assert.Contains("- `act`: do the act.", skill, StringComparison.Ordinal);
        Assert.Contains("- `escalate`: ask, unless a question about this thread is still open", skill, StringComparison.Ordinal);
        Assert.Contains("- `none`: nothing.", skill, StringComparison.Ordinal);
        Assert.Contains(
            "A draft under a line that is neither your own attempt's nor a named person's waits for a person whatever standing approval exists, unless the line carries a person's answer through a decision (its `answered_by` is not null), and your summary says so.",
            skill,
            StringComparison.Ordinal);
        Assert.Contains("`Responder:MaxAttempts` is 1 by default, and the rule holds anyway", skill, StringComparison.Ordinal);
        Assert.DoesNotContain("Where a `conversation.reply` is open whose `reply_verdict` names", skill, StringComparison.Ordinal);

        var printed = SkillPack.PrintedCommands(SkillPack.Find("responder").File);
        Assert.Contains(printed, line => line.StartsWith("jason journal list --campaign ", StringComparison.Ordinal) && line.Contains("--kind reply_classified", StringComparison.Ordinal));
        Assert.Contains(printed, line => line.StartsWith("jason decision list --campaign cmp_", StringComparison.Ordinal));
        var work = Assert.Single(printed, line => line.StartsWith("jason workitem list --campaign ", StringComparison.Ordinal));
        foreach (var status in new[] { "created", "awaiting_approval", "scheduled", "processing", "waiting", "succeeded" })
        {
            Assert.Contains($"--status {status}", work, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// The base set is the data file; the person's refinements come from the memory files the launch names, client, then
    /// project, then campaign, the nearest winning. Ten made-up extensions show the shape, each refining one base category
    /// under an id of its own, and one of them is a refinement that always escalates.
    /// </summary>
    [Fact]
    public void The_responder_classifies_by_the_data_file_and_the_person_s_reply_categories()
    {
        var skill = Flattened("responder");

        Assert.Contains("`categories.json`, beside this file", skill, StringComparison.Ordinal);
        Assert.Contains("a section headed \"Reply categories\" in the `AGENTS.md` at `memory_files.client`, then `memory_files.project`, then `memory_files.campaign`", skill, StringComparison.Ordinal);
        Assert.Contains("the nearer one wins: the campaign's over the project's, the project's over the client's", skill, StringComparison.Ordinal);

        var examples = Regex.Matches(File.ReadAllText(SkillPack.Find("responder").File), @"^- `([a-z][a-z0-9_]*)` refines `([a-z][a-z0-9_]*)`: ", RegexOptions.Multiline);
        Assert.Equal(10, examples.Count);
        Assert.Equal(10, examples.Select(match => match.Groups[1].Value).Distinct(StringComparer.Ordinal).Count());
        foreach (Match example in examples)
        {
            Assert.DoesNotContain(example.Groups[1].Value, BaseCategories);
            Assert.Contains(example.Groups[2].Value, BaseCategories);
        }

        Assert.Contains("- `very_interested` refines `interested`: ", skill, StringComparison.Ordinal);
        Assert.Contains("Always escalate", skill, StringComparison.Ordinal);
        Assert.Contains(
            "- `assistant_forward` refines `referral`: an assistant says to write to their manager. Escalate; the question names the thread so I can read who they named.",
            skill,
            StringComparison.Ordinal);
        Assert.DoesNotContain("as they wrote it", skill, StringComparison.Ordinal);
    }

    /// <summary>A campaign's goals are what a draft steers to; with none, the default goal is a demo and the verdict names none.</summary>
    [Fact]
    public void The_responder_drafts_toward_the_campaign_s_goals()
    {
        var skill = Flattened("responder");

        Assert.Contains("Read the campaign's `goals` with `jason campaign get`", skill, StringComparison.Ordinal);
        Assert.Contains("A campaign with no goals — the key absent, null or an empty list — steers to the default goal, a demo, and your verdict names no `--goal`", skill, StringComparison.Ordinal);
        Assert.DoesNotContain("objective", skill, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The verdict is recorded inside the responder's own attempt, before any reaction, with a reason that names the rule
    /// and never the reply; a reply is always a draft, released by the runtime and never by the role.
    /// </summary>
    [Fact]
    public void The_responder_records_the_verdict_before_it_reacts_and_never_releases_its_own_draft()
    {
        var skill = Flattened("responder");
        var printed = SkillPack.PrintedCommands(SkillPack.Find("responder").File);

        var record = Assert.Single(printed, line => line.StartsWith("jason reply record ", StringComparison.Ordinal));
        Assert.DoesNotContain("--actor", record, StringComparison.Ordinal);
        Assert.Contains("The reason names the rule you applied and never quotes or paraphrases the reply.", skill, StringComparison.Ordinal);
        Assert.Contains("is `draft` whenever you write a reply, whether or not a standing approval exists", skill, StringComparison.Ordinal);
        Assert.Contains("Whether it goes out now or waits for a person is the runtime's to decide, never yours.", skill, StringComparison.Ordinal);
        Assert.Contains("You never set a standing approval.", skill, StringComparison.Ordinal);
        Assert.Contains("say in your summary whether the reply goes out or waits", skill, StringComparison.Ordinal);
        Assert.Contains("find the one for the category you recorded, and whether it is `in_force`", skill, StringComparison.Ordinal);
        Assert.NotNull(typeof(Jason.Contracts.Api.StandingApprovalDto).GetProperty("InForce"));
        Assert.Single(
            SkillPack.PrintedCommands(SkillPack.Find("responder").File),
            line => line.StartsWith("jason approval list-standing cmp_", StringComparison.Ordinal));

        var draft = Assert.Single(printed, line => line.Contains("--operation conversation.reply", StringComparison.Ordinal));
        Assert.Contains("--context '{\"reply_verdict\":\"jrn_", draft, StringComparison.Ordinal);
        Assert.True(
            skill.IndexOf("jason reply record ", StringComparison.Ordinal) < skill.IndexOf("--operation conversation.reply", StringComparison.Ordinal),
            "The verdict is recorded before the draft that names it.");
    }

    /// <summary>
    /// A question the responder raises says the category in the category's own words and the thread to open, and
    /// neither quotes nor retells the reply: the person reads the reply in the thread, never in the question.
    /// </summary>
    [Fact]
    public void The_responder_asks_in_the_category_s_words_and_names_the_thread()
    {
        var skill = Flattened("responder");

        Assert.Contains(
            "The question names the category, in the category's own words, and the thread's identifier, so the person can open the thread.",
            skill,
            StringComparison.Ordinal);
        Assert.Contains("It quotes nothing, and it retells nothing of the reply beyond what the category says.", skill, StringComparison.Ordinal);
        Assert.DoesNotContain("Say what you read in your own words", skill, StringComparison.Ordinal);

        var raise = Assert.Single(
            SkillPack.PrintedCommands(SkillPack.Find("responder").File),
            line => line.StartsWith("jason decision raise ", StringComparison.Ordinal));
        var words = ShellWords.Split(raise).ToList();
        var question = words[words.IndexOf("--question") + 1];
        Assert.Contains("thread 501", question, StringComparison.Ordinal);
        Assert.DoesNotContain("and why it fits", skill, StringComparison.Ordinal);

        // The printed question is a referral, in referral's own words and nothing of the reply beyond them.
        var says = (string)Categories()["categories"]!.AsArray().Single(entry => (string)entry!["id"]! == "referral")!["says"]!;
        var own = says.TrimEnd('.').Replace(",", string.Empty, StringComparison.Ordinal);
        Assert.Contains(char.ToLowerInvariant(own[0]) + own[1..], question, StringComparison.Ordinal);
        Assert.DoesNotContain("colleague", question, StringComparison.Ordinal);
    }

    /// <summary>
    /// The result the skill prints is the shape the runtime holds a responder to, read from the runtime's own format, so
    /// the skill and the code cannot name different outcomes.
    /// </summary>
    [Fact]
    public void The_responder_answers_in_the_result_format_the_runtime_holds_it_to()
    {
        var skill = Flattened("responder");
        var format = Jason.Runtime.Dispatch.Responder.ResultFormat.AsObject();

        foreach (var outcome in format["properties"]!["outcome"]!["enum"]!.AsArray())
        {
            Assert.Contains($"`{(string)outcome!}`", skill, StringComparison.Ordinal);
        }

        foreach (var property in format["properties"]!.AsObject())
        {
            Assert.Contains($"`{property.Key}`", skill, StringComparison.Ordinal);
        }

        var complete = Assert.Single(
            SkillPack.PrintedCommands(SkillPack.Find("responder").File),
            line => line.StartsWith("jason workitem complete ", StringComparison.Ordinal) && line.Contains("\"outcome\"", StringComparison.Ordinal));
        var words = ShellWords.Split(complete).ToList();
        var result = JsonNode.Parse(words[words.IndexOf("--result") + 1]);
        var problems = SchemaValidator.Verdicts(SchemaValidator.Validate(result, format));
        Assert.True(problems.Count == 0, $"The result the responder prints is refused by its format: {string.Join("; ", problems.Select(problem => $"{problem.Pointer} {problem.Message}"))}");
    }
}
