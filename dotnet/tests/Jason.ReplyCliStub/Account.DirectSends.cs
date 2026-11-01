using System.Globalization;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

// Deliberately in the global namespace with the rest of this program.

/// <summary>
/// What a Reply that keeps direct sends adds to them, as reply-cli 0.17.0 documents it: a send may name the moment it goes
/// out after, <c>--send-after</c>, and carry a key, <c>--idempotency-key</c>; Reply answers it with its reference, the
/// moment it holds it for and its state, and <c>direct-send get</c> reads it back by that reference. A key Reply keeps
/// answers the same request again with the first answer, and sends nothing; the same key on another request is
/// <c>idempotency_key.reused</c>, carrying what Reply holds under it; a key whose first request is still being finished is
/// <c>idempotency_key.in_use</c>; and a request Reply refused keeps no key. All of it is one switch,
/// <c>direct_send</c>: a Reply that does not offer it has no <c>direct-send</c> route, and takes a send as it always took
/// one — now, with neither the moment nor the key read, and no reference in its answer. <c>direct-send cancel</c> takes back
/// a send it still holds, by the same reference.
/// </summary>
internal static partial class Account
{
    private const string KeysFile = "keys.json";
    private const string DirectSendsFile = "direct-sends.json";
    private const string ArrivalsFile = "keyed-arrivals.json";
    private const string SendFailsFile = "send-fails.json";

    /// <summary>A moment with its offset, as reply-cli takes one for <c>--send-after</c>: a date alone or a zoneless time is not one.</summary>
    private static readonly Regex OffsetMoment = new(
        @"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}(:\d{2}(\.\d+)?)?(Z|[+-]\d{2}:?\d{2})$", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    /// <summary>A send's channel as Reply writes it in its record, in its own enum, and reply-cli passes it on: <c>email</c> or <c>linkedIn</c>.</summary>
    internal static string WireChannel(string channel) => channel == "email" ? "email" : "linkedIn";

    /// <summary>The moment a send names, normalised to UTC as reply-cli sends it, or the CLI's usage error for one it does not take.</summary>
    private static (DateTimeOffset? At, Ending? Refusal) SendAfterOf(Parsed line)
    {
        if (line.Option("--send-after") is not { } raw)
        {
            return (null, null);
        }

        return OffsetMoment.IsMatch(raw)
            && DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.None, out var at)
            ? (at.ToUniversalTime(), null)
            : (null, Refused("usage.date", "--send-after must be a moment with its offset, e.g. 2026-11-03T14:00:00Z", exit: 2));
    }

    /// <summary>The sending schedule a send names, as reply-cli takes one for <c>--schedule-id</c>: a Reply id, or the CLI's usage error.</summary>
    private static (int? Id, Ending? Refusal) ScheduleOf(Parsed line)
    {
        if (line.Option("--schedule-id") is not { } raw)
        {
            return (null, null);
        }

        return IdOf(raw) is { } id ? (id, null) : (null, BadId("--schedule-id"));
    }

    /// <summary>
    /// Reply's refusal of a send timed by a schedule it cannot time it by, or null: a schedule the account holds none
    /// under, unknown and another account's alike; one that never opens, with no active day that has hours; and one read
    /// in the contact's own time zone for a contact Reply holds none for. The email route and the LinkedIn routes each
    /// name it in their own family, and reply-cli 0.18.0 gives both one code of its meaning
    /// (src/operations/direct-send.ts:130-138, mapped at :192-197). An account planted with no schedules times a send by
    /// any, as the rows that never read one need.
    /// </summary>
    private static Ending? Untimed(string root, DirectSend send, int schedule, int contactId, JsonObject ids)
    {
        var schedules = Records(root, SchedulesFile).ToList();
        if (schedules.Count == 0)
        {
            return null;
        }

        var family = send.Channel == "email" ? "contact." : "directLinkedIn.";
        if (schedules.FirstOrDefault(entry => entry["id"] is JsonValue id && id.TryGetValue<long>(out var held) && held == schedule) is not { } timing)
        {
            return Refused(
                "schedule.not_found", "Reply has no sending schedule by that id that this account can use, so nothing was sent.",
                status: 404, replyCode: family + "scheduleNotFound", ids: ids);
        }

        var opens = (timing["mainTimings"] as JsonArray ?? []).OfType<JsonObject>()
            .Any(day => Flag(day, "isActive") && day["timeRanges"] is JsonArray { Count: > 0 });
        if (!opens)
        {
            return Refused(
                "schedule.no_slot", "The sending schedule has no active day with a time range, so it never opens; nothing was sent.",
                status: 409, replyCode: family + "scheduleHasNoSlot", ids: ids);
        }

        var contact = Find(Read(root, ContactsFile), contactId);
        if (Flag(timing, "useProspectTimezone") && Text(contact, "timeZoneId").Length == 0)
        {
            return Refused(
                "contact.time_zone_missing", "Reply holds no time zone for the contact to read the schedule in, so nothing was sent.",
                status: 409, replyCode: family + "contactTimeZoneMissing", ids: ids);
        }

        return null;
    }

