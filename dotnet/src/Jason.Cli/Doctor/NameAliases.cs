using System.Globalization;
using Jason.Cli.Skills;
using Jason.Contracts.Api;

namespace Jason.Cli.Doctor;

/// <summary>The kinds of name a bundle writes as an alias when Jason did not ship it.</summary>
public enum AliasKind
{
    /// <summary>A plugin's id: a package this repository does not ship.</summary>
    Plugin,

    /// <summary>A role's name: a role a person added.</summary>
    Role,

    /// <summary>A skill pack's name: a pack this repository does not ship.</summary>
    SkillPack,
}

/// <summary>
/// The one rule for every name Jason did not ship: a plugin's id, a role's name, a skill pack's name. A name Jason ships
/// is a public word and travels as written; any other is the word of the person who chose it — <c>acme-crm</c> or
/// <c>acme-closer</c> says whose work an installation does — and is written as one alias per distinct name, so a reader
/// can still tell two entries apart or see that they are the same. The aliases of a kind are numbered from 1 in the
/// ordinal order of the names, so the same installation reads the same way twice; the mapping is never written.
/// </summary>
public sealed class NameAliases
{
    /// <summary>How the aliases are numbered, as a bundle's header says it.</summary>
    public const string Numbering = "per kind, from 1, in the ordinal order of the names";

    /// <summary>Each kind's alias, with N standing for its number, as a bundle's header lists it.</summary>
    public static IReadOnlyDictionary<AliasKind, string> Marks { get; } = new Dictionary<AliasKind, string>
    {
        [AliasKind.Plugin] = "<plugin-N>",
        [AliasKind.Role] = "<role-N>",
        [AliasKind.SkillPack] = "<skill-pack-N>",
    };

    private static readonly IReadOnlyDictionary<AliasKind, string> Prefixes = new Dictionary<AliasKind, string>
    {
        [AliasKind.Plugin] = "plugin",
        [AliasKind.Role] = "role",
        [AliasKind.SkillPack] = "skill-pack",
    };

    private readonly Dictionary<string, string> _aliases = new(StringComparer.OrdinalIgnoreCase);

    private NameAliases()
    {
    }

    /// <summary>None: a bundle that learned no name of a person's own.</summary>
    public static NameAliases None { get; } = new();

    /// <summary>Every name Jason ships, of any kind: a public word, whichever kind it is met as.</summary>
    public static IReadOnlySet<string> Shipped { get; } =
        new HashSet<string>([.. ShippedPlugins.Ids, .. ShippedRoles.Names, .. SkillPacks.All], StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The aliases for the names met, by kind. A name Jason ships, an empty one and one of this program's own marks are
    /// none of the person's; a name met as two kinds keeps the alias of the first kind in the order of
    /// <see cref="AliasKind"/>, since one name in a text cannot be told apart by kind.
    /// </summary>
    public static NameAliases For(IReadOnlyDictionary<AliasKind, IEnumerable<string>> met)
    {
        ArgumentNullException.ThrowIfNull(met);

        var aliases = new NameAliases();
        foreach (var kind in Enum.GetValues<AliasKind>())
        {
            if (!met.TryGetValue(kind, out var names))
            {
                continue;
            }

            var own = names
                .Where(name => !string.IsNullOrWhiteSpace(name) && !Shipped.Contains(name) && !name.StartsWith('<') && !aliases._aliases.ContainsKey(name))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Order(StringComparer.Ordinal)
                .ToList();
            for (var at = 0; at < own.Count; at++)
            {
                aliases._aliases[own[at]] = string.Create(CultureInfo.InvariantCulture, $"<{Prefixes[kind]}-{at + 1}>");
            }
        }

        return aliases;
    }

    /// <summary>The names and their aliases, for the rewriter that writes them; never written into a bundle.</summary>
    internal IReadOnlyDictionary<string, string> Map => _aliases;

    /// <summary>The alias of a name, or null where it has none.</summary>
    public string? Of(string name) => _aliases.GetValueOrDefault(name);

    // A name of the person's own must not print when an instance does.
    public override string ToString() => string.Create(CultureInfo.InvariantCulture, $"{_aliases.Count} aliases");
}
