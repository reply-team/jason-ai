using System.Net.WebSockets;
using System.Text.Json;
using Jason.Contracts.Api;
using Jason.Contracts.Json;
using Jason.Runtime.Api;
using Jason.Runtime.Domain;
using Jason.Runtime.Persistence;
using Jason.Runtime.Ui;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Jason.Runtime.Journal;

/// <summary>
/// <c>GET /v1/journal.stream</c>: the journal, line by line as it is written, over a WebSocket under the subprotocol
/// <see cref="Streams.JournalSubProtocol"/>. Each line is one text frame holding the line exactly as <c>journal.list</c>
/// answers it, oldest first. The stream reads what <c>journal.list</c> reads and writes nothing: it reads the table
/// every <see cref="PollInterval"/>, each read in a scope of its own, so no transaction is open between two reads.
/// </summary>
/// <remarks>
/// Where it starts is a position in the table, not a public id: a line's id is made before the write that stores it
/// waits for the database, so two lines can become readable in another order than their ids say, and a stream that
/// followed ids could pass a line over. The table's own key grows in the order the writes are committed, because
/// SQLite takes them one at a time. With no <see cref="Streams.SinceParameter"/> the stream starts at the last line
/// written when it opens; with one, a cursor of <c>journal.list</c>'s, it starts after the line the cursor names.
/// </remarks>
public static class JournalStream
{
    /// <summary>
    /// How long the stream waits between two reads that found nothing new: a line reaches a reader within about a
    /// second of being written, and a stream left open costs one indexed read a second.
    /// </summary>
    public static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(1);

    /// <summary>The most lines one read takes; a read that fills it is followed at once by the next.</summary>
    public const int BatchSize = 200;

    /// <summary>How long the runtime waits for the other side to answer its close before it lets the connection go.</summary>
    private static readonly TimeSpan CloseWait = TimeSpan.FromSeconds(2);

    public static async Task HandleAsync(HttpContext context, IServiceScopeFactory scopes, IHostApplicationLifetime lifetime)
    {
        if (!context.WebSockets.IsWebSocketRequest)
        {
            await ErrorResults.WriteAsync(context, StatusCodes.Status400BadRequest, "websocket_required", $"GET {Streams.JournalRoute} is a WebSocket upgrade under the subprotocol {Streams.JournalSubProtocol}; every operation is POST /{ApiVersion.Current}/{{noun}}.{{verb}}.", retryable: false).ConfigureAwait(false);
            return;
        }

        if (!context.WebSockets.WebSocketRequestedProtocols.Contains(Streams.JournalSubProtocol, StringComparer.Ordinal))
        {
            await ErrorResults.WriteAsync(context, StatusCodes.Status400BadRequest, "websocket_required", $"The upgrade must offer the subprotocol {Streams.JournalSubProtocol}, the form of the frames this stream sends.", retryable: false).ConfigureAwait(false);
            return;
        }

        long after;
        try
        {
            after = await StartAsync(scopes, context.Request.Query[Streams.SinceParameter].ToString(), context.RequestAborted).ConfigureAwait(false);
        }
        catch (DomainException ex)
        {
            await ErrorResults.WriteAsync(context, ex.StatusCode, ex.Code, ex.Message, ex.Retryable, ex.Details).ConfigureAwait(false);
            return;
        }

        var bearer = context.Features.Get<UiTokenBearer>();
        var tokens = context.RequestServices.GetService<UiTokenStore>();
        using var socket = await context.WebSockets.AcceptWebSocketAsync(Streams.JournalSubProtocol).ConfigureAwait(false);
        using var peerClosed = new CancellationTokenSource();
        using var ending = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted, lifetime.ApplicationStopping, peerClosed.Token);
        var receiving = ReceiveUntilClosedAsync(socket, peerClosed, ending.Token);

