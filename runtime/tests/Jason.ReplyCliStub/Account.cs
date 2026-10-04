using System.Collections;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

// Deliberately in the global namespace, as the top-level program in this project is: a namespace named after the
// assembly would be found ahead of the test helper classes of the same name in the project that references it.

/// <summary>
/// One Reply account, held as a directory of JSON files, and the handful of v3 paths the official plugin calls
/// in it. It stands for what every canonical operation ends at: a provider with its own integer identifiers, its
/// own list and sequence state, its own opt-out register, and its own ideas about which failures are answers and
/// which are statuses.
/// </summary>
/// <remarks>
/// The shapes are the ones Reply's published description actually uses, because a stand-in that answered more
/// tidily than the real thing would let a plugin pass here and fail there: the paged <c>{items, hasMore}</c>
/// envelope, <c>problem+json</c> with a stable <c>&lt;resource&gt;.&lt;variant&gt;</c> code, the overloaded 400
/// — a validation problem carries <c>errors[]</c> and no <c>code</c>, a business rejection the other way round —
/// the empty-bodied 401, and per-item failure inside a 200 in the two shapes that matter. Nothing here touches
/// the network or a shell, and nothing is written outside the account directory, so a test says what happened at
/// the provider by reading the files back.
/// </remarks>
internal static class Account
{
    private const string ContactsFile = "contacts.json";
    private const string ListsFile = "lists.json";
    private const string SequencesFile = "sequences.json";
    private const string EnrollmentsFile = "enrollments.json";
    private const string OptOutsFile = "optouts.json";
    private const string StatsFile = "stats.json";
    private const string ThreadsFile = "threads.json";
    private const string InstructionsFile = "instructions.json";
    private const string CredentialFile = "credential.json";

    /// <summary>How many accounts a sequence sends from, kept by this stand-in alone and never shown in an answer.</summary>
    private const string SendingAccounts = "_sendingAccounts";

    /// <summary>
    /// The file that says a store belongs to this stand-in. It is looked for before anything is created, read
    /// or written, because the directory this program finds is decided by a variable a test sets — and a test
    /// that forgot to set it would otherwise find the operator's own Reply configuration and start writing call
    /// logs and planted accounts into it.
    /// </summary>
    private const string MarkerFile = ".jason-stand-in";
    private const string CallsFile = "calls.jsonl";

    /// <summary>Where this provider's identifiers start; the number is meaningless, its stability is not.</summary>
    private const int FirstContactId = 1001;

    public static async Task<int> RunAsync(string profile, string method, string path, string? body, string[] argv)
    {
        var store = Locate();
        if (!File.Exists(Path.Combine(store, MarkerFile)))
        {
            // Not a store this stand-in planted. Refused before the directory is even created, in the shape the
            // real CLI refuses a call it will not make: a usage error, and its own code on stderr.
            await Console.Error.WriteLineAsync(new JsonObject
            {
                ["error"] = new JsonObject
                {
                    ["code"] = "usage.store",
                    ["title"] = "This is not a stand-in store.",
                    ["hint"] = $"No '{MarkerFile}' under '{store}'. A test sets the configuration directory; this one did not.",
                },
            }.ToJsonString());

            return 2;
        }

        var root = Path.Combine(store, profile);
        Directory.CreateDirectory(root);

        // Recorded before anything is answered. What a plugin did is then a fact a test can read rather than
        // something inferred from the state left behind: an obligation to read before writing is invisible
        // against a provider that would have answered the same either way.
        Record(root, method, path, body, argv);

        if (!File.Exists(Path.Combine(root, CredentialFile)))
        {
            // The CLI's own refusal, before there is any HTTP status to report and before any request is built.
            // reply-cli 0.5.1 spells it exactly this way: a usage error, so exit 2, with nothing on stdout and
            // one line of its `--json` error envelope on stderr, whose `auth.` code is what says the refusal was
            // about who is calling rather than about what was asked.
            var refusal = new JsonObject
            {
                ["error"] = new JsonObject
                {
                    ["code"] = "auth.required",
                    ["title"] = "Not authenticated.",
                    ["hint"] = $"Run `reply auth login` or set REPLY_API_KEY (profile '{profile}').",
                },
            };

            await Console.Error.WriteLineAsync(refusal.ToJsonString());
            return 2;
        }

        var instruction = Take(root, method, path);
        switch (instruction is null ? string.Empty : Text(instruction, "kind"))
        {
            case "answer":
                return await AnswerAsync(Number(instruction!, "code"), Scripted(Text(instruction!, "body")));

            case "hang":
                await Task.Delay(Number(instruction!, "milliseconds"));
                break;

            case "hold":
                {
                    // A test that kills something in the middle of a provider call needs the call to be
                    // demonstrably in flight, which no delay can promise: a sleep says how long to wait, never
                    // that the waiting started. So this one says it has started — the marker file — and then
                    // waits to be let go. The cap is not the mechanism; it is what stops a test that never
                    // releases it from holding a CI run open, and reaching it is a failed test either way.
                    if (Text(instruction!, "marker") is { Length: > 0 } marker)
                    {
                        await File.WriteAllTextAsync(marker, string.Empty);
                    }

                    var until = Text(instruction!, "until");
                    var capMs = Number(instruction!, "timeout_ms");
                    var deadline = DateTime.UtcNow.AddMilliseconds(capMs > 0 ? capMs : 120_000);
                    while (until.Length > 0 && !File.Exists(until) && DateTime.UtcNow < deadline)
                    {
                        await Task.Delay(25);
                    }

                    break;
                }

            case "prints":
                // Whatever the test built, written out exactly as it is: no envelope, no status, nothing this
                // program decides. It is the one answer `Answers` cannot express, because that one always
                // prints the CLI's own {code, data} around whatever it is given — and a caller has to survive
                // stdout that is not an envelope at all.
                await Console.Out.WriteAsync(Text(instruction!, "stdout"));
                await Console.Out.FlushAsync();

                // And whatever it said beside it. The real CLI says why it refused on stderr and nowhere else,
                // so an ending without that half is only half an ending.
                if (Text(instruction!, "stderr") is { Length: > 0 } said)
                {
                    await Console.Error.WriteLineAsync(said);
                }

                return Number(instruction!, "exit_code");

            case "lost":
                {
                    // Everything the call would have done is done, and then the answer is gone. Only a recovery
                    // read can say what happened, which is the whole reason this ending is reproducible.
                    Answer(root, method, path, body);
                    return await AnswerAsync(502, Problem(502, "Bad Gateway", "gateway.answerLost", "The answer was lost after the request had been carried out."));
                }
        }

        var (code, answer) = Answer(root, method, path, body);
        return await AnswerAsync(code, answer);
    }

