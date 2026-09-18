using System.Text.Json.Nodes;
using Jason.Contracts.Api;
using Jason.Runtime.Plugins.Invocation;
using Jason.Runtime.Routing;
using Jason.Runtime.WorkItems;

namespace Jason.Runtime.Execution;

/// <summary>
/// Everything a command is told about the attempt it runs. It is a value, not a database row: the handler has
/// already read what it needs, so the command never touches persistence.
/// </summary>
/// <param name="Kill">Signalled when the attempt must stop now — a cancellation, or a lease the runtime gave up on.</param>
/// <param name="Operation">The canonical operation a provider item names; null for agent work, which names a role.</param>
/// <param name="Plan">
/// For a provider operation, everything the claim resolved: the package, the route it was chosen by and the
/// composed input. It travels in memory from the claim rather than being looked up again, so a reload between
/// the claim and the run cannot change what was already decided. Null for agent work.
/// </param>
/// <param name="Deny">
/// What the launched agent may not do, in its host's own vocabulary, as the execution profile froze it. It is
/// the half of a policy that a work directory can carry; the other half is already composed into
/// <paramref name="EntryCommand"/>. Empty where no profile ran this item.
/// </param>
/// <param name="CliCommand">
/// The bare word the launched agent calls home with, as its profile names it. Null means this executable's own
/// name, which is the one the launcher puts within the child's reach.
/// </param>
public sealed record CommandContext(
    string WorkItemId,
    string AttemptId,
    int AttemptNumber,
    string CampaignId,
    string? ContactId,
    WorkItemKind Kind,
    string? Role,
    string? ExecutionProfile,
    JsonObject ContextSnapshot,
    JsonNode? ResultFormat,
    EffectiveLimits Limits,
    DateTimeOffset LockUntil,
    IReadOnlyList<string> EntryCommand,
    string WorkDir,
    CancellationToken Kill,
    string? Operation = null,
    ProviderOpPlan? Plan = null,
    IReadOnlyList<string>? Deny = null,
    string? CliCommand = null);

/// <summary>How the run ended, as the command saw it. What it means for the work item is decided elsewhere.</summary>
public abstract record CommandOutcome
{
    /// <summary>The executor finished through the API: the attempt was no longer running when the process ended.</summary>
    public sealed record Completed(AttemptLaunchDto? Launch) : CommandOutcome;

    /// <summary>The process ended while the attempt was still running — nobody told the runtime how it went.</summary>
    public sealed record Exited(int ExitCode, string? StderrTail, AttemptLaunchDto Launch) : CommandOutcome;

    /// <summary>
    /// The command could not be started at all. <paramref name="Code"/> is the attempt error it ends with: a
    /// launch that failed for a reason of its own — a role whose skill a host would silently ignore — says so
    /// rather than being flattened into "the executor could not be started".
    /// </summary>
    public sealed record LaunchFailed(string Message, AttemptLaunchDto Launch, string Code = AttemptErrors.ExecutorLaunchFailed) : CommandOutcome;

    /// <summary>The kill signal was given and honoured.</summary>
    public sealed record Killed(AttemptLaunchDto? Launch) : CommandOutcome;

    /// <summary>
    /// A plugin was invoked and the invocation ended — with an answer, with a declared failure, or with the
    /// protocol itself not completing. Which of the three it was, and what it means for the work item, is read
    /// from the result rather than decided here.
    /// </summary>
    /// <param name="Launch">Null where the invoker refused before any process existed.</param>
    public sealed record Provider(PluginInvocationResult Result, AttemptLaunchDto? Launch) : CommandOutcome;
}

/// <summary>The seam between the dispatcher and whatever actually performs the work of one kind.</summary>
public interface ICommand
{
    WorkItemKind Kind { get; }

    Task<CommandOutcome> RunAsync(CommandContext context, CancellationToken cancellationToken);
}
