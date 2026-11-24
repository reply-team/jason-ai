using System.Globalization;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

// Deliberately in the global namespace with the rest of this program.

/// <summary>
/// One refusal of a follow-up: the code the CLI prints, the code Reply's API answers it under, the status, and the sentence.
/// </summary>
internal sealed record FollowUpCode(string Code, string ReplyCode, int Status, string Title);

/// <summary>
/// Every code a follow-up of a direct email can end in, in one table: the CLI's spelling of each, Reply's own beside it, the
/// status Reply answers a refusal at the request with, and the CLI's sentence. The spellings are the ones the rule the rest
/// of this program follows gives Reply's codes (<c>contact.notFound</c> is <c>contact.not_found</c>), until the reply-cli
/// release that takes <c>--follows</c> names them; the names are then one edit here. A code a follow-up that fails after it
/// was taken is left under is read by <c>direct-send get</c>, and is the same one.
/// </summary>
internal static class FollowUpCodes
{
    public static readonly FollowUpCode NotFound = new(
        "follows.not_found", "contact.followedSendNotFound", 404,
        "Reply holds no direct email of this account by that reference to follow, so nothing was sent.");

    public static readonly FollowUpCode NotOwned = new(
        "follows.not_owned", "contact.followedSendNotOwned", 403,
        "The direct email belongs to a teammate this caller cannot act for, so nothing was sent.");

    public static readonly FollowUpCode OtherContact = new(
        "follows.other_contact", "contact.followedSendOtherContact", 400,
        "The direct email went to another contact, so nothing was sent.");

    public static readonly FollowUpCode OtherMailbox = new(
        "follows.other_mailbox", "contact.followedSendOtherMailbox", 400,
        "The direct email went from another mailbox, so nothing was sent.");

    public static readonly FollowUpCode NeverSent = new(
        "follows.never_sent", "contact.followedSendNeverSent", 409,
        "The direct email was cancelled or failed for good and will never go, so the follow-up was not sent.");

    public static readonly FollowUpCode NotSent = new(
        "follows.not_sent", "contact.followedSendNotSent", 409,
        "The direct email has not gone and is held for a later moment, so the follow-up was not sent.");

    public static readonly FollowUpCode NotPlaced = new(
        "follows.not_placed", "contact.followUpNotPlaced", 409,
        "The mailbox could not place the follow-up in the thread of the email it follows, so it was not sent.");

    public static readonly FollowUpCode BeforeFollowedSend = new(
        "follows.too_early", "contact.followUpBeforeFollowedSend", 409,
        "The follow-up is held for a moment that is not later than the direct email's, so nothing was sent.");
}

/// <summary>
/// What a Reply that offers follow-ups does with a direct email that follows another, as the reply-cli that takes
/// <c>--follows &lt;reference&gt;</c> on <c>contact send-email</c> documents it: the send calls a route of its own, takes no
/// subject, and goes into the thread of the earlier email it names, as a reply to it, under that email's subject with
/// <c>Re: </c> before it, or it does not go. One switch, <c>direct_send_follow_up</c>, turns the route off.
/// <para>
/// Every direct email Reply keeps opens a thread of its own when it goes, in the inbox's own store, which is how a test
/// reads the placement with <c>inbox get</c>; and its record keeps the mailbox, the contact and the subject the refusals
/// of a follow-up are answered from. Reply's refusals at the request are answered before anything is taken. A follow-up
/// held for a later moment or a schedule reads the send it follows when it comes due, which this stand-in never reaches by
/// itself: a test moves time through the account's <c>time-moves.json</c> (see <see cref="Settle"/>), and the next command
/// applies it.
/// </para>
/// </summary>
internal static partial class Account
{
    private const string TimeMovesFile = "time-moves.json";

    /// <summary>The first id a thread this stand-in opens is given from, above any a test plants.</summary>
    private const int FirstOpenedThread = 5000;

