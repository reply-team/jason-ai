using System.Text.Json.Nodes;
using Jason.Runtime.Dispatch;
using Jason.Runtime.Json;

namespace Jason.Runtime.Events;

/// <summary>
/// What makes two reads of one event the same, and what a line about it is said under. Keyed by the plugin and never by
/// the account it was read from: two bindings that name one account read each event twice, and the store keeps it once.
/// </summary>
public static class EventKeys
{
    /// <summary>
    /// The event's own key: <c>sha256:</c> over the plugin, the kind, the identifiers the kind names and the moment it
    /// happened, to the millisecond as every timestamp the runtime writes — and, for a moved thread, who moved it: the
    /// provider's move and the other side's reply at the same moment are two events, and the reply is the one a line says.
    /// The provider's own event id is kept with the event and is not part of it, so a provider that numbers a re-read
    /// afresh does not make one event two.
    /// </summary>
    public static string Store(string pluginId, ReadEvent read)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pluginId);
        ArgumentNullException.ThrowIfNull(read);
        var key = new JsonObject
        {
            ["plugin"] = pluginId,
            ["kind"] = read.Kind,
            ["moment"] = Moment(read),
        };

        foreach (var name in ReadEvent.Identifiers[read.Kind])
        {
            key[name] = read.Text(name);
        }

        if (read.Kind == ReadEvent.ThreadMoved)
        {
            key["moved_by"] = read.Text("moved_by");
        }

        return CanonicalJson.Hash(key);
    }

    /// <summary>
    /// What a line about the event is said under in a campaign. A thread the other side moved is said under the inbound
    /// read's own key — the plugin, the thread and the moment — so a move both reads found is said once; every other
    /// event under its own key.
    /// </summary>
    public static string Line(string pluginId, ReadEvent read)
    {
        ArgumentNullException.ThrowIfNull(read);
        return read.Kind == ReadEvent.ThreadMoved
            ? InboundSync.ObservationKey(pluginId, read.Text("thread")!, Moment(read))
            : Store(pluginId, read);
    }

    /// <summary>When the event happened, as every timestamp the runtime writes: ISO-8601 UTC to the millisecond.</summary>
    public static string Moment(ReadEvent read)
    {
        ArgumentNullException.ThrowIfNull(read);
        return InboundSync.Instant(read.OccurredAt);
    }
}
