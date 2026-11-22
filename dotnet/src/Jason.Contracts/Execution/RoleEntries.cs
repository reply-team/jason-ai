using System.Collections.Frozen;

namespace Jason.Contracts.Execution;

/// <summary>
/// The named ways into a role's job that a work item may carry as its <c>entry</c>: for each role, the set of entry
/// names it has. It is a fixed vocabulary, written here and nowhere else, and only the runtime writes an entry onto a
/// work item; no caller of the API names one.
/// <para>
/// The table is role-neutral in its shape. Today only the manager has rows; every other role has none, so no item of
/// theirs carries an entry. Where an entry is carried, the launch hands its text to the host in the envelope, read
/// from the role's own folder at <c>entries/&lt;name&gt;.md</c>.
/// </para>
/// </summary>
public static class RoleEntries
{
    private static readonly FrozenDictionary<string, FrozenSet<string>> Table = new Dictionary<string, FrozenSet<string>>(StringComparer.Ordinal)
    {
        ["manager"] = FrozenSet.Create(StringComparer.Ordinal, "start", "resume", "quick_look", "deep_review", "inbound_reply"),
    }.ToFrozenDictionary(StringComparer.Ordinal);

    private static readonly FrozenSet<string> None = FrozenSet<string>.Empty;

    /// <summary>The folder, inside a role's own folder, that holds one file per entry: <c>entries/&lt;name&gt;.md</c>.</summary>
    public const string Folder = "entries";

    /// <summary>The roles the table gives any entry to.</summary>
    public static IReadOnlyList<string> Roles { get; } = [.. Table.Keys.Order(StringComparer.Ordinal)];

    /// <summary>The entry names <paramref name="role"/> has; empty for a role the table does not name.</summary>
    public static FrozenSet<string> Of(string role)
    {
        ArgumentNullException.ThrowIfNull(role);
        return Table.GetValueOrDefault(role) ?? None;
    }

    /// <summary>Whether role <paramref name="role"/> may carry entry <paramref name="entry"/>: exact spellings only.</summary>
    public static bool Carries(string? role, string? entry) =>
        role is not null && entry is not null && Of(role).Contains(entry);

    /// <summary>The path of an entry's file relative to the role's folder.</summary>
    public static string RelativePath(string entry) => Path.Combine(Folder, entry + ".md");
}