    /// <summary>
    /// Where the real CLI keeps its store, and therefore where this one looks: the platform's own configuration
    /// directory. No flag carries it, because the child's environment is built from nothing and the package
    /// declares no environment capability — so this is the only way an account can be chosen, and choosing one
    /// exercises the base environment for real.
    /// </summary>
    /// <remarks>
    /// A variable that is missing or empty gives a directory of this program's own under the temporary tree,
    /// never a relative path: a relative one would resolve against whatever the caller's working directory
    /// happened to be. Nothing is written there either, because no store of this program's making carries the
    /// marker — the two rules together are what keep a forgotten variable from reaching anybody's real account.
    /// </remarks>
    private static string Locate()
    {
        string configured;
        if (OperatingSystem.IsWindows())
        {
            configured = Environment.GetEnvironmentVariable("APPDATA") ?? string.Empty;
        }
        else if (Environment.GetEnvironmentVariable("XDG_CONFIG_HOME") is { Length: > 0 } home)
        {
            configured = home;
        }
        else if (Environment.GetEnvironmentVariable("HOME") is { Length: > 0 } user)
        {
            configured = Path.Combine(user, ".config");
        }
        else
        {
            configured = string.Empty;
        }

        return configured.Length == 0
            ? Path.Combine(Path.GetTempPath(), "jason-reply-stand-in", "reply")
            : Path.Combine(configured, "reply");
    }

    // -------------------------------------------------------------------------------------------------------
    // The paths the official package calls
    // -------------------------------------------------------------------------------------------------------

    private static (int Code, JsonNode? Body) Answer(string root, string method, string path, string? body)
    {
        var query = string.Empty;
        var bare = path;
        var mark = path.IndexOf('?', StringComparison.Ordinal);
        if (mark >= 0)
        {
            query = path[(mark + 1)..];
            bare = path[..mark];
        }

        var segments = bare.Split('/', StringSplitOptions.RemoveEmptyEntries);
        return (segments, method) switch
        {
            (["v3", "sequences"], "GET") => Sequences(root, query),
            (["v3", "sequences", var sequence], "GET") => Sequence(root, sequence),
            (["v3", "sequences", var sequence, "contacts"], "GET") => SequenceContacts(root, sequence, query),
            (["v3", "sequences", var sequence, "contacts", "count"], "GET") => SequenceContactCount(root, sequence),
            (["v3", "sequences", var sequence, "contacts", var contact], "GET") => Participation(root, sequence, contact),
            (["v3", "sequences", var sequence, "contact-links", "bulk"], "POST") => Enroll(root, sequence, body),
            (["v3", "sequences", var sequence, "contact-links", var contact], "DELETE") => Remove(root, sequence, contact),
            (["v3", "sequences", var sequence, "pause"], "POST") => Pause(root, sequence),
            (["v3", "sequences", var sequence, "start"], "POST") => Start(root, sequence),
            (["v3", "sequences", var sequence, "stats"], "POST") => Stats(root, sequence, body),
            (["v3", "inbox", "threads", "filter"], "POST") => FilterThreads(root, query, body),
            (["v3", "inbox", "threads", var thread], "GET") => InboxThread(root, thread),
            (["v3", "inbox", "threads", var thread, "messages"], "GET") => ThreadMessages(root, thread, query),
            (["v3", "contacts"], "POST") => CreateContact(root, body),
            (["v3", "contacts", "import"], "POST") => Import(root, body),
            (["v3", "contacts", "set-opted-out"], "POST") => SetOptedOut(root, body),
            (["v3", "contacts", "filter"], "POST") => Filter(root, query, body),
            (["v3", "contacts", var contact, "statuses"], "GET") => Statuses(root, contact),
            (["v3", "contacts", var contact, "lists"], "GET") => ListsOf(root, contact),
            (["v3", "contact-lists"], "GET") => ContactLists(root, query),
            (["v3", "contact-lists", var list, "add-contacts"], "POST") => AddToList(root, list, body),
            _ => (404, Problem(404, "Not Found", "route.notFound", $"This account answers no {method} {bare}.")),
        };
    }

    private static (int, JsonNode?) Sequences(string root, string query) =>
        Page(
            [.. Records(root, SequencesFile).Select(sequence => (JsonNode)new JsonObject
            {
                ["id"] = sequence["id"]!.GetValue<int>(),
                ["name"] = Text(sequence, "name"),
                ["status"] = Text(sequence, "status"),
                ["isArchived"] = Flag(sequence, "isArchived"),
            })],
            query);

    private static (int, JsonNode?) Sequence(string root, string id) =>
        Find(root, SequencesFile, id) is { } sequence
            ? (200, Shown(sequence))
            : (404, Problem(404, "Not Found", "sequence.notFound", $"This account holds no sequence {id}."));

    /// <summary>
    /// A pause, as Reply documents it: idempotent, answered with the sequence itself whether it was just paused or
    /// already was. Its refusals carry the words Reply documents for it and none of this stand-in's own: an archived
    /// sequence is an incompatible state, a 409 `sequenceAction.archived`; a sequence it does not hold is a 404
    /// `sequenceAction.notFound`; and pausing one that never started is a domain rule rejection, a 400 with no code:
    /// the one 400 word Reply documents for a pause, `sequenceAction.teamMemberNotFound`, names a team member's
    /// rejection, and this is not one.
    /// </summary>
    private static (int, JsonNode?) Pause(string root, string id)
    {
        var sequences = Read(root, SequencesFile);
        if (Find(sequences, Number(id)) is not { } sequence)
        {
            return (404, Problem(404, "Not Found", "sequenceAction.notFound", "Sequence not found."));
        }

        if (Flag(sequence, "isArchived"))
        {
            return (409, Problem(409, "Conflict", "sequenceAction.archived", "Sequence is in an incompatible state (archived)."));
        }

        if (string.Equals((string?)sequence["status"], "new", StringComparison.Ordinal))
        {
            return (400, Problem(400, "Bad Request", code: null, "Domain rule rejection."));
        }

        sequence["status"] = "paused";
        Write(root, SequencesFile, sequences);
        return (200, Shown(sequence));
    }