    /// <summary>
    /// The key a send carries, as reply-cli takes one: 1 to 255 printable ASCII characters, none of them a space at either
    /// end, or the CLI's usage error.
    /// </summary>
    private static (string? Key, Ending? Refusal) KeyOf(Parsed line)
    {
        if (line.Option("--idempotency-key") is not { } key)
        {
            return (null, null);
        }

        return key.Length is > 0 and <= 255 && key.All(character => character is >= ' ' and <= '~') && key[0] != ' ' && key[^1] != ' '
            ? (key, null)
            : (null, Refused("usage.idempotency_key", "--idempotency-key must be 1 to 255 printable ASCII characters, with no space at either end.", exit: 2));
    }

    /// <summary>
    /// What Reply answers a keyed send it has met before, or null for a key it keeps nothing under: the first answer again,
    /// for the same request; <c>idempotency_key.reused</c> for another; <c>idempotency_key.in_use</c> while the first is
    /// still being finished.
    /// </summary>
    private static Ending? KeptUnder(string root, DirectSend send, string key, string request, JsonObject ids)
    {
        if (Records(root, KeysFile).FirstOrDefault(entry => Text(entry, "key") == key) is not { } kept)
        {
            return null;
        }

        var prefix = send.Channel == "email" ? "contact." : "directLinkedIn.";
        if (kept["pending"] is JsonValue pending && pending.GetValue<bool>())
        {
            return Refused(
                "idempotency_key.in_use", "Reply is still finishing the first request made under this key, so nothing was sent.",
                status: 409, replyCode: prefix + "idempotencyKeyInUse", ids: ids);
        }

        if (Text(kept, "request") == request)
        {
            return Done((JsonObject)kept["answer"]!.DeepClone());
        }

        var details = new JsonObject();
        if (DirectSendOf(root, Text(kept, "reference")) is { } held)
        {
            details["reference"] = Text(held, "reference");
            details["state"] = Text(held, "state");
            if (held["sendAfter"] is JsonValue moment)
            {
                details["send_after"] = moment.GetValue<string>();
            }
        }

        return Refused(
            "idempotency_key.reused", "Reply holds this key for another request, so nothing was sent.",
            status: 409, replyCode: prefix + "idempotencyKeyReused", ids: ids, details: details);
    }

    /// <summary>Each request that reached Reply's send route carrying a key, in order: the command and the key.</summary>
    private static void Arrived(string root, string command, string key)
    {
        var arrivals = Read(root, ArrivalsFile);
        arrivals.Add(new JsonObject { ["command"] = command, ["key"] = key });
        Write(root, ArrivalsFile, arrivals);
    }

    /// <summary>
    /// A send Reply's route took and failed, where a test planted one, once: the CLI's ending for it — its code and exit,
    /// Reply's status, code and sentence — and, where Reply named the send, its record, in the state planted, with the
    /// reference, the state and the moment on the refusal as reply-cli passes them on. A failed send keeps no key, so the
    /// same key goes through again. A Reply that keeps no direct sends names none. <paramref name="went"/> says the test
    /// planted it as a send that went all the same, which the caller records as sent.
    /// </summary>
    private static Ending? FailedSend(
        string root, DirectSend send, int contactId, DateTimeOffset? sendAfter, JsonObject ids, bool keeps, out bool went)
    {
        went = false;
        var path = Path.Combine(root, SendFailsFile);
        if (!File.Exists(path))
        {
            return null;
        }

        var planted = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        File.Delete(path);
        went = Flag(planted, "went");
        var details = new JsonObject();
        var named = keeps && Flag(planted, "named");
        var state = planted["state"] is JsonValue said ? said.GetValue<string>() : named ? "failed" : null;
        if (named)
        {
            var sends = Read(root, DirectSendsFile);
            var reference = (send.Channel == "email" ? "email-" : "linkedin-") + (1001 + sends.Count).ToString(CultureInfo.InvariantCulture);
            sends.Add(new JsonObject
            {
                ["reference"] = reference,
                ["channel"] = WireChannel(send.Channel),
                ["contactId"] = contactId,
                ["state"] = state,
                ["sendAfter"] = sendAfter is { } moment ? Utc(moment) : null,
                ["sentAt"] = null,
                ["messageId"] = null,
                ["failureReason"] = planted["detail"]?.DeepClone(),
            });
            Write(root, DirectSendsFile, sends);
            details["reference"] = reference;
            if (sendAfter is { } held)
            {
                details["send_after"] = Utc(held);
            }
        }

        if (keeps && state is not null)
        {
            details["state"] = state;
        }

        var status = planted["status"] is JsonValue answered ? answered.GetValue<int>() : (int?)null;
        var replyCode = planted["reply_code"] is JsonValue code ? code.GetValue<string>() : null;
        if (status == 429 && replyCode is not null && SpentAllowances.ContainsKey(replyCode))
        {
            return SpentAllowance(replyCode, ids, details);
        }

        return Refused(
            Text(planted, "code"),
            "Reply took the send and it did not go.",
            exit: Number(planted, "exit"),
            status: status,
            replyCode: replyCode,
            detail: planted["detail"] is JsonValue detail ? detail.GetValue<string>() : null,
            ids: ids,
            details: details.Count == 0 ? null : details);
    }

