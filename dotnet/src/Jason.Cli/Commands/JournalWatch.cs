using System.CommandLine;
using System.Net.WebSockets;
using System.Text;
using Jason.Cli.Discovery;
using Jason.Cli.Http;
using Jason.Cli.Human;
using Jason.Contracts.Api;

namespace Jason.Cli.Commands;

/// <summary>
/// <c>jason journal watch</c>: the journal's stream, <c>GET /v1/journal.stream</c>, one to one. It holds the stream
/// open until it is interrupted and prints each line as it arrives, as <c>journal.list</c> answers a line; it asks the
/// runtime and changes nothing.
/// </summary>
public static class JournalWatch
{
    /// <summary>How long an interrupted watch waits for the runtime to answer its close before it lets go.</summary>
    private static readonly TimeSpan CloseWait = TimeSpan.FromSeconds(2);

    public static Command Command(CliEnvironment env, Option<string?> actor)
    {
        ArgumentNullException.ThrowIfNull(env);
        ArgumentNullException.ThrowIfNull(actor);

        var command = new Command(
            "watch",
            "Print each chronicle entry as it is written, oldest first, until interrupted. Exit 0 on an interrupt, 1 when the runtime refuses the request, an --after that names no line among them, 3 when the runtime cannot be reached, refuses the token or ends the stream.");
        var after = new Option<string?>(Options.After)
        {
            HelpName = "id",
            Description = "The id of the last line you have (jrn_...): the watch starts after it, and may print a line you already have once more, never miss one. Without it, the entries written from now on.",
        };
        var human = VerbOptions.Human();
        command.Options.Add(after);
        command.Options.Add(human);

        command.SetAction((parseResult, cancellationToken) =>
        {
            ActorOption.Parse(parseResult.GetValue(actor), env.AttemptFromEnvironment());
            return WatchAsync(env, parseResult.GetValue(after), parseResult.GetValue(human), cancellationToken);
        });

        return command;
    }

    private static async Task<int> WatchAsync(CliEnvironment env, string? after, bool human, CancellationToken cancellationToken)
    {
        var reader = new DescriptorReader(env.Paths);
        var look = reader.Look();
        if (look.Descriptor is not { } descriptor)
        {
            return Fail(env, CliErrors.NoDescriptor, reader.NoRuntime(look), retryable: !look.Denied);
        }

        var path = string.IsNullOrEmpty(after)
            ? Streams.JournalRoute
            : $"{Streams.JournalRoute}?{Streams.AfterParameter}={Uri.EscapeDataString(after)}";

        using var client = new RuntimeClient(descriptor, env.AttemptFromEnvironment(), env.Error, env.HttpHandler);
        RuntimeStream stream;
        try
        {
            stream = await client.OpenStreamAsync(path, Streams.JournalSubProtocol, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return ExitCodes.Success;
        }
        catch (HttpRequestException ex)
        {
            return Fail(env, CliErrors.RuntimeUnreachable, $"The runtime at {descriptor.BaseUrl} did not open the journal's stream ({ex.Message}). The descriptor may be stale.");
        }
        catch (TaskCanceledException)
        {
            return Fail(env, CliErrors.RuntimeUnreachable, $"The runtime at {descriptor.BaseUrl} did not answer in time.");
        }

        using (stream)
        {
            if (stream.Refusal is { } refusal)
            {
                if (refusal.StatusCode == 401)
                {
                    return Fail(env, CliErrors.Unauthorized, "The runtime rejected the capability token from the descriptor; the descriptor is stale.");
                }

                env.Out.WriteLine(OperationRunner.ErrorText(refusal));
                return ExitCodes.ApiError;
            }

            return await ReadAsync(env, stream.Socket!, descriptor.BaseUrl, human, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>Prints every line until the person interrupts the watch or the runtime ends the stream.</summary>
    private static async Task<int> ReadAsync(CliEnvironment env, WebSocket socket, string baseUrl, bool human, CancellationToken cancellationToken)
    {
        var interrupted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var registration = cancellationToken.Register(() => interrupted.TrySetResult());
        var buffer = new byte[8192];
        using var line = new MemoryStream();
        while (true)
        {
            // The receive is never cancelled: a cancelled receive aborts the connection, and an interrupted watch closes it.
            var receiving = socket.ReceiveAsync(buffer, CancellationToken.None);
            if (await Task.WhenAny(receiving, interrupted.Task).ConfigureAwait(false) == interrupted.Task)
            {
                await CloseAsync(socket).ConfigureAwait(false);
                return ExitCodes.Success;
            }

            WebSocketReceiveResult received;
            try
            {
                received = await receiving.ConfigureAwait(false);
            }
            catch (WebSocketException ex)
            {
                return Fail(env, CliErrors.RuntimeUnreachable, $"The journal's stream from the runtime at {baseUrl} broke off ({ex.Message}).");
            }

            if (received.MessageType == WebSocketMessageType.Close)
            {
                await CloseAsync(socket).ConfigureAwait(false);
                return received.CloseStatus == WebSocketCloseStatus.PolicyViolation
                    ? Fail(env, CliErrors.Unauthorized, $"The runtime ended the journal's stream: {received.CloseStatusDescription}")
                    : Fail(env, CliErrors.RuntimeUnreachable, $"The runtime at {baseUrl} ended the journal's stream: {received.CloseStatusDescription}");
            }

            // A frame may arrive in parts, and a character may straddle two of them: the text is read once it is whole.
            line.Write(buffer, 0, received.Count);
            if (!received.EndOfMessage)
            {
                continue;
            }

            var json = Encoding.UTF8.GetString(line.GetBuffer(), 0, (int)line.Length);
            line.SetLength(0);
            env.Out.WriteLine(human ? JournalRenderers.Entry(json) ?? json : json);
            await env.Out.FlushAsync(CancellationToken.None).ConfigureAwait(false);
        }
    }

    /// <summary>Says the watch is over, and waits a moment for the runtime to say so too.</summary>
    private static async Task CloseAsync(WebSocket socket)
    {
        using var within = new CancellationTokenSource(CloseWait);
        try
        {
            if (socket.State is WebSocketState.Open or WebSocketState.CloseReceived)
            {
                await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, null, within.Token).ConfigureAwait(false);
            }
        }
        catch (Exception ex) when (ex is WebSocketException or OperationCanceledException)
        {
            // The connection is let go either way.
        }
    }

    private static int Fail(CliEnvironment env, string code, string message, bool retryable = true)
    {
        env.Out.WriteLine(CliErrors.Serialize(code, message, retryable));
        return ExitCodes.RuntimeUnavailable;
    }

    private static class Options
    {
        public const string After = "--after";
    }
}
