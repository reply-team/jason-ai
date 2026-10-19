using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Jason.Contracts.Events;
using Jason.Contracts.Operations;

namespace Jason.Runtime.Events;

/// <summary>
/// One page a read of events returned, after the events contract's output schema accepted it: the events, the cursor
/// to start the next read from, and whether the provider holds more now.
/// </summary>
public sealed record EventPage(IReadOnlyList<ReadEvent> Events, string Cursor, bool HasMore)
{
    /// <summary>
    /// The page a read's result is, held to the events contract's output schema first: null with the schema's problems
    /// when the schema refuses it, and null with none when the schema accepted something this reader cannot read — which
    /// the moments' one vocabulary is there to make impossible, and which the reader treats as a result outside the
    /// contract all the same. The one way a read's result becomes a page, so nothing unchecked reaches the store.
    /// </summary>
    public static EventPage? Read(EventsContract contract, JsonNode? result, out IReadOnlyList<SchemaProblem> problems)
    {
        ArgumentNullException.ThrowIfNull(contract);
        problems = contract.CheckResult(result);
        return problems.Count == 0 ? Read(result) : null;
    }

    /// <summary>
    /// The page a result is, or null when it is not one. The schema has already accepted it, so this only reads what
    /// the schema promised; a value that is not where the schema puts it means the result was never checked, and is
    /// not read as a page at all.
    /// </summary>
    internal static EventPage? Read(JsonNode? result)
    {
        if (result is not JsonObject page
            || page["events"] is not JsonArray events
            || page["cursor"] is not JsonValue cursor || cursor.GetValueKind() != JsonValueKind.String
            || page["has_more"] is not JsonValue more || more.GetValueKind() is not (JsonValueKind.True or JsonValueKind.False))
        {
            return null;
        }

        var read = new List<ReadEvent>(events.Count);
        foreach (var node in events)
        {
            if (ReadEvent.Read(node) is not { } one)
            {
                return null;
            }

            read.Add(one);
        }

        return new EventPage(read, cursor.GetValue<string>(), more.GetValue<bool>());
    }
}

/// <summary>
/// One event as the contract publishes it: its kind, when it happened, and the event itself — identifiers, moments and
/// the provider's own flags, and nothing else, because the schema admits nothing else.
/// </summary>
public sealed record ReadEvent(string Kind, DateTimeOffset OccurredAt, JsonObject Data)
{
    public const string ThreadMoved = "thread_moved";

    public const string ReplyDetected = "reply_detected";

    public const string InvitationAccepted = "invitation_accepted";

    public const string Bounce = "bounce";

    /// <summary>The word <c>moved_by</c> says a message the other side wrote with.</summary>
    public const string MovedByOtherSide = "other_side";

    /// <summary>
    /// The identifiers each kind names, in the order the contract lists them: what makes two reads of one event the same.
    /// A test holds the list to the contract's branches, so a kind or an identifier added there is added here.
    /// </summary>
    public static IReadOnlyDictionary<string, string[]> Identifiers { get; } = new Dictionary<string, string[]>(StringComparer.Ordinal)
    {
        [ThreadMoved] = ["thread", "contact", "campaign"],
        [ReplyDetected] = ["contact", "campaign", "thread"],
        [InvitationAccepted] = ["contact", "campaign", "account"],
        [Bounce] = ["contact", "campaign", "message"],
    };

    /// <summary>The string a field holds, or null where it holds none.</summary>
    public string? Text(string name) =>
        Data[name] is JsonValue value && value.GetValueKind() == JsonValueKind.String ? value.GetValue<string>() : null;

    /// <summary>The provider's campaign the event names, or null.</summary>
    public string? Campaign => Text("campaign");

    /// <summary>The provider's person the event names, or null.</summary>
    public string? Contact => Text("contact");

    /// <summary>
    /// Whether the event can be a reply from the other side: a thread the other side moved, or one the provider cannot
    /// say who moved. A move by the account or by the provider — a reply Jason sent, a category set — is not.
    /// </summary>
    public bool MovedByTheOtherSide => Kind == ThreadMoved && Text("moved_by") is null or MovedByOtherSide;

    /// <summary>
    /// The moments an event can happen at, as the contract's schema writes them: UTC with <c>Z</c>, to the second or to a
    /// fraction of it of at most seven digits. Read exactly so, the reader and the schema refuse the same moments, and a
    /// page the schema accepted is always a page here.
    /// </summary>
    private static readonly string[] Moments =
    [
        "yyyy'-'MM'-'dd'T'HH':'mm':'ss'Z'",
        "yyyy'-'MM'-'dd'T'HH':'mm':'ss'.'f'Z'",
        "yyyy'-'MM'-'dd'T'HH':'mm':'ss'.'ff'Z'",
        "yyyy'-'MM'-'dd'T'HH':'mm':'ss'.'fff'Z'",
        "yyyy'-'MM'-'dd'T'HH':'mm':'ss'.'ffff'Z'",
        "yyyy'-'MM'-'dd'T'HH':'mm':'ss'.'fffff'Z'",
        "yyyy'-'MM'-'dd'T'HH':'mm':'ss'.'ffffff'Z'",
        "yyyy'-'MM'-'dd'T'HH':'mm':'ss'.'fffffff'Z'",
    ];

    internal static ReadEvent? Read(JsonNode? node)
    {
        if (node is not JsonObject data
            || data["kind"] is not JsonValue kind || kind.GetValueKind() != JsonValueKind.String
            || !Identifiers.ContainsKey(kind.GetValue<string>())
            || data["occurred_at"] is not JsonValue occurred || occurred.GetValueKind() != JsonValueKind.String
            || !DateTimeOffset.TryParseExact(occurred.GetValue<string>(), Moments, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var moment))
        {
            return null;
        }

        return new ReadEvent(kind.GetValue<string>(), moment.ToUniversalTime(), (JsonObject)data.DeepClone());
    }
}
