using System.Globalization;
using System.Text.Json.Nodes;
using Jason.Contracts.Api;
using Jason.Contracts.Discovery;
using Jason.Contracts.Harness;
using Jason.Runtime.Campaigns;
using Jason.Runtime.Domain;
using Jason.Runtime.Harness;
using Jason.Runtime.Journal;
using Jason.Runtime.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Jason.Runtime.Projects;

/// <summary>
/// <c>project.link</c>: records where a project's folder is, and its campaigns', when the person moved, renamed or chose
/// one (<c>docs/projects.md</c>, "Linking a folder"). A folder is known by its marker, never its name, and the only
/// fact the filesystem changes is a recorded path, through this explicit act. The folder looked through is the project's,
/// then each folder directly in its <c>campaigns/</c>: two fixed levels, never a search of the disk.
/// </summary>
/// <remarks>
/// <para>
/// The database decides which project a campaign is in, never the folder its marker is found in: a campaign of another
/// project found here is reported and left as it is. A campaign of this project found nowhere below the folder keeps the
/// path it had, and is reported too. What a link writes on the disk is structure only, as everywhere in the harness: a
/// marker, the two files where absent, and the folders the policy makes for campaigns that have none.
/// </para>
/// <para>
/// The checks that refuse come before anything is written. The disk's acts are then made inside the transaction, each
/// level's marker first, and every path and the one line in the chronicle are saved together: a disk refusal takes the
/// database back, and what it marked already names the right project or campaign, so the next link uses it again.
/// </para>
/// </remarks>
public sealed class ProjectLinker(JasonDbContext db, JournalWriter journal, HarnessFolders folders, JasonPaths paths)
{
    public async Task<ProjectLinkResult> LinkAsync(ProjectLinkRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var actor = Actors.Resolve(request.Actor);

        var errors = new ValidationErrors();
        if (request.Reason is not null && request.Reason.Trim().Length > CampaignService.MaxReasonLength)
        {
            errors.Add("reason", "too_long", string.Create(CultureInfo.InvariantCulture, $"reason must be at most {CampaignService.MaxReasonLength} characters."));
        }

        var id = string.IsNullOrWhiteSpace(request.ProjectId) ? null : request.ProjectId.Trim();
        var path = request.Path is null ? null : ReadPath(request.Path, errors);
        if (id is null && request.Path is null)
        {
            errors.Add("project_id", "required", "name the project with project_id, its folder with path, or both.");
        }

        errors.ThrowIfAny();

        // The write lock is held from here: two links, or a link and a create, never decide a folder at once.
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        var project = id is null ? null : await ProjectService.LoadAsync(db, id, cancellationToken).ConfigureAwait(false);
        var (linked, folder, outcome, adopt) = path is null
            ? await ByPolicyAsync(project!, cancellationToken).ConfigureAwait(false)
            : await ByPathAsync(project, path, cancellationToken).ConfigureAwait(false);

        var campaigns = await db.Campaigns
            .Where(campaign => campaign.ProjectId == linked.Id)
            .OrderBy(campaign => campaign.PublicId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var found = HarnessFolders.MarkedCampaignFolders(folder);
        var named = found.Select(entry => entry.Marker).Where(marker => marker.Kind == HarnessMarker.Campaign && marker.Id is not null).Select(marker => marker.Id!).Distinct().ToList();
        var elsewhere = await db.Campaigns
            .AsNoTracking()
            .Where(campaign => named.Contains(campaign.PublicId) && campaign.ProjectId != linked.Id)
            .Select(campaign => new { campaign.PublicId, Project = campaign.Project!.PublicId })
            .ToDictionaryAsync(campaign => campaign.PublicId, campaign => campaign.Project, StringComparer.Ordinal, cancellationToken)
            .ConfigureAwait(false);

        // From here the disk is written, so the request is finished whether or not its caller is still there.
        if (adopt)
        {
            HarnessFolders.AdoptProjectFolder(folder, linked.PublicId);
        }
        else if (outcome == LinkFolderOutcome.Made)
        {
            folder = folders.MakeProjectFolder(linked.PublicId, linked.Slug);
        }

        var reported = new List<LinkReport>();
        var candidates = campaigns.ToDictionary(campaign => campaign.PublicId, _ => new List<string>(), StringComparer.Ordinal);
        foreach (var (at, marker) in found)
        {
            if (marker is { Kind: HarnessMarker.Campaign, Id: { } campaign } && candidates.TryGetValue(campaign, out var places))
            {
                places.Add(at);
            }
            else if (marker is { Kind: HarnessMarker.Campaign, Id: { } other } && elsewhere.TryGetValue(other, out var owner))
            {
                reported.Add(new LinkReport(at, LinkReportReason.AnotherProject, other, owner));
            }
            else
            {
                reported.Add(new LinkReport(at, LinkReportReason.Unknown, marker.Kind == HarnessMarker.Campaign ? marker.Id : null));
            }
        }

        var results = new List<LinkedCampaign>();
        var oldPaths = new JsonObject();
        var newPaths = new JsonObject();
        foreach (var campaign in campaigns)
        {
            var places = candidates[campaign.PublicId];
            var recorded = campaign.FolderPath;
            var kept = recorded is null ? null : places.FirstOrDefault(place => FolderPaths.SameFolder(place, recorded));
            LinkCampaignOutcome result;
            if (kept is not null)
            {
                result = LinkCampaignOutcome.Kept;
                reported.AddRange(places.Where(place => !ReferenceEquals(place, kept)).Select(place => new LinkReport(place, LinkReportReason.Duplicate, campaign.PublicId)));
            }
            else if (places.Count == 1)
            {
                result = LinkCampaignOutcome.Relinked;
                Record(campaign, places[0]);
            }
            else if (places.Count > 1)
            {
                result = LinkCampaignOutcome.Duplicate;
                reported.AddRange(places.Select(place => new LinkReport(place, LinkReportReason.Duplicate, campaign.PublicId)));
            }
            else if (recorded is not null)
            {
                result = LinkCampaignOutcome.NotFound;
            }
            else if (campaign.Status == CampaignStatus.Archived)
            {
                result = LinkCampaignOutcome.Archived;
            }
            else
            {
                result = LinkCampaignOutcome.Made;
                Record(campaign, await folders
                    .MakeCampaignFolderAsync(folder, campaign.PublicId, campaign.Name, HoldsAsync)
                    .ConfigureAwait(false));
            }

            results.Add(new LinkedCampaign(campaign.PublicId, campaign.FolderPath, result));
        }

        var before = linked.FolderPath;
        var moved = !string.Equals(before, folder, StringComparison.Ordinal);
        if (moved || newPaths.Count > 0)
        {
            linked.FolderPath = folder;
            journal.Append(
                db,
                actor,
                JournalKinds.ProjectLinked,
                campaign: null,
                key: linked.PublicId,
                old: new JsonObject { ["folder_path"] = before, ["campaigns"] = oldPaths },
                updated: new JsonObject { ["folder_path"] = folder, ["campaigns"] = newPaths },
                reason: string.IsNullOrWhiteSpace(request.Reason) ? null : request.Reason.Trim());
            await db.SaveChangesAsync(CancellationToken.None).ConfigureAwait(false);
        }

        await transaction.CommitAsync(CancellationToken.None).ConfigureAwait(false);
        return new ProjectLinkResult(
            ProjectMapper.ToDto(linked),
            outcome,
            results,
            [.. reported.OrderBy(report => report.FolderPath, StringComparer.Ordinal)]);

        void Record(Campaign campaign, string at)
        {
            oldPaths[campaign.PublicId] = campaign.FolderPath;
            newPaths[campaign.PublicId] = at;
            campaign.FolderPath = at;
        }
    }

    /// <summary>
    /// A path, read before anything else: absolute, a folder that is there — a link records a folder, and never makes the
    /// one it was named — and none of the places no level may be. The full path, as the platform writes it — without a
    /// Windows device prefix, which names the same folder and would make every later comparison of its volume another
    /// volume — or null with the problem added.
    /// </summary>
    private string? ReadPath(string written, ValidationErrors errors)
    {
        if (string.IsNullOrWhiteSpace(written) || written.Contains('\0', StringComparison.Ordinal) || !Path.IsPathFullyQualified(FolderPaths.Undevice(written)))
        {
            errors.Add("path", "not_absolute", "path must be an absolute path, one that starts at a drive, a share or /; ~ is not expanded.");
            return null;
        }

        var path = FolderPaths.Normal(FolderPaths.Undevice(written));
        if (!Directory.Exists(path) && !File.Exists(path))
        {
            errors.Add("path", "not_found", "path names no folder; a link records a folder that is there.");
            return null;
        }

        if (HarnessRoot.FolderRefusal(path, paths, folders.Home) is { } refusal)
        {
            errors.Add("path", "not_allowed", refusal);
            return null;
        }

        return path;
    }

    /// <summary>
    /// A path given: a folder whose marker names a project is that project's again; a folder with no marker becomes the
    /// project named. Anything else at the path — a marker of another project, of another level, of an id nobody here
    /// holds, one that cannot be read — is refused, as is a folder another project records and another folder for a
    /// project whose own is still there. So is a place that is no project's: the root, a folder holding it, the root's
    /// <c>projects/</c>, and a folder at, inside or around another level's.
    /// </summary>
    private async Task<(Project Project, string Folder, LinkFolderOutcome Outcome, bool Adopt)> ByPathAsync(Project? named, string path, CancellationToken cancellationToken)
    {
        if (HarnessRoot.RootRefusal(path, folders.Root) is { } misplaced)
        {
            throw new ValidationException([new ErrorDetail("path", "not_allowed", misplaced)]);
        }

        var marker = HarnessFolders.MarkerOf(path);
        Project project;
        if (marker is null)
        {
            project = named ?? throw new ValidationException(
                [new ErrorDetail("project_id", "required", "the folder at path has no marker, so project_id names the project it becomes.")]);
        }
        else if (marker is { Kind: HarnessMarker.Project, Id: { } owner }
            && await db.Projects.FirstOrDefaultAsync(candidate => candidate.PublicId == owner, cancellationToken).ConfigureAwait(false) is { } holder
            && (named is null || named.Id == holder.Id))
        {
            project = holder;
        }
        else
        {
            throw DomainErrors.HarnessFolderTaken(path);
        }

        var others = await db.Projects
            .AsNoTracking()
            .Where(other => other.Id != project.Id && other.FolderPath != null)
            .Select(other => new { other.PublicId, other.FolderPath })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        if (others.FirstOrDefault(other => FolderPaths.SameFolder(other.FolderPath!, path)) is { } recorder)
        {
            throw DomainErrors.HarnessFolderRecorded(path, recorder.PublicId);
        }

        var same = project.FolderPath is { } recorded && FolderPaths.SameFolder(recorded, path);
        if (!same && project.FolderPath is { } elsewhere && HarnessFolders.Holds(elsewhere, HarnessMarker.Project, project.PublicId))
        {
            throw DomainErrors.HarnessFolderPresent(project.PublicId, elsewhere, path);
        }

        await RefuseNestedAsync(project, path, same, others.Select(other => (other.PublicId, other.FolderPath!)), cancellationToken).ConfigureAwait(false);
        return marker is null ? (project, path, LinkFolderOutcome.Adopted, true)
            : same ? (project, project.FolderPath!, LinkFolderOutcome.Kept, false)
            : (project, path, LinkFolderOutcome.Relinked, false);
    }

    /// <summary>
    /// Refuses a path at, inside or around another level's folder, naming that folder: a session started in one would read
    /// the other's memory files. The levels are the folders the database records — another project's, and any campaign's,
    /// this project's own included, since a project's folder is never inside its own campaign's and holds them only where
    /// it is the folder recorded for it — and the markers a walk up from the path reads. Nothing below the path is searched:
    /// a marked folder there that nothing records stays the person's, as an unmarked one does.
    /// </summary>
    private async Task RefuseNestedAsync(Project project, string path, bool same, IEnumerable<(string Id, string Folder)> others, CancellationToken cancellationToken)
    {
        var at = FolderPaths.Real(path);
        var campaigns = (await db.Campaigns
                .AsNoTracking()
                .Where(campaign => campaign.FolderPath != null)
                .Select(campaign => new { campaign.PublicId, campaign.ProjectId, campaign.FolderPath })
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false))
            .Select(campaign => (campaign.PublicId, Mine: campaign.ProjectId == project.Id, Folder: campaign.FolderPath!, Real: FolderPaths.Real(campaign.FolderPath!)))
            .ToList();

        // Inside a campaign's folder first, so the folder named is the nearest one around the path.
        foreach (var campaign in campaigns)
        {
            if (FolderPaths.Same(at, campaign.Real) || FolderPaths.Inside(at, campaign.Real))
            {
                throw DomainErrors.HarnessFolderNested(path, $"is at or inside '{campaign.Folder}', the folder recorded for campaign '{campaign.PublicId}'");
            }
        }

        foreach (var (id, folder) in others)
        {
            var real = FolderPaths.Real(folder);
            if (FolderPaths.Inside(at, real))
            {
                throw DomainErrors.HarnessFolderNested(path, $"is inside '{folder}', the folder recorded for project '{id}'");
            }

            if (FolderPaths.Inside(real, at))
            {
                throw DomainErrors.HarnessFolderNested(path, $"holds '{folder}', the folder recorded for project '{id}'");
            }
        }

        foreach (var campaign in campaigns.Where(campaign => !(same && campaign.Mine)))
        {
            if (FolderPaths.Inside(campaign.Real, at))
            {
                throw DomainErrors.HarnessFolderNested(path, $"holds '{campaign.Folder}', the folder recorded for campaign '{campaign.PublicId}'");
            }
        }

        // Up from the path to the top of the file system, a marker per folder: bounded by the path's own depth.
        foreach (var above in FolderPaths.AndAbove(at).Skip(1))
        {
            if (MarkerAbove(above) is { Kind: HarnessMarker.Project or HarnessMarker.Campaign, Id: { } id } found)
            {
                throw DomainErrors.HarnessFolderNested(path, $"is inside '{above}', a folder marked for {found.Kind} '{id}'");
            }
        }
    }

