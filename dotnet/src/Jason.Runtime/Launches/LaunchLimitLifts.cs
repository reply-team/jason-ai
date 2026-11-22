using Jason.Runtime.Configuration;
using Jason.Runtime.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Jason.Runtime.Launches;

/// <summary>The one question the claim and the lift ask of the lifts: whether one covers a day.</summary>
public static class LaunchLimitLifts
{
    /// <summary>
    /// Whether a person lifted the daily limits for <paramref name="day"/>: a row of its local date taken in its zone. A row
    /// of an earlier date, or of the same date in another zone, covers nothing today. One indexed read.
    /// </summary>
    public static Task<bool> CoverAsync(JasonDbContext db, LocalDay day, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(db);
        return db.LaunchLimitLifts.AsNoTracking().AnyAsync(l => l.LocalDate == day.Date && l.TimeZone == day.TimeZone, ct);
    }
}
