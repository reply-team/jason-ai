using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Jason.Cli.Discovery;
using Jason.Cli.Http;
using Jason.Contracts.Api;
using Jason.Contracts.Json;

namespace Jason.Cli;

/// <summary>
/// How one verb presents its answer. With <c>--human</c> the renderer turns the response body into text for
/// people; a renderer that cannot make sense of the body returns null and the raw JSON is printed instead. A renderer
/// that has to ask the runtime something more first is <see cref="RenderHumanAsync"/>, used in place of the other.
/// </summary>
public sealed record RunOptions(bool Human, Func<string, string?>? RenderHuman = null)
{
    public Func<string, CancellationToken, Task<string?>>? RenderHumanAsync { get; init; }

    /// <summary>
    /// With <c>--prompt</c>, the text that briefs an agent session on the item the answer describes, in place of the
    /// answer. It is a rendering like <c>--human</c>: the answer is unchanged. A body it cannot read is refused, exit 1, and
    /// never printed: what was asked for is a brief made of ids, and the body is the item itself.
    /// </summary>
    public Func<string, CancellationToken, Task<string?>>? RenderPromptAsync { get; init; }

    public bool Prompt { get; init; }
}

/// <summary>
/// The single path from a composed request body to an exit code: read the descriptor, POST the operation,
/// print. Every verb goes through it, so the exit-code contract is defined in exactly one place.
/// </summary>
public static class OperationRunner
{
    /// <summary>
    /// 3 when the runtime cannot be reached or rejects the token (the envelope is on stdout), 1 when the API
    /// answers with an error (its body verbatim, or an envelope when the body is not JSON) or <c>--prompt</c> cannot read
    /// the answer (one line on stderr), 0 on success (the body verbatim, or the rendered text).
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
            env.Out.WriteLine(ErrorText(response));
            return ExitCodes.ApiError;
        }

        string? rendered = null;
        if (options.Prompt && options.RenderPromptAsync is not null)
        {
            rendered = await options.RenderPromptAsync(response.Body, cancellationToken).ConfigureAwait(false);
            if (rendered is null)
            {
                env.Error.WriteLine($"--prompt: {Human.PromptRenderers.NoItem}; nothing was printed.");
                return ExitCodes.ApiError;
            }
        }
        else if (options.Human && options.RenderHumanAsync is not null)
        {
            rendered = await options.RenderHumanAsync(response.Body, cancellationToken).ConfigureAwait(false);
        }
        else if (options.Human && options.RenderHuman is not null)
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

        var reader = new DescriptorReader(env.Paths);
        var look = reader.Look();
        if (look.Descriptor is not { } descriptor)
        {
            // A descriptor closed to this account does not open on a retry; a missing one appears once a runtime starts.
            return (Fail(env, CliErrors.NoDescriptor, reader.NoRuntime(look), retryable: !look.Denied), null);
        }

        using var client = new RuntimeClient(descriptor, env.AttemptFromEnvironment(), env.Error, env.HttpHandler, env.Clock, confined: env.Confined());
        RuntimeResponse response;
        try
        {
            response = await client.PostAsync(operation, RequestBody.Serialize(body), cancellationToken).ConfigureAwait(false);
        }
        catch (RuntimeTimeoutException ex)
        {
            // The runtime took the request, so it is reachable: this is the request's own end, not a dead runtime, and a
            // repeat starts the same work over rather than finding it done.
            env.Out.WriteLine(CliErrors.Serialize(
                CliErrors.RequestTimedOut,
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"The runtime at {descriptor.BaseUrl} took '{ex.Operation}' and did not answer within {ex.Budget.TotalSeconds:0} seconds, its budget and a margin; it may still be working. Asking again starts the work over."),
                retryable: false));
            return (ExitCodes.ApiError, null);
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

    /// <summary>
    /// What an error response prints as: the body verbatim where the runtime composed it — its error envelope, with a
    /// code — and otherwise an envelope that says what came back and quotes none of it. A body that is not JSON is the
    /// server's own answer to a failure nothing caught — empty, as often as not — and printed verbatim it gave an agent
    /// an exit code and nothing to read; a body that is JSON in some other shape is another server's, such as one that
    /// holds the port of a stale descriptor, and is no more the runtime's words. A failure on the server's side is worth
    /// trying again; a refusal of the request as it was is not.
    /// </summary>
    public static string ErrorText(RuntimeResponse response)
    {
        ArgumentNullException.ThrowIfNull(response);
        string what;
        try
        {
            var envelope = JsonSerializer.Deserialize<ErrorResponse>(response.Body, JasonJson.Options);
            if (envelope?.Error is { Code.Length: > 0 })
            {
                return response.Body;
            }

            what = "not an error envelope";
        }
        catch (JsonException)
        {
            what = IsJson(response.Body) ? "not an error envelope" : "not JSON";
        }

        return CliErrors.Serialize(
            CliErrors.RuntimeError,
            string.Create(CultureInfo.InvariantCulture, $"The runtime answered {response.StatusCode} with a body that is {what}."),
            retryable: response.StatusCode >= 500);
    }

    private static bool IsJson(string text)
    {
        try
        {
            using var document = JsonDocument.Parse(text);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static int Fail(CliEnvironment env, string code, string message, bool retryable = true)
    {
        env.Out.WriteLine(CliErrors.Serialize(code, message, retryable));
        return ExitCodes.RuntimeUnavailable;
    }
}
