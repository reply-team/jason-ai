using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

// Deliberately in the global namespace with the rest of this program.

/// <summary>
/// The work commands, answered as reply-cli 0.14.0 documents them, from the same files the account keeps. What each one
/// answers is reply-cli's contract — its result, its error code, its exit code — and never how reply-cli gets there: no
/// Reply endpoint is imitated here.
/// </summary>
internal static partial class Account
{
    private static readonly string[] PersonFields = ["email", "linkedin_url", "first_name", "last_name", "company", "title"];
    private static readonly string[] Presets = ["last-week", "last-month", "last-year", "all-time"];
    private static readonly string[] Sources = ["inbox", "sent", "unread", "ai-draft"];

    /// <summary>What one command ends with: its exit code, and the result it prints or the error it explains.</summary>
    private sealed record Ending(int Exit, JsonNode? Result, JsonObject? Error);

    /// <summary>A contact as reply-cli takes one: an id, or a person's fields.</summary>
    private sealed record ContactRef(int? Id, JsonObject? Person);

    public static async Task<int> RunCommandAsync(string profile, string command, string[] rest, string? stdin, string[] argv)
    {
        var store = Locate();
        if (!File.Exists(Path.Combine(store, MarkerFile)))
        {
            // Not a store this stand-in planted: refused before the directory is even created.
            return await PrintAsync(Refused("usage.store", $"No '{MarkerFile}' under '{store}'. A test sets the configuration directory; this one did not.", exit: 2));
        }

        var root = Path.Combine(store, profile);
        Directory.CreateDirectory(root);
        var line = Commands.Parse(command, rest);
        RecordCommand(root, command, line.Operands, stdin, argv);

        if (!File.Exists(Path.Combine(root, CredentialFile)))
        {
            return await PrintAsync(Refused("auth.required", "Not authenticated.", exit: 2));
        }

        if (line.UsageError is { } usage)
        {
            await Console.Error.WriteLineAsync("error: " + usage);
            return 2;
        }

        var instruction = TakeCommand(root, command, argv);
        switch (instruction is null ? string.Empty : Text(instruction, "kind"))
        {
            case "prints":
                await Console.Out.WriteAsync(Text(instruction!, "stdout"));
                await Console.Out.FlushAsync();
                if (Text(instruction!, "stderr") is { Length: > 0 } said)
                {
                    await Console.Error.WriteLineAsync(said);
                }

                return Number(instruction!, "exit_code");

            case "hang":
                await Task.Delay(Number(instruction!, "milliseconds"));
                break;

            case "hold":
                await HoldAsync(instruction!);
                break;

            case "lost":
                {
                    // Everything the command would have done is done, and then the answer is gone. What the CLI learned
                    // goes with it — except a sequence it was making, whose identifier only the lost answer carried.
                    // A send says which contact and account it was about, as reply-cli does for every ending of one.
                    var effect = Handle(root, command, line, stdin);
                    var learned = command == "sequence create"
                        ? null
                        : IsDirectSend(command)
                            ? DirectSendIds(command, line)
                            : effect.Result is JsonObject done ? IdsOf(done) : effect.Error?["ids"]?.DeepClone() as JsonObject;
                    return await PrintAsync(Refused(
                        "outcome.unknown", "The request may have reached Reply, but no answer came back.", exit: 3, ids: learned));
                }
        }

        return await PrintAsync(Handle(root, command, line, stdin));
    }

    private static Ending Handle(string root, string command, Parsed line, string? stdin) => command switch
    {
        "sequence list" => SequenceListByName(root, line, stdin),
        "sequence create" => SequenceCreateByName(root, line, stdin),
        "sequence get" => SequenceGet(root, line),
        "sequence contacts" => SequencePeople(root, line),
        "sequence stats" => SequenceCounts(root, line),
        "sequence add-contact" => SequenceAddContact(root, line, stdin),
        "sequence remove-contact" => SequenceRemoveContact(root, line, stdin),
        "sequence pause" => SequencePause(root, line),
        "sequence start" => SequenceStart(root, line),
        "contact-list add-contact" => ListAddContact(root, line, stdin),
        "contact find" => ContactFind(root, line, stdin),
        "contact get" => ContactGet(root, line),
        "contact update" => ContactUpdate(root, line, stdin),
        "contact delete" => ContactDelete(root, line),
        "contact opt-out" => ContactOptOut(root, line, stdin),
        "inbox list" => InboxList(root, line),
        "inbox get" => InboxGet(root, line),
        "inbox reply" => InboxReply(root, line, stdin),
        "inbox categories" => InboxCategories(root),
        "inbox categorize" => InboxCategorize(root, line),
        "inbox mark-read" => InboxMark(root, line, read: true),
        "inbox mark-unread" => InboxMark(root, line, read: false),
        "contact linkedin-connect" or "contact send-email" or "contact linkedin-message" or "contact linkedin-inmail" or "contact linkedin-voice"
            => DirectSendCommand(root, command, line, stdin),
        "account list" => AccountList(root, line),
        "auth whoami" => AuthWhoami(root),
        _ => throw new InvalidOperationException($"No handler for '{command}'."),
    };

    // -------------------------------------------------------------------------------------------------------
    // The commands
    // -------------------------------------------------------------------------------------------------------