    /// <summary>Reply's record of a send it kept: its reference, the moment it holds it for and its state; and the key, kept with its answer.</summary>
    private static JsonObject Kept(
        string root, DirectSend send, int contactId, DateTimeOffset? sendAfter, int? schedule, string? key, string request, JsonObject answer)
    {
        var sends = Read(root, DirectSendsFile);

        // A schedule holds the send for its next window, which this stand-in never reaches. While it waits Reply names no
        // moment, given one or not. One that opens later than ten minutes from now leaves the send waitingForSchedule,
        // with what Reply estimates under a member of its own, which reply-cli 0.18.0 passes on as estimated_send_at
        // (src/operations/direct-send.ts:320-323).
        var queued = (sendAfter is { } at && at > DateTimeOffset.UtcNow) || schedule is not null;
        var waiting = schedule is { } timedBy
            ? Records(root, SchedulesFile).FirstOrDefault(entry => entry["id"] is JsonValue id && id.TryGetValue<long>(out var held) && held == timedBy && entry.ContainsKey("_estimate"))
            : null;

        // A LinkedIn send Reply holds for later is linkedin-scheduled-<n> for its whole life; one that goes at once is
        // linkedin-<n>.
        var prefix = send.Channel == "email" ? "email-" : queued ? "linkedin-scheduled-" : "linkedin-";
        var reference = prefix + (1001 + sends.Count).ToString(CultureInfo.InvariantCulture);
        sendAfter = schedule is null ? sendAfter : null;
        var record = new JsonObject
        {
            ["reference"] = reference,
            ["channel"] = WireChannel(send.Channel),
            ["contactId"] = contactId,
            ["state"] = waiting is not null ? "waitingForSchedule" : queued ? "queued" : "sent",
            ["sendAfter"] = sendAfter is { } moment ? Utc(moment) : null,
            ["scheduleId"] = schedule,
            ["sentAt"] = queued ? null : answer["sent_at"]?.DeepClone(),
            ["messageId"] = queued ? null : answer["message_id"]?.DeepClone(),
        };
        if (waiting is not null)
        {
            record["estimatedSendAt"] = waiting["_estimate"]?.DeepClone();
        }

        sends.Add(record);
        Write(root, DirectSendsFile, sends);

        answer["reference"] = reference;
        if (sendAfter is { } named)
        {
            answer["send_after"] = Utc(named);
        }

        answer["state"] = Text(record, "state");
        if (waiting is not null)
        {
            answer["estimated_send_at"] = waiting["_estimate"]?.DeepClone();
        }
        if (queued)
        {
            // Held for its moment: nothing has gone, so nothing is named that went.
            foreach (var member in new[] { "message_id", "activity_id", "sent_at" })
            {
                if (answer.ContainsKey(member))
                {
                    answer[member] = null;
                }
            }
        }

        if (key is not null)
        {
            var keys = Read(root, KeysFile);
            keys.Add(new JsonObject { ["key"] = key, ["request"] = request, ["reference"] = reference, ["answer"] = answer.DeepClone() });
            Write(root, KeysFile, keys);
        }

        return answer;
    }

