using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Jason.Contracts.Discovery;
using Jason.Contracts.Ids;
using Jason.Contracts.Json;
using Jason.Contracts.Plugins;
using Jason.Runtime.Configuration;
using Jason.Runtime.Discovery;
using Jason.Runtime.Execution;
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
/// or kept warm. Nothing in production calls this yet — routing provider operations is the next increment.
/// </remarks>
public sealed partial class PluginInvoker(
    PluginRegistry registry,
    IPluginHostLocator locator,
    IOptionsMonitor<PluginsOptions> options,
    JasonPaths paths,
    RuntimeInfo info,
    TimeProvider clock,
    ILogger<PluginInvoker> logger)
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

    /// <summary>Both sides of every pipe speak one encoding, chosen here rather than inherited from a console.</summary>
    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);

    private static readonly Action<ILogger, string, string, string, string, string, int, Exception?> Started =
        LoggerMessage.Define<string, string, string, string, string, int>(
            LogLevel.Information,
            new EventId(1, nameof(Started)),
            "Plugin {PluginId} {Version} {Digest} invocation {InvocationId} for {Operation} started as pid {Pid}");

    private static readonly Action<ILogger, string, string, int, string, long, Exception?> Ended =
        LoggerMessage.Define<string, string, int, string, long>(
            LogLevel.Information,
            new EventId(2, nameof(Ended)),
            "Plugin invocation {InvocationId} (correlation {CorrelationId}) ended: exit {ExitCode}, {Verdict} in {DurationMs} ms");

    private static readonly Action<ILogger, string, string, string, string, Exception?> Refused =
        LoggerMessage.Define<string, string, string, string>(
            LogLevel.Warning,
            new EventId(3, nameof(Refused)),
            "Plugin invocation {InvocationId} of {PluginId} {Operation} was refused before launch: {Code}");

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
            PluginProtocol.OperationContractVersion,
            invocationId,
            request.CorrelationId,
            snapshotId);

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

        if (plugin.Manifest.Kind != PluginKind.Provider)
        {
            return Refuse(
                provenance,
                request,
                ProtocolCodes.PluginKindNotInvocable,
                $"A plugin of kind '{plugin.Manifest.Kind.ToString().ToLowerInvariant()}' is not invoked for an operation.");
        }

        if (!plugin.Supports(request.Operation))
        {
            return Refuse(
                provenance,
                request,
                ProtocolCodes.PluginOperationUnsupported,
                $"The plugin '{request.PluginId}' does not implement '{request.Operation}'.");
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

    private async Task<PluginInvocationResult> RunAsync(
        PluginInvocationRequest request,
        LoadedPlugin plugin,
        InvocationProvenance provenance,
        CancellationToken kill)
    {
        var settings = options.CurrentValue;

        // The caller's budget may only lower the plugin's own, never raise it.
        var timeoutMs = (int)Math.Clamp(
            request.Timeout?.TotalMilliseconds ?? plugin.Limits.TimeoutMs,
            1,
            plugin.Limits.TimeoutMs);

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
            PluginProtocol.OperationContractVersion,
            request.Input,
            new InvocationContext(
                request.Binding,
                request.AttemptId,
                request.AttemptNumber,
                request.WorkItemId,
                request.CampaignId,
                info.RuntimeVersion),
            grants,
            Limits(settings, plugin, timeoutMs));

        var workDir = paths.PluginInvocationDirectory(provenance.InvocationId);
        Directory.CreateDirectory(workDir);
        var command = PluginHostCommand.Build(locator, plugin.Manifest.Id, request.Operation, request.CorrelationId);
        var startInfo = StartInfo(command, workDir, grants);

        var startedAt = clock.GetUtcNow();
        var watch = Stopwatch.StartNew();
        Process process;
        try
        {
            process = Process.Start(startInfo) ?? throw new IOException($"Starting '{command[0]}' produced no process.");
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
                new Redactor((grants.Env?.Variables ?? []).Select(Environment.GetEnvironmentVariable)),
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
            // The token ends a write that has not started; the kill ends one already in flight, by breaking the
            // pipe the write is blocked on.
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
                await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
                killed = kill.IsCancellationRequested;
                timedOut = !killed;
            }

            await Task.WhenAll(pumps).ConfigureAwait(false);
            await envelope.ConfigureAwait(false);

            var exitCode = process.ExitCode;
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

    private static InvocationOutcome Classify(
        PluginInvocation invocation,
        PluginsOptions settings,
        BoundedCapture stdout,
        StderrSink stderr,
        bool tooLarge,
        bool killed,
        bool timedOut,
        int exitCode,
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

        if (exitCode is RejectedExitCode or UsageExitCode)
        {
            return Protocol(ProtocolCodes.PluginInvocationRejected, RejectionMessage(stderr.Tail));
        }

        if (exitCode != 0)
        {
            return Protocol(
                ProtocolCodes.PluginNoOutcome,
                string.Create(CultureInfo.InvariantCulture, $"The plugin host exited with {exitCode} without writing an outcome."));
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
            ? new InvocationOutcome.Succeeded(outcome.Result, outcome.ExternalIds, outcome.Diagnostics)
            : new InvocationOutcome.Failed(outcome.Error!, outcome.Diagnostics);
    }

    private static ProcessStartInfo StartInfo(IReadOnlyList<string> command, string workDir, InvocationGrants grants)
    {
        var startInfo = new ProcessStartInfo(command[0])
        {
            WorkingDirectory = workDir,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardInputEncoding = Utf8,
            StandardOutputEncoding = Utf8,
            StandardErrorEncoding = Utf8,
        };

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
            .Select(executable => new ExecutableGrant(executable.Name, executable.Path!))
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
    private static async Task WriteEnvelopeAsync(Process process, PluginInvocation invocation, CancellationToken lifetime)
    {
        try
        {
            var json = JsonSerializer.Serialize(invocation, JasonJson.Options);
            await process.StandardInput.WriteAsync(json.AsMemory(), lifetime).ConfigureAwait(false);
            process.StandardInput.Close();
        }
        catch (IOException)
        {
            // The child exited before reading its envelope. That is its own answer, delivered as an exit code.
        }
        catch (ObjectDisposedException)
        {
            // The same story, seen from the other side of an already-closed pipe.
        }
        catch (OperationCanceledException)
        {
            // The invocation's lifetime ended first. The child is being killed; there is nothing left to tell it.
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

            if (parsed is not JsonObject diagnostic || diagnostic["message"]?.GetValue<string>() is not { } message)
            {
                continue;
            }

            var code = diagnostic["data"]?["code"]?.GetValue<string>();
            return code is null
                ? $"The plugin host refused the invocation: {message}."
                : $"The plugin host refused the invocation: {message} ({code}).";
        }

        return "The plugin host refused the invocation before running any of the plugin's code.";
    }

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

    [GeneratedRegex(@"^[A-Za-z0-9_.:-]{1,128}$")]
    private static partial Regex CorrelationPattern();
}
