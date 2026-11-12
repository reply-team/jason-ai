using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Jason.Contracts.Api;
using Jason.Contracts.Ids;
using Jason.Contracts.Json;
using Jason.Contracts.Operations;
using Jason.Runtime.Configuration;
using Jason.Runtime.Contacts;
using Jason.Runtime.Decisions;
using Jason.Runtime.Dispatch;
using Jason.Runtime.Domain;
using Jason.Runtime.Harness;
using Jason.Runtime.Journal;
using Jason.Runtime.Persistence;
using Jason.Runtime.Projects;
using Jason.Runtime.WorkItems;
using Microsoft.EntityFrameworkCore;

namespace Jason.Runtime.Campaigns;

/// <summary>
/// The campaign lifecycle and the campaign context. Every change a role could later have to explain — a rename,
/// a transition, a context edit — is written to the chronicle in the same transaction as the change itself, so
/// there is no state the journal cannot account for. A campaign made here gets its folder in its project's folder in
/// the same request, and a campaign moved to another project takes its folder with it, or is not moved at all.
/// </summary>
public sealed class CampaignService(
    JasonDbContext db,
    JournalWriter journal,
    TimeProvider clock,
    WorkItemCanceller canceller,
    HarnessFolders folders,
    PatternTiming? timing = null)
{
    private readonly PatternTiming _timing = timing ?? PatternTiming.Default;

    public const int MaxNameLength = 200;

    public const int MaxReasonLength = 2000;

    public async Task<CampaignDto> CreateAsync(CampaignCreateRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var actor = Actors.Resolve(request.Actor);

        var errors = new ValidationErrors();
        ValidateName(request.Name, errors);
        ValidateReason(request.Reason, errors);
        var key = CreationKey.Read(request.IdempotencyKey, errors);
        if (request.ProjectId is not null && string.IsNullOrWhiteSpace(request.ProjectId))
        {
            errors.Add("project_id", "invalid", "project_id must name a project; leave it out for the default project.");
        }

        ReservedContextKeys.Validate(request.Context, _timing, errors);
        errors.ThrowIfAny();

        var name = request.Name!.Trim();
        var reason = NormalizeReason(request.Reason);
        var context = request.Context?.DeepClone().AsObject() ?? new JsonObject();
        ContextRules.EnsureWithinLimits(context);

        // A key the caller has used before answers here, before anything is written: with the campaign it made, if
        // this is the request that made it, and with a refusal if it is not.
        var digest = key is null ? null : CreationKey.Digest(request);
        if (key is not null && await MadeUnderAsync(key, digest!, cancellationToken).ConfigureAwait(false) is { } made)
        {
            return made;
        }

        var project = request.ProjectId is null
            ? await ProjectService.DefaultAsync(db, cancellationToken).ConfigureAwait(false)
            : await ProjectService.LoadAsync(db, request.ProjectId.Trim(), cancellationToken).ConfigureAwait(false);

        // The project's first need, where it has no folder recorded — the default project of an installation that had
        // campaigns before folders, say — is met in a step of its own before the campaign's: its folder then stands, and is
        // recorded, whatever happens to the campaign's. A recorded one the person moved is refused, never made again beside it.
        var projectFolder = await ProjectService.FirstNeedAsync(db, project, folders).ConfigureAwait(false);
        var now = clock.GetUtcNow().UtcDateTime;
        var campaign = new Campaign
        {
            PublicId = PublicId.New("cmp"),
            Name = name,
            ProjectId = project.Id,
            Project = project,
            Status = CampaignStatus.Draft,
            Context = context,
            CreatedAt = now,
            UpdatedAt = now,
            IdempotencyKey = key,
            IdempotencyDigest = digest,
        };

        // The line says a campaign was made and what it is called, as it always has; the key stays on the row.
        db.Campaigns.Add(campaign);
        journal.Append(db, actor, JournalKinds.CampaignCreated, campaign, key: "name", updated: JsonValue.Create(name), reason: reason);

        // The row is written first and held uncommitted while the folders are made, as a project's is: the key decides a
        // race before any folder exists, and a folder that cannot be made takes the row and its line back with it.
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateException) when (key is not null)
        {
            // Somebody else's create under the same key landed between the look above and this insert: the database
            // is what decides one key names one campaign. Theirs is answered as a repeat would be; a refusal over
            // anything else was never a race, and is what it was.
            db.ChangeTracker.Clear();
            if (await MadeUnderAsync(key, digest!, cancellationToken).ConfigureAwait(false) is { } won)
            {
                return won;
            }

            throw;
        }

        campaign.FolderPath = await folders
            .MakeCampaignFolderAsync(projectFolder, campaign.PublicId, name, id => HoldsAsync(id, cancellationToken))
            .ConfigureAwait(false);

        // Once the folders stand, the create is finished whether or not its caller is still there: a caller that went
        // away now would roll back a row its folder's marker already names, and that folder would refuse the retry the
        // key exists for. Cancellation lands only before anything is on the disk.
        await db.SaveChangesAsync(CancellationToken.None).ConfigureAwait(false);
        await transaction.CommitAsync(CancellationToken.None).ConfigureAwait(false);
        return CampaignMapper.ToDto(campaign);
    }

    /// <summary>
    /// The campaign a key already names, as it is now, when <paramref name="digest"/> is the request that made it; a
    /// refusal naming it when a different request did; null when the key names nothing yet.
    /// </summary>
    private async Task<CampaignDto?> MadeUnderAsync(string key, string digest, CancellationToken cancellationToken)
    {
        var made = await db.Campaigns
            .Include(c => c.ExternalIds)
            .Include(c => c.Project)
            .FirstOrDefaultAsync(c => c.IdempotencyKey == key, cancellationToken)
            .ConfigureAwait(false);

        return made is null ? null
            : string.Equals(made.IdempotencyDigest, digest, StringComparison.Ordinal) ? CampaignMapper.ToDto(made)
            : throw DomainErrors.IdempotencyKeyReused(made.PublicId);
    }

    public async Task<CampaignDto> GetAsync(CampaignGetRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var id = RequireId(request.CampaignId);
        var campaign = await db.Campaigns.AsNoTracking()
            .Include(c => c.ExternalIds)
            .Include(c => c.Project)
            .FirstOrDefaultAsync(c => c.PublicId == id, cancellationToken).ConfigureAwait(false)
            ?? throw DomainErrors.CampaignNotFound(id);
        return CampaignMapper.ToDto(campaign);
    }

    public async Task<Page<CampaignSummaryDto>> ListAsync(CampaignListRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var text = string.IsNullOrWhiteSpace(request.Query) ? null : request.Query.Trim();
        var errors = new ValidationErrors();
        if (text is { Length: > ContactSearch.MaxLength })
        {
            errors.Add("query", "too_long", string.Create(CultureInfo.InvariantCulture, $"query must be at most {ContactSearch.MaxLength} characters."));
        }

        if (request.ProjectId is not null && string.IsNullOrWhiteSpace(request.ProjectId))
        {
            errors.Add("project_id", "invalid", "project_id must name a project; leave it out for the campaigns of every project.");
        }

        errors.ThrowIfAny();

        var limit = Paging.ResolveLimit(request.Limit);
        var after = Paging.DecodeCursor(request.Cursor);

        // Each campaign is answered with its project, read beside it by the project's key rather than asked for again.
        var query = db.Campaigns.AsNoTracking().Include(c => c.Project).AsQueryable();
        if (request.ProjectId is not null)
        {
            var project = await db.Projects.AsNoTracking()
                .Where(p => p.PublicId == request.ProjectId.Trim())
                .Select(p => (int?)p.Id)
                .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false)
                ?? throw DomainErrors.ProjectNotFound(request.ProjectId.Trim());
            query = query.Where(c => c.ProjectId == project);
        }

        if (text is not null)
        {
            var pattern = TextFold.Containing(text);
            query = query.Where(c => EF.Functions.Like(TextFold.Fold(c.Name), pattern, TextFold.Escape));
        }

        // Archived campaigns are the only ones a listing hides: a finished campaign should not crowd the work
        // in front of a role, but asking for them by status, or for every campaign, still shows them.
        if (request.Status is { } status)
        {
            query = query.Where(c => c.Status == status);
        }
        else if (request.IncludeArchived != true)
        {
            query = query.Where(c => c.Status != CampaignStatus.Archived);
        }

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

        // The campaign may set its own pace within the bounds the installation is held to. A campaign cannot
        // buy itself a tighter loop than the runtime allows: every review is a launch on somebody's plan.
        if (request.ReviewSeconds is { IsSet: true, Value: { } seconds }
            && (seconds < ManagerOptions.MinimumReviewSeconds || seconds > ManagerOptions.MaximumReviewSeconds))
        {
            errors.Add(
                "review_seconds",
                "out_of_range",
                $"review_seconds must be between {ManagerOptions.MinimumReviewSeconds} and {ManagerOptions.MaximumReviewSeconds}; got {seconds}.");
        }

        if (request.AudienceThreshold is { IsSet: true, Value: { } threshold }
            && (threshold < ReserveCounter.MinimumThreshold || threshold > ReserveCounter.MaximumThreshold))
        {
            errors.Add(
                "audience_threshold",
                "out_of_range",
                $"audience_threshold must be between {ReserveCounter.MinimumThreshold} and {ReserveCounter.MaximumThreshold}; got {threshold}.");
        }

        // A campaign is always in a project, so the patch can move it but never take it out of every one.
        if (request.ProjectId is { IsSet: true, Value: var target } && string.IsNullOrWhiteSpace(target))
        {
            errors.Add("project_id", "required", "a campaign is always in a project; project_id names the one to move it to.");
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

        var project = request.ProjectId.IsSet
            ? await ProjectService.LoadAsync(db, request.ProjectId.Value!.Trim(), cancellationToken).ConfigureAwait(false)
            : campaign.Project!;

        // A campaign with a folder that changes project takes the folder along. First what can be decided without writing
        // anything — its folder is where it was recorded, the new project's recorded folder holds, the name is free there,
        // the two are on one volume — then the new project's first need, in a step of its own, and only then is anything
        // of the campaign's written. A campaign with no folder recorded moves in the database alone.
        (string Folder, string ProjectFolder, string Leaf)? move = null;
        if (project.Id != campaign.ProjectId && campaign.FolderPath is { } from)
        {
            var leaf = await folders
                .PlanCampaignMoveAsync(from, campaign.PublicId, ProjectService.PlannedFolder(project, folders), id => HoldsAsync(id, cancellationToken))
                .ConfigureAwait(false);
            move = (from, await ProjectService.FirstNeedAsync(db, project, folders).ConfigureAwait(false), leaf);
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

        if (request.ReviewSeconds.IsSet && campaign.ManagerReviewSeconds != request.ReviewSeconds.Value)
        {
            changes.Add(new FieldChange(
                "review_seconds",
                JsonValue.Create(campaign.ManagerReviewSeconds),
                JsonValue.Create(request.ReviewSeconds.Value)));
            campaign.ManagerReviewSeconds = request.ReviewSeconds.Value;
        }

        if (request.AudienceThreshold.IsSet && campaign.AudienceThreshold != request.AudienceThreshold.Value)
        {
            changes.Add(new FieldChange(
                "audience_threshold",
                JsonValue.Create(campaign.AudienceThreshold),
                JsonValue.Create(request.AudienceThreshold.Value)));
            campaign.AudienceThreshold = request.AudienceThreshold.Value;
        }

        // A move says where the campaign was and where it went by the projects' ids: their names change, their ids do not.
        var moved = project.Id != campaign.ProjectId;
        if (moved)
        {
            changes.Add(new FieldChange("project_id", JsonValue.Create(campaign.Project!.PublicId), JsonValue.Create(project.PublicId)));
            campaign.ProjectId = project.Id;
            campaign.Project = project;
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

        if (move is not { } taking)
        {
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return CampaignMapper.ToDto(campaign);
        }

        // The update is written and held uncommitted, the folder moves by one rename into the new project's, and only then
        // does the update commit. A rename the disk refuses takes the whole update back, and its refusal is the final
        // word; once the folder has moved, the update is finished whether or not its caller is still there, and a save
        // that fails puts the folder back first.
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        var to = folders.MoveCampaignFolder(taking.Folder, taking.ProjectFolder, taking.Leaf);
        try
        {
            campaign.FolderPath = to;
            await db.SaveChangesAsync(CancellationToken.None).ConfigureAwait(false);
            await transaction.CommitAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch
        {
            HarnessFolders.MoveBack(to, taking.Folder);
            throw;
        }

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

        ReservedContextKeys.Validate(request.Set, request.Unset, _timing, errors);
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

    /// <summary>
    /// Whether a campaign of that id is one this runtime holds: a campaign's leaf taken by another of them is numbered,
    /// since campaign names are not unique, and one marked for an id nobody holds is refused as taken.
    /// </summary>
    private Task<bool> HoldsAsync(string publicId, CancellationToken cancellationToken) =>
        db.Campaigns.AsNoTracking().AnyAsync(c => c.PublicId == publicId, cancellationToken);

    /// <summary>Shared by the membership and journal services: 404 when unknown.</summary>
    public static async Task<Campaign> LoadAsync(JasonDbContext db, string? publicId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(db);
        var id = RequireId(publicId);
        return await db.Campaigns.Include(c => c.ExternalIds).Include(c => c.Project).FirstOrDefaultAsync(c => c.PublicId == id, cancellationToken).ConfigureAwait(false)
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

        // Going live is what the review cadence is measured from — the first time and every time. A campaign
        // paused for a week and started again is reviewed a full interval after it wakes, rather than the
        // instant it does: the week it spent stopped is not a week nobody looked at it.
        if (target == CampaignStatus.Active)
        {
            campaign.ManagerReviewAnchor = campaign.UpdatedAt;

            // The inbound read starts from the first time only: what a person's conversations held before the campaign
            // went live is history, while a reply that came in during a pause is still one the manager has not seen.
            campaign.InboundWatermark ??= campaign.UpdatedAt;

            // A provider's events are said from the same first time, for the same reason.
            campaign.EventsFrom ??= campaign.UpdatedAt;
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
        // otherwise sit in the inbox for good — and so is a question somebody is still being asked about it.
        // Both join this change set, so the campaign and everything retired with it commit together.
        if (target == CampaignStatus.Archived)
        {
            await canceller.CancelOpenAsync(db, campaign.Id, null, actor, "campaign archived", cancellationToken).ConfigureAwait(false);
            await DecisionGate
                .CancelPendingAsync(db, journal, campaign, actor, "campaign archived", cancellationToken)
                .ConfigureAwait(false);
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        canceller.StopCancelled();
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

        // A name is printed to people wherever the campaign is listed, and a control character inside one would act on
        // their terminal rather than be read.
        if (name.Trim().Any(char.IsControl))
        {
            errors.Add("name", "invalid", "name must hold no control character.");
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