    /// <summary>
    /// A start, as Reply documents it: idempotent, answered with the sequence itself, live; it also resumes a paused
    /// one. Its refusals carry the words Reply documents for it and none of this stand-in's own: no contacts
    /// (`sequenceAction.noContacts`) and no email accounts (`sequenceAction.noEmailAccounts`), both 400; a sequence it
    /// does not hold, 404 `sequenceAction.notFound`; and an archived one, 409 `sequenceAction.archived`. The plugin maps
    /// by status and carries whatever code arrives.
    /// </summary>
    private static (int, JsonNode?) Start(string root, string id)
    {
        var sequences = Read(root, SequencesFile);
        if (Find(sequences, Number(id)) is not { } sequence)
        {
            return (404, Problem(404, "Not Found", "sequenceAction.notFound", "Sequence not found."));
        }

        if (Flag(sequence, "isArchived"))
        {
            return (409, Problem(409, "Conflict", "sequenceAction.archived", "Sequence is in an incompatible state (archived)."));
        }

        if (!Records(root, EnrollmentsFile).Any(entry => Matches(entry, "sequenceId", id)))
        {
            return (400, Problem(400, "Bad Request", "sequenceAction.noContacts", "Sequence has no contacts."));
        }

        if (sequence[SendingAccounts] is JsonValue sending && sending.GetValue<int>() == 0)
        {
            return (400, Problem(400, "Bad Request", "sequenceAction.noEmailAccounts", "Sequence has no email accounts."));
        }

        sequence["status"] = "active";
        Write(root, SequencesFile, sequences);
        return (200, Shown(sequence));
    }

    /// <summary>
    /// One person taken out of a sequence. Reply answers 204 with an empty body, and one 404, lumped as the participation
    /// read's is, for a sequence or a contact it does not hold or a contact that does not take part.
    /// </summary>
    private static (int, JsonNode?) Remove(string root, string sequenceId, string contactId)
    {
        if (Find(root, SequencesFile, sequenceId) is null)
        {
            return NotInSequence();
        }

        var enrollments = Read(root, EnrollmentsFile);
        var enrollment = enrollments.OfType<JsonObject>().FirstOrDefault(entry =>
            Matches(entry, "sequenceId", sequenceId) && Matches(entry, "contactId", contactId));
        if (enrollment is null)
        {
            return NotInSequence();
        }

        enrollments.Remove(enrollment);
        Write(root, EnrollmentsFile, enrollments);
        return (204, null);
    }

    /// <summary>
    /// The opt-out flag set or cleared for up to a hundred contacts at once. Non-atomic: the answer lists only the
    /// contacts that failed, and a contact already in the state asked for counts as updated.
    /// </summary>
    private static (int, JsonNode?) SetOptedOut(string root, string? body)
    {
        var request = Request(body);
        if (request?["contactIds"] is not JsonArray ids || ids.Count == 0 || ids.Count > 100)
        {
            return Invalid("/contactIds", "contactIds names between 1 and 100 contacts.");
        }

        if (request["isOptedOut"] is not JsonValue flag || !flag.TryGetValue<bool>(out var optedOut))
        {
            return Invalid("/isOptedOut", "isOptedOut is required.");
        }

        var numbers = new List<int>();
        foreach (var id in ids)
        {
            if (id is not JsonValue value || !value.TryGetValue<int>(out var number) || number <= 0)
            {
                return Invalid("/contactIds", "Each contact id is a positive integer.");
            }

            numbers.Add(number);
        }

        var contacts = Read(root, ContactsFile);
        var register = Read(root, OptOutsFile);
        var failed = new JsonObject();
        foreach (var number in numbers)
        {
            if (Find(contacts, number) is null)
            {
                failed[number.ToString(System.Globalization.CultureInfo.InvariantCulture)] = Failure("notFound", $"Contact {number} does not exist.");
                continue;
            }

            var held = register.FirstOrDefault(entry => entry!.GetValue<int>() == number);
            if (optedOut && held is null)
            {
                register.Add(number);
            }
            else if (!optedOut && held is not null)
            {
                register.Remove(held);
            }
        }

        Write(root, OptOutsFile, register);
        return (200, failed);
    }

    /// <summary>A sequence as Reply would answer it: without the keys this stand-in keeps for its own refusals.</summary>
    private static JsonObject Shown(JsonObject sequence)
    {
        var shown = (JsonObject)sequence.DeepClone();
        foreach (var key in shown.Select(member => member.Key).Where(key => key.StartsWith('_')).ToList())
        {
            shown.Remove(key);
        }

        return shown;
    }

    /// <summary>
    /// Reply's one documented 404 for a person's part in a sequence: "Sequence or contact not found, or contact not in
    /// sequence", under one code. A caller cannot tell from it which of the three it was.
    /// </summary>
    private static (int, JsonNode?) NotInSequence() =>
        (404, Problem(404, "Not Found", "sequenceContact.notInSequence", "Sequence or contact not found, or contact not in sequence."));

    private static (int, JsonNode?) Participation(string root, string sequenceId, string contactId)
    {
        if (Find(root, SequencesFile, sequenceId) is null)
        {
            return NotInSequence();
        }

        var enrollment = Records(root, EnrollmentsFile).FirstOrDefault(entry =>
            Matches(entry, "sequenceId", sequenceId) && Matches(entry, "contactId", contactId));

        // "Not enrolled", in the same lumped 404 a missing sequence or contact gets.
        return enrollment is null ? NotInSequence() : (200, enrollment.DeepClone());
    }

    /// <summary>
    /// The people a sequence holds, a page at a time, most recently added first, in the shape a live account
    /// answered: the key order it used, the address included — a read that must never map it has to be shown one —
    /// an empty last name where the person has none, and the date added with an offset and two fractional digits
    /// rather than in UTC. Built from the enrollments this account keeps, so somebody enrolled is somebody listed.
    /// </summary>
    private static (int, JsonNode?) SequenceContacts(string root, string sequenceId, string query)
    {
        if (Find(root, SequencesFile, sequenceId) is null)
        {
            return (404, Problem(404, "Not Found", "sequenceContact.sequenceNotFound", $"This account holds no sequence {sequenceId}."));
        }

        var contacts = Read(root, ContactsFile);
        var suppressed = OptOuts(root);
        var rows = Records(root, EnrollmentsFile)
            .Select((entry, position) => (Entry: entry, Position: position))
            .Where(row => Matches(row.Entry, "sequenceId", sequenceId))
            .OrderByDescending(row => Added(row.Entry))
            .ThenByDescending(row => row.Position)
            .Select(row => (JsonNode)Member(row.Entry, Find(contacts, row.Entry["contactId"]!.GetValue<int>()), suppressed))
            .ToList();

        var skip = Count(query, "skip") ?? 0;
        var top = Count(query, "top") ?? 25;
        var page = new JsonArray([.. rows.Skip(skip).Take(top).Select(row => row!.DeepClone())]);
        return (200, new JsonObject { ["items"] = page, ["hasMore"] = rows.Count > skip + page.Count });
    }

