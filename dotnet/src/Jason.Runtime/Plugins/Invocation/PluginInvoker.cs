using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Jason.Contracts.Discovery;
using Jason.Contracts.Events;
using Jason.Contracts.Ids;
using Jason.Contracts.Json;
using Jason.Contracts.Notifications;
using Jason.Contracts.Plugins;
using Jason.Contracts.Subscriptions;
using Jason.Runtime.Configuration;
using Jason.Runtime.Discovery;
using Jason.Runtime.Execution;
using Jason.Runtime.Hosting;
using Jason.Runtime.Plugins.Door;
using Jason.Runtime.Plugins.Manifest;
using Jason.Runtime.Plugins.Registry;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Jason.Runtime.Plugins.Invocation;

/// <summary>
/// Runs one operation of one plugin by starting the shipped executable in plugin-host mode: a fresh process, a
/// minimal environment, a working directory of its own, the invocation on stdin and one outcome on stdout. The
/// runtime never executes a line of the plugin's JavaScript, and it believes the child's answer only after the
/// answer has been re-validated against the protocol.
/// </summary>
/// <remarks>
/// Stateless and reentrant: several invocations may run at once, each with its own child, and nothing is pooled
/// or kept warm. Nothing in production calls this yet — routing provider operations is the next increment. A handler's
/// door is the one thing an invocation holds open, and its state is <see cref="HandlerCapabilities"/>'s, not the invoker's.
/// </remarks>
public sealed partial class PluginInvoker(
    PluginRegistry registry,
    IPluginHostLocator locator,
    LiveSettings<PluginsOptions> options,
    JasonPaths paths,
    RuntimeInfo info,
    TimeProvider clock,
    ILogger<PluginInvoker> logger,
    HandlerCapabilities capabilities,
    ListeningEndpoint endpoint)
{
    /// <summary>How much of the child's stderr travels back with a protocol failure: enough to explain it.</summary>
    public const int StderrTailChars = 4096;

    /// <summary>
    /// The two exit codes of the plugin-host protocol the runtime reads as a refusal. They are spelled here and
    /// in the host itself, because the two sides of a protocol are separate programs by design and the runtime
    /// references neither the host's assembly nor its engine.
    /// </summary>
    private const int UsageExitCode = 2;

    private const int RejectedExitCode = 3;

    private static readonly Action<ILogger, string, string, string, string, string, int, Exception?> Started =
        LoggerMessage.Define<string, string, string, string, string, int>(
            LogLevel.Information,
            new EventId(1, nameof(Started)),
            "Plugin {PluginId} {Version} {Digest} invocation {InvocationId} for {Operation} started as pid {Pid}");

    private static readonly Action<ILogger, string, string, int?, string, long, Exception?> Ended =
        LoggerMessage.Define<string, string, int?, string, long>(
            LogLevel.Information,
            new EventId(2, nameof(Ended)),
            "Plugin invocation {InvocationId} (correlation {CorrelationId}) ended: exit {ExitCode}, {Verdict} in {DurationMs} ms");

    private static readonly Action<ILogger, string, string, string, string, Exception?> Refused =
        LoggerMessage.Define<string, string, string, string>(
            LogLevel.Warning,
            new EventId(3, nameof(Refused)),
            "Plugin invocation {InvocationId} of {PluginId} {Operation} was refused before launch: {Code}");

    /// <summary>
    /// A test's way to hold the envelope write where continuous integration once found it: still in flight when
    /// the invocation let go of the child, although the deadline had cancelled it. Why the cancellation missed is
    /// not known from that run — only that it can, and that a write it missed must still never be closed from
    /// under. Nothing outside a test sets this.
    /// </summary>
    /// <remarks>
    /// An <see cref="AsyncLocal{T}"/>, so it is set only for the flow that set it: a static a test set would
    /// reach every invocation running beside it. It is the second reason this assembly has a friend.
    /// </remarks>
    internal static AsyncLocal<bool> EnvelopeWriteMissesTheCancellation { get; } = new();

    /// <summary>
    /// Invokes <paramref name="request"/> and answers with the outcome, where it came from and how it was run.
    /// <paramref name="kill"/> ends the process tree and yields <see cref="ProtocolCodes.PluginKilled"/>.
    /// </summary>
    public async Task<PluginInvocationResult> InvokeAsync(PluginInvocationRequest request, CancellationToken kill)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Input);

        LoadedPlugin? plugin;
        string snapshotId;
        if (request.Pinned is { } pinned)
        {
            // The caller decided which package runs, so the registry is not consulted at all: a reload landing
            // between that decision and this attempt cannot change what runs or what the provenance says ran.
            plugin = pinned.Plugin;
            snapshotId = pinned.SnapshotId;
        }
        else
        {
            // One snapshot for the whole invocation: a reload that happens while a child runs never changes what
            // that child was told, because the envelope was written from this reference.
            var snapshot = registry.Snapshot;
            plugin = snapshot.Find(request.PluginId);
            snapshotId = snapshot.Id;
        }

        var invocationId = PublicId.New(PluginProtocol.InvocationIdPrefix);
        var provenance = new InvocationProvenance(
            request.PluginId,
            plugin?.Manifest.Version,
            plugin?.Digest,
            PluginProtocol.CurrentVersion,
            ContractVersion(request.Purpose),
            invocationId,
            request.CorrelationId,
            snapshotId);

        // A pin says which package runs while the request's own id is what the provenance records and what every
        // line of the log about this invocation says. Where the two disagree, one of them is a lie about what
        // ran, and there is no reading of the request that makes it true: it is refused before a process exists.
        if (request.Pinned is { } chosen && !string.Equals(request.PluginId, chosen.Plugin.Manifest.Id, StringComparison.Ordinal))
        {
            return Refuse(
                provenance,
                request,
                ProtocolCodes.PluginInvocationRejected,
                $"This invocation names the plugin '{request.PluginId}' while the package pinned to it is '{chosen.Plugin.Manifest.Id}'.");
        }

        if (plugin is null)
        {
            return Refuse(provenance, request, ProtocolCodes.PluginNotLoaded, $"No plugin '{request.PluginId}' is in the active snapshot.");
        }

        if (plugin.Status != PluginStatus.Valid)
        {
            return Refuse(
                provenance,
                request,
                ProtocolCodes.PluginUnavailable,
                $"The plugin '{request.PluginId}' is not usable on this machine: {Codes(plugin.Problems)}.");
        }

        if (Unsuited(request, plugin) is { } unsuited)
        {
            return Refuse(provenance, request, unsuited.Code, unsuited.Message);
        }

        // A malformed correlation id is the caller's bug, not an answer about the plugin: it would travel on
        // argv and into every log line that explains this invocation.
        if (request.CorrelationId is null || !CorrelationPattern().IsMatch(request.CorrelationId))
        {
            throw new ArgumentException(
                $"A correlation id is 1 to {PluginProtocol.MaxCorrelationIdLength} characters of letters, digits, '_', '.', ':' or '-'.",
                nameof(request));
        }

        var inputBytes = Encoding.UTF8.GetByteCount(request.Input.ToJsonString());
        if (inputBytes > PluginProtocol.MaxInputBytes)
        {
            return Refuse(
                provenance,
                request,
                ProtocolCodes.PluginInputTooLarge,
                string.Create(CultureInfo.InvariantCulture, $"An input is at most {PluginProtocol.MaxInputBytes} bytes; this one is {inputBytes}."));
        }

        var bindingBytes = request.Binding is null ? 0 : Encoding.UTF8.GetByteCount(request.Binding.ToJsonString());
        if (bindingBytes > PluginProtocol.MaxBindingBytes)
        {
            return Refuse(
                provenance,
                request,
                ProtocolCodes.PluginBindingTooLarge,
                string.Create(CultureInfo.InvariantCulture, $"A binding is at most {PluginProtocol.MaxBindingBytes} bytes; this one is {bindingBytes}."));
        }

        return await RunAsync(request, plugin, provenance, kill).ConfigureAwait(false);
    }

    /// <summary>
    /// Why this plugin may not answer this request, or null where it may. A provider performs canonical operations it
    /// implements and, where it declares the events contract, reads its accounts' events; a notification plugin performs
    /// the delivery contract, in a version it declares; and a plugin of any kind takes a line of a journal kind it was
    /// granted a subscription to, the kind being the operation word.
    /// </summary>
    private static (string Code, string Message)? Unsuited(PluginInvocationRequest request, LoadedPlugin plugin)
    {
        var kind = plugin.Manifest.Kind.ToString().ToLowerInvariant();
        if (request.Purpose == InvocationPurpose.Subscription)
        {
            // The frozen grant decides, never the plugin's kind: a provider, a notification plugin and a subscriber
            // each take the kinds the person granted them, and nothing else.
            return plugin.Grants.Subscriptions.Contains(request.Operation, StringComparer.Ordinal)
                ? null
                : (ProtocolCodes.PluginOperationUnsupported, $"The plugin '{request.PluginId}' is not granted a subscription to '{request.Operation}'.");
        }

        if (request.Purpose == InvocationPurpose.Events)
        {
            if (!EventsCatalog.Knows(request.Operation))
            {
                return (ProtocolCodes.PluginOperationUnsupported, $"'{request.Operation}' is not an events contract this build publishes.");
            }

            if (plugin.Manifest.Kind != PluginKind.Provider)
            {
                return (ProtocolCodes.PluginKindNotInvocable, $"A plugin of kind '{kind}' is not invoked for a read of events.");
            }

            return plugin.Manifest.ReadsEvents
                ? null
                : (ProtocolCodes.PluginOperationUnsupported, $"The plugin '{request.PluginId}' declares no version of the events contract this build speaks.");
        }

        if (request.Purpose == InvocationPurpose.Delivery)
        {
            if (!DeliveryCatalog.Knows(request.Operation))
            {
                return (ProtocolCodes.PluginOperationUnsupported, $"'{request.Operation}' is not a delivery contract this build publishes.");
            }

            if (plugin.Manifest.Kind != PluginKind.Notification)
            {
                return (ProtocolCodes.PluginKindNotInvocable, $"A plugin of kind '{kind}' is not invoked for a delivery.");
            }

            return plugin.Manifest.Contracts.Operations.Contains(DeliveryContract.CurrentVersion)
                ? null
                : (ProtocolCodes.PluginOperationUnsupported, $"The plugin '{request.PluginId}' declares no version of the delivery contract this build speaks.");
        }

        if (plugin.Manifest.Kind != PluginKind.Provider)
        {
            return (ProtocolCodes.PluginKindNotInvocable, $"A plugin of kind '{kind}' is not invoked for a canonical operation.");
        }

        return plugin.Supports(request.Operation)
            ? null
            : (ProtocolCodes.PluginOperationUnsupported, $"The plugin '{request.PluginId}' does not implement '{request.Operation}'.");
    }

    /// <summary>
    /// The contract version an envelope and its provenance carry: the operation contract's, the delivery contract's, the
    /// events contract's or the subscription contract's.
    /// </summary>
    private static int ContractVersion(InvocationPurpose purpose) => purpose switch
    {
        InvocationPurpose.Delivery => DeliveryContract.CurrentVersion,
        InvocationPurpose.Events => EventsContract.CurrentVersion,
        InvocationPurpose.Subscription => SubscriptionContract.CurrentVersion,
        _ => PluginProtocol.OperationContractVersion,
    };

    private async Task<PluginInvocationResult> RunAsync(
        PluginInvocationRequest request,
        LoadedPlugin plugin,
        InvocationProvenance provenance,
        CancellationToken kill)
    {
        var settings = options.Current;

        // The caller's budget may only lower the plugin's own, never raise it.
        var timeoutMs = (int)Math.Clamp(
            request.Timeout?.TotalMilliseconds ?? plugin.Limits.TimeoutMs,
            1,
            plugin.Limits.TimeoutMs);

        // A door opens only where the caller asked for one and the plugin was granted a family of the Runtime API: the
        // operations it opens are that frozen grant, never the caller's to name. Should the revocation below never run, it
        // dies at the deadline and the kill grace together plus one call's bound: it is minted before the child starts,
        // and the margin covers that start and a call still in flight when the deadline comes. It is revoked when the
        // child's life is over, however it ended.
        var operations = plugin.Grants.ApiOperations;
        var ticket = request.Door is { } asked && operations.Count > 0
            ? capabilities.Mint(
                plugin.Manifest.Id,
                operations,
                asked.LineId,
                clock.GetUtcNow().AddMilliseconds((long)timeoutMs + settings.Invoker.KillGraceMs + PluginProtocol.DoorCallTimeoutMs))
            : null;
        try
        {
            var door = ticket is null
                ? null
                : new InvocationDoor(
                    string.Create(CultureInfo.InvariantCulture, $"http://127.0.0.1:{endpoint.Port}"),
                    ticket.Capability,
                    request.Door!.LineId,
                    [.. operations.Order(StringComparer.Ordinal)],
                    DoorLimits.Standard);
            return await RunChildAsync(request, plugin, provenance, settings, timeoutMs, door, kill).ConfigureAwait(false);
        }
        finally
        {
            if (ticket is not null)
            {
                capabilities.Revoke(ticket);
            }
        }
    }

    private async Task<PluginInvocationResult> RunChildAsync(
        PluginInvocationRequest request,
        LoadedPlugin plugin,
        InvocationProvenance provenance,
        PluginsOptions settings,
        int timeoutMs,
        InvocationDoor? door,
        CancellationToken kill)
    {
        var grants = Grants(plugin);
        var invocation = new PluginInvocation(
            PluginProtocol.CurrentVersion,
            provenance.InvocationId,
            request.CorrelationId,
            new InvocationPlugin(
                plugin.Manifest.Id,
                plugin.Manifest.Version,
                plugin.Manifest.Kind,
                plugin.Root,
                plugin.Digest,
                plugin.Manifest.Entry),
            request.Operation,
            ContractVersion(request.Purpose),
            request.Input,
            new InvocationContext(
                request.Binding,
                request.AttemptId,
                request.AttemptNumber,
                request.WorkItemId,
                request.CampaignId,
                info.RuntimeVersion,
                request.UnsettledSince,
                request.UnsettledReference),
            grants,
            Limits(settings, plugin, timeoutMs),
            door);

        var workDir = paths.PluginInvocationDirectory(provenance.InvocationId);
        Directory.CreateDirectory(workDir);
        var command = PluginHostCommand.Build(locator, plugin.Manifest.Id, request.Operation, request.CorrelationId);
        var startInfo = StartInfo(command, workDir, grants);

        var startedAt = clock.GetUtcNow();
        var watch = Stopwatch.StartNew();
        Process process;
        try
        {
            process = Jason.Contracts.Discovery.ChildProcess.Start(startInfo) ?? throw new IOException($"Starting '{command[0]}' produced no process.");
        }
        catch (Exception exception) when (exception is Win32Exception or IOException or InvalidOperationException)
        {
            Refused(logger, provenance.InvocationId, plugin.Manifest.Id, request.Operation, ProtocolCodes.PluginLaunchFailed, null);
            return new PluginInvocationResult(
                new InvocationOutcome.ProtocolFailure(ProtocolCodes.PluginLaunchFailed, exception.Message, null, null),
                provenance,
                null);
        }

        using (process)
        {
            Started(
                logger,
                plugin.Manifest.Id,
                plugin.Manifest.Version,
                plugin.Digest,
                provenance.InvocationId,
                request.Operation,
                process.Id,
                null);

            var tooLarge = false;
            var stdout = new BoundedCapture(settings.Invoker.OutcomeBytes, () =>
            {
                // A child that floods stdout is ended here rather than read to the end: the outcome it could
                // still write would be past the point where the channel stopped being trustworthy.
                tooLarge = true;
                TryKill(process);
            });

            var stderr = new StderrSink(
                Path.Combine(workDir, "stderr.log"),
                new Redactor([.. (grants.Env?.Variables ?? []).Select(Environment.GetEnvironmentVariable), door?.Capability]),
                settings.Invoker.StderrBytes,
                StderrTailChars);

            using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(kill);
            lifetime.CancelAfter(timeoutMs + settings.Invoker.KillGraceMs);

            // Both pipes are drained from before the envelope is written: a child that talks before it reads
            // would otherwise fill its output buffer and wait forever for a reader that is itself still writing.
            var pumps = new[]
            {
                stdout.DrainAsync(process.StandardOutput.BaseStream, CancellationToken.None),
                stderr.PumpAsync(process.StandardError),
            };

            // The envelope is written alongside the wait, never before it. An envelope larger than a pipe buffer
            // only finishes being written once the child reads it, and a child that reads nothing would
            // otherwise hold this thread here with the deadline below not yet running — nothing able to kill it.
            // The token is what stops the write once the child has ended, whichever way it ended (below).
            var envelope = WriteEnvelopeAsync(process, invocation, lifetime.Token);

            var timedOut = false;
            var killed = false;
            try
            {
                await process.WaitForExitAsync(lifetime.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                TryKill(process);
                killed = kill.IsCancellationRequested;
                timedOut = !killed;

                // The kill is a request, not a guarantee. Waiting for a child it did not end would be a wait
                // with nothing left to end it, so it is given the same grace as everything else that follows.
                await ChildProcess.EndedWithinAsync(process, settings.Invoker.KillGraceMs).ConfigureAwait(false);
            }

            // Whatever is still being written to the child is written to nobody now. A child that exited without
            // reading its envelope ended the wait without cancelling anything, and whatever it left holding its
            // stdin keeps the write from finishing or breaking for as long as it lives — so the write is told to
            // stop here, on every path, rather than only when a deadline or a kill happened to end the wait.
            await lifetime.CancelAsync().ConfigureAwait(false);

            // The wait for the pipes is bounded by the kill grace whichever way the child ended: after a kill —
            // the caller's, the deadline's, or this invoker's own when the child floods — and equally after a
            // child that exited by itself, which says nothing about what it left running on the same handles.
            // What was captured by then is what the invocation is classified from.
            await SettleAsync([.. pumps, envelope], settings.Invoker.KillGraceMs).ConfigureAwait(false);

            // Letting go is said out loud, because an abandoned pump keeps running: what is classified below has
            // to be what was captured by now, and a tail still being appended to while it is read does not merely
            // tear — the read throws, out of this method, stranding the handler slot this attempt is holding.
            stdout.Freeze();
            stderr.Freeze();
            Release(process);

            var exitCode = ExitCodeOf(process);
            var outcome = Classify(invocation, settings, stdout, stderr, tooLarge, killed, timedOut, exitCode, timeoutMs);
            var launch = new InvocationLaunch(command, process.Id, exitCode, startedAt, watch.ElapsedMilliseconds, workDir);
            Ended(
                logger,
                provenance.InvocationId,
                provenance.CorrelationId,
                exitCode,
                Verdict(outcome),
                watch.ElapsedMilliseconds,
                null);

            return new PluginInvocationResult(outcome, provenance, launch);
        }
    }

    /// <summary>
    /// Waits for the tasks that hold the child's pipes, for no longer than the kill grace. By the time this
    /// runs the child has ended, and a child may leave something running that inherited its standard handles:
    /// the read end of a pipe only ends once every writer has let go of it, so waiting for that would hold an
    /// invocation open for as long as the helper lived — with its answer already in hand. Whether the child was
    /// killed or exited by itself decides nothing here, because neither says what it left behind. An abandoned
    /// task is left to finish on its own — its pipe ends when the last writer does — and its failure is
    /// observed there rather than thrown here, where there is no longer anyone to tell.
    /// </summary>
    private static async Task SettleAsync(IReadOnlyList<Task> pipes, int killGraceMs)
    {
        var all = Task.WhenAll(pipes);
        if (await Task.WhenAny(all, Task.Delay(killGraceMs)).ConfigureAwait(false) == all)
        {
            await all.ConfigureAwait(false);
            return;
        }

        foreach (var pipe in pipes)
        {
            Observe(pipe);
        }

        Observe(all);
    }

    private static void Observe(Task abandoned) =>
        _ = abandoned.ContinueWith(
            static finished => _ = finished.Exception,
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted,
            TaskScheduler.Default);

    /// <summary>
    /// Lets go of the child's output pipes. A stream the invoker asked for is not closed when the process is
    /// disposed, because the caller could still be holding it, so an abandoned pump would otherwise keep both the
    /// handle and the file it writes into alive for as long as anything the child left behind keeps the other end
    /// open.
    /// </summary>
    /// <remarks>
    /// Stdin is not closed here: it is the envelope writer's, which closes it when its write ends. Closing it here
    /// raced that write. A writer whose write is still in flight refuses to be disposed and throws, and that threw
    /// out of the invocation with its answer already in hand, to be recorded as a failed launch of a child that
    /// had run. The cancellation above is what makes the write end at once; a write it misses ends when the pipe
    /// is read or breaks, and stdin is closed then.
    /// </remarks>
    private static void Release(Process process)
    {
        Close(process.StandardOutput);
        Close(process.StandardError);
    }

    /// <summary>Closes one of the child's pipes, whose other end may already be gone.</summary>
    private static void Close(IDisposable pipe)
    {
        try
        {
            pipe.Dispose();
        }
        catch (IOException)
        {
            // A pipe whose other end is already gone. There is nothing left to flush it to.
        }
    }

    private static InvocationOutcome Classify(
        PluginInvocation invocation,
        PluginsOptions settings,
        BoundedCapture stdout,
        StderrSink stderr,
        bool tooLarge,
        bool killed,
        bool timedOut,
        int? exitCode,
        int timeoutMs)
    {
        InvocationOutcome Protocol(string code, string message) =>
            new InvocationOutcome.ProtocolFailure(code, message, exitCode, stderr.Tail);

        if (tooLarge)
        {
            return Protocol(
                ProtocolCodes.PluginOutputTooLarge,
                string.Create(CultureInfo.InvariantCulture, $"The plugin host wrote more than {settings.Invoker.OutcomeBytes} bytes to stdout and was ended."));
        }

        if (killed)
        {
            return Protocol(ProtocolCodes.PluginKilled, "The invocation was cancelled and the process tree was ended.");
        }

        if (timedOut)
        {
            return Protocol(
                ProtocolCodes.PluginTimeout,
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"The plugin host did not exit within {timeoutMs + settings.Invoker.KillGraceMs} ms and was ended."));
        }

        if (exitCode is not { } code)
        {
            // Only reachable where a kill did not take, and the two answers above have already covered every
            // way of ending a child. Nothing this one could still write would be this invocation's answer.
            return Protocol(ProtocolCodes.PluginNoOutcome, "The plugin host was ended and had not exited.");
        }

        if (code is RejectedExitCode or UsageExitCode)
        {
            return Protocol(ProtocolCodes.PluginInvocationRejected, RejectionMessage(stderr.Tail));
        }

        if (code != 0)
        {
            return Protocol(
                ProtocolCodes.PluginNoOutcome,
                string.Create(CultureInfo.InvariantCulture, $"The plugin host exited with {code} without writing an outcome."));
        }

        var text = stdout.Text;
        if (string.IsNullOrWhiteSpace(text))
        {
            return Protocol(ProtocolCodes.PluginNoOutcome, "The plugin host exited without writing an outcome.");
        }

        if (!OutcomeValidator.TryValidate(text, invocation.InvocationId, out var outcome, out var problem))
        {
            return Protocol(ProtocolCodes.PluginMalformedOutcome, problem);
        }

        return outcome!.Status == OutcomeStatus.Succeeded
            ? new InvocationOutcome.Succeeded(outcome.Result, outcome.ExternalIds, outcome.Diagnostics, outcome.ExternalIdOrigins)
            : new InvocationOutcome.Failed(outcome.Error!, outcome.Diagnostics);
    }

    private static ProcessStartInfo StartInfo(IReadOnlyList<string> command, string workDir, InvocationGrants grants)
    {
        // Qualified: this namespace has a ChildProcess of its own, the one that waits for a child to end. Both sides of
        // every pipe speak one encoding, the door's rather than a console's.
        var startInfo = Jason.Contracts.Discovery.ChildProcess.StartInfo(command[0], ChildKind.SpeaksToJason);
        startInfo.WorkingDirectory = workDir;

        // One argument at a time, so nothing has to be quoted and no shell ever sees any of it.
        for (var index = 1; index < command.Count; index++)
        {
            startInfo.ArgumentList.Add(command[index]);
        }

        // Built from nothing rather than inherited: the child gets machine configuration and its granted
        // variables, and never learns the data directory, the token or anything else this process holds.
        startInfo.Environment.Clear();
        foreach (var (name, value) in ChildEnvironment.Build(grants.Env?.Variables ?? []))
        {
            startInfo.Environment[name] = value;
        }

        return startInfo;
    }

    private static InvocationGrants Grants(LoadedPlugin plugin)
    {
        var capabilities = plugin.Manifest.Capabilities;
        var executables = plugin.Executables
            .Where(executable => executable.Path is not null && plugin.Grants.Exec.Contains(executable.Name, StringComparer.Ordinal))
            .Select(executable => new ExecutableGrant(
                executable.Name,
                executable.Path!,
                executable.Launch.Count == 0 ? null : executable.Launch))
            .ToList();

        // A grant is absent, not empty, when the manifest never asked for the capability or the user granted none
        // of what it asked for: the child then refuses a call as "not granted", which is the answer a plugin
        // author can act on. An empty list would make it say "not on your list" about a list that does not exist.
        return new InvocationGrants(
            capabilities.Exec is null || executables.Count == 0 ? null : new ExecGrants(executables),
            capabilities.Http is null || plugin.Grants.Http.Count == 0 ? null : new HttpGrants(plugin.Grants.Http),
            capabilities.Env is null || plugin.Grants.Env.Count == 0 ? null : new EnvGrants(plugin.Grants.Env));
    }

    private static InvocationLimits Limits(PluginsOptions settings, LoadedPlugin plugin, int timeoutMs) =>
        new(
            timeoutMs,
            (long)plugin.Limits.MemoryMb * 1024 * 1024,
            settings.Limits.MaxStatements,
            settings.Limits.MaxRecursion,
            new ExecLimits(settings.Exec.OutputBytes, settings.Exec.MaxCalls),
            new HttpLimits(settings.Http.ResponseBytes, settings.Http.RequestBytes, settings.Http.MaxCalls, settings.Http.TimeoutMs),
            new LogLimits(settings.Invoker.LogLineBytes, settings.Invoker.StderrBytes));

    /// <summary>One JSON object, then end of file: the child reads until the stream ends rather than guessing.</summary>
    /// <remarks>
    /// This owns stdin from the first line to the last, and closes it however the write ends: that is the end of
    /// file the child reads to, and, after a write that was stopped or failed, the pipe let go of. Taken once, here,
    /// because the invocation may have finished with the process before this has finished with its pipe.
    /// </remarks>
    private static async Task WriteEnvelopeAsync(Process process, PluginInvocation invocation, CancellationToken lifetime)
    {
        var stdin = process.StandardInput;
        try
        {
            var json = JsonSerializer.Serialize(invocation, JasonJson.Options);
            var stop = EnvelopeWriteMissesTheCancellation.Value ? CancellationToken.None : lifetime;
            await stdin.WriteAsync(json.AsMemory(), stop).ConfigureAwait(false);
        }
        catch (IOException)
        {
            // The child exited before reading its envelope. That is its own answer, delivered as an exit code.
        }
        catch (OperationCanceledException)
        {
            // The child has ended, by itself or by the budget or a kill; there is nothing left to tell it.
        }
        finally
        {
            Close(stdin);
        }
    }

    private PluginInvocationResult Refuse(InvocationProvenance provenance, PluginInvocationRequest request, string code, string message)
    {
        Refused(logger, provenance.InvocationId, request.PluginId, request.Operation, code, null);
        return new PluginInvocationResult(new InvocationOutcome.ProtocolFailure(code, message, null, null), provenance, null);
    }

    /// <summary>
    /// What the host said when it refused, taken from the last line of stderr that is one of its own diagnostics.
    /// Stderr is JSON Lines by contract, but a host that broke unexpectedly writes plain text instead, so a line
    /// that does not parse is simply not the one being looked for.
    /// </summary>
    private static string RejectionMessage(string tail)
    {
        foreach (var line in tail.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Reverse())
        {
            JsonNode? parsed;
            try
            {
                parsed = JsonNode.Parse(line);
            }
            catch (JsonException)
            {
                continue;
            }

            if (parsed is not JsonObject diagnostic || Text(diagnostic, "message") is not { } message)
            {
                continue;
            }

            var code = diagnostic["data"] is JsonObject data ? Text(data, "code") : null;
            return code is null
                ? $"The plugin host refused the invocation: {message}."
                : $"The plugin host refused the invocation: {message} ({code}).";
        }

        return "The plugin host refused the invocation before running any of the plugin's code.";
    }

    /// <summary>
    /// One string field of an object nothing in this runtime wrote. A field of another type, or of another
    /// shape entirely, reads as absent rather than throwing: the child's stderr is text the child chose, and a
    /// refusal still has to come back as a refusal.
    /// </summary>
    private static string? Text(JsonObject parent, string name) =>
        parent[name] is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;

    private static string Verdict(InvocationOutcome outcome) => outcome switch
    {
        InvocationOutcome.Succeeded => "succeeded",
        InvocationOutcome.Failed failed => $"failed {failed.Error.Class.ToString().ToLowerInvariant()}/{failed.Error.Code}",
        InvocationOutcome.ProtocolFailure failure =>
            $"protocol_failure {OutcomeClassification.ClassOf(failure.Code).ToString().ToLowerInvariant()}/{failure.Code}",
        _ => "unknown",
    };

    private static string Codes(IReadOnlyList<ManifestProblem> problems) =>
        string.Join(", ", problems.Select(problem => problem.Code).Distinct(StringComparer.Ordinal));

    /// <summary>The child's exit code, or nothing when it is still running: a kill it survived has none yet.</summary>
    private static int? ExitCodeOf(Process process)
    {
        try
        {
            return process.ExitCode;
        }
        catch (InvalidOperationException)
        {
            return null;
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
            // Killing a whole tree is not available here; the budget's own end is what stops it instead.
        }
        catch (AggregateException)
        {
            // Part of the tree could not be ended. The process itself is the one that matters, and it was.
        }
    }

    [GeneratedRegex(@"^[A-Za-z0-9_.:-]{1,128}\z")]
    private static partial Regex CorrelationPattern();
}
