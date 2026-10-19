using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

// Deliberately in the global namespace with the rest of this program.

/// <summary>
/// The inbox's writes, answered as reply-cli 0.12.0 documents them: a reply in a thread, the categories a thread can be
/// filed under, filing one, and its read state. Like the other commands they answer reply-cli's contract — its result,
/// its error code, its exit code — from the account's own files, and every failure after the thread's id was read says
/// which thread it was about.
/// </summary>
internal static partial class Account
{
    private const string CategoriesFile = "categories.json";

    /// <summary>What a test plants on a thread so that its next reply is one Reply takes and cannot deliver.</summary>
    private const string SendFails = "_sendFails";

    /// <summary>The CLI's channel words and Reply's own spelling of each, which is what a thread carries.</summary>
    private static readonly Dictionary<string, string> InboxChannels = new(StringComparer.Ordinal)
    {
        ["email"] = "email",
        ["linkedin"] = "linkedIn",
    };

    private static readonly string[] MessageFields = ["body", "reply_to_message_id"];

    /// <summary>
    /// <c>inbox reply &lt;thread&gt; --message -</c>: the message is read from stdin and from nowhere else, the thread is read
    /// for its channel, and the reply goes to the thread's person on that channel. Nothing is sent on any refusal.
    /// </summary>
    private static Ending InboxReply(string root, Parsed line, string? stdin)
    {
        if (IdOf(line.Operands[0]) is not { } id)
        {
            return BadId("Thread id");
        }

        if (line.Option("--message") is not "-")
        {
            return Refused(
                "usage.message",
                line.Option("--message") is null ? "No message was given." : "--message takes only -: the message is read from stdin.",
                exit: 2);
        }

        var (message, unreadable) = MessageOf(stdin);
        if (unreadable is not null)
        {
            return unreadable;
        }

        var ids = new JsonObject { ["thread_id"] = id };
        var threads = Read(root, ThreadsFile);
        if (Find(threads, id) is not { } thread)
        {
            return Refused("inbox.thread_not_found", $"This account has no inbox thread {id} it can see.", status: 404, replyCode: "inboxThread.notFound", ids: ids);
        }

        var spelled = Text(thread, "channel");
        var channel = InboxChannels.FirstOrDefault(pair => pair.Value == spelled).Key;
        if (channel is null)
        {
            return Refused(
                "inbox.channel_mismatch", "Reply takes a reply only in an email or LinkedIn thread, and this thread is neither.",
                status: 200, ids: ids, details: new JsonObject { ["thread_channel"] = spelled.Length == 0 ? null : spelled });
        }

        var replyTo = message!["reply_to_message_id"] is JsonValue named ? named.GetValue<string>() : null;
        if (channel == "linkedin" && replyTo is not null)
        {
            return Refused(
                "inbox.channel_mismatch", "reply_to_message_id answers one email message; a LinkedIn thread takes a reply to the thread only.",
                status: 200, ids: ids, details: new JsonObject { ["thread_channel"] = channel });
        }

        if (thread["contact"]?["id"] is JsonValue person && OptOuts(root).Contains(person.GetValue<int>()))
        {
            return Refused(
                "contact.opted_out", "The thread's contact is opted out, so the reply was not sent.", status: 400,
                replyCode: "inboxThread.contactOptedOut", ids: ids);
        }

        if (thread[SendFails] is JsonObject failure)
        {
            return Refused(
                "inbox.send_failed", "Reply could not deliver the reply, so it was not sent.", status: 502,
                replyCode: "inboxThread.threadSendFailed", ids: ids, details: new JsonObject { ["status_code"] = failure["reason"]?.DeepClone() });
        }

        var messages = thread["messages"] as JsonArray ?? [];
        var sentAt = DateTimeOffset.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);
        var messageId = channel == "email" ? $"<r{id}-{messages.Count + 1}@stand-in.example>" : null;
        var sent = new JsonObject
        {
            ["channel"] = spelled,
            ["messageId"] = messageId,
            ["date"] = sentAt,
            ["body"] = message["body"]!.GetValue<string>(),
            ["isOutbound"] = true,
            // A live thread read shows no status on its outbound messages, so the reply added here carries none either.
            ["status"] = null,
        };
        if (replyTo is not null)
        {
            sent["replyToMessageId"] = replyTo;
        }

        messages.Add(sent);
        thread["messages"] = messages;
        thread["lastActivityDate"] = sentAt;
        Write(root, ThreadsFile, threads);