    private static (int, JsonNode?) SequenceContactCount(string root, string sequenceId) =>
        Find(root, SequencesFile, sequenceId) is null
            ? (404, Problem(404, "Not Found", "sequenceContact.sequenceNotFound", $"This account holds no sequence {sequenceId}."))
            : (200, new JsonObject { ["count"] = Records(root, EnrollmentsFile).Count(entry => Matches(entry, "sequenceId", sequenceId)) });

    /// <summary>
    /// A sequence's counts over one window, in the shape a live account answered: an overview per channel, every
    /// count and every percentage a whole number, and counts this provider reports that no document names. A
    /// window with a preset and a span at once, or a span that ends before it starts, is Reply's validation problem.
    /// </summary>
    private static (int, JsonNode?) Stats(string root, string sequenceId, string? body)
    {
        if (Find(root, SequencesFile, sequenceId) is null)
        {
            return (404, Problem(404, "Not Found", "sequenceStats.notFound", $"This account holds no sequence {sequenceId}."));
        }

        var filters = Request(body)?["filters"] as JsonObject;
        var preset = filters is null ? string.Empty : Text(filters, "dateRangePreset");
        var from = filters is null ? string.Empty : Text(filters, "from");
        var to = filters is null ? string.Empty : Text(filters, "to");
        if (preset.Length > 0 && (from.Length > 0 || to.Length > 0))
        {
            return Invalid("/filters", "A date range preset cannot be combined with from and to.");
        }

        if (from.Length > 0 && to.Length > 0
            && DateTimeOffset.TryParse(from, CultureInfo.InvariantCulture, DateTimeStyles.None, out var start)
            && DateTimeOffset.TryParse(to, CultureInfo.InvariantCulture, DateTimeStyles.None, out var end)
            && end < start)
        {
            return Invalid("/filters/to", "The end of the period is before its start.");
        }

        var planted = Records(root, StatsFile).FirstOrDefault(entry => Matches(entry, "sequenceId", sequenceId));
        if (planted is not null)
        {
            var answer = new JsonObject();
            foreach (var (name, value) in planted)
            {
                if (name != "sequenceId")
                {
                    answer[name] = value?.DeepClone();
                }
            }

            return (200, answer);
        }

        return (200, new JsonObject
        {
            ["emailOverview"] = Overview(
                "contacted", "delivered", "opened", "replied", "interested", "notReached", "optedOut", "outOfOffice", "bounced",
                "autoReplied", "meetingsBooked", "deliveredPercentage", "openedPercentage", "repliedPercentage",
                "interestedPercentage", "notReachedPercentage", "optedOutPercentage", "outOfOfficePercentage",
                "bouncedPercentage", "autoRepliedPercentage", "meetingsBookedPercentage"),
            ["linkedInOverview"] = Overview(
                "connectionsSent", "connectionsAccepted", "messagesSent", "replied", "inMailsSent", "inMailsReplied",
                "connectionNotesSent", "connectionNotesReplied", "profileViews", "likes", "follows", "endorses", "comments",
                "regularMessagesSent", "regularMessagesReplied", "connectionsAcceptedPercentage", "repliedPercentage",
                "inMailsRepliedPercentage", "connectionNotesRepliedPercentage", "regularMessagesRepliedPercentage"),
        });
    }

    /// <summary>
    /// The inbox threads a filter matches, the one that moved last first, each in the list's lighter shape: the whole
    /// party Reply holds — address, phone, profile URL, company and title — and no message history. A contact
    /// identifier this account does not hold is Reply's validation problem, a 400 with no code of its own, as a live
    /// account answered it; a field the filter does not define is one too.
    /// </summary>
    private static (int, JsonNode?) FilterThreads(string root, string query, string? body)
    {
        if (Request(body) is not { } filter)
        {
            return Invalid("/", "A JSON object is required.");
        }

        string[] known = ["contactIds", "sequenceIds", "source", "from", "to", "channels", "search"];
        if (filter.FirstOrDefault(member => !known.Contains(member.Key)) is { Key: { } stray })
        {
            return Invalid("/" + stray, "Field is not recognized.");
        }

        var contacts = Read(root, ContactsFile);
        var people = Identifiers(body, "contactIds");
        if (people is not null && people.FirstOrDefault(person => Find(contacts, person) is null) is var missing and > 0)
        {
            return Invalid("/contactIds", $"Contact {missing} was not found.");
        }

        var sequences = Identifiers(body, "sequenceIds");
        var source = Text(filter, "source");
        var since = DateTimeOffset.TryParse(Text(filter, "from"), CultureInfo.InvariantCulture, DateTimeStyles.None, out var from)
            ? from
            : DateTimeOffset.MinValue;

        var matched = Records(root, ThreadsFile)
            .Where(thread => people is null || (thread["contact"]?["id"] is JsonValue id && people.Contains(id.GetValue<int>())))
            .Where(thread => sequences is null || (thread["sequence"]?["id"] is JsonValue sequence && sequences.Contains(sequence.GetValue<int>())))
            .Where(thread => source != "inbox" || (thread["messages"] as JsonArray ?? []).Any(message => message?["isOutbound"] is JsonValue outbound && !outbound.GetValue<bool>()))
            .Where(thread => LastActivity(thread) >= since)
            .OrderByDescending(LastActivity)
            .Select(thread => (JsonNode)Listed(thread))
            .ToList();

        var skip = Count(query, "skip") ?? 0;
        var top = Count(query, "top") ?? 25;
        var page = new JsonArray([.. matched.Skip(skip).Take(top).Select(thread => thread!.DeepClone())]);
        return (200, new JsonObject { ["items"] = page, ["hasMore"] = matched.Count > skip + page.Count });
    }

    /// <summary>
    /// One thread in full, as Reply answers it: no subject of its own, the drafted answer if there is one, and the
    /// whole message history inline — which is what makes a long thread's answer long whatever page is asked for.
    /// </summary>
    private static (int, JsonNode?) InboxThread(string root, string threadId)
    {
        if (Find(root, ThreadsFile, threadId) is not { } thread)
        {
            return (404, Problem(404, "Not Found", "inboxThread.notFound", $"This account holds no thread {threadId}."));
        }

        var whole = new JsonObject();
        foreach (var name in new[] { "id", "channel", "isRead", "contact", "emailAccountId", "linkedInAccountId", "sequence", "messages", "aiDraft", "category", "hasMeetingIntent" })
        {
            if (thread.TryGetPropertyValue(name, out var value))
            {
                whole[name] = value?.DeepClone();
            }
        }

        return (200, whole);
    }