    private static Ending SequenceGet(string root, Parsed line)
    {
        if (IdOf(line.Operands[0]) is not { } id)
        {
            return BadId("Sequence id");
        }

        return Find(Read(root, SequencesFile), id) is { } sequence ? Done(Shown(sequence)) : NoSequence(id);
    }

    private static Ending SequencePeople(string root, Parsed line)
    {
        if (IdOf(line.Operands[0]) is not { } id)
        {
            return BadId("Sequence id");
        }

        var (limit, badLimit) = RangeOf(line, "--limit", 1, 1000, 100);
        if (badLimit is not null)
        {
            return badLimit;
        }

        var (offset, badOffset) = RangeOf(line, "--offset", 0, 1_000_000, 0);
        if (badOffset is not null)
        {
            return badOffset;
        }

        if (Find(Read(root, SequencesFile), id) is null)
        {
            return NoSequence(id);
        }

        var key = Key(id);
        var contacts = Read(root, ContactsFile);
        var suppressed = OptOuts(root);
        var rows = Records(root, EnrollmentsFile)
            .Select((entry, position) => (Entry: entry, Position: position))
            .Where(row => Matches(row.Entry, "sequenceId", key))
            .OrderByDescending(row => Added(row.Entry))
            .ThenByDescending(row => row.Position)
            .Select(row => (JsonNode)Member(row.Entry, Find(contacts, row.Entry["contactId"]!.GetValue<int>()), suppressed))
            .ToList();
        var page = rows.Skip(offset).Take(limit).ToList();
        return Done(new JsonObject
        {
            ["items"] = new JsonArray([.. page]),
            ["has_more"] = rows.Count > offset + page.Count,
            ["total"] = rows.Count,
        });
    }

    private static Ending SequenceCounts(string root, Parsed line)
    {
        if (IdOf(line.Operands[0]) is not { } id)
        {
            return BadId("Sequence id");
        }

        var preset = line.Option("--preset");
        var from = line.Option("--from");
        var to = line.Option("--to");
        if (preset is not null && !Presets.Contains(preset))
        {
            return Refused("usage.choice", $"--preset must be one of: {string.Join(", ", Presets)}.", exit: 2);
        }

        var bySpan = from is not null && to is not null;
        if ((preset is not null) == bySpan || (from is null) != (to is null))
        {
            return Refused("usage.stats", "Give the window as --preset, or as both --from and --to.", exit: 2);
        }

        if (bySpan)
        {
            if (MomentOf(from!) is not { } start || MomentOf(to!) is not { } end)
            {
                return Refused("usage.date", "--from and --to must be dates and times, e.g. 2026-10-01T00:00:00Z.", exit: 2);
            }

            if (start >= end)
            {
                return Refused("usage.stats", "--to must be later than --from.", exit: 2);
            }
        }

        if (Find(Read(root, SequencesFile), id) is null)
        {
            return NoSequence(id);
        }

        var planted = Records(root, StatsFile).FirstOrDefault(entry => Matches(entry, "sequenceId", Key(id)));
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

            return Done(answer);
        }

