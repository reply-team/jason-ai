using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

// Deliberately in the global namespace with the rest of this program.

/// <summary>
/// The sends outside any sequence, answered as reply-cli 0.14.0 documents them: the contact and the account by their ids,
/// the text from stdin and from nowhere else, held to the lengths Reply publishes before anything is sent, and Reply's
/// refusals in the CLI's words, each one saying which contact and account it was about. A send that goes out is appended
/// to the account's record of what was sent, which is how a test says whether anything went. Reply's own checks are kept
/// in the order a live account was seen to make them: a note too long is refused before the ids are looked at. Reply does
/// not refuse a contact it holds as opted out on these sends, and neither does this; it consults its blacklist on an
/// email alone.
/// </summary>
internal static partial class Account
{
    private const string ConnectionsFile = "connections.json";
    private const string SentFile = "sent.json";

    /// <summary>What a test plants on a LinkedIn account whose daily or weekly allowance of sends is spent.</summary>
    private const string Limit = "_limit";

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

    /// <summary>What a send's every failure after its ids were read says it was about: the contact and the account.</summary>
    private static JsonObject? DirectSendIds(string command, Parsed line)
    {
        if (!DirectSends.TryGetValue(command, out var send)
            || IdOf(line.Option("--contact-id") ?? string.Empty) is not { } contact
            || AccountIdOf(send, line.Option(send.AccountOption) ?? string.Empty) is not { } account)
        {
            return null;
        }

        return new JsonObject { ["contact_id"] = contact, [send.AccountOption[2..].Replace('-', '_')] = account };
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
        if (line.Option("--contact-id") is not { } contactRaw)
        {
            return Refused("usage.id", "No --contact-id was given.", exit: 2);
        }

        if (IdOf(contactRaw) is not { } contactId)
        {
            return BadId("--contact-id");
        }

        if (line.Option(send.AccountOption) is not { } accountRaw)
        {
            return Refused("usage.id", $"No {send.AccountOption} was given.", exit: 2);
        }

        if (AccountIdOf(send, accountRaw) is not { } accountId)
        {
            return BadId(send.AccountOption);
        }

        JsonObject? text = null;
        if (line.Option("--message") is not null || !send.TextOptional)
        {
            var (read, refusal) = DirectTextOf(line.Option("--message"), stdin, send);
            if (refusal is not null)
            {
                return refusal;
            }

            text = read;
        }

        var ids = DirectSendIds(command, line)!;
        var refused = send.Channel == "email"
            ? EmailRefusal(root, contactId, accountId, text!, ids)
            : LinkedInRefusal(root, command, send, contactId, accountId, text, ids);
        if (refused is not null)
        {
            return refused;
        }

        if (command == "contact linkedin-connect" && Connected(root, contactId, accountId))
        {
            return Done(new JsonObject { ["status"] = "already_connected", ["contact_id"] = contactId, ["activity_id"] = null, ["sent_at"] = null });
        }

        var sent = Read(root, SentFile);
        sent.Add(new JsonObject { ["command"] = command, ["contactId"] = contactId, ["accountId"] = accountId, ["text"] = text?.DeepClone() });
        Write(root, SentFile, sent);

        if (send.Channel == "email")
        {
            // Reply's answer to a direct email carries no moment, so the CLI answers none.
            return Done(new JsonObject
            {
                ["status"] = "sent",
                ["contact_id"] = contactId,
                ["message_id"] = $"<{60_000 + sent.Count}.direct@mail.example.com>",
                ["sent_at"] = null,
                ["reply_status"] = "sent",
            });
        }

        return Done(new JsonObject
        {
            ["status"] = "sent",
            ["contact_id"] = contactId,
            ["activity_id"] = 70_000 + sent.Count,
            ["sent_at"] = DateTimeOffset.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture),
        });
    }

    /// <summary>Reply's refusals of a direct email, in the order this stand-in makes them: the contact, the mailbox, the blacklist, the template.</summary>
    private static Ending? EmailRefusal(string root, int contactId, long accountId, JsonObject text, JsonObject ids)
    {
        if (Find(Read(root, ContactsFile), contactId) is not { } contact)
        {
            return Refused("contact.not_found", $"This account has no contact {contactId}.", status: 404, replyCode: "contact.notFound", ids: ids);
        }

        if (SenderOf(root, "email", accountId) is null)
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

        if (HoldsPlaceholder(text))
        {
            // Reply reads the text as a template of its own and refuses one it cannot read, under a code that says nothing
            // of it; the CLI has no word for it and passes it on as a refusal.
            return Refused(
                "reply.refused", "Reply refused the request.", status: 400, replyCode: "contact.unknown",
                detail: "Reached end of template before the expression was closed.", ids: ids);
        }

        return null;
    }

    /// <summary>Reply's refusals of a LinkedIn send, in the order this stand-in makes them.</summary>
    private static Ending? LinkedInRefusal(
        string root, string command, DirectSend send, int contactId, long accountId, JsonObject? text, JsonObject ids)
    {
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
            return Refused(
                "linkedin.limit_reached", $"The LinkedIn account reached its {spent} limit, so nothing was sent.", status: 429,
                replyCode: spent == "weekly" ? "directLinkedIn.weeklyLimitReached" : "directLinkedIn.dailyLimitReached", ids: ids,
                details: new JsonObject { ["limit"] = spent });
        }

        if (text is not null && HoldsPlaceholder(text))
        {
            return Refused(
                "linkedin.missing_variables", "The text holds a template variable Reply could not fill, so nothing was sent.",
                status: 400, replyCode: "directLinkedIn.missingVariables", ids: ids);
        }

        return null;
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

    private static bool Connected(string root, int contact, long account) =>
        Records(root, ConnectionsFile).Any(entry => Number(entry, "contactId") == contact && entry["accountId"]!.GetValue<long>() == account);
}