    /// <summary>One page of a thread's messages, the first sent first, paged with <c>top</c> and <c>skip</c>.</summary>
    private static (int, JsonNode?) ThreadMessages(string root, string threadId, string query)
    {
        if (Find(root, ThreadsFile, threadId) is not { } thread)
        {
            return (404, Problem(404, "Not Found", "inboxThread.notFound", $"This account holds no thread {threadId}."));
        }

        var messages = thread["messages"] as JsonArray ?? [];
        var skip = Count(query, "skip") ?? 0;
        var top = Count(query, "top") ?? 25;
        var page = new JsonArray([.. messages.Skip(skip).Take(top).Select(entry => entry!.DeepClone())]);
        return (200, new JsonObject { ["items"] = page, ["hasMore"] = messages.Count > skip + page.Count });
    }

    private static DateTimeOffset LastActivity(JsonObject thread) =>
        DateTimeOffset.TryParse(Text(thread, "lastActivityDate"), CultureInfo.InvariantCulture, DateTimeStyles.None, out var at)
            ? at
            : DateTimeOffset.MinValue;

    /// <summary>A thread as the list and the filter answer it: everything but the history, the draft and the accounts.</summary>
    private static JsonObject Listed(JsonObject thread)
    {
        var listed = new JsonObject();
        foreach (var name in new[] { "id", "channel", "isRead", "subject", "bodyPreview", "lastActivityDate", "isLastMessagePlanned", "contact", "sequence", "category", "hasMeetingIntent", "status" })
        {
            if (thread.TryGetPropertyValue(name, out var value))
            {
                listed[name] = value?.DeepClone();
            }
        }

        return listed;
    }

    private static JsonObject Overview(params string[] names)
    {
        var overview = new JsonObject();
        foreach (var name in names)
        {
            overview[name] = 0;
        }

        return overview;
    }

    /// <summary>When a person was added, for the order the list answers in; one planted without a date sorts last.</summary>
    private static DateTimeOffset Added(JsonObject enrollment) =>
        DateTimeOffset.TryParse(Text(enrollment, "addingDate"), CultureInfo.InvariantCulture, DateTimeStyles.None, out var added)
            ? added
            : DateTimeOffset.MinValue;

    private static JsonObject Member(JsonObject enrollment, JsonObject? contact, HashSet<int> suppressed)
    {
        var contactId = enrollment["contactId"]!.GetValue<int>();
        JsonNode step = enrollment["currentStep"] switch
        {
            JsonObject planted => planted.DeepClone(),
            JsonValue number when number.TryGetValue<int>(out var position) =>
                new JsonObject { ["stepId"] = 9900 + position, ["displayName"] = position.ToString(CultureInfo.InvariantCulture) },
            _ => new JsonObject { ["stepId"] = null, ["displayName"] = "Finished" },
        };

        return new JsonObject
        {
            ["contactId"] = contactId,
            ["sequenceId"] = enrollment["sequenceId"]!.GetValue<int>(),
            ["email"] = contact?["email"]?.DeepClone(),
            ["firstName"] = contact?["firstName"]?.DeepClone(),
            ["lastName"] = contact?["lastName"]?.DeepClone() ?? JsonValue.Create(string.Empty),
            ["company"] = contact?["company"]?.DeepClone(),
            ["title"] = contact?["title"]?.DeepClone(),
            ["statusInSequence"] = Text(enrollment, "statusInSequence"),
            ["currentStep"] = step,
            ["addingDate"] = enrollment["addingDate"]?.DeepClone() ?? JsonValue.Create("2026-10-02T09:15:42.17+03:00"),
            ["isOptedOut"] = suppressed.Contains(contactId),
            ["callStatus"] = contact?["callStatus"]?.DeepClone() ?? JsonValue.Create("none"),
            ["meetingStatus"] = contact?["meetingStatus"]?.DeepClone() ?? JsonValue.Create("none"),
            ["emailDisposition"] = enrollment["emailDisposition"]?.DeepClone(),
            ["emailAccountId"] = 77,
            ["linkedInAccountId"] = null,
        };
    }

    private static (int, JsonNode?) Enroll(string root, string sequenceId, string? body)
    {
        if (Find(root, SequencesFile, sequenceId) is not { } sequence)
        {
            return (404, Problem(404, "Not Found", "sequence.notFound", $"This account holds no sequence {sequenceId}."));
        }

        if (Flag(sequence, "isArchived"))
        {
            // A business rejection: a code and no errors[], which is the half of the overloaded 400 that a
            // validation failure never carries.
            return (400, Problem(400, "Bad Request", "sequence.archived", $"Sequence {sequenceId} is archived and takes no contacts."));
        }

        if (Identifiers(body, "contactIds") is not { } contactIds)
        {
            return Invalid("/contactIds", "An array of contact identifiers is required.");
        }

        var contacts = Read(root, ContactsFile);
        var enrollments = Read(root, EnrollmentsFile);
        var suppressed = OptOuts(root);
        var number = Number(sequenceId)!.Value;
        var added = new JsonArray();
        var notProcessed = new JsonObject();

        // Reply's own switch, honoured here rather than ignored: it takes the person out of every other
        // sequence they are in. A stand-in that dropped it would leave a plugin free to ask for it with nothing
        // to show the difference, and this is the one request whose damage is silent.
        if (Request(body)?["removeFromExisting"] is JsonValue switched
            && switched.TryGetValue<bool>(out var removeFromExisting)
            && removeFromExisting)
        {
            foreach (var elsewhere in enrollments.OfType<JsonObject>().ToList())
            {
                if (elsewhere["sequenceId"]!.GetValue<int>() != number
                    && contactIds.Contains(elsewhere["contactId"]!.GetValue<int>()))
                {
                    enrollments.Remove(elsewhere);
                }
            }
        }

        foreach (var contactId in contactIds)
        {
            var key = contactId.ToString(CultureInfo.InvariantCulture);
            if (Find(contacts, contactId) is null)
            {
                notProcessed[key] = Failure("contactNotFound", $"This account holds no contact {key}.");
            }
            else if (suppressed.Contains(contactId))
            {
                notProcessed[key] = Failure("contactOptedOut", $"Contact {key} has opted out.");
            }
            else if (enrollments.OfType<JsonObject>().Any(entry =>
                entry["sequenceId"]!.GetValue<int>() == number && entry["contactId"]!.GetValue<int>() == contactId))
            {
                notProcessed[key] = Failure("contactAlreadyInSequence", $"Contact {key} already takes part in sequence {sequenceId}.");
            }
            else
            {
                enrollments.Add(new JsonObject
                {
                    ["sequenceId"] = number,
                    ["contactId"] = contactId,
                    ["statusInSequence"] = "active",
                    ["currentStep"] = 1,
                });
                added.Add(contactId);
            }
        }

        Write(root, EnrollmentsFile, enrollments);

        // Per-item failure inside a 200: the status says the call was taken, not that the work was done.
        return (200, new JsonObject { ["added"] = added, ["notProcessed"] = notProcessed });
    }

