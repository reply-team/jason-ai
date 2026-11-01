using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

// Deliberately in the global namespace with the rest of this program.

/// <summary>
/// The sends outside any sequence, answered as reply-cli documents them: the account by its id, the contact by its id or
/// by the person's fields — looked up by every address given, the one the send reaches them at first, and imported as
/// the add commands import one when Reply holds nobody under any of them — the text from stdin and from nowhere else,
/// held to the lengths Reply publishes before anything is sent, and Reply's refusals in the CLI's words, each one saying
/// which contact and account it was about. When the person and the text both read stdin, they are one object. A send
/// given a person's fields goes only to a contact it creates: one a lookup finds, or one the import matched, is
/// <c>contact.already_held</c>, named and not sent to. Every answer names the contact it went to and whether the command
/// made it. A send that goes out is appended to the account's record of what was sent, which is how a test says whether
/// anything went; an entry carries its text as written and, for an email and for a LinkedIn message or InMail that goes
/// now, as Reply rendered it for the person, and a send held for a later moment the moment it is held for. What a Reply
/// that keeps direct sends adds — the moment, the key and the reference — is in Account.DirectSends.cs. Reply's own checks are kept in the order a live account was seen to make them: a note too long is
/// refused before the ids are looked at. Reply does not refuse a contact it holds as opted out on these sends, and
/// neither does this. It consults its blacklist on an email when it takes the send, and on a LinkedIn send when it makes
/// it, so a LinkedIn send to a contact the blacklist holds is taken here as any other: the failure Reply gives it when it
/// makes the send is not one this stand-in plays.
/// </summary>
internal static partial class Account
{
    private const string ConnectionsFile = "connections.json";
    private const string SentFile = "sent.json";

    /// <summary>What a test plants on a LinkedIn account whose daily or weekly allowance of sends is spent.</summary>
    private const string Limit = "_limit";

    /// <summary>
    /// Reply's two codes for a LinkedIn account whose allowance is spent, each with the allowance reply-cli names for it.
    /// Copied from reply-cli 0.17.2, src/operations/direct-send.ts, `LIMITS`.
    /// </summary>
    private static readonly Dictionary<string, string> SpentAllowances = new(StringComparer.Ordinal)
    {
        ["directLinkedIn.dailyLimitReached"] = "daily",
        ["directLinkedIn.weeklyLimitReached"] = "weekly",
    };

    /// <summary>What a test plants on a contact the account's blacklist holds.</summary>
    private const string Blacklisted = "_blacklisted";

    /// <summary>Reply refuses a note over 200 characters, by the field alone and with no code of its own.</summary>
    private const int LongestNote = 200;

    /// <summary>The most a JavaScript number holds exactly: reply-cli reads a mailbox's identifier up to it.</summary>
    private const long LargestMailbox = 9_007_199_254_740_991;

    /// <summary>
    /// One send: the channel of the account it goes from, the option naming that account, the fields its text takes on
    /// stdin, the longest each may be where Reply publishes it, whether it may go without a text (an invitation without a
    /// note), and whether it goes only to a connection of the LinkedIn account.
    /// </summary>
    private sealed record DirectSend(
        string Channel,
        string AccountOption,
        string[] Fields,
        Dictionary<string, int> Longest,
        bool TextOptional = false,
        bool NeedsConnection = false);

    private static readonly Dictionary<string, DirectSend> DirectSends = new(StringComparer.Ordinal)
    {
        ["contact send-email"] = new("email", "--email-account-id", ["subject", "body"], []),
        ["contact linkedin-connect"] = new("linkedin", "--linkedin-account-id", ["note"], [], TextOptional: true),
        ["contact linkedin-message"] = new("linkedin", "--linkedin-account-id", ["body"], new() { ["body"] = 8000 }, NeedsConnection: true),
        ["contact linkedin-inmail"] = new("linkedin", "--linkedin-account-id", ["subject", "body"], new() { ["subject"] = 200, ["body"] = 1900 }),
        ["contact linkedin-voice"] = new("linkedin", "--linkedin-account-id", ["script"], new() { ["script"] = 1000 }, NeedsConnection: true),
    };

    private static bool IsDirectSend(string command) => DirectSends.ContainsKey(command);

    /// <summary>The name a send's failure gives the account it was to go from: <c>email_account_id</c> or <c>linkedin_account_id</c>.</summary>
    private static string AccountKey(DirectSend send) => send.AccountOption[2..].Replace('-', '_');

