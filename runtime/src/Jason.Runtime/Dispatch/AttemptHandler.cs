using System.Globalization;
using System.Text.Json.Nodes;
using Jason.Contracts.Api;
using Jason.Runtime.Configuration;
using Jason.Runtime.Domain;
using Jason.Runtime.Execution;
using Jason.Runtime.Journal;
using Jason.Runtime.Persistence;
using Jason.Runtime.WorkItems;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Jason.Runtime.Dispatch;

/// <summary>
/// One claimed attempt, from start to verdict. The database is touched twice and only briefly — once to say the
/// work has begun, once to say how it ended — and the command runs between those two moments holding nothing:
/// a run that lasts an hour must not hold a connection, and a runtime that dies mid-run leaves a lease, not a
/// half-written row.
/// </summary>
public static class AttemptHandler
{
    private static readonly Action<ILogger, string, string, Exception?> AttemptStarted = LoggerMessage.Define<string, string>(
        LogLevel.Information,
        new EventId(1, nameof(AttemptStarted)),
        "Attempt {AttemptId} of work item {WorkItemId} started");

    private static readonly Action<ILogger, string, Exception?> AttemptVanished = LoggerMessage.Define<string>(
        LogLevel.Information,
        new EventId(2, nameof(AttemptVanished)),
        "Attempt {AttemptId} was no longer the one to run; the handler did nothing");

    private static readonly Action<ILogger, string, Exception?> OutcomeNotRecorded = LoggerMessage.Define<string>(
        LogLevel.Information,
        new EventId(3, nameof(OutcomeNotRecorded)),
        "Attempt {AttemptId} was decided elsewhere while its outcome was being written");

    private static readonly Action<ILogger, string, Exception?> CommandFailed = LoggerMessage.Define<string>(
        LogLevel.Error,
        new EventId(4, nameof(CommandFailed)),
        "The command of attempt {AttemptId} threw instead of reporting an outcome");

    public static async Task RunAsync(IServiceScopeFactory scopes, ClaimedWork work, RunningAttemptRegistry registry, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(scopes);
        ArgumentNullException.ThrowIfNull(work);
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(logger);

        var start = await StartAsync(scopes, work, logger).ConfigureAwait(false);
        if (start is not { } begun)
        {
            return;
        }

        var outcome = await RunCommandAsync(begun.Command, begun.Context, work, registry, logger).ConfigureAwait(false);
        await RecordAsync(scopes, work, outcome, logger).ConfigureAwait(false);
    }

    /// <summary>
    /// The work item becomes <c>processing</c> and the attempt starts running, or the handler finds the claim
    /// is no longer its to run — cancelled, taken back, or already decided — and quietly leaves it alone.
    /// </summary>
    private static async Task<(CommandContext Context, ICommand? Command)?> StartAsync(IServiceScopeFactory scopes, ClaimedWork work, ILogger logger)
    {
        await using var scope = scopes.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var db = services.GetRequiredService<JasonDbContext>();
        var item = await db.WorkItems.WithNavigation().Include(w => w.Attempts)
            .FirstOrDefaultAsync(w => w.Id == work.WorkItemId)
            .ConfigureAwait(false);
        var attempt = item?.Attempts.FirstOrDefault(a => a.Id == work.AttemptId);
        if (item is null || attempt is null || item.Status != WorkItemStatus.Scheduled || attempt.Status != AttemptStatus.Scheduled)
        {
            AttemptVanished(logger, work.AttemptPublicId, null);
            return null;
        }

        var now = services.GetRequiredService<TimeProvider>().GetUtcNow().UtcDateTime;
        WorkItemTransitions.Apply(item, WorkItemStatus.Processing, now);
        attempt.Status = AttemptStatus.Running;
        attempt.StartedAt = now;
        services.GetRequiredService<JournalWriter>().Append(
            db,
            Actors.Dispatcher,
            JournalKinds.WorkItemProcessing,
            campaign: null,
            key: "attempt",
            updated: JsonValue.Create(attempt.Number),
            workItem: item,
            attempt: attempt);

        try
        {
            await db.SaveChangesAsync().ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException)
        {
            AttemptVanished(logger, work.AttemptPublicId, null);
            return null;
        }

        AttemptStarted(logger, work.AttemptPublicId, work.WorkItemPublicId, null);
        var limits = EffectiveLimits.For(item, services.GetRequiredService<IOptionsMonitor<DispatcherOptions>>().CurrentValue);
        return (Context(item, attempt, limits), Pick(services, item.Kind));
    }

