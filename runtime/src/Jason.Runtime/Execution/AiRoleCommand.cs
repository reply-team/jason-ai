using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Jason.Contracts.Api;
using Jason.Contracts.Discovery;
using Jason.Contracts.Execution;
using Jason.Contracts.Json;
using Jason.Runtime.Configuration;
using Jason.Runtime.Execution.Hosts;
using Microsoft.Extensions.Logging;

namespace Jason.Runtime.Execution;

/// <summary>
/// Runs one attempt of an <c>ai_role</c> work item by starting the role's entry command as a child process.
/// It is deliberately generic: the runtime knows how to launch a program, hand it one JSON envelope on stdin
/// and watch it end, and nothing more. Which agent framework is behind that command is the user's business.
/// <para>
/// The envelope never carries the capability token — the child reads the descriptor file named in it — and
/// neither does the environment, so a host that dumps its own environment leaks nothing. What the child writes
/// is kept in its work directory with the token redacted line by line, and the last of its stderr travels back
/// as the trace of a failed attempt.
/// </para>
/// </summary>
public sealed class AiRoleCommand(
    JasonPaths paths,
    TokenRedactor redactor,
    LiveSettings<RolesOptions> roles,
    ILogger<AiRoleCommand> logger) : ICommand
{
    /// <summary>How much of the child's stderr is worth keeping on the attempt: enough to explain, not enough to store a log.</summary>
    public const int StderrTailBytes = 4096;

    public WorkItemKind Kind => WorkItemKind.AiRole;

    public async Task<CommandOutcome> RunAsync(CommandContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        Directory.CreateDirectory(context.WorkDir);
        var launch = new AttemptLaunchDto(context.EntryCommand, context.WorkDir, null, null);

        if (context.EntryCommand.Count == 0)
        {
            return new CommandOutcome.LaunchFailed("The role has no entry command.", launch);
        }

        // What the agent will find where it runs: what it may not do, and how to do the job. Both are written
        // before the child exists, and a role whose skill a host would silently ignore ends the attempt here.
        var prepared = WorkDirectory.Prepare(
            context.WorkDir,
            context.Deny ?? [],
            context.Role,
            paths.RoleSkillsDirectory,
            roles.Current.MaxSkillBytes);
        if (prepared.RefusalCode is { } refusal)
        {
            return new CommandOutcome.LaunchFailed(prepared.Message!, launch, refusal);
        }

        if (prepared.Message is { } note)
        {
            logger.LogWarning("Attempt {AttemptId} of work item {WorkItemId}: {Note}", context.AttemptId, context.WorkItemId, note);
        }

        var startInfo = new ProcessStartInfo(context.EntryCommand[0])
        {
            WorkingDirectory = context.WorkDir,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        // The first element is the program; the rest are arguments, passed one by one so nothing has to be
        // quoted and no shell ever sees them.
        for (var index = 1; index < context.EntryCommand.Count; index++)
        {
            startInfo.ArgumentList.Add(context.EntryCommand[index]);
        }

        // The child works against the same data directory this runtime owns. Nothing else is added and nothing
        // is taken away — least of all the token, which belongs in the descriptor file and nowhere else.
        startInfo.Environment[JasonPaths.DataDirectoryVariable] = paths.Root;

        Process process;
        try
        {
            process = Process.Start(startInfo)
                ?? throw new IOException($"Starting '{context.EntryCommand[0]}' produced no process.");
        }
        catch (Win32Exception ex)
        {
            return new CommandOutcome.LaunchFailed(ex.Message, launch);
        }
        catch (IOException ex)
        {
            return new CommandOutcome.LaunchFailed(ex.Message, launch);
        }
        catch (InvalidOperationException ex)
        {
            return new CommandOutcome.LaunchFailed(ex.Message, launch);
        }

        using (process)
        {
            launch = launch with { Pid = process.Id };
            logger.LogInformation(
                "Attempt {AttemptId} of work item {WorkItemId} started as pid {Pid}",
                context.AttemptId,
                context.WorkItemId,
                process.Id);

            // Registered before anything can block: a kill that arrives while the envelope is still being
            // written must be honoured just as promptly as one that arrives an hour into the run.
            using var killer = context.Kill.Register(() => TryKill(process));

            // The pipes are drained from the first moment. A child that talks before it reads would otherwise
            // fill its output buffer and wait forever for a reader that is itself waiting to finish writing.
            var tail = new StringBuilder();
            var pumps = new[]
            {
                OutputPump.PumpAsync(process.StandardOutput, Path.Combine(context.WorkDir, "stdout.log"), redactor, null, StderrTailBytes),
                OutputPump.PumpAsync(process.StandardError, Path.Combine(context.WorkDir, "stderr.log"), redactor, tail, StderrTailBytes),
            };

            await WriteEnvelopeAsync(process, context).ConfigureAwait(false);

            // Deliberately not the runtime's own token: a shutdown drains and lets children finish, and a
            // survivor completes its attempt against the next runtime instance rather than dying with this one.
            await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
            await Task.WhenAll(pumps).ConfigureAwait(false);

            launch = launch with { ExitCode = process.ExitCode };
            logger.LogInformation(
                "Attempt {AttemptId} of work item {WorkItemId} ended with exit code {ExitCode}",
                context.AttemptId,
                context.WorkItemId,
                process.ExitCode);

            return context.Kill.IsCancellationRequested
                ? new CommandOutcome.Killed(launch)
                : new CommandOutcome.Exited(process.ExitCode, tail.ToString(), launch);
        }
    }

    /// <summary>
    /// One JSON object, then end of file. Closing stdin is part of the contract: a host reads until the stream
    /// ends rather than guessing where the envelope stops.
    /// </summary>
    private async Task WriteEnvelopeAsync(Process process, CommandContext context)
    {
        var envelope = new LaunchEnvelope(
            LaunchEnvelope.CurrentVersion,
            context.AttemptId,
            context.AttemptNumber,
            context.WorkItemId,
            context.CampaignId,
            context.ContactId,
            context.Kind,
            context.Role,
            context.ExecutionProfile,
            context.ContextSnapshot,
            context.ResultFormat,
            context.Limits.TimeoutSeconds,
            context.Limits.HeartbeatSeconds,
            context.LockUntil,
            context.WorkDir,
            new RuntimeLocation(paths.DescriptorFile, ApiVersion.Current));

        try
        {
            await process.StandardInput.WriteAsync(JsonSerializer.Serialize(envelope, JasonJson.Options)).ConfigureAwait(false);
            process.StandardInput.Close();
        }
        catch (IOException)
        {
            // The child exited before reading its envelope. That is its own answer, delivered as an exit code.
        }
        catch (ObjectDisposedException)
        {
            // Same story, seen from the other side of an already-closed pipe.
        }
    }

    private static void TryKill(Process process)
    {
        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException)
        {
            // Already gone between the signal and the kill; there is nothing left to end.
        }
        catch (Win32Exception)
        {
            // The operating system refused the kill — the process is exiting, or no longer ours to end.
        }
        catch (NotSupportedException)
        {
            // Killing a whole tree is not available here; the attempt will end on its lease instead.
        }
        catch (AggregateException)
        {
            // Part of the tree could not be ended. The process itself is the one that matters and is handled above.
        }
    }
}
