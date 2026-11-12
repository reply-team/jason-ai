using Jason.Contracts.Discovery;
using Jason.Contracts.Execution;
using Jason.Contracts.Plugins;

namespace Jason.Runtime.Execution.Hosts;

/// <summary>
/// The environment a launched agent runs in: the runtime's own, less the runtime's configuration, plus the runtime's four
/// and the constants a host's row of the host table declares, never a secret and never a value taken from a person. An
/// unhappy agent host prints its environment, and whatever is in there is in the transcript the user keeps — so the
/// capability token is not among these, and never will be. It lives in the descriptor file the envelope names.
/// </summary>
/// <remarks>
/// What is taken out is every configuration override of the runtime: a name that starts with <c>JASON_</c> and holds the
/// double underscore that separates a section from a key, <c>JASON_Dispatcher__MaxParallel</c> say, compared without
/// regard to case. Any setting can be given that way, a plugin's secret under <c>Plugins:Config</c>
/// (<c>JASON_Plugins__Config__&lt;id&gt;__&lt;field&gt;</c>) among them, so a person who put one in the runtime's
/// environment would otherwise have handed it to every role's child and, through the first one that prints its
/// environment, to a transcript. The runtime's own single-word names (<c>JASON_DATA_DIR</c> and the like) are not
/// configuration and stay.
/// <para>
/// So is every name a plugin of the active snapshot is granted under <c>Plugins:Grants:&lt;id&gt;:Env</c>: such a name is
/// there to carry a plugin's credential to that plugin's host, and a role's agent is no plugin. A granted name the
/// machine list every plugin host gets holds (<see cref="BaseEnvironment.ForCurrentOs"/>), a proxy say, is machine
/// configuration and stays, granted or not. Everything else the runtime's environment holds is the person's machine and
/// stays too. This keeps those values out of a dump of the environment; it is not a wall, since a role's agent runs as
/// the person and can read what the person can.
/// </para>
/// </remarks>
public static class LaunchEnvironment
{
    /// <summary>The search path, by the one spelling every platform answers to.</summary>
    public const string SearchPathVariable = "PATH";

    /// <summary>What every variable of the runtime's own starts with, which a host's constant never does.</summary>
    private const string Prefix = "JASON_";

    /// <summary>
    /// Whether <paramref name="name"/> sets a setting of the runtime: the <c>JASON_</c> prefix and the double underscore
    /// that separates a section from a key, in any case, as the configuration reads such a name.
    /// </summary>
    public static bool IsConfigurationOverride(string name) =>
        name.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase) && name.Contains("__", StringComparison.Ordinal);

    /// <summary>
    /// Takes every configuration override of the runtime and every granted name off the machine list out of
    /// <paramref name="environment"/>, then adds the four: the data directory this runtime owns, the attempt and
    /// the work item the child is running — both non-secret values the envelope already carries, here so that an
    /// agent reporting through the CLI is its attempt without having to remember to say so — and the directory
    /// this build of Jason lives in, at the front of the search path so the bare command word the agent was
    /// allowed to run resolves to this runtime rather than to whatever else answers for that name.
    /// </summary>
    /// <param name="cliDirectory">Null where this process publishes no path of its own; the search path is then left as it is.</param>
    /// <param name="hostConstants">
    /// The constants of the host the attempt runs, added after the four: <see cref="IAgentHost.Environment"/>. A name that
    /// is one of the runtime's own, the search path included, or starts with the runtime's <c>JASON_</c> prefix is refused
    /// rather than applied, so a host can neither replace a value this runtime gave nor pass for the runtime.
    /// </param>
    /// <param name="grantedVariables">
    /// The names the active snapshot grants to any plugin under <c>Plugins:Grants:&lt;id&gt;:Env</c>; each is taken out
    /// unless the machine list holds it. Null or empty where no plugin is granted one.
    /// </param>
    public static void Apply(
        IDictionary<string, string?> environment,
        string dataDirectory,
        string attemptId,
        string workItemId,
        string? cliDirectory,
        IReadOnlyDictionary<string, string>? hostConstants = null,
        IEnumerable<string>? grantedVariables = null)
    {
        ArgumentNullException.ThrowIfNull(environment);

        // Refused before anything is written, so a refused name leaves the environment as it was.
        foreach (var name in hostConstants?.Keys ?? Enumerable.Empty<string>())
        {
            if (string.IsNullOrWhiteSpace(name)
                || name.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase)
                || string.Equals(name, SearchPathVariable, StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException($"A host's environment constant '{name}' is one of the runtime's own names, or has none.", nameof(hostConstants));
            }
        }

        // The runtime's configuration is the runtime's, never the child's: taken out before the four are written, so the
        // four are never mistaken for it.
        foreach (var name in environment.Keys.Where(IsConfigurationOverride).ToList())
        {
            environment.Remove(name);
        }

        // A plugin's granted names are that plugin's, never a role's, except where the machine list every plugin host
        // gets names one too. Names are compared as the operating system compares them, as the plugin host's are.
        var comparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        var machine = new HashSet<string>(BaseEnvironment.ForCurrentOs, comparer);
        var granted = new HashSet<string>((grantedVariables ?? []).Where(name => !string.IsNullOrEmpty(name) && !machine.Contains(name)), comparer);
        foreach (var name in environment.Keys.Where(granted.Contains).ToList())
        {
            environment.Remove(name);
        }

        environment[JasonPaths.DataDirectoryVariable] = dataDirectory;
        environment[ExecutionEnvironment.AttemptIdVariable] = attemptId;
        environment[ExecutionEnvironment.WorkItemIdVariable] = workItemId;
        foreach (var (name, value) in hostConstants ?? new Dictionary<string, string>())
        {
            environment[name] = value;
        }

        if (string.IsNullOrEmpty(cliDirectory))
        {
            return;
        }

        // Prepended rather than set: a host needs everything it already had — its own interpreter, git, node —
        // and only has to find this Jason before any other.
        environment[SearchPathVariable] = environment.TryGetValue(SearchPathVariable, out var existing) && !string.IsNullOrEmpty(existing)
            ? cliDirectory + Path.PathSeparator + existing
            : cliDirectory;
    }
}
