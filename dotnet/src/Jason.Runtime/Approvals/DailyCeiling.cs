using System.Globalization;
using System.Text.Json.Nodes;
using Jason.Runtime.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Jason.Runtime.Approvals;

/// <summary>
/// The daily ceiling of automatic sends: how many sends the automatic mode released from one campaign since the UTC day
/// began, counted by the rows it wrote.
/// </summary>
/// <remarks>
/// A release is a row born approved with the reason <c>automatic_mode</c>, decided at the claim's moment. The count is of
/// those rows, whatever became of each since — run, superseded at a retry, cancelled with its work — since a release spent
/// is a release counted. A retry runs under the row it was released under, so one send is never counted twice; a person's
/// approval writes another reason and counts for nothing.
/// </remarks>
public static class DailyCeiling
{
    /// <summary>The preview's member that carries the ceiling's three numbers where a send parks at it.</summary>
    public const string PreviewKey = "daily_ceiling";

    /// <summary>
    /// The partial index the count is read through: a campaign's automatic releases by the moment each was decided. It holds
    /// those rows and no other, so the count visits one campaign's releases of the day and nothing else.
    /// </summary>
    public const string Index = "ix_approvals_automatic_campaign_decided_at";

    /// <summary>
    /// How many sends the automatic mode released from <paramref name="campaignId"/> since <paramref name="since"/>, through
    /// <see cref="Index"/>. The reason is written into the text, not bound, so SQLite can tell the read is within the index's
    /// filter; a parameter there would leave it unable to use the index at all, and the named index makes that an error.
    /// </summary>
    internal static IQueryable<int> Released(JasonDbContext db, int campaignId, DateTime since) =>
        db.Database.SqlQuery<int>($"""
            SELECT count(*) AS "Value" FROM approvals INDEXED BY ix_approvals_automatic_campaign_decided_at
            WHERE reason = 'automatic_mode' AND campaign_id = {campaignId} AND decided_at >= {since}
            """);

    /// <summary>
    /// The ceiling a scan decides against: <paramref name="PerDay"/>, read from the settings once at the scan's start, and
    /// <paramref name="Since"/>, the 00:00Z the count begins at.
    /// </summary>
    internal sealed record Limit(int PerDay, DateTime Since);

    /// <summary>
    /// What the preview of a send parked at the ceiling says of it: three numbers and no content — the limit in force, the
    /// releases counted, and the moment the count began.
    /// </summary>
    internal static JsonObject Preview(Limit ceiling, int released)
    {
        ArgumentNullException.ThrowIfNull(ceiling);
        return new JsonObject
        {
            ["limit"] = ceiling.PerDay,
            ["released"] = released,
            ["since"] = ceiling.Since.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture),
        };
    }
}