    private static (int, JsonNode?) AddToList(string root, string listId, string? body)
    {
        var lists = Read(root, ListsFile);
        if (Find(lists, Number(listId)) is not { } list)
        {
            return (404, Problem(404, "Not Found", "contactList.notFound", $"This account holds no contact list {listId}."));
        }

        if (Identifiers(body, "contactIds") is not { } contactIds)
        {
            return Invalid("/contactIds", "An array of contact identifiers is required.");
        }

        var contacts = Read(root, ContactsFile);
        var suppressed = OptOuts(root);
        var members = list["members"]!.AsArray();
        var failures = new JsonObject();

        foreach (var contactId in contactIds)
        {
            var key = contactId.ToString(CultureInfo.InvariantCulture);
            if (Find(contacts, contactId) is null || suppressed.Contains(contactId))
            {
                // The documentation contradicts itself about what this value is, so the key's presence is the
                // failure and the value is never worth branching on.
                failures[key] = "contactNotProcessed";
            }
            else if (!members.Any(member => member!.GetValue<int>() == contactId))
            {
                members.Add(contactId);
            }

            // A contact already on the list is not reported at all — re-adding is documented nowhere, and this
            // silence is why the only sound way to answer "was it already a member" is a recovery read.
        }

        Write(root, ListsFile, lists);
        return (200, failures);
    }

    private static (int, JsonNode?) CreateContact(string root, string? body)
    {
        if (Request(body) is not { } request)
        {
            return Invalid("/", "A JSON object is required.");
        }

        var email = Text(request, "email");
        if (email.Length == 0)
        {
            // A validation problem: errors[] and no code, which is the other half of the overloaded 400.
            return Invalid("/email", "An email address is required.");
        }

        var contacts = Read(root, ContactsFile);
        if (contacts.OfType<JsonObject>().Any(contact => string.Equals(Text(contact, "email"), email, StringComparison.OrdinalIgnoreCase)))
        {
            // Reply publishes no code for a duplicate address on this path. This one is invented, and a plugin
            // that mapped it by name would be mapping a guess: an unrecognised code belongs in a default branch.
            return (400, Problem(400, "Bad Request", "contact.duplicate", $"This account already holds a contact for {email}."));
        }

        var created = new JsonObject
        {
            ["id"] = NextId(contacts),
            ["email"] = email,
            ["firstName"] = request["firstName"]?.DeepClone(),
            ["lastName"] = request["lastName"]?.DeepClone(),
            ["callStatus"] = "none",
            ["meetingStatus"] = "none",
        };

        contacts.Add(created);
        Write(root, ContactsFile, contacts);
        return (201, created.DeepClone());
    }

    private static (int, JsonNode?) Import(string root, string? body)
    {
        if (Request(body) is not { } request || request["items"] is not JsonArray items)
        {
            return Invalid("/items", "An array of contacts is required.");
        }

        var contacts = Read(root, ContactsFile);
        var answered = new JsonArray();
        var seen = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        int created = 0, updated = 0, skipped = 0, failed = 0;

        foreach (var node in items)
        {
            var item = node as JsonObject;
            var email = item is null ? string.Empty : Text(item, "email");
            var firstName = item is null ? string.Empty : Text(item, "firstName");

            if (email.Length == 0 || firstName.Length == 0)
            {
                // The rule that decides how a contact is ensured: Reply refuses an item with no first name, and
                // Jason's own contact projection allows none, so this path cannot serve every contact.
                answered.Add(new JsonObject { ["id"] = null, ["status"] = "failed", ["error"] = "Invalid data" });
                failed++;
                continue;
            }

            if (seen.TryGetValue(email, out var already))
            {
                answered.Add(new JsonObject { ["id"] = already, ["status"] = "skipped", ["error"] = null });
                skipped++;
                continue;
            }

            var existing = contacts.OfType<JsonObject>()
                .FirstOrDefault(contact => string.Equals(Text(contact, "email"), email, StringComparison.OrdinalIgnoreCase));

            if (existing is not null)
            {
                // Email is the deduplication key, which is what makes this one call create or match.
                existing["firstName"] = firstName;
                var id = existing["id"]!.GetValue<int>();
                seen[email] = id;
                answered.Add(new JsonObject { ["id"] = id, ["status"] = "updated", ["error"] = null });
                updated++;
            }
            else
            {
                var id = NextId(contacts);
                contacts.Add(new JsonObject
                {
                    ["id"] = id,
                    ["email"] = email,
                    ["firstName"] = firstName,
                    ["lastName"] = item!["lastName"]?.DeepClone(),
                    ["callStatus"] = "none",
                    ["meetingStatus"] = "none",
                });
                seen[email] = id;
                answered.Add(new JsonObject { ["id"] = id, ["status"] = "created", ["error"] = null });
                created++;
            }
        }

        Write(root, ContactsFile, contacts);

        // Synchronously, with the identifiers positionally aligned to the request: no background job, no
        // Location header, nothing to poll.
        return (200, new JsonObject
        {
            ["items"] = answered,
            ["added"] = created,
            ["updated"] = updated,
            ["skipped"] = skipped,
            ["failed"] = failed,
        });
    }

