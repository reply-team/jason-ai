using Jason.Contracts.Discovery;
using Jason.Contracts.Execution;

namespace Jason.Runtime.Execution.Hosts;

/// <summary>
/// The environment a launched agent runs in: the runtime's own, plus four values and not one more. An unhappy
/// agent host prints its environment, and whatever is in there is in the transcript the user keeps — so the
/// capability token is not among these, and never will be. It lives in the descriptor file the envelope names.
/// </summary>
public static class LaunchEnvironment
{
    /// <summary>The search path, by the one spelling every platform answers to.</summary>
    public const string SearchPathVariable = "PATH";

    /// <summary>
    /// Adds the four to <paramref name="environment"/>: the data directory this runtime owns, the attempt and
    /// the work item the child is running — both non-secret values the envelope already carries, here so that an
    /// agent reporting through the CLI is its attempt without having to remember to say so — and the directory
    /// this build of Jason lives in, at the front of the search path so the bare command word the agent was
    /// allowed to run resolves to this runtime rather than to whatever else answers for that name.
    /// </summary>
    /// <param name="cliDirectory">Null where this process publishes no path of its own; the search path is then left as it is.</param>
    public static void Apply(
        IDictionary<string, string?> environment,
        string dataDirectory,
        string attemptId,
        string workItemId,
        string? cliDirectory)
    {
        ArgumentNullException.ThrowIfNull(environment);

        environment[JasonPaths.DataDirectoryVariable] = dataDirectory;
        environment[ExecutionEnvironment.AttemptIdVariable] = attemptId;
        environment[ExecutionEnvironment.WorkItemIdVariable] = workItemId;

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
