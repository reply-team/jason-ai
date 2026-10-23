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
