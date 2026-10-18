using Jason.Contracts.Api;
using Jason.Contracts.Json;
using Jason.Runtime.Api;
using Jason.Runtime.Discovery;
using Microsoft.AspNetCore.Http;

namespace Jason.Runtime.Ui;

/// <summary>
/// <c>POST /ui/session</c>: the page trades the ticket from its link for a UI token. Not an operation — nothing
/// but the page ever calls it — and the only POST with no bearer, which is why it insists on the runtime's own
/// <c>Origin</c> and burns the ticket whatever happens next.
/// </summary>
public static class UiSessionDoor
{
    public static async Task HandleAsync(HttpContext context, UiTickets tickets, UiTokenStore tokens, RuntimeInfo info)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(info);
        UiFiles.ApplyHeaders(context.Response);

        var origin = context.Request.Headers.Origin.ToString();
        if (origin.Length == 0 || !OriginRule.IsOwn(origin, context.Connection.LocalPort))
        {
            await OriginRule.Refuse(context).ConfigureAwait(false);
            return;
        }

        // The body is read the way an operation's is, so whatever cannot be read is a 400 in the same words, never a bare 500.
        UiSessionRequest request;
        try
        {
            request = await OperationEndpoints.ReadRequestAsync<UiSessionRequest>(context).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            if (OperationEndpoints.CallerWentAway(context, exception))
            {
                context.Response.StatusCode = StatusCodes.Status499ClientClosedRequest;
                return;
            }

            await ErrorResults.WriteAsync(context, StatusCodes.Status400BadRequest, "invalid_request", OperationEndpoints.Unreadable(exception), retryable: false).ConfigureAwait(false);
            return;
        }

        var ticket = request.Ticket?.Trim();
        if (string.IsNullOrEmpty(ticket) || !tickets.TryRedeem(ticket))
        {
            await ErrorResults.WriteAsync(context, StatusCodes.Status401Unauthorized, "ticket_invalid", "The link has been used or has expired. Run `jason ui open` for a new one.", retryable: false).ConfigureAwait(false);
            return;
        }

        var (token, issued) = await tokens.IssueAsync(Environment.UserName, context.RequestAborted).ConfigureAwait(false);
        await context.Response.WriteAsJsonAsync(
            new UiSessionResponse(token, issued.Id, issued.IssuedTo, ApiVersion.Current, info.InstanceId),
            JasonJson.Options,
            context.RequestAborted).ConfigureAwait(false);
    }
}
