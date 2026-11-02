using System.Globalization;
using System.Text.Json.Nodes;
using Jason.Contracts.Api;
using Jason.Runtime.Campaigns;
using Jason.Runtime.Domain;
using Jason.Runtime.Journal;
using Jason.Runtime.Persistence;
using Jason.Runtime.Replies;
using Microsoft.EntityFrameworkCore;

namespace Jason.Runtime.Approvals;

/// <summary>
/// The three verbs of a campaign's standing approvals: set one for a category, take it back, and read what stands. Only a
/// person sets or unsets one, and the request names them; any caller reads them. Nothing inside the runtime calls the two
/// that change them: the claim reads the record, and the record is the chronicle's lines.
/// </summary>
public sealed class StandingApprovalService(JasonDbContext db, JournalWriter journal, TimeProvider clock)
{
    /// <summary>
    /// Sets the campaign's standing approval for a category whole. A request equal to the one in force writes nothing and
    /// answers with it, so a set sent again after its answer was lost is never a second line; any other set is a new line
    /// that replaces the record, and the count its ceiling reads starts again from it.
    /// </summary>
    public async Task<StandingApprovalDto> SetAsync(ApprovalSetStandingRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var actor = Person(request.Actor);
        var now = clock.GetUtcNow();

        var errors = new ValidationErrors();
        var category = ReplyService.Category(request.Category, errors);
        var operations = Operations(request.Operations, errors);
        if (request.ExpiresAt is { } expires && expires <= now)
        {
            errors.Add("expires_at", "in_the_past", "expires_at must be a moment later than now.");
        }

        if (request.MaxReleases is < 1 or > StandingApprovals.MaxReleases)
        {
            errors.Add("max_releases", "invalid", string.Create(CultureInfo.InvariantCulture, $"max_releases must be from 1 to {StandingApprovals.MaxReleases}."));
        }

        ValidateReason(request.Reason, errors);
        errors.ThrowIfAny();

        var campaign = await Changeable(request.CampaignId, cancellationToken).ConfigureAwait(false);
        var record = StandingApprovals.Line(category!, operations!, request.ExpiresAt, request.MaxReleases);
        var newest = await StandingApprovals.Newest(db, campaign.Id, category!).FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        var standing = newest is { Kind: JournalKinds.StandingApprovalSet } ? newest : null;
        if (standing is not null && JsonNode.DeepEquals(standing.New, record))
        {
            var current = await DtoAsync(campaign, standing, cancellationToken).ConfigureAwait(false);
            if (current.InForce)
            {
                return current;
            }
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
        return Dto(campaign, StandingApprovals.Read(line), released: 0, now.UtcDateTime);
    }

    /// <summary>Takes the campaign's standing approval for a category back. A category that is not set writes nothing.</summary>
    public async Task<StandingApprovalDto> UnsetAsync(ApprovalUnsetStandingRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var actor = Person(request.Actor);

        var errors = new ValidationErrors();
        var category = ReplyService.Category(request.Category, errors);
        ValidateReason(request.Reason, errors);
        errors.ThrowIfAny();

        var campaign = await Changeable(request.CampaignId, cancellationToken).ConfigureAwait(false);
        var newest = await StandingApprovals.Newest(db, campaign.Id, category!).FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        if (newest is { Kind: JournalKinds.StandingApprovalSet })
        {
            journal.Append(
                db,
                actor,
                JournalKinds.StandingApprovalUnset,
                campaign,
                key: category,
                old: newest.New?.DeepClone(),
                reason: Text(request.Reason));
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        return Unset(campaign, category!);
    }

    /// <summary>
    /// The campaign's categories whose newest line is a set, in category order, with what each covers, how many releases
    /// it has made and whether it is still in force. An archived campaign still answers: its record is history.
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
            .Select(pair => pair.Value)
            .ToList();

        var answers = new List<StandingApprovalDto>(sets.Count);
        foreach (var set in sets)
        {
            answers.Add(await DtoAsync(campaign, set, cancellationToken).ConfigureAwait(false));
        }

        return Paging.ToPage(answers, limit, answer => answer.Category, answer => answer);
    }

    private async Task<StandingApprovalDto> DtoAsync(Campaign campaign, JournalEntry set, CancellationToken cancellationToken)
    {
        var released = await StandingApprovals.Released(db, set).SingleAsync(cancellationToken).ConfigureAwait(false);
        return Dto(campaign, StandingApprovals.Read(set), released, clock.GetUtcNow().UtcDateTime);
    }

    private static StandingApprovalDto Dto(Campaign campaign, StandingApprovals.Record record, int released, DateTime now)
    {
        var expired = record.ExpiresAt is { } expires && expires.UtcDateTime <= now;
        var spent = record.MaxReleases is { } ceiling && released >= ceiling;
        return new StandingApprovalDto(
            campaign.PublicId,
            record.Category,
            record.Operations,
            record.ExpiresAt,
            record.MaxReleases,
            released,
            InForce: !expired && !spent,
            new ActorRef(record.Line.ActorType, record.Line.ActorId),
            new DateTimeOffset(DateTime.SpecifyKind(record.Line.Ts, DateTimeKind.Utc)),
            record.Line.PublicId);
    }

    private static StandingApprovalDto Unset(Campaign campaign, string category) =>
        new(campaign.PublicId, category, null, null, null, Released: 0, InForce: false, SetBy: null, SetAt: null, LineId: null);

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
            errors.Add("operations", "invalid", $"operations must name one or both of {string.Join(" and ", StandingApprovals.Coverable)}.");
            return null;
        }

        return operations;
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
