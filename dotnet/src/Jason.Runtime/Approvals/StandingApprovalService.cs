using System.Globalization;
using System.Text.Json.Nodes;
using Jason.Contracts.Api;
using Jason.Runtime.Audiences;
using Jason.Runtime.Campaigns;
using Jason.Runtime.Domain;
using Jason.Runtime.Execution;
using Jason.Runtime.Journal;
using Jason.Runtime.Persistence;
using Jason.Runtime.Replies;
using Microsoft.EntityFrameworkCore;

namespace Jason.Runtime.Approvals;

/// <summary>
/// The three verbs of a campaign's standing approvals: set one for a category of reply, or the allowance for its
/// searches; take it back; and read what stands. Only a person sets or unsets one, and the request names them; any caller
/// reads them. Nothing inside the runtime calls the two that change them: the claim reads the record, and the record is
/// the chronicle's lines.
/// </summary>
public sealed class StandingApprovalService(JasonDbContext db, JournalWriter journal, TimeProvider clock)
{
    /// <summary>
    /// Sets the campaign's standing approval for a category whole, or its allowance. A request equal to the one in force
    /// writes nothing and answers with it, so a set sent again after its answer was lost is never a second line. Any other
    /// set for a category is a new line that replaces the record, and the count its ceiling reads starts again from it; any
    /// other allowance retires the one before it, an unset and then the set.
    /// </summary>
    public async Task<StandingApprovalDto> SetAsync(ApprovalSetStandingRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var actor = Person(request.Actor);
        if (request.Operations?.Any(operation => string.Equals(operation?.Trim(), StandingApprovals.Searches, StringComparison.Ordinal)) == true)
        {
            return await AllowAsync(request, actor, cancellationToken).ConfigureAwait(false);
        }

        var now = clock.GetUtcNow();
        var errors = new ValidationErrors();
        var category = ReplyService.Category(request.Category, errors);
        if (string.Equals(category, StandingApprovals.Searches, StringComparison.Ordinal))
        {
            errors.Add("category", "reserved", $"{StandingApprovals.Searches} names the allowance for the campaign's searches, never a category of reply: set it with operations {StandingApprovals.Searches}.");
        }

        var operations = Operations(request.Operations, errors);
        if (request.ExpiresAt is { } expires && expires <= now)
        {
            errors.Add("expires_at", "in_the_past", "expires_at must be a moment later than now.");
        }

        if (request.MaxReleases is < 1 or > StandingApprovals.MaxReleases)
        {
            errors.Add("max_releases", "invalid", string.Create(CultureInfo.InvariantCulture, $"max_releases must be from 1 to {StandingApprovals.MaxReleases}."));
        }

        NotForAReply(request.Audience, "audience", errors);
        NotForAReply(request.MaxPeople, "max_people", errors);
        NotForAReply(request.MaxPerSearch, "max_per_search", errors);
        ValidateReason(request.Reason, errors);
        errors.ThrowIfAny();

        var campaign = await Changeable(request.CampaignId, cancellationToken).ConfigureAwait(false);
        var record = StandingApprovals.Line(category!, operations!, request.ExpiresAt, request.MaxReleases);
        var newest = await StandingApprovals.Newest(db, campaign.Id, category!).FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        var standing = newest is { Kind: JournalKinds.StandingApprovalSet } ? newest : null;
        if (await SameInForceAsync(campaign, standing, record, cancellationToken).ConfigureAwait(false) is { } current)
        {
            return current;
        }

        var line = journal.Append(
            db,
            actor,
            JournalKinds.StandingApprovalSet,
            campaign,
            key: category,
            old: standing?.New?.DeepClone(),
            updated: record,
            reason: Text(request.Reason));
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return Dto(campaign, StandingApprovals.Read(line), released: 0, people: null, now.UtcDateTime);
    }

