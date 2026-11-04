using System.Collections.Frozen;
using System.Globalization;
using Jason.Contracts.Api;
using Jason.Runtime.Configuration;
using Jason.Runtime.Plugins.Manifest;

namespace Jason.Runtime.Plugins.Registry;

/// <summary>
/// What one plugin may actually use: the intersection of what its manifest requested and what the user granted,
/// frozen at the reload that resolved it. Warnings name grants that asked for something the manifest never
/// declared — nothing is ever granted that was not declared.
/// </summary>
public sealed record ResolvedGrants(
    IReadOnlyList<string> Exec,
    IReadOnlyList<string> Http,
    IReadOnlyList<string> Env,
    IReadOnlyList<ManifestProblem> Warnings)
{
    /// <summary>The default for a plugin nobody has decided about: declaration is not permission.</summary>
    public static ResolvedGrants None { get; } = new([], [], [], []);

    /// <summary>The Runtime API families granted, as the manifest spells them.</summary>
    public IReadOnlyList<string> Api { get; init; } = [];

    /// <summary>
    /// The operations those families open, resolved once at the load that froze these grants: the set a handler's
    /// capability is minted with, and the only one its door opens.
    /// </summary>
    public IReadOnlySet<string> ApiOperations { get; init; } = FrozenSet<string>.Empty;
}

public static class GrantResolver
{
    /// <summary>Everything a capability requested, in one entry.</summary>
    public const string Everything = "*";

    public static ResolvedGrants Resolve(PluginManifest manifest, PluginGrant? grant)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        if (grant is null)
        {
            return ResolvedGrants.None;
        }

        var warnings = new List<ManifestProblem>();
        var executables = manifest.Capabilities.Exec is null
            ? []
            : manifest.Capabilities.Exec.Executables.Select(executable => executable.Name).ToList();

        var exec = Match("exec", grant.Exec, executables, StringComparer.Ordinal, warnings);
        var http = Match("http", grant.Http, manifest.Capabilities.Http?.Hosts ?? [], StringComparer.OrdinalIgnoreCase, warnings);
        var env = Match("env", grant.Env, manifest.Capabilities.Env?.Variables ?? [], StringComparer.Ordinal, warnings);
        var api = Match("api", grant.Api, manifest.Capabilities.Api?.Families ?? [], StringComparer.Ordinal, warnings);
        return new ResolvedGrants(exec, http, env, warnings)
        {
            Api = api,
            ApiOperations = OperationFamilies.OperationsOf(api),
        };
    }

    /// <summary>
    /// Hosts are compared the way hosts are compared, programs and variable names exactly. Whatever matches is
    /// recorded as the manifest spells it, so everything downstream compares one form.
    /// </summary>
    private static IReadOnlyList<string> Match(
        string capability,
        IReadOnlyList<string> granted,
        IReadOnlyList<string> requested,
        StringComparer comparer,
        List<ManifestProblem> warnings)
    {
        var resolved = new List<string>();
        for (var index = 0; index < granted.Count; index++)
        {
            var entry = granted[index];
            if (string.Equals(entry, Everything, StringComparison.Ordinal))
            {
                foreach (var name in requested)
                {
                    Include(resolved, name);
                }

                continue;
            }

            var match = requested.FirstOrDefault(name => comparer.Equals(name, entry));
            if (match is null)
            {
                warnings.Add(new ManifestProblem(
                    ProblemCodes.GrantUnrequested,
                    string.Create(CultureInfo.InvariantCulture, $"grants.{capability}[{index}]"),
                    $"the settings grant '{entry}', which this plugin's manifest does not request; nothing was granted for it."));
                continue;
            }

            Include(resolved, match);
        }

        return resolved;
    }

    private static void Include(List<string> resolved, string name)
    {
        if (!resolved.Contains(name, StringComparer.Ordinal))
        {
            resolved.Add(name);
        }
    }
}