        try
        {
            var (status, description) = await SendAsync(socket, scopes, after, bearer, tokens, lifetime.ApplicationStopping, peerClosed.Token, ending.Token, context.RequestAborted).ConfigureAwait(false);
            if (socket.State is WebSocketState.Open or WebSocketState.CloseReceived)
            {
                using var closing = new CancellationTokenSource(CloseWait);
                await socket.CloseOutputAsync(status, description, closing.Token).ConfigureAwait(false);
            }

            await Task.WhenAny(receiving, Task.Delay(CloseWait, CancellationToken.None)).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is WebSocketException or OperationCanceledException)
        {
            // The other side went away, or the close it was sent was not answered in time: there is nobody to tell.
        }
        finally
        {
            if (!receiving.IsCompleted)
            {
                socket.Abort();
            }

            await receiving.ConfigureAwait(false);
        }
    }

    /// <summary>The key of the last line the stream does not send: the one a cursor names, or the newest line now.</summary>
    private static async Task<long> StartAsync(IServiceScopeFactory scopes, string since, CancellationToken cancellationToken)
    {
        string? named;
        try
        {
            named = Paging.DecodeCursor(since);
        }
        catch (ValidationException)
        {
            throw new ValidationException([new ErrorDetail(Streams.SinceParameter, "invalid", $"{Streams.SinceParameter} must be a next_cursor journal.list returned.")]);
        }

        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<JasonDbContext>();
        var lines = db.Journal.AsNoTracking();
        if (named is not null)
        {
            lines = lines.Where(e => string.Compare(e.PublicId, named) <= 0);
        }

        return await lines.MaxAsync(e => (long?)e.Id, cancellationToken).ConfigureAwait(false) ?? 0;
    }

    /// <summary>Sends every line after <paramref name="after"/> until the stream has to end, and answers how it ends.</summary>
    private static async Task<(WebSocketCloseStatus Status, string Description)> SendAsync(
        WebSocket socket,
        IServiceScopeFactory scopes,
        long after,
        UiTokenBearer? bearer,
        UiTokenStore? tokens,
        CancellationToken stopping,
        CancellationToken peerClosed,
        CancellationToken ending,
        CancellationToken aborted)
    {
        // Only the wait between two reads ends early on a stop or the other side's close; a read and a send in flight
        // finish, because a send cancelled part way leaves no connection to send the close on.
        while (true)
        {
            if (peerClosed.IsCancellationRequested)
            {
                return (WebSocketCloseStatus.NormalClosure, string.Empty);
            }

            if (stopping.IsCancellationRequested)
            {
                return (WebSocketCloseStatus.EndpointUnavailable, "The runtime is stopping.");
            }

            if (bearer is not null && (tokens is null || !tokens.IsLive(bearer.Hash)))
            {
                return (WebSocketCloseStatus.PolicyViolation, "The UI token this stream was opened with has been revoked.");
            }

            IReadOnlyList<JournalEntryDto> lines;
            long last;
            using (var scope = scopes.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<JasonDbContext>();
                var fetched = await db.Journal.AsNoTracking()
                    .Where(e => e.Id > after)
                    .OrderBy(e => e.Id)
                    .Take(BatchSize)
                    .ToListAsync(aborted)
                    .ConfigureAwait(false);
                last = fetched.Count == 0 ? after : fetched[^1].Id;
                lines = fetched.Count == 0 ? [] : await JournalEntries.ToDtosAsync(db, fetched, aborted).ConfigureAwait(false);
            }

            foreach (var line in lines)
            {
                await socket.SendAsync(JsonSerializer.SerializeToUtf8Bytes(line, JasonJson.Options), WebSocketMessageType.Text, endOfMessage: true, aborted).ConfigureAwait(false);
            }

            after = last;
            if (lines.Count < BatchSize)
            {
                try
                {
                    await Task.Delay(PollInterval, ending).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (stopping.IsCancellationRequested || peerClosed.IsCancellationRequested)
                {
                    // Said at the top of the loop, which is the one place a close is sent from.
                }
            }
        }
    }

    /// <summary>
    /// The stream takes nothing from the other side; it reads only to see the close, and says so through
    /// <paramref name="peerClosed"/>. A frame of any other kind is passed over.
    /// </summary>
    private static async Task ReceiveUntilClosedAsync(WebSocket socket, CancellationTokenSource peerClosed, CancellationToken ending)
    {
        var buffer = new byte[1024];
        try
        {
            while (true)
            {
                var received = await socket.ReceiveAsync(buffer, CancellationToken.None).ConfigureAwait(false);
                if (received.MessageType == WebSocketMessageType.Close)
                {
                    await peerClosed.CancelAsync().ConfigureAwait(false);
                    return;
                }
            }
        }
        catch (Exception ex) when (ex is WebSocketException or OperationCanceledException or ObjectDisposedException)
        {
            // The connection ended without a close of the other side's: the send loop learns it from its own token.
            if (!ending.IsCancellationRequested)
            {
                await peerClosed.CancelAsync().ConfigureAwait(false);
            }
        }
    }
}