    /// <summary>
    /// What a send whose answer was lost says it was about, as its every other ending does: the ids its refusal carried,
    /// or, for one that went, the contact it went to and the account.
    /// </summary>
    private static JsonObject? LostSendIds(string command, Parsed line, Ending effect)
    {
        if (effect.Error?["ids"] is JsonObject said)
        {
            return (JsonObject)said.DeepClone();
        }

        var send = DirectSends[command];
        if (effect.Result is not JsonObject done || done["contact_id"] is null
            || AccountIdOf(send, line.Option(send.AccountOption) ?? string.Empty) is not { } account)
        {
            return null;
        }

        return new JsonObject { ["contact_id"] = done["contact_id"]!.DeepClone(), [AccountKey(send)] = account };
    }

    /// <summary>An account's id as reply-cli reads one: a mailbox's up to 2^53 − 1, a LinkedIn account's in 32 bits.</summary>
    private static long? AccountIdOf(DirectSend send, string raw)
    {
        if (send.Channel != "email")
        {
            return IdOf(raw);
        }

        var value = raw.Trim();
        return value.Length is > 0 and <= 16 && value[0] != '0' && value.All(char.IsAsciiDigit)
            && long.Parse(value, CultureInfo.InvariantCulture) is var number && number <= LargestMailbox
            ? number
            : null;
    }