    /// <summary>
    /// <c>direct-send get &lt;reference&gt;</c>: a send Reply kept, by its reference. A Reply without the route answers the
    /// 404 reply-cli passes through as it is; one with it answers a reference it holds nothing under as
    /// <c>direct_send.not_found</c>.
    /// </summary>
    private static Ending DirectSendGet(string root, Parsed line)
    {
        var reference = line.Operands[0];
        if (!Regex.IsMatch(reference, Jason.ReplyCliStub.DirectSendReference.Pattern))
        {
            return Refused("usage.reference", "That is not a reference Reply gives: email-<n>, linkedin-<n> or linkedin-scheduled-<n>, n from 1.", exit: 2);
        }

        if (!Offered(root, "direct_send"))
        {
            return NotOffered("direct_send", Uri.EscapeDataString(reference), ids: null);
        }

        if (DirectSendOf(root, reference) is not { } held)
        {
            return Refused("direct_send.not_found", $"Reply holds no direct send {reference}.", status: 404, replyCode: "directSend.notFound");
        }

        var record = new JsonObject
        {
            ["reference"] = Text(held, "reference"),
            ["channel"] = Text(held, "channel"),
            ["contact_id"] = held["contactId"]!.DeepClone(),
            ["state"] = Text(held, "state"),
            ["send_after"] = held["sendAfter"]?.DeepClone(),
            ["sent_at"] = held["sentAt"]?.DeepClone(),
            ["message_id"] = held["messageId"]?.DeepClone(),
            ["failure_reason"] = held["failureReason"]?.DeepClone(),
            ["cancelled_at"] = held["cancelledAt"]?.DeepClone(),
        };

        // The moment Reply expects to send a send waiting for its schedule, only where it gave one, and null in every
        // other state, as reply-cli 0.18.0 passes it on (src/operations/direct-sends.ts:54).
        if (held.ContainsKey("estimatedSendAt"))
        {
            record["estimated_send_at"] = Text(held, "state") == "waitingForSchedule" ? held["estimatedSendAt"]?.DeepClone() : null;
        }

        // Reply's own code for a failure, only where it gave one, as reply-cli passes it on.
        if (held["failureCode"] is { } code)
        {
            record["failure_code"] = code.DeepClone();
        }

        // The message as it went, only where Reply kept that copy: a send it rendered names both.
        if (held["renderedSubject"] is { } subject)
        {
            record["rendered_subject"] = subject.DeepClone();
            record["rendered_body"] = held["renderedBody"]?.DeepClone();
        }

        return Done(record);
    }

    /// <summary>
    /// <c>direct-send cancel &lt;reference&gt;</c>: cancels a send Reply still holds for its moment, and answers its record as
    /// <c>direct-send get</c> prints it. A send already cancelled answers the same, cancelled when it first was; one that
    /// went is <c>direct_send.already_sent</c>, one Reply is sending now <c>direct_send.already_sending</c>, as a Reply that
    /// times deferred LinkedIn sends itself answers it, and one that failed <c>direct_send.already_failed</c>, with nothing
    /// changed. The reference and the route are read as <c>direct-send get</c> reads them.
    /// </summary>
    private static Ending DirectSendCancel(string root, Parsed line)
    {
        var reference = line.Operands[0];
        if (!Regex.IsMatch(reference, Jason.ReplyCliStub.DirectSendReference.Pattern))
        {
            return Refused("usage.reference", "That is not a reference Reply gives: email-<n>, linkedin-<n> or linkedin-scheduled-<n>, n from 1.", exit: 2);
        }

        if (!Offered(root, "direct_send"))
        {
            return NotOffered("direct_send", Uri.EscapeDataString(reference), ids: null, route: "POST /v3/direct-sends/{0}/cancel");
        }

        var sends = Read(root, DirectSendsFile);
        if (sends.OfType<JsonObject>().FirstOrDefault(entry => Text(entry, "reference") == reference) is not { } held)
        {
            return Refused("direct_send.not_found", $"Reply holds no direct send {reference}.", status: 404, replyCode: "directSend.notFound");
        }

        switch (Text(held, "state"))
        {
            case "sent":
                return Refused("direct_send.already_sent", $"Reply has sent {reference}, so it cannot be cancelled.", status: 409, replyCode: "directSend.alreadySent");
            case "sending":
                // reply-cli 0.18.0's src/operations/direct-send.ts:235, read for a cancel at src/operations/direct-sends.ts:77.
                return Refused("direct_send.already_sending", $"Reply is sending {reference}, so it cannot be cancelled.", status: 409, replyCode: "directSend.alreadySending");
            case "failed":
                return Refused("direct_send.already_failed", $"Reply failed to send {reference}, so it cannot be cancelled.", status: 409, replyCode: "directSend.alreadyFailed");
            case "cancelled":
                break;
            default:
                held["state"] = "cancelled";
                held["cancelledAt"] = Utc(DateTimeOffset.UtcNow);
                Write(root, DirectSendsFile, sends);
                break;
        }

        return DirectSendGet(root, line);
    }

    private static JsonObject? DirectSendOf(string root, string reference) =>
        Records(root, DirectSendsFile).FirstOrDefault(entry => Text(entry, "reference") == reference);

    private static string Utc(DateTimeOffset at) => at.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
}
