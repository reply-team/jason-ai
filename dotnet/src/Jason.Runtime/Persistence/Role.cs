using System.Text.Json.Nodes;

namespace Jason.Runtime.Persistence;

/// <summary>
/// A job description, not a process. The nine builtin roles arrive with the migration and are launchable only
/// once a command exists for them — either their own or the one configured for every role.
/// </summary>
public sealed class Role
{
    public int Id { get; set; }

    /// <summary><c>rol_</c> + ULID.</summary>
    public required string PublicId { get; set; }

    public required string Name { get; set; }

    /// <summary>True for the seeded roster: a builtin role is part of the runtime's vocabulary.</summary>
    public bool Builtin { get; set; }

    public string? Description { get; set; }

    /// <summary>The command that starts an agent host for this role; empty means "use the configured default, if any".</summary>
    public List<string> EntryCommand { get; set; } = [];

    /// <summary>Opaque to the runtime and handed to whoever launches the role.</summary>
    public JsonObject ProfileDefaults { get; set; } = new();

    /// <summary>
    /// Which execution profile this role's work uses where the item and its campaign say nothing: a statement
    /// about a kind of worker everywhere, which is why a campaign's own policy outranks it.
    /// </summary>
    public string? ExecutionProfile { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }
}