    /// <summary>
    /// The command runs outside every scope and is never given the runtime's own cancellation: stopping this
    /// process must not kill a child that is doing real work. Only a kill — a cancellation, or a lease the
    /// runtime gave up on — reaches it, through the token the registry holds.
    /// </summary>
    private static async Task<CommandOutcome> RunCommandAsync(
        ICommand? command,
        CommandContext context,
        ClaimedWork work,
        RunningAttemptRegistry registry,
        ILogger logger)
    {
        var launch = new AttemptLaunchDto(context.EntryCommand, context.WorkDir, null, null);
        if (command is null)
        {
            return new CommandOutcome.LaunchFailed(
                $"No command is registered for kind '{SnakeCaseEnumConverter<WorkItemKind>.Format(context.Kind)}'.",
                launch);
        }

        using var kill = new CancellationTokenSource();
        using var registration = registry.Register(work.AttemptPublicId, kill.Cancel);
        try
        {
            return await command.RunAsync(context with { Kill = kill.Token }, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // A command that throws never started anything the runtime can observe, so it is a failed launch.
            CommandFailed(logger, work.AttemptPublicId, ex);
            return new CommandOutcome.LaunchFailed(ex.Message, launch);
        }
    }

    /// <summary>
    /// What the run meant for the attempt — unless the executor already said so itself through the API, in
    /// which case its own word stands and the handler only records how the process was run.
    /// </summary>
    private static async Task RecordAsync(IServiceScopeFactory scopes, ClaimedWork work, CommandOutcome outcome, ILogger logger)
    {
        await using var scope = scopes.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var db = services.GetRequiredService<JasonDbContext>();
        var attempt = await db.Attempts.Include(a => a.WorkItem)
            .FirstOrDefaultAsync(a => a.Id == work.AttemptId)
            .ConfigureAwait(false);
        if (attempt?.WorkItem is not { } item)
        {
            return;
        }

        if (Launch(outcome) is { } launch)
        {
            attempt.Launch = launch;
        }

        if (attempt.Status == AttemptStatus.Running)
        {
            Decide(services.GetRequiredService<AttemptOutcomes>(), db, item, attempt, outcome);
        }

        try
        {
            await db.SaveChangesAsync().ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException)
        {
            OutcomeNotRecorded(logger, work.AttemptPublicId, null);
        }
    }

    private static void Decide(AttemptOutcomes outcomes, JasonDbContext db, WorkItem item, Attempt attempt, CommandOutcome outcome)
    {
        switch (outcome)
        {
            case CommandOutcome.Exited exited:
                outcomes.Fail(
                    db,
                    item,
                    attempt,
                    AttemptErrors.ExecutorExited,
                    string.Create(CultureInfo.InvariantCulture, $"The executor exited with code {exited.ExitCode} without calling workitem.complete."),
                    exited.StderrTail,
                    details: null,
                    Actors.Dispatcher);
                break;
            case CommandOutcome.LaunchFailed failed:
                outcomes.Fail(db, item, attempt, AttemptErrors.ExecutorLaunchFailed, failed.Message, trace: null, details: null, Actors.Dispatcher);
                break;
            case CommandOutcome.Killed:
                outcomes.Fail(db, item, attempt, AttemptErrors.ExecutorExited, "The executor was stopped.", trace: null, details: null, Actors.Dispatcher);
                break;
            default:
                // Completed: the executor reported through the API and the attempt is already finished there.
                break;
        }
    }

    private static AttemptLaunchDto? Launch(CommandOutcome outcome) => outcome switch
    {
        CommandOutcome.Completed completed => completed.Launch,
        CommandOutcome.Exited exited => exited.Launch,
        CommandOutcome.LaunchFailed failed => failed.Launch,
        CommandOutcome.Killed killed => killed.Launch,
        _ => null,
    };

    /// <summary>The last registration wins, so a test can put its own command in front of the runtime's.</summary>
    private static ICommand? Pick(IServiceProvider services, WorkItemKind kind) =>
        services.GetServices<ICommand>().LastOrDefault(command => command.Kind == kind);

    private static CommandContext Context(WorkItem item, Attempt attempt, EffectiveLimits limits) => new(
        item.PublicId,
        attempt.PublicId,
        attempt.Number,
        item.Campaign!.PublicId,
        item.Contact?.PublicId,
        item.Kind,
        item.Role,
        item.ExecutionProfile,
        attempt.ContextSnapshot.DeepClone().AsObject(),
        item.ResultFormat?.DeepClone(),
        limits,
        WorkItemMapper.Utc(attempt.LockUntil),
        attempt.Launch?.EntryCommand ?? [],
        attempt.Launch?.WorkDir ?? string.Empty,
        CancellationToken.None);
}