    private static readonly Regex FollowsReference = new(@"^email-[1-9]\d{0,9}$", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    /// <summary>
    /// What <c>--follows</c> names: an email direct send's reference, or a usage error. Refusing a reference that is not
    /// <c>email-&lt;n&gt;</c> as <c>usage.reference</c> is this stand-in's own choice, not confirmed by reply-cli.
    /// </summary>
    private static (string? Reference, Ending? Refusal) FollowsOf(Parsed line)
    {
        if (line.Option("--follows") is not { } raw)
        {
            return (null, null);
        }

        return FollowsReference.IsMatch(raw)
            ? (raw, null)
            : (null, Refused("usage.reference", "--follows takes the reference of an earlier direct email: email-<n>, n from 1.", exit: 2));
    }

    private static Ending Refusal(FollowUpCode code, JsonObject ids) =>
        Refused(code.Code, code.Title, status: code.Status, replyCode: code.ReplyCode, ids: ids);

    /// <summary>
    /// Reply's refusals of a follow-up at the request, in the order it makes them, or null: no send of the account by that
    /// reference, a send to another contact, one from another mailbox where the follow-up names one, one that was cancelled
    /// or failed for good, a follow-up going at once when the send has not gone, and two exact moments where the
    /// follow-up's is not later. A send held for a schedule has no exact moment to compare.
    /// </summary>
    private static Ending? FollowRefusal(
        string root, string follows, int contactId, long? mailbox, DateTimeOffset? moment, int? schedule, JsonObject ids)
    {
        if (DirectSendOf(root, follows) is not { } named || Text(named, "channel") != WireChannel("email"))
        {
            return Refusal(FollowUpCodes.NotFound, ids);
        }

        if (named["contactId"] is JsonValue contact && contact.TryGetValue<int>(out var sentTo) && sentTo != contactId)
        {
            return Refusal(FollowUpCodes.OtherContact, ids);
        }

        if (mailbox is { } given && named["accountId"] is JsonValue from && from.TryGetValue<long>(out var sentFrom) && sentFrom != given)
        {
            return Refusal(FollowUpCodes.OtherMailbox, ids);
        }

        switch (Text(named, "state"))
        {
            case "cancelled" or "failed":
                return Refusal(FollowUpCodes.NeverSent, ids);
            case "sent":
                return null;
        }

        if (moment is null && schedule is null)
        {
            return Refusal(FollowUpCodes.NotSent, ids);
        }

        return moment is { } later && named["scheduleId"] is null
            && DateTimeOffset.TryParse(Text(named, "sendAfter"), CultureInfo.InvariantCulture, DateTimeStyles.None, out var earlier)
            && later <= earlier
            ? Refusal(FollowUpCodes.BeforeFollowedSend, ids)
            : null;
    }

    private const string PlaceFailsFile = "place-fails.json";

    /// <summary>
    /// A mailbox a test made unable to place the next follow-up in its thread (the road that fails inside a send that goes at
    /// once): the follow-up is recorded failed under Reply's code and refused as <see cref="FollowUpCodes.NotPlaced"/>, once.
    /// </summary>
    private static Ending? CannotPlace(string root, string follows, int contactId, long accountId, JsonObject ids)
    {
        var path = Path.Combine(root, PlaceFailsFile);
        if (!File.Exists(path))
        {
            return null;
        }

        File.Delete(path);
        var sends = Read(root, DirectSendsFile);
        var reference = "email-" + (1001 + sends.Count).ToString(CultureInfo.InvariantCulture);
        var record = new JsonObject
        {
            ["reference"] = reference, ["channel"] = WireChannel("email"), ["contactId"] = contactId, ["accountId"] = accountId,
            ["sendAfter"] = null, ["sentAt"] = null, ["messageId"] = null, ["follows"] = follows,
        };
        Failed(record, FollowUpCodes.NotPlaced);
        sends.Add(record);
        Write(root, DirectSendsFile, sends);
        return Refused(
            FollowUpCodes.NotPlaced.Code, FollowUpCodes.NotPlaced.Title, status: FollowUpCodes.NotPlaced.Status,
            replyCode: FollowUpCodes.NotPlaced.ReplyCode, ids: ids, details: new JsonObject { ["reference"] = reference, ["state"] = "failed" });
    }

    /// <summary>The subject a follow-up goes under: <c>Re: </c> and the subject of the email it follows, never doubled.</summary>
    private static string ReplySubject(string subject) =>
        subject.StartsWith("Re:", StringComparison.OrdinalIgnoreCase) ? subject : "Re: " + subject;

    /// <summary>
    /// A direct email goes: it opens a thread of its own, or, for a follow-up, is added to the thread of the send it follows
    /// under <c>Re: </c> and that send's subject, as a reply to its message. The records are changed in
    /// <paramref name="sends"/>, which the caller writes; the thread store is written here.
    /// </summary>
    private static void Gone(string root, JsonArray sends, JsonObject record)
    {
        if (Text(record, "channel") != WireChannel("email"))
        {
            return;
        }

        var threads = Read(root, ThreadsFile);
        if (record["messageId"] is null)
        {
            record["messageId"] = $"<{61_000 + sends.Count}.direct@mail.example.com>";
        }

        if (Text(record, "follows") is { Length: > 0 } follows
            && sends.OfType<JsonObject>().FirstOrDefault(entry => Text(entry, "reference") == follows) is { } named)
        {
            var thread = ThreadOfSend(threads, sends, named);
            var subject = ReplySubject(Text(named, "subject"));
            record["subject"] = subject;
            record["threadId"] = thread["id"]!.GetValue<int>();
            var message = ThreadMessage(record, subject, Text(record, "body"));
            message["replyToMessageId"] = named["messageId"]?.DeepClone();
            (thread["messages"] as JsonArray)!.Add(message);
            thread["lastActivityDate"] = message["date"]!.DeepClone();
        }
        else
        {
            ThreadOfSend(threads, sends, record);
        }

        Write(root, ThreadsFile, threads);
    }

    /// <summary>The thread of a send, opened with that send as its first message where it has none yet.</summary>
    private static JsonObject ThreadOfSend(JsonArray threads, JsonArray sends, JsonObject send)
    {
        if (send["threadId"] is JsonValue held && held.TryGetValue<int>(out var id) && Find(threads, id) is { } opened)
        {
            return opened;
        }

        if (send["messageId"] is null)
        {
            send["messageId"] = $"<{61_000 + sends.Count}.direct@mail.example.com>";
        }

        var message = ThreadMessage(send, Text(send, "subject"), Text(send, "body"));
        var thread = new JsonObject
        {
            ["id"] = Math.Max(FirstOpenedThread, threads.OfType<JsonObject>().Select(entry => entry["id"]!.GetValue<int>()).DefaultIfEmpty(0).Max()) + 1,
            ["channel"] = "email",
            ["isRead"] = true,
            ["contact"] = new JsonObject { ["id"] = send["contactId"]?.DeepClone() },
            ["emailAccountId"] = send["accountId"]?.DeepClone(),
            ["lastActivityDate"] = message["date"]!.DeepClone(),
            ["messages"] = new JsonArray(message),
        };
        threads.Add(thread);
        send["threadId"] = thread["id"]!.GetValue<int>();
        return thread;
    }

    /// <summary>A direct email as its thread holds it: the message this side wrote, dated in UTC with two fractional digits.</summary>
    private static JsonObject ThreadMessage(JsonObject send, string subject, string body)
    {
        var now = DateTimeOffset.UtcNow;
        now = now.AddTicks(-(now.Ticks % (TimeSpan.TicksPerMillisecond * 10)));
        return new JsonObject
        {
            ["channel"] = "email",
            ["messageId"] = send["messageId"]?.DeepClone(),
            ["date"] = now.ToString("yyyy-MM-dd'T'HH:mm:ss.ff'Z'", CultureInfo.InvariantCulture),
            ["subject"] = subject,
            ["body"] = body,
            ["isOutbound"] = true,
            ["status"] = null,
        };
    }

    /// <summary>
    /// Time passing, which this stand-in never does by itself: what a test asked for through <c>time-moves.json</c> —
    /// <c>{reference, to}</c> in order, <c>to</c> being <c>sent</c>, <c>failed</c>, <c>cancelled</c> or <c>due</c> — is applied
    /// before the next command answers. A send held for its moment goes, fails or is cancelled; a follow-up that comes due
    /// reads the send it follows: sent, it goes into that thread; cancelled or failed for good, it fails final as
    /// <see cref="FollowUpCodes.NeverSent"/>; held for a later moment, it fails final as <see cref="FollowUpCodes.NotSent"/>;
    /// and one whose send is due this very moment stays queued, for the retry Reply makes.
    /// </summary>
    private static void Settle(string root)
    {
        var path = Path.Combine(root, TimeMovesFile);
        if (!File.Exists(path))
        {
            return;
        }

        var moves = Read(root, TimeMovesFile);
        File.Delete(path);
        var sends = Read(root, DirectSendsFile);
        foreach (var move in moves.OfType<JsonObject>())
        {
            if (sends.OfType<JsonObject>().FirstOrDefault(entry => Text(entry, "reference") == Text(move, "reference")) is not { } held)
            {
                continue;
            }

            var waiting = Text(held, "state") is "queued" or "waitingForSchedule";
            switch (Text(move, "to"))
            {
                case "sent" when waiting:
                    Went(root, sends, held);
                    break;
                case "failed" when waiting:
                    held["state"] = "failed";
                    held["failureReason"] = "Reply could not send it.";
                    break;
                case "cancelled" when waiting:
                    held["state"] = "cancelled";
                    held["cancelledAt"] = Utc(DateTimeOffset.UtcNow);
                    break;
                case "due" when waiting && Text(held, "follows").Length > 0:
                    FollowUpDue(root, sends, held);
                    break;
            }
        }

        Write(root, DirectSendsFile, sends);
    }

    private static void Went(string root, JsonArray sends, JsonObject held)
    {
        held["state"] = "sent";
        held["sentAt"] = Utc(DateTimeOffset.UtcNow);
        held.Remove("estimatedSendAt");
        Gone(root, sends, held);
    }

    private static void FollowUpDue(string root, JsonArray sends, JsonObject held)
    {
        var named = sends.OfType<JsonObject>().FirstOrDefault(entry => Text(entry, "reference") == Text(held, "follows"));
        switch (named is null ? "cancelled" : Text(named, "state"))
        {
            case "sent":
                Went(root, sends, held);
                return;
            case "cancelled" or "failed":
                Failed(held, FollowUpCodes.NeverSent);
                return;
        }

        // Still held: one whose moment has passed is due this very scan, so Reply tries again; any other waits for a
        // later moment, and this follow-up will not wait for it.
        var passed = Text(named!, "state") == "queued"
            && DateTimeOffset.TryParse(Text(named!, "sendAfter"), CultureInfo.InvariantCulture, DateTimeStyles.None, out var at)
            && at <= DateTimeOffset.UtcNow;
        if (!passed)
        {
            Failed(held, FollowUpCodes.NotSent);
        }
    }

    private static void Failed(JsonObject held, FollowUpCode code)
    {
        held["state"] = "failed";
        held["failureReason"] = code.Title;
        held["failureCode"] = code.ReplyCode;
    }
}
