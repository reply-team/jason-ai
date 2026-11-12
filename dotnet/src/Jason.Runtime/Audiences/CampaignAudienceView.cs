using System.Globalization;
using System.Text.Json;
using Jason.Contracts.Api;
using Jason.Runtime.Approvals;
using Jason.Runtime.Campaigns;
using Jason.Runtime.Contacts;
using Jason.Runtime.Domain;
using Jason.Runtime.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Jason.Runtime.Audiences;

/// <summary>
/// <c>campaign.audience</c>: where one campaign's audience stands, as the facts its records hold — the reserve's count and
/// threshold, a page of its audiences with the searches the provider took under each and the memberships they landed, and
/// the allowance with its use. Nothing is kept for the read; each part is read where the runtime keeps it, through an index,
/// so a campaign's hundredth audience costs what its first does.
/// </summary>
public sealed class CampaignAudienceView(JasonDbContext db, TimeProvider clock)
{
    public const int DefaultSearchLimit = 10;

    public const int MaxSearchLimit = 100;

    public async Task<CampaignAudienceResponse> ReadAsync(CampaignAudienceRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var limit = Paging.ResolveLimit(request.Limit);
        var each = SearchLimit(request.SearchLimit);
        var before = Before(request.Cursor);
        var campaign = await CampaignService.LoadAsync(db, request.CampaignId, cancellationToken).ConfigureAwait(false);

        var untouched = await CampaignPeople.UntouchedAsync(db, campaign.Id, cancellationToken).ConfigureAwait(false);
        var allowance = await StandingApprovals.AllowanceAsync(db, campaign.Id, cancellationToken).ConfigureAwait(false);
        var fetched = await Page(db, campaign.Id, before, limit + 1).ToListAsync(cancellationToken).ConfigureAwait(false);
        var shown = fetched.Take(limit).ToList();
        var ids = shown.Select(audience => audience.Id).ToList();
        // The counts before the rows: a search that begins between the two reads is then a row the counts have not seen,
        // never a count with no row behind it that a reader would take for an earlier search.
        var tallies = await Tallies(db, ids).ToDictionaryAsync(tally => tally.AudienceId, cancellationToken).ConfigureAwait(false);
        var searches = (await Searches(db, ids, each).ToListAsync(cancellationToken).ConfigureAwait(false))
            .OrderByDescending(search => search.Id)
            .ToLookup(search => search.AudienceId);

        return new CampaignAudienceResponse(
            campaign.PublicId,
            new AudienceReserve(untouched, campaign.AudienceThreshold),
            allowance is null
                ? null
                : StandingApprovalService.Dto(campaign, allowance.Record, allowance.Released, allowance.People, clock.GetUtcNow().UtcDateTime),
            [.. shown.Select(audience => new CampaignAudienceDto(
                audience.PublicId,
                audience.Number,
                ContactMapper.ToOffset(audience.CreatedAt),
                audience.Definition.DeepClone().AsObject(),
                tallies.GetValueOrDefault(audience.Id)?.Landed ?? 0,
                tallies.GetValueOrDefault(audience.Id)?.Searches ?? 0,
                [.. searches[audience.Id].Select(search => new AudienceSearchDto(
                    search.Search,
                    search.State,
                    search.Asked,
                    search.Found,
                    search.Landed,
                    ContactMapper.ToOffset(search.BeganAt),
                    search.EndedAs,
                    search.EndedAt is { } ended ? ContactMapper.ToOffset(ended) : null))]))],
            fetched.Count > limit ? Paging.EncodeCursor(shown[^1].Number.ToString(CultureInfo.InvariantCulture)) : null);
    }