    private static (int, JsonNode?) Statuses(string root, string contactId)
    {
        if (Find(root, ContactsFile, contactId) is not { } contact)
        {
            // The same call answers the pin-validity question: a pin that no longer resolves is a 404.
            return (404, Problem(404, "Not Found", "contact.notFound", $"This account holds no contact {contactId}."));
        }

        var id = contact["id"]!.GetValue<int>();
        var sequences = Records(root, SequencesFile).ToList();
        var taking = new JsonArray();
        foreach (var enrollment in Records(root, EnrollmentsFile).Where(entry => entry["contactId"]!.GetValue<int>() == id))
        {
            var sequenceId = enrollment["sequenceId"]!.GetValue<int>();
            taking.Add(new JsonObject
            {
                ["id"] = sequenceId,
                ["name"] = Text(sequences.FirstOrDefault(sequence => sequence["id"]!.GetValue<int>() == sequenceId), "name"),
                ["statusInSequence"] = Text(enrollment, "statusInSequence"),
            });
        }

        return (200, new JsonObject
        {
            ["contactId"] = id,
            ["isOptedOut"] = OptOuts(root).Contains(id),
            ["callStatus"] = Text(contact, "callStatus"),
            ["meetingStatus"] = Text(contact, "meetingStatus"),
            ["sequences"] = taking,
        });
    }

    /// <summary>
    /// The lists this person is on, as Reply answers it — which is to say: only the shared ones. Against a real
    /// account this endpoint answered with an empty array for a contact provably on a list that was not shared,
    /// checked twice a minute apart and confirmed from the other side by the search below. That is why the
    /// package does not perform its recovery read here, and modelling the endpoint as it really behaves is what
    /// keeps the offline tests from proving something the provider does not do.
    /// </summary>
    private static (int, JsonNode?) ListsOf(string root, string contactId)
    {
        if (Find(root, ContactsFile, contactId) is not { } contact)
        {
            return (404, Problem(404, "Not Found", "contact.notFound", $"This account holds no contact {contactId}."));
        }

        var id = contact["id"]!.GetValue<int>();
        var lists = new JsonArray();
        foreach (var list in Records(root, ListsFile))
        {
            if (Flag(list, "isShared") && list["members"]!.AsArray().Any(member => member!.GetValue<int>() == id))
            {
                lists.Add(new JsonObject { ["id"] = list["id"]!.GetValue<int>(), ["name"] = Text(list, "name") });
            }
        }

        // A bare array, with no envelope around it: one of the shapes that makes reading Reply's answers a
        // per-path question rather than a general rule.
        return (200, lists);
    }

    /// <summary>
    /// The contacts search, paged with <c>top</c> and <c>skip</c>, as a live account answered it. Scoped to one
    /// list, it is the read the package performs to answer whether a person is already on a list, and it answers
    /// for a private list as readily as for a shared one — the difference that made it the read. With a
    /// <c>searchTerm</c>, it answers every contact whose address contains the term, whatever the case of either:
    /// Reply's own search reaches further than the address, which only adds near matches a caller has to tell apart
    /// anyway. A rule set is ignored, as the live account ignored it, and so is anything else the body carries.
    /// </summary>
    private static (int, JsonNode?) Filter(string root, string query, string? body)
    {
        var request = Request(body);
        var contacts = Read(root, ContactsFile);
        IEnumerable<JsonObject> found = contacts.OfType<JsonObject>();

        if (request?["listId"] is JsonValue named && named.TryGetValue<int>(out var listId))
        {
            var members = Find(Read(root, ListsFile), listId) is { } list
                ? list["members"]!.AsArray().Select(member => member!.GetValue<int>()).ToList()
                : [];
            found = members.Select(member => Find(contacts, member)).Where(contact => contact is not null).Select(contact => contact!);
        }

        if (request?["searchTerm"] is JsonValue searched && searched.TryGetValue<string>(out var term) && term.Length > 0)
        {
            found = found.Where(contact =>
                contact["email"] is JsonValue email && email.TryGetValue<string>(out var address)
                && address.Contains(term, StringComparison.OrdinalIgnoreCase));
        }

        var matched = found.ToList();

        var skip = Count(query, "skip") ?? 0;
        var top = Count(query, "top") ?? 25;
        var page = new JsonArray([.. matched.Skip(skip).Take(top).Select(contact => (JsonNode)contact.DeepClone())]);
        return (200, new JsonObject { ["items"] = page, ["hasMore"] = matched.Count > skip + page.Count });
    }

    private static (int, JsonNode?) ContactLists(string root, string query) =>
        Page(
            [.. Records(root, ListsFile).Select(list => (JsonNode)new JsonObject
            {
                ["id"] = list["id"]!.GetValue<int>(),
                ["name"] = Text(list, "name"),
            })],
            query);

    /// <summary>The paged envelope: the items asked for, and whether the account holds more of them.</summary>
    private static (int, JsonNode?) Page(JsonArray items, string query)
    {
        var limit = Limit(query);
        var page = new JsonArray([.. items.Take(limit ?? items.Count).Select(item => item!.DeepClone())]);
        return (200, new JsonObject { ["items"] = page, ["hasMore"] = limit is not null && items.Count > limit });
    }

    // -------------------------------------------------------------------------------------------------------
    // Answering, and the instructions a test leaves in the directory
    // -------------------------------------------------------------------------------------------------------

    private static async Task<int> AnswerAsync(int code, JsonNode? body)
    {
        await Console.Out.WriteLineAsync(new JsonObject { ["code"] = code, ["data"] = body }.ToJsonString());
        await Console.Out.FlushAsync();

        // The exit code says only which side of 400 the status fell on; the status itself is on stdout.
        return code >= 400 ? 1 : 0;
    }

