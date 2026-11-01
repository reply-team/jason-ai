using System.Text.Json.Nodes;
using Jason.Runtime.Journal;
using Jason.Runtime.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Jason.Runtime.Notifications;

/// <summary>
/// The lines that say an account has a span of events nobody will read: one each, said once by the read that skips it.
/// The fourth thing a pointer says, counted as failed protective work is — woken on, folded at a start, and counted by
/// the sender in the window since the last pointer that reached anybody — and by account, not by line.
/// </summary>
public static class EventGapLines
{
    public const string Kind = JournalKinds.EventsGapObserved;

    /// <summary>Every such line in the journal, for a caller to bound by position.</summary>
    public static IQueryable<JournalEntry> Of(JasonDbContext db)
    {
        ArgumentNullException.ThrowIfNull(db);
        return db.Journal.AsNoTracking().Where(entry => entry.Kind == Kind);
    }

    /// <summary>The account a gap line is about: the source it names.</summary>
    public static string Source(JsonNode? payload) => payload?["source"] is JsonValue value && value.TryGetValue<string>(out var source) ? source : string.Empty;
}
