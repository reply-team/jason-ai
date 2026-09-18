using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Jason.Contracts.Api;
using Jason.Contracts.Ids;
using Jason.Contracts.Json;
using Jason.Runtime.Domain;
using Jason.Runtime.Journal;
using Jason.Runtime.Persistence;
using Jason.Runtime.WorkItems;
using Microsoft.EntityFrameworkCore;

namespace Jason.Runtime.Campaigns;

/// <summary>
/// The campaign lifecycle and the campaign context. Every change a role could later have to explain — a rename,
/// a transition, a context edit — is written to the chronicle in the same transaction as the change itself, so
/// there is no state the journal cannot account for.
/// </summary>
public sealed class CampaignService(JasonDbContext db, JournalWriter journal, TimeProvider clock, WorkItemCanceller canceller)
{
    public const int MaxNameLength = 200;

    public const int MaxReasonLength = 2000;

    public async Task<CampaignDto> CreateAsync(CampaignCreateRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var actor = Actors.Resolve(request.Actor);

        var errors = new ValidationErrors();
        ValidateName(request.Name, errors);
        ValidateReason(request.Reason, errors);
        errors.ThrowIfAny();

        var name = request.Name!.Trim();
        var reason = NormalizeReason(request.Reason);
        var context = request.Context?.DeepClone().AsObject() ?? new JsonObject();
        ContextRules.EnsureWithinLimits(context);

        var now = clock.GetUtcNow().UtcDateTime;
        var campaign = new Campaign
        {
            PublicId = PublicId.New("cmp"),
            Name = name,
            Status = CampaignStatus.Draft,
            Context = context,
            CreatedAt = now,
            UpdatedAt = now,
        };

        db.Campaigns.Add(campaign);
        journal.Append(db, actor, JournalKinds.CampaignCreated, campaign, key: "name", updated: JsonValue.Create(name), reason: reason);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return CampaignMapper.ToDto(campaign);
    }

    public async Task<CampaignDto> GetAsync(CampaignGetRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var id = RequireId(request.CampaignId);
        var campaign = await db.Campaigns.AsNoTracking().Include(c => c.ExternalIds).FirstOrDefaultAsync(c => c.PublicId == id, cancellationToken).ConfigureAwait(false)
            ?? throw DomainErrors.CampaignNotFound(id);
        return CampaignMapper.ToDto(campaign);
    }

    public async Task<Page<CampaignSummaryDto>> ListAsync(CampaignListRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var limit = Paging.ResolveLimit(request.Limit);
        var after = Paging.DecodeCursor(request.Cursor);

        var query = db.Campaigns.AsNoTracking();

        // Archived campaigns are the only ones a listing hides: a finished campaign should not crowd the work
        // in front of a role, but asking for them by status still shows them.
        query = request.Status is { } status
            ? query.Where(c => c.Status == status)
            : query.Where(c => c.Status != CampaignStatus.Archived);

        if (after is not null)
        {
            query = query.Where(c => string.Compare(c.PublicId, after) > 0);
        }

        var fetched = await query.OrderBy(c => c.PublicId).Take(limit + 1).ToListAsync(cancellationToken).ConfigureAwait(false);
        return Paging.ToPage(fetched, limit, c => c.PublicId, CampaignMapper.ToSummary);
    }

