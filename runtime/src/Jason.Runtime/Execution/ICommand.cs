using System.Text.Json.Nodes;
using Jason.Contracts.Api;
using Jason.Runtime.WorkItems;

namespace Jason.Runtime.Execution;

/// <summary>
/// Everything a command is told about the attempt it runs. It is a value, not a database row: the handler has
/// already read what it needs, so the command never touches persistence.
/// </summary>
/// <param name="Kill">Signalled when the attempt must stop now — a cancellation, or a lease the runtime gave up on.</param>
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
    CancellationToken Kill);

/// <summary>How the run ended, as the command saw it. What it means for the work item is decided elsewhere.</summary>
public abstract record CommandOutcome
{
    /// <summary>The executor finished through the API: the attempt was no longer running when the process ended.</summary>
    public sealed record Completed(AttemptLaunchDto? Launch) : CommandOutcome;

    /// <summary>The process ended while the attempt was still running — nobody told the runtime how it went.</summary>
    public sealed record Exited(int ExitCode, string? StderrTail, AttemptLaunchDto Launch) : CommandOutcome;

    /// <summary>The command could not be started at all.</summary>
    public sealed record LaunchFailed(string Message, AttemptLaunchDto Launch) : CommandOutcome;

    /// <summary>The kill signal was given and honoured.</summary>
    public sealed record Killed(AttemptLaunchDto? Launch) : CommandOutcome;
}

/// <summary>The seam between the dispatcher and whatever actually performs the work of one kind.</summary>
public interface ICommand
{
    WorkItemKind Kind { get; }

    Task<CommandOutcome> RunAsync(CommandContext context, CancellationToken cancellationToken);
}
