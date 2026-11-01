using System.Text.Json.Serialization;
using Jason.Contracts.Json;

namespace Jason.Contracts.Api;

/// <summary>
/// A project: one business goal of the client — usually one product — and the campaigns that work toward it. Every
/// campaign is in exactly one. <c>slug</c> is made once, from the name or as given, is unique and never changes;
/// <c>default</c> says whether this is the project a campaign created without naming one lands in, which every installation
/// has exactly one of. <c>folder_path</c> is the full path of the project's folder under the harness root, null until one
/// is made — at its creation, or at its first campaign's for a project that existed before folders did.
/// </summary>
public sealed record ProjectDto(string Id, string Name, string Slug, bool Default, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt, string? FolderPath = null);

/// <summary>
/// <c>slug</c> is optional: absent, it is made from the name. There is no idempotency key, and none is needed: the slug is
/// unique, so a create sent again after its answer was lost is refused as <c>project_exists</c>, and the refusal names the
/// project the first one made.
/// </summary>
public sealed record ProjectCreateRequest(string? Name, string? Slug, ActorRef? Actor, string? Reason);

/// <summary>A project by its id or by its slug: exactly one of the two.</summary>
public sealed record ProjectGetRequest(string? ProjectId, string? Slug = null);

/// <summary><c>query</c> finds projects by any part of their name, whatever its case or accents.</summary>
public sealed record ProjectListRequest(string? Query, int? Limit, string? Cursor);

/// <summary>A partial patch: an absent <c>name</c> leaves the name alone. A project's slug never changes.</summary>
public sealed record ProjectUpdateRequest(
    string? ProjectId,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] Optional<string?> Name,
    ActorRef? Actor,
    string? Reason);

/// <summary>
/// Records where a project's folder is, and its campaigns' (<c>docs/projects.md</c>, "Linking a folder"). At least one of
/// the two is given: a <c>path</c> whose marker names a project this runtime holds records that folder again for it; a
/// <c>path</c> with no marker becomes the folder of the project <c>project_id</c> names; a <c>project_id</c> alone makes the
/// project's folder by the policy where none is recorded, and looks through it again where one is. <c>path</c> is
/// absolute and names a folder that is there.
/// </summary>
public sealed record ProjectLinkRequest(string? ProjectId, string? Path, ActorRef? Actor, string? Reason);

/// <summary>
/// What a link did: the project as it is now, what became of its folder, every campaign of the project with what became
/// of its folder, and the folders below <c>campaigns/</c> it found and did not act on. Ids and paths only, never a name.
/// The campaigns are every one the project has, in id order, and are not paged.
/// </summary>
public sealed record ProjectLinkResult(ProjectDto Project, LinkFolderOutcome Folder, IReadOnlyList<LinkedCampaign> Campaigns, IReadOnlyList<LinkReport> Reported);

/// <summary>A campaign of the linked project and the folder now recorded for it, null where it has none.</summary>
public sealed record LinkedCampaign(string CampaignId, string? FolderPath, LinkCampaignOutcome Outcome);

/// <summary>
/// A folder below the project's <c>campaigns/</c> whose marker the link did not act on: the campaign it names, where it
/// names one, and the project the runtime puts that campaign in, where that is another.
/// </summary>
public sealed record LinkReport(string FolderPath, LinkReportReason Reason, string? CampaignId = null, string? ProjectId = null);

/// <summary>What became of the project's folder.</summary>
public enum LinkFolderOutcome
{
    /// <summary>The folder its marker names the project from is recorded again: the person moved or renamed it.</summary>
    Relinked,

    /// <summary>A folder with no marker became the project's: its marker was written, and the two files where absent.</summary>
    Adopted,

    /// <summary>The project had none, and the policy made one under the root.</summary>
    Made,

    /// <summary>The folder recorded is the folder: nothing about it changed.</summary>
    Kept,
}

/// <summary>What became of one campaign's folder.</summary>
public enum LinkCampaignOutcome
{
    /// <summary>Found below the project's <c>campaigns/</c> by its marker somewhere other than recorded: that path is recorded now.</summary>
    Relinked,

    /// <summary>Found where it is recorded.</summary>
    Kept,

    /// <summary>It had none, and the policy made one in the project's folder.</summary>
    Made,

    /// <summary>Recorded, and not found below the project's <c>campaigns/</c>: the recorded path stays.</summary>
    NotFound,

    /// <summary>Two or more folders there carry its marker and none of them is the one recorded: the recorded path stays.</summary>
    Duplicate,

    /// <summary>Archived and with no folder: none is made for a campaign that can never run again.</summary>
    Archived,
}

/// <summary>Why a folder below <c>campaigns/</c> was not acted on.</summary>
public enum LinkReportReason
{
    /// <summary>Its marker names a campaign the runtime puts in another project: the database decides, never the folder.</summary>
    AnotherProject,

    /// <summary>Its marker names an id no campaign here holds, is another level's, or cannot be read.</summary>
    Unknown,

    /// <summary>A second folder carrying the marker of one campaign.</summary>
    Duplicate,
}