    /// <summary>
    /// A page of the campaign's audiences, the newest first, those numbered under <paramref name="before"/> where a page came
    /// before: off the end of the campaign's index on its numbers.
    /// </summary>
    internal static IQueryable<Audience> Page(JasonDbContext db, int campaignId, int? before, int take)
    {
        ArgumentNullException.ThrowIfNull(db);
        var audiences = db.Audiences.AsNoTracking().Where(audience => audience.CampaignId == campaignId);
        if (before is { } below)
        {
            audiences = audiences.Where(audience => audience.Number < below);
        }

        return audiences.OrderByDescending(audience => audience.Number).Take(take);
    }

    /// <summary>
    /// The latest <paramref name="each"/> searches the provider took under each of these audiences, by the searches' index
    /// on the audience, in no order: a campaign that tops an audience up all year answers the same few rows each time.
    /// </summary>
    /// <remarks>
    /// In SQL because "the latest few of each" has no translation EF makes for SQLite. Each audience's searches are taken
    /// off the end of its index on the audience, newest first, and the reading stops at the limit, so an audience's
    /// thousandth search costs no more than its tenth.
    /// </remarks>
    internal static IQueryable<SearchRow> Searches(JasonDbContext db, IReadOnlyCollection<int> audiences, int each)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(audiences);
        var asked = JsonSerializer.Serialize(audiences);
        return db.Database.SqlQuery<SearchRow>($"""
            SELECT s.id AS Id, s.audience_id AS AudienceId, w.public_id AS Search, s.state AS State, s.asked AS Asked,
                   s.found AS Found, s.landed AS Landed, s.created_at AS BeganAt, s.ended_as AS EndedAs, s.ended_at AS EndedAt
            FROM audiences AS a
            JOIN audience_searches AS s ON s.id IN (
                SELECT t.id FROM audience_searches AS t WHERE t.audience_id = a.id ORDER BY t.id DESC LIMIT {each})
            JOIN work_items AS w ON w.id = s.search_work_item_id
            WHERE a.id IN (SELECT value FROM json_each({asked}))
            """);
    }

    /// <summary>
    /// How many searches the provider took under each of these audiences, and how many memberships they landed between
    /// them: from the search rows, whose count the landing moves in the change set that writes each landing row, so the two
    /// numbers come from one read and the cost grows with the searches, not with the people.
    /// </summary>
    internal static IQueryable<Tally> Tallies(JasonDbContext db, IReadOnlyCollection<int> audiences)
    {
        ArgumentNullException.ThrowIfNull(db);
        return db.AudienceSearches.AsNoTracking()
            .Where(search => audiences.Contains(search.AudienceId))
            .GroupBy(search => search.AudienceId)
            .Select(group => new Tally(group.Key, group.Count(), group.Sum(search => search.Landed)));
    }

    /// <summary>How many of each audience's searches a page lists: 1 to <see cref="MaxSearchLimit"/>, and <see cref="DefaultSearchLimit"/> when absent.</summary>
    private static int SearchLimit(int? asked) => asked switch
    {
        null => DefaultSearchLimit,
        >= 1 and <= MaxSearchLimit => asked.Value,
        _ => throw new ValidationException([new ErrorDetail(
            "search_limit", "invalid", string.Create(CultureInfo.InvariantCulture, $"search_limit must be between 1 and {MaxSearchLimit}."))]),
    };

    /// <summary>The number of the last audience a page showed; the next page reads those numbered under it.</summary>
    private static int? Before(string? cursor)
    {
        if (Paging.DecodeCursor(cursor) is not { } decoded)
        {
            return null;
        }

        return int.TryParse(decoded, NumberStyles.None, CultureInfo.InvariantCulture, out var number) && number > 0
            ? number
            : throw new ValidationException([new ErrorDetail("cursor", "invalid", "cursor must be the next_cursor returned by an earlier page.")]);
    }

    internal sealed record SearchRow(
        int Id, int AudienceId, string Search, string? State, int Asked, int? Found, int Landed, DateTime BeganAt, string? EndedAs, DateTime? EndedAt);

    internal sealed record Tally(int AudienceId, int Searches, int Landed);
}
