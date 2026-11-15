using System.Text.Json.Nodes;
using Jason.Runtime.Events;
using Jason.Runtime.Journal;
using Jason.Runtime.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Jason.Runtime.Notifications;

/// <summary>
/// The lines that say an email bounced: a <c>provider_event_observed</c> line whose event is a bounce, one in each campaign
/// the event reached, all written in the one transaction that stored the event. The seventh thing a pointer says, counted
/// as an account whose events were not read is — woken on, folded at a start, and counted by the sender in the window
/// since the last pointer that reached anybody — and by event, not by line: every line of one event carries the event's
/// own key. An event the store kept as history, or that reached no campaign, says no line, and is not counted.
/// </summary>
public static class BounceLines
{
    public const string Kind = JournalKinds.ProviderEventObserved;

    /// <summary>The provider event's kind a bounce is.</summary>
    public const string EventKind = ReadEvent.Bounce;

    /// <summary>Every line of the kind, for a caller to bound by position and to keep by <see cref="Bounced"/>.</summary>
    public static IQueryable<JournalEntry> Of(JasonDbContext db)
    {
        ArgumentNullException.ThrowIfNull(db);
        return db.Journal.AsNoTracking().Where(entry => entry.Kind == Kind);
    }

    /// <summary>Whether a line of the kind says a bounce: only its payload says, beside replies detected and invitations accepted.</summary>
    public static bool Bounced(JsonNode? payload) =>
        payload?["kind"] is JsonValue value && value.TryGetValue<string>(out var kind) && string.Equals(kind, EventKind, StringComparison.Ordinal);
}