    /// <summary>
    /// Takes the campaign's standing approval for a category back, or its allowance, named by its operation. One that is
    /// not set writes nothing.
    /// </summary>
    public async Task<StandingApprovalDto> UnsetAsync(ApprovalUnsetStandingRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var actor = Person(request.Actor);

        var errors = new ValidationErrors();
        string? key;
        if (request.Operation is { } operation)
        {
            key = StandingApprovals.Searches;
            if (request.Category is not null)
            {
                errors.Add("operation", "not_allowed", "Name the category of reply or the allowance's operation, not both.");
            }
            else if (!string.Equals(operation.Trim(), StandingApprovals.Searches, StringComparison.Ordinal))
            {
                errors.Add("operation", "invalid", $"operation names an allowance, and {StandingApprovals.Searches} is the one operation an allowance covers.");
            }
        }
        else
        {
            key = ReplyService.Category(request.Category, errors);
            if (string.Equals(key, StandingApprovals.Searches, StringComparison.Ordinal))
            {
                errors.Add("category", "reserved", $"{StandingApprovals.Searches} names the allowance for the campaign's searches, never a category of reply: take it back with operation {StandingApprovals.Searches}.");
            }
        }

        ValidateReason(request.Reason, errors);
        errors.ThrowIfAny();

        var campaign = await Changeable(request.CampaignId, cancellationToken).ConfigureAwait(false);
        var newest = await StandingApprovals.Newest(db, campaign.Id, key!).FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        if (newest is { Kind: JournalKinds.StandingApprovalSet })
        {
            StandingApprovals.Retire(db, journal, actor, campaign, newest, Text(request.Reason));
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        return request.Operation is null ? Unset(campaign, key!, operations: null) : Unset(campaign, category: null, [StandingApprovals.Searches]);
    }

    /// <summary>
    /// The campaign's standing approvals whose newest line is a set, the allowance among them, in the order of what they
    /// are keyed by, with what each covers, how much it has let go and whether it is still in force. An archived campaign
    /// still answers: its record is history.
    /// </summary>
    public async Task<Page<StandingApprovalDto>> ListAsync(ApprovalListStandingRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var limit = Paging.ResolveLimit(request.Limit);
        var after = Paging.DecodeCursor(request.Cursor);
        var campaign = await CampaignService.LoadAsync(db, request.CampaignId, cancellationToken).ConfigureAwait(false);

        var newest = new Dictionary<string, JournalEntry>(StringComparer.Ordinal);
        foreach (var line in await StandingApprovals.Lines(db, campaign.Id).ToListAsync(cancellationToken).ConfigureAwait(false))
        {
            newest[line.Key ?? string.Empty] = line;
        }

        var sets = newest
            .Where(pair => pair.Value.Kind == JournalKinds.StandingApprovalSet)
            .Where(pair => after is null || string.CompareOrdinal(pair.Key, after) > 0)
            .OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .Take(limit + 1)
            .ToList();

        var answers = new List<(string Key, StandingApprovalDto Answer)>(sets.Count);
        foreach (var (key, set) in sets)
        {
            answers.Add((key, await DtoAsync(campaign, set, cancellationToken).ConfigureAwait(false)));
        }

        return Paging.ToPage(answers, limit, answer => answer.Key, answer => answer.Answer);
    }

    /// <summary>
    /// The allowance: the one operation, alone and with no category; an audience the campaign holds, by its number; a
    /// ceiling in people, and the optional cap per search and expiry. A count of releases is a reply's limit, not this one's.
    /// </summary>
    private async Task<StandingApprovalDto> AllowAsync(ApprovalSetStandingRequest request, ActorRef actor, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var errors = new ValidationErrors();
        if (request.Operations!.Any(operation => !string.Equals(operation?.Trim(), StandingApprovals.Searches, StringComparison.Ordinal)))
        {
            errors.Add("operations", "invalid", $"{StandingApprovals.Searches} is allowed alone: an allowance covers the campaign's searches and nothing beside them.");
        }

        if (request.Category is not null)
        {
            errors.Add("category", "not_allowed", "An allowance is named by its audience, never by a category of reply.");
        }

        if (request.MaxReleases is not null)
        {
            errors.Add("max_releases", "not_allowed", "An allowance counts people, by max_people, never releases.");
        }

        if (request.Audience is null)
        {
            errors.Add("audience", "required", "audience is required: the number of the campaign's audience whose searches the allowance covers.");
        }
        else if (request.Audience < 1)
        {
            errors.Add("audience", "invalid", "audience is the number of one of the campaign's audiences, from 1.");
        }

        StandingApprovals.ValidateLimits(request.MaxPeople, request.MaxPerSearch, request.ExpiresAt, now, errors);
        ValidateReason(request.Reason, errors);
        errors.ThrowIfAny();

        var campaign = await Changeable(request.CampaignId, cancellationToken).ConfigureAwait(false);
        var audience = await CampaignAudiences.ByNumber(db, campaign.Id, request.Audience!.Value).AsNoTracking().FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false)
            ?? throw new NotFoundException(
                AttemptErrors.AudienceUnknown,
                string.Create(CultureInfo.InvariantCulture, $"Campaign '{campaign.PublicId}' holds no audience under {request.Audience}: a campaign numbers its audiences from 1, in the order their first searches were claimed, so name one it holds."));

        var record = StandingApprovals.AllowanceLine(new AudienceRef(audience.PublicId, audience.Number), request.MaxPeople!.Value, request.MaxPerSearch, request.ExpiresAt);
        var newest = await StandingApprovals.Newest(db, campaign.Id, StandingApprovals.Searches).FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        var standing = newest is { Kind: JournalKinds.StandingApprovalSet } ? newest : null;
        if (await SameInForceAsync(campaign, standing, record, cancellationToken).ConfigureAwait(false) is { } current)
        {
            return current;
        }

        var line = StandingApprovals.Allow(db, journal, actor, campaign, standing, record, Text(request.Reason));
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return Dto(campaign, StandingApprovals.Read(line), released: 0, people: 0, now.UtcDateTime);
    }

