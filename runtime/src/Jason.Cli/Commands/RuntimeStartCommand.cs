using System.Diagnostics;
using System.Text.Json;
using Jason.Cli.Discovery;
using Jason.Cli.Http;
using Jason.Cli.Process;
using Jason.Contracts.Api;
using Jason.Contracts.Discovery;
using Jason.Contracts.Json;

namespace Jason.Cli.Commands;

/// <summary>
/// <c>jason runtime start</c>: make sure a runtime is running and say which one. A runtime that already
/// answers is reported as it stands — starting twice is not an error, and a second process on the same data
/// directory would only lose a fight over the lock. The same goes for one that is running and has not answered:
/// it is named, and nothing is launched over it. Otherwise this launches the service in the background and
/// waits, for no longer than its own wait, for it to publish an endpoint of its own. Nothing is registered
/// until you ask: starting a runtime here is this command's whole effect, and
/// <c>jason runtime autostart enable</c> is a separate act.
/// </summary>
public static class RuntimeStartCommand
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan DefaultPoll = TimeSpan.FromMilliseconds(200);

    /// <summary>
    /// The longest any one question to a runtime may take while this verb waits for it to come up. A runtime on
    /// loopback answers <c>system.info</c> in milliseconds, so two seconds is far above what an answer costs and
    /// still short enough that the loop looks at the child and at its deadline every couple of seconds. Without
    /// it a question is bounded only by the HTTP client's thirty-second timeout, which is the whole wait over
    /// again.
    /// </summary>
    private static readonly TimeSpan PollCeiling = TimeSpan.FromSeconds(2);

    /// <summary>
    /// How long the question put to a descriptor found before anything is launched may take. More generous than
    /// <see cref="PollCeiling"/>: this is the one question whose answer decides whether a runtime is already
    /// there, and a busy one that is slow to answer should not be taken for a gone one.
    /// </summary>
    private static readonly TimeSpan PreLaunchBound = TimeSpan.FromSeconds(5);

    public static async Task<int> RunAsync(CliEnvironment env, bool human, CancellationToken cancellationToken, TimeSpan? timeout = null, TimeSpan? poll = null)
    {
        ArgumentNullException.ThrowIfNull(env);

        var reader = new DescriptorReader(env.Paths);
        var processes = env.Processes ?? RuntimeProcessControl.Instance;
        var descriptor = reader.Read();
        if (descriptor is not null)
        {
            var answer = await ReportAsync(env, human, descriptor, PreLaunchBound, cancellationToken).ConfigureAwait(false);
            if (answer == Answer.Reported)
            {
                return ExitCodes.Success;
            }

            // A descriptor whose runtime took the call and did not answer, and whose process is still running, is
            // not stale: it is a runtime that is busy or stuck, and a second process started beside it would only
            // lose a fight over the data directory's lock. So nothing is launched, and the person is told which
            // process it is. "Still running" is the descriptor's own rule, shared with `uninstall`: a process that
            // started after the descriptor was written has only the runtime's old id. That, a process that has gone,
            // and a port that refuses or answers as someone else all leave a stale descriptor, replaced below.
            if (answer == Answer.PublishedButSilent && Refusal(env, descriptor, processes) is { } refusal)
            {
                return Fail(env, refusal, retryable: true);
            }
        }

        // Whatever the descriptor claimed, nobody is answering for it: the instance it names is the one the new
        // runtime must not be confused with.
        var staleInstance = descriptor?.InstanceId;

        // The log files as they stand before anything is launched, so that what this start wrote can be told from
        // what an earlier one did.
        var earlierLogs = LogFiles(env.Paths);

        // Another copy of this program: what a person typing `jason runtime start` means by "the runtime".
        // Said here rather than inside the seam, because the applier starts something else entirely.
        using var child = processes.Launch(env.Paths, SelfExecutable.Command);

        var waited = timeout ?? DefaultTimeout;
        var elapsed = Stopwatch.StartNew();

        // The last descriptor the new runtime published that never produced an answer, so that running out of
        // time can say that it published one instead of saying it did not.
        RuntimeDescriptor? unanswered = null;
        while (true)
        {
            var published = reader.Read();
            if (published is not null && !string.Equals(published.InstanceId, staleInstance, StringComparison.Ordinal))
            {
                var left = waited - elapsed.Elapsed;
                if (await ReportAsync(env, human, published, CallBound(left), cancellationToken).ConfigureAwait(false) == Answer.Reported)
                {
                    return ExitCodes.Success;
                }

                unanswered = published;
            }

            if (child.HasExited)
            {
                // In its own words where it said any: a runtime that refuses to start says why on the standard
                // error it was started with, and that sentence is the one thing an operator can act on.
                var said = child.Said;
                return Fail(
                    env,
                    $"The runtime process exited with code {child.ExitCode} before publishing an endpoint descriptor. "
                    + (said.Length > 0 ? $"It said: {said} " : string.Empty)
                    + Evidence(env, earlierLogs, exited: true, said: said.Length > 0),
                    retryable: false);
            }

            if (elapsed.Elapsed >= waited)
            {
                // Not "it stopped": nothing here ended it, and it may yet publish -- a first start migrates the
                // database after a backup. Said as what is known, with what to do about each outcome, and never
                // `jason runtime run`, which keeps a runtime in the foreground and would block an agent for good.
                var seconds = RuntimeStopCommand.Seconds(waited);
                var message = unanswered is null
                    ? string.Create(
                        System.Globalization.CultureInfo.InvariantCulture,
                        $"The runtime process {child.Id} did not publish an endpoint descriptor within {seconds} s and is still running: it may still be starting. 'jason runtime status' answers once it has; if it never does, end process {child.Id}. ")
                    : string.Create(
                        System.Globalization.CultureInfo.InvariantCulture,
                        $"The runtime process {child.Id} published an endpoint descriptor at {unanswered.BaseUrl} but did not answer within {seconds} s and is still running. 'jason runtime status' answers once it does; if it never does, end process {child.Id}. ");
                return Fail(env, message + Evidence(env, earlierLogs, exited: false, said: false), retryable: true);
            }

            await Task.Delay(poll ?? DefaultPoll, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Why a runtime that took the call and did not answer is not launched over, or null where it is to be: its
    /// process has gone, or has been given to another program since.
    /// </summary>
    private static string? Refusal(CliEnvironment env, RuntimeDescriptor descriptor, IRuntimeProcessControl processes)
    {
        var seconds = RuntimeStopCommand.Seconds(PreLaunchBound);
        return DescriptorProcess.Of(processes, descriptor.Pid, descriptor.StartedAt) switch
        {
            ProcessHolder.TheRuntime => string.Create(
                System.Globalization.CultureInfo.InvariantCulture,
                $"A runtime (process {descriptor.Pid}) published an endpoint descriptor at {descriptor.BaseUrl} and did not answer within {seconds} s; it is still running. 'jason runtime status' answers once it does; if it never does, end process {descriptor.Pid} and start again."),

            // Running, and not shown to be the runtime or not to be -- a runtime a 0.1.0 logon registration started
            // runs in a session this account may not ask. Not launched over, as the runtime it may be, and not named
            // as a process to end, which would be somebody else's program if it is not.
            ProcessHolder.Unreadable => string.Create(
                System.Globalization.CultureInfo.InvariantCulture,
                $"A runtime published an endpoint descriptor at {descriptor.BaseUrl} and did not answer within {seconds} s, and process {descriptor.Pid}, which it names, is running; but when that process started cannot be read from here, so whether it is that runtime cannot be told. 'jason runtime status' answers once it does; if no runtime is running, it is another program that now has the runtime's old id, and deleting the descriptor at '{env.Paths.DescriptorFile}' lets 'jason runtime start' go on."),

            _ => null,
        };
    }

    /// <summary>What came of asking the runtime a descriptor names who it is.</summary>
    private enum Answer
    {
        /// <summary>The runtime the descriptor names answered as itself, and its answer has been printed.</summary>
        Reported,

        /// <summary>
        /// The call was still unanswered when its bound ran out: something took the connection and said nothing.
        /// Different from <see cref="Other"/>, where the descriptor can be taken as stale.
        /// </summary>
        PublishedButSilent,

        /// <summary>No answer that is a runtime's: refused, a rejected token, another instance, an unreadable body.</summary>
        Other,
    }

    /// <summary>
    /// What a single question may take: what is left of the wait, but never more than <see cref="PollCeiling"/>,
    /// and never a negative time, which a cancellation timer does not accept.
    /// </summary>
    private static TimeSpan CallBound(TimeSpan left) =>
        left <= TimeSpan.Zero ? TimeSpan.Zero : left < PollCeiling ? left : PollCeiling;

    /// <summary>
    /// Asks the runtime the descriptor names who it is, for no longer than <paramref name="bound"/>, and prints
    /// the answer when it is the same instance. Anything else — no answer, a rejected token, a different
    /// instance — means there is nothing running to report, and is not written anywhere: the caller is still
    /// deciding, and its own answer comes later. A call that outlasts its bound is told apart from the rest,
    /// because only that one says a process is holding the endpoint.
    /// </summary>
    private static async Task<Answer> ReportAsync(CliEnvironment env, bool human, RuntimeDescriptor descriptor, TimeSpan bound, CancellationToken cancellationToken)
    {
        using var unspoken = new StringWriter();
        using var own = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        own.CancelAfter(bound);

        RuntimeResponse? response;
        try
        {
            (_, response) = await OperationRunner.SendAsync(env with { Out = unspoken }, Operations.SystemInfo, RequestBody.Empty(), own.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (own.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            // This call's own bound, and not the caller giving up: the caller's cancellation still propagates.
            // The runner treats a cancelled token as the caller's, so the cancellation reaches here as thrown.
            return Answer.PublishedButSilent;
        }

        if (response is null || !response.IsSuccess)
        {
            return Answer.Other;
        }

        var info = Parse(response);
        if (info is null || !string.Equals(info.InstanceId, descriptor.InstanceId, StringComparison.Ordinal))
        {
            return Answer.Other;
        }

        if (human)
        {
            RuntimeStatusCommand.RenderHuman(env.Out, info, descriptor);
        }
        else
        {
            env.Out.WriteLine(response.Body);
        }

        return Answer.Reported;
    }

    private static SystemInfoResponse? Parse(RuntimeResponse response)
    {
        try
        {
            return JsonSerializer.Deserialize<SystemInfoResponse>(response.Body, JasonJson.Options);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// Where to look — and never at log files this start did not write.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A runtime can fail before its logging is configured: preparing the data directory is the first thing it
    /// does, and the log directory is created by that very step. So the message that sent an operator to
    /// <c>logs/</c> was, in the one case they most needed it, sending them to six empty directories on the
    /// first run of a new installation. Say which of the two happened instead. Reading the settings is the next
    /// thing it does, and still before there is a log, so a settings file it cannot read is a cause of the same
    /// kind: it is named beside the data directory, by its path and never by anything in it.
    /// </para>
    /// <para>
    /// Whether it logged is a question about this start, answered against the files as they stood before the
    /// launch: a file that is new, or longer, or written since. An empty directory was the old test, and it held only
    /// on a first run — most hand edits are made on an installation that has logged before, and there a settings file
    /// that stopped the start before it logged anything sent the person to an earlier day's files. Compared rather
    /// than timed: a file's time is the file system's clock at a coarser grain than this process's, and a log a child
    /// wrote a moment after the launch can carry a time from just before it.
    /// </para>
    /// <para>
    /// A child that said why it would not start has answered the question this would otherwise guess at. What it said
    /// is this launch's own — only this child writes the standard error the verb reads — so the causes guessed at below
    /// are not set beside it: another runtime holding the lock is not a settings file that cannot be read, and a guess
    /// next to the real reason only misleads. All that is left to say is whether this start wrote a log.
    /// </para>
    /// </remarks>
    /// <param name="env">The environment the verb runs in.</param>
    /// <param name="earlierLogs">The log files as they stood before the launch (<see cref="LogFiles"/>).</param>
    /// <param name="exited">
    /// Whether the child has exited. Only then is "it stopped before logging started" something this knows: a
    /// child still running when the wait ran out has not stopped at all.
    /// </param>
    /// <param name="said">Whether the child said why it would not start, which this launch's message then carries.</param>
    private static string Evidence(CliEnvironment env, IReadOnlyDictionary<string, LogFile> earlierLogs, bool exited, bool said)
    {
        var logs = LogFiles(env.Paths);
        if (logs.Any(file => !earlierLogs.TryGetValue(file.Key, out var earlier) || earlier != file.Value))
        {
            return $"See the log files under '{env.Paths.LogsDirectory}'.";
        }

        if (said)
        {
            return $"No log file under '{env.Paths.LogsDirectory}' was written by this start.";
        }

        var cause = $"which is what happens when its settings file at '{env.Paths.UserSettingsFile}' cannot be read, "
            + $"or the data directory at '{env.Paths.Root}' cannot be prepared.";
        if (logs.Count == 0)
        {
            return exited
                ? $"There are no log files under '{env.Paths.LogsDirectory}', so it stopped before logging started — " + cause
                : $"There are no log files under '{env.Paths.LogsDirectory}' yet.";
        }

        return exited
            ? $"None of the log files under '{env.Paths.LogsDirectory}' was written by this start, so it stopped before logging started — " + cause
            : $"None of the log files under '{env.Paths.LogsDirectory}' has been written by this start yet.";
    }

    /// <summary>
    /// The files directly under the log directory, by name, with each one's length and when it was last written. Each
    /// is asked about on its own rather than read from the directory's listing, which can lag behind a file another
    /// process still holds open. A directory that does not exist or cannot be listed holds none.
    /// </summary>
    private static Dictionary<string, LogFile> LogFiles(JasonPaths paths)
    {
        var files = new Dictionary<string, LogFile>(StringComparer.Ordinal);
        try
        {
            if (!Directory.Exists(paths.LogsDirectory))
            {
                return files;
            }

            foreach (var path in Directory.EnumerateFiles(paths.LogsDirectory))
            {
                var file = new FileInfo(path);
                if (file.Exists)
                {
                    files[file.Name] = new LogFile(file.Length, file.LastWriteTimeUtc);
                }
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            files.Clear();
        }

        return files;
    }

    /// <summary>What is compared of one log file to tell whether a start wrote to it.</summary>
    private readonly record struct LogFile(long Length, DateTime WrittenAt);

    private static int Fail(CliEnvironment env, string message, bool retryable)
    {
        env.Out.WriteLine(CliErrors.Serialize(CliErrors.RuntimeStartFailed, message, retryable));
        return ExitCodes.ApiError;
    }
}