    public async Task<CampaignDto> UpdateAsync(CampaignUpdateRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var actor = Actors.Resolve(request.Actor);

        var errors = new ValidationErrors();
        ValidateReason(request.Reason, errors);
        if (request.Name.IsSet)
        {
            ValidateName(request.Name.Value, errors);
        }

        errors.ThrowIfAny();

        var campaign = await LoadAsync(db, request.CampaignId, cancellationToken).ConfigureAwait(false);
        if (campaign.Status == CampaignStatus.Archived)
        {
            throw DomainErrors.CampaignArchived(campaign.PublicId);
        }

        // The profile is the one patched field that cannot be checked before the campaign is loaded: it is a
        // name, and whether anything answers it is a question for the database.
        string? profile = null;
        if (request.ExecutionProfile.IsSet)
        {
            profile = await ExecutionProfilePolicy
                .ResolveAsync(db, "execution_profile", request.ExecutionProfile.Value, errors, cancellationToken)
                .ConfigureAwait(false);
            errors.ThrowIfAny();
        }

        // Only the fields that really moved, so asking a campaign for what it already says writes nothing: a
        // retry after a lost response is never a second event in the chronicle.
        var changes = new List<FieldChange>();
        var name = request.Name.IsSet ? request.Name.Value!.Trim() : campaign.Name;
        if (!string.Equals(campaign.Name, name, StringComparison.Ordinal))
        {
            changes.Add(new FieldChange("name", JsonValue.Create(campaign.Name), JsonValue.Create(name)));
            campaign.Name = name;
        }

        if (request.ExecutionProfile.IsSet && !string.Equals(campaign.ExecutionProfile, profile, StringComparison.Ordinal))
        {
            changes.Add(new FieldChange("execution_profile", JsonValue.Create(campaign.ExecutionProfile), JsonValue.Create(profile)));
            campaign.ExecutionProfile = profile;
        }

        if (changes.Count == 0)
        {
            return CampaignMapper.ToDto(campaign);
        }

        campaign.UpdatedAt = clock.GetUtcNow().UtcDateTime;
        var updateReason = NormalizeReason(request.Reason);
        foreach (var change in changes)
        {
            journal.Append(db, actor, JournalKinds.CampaignUpdated, campaign, key: change.Key, old: change.Old, updated: change.New, reason: updateReason);
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return CampaignMapper.ToDto(campaign);
    }

    public Task<CampaignDto> StartAsync(CampaignTransitionRequest request, CancellationToken cancellationToken) =>
        TransitionAsync(request, CampaignStatus.Active, JournalKinds.CampaignStarted, cancellationToken);

    public Task<CampaignDto> PauseAsync(CampaignTransitionRequest request, CancellationToken cancellationToken) =>
        TransitionAsync(request, CampaignStatus.Paused, JournalKinds.CampaignPaused, cancellationToken);

    public Task<CampaignDto> ArchiveAsync(CampaignTransitionRequest request, CancellationToken cancellationToken) =>
        TransitionAsync(request, CampaignStatus.Archived, JournalKinds.CampaignArchived, cancellationToken);

    public async Task<CampaignDto> UpdateContextAsync(CampaignUpdateContextRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var actor = Actors.Resolve(request.Actor);

        var errors = new ValidationErrors();
        ValidateReason(request.Reason, errors);
        if (request.Set is not null && request.Set.Any(pair => string.IsNullOrWhiteSpace(pair.Key)))
        {
            errors.Add("set", "invalid", "context keys must not be blank.");
        }

        if (request.Unset is not null)
        {
            for (var index = 0; index < request.Unset.Count; index++)
            {
                if (string.IsNullOrWhiteSpace(request.Unset[index]))
                {
                    errors.Add(string.Create(CultureInfo.InvariantCulture, $"unset[{index}]"), "invalid", "context keys must not be blank.");
                }
            }
        }

        errors.ThrowIfAny();

        var campaign = await LoadAsync(db, request.CampaignId, cancellationToken).ConfigureAwait(false);
        if (campaign.Status == CampaignStatus.Archived)
        {
            throw DomainErrors.CampaignArchived(campaign.PublicId);
        }

        var context = campaign.Context.DeepClone().AsObject();
        var changes = new List<FieldChange>();

        if (request.Set is not null)
        {
            foreach (var (key, value) in request.Set)
            {
                var present = context.TryGetPropertyValue(key, out var current);
                if (present && JsonNode.DeepEquals(current, value))
                {
                    continue;
                }

                // Every node is cloned into its own tree: a JSON node belongs to exactly one parent, and the
                // journal entry must keep the value even after the context moves on.
                changes.Add(new FieldChange(key, current?.DeepClone(), value?.DeepClone()));
                context[key] = value?.DeepClone();
            }
        }

        if (request.Unset is not null)
        {
            foreach (var key in request.Unset)
            {
                if (!context.TryGetPropertyValue(key, out var current))
                {
                    continue;
                }

                // Setting a key to JSON null and unsetting it both journal a null new value; what tells them
                // apart is the context itself, where one keeps the key and the other no longer has it.
                changes.Add(new FieldChange(key, current?.DeepClone(), null));
                context.Remove(key);
            }
        }

        ContextRules.EnsureWithinLimits(context);
        if (changes.Count == 0)
        {
            return CampaignMapper.ToDto(campaign);
        }

        campaign.Context = context;
        campaign.UpdatedAt = clock.GetUtcNow().UtcDateTime;
        var reason = NormalizeReason(request.Reason);
        foreach (var change in changes)
        {
            journal.Append(db, actor, JournalKinds.ContextUpdated, campaign, key: change.Key, old: change.Old, updated: change.New, reason: reason);
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return CampaignMapper.ToDto(campaign);
    }

    /// <summary>Shared by the membership and journal services: 404 when unknown.</summary>
    public static async Task<Campaign> LoadAsync(JasonDbContext db, string? publicId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(db);
        var id = RequireId(publicId);
        return await db.Campaigns.Include(c => c.ExternalIds).FirstOrDefaultAsync(c => c.PublicId == id, cancellationToken).ConfigureAwait(false)
            ?? throw DomainErrors.CampaignNotFound(id);
    }

    private async Task<CampaignDto> TransitionAsync(CampaignTransitionRequest request, CampaignStatus target, string kind, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var actor = Actors.Resolve(request.Actor);

        var errors = new ValidationErrors();
        ValidateReason(request.Reason, errors);
        errors.ThrowIfAny();

        var campaign = await LoadAsync(db, request.CampaignId, cancellationToken).ConfigureAwait(false);

        // Asking for the state the campaign is already in is how a retry after a lost response looks: answer
        // with the campaign and write nothing, so repeating a call is never a second event in the chronicle.
        if (campaign.Status == target)
        {
            return CampaignMapper.ToDto(campaign);
        }

        if (campaign.Status == CampaignStatus.Archived)
        {
            throw DomainErrors.CampaignArchived(campaign.PublicId);
        }

        if (!IsLegal(campaign.Status, target))
        {
            throw DomainErrors.InvalidTransition(campaign.Status, target);
        }

        var previous = campaign.Status;
        campaign.Status = target;
        campaign.UpdatedAt = clock.GetUtcNow().UtcDateTime;
        if (target == CampaignStatus.Archived)
        {
            campaign.ArchivedAt = campaign.UpdatedAt;
        }

        journal.Append(
            db,
            actor,
            kind,
            campaign,
            key: "status",
            old: JsonSerializer.SerializeToNode(previous, JasonJson.Options),
            updated: JsonSerializer.SerializeToNode(target, JasonJson.Options),
            reason: NormalizeReason(request.Reason));

        // An archived campaign can never run again, so work still queued for it is dead weight that would
        // otherwise sit in the inbox for good.
        if (target == CampaignStatus.Archived)
        {
            await canceller.CancelOpenAsync(db, campaign.Id, null, actor, "campaign archived", cancellationToken).ConfigureAwait(false);
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return CampaignMapper.ToDto(campaign);
    }

    /// <summary>draft → active, paused → active (a resume), active → paused, and anything at all → archived.</summary>
    private static bool IsLegal(CampaignStatus from, CampaignStatus to) => (from, to) switch
    {
        (CampaignStatus.Draft, CampaignStatus.Active) => true,
        (CampaignStatus.Paused, CampaignStatus.Active) => true,
        (CampaignStatus.Active, CampaignStatus.Paused) => true,
        (_, CampaignStatus.Archived) => true,
        _ => false,
    };

    private static string RequireId(string? publicId) =>
        string.IsNullOrWhiteSpace(publicId) ? throw DomainErrors.Required("campaign_id") : publicId.Trim();

    private static void ValidateName(string? name, ValidationErrors errors)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            errors.Add("name", "required", "name is required.");
            return;
        }

        if (name.Trim().Length > MaxNameLength)
        {
            errors.Add("name", "too_long", string.Create(CultureInfo.InvariantCulture, $"name must be at most {MaxNameLength} characters."));
        }
    }

    private static void ValidateReason(string? reason, ValidationErrors errors)
    {
        if (reason is not null && reason.Trim().Length > MaxReasonLength)
        {
            errors.Add("reason", "too_long", string.Create(CultureInfo.InvariantCulture, $"reason must be at most {MaxReasonLength} characters."));
        }
    }

    private static string? NormalizeReason(string? reason) => string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();

    /// <summary>One field that moved, and what it moved from: a campaign attribute, or a key of its context.</summary>
    private readonly record struct FieldChange(string Key, JsonNode? Old, JsonNode? New);
}