        return Done(new JsonObject
        {
            ["status"] = "sent",
            ["thread_id"] = id,
            ["channel"] = channel,
            ["message_id"] = messageId,
            ["sent_at"] = sentAt,
        });
    }

    /// <summary>The message on stdin, held to <c>{body, reply_to_message_id?}</c> exactly as the CLI holds it.</summary>
    private static (JsonObject? Message, Ending? Refusal) MessageOf(string? stdin)
    {
        JsonNode? parsed;
        try
        {
            parsed = JsonNode.Parse(stdin ?? string.Empty);
        }
        catch (JsonException)
        {
            return (null, Refused("usage.message", "The message on stdin must be JSON: {body, reply_to_message_id}.", exit: 2));
        }

        if (parsed is not JsonObject message)
        {
            return (null, Refused("usage.message", "The message must be a JSON object: {body, reply_to_message_id}.", exit: 2));
        }

        if (message.Select(member => member.Key).FirstOrDefault(key => !MessageFields.Contains(key)) is { } unknown)
        {
            return (null, Refused("usage.message", $"The message has an unknown field '{unknown}'.", exit: 2));
        }

        if (message["body"] is not JsonValue body || !body.TryGetValue<string>(out var text) || EcmaTrim(text).Length == 0)
        {
            return (null, Refused("usage.message", "The message needs a body: a string that is not blank.", exit: 2));
        }

        if (message["reply_to_message_id"] is { } replyTo
            && (replyTo is not JsonValue value || !value.TryGetValue<string>(out var answered) || answered.Length == 0))
        {
            return (null, Refused("usage.message", "reply_to_message_id must be a message id: a string that is not empty.", exit: 2));
        }

        return (message, null);
    }

    /// <summary><c>inbox categories</c>: the categories as Reply lists them, its reserved ones included.</summary>
    private static Ending InboxCategories(string root) =>
        Done(new JsonObject { ["items"] = Read(root, CategoriesFile).DeepClone() });

    /// <summary>
    /// <c>inbox categorize &lt;thread&gt; (--category-id N | --clear)</c>: one absolute set, so a repeat lands on the same
    /// state. A live account answers a category it does not hold, on a thread it does, exactly as it answers a missing
    /// thread — one 404 with the thread's code — so reply-cli's own word for a missing category never comes back from a
    /// filing, and neither does it here.
    /// </summary>
    private static Ending InboxCategorize(string root, Parsed line)
    {
        if (IdOf(line.Operands[0]) is not { } id)
        {
            return BadId("Thread id");
        }

        var named = line.Option("--category-id");
        if ((named is null) == !line.Has("--clear"))
        {
            return Refused("usage.inbox", "Give exactly one of --category-id and --clear.", exit: 2);
        }

        int? categoryId = null;
        if (named is not null)
        {
            if (IdOf(named) is not { } parsed)
            {
                return BadId("--category-id");
            }

            categoryId = parsed;
        }

        var ids = new JsonObject { ["thread_id"] = id };
        var threads = Read(root, ThreadsFile);
        if (Find(threads, id) is not { } thread)
        {
            return Refused("inbox.thread_not_found", $"This account has no inbox thread {id} it can see.", status: 404, replyCode: "inboxThread.notFound", ids: ids);
        }

        JsonNode? category = null;
        if (categoryId is { } wanted)
        {
            if (Find(Read(root, CategoriesFile), wanted) is not { } held)
            {
                return Refused("inbox.thread_not_found", $"This account has no inbox thread {id} it can see.", status: 404, replyCode: "inboxThread.notFound", ids: ids);
            }

            category = new JsonObject { ["id"] = wanted, ["name"] = Text(held, "name") };
        }

        thread["category"] = category;
        Write(root, ThreadsFile, threads);
        return Done(new JsonObject
        {
            ["status"] = categoryId is null ? "cleared" : "categorized",
            ["thread_id"] = id,
            ["category_id"] = categoryId,
        });
    }

    /// <summary>
    /// <c>inbox mark-read</c> and <c>inbox mark-unread</c>: the read state set outright, a thread already so answered the
    /// same way.
    /// </summary>
    private static Ending InboxMark(string root, Parsed line, bool read)
    {
        if (IdOf(line.Operands[0]) is not { } id)
        {
            return BadId("Thread id");
        }

        var threads = Read(root, ThreadsFile);
        if (Find(threads, id) is not { } thread)
        {
            return Refused(
                "inbox.thread_not_found", $"This account has no inbox thread {id} it can see.", status: 200, replyCode: "notFound",
                ids: new JsonObject { ["thread_id"] = id });
        }

        thread["isRead"] = read;
        Write(root, ThreadsFile, threads);
        return Done(new JsonObject { ["status"] = read ? "read" : "unread", ["thread_id"] = id });
    }
}