    /// <summary>
    /// The first instruction that names this call, spent as it is taken, so the attempt after it sees the
    /// account again. A test scripts a failing attempt and a recovering one by scripting the first alone.
    /// </summary>
    private static JsonObject? Take(string root, string method, string path)
    {
        var file = Path.Combine(root, InstructionsFile);
        if (!File.Exists(file)
            || JsonNode.Parse(File.ReadAllText(file)) is not JsonObject instructions
            || instructions["scripted"] is not JsonArray scripted)
        {
            return null;
        }

        for (var position = 0; position < scripted.Count; position++)
        {
            if (scripted[position] is not JsonObject instruction
                || !string.Equals(Text(instruction, "method"), method, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(Text(instruction, "path"), path, StringComparison.Ordinal))
            {
                continue;
            }

            scripted.RemoveAt(position);
            File.WriteAllText(file, instructions.ToJsonString(), new UTF8Encoding(false));
            return instruction;
        }

        return null;
    }

    /// <summary>
    /// A body a test wrote out by hand. It is answered as it stands — as JSON when it is JSON, as the string it
    /// is when it is not — because an answer no provider would send is exactly what some tests are about.
    /// </summary>
    private static JsonNode? Scripted(string body)
    {
        if (body.Trim().Length == 0)
        {
            return null;
        }

        try
        {
            return JsonNode.Parse(body);
        }
        catch (JsonException)
        {
            return JsonValue.Create(body);
        }
    }

    private static void Record(string root, string method, string path, string? body, string[] argv)
    {
        var entry = new JsonObject
        {
            ["method"] = method,
            ["path"] = path,
            ["body"] = body,
            ["args"] = new JsonArray([.. argv.Select(argument => (JsonNode)JsonValue.Create(argument))]),
            ["env"] = VendorVariables(),
        };

        Append(Path.Combine(root, CallsFile), entry.ToJsonString() + "\n");
    }

    /// <summary>
    /// Every vendor-named variable this process was given, by name and value. Only the ones named after this
    /// vendor, because that is the whole of the question: an operator's own <c>REPLY_API_KEY</c> or
    /// <c>REPLY_TEAM_ID</c> must not reach a child, and the one name that legitimately does is the one the
    /// package itself sets on the call. Copying the rest of a machine's environment into a file would answer
    /// nothing and would put whatever a developer exported into it.
    /// </summary>
    private static JsonObject VendorVariables()
    {
        var seen = new JsonObject();
        foreach (DictionaryEntry variable in Environment.GetEnvironmentVariables())
        {
            var name = variable.Key.ToString() ?? string.Empty;
            if (name.StartsWith("REPLY_", StringComparison.OrdinalIgnoreCase))
            {
                seen[name] = variable.Value?.ToString();
            }
        }

        return seen;
    }

    /// <summary>
    /// Appends one line to the log. Appended rather than rewritten, because two invocations of this program can
    /// be in flight at once and a read-modify-write would quietly lose one of them.
    /// </summary>
    private static void Append(string file, string line)
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                using var stream = new FileStream(file, FileMode.Append, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
                using var writer = new StreamWriter(stream, new UTF8Encoding(false));
                writer.Write(line);
                return;
            }
            catch (IOException) when (attempt < 100)
            {
                Thread.Sleep(20);
            }
        }
    }

    // -------------------------------------------------------------------------------------------------------
    // The files, and the small readings of them
    // -------------------------------------------------------------------------------------------------------

    private static JsonArray Read(string root, string fileName)
    {
        var file = Path.Combine(root, fileName);
        return (File.Exists(file) ? JsonNode.Parse(File.ReadAllText(file)) as JsonArray : null) ?? [];
    }

    private static void Write(string root, string fileName, JsonNode content) =>
        File.WriteAllText(Path.Combine(root, fileName), content.ToJsonString(), new UTF8Encoding(false));

    private static IEnumerable<JsonObject> Records(string root, string fileName) => Read(root, fileName).OfType<JsonObject>();

    private static HashSet<int> OptOuts(string root) =>
        [.. Read(root, OptOutsFile).Select(entry => entry!.GetValue<int>())];

    private static JsonObject? Find(string root, string fileName, string id) => Find(Read(root, fileName), Number(id));

    private static JsonObject? Find(JsonArray records, int? id) =>
        id is null ? null : records.OfType<JsonObject>().FirstOrDefault(record => record["id"]!.GetValue<int>() == id);

    private static bool Matches(JsonObject record, string member, string id) =>
        Number(id) is { } number && record[member]!.GetValue<int>() == number;

    private static int NextId(JsonArray contacts) =>
        contacts.Count == 0 ? FirstContactId : contacts.OfType<JsonObject>().Max(contact => contact["id"]!.GetValue<int>()) + 1;

    private static JsonObject? Request(string? body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return null;
        }

        try
        {
            return JsonNode.Parse(body) as JsonObject;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static IReadOnlyList<int>? Identifiers(string? body, string member)
    {
        if (Request(body) is not { } request || request[member] is not JsonArray array)
        {
            return null;
        }

        var identifiers = new List<int>();
        foreach (var entry in array)
        {
            if (entry is not JsonValue value || !value.TryGetValue<int>(out var identifier))
            {
                return null;
            }

            identifiers.Add(identifier);
        }

        return identifiers;
    }

    /// <summary>One numeric query parameter, by name, or nothing where the call did not carry it.</summary>
    private static int? Count(string query, string name)
    {
        foreach (var pair in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var separator = pair.IndexOf('=', StringComparison.Ordinal);
            if (separator > 0
                && string.Equals(pair[..separator], name, StringComparison.Ordinal)
                && int.TryParse(pair[(separator + 1)..], NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
            {
                return value;
            }
        }

        return null;
    }

    private static int? Limit(string query)
    {
        foreach (var pair in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var separator = pair.IndexOf('=', StringComparison.Ordinal);
            if (separator > 0
                && string.Equals(pair[..separator], "limit", StringComparison.Ordinal)
                && int.TryParse(pair[(separator + 1)..], NumberStyles.Integer, CultureInfo.InvariantCulture, out var limit))
            {
                return limit;
            }
        }

        return null;
    }

    private static JsonObject Problem(int status, string title, string? code, string detail)
    {
        var problem = new JsonObject
        {
            ["type"] = "about:blank",
            ["title"] = title,
            ["status"] = status,
            ["detail"] = detail,
        };

        if (code is not null)
        {
            problem["code"] = code;
        }

        return problem;
    }

    private static (int, JsonNode?) Invalid(string pointer, string detail) => (400, new JsonObject
    {
        ["type"] = "about:blank",
        ["title"] = "Bad Request",
        ["status"] = 400,
        ["errors"] = new JsonArray(new JsonObject { ["pointer"] = pointer, ["detail"] = detail }),
    });

    /// <summary>
    /// One person the bulk enrol would not take. The slugs are the ones the published table names — it is
    /// labelled "Common" and the type behind it is declared nowhere, so <c>contactOptedOut</c> stands for the
    /// open half of that set: a real word this stand-in answers with and no published table maps.
    /// </summary>
    private static JsonObject Failure(string error, string details) =>
        new() { ["error"] = error, ["errorDetails"] = details };

    private static string Text(JsonObject? parent, string name) =>
        parent?[name] is JsonValue value && value.TryGetValue<string>(out var text) ? text : string.Empty;

    private static bool Flag(JsonObject parent, string name) =>
        parent[name] is JsonValue value && value.TryGetValue<bool>(out var flag) && flag;

    private static int Number(JsonObject parent, string name) =>
        parent[name] is JsonValue value && value.TryGetValue<int>(out var number) ? number : 0;

    private static int? Number(string value) =>
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number) ? number : null;
}
