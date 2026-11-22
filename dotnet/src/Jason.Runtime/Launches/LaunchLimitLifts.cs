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

    /// <summary>
    /// Whether a person's release at <paramref name="releasedAt"/> was a lift's: made once a lift was taken and within the
    /// day it covers, in the zone it was taken in. A lift lets go the holds of its day as a person's resume, and the claim
    /// asks this so that what a lift let go is past the limit only while the lift covers the day, where a person's own resume
    /// of one item keeps its run past the limit. A day is at most 25 hours, so only the lifts of the 26 hours before the
    /// release are read; a lift whose zone this system no longer knows covers nothing.
    /// </summary>
    public static async Task<bool> ReleasedAsync(JasonDbContext db, DateTime releasedAt, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(db);
        var earliest = releasedAt.AddHours(-26);
        var lifts = await db.LaunchLimitLifts.AsNoTracking()
            .Where(l => l.CreatedAt <= releasedAt && l.CreatedAt >= earliest)
            .Select(l => new { l.LocalDate, l.TimeZone })
            .ToListAsync(ct)
            .ConfigureAwait(false);
        return lifts.Any(lift => InstallationDay.ZoneOf(lift.TimeZone) is { } zone
            && LocalDay.Of(releasedAt, zone, lift.TimeZone).Date == lift.LocalDate);
    }
}
