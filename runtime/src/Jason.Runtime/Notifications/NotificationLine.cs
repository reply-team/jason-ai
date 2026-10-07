using System.Globalization;
using System.Text.Json.Nodes;
using Jason.Contracts.Plugins;

namespace Jason.Runtime.Notifications;

/// <summary>
/// The <c>notification_sent</c> line's payload: which notification, the counts it carried, the journal line it covers
/// through, each channel's outcome, and when a notification last reached anybody. It is also the notifier's durable
/// watermark: a process starts reading from the line it covers through, and counts the floor from the last delivery
/// it carries forward.
/// </summary>
/// <remarks>
/// It names no campaign, work item or attempt, so it never wakes a manager; and it holds three counts, two journal
/// positions and the channels' outcomes, never what the notification said.
/// </remarks>
public sealed record NotificationLine(
    string NotificationId,
    int Approvals,
    int Decisions,
    string? CoveredThrough,
    IReadOnlyList<ChannelOutcome> Channels,
    DateTime? LastDeliveredAt,
    int ProtectiveFailures = 0,
    string? DeliveredThrough = null)
{
    public const string CoveredThroughKey = "covered_through";

    /// <summary>
    /// The line the last notification that reached anybody covered through, carried forward by one that reached nobody:
    /// where the next count of failed protective work starts.
    /// </summary>
    public const string DeliveredThroughKey = "delivered_through";

    public const string LastDeliveredAtKey = "last_delivered_at";

    /// <summary>The longest diagnostic a line keeps for one channel.</summary>
    public const int MaxDetail = 500;

    /// <summary>The longest receipt a line keeps for one channel.</summary>
    public const int MaxReceipt = 256;

    public JsonObject ToJson()
    {
        var channels = new JsonArray();
        foreach (var outcome in Channels)
        {
            var entry = new JsonObject
            {
                ["channel"] = outcome.Channel,
                ["kind"] = outcome.Kind,
                ["outcome"] = ChannelOutcome.Word(outcome.Result),
            };

            Put(entry, "code", outcome.Code);
            Put(entry, "class", outcome.Class is { } failure ? Class(failure) : null);
            Put(entry, "reason", outcome.Reason);
            Put(entry, "detail", Cut(outcome.Detail, MaxDetail));
            Put(entry, "receipt", Cut(outcome.Receipt, MaxReceipt));
            if (outcome.Result != ChannelResult.NotAttempted)
            {
                entry["attempts"] = outcome.Attempts;
            }

            channels.Add(entry);
        }

        return new JsonObject
        {
            ["notification_id"] = NotificationId,
            ["counts"] = new JsonObject { ["approvals"] = Approvals, ["decisions"] = Decisions, ["protective_failures"] = ProtectiveFailures },
            [CoveredThroughKey] = CoveredThrough,
            ["channels"] = channels,
            [LastDeliveredAtKey] = LastDeliveredAt is { } at ? Format(at) : null,
            [DeliveredThroughKey] = DeliveredThrough,
        };
    }

    /// <summary>A line as it was written, or null when what is there is not one this build wrote.</summary>
    public static NotificationLine? Read(JsonNode? payload)
    {
        if (payload is not JsonObject line
            || Text(line["notification_id"]) is not { } id
            || line["counts"] is not JsonObject counts
            || line["channels"] is not JsonArray channels)
        {
            return null;
        }

        var outcomes = new List<ChannelOutcome>(channels.Count);
        foreach (var node in channels)
        {
            if (node is not JsonObject entry
                || Text(entry["channel"]) is not { } channel
                || Text(entry["kind"]) is not { } kind
                || ChannelOutcome.FromWord(Text(entry["outcome"])) is not { } result)
            {
                return null;
            }

            outcomes.Add(new ChannelOutcome(
                channel,
                kind,
                result,
                Text(entry["code"]),
                ClassOf(Text(entry["class"])),
                Text(entry["reason"]),
                Text(entry["detail"]),
                Text(entry["receipt"]),
                entry["attempts"] is JsonValue attempts && attempts.TryGetValue<int>(out var count) ? count : 0));
        }

        var (coveredThrough, lastDeliveredAt) = ReadWatermark(line);
        return new NotificationLine(
            id,
            Number(counts["approvals"]),
            Number(counts["decisions"]),
            coveredThrough,
            outcomes,
            lastDeliveredAt,
            Number(counts["protective_failures"]),
            ReadDeliveredThrough(line));
    }

    /// <summary>
    /// Where a start counts failed protective work from: the line the last notification that reached anybody covered
    /// through. A line written before the key existed starts the window at what it covered itself, so history is not
    /// walked; a line that carries the key with no value starts it at the beginning of a journal that was empty then.
    /// </summary>
    public static string? ReadDeliveredThrough(JsonNode? payload) =>
        payload is not JsonObject line ? null
        : line.ContainsKey(DeliveredThroughKey) ? Text(line[DeliveredThroughKey])
        : Text(line[CoveredThroughKey]);

    /// <summary>What a start needs from the last line: where it covered through, and the last delivery it carried.</summary>
    public static (string? CoveredThrough, DateTime? LastDeliveredAt) ReadWatermark(JsonNode? payload)
    {
        if (payload is not JsonObject line)
        {
            return (null, null);
        }

        DateTime? delivered = Text(line[LastDeliveredAtKey]) is { } text
            && DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var at)
                ? DateTime.SpecifyKind(at, DateTimeKind.Utc)
                : null;
        return (Text(line[CoveredThroughKey]), delivered);
    }

    /// <summary>A time as the line writes it: ISO-8601 UTC to the millisecond.</summary>
    public static string Format(DateTime at) =>
        DateTime.SpecifyKind(at, DateTimeKind.Utc).ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture);

    private static string Class(FailureClass failure) => failure switch
    {
        FailureClass.Transient => "transient",
        FailureClass.Permanent => "permanent",
        FailureClass.Validation => "validation",
        FailureClass.Ambiguous => "ambiguous",
        _ => throw new ArgumentOutOfRangeException(nameof(failure), failure, "Not a failure class."),
    };

    private static FailureClass? ClassOf(string? word) => word switch
    {
        "transient" => FailureClass.Transient,
        "permanent" => FailureClass.Permanent,
        "validation" => FailureClass.Validation,
        "ambiguous" => FailureClass.Ambiguous,
        _ => null,
    };

    private static void Put(JsonObject entry, string key, string? value)
    {
        if (value is not null)
        {
            entry[key] = value;
        }
    }

    private static string? Cut(string? text, int length) => text is { Length: var count } && count > length ? text[..length] : text;

    private static string? Text(JsonNode? node) => node is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;

    private static int Number(JsonNode? node) => node is JsonValue value && value.TryGetValue<int>(out var number) ? number : 0;
}
