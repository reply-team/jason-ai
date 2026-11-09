using System.Net.WebSockets;
using System.Text.Json;
using Jason.Contracts.Api;
using Jason.Contracts.Ids;
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
/// SQLite takes them one at a time. With no <see cref="Streams.AfterParameter"/> the stream starts at the last line
/// written when it opens; with one, a line's public id, it starts after that line in the order of the table. So a program
/// that resumes from the newest id <c>journal.list</c> showed it may be sent a line it already has once more, one
/// committed after that line under an older id, and is never short of one: it drops a repeat by its id.
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

    /// <summary>The prefix of a journal line's public id, the form <see cref="Streams.AfterParameter"/> takes.</summary>
    private const string JournalIdPrefix = "jrn";

    /// <summary>The one word a stream closed for a revoked UI token gives as its reason.</summary>
    public const string RevokedReason = "revoked";

    /// <summary>How long the runtime waits for the other side to answer its close before it lets the connection go.</summary>
    private static readonly TimeSpan CloseWait = TimeSpan.FromSeconds(2);

    /// <summary>
    /// The longest one frame may wait for the other side to take it. A reader that has stopped taking frames fills the
    /// connection's buffers and the send waits on them; past this the reader is cut, without a close, because it
    /// would not read one either.
    /// </summary>
    public static readonly TimeSpan SendWait = TimeSpan.FromSeconds(30);

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
            after = await StartAsync(scopes, context.Request.Query[Streams.AfterParameter].ToString(), context.RequestAborted).ConfigureAwait(false);
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
            // A stream whose last send was cut has no close to give: the finally lets the connection go.
            if (await SendAsync(socket, scopes, after, bearer, tokens, lifetime.ApplicationStopping, peerClosed.Token, ending.Token).ConfigureAwait(false) is { } close)
            {
                if (socket.State is WebSocketState.Open or WebSocketState.CloseReceived)
                {
                    using var closing = new CancellationTokenSource(CloseWait);
                    await socket.CloseOutputAsync(close.Status, close.Description, closing.Token).ConfigureAwait(false);
                }

                await Task.WhenAny(receiving, Task.Delay(CloseWait, CancellationToken.None)).ConfigureAwait(false);
            }
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

    /// <summary>
    /// The key of the last line the stream does not send: the line <see cref="Streams.AfterParameter"/> names by its
    /// public id, or the newest line now. The one place the parameter is read.
    /// </summary>
    private static async Task<long> StartAsync(IServiceScopeFactory scopes, string afterId, CancellationToken cancellationToken)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<JasonDbContext>();
        if (string.IsNullOrEmpty(afterId))
        {
            return await db.Journal.AsNoTracking().MaxAsync(e => (long?)e.Id, cancellationToken).ConfigureAwait(false) ?? 0;
        }

        if (!PublicId.IsWellFormed(JournalIdPrefix, afterId))
        {
            throw new ValidationException([new ErrorDetail(Streams.AfterParameter, "invalid", $"{Streams.AfterParameter} must be the id of a journal line, {JournalIdPrefix}_ and 26 characters.")]);
        }

        return await db.Journal.AsNoTracking().Where(e => e.PublicId == afterId).Select(e => (long?)e.Id).SingleOrDefaultAsync(cancellationToken).ConfigureAwait(false)
            ?? throw new ValidationException([new ErrorDetail(Streams.AfterParameter, "unknown", $"{Streams.AfterParameter} names no journal line.")]);
    }

    /// <summary>
    /// Sends every line after <paramref name="after"/> until the stream has to end, and answers how it ends: the close
    /// to send, or none when a send was cut.
    /// </summary>
    private static async Task<(WebSocketCloseStatus Status, string Description)?> SendAsync(
        WebSocket socket,
        IServiceScopeFactory scopes,
        long after,
        UiTokenBearer? bearer,
        UiTokenStore? tokens,
        CancellationToken stopping,
        CancellationToken peerClosed,
        CancellationToken ending)
    {
        // The wait, the read and every send end on a stop, on the other side's close and on the connection's end, so
        // an open stream never holds a stop. A read or a wait cut short is said at the top of the loop. A send cut short
        // leaves a frame half written, after which no close can be sent, and the other side was not reading anyway.
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
                return (WebSocketCloseStatus.PolicyViolation, RevokedReason);
            }

            IReadOnlyList<JournalEntryDto> lines;
            long last;
            try
            {
                using var scope = scopes.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<JasonDbContext>();
                var fetched = await db.Journal.AsNoTracking()
                    .Where(e => e.Id > after)
                    .OrderBy(e => e.Id)
                    .Take(BatchSize)
                    .ToListAsync(ending)
                    .ConfigureAwait(false);
                last = fetched.Count == 0 ? after : fetched[^1].Id;
                lines = fetched.Count == 0 ? [] : await JournalEntries.ToDtosAsync(db, fetched, ending).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stopping.IsCancellationRequested || peerClosed.IsCancellationRequested)
            {
                continue;
            }

            foreach (var line in lines)
            {
                using var sending = CancellationTokenSource.CreateLinkedTokenSource(ending);
                sending.CancelAfter(SendWait);
                try
                {
                    await socket.SendAsync(JsonSerializer.SerializeToUtf8Bytes(line, JasonJson.Options), WebSocketMessageType.Text, endOfMessage: true, sending.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (sending.IsCancellationRequested)
                {
                    return null;
                }
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
