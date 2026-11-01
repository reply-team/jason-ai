using Jason.Runtime.Journal;
using Jason.Runtime.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Jason.Runtime.Notifications;

/// <summary>
/// The lines that say a provider block holds a campaign's work: one per campaign and block, under the block's public id,
/// the first written when the block opens. The fifth thing a pointer says, counted as an account's gap is — woken on,
/// folded at a start, and counted by the sender in the window since the last pointer that reached anybody — and by
/// block, not by line or campaign.
/// </summary>
public static class ProviderBlockLines
{
    public const string Kind = JournalKinds.ProviderBlocked;

    /// <summary>Every such line in the journal, for a caller to bound by position.</summary>
    public static IQueryable<JournalEntry> Of(JasonDbContext db)
    {
        ArgumentNullException.ThrowIfNull(db);
        return db.Journal.AsNoTracking().Where(entry => entry.Kind == Kind);
    }

    /// <summary>
    /// The lines that said their block first: the only ones that wake a pointer. A later line of a block, as the claim
    /// parks another campaign's items on it, says nothing a person was not already pointed at, whether the first was
    /// written before the window or inside it. Bounded by position like <see cref="Of"/>; the line before it is looked up
    /// over the kind and key index.
    /// </summary>
    public static IQueryable<JournalEntry> First(JasonDbContext db)
    {
        ArgumentNullException.ThrowIfNull(db);
        return Of(db).Where(entry => entry.Key != null
            && !db.Journal.Any(earlier => earlier.Kind == Kind && earlier.Key == entry.Key && earlier.Id < entry.Id));
    }
}