    /// <summary>The standing approval in force where it is exactly the one asked for, so asking again writes nothing; else null.</summary>
    private async Task<StandingApprovalDto?> SameInForceAsync(Campaign campaign, JournalEntry? standing, JsonObject record, CancellationToken cancellationToken)
    {
        if (standing is null || !JsonNode.DeepEquals(standing.New, record))
        {
            return null;
        }

        var current = await DtoAsync(campaign, standing, cancellationToken).ConfigureAwait(false);
        return current.InForce ? current : null;
    }

    private async Task<StandingApprovalDto> DtoAsync(Campaign campaign, JournalEntry set, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        if (await StandingApprovals.UsedAsync(db, set, cancellationToken).ConfigureAwait(false) is { } allowance)
        {
            return Dto(campaign, allowance.Record, allowance.Released, allowance.People, now);
        }

        var released = await StandingApprovals.Released(db, set).SingleAsync(cancellationToken).ConfigureAwait(false);
        return Dto(campaign, StandingApprovals.Read(set), released, people: null, now);
    }

    /// <summary>A standing approval as every read answers it: <c>approval.list_standing</c>, and the allowance <c>campaign.audience</c> shows.</summary>
    internal static StandingApprovalDto Dto(Campaign campaign, StandingApprovals.Record record, int released, int? people, DateTime now) =>
        new(

            campaign.PublicId,
            record.Category,
            record.Operations,
            record.Audience,
            record.ExpiresAt,
            record.MaxReleases,
            record.MaxPeople,
            record.MaxPerSearch,
            released,
            people,
            record.InForce(released, people, now, campaign.Status),
            new ActorRef(record.Line.ActorType, record.Line.ActorId),
            new DateTimeOffset(DateTime.SpecifyKind(record.Line.Ts, DateTimeKind.Utc)),
            record.Line.PublicId);

    private static StandingApprovalDto Unset(Campaign campaign, string? category, IReadOnlyList<string>? operations) =>
        new(campaign.PublicId, category, operations, Audience: null, ExpiresAt: null, MaxReleases: null, MaxPeople: null, MaxPerSearch: null,
            Released: 0, People: null, InForce: false, SetBy: null, SetAt: null, LineId: null);

    /// <summary>The campaign, where its standing approvals may still change: an archived one's work is over.</summary>
    private async Task<Campaign> Changeable(string? campaignId, CancellationToken cancellationToken)
    {
        var campaign = await CampaignService.LoadAsync(db, campaignId, cancellationToken).ConfigureAwait(false);
        return campaign.Status == CampaignStatus.Archived ? throw DomainErrors.CampaignArchived(campaign.PublicId) : campaign;
    }

    /// <summary>
    /// The operations named, each once and in order; <c>conversation.reply</c> alone when none are named. A message to a
    /// person outside the thread is covered only where it is named.
    /// </summary>
    private static List<string>? Operations(IReadOnlyList<string>? named, ValidationErrors errors)
    {
        if (named is null)
        {
            return [.. StandingApprovals.DefaultOperations];
        }

        var operations = named.Select(operation => operation?.Trim() ?? string.Empty).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();
        if (operations.Count == 0 || operations.Any(operation => !StandingApprovals.Coverable.Contains(operation, StringComparer.Ordinal)))
        {
            errors.Add(
                "operations",
                "invalid",
                $"operations must name one or both of {string.Join(" and ", StandingApprovals.Coverable)}, or {StandingApprovals.Searches} alone for the allowance.");
            return null;
        }

        return operations;
    }

    /// <summary>A limit of the allowance's, given to a category of reply, which counts drafts and names no audience.</summary>
    private static void NotForAReply(int? value, string field, ValidationErrors errors)
    {
        if (value is not null)
        {
            errors.Add(field, "not_allowed", $"{field} belongs to the allowance for the campaign's searches (operations {StandingApprovals.Searches}), not to a category of reply.");
        }
    }

    /// <summary>
    /// Who is setting it. An absent actor is an anonymous person, which is right for creating work and wrong for a standing
    /// approval: it is a decision made in advance, and a recorded decision names the person who made it. As with every
    /// decision, nothing here can tell a person from a process holding that person's command line; what it guarantees is
    /// that no role and no run sets one.
    /// </summary>
    private static ActorRef Person(ActorRef? claimed)
    {
        var actor = Actors.Resolve(claimed);
        if (actor.Type != ActorType.Human)
        {
            throw DomainErrors.StandingApprovalNotHuman(actor.Type);
        }

        return string.IsNullOrWhiteSpace(actor.Id) ? throw DomainErrors.StandingApprovalActorRequired() : actor;
    }

    private static void ValidateReason(string? reason, ValidationErrors errors)
    {
        if (reason is not null && reason.Trim().Length > CampaignService.MaxReasonLength)
        {
            errors.Add("reason", "too_long", string.Create(CultureInfo.InvariantCulture, $"reason must be at most {CampaignService.MaxReasonLength} characters."));
        }
    }

    private static string? Text(string? reason) => string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
}