    /// <summary>The marker of a folder above a path, null where it has none or the disk will not let it be read: only a project's or a campaign's refuses.</summary>
    private static HarnessMarker? MarkerAbove(string folder)
    {
        try
        {
            return HarnessMarker.In(folder);
        }
        catch (Exception unread) when (unread is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>
    /// A project alone: its recorded folder, which has to be there still with its marker — a moved one is never made again
    /// beside it — or, where none is recorded, the folder the policy makes under the root, which is made once every check
    /// has passed.
    /// </summary>
    private async Task<(Project Project, string Folder, LinkFolderOutcome Outcome, bool Adopt)> ByPolicyAsync(Project project, CancellationToken cancellationToken)
    {
        if (project.FolderPath is { } recorded)
        {
            return HarnessFolders.Holds(recorded, HarnessMarker.Project, project.PublicId)
                ? (project, recorded, LinkFolderOutcome.Kept, false)
                : throw DomainErrors.HarnessLinkFolderMissing(project.PublicId, recorded);
        }

        var planned = folders.ProjectFolderOf(project.Slug);
        var recorders = await db.Projects
            .AsNoTracking()
            .Where(other => other.Id != project.Id && other.FolderPath != null)
            .Select(other => new { other.PublicId, other.FolderPath })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        if (recorders.FirstOrDefault(other => FolderPaths.SameFolder(other.FolderPath!, planned)) is { } recorder)
        {
            throw DomainErrors.HarnessFolderRecorded(planned, recorder.PublicId);
        }

        return (project, planned, LinkFolderOutcome.Made, false);
    }

    /// <summary>Whether a campaign of that id is one this runtime holds, which numbers a leaf rather than refusing it.</summary>
    private Task<bool> HoldsAsync(string publicId) =>
        db.Campaigns.AsNoTracking().AnyAsync(campaign => campaign.PublicId == publicId, CancellationToken.None);
}