        return Done(new JsonObject { ["emailOverview"] = Overview(EmailCounts), ["linkedInOverview"] = Overview(LinkedInCounts) });
    }

    private static Ending SequenceAddContact(string root, Parsed line, string? stdin)
    {
        if (IdOf(line.Operands[0]) is not { } id)
        {
            return BadId("Sequence id");
        }

        var (contact, badContact) = ContactOf(line, stdin);
        if (badContact is not null)
        {
            return badContact;
        }

        int? position = null;
        if (line.Option("--start-step") is not null)
        {
            var (step, badStep) = RangeOf(line, "--start-step", 1, 10_000, 1);
            if (badStep is not null)
            {
                return badStep;
            }

            position = step;
        }

        var ids = new JsonObject { ["sequence_id"] = id };
        if (Find(Read(root, SequencesFile), id) is not { } sequence)
        {
            return NoSequence(id, ids);
        }

        if (Flag(sequence, "isArchived"))
        {
            return Refused("sequence.archived", $"Sequence {id} is archived.", status: 200, ids: ids);
        }

        int? startStepId = null;
        if (position is { } wanted)
        {
            var (stepId, why) = StepAt(sequence, wanted);
            if (stepId is null)
            {
                return Refused("sequence.step_not_found", $"There is no step {wanted} to start at: {why}.", status: 200, ids: ids);
            }

            startStepId = stepId;
        }

        var (contactId, created, notImported) = Ensure(root, contact!, ids);
        if (notImported is not null)
        {
            return notImported;
        }

        ids["contact_id"] = contactId;
        var result = new JsonObject
        {
            ["status"] = "added",
            ["sequence_id"] = id,
            ["contact_id"] = contactId,
            ["contact_created"] = created,
            ["sequence_active"] = Text(sequence, "status") == "active",
        };

        var key = Key(id);
        var enrollments = Read(root, EnrollmentsFile);
        if (!created && enrollments.OfType<JsonObject>().Any(entry =>
                Matches(entry, "sequenceId", key) && entry["contactId"]!.GetValue<int>() == contactId))
        {
            result["status"] = "already_in_sequence";
            return Done(result);
        }

        if ((sequence["steps"] as JsonArray ?? []).Count == 0)
        {
            return Refused("sequence.has_no_steps", $"Sequence {id} has no step to send.", status: 400, replyCode: "sequenceContact.noStepsInSequence", ids: ids);
        }

        if (Find(Read(root, ContactsFile), contactId) is null)
        {
            return Refused("contact.not_found", $"This account has no contact {contactId}.", status: 404, replyCode: "contact.notFound", ids: ids);
        }

        if (OptOuts(root).Contains(contactId))
        {
            return Refused("contact.opted_out", $"Contact {contactId} is opted out, so Reply did not add them to the sequence.", status: 200, ids: ids);
        }

        enrollments.Add(new JsonObject
        {
            ["sequenceId"] = id,
            ["contactId"] = contactId,
            ["statusInSequence"] = "active",
            ["currentStep"] = startStepId is { } stepIdAdded
                ? new JsonObject { ["stepId"] = stepIdAdded, ["displayName"] = position!.Value.ToString(CultureInfo.InvariantCulture) }
                : JsonValue.Create(1),
        });
        Write(root, EnrollmentsFile, enrollments);
        return Done(result);
    }

    private static Ending SequenceRemoveContact(string root, Parsed line, string? stdin)
    {
        if (IdOf(line.Operands[0]) is not { } id)
        {
            return BadId("Sequence id");
        }

        var (contact, badContact) = ContactOf(line, stdin);
        if (badContact is not null)
        {
            return badContact;
        }

        var ids = new JsonObject { ["sequence_id"] = id };
        var (found, refused) = Lookup(root, contact!, ids);
        if (refused is not null)
        {
            return refused;
        }

        if (found is not { } contactId)
        {
            return Done(new JsonObject { ["status"] = "not_in_sequence", ["sequence_id"] = id, ["contact_id"] = null });
        }

        ids["contact_id"] = contactId;
        var key = Key(id);
        var enrollments = Read(root, EnrollmentsFile);
        var taking = enrollments.OfType<JsonObject>().FirstOrDefault(entry =>
            Matches(entry, "sequenceId", key) && entry["contactId"]!.GetValue<int>() == contactId);

        if (taking is null && Find(Read(root, SequencesFile), id) is null)
        {
            return NoSequence(id, ids);
        }

        if (taking is null || Text(taking, "statusInSequence") == "finished")
        {
            return Done(new JsonObject { ["status"] = "not_in_sequence", ["sequence_id"] = id, ["contact_id"] = contactId });
        }

        enrollments.Remove(taking);
        Write(root, EnrollmentsFile, enrollments);
        return Done(new JsonObject { ["status"] = "removed", ["sequence_id"] = id, ["contact_id"] = contactId });
    }

    private static Ending SequencePause(string root, Parsed line)
    {
        if (IdOf(line.Operands[0]) is not { } id)
        {
            return BadId("Sequence id");
        }

        var ids = new JsonObject { ["sequence_id"] = id };
        var sequences = Read(root, SequencesFile);
        if (Find(sequences, id) is not { } sequence)
        {
            return NoSequence(id, ids);
        }

        if (Flag(sequence, "isArchived"))
        {
            return Refused("sequence.archived", $"Sequence {id} is archived.", status: 409, replyCode: "sequenceAction.archived", ids: ids);
        }

        if (Text(sequence, "status") == "new")
        {
            return Refused(
                "sequence.not_pausable", "Reply will not pause this sequence.", status: 409, replyCode: "sequenceAction.status",
                detail: "Cannot pause a sequence because its current status is New", ids: ids);
        }

        sequence["status"] = "paused";
        Write(root, SequencesFile, sequences);
        return Done(new JsonObject { ["status"] = "paused", ["sequence_id"] = id });
    }

    private static Ending SequenceStart(string root, Parsed line)
    {
        if (IdOf(line.Operands[0]) is not { } id)
        {
            return BadId("Sequence id");
        }

        int? expected = null;
        if (line.Option("--expect-contacts") is not null)
        {
            var (count, badCount) = RangeOf(line, "--expect-contacts", 0, 10_000_000, 0);
            if (badCount is not null)
            {
                return badCount;
            }

            expected = count;
        }

        var ids = new JsonObject { ["sequence_id"] = id };
        var sequences = Read(root, SequencesFile);
        if (Find(sequences, id) is not { } sequence)
        {
            return NoSequence(id, ids);
        }

        var key = Key(id);
        var contacts = Records(root, EnrollmentsFile).Count(entry => Matches(entry, "sequenceId", key));
        string? previous = null;
        if (expected is { } wanted)
        {
            if (Flag(sequence, "isArchived"))
            {
                return Refused("sequence.archived", $"Sequence {id} is archived.", status: 200, ids: ids);
            }

            previous = Text(sequence, "status") is { Length: > 0 } state ? state : null;
            if (previous == "active")
            {
                return Done(new JsonObject
                {
                    ["status"] = "already_active",
                    ["sequence_id"] = id,
                    ["previous_status"] = previous,
                    ["contacts"] = contacts,
                });
            }

            if (contacts != wanted)
            {
                return Refused(
                    "sequence.contact_count_changed",
                    $"The sequence holds {contacts} contacts, not the {wanted} expected, so nothing was started.",
                    status: 200, ids: ids, details: new JsonObject { ["expected"] = wanted, ["actual"] = contacts });
            }
        }

        if (Flag(sequence, "isArchived"))
        {
            return Refused("sequence.archived", $"Sequence {id} is archived.", status: 409, replyCode: "sequenceAction.archived", ids: ids);
        }

        if (contacts == 0)
        {
            return Refused(
                "sequence.not_startable", "Reply will not start this sequence.", status: 400, replyCode: "sequenceAction.noContacts",
                detail: "Cannot start a sequence without contacts", ids: ids);
        }

        if (sequence[SendingAccounts] is JsonValue sending && sending.GetValue<int>() == 0)
        {
            return Refused(
                "sequence.not_startable", "Reply will not start this sequence.", status: 400, replyCode: "sequenceAction.unknown",
                detail: "Cannot start a sequence without selected email account", ids: ids);
        }

        sequence["status"] = "active";
        Write(root, SequencesFile, sequences);
        return Done(new JsonObject
        {
            ["status"] = "started",
            ["sequence_id"] = id,
            ["previous_status"] = previous,
            ["contacts"] = expected is null ? null : contacts,
        });
    }

    private static Ending ListAddContact(string root, Parsed line, string? stdin)
    {
        if (IdOf(line.Operands[0]) is not { } listId)
        {
            return BadId("List id");
        }

        var (contact, badContact) = ContactOf(line, stdin);
        if (badContact is not null)
        {
            return badContact;
        }

        var ids = new JsonObject { ["list_id"] = listId };
        var (contactId, created, notImported) = Ensure(root, contact!, ids);
        if (notImported is not null)
        {
            return notImported;
        }

        ids["contact_id"] = contactId;
        if (line.Has("--refuse-opted-out"))
        {
            if (Find(Read(root, ContactsFile), contactId) is null)
            {
                return Refused("contact.not_found", $"This account has no contact {contactId}.", status: 404, replyCode: "contact.notFound", ids: ids);
            }

            if (OptOuts(root).Contains(contactId))
            {
                return Refused("contact.opted_out", $"Contact {contactId} is opted out, so it was not added to the list.", status: 200, ids: ids);
            }
        }

        var lists = Read(root, ListsFile);
        if (Find(lists, listId) is not { } list)
        {
            return Refused("contact-list.not_found", $"This account has no contact list {listId}.", status: 404, replyCode: "contactsList.notFound", ids: ids);
        }

        if (Find(Read(root, ContactsFile), contactId) is null)
        {
            return Refused(
                "reply.refused", "Reply did not add the contact to the list.", status: 200, replyCode: "contactNotProcessed",
                detail: "Contact was not added to the list", ids: ids);
        }

        var members = list["members"]!.AsArray();
        if (!members.Any(member => member!.GetValue<int>() == contactId))
        {
            members.Add(contactId);
            Write(root, ListsFile, lists);
        }

        return Done(new JsonObject { ["status"] = "added", ["list_id"] = listId, ["contact_id"] = contactId, ["contact_created"] = created });
    }

    private static Ending ContactOptOut(string root, Parsed line, string? stdin)
    {
        var (contact, badContact) = ContactOf(line, stdin);
        if (badContact is not null)
        {
            return badContact;
        }

        var ids = new JsonObject();
        var (found, refused) = Lookup(root, contact!, ids);
        if (refused is not null)
        {
            return refused;
        }

        var created = false;
        int contactId;
        if (found is { } held)
        {
            contactId = held;
        }
        else
        {
            var (made, isNew, notImported) = Import(root, contact!.Person!, ids);
            if (notImported is not null)
            {
                return notImported;
            }

            contactId = made;
            created = isNew;
        }

        ids["contact_id"] = contactId;
        if (Find(Read(root, ContactsFile), contactId) is null)
        {
            return Refused("contact.not_found", $"This account has no contact {contactId}.", status: 200, replyCode: "notFound", ids: ids);
        }

        var register = Read(root, OptOutsFile);
        if (!register.Any(entry => entry!.GetValue<int>() == contactId))
        {
            register.Add(contactId);
            Write(root, OptOutsFile, register);
        }

        return Done(new JsonObject { ["status"] = "opted_out", ["contact_id"] = contactId, ["contact_created"] = created });
    }

    /// <summary>
    /// The contact Reply holds under one address, never creating one: <c>--contact -</c> and nothing else, then exactly
    /// one of <c>{email}</c> and <c>{linkedin_url}</c> on stdin. Null when Reply holds nobody there; two is
    /// <c>contact.not_unique</c>. No message repeats the address.
    /// </summary>
    private static Ending ContactFind(string root, Parsed line, string? stdin)
    {
        if (line.Option("--contact") != "-")
        {
            return Refused("usage.contact", "--contact takes only -: the address is read from stdin.", exit: 2);
        }

        JsonObject given;
        try
        {
            given = JsonNode.Parse(stdin ?? string.Empty) as JsonObject ?? throw new JsonException("not an object");
        }
        catch (JsonException)
        {
            return Refused("usage.contact", "stdin must be one JSON object.", exit: 2);
        }

        if (given.Count != 1 || given.First().Key is not ("email" or "linkedin_url")
            || given.First().Value is not JsonValue raw || !raw.TryGetValue<string>(out var text) || EcmaTrim(text).Length == 0)
        {
            return Refused("usage.contact", "Give exactly one of email and linkedin_url, as a string.", exit: 2);
        }

        var field = given.First().Key;
        var value = EcmaTrim(text);
        if (field == "email" ? !IsAddress(value) : LinkedInKey(value) is null)
        {
            return Refused("usage.contact", field == "email" ? "The email is not an email address." : "The linkedin_url is not an absolute http or https URL.", exit: 2);
        }

        var (found, refused) = Lookup(root, new ContactRef(null, new JsonObject { [field] = value }), []);
        return refused ?? Done(new JsonObject { ["contact_id"] = found });
    }

    private static Ending InboxList(string root, Parsed line)
    {
        var byContact = line.Option("--contact-id");
        var bySequence = line.Option("--sequence-id");
        if ((byContact is null) == (bySequence is null))
        {
            return Refused("usage.inbox", "Give exactly one of --contact-id and --sequence-id.", exit: 2);
        }

        int? person = null;
        int? sequence = null;
        if (byContact is not null)
        {
            if (IdOf(byContact) is not { } contactId)
            {
                return BadId("--contact-id");
            }

            person = contactId;
        }
        else
        {
            if (IdOf(bySequence!) is not { } sequenceId)
            {
                return BadId("--sequence-id");
            }

            sequence = sequenceId;
        }

        var source = line.Option("--source") ?? "inbox";
        if (!Sources.Contains(source))
        {
            return Refused("usage.choice", $"--source must be one of: {string.Join(", ", Sources)}.", exit: 2);
        }

        // The CLI's channel words, and Reply's spelling of each, which is what a thread carries.
        string? channel = null;
        if (line.Option("--channel") is { } named)
        {
            if (!InboxChannels.TryGetValue(named, out channel))
            {
                return Refused("usage.choice", $"--channel must be one of: {string.Join(", ", InboxChannels.Keys)}.", exit: 2);
            }
        }

        int? category = null;
        if (line.Option("--category-id") is { } filed)
        {
            if (IdOf(filed) is not { } categoryId)
            {
                return BadId("--category-id");
            }

            category = categoryId;
        }

        DateTimeOffset? since = null;
        if (line.Option("--since") is { } raw)
        {
            if (MomentOf(raw) is not { } moment)
            {
                return Refused("usage.date", "--since must be a date and time, e.g. 2026-10-01T00:00:00Z.", exit: 2);
            }

            since = moment;
        }

        var (limit, badLimit) = RangeOf(line, "--limit", 1, 100, 20);
        if (badLimit is not null)
        {
            return badLimit;
        }

        var (offset, badOffset) = RangeOf(line, "--offset", 0, 1_000_000, 0);
        if (badOffset is not null)
        {
            return badOffset;
        }

        if (person is { } held && Find(Read(root, ContactsFile), held) is null)
        {
            // Reply answers a contact it does not hold with a validation problem, which reply-cli names this way.
            return Refused(
                "reply.invalid_request", "Reply did not accept the request as given.", status: 400,
                details: new JsonObject { ["fields"] = new JsonArray("contactIds") });
        }

        // `--since` keeps the threads that moved at or after it: reply-cli asks Reply from one millisecond earlier.
        var matched = Records(root, ThreadsFile)
            .Where(thread => person is null || (thread["contact"]?["id"] is JsonValue id && id.GetValue<int>() == person))
            .Where(thread => sequence is null || (thread["sequence"]?["id"] is JsonValue id && id.GetValue<int>() == sequence))
            .Where(thread => source switch { "inbox" => Inbound(thread), "sent" => LatestOutbound(thread), _ => false })
            .Where(thread => channel is null || Text(thread, "channel") == channel)
            .Where(thread => category is null || (thread["category"]?["id"] is JsonValue id && id.GetValue<int>() == category))
            .Where(thread => since is null || LastActivity(thread) >= since)
            .OrderByDescending(LastActivity)
            .Select(thread => (JsonNode)Listed(thread))
            .ToList();
        var page = matched.Skip(offset).Take(limit).ToList();
        return Done(new JsonObject { ["items"] = new JsonArray([.. page]), ["has_more"] = matched.Count > offset + page.Count });
    }

    private static Ending InboxGet(string root, Parsed line)
    {
        if (IdOf(line.Operands[0]) is not { } id)
        {
            return BadId("Thread id");
        }

        var (limit, badLimit) = RangeOf(line, "--limit", 1, 20, 20);
        if (badLimit is not null)
        {
            return badLimit;
        }

        var (offset, badOffset) = RangeOf(line, "--offset", 0, 1_000_000, 0);
        if (badOffset is not null)
        {
            return badOffset;
        }

        if (Find(Read(root, ThreadsFile), id) is not { } thread)
        {
            return Refused("inbox.thread_not_found", $"This account has no inbox thread {id} it can see.", status: 404, replyCode: "inboxThread.notFound");
        }

        // reply-cli leaves the inline history out — the paged messages are the history — and adds the subject of the
        // latest email message, since Reply's thread has none of its own.
        var header = new JsonObject();
        foreach (var name in new[] { "id", "channel", "isRead", "contact", "emailAccountId", "linkedInAccountId", "sequence", "aiDraft", "category", "hasMeetingIntent" })
        {
            if (thread.TryGetPropertyValue(name, out var value))
            {
                header[name] = value?.DeepClone();
            }
        }

        var messages = (thread["messages"] as JsonArray ?? []).OfType<JsonObject>().ToList();
        var latestEmail = messages.LastOrDefault(message => Text(message, "channel") == "email");
        header["subject"] = latestEmail is not null && latestEmail["subject"] is JsonValue subject && subject.TryGetValue<string>(out var text)
            ? text
            : null;
        var page = messages.Skip(offset).Take(limit).Select(message => (JsonNode)message.DeepClone()).ToList();
        return Done(new JsonObject
        {
            ["thread"] = header,
            ["messages"] = new JsonObject { ["items"] = new JsonArray([.. page]), ["has_more"] = messages.Count > offset + page.Count },
        });
    }

    // -------------------------------------------------------------------------------------------------------
    // What the commands share
    // -------------------------------------------------------------------------------------------------------

    /// <summary>The contact as reply-cli takes it: exactly one of --contact-id, --email with its name flags, or --contact.</summary>
    private static (ContactRef? Contact, Ending? Refusal) ContactOf(Parsed line, string? stdin)
    {
        var byId = line.Option("--contact-id");
        var byFlags = new[] { "--email", "--first-name", "--last-name", "--company", "--title" }.Any(flag => line.Option(flag) is not null);
        var byJson = line.Option("--contact");
        var forms = (byId is null ? 0 : 1) + (byFlags ? 1 : 0) + (byJson is null ? 0 : 1);
        if (forms != 1)
        {
            return (null, Refused("usage.contact", forms == 0 ? "No contact was given." : "The contact was given more than one way.", exit: 2));
        }

        if (byId is not null)
        {
            return IdOf(byId) is { } id ? (new ContactRef(id, null), null) : (null, BadId("--contact-id"));
        }

        JsonObject given;
        if (byJson is not null)
        {
            try
            {
                given = JsonNode.Parse(byJson == "-" ? stdin ?? string.Empty : byJson) as JsonObject
                    ?? throw new JsonException("The contact is not an object.");
            }
            catch (JsonException)
            {
                return (null, Refused("usage.contact", "--contact must be a JSON object.", exit: 2));
            }

            if (given.FirstOrDefault(member => !PersonFields.Contains(member.Key)) is { Key: { } stray })
            {
                return (null, Refused("usage.contact", $"--contact has an unknown field '{stray}'.", exit: 2));
            }
        }
        else
        {
            given = new JsonObject
            {
                ["email"] = line.Option("--email"),
                ["first_name"] = line.Option("--first-name"),
                ["last_name"] = line.Option("--last-name"),
                ["company"] = line.Option("--company"),
                ["title"] = line.Option("--title"),
            };
        }

        var person = new JsonObject();
        foreach (var field in PersonFields)
        {
            if (given[field] is JsonValue value && value.TryGetValue<string>(out var text) && EcmaTrim(text) is { Length: > 0 } kept)
            {
                person[field] = kept;
            }
        }

        if (person["email"] is null && person["linkedin_url"] is null)
        {
            return (null, Refused("usage.contact", "The contact needs an email or a LinkedIn URL.", exit: 2));
        }

        if (person["email"] is JsonValue email && !IsAddress(email.GetValue<string>()))
        {
            return (null, Refused("usage.contact", "The contact's email is not an email address.", exit: 2));
        }

        if (person["linkedin_url"] is JsonValue link && LinkedInKey(link.GetValue<string>()) is null)
        {
            return (null, Refused("usage.contact", "The contact's linkedin_url is not an absolute http or https URL.", exit: 2));
        }

        return (new ContactRef(null, person), null);
    }

    /// <summary>
    /// JavaScript's String.prototype.trim, which reply-cli uses on every field of a person: WhiteSpace and
    /// LineTerminator as ECMAScript defines them. It is not .NET's Trim, which keeps U+FEFF and drops U+0085.
    /// </summary>
    private static string EcmaTrim(string value)
    {
        // Written as escapes, never as the characters themselves: three of them are invisible in a diff.
        static bool Blank(char c) =>
            c is '\t' or '\v' or '\f' or '\uFEFF' or '\n' or '\r' or '\u2028' or '\u2029'
            || CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.SpaceSeparator;

        var start = 0;
        var end = value.Length;
        while (start < end && Blank(value[start]))
        {
            start++;
        }

        while (end > start && Blank(value[end - 1]))
        {
            end--;
        }

        return value[start..end];
    }

    /// <summary>
    /// reply-cli's ensure: an id as given; a person given by email imported straight away, matched by the email; and one
    /// given only by LinkedIn URL looked up first, by its key, and imported only when Reply holds nobody under it.
    /// </summary>
    private static (int Id, bool Created, Ending? Refusal) Ensure(string root, ContactRef contact, JsonObject ids)
    {
        if (contact.Id is { } id)
        {
            return (id, false, null);
        }

        if (contact.Person!["email"] is null)
        {
            var (found, refused) = Lookup(root, contact, ids);
            if (refused is not null)
            {
                return (0, false, refused);
            }

            if (found is { } held)
            {
                return (held, false, null);
            }
        }

        return Import(root, contact.Person!, ids);
    }

    /// <summary>
    /// reply-cli's import: keyed by the email, filling only fields the match left empty, or — with no email — keyed by the
    /// LinkedIn URL and writing it as its key; refused without a first name, as Reply refuses it, even for somebody it holds.
    /// </summary>
    private static (int Id, bool Created, Ending? Refusal) Import(string root, JsonObject person, JsonObject ids)
    {
        if (person["first_name"] is null)
        {
            return (0, false, Refused("reply.refused", "Reply did not import the contact.", status: 200, detail: "Invalid data", ids: ids));
        }

        var contacts = Read(root, ContactsFile);
        var email = person["email"]?.GetValue<string>();
        var key = person["linkedin_url"] is JsonValue link ? LinkedInKey(link.GetValue<string>()) : null;
        var held = email is null
            ? null
            : contacts.OfType<JsonObject>().FirstOrDefault(entry => string.Equals(Text(entry, "email"), email, StringComparison.OrdinalIgnoreCase));
        if (held is not null)
        {
            foreach (var (field, name) in new[] { ("first_name", "firstName"), ("last_name", "lastName"), ("company", "company"), ("title", "title") })
            {
                if (Text(held, name).Length == 0 && person[field] is { } value)
                {
                    held[name] = value.DeepClone();
                }
            }

            if (Text(held, "linkedInUrl").Length == 0 && key is not null)
            {
                held["linkedInUrl"] = key;
            }

            Write(root, ContactsFile, contacts);
            return (held["id"]!.GetValue<int>(), false, null);
        }

        var created = NextId(contacts);
        contacts.Add(new JsonObject
        {
            ["id"] = created,
            ["email"] = email,
            ["linkedInUrl"] = key,
            ["firstName"] = person["first_name"]!.DeepClone(),
            ["lastName"] = person["last_name"]?.DeepClone(),
            ["company"] = person["company"]?.DeepClone(),
            ["title"] = person["title"]?.DeepClone(),
            ["callStatus"] = "none",
            ["meetingStatus"] = "none",
        });
        Write(root, ContactsFile, contacts);
        return (created, true, null);
    }

    /// <summary>
    /// reply-cli's find: an id as given, or the one contact under the address — by its email when there is one, equal
    /// ignoring case; with only a LinkedIn URL, by its key, which is what asking Reply for the lowercased slug and then
    /// for the spelling as given finds. Two is <c>contact.not_unique</c>.
    /// </summary>
    private static (int? Id, Ending? Refusal) Lookup(string root, ContactRef contact, JsonObject ids)
    {
        if (contact.Id is { } id)
        {
            return (id, null);
        }

        var person = contact.Person!;
        var holding = person["email"] is JsonValue email
            ? Records(root, ContactsFile).Where(entry => string.Equals(Text(entry, "email"), email.GetValue<string>(), StringComparison.OrdinalIgnoreCase))
            : Records(root, ContactsFile).Where(entry =>
                Text(entry, "linkedInUrl") is { Length: > 0 } held && LinkedInKey(held) == LinkedInKey(person["linkedin_url"]!.GetValue<string>()));
        var found = holding.Select(entry => entry["id"]!.GetValue<int>()).Distinct().ToList();

        if (found.Count > 1)
        {
            return (null, Refused("contact.not_unique", "Reply holds more than one contact under this address.", status: 200, ids: ids));
        }

        return (found.Count == 1 ? found[0] : null, null);
    }

    /// <summary>The Nth step of a chain of steps, walked from the one with no parent, or why there is none.</summary>
    private static (int? Id, string Why) StepAt(JsonObject sequence, int position)
    {
        var steps = (sequence["steps"] as JsonArray ?? []).OfType<JsonObject>().ToList();
        if (steps.Count == 0)
        {
            return (null, "the sequence has no steps");
        }

        var roots = steps.Where(step => step["parentId"] is null).ToList();
        if (roots.Count != 1)
        {
            return (null, $"its steps have {roots.Count} starting points, so they are not one chain");
        }

        var at = roots[0];
        for (var index = 1; index <= steps.Count; index++)
        {
            if (string.Equals(Text(at, "type"), "condition", StringComparison.OrdinalIgnoreCase))
            {
                return (null, $"step {index} is a condition, so the steps after it branch");
            }

            var id = at["id"]!.GetValue<int>();
            if (index == position)
            {
                return (id, string.Empty);
            }

            var next = steps.Where(step => step["parentId"] is JsonValue parent && parent.TryGetValue<int>(out var parentId) && parentId == id).ToList();
            if (next.Count == 0)
            {
                return (null, $"the chain ends at step {index}");
            }

            if (next.Count > 1)
            {
                return (null, $"step {index} is followed by {next.Count} steps");
            }

            at = next[0];
        }

        return (null, "its steps lead back into one another");
    }

    private static Ending NoSequence(int id, JsonObject? ids = null) =>
        Refused("sequence.not_found", $"This account has no sequence {id}.", status: 404, replyCode: "sequence.notFound", ids: ids);

    private static Ending BadId(string label) =>
        Refused("usage.id", $"{label} must be a Reply id: a positive whole number.", exit: 2);

    private static (int Value, Ending? Refusal) RangeOf(Parsed line, string option, int min, int max, int fallback)
    {
        var raw = line.Option(option);
        if (raw is null)
        {
            return (fallback, null);
        }

        return int.TryParse(raw.Trim(), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var value) && value >= min && value <= max
            ? (value, null)
            : (0, Refused("usage.range", $"{option} must be a whole number from {min} to {max}.", exit: 2));
    }

    /// <summary>A Reply id as reply-cli reads one: a positive whole number of at most ten digits that fits 32 bits.</summary>
    private static int? IdOf(string raw)
    {
        var value = raw.Trim();
        if (value.Length is 0 or > 10 || value[0] == '0' || !value.All(char.IsAsciiDigit))
        {
            return null;
        }

        var number = long.Parse(value, CultureInfo.InvariantCulture);
        return number <= int.MaxValue ? (int)number : null;
    }

    /// <summary>A moment as reply-cli reads one; a time without a zone is UTC.</summary>
    private static DateTimeOffset? MomentOf(string raw) =>
        DateTimeOffset.TryParse(raw.Trim(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var at)
            ? at
            : null;

    private static bool IsAddress(string value)
    {
        var at = value.IndexOf('@', StringComparison.Ordinal);
        return at > 0 && at == value.LastIndexOf('@') && at < value.Length - 1 && !value.Any(char.IsWhiteSpace);
    }

    private static string Key(int id) => id.ToString(CultureInfo.InvariantCulture);

    private static Ending Done(JsonObject result) => new(0, result, null);

    private static Ending Refused(
        string code,
        string title,
        int exit = 1,
        int? status = null,
        string? replyCode = null,
        string? detail = null,
        JsonObject? ids = null,
        JsonObject? details = null) =>
        new(exit, null, ErrorOf(code, title, status, replyCode, detail, ids, details));

    /// <summary>reply-cli's <c>--json</c> error envelope, its empty members left out as reply-cli leaves them out.</summary>
    private static JsonObject ErrorOf(
        string code, string title, int? status, string? replyCode, string? detail, JsonObject? ids, JsonObject? details)
    {
        var error = new JsonObject { ["code"] = code, ["title"] = title };
        if (status is { } answered)
        {
            error["status"] = answered;
        }

        if (replyCode is not null)
        {
            error["reply_code"] = replyCode;
        }

        if (detail is not null)
        {
            error["detail"] = detail;
        }

        if (ids is { Count: > 0 })
        {
            error["ids"] = ids.DeepClone();
        }

        if (details is not null)
        {
            error["details"] = details.DeepClone();
        }

        return error;
    }

    private static JsonObject? IdsOf(JsonObject result)
    {
        var ids = new JsonObject();
        foreach (var name in new[] { "sequence_id", "contact_id", "list_id", "thread_id" })
        {
            if (result[name] is JsonValue value && value.TryGetValue<int>(out var id))
            {
                ids[name] = id;
            }
        }

        return ids.Count == 0 ? null : ids;
    }

    private static async Task<int> PrintAsync(Ending ending)
    {
        if (ending.Result is not null)
        {
            await Console.Out.WriteLineAsync(ending.Result.ToJsonString());
            await Console.Out.FlushAsync();
        }

        if (ending.Error is not null)
        {
            await Console.Error.WriteLineAsync(new JsonObject { ["error"] = ending.Error }.ToJsonString());
        }

        return ending.Exit;
    }

    /// <summary>
    /// Says the call has started — the marker file — and waits for the release file, or for its cap. A sleep says how
    /// long to wait, never that the waiting began, so a test that interrupts a call in flight observes it instead.
    /// </summary>
    private static async Task HoldAsync(JsonObject instruction)
    {
        if (Text(instruction, "marker") is { Length: > 0 } marker)
        {
            await File.WriteAllTextAsync(marker, string.Empty);
        }

        var until = Text(instruction, "until");
        var capMs = Number(instruction, "timeout_ms");
        var deadline = DateTime.UtcNow.AddMilliseconds(capMs > 0 ? capMs : 120_000);
        while (until.Length > 0 && !File.Exists(until) && DateTime.UtcNow < deadline)
        {
            await Task.Delay(25);
        }
    }

    /// <summary>The first instruction a test left for this command, spent as it is taken; <c>match</c> narrows it to whole words of the argv.</summary>
    private static JsonObject? TakeCommand(string root, string command, string[] argv)
    {
        var file = Path.Combine(root, InstructionsFile);
        if (!File.Exists(file)
            || JsonNode.Parse(File.ReadAllText(file)) is not JsonObject instructions
            || instructions["scripted"] is not JsonArray scripted)
        {
            return null;
        }

        var line = " " + string.Join(' ', argv) + " ";
        for (var position = 0; position < scripted.Count; position++)
        {
            if (scripted[position] is not JsonObject instruction
                || !string.Equals(Text(instruction, "command"), command, StringComparison.Ordinal)
                || (Text(instruction, "match") is { Length: > 0 } match && !line.Contains(" " + match + " ", StringComparison.Ordinal)))
            {
                continue;
            }

            scripted.RemoveAt(position);
            File.WriteAllText(file, instructions.ToJsonString(), new UTF8Encoding(false));
            return instruction;
        }

        return null;
    }

    private static void RecordCommand(string root, string command, IReadOnlyList<string> operands, string? stdin, string[] argv)
    {
        var entry = new JsonObject
        {
            ["command"] = command,
            ["operands"] = new JsonArray([.. operands.Select(operand => (JsonNode)JsonValue.Create(operand))]),
            ["body"] = stdin,
            ["args"] = new JsonArray([.. argv.Select(argument => (JsonNode)JsonValue.Create(argument))]),
            ["env"] = VendorVariables(),
        };

        Append(Path.Combine(root, CallsFile), entry.ToJsonString() + "\n");
    }
}