    private static Ending DirectSendCommand(string root, string command, Parsed line, string? stdin)
    {
        var send = DirectSends[command];
        var (contactStdin, textStdin, badStdin) = StdinOf(line, "--message", stdin);
        if (badStdin is not null)
        {
            return badStdin;
        }

        var (contact, badContact) = ContactOf(line, contactStdin);
        if (badContact is not null)
        {
            return badContact;
        }

        // A person given by their fields is looked up by the address the send reaches them at, so one without it is
        // refused before anything is asked.
        var reachedBy = send.Channel == "email" ? "email" : "linkedin_url";
        if (contact!.Person is { } person && person[reachedBy] is null)
        {
            return Refused(
                "usage.contact",
                send.Channel == "email" ? $"{command} needs the contact's email: the email goes to it." : $"{command} needs the contact's linkedin_url: the send goes to that profile.",
                exit: 2);
        }

        if (line.Option(send.AccountOption) is not { } accountRaw)
        {
            return Refused("usage.id", $"No {send.AccountOption} was given.", exit: 2);
        }

        if (AccountIdOf(send, accountRaw) is not { } accountId)
        {
            return BadId(send.AccountOption);
        }

        var (sendAfter, badMoment) = SendAfterOf(line);
        if (badMoment is not null)
        {
            return badMoment;
        }

        var (schedule, badSchedule) = ScheduleOf(line);
        if (badSchedule is not null)
        {
            return badSchedule;
        }

        var (key, badKey) = KeyOf(line);
        if (badKey is not null)
        {
            return badKey;
        }

        JsonObject? text = null;
        if (line.Option("--message") is not null || !send.TextOptional)
        {
            var (read, refusal) = DirectTextOf(line.Option("--message"), textStdin, send);
            if (refusal is not null)
            {
                return refusal;
            }

            text = read;
        }

        // The contact first: the id given, or a contact this send creates; one Reply already held is named and not sent to.
        var created = contact.Id is null;
        var (contactId, notCreated) = contact.Id is { } given
            ? (given, (Ending?)null)
            : CreatedFor(root, contact.Person!, send.Channel, new JsonObject { [AccountKey(send)] = accountId });
        if (notCreated is not null)
        {
            return notCreated;
        }

        var ids = new JsonObject { ["contact_id"] = contactId, [AccountKey(send)] = accountId };

        // The contact is held, or was just imported, by a route Reply offers; the send's own route may be one it does not.
        if (send.Channel == "email" && !Offered(root, "send_direct_email"))
        {
            return NotOffered("send_direct_email", contactId, ids);
        }

        // A Reply that keeps direct sends reads the moment, the schedule and the key; one that does not reads none, and sends now.
        var keeps = Offered(root, "direct_send");
        var moment = keeps ? sendAfter : null;
        var timedBy = keeps ? schedule : null;
        var request = new JsonObject
        {
            ["command"] = command,
            ["contactId"] = contactId,
            ["accountId"] = accountId,
            ["text"] = text?.DeepClone(),
            ["sendAfter"] = moment is { } named ? Utc(named) : null,
            ["scheduleId"] = timedBy,
        }.ToJsonString();
        if (key is not null)
        {
            Arrived(root, command, key);
        }

        if (keeps && key is not null && KeptUnder(root, send, key, request, ids) is { } answered)
        {
            return answered;
        }

        if (timedBy is { } scheduleId && Untimed(root, send, scheduleId, contactId, ids) is { } untimed)
        {
            return untimed;
        }

        JsonObject? rendered = null;
        var refused = send.Channel == "email"
            ? EmailRefusal(root, contactId, accountId, text!, ids, out rendered)
            : LinkedInRefusal(root, command, send, contactId, accountId, text, moment, ids, out rendered);
        if (refused is not null)
        {
            return refused;
        }

        if (command == "contact linkedin-connect" && Connected(root, contactId, accountId))
        {
            return Done(new JsonObject { ["status"] = "already_connected", ["contact_id"] = contactId, ["contact_created"] = created, ["activity_id"] = null, ["sent_at"] = null });
        }

        if (FailedSend(root, send, contactId, moment, ids, keeps, out var went) is { } failed)
        {
            if (went)
            {
                // Planted as a send that went before Reply answered its failure: the worst such an answer can hide.
                Recorded(root, command, contactId, accountId, text, rendered, moment, timedBy);
            }

            return failed;
        }

        var sent = Recorded(root, command, contactId, accountId, text, rendered, moment, timedBy);

        // Reply's answer to a direct email carries no moment, so the CLI answers none.
        var answer = send.Channel == "email"
            ? new JsonObject
            {
                ["status"] = "sent",
                ["contact_id"] = contactId,
                ["contact_created"] = created,
                ["message_id"] = $"<{60_000 + sent.Count}.direct@mail.example.com>",
                ["sent_at"] = null,
                ["reply_status"] = "sent",
            }
            : new JsonObject
            {
                ["status"] = "sent",
                ["contact_id"] = contactId,
                ["contact_created"] = created,
                ["activity_id"] = 70_000 + sent.Count,
                ["sent_at"] = DateTimeOffset.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture),
            };
        return Done(keeps ? Kept(root, send, contactId, moment, timedBy, key, request, answer) : answer);
    }

    /// <summary>A send as it went, recorded in the order sends went; every send recorded so far.</summary>
    private static JsonArray Recorded(
        string root, string command, int contactId, long accountId, JsonNode? text, JsonObject? rendered, DateTimeOffset? moment, int? schedule)
    {
        var sent = Read(root, SentFile);
        var entry = new JsonObject { ["command"] = command, ["contactId"] = contactId, ["accountId"] = accountId, ["text"] = text?.DeepClone() };
        if (rendered is not null)
        {
            entry["rendered"] = rendered;
        }

        if (moment is { } held)
        {
            entry["sendAfter"] = Utc(held);
        }

        if (schedule is { } timed)
        {
            entry["scheduleId"] = timed;
        }

        sent.Add(entry);
        Write(root, SentFile, sent);
        return sent;
    }

    /// <summary>
    /// Reply's refusals of a direct email, in the order this stand-in makes them: the contact, the mailbox, the blacklist,
    /// the template; and, when none refuses, the text as Reply rendered it.
    /// </summary>
    private static Ending? EmailRefusal(string root, int contactId, long accountId, JsonObject text, JsonObject ids, out JsonObject? rendered)
    {
        rendered = null;
        if (Find(Read(root, ContactsFile), contactId) is not { } contact)
        {
            return Refused("contact.not_found", $"This account has no contact {contactId}.", status: 404, replyCode: "contact.notFound", ids: ids);
        }

        if (SenderOf(root, "email", accountId) is not { } mailbox)
        {
            return Refused(
                "email_account.not_found", "Reply has no mailbox by that id that this account can send from, so nothing was sent.",
                status: 400, replyCode: "contact.emailAccountNotFound", ids: ids);
        }

        if (Flag(contact, Blacklisted))
        {
            return Refused(
                "contact.blacklisted", "The contact is on the account's blacklist, so nothing was sent.",
                status: 400, replyCode: "contact.contactInBlackList", ids: ids);
        }

        // Reply reads the text as a template of its own and refuses one it cannot fill or read, under a code that says
        // nothing of it.
        var (filled, refusal) = RenderedEmail(text, contact, mailbox, ids);
        rendered = filled;
        return refusal;
    }

    /// <summary>
    /// Reply's refusals of a LinkedIn send, in the order this stand-in makes them; and, when none refuses, the text as Reply
    /// rendered it, for a send that goes now.
    /// </summary>
    private static Ending? LinkedInRefusal(
        string root, string command, DirectSend send, int contactId, long accountId, JsonObject? text, DateTimeOffset? moment, JsonObject ids,
        out JsonObject? rendered)
    {
        rendered = null;
        if (!Enabled(root, "directLinkedIn"))
        {
            return Refused(
                "linkedin.feature_disabled", "Direct LinkedIn sends are not enabled for this account, so nothing was sent.",
                status: 403, replyCode: "directLinkedIn.featureDisabled", ids: ids);
        }

        if (text?["note"] is JsonValue note && note.GetValue<string>().EnumerateRunes().Count() > LongestNote)
        {
            return Refused(
                "reply.invalid_request", "Reply did not accept the request as given.", status: 400, ids: ids,
                details: new JsonObject { ["fields"] = new JsonArray("message") });
        }

        if (Find(Read(root, ContactsFile), contactId) is not { } contact)
        {
            return Refused("contact.not_found", $"This account has no contact {contactId}.", status: 404, replyCode: "contact.notFound", ids: ids);
        }

        if (SenderOf(root, send.Channel, accountId) is not { } account)
        {
            return Refused(
                "linkedin.account_not_found", "Reply has no LinkedIn account by that id that this account can send from, so nothing was sent.",
                status: 404, replyCode: "directLinkedIn.linkedInAccountNotFound", ids: ids);
        }

        switch (Text(account, "status"))
        {
            case "disabled":
                return Refused(
                    "linkedin.account_inactive", "The LinkedIn account is inactive, so nothing was sent.",
                    status: 409, replyCode: "directLinkedIn.linkedInAccountInactive", ids: ids);
            case "cookieInvalid":
                return Refused(
                    "linkedin.invalid_cookie", "The LinkedIn account's session is no longer valid, so nothing was sent; reconnect the account.",
                    status: 409, replyCode: "directLinkedIn.invalidCookie", ids: ids);
        }

        if (Text(contact, "linkedInUrl").Length == 0)
        {
            return Refused(
                "contact.no_linkedin_profile", "Reply holds no LinkedIn profile URL for the contact, so nothing was sent.",
                status: 404, replyCode: "directLinkedIn.noLinkedInProfile", ids: ids);
        }

        if (command == "contact linkedin-connect" && Connected(root, contactId, accountId))
        {
            // Nothing needs sending: the caller answers already_connected.
            return null;
        }

        if (send.NeedsConnection && !Connected(root, contactId, accountId))
        {
            return Refused(
                "linkedin.not_connected", "The LinkedIn account is not connected with the contact, so nothing was sent.",
                status: 409, replyCode: "directLinkedIn.notConnected", ids: ids);
        }

        if (Text(account, Limit) is { Length: > 0 } spent)
        {
            return SpentAllowance(spent == "weekly" ? "directLinkedIn.weeklyLimitReached" : "directLinkedIn.dailyLimitReached", ids, []);
        }

        // An invitation's note and a voice script fill nothing in: a double opening brace there is a variable Reply refuses.
        if (command is not ("contact linkedin-message" or "contact linkedin-inmail"))
        {
            return text is not null && HoldsPlaceholder(text)
                ? Refused(
                    "linkedin.missing_variables", "The text holds a template variable Reply could not fill, so nothing was sent.",
                    status: 400, replyCode: "directLinkedIn.missingVariables", ids: ids)
                : null;
        }

        // Reply reads a message or an InMail as a template of its own, and fills in one it sends within the call before it
        // sends it: one it cannot fill or read is refused. A send held for later, or one Reply queues behind the account's
        // other sends, which this stand-in does not do, is filled in when it goes.
        if (text is null || (moment is { } held && held > DateTimeOffset.UtcNow))
        {
            return null;
        }

        var (filled, refusal) = RenderedLinkedIn(text, contact, account, ids);
        rendered = filled;
        return refusal;
    }

    /// <summary>
    /// A send Reply refused at 429 under one of its codes for a spent allowance, as reply-cli 0.17.2 answers it
    /// (src/operations/direct-send.ts, `refusal_of_send` and `send_refusal`): <c>linkedin.limit_reached</c>, exit 1, the
    /// allowance under <c>details.limit</c> and, beside it, the reference, the state and the moment where Reply named the
    /// send it made a record of. It reads that before any other refusal of a send, so the status alone never makes it
    /// <c>rate_limited</c>.
    /// </summary>
    private static Ending SpentAllowance(string replyCode, JsonObject ids, JsonObject held)
    {
        var allowance = SpentAllowances[replyCode];
        var details = new JsonObject { ["limit"] = allowance };
        foreach (var (name, value) in held)
        {
            details[name] = value?.DeepClone();
        }

        return Refused(
            "linkedin.limit_reached", $"The LinkedIn account reached its {allowance} limit, so nothing was sent.", status: 429,
            replyCode: replyCode, ids: ids, details: details);
    }

    /// <summary>Whether any field of the text holds a double opening brace, which Reply reads as the start of a template variable.</summary>
    private static bool HoldsPlaceholder(JsonObject text) =>
        text.Any(field => ((string?)field.Value)?.Contains("{{", StringComparison.Ordinal) == true);

    /// <summary>
    /// The text on stdin, held to exactly the send's fields, each a string that is not blank and no longer than Reply
    /// publishes, counted in characters as the CLI counts them, so one outside the basic plane counts once.
    /// </summary>
    private static (JsonObject? Text, Ending? Refusal) DirectTextOf(string? option, string? stdin, DirectSend send)
    {
        if (option != "-")
        {
            return (null, Refused(
                "usage.message", option is null ? "No message was given." : "--message takes only -: the message is read from stdin.", exit: 2));
        }

        JsonObject text;
        try
        {
            text = JsonNode.Parse(stdin ?? string.Empty) as JsonObject ?? throw new JsonException("not an object");
        }
        catch (JsonException)
        {
            return (null, Refused("usage.message", "The message on stdin must be one JSON object.", exit: 2));
        }

        if (text.Select(member => member.Key).FirstOrDefault(key => !send.Fields.Contains(key)) is { } unknown)
        {
            return (null, Refused("usage.message", $"The message has an unknown field '{unknown}'.", exit: 2));
        }

        foreach (var field in send.Fields)
        {
            if (text[field] is not JsonValue value || !value.TryGetValue<string>(out var written) || EcmaTrim(written).Length == 0)
            {
                return (null, Refused("usage.message", $"The message needs a {field}: a string that is not blank.", exit: 2));
            }

            if (send.Longest.TryGetValue(field, out var longest) && written.EnumerateRunes().Count() > longest)
            {
                return (null, Refused("usage.message", $"The {field} is longer than Reply takes: at most {longest} characters.", exit: 2));
            }
        }

        return (text, null);
    }

    /// <summary>
    /// reply-cli's contact for a send given a person's fields, which it sends only to a contact it creates: the person is
    /// looked up by every address given, the one the send reaches them at first, and imported as the add commands import
    /// one only when Reply holds nobody under any of them. A contact a lookup finds, or one the import matched rather than
    /// made, is <c>contact.already_held</c>, naming it, and nothing is sent.
    /// </summary>
    private static (int Id, Ending? Refusal) CreatedFor(string root, JsonObject person, string channel, JsonObject ids)
    {
        foreach (var by in channel == "email" ? new[] { "email", "linkedin" } : ["linkedin", "email"])
        {
            if (person[by == "email" ? "email" : "linkedin_url"] is null)
            {
                continue;
            }

            var (found, refused) = Lookup(root, new ContactRef(null, person), ids, by);
            if (refused is not null)
            {
                return (0, refused);
            }

            if (found is { } held)
            {
                return (0, AlreadyHeld(held, ids));
            }
        }

        var (made, created, notImported) = Import(root, person, ids);
        if (notImported is not null)
        {
            return (0, notImported);
        }

        return created ? (made, null) : (0, AlreadyHeld(made, ids));
    }

    private static Ending AlreadyHeld(int contactId, JsonObject ids)
    {
        ids["contact_id"] = contactId;
        return Refused(
            "contact.already_held",
            $"Reply already holds contact {contactId} under what was given, so nothing was sent: a send given a person's fields goes only to a contact it creates.",
            status: 200, ids: ids);
    }

    private static bool Connected(string root, int contact, long account) =>
        Records(root, ConnectionsFile).Any(entry => Number(entry, "contactId") == contact && entry["accountId"]!.GetValue<long>() == account);
}
