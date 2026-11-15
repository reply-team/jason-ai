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

    /// <summary>
    /// How many events said a bounce in the lines of the kind in <c>(after, upTo]</c>: counted by the database, by the key
    /// every line of one event carries, so a window that grows while no pointer reaches anybody is never read into memory.
    /// The payload's <c>$.kind</c> is the one <see cref="Bounced"/> reads, and SQLite's <c>=</c> keeps exactly the payloads
    /// it keeps: a number, a boolean, an object, an array, a null or an absent kind is never the bounce's text. Read through
    /// the kind index, bounded by the window on both sides.
    /// </summary>
    public static IQueryable<int> Count(JasonDbContext db, int after, int upTo)
    {
        ArgumentNullException.ThrowIfNull(db);
        return db.Database.SqlQuery<int>($"""
            SELECT count(DISTINCT key) AS "Value" FROM journal INDEXED BY ix_journal_kind
            WHERE kind = {Kind} AND id > {after} AND id <= {upTo} AND json_extract(new_json, '$.kind') = {EventKind}
            """);
    }

    /// <summary>Whether a line of the kind says a bounce: only its payload says, beside replies detected and invitations accepted.</summary>
    public static bool Bounced(JsonNode? payload) =>
        payload?["kind"] is JsonValue value && value.TryGetValue<string>(out var kind) && string.Equals(kind, EventKind, StringComparison.Ordinal);
}
