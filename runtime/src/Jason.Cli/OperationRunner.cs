using System.Text.Json.Nodes;
using Jason.Cli.Discovery;
using Jason.Cli.Http;

namespace Jason.Cli;

/// <summary>
/// How one verb presents its answer. With <c>--human</c> the renderer turns the response body into text for
/// people; a renderer that cannot make sense of the body returns null and the raw JSON is printed instead.
/// </summary>
public sealed record RunOptions(bool Human, Func<string, string?>? RenderHuman = null);

/// <summary>
/// The single path from a composed request body to an exit code: read the descriptor, POST the operation,
/// print. Every verb goes through it, so the exit-code contract is defined in exactly one place.
/// </summary>
public static class OperationRunner
{
    /// <summary>
    /// 3 when the runtime cannot be reached or rejects the token (the envelope is on stdout), 1 when the API
    /// answers with an error (its body verbatim), 0 on success (the body verbatim, or the rendered text).
    /// </summary>
    public static async Task<int> RunAsync(CliEnvironment env, string operation, JsonObject body, RunOptions options, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(env);
        ArgumentNullException.ThrowIfNull(options);

        var (exitCode, response) = await SendAsync(env, operation, body, cancellationToken).ConfigureAwait(false);
        if (response is null)
        {
            return exitCode;
        }

        if (!response.IsSuccess)
        {
            env.Out.WriteLine(response.Body);
            return ExitCodes.ApiError;
        }

        string? rendered = null;
        if (options.Human && options.RenderHuman is not null)
        {
            rendered = options.RenderHuman(response.Body);
        }

        env.Out.WriteLine(rendered ?? response.Body);
        return ExitCodes.Success;
    }

    /// <summary>
    /// The same call without the printing of a successful or failed response, for verbs that do more with it
    /// than show it. A runtime that cannot be reached is still reported here — exit code 3, envelope on
    /// stdout, no response — because that answer is the same for every caller.
    /// </summary>
    public static async Task<(int ExitCode, RuntimeResponse? Response)> SendAsync(CliEnvironment env, string operation, JsonObject body, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(env);
        ArgumentNullException.ThrowIfNull(body);

        var descriptor = new DescriptorReader(env.Paths).Read();
        if (descriptor is null)
        {
            return (Fail(env, CliErrors.NoDescriptor, $"No runtime endpoint descriptor at '{env.Paths.DescriptorFile}'. Is the runtime running?"), null);
        }

        using var client = new RuntimeClient(descriptor, env.HttpHandler);
        RuntimeResponse response;
        try
        {
            response = await client.PostAsync(operation, RequestBody.Serialize(body), cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            return (Fail(env, CliErrors.RuntimeUnreachable, $"The runtime at {descriptor.BaseUrl} did not answer ({ex.Message}). The descriptor may be stale."), null);
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return (Fail(env, CliErrors.RuntimeUnreachable, $"The runtime at {descriptor.BaseUrl} did not answer in time."), null);
        }

        if (response.StatusCode == 401)
        {
            return (Fail(env, CliErrors.Unauthorized, "The runtime rejected the capability token from the descriptor; the descriptor is stale."), null);
        }

        return (ExitCodes.Success, response);
    }

    private static int Fail(CliEnvironment env, string code, string message)
    {
        env.Out.WriteLine(CliErrors.Serialize(code, message, retryable: true));
        return ExitCodes.RuntimeUnavailable;
    }
}
