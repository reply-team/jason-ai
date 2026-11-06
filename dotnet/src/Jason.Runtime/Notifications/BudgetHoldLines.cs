using System.Text.Json.Nodes;
using Jason.Runtime.Journal;
using Jason.Runtime.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Jason.Runtime.Notifications;

/// <summary>
/// The lines that say the claim held an item at a daily limit of launches: a <c>workitem_waiting</c> line that names
/// <c>held_by</c>, written once per hold. The sixth thing a pointer says. These lines wake a pointer and are folded at a
/// start; what the pointer then counts is the items that wait on a limit as it is sent, as approvals are counted, not
/// these lines.
/// </summary>
public static class BudgetHoldLines
{
    public const string Kind = JournalKinds.WorkItemWaiting;

    /// <summary>Every line of the kind, for a caller to bound by position and to keep by <see cref="Holds"/>.</summary>
    public static IQueryable<JournalEntry> Of(JasonDbContext db)
    {
        ArgumentNullException.ThrowIfNull(db);
        return db.Journal.AsNoTracking().Where(entry => entry.Kind == Kind);
    }

    /// <summary>Whether a line of the kind is a hold: only its payload says, and a wait of any other sort does not name one.</summary>
    public static bool Holds(JsonNode? payload) => payload?["held_by"] is JsonObject;
}
