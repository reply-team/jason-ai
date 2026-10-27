namespace Jason.Cli.Commands;

/// <summary>
/// <c>jason runtime restart</c>: stop, then start. "Nothing was running" is not a failure of a restart, so
/// that case is swallowed and only the start speaks; anything the stop genuinely failed on is reported as it
/// stands and no new runtime is launched, because the old one is still there.
/// </summary>
/// <remarks>
/// An update the start would refuse to start around is read before the stop: stopped first, the runtime was gone and the
/// start's refusal left nothing running. It is refused with the start's own envelope, and nothing is stopped.
/// </remarks>
public static class RuntimeRestartCommand
{
    public static async Task<int> RunAsync(CliEnvironment env, bool human, CancellationToken cancellationToken, TimeSpan? timeout = null, TimeSpan? poll = null)
    {
        ArgumentNullException.ThrowIfNull(env);

        if (Contracts.Update.UnfinishedUpdate.Read(env.Paths) is { Replacing: true } unfinished)
        {
            var (code, message) = unfinished.Refusal("Nothing was stopped or started.");
            env.Out.WriteLine(CliErrors.Serialize(code, message, retryable: false));
            return ExitCodes.ApiError;
        }

        using var stopOutput = new StringWriter();
        var stopped = await RuntimeStopCommand.RunAsync(env with { Out = stopOutput }, human, cancellationToken, timeout, poll).ConfigureAwait(false);
        if (stopped is not ExitCodes.Success and not ExitCodes.RuntimeUnavailable)
        {
            env.Out.Write(stopOutput.ToString());
            return stopped;
        }

        return await RuntimeStartCommand.RunAsync(env, human, cancellationToken, timeout, poll).ConfigureAwait(false);
    }
}
