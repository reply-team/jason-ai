using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

// Deliberately in the global namespace with the rest of this program.

/// <summary>
/// The sends outside any sequence, answered as reply-cli 0.14.0 documents them: the contact and the account by their ids,
/// the text from stdin and from nowhere else, and Reply's refusals in the CLI's words, each one saying which contact and
/// account it was about. A send that goes out is appended to the account's record of what was sent, which is how a test
/// says whether anything went. Reply's own checks are kept in the order a live account was seen to make them: a note too
/// long is refused before the ids are looked at. Reply does not refuse a contact it holds as opted out on these sends,
/// and neither does this.
/// </summary>
internal static partial class Account
{
    private const string ConnectionsFile = "connections.json";
    private const string SentFile = "sent.json";

    /// <summary>What a test plants on a LinkedIn account whose daily or weekly allowance of sends is spent.</summary>
    private const string Limit = "_limit";

    /// <summary>Reply refuses a note over 200 characters, by the field alone and with no code of its own.</summary>
    private const int LongestNote = 200;

    /// <summary>
    /// One send: the channel of the account it goes from, the fields its text takes on stdin, and whether it may go
    /// without one (an invitation without a note).
    /// </summary>
    private sealed record DirectSend(string Channel, string AccountOption, string[] Fields, bool TextOptional);

    private static readonly Dictionary<string, DirectSend> DirectSends = new(StringComparer.Ordinal)
    {
        ["contact linkedin-connect"] = new("linkedin", "--linkedin-account-id", ["note"], TextOptional: true),
    };

    private static bool IsDirectSend(string command) => DirectSends.ContainsKey(command);

    /// <summary>What a send's every failure after its ids were read says it was about: the contact and the account.</summary>
    private static JsonObject? DirectSendIds(string command, Parsed line)
    {
        if (!DirectSends.TryGetValue(command, out var send)
            || IdOf(line.Option("--contact-id") ?? string.Empty) is not { } contact
            || IdOf(line.Option(send.AccountOption) ?? string.Empty) is not { } account)
        {
            return null;
        }

        return new JsonObject { ["contact_id"] = contact, [send.AccountOption[2..].Replace('-', '_')] = account };
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

        if (IdOf(accountRaw) is not { } accountId)
        {
            return BadId(send.AccountOption);
        }

        JsonObject? text = null;
        if (line.Option("--message") is not null || !send.TextOptional)
        {
            var (read, refusal) = DirectTextOf(line.Option("--message"), stdin, send.Fields);
            if (refusal is not null)
            {
                return refusal;
            }

            text = read;
        }

        var ids = DirectSendIds(command, line)!;
        if (send.Channel == "linkedin" && !Enabled(root, "directLinkedIn"))
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
            return Done(new JsonObject { ["status"] = "already_connected", ["contact_id"] = contactId, ["activity_id"] = null, ["sent_at"] = null });
        }

        if (Text(account, Limit) is { Length: > 0 } spent)
        {
            return Refused(
                "linkedin.limit_reached", $"The LinkedIn account reached its {spent} limit, so nothing was sent.", status: 429,
                replyCode: spent == "weekly" ? "directLinkedIn.weeklyLimitReached" : "directLinkedIn.dailyLimitReached", ids: ids,
                details: new JsonObject { ["limit"] = spent });
        }

        if (text is not null && text.Any(field => ((string?)field.Value)?.Contains("{{", StringComparison.Ordinal) == true))
        {
            return Refused(
                "linkedin.missing_variables", "The text holds a template variable Reply could not fill, so nothing was sent.",
                status: 400, replyCode: "directLinkedIn.missingVariables", ids: ids);
        }

        var sent = Read(root, SentFile);
        sent.Add(new JsonObject { ["command"] = command, ["contactId"] = contactId, ["accountId"] = accountId, ["text"] = text?.DeepClone() });
        Write(root, SentFile, sent);

        return Done(new JsonObject
        {
            ["status"] = "sent",
            ["contact_id"] = contactId,
            ["activity_id"] = 70_000 + sent.Count,
            ["sent_at"] = DateTimeOffset.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture),
        });
    }

    /// <summary>The text on stdin, held to exactly the send's fields, each a string that is not blank, as the CLI holds it.</summary>
    private static (JsonObject? Text, Ending? Refusal) DirectTextOf(string? option, string? stdin, string[] fields)
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

        if (text.Select(member => member.Key).FirstOrDefault(key => !fields.Contains(key)) is { } unknown)
        {
            return (null, Refused("usage.message", $"The message has an unknown field '{unknown}'.", exit: 2));
        }

        foreach (var field in fields)
        {
            if (text[field] is not JsonValue value || !value.TryGetValue<string>(out var written) || EcmaTrim(written).Length == 0)
            {
                return (null, Refused("usage.message", $"The message needs a {field}: a string that is not blank.", exit: 2));
            }
        }

        return (text, null);
    }

    private static bool Connected(string root, int contact, int account) =>
        Records(root, ConnectionsFile).Any(entry => Number(entry, "contactId") == contact && entry["accountId"]!.GetValue<long>() == account);
}
