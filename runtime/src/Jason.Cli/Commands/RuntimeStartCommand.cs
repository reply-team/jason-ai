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
/// directory would only lose a fight over the lock. Otherwise this launches the service in the background and
/// waits for it to publish an endpoint of its own. Nothing is registered until you ask: starting a
/// runtime here is this command's whole effect, and <c>jason runtime autostart enable</c> is a separate act.
/// </summary>
public static class RuntimeStartCommand
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan DefaultPoll = TimeSpan.FromMilliseconds(200);

    public static async Task<int> RunAsync(CliEnvironment env, bool human, CancellationToken cancellationToken, TimeSpan? timeout = null, TimeSpan? poll = null)
    {
        ArgumentNullException.ThrowIfNull(env);

        var reader = new DescriptorReader(env.Paths);
        var descriptor = reader.Read();
        if (descriptor is not null && await ReportAsync(env, human, descriptor, cancellationToken).ConfigureAwait(false))
        {
            return ExitCodes.Success;
        }

        // Whatever the descriptor claimed, nobody is answering for it: the instance it names is the one the new
        // runtime must not be confused with.
        var staleInstance = descriptor?.InstanceId;
        var processes = env.Processes ?? RuntimeProcessControl.Instance;
        // Another copy of this program: what a person typing `jason runtime start` means by "the runtime".
        // Said here rather than inside the seam, because the applier starts something else entirely.
        using var child = processes.Launch(env.Paths, SelfExecutable.Command);

        var waited = timeout ?? DefaultTimeout;
        var elapsed = Stopwatch.StartNew();
        while (true)
        {
            var published = reader.Read();
            if (published is not null
                && !string.Equals(published.InstanceId, staleInstance, StringComparison.Ordinal)
                && await ReportAsync(env, human, published, cancellationToken).ConfigureAwait(false))
            {
                return ExitCodes.Success;
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
                    + Evidence(env, exited: true),
                    retryable: false);
            }

            if (elapsed.Elapsed >= waited)
            {
                // Not "it stopped": nothing here ended it, and it may yet publish -- a first start migrates the
                // database after a backup. Said as what is known, with what to do about each outcome, and never
                // `jason runtime run`, which keeps a runtime in the foreground and would block an agent for good.
                return Fail(
                    env,
                    string.Create(
                        System.Globalization.CultureInfo.InvariantCulture,
                        $"The runtime process {child.Id} did not publish an endpoint descriptor within {RuntimeStopCommand.Seconds(waited)} s and is still running: it may still be starting. 'jason runtime status' answers once it has; if it never does, end process {child.Id}. ")
                    + Evidence(env, exited: false),
                    retryable: true);
            }

            await Task.Delay(poll ?? DefaultPoll, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Asks the runtime the descriptor names who it is, and prints the answer when it is the same instance.
    /// Anything else — no answer, a rejected token, a different instance — means there is nothing running to
    /// report, and is not written anywhere: the caller is still deciding, and its own answer comes later.
    /// </summary>
    private static async Task<bool> ReportAsync(CliEnvironment env, bool human, RuntimeDescriptor descriptor, CancellationToken cancellationToken)
    {
        using var unspoken = new StringWriter();
        var (_, response) = await OperationRunner.SendAsync(env with { Out = unspoken }, Operations.SystemInfo, RequestBody.Empty(), cancellationToken).ConfigureAwait(false);
        if (response is null || !response.IsSuccess)
        {
            return false;
        }

        var info = Parse(response);
        if (info is null || !string.Equals(info.InstanceId, descriptor.InstanceId, StringComparison.Ordinal))
        {
            return false;
        }

        if (human)
        {
            RuntimeStatusCommand.RenderHuman(env.Out, info, descriptor);
        }
        else
        {
            env.Out.WriteLine(response.Body);
        }

        return true;
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
    /// Where to look — and never a directory with nothing in it.
    /// </summary>
    /// <remarks>
    /// A runtime can fail before its logging is configured: preparing the data directory is the first thing it
    /// does, and the log directory is created by that very step. So the message that sent an operator to
    /// <c>logs/</c> was, in the one case they most needed it, sending them to six empty directories on the
    /// first run of a new installation. Say which of the two happened instead.
    /// </remarks>
    /// <param name="env">The environment the verb runs in.</param>
    /// <param name="exited">
    /// Whether the child has exited. Only then is "it stopped before logging started" something this knows: a
    /// child still running when the wait ran out has not stopped at all.
    /// </param>
    private static string Evidence(CliEnvironment env, bool exited)
    {
        bool any;
        try
        {
            any = Directory.Exists(env.Paths.LogsDirectory) && Directory.EnumerateFiles(env.Paths.LogsDirectory).Any();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            any = false;
        }

        if (any)
        {
            return $"See the log files under '{env.Paths.LogsDirectory}'.";
        }

        return exited
            ? $"There are no log files under '{env.Paths.LogsDirectory}', so it stopped before logging started — "
                + $"which is what happens when the data directory at '{env.Paths.Root}' cannot be prepared."
            : $"There are no log files under '{env.Paths.LogsDirectory}' yet.";
    }

    private static int Fail(CliEnvironment env, string message, bool retryable)
    {
        env.Out.WriteLine(CliErrors.Serialize(CliErrors.RuntimeStartFailed, message, retryable));
        return ExitCodes.ApiError;
    }
}
