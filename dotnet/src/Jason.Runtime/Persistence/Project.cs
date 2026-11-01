namespace Jason.Runtime.Persistence;

/// <summary>
/// One business goal of the client — in practice, usually one product — and the campaigns that work toward it. Every
/// campaign belongs to exactly one project. A project is Jason's own: it never reaches a plugin's input, is never pinned
/// to a provider, and no provider has to understand it.
/// </summary>
/// <remarks>
/// Every installation has one default project, made by the migration that brought projects in, which every campaign made
/// before then belongs to and every campaign created without naming a project lands in. It can be renamed and cannot stop
/// being the default.
/// </remarks>
public sealed class Project
{
    /// <summary>Internal key, used only for foreign keys inside the database. Never exposed.</summary>
    public int Id { get; set; }

    /// <summary>The only identifier that leaves the runtime: <c>prj_</c> + ULID.</summary>
    public required string PublicId { get; set; }

    public required string Name { get; set; }

    /// <summary>Made once from the name, or given, when the project is created; unique, and never changed after.</summary>
    public required string Slug { get; set; }

    /// <summary>Whether this is the installation's default project. Exactly one is, and the database is what says so.</summary>
    public bool IsDefault { get; set; }

    /// <summary>Where the project's folder is, once there are folders. Nothing writes it yet, and no answer carries it.</summary>
    public string? FolderPath { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }
}
